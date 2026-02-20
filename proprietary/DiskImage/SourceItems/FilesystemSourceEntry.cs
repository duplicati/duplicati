// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Proprietary.DiskImage.Filesystem;

namespace Duplicati.Proprietary.DiskImage.SourceItems;

/// <summary>
/// Represents a filesystem as a source entry for backup operations.
/// Acts as a container for files within a partition.
/// </summary>
internal class FilesystemSourceEntry(string parentPath, IFilesystem filesystem)
    : DiskImageEntryBase(System.IO.Path.Combine(parentPath, $"fs_{filesystem.Type}{System.IO.Path.DirectorySeparatorChar}"))
{
    /// <inheritdoc />
    public override bool IsFolder => true;

    /// <summary>
    /// Gets the underlying filesystem instance.
    /// </summary>
    public IFilesystem Filesystem => filesystem;

    /// <inheritdoc />
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Yield the files in the filesystem
        await foreach (var file in filesystem.ListFilesAsync(cancellationToken))
        {
            yield return new FileSourceEntry(this.Path, filesystem, file);
        }
    }

    /// <summary>
    /// Gets the filesystem geometry metadata for this filesystem.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The filesystem geometry metadata, or null if not available.</returns>
    public async Task<FilesystemGeometry?> GetFilesystemGeometry(CancellationToken cancellationToken)
    {
        var fsMetadata = await filesystem.GetFilesystemMetadataAsync(cancellationToken);
        int blockSize = 1024 * 1024; // Default 1MB blocks

        if (fsMetadata is UnkownFilesystemMetadata unknownMeta)
        {
            blockSize = unknownMeta.BlockSize;
        }

        return new FilesystemGeometry
        {
            Type = filesystem.Type,
            PartitionNumber = filesystem.Partition.PartitionNumber,
            PartitionStartOffset = filesystem.Partition.StartOffset,
            BlockSize = blockSize,
            Metadata = fsMetadata != null ? JsonSerializer.Serialize(fsMetadata) : null
        };
    }

    /// <inheritdoc />
    public override async Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        var metadata = await base.GetMinorMetadata(cancellationToken);

        // Add filesystem-level metadata
        metadata["diskimage:Type"] = "filesystem";
        metadata["filesystem:Type"] = filesystem.Type.ToString();
        metadata["filesystem:PartitionNumber"] = filesystem.Partition.PartitionNumber.ToString();
        metadata["filesystem:PartitionStartOffset"] = filesystem.Partition.StartOffset.ToString();

        // Get filesystem-specific metadata
        try
        {
            var fsMetadata = await filesystem.GetFilesystemMetadataAsync(cancellationToken);
            if (fsMetadata != null)
            {
                metadata["filesystem:Metadata"] = JsonSerializer.Serialize(fsMetadata);

                // For UnknownFilesystem, extract block size
                if (fsMetadata is UnkownFilesystemMetadata unknownMeta)
                {
                    metadata["filesystem:BlockSize"] = unknownMeta.BlockSize.ToString();
                }
            }
        }
        catch
        {
            // Metadata not available
        }

        return metadata;
    }

}
