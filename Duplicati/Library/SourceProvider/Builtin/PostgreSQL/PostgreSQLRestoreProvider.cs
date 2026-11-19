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
public sealed class PostgreSQLRestoreProvider : IRestoreDestinationProviderModule
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
    public static readonly string[] LargeObjectReplaceArguments = ["--clean", "--if-exists"];

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
    /// <param name="url">The target URL</param>
    /// <param name="options">The restore options</param>
    public PostgreSQLRestoreProvider(string url, Dictionary<string, string?> options)
    {
        m_options = PostgreSQLRestoreOptions.Parse(url, options);
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
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.ClientTooOld(PostgreSQLClientTools.Psql), "PostgreSQLRestoreClientTooOld");
        }

        // Restoring over a database by accident cannot be undone, so it has to be asked for
        if (!Options.TablesOnly && !Options.Overwrite)
        {
            var output = (await PostgreSQLClientTools.QueryAsync(m_psql, UserTableCountQuery, Connection, cancel).ConfigureAwait(false)).Trim();
            if (!long.TryParse(output, out var tables))
                throw new UserInformationException(Strings.PostgreSQLSourceProvider.ToolFailed(PostgreSQLClientTools.Psql, 0, output), "PostgreSQLToolFailed");
            if (tables > 0)
                throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TargetNotEmpty(Connection.Database, tables), "PostgreSQLRestoreTargetNotEmpty");
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
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.NotADatabaseEntry(path), "PostgreSQLRestoreUnknownEntry");

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
        => [.. arguments, "--no-owner", "--no-acl", "--exit-on-error", "--no-password", "--dbname=" + DatabaseConnectionString(database)];

    /// <summary>
    /// The arguments for a psql that runs the statements it reads from its standard input
    /// </summary>
    /// <param name="stopOnError">True to end at the first statement that fails</param>
    /// <returns>The arguments</returns>
    public static IReadOnlyList<string> BuildScriptArguments(bool stopOnError)
        => stopOnError
            ? ["--no-psqlrc", "--no-password", "--quiet", "--set=ON_ERROR_STOP=1"]
            : ["--no-psqlrc", "--no-password", "--quiet"];

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

        string copy;
        if (archive != null)
        {
            // The rows are in the encoding of the dump, and the names in its COPY statements are written for an empty search path
            foreach (var description in new[] { PostgreSQLArchiveReader.EncodingDescription, PostgreSQLArchiveReader.StandardStringsDescription, PostgreSQLArchiveReader.SearchPathDescription })
                foreach (var entry in archive.Entries.Where(x => x.Description == description && !string.IsNullOrWhiteSpace(x.Definition)))
                    sb.Append(entry.Definition!.TrimEnd()).Append('\n');

            var data = archive.Entries.FirstOrDefault(x => x.HasData && x.Description == PostgreSQLArchiveReader.TableDataDescription && x.Namespace == schema && x.Tag == table);
            if (data == null || string.IsNullOrWhiteSpace(data.CopyStatement))
                throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TableNotInArchive(schema + "." + table), "PostgreSQLRestoreTableNotInArchive");
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
                throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TableOfContentsDamaged(ex.Message), "PostgreSQLRestoreTableOfContentsDamaged", ex);
            }

            // pg_restore reads the archives of its own version and older ones
            var archiveMajor = ArchiveClientMajor(m_archive.ClientVersion);
            if (archiveMajor == null || !PostgreSQLClientTools.SupportedMajorVersions.Contains(archiveMajor.Value))
                throw new UserInformationException(Strings.PostgreSQLRestoreProvider.UnsupportedArchiveClient(m_archive.ClientVersion), "PostgreSQLRestoreUnsupportedArchive");
            toolMajor = Math.Max(toolMajor, archiveMajor.Value);

            // A dump uses what its own server version has, which an older server may not know
            var sourceMajor = ArchiveClientMajor(m_archive.ServerVersion) ?? archiveMajor.Value;
            if (!Options.TablesOnly && sourceMajor > server.Major)
                throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TargetOlderThanSource(sourceMajor, server.Major), "PostgreSQLRestoreTargetTooOld");

            m_pgRestore = PostgreSQLClientTools.LocateTool(PostgreSQLClientTools.PgRestore, toolMajor, Connection);
            await PostgreSQLClientTools.VerifyToolVersionAsync(PostgreSQLClientTools.PgRestore, m_pgRestore, toolMajor, Connection, cancel).ConfigureAwait(false);
        }

        if (Options.TablesOnly)
            return;

        if (m_archive == null)
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TableOfContentsMissing, "PostgreSQLRestoreNoTableOfContents");

        // pg_restore writes the statements for the objects, and they are run together with the drops in one
        // transaction, so a target that cannot take the objects keeps what it had
        byte[] createStatements;
        using (var list = WriteEntryList(ObjectEntryIds(m_archive)))
        {
            var (exitCode, errorOutput, output) = await PostgreSQLClientTools.RunFilterAsync(
                PostgreSQLClientTools.PgRestore,
                PostgreSQLClientTools.BuildStartInfo(m_pgRestore!, ["--use-list=" + (string)list, "--no-owner", "--no-acl", "--file=-"], Connection),
                tableOfContents!,
                cancel).ConfigureAwait(false);
            if (exitCode != 0)
                throw new UserInformationException(Strings.PostgreSQLSourceProvider.ToolFailed(PostgreSQLClientTools.PgRestore, exitCode, errorOutput), "PostgreSQLToolFailed");
            createStatements = output;
        }

        // The statements hold text from the backup, and are only run by psql when pg_restore switched off the commands of psql for them
        if (!IsRestricted(createStatements))
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.ClientTooOld(PostgreSQLClientTools.PgRestore), "PostgreSQLRestoreClientTooOld");

        var script = BuildSchemaScript(m_archive, createStatements, Options.Overwrite, NewRestrictKey());
        var (scriptExitCode, scriptErrors) = await PostgreSQLClientTools.RunWithInputAsync(
            PostgreSQLClientTools.Psql,
            PostgreSQLClientTools.BuildStartInfo(m_psql!, BuildScriptArguments(false), Connection),
            script,
            cancel).ConfigureAwait(false);
        if (scriptExitCode != 0)
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.ToolFailed(PostgreSQLClientTools.Psql, scriptExitCode, scriptErrors), "PostgreSQLToolFailed");
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
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.ToolFailed(PostgreSQLClientTools.PgRestore, exitCode, errorOutput), "PostgreSQLToolFailed");
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
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TableOfContentsNeeded(schema + "." + table), "PostgreSQLRestoreNoTableOfContents");

        var definition = m_archive.Entries.FirstOrDefault(x => x.Description == PostgreSQLArchiveReader.TableDescription && x.Namespace == schema && x.Tag == table)
            ?? throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TableNotInArchive(schema + "." + table), "PostgreSQLRestoreTableNotInArchive");

        // One at a time, as two tables may need the same schema
        await m_createTableLock.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            var exists = (await PostgreSQLClientTools.QueryAsync(m_psql!, $"select to_regclass({QuoteLiteral(QualifiedName(schema, table))}) is not null", Connection, cancel).ConfigureAwait(false)).Trim();
            if (exists == "t")
                return;

            await PostgreSQLClientTools.QueryAsync(m_psql!, $"create schema if not exists {QuoteIdentifier(schema)}", Connection, cancel).ConfigureAwait(false);

            using var list = WriteEntryList([definition.DumpId]);
            await RunRestoreAsync(["--use-list=" + (string)list], m_tableOfContents!, cancel).ConfigureAwait(false);
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
        var psql = PostgreSQLClientTools.StartForInput(PostgreSQLClientTools.Psql, PostgreSQLClientTools.BuildStartInfo(m_psql!, BuildScriptArguments(true), Connection));
        try
        {
            await psql.Input.WriteAsync(Encoding.UTF8.GetBytes(script), cancel).ConfigureAwait(false);
        }
        catch (IOException)
        {
            using (psql)
            {
                var (exitCode, errorOutput) = await psql.WaitForExitAsync(cancel).ConfigureAwait(false);
                throw new IOException(Strings.PostgreSQLSourceProvider.ToolFailed(PostgreSQLClientTools.Psql, exitCode, errorOutput));
            }
        }
        catch
        {
            psql.Dispose();
            throw;
        }

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
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TableOfContentsNeeded(PostgreSQLSourceProvider.LargeObjectsName), "PostgreSQLRestoreNoTableOfContents");

        // The list is read when pg_restore starts, before it reads the archive
        var list = WriteEntryList(LargeObjectEntryIds(m_archive, !Options.TablesOnly));
        // Loaded on their own, the objects are created, so the ones that exist are removed first when they may be replaced
        var arguments = new List<string> { "--use-list=" + (string)list, "--single-transaction" };
        if (Options.TablesOnly && Options.Overwrite)
            arguments.AddRange(LargeObjectReplaceArguments);
        PostgreSQLClientTools.RunningTool restore;
        try
        {
            restore = PostgreSQLClientTools.StartForInput(
                PostgreSQLClientTools.PgRestore,
                PostgreSQLClientTools.BuildStartInfo(m_pgRestore!, BuildRestoreArguments(Connection.Database, arguments), Connection));
        }
        catch
        {
            list.Dispose();
            throw;
        }

        try
        {
            await restore.Input.WriteAsync(m_tableOfContents!, cancel).ConfigureAwait(false);
        }
        catch (IOException)
        {
            using (restore)
            using (list)
            {
                var (exitCode, errorOutput) = await restore.WaitForExitAsync(cancel).ConfigureAwait(false);
                throw new IOException(Strings.PostgreSQLSourceProvider.ToolFailed(PostgreSQLClientTools.PgRestore, exitCode, errorOutput));
            }
        }
        catch
        {
            restore.Dispose();
            list.Dispose();
            throw;
        }

        return new PostgreSQLLargeObjectStream(restore, failure =>
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
            throw new UserInformationException(Strings.PostgreSQLRestoreProvider.StepsFailed(string.Join("; ", m_failures)), "PostgreSQLRestoreFailed");

        if (Options.TablesOnly)
        {
            // With the table of contents the rows are loaded with the columns of its COPY statements,
            // which only fit rows from the same dump
            var tocRunId = m_tableOfContentsRunId;
            if (m_tableOfContents != null && !m_loadedTables.IsEmpty && !Options.AllowMixedVersions)
            {
                // Without the metadata it cannot be told whether the rows fit, so the check fails closed
                if (tocRunId == null)
                    throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TablesRunIdsUnknown(PostgreSQLRestoreOptions.AllowMixedVersionsOption), "PostgreSQLRestoreMixedVersions");

                var otherDump = m_loadedTables.Keys
                    .Where(x => !m_entryRunIds.TryGetValue(x, out var runId) || runId != tocRunId)
                    .Select(PostgreSQLSourceProvider.UnescapeName)
                    .Order(StringComparer.Ordinal)
                    .ToList();
                if (otherDump.Count > 0)
                    throw new UserInformationException(Strings.PostgreSQLRestoreProvider.TablesFromOtherDump(ListNames(otherDump), PostgreSQLRestoreOptions.AllowMixedVersionsOption), "PostgreSQLRestoreMixedVersions");
            }
            return;
        }

        // A database without tables has no entry that made the target ready
        await PrepareAsync(cancel).ConfigureAwait(false);
        var archive = m_archive!;

        var tables = archive.Entries
            .Where(x => x.HasData && x.Description == PostgreSQLArchiveReader.TableDataDescription)
            .Select(x => PostgreSQLSourceProvider.TableEntryName(x.Namespace ?? string.Empty, x.Tag ?? string.Empty))
            .ToList();
        var missing = tables.Where(x => !m_loadedTables.ContainsKey(x)).Select(PostgreSQLSourceProvider.UnescapeName).ToList();
        var required = tables.Count;

        // Without their content the large objects would be created empty, and that is not reported otherwise
        if (archive.Entries.Any(x => x.HasData && x.Description == PostgreSQLArchiveReader.LargeObjectsDescription))
        {
            required++;
            if (!m_largeObjectsLoaded)
                missing.Add(PostgreSQLSourceProvider.LargeObjectsName);
        }

        if (missing.Count > 0)
        {
            if (!Options.AllowMissingTables)
                throw new UserInformationException(Strings.PostgreSQLRestoreProvider.PartialDatabase(required - missing.Count, required, ListNames(missing), PostgreSQLRestoreOptions.AllowMissingTablesOption), "PostgreSQLRestorePartialDatabase");
            Logging.Log.WriteWarningMessage(LOGTAG, "PartialDatabase", null, "{0}", Strings.PostgreSQLRestoreProvider.PartialDatabaseWarning(required - missing.Count, required, ListNames(missing)));
        }

        if (!Options.AllowMixedVersions)
        {
            // Without the metadata it cannot be told whether the entries belong together, so the restore fails closed
            var tableOfContentsRunId = m_tableOfContentsRunId;
            if (tableOfContentsRunId == null)
                throw new UserInformationException(Strings.PostgreSQLRestoreProvider.EntryRunIdsUnknown(PostgreSQLRestoreOptions.AllowMixedVersionsOption), "PostgreSQLRestoreMixedVersions");

            var loaded = m_loadedTables.Keys.ToList();
            if (m_largeObjectsLoaded)
                loaded.Add(PostgreSQLSourceProvider.LargeObjectsName);
            var stale = loaded
                .Where(x => !m_entryRunIds.TryGetValue(x, out var runId) || runId != tableOfContentsRunId)
                .Select(PostgreSQLSourceProvider.UnescapeName)
                .Order(StringComparer.Ordinal)
                .ToList();
            if (stale.Count > 0)
                throw new UserInformationException(Strings.PostgreSQLRestoreProvider.MixedVersions(ListNames(stale), PostgreSQLRestoreOptions.AllowMixedVersionsOption), "PostgreSQLRestoreMixedVersions");
        }

        var sequences = archive.Entries.Where(x => x.Description == PostgreSQLArchiveReader.SequenceSetDescription).Select(x => x.DumpId).ToList();
        if (sequences.Count > 0)
        {
            using var list = WriteEntryList(sequences);
            await RunRestoreAsync(["--use-list=" + (string)list, "--single-transaction"], m_tableOfContents!, cancel).ConfigureAwait(false);
        }

        progressCallback?.Invoke(ProgressBeforePostData);

        // The indexes and constraints are created last, when all the rows are in place
        await RunRestoreAsync(["--section=post-data"], m_tableOfContents!, cancel).ConfigureAwait(false);
        progressCallback?.Invoke(1);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        m_prepareLock.Dispose();
        m_createTableLock.Dispose();
    }
}
