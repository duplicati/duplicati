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
using Duplicati.Library.Snapshots.Windows;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Duplicati.UnitTest")]

namespace Duplicati.Library.SourceProvider.Builtin.HyperV
{
    /// <summary>
    /// A source provider that exposes Hyper-V virtual machines as a virtual
    /// folder hierarchy rooted at <c>%HYPERV%</c>.
    /// <para>
    /// The hierarchy is:
    /// <c>%HYPERV%\</c> → <c>%HYPERV%\&lt;vm-guid&gt;\</c> → the VM's files and folders
    /// (configuration files, virtual hard disks, snapshots), read through the snapshot service.
    /// </para>
    /// <para>
    /// The virtual VM folder carries metadata (<c>hyperv:Name</c>) with the friendly
    /// name of the virtual machine so the user interface can display the name instead
    /// of the raw GUID.
    /// </para>
    /// </summary>
    public class HyperVSourceProvider : ISourceProviderModule, IPrefixedSourceProviderModule, ISnapshotAwareModule
    {
        /// <summary>
        /// The log tag for this class
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<HyperVSourceProvider>();

        /// <summary>
        /// The path prefix that identifies Hyper-V sources
        /// </summary>
        public const string HYPERV_PATH_PREFIX = @"%HYPERV%";

        /// <summary>
        /// The metadata key prefix used by this provider
        /// </summary>
        public const string METADATA_PREFIX = "hyperv:";

        /// <summary>
        /// The module key
        /// </summary>
        public const string MODULE_KEY = "hyperv";

        /// <summary>
        /// The options used to create this provider
        /// </summary>
        private readonly Dictionary<string, string?> _options;

        /// <summary>
        /// The source paths that requested Hyper-V content (e.g. <c>%HYPERV%</c> or <c>%HYPERV%\\&lt;guid&gt;</c>)
        /// </summary>
        private readonly IReadOnlyList<string> _requestedSources;

        /// <summary>
        /// The snapshot service used to read the underlying files
        /// </summary>
        private ISnapshotService? _snapshotService;

        /// <summary>
        /// Lazily queried list of guests selected for backup
        /// </summary>
        private readonly Lazy<List<HyperVGuest>> _guests;

        /// <summary>
        /// Creates a new instance for metadata/module loading only
        /// </summary>
        public HyperVSourceProvider()
        {
            _options = new Dictionary<string, string?>();
            _requestedSources = [];
            _guests = new Lazy<List<HyperVGuest>>(() => []);
        }

        /// <summary>
        /// Creates a new instance for the given set of requested source paths
        /// </summary>
        /// <param name="requestedSources">The source paths that requested Hyper-V content</param>
        /// <param name="options">The commandline options</param>
        public HyperVSourceProvider(IEnumerable<string> requestedSources, IReadOnlyDictionary<string, string?> options)
        {
            _requestedSources = requestedSources.ToList();
            _options = new Dictionary<string, string?>(options, StringComparer.OrdinalIgnoreCase);
            _guests = new Lazy<List<HyperVGuest>>(QueryGuests);
        }

        /// <inheritdoc />
        public string Key => MODULE_KEY;

        /// <inheritdoc />
        public string DisplayName => "Hyper-V virtual machines";

        /// <inheritdoc />
        public string Description => "Exposes Hyper-V virtual machines as a virtual folder structure for backup";

        /// <inheritdoc />
        public IList<ICommandLineArgument> SupportedCommands => [];

        /// <inheritdoc />
        public string MountedPath => Util.AppendDirSeparator(HYPERV_PATH_PREFIX);

        /// <inheritdoc />
        public bool NeedsStoredMetadata => true;

