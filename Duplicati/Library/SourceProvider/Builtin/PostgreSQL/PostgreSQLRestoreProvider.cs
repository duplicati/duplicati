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


using System.Collections.Concurrent;
using System.Text;
using Duplicati.Library.Interface;

namespace Duplicati.Library.SourceProvider.Builtin.PostgreSQL;

/// <summary>
/// Restores a backup made by <see cref="PostgreSQLSourceProvider"/> into a PostgreSQL database.
/// The entries are loaded as the restore engine writes them, so nothing the size of the
/// database is written to disk.
/// <para>
/// A restore of the whole database runs in this order: the table of contents is restored
/// first; the objects are created from it with pg_restore; the rows of each table are loaded
/// with COPY, each table in a transaction of its own; and when everything has arrived the
/// sequence values, and then the indexes and constraints, are applied with pg_restore.
/// </para>
/// <para>
/// A restore of tables only loads the rows of the restored tables into tables that exist.
/// </para>
/// </summary>
public sealed class PostgreSQLRestoreProvider : IRestoreDestinationProviderModule, IRestoreSelectionReceiver
{
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<PostgreSQLRestoreProvider>();

    /// <summary>
    /// The tables a restore of the whole database must not find in the target:
    /// ordinary and partitioned tables outside the system schemas
    /// </summary>
    private const string UserTableCountQuery =
        "select count(*) from pg_class c join pg_namespace n on n.oid = c.relnamespace"
        + " where c.relkind in ('r', 'p') and n.nspname !~ '^pg_' and n.nspname <> 'information_schema'";

    /// <summary>The pg_restore arguments that remove each large object that exists before it is created</summary>
    public static readonly string[] LargeObjectReplaceArguments = [PostgreSQLArguments.Clean, PostgreSQLArguments.IfExists];

    /// <summary>How many names a message lists before it gives the number of the rest</summary>
    private const int MaxNamesListed = 10;

    /// <summary>The share of the final progress that is used before the indexes and constraints are created</summary>
    private const double ProgressBeforePostData = 0.2;

    private readonly PostgreSQLRestoreOptions? m_options;
    private readonly SemaphoreSlim m_prepareLock = new(1, 1);
    private readonly SemaphoreSlim m_createTableLock = new(1, 1);

    /// <summary>The table of contents entry as it was restored, or null if it is not part of the restore</summary>
    private volatile byte[]? m_tableOfContents;
    private PostgreSQLArchiveReader? m_archive;
    private PostgreSQLSourceProvider.ServerInfo? m_server;
    private string? m_psql;
    private string? m_pgRestore;
    private bool m_prepared;
    private Exception? m_prepareFailure;

    /// <summary>The names of the table entries whose rows were committed</summary>
    private readonly ConcurrentDictionary<string, bool> m_loadedTables = new(StringComparer.Ordinal);
    /// <summary>The dump each restored table, and the large objects, come from, by entry name</summary>
    private readonly ConcurrentDictionary<string, string> m_entryRunIds = new(StringComparer.Ordinal);
    /// <summary>What went wrong in steps that end after the restore engine has closed the entry</summary>
    private readonly ConcurrentQueue<string> m_failures = new();
    private volatile string? m_tableOfContentsRunId;
    /// <summary>True when the large objects were restored</summary>
    private volatile bool m_largeObjectsLoaded;
    /// <summary>The names of the entries the restore writes, or null when the restore engine did not tell</summary>
    private volatile IReadOnlySet<string>? m_selectedEntries;

    /// <summary>
    /// Creates an instance that only describes the provider
    /// </summary>
    public PostgreSQLRestoreProvider()
    {
        m_options = null;
    }

    /// <summary>
    /// Creates the provider for a target
    /// </summary>
    /// <param name="url">The target URL; its options are already among <paramref name="options"/></param>
    /// <param name="options">The restore options, with those of the URL</param>
    public PostgreSQLRestoreProvider(string url, Dictionary<string, string?> options)
    {
        m_options = PostgreSQLRestoreOptions.Parse(options);
    }

    private PostgreSQLRestoreOptions Options => m_options ?? throw new InvalidOperationException("The provider was created to describe itself only");
    private PostgreSQLOptions Connection => Options.Connection;

    /// <inheritdoc />
    public string Key => PostgreSQLOptions.Scheme;
    /// <inheritdoc />
    public string DisplayName => Strings.PostgreSQLRestoreProvider.DisplayName;
    /// <inheritdoc />
    public string Description => Strings.PostgreSQLRestoreProvider.Description;
    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => PostgreSQLRestoreOptions.SupportedCommands;

    /// <summary>
    /// The folder the restore engine places the entries in. A database has no folders,
    /// so this only gives the entries a common root.
    /// </summary>
    public string TargetDestination => Path.DirectorySeparatorChar + PostgreSQLOptions.Scheme;

    /// <summary>
    /// The table of contents is restored first, as the objects are created from it
    /// before the rows of any table can be loaded
    /// </summary>
    public IList<string> GetPriorityFiles()
        => [Path.DirectorySeparatorChar + PostgreSQLSourceProvider.TableOfContentsName];

    /// <inheritdoc />
    public Task Test(CancellationToken cancellationToken)
        => PostgreSQLSourceProvider.GetServerInfoAsync(Connection, cancellationToken);

