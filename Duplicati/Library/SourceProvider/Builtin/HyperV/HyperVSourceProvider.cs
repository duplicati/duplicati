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
using Duplicati.Library.Utility;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Duplicati.UnitTest")]

namespace Duplicati.Library.SourceProvider.Builtin.HyperV
{
    /// <summary>
    /// A source provider that exposes Hyper-V virtual machines as a virtual
    /// folder hierarchy. The machines are selected with sources that start with
    /// <c>%HYPERV%</c>, and the backed up entries are placed below <c>\\duplicati\hyperv\</c>.
    /// <para>
    /// The hierarchy is:
    /// <c>\\duplicati\hyperv\</c> → <c>\\duplicati\hyperv\&lt;vm-guid&gt;\</c> → the VM's files and folders
    /// (configuration files, virtual hard disks, snapshots), read through the snapshot service
    /// and placed directly in the VM folder under their own names
    /// (<c>\\duplicati\hyperv\&lt;vm-guid&gt;\disk.vhdx</c>); names that clash are numbered
    /// (<c>disk-1.vhdx</c>, <c>disk-2.vhdx</c>).
    /// Each of these entries records the local path in the <c>hyperv:orig-path</c> metadata.
    /// </para>
    /// <para>
    /// The virtual VM folder carries metadata (<c>hyperv:Name</c>) with the friendly
    /// name of the virtual machine so the user interface can display the name instead
    /// of the raw GUID.
    /// </para>
    /// </summary>
    public class HyperVSourceProvider : ISourceProviderModule, IPrefixedSourceProviderModule, ISnapshotAwareModule, IRestoredItemRegistrationModule
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
        /// The share name of the virtual root the entries are placed in
        /// </summary>
        public const string VIRTUAL_SHARE = "hyperv";

        /// <summary>
        /// The version of the metadata written to the entries
        /// </summary>
        public const string METADATA_VERSION = "1";

        /// <summary>
        /// The separator in a Hyper-V source path, such as <c>%HYPERV%\&lt;guid&gt;</c>.
        /// The source syntax is Windows-style and is parsed the same way on every
        /// platform, so a non-Windows machine recognizes the source and can report
        /// that it is not supported instead of treating it as a relative file path.
        /// The entries the provider produces use the platform separator, as they
        /// are only produced on Windows.
        /// </summary>
        public const char SOURCE_PATH_SEPARATOR = '\\';

        /// <summary>
        /// The metadata key prefix used by this provider
        /// </summary>
        public const string METADATA_PREFIX = "hyperv:";

        /// <summary>
        /// The module key
        /// </summary>
        public const string MODULE_KEY = "hyperv";

        /// <summary>
        /// The option that suppresses the warning about running on a client version of Windows
        /// </summary>
        public const string IGNORE_CLIENT_WARNING_OPTION = "hyperv-ignore-client-warning";

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
        public IList<ICommandLineArgument> SupportedCommands =>
        [
            new CommandLineArgument(IGNORE_CLIENT_WARNING_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.HyperVSourceProvider.IgnoreConsistencyWarningShort, Strings.HyperVSourceProvider.IgnoreConsistencyWarningLong),
        ];

        /// <inheritdoc />
        public string MountedPath => VirtualSourcePath.GetMountedPath(VIRTUAL_SHARE);

