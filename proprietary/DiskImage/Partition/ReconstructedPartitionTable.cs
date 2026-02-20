// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Disk;

namespace Duplicati.Proprietary.DiskImage.Partition;

/// <summary>
/// A reconstructed partition table for restore operations.
/// This is a lightweight implementation that stores metadata from the backup.
/// It exists only to satisfy the IPartition.PartitionTable reference during restore.
/// </summary>
internal class ReconstructedPartitionTable : IPartitionTable
{
    private readonly IRawDisk _rawDisk;
    private readonly GeometryMetadata _geometry;
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
