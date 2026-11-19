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

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.SourceProvider.DiskImage.Filesystem;
using Duplicati.Library.SourceProvider.DiskImage.Filesystem.Fat32;
using Duplicati.Library.SourceProvider.DiskImage.Filesystem.Ntfs;
using Duplicati.Library.SourceProvider.DiskImage.General;

namespace Duplicati.Library.SourceProvider.DiskImage.SourceItems;

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
            blockSize = unknownMeta.BlockSize;
        else if (fsMetadata is Fat32FilesystemMetadata fat32Meta)
            blockSize = fat32Meta.BlockSize;
        else if (fsMetadata is NtfsFilesystemMetadata ntfsMeta)
            blockSize = ntfsMeta.BlockSize;

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
                // For FAT32, extract cluster information
                else if (fsMetadata is Fat32FilesystemMetadata fat32Meta)
                {
                    metadata["filesystem:BlockSize"] = fat32Meta.BlockSize.ToString();
                    metadata["filesystem:ClusterSize"] = fat32Meta.ClusterSize.ToString();
                    metadata["filesystem:TotalClusters"] = fat32Meta.TotalClusters.ToString();
                    metadata["filesystem:AllocatedClusters"] = fat32Meta.AllocatedClusters.ToString();
                    metadata["filesystem:FreeClusters"] = fat32Meta.FreeClusters.ToString();
                }
                // For NTFS, extract cluster and MFT information
                else if (fsMetadata is NtfsFilesystemMetadata ntfsMeta)
                {
                    metadata["filesystem:BlockSize"] = ntfsMeta.BlockSize.ToString();
                    metadata["filesystem:ClusterSize"] = ntfsMeta.ClusterSize.ToString();
                    metadata["filesystem:TotalClusters"] = ntfsMeta.TotalClusters.ToString();
                    metadata["filesystem:AllocatedClusters"] = ntfsMeta.AllocatedClusters.ToString();
                    metadata["filesystem:FreeClusters"] = ntfsMeta.FreeClusters.ToString();
                    metadata["filesystem:MftRecordSize"] = ntfsMeta.MftRecordSize.ToString();
                    metadata["filesystem:HasJournal"] = ntfsMeta.HasJournal.ToString();
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
