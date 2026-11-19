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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.SourceProvider.DiskImage.Partition;
using Duplicati.Library.SourceProvider.DiskImage.Filesystem;
using Duplicati.Library.Utility;
using Duplicati.Library.SourceProvider.DiskImage.General;
using Duplicati.Library.SourceProvider.DiskImage.Filesystem.Fat32;
using Duplicati.Library.SourceProvider.DiskImage.Filesystem.Ntfs;

namespace Duplicati.Library.SourceProvider.DiskImage.SourceItems;

/// <summary>
/// Represents a partition as a source entry for backup operations.
/// Acts as a container for the filesystem within the partition.
/// </summary>
internal class PartitionSourceEntry(string parentPath, IPartition partition, bool treatFilesystemAsUnknown = false)
    : DiskImageEntryBase(System.IO.Path.Combine(parentPath, PartitionSegment.Format(partition.PartitionTable.TableType, partition.PartitionNumber) + System.IO.Path.DirectorySeparatorChar))
{
    /// <inheritdoc />
    public override bool IsFolder => true;

    /// <inheritdoc />
    public override long Size => partition.Size;

    /// <summary>
    /// Gets the underlying partition instance.
    /// </summary>
    public IPartition Partition => partition;

    /// <summary>
    /// Gets a value indicating whether to treat the filesystem as unknown.
    /// </summary>
    private bool TreatFilesystemAsUnknown => treatFilesystemAsUnknown;

    /// <inheritdoc />
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Create a filesystem parser based on the detected filesystem type
        // If the filesystem is unknown, fall back to raw block-level access
        IFilesystem fs = TreatFilesystemAsUnknown || partition.FilesystemType == FileSystemType.Unknown
            ? new UnknownFilesystem(partition)
            : partition.FilesystemType switch
            {
                FileSystemType.FAT32 => new Fat32Filesystem(partition),
                FileSystemType.NTFS => new NtfsFilesystem(partition),
                _ => new UnknownFilesystem(partition)
            };

        var filesystemEntry = new FilesystemSourceEntry(this.Path, fs);

        // Emit the partition info file first, so that a restore selection containing
        // only this partition still carries the partition size and filesystem block
        // size (geometry.json sits at the disk level and is not part of such a selection)
        var info = new PartitionInfoMetadata
        {
            Partition = new PartitionGeometry
            {
                Number = partition.PartitionNumber,
                Type = partition.Type,
                StartOffset = partition.StartOffset,
                Size = partition.Size,
                Name = partition.Name,
                FilesystemType = partition.FilesystemType,
                VolumeGuid = partition.VolumeGuid,
                TableType = partition.PartitionTable.TableType
            },
            Filesystem = await filesystemEntry.GetFilesystemGeometry(cancellationToken)
        };
        yield return new PartitionInfoSourceEntry(this.Path, info);

        // Yield filesystem-specific entries
        yield return filesystemEntry;
    }

    /// <inheritdoc />
    public override async Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        var metadata = await base.GetMinorMetadata(cancellationToken);

        // Add partition-level metadata
        metadata["diskimage:Type"] = "partition";
        metadata["partition:Number"] = partition.PartitionNumber.ToString();
        metadata["partition:Type"] = partition.Type.ToString();
        metadata["partition:StartOffset"] = partition.StartOffset.ToString();
        metadata["partition:Size"] = partition.Size.ToString();
        metadata["partition:FilesystemType"] = partition.FilesystemType.ToString();
        metadata["partition:TableType"] = partition.PartitionTable.TableType.ToString();

        if (!string.IsNullOrEmpty(partition.Name))
        {
            metadata["partition:Name"] = partition.Name;
            metadata["diskimage:Name"] = $"{partition.Name} ({partition.Type}, {Utility.Utility.FormatSizeString(partition.Size)})";
        }
        else
        {
            metadata["diskimage:Name"] = $"{partition.Type} ({Utility.Utility.FormatSizeString(partition.Size)})";
        }

        if (partition.VolumeGuid.HasValue)
            metadata["partition:VolumeGuid"] = partition.VolumeGuid.Value.ToString();

        return metadata;
    }

}
