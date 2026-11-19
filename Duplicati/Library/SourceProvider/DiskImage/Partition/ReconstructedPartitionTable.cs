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
/// A reconstructed partition table for restore operations.
/// This is a lightweight implementation that stores metadata from the backup.
/// It exists only to satisfy the IPartition.PartitionTable reference during restore.
/// </summary>
internal class ReconstructedPartitionTable : IPartitionTable
{
    private readonly IRawDisk _rawDisk;
    private readonly GeometryMetadata? _geometry;
    private bool _disposed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReconstructedPartitionTable"/> class.
    /// </summary>
    /// <param name="rawDisk">The raw disk.</param>
    /// <param name="geometry">The geometry metadata.</param>
    /// <param name="tableType">The partition table type (GPT or MBR).</param>
    public ReconstructedPartitionTable(IRawDisk rawDisk, GeometryMetadata geometry, PartitionTableType tableType)
    {
        _rawDisk = rawDisk;
        _geometry = geometry;
        TableType = tableType;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReconstructedPartitionTable"/> class
    /// without geometry metadata, for partitions created on the target disk during restore.
    /// </summary>
    /// <param name="rawDisk">The raw disk.</param>
    /// <param name="tableType">The partition table type (GPT or MBR).</param>
    public ReconstructedPartitionTable(IRawDisk rawDisk, PartitionTableType tableType)
    {
        _rawDisk = rawDisk;
        TableType = tableType;
    }

    /// <inheritdoc />
    public IRawDisk? RawDisk => _rawDisk;

    /// <inheritdoc />
    public PartitionTableType TableType { get; }

    /// <inheritdoc />
    public IAsyncEnumerable<IPartition> EnumeratePartitions(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Enumeration not supported on reconstructed partition table.");
    }

    /// <inheritdoc />
    public Task<IPartition?> GetPartitionAsync(int partitionNumber, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("GetPartitionAsync not supported on reconstructed partition table.");
    }

    /// <inheritdoc />
    public Task<Stream> GetProtectiveMbrAsync(CancellationToken cancellationToken)
    {
        if (TableType == PartitionTableType.MBR)
        {
            throw new NotSupportedException("MBR does not have a protective MBR.");
        }
        throw new NotSupportedException("GetProtectiveMbrAsync not supported on reconstructed partition table.");
    }

    /// <inheritdoc />
    public Task<Stream> GetPartitionTableDataAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("GetPartitionTableDataAsync not supported on reconstructed partition table.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