        /// <summary>
        /// Checks whether the given source path is a Hyper-V source path
        /// </summary>
        /// <param name="source">The source path to check</param>
        /// <returns>True if the path is a Hyper-V source path</returns>
        public static bool IsHyperVSource(string source)
            => !string.IsNullOrWhiteSpace(source)
                && (source.Equals(HYPERV_PATH_PREFIX, StringComparison.OrdinalIgnoreCase)
                    || source.StartsWith(HYPERV_PATH_PREFIX + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        /// <inheritdoc />
        public bool MatchesSource(string source)
            => IsHyperVSource(source);

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
            using var hypervUtility = new Snapshots.Windows.HyperVUtility();
            PrepareOptions(options, hypervUtility);
        }

        /// <summary>
        /// Applies the required option changes:
        /// forces snapshot-policy to "required", removes the Hyper-V VSS writer from
        /// the excluded writers, and switches the snapshot provider away from Wmi.
        /// </summary>
        /// <param name="options">The commandline options, which may be modified</param>
        /// <param name="hypervUtility">The Hyper-V utility instance</param>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        internal void PrepareOptions(IDictionary<string, string?> options, IHyperVUtility hypervUtility)
        {
            if (!hypervUtility.IsHyperVInstalled)
                return;

            if (options.TryGetValue("vss-exclude-writers", out var excludedWritersOption) && !string.IsNullOrWhiteSpace(excludedWritersOption))
            {
                var excludedWriters = excludedWritersOption.Split(';')
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => new Guid(x.Trim()))
                    .ToArray();

                if (excludedWriters.Contains(hypervUtility.HyperVWriterGuid))
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "CannotExcludeHyperVVSSWriter", null, "Excluded writers for VSS cannot contain Hyper-V writer when backuping Hyper-V virtual machines. Removing \"{0}\" to continue", hypervUtility.HyperVWriterGuid.ToString());
                    options["vss-exclude-writers"] = string.Join(";", excludedWriters.Where(x => x != hypervUtility.HyperVWriterGuid));
                }
            }

