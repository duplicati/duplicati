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

namespace Duplicati.Proprietary.DiskImage.SourceItems;

internal class PartitionSourceEntry(string parentPath, IPartition partition)
    : DiskImageEntryBase(PathCombine(parentPath, $"part_{partition.PartitionTable.TableType}_{partition.PartitionNumber}{System.IO.Path.DirectorySeparatorChar}"))
{
    public override bool IsFolder => true;
    public override long Size => partition.Size;

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (partition.FilesystemType == FileSystemType.Unknown)
        {
            var fs = new UnknownFilesystem(partition);
            yield return new FilesystemSourceEntry(this.Path, fs);
        }
    }

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
            metadata["partition:Name"] = partition.Name;

        if (partition.VolumeGuid.HasValue)
            metadata["partition:VolumeGuid"] = partition.VolumeGuid.Value.ToString();

        return metadata;
    }

    private static string PathCombine(string p1, string p2)
    {
        if (string.IsNullOrEmpty(p1)) return p2;
        if (string.IsNullOrEmpty(p2)) return p1;
        return p1.TrimEnd('/') + "/" + p2.TrimStart('/');
    }
}
