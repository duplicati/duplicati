// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;
using Duplicati.Proprietary.DiskImage.Disk;
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
    /// Temporary files created during the restore process, keyed by path.
    /// </summary>
    private readonly ConcurrentDictionary<string, TempFile> _temporaryFiles = new();

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

        _devicePath = options.GetValueOrDefault(OptionsHelper.DISK_DEVICE_OPTION) ?? "";
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
        if (!_hasSetOverwriteOption)
            throw new UserInformationException(Strings.RestoreOverwriteNotSet, "OverwriteOptionNotSet");

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

        if (_temporaryFiles.ContainsKey(path))
            return Task.FromResult(true);

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<Stream> OpenWrite(string path, CancellationToken cancel)
    {
        path = NormalizePath(path);

        var file = _temporaryFiles.GetOrAdd(path, _ => new TempFile());
        return Task.FromResult<Stream>(SystemIO.IO_OS.FileOpenWrite(file));
    }

    /// <inheritdoc />
    public Task<Stream> OpenRead(string path, CancellationToken cancel)
    {
        path = NormalizePath(path);

        if (_temporaryFiles.TryGetValue(path, out var tempFile))
            return Task.FromResult<Stream>(SystemIO.IO_OS.FileOpenRead(tempFile));

        throw new FileNotFoundException($"File not found: {path}");
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadWrite(string path, CancellationToken cancel)
    {
        path = NormalizePath(path);

        var file = _temporaryFiles.GetOrAdd(path, _ => new TempFile());
        return Task.FromResult<Stream>(SystemIO.IO_OS.FileOpenReadWrite(file));
    }

    /// <inheritdoc />
    public Task<long> GetFileLength(string path, CancellationToken cancel)
    {
        path = NormalizePath(path);

        if (_temporaryFiles.TryGetValue(path, out var tempFile))
            return Task.FromResult(SystemIO.IO_OS.FileLength(tempFile));

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
        var diskItems = GetMetadataByType("disk");
        var partitionItems = GetMetadataByType("partition");
        var blockItems = GetMetadataByType("block");
        var fileItems = GetMetadataByType("file");

        // Restore disk-level items first (partition tables)
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

        // Restore block-level items (raw sectors)
        if (blockItems.Count > 0)
        {
            await RestoreBlockItems(blockItems, cancel);
            processedCount += blockItems.Count;
            progressCallback?.Invoke(processedCount / (double)totalItems);
        }

        // Restore file-level items (filesystem files)
        if (fileItems.Count > 0)
        {
            await RestoreFileItems(fileItems, cancel);
            processedCount += fileItems.Count;
            progressCallback?.Invoke(processedCount / (double)totalItems);
        }

        // Cleanup temporary files
        foreach (var tempFile in _temporaryFiles.Values)
            tempFile.Dispose();
        _temporaryFiles.Clear();
        _metadata.Clear();

        Log.WriteInformationMessage(LOGTAG, "RestoreComplete", "Restore operation completed.");
    }

    /// <summary>
    /// Restores disk-level items (partition tables, boot sectors).
    /// </summary>
    private async Task RestoreDiskItems(List<KeyValuePair<string, Dictionary<string, string?>>> items, CancellationToken cancel)
    {
        foreach (var item in items)
        {
            if (cancel.IsCancellationRequested)
                break;

            var path = item.Key;
            var metadata = item.Value;

            if (!_temporaryFiles.TryGetValue(path, out var tempFile))
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
                if (_validateSize && long.TryParse(sourceSizeStr, out var sourceSize))
                {
                    if (_targetDisk!.Size < sourceSize)
                    {
                        throw new InvalidOperationException(
                            string.Format(Strings.RestoreTargetTooSmall, _targetDisk.Size, sourceSize));
                    }
                }

                // Read the data from temp file
                byte[] data;
                using (var stream = SystemIO.IO_OS.FileOpenRead(tempFile))
                {
                    data = new byte[stream.Length];
                    await stream.ReadExactlyAsync(data, cancel);
                }

                // Write to disk (offset 0 for partition table/boot sector)
                // TODO in the future, write the correct GPT table: primary at offset 0, secondary at end of disk. The header should also be updated to have correct offsets and CRCs.
                await _targetDisk!.WriteBytesAsync(0, data, cancel);

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

            if (!_temporaryFiles.TryGetValue(path, out var tempFile))
                continue;

            try
            {
                // Get partition metadata
                metadata.TryGetValue("partition:Number", out var partitionNumber);
                metadata.TryGetValue("partition:Type", out var partitionType);
                metadata.TryGetValue("partition:Name", out var partitionName);
                metadata.TryGetValue("partition:FilesystemType", out var filesystemType);
                metadata.TryGetValue("partition:VolumeGuid", out var volumeGuid);

                // Get partition offset from metadata (use new key format, fallback to old)
                if (!metadata.TryGetValue("partition:StartOffset", out var offsetStr) || !long.TryParse(offsetStr, out var offset))
                {
                    // Fallback to old key format for backward compatibility
                    if (!metadata.TryGetValue("partition_offset", out offsetStr) || !long.TryParse(offsetStr, out offset))
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestorePartitionNoOffset", null, $"No partition offset found for: {path}");
                        continue;
                    }
                }

                Log.WriteInformationMessage(LOGTAG, "RestorePartitionItemMetadata",
                    $"Restoring partition #{partitionNumber} (Type: {partitionType}, Name: {partitionName}, FS: {filesystemType}, GUID: {volumeGuid})");

                // Read the data from temp file
                byte[] data;
                using (var stream = SystemIO.IO_OS.FileOpenRead(tempFile))
                {
                    data = new byte[stream.Length];
                    await stream.ReadExactlyAsync(data, cancel);
                }

                // Write to partition location
                await _targetDisk!.WriteBytesAsync(offset, data, cancel);

                Log.WriteInformationMessage(LOGTAG, "RestorePartitionItem", $"Restored partition item: {path} (Partition #{partitionNumber}, Offset: {offset})");
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestorePartitionItemFailed", ex, $"Failed to restore partition item: {path}");
                throw;
            }
        }
    }

    /// <summary>
    /// Restores block-level items (raw sectors).
    /// </summary>
    private async Task RestoreBlockItems(List<KeyValuePair<string, Dictionary<string, string?>>> items, CancellationToken cancel)
    {
        foreach (var item in items)
        {
            if (cancel.IsCancellationRequested)
                break;

            var path = item.Key;
            var metadata = item.Value;

            if (!_temporaryFiles.TryGetValue(path, out var tempFile))
                continue;

            try
            {
                // Get block metadata
                metadata.TryGetValue("file:Path", out var filePath);
                metadata.TryGetValue("file:Size", out var fileSize);
                metadata.TryGetValue("filesystem:Type", out var filesystemType);

                // Get block address from metadata (use new key format, fallback to old)
                if (!metadata.TryGetValue("block:Address", out var addressStr) || !long.TryParse(addressStr, out var address))
                {
                    // Fallback to old key format for backward compatibility
                    if (!metadata.TryGetValue("block_address", out addressStr) || !long.TryParse(addressStr, out address))
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreBlockNoAddress", null, $"No block address found for: {path}");
                        continue;
                    }
                }

                Log.WriteInformationMessage(LOGTAG, "RestoreBlockItemMetadata",
                    $"Restoring block from {filePath} (Size: {fileSize}, FS: {filesystemType})");

                // Read the data from temp file
                byte[] data;
                using (var stream = SystemIO.IO_OS.FileOpenRead(tempFile))
                {
                    data = new byte[stream.Length];
                    await stream.ReadExactlyAsync(data, cancel);
                }

                // Write to block location
                await _targetDisk!.WriteBytesAsync(address, data, cancel);

                Log.WriteInformationMessage(LOGTAG, "RestoreBlockItem", $"Restored block item: {path} at address {address}");
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreBlockItemFailed", ex, $"Failed to restore block item: {path}");
                throw;
            }
        }
    }

    /// <summary>
    /// Restores file-level items.
    /// </summary>
    private async Task RestoreFileItems(List<KeyValuePair<string, Dictionary<string, string?>>> items, CancellationToken cancel)
    {
        // File-level restore would require filesystem implementation
        // For now, log that this is not yet implemented
        if (items.Count > 0)
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreFileNotImplemented", null, $"File-level restore not yet implemented. {items.Count} items skipped.");
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

        foreach (var file in _temporaryFiles.Values)
            file.Dispose();
        _temporaryFiles.Clear();

        _disposed = true;
    }
}