        /// <inheritdoc />
        public string SourcePrefix => HYPERV_PATH_PREFIX;

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
                    || source.StartsWith(HYPERV_PATH_PREFIX + SOURCE_PATH_SEPARATOR, StringComparison.OrdinalIgnoreCase));

        /// <inheritdoc />
        public bool MatchesSource(string source)
            => IsHyperVSource(source);

        /// <inheritdoc />
        public string OriginalPathMetadataKey => METADATA_PREFIX + VirtualSourcePath.ORIGINAL_PATH_KEY;

        /// <inheritdoc />
        /// <remarks>
        /// <c>%HYPERV%</c> is the root and <c>%HYPERV%\&lt;vm-guid&gt;</c> is the folder of a machine.
        /// A path below a machine names a local file, which is stored under a name
        /// assigned when the machine is enumerated, so it is not translated.
        /// </remarks>
        public string? TranslateSourcePath(string sourcePath)
        {
            if (!IsHyperVSource(sourcePath))
                return null;

            var parts = sourcePath.Substring(HYPERV_PATH_PREFIX.Length).Split(SOURCE_PATH_SEPARATOR, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => MountedPath,
                1 => MountedPath + parts[0] + Path.DirectorySeparatorChar,
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

            var ignoreClientWarning = options.TryGetValue(IGNORE_CLIENT_WARNING_OPTION, out var ignoreClientWarningOption)
                && Library.Utility.Utility.ParseBool(ignoreClientWarningOption, false);
            if (!hypervUtility.IsVSSWriterSupported && !ignoreClientWarning)
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
            var providerId = Library.Utility.Utility.ParseGuidOption(_options, "vss-provider-id", Guid.Empty);
            hypervUtility.QueryHyperVGuestsInfo(provider, providerId, true);

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
                .Select(x => x.Substring(HYPERV_PATH_PREFIX.Length).Trim(SOURCE_PATH_SEPARATOR))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x =>
                {
                    var parts = x.Split(SOURCE_PATH_SEPARATOR, 2);
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
        public Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows() || !Util.AppendDirSeparator(path).StartsWith(MountedPath, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<ISourceProviderEntry?>(null);

            return VirtualSourcePath.FindEntryAsync(new HyperVRootEntry(MountedPath, _guests.Value, _snapshotService), path, isFolder, cancellationToken);
        }

        /// <summary>
        /// The text added to the name of a machine that is registered as a copy
        /// </summary>
        public const string RESTORED_NAME_SUFFIX = " (restored)";

        /// <summary>
        /// The file extensions of virtual hard disks
        /// </summary>
        private static readonly string[] VIRTUAL_DISK_EXTENSIONS = [".vhdx", ".avhdx", ".vhd", ".avhd"];

        /// <inheritdoc />
        /// <remarks>
        /// A machine restored to its original location is registered in place with its own ID,
        /// unless Hyper-V already has it. A machine restored to another folder is registered as
        /// a copy with a new ID and <see cref="RESTORED_NAME_SUFFIX"/> added to its name, with
        /// its disks and differencing disk chains pointing at the restored files.
        /// </remarks>
        public Task RegisterRestoredItemsAsync(IReadOnlyList<RestoredVirtualEntry> restoredEntries, IReadOnlyCollection<string> versionPaths, IReadOnlyDictionary<string, string?> options, CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows())
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "HyperVRegisterWindowsOnly", null, "Restored Hyper-V virtual machines can only be registered on Windows");
                return Task.CompletedTask;
            }

            RegisterRestoredItemsWindows(restoredEntries, versionPaths, cancellationToken);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Registers the restored machines (Windows-only implementation)
        /// </summary>
        /// <param name="restoredEntries">The entries restored from below the mount point</param>
        /// <param name="versionPaths">The stored paths of all entries below the mount point in the restored version, except the virtual folders</param>
        /// <param name="cancellationToken">The cancellation token</param>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void RegisterRestoredItemsWindows(IReadOnlyList<RestoredVirtualEntry> restoredEntries, IReadOnlyCollection<string> versionPaths, CancellationToken cancellationToken)
        {
            var versionPathsByMachine = versionPaths
                .GroupBy(GetMachineFolderName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);

            var machines = restoredEntries
                .Where(x => x.OriginalPath != null)
                .GroupBy(x => GetMachineFolderName(x.Path), StringComparer.OrdinalIgnoreCase);

            using var registration = new HyperVRestoreRegistration();
            foreach (var machine in machines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Guid.TryParse(machine.Key, out var vmId))
                    continue;

                // A machine is only registered when all of its files were restored
                var restoredPaths = machine.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (versionPathsByMachine.TryGetValue(machine.Key, out var allPaths) && !allPaths.All(restoredPaths.Contains))
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "HyperVRegisterIncomplete", null, "Not all files of the virtual machine {0} were restored, so it is not registered with Hyper-V", vmId);
                    continue;
                }

                try
                {
                    RegisterMachine(registration, vmId, machine.ToList());
                }
                catch (Exception ex) when (!ex.IsAbortException())
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "HyperVRegisterFailed", ex, "The files of the virtual machine {0} were restored, but it could not be registered with Hyper-V: {1}", vmId, ex.Message);
                }
            }
        }

        /// <summary>
        /// Registers a single restored machine
        /// </summary>
        /// <param name="registration">The registration helper</param>
        /// <param name="vmId">The ID the machine was backed up with</param>
        /// <param name="entries">The restored entries of the machine that have a local path</param>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static void RegisterMachine(HyperVRestoreRegistration registration, Guid vmId, List<RestoredVirtualEntry> entries)
        {
            var configuration = entries.FirstOrDefault(x => !x.IsFolder && IsConfigurationFile(x.OriginalPath!, vmId));
            if (configuration == null)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "HyperVRegisterNoConfiguration", null, "The configuration file of the virtual machine {0} was not restored, so it is not registered with Hyper-V", vmId);
                return;
            }

            // The checkpoint configurations are in a folder named Snapshots, which is
            // flattened into the machine folder when restoring to another folder
            var snapshotFolder = entries
                .Where(x => Path.GetFileName(Path.GetDirectoryName(x.OriginalPath!.TrimEnd(Path.DirectorySeparatorChar))) is string parent
                    && parent.Equals("Snapshots", StringComparison.OrdinalIgnoreCase))
                .Select(x => Path.GetDirectoryName(x.TargetPath.TrimEnd(Path.DirectorySeparatorChar)))
                .FirstOrDefault()
                ?? Path.GetDirectoryName(configuration.TargetPath)!;

            var originalLocation = entries.All(x => string.Equals(
                x.TargetPath.TrimEnd(Path.DirectorySeparatorChar),
                x.OriginalPath!.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase));

            if (originalLocation)
            {
                if (registration.IsRegistered(vmId))
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "HyperVAlreadyRegistered", "The virtual machine {0} is already registered with Hyper-V", vmId);
                    return;
                }

                var (_, name) = registration.Import(configuration.TargetPath, snapshotFolder, false, new Dictionary<string, string>(), null);
                Logging.Log.WriteInformationMessage(LOGTAG, "HyperVRegistered", "Registered the virtual machine \"{0}\" ({1}) with Hyper-V", name, vmId);
                return;
            }

            var restoredFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries.Where(x => !x.IsFolder))
                restoredFiles.TryAdd(entry.OriginalPath!, entry.TargetPath);

            // A restored differencing disk still refers to the parent it was backed up with,
            // which must be replaced with the restored parent
            foreach (var disk in entries.Where(x => !x.IsFolder && VIRTUAL_DISK_EXTENSIONS.Contains(Path.GetExtension(x.TargetPath), StringComparer.OrdinalIgnoreCase)))
            {
                var parent = registration.GetParentDisk(disk.TargetPath);
                if (parent == null)
                    continue;

                if (restoredFiles.TryGetValue(parent, out var restoredParent))
                    registration.SetParentDisk(disk.TargetPath, restoredParent);
                else
                    Logging.Log.WriteWarningMessage(LOGTAG, "HyperVParentDiskNotRestored", null, "The differencing disk {0} refers to the parent disk {1}, which was not restored", disk.TargetPath, parent);
            }

            var (id, copyName) = registration.Import(configuration.TargetPath, snapshotFolder, true, restoredFiles, RESTORED_NAME_SUFFIX);
            Logging.Log.WriteInformationMessage(LOGTAG, "HyperVRegisteredCopy", "Registered the restored virtual machine {0} with Hyper-V as \"{1}\" ({2})", vmId, copyName, id);
        }

        /// <summary>
        /// Gets the name of the machine folder an entry is stored in, which is the machine ID
        /// </summary>
        /// <param name="path">The stored path of the entry</param>
        /// <returns>The machine folder name</returns>
        private string GetMachineFolderName(string path)
            => path.Substring(Math.Min(MountedPath.Length, path.Length)).Split(Path.DirectorySeparatorChar, 2)[0];

        /// <summary>
        /// Checks whether a file is the configuration file of a machine
        /// </summary>
        /// <param name="path">The local path of the file</param>
        /// <param name="vmId">The machine ID</param>
        /// <returns>True if the file is the machine configuration</returns>
        private static bool IsConfigurationFile(string path, Guid vmId)
        {
            var fileName = Path.GetFileName(path);
            return fileName.Equals($"{vmId}.vmcx", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals($"{vmId}.xml", StringComparison.OrdinalIgnoreCase);
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
