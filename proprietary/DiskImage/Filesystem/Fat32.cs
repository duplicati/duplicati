// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.General;
using Duplicati.Proprietary.DiskImage.Partition;

namespace Duplicati.Proprietary.DiskImage.Filesystem;

/// <summary>
/// Metadata for FAT32 filesystems, storing cluster and block size information.
/// </summary>
public sealed record Fat32FilesystemMetadata
{
    /// <summary>
    /// Gets the block size used for reading/writing data.
    /// </summary>
    public int BlockSize { get; init; }

    /// <summary>
    /// Gets the size of a cluster in bytes.
    /// </summary>
    public int ClusterSize { get; init; }

    /// <summary>
    /// Gets the total number of clusters in the volume.
    /// </summary>
    public long TotalClusters { get; init; }

    /// <summary>
    /// Gets the number of allocated (in-use) clusters.
    /// </summary>
    public long AllocatedClusters { get; init; }

    /// <summary>
    /// Gets the number of free clusters.
    /// </summary>
    public long FreeClusters { get; init; }
}

/// <summary>
/// Represents a file (block) in a FAT32 filesystem.
/// </summary>
public class Fat32File : IFile
{
    /// <inheritdoc />
    public string? Path { get; init; }

    /// <inheritdoc />
    public long? Address { get; init; }

    /// <inheritdoc />
    public long Size { get; init; }

    /// <inheritdoc />
    public bool IsDirectory => false;

    /// <summary>
    /// Gets the last modification timestamp of the block.
    /// This is the maximum timestamp of any file whose clusters overlap this block.
    /// </summary>
    public DateTime? LastModified { get; init; }

    /// <summary>
    /// Gets a value indicating whether the block contains any allocated clusters.
    /// </summary>
    public bool IsAllocated { get; init; }
}

/// <summary>
/// Represents a FAT32 filesystem.
/// Provides filesystem-aware block-level access with cluster allocation tracking.
/// </summary>
internal class Fat32Filesystem : IFilesystem
{
    /// <summary>
    /// Cache of zero buffers keyed by block size.
    /// </summary>
    private static readonly ConcurrentDictionary<int, byte[]> s_zeroBuffers = new();

    /// <inheritdoc />
    public FileSystemType Type => FileSystemType.FAT32;

    /// <inheritdoc />
    public IPartition Partition { get; }

    /// <summary>
    /// Gets the block size used for reading/writing data.
    /// </summary>
    private readonly int m_blockSize;

    /// <summary>
    /// The parsed FAT32 boot sector.
    /// </summary>
    private readonly Fat32BootSector m_bootSector;

    /// <summary>
    /// The FAT table reader.
    /// </summary>
    private readonly Fat32Table m_fatTable;

    /// <summary>
    /// The directory walker that built the cluster-to-timestamp map.
    /// </summary>
    private readonly Fat32DirectoryWalker m_directoryWalker;

    /// <summary>
    /// Maps block index to block metadata (LastModified, IsAllocated).
    /// </summary>
    private readonly BlockMetadata[] m_blockMetadata;

    /// <summary>
    /// The total number of blocks in the partition.
    /// </summary>
    private readonly long m_blockCount;

    /// <summary>
    /// Flag to indicate whether the object has been disposed.
    /// </summary>
    private bool m_disposed;

