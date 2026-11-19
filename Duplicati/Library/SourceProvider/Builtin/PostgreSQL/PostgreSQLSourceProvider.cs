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

using System.Runtime.CompilerServices;
using System.Text;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.SourceProvider.Builtin.PostgreSQL;

/// <summary>
/// A source provider that backs up one PostgreSQL database by running pg_dump and reading its
/// output as a stream. Nothing is written to disk, so the database can be larger than the
/// free space where the backup runs.
/// <para>
/// The entries below the mountpoint are:
/// <c>toc</c>, the header and table of contents of the archive as pg_dump wrote them;
/// <c>tables/&lt;schema&gt;.&lt;table&gt;</c>, the rows of each table as COPY text, cut into
/// padded segments (see <see cref="PostgreSQLSegmenter"/>);
/// and <c>large-objects</c>, the large object blocks of the archive, when they are included.
/// The names come from the schema and table names, so they are the same in every backup.
/// </para>
/// <para>
/// The entries can only be read once and in the order they are listed, see <see cref="ISequentialSourceProvider"/>.
/// </para>
/// </summary>
public sealed class PostgreSQLSourceProvider : ISourceProviderModule, ISequentialSourceProvider
{
    /// <summary>The name of the entry with the table of contents</summary>
    public const string TableOfContentsName = "toc";
    /// <summary>The name of the folder with the tables</summary>
    public const string TablesFolderName = "tables";
    /// <summary>The name of the entry with the large objects</summary>
    public const string LargeObjectsName = "large-objects";
    /// <summary>The character between the schema and the table in an entry name</summary>
    public const char SchemaSeparator = '.';

    /// <summary>The prefix of the metadata the provider records</summary>
    public const string MetadataPrefix = "postgresql:";
    /// <summary>The metadata with the type of an entry</summary>
    public const string MetadataType = MetadataPrefix + "Type";
    /// <summary>The metadata with the id of the dump an entry came from; entries of one consistent dump share it</summary>
    public const string MetadataRunId = MetadataPrefix + "RunId";
    /// <summary>The metadata with the catalog object id of a table</summary>
    public const string MetadataOid = MetadataPrefix + "Oid";
    /// <summary>The metadata with the id of the table data in the table of contents of its dump</summary>
    public const string MetadataDumpId = MetadataPrefix + "DumpId";
    /// <summary>The metadata with the schema of a table</summary>
    public const string MetadataSchema = MetadataPrefix + "Schema";
    /// <summary>The metadata with the name of a table</summary>
    public const string MetadataTable = MetadataPrefix + "Table";
    /// <summary>The metadata with the way the rows are cut into segments</summary>
    public const string MetadataSegmentScheme = MetadataPrefix + "SegmentScheme";
    /// <summary>The metadata with the block size the segments are padded to</summary>
    public const string MetadataSegmentBlockSize = MetadataPrefix + "SegmentBlockSize";
    /// <summary>The metadata with the server version</summary>
    public const string MetadataServerVersion = MetadataPrefix + "ServerVersion";
    /// <summary>The metadata with the version of pg_dump</summary>
    public const string MetadataClientVersion = MetadataPrefix + "ClientVersion";
    /// <summary>The metadata with the archive format version</summary>
    public const string MetadataArchiveVersion = MetadataPrefix + "ArchiveVersion";
    /// <summary>The metadata with the size of the database on the server, in bytes</summary>
    public const string MetadataDatabaseSize = MetadataPrefix + "DatabaseSize";
    /// <summary>The metadata with the database name</summary>
    public const string MetadataDatabase = MetadataPrefix + "Database";

    /// <summary>
    /// The metadata on the root entry that tells a client what kind of source the backup holds,
    /// so it can offer a database as the restore target
    /// </summary>
    public const string MetadataExtendedType = "ExtType";

