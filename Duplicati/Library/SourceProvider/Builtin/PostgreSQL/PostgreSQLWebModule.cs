// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.


using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duplicati.Library.Interface;

namespace Duplicati.Library.SourceProvider.Builtin.PostgreSQL;

/// <summary>
/// The checks a user interface can run against a PostgreSQL database before it is backed up or
/// restored to: whether the role can do what is needed, and how much the database holds. It
/// answers in the shape the other sources' web modules use, so they are all read alike.
/// </summary>
public sealed class PostgreSQLWebModule : IWebModule
{
    /// <summary>The option with the operation to run</summary>
    public const string OperationOption = "operation";
    /// <summary>The option with the URL of the database</summary>
    public const string UrlOption = "url";
    /// <summary>
    /// The option that says what the database is checked for. Without it the database is checked as
    /// the source of a backup.
    /// </summary>
    public const string PurposeOption = "purpose";
    /// <summary>The value of <see cref="PurposeOption"/> that checks the database as the target of a restore</summary>
    public const string RestorePurpose = "restore";
    /// <summary>
    /// The option that makes an encrypted connection a requirement. Without it a connection that is
    /// not encrypted is reported, and does not fail the check: a database on the same machine is
    /// often reached without TLS.
    /// </summary>
    public const string RequireEncryptionOption = "require-encryption";

    /// <summary>The operation that reports whether the role can do what a backup or restore needs</summary>
    public const string CheckPermissionsOperation = "CheckPermissions";
    /// <summary>The operation that reports how much the database holds</summary>
    public const string CountItemsOperation = "CountItems";
    /// <summary>The operations of the module</summary>
    public static readonly string[] Operations = [CheckPermissionsOperation, CountItemsOperation];

    /// <summary>The result key holding the JSON list of checks</summary>
    public const string PermissionsResultKey = "permissions";
    /// <summary>The result key holding the JSON object of counts</summary>
    public const string CountsResultKey = "counts";

    /// <summary>The check that the server accepted the connection</summary>
    public const string ConnectionCheck = "connection";
    /// <summary>The check that the connection is encrypted</summary>
    public const string TlsCheck = "tls";
    /// <summary>The check that the server is a version that is supported</summary>
    public const string ServerVersionCheck = "serverVersion";
    /// <summary>The check that the role can read every table</summary>
    public const string TableAccessCheck = "tableAccess";
    /// <summary>The check that row-level security does not hide rows from the role</summary>
    public const string RowLevelSecurityCheck = "rowLevelSecurity";
    /// <summary>What a backup does with the large objects of the database</summary>
    public const string LargeObjectsCheck = "largeObjects";
    /// <summary>The check that the role has no more rights than a backup needs</summary>
    public const string SuperuserCheck = "superuser";
    /// <summary>The check that the role can create the objects a whole database restore creates</summary>
    public const string CreateObjectsCheck = "createObjects";
    /// <summary>The check of whether the database a restore writes to already has tables</summary>
    public const string ExistingTablesCheck = "existingTables";

    /// <summary>How many tables or large objects a description names before it gives the number of the rest</summary>
    public const int MaxNamesListed = 20;

    /// <summary>The separator psql writes between the columns of a row in unaligned output</summary>
    public const char FieldSeparator = '|';

    /// <summary>
    /// The tables a dump reads: ordinary and partitioned tables outside the system schemas
    /// </summary>
    private const string UserTables =
        "from pg_class c join pg_namespace n on n.oid = c.relnamespace"
        + " where c.relkind in ('r', 'p') and n.nspname !~ '^pg_' and n.nspname <> 'information_schema'";

    /// <summary>
    /// Whether the role may read a large object <c>m</c>: it owns it, or it or a role it is a member
    /// of, or everyone, was granted SELECT. Written out because the function for it only exists
    /// from version 18.
    /// </summary>
    private const string LargeObjectReadable =
        "(pg_has_role(m.lomowner, 'USAGE') or exists (select 1 from aclexplode(m.lomacl) a"
        + " where a.privilege_type = 'SELECT' and (a.grantee = 0 or pg_has_role(a.grantee, 'USAGE'))))";