    /// <inheritdoc />
    public async Task Initialize(CancellationToken cancel)
    {
        m_server = await PostgreSQLSourceProvider.GetServerInfoAsync(Connection, cancel).ConfigureAwait(false);
        m_psql = PostgreSQLClientTools.LocateTool(PostgreSQLClientTools.Psql, m_server.Major, Connection);

        // Text from the backup is only given to a psql that can switch off its own commands for it
        var probeKey = NewRestrictKey();
        var (probeExitCode, probeErrors) = await PostgreSQLClientTools.RunWithInputAsync(
            PostgreSQLClientTools.Psql,
            PostgreSQLClientTools.BuildStartInfo(m_psql, BuildScriptArguments(true), Connection),
            Encoding.UTF8.GetBytes(RestrictCommand + probeKey + "\n" + UnrestrictCommand + probeKey + "\n"),
            cancel).ConfigureAwait(false);
        if (probeExitCode != 0)
        {
            Logging.Log.WriteVerboseMessage(LOGTAG, "PsqlRestrictProbe", "Checking that psql supports {0} failed: {1}", RestrictCommand.Trim(), probeErrors);
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.ClientTooOld(PostgreSQLClientTools.Psql), PostgreSQLEventIds.RestoreClientTooOld);
        }

        // Restoring over a database by accident cannot be undone, so it has to be asked for
        if (!Options.TablesOnly && !Options.Overwrite)
        {
            var output = (await PostgreSQLClientTools.QueryAsync(m_psql, UserTableCountQuery, Connection, cancel).ConfigureAwait(false)).Trim();
            if (!long.TryParse(output, out var tables))
                throw new UserInformationException(Strings.PostgreSQLSourceProvider.ToolFailed(PostgreSQLClientTools.Psql, 0, output), PostgreSQLEventIds.ToolFailed);
            if (tables > 0)
                throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TargetNotEmpty(Connection.Database, tables), PostgreSQLEventIds.RestoreTargetNotEmpty);
        }
    }

    /// <summary>The kinds of entries a backup of a database holds</summary>
    public enum EntryKind
    {
        /// <summary>The table of contents</summary>
        TableOfContents,
        /// <summary>The rows of a table</summary>
        Table,
        /// <summary>The large objects</summary>
        LargeObjects,
        /// <summary>What was read of an entry before a dump failed</summary>
        Partial
    }

    /// <summary>
    /// Tells what an entry is from its name. The folders of the path are not used, as the
    /// restore engine removes the ones every restored entry shares.
    /// </summary>
    /// <param name="path">The path the restore engine gives the entry</param>
    /// <returns>The kind of entry, and for a table its schema and name</returns>
    public static (EntryKind Kind, string Name, string? Schema, string? Table) Classify(string path)
    {
        var name = path.Substring(path.LastIndexOfAny(['/', '\\']) + 1);
        if (IsPartialEntryName(name))
            return (EntryKind.Partial, name, null, null);
        if (name == PostgreSQLSourceProvider.TableOfContentsName)
            return (EntryKind.TableOfContents, name, null, null);
        if (name == PostgreSQLSourceProvider.LargeObjectsName)
            return (EntryKind.LargeObjects, name, null, null);

        // The separator is escaped inside the names, so the first one is between them
        var separator = name.IndexOf(PostgreSQLSourceProvider.SchemaSeparator);
        if (separator <= 0 || separator == name.Length - 1)
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.NotADatabaseEntry(path), PostgreSQLEventIds.RestoreUnknownEntry);

        return (EntryKind.Table, name, PostgreSQLSourceProvider.UnescapeName(name.Substring(0, separator)), PostgreSQLSourceProvider.UnescapeName(name.Substring(separator + 1)));
    }

    /// <summary>
    /// Tells whether an entry name is what a failed dump left of an entry. A table can be named
    /// like the suffix, as in "public.duplicati-partial", so the name before the suffix must be an
    /// entry name of its own: a table, which has its own separator, or the large objects.
    /// </summary>
    /// <param name="name">The entry name, without folders</param>
    /// <returns>True for the part of a failed dump</returns>
    public static bool IsPartialEntryName(string name)
    {
        if (!name.EndsWith(StreamedSource.PartialEntrySuffix, StringComparison.Ordinal))
            return false;

        var entry = name.Substring(0, name.Length - StreamedSource.PartialEntrySuffix.Length);
        return entry.Contains(PostgreSQLSourceProvider.SchemaSeparator)
            || entry == PostgreSQLSourceProvider.LargeObjectsName
            || entry == PostgreSQLSourceProvider.TableOfContentsName;
    }

    /// <summary>
    /// Writes a name so the server reads it as that exact identifier
    /// </summary>
    /// <param name="name">The name</param>
    /// <returns>The quoted identifier</returns>
    public static string QuoteIdentifier(string name)
        => "\"" + name.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// Writes a text so the server reads it as that exact string
    /// </summary>
    /// <param name="text">The text</param>
    /// <returns>The quoted string</returns>
    public static string QuoteLiteral(string text)
        => (text.Contains('\\') ? "E'" : "'") + text.Replace("\\", "\\\\").Replace("'", "''") + "'";

    /// <summary>
    /// The schema and name of a table as one identifier
    /// </summary>
    public static string QualifiedName(string schema, string table)
        => QuoteIdentifier(schema) + "." + QuoteIdentifier(table);

    /// <summary>
    /// The value of the dbname parameter that names a database and nothing else,
    /// whatever characters the name has
    /// </summary>
    /// <param name="database">The database</param>
    /// <returns>A connection string with the database name</returns>
    public static string DatabaseConnectionString(string database)
        => "dbname='" + database.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    /// <summary>
    /// The arguments every pg_restore run has: the role names and grants of the source are not
    /// applied, as they differ between servers, and the first error ends the run
    /// </summary>
    /// <param name="database">The target database</param>
    /// <param name="arguments">The arguments of this run</param>
    /// <returns>The arguments</returns>
    public static IReadOnlyList<string> BuildRestoreArguments(string database, IEnumerable<string> arguments)
        => [.. arguments, PostgreSQLArguments.NoOwner, PostgreSQLArguments.NoAcl, PostgreSQLArguments.ExitOnError, PostgreSQLArguments.NoPassword, PostgreSQLArguments.DbNamePrefix + DatabaseConnectionString(database)];

    /// <summary>
    /// The arguments for a psql that runs the statements it reads from its standard input
    /// </summary>
    /// <param name="stopOnError">True to end at the first statement that fails</param>
    /// <returns>The arguments</returns>
    public static IReadOnlyList<string> BuildScriptArguments(bool stopOnError)
        => stopOnError
            ? [PostgreSQLArguments.NoPsqlrc, PostgreSQLArguments.NoPassword, PostgreSQLArguments.Quiet, PostgreSQLArguments.StopOnError]
            : [PostgreSQLArguments.NoPsqlrc, PostgreSQLArguments.NoPassword, PostgreSQLArguments.Quiet];

    /// <summary>
    /// The statements a psql reads before the rows of a table: they start a transaction, set up
    /// the session as the dump expects, and start the COPY.
    /// </summary>
    /// <param name="archive">The table of contents, or null if it is not part of the restore</param>
    /// <param name="schema">The schema of the table</param>
    /// <param name="table">The name of the table</param>
    /// <param name="tablesOnly">True when the rows are loaded into a table that exists</param>
    /// <param name="overwrite">True when rows that exist are replaced</param>
    /// <param name="restrictKey">The key that switches off the commands of psql for what follows</param>
    /// <returns>The statements</returns>
    public static string BuildLoadScript(PostgreSQLArchiveReader? archive, string schema, string table, bool tablesOnly, bool overwrite, string restrictKey)
    {
        var qualified = QualifiedName(schema, table);
        var sb = new StringBuilder();
        // What follows holds text from the backup, which must only ever be read as statements and rows
        sb.Append(RestrictCommand).Append(restrictKey).Append('\n');
        sb.Append("BEGIN;\n");
        // The COPY runs for as long as the restore takes to write the rows, so no limit of the role or database may end it
        sb.Append(LoadSessionSettings);

        string copy;
        if (archive != null)
        {
            // The rows are in the encoding of the dump, and the names in its COPY statements are written for an empty search path
            foreach (var description in new[] { PostgreSQLArchiveReader.EncodingDescription, PostgreSQLArchiveReader.StandardStringsDescription, PostgreSQLArchiveReader.SearchPathDescription })
                foreach (var entry in archive.Entries.Where(x => x.Description == description && !string.IsNullOrWhiteSpace(x.Definition)))
                    sb.Append(entry.Definition!.TrimEnd()).Append('\n');

            var data = archive.Entries.FirstOrDefault(x => x.HasData && x.Description == PostgreSQLArchiveReader.TableDataDescription && x.Namespace == schema && x.Tag == table);
            if (data == null || string.IsNullOrWhiteSpace(data.CopyStatement))
                throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TableNotInArchive(schema + "." + table), PostgreSQLEventIds.RestoreTableNotInArchive);
            copy = data.CopyStatement.TrimEnd();
        }
        else
        {
            // Without the table of contents the columns are taken in the order the table has them
            copy = $"COPY {qualified} FROM stdin;";
        }

        if (tablesOnly)
        {
            if (overwrite)
                sb.Append($"TRUNCATE TABLE ONLY {qualified};\n");
            else
            {
                // The body holds the name of the table, so the quote is one the name cannot hold
                var quote = "$d" + restrictKey + "$";
                sb.Append($"DO {quote} BEGIN IF EXISTS (SELECT 1 FROM {qualified}) THEN RAISE EXCEPTION '%', {QuoteLiteral(Strings.PostgreSQLRestoreProvider.TableHasRows(schema + "." + table))}; END IF; END {quote};\n");
            }
        }

        sb.Append(copy).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// The settings of a session that loads rows, as pg_restore sets them: no time limits, as the
    /// rows arrive as fast as the restore writes them, and XML values read as content.
    /// The transaction time limit exists from PostgreSQL 17, so it is only set there.
    /// </summary>
    public const string LoadSessionSettings =
        "SET statement_timeout = 0;\n"
        + "SET lock_timeout = 0;\n"
        + "SET idle_in_transaction_session_timeout = 0;\n"
        + "SET xmloption = content;\n"
        + "SELECT pg_catalog.set_config('transaction_timeout', '0', false) WHERE pg_catalog.current_setting('server_version_num')::int >= 170000;\n";

    /// <summary>The psql command that refuses every psql command after it, until it is lifted with the same key</summary>
    public const string RestrictCommand = "\\restrict ";
    /// <summary>The psql command that lifts <see cref="RestrictCommand"/></summary>
    public const string UnrestrictCommand = "\\unrestrict ";

    /// <summary>
    /// A key for <see cref="RestrictCommand"/> that text from a backup cannot know
    /// </summary>
    public static string NewRestrictKey()
        => Guid.NewGuid().ToString("N");

    /// <summary>
    /// The ids of the entries that create the objects of the database before the rows are loaded.
    /// The schemas are left out, as they are created only when missing, and so is the database itself.
    /// </summary>
    /// <param name="archive">The table of contents</param>
    /// <returns>The ids, in archive order</returns>
    public static IReadOnlyList<int> ObjectEntryIds(PostgreSQLArchiveReader archive)
        => EffectiveSections(archive.Entries)
            .Where(x => x.Section == PostgreSQLArchiveReader.SectionPreData)
            .Select(x => x.Entry)
            .Where(x => x.Description != null && x.Description != PostgreSQLArchiveReader.SchemaDescription && !x.Description.StartsWith(PostgreSQLArchiveReader.DatabaseDescriptionPrefix, StringComparison.Ordinal))
            .Select(x => x.DumpId)
            .ToList();

    /// <summary>
    /// The section each entry is restored in. Comments and security labels have no section of
    /// their own and are restored with the entry before them, as pg_restore does.
    /// </summary>
    /// <param name="entries">The entries of the table of contents, in archive order</param>
    /// <returns>The entries with their sections, in archive order</returns>
    public static IEnumerable<(PostgreSQLArchiveEntry Entry, int Section)> EffectiveSections(IEnumerable<PostgreSQLArchiveEntry> entries)
    {
        var current = PostgreSQLArchiveReader.SectionPreData;
        foreach (var entry in entries)
        {
            if (entry.Section != PostgreSQLArchiveReader.SectionNone)
                current = entry.Section;
            yield return (entry, current);
        }
    }

    /// <summary>
    /// The statements that make the target ready for the rows, as one transaction: with
    /// <paramref name="overwrite"/> the objects of the backup are dropped, then the schemas that
    /// are missing and the objects are created. If creating fails, nothing was dropped.
    /// </summary>
    /// <param name="archive">The table of contents</param>
    /// <param name="createStatements">The statements pg_restore writes for the objects</param>
    /// <param name="overwrite">True to drop the objects of the backup that exist first</param>
    /// <param name="restrictKey">The key that switches off the commands of psql for text from the backup</param>
    /// <returns>The statements, as UTF-8 around the bytes pg_restore wrote</returns>
    public static byte[] BuildSchemaScript(PostgreSQLArchiveReader archive, byte[] createStatements, bool overwrite, string restrictKey)
    {
        var sb = new StringBuilder();
        // A drop that fails, as for an object that is not there, is undone alone and the rest carries on
        sb.Append("\\set ON_ERROR_ROLLBACK on\n");
        sb.Append(RestrictCommand).Append(restrictKey).Append('\n');
        sb.Append("BEGIN;\n");
        if (overwrite)
            sb.Append(BuildDropScript(archive));
        sb.Append(UnrestrictCommand).Append(restrictKey).Append('\n');

        // From here the first error ends psql, and the server rolls everything back
        sb.Append("\\set ON_ERROR_ROLLBACK off\n");
        sb.Append("\\set ON_ERROR_STOP on\n");
        sb.Append(RestrictCommand).Append(restrictKey).Append('\n');
        // A schema that exists is kept, with whatever else it holds
        foreach (var entry in archive.Entries.Where(x => x.Description == PostgreSQLArchiveReader.SchemaDescription && !string.IsNullOrEmpty(x.Tag)))
            sb.Append($"CREATE SCHEMA IF NOT EXISTS {QuoteIdentifier(entry.Tag!)};\n");
        sb.Append(UnrestrictCommand).Append(restrictKey).Append('\n');

        // pg_restore restricts its own statements, with a key of its own. They are passed on as the bytes it wrote.
        var length = createStatements.Length;
        while (length > 0 && char.IsWhiteSpace((char)createStatements[length - 1]))
            length--;

        var script = new MemoryStream();
        script.Write(Encoding.UTF8.GetBytes(sb.ToString()));
        script.Write(createStatements, 0, length);
        script.Write("\nCOMMIT;\n"u8);
        return script.ToArray();
    }

    /// <summary>
    /// Tells whether pg_restore switched off the commands of psql for the statements it wrote
    /// </summary>
    /// <param name="statements">What pg_restore wrote</param>
    /// <returns>True when the statements are restricted</returns>
    public static bool IsRestricted(ReadOnlySpan<byte> statements)
    {
        var command = Encoding.UTF8.GetBytes(RestrictCommand);
        var lineCommand = Encoding.UTF8.GetBytes("\n" + RestrictCommand);
        return statements.StartsWith(command) || statements.IndexOf(lineCommand) >= 0;
    }

    /// <summary>
    /// The statements that drop the objects of a dump, last created first. They are run
    /// without stopping at errors, as an object that is not there cannot be dropped.
    /// The database itself is never dropped: a dump names the database it was taken from,
    /// which can be another database on the server that is restored to. The schemas are
    /// not dropped either, as they can hold objects that are not in the backup.
    /// </summary>
    /// <param name="archive">The table of contents</param>
    /// <returns>The statements</returns>
    public static string BuildDropScript(PostgreSQLArchiveReader archive)
    {
        var sb = new StringBuilder();
        sb.Append("SET client_min_messages = warning;\n");
        foreach (var entry in archive.Entries.Reverse())
        {
            if (entry.Description == null
                || entry.Description == PostgreSQLArchiveReader.SchemaDescription
                || entry.Description.StartsWith(PostgreSQLArchiveReader.DatabaseDescriptionPrefix, StringComparison.Ordinal))
                continue;

            if (entry.Description == PostgreSQLArchiveReader.LargeObjectMetadataDescription)
            {
                // From PostgreSQL 17 a group of large objects is one entry that lists their ids, one on each line, and has no drop statement
                foreach (var oid in (entry.Definition ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (oid.All(char.IsAsciiDigit))
                        sb.Append($"SELECT pg_catalog.lo_unlink('{oid}');\n");
            }
            else if (!string.IsNullOrWhiteSpace(entry.DropStatement))
                sb.Append(entry.DropStatement.TrimEnd()).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// The ids of the entries that restore the large objects. Before PostgreSQL 17 the objects
    /// themselves are created with the other objects, and only their content is loaded here.
    /// </summary>
    /// <param name="archive">The table of contents</param>
    /// <param name="objectsAreCreated">True when the entries that create objects before the data were already applied</param>
    /// <returns>The ids, in archive order</returns>
    public static IReadOnlyList<int> LargeObjectEntryIds(PostgreSQLArchiveReader archive, bool objectsAreCreated)
        => archive.Entries
            .Where(x => x.Description is PostgreSQLArchiveReader.LargeObjectDescription or PostgreSQLArchiveReader.LargeObjectMetadataDescription or PostgreSQLArchiveReader.LargeObjectsDescription)
            .Where(x => !objectsAreCreated || x.Section != PostgreSQLArchiveReader.SectionPreData)
            .Select(x => x.DumpId)
            .ToList();

    /// <summary>
    /// Reads the major version from the version pg_dump records in an archive, as in "16.4 (Debian 16.4-1)"
    /// </summary>
    /// <param name="clientVersion">The recorded version</param>
    /// <returns>The major version, or null if the text is not understood</returns>
    public static int? ArchiveClientMajor(string clientVersion)
    {
        var digits = new string(clientVersion.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var major) ? major : null;
    }

    /// <summary>
    /// Makes the target ready for the rows: reads the table of contents and, for a restore of
    /// the whole database, creates its objects. Runs once; a failure is given to every caller.
    /// </summary>
    private async Task PrepareAsync(CancellationToken cancel)
    {
        await m_prepareLock.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            if (m_prepareFailure != null)
                throw m_prepareFailure;
            if (m_prepared)
                return;

            try
            {
                await PrepareCoreAsync(cancel).ConfigureAwait(false);
                m_prepared = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_prepareFailure = ex;
                throw;
            }
        }
        finally
        {
            m_prepareLock.Release();
        }
    }

    private async Task PrepareCoreAsync(CancellationToken cancel)
    {
        var server = m_server ?? throw new InvalidOperationException("The provider is not initialized");
        var toolMajor = server.Major;

        var tableOfContents = m_tableOfContents;
        if (tableOfContents != null)
        {
            try
            {
                m_archive = await PostgreSQLArchiveReader.OpenAsync(new MemoryStream(tableOfContents, false), cancel).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is EndOfStreamException or UserInformationException)
            {
                // A table of contents whose restore failed is handed over with what was written of it
                throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TableOfContentsDamaged(ex.Message), PostgreSQLEventIds.RestoreTableOfContentsDamaged, ex);
            }

            // pg_restore reads the archives of its own version and older ones
            var archiveMajor = ArchiveClientMajor(m_archive.ClientVersion);
            if (archiveMajor == null || !PostgreSQLClientTools.SupportedMajorVersions.Contains(archiveMajor.Value))
                throw new UserInformationException(Strings.PostgreSQLRestoreProvider.UnsupportedArchiveClient(m_archive.ClientVersion), PostgreSQLEventIds.RestoreUnsupportedArchive);
            toolMajor = Math.Max(toolMajor, archiveMajor.Value);

            // A dump uses what its own server version has, which an older server may not know
            var sourceMajor = ArchiveClientMajor(m_archive.ServerVersion) ?? archiveMajor.Value;
            if (!Options.TablesOnly && sourceMajor > server.Major)
                throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TargetOlderThanSource(sourceMajor, server.Major), PostgreSQLEventIds.RestoreTargetTooOld);

            m_pgRestore = PostgreSQLClientTools.LocateTool(PostgreSQLClientTools.PgRestore, toolMajor, Connection);
            await PostgreSQLClientTools.VerifyToolVersionAsync(PostgreSQLClientTools.PgRestore, m_pgRestore, toolMajor, Connection, cancel).ConfigureAwait(false);
        }

        // The run ids are in the metadata, which is restored with the table of contents and before
        // any other entry. Without them it cannot be told whether the entries belong together, so
        // the restore stops here, before anything is dropped or loaded, and not after the rows.
        if (tableOfContents != null && m_tableOfContentsRunId == null && !Options.AllowMixedVersions)
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.EntryRunIdsUnknown(PostgreSQLRestoreOptions.AllowMixedVersionsOption), PostgreSQLEventIds.RestoreMixedVersions);

        if (Options.TablesOnly)
            return;

        if (m_archive == null)
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TableOfContentsMissing, PostgreSQLEventIds.RestoreNoTableOfContents);

        // Every object of the backup is created, and with --overwrite dropped first, so a selection
        // that leaves tables out is refused before anything in the database changes
        if (m_selectedEntries is { } selected && !Options.AllowMissingTables)
        {
            var unselected = RequiredEntryNames(m_archive).Where(x => !selected.Contains(x)).Select(PostgreSQLSourceProvider.UnescapeName).ToList();
            if (unselected.Count > 0)
                throw new UserInformationException(Strings.PostgreSQLRestoreProvider.SelectionIncomplete(ListNames(unselected), PostgreSQLRestoreOptions.AllowMissingTablesOption), PostgreSQLEventIds.RestoreSelectionIncomplete);
        }

        // pg_restore writes the statements for the objects, and they are run together with the drops in one
        // transaction, so a target that cannot take the objects keeps what it had
        byte[] createStatements;
        using (var list = WriteEntryList(ObjectEntryIds(m_archive)))
        {
            var (exitCode, errorOutput, output) = await PostgreSQLClientTools.RunFilterAsync(
                PostgreSQLClientTools.PgRestore,
                PostgreSQLClientTools.BuildStartInfo(m_pgRestore!, [PostgreSQLArguments.UseListPrefix + (string)list, PostgreSQLArguments.NoOwner, PostgreSQLArguments.NoAcl, PostgreSQLArguments.FileStandardOutput], Connection),
                tableOfContents!,
                cancel).ConfigureAwait(false);
            if (exitCode != 0)
                throw new UserInformationException(Strings.PostgreSQLSourceProvider.ToolFailed(PostgreSQLClientTools.PgRestore, exitCode, errorOutput), PostgreSQLEventIds.ToolFailed);
            createStatements = output;
        }

        // The statements hold text from the backup, and are only run by psql when pg_restore switched off the commands of psql for them
        if (!IsRestricted(createStatements))
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.ClientTooOld(PostgreSQLClientTools.PgRestore), PostgreSQLEventIds.RestoreClientTooOld);

        var script = BuildSchemaScript(m_archive, createStatements, Options.Overwrite, NewRestrictKey());
        var (scriptExitCode, scriptErrors) = await PostgreSQLClientTools.RunWithInputAsync(
            PostgreSQLClientTools.Psql,
            PostgreSQLClientTools.BuildStartInfo(m_psql!, BuildScriptArguments(false), Connection),
            script,
            cancel).ConfigureAwait(false);
        if (scriptExitCode != 0)
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.ToolFailed(PostgreSQLClientTools.Psql, scriptExitCode, scriptErrors), PostgreSQLEventIds.ToolFailed);
        if (!string.IsNullOrWhiteSpace(scriptErrors))
            Logging.Log.WriteVerboseMessage(LOGTAG, "DropExistingObjects", "Dropping the objects of the backup from the database reported: {0}", scriptErrors);
    }

    /// <summary>
    /// Runs pg_restore on an archive that is the table of contents alone, which is all it reads
    /// for entries that have no data
    /// </summary>
    private async Task RunRestoreAsync(IEnumerable<string> arguments, byte[] tableOfContents, CancellationToken cancel)
    {
        var (exitCode, errorOutput) = await PostgreSQLClientTools.RunWithInputAsync(
            PostgreSQLClientTools.PgRestore,
            PostgreSQLClientTools.BuildStartInfo(m_pgRestore!, BuildRestoreArguments(Connection.Database, arguments), Connection),
            tableOfContents,
            cancel).ConfigureAwait(false);
        if (exitCode != 0)
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.ToolFailed(PostgreSQLClientTools.PgRestore, exitCode, errorOutput), PostgreSQLEventIds.ToolFailed);
    }

    /// <summary>
    /// Writes the file pg_restore reads the entries to restore from: one id on each line
    /// </summary>
    private static Library.Utility.TempFile WriteEntryList(IEnumerable<int> ids)
    {
        var file = new Library.Utility.TempFile();
        File.WriteAllLines(file, ids.Select(x => x + ";"));
        return file;
    }

    /// <summary>
    /// Creates a table from its definition in the table of contents if the database does not have it
    /// </summary>
    private async Task CreateTableIfMissingAsync(string schema, string table, CancellationToken cancel)
    {
        if (m_archive == null)
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TableOfContentsNeeded(schema + "." + table), PostgreSQLEventIds.RestoreNoTableOfContents);

        var definition = m_archive.Entries.FirstOrDefault(x => x.Description == PostgreSQLArchiveReader.TableDescription && x.Namespace == schema && x.Tag == table)
            ?? throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TableNotInArchive(schema + "." + table), PostgreSQLEventIds.RestoreTableNotInArchive);

        // One at a time, as two tables may need the same schema
        await m_createTableLock.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            var exists = (await PostgreSQLClientTools.QueryAsync(m_psql!, $"select to_regclass({QuoteLiteral(QualifiedName(schema, table))}) is not null", Connection, cancel).ConfigureAwait(false)).Trim();
            if (exists == "t")
                return;

            await PostgreSQLClientTools.QueryAsync(m_psql!, $"create schema if not exists {QuoteIdentifier(schema)}", Connection, cancel).ConfigureAwait(false);

            using var list = WriteEntryList([definition.DumpId]);
            await RunRestoreAsync([PostgreSQLArguments.UseListPrefix + (string)list], m_tableOfContents!, cancel).ConfigureAwait(false);
        }
        finally
        {
            m_createTableLock.Release();
        }
    }

    private async Task<Stream> OpenTableAsync(string name, string schema, string table, CancellationToken cancel)
    {
        await PrepareAsync(cancel).ConfigureAwait(false);

        if (Options.TablesOnly && Options.CreateTables)
            await CreateTableIfMissingAsync(schema, table, cancel).ConfigureAwait(false);

        var script = BuildLoadScript(m_archive, schema, table, Options.TablesOnly, Options.Overwrite, NewRestrictKey());
        var psql = await PostgreSQLClientTools.StartWithInputAsync(
            PostgreSQLClientTools.Psql,
            PostgreSQLClientTools.BuildStartInfo(m_psql!, BuildScriptArguments(true), Connection),
            Encoding.UTF8.GetBytes(script),
            cancel).ConfigureAwait(false);

        return new PostgreSQLTableStream(psql, schema + "." + table, () => m_loadedTables[name] = true, failure =>
        {
            Logging.Log.WriteErrorMessage(LOGTAG, "TableIncomplete", null, "{0}", failure);
            m_failures.Enqueue(failure);
        });
    }

    private async Task<Stream> OpenLargeObjectsAsync(CancellationToken cancel)
    {
        await PrepareAsync(cancel).ConfigureAwait(false);
        if (m_archive == null)
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TableOfContentsNeeded(PostgreSQLSourceProvider.LargeObjectsName), PostgreSQLEventIds.RestoreNoTableOfContents);

        // pg_restore reads the list after the table of contents, so the file is kept until pg_restore has ended
        var list = WriteEntryList(LargeObjectEntryIds(m_archive, !Options.TablesOnly));
        // Loaded on their own, the objects are created, so the ones that exist are removed first when they may be replaced
        var arguments = new List<string> { PostgreSQLArguments.UseListPrefix + (string)list, PostgreSQLArguments.SingleTransaction };
        if (Options.TablesOnly && Options.Overwrite)
            arguments.AddRange(LargeObjectReplaceArguments);
        PostgreSQLClientTools.RunningTool restore;
        try
        {
            restore = await PostgreSQLClientTools.StartWithInputAsync(
                PostgreSQLClientTools.PgRestore,
                PostgreSQLClientTools.BuildStartInfo(m_pgRestore!, BuildRestoreArguments(Connection.Database, arguments), Connection),
                m_tableOfContents!,
                cancel).ConfigureAwait(false);
        }
        catch
        {
            list.Dispose();
            throw;
        }

        return new PostgreSQLLargeObjectStream(restore, cancel, failure =>
        {
            list.Dispose();
            if (failure == null)
                m_largeObjectsLoaded = true;
            else
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "LargeObjectsFailed", null, "Failed to restore the large objects: {0}", failure);
                m_failures.Enqueue(failure);
            }
        });
    }

    private Task<Stream> OpenAsync(string path, CancellationToken cancel)
    {
        var (kind, name, schema, table) = Classify(path);
        switch (kind)
        {
            case EntryKind.TableOfContents:
                return Task.FromResult<Stream>(new PostgreSQLTableOfContentsStream(data => m_tableOfContents = data));
            case EntryKind.LargeObjects:
                return OpenLargeObjectsAsync(cancel);
            case EntryKind.Partial:
                // What a failed dump left of a table is not a table
                Logging.Log.WriteWarningMessage(LOGTAG, "PartialEntrySkipped", null, "The entry {0} holds only the part of a table that was read before a dump failed, and is not restored to the database", path);
                return Task.FromResult<Stream>(new PostgreSQLDiscardStream());
            default:
                return OpenTableAsync(name, schema!, table!, cancel);
        }
    }

    /// <inheritdoc />
    public Task<Stream> OpenWrite(string path, CancellationToken cancel)
        => OpenAsync(path, cancel);

    /// <inheritdoc />
    public Task<Stream> OpenReadWrite(string path, CancellationToken cancel)
        => OpenAsync(path, cancel);

    /// <inheritdoc />
    public Task<Stream> OpenRead(string path, CancellationToken cancel)
        => throw new NotSupportedException(Strings.PostgreSQLRestoreProvider.ReadNotSupported(path));

    /// <summary>
    /// Records which entries the restore writes. A restore of the whole database needs the table
    /// of contents, which is refused here; whether every table is selected is only known from the
    /// table of contents, and is checked when it has been restored, before anything is dropped.
    /// </summary>
    public Task SetRestoreSelection(IReadOnlyCollection<string> targetPaths, CancellationToken cancel)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in targetPaths)
        {
            var (kind, name, _, _) = Classify(path);
            if (kind != EntryKind.Partial)
                names.Add(name);
        }

        if (!Options.DryRun && !Options.TablesOnly && !names.Contains(PostgreSQLSourceProvider.TableOfContentsName))
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TableOfContentsMissing, PostgreSQLEventIds.RestoreNoTableOfContents);

        m_selectedEntries = names;
        return Task.CompletedTask;
    }

    /// <summary>
    /// The entries a restore of the whole database needs: the rows of every table, and the large
    /// objects when the backup has them
    /// </summary>
    /// <param name="archive">The table of contents</param>
    /// <returns>The entry names</returns>
    public static IReadOnlyList<string> RequiredEntryNames(PostgreSQLArchiveReader archive)
    {
        var names = archive.Entries
            .Where(x => x.HasData && x.Description == PostgreSQLArchiveReader.TableDataDescription)
            .Select(x => PostgreSQLSourceProvider.TableEntryName(x.Namespace ?? string.Empty, x.Tag ?? string.Empty))
            .ToList();

        // Without their content the large objects would be created empty, and that is not reported otherwise
        if (archive.Entries.Any(x => x.HasData && x.Description == PostgreSQLArchiveReader.LargeObjectsDescription))
            names.Add(PostgreSQLSourceProvider.LargeObjectsName);

        return names;
    }

    /// <summary>
    /// Nothing is reported as existing, so the restore engine writes every entry from its start to its end
    /// </summary>
    public Task<bool> FileExists(string path, CancellationToken cancel)
        => Task.FromResult(false);

    /// <inheritdoc />
    public Task<long> GetFileLength(string path, CancellationToken cancel)
        => throw new NotSupportedException(Strings.PostgreSQLRestoreProvider.ReadNotSupported(path));

    /// <inheritdoc />
    public Task<bool> CreateFolderIfNotExists(string path, CancellationToken cancel)
        => Task.FromResult(false);

    /// <inheritdoc />
    public Task<bool> HasReadOnlyAttribute(string path, CancellationToken cancel)
        => Task.FromResult(false);

    /// <inheritdoc />
    public Task ClearReadOnlyAttribute(string path, CancellationToken cancel)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task DeleteFolder(string path, CancellationToken cancel)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task DeleteFile(string path, CancellationToken cancel)
        => Task.CompletedTask;

    /// <summary>
    /// The metadata is not applied to the database. It tells which dump an entry comes from,
    /// which shows whether the tables of a version belong together.
    /// </summary>
    public Task<bool> WriteMetadata(string path, Dictionary<string, string?> metadata, bool restoreSymlinkMetadata, bool restorePermissions, bool hasRestoredEntriesBelow, CancellationToken cancel)
    {
        if (!metadata.TryGetValue(PostgreSQLSourceProvider.MetadataRunId, out var runId) || string.IsNullOrEmpty(runId))
            return Task.FromResult(false);
        if (!metadata.TryGetValue(PostgreSQLSourceProvider.MetadataType, out var type))
            return Task.FromResult(false);

        if (type == PostgreSQLSourceProvider.TypeTableOfContents)
            m_tableOfContentsRunId = runId;
        else if (type == PostgreSQLSourceProvider.TypeTable || type == PostgreSQLSourceProvider.TypeLargeObjects)
            m_entryRunIds[Classify(path).Name] = runId;

        return Task.FromResult(true);
    }

    private static string ListNames(IReadOnlyCollection<string> names)
        => names.Count <= MaxNamesListed
            ? string.Join(", ", names)
            : string.Join(", ", names.Take(MaxNamesListed)) + $" (+{names.Count - MaxNamesListed})";

    /// <summary>
    /// The restored tables, and the large objects, that do not come from the dump of the table of
    /// contents, or whose dump is not known
    /// </summary>
    /// <param name="tableOfContentsRunId">The run id of the table of contents</param>
    /// <returns>The names of the entries, sorted</returns>
    private List<string> EntriesFromOtherDumps(string tableOfContentsRunId)
    {
        var loaded = m_loadedTables.Keys.ToList();
        if (m_largeObjectsLoaded)
            loaded.Add(PostgreSQLSourceProvider.LargeObjectsName);

        return loaded
            .Where(x => !m_entryRunIds.TryGetValue(x, out var runId) || runId != tableOfContentsRunId)
            .Select(PostgreSQLSourceProvider.UnescapeName)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task Finalize(Action<double>? progressCallback, bool isComplete, CancellationToken cancel)
    {
        // A dry run writes no entries, so there is nothing to complete
        if (Options.DryRun)
            return;

        if (!isComplete)
        {
            if (!Options.TablesOnly)
                Logging.Log.WriteWarningMessage(LOGTAG, "DatabaseIncomplete", null, "The restore did not complete, so the sequence values, indexes and constraints of the database {0} were not applied", Connection.Database);
            return;
        }

        if (!m_failures.IsEmpty)
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.StepsFailed(string.Join("; ", m_failures)), PostgreSQLEventIds.RestoreFailed);

        if (Options.TablesOnly)
        {
            // With the table of contents the rows are loaded with the columns of its COPY statements, and the
            // large objects with its entry ids, which only fit the same dump. An unknown run id of the table
            // of contents already stopped the restore before anything was loaded.
            if (m_tableOfContents != null && m_tableOfContentsRunId is { } tocRunId && !Options.AllowMixedVersions)
            {
                var otherDump = EntriesFromOtherDumps(tocRunId);
                if (otherDump.Count > 0)
                    throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TablesFromOtherDump(ListNames(otherDump), PostgreSQLRestoreOptions.AllowMixedVersionsOption), PostgreSQLEventIds.RestoreMixedVersions);
            }
            return;
        }

        // A database without tables has no entry that made the target ready
        await PrepareAsync(cancel).ConfigureAwait(false);
        var archive = m_archive!;

        var requiredEntries = RequiredEntryNames(archive);
        var required = requiredEntries.Count;
        var missing = requiredEntries
            .Where(x => x == PostgreSQLSourceProvider.LargeObjectsName ? !m_largeObjectsLoaded : !m_loadedTables.ContainsKey(x))
            .Select(PostgreSQLSourceProvider.UnescapeName)
            .ToList();

        if (missing.Count > 0)
        {
            if (!Options.AllowMissingTables)
                throw new UserInformationException(Strings.PostgreSQLRestoreProvider.PartialDatabase(required - missing.Count, required, ListNames(missing), PostgreSQLRestoreOptions.AllowMissingTablesOption), PostgreSQLEventIds.RestorePartialDatabase);
            Logging.Log.WriteWarningMessage(LOGTAG, "PartialDatabase", null, "{0}", Strings.PostgreSQLRestoreProvider.PartialDatabaseWarning(required - missing.Count, required, ListNames(missing)));
        }

        // An unknown run id of the table of contents already stopped the restore in PrepareAsync
        if (!Options.AllowMixedVersions && m_tableOfContentsRunId is { } tableOfContentsRunId)
        {
            var stale = EntriesFromOtherDumps(tableOfContentsRunId);
            if (stale.Count > 0)
                throw new UserInformationException(Strings.PostgreSQLRestoreProvider.MixedVersions(ListNames(stale), PostgreSQLRestoreOptions.AllowMixedVersionsOption), PostgreSQLEventIds.RestoreMixedVersions);
        }

        var sequences = archive.Entries.Where(x => x.Description == PostgreSQLArchiveReader.SequenceSetDescription).Select(x => x.DumpId).ToList();
        if (sequences.Count > 0)
        {
            using var list = WriteEntryList(sequences);
            await RunRestoreAsync([PostgreSQLArguments.UseListPrefix + (string)list, PostgreSQLArguments.SingleTransaction], m_tableOfContents!, cancel).ConfigureAwait(false);
        }

        progressCallback?.Invoke(ProgressBeforePostData);

        // The indexes and constraints are created last, when all the rows are in place
        await RunRestoreAsync([PostgreSQLArguments.SectionPostData], m_tableOfContents!, cancel).ConfigureAwait(false);
        progressCallback?.Invoke(1);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        m_prepareLock.Dispose();
        m_createTableLock.Dispose();
    }
}
