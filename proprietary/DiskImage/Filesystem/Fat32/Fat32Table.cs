// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Partition;

namespace Duplicati.Proprietary.DiskImage.Filesystem;

/// <summary>
/// Reads and provides access to the FAT32 File Allocation Table (FAT).
/// This class reads the FAT from disk and provides methods to query cluster allocation state
/// and follow cluster chains.
/// </summary>
internal class Fat32Table
{
    /// <summary>
    /// Value indicating a free cluster.
    /// </summary>
    public const uint FREE_CLUSTER = 0x00000000;

    /// <summary>
    /// Value indicating a bad cluster.
    /// </summary>
    public const uint BAD_CLUSTER = 0x0FFFFFF7;

    /// <summary>
    /// Minimum value indicating end of cluster chain.
    /// </summary>
    public const uint EOC_MIN = 0x0FFFFFF8;

    /// <summary>
    /// Maximum value indicating end of cluster chain.
    /// </summary>
    public const uint EOC_MAX = 0x0FFFFFFF;

    /// <summary>
    /// Mask for extracting the 28-bit FAT entry value.
    /// </summary>
    public const uint ENTRY_MASK = 0x0FFFFFFF;

    /// <summary>
    /// The boot sector containing FAT geometry information.
    /// </summary>
    private readonly Fat32BootSector m_bootSector;

    /// <summary>
    /// The raw FAT data read from disk.
    /// </summary>
    private readonly byte[] m_fatData;

    /// <summary>
    /// Bitmap for fast lookup of allocated clusters.
    /// Index 0 corresponds to cluster 0 (reserved), index 2 corresponds to cluster 2 (first data cluster).
    /// </summary>
    private readonly BitArray m_allocationBitmap;

    /// <summary>
    /// Total number of clusters in the FAT (including reserved clusters 0 and 1).
    /// </summary>
    private readonly uint m_totalClusters;

    /// <summary>
    /// Initializes a new instance of the <see cref="Fat32Table"/> class by reading the FAT from disk.
    /// </summary>
    /// <param name="partition">The partition containing the FAT32 filesystem.</param>
    /// <param name="bootSector">The parsed boot sector containing FAT geometry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if partition or bootSector is null.</exception>
    public Fat32Table(IPartition partition, Fat32BootSector bootSector, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partition);

        m_bootSector = bootSector;

        // Calculate FAT size in bytes
        var fatSizeInBytes = (long)bootSector.FatSize32 * bootSector.BytesPerSector;

        // Calculate total number of FAT entries
        m_totalClusters = (uint)(fatSizeInBytes / 4);

        // Read the entire FAT into memory
        m_fatData = new byte[fatSizeInBytes];

        using var stream = partition.OpenReadAsync(cancellationToken).Result;
        stream.Seek(bootSector.FatStartOffset, SeekOrigin.Begin);
        stream.ReadExactly(m_fatData);

        // Build the allocation bitmap
        m_allocationBitmap = BuildAllocationBitmap();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Fat32Table"/> class from raw FAT data.
    /// This constructor is primarily used for testing.
    /// </summary>
    /// <param name="bootSector">The parsed boot sector containing FAT geometry.</param>
    /// <param name="fatData">The raw FAT data.</param>
    public Fat32Table(Fat32BootSector bootSector, byte[] fatData)
    {
        ArgumentNullException.ThrowIfNull(fatData);

        m_bootSector = bootSector;
        m_fatData = fatData;
        m_totalClusters = (uint)(fatData.Length / 4);
        m_allocationBitmap = BuildAllocationBitmap();
    }

    /// <summary>
    /// Gets the total number of clusters in the FAT.
    /// </summary>
    public uint TotalClusters => m_totalClusters;

    /// <summary>
    /// Gets the number of allocated (in-use) clusters.
    /// </summary>
    public uint AllocatedClusters
    {
        get
        {
            var count = 0;
            foreach (bool bit in m_allocationBitmap)
            {
                if (bit)
                    count++;
            }
            return (uint)count;
        }
    }

    /// <summary>
    /// Gets the number of free clusters.
    /// </summary>
    public uint FreeClusters => m_totalClusters - AllocatedClusters;

