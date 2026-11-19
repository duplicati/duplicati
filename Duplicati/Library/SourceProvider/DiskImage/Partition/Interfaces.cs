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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.SourceProvider.DiskImage.Disk;
using Duplicati.Library.SourceProvider.DiskImage.General;

namespace Duplicati.Library.SourceProvider.DiskImage.Partition;

/// <summary>
/// Represents a single partition on a disk.
/// </summary>
internal interface IPartition : IDisposable
{
    /// <summary>
    /// Gets the parent partition table.
    /// </summary>
    IPartitionTable PartitionTable { get; }

    /// <summary>
    /// Gets the partition number (1-based).
    /// </summary>
    int PartitionNumber { get; }

    /// <summary>
    /// Gets the partition type identifier.
    /// </summary>
    PartitionType Type { get; }

    /// <summary>
    /// Gets the starting offset in bytes.
    /// </summary>
    long StartOffset { get; }

    /// <summary>
    /// Gets the size in bytes.
    /// </summary>
    long Size { get; }

    /// <summary>
    /// Gets the partition name (if available).
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// Gets the filesystem type (if detectable).
    /// </summary>
    FileSystemType FilesystemType { get; }

    /// <summary>
    /// Gets the volume GUID (for GPT).
    /// </summary>
    Guid? VolumeGuid { get; }

    /// <summary>
    /// Creates a stream to read the raw partition data.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stream for reading partition data.</returns>
    Task<Stream> OpenReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a stream to write the raw partition data.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stream for writing partition data.</returns>
    Task<Stream> OpenWriteAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Partition table parsing interface.
/// </summary>
internal interface IPartitionTable : IDisposable
{
    /// <summary>
    /// Gets the raw disk this table was parsed from. Null if the table was created from a byte array.
    /// </summary>
    IRawDisk? RawDisk { get; }

    /// <summary>
    /// Gets the partition table type.
    /// </summary>
    PartitionTableType TableType { get; }

    /// <summary>
    /// Enumerates all partitions on the disk.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of partitions.</returns>
    IAsyncEnumerable<IPartition> EnumeratePartitions(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a specific partition by number.
    /// </summary>
    /// <param name="partitionNumber">The partition number.</param>
    /// <returns>The partition, or null if not found.</returns>
    Task<IPartition?> GetPartitionAsync(int partitionNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the protective MBR (for GPT disks).
    /// </summary>
    Task<Stream> GetProtectiveMbrAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the raw partition table data for backup/restore purposes.
    /// For MBR: returns the MBR sector (512 bytes).
    /// For GPT: returns the protective MBR + GPT header + partition entries.
    /// </summary>
    Task<Stream> GetPartitionTableDataAsync(CancellationToken cancellationToken);
}