    /// <summary>The type of the root entry</summary>
    public const string TypeDatabase = "Database";
    /// <summary>The type of the table of contents entry</summary>
    public const string TypeTableOfContents = "TableOfContents";
    /// <summary>The type of the folder with the tables</summary>
    public const string TypeTables = "Tables";
    /// <summary>The type of a table entry</summary>
    public const string TypeTable = "Table";
    /// <summary>The type of the large objects entry</summary>
    public const string TypeLargeObjects = "LargeObjects";

    /// <summary>
    /// The statement that reads the server version and the size of the database
    /// </summary>
    private const string ServerInfoQuery = "select current_setting('server_version_num') || '|' || pg_database_size(current_database())";

    /// <summary>
    /// The id the session knows the large objects entry by; dump ids are positive
    /// </summary>
    internal const int LargeObjectsId = -1;

    private readonly string m_mountPoint;
    private readonly string m_url;
    private readonly Dictionary<string, string?> m_rawOptions;
    private PostgreSQLDumpSession? m_session;
    private PostgreSQLSourceEntry? m_root;

    /// <summary>
    /// Creates the provider for listing its options
    /// </summary>
    public PostgreSQLSourceProvider()
    {
        m_mountPoint = string.Empty;
        m_url = string.Empty;
        m_rawOptions = [];
    }

    /// <summary>
    /// Creates the provider for a source
    /// </summary>
    /// <param name="url">The source URL</param>
    /// <param name="mountPoint">The path the database is placed at in the backup</param>
    /// <param name="options">The backup options</param>
    public PostgreSQLSourceProvider(string url, string mountPoint, Dictionary<string, string?> options)
    {
        m_url = url;
        m_mountPoint = mountPoint;
        m_rawOptions = options;
    }

    /// <inheritdoc />
    public string Key => PostgreSQLOptions.Scheme;
    /// <inheritdoc />
    public string DisplayName => Strings.PostgreSQLSourceProvider.DisplayName;
    /// <inheritdoc />
    public string Description => Strings.PostgreSQLSourceProvider.Description;
    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => PostgreSQLOptions.SupportedCommands;
    /// <inheritdoc />
    public string MountedPath => m_mountPoint;
    /// <summary>
    /// The metadata is kept in the database, so a client can see what kind of source the
    /// backup holds without reading the remote volumes
    /// </summary>
    public bool NeedsStoredMetadata => true;
    /// <inheritdoc />
    public bool NeedsCaseSensitiveFilters => true;

    /// <summary>
    /// The server a source connects to
    /// </summary>
    /// <param name="ServerVersionNum">The server version, as in 170011</param>
    /// <param name="DatabaseSize">The size of the database in bytes</param>
    public sealed record ServerInfo(int ServerVersionNum, long DatabaseSize)
    {
        /// <summary>The major version of the server</summary>
        public int Major => PostgreSQLClientTools.MajorFromServerVersionNum(ServerVersionNum);
    }

