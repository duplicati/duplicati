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
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Snapshots;
using Duplicati.Library.Snapshots.Windows;
using Duplicati.Library.Utility;

namespace Duplicati.Library.SourceProvider.Builtin.MSSQL
{
    /// <summary>
    /// A source provider that exposes Microsoft SQL Server databases as a virtual
    /// folder hierarchy rooted at <c>%MSSQL%</c>.
    /// <para>
    /// The hierarchy is:
    /// <c>%MSSQL%\</c> → <c>%MSSQL%\&lt;server&gt;\</c> → <c>%MSSQL%\&lt;server&gt;\&lt;instance&gt;\</c>
    /// → <c>%MSSQL%\&lt;server&gt;\&lt;instance&gt;\&lt;database&gt;\</c> → the database's files,
    /// read through the snapshot service.
    /// Databases on the default (unnamed) instance are placed directly under the server folder.
    /// </para>
    /// <para>
    /// Each level carries metadata (<c>mssql:Name</c>, <c>mssql:Type</c>, etc.) so the
    /// user interface can display a proper grouping of server ⇒ instance ⇒ database ⇒ files.
    /// </para>
    /// </summary>
    public class MSSQLSourceProvider : ISourceProviderModule, IPrefixedSourceProviderModule, ISnapshotAwareModule
    {
        /// <summary>
        /// The log tag for this class
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<MSSQLSourceProvider>();

        /// <summary>
        /// The path prefix that identifies MSSQL sources
        /// </summary>
        public const string MSSQL_PATH_PREFIX = @"%MSSQL%";

        /// <summary>
        /// The module key
        /// </summary>
        public const string MODULE_KEY = "mssql";

        /// <summary>
        /// The options used to create this provider
        /// </summary>
        private readonly Dictionary<string, string?> _options;

        /// <summary>
        /// The source paths that requested MSSQL content
        /// (e.g. <c>%MSSQL%</c>, <c>%MSSQL%\server</c>, <c>%MSSQL%\server\instance\database</c>)
        /// </summary>
        private readonly IReadOnlyList<string> _requestedSources;

        /// <summary>
        /// The snapshot service used to read the underlying files
        /// </summary>
        private ISnapshotService? _snapshotService;

        /// <summary>
        /// Lazily queried list of databases selected for backup
        /// </summary>
        private readonly Lazy<List<MSSQLDB>> _databases;

        /// <summary>
        /// A target database parsed from a source path
        /// </summary>
        private sealed record TargetDb
        {
            /// <summary>
            /// The original path
            /// </summary>
            public required string Path { get; init; }
            /// <summary>
            /// The database name
            /// </summary>
            public required string Database { get; init; }
            /// <summary>
            /// The server name
            /// </summary>
            public required string Server { get; init; }
            /// <summary>
            /// The instance id
            /// </summary>
            public required string InstanceId { get; init; }
        }

        /// <summary>
        /// Creates a new instance for metadata/module loading only
        /// </summary>
        public MSSQLSourceProvider()
        {
            _options = new Dictionary<string, string?>();
            _requestedSources = [];
            _databases = new Lazy<List<MSSQLDB>>(() => []);
        }

        /// <summary>
        /// Creates a new instance for the given set of requested source paths
        /// </summary>
        /// <param name="requestedSources">The source paths that requested MSSQL content</param>
        /// <param name="options">The commandline options</param>
        public MSSQLSourceProvider(IEnumerable<string> requestedSources, IReadOnlyDictionary<string, string?> options)
        {
            _requestedSources = requestedSources.ToList();
            _options = new Dictionary<string, string?>(options, StringComparer.OrdinalIgnoreCase);
            _databases = new Lazy<List<MSSQLDB>>(QueryDatabases);
        }

        /// <inheritdoc />
        public string Key => MODULE_KEY;

        /// <inheritdoc />
        public string DisplayName => "Microsoft SQL Server databases";

        /// <inheritdoc />
        public string Description => "Exposes Microsoft SQL Server databases as a virtual folder structure for backup";

        /// <inheritdoc />
        public IList<ICommandLineArgument> SupportedCommands => [];

        /// <inheritdoc />
        public string MountedPath => Util.AppendDirSeparator(MSSQL_PATH_PREFIX);

        /// <inheritdoc />
        public bool NeedsStoredMetadata => true;

