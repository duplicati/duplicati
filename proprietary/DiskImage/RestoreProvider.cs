// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

    // Constants for partition table synthesis
    private const int MbrSize = 512;
    private const int GptHeaderSize = 92;
    private const ushort MbrBootSignature = 0xAA55;
    private const byte ProtectiveMbrType = 0xEE;
    private const long GptSignature = 0x5452415020494645; // "EFI PART" in little-endian
    private const uint GptRevision = 0x00010000; // Version 1.0
    private const int PartitionEntrySize = 128; // Standard GPT partition entry size

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

    /// <summary>
    /// Parses a partition segment from the path and returns the corresponding partition.
    /// The segment is expected to be in the format "part_{PartitionTableType}_{PartitionNumber}", e.g. "part_GPT_1".
    /// </summary>
    /// <param name="segment">The partition segment string.</param>
    /// <returns>The corresponding partition.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the partition segment cannot be parsed or the partition is not found.</exception>
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

    /// <summary>
    /// Parses a filesystem segment from the path and returns the corresponding filesystem.
    /// The segment is expected to be in the format "fs_{FileSystemType}", e.g. "fs_NTFS".
    /// </summary>
    /// <param name="partition">The partition to which the filesystem belongs.</param>
    /// <param name="segment">The filesystem segment string.</param>
    /// <returns>The corresponding filesystem.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the filesystem segment cannot be parsed or the filesystem is not found.</exception>
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

    /// <summary>
    /// Parses the given path to determine if it refers to a disk-level item, partition, or file, and returns the corresponding objects.
    /// The path is expected to be in the format:
    /// root/part_{PartitionTableType}_{PartitionNumber}/fs_{FileSystemType}/path/to/file
    /// </summary>
    /// <param name="path">The path to parse.</param>
    /// <returns>A tuple containing the item type, partition, and filesystem.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the path cannot be parsed.</exception>
    public (string, IPartition?, IFilesystem?) ParsePath(string path)
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
                    var partitionTableData = SynthesizePartitionTable(_geometryMetadata);
                    if (partitionTableData != null)
                    {
                        // Write primary partition table at the start of the disk
                        await _targetDisk.WriteBytesAsync(0, partitionTableData, cancel).ConfigureAwait(false);
                        Log.WriteInformationMessage(LOGTAG, "PartitionTableWritten",
                            $"Successfully wrote {_geometryMetadata.PartitionTable.Type} partition table to disk.");

                        // For GPT, also write the secondary GPT header at the end of the disk
                        if (_geometryMetadata.PartitionTable.Type == PartitionTableType.GPT)
                        {
                            await WriteSecondaryGPT(partitionTableData, cancel).ConfigureAwait(false);
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
                PartitionTableType.GPT => new ReconstructedGPT(_targetDisk, _geometryMetadata),
                PartitionTableType.MBR => new ReconstructedMBR(_targetDisk, _geometryMetadata),
                PartitionTableType.Unknown => new UnknownPartitionTable(_targetDisk),
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
    /// Synthesizes a partition table (MBR or GPT) from geometry metadata into a byte array.
    /// Auto-detects whether to create MBR or GPT based on the metadata.
    /// </summary>
    /// <param name="metadata">The geometry metadata containing partition table information.</param>
    /// <returns>A byte array containing the synthesized partition table data.</returns>
    private byte[]? SynthesizePartitionTable(GeometryMetadata metadata)
    {
        if (metadata.PartitionTable == null)
            return null;

        return metadata.PartitionTable.Type switch
        {
            PartitionTableType.MBR => SynthesizeMBR(metadata),
            PartitionTableType.GPT => SynthesizeGPT(metadata),
            _ => null
        };
    }

    /// <summary>
    /// Synthesizes an MBR partition table from geometry metadata.
    /// </summary>
    /// <param name="metadata">The geometry metadata containing partition information.</param>
    /// <returns>A byte array containing the synthesized MBR partition table.</returns>
    private byte[] SynthesizeMBR(GeometryMetadata metadata)
    {
        var sectorSize = metadata.Disk?.SectorSize ?? MbrSize;
        var mbrData = new byte[sectorSize];

        // Boot code area (first 446 bytes) - typically zeros for new MBR
        // Could copy from original if available, but zeros are fine for restore

        // Partition entries start at offset 446
        int partitionEntryOffset = 446;
        int partitionEntrySize = 16;

        if (metadata.Partitions != null)
        {
            // MBR supports up to 4 primary partitions
            var mbrPartitions = metadata.Partitions
                .Where(p => p.TableType == PartitionTableType.MBR)
                .OrderBy(p => p.Number)
                .Take(4)
                .ToList();

            for (int i = 0; i < mbrPartitions.Count && i < 4; i++)
            {
                var part = mbrPartitions[i];
                int offset = partitionEntryOffset + (i * partitionEntrySize);

                WriteMBRPartitionEntry(mbrData, offset, part, sectorSize);
            }
        }

        // Boot signature at offset 510-511 (0xAA55)
        mbrData[510] = 0x55;
        mbrData[511] = 0xAA;

        return mbrData;
    }

    /// <summary>
    /// Writes a single MBR partition entry to the specified offset.
    /// </summary>
    /// <param name="mbrData">The MBR data buffer.</param>
    /// <param name="offset">The offset in the buffer to write the entry.</param>
    /// <param name="part">The partition geometry.</param>
    /// <param name="sectorSize">The sector size in bytes.</param>
    private void WriteMBRPartitionEntry(byte[] mbrData, int offset, PartitionGeometry part, int sectorSize)
    {
        // Status byte (0x80 = bootable, 0x00 = not bootable)
        // Default to not bootable, could be enhanced to detect bootable partitions
        mbrData[offset] = 0x00;

        // CHS start (3 bytes) - use LBA translation or zeros
        // Modern systems use LBA, so we can set these to 0xFF for invalid CHS
        mbrData[offset + 1] = 0xFF;
        mbrData[offset + 2] = 0xFF;
        mbrData[offset + 3] = 0xFF;

        // Partition type byte
        mbrData[offset + 4] = GetMBRPartitionTypeByte(part);

        // CHS end (3 bytes) - use LBA translation or zeros
        mbrData[offset + 5] = 0xFF;
        mbrData[offset + 6] = 0xFF;
        mbrData[offset + 7] = 0xFF;

        // Start LBA (4 bytes, little-endian)
        uint startLba = (uint)(part.StartOffset / sectorSize);
        BitConverter.GetBytes(startLba).CopyTo(mbrData, offset + 8);

        // Size in sectors (4 bytes, little-endian)
        uint sizeInSectors = (uint)(part.Size / sectorSize);
        BitConverter.GetBytes(sizeInSectors).CopyTo(mbrData, offset + 12);
    }

    /// <summary>
    /// Gets the MBR partition type byte based on partition geometry.
    /// </summary>
    /// <param name="part">The partition geometry.</param>
    /// <returns>The MBR partition type byte.</returns>
    private byte GetMBRPartitionTypeByte(PartitionGeometry part)
    {
        // Map partition type and filesystem to MBR type byte
        return part.FilesystemType switch
        {
            FileSystemType.NTFS => 0x07,
            FileSystemType.FAT12 => 0x01,
            FileSystemType.FAT16 => 0x06,
            FileSystemType.FAT32 => 0x0C,  // LBA
            FileSystemType.ExFAT => 0x07,  // Same as NTFS
            _ => part.Type switch
            {
                PartitionType.EFI => 0xEF,
                PartitionType.Extended => 0x0F,
                _ => 0x07  // Default to NTFS/IFS type
            }
        };
    }

    /// <summary>
    /// Synthesizes a GPT partition table from geometry metadata.
    /// </summary>
    /// <param name="metadata">The geometry metadata containing partition information.</param>
    /// <returns>A byte array containing the synthesized GPT partition table.</returns>
    private byte[] SynthesizeGPT(GeometryMetadata metadata)
    {
        var sectorSize = metadata.Disk?.SectorSize ?? MbrSize;
        var diskSize = metadata.Disk?.Size ?? 0;
        var diskSectors = diskSize / sectorSize;

        // Calculate sizes
        int numPartitionEntries = 128;  // Standard GPT supports 128 entries
        int partitionEntriesSize = numPartitionEntries * PartitionEntrySize;
        int partitionEntriesSectors = (partitionEntriesSize + sectorSize - 1) / sectorSize;

        // Total GPT data: Protective MBR (1 sector) + GPT Header (1 sector) + Partition Entries
        int totalGptSectors = 2 + partitionEntriesSectors;
        long totalSize = totalGptSectors * sectorSize;

        var gptData = new byte[totalSize];

        // Write protective MBR at LBA 0
        WriteProtectiveMBR(gptData, metadata, sectorSize, diskSectors);

        // Write GPT header at LBA 1 (sectorSize offset)
        WriteGPTHeader(gptData, metadata, sectorSize, partitionEntriesSectors, numPartitionEntries, diskSectors);

        // Write partition entries starting at LBA 2 (2 * sectorSize offset)
        WriteGPTPartitionEntries(gptData, metadata, sectorSize, partitionEntriesSectors);

        return gptData;
    }

    /// <summary>
    /// Writes the protective MBR for GPT.
    /// </summary>
    /// <param name="gptData">The GPT data buffer.</param>
    /// <param name="metadata">The geometry metadata.</param>
    /// <param name="sectorSize">The sector size in bytes.</param>
    /// <param name="diskSectors">The total number of sectors on the disk.</param>
    private void WriteProtectiveMBR(byte[] gptData, GeometryMetadata metadata, int sectorSize, long diskSectors)
    {
        // Boot code (first 446 bytes) - zeros

        // Partition entry 1 (at offset 446): Protective MBR entry
        // Status byte
        gptData[446] = 0x00;

        // CHS start
        gptData[447] = 0x00;
        gptData[448] = 0x02;
        gptData[449] = 0x00;

        // Partition type: 0xEE (GPT protective)
        gptData[450] = ProtectiveMbrType;

        // CHS end (max values for large disks)
        gptData[451] = 0xFF;
        gptData[452] = 0xFF;
        gptData[453] = 0xFF;

        // Start LBA = 1 (GPT header is at LBA 1)
        BitConverter.GetBytes(1u).CopyTo(gptData, 454);

        // Size in sectors (max 0xFFFFFFFF for protective MBR)
        uint sizeInSectors = diskSectors > uint.MaxValue ? uint.MaxValue : (uint)(diskSectors - 1);
        BitConverter.GetBytes(sizeInSectors).CopyTo(gptData, 458);

        // Boot signature at offset 510-511
        gptData[510] = 0x55;
        gptData[511] = 0xAA;
    }

    /// <summary>
    /// Writes the GPT header.
    /// </summary>
    /// <param name="gptData">The GPT data buffer.</param>
    /// <param name="metadata">The geometry metadata.</param>
    /// <param name="sectorSize">The sector size in bytes.</param>
    /// <param name="partitionEntriesSectors">The number of sectors for partition entries.</param>
    /// <param name="numPartitionEntries">The number of partition entries.</param>
    /// <param name="diskSectors">The total number of sectors on the disk.</param>
    private void WriteGPTHeader(byte[] gptData, GeometryMetadata metadata, int sectorSize,
        int partitionEntriesSectors, int numPartitionEntries, long diskSectors)
    {
        int headerOffset = sectorSize;  // GPT header is at LBA 1

        // Signature: "EFI PART" in little-endian
        BitConverter.GetBytes(GptSignature).CopyTo(gptData, headerOffset + 0);

        // Revision: 1.0 (0x00010000)
        BitConverter.GetBytes(GptRevision).CopyTo(gptData, headerOffset + 8);

        // Header size: 92 bytes
        BitConverter.GetBytes((uint)GptHeaderSize).CopyTo(gptData, headerOffset + 12);

        // CRC32 of header (calculated later) - set to 0 for now
        BitConverter.GetBytes(0u).CopyTo(gptData, headerOffset + 16);

        // Reserved: must be 0
        BitConverter.GetBytes(0u).CopyTo(gptData, headerOffset + 20);

        // Current LBA: 1 (this header is at LBA 1)
        BitConverter.GetBytes((long)1).CopyTo(gptData, headerOffset + 24);

        // Backup LBA: last sector of disk
        long backupLba = diskSectors - 1;
        BitConverter.GetBytes(backupLba).CopyTo(gptData, headerOffset + 32);

        // First usable LBA: after partition entries
        long firstUsableLba = 2 + partitionEntriesSectors;
        BitConverter.GetBytes(firstUsableLba).CopyTo(gptData, headerOffset + 40);

        // Last usable LBA: before backup header
        long lastUsableLba = diskSectors - partitionEntriesSectors - 2;
        BitConverter.GetBytes(lastUsableLba).CopyTo(gptData, headerOffset + 48);

        // Disk GUID - generate new or use from metadata if available
        var diskGuid = Guid.NewGuid();
        diskGuid.ToByteArray().CopyTo(gptData, headerOffset + 56);

        // Partition entry LBA: 2 (entries start at LBA 2)
        BitConverter.GetBytes((long)2).CopyTo(gptData, headerOffset + 72);

        // Number of partition entries
        BitConverter.GetBytes((uint)numPartitionEntries).CopyTo(gptData, headerOffset + 80);

        // Size of partition entry: 128 bytes
        BitConverter.GetBytes((uint)PartitionEntrySize).CopyTo(gptData, headerOffset + 84);

        // CRC32 of partition entries (calculated later)
        BitConverter.GetBytes(0u).CopyTo(gptData, headerOffset + 88);

        // Calculate and write CRC32 of header
        uint headerCrc = CalculateCrc32(gptData, headerOffset, GptHeaderSize);
        BitConverter.GetBytes(headerCrc).CopyTo(gptData, headerOffset + 16);
    }

    /// <summary>
    /// Writes GPT partition entries.
    /// </summary>
    /// <param name="gptData">The GPT data buffer.</param>
    /// <param name="metadata">The geometry metadata.</param>
    /// <param name="sectorSize">The sector size in bytes.</param>
    /// <param name="partitionEntriesSectors">The number of sectors for partition entries.</param>
    private void WriteGPTPartitionEntries(byte[] gptData, GeometryMetadata metadata, int sectorSize, int partitionEntriesSectors)
    {
        int entriesOffset = 2 * sectorSize;  // Entries start at LBA 2

        if (metadata.Partitions == null)
            return;

        var gptPartitions = metadata.Partitions
            .Where(p => p.TableType == PartitionTableType.GPT)
            .OrderBy(p => p.Number)
            .Take(128)  // GPT standard supports 128 entries
            .ToList();

        // Calculate partition entries CRC32
        var entriesData = new byte[partitionEntriesSectors * sectorSize];

        for (int i = 0; i < gptPartitions.Count; i++)
        {
            var part = gptPartitions[i];
            int entryOffset = i * PartitionEntrySize;
            WriteGPTPartitionEntry(entriesData, entryOffset, part, sectorSize);
        }

        // Copy entries to main buffer
        entriesData.CopyTo(gptData, entriesOffset);

        // Calculate and write CRC32 of partition entries to header
        uint entriesCrc = CalculateCrc32(entriesData, 0, entriesData.Length);
        int headerOffset = sectorSize;
        BitConverter.GetBytes(entriesCrc).CopyTo(gptData, headerOffset + 88);

        // Recalculate header CRC with updated partition entries CRC
        uint headerCrc = CalculateCrc32(gptData, headerOffset, GptHeaderSize);
        BitConverter.GetBytes(headerCrc).CopyTo(gptData, headerOffset + 16);
    }

    /// <summary>
    /// Writes a single GPT partition entry.
    /// </summary>
    /// <param name="entriesData">The partition entries buffer.</param>
    /// <param name="offset">The offset in the buffer to write the entry.</param>
    /// <param name="part">The partition geometry.</param>
    /// <param name="sectorSize">The sector size in bytes.</param>
    private void WriteGPTPartitionEntry(byte[] entriesData, int offset, PartitionGeometry part, int sectorSize)
    {
        // Partition type GUID (16 bytes)
        var typeGuid = GetGPTPartitionTypeGuid(part);
        typeGuid.ToByteArray().CopyTo(entriesData, offset + 0);

        // Unique partition GUID (16 bytes) - use VolumeGuid if available, otherwise generate
        var uniqueGuid = part.VolumeGuid ?? Guid.NewGuid();
        uniqueGuid.ToByteArray().CopyTo(entriesData, offset + 16);

        // Starting LBA (8 bytes)
        long startLba = part.StartOffset / sectorSize;
        BitConverter.GetBytes(startLba).CopyTo(entriesData, offset + 32);

        // Ending LBA (8 bytes)
        long sizeInSectors = part.Size / sectorSize;
        long endLba = startLba + sizeInSectors - 1;
        BitConverter.GetBytes(endLba).CopyTo(entriesData, offset + 40);

        // Attributes (8 bytes) - default to 0
        BitConverter.GetBytes((long)0).CopyTo(entriesData, offset + 48);

        // Partition name (72 bytes, UTF-16LE)
        string name = part.Name ?? $"Partition {part.Number}";
        var nameBytes = Encoding.Unicode.GetBytes(name);
        int nameLength = Math.Min(nameBytes.Length, 72);
        Array.Copy(nameBytes, 0, entriesData, offset + 56, nameLength);
        // Pad remainder with zeros (already zeroed)
    }

    /// <summary>
    /// Gets the GPT partition type GUID based on partition geometry.
    /// </summary>
    /// <param name="part">The partition geometry.</param>
    /// <returns>The GPT partition type GUID.</returns>
    private Guid GetGPTPartitionTypeGuid(PartitionGeometry part)
    {
        return part.Type switch
        {
            PartitionType.EFI => Guid.Parse("C12A7328-F81F-11D2-BA4B-00A0C93EC93B"),
            PartitionType.MicrosoftReserved => Guid.Parse("E3C9E316-0B5C-4DB8-817D-F92DF00215AE"),
            PartitionType.Recovery => Guid.Parse("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC"),
            PartitionType.LinuxFilesystem => Guid.Parse("0FC63DAF-8483-4772-8E79-3D69D8477DE4"),
            PartitionType.LinuxSwap => Guid.Parse("0657FD6D-A4AB-43C4-84E5-0933C84B4F4F"),
            PartitionType.LinuxLVM => Guid.Parse("E6D6D379-F507-44C2-A23C-238F2A3DF928"),
            PartitionType.LinuxRAID => Guid.Parse("A19D880F-05FC-4D3B-A006-743F0F84911E"),
            PartitionType.BIOSBoot => Guid.Parse("21686148-6449-6E6F-744E-656564454649"),
            _ => Guid.Parse("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7")  // Microsoft Basic Data (default)
        };
    }

    /// <summary>
    /// Calculates CRC32 checksum for the given data.
    /// </summary>
    /// <param name="data">The data buffer.</param>
    /// <param name="offset">The offset in the buffer to start calculation.</param>
    /// <param name="count">The number of bytes to calculate.</param>
    /// <returns>The CRC32 checksum.</returns>
    private uint CalculateCrc32(byte[] data, int offset, int count)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < count; i++)
        {
            byte b = data[offset + i];
            crc ^= b;
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ 0xEDB88320;
                else
                    crc >>= 1;
            }
        }
        return ~crc;
    }

    /// <summary>
    /// Writes the secondary (backup) GPT header and partition entries to the end of the disk.
    /// </summary>
    /// <param name="primaryGptData">The primary GPT data.</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task WriteSecondaryGPT(byte[] primaryGptData, CancellationToken cancel)
    {
        if (_targetDisk == null || _geometryMetadata?.Disk == null)
            return;

        var sectorSize = _geometryMetadata.Disk.SectorSize;
        var diskSectors = _geometryMetadata.Disk.Sectors;

        // Calculate sizes
        int numPartitionEntries = 128;
        int partitionEntriesSize = numPartitionEntries * PartitionEntrySize;
        int partitionEntriesSectors = (partitionEntriesSize + sectorSize - 1) / sectorSize;

        // Secondary GPT layout:
        // - Partition entries (before header)
        // - Secondary GPT header (last sector)

        // Read primary header to get disk GUID and other fields
        int primaryHeaderOffset = sectorSize;
        var diskGuid = new byte[16];
        Array.Copy(primaryGptData, primaryHeaderOffset + 56, diskGuid, 0, 16);

        // Read partition entries CRC from primary
        byte[] partitionEntriesCrcBytes = new byte[4];
        Array.Copy(primaryGptData, primaryHeaderOffset + 88, partitionEntriesCrcBytes, 0, 4);

        // Create secondary header
        var secondaryHeader = new byte[GptHeaderSize];

        // Signature: "EFI PART"
        BitConverter.GetBytes(GptSignature).CopyTo(secondaryHeader, 0);

        // Revision: 1.0
        BitConverter.GetBytes(GptRevision).CopyTo(secondaryHeader, 8);

        // Header size: 92 bytes
        BitConverter.GetBytes((uint)GptHeaderSize).CopyTo(secondaryHeader, 12);

        // CRC32 (calculated later)
        BitConverter.GetBytes(0u).CopyTo(secondaryHeader, 16);

        // Reserved
        BitConverter.GetBytes(0u).CopyTo(secondaryHeader, 20);

        // Current LBA: last sector (backup header location)
        long secondaryHeaderLba = diskSectors - 1;
        BitConverter.GetBytes(secondaryHeaderLba).CopyTo(secondaryHeader, 24);

        // Backup LBA: 1 (primary header location)
        BitConverter.GetBytes((long)1).CopyTo(secondaryHeader, 32);

        // First usable LBA
        long firstUsableLba = 2 + partitionEntriesSectors;
        BitConverter.GetBytes(firstUsableLba).CopyTo(secondaryHeader, 40);

        // Last usable LBA
        long lastUsableLba = diskSectors - partitionEntriesSectors - 2;
        BitConverter.GetBytes(lastUsableLba).CopyTo(secondaryHeader, 48);

        // Disk GUID (same as primary)
        diskGuid.CopyTo(secondaryHeader, 56);

        // Partition entry LBA: right before the secondary header
        long secondaryEntriesLba = diskSectors - partitionEntriesSectors - 1;
        BitConverter.GetBytes(secondaryEntriesLba).CopyTo(secondaryHeader, 72);

        // Number of partition entries
        BitConverter.GetBytes((uint)numPartitionEntries).CopyTo(secondaryHeader, 80);

        // Size of partition entry
        BitConverter.GetBytes((uint)PartitionEntrySize).CopyTo(secondaryHeader, 84);

        // Partition entries CRC32 (same as primary)
        partitionEntriesCrcBytes.CopyTo(secondaryHeader, 88);

        // Calculate and write CRC32 of secondary header
        uint headerCrc = CalculateCrc32(secondaryHeader, 0, GptHeaderSize);
        BitConverter.GetBytes(headerCrc).CopyTo(secondaryHeader, 16);

        // Write secondary partition entries (same as primary)
        long entriesStartOffset = 2 * sectorSize;
        int entriesByteSize = partitionEntriesSectors * sectorSize;
        var partitionEntries = new byte[entriesByteSize];
        Array.Copy(primaryGptData, entriesStartOffset, partitionEntries, 0, entriesByteSize);

        long secondaryEntriesOffset = secondaryEntriesLba * sectorSize;
        await _targetDisk.WriteBytesAsync(secondaryEntriesOffset, partitionEntries, cancel).ConfigureAwait(false);

        // Write secondary header at the last sector
        long secondaryHeaderOffset = secondaryHeaderLba * sectorSize;
        await _targetDisk.WriteBytesAsync(secondaryHeaderOffset, secondaryHeader, cancel).ConfigureAwait(false);

        Log.WriteInformationMessage(LOGTAG, "SecondaryGPTWritten",
            $"Successfully wrote secondary GPT header at LBA {secondaryHeaderLba} and partition entries at LBA {secondaryEntriesLba}.");
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
        private readonly byte[] _buffer;
        private readonly bool _bufferRented;
        private long _position;
        private long _length;
        private bool _disposed = false;

        public PartitionWriteStream(IRawDisk disk, long startOffset, long maxSize)
        {
            _disk = disk;
            _startOffset = startOffset;
            _maxSize = maxSize;
            // Rent buffer from ArrayPool to avoid LOH allocations for large buffers
            _buffer = ArrayPool<byte>.Shared.Rent((int)maxSize);
            _bufferRented = true;
            _position = 0;
            _length = 0;
        }

        public override bool CanRead => false;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > _maxSize)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            if (newPosition < 0 || newPosition > _maxSize)
                throw new IOException("Cannot seek beyond partition size.");
            _position = newPosition;
            return _position;
        }
        public override void SetLength(long value)
        {
            if (value > _maxSize)
                throw new IOException($"Cannot write beyond partition size of {_maxSize} bytes.");
            _length = value;
            if (_position > _length)
                _position = _length;
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_position + count > _maxSize)
                throw new IOException($"Cannot write beyond partition size of {_maxSize} bytes.");
            Buffer.BlockCopy(buffer, offset, _buffer, (int)_position, count);
            _position += count;
            if (_position > _length)
                _length = _position;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Write all buffered data to disk
                    if (_length > 0)
                    {
                        _disk.WriteBytesAsync(_startOffset, _buffer.AsMemory(0, (int)_length), CancellationToken.None).GetAwaiter().GetResult();
                    }
                    // Return the rented buffer to the pool
                    if (_bufferRented)
                    {
                        ArrayPool<byte>.Shared.Return(_buffer);
                    }
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