    /// <summary>
    /// Connects to the server and reads its version and the size of the database.
    /// Fails if the server version is not supported.
    /// </summary>
    /// <param name="options">The provider options</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The server information</returns>
    public static async Task<ServerInfo> GetServerInfoAsync(PostgreSQLOptions options, CancellationToken cancellationToken)
    {
        var output = (await PostgreSQLClientTools.QueryAsync(PostgreSQLClientTools.LocateAnyPsql(options), ServerInfoQuery, options, cancellationToken).ConfigureAwait(false)).Trim();
        var parts = output.Split('|');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var versionNum) || !long.TryParse(parts[1], out var size))
            throw new UserInformationException(Strings.PostgreSQLSourceProvider.ToolFailed(PostgreSQLClientTools.Psql, 0, output), "PostgreSQLToolFailed");

        var info = new ServerInfo(versionNum, size);
        if (!PostgreSQLClientTools.SupportedMajorVersions.Contains(info.Major))
            throw new UserInformationException(
                Strings.PostgreSQLSourceProvider.UnsupportedServerVersion(info.Major, string.Join(", ", PostgreSQLClientTools.SupportedMajorVersions.Order())),
                "PostgreSQLUnsupportedServerVersion");

        return info;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var options = PostgreSQLOptions.Parse(m_url, m_rawOptions);
        var server = await GetServerInfoAsync(options, cancellationToken).ConfigureAwait(false);

        // A dump is only restored by the tools of the same major version, so it is made with them
        var pgDump = PostgreSQLClientTools.LocateTool(PostgreSQLClientTools.PgDump, server.Major, options);
        await PostgreSQLClientTools.VerifyToolVersionAsync(PostgreSQLClientTools.PgDump, pgDump, server.Major, options, cancellationToken).ConfigureAwait(false);

        var tool = PostgreSQLClientTools.Start(
            PostgreSQLClientTools.PgDump,
            PostgreSQLClientTools.BuildStartInfo(pgDump, PostgreSQLClientTools.BuildDumpArguments(options, server.Major), options));

        m_session = await PostgreSQLDumpSession.StartAsync(tool, cancellationToken).ConfigureAwait(false);
        m_root = PostgreSQLSourceEntry.CreateTree(m_session, m_mountPoint, options, server, DateTime.UtcNow, Guid.NewGuid().ToString("N"));
    }

    /// <inheritdoc />
    public async Task TestAsync(CancellationToken cancellationToken)
        => await GetServerInfoAsync(PostgreSQLOptions.Parse(m_url, m_rawOptions), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield return m_root ?? throw new InvalidOperationException("The provider is not initialized");
    }

    /// <inheritdoc />
    public Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
        => Task.FromResult<ISourceProviderEntry?>(m_root?.Find(path, isFolder));

    /// <inheritdoc />
    public void Dispose()
    {
        m_session?.Dispose();
        m_session = null;
    }

    /// <summary>
    /// Builds the entry name of a table. The schema and the table name are escaped, so the
    /// separator between them is the only unescaped one and the name is valid as a file name.
    /// </summary>
    /// <param name="schema">The schema</param>
    /// <param name="table">The table name</param>
    /// <returns>The entry name</returns>
    public static string TableEntryName(string schema, string table)
        => EscapeName(schema) + SchemaSeparator + EscapeName(table);

    /// <summary>
    /// The characters that are escaped in a name, besides control characters
    /// </summary>
    private const string EscapedCharacters = "%./\\:*?\"<>|";

    /// <summary>
    /// Escapes a schema or table name as %XX for each byte of a character that is not kept
    /// </summary>
    /// <param name="name">The name</param>
    /// <returns>The escaped name</returns>
    public static string EscapeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var edgeSpace = c == ' ' && (i == 0 || i == name.Length - 1);
            if (char.IsControl(c) || edgeSpace || EscapedCharacters.Contains(c))
                foreach (var b in Encoding.UTF8.GetBytes(c.ToString()))
                    sb.Append('%').Append(b.ToString("X2"));
            else
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Reverses <see cref="EscapeName"/>
    /// </summary>
    /// <param name="name">The escaped name</param>
    /// <returns>The name</returns>
    public static string UnescapeName(string name)
        => System.Uri.UnescapeDataString(name);
}

/// <summary>
/// An entry of the PostgreSQL source: the database folder, the table of contents, the folder
/// with the tables, a table, or the large objects
/// </summary>
public sealed class PostgreSQLSourceEntry : ISequentialSourceProviderEntry
{
    private readonly PostgreSQLDumpSession m_session;
    private readonly string m_type;
    private readonly DateTime m_runStarted;
    private readonly Dictionary<string, string?> m_metadata;
    private readonly List<PostgreSQLSourceEntry> m_children = [];
    private readonly int m_dumpId;
    private readonly int m_blockSize;
    private readonly byte[]? m_content;