        /// <summary>
        /// Checks whether the given source path is a MSSQL source path
        /// </summary>
        /// <param name="source">The source path to check</param>
        /// <returns>True if the path is a MSSQL source path</returns>
        public static bool IsMSSQLSource(string source)
            => !string.IsNullOrWhiteSpace(source)
                && (source.Equals(MSSQL_PATH_PREFIX, StringComparison.OrdinalIgnoreCase)
                    || source.StartsWith(MSSQL_PATH_PREFIX + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        /// <inheritdoc />
        public bool MatchesSource(string source)
            => IsMSSQLSource(source);

        /// <inheritdoc />
        public bool IsSupported => OperatingSystem.IsWindows();

        /// <inheritdoc />
        public void PrepareOptions(IReadOnlyList<string> sources, IDictionary<string, string?> options)
        {
            if (!OperatingSystem.IsWindows())
                return;

            PrepareOptionsWindows(options);
        }

        /// <summary>
        /// Applies the required option changes (Windows-only implementation)
        /// </summary>
        /// <param name="options">The commandline options, which may be modified</param>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void PrepareOptionsWindows(IDictionary<string, string?> options)
        {
            PrepareOptions(options, new MSSQLUtility());
        }

        /// <summary>
        /// Applies the required option changes:
        /// forces snapshot-policy to "required", removes the MSSQL VSS writer from
        /// the excluded writers, and switches the snapshot provider away from Wmi.
        /// </summary>
        /// <param name="options">The commandline options, which may be modified</param>
        /// <param name="mssqlUtility">The MSSQL utility instance</param>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        internal void PrepareOptions(IDictionary<string, string?> options, IMSSQLUtility mssqlUtility)
        {
            if (!mssqlUtility.IsMSSQLInstalled)
                return;

            if (options.TryGetValue("vss-exclude-writers", out var excludedWritersOption) && !string.IsNullOrWhiteSpace(excludedWritersOption))
            {
                var excludedWriters = excludedWritersOption.Split(';')
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => new Guid(x.Trim()))
                    .ToArray();

                if (excludedWriters.Contains(mssqlUtility.MSSQLWriterGuid))
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "CannotExcludeMsSqlVSSWriter", null, "Excluded writers for VSS cannot contain MS SQL writer when backuping Microsoft SQL Server databases. Removing \"{0}\" to continue", mssqlUtility.MSSQLWriterGuid.ToString());
                    options["vss-exclude-writers"] = string.Join(";", excludedWriters.Where(x => x != mssqlUtility.MSSQLWriterGuid));
                }
            }

