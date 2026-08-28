// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.Filesystem;
using Duplicati.Proprietary.DiskImage.Filesystem.Fat32;
using Duplicati.Proprietary.DiskImage.Filesystem.Ntfs;
using Duplicati.Proprietary.DiskImage.General;
using Duplicati.Proprietary.DiskImage.Partition;

namespace Duplicati.Proprietary.DiskImage;

/// <summary>
/// The type of item a restore path refers to within the disk image hierarchy.
/// </summary>
internal enum RestorePathType
{
    /// <summary>The path refers to the disk-level geometry metadata file (geometry.json).</summary>
    Geometry,

    /// <summary>The path refers to a partition-level info file (partitioninfo.json).</summary>
    PartitionInfo,

    /// <summary>The path refers to disk-level data (e.g. the partition table).</summary>
    Disk,

    /// <summary>The path refers to partition-level data.</summary>
    Partition,

    /// <summary>The path refers to a file within a filesystem on a partition.</summary>
    File
}

/// <summary>
/// Restore provider for disk images. Allows restoring disk images back to physical disks.
/// When the target URL points to a partition within a disk (e.g. part_GPT_1), the data
/// is restored into that partition instead, which also supports backups of a single
/// partition that have no geometry metadata.
/// </summary>
public sealed class RestoreProvider : IRestoreDestinationProviderModule, IDisposable
{
    private static readonly string LOGTAG = Log.LogTagFromType<RestoreProvider>();

    private readonly string _devicePath;
    private readonly string _restorePath;

    /// <summary>
    /// The subpath within the disk hierarchy that the target URL points to
    /// (the part after the device name, e.g. a partition segment such as
    /// "part_GPT_1"), or empty if the URL targets the whole disk.
    /// </summary>
    private readonly string _subpath;

    private readonly bool _autoUnmount;
    private readonly bool _skipPartitionTable;
    private readonly bool _validateSize;
    private readonly bool _hasSetOverwriteOption;
    private IRawDisk? _targetDisk;
    private bool _disposed;

    /// <summary>
    /// Tracks pending writes for items that need to be written during Finalize.
    /// For partition table items, this stores the data to be written.
    /// </summary>
    private readonly ConcurrentDictionary<string, PendingWrite> _pendingWrites = new();

    /// <summary>
    /// Stores geometry metadata parsed from restored geometry files.
    /// Used to reconstruct disk, partition, and filesystem structures.
    /// </summary>
    private GeometryMetadata? _geometryMetadata;

    /// <summary>
    /// Stores partition info metadata parsed from restored partitioninfo.json files.
    /// When the restore target is a specific partition, this is keyed by the target
    /// partition number and provides the partition size and filesystem block size
    /// when geometry.json is not part of the restore selection. When the restore
    /// target is a whole disk, this is keyed by the source partition number and is
    /// used to create target partitions for partition backups.
    /// </summary>
    private readonly ConcurrentDictionary<int, PartitionInfoMetadata> _partitionInfos = new();

    /// <summary>
    /// Maps source partition numbers to partitions created on the target disk
    /// during this restore (whole-disk target without geometry metadata).
    /// </summary>
    private readonly Dictionary<int, IPartition> _createdPartitions = [];

    /// <summary>
    /// The allocations for partitions created during this restore, whose partition
    /// table entries are written to the target disk during Finalize.
    /// </summary>
    private readonly List<PartitionCreator.AllocatedPartition> _pendingPartitionCreations = [];

    /// <summary>
    /// Serializes partition creation, which can be triggered from concurrent writes.
    /// </summary>
    private readonly Lock _creationLock = new();

    private List<IPartition> _partitions = [];
    private List<IFilesystem> _filesystems = [];

    /// <summary>
    /// Represents a pending write operation.
    /// </summary>
    private abstract class PendingWrite : IDisposable
    {
        public abstract void Dispose();
    }

    /// <summary>
    /// Pending write for disk-level data (stored in memory until Finalize).
    /// </summary>
    private class DiskPendingWrite : PendingWrite
    {
        // Empty class, as this is used for tracking whether we have to write
        // disk-level data (e.g. partition table) during Finalize.

        public override void Dispose()
        {
            // Nothing to dispose
        }
    }

    /// <summary>
    /// Pending write for partition data (stored in memory until Finalize).
    /// </summary>
    private class PartitionPendingWrite(IPartition Partition) : PendingWrite
    {
        // Currently unused, but stored for potential future use if we need to
        // track partition-level writes separately from disk-level writes.
        public IPartition Partition { get; } = Partition;

        // Empty class, as this is used for tracking whether we have to write
        // partition-level data during Finalize. Although, this will probably
        // be handled by the file system writes.

        public override void Dispose()
        {
            // Nothing to dispose
        }
    }

    /// <summary>
    /// Default constructor for the restore provider.
    /// Only used for loading metadata about the provider.
    /// </summary>
    public RestoreProvider()
    {
        _devicePath = null!;
        _restorePath = null!;
        _subpath = string.Empty;
        _skipPartitionTable = false;
        _validateSize = true;
        _hasSetOverwriteOption = false;
    }

    /// <summary>
    /// Constructs the RestoreProvider with the given URL and options.
    /// </summary>
    /// <param name="url">The destination URL for the restore operation</param>
    /// <param name="options">The options for the restore operation</param>
    public RestoreProvider(string url, Dictionary<string, string?> options)
    {
        var uri = new Library.Utility.RelaxedUri(url);
        _restorePath = uri.HostAndPath;

        // Split the target into the disk device path and an optional subpath
        // within the disk hierarchy (e.g. a partition). When a subpath is present,
        // the restore writes into that partition instead of rewriting the whole disk,
        // which also allows restoring partition backups that have no geometry.json.
        (_devicePath, _subpath) = SourceProvider.SplitDeviceAndSubpath(uri.HostAndPath);

        _skipPartitionTable = Utility.ParseBoolOption(options, OptionsHelper.DISK_RESTORE_SKIP_PARTITION_TABLE_OPTION);
        _validateSize = !Utility.ParseBoolOption(options, OptionsHelper.DISK_RESTORE_SKIP_SIZE_VALIDATION_OPTION);
        _autoUnmount = Utility.ParseBoolOption(options, OptionsHelper.DISK_RESTORE_AUTO_UNMOUNT_OPTION);
        _hasSetOverwriteOption = Utility.ParseBoolOption(options, "overwrite");
    }

    /// <inheritdoc />
    public string Key => OptionsHelper.ModuleKey;

    /// <inheritdoc />
    public string DisplayName => Strings.RestoreProviderDisplayName;

    /// <inheritdoc />
    public string Description => Strings.RestoreProviderDescription;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => OptionsHelper.SupportedCommands;

    /// <inheritdoc />
    public string TargetDestination => _restorePath;

