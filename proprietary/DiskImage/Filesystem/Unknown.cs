
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Partition;

namespace Duplicati.Proprietary.DiskImage.Filesystem;

public sealed record UnkownFilesystemMetadata
{
    public int BlockSize { get; init; }
}

public class UnknownFilesystemFile : IFile
{
    public string? Path => null;
    public long? Address { get; init; }
    public long Size { get; init; }
    public bool IsDirectory => false;
}

public class UnknownFilesystem : IFilesystem
{
    public FileSystemType Type => FileSystemType.Unknown;

    public IPartition Partition { get => m_partition; }

    private int m_blockSize;
    private bool m_disposed = false;
    private readonly IPartition m_partition;

    public UnknownFilesystem(IPartition partition, int blockSize = 1024 * 1024 * 1024)
    {
        m_partition = partition;
        if (blockSize < 0 || blockSize % partition.PartitionTable.RawDisk?.SectorSize != 0)
            throw new ArgumentException("Block size must be non-negative and a multiple of the partition sector size.", nameof(blockSize));
        m_blockSize = blockSize;
    }

    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
    }

    public Task<object?> GetFilesystemMetadataAsync(CancellationToken cancellationToken)
    {
        var cfg = new UnkownFilesystemMetadata
        {
            BlockSize = m_blockSize,
        };

        return Task.FromResult<object?>(cfg);
    }

    public async IAsyncEnumerable<IFile> ListFilesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long totalSize = m_partition.Size;
        long blockSize = m_blockSize;
        long blockCount = (totalSize + blockSize - 1) / blockSize;

        for (long i = 0; i < blockCount; i++)
        {
            long address = m_partition.StartOffset + i * blockSize;
            long size = Math.Min(blockSize, totalSize - i * blockSize);
            yield return new UnknownFilesystemFile()
            {
                Address = address,
                Size = size
            };
        }
    }

    public async IAsyncEnumerable<IFile> ListFilesAsync(IFile directory, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (directory is not UnknownFilesystemFile)
            throw new ArgumentException("The specified directory does not belong to this filesystem.", nameof(directory));
        if (!directory.IsDirectory)
            throw new ArgumentException("The specified file is not a directory.", nameof(directory));

        yield break;
    }

    public async Task<Stream> OpenFileAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not UnknownFilesystemFile)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (m_partition.PartitionTable.RawDisk == null)
            throw new InvalidOperationException("Cannot read from partition without a raw disk.");

        long address = file.Address ?? throw new ArgumentException("The specified file does not have a valid address.", nameof(file));

        var startSector = address / m_partition.PartitionTable.RawDisk.SectorSize;
        var sectorCount = (file.Size + m_partition.PartitionTable.RawDisk.SectorSize - 1) / m_partition.PartitionTable.RawDisk.SectorSize;
        using var partitionStream = await m_partition.PartitionTable.RawDisk.ReadSectorsAsync(startSector, (int)sectorCount, cancellationToken);
        var buffer = new byte[file.Size];
        await partitionStream.ReadExactlyAsync(buffer, 0, buffer.Length, cancellationToken);
        return new MemoryStream(buffer);
    }
}