            if (!options.TryGetValue("snapshot-policy", out var snapshotPolicy) || !"required".Equals(snapshotPolicy, StringComparison.OrdinalIgnoreCase))
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "MustSetSnapshotPolicy", null, "Snapshot policy have to be set to \"required\" when backuping Hyper-V virtual machines. Changing to \"required\" to continue");
                options["snapshot-policy"] = "required";
            }

            if (!hypervUtility.IsVSSWriterSupported)
                Logging.Log.WriteWarningMessage(LOGTAG, "HyperVOnServerOnly", null, "This is client version of Windows. Hyper-V VSS writer is present only on Server version. Backup will continue, but will be crash consistent only in opposite to application consistent in Server version");

            var providerName = options.TryGetValue("snapshot-provider", out var sp) ? sp : null;
            var provider = string.IsNullOrWhiteSpace(providerName)
                ? Snapshots.WindowsSnapshot.DEFAULT_WINDOWS_SNAPSHOT_QUERY_PROVIDER
                : Library.Utility.Utility.ParseEnum(providerName, Snapshots.WindowsSnapshot.DEFAULT_WINDOWS_SNAPSHOT_QUERY_PROVIDER);
            if (provider == Snapshots.WindowsSnapshotProvider.Wmi)
            {
                provider = Snapshots.WindowsSnapshotProvider.Native;
                options["snapshot-provider"] = provider.ToString();
                Logging.Log.WriteWarningMessage(LOGTAG, "WmiNotSupportedForHyperV", null, $"The {Snapshots.WindowsSnapshotProvider.Wmi} cannot be used for HyperV backups, switching to {provider}");
            }
        }

        /// <inheritdoc />
        public ISourceProviderModule CreateForSources(IReadOnlyList<string> sources, IReadOnlyDictionary<string, string?> options)
            => new HyperVSourceProvider(sources, options);

        /// <summary>
        /// Queries the Hyper-V guests that match the requested source paths
        /// </summary>
        /// <returns>The list of guests selected for backup</returns>
        private List<HyperVGuest> QueryGuests()
        {
            if (!OperatingSystem.IsWindows())
                return [];

            return QueryGuestsWindows();
        }

        /// <summary>
        /// Queries the Hyper-V guests that match the requested source paths (Windows-only implementation)
        /// </summary>
        /// <returns>The list of guests selected for backup</returns>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private List<HyperVGuest> QueryGuestsWindows()
        {
            using var hypervUtility = new Snapshots.Windows.HyperVUtility();
            if (!hypervUtility.IsHyperVInstalled)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "HyperVNotInstalled", null, "Hyper-V is not installed, no virtual machines will be backed up");
                return [];
            }

            var provider = Library.Utility.Utility.ParseEnumOption(_options, "snapshot-provider", Snapshots.WindowsSnapshot.DEFAULT_WINDOWS_SNAPSHOT_QUERY_PROVIDER);
            hypervUtility.QueryHyperVGuestsInfo(provider, true);

            return SelectGuests(hypervUtility, _requestedSources);
        }

        /// <summary>
        /// Selects the guests that match the requested source paths from the available guests
        /// </summary>
        /// <param name="hypervUtility">The Hyper-V utility with the queried guests</param>
        /// <param name="requestedSources">The source paths that requested Hyper-V content</param>
        /// <returns>The list of guests selected for backup</returns>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        internal static List<HyperVGuest> SelectGuests(IHyperVUtility hypervUtility, IEnumerable<string> requestedSources)
        {
            var guests = hypervUtility.Guests ?? [];

            Logging.Log.WriteInformationMessage(LOGTAG, "HyperVMachineCount", "Found {0} virtual machines on Hyper-V", guests.Count);
            foreach (var guest in guests)
                Logging.Log.WriteProfilingMessage(LOGTAG, "FoundHyperVMachine", "Found VM name {0}, ID {1}, files {2}", guest.Name, guest.ID, string.Join(";", guest.DataPaths ?? []));

            // No filters requested, include all guests
            if (requestedSources.Any(x => x.Equals(HYPERV_PATH_PREFIX, StringComparison.OrdinalIgnoreCase)))
                return guests.ToList();

            // Pick only the requested guests, with optional subpath restrictions
            var requested = requestedSources
                .Where(IsHyperVSource)
                .Select(x => x.Substring(HYPERV_PATH_PREFIX.Length).Trim(Path.DirectorySeparatorChar))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x =>
                {
                    var parts = x.Split(Path.DirectorySeparatorChar, 2);
                    return (Id: parts[0], SubPath: parts.Length > 1 ? parts[1] : null);
                })
                .ToList();

            var result = new Dictionary<Guid, HyperVGuest>();
            var subPaths = new Dictionary<Guid, List<string>>();

            foreach (var (id, subPath) in requested)
            {
                if (!Guid.TryParse(id, out var guid))
                    throw new UserInformationException($"The Hyper-V guest id \"{id}\" is not a valid GUID", "HyperVGuestIdInvalid");

                var found = guests.Where(x => x.ID == guid).ToList();
                if (found.Count != 1)
                    throw new UserInformationException($"Hyper-V guest specified in source with ID {id} cannot be found", "HyperVGuestNotFound");

                if (string.IsNullOrWhiteSpace(subPath))
                {
                    // Full guest selected, clear any subpath restrictions
                    result[guid] = found[0];
                    subPaths.Remove(guid);
                }
                else if (!result.ContainsKey(guid))
                {
                    // Subpath restriction for this guest
                    if (!subPaths.TryGetValue(guid, out var list))
                        subPaths[guid] = list = [];
                    list.Add(subPath);
                }
            }

            // Create restricted copies for guests with subpaths
            foreach (var (guid, paths) in subPaths)
            {
                var guest = guests.First(x => x.ID == guid);
                result[guid] = new HyperVGuest(guest.Name, guest.ID, paths.Distinct(Library.Utility.Utility.ClientFilenameStringComparer).ToList());
            }

            return result.Values.ToList();
        }

        /// <inheritdoc />
        public Task<IEnumerable<string>> GetSnapshotPathsAsync(CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows())
                return Task.FromResult(Enumerable.Empty<string>());

            // Include all data paths of the selected guests in the snapshot
            var paths = _guests.Value
                .SelectMany(x => x.DataPaths ?? [])
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
            // Force evaluation so missing guests are reported before the backup starts
            _ = _guests.Value;
        }

        /// <inheritdoc />
        public Task TestAsync(CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows())
                throw new UserInformationException("Hyper-V backup works only on Windows OS", "HyperVWindowsOnly");

            TestWindows();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Performs the Windows-specific test, verifying that Hyper-V is installed
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void TestWindows()
        {
            using var hypervUtility = new Snapshots.Windows.HyperVUtility();
            if (!hypervUtility.IsHyperVInstalled)
                throw new UserInformationException("Hyper-V is not installed", "HyperVNotInstalled");
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows())
                yield break;

            // The snapshot service may be null when browsing (e.g. from the filesystem plugin);
            // the virtual hierarchy levels above the actual files do not need it
            yield return new HyperVRootEntry(MountedPath, _guests.Value, _snapshotService);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows() || !IsHyperVSource(path))
                return null;

            var root = new HyperVRootEntry(MountedPath, _guests.Value, _snapshotService);
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