    /// <inheritdoc />
    public async Task Initialize(CancellationToken cancel)
    {
        if (string.IsNullOrEmpty(_devicePath))
            throw new UserInformationException("Disk device path is not specified.", "DiskDeviceNotSpecified");

        if (OperatingSystem.IsWindows())
            _targetDisk = new Windows(_devicePath);
        else if (OperatingSystem.IsMacOS())
            _targetDisk = new Mac(_devicePath);
        else if (OperatingSystem.IsLinux())
            _targetDisk = new Linux(_devicePath);
        else
            throw new PlatformNotSupportedException(Strings.PlatformNotSupported);

        if (_autoUnmount)
            if (!await _targetDisk.AutoUnmountAsync(cancel).ConfigureAwait(false))
                throw new UserInformationException($"Failed to auto unmount target disk: {_devicePath}. Ensure the disk is not in use and you have sufficient permissions.", "DiskAutoUnmountFailed");

        var msg = await _targetDisk.InitializeAsync(enableWrite: true, cancel);
        if (!string.IsNullOrWhiteSpace(msg))
            throw new UserInformationException(string.Format(Strings.RestoreDeviceNotWriteable, _devicePath, msg), "DiskInitializeFailed");

        // When the target URL points to a partition within the disk, resolve the
        // target partition from the disk's partition table. This allows restoring
        // backups of a single partition (which have no geometry.json) directly
        // into that partition.
        if (!string.IsNullOrEmpty(_subpath))
            await ResolveTargetPartitionAsync(cancel).ConfigureAwait(false);

        // Validate target size if requested
        if (_validateSize)
        {
            // Size validation will be done during Finalize when we have source metadata
            Log.WriteInformationMessage(LOGTAG, "RestoreSizeValidationEnabled", "Target size validation is enabled.");
        }
    }

    /// <summary>
    /// Resolves the target partition identified by <see cref="_subpath"/> from the
    /// target disk's partition table and registers it, so that restore paths
    /// referencing the partition can be mapped without geometry metadata.
    /// </summary>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>An awaitable task.</returns>
    /// <exception cref="UserInformationException">Thrown if the subpath does not identify a partition, or the partition cannot be found on the target disk.</exception>
    private async Task ResolveTargetPartitionAsync(CancellationToken cancel)
    {
        var segment = _subpath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(segment))
            throw new UserInformationException(string.Format(Strings.RestoreInvalidPath, _restorePath), "DiskRestoreInvalidPath");

        // The segment is expected in the format "part_{PartitionTableType}_{PartitionNumber}", e.g. "part_GPT_1"
        var parts = segment.Split('_');
        if (parts.Length < 3
            || !parts[0].Equals("part", StringComparison.OrdinalIgnoreCase)
            || !Enum.TryParse<PartitionTableType>(parts[1], true, out var ptType)
            || !int.TryParse(parts[2], out var partitionNumber))
            throw new UserInformationException($"The restore target '{_restorePath}' does not point to a partition. Expected format: part_{{PartitionTableType}}_{{PartitionNumber}}", "DiskRestoreInvalidPartitionPath");

        var table = await PartitionTableFactory.CreateAsync(_targetDisk!, cancel).ConfigureAwait(false);
        if (table == null || table.TableType == PartitionTableType.Unknown)
            throw new UserInformationException($"The target disk '{_devicePath}' does not have a recognizable partition table, so the target partition '{segment}' cannot be resolved.", "DiskRestoreNoPartitionTable");

        if (table.TableType != ptType)
            throw new UserInformationException($"The target disk '{_devicePath}' uses a {table.TableType} partition table, which does not match the requested {ptType} partition '{segment}'.", "DiskRestorePartitionTableMismatch");

        var partition = await table.GetPartitionAsync(partitionNumber, cancel).ConfigureAwait(false)
            ?? throw new UserInformationException(string.Format(Strings.RestoreTargetNotFound, $"{_devicePath}/{segment}"), "DiskRestorePartitionNotFound");

        _partitions.Add(partition);