            if (!options.TryGetValue("snapshot-policy", out var snapshotPolicy) || !"required".Equals(snapshotPolicy, StringComparison.OrdinalIgnoreCase))
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "MustSetSnapshotPolicy", null, "Snapshot policy have to be set to \"required\" when backuping Microsoft SQL Server databases. Changing to \"required\" to continue");
                options["snapshot-policy"] = "required";
            }

            var providerName = options.TryGetValue("snapshot-provider", out var sp) ? sp : null;
            var provider = string.IsNullOrWhiteSpace(providerName)
                ? WindowsSnapshot.DEFAULT_WINDOWS_SNAPSHOT_QUERY_PROVIDER
                : Library.Utility.Utility.ParseEnum(providerName, WindowsSnapshot.DEFAULT_WINDOWS_SNAPSHOT_QUERY_PROVIDER);
            if (provider == WindowsSnapshotProvider.Wmi)
            {
                provider = WindowsSnapshotProvider.Native;
                options["snapshot-provider"] = provider.ToString();
                Logging.Log.WriteWarningMessage(LOGTAG, "WmiNotSupportedForMSSQL", null, $"The {WindowsSnapshotProvider.Wmi} cannot be used for MSSQL backups, switching to {provider}");
            }
        }

        /// <inheritdoc />
        public ISourceProviderModule CreateForSources(IReadOnlyList<string> sources, IReadOnlyDictionary<string, string?> options)
            => new MSSQLSourceProvider(sources, options);

        /// <summary>
        /// Parses a source path into a target database descriptor
        /// </summary>
        /// <param name="path">The source path</param>
        /// <returns>The parsed target, or null if the path is not a MSSQL source path</returns>
        private static TargetDb? ParsePathEntry(string path)
        {
            if (!IsMSSQLSource(path))
                return null;

            var parts = path.Split(['\\'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || !parts[0].Equals(MSSQL_PATH_PREFIX, StringComparison.OrdinalIgnoreCase))
                return null;

            return parts.Length switch
            {
                // Match all MSSQL databases
                1 => new TargetDb() { Path = path, Server = "", InstanceId = "", Database = "" },
                // Match a server
                2 => new TargetDb() { Path = path, Server = parts[1], InstanceId = "", Database = "" },
                // Match a server instance, or database on default server instance
                3 => new TargetDb() { Path = path, Server = parts[1], InstanceId = parts[2], Database = "" },
                // Match a database on a server instance
                4 => new TargetDb() { Path = path, Server = parts[1], InstanceId = parts[2], Database = parts[3] },
                _ => null
            };
        }

        /// <summary>
        /// Queries the MSSQL databases that match the requested source paths
        /// </summary>
        /// <returns>The list of databases selected for backup</returns>
        private List<MSSQLDB> QueryDatabases()
        {
            if (!OperatingSystem.IsWindows())
                return [];

            return QueryDatabasesWindows();
        }

        /// <summary>
        /// Queries the MSSQL databases that match the requested source paths (Windows-only implementation)
        /// </summary>
        /// <returns>The list of databases selected for backup</returns>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private List<MSSQLDB> QueryDatabasesWindows()
        {
            var mssqlUtility = new MSSQLUtility();
            if (!mssqlUtility.IsMSSQLInstalled)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "MSSQLNotInstalled", null, "Microsoft SQL Server is not installed, no databases will be backed up");
                return [];
            }

            var provider = Library.Utility.Utility.ParseEnumOption(_options, "snapshot-provider", WindowsSnapshot.DEFAULT_WINDOWS_SNAPSHOT_QUERY_PROVIDER);
            mssqlUtility.QueryDBsInfo(provider);

            return SelectDatabases(mssqlUtility, _requestedSources);
        }

        /// <summary>
        /// Selects the databases that match the requested source paths from the available databases
        /// </summary>
        /// <param name="mssqlUtility">The MSSQL utility with the queried databases</param>
        /// <param name="requestedSources">The source paths that requested MSSQL content</param>
        /// <returns>The list of databases selected for backup</returns>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        internal static List<MSSQLDB> SelectDatabases(IMSSQLUtility mssqlUtility, IEnumerable<string> requestedSources)
        {
            Logging.Log.WriteInformationMessage(LOGTAG, "MsSqlDatabaseCount", "Found {0} databases on Microsoft SQL Server", mssqlUtility.DBs.Count);
            foreach (var db in mssqlUtility.DBs)
                Logging.Log.WriteProfilingMessage(LOGTAG, "MsSqlDatabaseName", "Found DB name {0}, Server {1}, Instance {2}, files {3}", db.Database, db.Server, db.InstanceId, string.Join(";", db.DataPaths));

            var includedDbs = requestedSources
                .Select(ParsePathEntry)
                .WhereNotNull()
                .ToList();

            // Catch-all: no specific targets means all databases
            if (includedDbs.Any(x => string.IsNullOrWhiteSpace(x.Server)))
                return mssqlUtility.DBs.ToList();

            var serverInstanceMap = mssqlUtility.DBs
                .GroupBy(x => x.ServerInstanceId)
                .ToDictionary(x => x.Key, x => x.GroupBy(y => y.Database).ToDictionary(y => y.Key, y => y.ToList(), StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

            var serverMap = mssqlUtility.DBs
                .GroupBy(x => x.Server)
                .ToDictionary(x => x.Key, x => x.GroupBy(y => y.Database).ToDictionary(y => y.Key, y => y.ToList(), StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

            var dbsForBackup = new List<MSSQLDB>();
            foreach (var db in includedDbs)
            {
                if (!serverMap.TryGetValue(db.Server, out var serverDbs))
                    throw new UserInformationException($"Server name specified in path as \"{db.Path}\" cannot be found", "MsSqlServerNotFound");

                // No instance id, so grab everything from that server
                if (string.IsNullOrWhiteSpace(db.InstanceId))
                {
                    dbsForBackup.AddRange(serverDbs.SelectMany(x => x.Value));
                    continue;
                }

                // Fully qualified name server\instance\database
                if (!string.IsNullOrWhiteSpace(db.Database))
                {
                    if (!serverInstanceMap.TryGetValue($"{db.Server}\\{db.InstanceId}", out var mappedServerInstance))
                        throw new UserInformationException($"Server instance id specified in path as \"{db.Path}\" cannot be found", "MsSqlServerInstanceNotFound");

                    if (!mappedServerInstance.TryGetValue(db.Database, out var mappedList))
                        throw new UserInformationException($"Database name specified in path as \"{db.Path}\" cannot be found", "MsSqlDatabaseNotFound");

                    dbsForBackup.AddRange(mappedList);
                    continue;
                }

                // At this point we have a server name and one more identifier.
                // It could be a database name or an instance id.
                var matchesInstance = serverInstanceMap.TryGetValue($"{db.Server}\\{db.InstanceId}", out var serverInstanceDbs);
                var matchesDb = serverDbs.TryGetValue(db.InstanceId, out var dbList);

                if (matchesInstance && matchesDb)
                    throw new UserInformationException($"Server instance id specified in path as \"{db.Path}\" is ambiguous", "MsSqlServerInstanceAmbiguous");

                if (matchesInstance)
                    dbsForBackup.AddRange(serverInstanceDbs!.SelectMany(x => x.Value));
                else if (matchesDb)
                    dbsForBackup.AddRange(dbList!);
                else
                    throw new UserInformationException($"Server instance id specified in path as \"{db.Path}\" cannot be found", "MsSqlServerInstanceNotFound");
            }

            // Merge duplicates that we may have picked up
            return dbsForBackup
                .GroupBy(x => (x.Server, x.InstanceId, x.Database), x => x.DataPaths)
                .Select(x => new MSSQLDB
                {
                    Server = x.Key.Server,
                    InstanceId = x.Key.InstanceId,
                    Database = x.Key.Database,
                    DataPaths = x.SelectMany(y => y).Distinct(Library.Utility.Utility.ClientFilenameStringComparer).ToList()
                })
                .ToList();
        }

        /// <inheritdoc />
        public Task<IEnumerable<string>> GetSnapshotPathsAsync(CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows())
                return Task.FromResult(Enumerable.Empty<string>());

            // Include all data paths of the selected databases in the snapshot
            var paths = _databases.Value
                .SelectMany(x => x.DataPaths)
                .Distinct(Library.Utility.Utility.ClientFilenameStringComparer)
                .ToList();

            return Task.FromResult<IEnumerable<string>>(paths);
        }

        /// <inheritdoc />
        public void SetSnapshotService(ISnapshotService? snapshotService)
        {
            _snapshotService = snapshotService;
        }

        /// <inheritdoc />
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            if (OperatingSystem.IsWindows())
                InitializeWindows();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Performs the Windows-specific initialization, which forces the query to run
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void InitializeWindows()
        {
            // Force evaluation so missing databases are reported before the backup starts
            _ = _databases.Value;
        }

        /// <inheritdoc />
        public Task TestAsync(CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows())
                throw new UserInformationException("Microsoft SQL Server backup works only on Windows OS", "MsSqlWindowsOnly");

            TestWindows();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Performs the Windows-specific test, verifying that MSSQL is installed
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void TestWindows()
        {
            var mssqlUtility = new MSSQLUtility();
            if (!mssqlUtility.IsMSSQLInstalled)
                throw new UserInformationException("Microsoft SQL Server is not installed", "MsSqlNotInstalled");
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows())
                yield break;

            // The snapshot service may be null when browsing (e.g. from the filesystem plugin);
            // the virtual hierarchy levels above the actual files do not need it
            yield return new MSSQLRootEntry(MountedPath, _databases.Value, _snapshotService);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows() || !IsMSSQLSource(path))
                return null;

            var root = new MSSQLRootEntry(MountedPath, _databases.Value, _snapshotService);
            var targetPath = Util.AppendDirSeparator(path.TrimEnd(Path.DirectorySeparatorChar));

            // Root itself
            if (string.Equals(targetPath, root.Path, StringComparison.OrdinalIgnoreCase))
                return root;

            // Walk down the virtual tree
            ISourceProviderEntry current = root;
            var relative = targetPath.Substring(root.Path.Length);
            var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < segments.Length; i++)
            {
                var isLast = i == segments.Length - 1;
                var found = false;
                await foreach (var entry in current.Enumerate(cancellationToken).ConfigureAwait(false))
                {
                    var name = entry.Path.TrimEnd(Path.DirectorySeparatorChar)
                        .Split(Path.DirectorySeparatorChar)
                        .Last();

                    if (!name.Equals(segments[i], StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (isLast && entry.IsFolder != isFolder)
                        return null;

                    current = entry;
                    found = true;
                    break;
                }

                if (!found)
                    return null;
            }

            return current;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // The snapshot service is shared with other providers and disposed by the caller
        }
    }
}
