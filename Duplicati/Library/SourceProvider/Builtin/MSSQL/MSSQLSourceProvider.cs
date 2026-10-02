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
    /// folder hierarchy. The databases are selected with sources that start with
    /// <c>%MSSQL%</c>, and the backed up entries are placed below <c>\\duplicati\mssql\</c>.
    /// <para>
    /// The hierarchy is:
    /// <c>\\duplicati\mssql\</c> → <c>&lt;server&gt;\</c> → <c>&lt;instance&gt;\</c>
    /// → <c>&lt;database&gt;\</c> → the database's files, read through the snapshot service
    /// and placed directly in the database folder under their own names
    /// (<c>\\duplicati\mssql\&lt;server&gt;\&lt;instance&gt;\&lt;database&gt;\db.mdf</c>);
    /// names that clash are numbered (<c>data-1.ndf</c>, <c>data-2.ndf</c>).
    /// The default (unnamed) instance is named <see cref="DEFAULT_INSTANCE_NAME"/>.
    /// Each of the file entries records the local path in the <c>mssql:orig-path</c> metadata.
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
        /// The separator in a MSSQL source path, such as <c>%MSSQL%\&lt;server&gt;\&lt;database&gt;</c>.
        /// The source syntax is Windows-style and is parsed the same way on every
        /// platform, so a non-Windows machine recognizes the source and can report
        /// that it is not supported instead of treating it as a relative file path.
        /// The entries the provider produces use the platform separator, as they
        /// are only produced on Windows.
        /// </summary>
        public const char SOURCE_PATH_SEPARATOR = '\\';

        /// <summary>
        /// The module key
        /// </summary>
        public const string MODULE_KEY = "mssql";

        /// <summary>
        /// The metadata key prefix used by this provider
        /// </summary>
        public const string METADATA_PREFIX = "mssql:";

        /// <summary>
        /// The version of the metadata written to the entries
        /// </summary>
        public const string METADATA_VERSION = "1";

        /// <summary>
        /// The share name of the virtual root the entries are placed in
        /// </summary>
        public const string VIRTUAL_SHARE = "mssql";

        /// <summary>
        /// The name SQL Server uses for the default (unnamed) instance
        /// </summary>
        public const string DEFAULT_INSTANCE_NAME = "MSSQLSERVER";

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
        public string MountedPath => VirtualSourcePath.GetMountedPath(VIRTUAL_SHARE);

        /// <inheritdoc />
        public string SourcePrefix => MSSQL_PATH_PREFIX;

        /// <summary>
        /// Gets the name of the instance a database is on, using
        /// <see cref="DEFAULT_INSTANCE_NAME"/> for the default instance
        /// </summary>
        /// <param name="db">The database</param>
        /// <returns>The instance name</returns>
        internal static string GetInstanceName(MSSQLDB db)
            => string.IsNullOrWhiteSpace(db.InstanceId) ? DEFAULT_INSTANCE_NAME : db.InstanceId;

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
                    || source.StartsWith(MSSQL_PATH_PREFIX + SOURCE_PATH_SEPARATOR, StringComparison.OrdinalIgnoreCase));

        /// <inheritdoc />
        public bool MatchesSource(string source)
            => IsMSSQLSource(source);

        /// <inheritdoc />
        /// <remarks>
        /// The paths follow the source syntax: <c>%MSSQL%\&lt;server&gt;</c>,
        /// <c>%MSSQL%\&lt;server&gt;\&lt;instance&gt;\&lt;database&gt;</c>, and
        /// <c>%MSSQL%\&lt;server&gt;\&lt;x&gt;</c>, where <c>x</c> is an instance when the path
        /// ends with a separator or is <see cref="DEFAULT_INSTANCE_NAME"/>, and otherwise a
        /// database on the default instance, as earlier versions wrote it.
        /// </remarks>
        public string? TranslateSourcePath(string sourcePath)
        {
            if (!IsMSSQLSource(sourcePath))
                return null;

            var ds = Path.DirectorySeparatorChar;
            var parts = sourcePath.Substring(MSSQL_PATH_PREFIX.Length).Split(SOURCE_PATH_SEPARATOR, StringSplitOptions.RemoveEmptyEntries);
            var isFolder = sourcePath.EndsWith(SOURCE_PATH_SEPARATOR);

            return parts.Length switch
            {
                0 => MountedPath,
                1 => MountedPath + parts[0] + ds,
                2 when isFolder || parts[1].Equals(DEFAULT_INSTANCE_NAME, StringComparison.OrdinalIgnoreCase)
                    => MountedPath + parts[0] + ds + parts[1] + ds,
                2 => MountedPath + parts[0] + ds + DEFAULT_INSTANCE_NAME + ds + parts[1] + ds,
                3 => MountedPath + parts[0] + ds + parts[1] + ds + parts[2] + ds,
                _ => null
            };
        }

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

            var parts = path.Split(SOURCE_PATH_SEPARATOR, StringSplitOptions.RemoveEmptyEntries);
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
            var providerId = Library.Utility.Utility.ParseGuidOption(_options, "vss-provider-id", Guid.Empty);
            mssqlUtility.QueryDBsInfo(provider, providerId);

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

            // The default instance is keyed by its name, so it can be selected like a named instance
            var serverInstanceMap = mssqlUtility.DBs
                .GroupBy(x => $"{x.Server}\\{GetInstanceName(x)}", StringComparer.OrdinalIgnoreCase)
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
        public Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows() || !Util.AppendDirSeparator(path).StartsWith(MountedPath, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<ISourceProviderEntry?>(null);

            return VirtualSourcePath.FindEntryAsync(new MSSQLRootEntry(MountedPath, _databases.Value, _snapshotService), path, isFolder, cancellationToken);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // The snapshot service is shared with other providers and is released by
            // the file source, or by the wrapper the source provider factory puts around
            // the first snapshot-aware provider when there is no file source
        }
    }
}