        Log.WriteInformationMessage(LOGTAG, "RestoreTargetPartitionResolved",
            $"Restoring into partition {partitionNumber} ({ptType}) on {_devicePath}, offset: {partition.StartOffset}, size: {partition.Size} bytes");
    }

    /// <inheritdoc />
    public async Task Test(CancellationToken cancellationToken)
    {
        if (_targetDisk == null)
            throw new InvalidOperationException("Provider not initialized.");

        if (!_targetDisk.IsWriteable)
            throw new InvalidOperationException("Target disk is not writeable.");

        // Check if we have permission to write to the target device by reading
        // a sector and then writing it back.
        try
        {
            using var sectorStream = await _targetDisk.ReadSectorsAsync(0, 1, cancellationToken).ConfigureAwait(false);
            var sector = new byte[_targetDisk.SectorSize];
            await sectorStream.ReadAsync(sector, cancellationToken).ConfigureAwait(false);
            await _targetDisk.WriteBytesAsync(0, sector, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "RestoreDeviceNotWriteable", ex, $"Failed to write to target device: {_devicePath}. Ensure the device is not in use, is not write-protected, not mounted, and you have sufficient permissions.");
            throw;
        }

        Log.WriteInformationMessage(LOGTAG, "RestoreTestSuccess", $"Successfully opened target device: {_devicePath}, Size: {_targetDisk.Size} bytes, SectorSize: {_targetDisk.SectorSize}");
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> CreateFolderIfNotExists(string path, CancellationToken cancel)
    {
        // Disk images don't have folders in the traditional sense.
        // The "folders" are virtual representations of disks/partitions/filesystems.
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> FileExists(string path, CancellationToken cancel)
    {
        path = NormalizePath(path);

        if (_pendingWrites.ContainsKey(path))
            return Task.FromResult(true);

        return Task.FromResult(false);
    }

    /// <summary>
    /// Parses a partition segment from the path and returns the corresponding partition.
    /// The segment is expected to be in the format "part_{PartitionTableType}_{PartitionNumber}", e.g. "part_GPT_1".
    /// When a target partition has been set, it is always returned, as the partition number
    /// in the path refers to the source partition. Only for full-disk restores is the
    /// segment matched against the source partitions.
    /// </summary>
    /// <param name="segment">The partition segment string.</param>
    /// <returns>The corresponding partition.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the partition segment cannot be parsed or the partition is not found.</exception>
    internal IPartition ParsePartition(string segment)
    {
        // Example segment: "part_GPT_1"
        var parts = segment.Split('_');
        if (parts.Length < 3)
            throw new InvalidOperationException($"Unable to parse partition information from segment: {segment}. Expected format: part_{{PartitionTableType}}_{{PartitionNumber}}");

        // Parse partition table type (second part)
        if (!Enum.TryParse<PartitionTableType>(parts[1], true, out var ptType))
            throw new InvalidOperationException($"Unable to parse partition table type from segment: {segment}. Tried {parts[1]}");

        // Parse partition number (third part)
        if (!int.TryParse(parts[2], out var pn))
            throw new InvalidOperationException($"Unable to parse partition number from segment: {segment}. Tried {parts[2]}");

        // When restoring into a target partition, all partition content maps to that
        // partition; the number in the path is the source partition's and is not matched
        if (!string.IsNullOrEmpty(_subpath))
            return _partitions.FirstOrDefault()
                ?? throw new InvalidOperationException("The target partition has not been resolved.");

        // Full-disk restore: match the source partition from the path. The lookup
        // is done under the creation lock because CreateTargetPartition (triggered
        // by concurrent writes) adds to the list.
        IPartition? partition;
        lock (_creationLock)
        {
            partition = _partitions.FirstOrDefault(p =>
                p.PartitionNumber == pn &&
                p.PartitionTable.TableType == ptType);
        }

        if (partition == null)
        {
            // The restore set contains partition data but no geometry metadata
            // (e.g. a partition backup), so the partition does not exist in the
            // reconstructed structures. Create the partition on the target disk.
            partition = CreateTargetPartition(pn);
        }

        return partition;
    }

    /// <summary>
    /// Creates a new partition on the target disk for a partition backup being
    /// restored to a whole-disk target. The partition is allocated in free space
    /// (preferring the source partition's original offset) and registered so that
    /// restored content maps to it; the partition table entry is written during
    /// Finalize. Requires partition information (partitioninfo.json) from the backup.
    /// </summary>
    /// <param name="sourceNumber">The source partition number.</param>
    /// <returns>The created partition.</returns>
    /// <exception cref="UserInformationException">Thrown if the backup has no partition information, the partition table is skipped, or the disk has no suitable free space.</exception>
    private IPartition CreateTargetPartition(int sourceNumber)
    {
        lock (_creationLock)
        {
            if (_createdPartitions.TryGetValue(sourceNumber, out var existing))
                return existing;

            if (!_partitionInfos.TryGetValue(sourceNumber, out var info) || info.Partition == null)
                throw new UserInformationException($"The backup does not contain partition information ({PartitionInfoMetadata.FileName}) for partition {sourceNumber}. Restoring partition data to a whole-disk target requires a backup that includes partition information.", "DiskRestorePartitionInfoMissing");

            if (_skipPartitionTable)
                throw new UserInformationException($"Cannot create a partition on the target disk '{_devicePath}' because the partition table is excluded from the restore ({OptionsHelper.DISK_RESTORE_SKIP_PARTITION_TABLE_OPTION} is set). Restore to an existing partition on the target disk instead, or unset the option.", "DiskRestorePartitionCreationSkipped");

            var source = info.Partition;
            var allocated = PartitionCreator.AllocateAsync(
                _targetDisk!,
                source.Size,
                source.StartOffset,
                source.Type,
                source.FilesystemType,
                source.Name,
                source.VolumeGuid,
                _pendingPartitionCreations,
                CancellationToken.None).Await();

            var partition = new BasePartition
            {
                PartitionNumber = sourceNumber,
                Type = source.Type,
                PartitionTable = new ReconstructedPartitionTable(_targetDisk!, allocated.TableType),
                StartOffset = allocated.StartOffset,
                Size = allocated.Size,
                Name = source.Name,
                FilesystemType = source.FilesystemType,
                VolumeGuid = source.VolumeGuid,
                RawDisk = _targetDisk,
                StartingLba = allocated.StartOffset / _targetDisk!.SectorSize,
                EndingLba = (allocated.StartOffset + allocated.Size) / _targetDisk!.SectorSize - 1,
                Attributes = 0
            };

            _partitions.Add(partition);
            _createdPartitions[sourceNumber] = partition;
            _pendingPartitionCreations.Add(allocated);

            Log.WriteInformationMessage(LOGTAG, "RestoreTargetPartitionCreated",
                $"Creating partition for source partition {sourceNumber} on {_devicePath}, offset: {allocated.StartOffset}, size: {allocated.Size} bytes; the partition table entry is written when the restore completes");

            return partition;
        }
    }

    /// <summary>
    /// Resolves the partition for partition-level content whose restore path has no
    /// partition segment. This shape results from a restore selection that lies
    /// entirely within a single source partition, where the restore path mapping
    /// stripped the partition folder as part of the common prefix. When the restore
    /// target is a partition, the content maps to it. For a whole-disk target, the
    /// partition info captured from the backup (a priority file, restored before any
    /// partition content) identifies the source partition: an existing partition
    /// with that number is used when present, otherwise a new partition is created
    /// on the target disk.
    /// </summary>
    /// <returns>The partition the content maps to.</returns>
    /// <exception cref="UserInformationException">Thrown if the backup has no partition information, or contains information for multiple partitions, which cannot be told apart without partition segments in the paths.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the target partition has not been resolved (partition target only).</exception>
    private IPartition ResolveImplicitTargetPartition()
    {
        // When restoring into a target partition, all partition content maps to
        // that partition
        if (!string.IsNullOrEmpty(_subpath))
            return _partitions.FirstOrDefault()
                ?? throw new InvalidOperationException("The target partition has not been resolved.");

        int sourceNumber;
        lock (_creationLock)
        {
            if (_partitionInfos.IsEmpty)
                throw new UserInformationException($"The backup does not contain partition information ({PartitionInfoMetadata.FileName}) for the partition being restored. Restoring partition data to a whole-disk target requires a backup that includes partition information; alternatively, restore to an existing partition on the target disk (e.g. '{_restorePath}{Path.DirectorySeparatorChar}part_GPT_1').", "DiskRestorePartitionInfoMissing");

            // Whole-disk restores key the info by source partition number; more
            // than one entry means the restore set spans multiple source partitions
            // and the stripped paths cannot be attributed to a single partition.
            if (_partitionInfos.Count > 1)
                throw new UserInformationException($"The restore data contains partition-level data for multiple partitions, but the restore paths do not identify the partition. Restore the partitions including their partition folders, or restore each partition to a partition on the target disk instead (e.g. '{_restorePath}{Path.DirectorySeparatorChar}part_GPT_1').", "DiskRestoreAmbiguousPartitionContent");

            sourceNumber = _partitionInfos.Keys.First();

            // The lookup is done under the creation lock because CreateTargetPartition
            // (triggered by concurrent writes) adds to the list.
            var partition = _partitions.FirstOrDefault(p => p.PartitionNumber == sourceNumber);
            if (partition != null)
                return partition;
        }

        // The restore set contains partition data but no geometry metadata
        // (e.g. a partition backup), so the partition does not exist in the
        // reconstructed structures. Create the partition on the target disk.
        return CreateTargetPartition(sourceNumber);
    }

    /// <summary>
    /// Parses a filesystem segment from the path and returns the corresponding filesystem.
    /// The segment is expected to be in the format "fs_{FileSystemType}", e.g. "fs_NTFS".
    /// </summary>
    /// <param name="partition">The partition to which the filesystem belongs.</param>
    /// <param name="segment">The filesystem segment string.</param>
    /// <returns>The corresponding filesystem.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the filesystem segment cannot be parsed or the filesystem is not found.</exception>
    internal IFilesystem ParseFilesystem(IPartition partition, string segment)
    {
        // Example segment: "fs_NTFS"
        var parts = segment.Split('_');
        if (parts.Length < 2)
            throw new InvalidOperationException($"Unable to parse filesystem information from segment: {segment}. Expected format: fs_{{FileSystemType}}");

        // Reconstruct filesystem type from remaining parts (e.g., "fs_Unknown" or "fs_NTFS")
        var fsTypeStr = string.Join('_', parts[1..]);
        if (!Enum.TryParse<FileSystemType>(fsTypeStr, true, out var fsType))
            throw new InvalidOperationException($"Unable to parse filesystem type from segment: {segment}. Tried {fsTypeStr}");

        // Find the filesystem in our reconstructed list
        var fs = _filesystems.FirstOrDefault(f => f.Partition.PartitionNumber == partition.PartitionNumber && f.Type == fsType);

        // Read under the creation lock, as CreateTargetPartition (triggered by
        // concurrent writes) adds to the dictionary
        bool isCreatedPartition;
        lock (_creationLock)
            isCreatedPartition = _createdPartitions.ContainsKey(partition.PartitionNumber);

        if (fs == null && (!string.IsNullOrEmpty(_subpath) || isCreatedPartition))
        {
            // When restoring into a target partition, or into a partition created
            // on the target disk, geometry.json is not part of the restore selection.
            // The partition info file (restored as a priority file before any
            // partition content) provides the block size and source partition size,
            // and is required for the restore. For a partition target the info is
            // keyed by the target partition number; for created partitions by the
            // source partition number (which the created partition carries).
            if (!_partitionInfos.TryGetValue(partition.PartitionNumber, out var info))
                throw new UserInformationException($"The backup does not contain partition information ({PartitionInfoMetadata.FileName}) for the partition being restored. Restoring directly into a partition requires a backup that includes partition information.", "DiskRestorePartitionInfoMissing");

            if (info.Filesystem != null && info.Filesystem.Type != fsType)
                throw new InvalidOperationException($"The backup describes the filesystem on the restored partition as {info.Filesystem.Type}, but the restore path refers to it as {fsType}.");

            if (_validateSize && info.Partition != null && info.Partition.Size > partition.Size)
                throw new UserInformationException(
                    string.Format(Strings.RestoreTargetTooSmall, partition.Size, info.Partition.Size),
                    "RestoreTargetTooSmall");

            var blockSize = info.Filesystem is { BlockSize: > 0 } fsGeom
                ? fsGeom.BlockSize
                : throw new UserInformationException($"The partition information ({PartitionInfoMetadata.FileName}) in the backup does not provide a valid block size.", "DiskRestoreBlockSizeMissing");

            fs = new UnknownFilesystem(partition, blockSize, fsType);
            _filesystems.Add(fs);
            Log.WriteInformationMessage(LOGTAG, "FilesystemHandlerCreated",
                $"Created block-level filesystem handler (reported as {fsType}) for target partition {partition.PartitionNumber} with block size {blockSize}; content is restored as raw blocks");
        }

        if (fs == null)
            throw new InvalidOperationException($"No matching filesystem found for segment: {segment} with type {fsType}");

        return fs;
    }

    /// <summary>
    /// Parses the given path to determine if it refers to a disk-level item, partition, partition info file, or file, and returns the corresponding objects.
    /// The path is expected to be in the format:
    /// root/part_{PartitionTableType}_{PartitionNumber}/fs_{FileSystemType}/path/to/file
    /// When the restore selection lies entirely within a single source partition, the
    /// restore path mapping strips the partition folder as part of the common prefix;
    /// such paths carry no part_ segment and are resolved through the captured
    /// partition info (see <see cref="ResolveImplicitTargetPartition"/>).
    /// </summary>
    /// <param name="path">The path to parse.</param>
    /// <returns>A tuple containing the item type, partition, and filesystem.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the path cannot be parsed.</exception>
    internal (RestorePathType Type, IPartition? Partition, IFilesystem? Filesystem) ParsePath(string path)
    {
        // For disk image restore, the path is expected to be in the format:
        // root/part_{PartitionTableType}_{PartitionNumber}/fs_{FileSystemType}/path/to/file
        // We need to parse out the partition and filesystem information from the path for proper handling

        // Normalize path separators
        path = NormalizePath(path);

        // On Windows, both / and \ are path separators. On other platforms, only / is.
        var separators = OperatingSystem.IsWindows() ? new[] { '/', '\\' } : new[] { '/' };

        if (IsGeometryFile(path))
        {
            // The path matches the target disk's own geometry file. With a partition
            // target this still means the restore set contains a full-disk backup
            if (!string.IsNullOrEmpty(_subpath))
                throw DiskContentToPartitionTarget();
            return (RestorePathType.Geometry, null, null);
        }

        var segments = path.Split(separators, StringSplitOptions.RemoveEmptyEntries) ??
            throw new InvalidOperationException($"Unable to parse path: {path}");

        // When the restore target is a single partition, a geometry file in the
        // restore set indicates a full-disk backup, which cannot be restored into
        // a partition (its partition table and other partitions would have nowhere
        // to go). Fail here, before any partition content is written. The geometry
        // file sits at the disk or partition level, so a file named geometry.json
        // inside a filesystem (fs_ segment present) is regular file content and is
        // restored like any other file.
        if (!string.IsNullOrEmpty(_subpath)
            && segments.Length > 0
            && segments[^1].Equals(GeometryMetadata.FileName, StringComparison.OrdinalIgnoreCase)
            && !segments.Any(s => s.StartsWith("fs_", StringComparison.OrdinalIgnoreCase)))
            throw DiskContentToPartitionTarget();

        static UserInformationException DiskContentToPartitionTarget()
            => new($"The backup contains a full disk image (including '{GeometryMetadata.FileName}'), which cannot be restored into a single partition. Restore to a whole-disk target instead.", "DiskRestoreDiskContentToPartitionTarget");

        string? partitionSegment = segments.FirstOrDefault(s => s.StartsWith("part_", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(partitionSegment))
        {
            string? filesystemSegment = segments.FirstOrDefault(s => s.StartsWith("fs_", StringComparison.OrdinalIgnoreCase));

            // A partition info file sits directly in the partition folder and
            // travels with the partition content. It is not resolved to a
            // partition here, as it is restored during the priority-file phase
            // where the geometry-based reconstruction may not have run yet.
            if (filesystemSegment == null && segments[^1].Equals(PartitionInfoMetadata.FileName, StringComparison.OrdinalIgnoreCase))
                return (RestorePathType.PartitionInfo, null, null);

            var partition = ParsePartition(partitionSegment);
            if (!string.IsNullOrEmpty(filesystemSegment))
            {
                var filesystem = ParseFilesystem(partition, filesystemSegment);
                return (RestorePathType.File, partition, filesystem);
            }
            return (RestorePathType.Partition, partition, null);
        }

        // No partition segment in the path. Partition-level content has this shape
        // when the restore selection is entirely within a single source partition:
        // the restore path mapping strips the common prefix, which then includes
        // the partition folder. The content belongs to that single source partition.
        var implicitFilesystemSegment = segments.FirstOrDefault(s => s.StartsWith("fs_", StringComparison.OrdinalIgnoreCase));

        // A partition info file sits directly in the partition folder, so with the
        // partition folder stripped it appears at the root. It is not resolved to a
        // partition here, as it is restored during the priority-file phase where the
        // geometry-based reconstruction may not have run yet. A file of the same
        // name inside a filesystem (fs_ segment present) is regular file content.
        if (implicitFilesystemSegment == null
            && segments.Length > 0
            && segments[^1].Equals(PartitionInfoMetadata.FileName, StringComparison.OrdinalIgnoreCase))
            return (RestorePathType.PartitionInfo, null, null);

        if (implicitFilesystemSegment != null)
        {
            var partition = ResolveImplicitTargetPartition();
            var filesystem = ParseFilesystem(partition, implicitFilesystemSegment);
            return (RestorePathType.File, partition, filesystem);
        }

        return (RestorePathType.Disk, null, null);
    }

    /// <inheritdoc />
    public Task<Stream> OpenWrite(string path, CancellationToken cancel)
    {
        var (typeStr, partition, filesystem) = ParsePath(path);

        return typeStr switch
        {
            RestorePathType.Geometry => OpenWriteGeometry(cancel),
            RestorePathType.PartitionInfo => OpenWritePartitionInfo(path, cancel),
            RestorePathType.Disk => OpenWriteDisk(path, cancel),
            RestorePathType.Partition => OpenWritePartition(path, partition!, cancel),
            RestorePathType.File => filesystem!.OpenWriteStreamAsync(path, cancel),
            _ => throw new NotSupportedException($"Unsupported item type: {typeStr}")
        };
    }

    /// <summary>
    /// Opens a stream for writing partition info metadata (captured when disposed).
    /// When restoring into a partition, the info is keyed by the target partition;
    /// for whole-disk restores it is keyed by the source partition number and is
    /// used to create partitions on the target disk when the backup has no
    /// geometry metadata.
    /// </summary>
    private Task<Stream> OpenWritePartitionInfo(string path, CancellationToken cancel)
        => Task.FromResult<Stream>(new CaptureStream(new MemoryStream(), data => CapturePartitionInfo(path, data)));

    /// <summary>
    /// Opens a stream for read-write access to partition info metadata.
    /// </summary>
    private async Task<Stream> OpenReadWritePartitionInfo(string path, CancellationToken cancel)
    {
        var stream = new MemoryStream();
        if (TryGetTargetPartitionInfo(out var existing))
        {
            var current = System.Text.Encoding.UTF8.GetBytes(existing!.ToJson());
            await stream.WriteAsync(current, cancel);
            stream.Position = 0;
        }

        return new CaptureStream(stream, data => CapturePartitionInfo(path, data));
    }

    /// <summary>
    /// Captures and parses partition info metadata written during restore.
    /// </summary>
    /// <param name="path">The path of the partition info file.</param>
    /// <param name="data">The written data.</param>
    /// <exception cref="UserInformationException">Thrown if the restore set contains info for multiple source partitions, which cannot be restored into a single partition.</exception>
    private void CapturePartitionInfo(string path, ReadOnlyMemory<byte> data)
    {
        PartitionInfoMetadata? info;
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(data.Span);
            info = PartitionInfoMetadata.FromJson(json);
        }
        catch (Exception ex)
        {
            Log.WriteWarningMessage(LOGTAG, "PartitionInfoParseFailed", ex, $"Failed to parse partition info from '{path}'.");
            return;
        }

        if (info == null)
        {
            Log.WriteWarningMessage(LOGTAG, "PartitionInfoParseFailed", null, $"Failed to parse partition info from '{path}': parsed object was null.");
            return;
        }

        if (info.Version > PartitionInfoMetadata.CurrentVersion)
            Log.WriteWarningMessage(LOGTAG, "PartitionInfoVersionNewer", null,
                $"The partition info from '{path}' has version {info.Version}, which is newer than the supported version {PartitionInfoMetadata.CurrentVersion}. Some fields may be ignored.");

        // Without the source partition number, multi-partition conflicts cannot be
        // detected and the size validation in ParseFilesystem has no source size.
        // Refuse to store incomplete info so a previously captured, complete info
        // is not silently overwritten by a lesser one.
        if (info.Partition?.Number is not int newNumber)
        {
            Log.WriteWarningMessage(LOGTAG, "PartitionInfoIncomplete", null,
                $"Ignoring partition info from '{path}' because it does not identify the source partition.");
            return;
        }

        if (string.IsNullOrEmpty(_subpath))
        {
            // Whole-disk restore: geometry.json normally provides the metadata.
            // The info is stored keyed by the source partition number so that
            // partition backups (which have no geometry.json) can be restored by
            // creating the partition on the target disk.
            _partitionInfos[newNumber] = info;
            Log.WriteInformationMessage(LOGTAG, "PartitionInfoParsed",
                $"Parsed partition info for source partition {newNumber}: filesystem {info.Filesystem?.Type}, block size {info.Filesystem?.BlockSize}, source size {info.Partition?.Size} bytes");
            return;
        }

        // The info describes the source partition, but is keyed by the target
        // partition: when a target partition has been set, all restored content
        // maps to it regardless of the partition numbers in the paths
        var target = _partitions.FirstOrDefault();
        if (target == null)
        {
            Log.WriteWarningMessage(LOGTAG, "PartitionInfoParseFailed", null, $"Failed to capture partition info from '{path}': the target partition has not been resolved.");
            return;
        }

        // A second partition info describing a different source partition means the
        // restore set contains multiple partitions (e.g. a disk backup without the
        // geometry file), which cannot be mapped into the single target partition.
        // Since incomplete infos are rejected above, any existing entry always has
        // a source partition number to compare against.
        if (_partitionInfos.TryGetValue(target.PartitionNumber, out var existing)
            && existing.Partition!.Number != newNumber)
            throw new UserInformationException($"The backup contains data for multiple partitions (at least partitions {existing.Partition.Number} and {newNumber}), which cannot be restored into a single partition. Restore to a whole-disk target instead.", "DiskRestoreDiskContentToPartitionTarget");

        _partitionInfos[target.PartitionNumber] = info;
        Log.WriteInformationMessage(LOGTAG, "PartitionInfoParsed",
            $"Parsed partition info for target partition {target.PartitionNumber}: filesystem {info.Filesystem?.Type}, block size {info.Filesystem?.BlockSize}, source size {info.Partition?.Size} bytes");
    }

    /// <summary>
    /// Gets the partition info captured for the target partition, if any.
    /// Only applies when the restore target is a specific partition; whole-disk
    /// restores store infos keyed by source partition number.
    /// </summary>
    /// <param name="info">The captured partition info, or <c>null</c> if none was captured.</param>
    /// <returns><c>true</c> if partition info was captured for the target partition; otherwise, <c>false</c>.</returns>
    private bool TryGetTargetPartitionInfo(out PartitionInfoMetadata? info)
    {
        info = null;
        if (string.IsNullOrEmpty(_subpath))
            return false;

        var target = _partitions.FirstOrDefault();
        return target != null && _partitionInfos.TryGetValue(target.PartitionNumber, out info);
    }

    /// <summary>
    /// Opens a stream for writing disk-level data (stored in memory until Finalize).
    /// </summary>
    private Task<Stream> OpenWriteDisk(string path, CancellationToken cancel)
    {
        var stream = new MemoryStream();

        var wrapper = new CaptureStream(stream, data =>
        {
            var pendingWrite = new DiskPendingWrite();
            _pendingWrites.AddOrUpdate(path, pendingWrite, (_, old) =>
            {
                old.Dispose();
                return pendingWrite;
            });
        });

        return Task.FromResult<Stream>(wrapper);
    }

    /// <summary>
    /// Opens a stream for writing partition data (stored in memory until Finalize).
    /// </summary>
    private Task<Stream> OpenWritePartition(string path, IPartition partition, CancellationToken cancel)
    {
        var stream = new MemoryStream();

        var wrapper = new CaptureStream(stream, data =>
        {
            var pendingWrite = new PartitionPendingWrite(partition);
            _pendingWrites.AddOrUpdate(path, pendingWrite, (_, old) =>
            {
                old.Dispose();
                return pendingWrite;
            });
        });

        return Task.FromResult<Stream>(wrapper);
    }

    /// <summary>
    /// Opens a stream for writing geometry metadata (stored in memory until Finalize).
    /// </summary>
    private Task<Stream> OpenWriteGeometry(CancellationToken cancel)
    {
        var stream = new MemoryStream();

        var wrapper = new CaptureStream(stream, data =>
        {
            try
            {
                // Parse the geometry metadata from the JSON data
                var json = System.Text.Encoding.UTF8.GetString(data.Span);
                _geometryMetadata = GeometryMetadata.FromJson(json);

                Log.WriteInformationMessage(LOGTAG, "GeometryMetadataParsed", "Successfully parsed geometry metadata from geometry.json during OpenWrite");
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "GeometryMetadataParseFailed", ex,
                    "Failed to parse geometry metadata from geometry.json during OpenWrite");
            }
        });

        return Task.FromResult<Stream>(wrapper);
    }

    /// <inheritdoc />
    public Task<Stream> OpenRead(string path, CancellationToken cancel)
    {
        var (typeStr, partition, filesystem) = ParsePath(path);

        return typeStr switch
        {
            RestorePathType.Disk => OpenReadDisk(path, cancel),
            RestorePathType.Partition => OpenReadPartition(path, partition!, cancel),
            RestorePathType.Geometry => OpenReadGeometry(cancel),
            RestorePathType.PartitionInfo => OpenReadPartitionInfo(path, cancel),
            RestorePathType.File => filesystem!.OpenReadStreamAsync(path, cancel),
            _ => throw new NotSupportedException($"Unsupported item type: {typeStr}")
        };
    }

    /// <summary>
    /// Opens a stream for reading geometry metadata.
    /// </summary>
    private Task<Stream> OpenReadGeometry(CancellationToken cancel)
    {
        if (_geometryMetadata == null)
            throw new InvalidOperationException("Geometry metadata not available for reading.");

        var json = _geometryMetadata.ToJson();
        var data = System.Text.Encoding.UTF8.GetBytes(json);
        return Task.FromResult<Stream>(new MemoryStream(data));
    }

    /// <summary>
    /// Opens a stream for reading partition info metadata.
    /// </summary>
    private Task<Stream> OpenReadPartitionInfo(string path, CancellationToken cancel)
    {
        if (TryGetTargetPartitionInfo(out var info))
        {
            var data = System.Text.Encoding.UTF8.GetBytes(info!.ToJson());
            return Task.FromResult<Stream>(new MemoryStream(data));
        }

        // Partition info is only readable when the restore target is a specific
        // partition, where it is keyed by the target partition number. Whole-disk
        // restores store infos keyed by source partition number, which the path
        // does not identify (there may be several, and geometry.json provides the
        // metadata), so return an empty stream instead of failing.
        Log.WriteVerboseMessage(LOGTAG, "PartitionInfoNotAvailable", $"Partition info metadata for '{path}' was not captured; returning an empty stream.");
        return Task.FromResult<Stream>(new MemoryStream());
    }

    /// <summary>
    /// Opens a stream for reading disk-level data.
    /// </summary>
    private Task<Stream> OpenReadDisk(string path, CancellationToken cancel)
    {
        if (_targetDisk == null)
            throw new InvalidOperationException("Target disk not initialized.");

        throw new InvalidOperationException("Reading raw disk data as part of the restore flow is currently not supported in this implementation.");
    }

    /// <summary>
    /// Opens a stream for reading partition data.
    /// </summary>
    private Task<Stream> OpenReadPartition(string path, IPartition partition, CancellationToken cancel)
    {
        if (_targetDisk == null)
            throw new InvalidOperationException("Target disk not initialized.");

        throw new InvalidOperationException("Reading raw partition data as part of the restore flow is currently not supported in this implementation.");
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadWrite(string path, CancellationToken cancel)
    {
        var (typeStr, partition, filesystem) = ParsePath(path);

        return typeStr switch
        {
            RestorePathType.Disk => OpenWriteDisk(path, cancel), // For disk-level, we treat read-write as write since we only capture the data to be written during Finalize
            RestorePathType.Partition => Task.FromResult((Stream)new MemoryStream()),
            RestorePathType.Geometry => OpenReadWriteGeometry(cancel),
            RestorePathType.PartitionInfo => OpenReadWritePartitionInfo(path, cancel),
            RestorePathType.File => filesystem!.OpenReadWriteStreamAsync(path, cancel),
            _ => throw new NotSupportedException($"Unsupported item type: {typeStr}")
        };
    }

    /// <summary>
    /// Opens a stream for read-write access to geometry metadata.
    /// </summary>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>A stream for read-write access to geometry metadata.</returns>
    private async Task<Stream> OpenReadWriteGeometry(CancellationToken cancel)
    {
        // For read-write, we return a stream that can be read from (current state)
        // and written to (updating the state).
        var currentData = Array.Empty<byte>();
        if (_geometryMetadata != null)
        {
            var json = _geometryMetadata.ToJson();
            currentData = System.Text.Encoding.UTF8.GetBytes(json);
        }

        var stream = new MemoryStream();
        if (currentData.Length > 0)
        {
            await stream.WriteAsync(currentData, cancel);
            stream.Position = 0;
        }

        var wrapper = new CaptureStream(stream, async data =>
        {
            try
            {
                var json = System.Text.Encoding.UTF8.GetString(data.Span);
                var newGeometry = GeometryMetadata.FromJson(json);
                if (newGeometry != null)
                {
                    _geometryMetadata = newGeometry;

                    if (!string.IsNullOrEmpty(_subpath))
                    {
                        // The restore target is a single partition, so disk-level
                        // geometry must not be applied to the target disk.
                        Log.WriteInformationMessage(LOGTAG, "GeometryMetadataIgnored",
                            "Geometry metadata was restored, but the restore target is a partition; the partition table and disk structures of the target disk will not be modified.");
                        return;
                    }

                    // Clear existing reconstructed objects
                    foreach (var part in _partitions)
                        part.Dispose();
                    _partitions.Clear();
                    foreach (var fs in _filesystems)
                        fs.Dispose();
                    _filesystems.Clear();

                    // Reconstruct disk, partition table, partitions, and filesystems from geometry metadata
                    ReconstructFromGeometryMetadata();

                    using var _ = await OpenWriteDisk("disk", cancel); // Mark disk-level data as pending write for Finalize

                    Log.WriteInformationMessage(LOGTAG, "GeometryMetadataUpdated", $"Successfully updated geometry metadata during ReadWrite. Reconstructed {_partitions.Count} partitions and {_filesystems.Count} filesystems.");
                }
                else
                {
                    Log.WriteWarningMessage(LOGTAG, "GeometryMetadataUpdateFailed", null, $"Failed to parse geometry metadata during ReadWrite. Parsed object was null.");
                }
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "GeometryMetadataParseFailed", ex,
                    $"Failed to parse geometry metadata during ReadWrite");
            }
        });

        return wrapper;
    }

    /// <summary>
    /// Returns the number of bytes needed to encode the JSON string as UTF-8.
    /// </summary>
    /// <param name="json">The JSON string; can be <c>null</c></param>
    /// <returns>The length in bytes, or 0 if the string is null or empty</returns>
    private static long GetJsonUtf8Length(string? json)
        => string.IsNullOrWhiteSpace(json) ? 0L : System.Text.Encoding.UTF8.GetByteCount(json);

    /// <summary>
    /// Gets the length of a file at the specified path.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>The file length in bytes.</returns>
    public Task<long> GetFileLength(string path, CancellationToken cancel)
    {
        var (typeStr, partition, filesystem) = ParsePath(path);

        return typeStr switch
        {
            RestorePathType.Disk => Task.FromResult(0L),
            RestorePathType.Partition => Task.FromResult(0L),
            RestorePathType.Geometry => Task.FromResult(GetJsonUtf8Length(_geometryMetadata?.ToJson())),
            RestorePathType.PartitionInfo => Task.FromResult(TryGetTargetPartitionInfo(out var info) ? GetJsonUtf8Length(info?.ToJson()) : 0L),
            RestorePathType.File => filesystem!.GetFileLengthAsync(path, cancel),
            _ => throw new NotSupportedException($"Unsupported item type: {typeStr}")
        };
    }

    /// <inheritdoc />
    public Task<bool> HasReadOnlyAttribute(string path, CancellationToken cancel)
        => Task.FromResult(false);

    /// <inheritdoc />
    public Task ClearReadOnlyAttribute(string path, CancellationToken cancel)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task<bool> WriteMetadata(string path, Dictionary<string, string?> metadata, bool restoreSymlinkMetadata, bool restorePermissions, CancellationToken cancel)
    {
        // TODO properly handle metadata

        // When the entry describes a source partition, validate that the target
        // partition is large enough to hold the source partition's data.
        if (_validateSize
            && metadata.TryGetValue("diskimage:Type", out var entryType)
            && string.Equals(entryType, "partition", StringComparison.OrdinalIgnoreCase)
            && metadata.TryGetValue("partition:Number", out var numberStr)
            && int.TryParse(numberStr, out var partitionNumber)
            && metadata.TryGetValue("partition:Size", out var sizeStr)
            && long.TryParse(sizeStr, out var sourceSize))
        {
            // When a target partition has been set, it is always the target of the
            // restore; otherwise match the source partition number (full-disk restore).
            var target = !string.IsNullOrEmpty(_subpath)
                ? _partitions.FirstOrDefault()
                : _partitions.FirstOrDefault(p => p.PartitionNumber == partitionNumber);

            if (target != null && sourceSize > target.Size)
                throw new UserInformationException(
                    string.Format(Strings.RestoreTargetTooSmall, target.Size, sourceSize),
                    "RestoreTargetTooSmall");
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task DeleteFolder(string path, CancellationToken cancel)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task DeleteFile(string path, CancellationToken cancel)
        => Task.CompletedTask;

    /// <inheritdoc />
    public IList<string> GetPriorityFiles()
    {
        return [GeometryMetadata.FileName, PartitionInfoMetadata.FileName];
    }

    /// <summary>
    /// Checks if a file path is the geometry metadata file.
    /// </summary>
    /// <remarks>Assumes the path has already been normalized by ParsePath.</remarks>
    /// <param name="path">The file path to check (already normalized by ParsePath).</param>
    /// <returns><c>true</c> if the path is the geometry file; otherwise, <c>false</c>.</returns>
    private bool IsGeometryFile(string path)
    {
        if (_targetDisk == null)
            return false;

        string geometryDevicePath = NormalizePath(_targetDisk.DevicePath);

        string expectedGeometryPath = $"{geometryDevicePath}{Path.DirectorySeparatorChar}{GeometryMetadata.FileName}";

        return string.Equals(path, expectedGeometryPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task Finalize(Action<double>? progressCallback, CancellationToken cancel)
    {
        if (_targetDisk == null)
            throw new InvalidOperationException("Provider not initialized.");

        var totalItems = _pendingWrites.Count + _pendingPartitionCreations.Count;
        if (totalItems == 0)
        {
            Log.WriteInformationMessage(LOGTAG, "RestoreNoItems", "No items to restore.");
            return;
        }

        Log.WriteInformationMessage(LOGTAG, "RestoreStarting", $"Starting final restore of {totalItems} items to {_devicePath}");

        var processedCount = 0;

        // Group items by type for ordered restoration
        var diskItems = _pendingWrites.Where(kv => kv.Value is DiskPendingWrite).ToList();
        var partitionItems = _pendingWrites.Where(kv => kv.Value is PartitionPendingWrite).ToList();

        // Restore disk-level items (partition table)
        if (!_skipPartitionTable && diskItems.Count > 0)
        {
            if (!string.IsNullOrEmpty(_subpath))
            {
                // The restore target is a single partition, so the partition table
                // of the target disk must not be modified.
                Log.WriteWarningMessage(LOGTAG, "PartitionTableRestoreSkipped", null,
                    "Skipping partition table restore because the restore target is a partition, not a whole disk.");
            }
            else if (_geometryMetadata?.PartitionTable != null)
            {
                try
                {
                    var partitionTableData = PartitionTableSynthesizer.SynthesizePartitionTable(_geometryMetadata);
                    if (partitionTableData != null)
                    {
                        // Write primary partition table at the start of the disk
                        await _targetDisk.WriteBytesAsync(0, partitionTableData, cancel).ConfigureAwait(false);
                        Log.WriteInformationMessage(LOGTAG, "PartitionTableWritten",
                            $"Successfully wrote {_geometryMetadata.PartitionTable.Type} partition table to disk.");

                        // For GPT, also write the secondary GPT header at the end of the disk
                        if (_geometryMetadata.PartitionTable.Type == PartitionTableType.GPT)
                        {
                            await PartitionTableSynthesizer.WriteSecondaryGPTAsync(_targetDisk, _geometryMetadata, partitionTableData, cancel).ConfigureAwait(false);
                            Log.WriteInformationMessage(LOGTAG, "SecondaryGPTWritten",
                                "Successfully wrote secondary GPT header and partition entries.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage(LOGTAG, "PartitionTableWriteFailed", ex,
                        $"Failed to write partition table to disk: {ex.Message}");
                    throw;
                }
            }
            else
            {
                Log.WriteWarningMessage(LOGTAG, "NoPartitionTableMetadata", null,
                    "Disk-level items pending but no partition table metadata available to write.");
            }
            processedCount += diskItems.Count;
            progressCallback?.Invoke(processedCount / (double)totalItems);
        }

        // Write partition table entries for partitions created during this restore
        // (partition backups restored to a whole-disk target). The partition content
        // has already been written to the allocated space; adding the table entries
        // last ensures an interrupted restore leaves no entries pointing at
        // incomplete data.
        if (_pendingPartitionCreations.Count > 0)
        {
            if (_skipPartitionTable)
            {
                // Unreachable: partition creation is refused when the partition
                // table is skipped. Guarded here for safety.
                Log.WriteWarningMessage(LOGTAG, "PartitionCreationSkipped", null,
                    "Skipping creation of partition table entries because the partition table is excluded from the restore.");
            }
            else
            {
                try
                {
                    await PartitionCreator.WritePartitionsAsync(_targetDisk, _pendingPartitionCreations, cancel).ConfigureAwait(false);
                    Log.WriteInformationMessage(LOGTAG, "PartitionEntriesWritten",
                        $"Successfully wrote {_pendingPartitionCreations.Count} partition table entrie(s) to {_devicePath}.");
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage(LOGTAG, "PartitionCreationFailed", ex,
                        $"Failed to create the partition on the target disk: {ex.Message}");
                    throw;
                }
            }
        }

        // Restore partition-level items
        if (partitionItems.Count > 0)
        {
            // Currently a NOP operation. If a partition needs to restore
            // specific data during restore, it's here.
            processedCount += partitionItems.Count;
            progressCallback?.Invoke(processedCount / (double)totalItems);
        }

        // Cleanup
        foreach (var pendingWrite in _pendingWrites.Values)
            pendingWrite.Dispose();
        _pendingWrites.Clear();

        Log.WriteInformationMessage(LOGTAG, "RestoreComplete", "Restore operation completed.");
    }

    /// <summary>
    /// Normalizes the given path.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The normalized path.</returns>
    private string NormalizePath(string path)
    {
        // Remove any leading/trailing separators and normalize
        return path.TrimStart('/', '\\').TrimEnd('/', '\\');
    }

    /// <summary>
    /// Disposes the restore provider.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _targetDisk?.Dispose();

        foreach (var pendingWrite in _pendingWrites.Values)
            pendingWrite.Dispose();
        _pendingWrites.Clear();

        _disposed = true;
    }

    /// <summary>
    /// Reconstructs IRawDisk, IPartitionTable, IPartition, and IFilesystem objects
    /// from the geometry metadata. This is called when geometry.json is written during restore.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if geometry metadata or target disk is not available.</exception>
    private void ReconstructFromGeometryMetadata()
    {
        if (_geometryMetadata == null)
            throw new InvalidOperationException("Geometry metadata is not available for reconstruction.");

        if (_targetDisk == null)
            throw new InvalidOperationException("Target disk is not initialized.");

        // Create reconstructed partition table based on metadata
        IPartitionTable? partitionTable = null;
        if (_geometryMetadata.PartitionTable != null)
        {
            partitionTable = _geometryMetadata.PartitionTable.Type switch
            {
                PartitionTableType.GPT => new ReconstructedPartitionTable(_targetDisk, _geometryMetadata, PartitionTableType.GPT),
                PartitionTableType.MBR => new ReconstructedPartitionTable(_targetDisk, _geometryMetadata, PartitionTableType.MBR),
                PartitionTableType.Unknown => new UnknownPartitionTable(_targetDisk),
                _ => null
            };
        }

        // Reconstruct partitions from metadata
        if (_geometryMetadata.Partitions != null && partitionTable != null)
        {
            foreach (var partGeom in _geometryMetadata.Partitions)
            {
                var partition = new BasePartition
                {
                    PartitionNumber = partGeom.Number,
                    Type = partGeom.Type,
                    PartitionTable = partitionTable,
                    StartOffset = partGeom.StartOffset,
                    Size = partGeom.Size,
                    Name = partGeom.Name,
                    FilesystemType = partGeom.FilesystemType,
                    VolumeGuid = partGeom.VolumeGuid,
                    RawDisk = _targetDisk,
                    StartingLba = 0,
                    EndingLba = 0,
                    Attributes = 0
                };
                _partitions.Add(partition);
            }
        }

        // Reconstruct filesystems from metadata
        if (_geometryMetadata.Filesystems != null)
        {
            foreach (var fsGeom in _geometryMetadata.Filesystems)
            {
                // Find the corresponding partition for this filesystem
                var partition = _partitions.FirstOrDefault(p => p.PartitionNumber == fsGeom.PartitionNumber);
                if (partition != null)
                {
                    var filesystem = CreateFilesystemFromGeometry(partition, fsGeom);
                    if (filesystem != null)
                        _filesystems.Add(filesystem);
                }
            }
        }
    }

    /// <summary>
    /// Creates an IFilesystem instance from filesystem geometry metadata.
    /// </summary>
    /// <param name="partition">The partition to create the filesystem for.</param>
    /// <param name="fsGeom">The filesystem geometry metadata.</param>
    /// <returns>An IFilesystem instance, or null if the filesystem type is not supported.</returns>
    private IFilesystem? CreateFilesystemFromGeometry(IPartition partition, FilesystemGeometry fsGeom)
    {
        try
        {
            return fsGeom.Type switch
            {
                FileSystemType.FAT32 => new Fat32Filesystem(partition, fsGeom.BlockSize),
                FileSystemType.NTFS => new NtfsFilesystem(partition, fsGeom.BlockSize),
                _ => new UnknownFilesystem(partition, fsGeom.BlockSize)
            };
        }
        catch
        {
            // If creating the filesystem fails (e.g., invalid boot sector on blank disk),
            // fall back to UnknownFilesystem for raw block access but report the original type
            // so that path lookup works correctly
            return new UnknownFilesystem(partition, fsGeom.BlockSize, fsGeom.Type);
        }
    }

    /// <summary>
    /// A stream that captures the written data when disposed and invokes a callback.
    /// </summary>
    private class CaptureStream : Stream
    {
        private readonly MemoryStream _innerStream;
        private readonly Action<ReadOnlyMemory<byte>> _onCaptured;
        private bool _disposed = false;

        public CaptureStream(MemoryStream innerStream, Action<ReadOnlyMemory<byte>> onCaptured)
        {
            _innerStream = innerStream;
            _onCaptured = onCaptured;
        }

        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => _innerStream.CanWrite;
        public override long Length => _innerStream.Length;
        public override long Position
        {
            get => _innerStream.Position;
            set => _innerStream.Position = value;
        }

        public override void Flush() => _innerStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
        public override void SetLength(long value) => _innerStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Capture the data before disposing
                    _innerStream.Position = 0;
                    var data = _innerStream.GetBuffer().AsMemory(0, (int)_innerStream.Length);
                    _onCaptured(data);
                    _innerStream.Dispose();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