    private PostgreSQLSourceEntry(PostgreSQLDumpSession session, string type, string path, bool isFolder, bool isRoot, DateTime runStarted, Dictionary<string, string?> metadata, int dumpId, int blockSize, byte[]? content)
    {
        m_session = session;
        m_type = type;
        Path = path;
        IsFolder = isFolder;
        IsRootEntry = isRoot;
        m_runStarted = runStarted;
        m_metadata = metadata;
        m_metadata[PostgreSQLSourceProvider.MetadataType] = type;
        m_dumpId = dumpId;
        m_blockSize = blockSize;
        m_content = content;
    }

    /// <summary>
    /// Builds the entries of a dump from its table of contents
    /// </summary>
    /// <param name="session">The dump</param>
    /// <param name="mountPoint">The path of the database folder</param>
    /// <param name="options">The provider options</param>
    /// <param name="server">The server the dump is taken from</param>
    /// <param name="runStarted">When the dump started</param>
    /// <param name="runId">The id of the dump</param>
    /// <returns>The root entry</returns>
    public static PostgreSQLSourceEntry CreateTree(PostgreSQLDumpSession session, string mountPoint, PostgreSQLOptions options, PostgreSQLSourceProvider.ServerInfo server, DateTime runStarted, string runId)
    {
        var reader = session.Reader;
        var rootPath = Util.AppendDirSeparator(mountPoint);
        var tablesPath = Util.AppendDirSeparator(rootPath + PostgreSQLSourceProvider.TablesFolderName);

        Dictionary<string, string?> Metadata()
            => new() { [PostgreSQLSourceProvider.MetadataRunId] = runId };

        var rootMetadata = Metadata();
        rootMetadata[PostgreSQLSourceProvider.MetadataDatabase] = options.Database;
        rootMetadata[PostgreSQLSourceProvider.MetadataExtendedType] = PostgreSQLOptions.Scheme;
        var root = new PostgreSQLSourceEntry(session, PostgreSQLSourceProvider.TypeDatabase, rootPath, true, true, runStarted, rootMetadata, 0, options.BlockSize, null);

        var tocMetadata = Metadata();
        tocMetadata[PostgreSQLSourceProvider.MetadataDatabase] = options.Database;
        tocMetadata[PostgreSQLSourceProvider.MetadataServerVersion] = reader.ServerVersion;
        tocMetadata[PostgreSQLSourceProvider.MetadataClientVersion] = reader.ClientVersion;
        tocMetadata[PostgreSQLSourceProvider.MetadataArchiveVersion] = reader.ArchiveVersion;
        tocMetadata[PostgreSQLSourceProvider.MetadataDatabaseSize] = server.DatabaseSize.ToString();
        root.m_children.Add(new PostgreSQLSourceEntry(session, PostgreSQLSourceProvider.TypeTableOfContents, rootPath + PostgreSQLSourceProvider.TableOfContentsName, false, false, runStarted, tocMetadata, 0, options.BlockSize, reader.HeaderAndTableOfContents));

        var tables = new PostgreSQLSourceEntry(session, PostgreSQLSourceProvider.TypeTables, tablesPath, true, false, runStarted, Metadata(), 0, options.BlockSize, null);
        root.m_children.Add(tables);

        // The tables are listed in the order of the table of contents, which is the order of their data in the archive
        foreach (var entry in reader.Entries.Where(x => x.HasData && x.Description == PostgreSQLArchiveReader.TableDataDescription))
        {
            var schema = entry.Namespace ?? string.Empty;
            var table = entry.Tag ?? string.Empty;
            var metadata = Metadata();
            metadata[PostgreSQLSourceProvider.MetadataSchema] = schema;
            metadata[PostgreSQLSourceProvider.MetadataTable] = table;
            metadata[PostgreSQLSourceProvider.MetadataOid] = entry.Oid;
            metadata[PostgreSQLSourceProvider.MetadataDumpId] = entry.DumpId.ToString();
            metadata[PostgreSQLSourceProvider.MetadataSegmentScheme] = PostgreSQLSegmenter.SchemeName;
            metadata[PostgreSQLSourceProvider.MetadataSegmentBlockSize] = options.BlockSize.ToString();
            tables.m_children.Add(new PostgreSQLSourceEntry(session, PostgreSQLSourceProvider.TypeTable, tablesPath + PostgreSQLSourceProvider.TableEntryName(schema, table), false, false, runStarted, metadata, entry.DumpId, options.BlockSize, null));
        }

        // The large objects follow the tables in the archive
        if (reader.Entries.Any(x => x.HasData && x.Description == PostgreSQLArchiveReader.LargeObjectsDescription))
            root.m_children.Add(new PostgreSQLSourceEntry(session, PostgreSQLSourceProvider.TypeLargeObjects, rootPath + PostgreSQLSourceProvider.LargeObjectsName, false, false, runStarted, Metadata(), PostgreSQLSourceProvider.LargeObjectsId, options.BlockSize, null));

        return root;
    }