    /// <summary>
    /// Checks if a cluster is allocated (in use).
    /// </summary>
    /// <param name="clusterNumber">The cluster number to check.</param>
    /// <returns>True if the cluster is allocated (in use, not free, not bad); otherwise, false.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if cluster number is out of range.</exception>
    public bool IsClusterAllocated(uint clusterNumber)
    {
        if (clusterNumber >= m_totalClusters)
            throw new ArgumentOutOfRangeException(nameof(clusterNumber), $"Cluster number {clusterNumber} is out of range. Valid range is 0 to {m_totalClusters - 1}.");

        return m_allocationBitmap[(int)clusterNumber];
    }

    /// <summary>
    /// Gets the next cluster in a cluster chain.
    /// </summary>
    /// <param name="clusterNumber">The current cluster number.</param>
    /// <returns>The next cluster number in the chain, or the end-of-chain marker if this is the last cluster.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if cluster number is out of range.</exception>
    public uint GetNextCluster(uint clusterNumber)
    {
        if (clusterNumber >= m_totalClusters)
            throw new ArgumentOutOfRangeException(nameof(clusterNumber), $"Cluster number {clusterNumber} is out of range. Valid range is 0 to {m_totalClusters - 1}.");

        var entry = GetFatEntry(clusterNumber);

        // Return the masked entry value (only lower 28 bits are valid)
        return entry & ENTRY_MASK;
    }

    /// <summary>
    /// Gets the full cluster chain starting from the specified cluster.
    /// </summary>
    /// <param name="startCluster">The starting cluster number.</param>
    /// <returns>A list of cluster numbers in the chain, including the start cluster.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if start cluster is out of range.</exception>
    /// <exception cref="InvalidOperationException">Thrown if a circular chain is detected.</exception>
    public List<uint> GetClusterChain(uint startCluster)
    {
        if (startCluster >= m_totalClusters)
            throw new ArgumentOutOfRangeException(nameof(startCluster), $"Cluster number {startCluster} is out of range. Valid range is 0 to {m_totalClusters - 1}.");

        var chain = new List<uint>();
        var visited = new HashSet<uint>();
        var currentCluster = startCluster;

        while (true)
        {
            // Check for circular chain
            if (!visited.Add(currentCluster))
                throw new InvalidOperationException($"Circular cluster chain detected at cluster {currentCluster}.");

            chain.Add(currentCluster);

            var nextCluster = GetNextCluster(currentCluster);

            // Check for end of chain
            if (IsEndOfChain(nextCluster))
                break;

            // Check for bad or free cluster in the middle of a chain
            if (nextCluster == BAD_CLUSTER || nextCluster == FREE_CLUSTER)
                throw new InvalidOperationException($"Invalid cluster {nextCluster:X} found in chain at cluster {currentCluster}.");

            currentCluster = nextCluster;
        }

        return chain;
    }

    /// <summary>
    /// Gets the raw FAT entry value at the specified cluster index.
    /// </summary>
    /// <param name="clusterNumber">The cluster number.</param>
    /// <returns>The raw 32-bit FAT entry value.</returns>
    private uint GetFatEntry(uint clusterNumber)
    {
        var offset = (int)(clusterNumber * 4);
        return BinaryPrimitives.ReadUInt32LittleEndian(m_fatData.AsSpan(offset, 4));
    }

    /// <summary>
    /// Builds the cluster allocation bitmap from the FAT data.
    /// </summary>
    /// <returns>A BitArray where each bit indicates whether the corresponding cluster is allocated.</returns>
    private BitArray BuildAllocationBitmap()
    {
        var bitmap = new BitArray((int)m_totalClusters);

        for (uint i = 0; i < m_totalClusters; i++)
        {
            var entry = GetFatEntry(i) & ENTRY_MASK;

            // A cluster is allocated if it's not free and not bad
            // EOC markers and valid next-cluster values all indicate allocation
            bool isAllocated = entry != FREE_CLUSTER && entry != BAD_CLUSTER;
            bitmap[(int)i] = isAllocated;
        }

        return bitmap;
    }

    /// <summary>
    /// Checks if a FAT entry value indicates end of cluster chain.
    /// </summary>
    /// <param name="entry">The FAT entry value.</param>
    /// <returns>True if the entry indicates end of chain; otherwise, false.</returns>
    private static bool IsEndOfChain(uint entry)
    {
        return entry >= EOC_MIN && entry <= EOC_MAX;
    }
}
