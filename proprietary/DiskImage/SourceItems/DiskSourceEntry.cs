// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.Partition;

namespace Duplicati.Proprietary.DiskImage.SourceItems;

internal class DiskSourceEntry(SourceProvider provider, IRawDisk disk)
    : DiskImageEntryBase(provider.MountedPath)
{
    public override bool IsFolder => true;
    public override bool IsRootEntry => true;
    public override long Size => disk.Size;

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var table = await PartitionTableFactory.CreateAsync(disk, cancellationToken);
        if (table != null)
        {
            await foreach (var partition in table.EnumeratePartitions(cancellationToken))
            {
                yield return new PartitionSourceEntry(this.Path, partition);
            }
        }
    }

    public override async Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        var metadata = await base.GetMinorMetadata(cancellationToken);

        // Add disk-level metadata
        metadata["diskimage:Type"] = "disk";
        metadata["disk:DevicePath"] = disk.DevicePath;
        metadata["disk:Size"] = disk.Size.ToString();
        metadata["disk:SectorSize"] = disk.SectorSize.ToString();
        metadata["disk:Sectors"] = disk.Sectors.ToString();

        // Add partition table metadata if available
        var table = await PartitionTableFactory.CreateAsync(disk, cancellationToken);
        if (table != null)
        {
            metadata["disk:PartitionTableType"] = table.TableType.ToString();

            // Get the full partition table data for restore
            try
            {
                using var tableData = await table.GetPartitionTableDataAsync(cancellationToken);
                metadata["disk:PartitionTableDataSize"] = tableData.Length.ToString();

                // For GPT disks, also record specific info
                if (table.TableType == PartitionTableType.GPT)
                {
                    metadata["disk:HasProtectiveMbr"] = "true";
                }
            }
            catch
            {
                // Partition table data not available
            }
        }

        return metadata;
    }
}
