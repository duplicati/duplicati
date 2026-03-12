// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Partition;

namespace Duplicati.Proprietary.DiskImage.Filesystem;

/// <summary>
/// Walks the FAT32 directory tree to extract file metadata and build a cluster-to-timestamp map.
/// </summary>
internal class Fat32DirectoryWalker
{
    /// <summary>
    /// Attribute indicating a directory entry.
    /// </summary>
    private const byte ATTR_DIRECTORY = 0x10;

    /// <summary>
    /// Attribute indicating a long filename (LFN) entry.
    /// </summary>
    private const byte ATTR_LONG_FILENAME = 0x0F;

    /// <summary>
    /// Attribute mask for the volume label.
    /// </summary>
    private const byte ATTR_VOLUME_ID = 0x08;

    /// <summary>
    /// First byte indicating a deleted directory entry.
    /// </summary>
    private const byte DELETED_ENTRY_MARKER = 0xE5;

    /// <summary>
    /// First byte indicating end of directory (no more entries follow).
    /// </summary>
    private const byte END_OF_DIRECTORY_MARKER = 0x00;

    /// <summary>
    /// Offset to the attributes field in a directory entry.
    /// </summary>
    private const int ENTRY_ATTRIBUTES_OFFSET = 0x0B;

    /// <summary>
    /// Offset to the creation time field in a directory entry.
    /// </summary>
    private const int ENTRY_CREATION_TIME_OFFSET = 0x0E;

    /// <summary>
    /// Offset to the creation date field in a directory entry.
    /// </summary>
    private const int ENTRY_CREATION_DATE_OFFSET = 0x10;

    /// <summary>
    /// Offset to the first cluster high word in a directory entry.
    /// </summary>
    private const int ENTRY_CLUSTER_HIGH_OFFSET = 0x14;

    /// <summary>
    /// Offset to the last write time field in a directory entry.
    /// </summary>
    private const int ENTRY_WRITE_TIME_OFFSET = 0x16;

    /// <summary>
    /// Offset to the last write date field in a directory entry.
    /// </summary>
    private const int ENTRY_WRITE_DATE_OFFSET = 0x18;

    /// <summary>
    /// Offset to the first cluster low word in a directory entry.
    /// </summary>
    private const int ENTRY_CLUSTER_LOW_OFFSET = 0x1A;

    /// <summary>
    /// Offset to the file size field in a directory entry.
    /// </summary>
    private const int ENTRY_FILE_SIZE_OFFSET = 0x1C;

    /// <summary>
    /// Size of a single directory entry in bytes.
    /// </summary>
    private const int DIRECTORY_ENTRY_SIZE = 32;

    /// <summary>
    /// The boot sector containing FAT32 geometry information.
    /// </summary>
    private readonly Fat32BootSector m_bootSector;

    /// <summary>
    /// The FAT table for looking up cluster chains.
    /// </summary>
    private readonly Fat32Table m_fatTable;

    /// <summary>
    /// The partition for reading cluster data.
    /// </summary>
    private readonly IPartition m_partition;

    /// <summary>
    /// Maps each allocated cluster to the modification time of the file that owns it.
    /// </summary>
    private readonly Dictionary<uint, DateTime> m_clusterToTimestampMap;

