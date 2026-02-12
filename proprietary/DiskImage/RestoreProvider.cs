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
using Duplicati.Proprietary.DiskImage.Partition;

namespace Duplicati.Proprietary.DiskImage;

/// <summary>
/// Restore provider for disk images. Allows restoring disk images back to physical disks.
/// </summary>
public sealed class RestoreProvider : IRestoreDestinationProviderModule, IDisposable
{
    private static readonly string LOGTAG = Log.LogTagFromType<RestoreProvider>();

    private readonly string _devicePath;
    private readonly string _restorePath;
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
        var uri = new Library.Utility.Uri(url);
        _restorePath = uri.HostAndPath;
        _devicePath = uri.HostAndPath;

        _skipPartitionTable = Utility.ParseBoolOption(options, OptionsHelper.DISK_RESTORE_SKIP_PARTITION_TABLE_OPTION);
        _validateSize = Utility.ParseBoolOption(options, OptionsHelper.DISK_RESTORE_VALIDATE_SIZE_OPTION);
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
        if (OperatingSystem.IsWindows())
        {
            if (string.IsNullOrEmpty(_devicePath))
                throw new UserInformationException("Disk device path is not specified.", "DiskDeviceNotSpecified");

            _targetDisk = new Windows(_devicePath);
            if (!await _targetDisk.InitializeAsync(enableWrite: true, cancel))
                throw new UserInformationException(string.Format(Strings.RestoreDeviceNotWriteable, _devicePath), "DiskInitializeFailed");
        }
        else
        {
            throw new PlatformNotSupportedException(Strings.RestorePlatformNotSupported);
        }

