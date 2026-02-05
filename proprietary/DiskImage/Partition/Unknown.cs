using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Disk;

namespace Duplicati.Proprietary.DiskImage.Partition;

public class UnknownPartition : IPartition
{
    public IPartitionTable PartitionTable { get; }
    public int PartitionNumber => 1;
    public PartitionType Type => PartitionType.Unknown;
    public long StartOffset => 0;
    public long Size { get; }
    public string? Name => null;
    public FileSystemType FilesystemType => FileSystemType.Unknown;
    public Guid? VolumeGuid => null;

    public UnknownPartition(IPartitionTable partitionTable, long size)
    {
        PartitionTable = partitionTable;
        Size = size;
    }

    public async Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
    {
        if (PartitionTable.RawDisk == null)
            throw new InvalidOperationException("No raw disk available for reading partition data.");

        return await PartitionTable.RawDisk.ReadBytesAsync(StartOffset, (int)Size, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        // Nothing to dispose here
    }
}

public class UnknownPartitionTable : IPartitionTable
{
    private readonly IRawDisk? m_rawDisk;
    private bool m_disposed = false;

    public IRawDisk? RawDisk => m_rawDisk;
    public PartitionTableType TableType => PartitionTableType.Unknown;

    public UnknownPartitionTable(IRawDisk? disk)
    {
        m_rawDisk = disk;
    }

    public async IAsyncEnumerable<IPartition> EnumeratePartitions([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var partition = await GetPartitionAsync(1, cancellationToken).ConfigureAwait(false);
        if (partition != null)
            yield return partition;
    }

    public Task<IPartition?> GetPartitionAsync(int partitionNumber, CancellationToken cancellationToken)
    {
        if (partitionNumber != 1 || m_rawDisk == null)
            return Task.FromResult<IPartition?>(null);

        return Task.FromResult<IPartition?>(new UnknownPartition(this, m_rawDisk.Size));
    }

    public Task<Stream> GetProtectiveMbrAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Unknown partition table does not have a protective MBR.");
    }

    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
    }
}