    /// <summary>
    /// Initializes a new instance of the <see cref="Fat32DirectoryWalker"/> class and walks the directory tree.
    /// </summary>
    /// <param name="partition">The partition containing the FAT32 filesystem.</param>
    /// <param name="bootSector">The parsed boot sector containing FAT32 geometry.</param>
    /// <param name="fatTable">The FAT table for cluster chain lookups.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
    public Fat32DirectoryWalker(IPartition partition, Fat32BootSector bootSector, Fat32Table fatTable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(fatTable);

        m_partition = partition;
        m_bootSector = bootSector;
        m_fatTable = fatTable;
        m_clusterToTimestampMap = [];

        // Walk the directory tree starting from the root cluster
        WalkDirectoryTree(bootSector.RootCluster, DateTime.UnixEpoch, cancellationToken);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Fat32DirectoryWalker"/> class for testing.
    /// This constructor allows direct specification of cluster data without reading from a partition.
    /// </summary>
    /// <param name="bootSector">The parsed boot sector containing FAT32 geometry.</param>
    /// <param name="fatTable">The FAT table for cluster chain lookups.</param>
    /// <param name="clusterData">A function that returns cluster data for a given cluster number.</param>
    /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
    internal Fat32DirectoryWalker(Fat32BootSector bootSector, Fat32Table fatTable, Func<uint, byte[]> clusterData)
    {
        ArgumentNullException.ThrowIfNull(fatTable);
        ArgumentNullException.ThrowIfNull(clusterData);

        m_partition = null!;
        m_bootSector = bootSector;
        m_fatTable = fatTable;
        m_clusterToTimestampMap = [];

        // Walk the directory tree starting from the root cluster using the provided cluster data
        WalkDirectoryTreeWithData(bootSector.RootCluster, DateTime.UnixEpoch, clusterData);
    }

    /// <summary>
    /// Gets the cluster-to-timestamp map. Each allocated cluster is mapped to the modification time
    /// of the file or directory that owns it.
    /// </summary>
    public IReadOnlyDictionary<uint, DateTime> ClusterToTimestampMap => m_clusterToTimestampMap;

    /// <summary>
    /// Recursively walks the directory tree starting from the specified cluster.
    /// </summary>
    /// <param name="startCluster">The starting cluster number (root or subdirectory).</param>
    /// <param name="directoryTimestamp">The modification timestamp of the parent directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private void WalkDirectoryTree(uint startCluster, DateTime directoryTimestamp, CancellationToken cancellationToken)
    {
        using var stream = m_partition.OpenReadAsync(cancellationToken).Result;

        byte[] clusterData(uint clusterNum)
        {
            var data = new byte[m_bootSector.ClusterSize];
            var offset = m_bootSector.ClusterToByteOffset(clusterNum);
            stream.Seek(offset, SeekOrigin.Begin);
            stream.ReadExactly(data);
            return data;
        }

        WalkDirectoryTreeWithData(startCluster, directoryTimestamp, clusterData);
    }

    /// <summary>
    /// Recursively walks the directory tree using provided cluster data.
    /// </summary>
    /// <param name="startCluster">The starting cluster number (root or subdirectory).</param>
    /// <param name="directoryTimestamp">The modification timestamp of the parent directory.</param>
    /// <param name="clusterData">Function to get cluster data.</param>
    private void WalkDirectoryTreeWithData(uint startCluster, DateTime directoryTimestamp, Func<uint, byte[]> clusterData)
    {
        // Get the cluster chain for this directory
        var clusterChain = m_fatTable.GetClusterChain(startCluster);

        foreach (var cluster in clusterChain)
        {
            // Mark the directory's own cluster with its timestamp
            if (!m_clusterToTimestampMap.ContainsKey(cluster))
            {
                m_clusterToTimestampMap[cluster] = directoryTimestamp;
            }

            // Read and process the directory entries in this cluster
            var data = clusterData(cluster);
            ProcessDirectoryEntries(data, directoryTimestamp, clusterData);
        }
    }

    /// <summary>
    /// Processes directory entries from raw cluster data.
    /// </summary>
    /// <param name="data">The raw cluster data containing directory entries.</param>
    /// <param name="directoryTimestamp">The modification timestamp of the parent directory.</param>
    /// <param name="clusterData">Function to get cluster data for subdirectories.</param>
    private void ProcessDirectoryEntries(byte[] data, DateTime directoryTimestamp, Func<uint, byte[]> clusterData)
    {
        var entryCount = data.Length / DIRECTORY_ENTRY_SIZE;

        for (int i = 0; i < entryCount; i++)
        {
            var entryOffset = i * DIRECTORY_ENTRY_SIZE;
            var entrySpan = data.AsSpan(entryOffset, DIRECTORY_ENTRY_SIZE);

            // Check for end of directory marker
            if (entrySpan[0] == END_OF_DIRECTORY_MARKER)
                break;

            // Skip deleted entries
            if (entrySpan[0] == DELETED_ENTRY_MARKER)
                continue;

            // Get the attribute byte
            var attributes = entrySpan[ENTRY_ATTRIBUTES_OFFSET];

            // Skip long filename entries
            if (attributes == ATTR_LONG_FILENAME)
                continue;

            // Skip volume labels
            if ((attributes & ATTR_VOLUME_ID) == ATTR_VOLUME_ID)
                continue;

            // Check if this is a directory
            var isDirectory = (attributes & ATTR_DIRECTORY) == ATTR_DIRECTORY;

            // Extract the starting cluster number
            var clusterHigh = BinaryPrimitives.ReadUInt16LittleEndian(entrySpan.Slice(ENTRY_CLUSTER_HIGH_OFFSET, 2));
            var clusterLow = BinaryPrimitives.ReadUInt16LittleEndian(entrySpan.Slice(ENTRY_CLUSTER_LOW_OFFSET, 2));
            var startCluster = (uint)((clusterHigh << 16) | clusterLow);

            // Skip entries with invalid cluster numbers (0 means unallocated for files)
            if (startCluster < 2)
                continue;

            // Extract the filename to check for . and .. entries
            var filename = GetShortFilename(entrySpan);

            // Skip . and .. entries to avoid infinite recursion
            if (filename == "." || filename == "..")
                continue;

            // Extract the last write timestamp
            var lastWriteTime = ParseDateTime(
                BinaryPrimitives.ReadUInt16LittleEndian(entrySpan.Slice(ENTRY_WRITE_DATE_OFFSET, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(entrySpan.Slice(ENTRY_WRITE_TIME_OFFSET, 2)));

            // Use the file's own timestamp, or fall back to the directory's timestamp
            var fileTimestamp = lastWriteTime > DateTime.UnixEpoch ? lastWriteTime : directoryTimestamp;

            if (isDirectory)
            {
                // Recursively process subdirectories (only if the cluster is valid)
                if (startCluster < m_fatTable.TotalClusters)
                {
                    WalkDirectoryTreeWithData(startCluster, fileTimestamp, clusterData);
                }
            }
            else
            {
                // For files, map all clusters in the file's chain to the file's timestamp
                try
                {
                    // Only process if the start cluster is within valid range
                    if (startCluster >= m_fatTable.TotalClusters)
                        continue;

                    var fileClusters = m_fatTable.GetClusterChain(startCluster);
                    foreach (var fileCluster in fileClusters)
                    {
                        // For overlapping clusters, use the most recent timestamp
                        if (!m_clusterToTimestampMap.TryGetValue(fileCluster, out var existingTimestamp) ||
                            fileTimestamp > existingTimestamp)
                        {
                            m_clusterToTimestampMap[fileCluster] = fileTimestamp;
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // If we can't get the cluster chain (e.g., circular chain), skip this file
                    // but still mark the starting cluster if it's valid
                    if (startCluster < m_fatTable.TotalClusters)
                    {
                        if (!m_clusterToTimestampMap.TryGetValue(startCluster, out var existingTimestamp) ||
                            fileTimestamp > existingTimestamp)
                        {
                            m_clusterToTimestampMap[startCluster] = fileTimestamp;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets the short filename (8.3 format) from a directory entry.
    /// </summary>
    /// <param name="entry">The directory entry span.</param>
    /// <returns>The filename string.</returns>
    private static string GetShortFilename(ReadOnlySpan<byte> entry)
    {
        // Short filename is 11 bytes: 8 for name, 3 for extension
        var name = entry.Slice(0, 8);
        var ext = entry.Slice(8, 3);

        // Trim spaces and construct the filename
        var nameStr = System.Text.Encoding.ASCII.GetString(name).TrimEnd();
        var extStr = System.Text.Encoding.ASCII.GetString(ext).TrimEnd();

        if (string.IsNullOrEmpty(extStr))
            return nameStr;

        return $"{nameStr}.{extStr}";
    }

    /// <summary>
    /// Parses FAT32 date and time fields into a DateTime.
    /// </summary>
    /// <param name="date">The date field (bits 15-9 = year-1980, bits 8-5 = month, bits 4-0 = day).</param>
    /// <param name="time">The time field (bits 15-11 = hours, bits 10-5 = minutes, bits 4-0 = seconds/2).</param>
    /// <returns>The parsed DateTime in UTC.</returns>
    internal static DateTime ParseDateTime(ushort date, ushort time)
    {
        // If both date and time are 0, return Unix epoch
        if (date == 0 && time == 0)
            return DateTime.UnixEpoch;

        // Extract date components
        var year = 1980 + ((date >> 9) & 0x7F);
        var month = (date >> 5) & 0x0F;
        var day = date & 0x1F;

        // Extract time components
        var hour = (time >> 11) & 0x1F;
        var minute = (time >> 5) & 0x3F;
        var second = (time & 0x1F) * 2;

        // Validate the values
        if (year < 1980 || year > 2107)
            return DateTime.UnixEpoch;
        if (month < 1 || month > 12)
            return DateTime.UnixEpoch;
        if (day < 1 || day > 31)
            return DateTime.UnixEpoch;
        if (hour > 23)
            return DateTime.UnixEpoch;
        if (minute > 59)
            return DateTime.UnixEpoch;
        if (second > 59)
            return DateTime.UnixEpoch;

        try
        {
            // Create DateTime in UTC (FAT32 doesn't store timezone info)
            var localTime = new DateTime((int)year, (int)month, (int)day, (int)hour, (int)minute, (int)second, DateTimeKind.Utc);
            return localTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.UnixEpoch;
        }
    }
}