        // Validate target size if requested
        if (_validateSize)
        {
            // Size validation will be done during Finalize when we have source metadata
            Log.WriteInformationMessage(LOGTAG, "RestoreSizeValidationEnabled", "Target size validation is enabled.");
        }
    }

    /// <inheritdoc />
    public async Task Test(CancellationToken cancellationToken)
    {
        if (_targetDisk == null)
            throw new InvalidOperationException("Provider not initialized.");

        // TODO Test write access by trying to read disk info (we already opened for write during Initialize)
        Log.WriteInformationMessage(LOGTAG, "RestoreTestSuccess", $"Successfully opened target device: {_devicePath}, Size: {_targetDisk.Size} bytes, SectorSize: {_targetDisk.SectorSize}");
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> CreateFolderIfNotExists(string path, CancellationToken cancel)
    {
        // TODO current disk images don't have folders in the traditional sense
        // The "folders" are virtual representations of disks/partitions/filesystems
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> FileExists(string path, CancellationToken cancel)
    {
        path = NormalizePath(path);

        // TODO query the filesystem to check if the file exists.
        if (_pendingWrites.ContainsKey(path))
            return Task.FromResult(true);

        return Task.FromResult(false);
    }

    public IPartition ParsePartition(string segment)
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

        // Find the partition in our reconstructed list
        var partition = _partitions.FirstOrDefault(p =>
            p.PartitionNumber == pn &&
            p.PartitionTable.TableType == ptType);

        if (partition == null)
            throw new InvalidOperationException($"Partition not found for segment: {segment}. Partition number {pn} with table type {ptType} not in reconstructed partitions.");

        return partition;
    }

    public IFilesystem ParseFilesystem(IPartition partition, string segment)
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
        if (fs == null)
            throw new InvalidOperationException($"No matching filesystem found for segment: {segment} with type {fsType}");

        return fs;
    }

    public (string, IPartition?, IFilesystem?) ParsePath(string path)
    {
        // For disk image restore, the path is expected to be in the format:
        // root/part_{PartitionTableType}_{PartitionNumber}/fs_{FileSystemType}/path/to/file
        // We need to parse out the partition and filesystem information from the path for proper handling

        // Normalize path separators
        path = NormalizePath(path);
        var segments = path.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries) ??
            throw new InvalidOperationException($"Unable to parse path: {path}");
        // TODO also check for root/, but handle that later when the mount path issue is handled.
        if (segments.Length >= 2 && segments[^1] == "geometry.json")
            return ("geometry", null, null);

        string? partitionSegment = segments.FirstOrDefault(s => s.StartsWith("part_", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(partitionSegment))
        {
            var partition = ParsePartition(partitionSegment);
            string? filesystemSegment = segments.FirstOrDefault(s => s.StartsWith("fs_", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(filesystemSegment))
            {
                var filesystem = ParseFilesystem(partition, filesystemSegment);
                return ("file", partition, filesystem);
            }
            return ("partition", partition, null);
        }
        return ("disk", null, null);
    }

    /// <inheritdoc />
    public Task<Stream> OpenWrite(string path, CancellationToken cancel)
    {
        var (typeStr, partition, filesystem) = ParsePath(path);

        return typeStr switch
        {
            "geometry" => OpenWriteGeometry(cancel),
            "disk" => OpenWriteDisk(path, cancel),
            "partition" => OpenWritePartition(path, partition!, cancel),
            "file" => filesystem!.OpenWriteStreamAsync(path, cancel),
            _ => throw new NotSupportedException($"Unsupported item type: {typeStr}")
        };
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
                var json = System.Text.Encoding.UTF8.GetString(data);
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
            "disk" => OpenReadDisk(path, cancel),
            "partition" => OpenReadPartition(path, partition!, cancel),
            "geometry" => OpenReadGeometry(cancel),
            "file" => filesystem!.OpenReadStreamAsync(path, cancel),
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
            "disk" => Task.FromResult((Stream)new MemoryStream()),
            "partition" => Task.FromResult((Stream)new MemoryStream()),
            "geometry" => OpenReadWriteGeometry(cancel),
            "file" => filesystem!.OpenReadWriteStreamAsync(path, cancel),
            _ => throw new NotSupportedException($"Unsupported item type: {typeStr}")
        };
    }

    /// <summary>
    /// Opens a stream for read-write access to geometry metadata.
    /// </summary>
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

        var wrapper = new CaptureStream(stream, data =>
        {
            try
            {
                var json = System.Text.Encoding.UTF8.GetString(data);
                var newGeometry = GeometryMetadata.FromJson(json);
                if (newGeometry != null)
                {
                    _geometryMetadata = newGeometry;

                    // Clear existing reconstructed objects
                    foreach (var part in _partitions)
                        part.Dispose();
                    _partitions.Clear();
                    foreach (var fs in _filesystems)
                        fs.Dispose();
                    _filesystems.Clear();

                    // Reconstruct disk, partition table, partitions, and filesystems from geometry metadata
                    ReconstructFromGeometryMetadata();

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

    public Task<long> GetFileLength(string path, CancellationToken cancel)
    {
        var (typeStr, partition, filesystem) = ParsePath(path);

        return typeStr switch
        {
            "disk" => Task.FromResult(0L),
            "partition" => Task.FromResult(0L),
            "geometry" => Task.FromResult((long)_geometryMetadata!.ToJson().Count()),
            "file" => filesystem!.GetFileLengthAsync(path, cancel),
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
        return ["geometry.json"];
    }

    /// <summary>
    /// Checks if a file path is the geometry metadata file.
    /// </summary>
    private static bool IsGeometryFile(string path)
    {
        return path.EndsWith("geometry.json", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task Finalize(Action<double>? progressCallback, CancellationToken cancel)
    {
        if (_targetDisk == null)
            throw new InvalidOperationException("Provider not initialized.");

        var totalItems = _pendingWrites.Count;
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

        // Restore disk-level items (full disk image)
        if (!_skipPartitionTable && diskItems.Count > 0)
        {
            // TODO currently a NOP operation
            processedCount += diskItems.Count;
            progressCallback?.Invoke(processedCount / (double)totalItems);
        }

        // Restore partition-level items
        if (partitionItems.Count > 0)
        {
            // TODO currently a NOP operation.
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
                PartitionTableType.GPT => new ReconstructedGPT(_targetDisk, _geometryMetadata),
                PartitionTableType.MBR => new ReconstructedMBR(_targetDisk, _geometryMetadata),
                _ => null
            };
        }

        // Reconstruct partitions from metadata
        if (_geometryMetadata.Partitions != null && partitionTable != null)
        {
            foreach (var partGeom in _geometryMetadata.Partitions)
            {
                var partition = new ReconstructedPartition(partitionTable, partGeom, _targetDisk);
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
    private IFilesystem? CreateFilesystemFromGeometry(IPartition partition, FilesystemGeometry fsGeom)
    {
        return fsGeom.Type switch
        {
            // For now, we use UnknownFilesystem as the base implementation
            // Specific filesystem implementations can be added later
            _ => new UnknownFilesystem(partition, fsGeom.BlockSize)
        };
    }

    /// <summary>
    /// A reconstructed GPT partition table for restore operations.
    /// This is a lightweight implementation that stores metadata from the backup.
    /// </summary>
    private class ReconstructedGPT : IPartitionTable
    {
        private readonly IRawDisk _rawDisk;
        private readonly GeometryMetadata _geometry;
        private bool _disposed = false;

        public ReconstructedGPT(IRawDisk rawDisk, GeometryMetadata geometry)
        {
            _rawDisk = rawDisk;
            _geometry = geometry;
        }

        public IRawDisk? RawDisk => _rawDisk;
        public PartitionTableType TableType => PartitionTableType.GPT;

        public IAsyncEnumerable<IPartition> EnumeratePartitions(CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Enumeration not supported on reconstructed partition table.");
        }

        public Task<IPartition?> GetPartitionAsync(int partitionNumber, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("GetPartitionAsync not supported on reconstructed partition table.");
        }

        public Task<Stream> GetProtectiveMbrAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException("GetProtectiveMbrAsync not supported on reconstructed partition table.");
        }

        public Task<Stream> GetPartitionTableDataAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException("GetPartitionTableDataAsync not supported on reconstructed partition table.");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// A reconstructed MBR partition table for restore operations.
    /// This is a lightweight implementation that stores metadata from the backup.
    /// </summary>
    private class ReconstructedMBR : IPartitionTable
    {
        private readonly IRawDisk _rawDisk;
        private readonly GeometryMetadata _geometry;
        private bool _disposed = false;

        public ReconstructedMBR(IRawDisk rawDisk, GeometryMetadata geometry)
        {
            _rawDisk = rawDisk;
            _geometry = geometry;
        }

        public IRawDisk? RawDisk => _rawDisk;
        public PartitionTableType TableType => PartitionTableType.MBR;

        public IAsyncEnumerable<IPartition> EnumeratePartitions(CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Enumeration not supported on reconstructed partition table.");
        }

        public Task<IPartition?> GetPartitionAsync(int partitionNumber, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("GetPartitionAsync not supported on reconstructed partition table.");
        }

        public Task<Stream> GetProtectiveMbrAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException("MBR does not have a protective MBR.");
        }

        public Task<Stream> GetPartitionTableDataAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException("GetPartitionTableDataAsync not supported on reconstructed partition table.");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// A reconstructed partition for restore operations.
    /// This is created from geometry metadata and associated with the target disk.
    /// </summary>
    private class ReconstructedPartition : IPartition
    {
        private readonly IPartitionTable _partitionTable;
        private readonly IRawDisk _rawDisk;
        private bool _disposed = false;

        public ReconstructedPartition(IPartitionTable partitionTable, PartitionGeometry geometry, IRawDisk rawDisk)
        {
            _partitionTable = partitionTable;
            _rawDisk = rawDisk;
            PartitionNumber = geometry.Number;
            Type = geometry.Type;
            StartOffset = geometry.StartOffset;
            Size = geometry.Size;
            Name = geometry.Name;
            FilesystemType = geometry.FilesystemType;
            VolumeGuid = geometry.VolumeGuid;
        }

        public int PartitionNumber { get; }
        public PartitionType Type { get; }
        public IPartitionTable PartitionTable => _partitionTable;
        public long StartOffset { get; }
        public long Size { get; }
        public string? Name { get; }
        public FileSystemType FilesystemType { get; }
        public Guid? VolumeGuid { get; }

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            return _rawDisk.ReadBytesAsync(StartOffset, (int)Math.Min(Size, int.MaxValue), cancellationToken);
        }

        public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<Stream>(new PartitionWriteStream(_rawDisk, StartOffset, Size));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// A stream that writes data to a partition on the raw disk.
    /// </summary>
    private class PartitionWriteStream : Stream
    {
        private readonly IRawDisk _disk;
        private readonly long _startOffset;
        private readonly long _maxSize;
        private readonly MemoryStream _buffer;
        private bool _disposed = false;

        public PartitionWriteStream(IRawDisk disk, long startOffset, long maxSize)
        {
            _disk = disk;
            _startOffset = startOffset;
            _maxSize = maxSize;
            _buffer = new MemoryStream();
        }

        public override bool CanRead => false;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _buffer.Length;
        public override long Position
        {
            get => _buffer.Position;
            set => _buffer.Position = value;
        }

        public override void Flush() => _buffer.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => _buffer.Seek(offset, origin);
        public override void SetLength(long value)
        {
            if (value > _maxSize)
                throw new IOException($"Cannot write beyond partition size of {_maxSize} bytes.");
            _buffer.SetLength(value);
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_buffer.Position + count > _maxSize)
                throw new IOException($"Cannot write beyond partition size of {_maxSize} bytes.");
            _buffer.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Write all buffered data to disk
                    _buffer.Position = 0;
                    var data = _buffer.ToArray();
                    if (data.Length > 0)
                    {
                        _disk.WriteBytesAsync(_startOffset, data, CancellationToken.None).GetAwaiter().GetResult();
                    }
                    _buffer.Dispose();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// A stream that captures the written data when disposed and invokes a callback.
    /// </summary>
    private class CaptureStream : Stream
    {
        private readonly MemoryStream _innerStream;
        private readonly Action<byte[]> _onCaptured;
        private bool _disposed = false;

        public CaptureStream(MemoryStream innerStream, Action<byte[]> onCaptured)
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
                    var data = _innerStream.ToArray();
                    _onCaptured(data);
                    _innerStream.Dispose();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
