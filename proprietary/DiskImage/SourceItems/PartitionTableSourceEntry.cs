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

/// <summary>
/// Represents the partition table as a source entry for backup/restore purposes.
/// This allows the partition table (MBR or GPT) to be backed up and restored separately.
/// </summary>
internal class PartitionTableSourceEntry(string parentPath, IPartitionTable table, IRawDisk disk)
    : DiskImageEntryBase(PathCombine(parentPath, $"partition_table_{table.TableType}"))
{
    public override bool IsFolder => false;
    public override bool IsMetaEntry => true;
    public override long Size => GetTableSize();

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        return table.GetPartitionTableDataAsync(cancellationToken);
    }

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Partition table is a leaf node - no children
        await Task.CompletedTask;
        yield break;
    }

    public override async Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        var metadata = await base.GetMinorMetadata(cancellationToken);

        // Add partition table metadata
        metadata["diskimage:Type"] = "partition_table";
        metadata["partition_table:Type"] = table.TableType.ToString();
        metadata["partition_table:Size"] = Size.ToString();
        metadata["disk:SectorSize"] = disk.SectorSize.ToString();

        // Add type-specific metadata
        if (table.TableType == PartitionTableType.MBR)
        {
            metadata["partition_table:MbrSize"] = "512";
        }
        else if (table.TableType == PartitionTableType.GPT)
        {
            metadata["partition_table:HasProtectiveMbr"] = "true";
            metadata["partition_table:HeaderSize"] = "92"; // GPT header is 92 bytes
        }

        return metadata;
    }

    private long GetTableSize()
    {
        try
        {
            // Try to get the size by reading the table data
            using var stream = table.GetPartitionTableDataAsync(CancellationToken.None).Result;
            return stream.Length;
        }
        catch
        {
            // Fallback to estimated sizes
            return table.TableType switch
            {
                PartitionTableType.MBR => 512,
                PartitionTableType.GPT => 512 + 512 + (128 * 4), // Protective MBR + Header + 4 entries
                _ => 0
            };
        }
    }

    private static string PathCombine(string p1, string p2)
    {
        if (string.IsNullOrEmpty(p1)) return p2;
        if (string.IsNullOrEmpty(p2)) return p1;
        return p1.TrimEnd('/') + "/" + p2.TrimStart('/');
    }
}
