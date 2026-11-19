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
using Duplicati.Library.SourceProvider.DiskImage.Disk;
using Duplicati.Library.SourceProvider.DiskImage.General;
using Duplicati.Library.SourceProvider.DiskImage.Partition;

namespace Duplicati.Library.SourceProvider.DiskImage.SourceItems;

/// <summary>
/// Represents the partition table as a source entry for backup/restore purposes.
/// This allows the partition table (MBR or GPT) to be backed up and restored separately.
/// </summary>
internal class PartitionTableSourceEntry(string parentPath, IPartitionTable table, IRawDisk disk)
    : DiskImageEntryBase(System.IO.Path.Combine(parentPath, $"partition_table_{table.TableType}"))
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

}