    /// <summary>
    /// Metadata for a single block.
    /// </summary>
    private readonly struct BlockMetadata
    {
        /// <summary>
        /// The last modification timestamp for this block.
        /// </summary>
        public DateTime LastModified { get; init; }

        /// <summary>
        /// Whether this block contains any allocated clusters.
        /// </summary>
        public bool IsAllocated { get; init; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Fat32Filesystem"/> class.
    /// </summary>
    /// <param name="partition">The parent partition.</param>
    /// <param name="blockSize">The block size for reading/writing (default is 1MB).</param>
    /// <exception cref="ArgumentNullException">Thrown if partition is null.</exception>
    /// <exception cref="ArgumentException">Thrown if block size is invalid.</exception>
    internal Fat32Filesystem(IPartition partition, int blockSize = 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(partition);

        Partition = partition;

        var sectorSize = partition.PartitionTable.RawDisk?.SectorSize ?? 512;
        if (blockSize <= 0 || blockSize % sectorSize != 0)
            throw new ArgumentException($"Block size must be positive and a multiple of the sector size ({sectorSize} bytes).", nameof(blockSize));

        m_blockSize = blockSize;

        // Read and parse the boot sector
        var bootSectorData = new byte[512];
        using (var stream = partition.OpenReadAsync(CancellationToken.None).Result)
        {
            stream.ReadExactly(bootSectorData);
        }
        m_bootSector = new Fat32BootSector(bootSectorData);

        // Validate block size is a multiple of cluster size
        if (m_blockSize % m_bootSector.ClusterSize != 0)
            throw new ArgumentException($"Block size ({m_blockSize}) must be a multiple of the cluster size ({m_bootSector.ClusterSize}).", nameof(blockSize));

        // Read the FAT table
        m_fatTable = new Fat32Table(partition, m_bootSector, CancellationToken.None);

        // Walk the directory tree to build cluster-to-timestamp map
        m_directoryWalker = new Fat32DirectoryWalker(partition, m_bootSector, m_fatTable, CancellationToken.None);

        // Pre-compute block metadata
        m_blockCount = (partition.Size + m_blockSize - 1) / m_blockSize;
        m_blockMetadata = BuildBlockMetadata();
    }

    /// <summary>
    /// Builds the metadata array for all blocks.
    /// </summary>
    /// <returns>An array of BlockMetadata for each block.</returns>
    private BlockMetadata[] BuildBlockMetadata()
    {
        var metadata = new BlockMetadata[m_blockCount];
        var clusterToTimestampMap = m_directoryWalker.ClusterToTimestampMap;
        var dataRegionStart = m_bootSector.DataStartOffset;
        var dataRegionEnd = dataRegionStart + (long)m_bootSector.TotalDataClusters * m_bootSector.ClusterSize;
        var fatRegionEnd = dataRegionStart;
        var now = DateTime.UtcNow;

        for (long blockIndex = 0; blockIndex < m_blockCount; blockIndex++)
        {
            long blockStart = blockIndex * m_blockSize;
            long blockEnd = Math.Min(blockStart + m_blockSize, Partition.Size);

            // Check if block is in reserved/FAT region (before data region)
            if (blockStart < fatRegionEnd)
            {
                // Reserved sectors and FAT region are always allocated
                metadata[blockIndex] = new BlockMetadata
                {
                    LastModified = now,
                    IsAllocated = true
                };
                continue;
            }

            // Check if block is beyond the data region
            if (blockStart >= dataRegionEnd)
            {
                // Beyond the data region - treat as unallocated with epoch timestamp
                metadata[blockIndex] = new BlockMetadata
                {
                    LastModified = DateTime.UnixEpoch,
                    IsAllocated = false
                };
                continue;
            }

            // Block is in data region - check which clusters fall within it
            DateTime maxTimestamp = DateTime.UnixEpoch;
            bool hasAllocatedClusters = false;

            // Calculate which clusters are in this block
            long firstClusterInBlock = 2 + (blockStart - dataRegionStart) / m_bootSector.ClusterSize;
            long lastClusterInBlock = 2 + (blockEnd - 1 - dataRegionStart) / m_bootSector.ClusterSize;

            // Clamp to valid cluster range
            firstClusterInBlock = Math.Max(2, firstClusterInBlock);
            lastClusterInBlock = Math.Min(lastClusterInBlock, m_fatTable.TotalClusters - 1);

            for (long cluster = firstClusterInBlock; cluster <= lastClusterInBlock; cluster++)
            {
                if (m_fatTable.IsClusterAllocated((uint)cluster))
                {
                    hasAllocatedClusters = true;

                    // Get the timestamp for this cluster
                    if (clusterToTimestampMap.TryGetValue((uint)cluster, out var timestamp))
                    {
                        if (timestamp > maxTimestamp)
                        {
                            maxTimestamp = timestamp;
                        }
                    }
                    else
                    {
                        // Allocated cluster without a timestamp - use current time
                        maxTimestamp = now;
                    }
                }
            }

            metadata[blockIndex] = new BlockMetadata
            {
                LastModified = maxTimestamp,
                IsAllocated = hasAllocatedClusters
            };
        }

        return metadata;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
    }

    /// <inheritdoc />
    public Task<object?> GetFilesystemMetadataAsync(CancellationToken cancellationToken)
    {
        var metadata = new Fat32FilesystemMetadata
        {
            BlockSize = m_blockSize,
            ClusterSize = m_bootSector.ClusterSize,
            TotalClusters = m_fatTable.TotalClusters,
            AllocatedClusters = m_fatTable.AllocatedClusters,
            FreeClusters = m_fatTable.FreeClusters
        };

        return Task.FromResult<object?>(metadata);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IFile> ListFilesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (long i = 0; i < m_blockCount; i++)
        {
            long address = i * m_blockSize;
            long size = Math.Min(m_blockSize, Partition.Size - i * m_blockSize);
            var blockMeta = m_blockMetadata[i];

            if (size > 0)
            {
                yield return new Fat32File()
                {
                    Path = $"block_{i:X016}",
                    Address = address,
                    Size = size,
                    LastModified = blockMeta.LastModified,
                    IsAllocated = blockMeta.IsAllocated
                };
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IFile> ListFilesAsync(IFile directory, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (directory is not Fat32File)
            throw new ArgumentException("The specified directory does not belong to this filesystem.", nameof(directory));
        if (!directory.IsDirectory)
            throw new ArgumentException("The specified file is not a directory.", nameof(directory));

        yield break;
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadStreamAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not Fat32File fat32File)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (file.IsDirectory)
            throw new ArgumentException("The specified file is a directory.", nameof(file));

        long address = fat32File.Address ?? throw new ArgumentException("File address is required.", nameof(file));
        long size = fat32File.Size;

        BoundsCheck(address, size);

        if (fat32File.IsAllocated)
        {
            return new Fat32Stream(Partition.PartitionTable.RawDisk!, Partition.StartOffset + address, size, readEnabled: true, writeEnabled: false);
        }
        else
        {
            return new Fat32ZeroStream(m_blockSize);
        }
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadStreamAsync(string path, CancellationToken cancellationToken)
    {
        long address = ParsePathToAddress(path);
        long size = Math.Min((long)m_blockSize, Partition.Size - address);

        return await OpenReadStreamAsync(new Fat32File { Address = address, Size = size, IsAllocated = true }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenWriteStreamAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not Fat32File fat32File)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (file.IsDirectory)
            throw new ArgumentException("The specified file is a directory.", nameof(file));

        long address = fat32File.Address ?? throw new ArgumentException("File address is required.", nameof(file));
        long size = fat32File.Size;

        BoundsCheck(address, size);

        // For write operations, always use the full stream (same as UnknownFilesystemStream)
        return new Fat32Stream(Partition.PartitionTable.RawDisk!, Partition.StartOffset + address, size, readEnabled: false, writeEnabled: true);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenWriteStreamAsync(string path, CancellationToken cancellationToken)
    {
        long address = ParsePathToAddress(path);
        long size = Math.Min((long)m_blockSize, Partition.Size - address);

        return await OpenWriteStreamAsync(new Fat32File { Address = address, Size = size, IsAllocated = true }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadWriteStreamAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not Fat32File fat32File)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (file.IsDirectory)
            throw new ArgumentException("The specified file is a directory.", nameof(file));

        long address = fat32File.Address ?? throw new ArgumentException("File address is required.", nameof(file));
        long size = fat32File.Size;

        BoundsCheck(address, size);

        return new Fat32Stream(Partition.PartitionTable.RawDisk!, Partition.StartOffset + address, size, readEnabled: true, writeEnabled: true);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadWriteStreamAsync(string path, CancellationToken cancellationToken)
    {
        long address = ParsePathToAddress(path);
        long size = Math.Min((long)m_blockSize, Partition.Size - address);

        return await OpenReadWriteStreamAsync(new Fat32File { Address = address, Size = size, IsAllocated = true }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<long> GetFileLengthAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not Fat32File fat32File)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (file.IsDirectory)
            throw new ArgumentException("The specified file is a directory.", nameof(file));

        return Task.FromResult(fat32File.Size);
    }

    /// <inheritdoc />
    public Task<long> GetFileLengthAsync(string path, CancellationToken cancellationToken)
    {
        long address = ParsePathToAddress(path);
        long size = Math.Min((long)m_blockSize, Partition.Size - address);

        return Task.FromResult(size);
    }

    /// <summary>
    /// Parses a file path to extract the corresponding block address.
    /// </summary>
    /// <param name="path">The file path to parse.</param>
    /// <returns>The block address corresponding to the file path.</returns>
    /// <exception cref="ArgumentException">Thrown if the path format is invalid.</exception>
    private static long ParsePathToAddress(string path)
    {
        // Path format: root/part_{PartitionTableType}_{PartitionNumber}/fs_{FileSystemType}/{Address}
        // We need to extract the part after the last slash
        var parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            throw new ArgumentException("Invalid path format.", nameof(path));

        return Convert.ToInt64(parts[^1], 16);
    }

    /// <summary>
    /// Checks if the specified range is valid for the partition.
    /// </summary>
    /// <param name="start">The starting address of the range.</param>
    /// <param name="size">The size of the range.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the start or size is negative or exceeds the partition size.</exception>
    /// <exception cref="ArgumentException">Thrown if the start or size is not a multiple of the sector size.</exception>
    public void BoundsCheck(long start, long size)
    {
        if (start < 0 || size < 0)
            throw new ArgumentOutOfRangeException("Start and size must be non-negative.");

        if (start + size > Partition.Size)
            throw new ArgumentOutOfRangeException("The specified range exceeds the partition size.");

        var sectorSize = Partition.PartitionTable.RawDisk?.SectorSize ?? 512;

        if (start % sectorSize != 0 || size % sectorSize != 0)
            throw new ArgumentException($"Start and size must be multiples of the sector size ({sectorSize} bytes).");
    }

}
