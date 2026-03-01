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
        else
            throw new PlatformNotSupportedException(Strings.PlatformNotSupported);

        if (_autoUnmount)
            if (!await _targetDisk.AutoUnmountAsync(cancel).ConfigureAwait(false))
                throw new UserInformationException($"Failed to auto unmount target disk: {_devicePath}. Ensure the disk is not in use and you have sufficient permissions.", "DiskAutoUnmountFailed");

        if (!await _targetDisk.InitializeAsync(enableWrite: true, cancel))
            throw new UserInformationException(string.Format(Strings.RestoreDeviceNotWriteable, _devicePath), "DiskInitializeFailed");

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

        // Find the partition in our reconstructed list
        var partition = _partitions.FirstOrDefault(p =>
            p.PartitionNumber == pn &&
            p.PartitionTable.TableType == ptType);

        if (partition == null)
            throw new InvalidOperationException($"Partition not found for segment: {segment}. Partition number {pn} with table type {ptType} not in reconstructed partitions.");

        return partition;
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
        if (fs == null)
            throw new InvalidOperationException($"No matching filesystem found for segment: {segment} with type {fsType}");

        return fs;
    }

    /// <summary>
    /// Parses the given path to determine if it refers to a disk-level item, partition, or file, and returns the corresponding objects.
    /// The path is expected to be in the format:
    /// root/part_{PartitionTableType}_{PartitionNumber}/fs_{FileSystemType}/path/to/file
    /// </summary>
    /// <param name="path">The path to parse.</param>
    /// <returns>A tuple containing the item type, partition, and filesystem.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the path cannot be parsed.</exception>
    internal (string, IPartition?, IFilesystem?) ParsePath(string path)
    {
        // For disk image restore, the path is expected to be in the format:
        // root/part_{PartitionTableType}_{PartitionNumber}/fs_{FileSystemType}/path/to/file
        // We need to parse out the partition and filesystem information from the path for proper handling

        // Normalize path separators
        path = NormalizePath(path);

        if (IsGeometryFile(path))
            return ("geometry", null, null);

        var segments = path.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries) ??
            throw new InvalidOperationException($"Unable to parse path: {path}");


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
            "disk" => OpenWriteDisk(path, cancel), // For disk-level, we treat read-write as write since we only capture the data to be written during Finalize
            "partition" => Task.FromResult((Stream)new MemoryStream()),
            "geometry" => OpenReadWriteGeometry(cancel),
            "file" => filesystem!.OpenReadWriteStreamAsync(path, cancel),
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
    /// <param name="path">The file path to check.</param>
    /// <returns><c>true</c> if the path is the geometry file; otherwise, <c>false</c>.</returns>
    private static bool IsGeometryFile(string path)
    {
        // Check for geometry.json (must be at root or top level)
        // Valid paths: "geometry.json", "root/geometry.json"
        if (!path.EndsWith("geometry.json", StringComparison.OrdinalIgnoreCase))
            return false;

        // TODO only check the last path for now.
        return true;

        // Normalize path separators
        var normalized = path.Replace('\\', '/').TrimStart('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Should be at most 2 segments (e.g. "root/geometry.json")
        return segments.Length <= 2;
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

        // Restore disk-level items (partition table)
        if (!_skipPartitionTable && diskItems.Count > 0)
        {
            if (_geometryMetadata?.PartitionTable != null)
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
        return fsGeom.Type switch
        {
            // For now, we use UnknownFilesystem as the base implementation
            // Specific filesystem implementations can be added later
            _ => new UnknownFilesystem(partition, fsGeom.BlockSize)
        };
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