    /// <summary>
    /// Decides whether the connection to a database is encrypted. With a TLS mode that does not connect
    /// without TLS it is, whatever the server reports: behind a connection pooler the server reports
    /// the pooler's connection to it, not the one from the client tools. With the other modes the
    /// server's word is the only one there is.
    /// </summary>
    /// <param name="database">The database</param>
    /// <param name="serverReportsTls">Whether the server reports TLS for the connection it sees</param>
    /// <returns>True if the connection is encrypted</returns>
    private static bool UsesTls(PostgreSQLOptions database, string serverReportsTls)
        => database.RequiresEncryption || serverReportsTls == True;

    /// <summary>
    /// One row: the server version number, the version as the server writes it, whether the
    /// connection the server sees uses TLS, whether the role is a superuser, and whether it bypasses
    /// row-level security.
    /// </summary>
    public const string SessionQuery =
        "select current_setting('server_version_num'), current_setting('server_version'),"
        + " coalesce((select ssl from pg_stat_ssl where pid = pg_backend_pid()), false), r.rolsuper, r.rolbypassrls"
        + " from pg_roles r where r.rolname = current_user";

    /// <summary>One row: the number of tables, and how many of them have row-level security</summary>
    public const string TableSummaryQuery =
        "select count(*), count(*) filter (where c.relrowsecurity) " + UserTables;

    /// <summary>The tables the role cannot read, by name</summary>
    public const string UnreadableTablesQuery =
        "select quote_ident(n.nspname) || '.' || quote_ident(c.relname) " + UserTables
        + " and not (has_schema_privilege(n.oid, 'USAGE') and has_table_privilege(c.oid, 'SELECT')) order by 1";

    /// <summary>One row: the number of large objects, and how many of them the role cannot read</summary>
    public const string LargeObjectSummaryQuery =
        "select count(*), count(*) filter (where not " + LargeObjectReadable + ") from pg_largeobject_metadata m";

    /// <summary>The first large objects the role cannot read, by OID</summary>
    public static readonly string UnreadableLargeObjectsQuery =
        "select m.oid from pg_largeobject_metadata m where not " + LargeObjectReadable
        + " order by m.oid limit " + MaxNamesListed.ToString(CultureInfo.InvariantCulture);

    /// <summary>One row: the number of tables and the size of the database in bytes</summary>
    public const string CountQuery =
        "select (select count(*) " + UserTables + "), pg_database_size(current_database())";

    /// <summary>One row: whether the role may create schemas in the database</summary>
    public const string CreatePrivilegeQuery = "select has_database_privilege(current_database(), 'CREATE')";

    private const string True = "t";

    /// <summary>
    /// Runs one statement against a database and returns what psql wrote: one row on each line,
    /// with the columns separated by <see cref="FieldSeparator"/>
    /// </summary>
    /// <param name="options">The database to connect to</param>
    /// <param name="sql">The statement</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The output</returns>
    public delegate Task<string> QueryRunner(PostgreSQLOptions options, string sql, CancellationToken cancellationToken);

    private readonly QueryRunner m_query;

    /// <summary>
    /// Creates the module, which runs its statements with psql
    /// </summary>
    public PostgreSQLWebModule()
        : this((options, sql, cancellationToken) => PostgreSQLClientTools.QueryAsync(PostgreSQLClientTools.LocateAnyPsql(options), sql, options, cancellationToken))
    {
    }

    /// <summary>
    /// Creates the module with what runs its statements, so the checks can be tested without a server
    /// </summary>
    /// <param name="query">Runs a statement</param>
    public PostgreSQLWebModule(QueryRunner query)
    {
        m_query = query;
    }

    /// <inheritdoc />
    public string Key => PostgreSQLOptions.Scheme;
    /// <inheritdoc />
    public string DisplayName => Strings.PostgreSQLWebModule.DisplayName;
    /// <inheritdoc />
    public string Description => Strings.PostgreSQLWebModule.Description;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands =>
    [
        new CommandLineArgument(OperationOption, CommandLineArgument.ArgumentType.Enumeration, Strings.PostgreSQLWebModule.OperationShort, Strings.PostgreSQLWebModule.OperationLong, CheckPermissionsOperation, [], Operations),
        new CommandLineArgument(UrlOption, CommandLineArgument.ArgumentType.String, Strings.PostgreSQLWebModule.UrlShort, Strings.PostgreSQLWebModule.UrlLong),
        new CommandLineArgument(PurposeOption, CommandLineArgument.ArgumentType.String, Strings.PostgreSQLWebModule.PurposeShort, Strings.PostgreSQLWebModule.PurposeLong),
        new CommandLineArgument(RequireEncryptionOption, CommandLineArgument.ArgumentType.Boolean, Strings.PostgreSQLWebModule.RequireEncryptionShort, Strings.PostgreSQLWebModule.RequireEncryptionLong, "false"),
    ];

