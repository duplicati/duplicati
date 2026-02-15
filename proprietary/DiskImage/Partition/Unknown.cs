using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Disk;

namespace Duplicati.Proprietary.DiskImage.Partition;

/// <summary>
/// Represents an unknown partition type.
/// Used when the partition type cannot be determined.
/// </summary>
public class UnknownPartition : IPartition
{
    /// <inheritdoc />
    public IPartitionTable PartitionTable { get; }

    /// <inheritdoc />
    public int PartitionNumber => 1;

    /// <inheritdoc />
    public PartitionType Type => PartitionType.Unknown;

    /// <inheritdoc />
    public long StartOffset => 0;

    /// <inheritdoc />
    public long Size { get; }

    /// <inheritdoc />
    public string? Name => null;

    /// <inheritdoc />
    public FileSystemType FilesystemType => FileSystemType.Unknown;

    /// <inheritdoc />
    public Guid? VolumeGuid => null;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnknownPartition"/> class.
    /// </summary>
    /// <param name="partitionTable">The parent partition table.</param>
    /// <param name="size">The partition size in bytes.</param>
    public UnknownPartition(IPartitionTable partitionTable, long size)
    {
        PartitionTable = partitionTable;
        Size = size;
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
    {
        if (PartitionTable.RawDisk == null)
            throw new InvalidOperationException("No raw disk available for reading partition data.");

        return await PartitionTable.RawDisk.ReadBytesAsync(StartOffset, (int)Size, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken)
    {
        if (PartitionTable.RawDisk == null)
            throw new InvalidOperationException("No raw disk available for writing partition data.");
        return Task.FromResult<Stream>(new PartitionWriteStream(PartitionTable.RawDisk, StartOffset, Size));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to dispose here
    }

}

/// <summary>
/// Represents an unknown partition table type.
/// Treats the entire disk as a single partition when the partition table type cannot be determined.
/// </summary>
public class UnknownPartitionTable : IPartitionTable
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

        return Task.FromResult<IPartition?>(new UnknownPartition(this, m_rawDisk.Size));
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
