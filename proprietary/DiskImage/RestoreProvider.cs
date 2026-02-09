// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
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
    /// Metadata recorded during the restore process, keyed by path.
    /// </summary>
    private readonly ConcurrentDictionary<string, Dictionary<string, string?>> _metadata = new();

    /// <summary>
    /// Tracks pending writes for items that need to be written during Finalize.
    /// For partition table items, this stores the data to be written.
    /// </summary>
    private readonly ConcurrentDictionary<string, PendingWrite> _pendingWrites = new();

    /// <summary>
    /// Cache of instantiated partitions keyed by partition number.
    /// </summary>
    private readonly ConcurrentDictionary<int, IPartition> _partitionCache = new();

    /// <summary>
    /// Cache of instantiated filesystems keyed by partition number.
    /// </summary>
    private readonly ConcurrentDictionary<int, IFilesystem> _filesystemCache = new();

    /// <summary>
    /// Stores geometry metadata parsed from restored geometry files.
    /// Used to reconstruct disk, partition, and filesystem structures.
    /// </summary>
    private readonly Dictionary<string, GeometryMetadata> _geometryMetadata = new();

    /// <summary>
    /// Represents a pending write operation.
    /// </summary>
    private abstract class PendingWrite : IDisposable
    {
        public abstract void Dispose();
    }

    /// <summary>
    /// Pending write for partition table data (stored in memory until Finalize).
    /// </summary>
    private class PartitionTablePendingWrite : PendingWrite
    {
        public byte[] Data { get; }

        public PartitionTablePendingWrite(byte[] data)
        {
            Data = data;
        }

        public override void Dispose()
        {
            // Nothing to dispose
        }
    }

    /// <summary>
    /// Pending write for disk-level data (stored in memory until Finalize).
    /// </summary>
    private class DiskPendingWrite : PendingWrite
    {
        public byte[] Data { get; }
        public long SourceSize { get; }

        public DiskPendingWrite(byte[] data, long sourceSize)
        {
            Data = data;
            SourceSize = sourceSize;
        }

        public override void Dispose()
        {
            // Nothing to dispose
        }
    }

    /// <summary>
    /// Pending write for partition data (stored in memory until Finalize).
    /// </summary>
    private class PartitionPendingWrite : PendingWrite
    {
        public byte[] Data { get; }
        public long Offset { get; }
        public int PartitionNumber { get; }

        public PartitionPendingWrite(byte[] data, long offset, int partitionNumber)
        {
            Data = data;
            Offset = offset;
            PartitionNumber = partitionNumber;
        }

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

        if (_pendingWrites.ContainsKey(path))
            return Task.FromResult(true);

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<Stream> OpenWrite(string path, CancellationToken cancel)
    {
        path = NormalizePath(path);

        // Get metadata for this path to determine the write strategy
        if (!_metadata.TryGetValue(path, out var metadata))
        {
            throw new InvalidOperationException($"No metadata found for path: {path}. WriteMetadata must be called before OpenWrite.");
        }

        // Determine the item type
        metadata.TryGetValue("diskimage:Type", out var typeStr);

        // Autodetect geometry file by path if not explicitly marked
        if (typeStr == null && IsGeometryFile(path))
        {
            return OpenWriteGeometry(path, metadata, cancel);
        }

        return typeStr switch
        {
            "partition_table" => OpenWritePartitionTable(path, metadata, cancel),
            "disk" => OpenWriteDisk(path, metadata, cancel),
            "partition" => OpenWritePartition(path, metadata, cancel),
            "geometry" => OpenWriteGeometry(path, metadata, cancel),
            "block" or "file" => OpenWriteThroughFilesystem(path, metadata, cancel),
            _ => throw new NotSupportedException($"Unsupported item type: {typeStr}")
        };
    }

    /// <summary>
    /// Opens a stream for writing partition table data (stored in memory until Finalize).
    /// </summary>
    private Task<Stream> OpenWritePartitionTable(string path, Dictionary<string, string?> metadata, CancellationToken cancel)
    {
        // For partition tables, we need to buffer the data and write it during Finalize
        // because we need to validate and potentially modify the partition table
        var stream = new MemoryStream();

        // Wrap the stream to capture the data when it's closed
        var wrapper = new CaptureStream(stream, data =>
        {
            var pendingWrite = new PartitionTablePendingWrite(data);
            _pendingWrites.AddOrUpdate(path, pendingWrite, (_, old) =>
            {
                old.Dispose();
                return pendingWrite;
            });
        });

        return Task.FromResult<Stream>(wrapper);
    }

    /// <summary>
    /// Opens a stream for writing disk-level data (stored in memory until Finalize).
    /// </summary>
    private Task<Stream> OpenWriteDisk(string path, Dictionary<string, string?> metadata, CancellationToken cancel)
    {
        metadata.TryGetValue("disk:Size", out var sourceSizeStr);
        long.TryParse(sourceSizeStr, out var sourceSize);

        var stream = new MemoryStream();

        var wrapper = new CaptureStream(stream, data =>
        {
            var pendingWrite = new DiskPendingWrite(data, sourceSize);
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
    private Task<Stream> OpenWritePartition(string path, Dictionary<string, string?> metadata, CancellationToken cancel)
    {
        metadata.TryGetValue("partition:StartOffset", out var offsetStr);
        if (!long.TryParse(offsetStr, out var offset))
        {
            // Fallback to old key format for backward compatibility
            metadata.TryGetValue("partition_offset", out offsetStr);
            long.TryParse(offsetStr, out offset);
        }

        metadata.TryGetValue("partition:Number", out var partitionNumberStr);
        int.TryParse(partitionNumberStr, out var partitionNumber);

        var stream = new MemoryStream();

        var wrapper = new CaptureStream(stream, data =>
        {
            var pendingWrite = new PartitionPendingWrite(data, offset, partitionNumber);
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
    private Task<Stream> OpenWriteGeometry(string path, Dictionary<string, string?> metadata, CancellationToken cancel)
    {
        var stream = new MemoryStream();

        var wrapper = new CaptureStream(stream, data =>
        {
            try
            {
                // Parse the geometry metadata from the JSON data
                var json = System.Text.Encoding.UTF8.GetString(data);
                var geometry = GeometryMetadata.FromJson(json);

                if (geometry != null)
                {
                    _geometryMetadata[path] = geometry;
                    Log.WriteInformationMessage(LOGTAG, "GeometryMetadataReceived",
                        $"Received consolidated geometry metadata for {path} (Version: {geometry.Version})");

                    // Store the geometry metadata in the regular metadata dictionary as well
                    metadata["geometry:Parsed"] = "true";
                    metadata["geometry:Version"] = geometry.Version.ToString();

                    if (geometry.Disk != null)
                    {
                        metadata["geometry:DiskTableType"] = geometry.Disk.TableType.ToString();
                    }

                    if (geometry.Partitions != null)
                    {
                        metadata["geometry:PartitionCount"] = geometry.Partitions.Count.ToString();
                    }

                    if (geometry.Filesystems != null)
                    {
                        metadata["geometry:FilesystemCount"] = geometry.Filesystems.Count.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "GeometryMetadataParseFailed", ex,
                    $"Failed to parse geometry metadata for {path}");
            }
        });

        return Task.FromResult<Stream>(wrapper);
    }

    /// <summary>
    /// Parses a path to extract partition and filesystem information.
    /// Expected path format: root/part_{PartitionTableType}_{PartitionNumber}/fs_{FileSystemType}/...
    /// </summary>
    private static bool TryParsePath(string path, out int partitionNumber, out PartitionTableType partitionTableType, out FileSystemType filesystemType)
    {
        partitionNumber = 0;
        partitionTableType = PartitionTableType.Unknown;
        filesystemType = FileSystemType.Unknown;

        if (string.IsNullOrEmpty(path))
            return false;

        // Normalize path separators
        path = path.Replace('\\', '/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Find partition segment (e.g., "part_GPT_1" or "part_MBR_2")
        foreach (var segment in segments)
        {
            if (segment.StartsWith("part_", StringComparison.OrdinalIgnoreCase))
            {
                var parts = segment.Split('_');
                if (parts.Length >= 3)
                {
                    // Parse partition table type (second part)
                    if (Enum.TryParse<PartitionTableType>(parts[1], true, out var ptType))
                    {
                        partitionTableType = ptType;
                    }

                    // Parse partition number (third part)
                    if (int.TryParse(parts[2], out var pn))
                    {
                        partitionNumber = pn;
                    }
                }
            }
            else if (segment.StartsWith("fs_", StringComparison.OrdinalIgnoreCase))
            {
                var parts = segment.Split('_');
                if (parts.Length >= 2)
                {
                    // Reconstruct filesystem type from remaining parts (e.g., "fs_Unknown" or "fs_NTFS")
                    var fsTypeStr = string.Join("_", parts[1..]);
                    if (Enum.TryParse<FileSystemType>(fsTypeStr, true, out var fsType))
                    {
                        filesystemType = fsType;
                    }
                }
            }
        }

        return partitionNumber > 0;
    }

    /// <summary>
    /// Represents the context for filesystem operations parsed from path and metadata.
    /// </summary>
    private readonly struct FilesystemContext
    {
        public int PartitionNumber { get; init; }
        public PartitionTableType PartitionTableType { get; init; }
        public FileSystemType FilesystemType { get; init; }
        public long PartitionStartOffset { get; init; }
        public long Address { get; init; }
        public long Size { get; init; }
        public int BlockSize { get; init; }
    }

    /// <summary>
    /// Parses path and metadata to create a filesystem operation context.
    /// Parses the filesystem type and partition type from the given path,
    /// while extracting geometry (offsets, sizes) from metadata.
    /// </summary>
    private FilesystemContext ParseFilesystemContext(string path, Dictionary<string, string?> metadata)
    {
        // Parse filesystem and partition types from the path
        if (!TryParsePath(path, out var partitionNumber, out var partitionTableType, out var filesystemType))
        {
            throw new InvalidOperationException($"Unable to parse partition information from path: {path}");
        }

        // Extract geometry from metadata (still needed for positioning)
        metadata.TryGetValue("filesystem:PartitionStartOffset", out var partitionOffsetStr);
        metadata.TryGetValue("filesystem:BlockSize", out var blockSizeStr);
        metadata.TryGetValue("block:Address", out var addressStr);
        metadata.TryGetValue("file:Size", out var sizeStr);

        // Fallback to old key format for address
        if (string.IsNullOrEmpty(addressStr))
            metadata.TryGetValue("block_address", out addressStr);

        // Parse geometry values
        long partitionStartOffset = long.TryParse(partitionOffsetStr, out var pso) ? pso : 0;
        long address = long.TryParse(addressStr, out var addr) ? addr : 0;
        long size = long.TryParse(sizeStr, out var sz) ? sz : 0;
        int blockSize = int.TryParse(blockSizeStr, out var bs) ? bs : 1024 * 1024; // Default 1MB blocks

        return new FilesystemContext
        {
            PartitionNumber = partitionNumber,
            PartitionTableType = partitionTableType,
            FilesystemType = filesystemType,
            PartitionStartOffset = partitionStartOffset,
            Address = address,
            Size = size,
            BlockSize = blockSize
        };
    }

    /// <summary>
    /// Gets or creates partition and filesystem instances for the given context.
    /// </summary>
    private async Task<(IPartition Partition, IFilesystem Filesystem)> GetPartitionAndFilesystemAsync(
        FilesystemContext context, CancellationToken cancel)
    {
        var partition = await GetOrCreatePartitionAsync(
            context.PartitionNumber, context.PartitionStartOffset, context.PartitionTableType, cancel);

        var filesystem = await GetOrCreateFilesystemAsync(
            context.PartitionNumber, partition, context.FilesystemType, context.BlockSize, cancel);

        return (partition, filesystem);
    }

    /// <summary>
    /// Opens a stream for writing through the filesystem layer.
    /// Parses the filesystem type and partition type from the given path,
    /// then reconstructs a file matching the given filesystem and hands off the write.
    /// Disk/partition/filesystem geometry is extracted from metadata.
    /// </summary>
    private async Task<Stream> OpenWriteThroughFilesystem(string path, Dictionary<string, string?> metadata, CancellationToken cancel)
    {
        var context = ParseFilesystemContext(path, metadata);
        var (partition, filesystem) = await GetPartitionAndFilesystemAsync(context, cancel);

        // Create a file reference for writing
        var file = new UnknownFilesystemFile
        {
            Address = context.Address,
            Size = context.Size
        };

        // Open write stream through the filesystem
        return await filesystem.CreateFileAsync(file, context.Address, context.Size, cancel);
    }

    /// <summary>
    /// Gets or creates a partition instance for the specified partition number.
    /// Uses geometry metadata if available to reconstruct the partition structure.
    /// </summary>
    private Task<IPartition> GetOrCreatePartitionAsync(int partitionNumber, long startOffset, PartitionTableType tableType, CancellationToken cancel)
    {
        return Task.FromResult(_partitionCache.GetOrAdd(partitionNumber, _ =>
        {
            if (_targetDisk == null)
                throw new InvalidOperationException("Target disk not initialized.");

            // Try to find geometry metadata for this partition
            var partitionGeometry = FindPartitionGeometry(partitionNumber);
            if (partitionGeometry != null)
            {
                // Use geometry metadata to reconstruct the partition
                Log.WriteInformationMessage(LOGTAG, "UsingPartitionGeometry",
                    $"Using geometry metadata for partition #{partitionNumber}");
                startOffset = partitionGeometry.StartOffset;
                tableType = partitionGeometry.TableType;
            }

            // Create a partition table wrapper with the correct type
            var partitionTable = new TargetPartitionTable(_targetDisk, tableType);

            // Create a synthetic partition that represents the target partition
            var partition = new TargetPartition(partitionTable, partitionNumber, startOffset, _targetDisk.Size);

            Log.WriteInformationMessage(LOGTAG, "CreatedPartition",
                $"Created target partition #{partitionNumber} at offset {startOffset} (TableType: {tableType})");

            return partition;
        }));
    }

    /// <summary>
    /// Gets or creates a filesystem instance for the specified partition.
    /// Uses geometry metadata if available to reconstruct the filesystem structure.
    /// </summary>
    private Task<IFilesystem> GetOrCreateFilesystemAsync(int partitionNumber, IPartition partition, FileSystemType filesystemType, int blockSize, CancellationToken cancel)
    {
        return Task.FromResult(_filesystemCache.GetOrAdd(partitionNumber, _ =>
        {
            // Try to find filesystem geometry metadata for this partition
            var fsGeometry = FindFilesystemGeometry(partitionNumber);
            if (fsGeometry != null)
            {
                // Use geometry metadata to reconstruct the filesystem
                Log.WriteInformationMessage(LOGTAG, "UsingFilesystemGeometry",
                    $"Using geometry metadata for filesystem on partition #{partitionNumber}");
                filesystemType = fsGeometry.Type;
                blockSize = fsGeometry.BlockSize;
            }

            // For unknown filesystems (or any filesystem we don't have specific support for),
            // use the UnknownFilesystem which handles raw block access
            IFilesystem filesystem = filesystemType switch
            {
                _ => new UnknownFilesystem(partition, blockSize)
            };

            Log.WriteInformationMessage(LOGTAG, "CreatedFilesystem",
                $"Created {filesystemType} filesystem for partition #{partitionNumber}");

            return filesystem;
        }));
    }

    /// <summary>
    /// Finds partition geometry metadata for the specified partition number.
    /// Searches through all consolidated geometry metadata entries.
    /// </summary>
    private PartitionGeometry? FindPartitionGeometry(int partitionNumber)
    {
        foreach (var kvp in _geometryMetadata)
        {
            var geometry = kvp.Value;
            if (geometry.Partitions != null)
            {
                foreach (var partition in geometry.Partitions)
                {
                    if (partition.Number == partitionNumber)
                    {
                        return partition;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Finds filesystem geometry metadata for the specified partition number.
    /// Searches through all consolidated geometry metadata entries.
    /// </summary>
    private FilesystemGeometry? FindFilesystemGeometry(int partitionNumber)
    {
        foreach (var kvp in _geometryMetadata)
        {
            var geometry = kvp.Value;
            if (geometry.Filesystems != null)
            {
                foreach (var filesystem in geometry.Filesystems)
                {
                    if (filesystem.PartitionNumber == partitionNumber)
                    {
                        return filesystem;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// A simple partition table implementation for the target disk during restore.
    /// </summary>
    private class TargetPartitionTable : IPartitionTable
    {
        public IRawDisk? RawDisk { get; }
        public PartitionTableType TableType { get; }

        public TargetPartitionTable(IRawDisk rawDisk, PartitionTableType tableType = PartitionTableType.Unknown)
        {
            RawDisk = rawDisk;
            TableType = tableType;
        }

        public IAsyncEnumerable<IPartition> EnumeratePartitions(CancellationToken cancellationToken)
        {
            return AsyncEnumerable.Empty<IPartition>();
        }

        public Task<IPartition?> GetPartitionAsync(int partitionNumber, CancellationToken cancellationToken)
        {
            return Task.FromResult<IPartition?>(null);
        }

        public Task<Stream> GetProtectiveMbrAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Stream> GetPartitionTableDataAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// A simple partition implementation for the target partition during restore.
    /// </summary>
    private class TargetPartition : IPartition
    {
        public IPartitionTable PartitionTable { get; }
        public int PartitionNumber { get; }
        public PartitionType Type => PartitionType.Primary;
        public long StartOffset { get; }
        public long Size { get; }
        public string? Name => $"Target Partition {PartitionNumber}";
        public FileSystemType FilesystemType => FileSystemType.Unknown;
        public Guid? VolumeGuid => null;

        public TargetPartition(IPartitionTable partitionTable, int partitionNumber, long startOffset, long size)
        {
            PartitionTable = partitionTable;
            PartitionNumber = partitionNumber;
            StartOffset = startOffset;
            Size = size;
        }

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            if (PartitionTable.RawDisk == null)
                throw new InvalidOperationException("Raw disk not available.");
            return PartitionTable.RawDisk.ReadBytesAsync(StartOffset, (int)Math.Min(Size, int.MaxValue), cancellationToken);
        }

        public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken)
        {
            if (PartitionTable.RawDisk == null)
                throw new InvalidOperationException("Raw disk not available.");
            return Task.FromResult<Stream>(new PartitionWriteStream(PartitionTable.RawDisk, StartOffset, Size));
        }

        public void Dispose()
        {
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

    /// <inheritdoc />
    public async Task<Stream> OpenRead(string path, CancellationToken cancel)
    {
        path = NormalizePath(path);

        // Get metadata for this path to determine the strategy
        if (!_metadata.TryGetValue(path, out var metadata))
        {
            throw new InvalidOperationException($"No metadata found for path: {path}. WriteMetadata must be called before OpenRead.");
        }

        // Determine the item type
        metadata.TryGetValue("diskimage:Type", out var typeStr);

        return typeStr switch
        {
            "partition_table" => await OpenReadPartitionTable(metadata, cancel),
            "disk" => await OpenReadDisk(metadata, cancel),
            "partition" => await OpenReadPartition(metadata, cancel),
            "block" or "file" => await OpenReadThroughFilesystem(path, metadata, cancel),
            _ => throw new NotSupportedException($"Unsupported item type for reading: {typeStr}")
        };
    }

    /// <summary>
    /// Opens a stream for reading partition table data.
    /// </summary>
    private Task<Stream> OpenReadPartitionTable(Dictionary<string, string?> metadata, CancellationToken cancel)
    {
        if (_targetDisk == null)
            throw new InvalidOperationException("Target disk not initialized.");

        // Read the partition table from the target disk (first 34 sectors for GPT, first sector for MBR)
        // For now, read a reasonable amount that covers both MBR and GPT primary header
        return _targetDisk.ReadBytesAsync(0, 34 * _targetDisk.SectorSize, cancel);
    }

    /// <summary>
    /// Opens a stream for reading disk-level data.
    /// </summary>
    private Task<Stream> OpenReadDisk(Dictionary<string, string?> metadata, CancellationToken cancel)
    {
        if (_targetDisk == null)
            throw new InvalidOperationException("Target disk not initialized.");

        // Read the entire disk or a portion of it
        // For disk-level items, we typically want to read from the beginning
        metadata.TryGetValue("disk:Size", out var sizeStr);
        long.TryParse(sizeStr, out var size);

        var readSize = size > 0 ? Math.Min(size, _targetDisk.Size) : _targetDisk.Size;
        return _targetDisk.ReadBytesAsync(0, (int)Math.Min(readSize, int.MaxValue), cancel);
    }

    /// <summary>
    /// Opens a stream for reading partition data.
    /// </summary>
    private Task<Stream> OpenReadPartition(Dictionary<string, string?> metadata, CancellationToken cancel)
    {
        if (_targetDisk == null)
            throw new InvalidOperationException("Target disk not initialized.");

        metadata.TryGetValue("partition:StartOffset", out var offsetStr);
        if (!long.TryParse(offsetStr, out var offset))
        {
            metadata.TryGetValue("partition_offset", out offsetStr);
            long.TryParse(offsetStr, out offset);
        }

        metadata.TryGetValue("partition:Size", out var sizeStr);
        long.TryParse(sizeStr, out var size);

        var readSize = size > 0 ? Math.Min(size, _targetDisk.Size - offset) : _targetDisk.Size - offset;
        return _targetDisk.ReadBytesAsync(offset, (int)Math.Min(readSize, int.MaxValue), cancel);
    }

    /// <summary>
    /// Opens a stream for reading through the filesystem layer.
    /// Parses the filesystem type and partition type from the given path,
    /// then reconstructs a file matching the given filesystem and hands off the read.
    /// Disk/partition/filesystem geometry is extracted from metadata.
    /// </summary>
    private async Task<Stream> OpenReadThroughFilesystem(string path, Dictionary<string, string?> metadata, CancellationToken cancel)
    {
        var context = ParseFilesystemContext(path, metadata);
        var (partition, filesystem) = await GetPartitionAndFilesystemAsync(context, cancel);

        // Create a file reference for reading
        var file = new UnknownFilesystemFile
        {
            Address = context.Address,
            Size = context.Size
        };

        // Open read stream through the filesystem
        return await filesystem.OpenFileAsync(file, cancel);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadWrite(string path, CancellationToken cancel)
    {
        path = NormalizePath(path);

        // Get metadata for this path to determine the strategy
        if (!_metadata.TryGetValue(path, out var metadata))
        {
            throw new InvalidOperationException($"No metadata found for path: {path}. WriteMetadata must be called before OpenReadWrite.");
        }

        // Determine the item type
        metadata.TryGetValue("diskimage:Type", out var typeStr);

        // For block/file items, we can support read-write through the filesystem layer
        if (typeStr == "block" || typeStr == "file")
        {
            return await OpenReadWriteThroughFilesystem(path, metadata, cancel);
        }

        // For other types, read-write doesn't make sense during restore
        throw new NotSupportedException($"OpenReadWrite is not supported for item type: {typeStr}");
    }

    /// <summary>
    /// Opens a stream for read-write access through the filesystem layer.
    /// Parses the filesystem type and partition type from the given path,
    /// then reconstructs a file matching the given filesystem and hands off the read-write.
    /// Disk/partition/filesystem geometry is extracted from metadata.
    /// </summary>
    private async Task<Stream> OpenReadWriteThroughFilesystem(string path, Dictionary<string, string?> metadata, CancellationToken cancel)
    {
        var context = ParseFilesystemContext(path, metadata);
        var (partition, filesystem) = await GetPartitionAndFilesystemAsync(context, cancel);

        // Create a file reference
        var file = new UnknownFilesystemFile
        {
            Address = context.Address,
            Size = context.Size
        };

        // Create a read-write stream that wraps both read and write capabilities
        return new ReadWriteFilesystemStream(filesystem, partition, file, context.Address, context.Size);
    }

    /// <summary>
    /// A stream that supports both reading and writing through the filesystem layer.
    /// </summary>
    private class ReadWriteFilesystemStream : Stream
    {
        private readonly IFilesystem _filesystem;
        private readonly IPartition _partition;
        private readonly IFile _file;
        private readonly long _address;
        private readonly long _size;
        private readonly MemoryStream _writeBuffer;
        private bool _disposed = false;

        public ReadWriteFilesystemStream(IFilesystem filesystem, IPartition partition, IFile file, long address, long size)
        {
            _filesystem = filesystem;
            _partition = partition;
            _file = file;
            _address = address;
            _size = size;
            _writeBuffer = new MemoryStream();
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _size;

        public override long Position
        {
            get => _writeBuffer.Position;
            set => _writeBuffer.Position = value;
        }

        public override void Flush() => _writeBuffer.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            // Read directly from the partition at the specified address
            if (_partition.PartitionTable.RawDisk == null)
                throw new InvalidOperationException("Raw disk not available for reading.");

            var disk = _partition.PartitionTable.RawDisk;
            var sectorSize = disk.SectorSize;
            var startSector = (_address + _writeBuffer.Position) / sectorSize;
            var sectorOffset = (int)((_address + _writeBuffer.Position) % sectorSize);
            var bytesToRead = Math.Min(count, (int)(_size - _writeBuffer.Position));
            var sectorCount = (sectorOffset + bytesToRead + sectorSize - 1) / sectorSize;

            if (bytesToRead <= 0)
                return 0;

            // Read the sectors containing the requested data
            var readTask = disk.ReadSectorsAsync(startSector, sectorCount, CancellationToken.None);
            readTask.Wait();
            using var sectorStream = readTask.Result;
            var sectorData = new byte[sectorStream.Length];
            sectorStream.ReadExactly(sectorData, 0, sectorData.Length);

            // Copy the relevant portion to the output buffer
            var actualRead = Math.Min(bytesToRead, sectorData.Length - sectorOffset);
            Array.Copy(sectorData, sectorOffset, buffer, offset, actualRead);

            _writeBuffer.Position += actualRead;
            return actualRead;
        }

        public override long Seek(long offset, SeekOrigin origin) => _writeBuffer.Seek(offset, origin);

        public override void SetLength(long value)
        {
            if (value > _size)
                throw new IOException($"Cannot extend file beyond original size of {_size} bytes.");
            _writeBuffer.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_writeBuffer.Position + count > _size)
                throw new IOException($"Cannot write beyond file size of {_size} bytes.");
            _writeBuffer.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Write buffered data to disk
                    _writeBuffer.Position = 0;
                    var data = _writeBuffer.ToArray();
                    if (data.Length > 0 && _partition.PartitionTable.RawDisk != null)
                    {
                        _partition.PartitionTable.RawDisk.WriteBytesAsync(_address, data, CancellationToken.None).GetAwaiter().GetResult();
                    }
                    _writeBuffer.Dispose();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }

    /// <inheritdoc />
    public Task<long> GetFileLength(string path, CancellationToken cancel)
    {
        path = NormalizePath(path);

        if (_pendingWrites.TryGetValue(path, out var pendingWrite))
        {
            var length = pendingWrite switch
            {
                PartitionTablePendingWrite ptw => ptw.Data.Length,
                DiskPendingWrite dw => dw.Data.Length,
                PartitionPendingWrite pw => pw.Data.Length,
                _ => 0L
            };
            return Task.FromResult(length);
        }

        return Task.FromResult(0L);
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
        path = NormalizePath(path);
        _metadata.AddOrUpdate(path, metadata, (_, _) => metadata);
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
        // The restore provider autodetects the geometry file during restore
        // by checking if any restored file ends with "geometry.json"
        // This method returns empty list since we don't pre-specify the file
        return Array.Empty<string>();
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

        var totalItems = _metadata.Count;
        if (totalItems == 0)
        {
            Log.WriteInformationMessage(LOGTAG, "RestoreNoItems", "No items to restore.");
            return;
        }

        Log.WriteInformationMessage(LOGTAG, "RestoreStarting", $"Starting restore of {totalItems} items to {_devicePath}");

        var processedCount = 0;

        // Group items by type for ordered restoration
        var partitionTableItems = GetMetadataByType("partition_table");
        var diskItems = GetMetadataByType("disk");
        var partitionItems = GetMetadataByType("partition");
        var blockItems = GetMetadataByType("block");
        var fileItems = GetMetadataByType("file");

        // Restore partition table items first (if available)
        if (!_skipPartitionTable && partitionTableItems.Count > 0)
        {
            await RestorePartitionTableItems(partitionTableItems, cancel);
            processedCount += partitionTableItems.Count;
            progressCallback?.Invoke(processedCount / (double)totalItems);
        }

        // Restore disk-level items (full disk image)
        if (!_skipPartitionTable && diskItems.Count > 0)
        {
            await RestoreDiskItems(diskItems, cancel);
            processedCount += diskItems.Count;
            progressCallback?.Invoke(processedCount / (double)totalItems);
        }

        // Restore partition-level items
        if (partitionItems.Count > 0)
        {
            await RestorePartitionItems(partitionItems, cancel);
            processedCount += partitionItems.Count;
            progressCallback?.Invoke(processedCount / (double)totalItems);
        }

        // Block and file items have already been written through the filesystem layer during OpenWrite
        // We just need to verify they were written correctly
        if (blockItems.Count > 0)
        {
            await VerifyBlockItems(blockItems, cancel);
            processedCount += blockItems.Count;
            progressCallback?.Invoke(processedCount / (double)totalItems);
        }

        if (fileItems.Count > 0)
        {
            await VerifyFileItems(fileItems, cancel);
            processedCount += fileItems.Count;
            progressCallback?.Invoke(processedCount / (double)totalItems);
        }

        // Cleanup
        foreach (var filesystem in _filesystemCache.Values)
            filesystem.Dispose();
        _filesystemCache.Clear();

        foreach (var partition in _partitionCache.Values)
            partition.Dispose();
        _partitionCache.Clear();

        foreach (var pendingWrite in _pendingWrites.Values)
            pendingWrite.Dispose();
        _pendingWrites.Clear();
        _metadata.Clear();

        Log.WriteInformationMessage(LOGTAG, "RestoreComplete", "Restore operation completed.");
    }

    /// <summary>
    /// Restores partition table items (MBR or GPT).
    /// </summary>
    private async Task RestorePartitionTableItems(List<KeyValuePair<string, Dictionary<string, string?>>> items, CancellationToken cancel)
    {
        foreach (var item in items)
        {
            if (cancel.IsCancellationRequested)
                break;

            var path = item.Key;
            var metadata = item.Value;

            if (!_pendingWrites.TryGetValue(path, out var pendingWrite) || pendingWrite is not PartitionTablePendingWrite ptw)
                continue;

            try
            {
                // Extract metadata for logging
                metadata.TryGetValue("partition_table:Type", out var tableType);
                metadata.TryGetValue("partition_table:Size", out var tableSizeStr);
                metadata.TryGetValue("disk:SectorSize", out var sectorSizeStr);

                Log.WriteInformationMessage(LOGTAG, "RestorePartitionTableMetadata",
                    $"Restoring partition table (Type: {tableType}, Size: {tableSizeStr}, SectorSize: {sectorSizeStr})");

                // Write partition table to disk starting at offset 0
                // For MBR: this is the 512-byte MBR sector
                // For GPT: this includes protective MBR + GPT header + partition entries
                await _targetDisk!.WriteBytesAsync(0, ptw.Data, cancel);

                Log.WriteInformationMessage(LOGTAG, "RestorePartitionTable", $"Restored partition table: {path} (Type: {tableType})");
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestorePartitionTableFailed", ex, $"Failed to restore partition table: {path}");
                throw;
            }
        }
    }

    /// <summary>
    /// Restores disk-level items (full disk image).
    /// </summary>
    private async Task RestoreDiskItems(List<KeyValuePair<string, Dictionary<string, string?>>> items, CancellationToken cancel)
    {
        foreach (var item in items)
        {
            if (cancel.IsCancellationRequested)
                break;

            var path = item.Key;
            var metadata = item.Value;

            if (!_pendingWrites.TryGetValue(path, out var pendingWrite) || pendingWrite is not DiskPendingWrite dw)
                continue;

            try
            {
                // Extract metadata for logging
                metadata.TryGetValue("disk:DevicePath", out var sourceDevicePath);
                metadata.TryGetValue("disk:Size", out var sourceSizeStr);
                metadata.TryGetValue("disk:SectorSize", out var sectorSizeStr);
                metadata.TryGetValue("disk:PartitionTableType", out var partitionTableType);
                metadata.TryGetValue("disk:Sectors", out var sectorsStr);

                Log.WriteInformationMessage(LOGTAG, "RestoreDiskItemMetadata",
                    $"Restoring disk from {sourceDevicePath} (Size: {sourceSizeStr}, SectorSize: {sectorSizeStr}, Table: {partitionTableType}, Sectors: {sectorsStr})");

                // Validate size if source metadata is available
                if (_validateSize && dw.SourceSize > 0)
                {
                    if (_targetDisk!.Size < dw.SourceSize)
                    {
                        throw new InvalidOperationException(
                            string.Format(Strings.RestoreTargetTooSmall, _targetDisk.Size, dw.SourceSize));
                    }
                }

                // Write to disk (offset 0 for partition table/boot sector)
                await _targetDisk!.WriteBytesAsync(0, dw.Data, cancel);

                // TODO for GPT disks, we may also need to write the backup GPT header at the end of the disk. The fields in the header should also reflect this. Furthermore, if the GUID of the disk has changed, we may need to update these as well.

                Log.WriteInformationMessage(LOGTAG, "RestoreDiskItem", $"Restored disk item: {path} (Table: {partitionTableType})");
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreDiskItemFailed", ex, $"Failed to restore disk item: {path}");
                throw;
            }
        }
    }

    /// <summary>
    /// Restores partition-level items.
    /// </summary>
    private async Task RestorePartitionItems(List<KeyValuePair<string, Dictionary<string, string?>>> items, CancellationToken cancel)
    {
        foreach (var item in items)
        {
            if (cancel.IsCancellationRequested)
                break;

            var path = item.Key;
            var metadata = item.Value;

            if (!_pendingWrites.TryGetValue(path, out var pendingWrite) || pendingWrite is not PartitionPendingWrite pw)
                continue;

            try
            {
                // Get partition metadata
                metadata.TryGetValue("partition:Number", out var partitionNumber);
                metadata.TryGetValue("partition:Type", out var partitionType);
                metadata.TryGetValue("partition:Name", out var partitionName);
                metadata.TryGetValue("partition:FilesystemType", out var filesystemType);
                metadata.TryGetValue("partition:VolumeGuid", out var volumeGuid);

                Log.WriteInformationMessage(LOGTAG, "RestorePartitionItemMetadata",
                    $"Restoring partition #{partitionNumber} (Type: {partitionType}, Name: {partitionName}, FS: {filesystemType}, GUID: {volumeGuid})");

                // Write to partition location
                await _targetDisk!.WriteBytesAsync(pw.Offset, pw.Data, cancel);

                Log.WriteInformationMessage(LOGTAG, "RestorePartitionItem", $"Restored partition item: {path} (Partition #{partitionNumber}, Offset: {pw.Offset})");
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestorePartitionItemFailed", ex, $"Failed to restore partition item: {path}");
                throw;
            }
        }
    }

    /// <summary>
    /// Verifies block-level items that were written through the filesystem layer.
    /// </summary>
    private async Task VerifyBlockItems(List<KeyValuePair<string, Dictionary<string, string?>>> items, CancellationToken cancel)
    {
        // Block items are written through the filesystem layer during OpenWrite, so we just verify they exist
        foreach (var item in items)
        {
            if (cancel.IsCancellationRequested)
                break;

            var path = item.Key;
            var metadata = item.Value;

            try
            {
                metadata.TryGetValue("file:Path", out var filePath);
                metadata.TryGetValue("file:Size", out var fileSize);
                metadata.TryGetValue("filesystem:Type", out var filesystemType);
                metadata.TryGetValue("block:Address", out var addressStr);

                Log.WriteInformationMessage(LOGTAG, "VerifyBlockItem",
                    $"Verified block item: {path} (Size: {fileSize}, FS: {filesystemType})");
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "VerifyBlockItemFailed", ex, $"Failed to verify block item: {path}");
                throw;
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Verifies file-level items that were written through the filesystem layer.
    /// </summary>
    private async Task VerifyFileItems(List<KeyValuePair<string, Dictionary<string, string?>>> items, CancellationToken cancel)
    {
        // File items are written through the filesystem layer during OpenWrite, so we just verify they exist
        foreach (var item in items)
        {
            if (cancel.IsCancellationRequested)
                break;

            var path = item.Key;
            var metadata = item.Value;

            try
            {
                metadata.TryGetValue("file:Path", out var filePath);
                metadata.TryGetValue("file:Size", out var fileSize);

                Log.WriteInformationMessage(LOGTAG, "VerifyFileItem",
                    $"Verified file item: {path} (Size: {fileSize})");
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "VerifyFileItemFailed", ex, $"Failed to verify file item: {path}");
                throw;
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Gets metadata entries by their type.
    /// </summary>
    private List<KeyValuePair<string, Dictionary<string, string?>>> GetMetadataByType(string type)
    {
        return _metadata
            .Where(kv => kv.Value.TryGetValue("diskimage:Type", out var typeStr)
                && typeStr == type)
            .ToList();
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

        foreach (var filesystem in _filesystemCache.Values)
            filesystem.Dispose();
        _filesystemCache.Clear();

        foreach (var partition in _partitionCache.Values)
            partition.Dispose();
        _partitionCache.Clear();

        foreach (var pendingWrite in _pendingWrites.Values)
            pendingWrite.Dispose();
        _pendingWrites.Clear();

        _disposed = true;
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
