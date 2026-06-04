// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Proprietary.DiskImage.Partition;
using Duplicati.Proprietary.DiskImage.Filesystem;
using Duplicati.Library.Utility;
using Duplicati.Proprietary.DiskImage.General;
using Duplicati.Proprietary.DiskImage.Filesystem.Fat32;
using Duplicati.Proprietary.DiskImage.Filesystem.Ntfs;

namespace Duplicati.Proprietary.DiskImage.SourceItems;

/// <summary>
/// Represents a partition as a source entry for backup operations.
/// Acts as a container for the filesystem within the partition.
/// </summary>
internal class PartitionSourceEntry(string parentPath, IPartition partition, bool treatFilesystemAsUnknown = false)
    : DiskImageEntryBase(System.IO.Path.Combine(parentPath, $"part_{partition.PartitionTable.TableType}_{partition.PartitionNumber}{System.IO.Path.DirectorySeparatorChar}"))
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
        IFilesystem fs = TreatFilesystemAsUnknown || partition.FilesystemType == FileSystemType.Unknown
            ? new UnknownFilesystem(partition)
            : partition.FilesystemType switch
            {
                FileSystemType.FAT32 => new Fat32Filesystem(partition),
                FileSystemType.NTFS => new NtfsFilesystem(partition),
                _ => new UnknownFilesystem(partition)
            };
        yield return new FilesystemSourceEntry(this.Path, fs);
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
            metadata["diskimage:Name"] = $"{partition.Name} ({partition.Type}, {Utility.FormatSizeString(partition.Size)})";
        }
        else
        {
            metadata["diskimage:Name"] = $"{partition.Type} ({Utility.FormatSizeString(partition.Size)})";
        }

        if (partition.VolumeGuid.HasValue)
            metadata["partition:VolumeGuid"] = partition.VolumeGuid.Value.ToString();

        return metadata;
    }

}
