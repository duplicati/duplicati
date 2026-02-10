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
        if (parts.Length >= 3)
            // Parse partition table type (second part)
            if (Enum.TryParse<PartitionTableType>(parts[1], true, out var ptType))
            {
                // Parse partition number (third part)
                if (int.TryParse(parts[2], out var pn))
                {
                    var partition = _partitions[pn - 1];
                    if (partition.PartitionTable.TableType == ptType)
                        return partition;
                    else
                        throw new InvalidOperationException($"Partition table type mismatch for segment: {segment}. Expected: {partition.PartitionTable.TableType}, Parsed: {ptType}");
                }
                else
                    throw new InvalidOperationException($"Unable to parse partition number from segment: {segment}. Tried {parts[2]}");
            }
            else
                throw new InvalidOperationException($"Unable to parse partition table type from segment: {segment}. Tried {parts[1]}");
        else
            throw new InvalidOperationException($"Unable to parse partition information from segment: {segment}. Expected format: part_{{PartitionTableType}}_{{PartitionNumber}}");
    }

    public IFilesystem ParseFilesystem(string segment)
    {
        // Example segment: "fs_NTFS"
        var parts = segment.Split('_');
        if (parts.Length >= 2)
        {
            // Reconstruct filesystem type from remaining parts (e.g., "fs_Unknown" or "fs_NTFS")
            var fsTypeStr = string.Join('_', parts[1..]);
            if (Enum.TryParse<FileSystemType>(fsTypeStr, true, out var fsType))
            {
                // TODO lookup filesystem information from metadata and return an IFilesystem instance
                var fs = _filesystems.FirstOrDefault(f => f.Type == fsType);
                if (fs != null)
                    return fs;
                else
                    throw new InvalidOperationException($"No matching filesystem found for segment: {segment} with type {fsType}");
            }
            else
                throw new InvalidOperationException($"Unable to parse filesystem type from segment: {segment}. Tried {fsTypeStr}");
        }
        else
            throw new InvalidOperationException($"Unable to parse filesystem information from segment: {segment}. Expected format: fs_{{FileSystemType}}");
    }

    public (string, IPartition?, IFilesystem?) ParsePath(string path)
    {
        // For disk image restore, the path is expected to be in the format:
        // root/part_{PartitionTableType}_{PartitionNumber}/fs_{FileSystemType}/path/to/file
        // We need to parse out the partition and filesystem information from the path for proper handling

        // Normalize path separators
        path = NormalizePath(path);
        if (path == "root/geometry.json")
            return ("geometry", null, null);
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        string? partitionSegment = segments.FirstOrDefault(s => s.StartsWith("part_", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(partitionSegment))
        {
            var partition = ParsePartition(partitionSegment);
            string? filesystemSegment = segments.FirstOrDefault(s => s.StartsWith("fs_", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(filesystemSegment))
            {
                var filesystem = ParseFilesystem(filesystemSegment);
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
            //"disk" => OpenReadDisk(path, cancel),
            //"partition" => OpenReadPartition(path, partition!, cancel),
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
            //"disk" => OpenReadWriteDisk(path, cancel),
            //"partition" => OpenReadWritePartition(path, partition!, cancel),
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
                    Log.WriteInformationMessage(LOGTAG, "GeometryMetadataUpdated", $"Successfully updated geometry metadata during ReadWrite");
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
            //"disk" => OpenReadWriteDisk(path, cancel),
            //"partition" => OpenReadWritePartition(path, partition!, cancel),
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