    /// <inheritdoc />
    public IDictionary<string, IDictionary<string, string>> GetLookups()
        => new Dictionary<string, IDictionary<string, string>>();

    /// <summary>
    /// One check, in the shape the other sources report a permission in. <see cref="Enabled"/> says
    /// the check passed. A check that is required and did not pass stops the backup or the restore;
    /// one that is not required and did not pass is something to be aware of.
    /// </summary>
    public sealed record PermissionStatus(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("requiredForBackup")] bool RequiredForBackup,
        [property: JsonPropertyName("requiredForRestore")] bool RequiredForRestore,
        [property: JsonPropertyName("enabled")] bool Enabled);

    /// <summary>The number of tables in the database</summary>
    public sealed record TableCounts([property: JsonPropertyName("total")] long Total);

    /// <summary>The size of the database, as the server reports it</summary>
    public sealed record DatabaseCounts([property: JsonPropertyName("sizeBytes")] long SizeBytes);

    /// <summary>What <see cref="CountItemsOperation"/> reports</summary>
    public sealed record CountResult(
        [property: JsonPropertyName("tables")] TableCounts Tables,
        [property: JsonPropertyName("database")] DatabaseCounts Database);

    /// <inheritdoc />
    public async Task<IDictionary<string, string>> Execute(IDictionary<string, string> options, CancellationToken cancellationToken)
    {
        options.TryGetValue(OperationOption, out var operation);
        options.TryGetValue(UrlOption, out var url);
        if (string.IsNullOrWhiteSpace(url))
            throw new UserInformationException(Strings.PostgreSQLWebModule.MissingUrl, PostgreSQLEventIds.MissingOption);
        if (!url.StartsWith(PostgreSQLOptions.Scheme + "://", StringComparison.OrdinalIgnoreCase))
            throw new UserInformationException(Strings.PostgreSQLWebModule.NotADatabaseUrl(PostgreSQLOptions.Scheme), PostgreSQLEventIds.InvalidOption);

        // The database is named by the options of the URL alone, as it is for a backup
        var connection = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var query = new Library.Utility.RelaxedUri(url).QueryParameters;
        foreach (var key in query.AllKeys)
            if (key != null)
                connection[key] = query[key];

        using var database = PostgreSQLOptions.Parse(connection);
        var requireEncryption = Library.Utility.Utility.ParseBoolOption(options.AsReadOnly()!, RequireEncryptionOption);

        if (string.Equals(operation, CountItemsOperation, StringComparison.OrdinalIgnoreCase))
            return new Dictionary<string, string>
            {
                [CountsResultKey] = JsonSerializer.Serialize(await CountItemsAsync(database, cancellationToken).ConfigureAwait(false))
            };

        if (string.Equals(operation, CheckPermissionsOperation, StringComparison.OrdinalIgnoreCase))
        {
            var forRestore = options.TryGetValue(PurposeOption, out var purpose) && string.Equals(purpose, RestorePurpose, StringComparison.OrdinalIgnoreCase);
            var checks = forRestore
                ? await CheckRestoreTargetAsync(database, requireEncryption, cancellationToken).ConfigureAwait(false)
                : await CheckPermissionsAsync(database, requireEncryption, cancellationToken).ConfigureAwait(false);

            return new Dictionary<string, string> { [PermissionsResultKey] = JsonSerializer.Serialize(checks) };
        }

        throw new UserInformationException(Strings.PostgreSQLWebModule.UnsupportedOperation(operation ?? string.Empty), PostgreSQLEventIds.InvalidOption);
    }

    /// <summary>
    /// Checks a database as the source of a backup. A connection that fails throws with what psql
    /// wrote, as nothing else can be checked.
    /// </summary>
    /// <param name="source">The database</param>
    /// <param name="requireEncryption">True when a connection that is not encrypted fails the check</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The checks</returns>
    public async Task<IReadOnlyList<PermissionStatus>> CheckPermissionsAsync(PostgreSQLOptions source, bool requireEncryption, CancellationToken cancellationToken)
    {
        var session = await SingleRowAsync(source, SessionQuery, 5, cancellationToken).ConfigureAwait(false);
        var major = PostgreSQLClientTools.MajorFromServerVersionNum((int)ParseCount(session[0]));
        var usesTls = UsesTls(source, session[2]);
        var isSuperuser = session[3] == True;
        var bypassesRowSecurity = isSuperuser || session[4] == True;
        var supportedVersion = PostgreSQLClientTools.SupportedMajorVersions.Contains(major);

        var tableSummary = await SingleRowAsync(source, TableSummaryQuery, 2, cancellationToken).ConfigureAwait(false);
        var tables = ParseCount(tableSummary[0]);
        var tablesWithRowSecurity = ParseCount(tableSummary[1]);
        var unreadableTables = await RowsAsync(source, UnreadableTablesQuery, cancellationToken).ConfigureAwait(false);

        var largeObjectSummary = await SingleRowAsync(source, LargeObjectSummaryQuery, 2, cancellationToken).ConfigureAwait(false);
        var largeObjects = ParseCount(largeObjectSummary[0]);
        var unreadableLargeObjects = ParseCount(largeObjectSummary[1]);

        var checks = new List<PermissionStatus>
        {
            ForBackup(ConnectionCheck, true, true, Strings.PostgreSQLWebModule.Connected(source.Database, source.Host, source.Port, source.Username)),
            ForBackup(TlsCheck, requireEncryption, usesTls, usesTls ? Strings.PostgreSQLWebModule.Encrypted : Strings.PostgreSQLWebModule.NotEncrypted),
            ForBackup(ServerVersionCheck, true, supportedVersion, supportedVersion
                ? Strings.PostgreSQLWebModule.ServerVersion(session[1])
                : Strings.PostgreSQLWebModule.ServerVersionNotBackedUp(session[1], SupportedVersions)),
            ForBackup(TableAccessCheck, true, unreadableTables.Count == 0, unreadableTables.Count == 0
                ? Strings.PostgreSQLWebModule.TablesReadable(tables)
                : Strings.PostgreSQLWebModule.TablesNotReadable(ListNames(unreadableTables, unreadableTables.Count))),
            ForBackup(RowLevelSecurityCheck, true, tablesWithRowSecurity == 0 || bypassesRowSecurity, tablesWithRowSecurity == 0
                ? Strings.PostgreSQLWebModule.NoRowSecurity
                : bypassesRowSecurity
                    ? Strings.PostgreSQLWebModule.RowSecurityBypassed(tablesWithRowSecurity)
                    : Strings.PostgreSQLWebModule.RowSecurityHidesRows(tablesWithRowSecurity))
        };

        if (largeObjects == 0)
            checks.Add(ForBackup(LargeObjectsCheck, false, true, Strings.PostgreSQLWebModule.NoLargeObjects));
        else if (!source.IncludeLargeObjects)
            checks.Add(ForBackup(LargeObjectsCheck, false, false, Strings.PostgreSQLWebModule.LargeObjectsLeftOut(largeObjects)));
        else if (unreadableLargeObjects == 0)
            checks.Add(ForBackup(LargeObjectsCheck, true, true, Strings.PostgreSQLWebModule.LargeObjectsReadable(largeObjects)));
        else
        {
            var oids = await RowsAsync(source, UnreadableLargeObjectsQuery, cancellationToken).ConfigureAwait(false);
            checks.Add(ForBackup(LargeObjectsCheck, true, false, Strings.PostgreSQLWebModule.LargeObjectsNotReadable(unreadableLargeObjects, largeObjects, ListNames(oids, unreadableLargeObjects))));
        }

        checks.Add(ForBackup(SuperuserCheck, false, !isSuperuser, isSuperuser ? Strings.PostgreSQLWebModule.Superuser : Strings.PostgreSQLWebModule.NotSuperuser));

        return checks;
    }

    /// <summary>
    /// Checks a database as the target of a restore. A connection that fails throws with what psql
    /// wrote. What a restore needs depends on what is restored: a whole database creates its
    /// objects, and tables are only loaded into, so what the role may do and what the database
    /// holds are reported, not required.
    /// </summary>
    /// <param name="target">The database</param>
    /// <param name="requireEncryption">True when a connection that is not encrypted fails the check</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The checks</returns>
    public async Task<IReadOnlyList<PermissionStatus>> CheckRestoreTargetAsync(PostgreSQLOptions target, bool requireEncryption, CancellationToken cancellationToken)
    {
        var session = await SingleRowAsync(target, SessionQuery, 5, cancellationToken).ConfigureAwait(false);
        var major = PostgreSQLClientTools.MajorFromServerVersionNum((int)ParseCount(session[0]));
        var usesTls = UsesTls(target, session[2]);
        var supportedVersion = PostgreSQLClientTools.SupportedMajorVersions.Contains(major);

        var tables = ParseCount((await SingleRowAsync(target, TableSummaryQuery, 2, cancellationToken).ConfigureAwait(false))[0]);
        var canCreate = (await SingleRowAsync(target, CreatePrivilegeQuery, 1, cancellationToken).ConfigureAwait(false))[0] == True;

        return
        [
            ForRestore(ConnectionCheck, true, true, Strings.PostgreSQLWebModule.Connected(target.Database, target.Host, target.Port, target.Username)),
            ForRestore(TlsCheck, requireEncryption, usesTls, usesTls ? Strings.PostgreSQLWebModule.Encrypted : Strings.PostgreSQLWebModule.NotEncrypted),
            ForRestore(ServerVersionCheck, true, supportedVersion, supportedVersion
                ? Strings.PostgreSQLWebModule.ServerVersion(session[1])
                : Strings.PostgreSQLWebModule.ServerVersionNotRestoredTo(session[1], SupportedVersions)),
            ForRestore(CreateObjectsCheck, false, canCreate, canCreate ? Strings.PostgreSQLWebModule.CanCreate : Strings.PostgreSQLWebModule.CannotCreate),
            ForRestore(ExistingTablesCheck, false, tables == 0, tables == 0 ? Strings.PostgreSQLWebModule.NoTables : Strings.PostgreSQLWebModule.HasTables(tables))
        ];
    }

    /// <summary>
    /// Counts the tables and reads the size of the database
    /// </summary>
    /// <param name="source">The database</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The counts</returns>
    public async Task<CountResult> CountItemsAsync(PostgreSQLOptions source, CancellationToken cancellationToken)
    {
        var row = await SingleRowAsync(source, CountQuery, 2, cancellationToken).ConfigureAwait(false);
        return new CountResult(new TableCounts(ParseCount(row[0])), new DatabaseCounts(ParseCount(row[1])));
    }

    private static string SupportedVersions
        => string.Join(", ", PostgreSQLClientTools.SupportedMajorVersions.Order());

    private static PermissionStatus ForBackup(string name, bool required, bool passed, string description)
        => new(name, description, required, false, passed);

    private static PermissionStatus ForRestore(string name, bool required, bool passed, string description)
        => new(name, description, false, required, passed);

    /// <summary>
    /// Writes the first names of a list, and how many more there are
    /// </summary>
    private static string ListNames(IReadOnlyList<string> names, long total)
    {
        var listed = names.Take(MaxNamesListed).ToList();
        var text = string.Join(", ", listed);
        return total > listed.Count ? Strings.PostgreSQLWebModule.NamesAndMore(text, total - listed.Count) : text;
    }

    private async Task<IReadOnlyList<string>> RowsAsync(PostgreSQLOptions database, string sql, CancellationToken cancellationToken)
        => (await m_query(database, sql, cancellationToken).ConfigureAwait(false))
            .Split('\n')
            .Select(x => x.TrimEnd('\r'))
            .Where(x => x.Length > 0)
            .ToList();

    private async Task<string[]> SingleRowAsync(PostgreSQLOptions database, string sql, int columns, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(database, sql, cancellationToken).ConfigureAwait(false);
        var row = rows.Count == 1 ? rows[0].Split(FieldSeparator) : [];
        if (row.Length != columns)
            throw new UserInformationException(Strings.PostgreSQLWebModule.UnexpectedAnswer, PostgreSQLEventIds.ToolFailed);

        return row;
    }

    private static long ParseCount(string value)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? count
            : throw new UserInformationException(Strings.PostgreSQLWebModule.UnexpectedAnswer, PostgreSQLEventIds.ToolFailed);
}
