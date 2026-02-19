using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Disk;

namespace Duplicati.Proprietary.DiskImage.Partition;

/// <summary>
/// Represents an unknown partition table type.
/// Treats the entire disk as a single partition when the partition table type cannot be determined.
/// </summary>
internal class UnknownPartitionTable : IPartitionTable
{
    /// <summary>
    /// The raw disk associated with this partition table, if available. May be null if no disk access is possible.
    /// </summary>
    private readonly IRawDisk? m_rawDisk;

    /// <summary>
    /// Indicates whether this instance has been disposed. After disposal, the instance should not be used.
    /// </summary>
    private bool m_disposed = false;

    /// <inheritdoc />
    public IRawDisk? RawDisk => m_rawDisk;

    /// <inheritdoc />
    public PartitionTableType TableType => PartitionTableType.Unknown;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnknownPartitionTable"/> class.
    /// </summary>
    /// <param name="disk">The raw disk, or null if not available.</param>
    public UnknownPartitionTable(IRawDisk? disk)
    {
        m_rawDisk = disk;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IPartition> EnumeratePartitions([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var partition = await GetPartitionAsync(1, cancellationToken).ConfigureAwait(false);
        if (partition != null)
            yield return partition;
    }

    /// <inheritdoc />
    public Task<IPartition?> GetPartitionAsync(int partitionNumber, CancellationToken cancellationToken)
    {
        if (partitionNumber != 1 || m_rawDisk == null)
            return Task.FromResult<IPartition?>(null);

        return Task.FromResult<IPartition?>(new BasePartition
        {
            PartitionNumber = 1,
            Type = PartitionType.Unknown,
            PartitionTable = this,
            StartOffset = 0,
            Size = m_rawDisk.Size,
            Name = null,
            FilesystemType = FileSystemType.Unknown,
            VolumeGuid = null,
            RawDisk = m_rawDisk,
            StartingLba = 0,
            EndingLba = 0,
            Attributes = 0
        });
    }

    /// <inheritdoc />
    public Task<Stream> GetProtectiveMbrAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Unknown partition table does not have a protective MBR.");
    }

    /// <inheritdoc />
    public Task<Stream> GetPartitionTableDataAsync(CancellationToken cancellationToken)
    {
        // For unknown partition tables, return empty data
        // The disk will be treated as one big partition with unknown filesystem
        return Task.FromResult<Stream>(new MemoryStream(Array.Empty<byte>(), writable: false));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
    }
}