    /// <summary>
    /// Finds an entry by its path
    /// </summary>
    /// <param name="path">The path</param>
    /// <param name="isFolder">True to look for a folder</param>
    /// <returns>The entry, or null</returns>
    public PostgreSQLSourceEntry? Find(string path, bool isFolder)
    {
        var wanted = isFolder ? Util.AppendDirSeparator(path) : path;
        if (IsFolder == isFolder && string.Equals(Path, wanted, StringComparison.Ordinal))
            return this;

        return m_children.Select(x => x.Find(path, isFolder)).FirstOrDefault(x => x != null);
    }

    /// <inheritdoc />
    public bool IsFolder { get; }
    /// <inheritdoc />
    public bool IsMetaEntry => false;
    /// <inheritdoc />
    public bool IsRootEntry { get; }
    /// <inheritdoc />
    public DateTime CreatedUtc => m_runStarted;
    /// <summary>
    /// The time of the dump, so every entry is read in every backup
    /// </summary>
    public DateTime LastModificationUtc => m_runStarted;
    /// <inheritdoc />
    public string Path { get; }
    /// <summary>
    /// The size, which is only known for the table of contents; a negative size is one that is not known
    /// </summary>
    public long Size => IsFolder ? 0 : m_content?.Length ?? -1;
    /// <inheritdoc />
    public bool IsSymlink => false;
    /// <inheritdoc />
    public string? SymlinkTarget => null;
    /// <inheritdoc />
    public FileAttributes Attributes => IsFolder ? FileAttributes.Directory : FileAttributes.Normal;
    /// <inheritdoc />
    public bool IsSpecialFile => false;
    /// <inheritdoc />
    public bool IsCharacterDevice => false;
    /// <inheritdoc />
    public bool IsAlternateStream => false;
    /// <inheritdoc />
    public string? HardlinkTargetId => null;

    /// <inheritdoc />
    public Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        if (IsFolder)
            throw new InvalidOperationException("A folder cannot be read");

        // The table of contents was read when the dump started and is kept in memory
        if (m_content != null)
            return Task.FromResult<Stream>(new MemoryStream(m_content, false));

        var openFailure = m_session.TryOpen(m_dumpId)
            ? null
            : new InvalidOperationException(Strings.PostgreSQLSourceProvider.TableOpenedTwice(Path));

        return Task.FromResult<Stream>(m_type == PostgreSQLSourceProvider.TypeLargeObjects
            ? new PostgreSQLDumpStream(m_session.ReadLargeObjectsAsync, null, openFailure)
            : new PostgreSQLDumpStream((buffer, token) => m_session.ReadTableAsync(m_dumpId, Path, buffer, token), new PostgreSQLSegmenter(m_blockSize), openFailure));
    }

    /// <inheritdoc />
    public Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>(m_metadata));

    /// <inheritdoc />
    public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
        => Task.FromResult(m_children.Any(x => !x.IsFolder && string.Equals(System.IO.Path.GetFileName(x.Path), filename, StringComparison.Ordinal)));

    /// <inheritdoc />
    public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        foreach (var child in m_children)
            yield return child;
    }
}
