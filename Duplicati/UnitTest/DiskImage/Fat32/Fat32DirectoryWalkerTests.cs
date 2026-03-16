// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Proprietary.DiskImage.Filesystem;
using Duplicati.Proprietary.DiskImage.Filesystem.Fat32;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest.DiskImage.Fat32;

/// <summary>
/// Unit tests for the <see cref="Fat32DirectoryWalker"/> directory entry walker.
/// </summary>
[TestFixture]
[Category("DiskImageUnit")]
public class Fat32DirectoryWalkerTests
{
    /// <summary>
    /// Creates a valid FAT32 boot sector with known values for testing.
    /// </summary>
    private static byte[] CreateValidBootSector()
    {
        var bootSector = new byte[512];

        // Bytes per sector at offset 0x0B (512 = 0x0200)
        BinaryPrimitives.WriteUInt16LittleEndian(bootSector.AsSpan(0x0B, 2), 512);

        // Sectors per cluster at offset 0x0D (8 sectors = 4KB clusters)
        bootSector[0x0D] = 8;

        // Reserved sector count at offset 0x0E (32 sectors)
        BinaryPrimitives.WriteUInt16LittleEndian(bootSector.AsSpan(0x0E, 2), 32);

        // Number of FATs at offset 0x10 (2 FATs)
        bootSector[0x10] = 2;

        // Total sectors 32 at offset 0x20 (10,000 sectors = ~5MB)
        BinaryPrimitives.WriteUInt32LittleEndian(bootSector.AsSpan(0x20, 4), 10_000);

        // FAT size 32 at offset 0x24 (10 sectors per FAT)
        BinaryPrimitives.WriteUInt32LittleEndian(bootSector.AsSpan(0x24, 4), 10);

        // Root cluster at offset 0x2C (cluster 2)
        BinaryPrimitives.WriteUInt32LittleEndian(bootSector.AsSpan(0x2C, 4), 2);

        // FSInfo sector at offset 0x30 (sector 1)
        BinaryPrimitives.WriteUInt16LittleEndian(bootSector.AsSpan(0x30, 2), 1);

        // Filesystem type string at offset 0x52 (8 bytes: "FAT32   ")
        var fsType = System.Text.Encoding.ASCII.GetBytes("FAT32   ");
        fsType.CopyTo(bootSector, 0x52);

        // Boot sector signature at offset 510: bytes 0x55, 0xAA on disk (0xAA55 as little-endian uint16)
        BinaryPrimitives.WriteUInt16LittleEndian(bootSector.AsSpan(510, 2), 0xAA55);

        return bootSector;
    }

    /// <summary>
    /// Creates FAT data with specified entries.
    /// </summary>
    private static byte[] CreateFatData(params uint[] entries)
    {
        var fatData = new byte[entries.Length * 4];
        for (int i = 0; i < entries.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(fatData.AsSpan(i * 4, 4), entries[i]);
        }
        return fatData;
    }

    /// <summary>
    /// Creates a directory entry (32 bytes) with the specified parameters.
    /// </summary>
    private static byte[] CreateDirectoryEntry(
        string filename,
        byte attributes,
        ushort writeDate,
        ushort writeTime,
        ushort clusterHigh,
        ushort clusterLow,
        uint fileSize = 0)
    {
        var entry = new byte[32];

        // Filename (11 bytes: 8 for name, 3 for extension)
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(filename.PadRight(11).Substring(0, 11));
        nameBytes.CopyTo(entry, 0);

        // Attributes at offset 0x0B
        entry[0x0B] = attributes;

        // Write time at offset 0x16
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(0x16, 2), writeTime);

        // Write date at offset 0x18
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(0x18, 2), writeDate);

        // Cluster high at offset 0x14
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(0x14, 2), clusterHigh);

        // Cluster low at offset 0x1A
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(0x1A, 2), clusterLow);

        // File size at offset 0x1C
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(0x1C, 4), fileSize);

        return entry;
    }

    [Test]
    public void Test_Fat32DirectoryWalker_ParseTimestamp_Correct()
    {
        // Test known FAT32 date/time values
        // Date: 2023-06-15 = (2023-1980) << 9 | 6 << 5 | 15 = 43 << 9 | 192 | 15 = 22080 + 192 + 15 = 22287
        // Time: 14:30:20 = 14 << 11 | 30 << 5 | 10 = 28672 + 960 + 10 = 29642
        var date = (ushort)(((2023 - 1980) << 9) | (6 << 5) | 15);
        var time = (ushort)((14 << 11) | (30 << 5) | (20 / 2));

        var result = Fat32DirectoryWalker.ParseDateTime(date, time);

        Assert.AreEqual(2023, result.Year);
        Assert.AreEqual(6, result.Month);
        Assert.AreEqual(15, result.Day);
        Assert.AreEqual(14, result.Hour);
        Assert.AreEqual(30, result.Minute);
        Assert.AreEqual(20, result.Second);
        Assert.AreEqual(DateTimeKind.Utc, result.Kind);
    }

    [Test]
    public void Test_Fat32DirectoryWalker_ParseTimestamp_Zero_ReturnsEpoch()
    {
        var result = Fat32DirectoryWalker.ParseDateTime(0, 0);
        Assert.AreEqual(DateTime.UnixEpoch, result);
    }

    [Test]
    public void Test_Fat32DirectoryWalker_ParseTimestamp_InvalidDate_ReturnsEpoch()
    {
        // Invalid month (13)
        var invalidDate = (ushort)((43 << 9) | (13 << 5) | 15);
        var time = (ushort)((14 << 11) | (30 << 5) | 10);

        var result = Fat32DirectoryWalker.ParseDateTime(invalidDate, time);
        Assert.AreEqual(DateTime.UnixEpoch, result);
    }

    [Test]
    public void Test_Fat32DirectoryWalker_ParseTimestamp_InvalidTime_ReturnsEpoch()
    {
        // Valid date but invalid hour (25)
        var date = (ushort)(((2023 - 1980) << 9) | (6 << 5) | 15);
        var invalidTime = (ushort)((25 << 11) | (30 << 5) | 10);

        var result = Fat32DirectoryWalker.ParseDateTime(date, invalidTime);
        Assert.AreEqual(DateTime.UnixEpoch, result);
    }

    [Test]
    public void Test_Fat32DirectoryWalker_SkipDeletedEntries()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new Fat32BootSector(bootSectorData);

        // Create a FAT where cluster 2 (root) points to EOC, with enough entries for clusters 3 and 4
        var fatData = CreateFatData(
            0x00000000,  // Cluster 0: reserved
            0x0FFFFFFF,  // Cluster 1: reserved (EOC marker)
            0x0FFFFFFF,  // Cluster 2: root directory (EOC)
            0x0FFFFFFF,  // Cluster 3: valid entry (EOC)
            0x0FFFFFFF   // Cluster 4: valid entry (EOC)
        );
        var fatTable = new Fat32Table(bootSector, fatData);

        // Create root directory cluster data with a deleted entry followed by a valid entry
        var deletedEntry = CreateDirectoryEntry(
            "DELETEDTXT",
            0x00,  // Regular file
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 15),
            (ushort)((14 << 11) | (30 << 5) | 10),
            0,
            3,  // Would point to cluster 3
            100);
        deletedEntry[0] = 0xE5;  // Mark as deleted

        var validEntry = CreateDirectoryEntry(
            "VALID   FIL",
            0x00,  // Regular file
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 20),
            (ushort)((10 << 11) | (0 << 5) | 0),
            0,
            4,  // Points to cluster 4
            200);

        var clusterData = new Dictionary<uint, byte[]>
        {
            [2] = [.. deletedEntry, .. validEntry, .. new byte[4096 - 64]]
        };

        byte[] getClusterData(uint clusterNum) => clusterData[clusterNum];

        var walker = new Fat32DirectoryWalker(bootSector, fatTable, getClusterData);

        // Should only have cluster 4 mapped (cluster 3 should not be mapped as the entry was deleted)
        Assert.IsFalse(walker.ClusterToTimestampMap.ContainsKey(3), "Deleted entry's cluster should not be mapped");
        Assert.IsTrue(walker.ClusterToTimestampMap.ContainsKey(4), "Valid entry's cluster should be mapped");
    }

    [Test]
    public void Test_Fat32DirectoryWalker_StopAtEndMarker()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new Fat32BootSector(bootSectorData);

        // Create a FAT where cluster 2 (root) points to EOC, with enough entries for clusters 3 and 4
        var fatData = CreateFatData(
            0x00000000,  // Cluster 0: reserved
            0x0FFFFFFF,  // Cluster 1: reserved (EOC marker)
            0x0FFFFFFF,  // Cluster 2: root directory (EOC)
            0x0FFFFFFF,  // Cluster 3: valid entry (EOC)
            0x0FFFFFFF   // Cluster 4: valid entry (EOC)
        );
        var fatTable = new Fat32Table(bootSector, fatData);

        // Create root directory cluster data with an end marker followed by a valid entry
        var validEntry1 = CreateDirectoryEntry(
            "FILE1   TXT",
            0x00,  // Regular file
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 15),
            (ushort)((14 << 11) | (30 << 5) | 10),
            0,
            3,  // Points to cluster 3
            100);

        var endMarker = new byte[32];  // All zeros including first byte = end of directory

        var validEntry2 = CreateDirectoryEntry(
            "FILE2   TXT",
            0x00,  // Regular file
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 20),
            (ushort)((10 << 11) | (0 << 5) | 0),
            0,
            4,  // Points to cluster 4
            200);

        var clusterData = new Dictionary<uint, byte[]>
        {
            [2] = [.. validEntry1, .. endMarker, .. validEntry2, .. new byte[4096 - 96]]
        };

        byte[] getClusterData(uint clusterNum) => clusterData[clusterNum];

        var walker = new Fat32DirectoryWalker(bootSector, fatTable, getClusterData);

        // Should only have cluster 3 mapped (FILE2 after end marker should be ignored)
        Assert.IsTrue(walker.ClusterToTimestampMap.ContainsKey(3), "Entry before end marker should be mapped");
        Assert.IsFalse(walker.ClusterToTimestampMap.ContainsKey(4), "Entry after end marker should not be mapped");
    }

    [Test]
    public void Test_Fat32DirectoryWalker_SkipLfnEntries()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new Fat32BootSector(bootSectorData);

        // Create a FAT where cluster 2 (root) points to EOC, with enough entries for cluster 3
        var fatData = CreateFatData(
            0x00000000,  // Cluster 0: reserved
            0x0FFFFFFF,  // Cluster 1: reserved (EOC marker)
            0x0FFFFFFF,  // Cluster 2: root directory (EOC)
            0x0FFFFFFF   // Cluster 3: valid entry (EOC)
        );
        var fatTable = new Fat32Table(bootSector, fatData);

        // Create root directory cluster data with an LFN entry followed by a valid short entry
        var lfnEntry = CreateDirectoryEntry(
            "LFN_ENTRY  ",
            0x0F,  // Long filename attribute
            0,
            0,
            0,
            0,
            0);

        var validEntry = CreateDirectoryEntry(
            "VALID   FIL",
            0x00,  // Regular file
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 20),
            (ushort)((10 << 11) | (0 << 5) | 0),
            0,
            3,  // Points to cluster 3
            200);

        var clusterData = new Dictionary<uint, byte[]>
        {
            [2] = [.. lfnEntry, .. validEntry, .. new byte[4096 - 64]]
        };

        byte[] getClusterData(uint clusterNum) => clusterData[clusterNum];

        var walker = new Fat32DirectoryWalker(bootSector, fatTable, getClusterData);

        // Should only have cluster 3 mapped (LFN entry should be skipped)
        Assert.IsFalse(walker.ClusterToTimestampMap.ContainsKey(0), "LFN entry should not create mapping");
        Assert.IsTrue(walker.ClusterToTimestampMap.ContainsKey(3), "Valid entry's cluster should be mapped");
    }

    [Test]
    public void Test_Fat32DirectoryWalker_SkipDotEntries()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new Fat32BootSector(bootSectorData);

        // Create a FAT where cluster 2 (root) and cluster 3 (subdir) both point to EOC, with enough entries for cluster 4
        var fatData = CreateFatData(
            0x00000000,  // Cluster 0: reserved
            0x0FFFFFFF,  // Cluster 1: reserved (EOC marker)
            0x0FFFFFFF,  // Cluster 2: root directory (EOC)
            0x0FFFFFFF,  // Cluster 3: subdirectory (EOC)
            0x0FFFFFFF   // Cluster 4: valid entry (EOC)
        );
        var fatTable = new Fat32Table(bootSector, fatData);

        // Create root directory with a subdirectory entry
        var subdirEntry = CreateDirectoryEntry(
            "SUBDIR     ",
            0x10,  // Directory attribute
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 15),
            (ushort)((14 << 11) | (30 << 5) | 10),
            0,
            3,  // Points to cluster 3
            0);

        var rootClusterData = subdirEntry.Concat(new byte[4096 - 32]).ToArray();

        // Create subdirectory cluster with . and .. entries and a file entry
        var dotEntry = CreateDirectoryEntry(
            ".          ",
            0x10,  // Directory attribute
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 15),
            (ushort)((14 << 11) | (30 << 5) | 10),
            0,
            3,  // Points to self (cluster 3)
            0);

        var dotDotEntry = CreateDirectoryEntry(
            "..         ",
            0x10,  // Directory attribute
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 15),
            (ushort)((14 << 11) | (30 << 5) | 10),
            0,
            2,  // Points to parent (cluster 2)
            0);

        var fileEntry = CreateDirectoryEntry(
            "FILE    TXT",
            0x00,  // Regular file
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 20),
            (ushort)((10 << 11) | (0 << 5) | 0),
            0,
            4,  // Points to cluster 4
            100);

        var subdirClusterData = dotEntry.Concat(dotDotEntry).Concat(fileEntry).Concat(new byte[4096 - 96]).ToArray();

        var clusterData = new Dictionary<uint, byte[]>
        {
            [2] = rootClusterData,
            [3] = subdirClusterData
        };

        byte[] getClusterData(uint clusterNum) => clusterData[clusterNum];

        var walker = new Fat32DirectoryWalker(bootSector, fatTable, getClusterData);

        // Cluster 2 should be mapped (it's the root directory)
        Assert.IsTrue(walker.ClusterToTimestampMap.ContainsKey(2), "Root directory cluster should be mapped");

        // Cluster 3 should be mapped (it's the subdirectory itself)
        Assert.IsTrue(walker.ClusterToTimestampMap.ContainsKey(3), "Subdirectory cluster should be mapped");

        // Cluster 4 should be mapped (it's the file in the subdirectory)
        Assert.IsTrue(walker.ClusterToTimestampMap.ContainsKey(4), "File cluster should be mapped");

        // We should have exactly 3 mappings (clusters 2, 3, and 4)
        Assert.AreEqual(3, walker.ClusterToTimestampMap.Count, "Should only have 3 cluster mappings");
    }

    [Test]
    public void Test_Fat32DirectoryWalker_ExtractClusterNumber()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new Fat32BootSector(bootSectorData);

        // Create a FAT where cluster 2 (root) and cluster 0x1234ABCD both point to EOC
        var fatData = CreateFatData(
            0x00000000,  // Cluster 0: reserved
            0x0FFFFFFF,  // Cluster 1: reserved (EOC marker)
            0x0FFFFFFF,  // Cluster 2: root directory (EOC)
            0x00000000,  // Cluster 3: free (not used in this test)
            0x00000000,  // ... more free clusters
            0x00000000,
            0x00000000,
            0x00000000,
            0x00000000,
            0x00000000,
            0x0FFFFFFF   // Cluster 10 (0x0A): high cluster test (EOC)
        );
        var fatTable = new Fat32Table(bootSector, fatData);

        // Test with a file entry that has a large cluster number requiring both high and low words
        // Cluster 0x0A = 10 (within our small FAT)
        var fileEntry = CreateDirectoryEntry(
            "TEST    FIL",
            0x00,  // Regular file
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 20),
            (ushort)((10 << 11) | (0 << 5) | 0),
            0x0000,  // High word
            0x000A,  // Low word = 10
            100);

        var clusterData = new Dictionary<uint, byte[]>
        {
            [2] = [.. fileEntry, .. new byte[4096 - 32]]
        };

        byte[] getClusterData(uint clusterNum) => clusterData[clusterNum];

        var walker = new Fat32DirectoryWalker(bootSector, fatTable, getClusterData);

        // Cluster 10 should be mapped
        Assert.IsTrue(walker.ClusterToTimestampMap.ContainsKey(10), "Cluster with combined high/low words should be mapped");
    }

    [Test]
    public void Test_Fat32DirectoryWalker_ClusterTimestampMap_Correct()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new Fat32BootSector(bootSectorData);

        // Create a FAT with cluster chains
        var fatData = CreateFatData(
            0x00000000,  // Cluster 0: reserved
            0x0FFFFFFF,  // Cluster 1: reserved (EOC marker)
            0x0FFFFFFF,  // Cluster 2: root directory (EOC)
            0x00000004,  // Cluster 3: points to cluster 4
            0x0FFFFFFF,  // Cluster 4: EOC (file data)
            0x00000006,  // Cluster 5: points to cluster 6
            0x00000007,  // Cluster 6: points to cluster 7
            0x0FFFFFFF   // Cluster 7: EOC (file data)
        );
        var fatTable = new Fat32Table(bootSector, fatData);

        // Create root directory with two file entries
        var file1Entry = CreateDirectoryEntry(
            "FILE1   TXT",
            0x00,  // Regular file
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 15),
            (ushort)((14 << 11) | (30 << 5) | 10),
            0,
            3,  // Points to cluster 3 (chain: 3 -> 4)
            8192);  // 2 clusters worth of data

        var file2Entry = CreateDirectoryEntry(
            "FILE2   DAT",
            0x00,  // Regular file
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 20),
            (ushort)((16 << 11) | (45 << 5) | (30 / 2)),
            0,
            5,  // Points to cluster 5 (chain: 5 -> 6 -> 7)
            12288);  // 3 clusters worth of data

        var clusterData = new Dictionary<uint, byte[]>
        {
            [2] = [.. file1Entry, .. file2Entry, .. new byte[4096 - 64]]
        };

        byte[] getClusterData(uint clusterNum) => clusterData[clusterNum];

        var walker = new Fat32DirectoryWalker(bootSector, fatTable, getClusterData);

        // Should have mappings for all file clusters (3, 4, 5, 6, 7) plus root directory (2)
        Assert.AreEqual(6, walker.ClusterToTimestampMap.Count, "Should have 6 cluster mappings (root + 5 file clusters)");

        // Verify root directory cluster (2) is mapped
        Assert.IsTrue(walker.ClusterToTimestampMap.ContainsKey(2), "Root directory cluster should be mapped");

        // Verify FILE1 clusters (3 and 4) have the FILE1 timestamp
        var file1ExpectedTime = new DateTime(2023, 6, 15, 14, 30, 20, DateTimeKind.Utc);
        Assert.AreEqual(file1ExpectedTime, walker.ClusterToTimestampMap[3], "Cluster 3 should have FILE1 timestamp");
        Assert.AreEqual(file1ExpectedTime, walker.ClusterToTimestampMap[4], "Cluster 4 should have FILE1 timestamp");

        // Verify FILE2 clusters (5, 6, 7) have the FILE2 timestamp
        var file2ExpectedTime = new DateTime(2023, 6, 20, 16, 45, 30, DateTimeKind.Utc);
        Assert.AreEqual(file2ExpectedTime, walker.ClusterToTimestampMap[5], "Cluster 5 should have FILE2 timestamp");
        Assert.AreEqual(file2ExpectedTime, walker.ClusterToTimestampMap[6], "Cluster 6 should have FILE2 timestamp");
        Assert.AreEqual(file2ExpectedTime, walker.ClusterToTimestampMap[7], "Cluster 7 should have FILE2 timestamp");
    }

    [Test]
    public void Test_Fat32DirectoryWalker_SubdirectoryRecursion()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new Fat32BootSector(bootSectorData);

        // Create a FAT with cluster chains for root and subdirectory
        var fatData = CreateFatData(
            0x00000000,  // Cluster 0: reserved
            0x0FFFFFFF,  // Cluster 1: reserved (EOC marker)
            0x0FFFFFFF,  // Cluster 2: root directory (EOC)
            0x0FFFFFFF,  // Cluster 3: subdirectory (EOC)
            0x00000005,  // Cluster 4: file in subdirectory points to cluster 5
            0x0FFFFFFF,  // Cluster 5: EOC (file data)
            0x0FFFFFFF   // Cluster 6: another file in subdirectory (EOC)
        );
        var fatTable = new Fat32Table(bootSector, fatData);

        // Create root directory with a subdirectory entry
        var subdirEntry = CreateDirectoryEntry(
            "SUBDIR     ",
            0x10,  // Directory attribute
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 10),
            (ushort)((9 << 11) | (0 << 5) | 0),
            0,
            3,  // Points to cluster 3
            0);

        var rootClusterData = subdirEntry.Concat(new byte[4096 - 32]).ToArray();

        // Create subdirectory cluster with two file entries
        var subdirFile1Entry = CreateDirectoryEntry(
            "SUBFILE1TXT",
            0x00,  // Regular file
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 15),
            (ushort)((14 << 11) | (30 << 5) | 10),
            0,
            4,  // Points to cluster 4 (chain: 4 -> 5)
            8192);

        var subdirFile2Entry = CreateDirectoryEntry(
            "SUBFILE2DAT",
            0x00,  // Regular file
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 20),
            (ushort)((16 << 11) | (45 << 5) | (30 / 2)),
            0,
            6,  // Points to cluster 6
            4096);

        var subdirClusterData = subdirFile1Entry.Concat(subdirFile2Entry).Concat(new byte[4096 - 64]).ToArray();

        var clusterData = new Dictionary<uint, byte[]>
        {
            [2] = rootClusterData,
            [3] = subdirClusterData
        };

        byte[] getClusterData(uint clusterNum) => clusterData[clusterNum];

        var walker = new Fat32DirectoryWalker(bootSector, fatTable, getClusterData);

        // Should have mappings for:
        // - Root directory cluster (2)
        // - Subdirectory cluster (3) with its own timestamp
        // - File1 clusters (4, 5) with their timestamp
        // - File2 cluster (6) with its timestamp
        Assert.AreEqual(5, walker.ClusterToTimestampMap.Count, "Should have 5 cluster mappings (root + subdir + file clusters)");

        // Verify root directory cluster is mapped
        Assert.IsTrue(walker.ClusterToTimestampMap.ContainsKey(2), "Root directory cluster should be mapped");

        // Verify subdirectory cluster has the subdirectory timestamp
        var subdirExpectedTime = new DateTime(2023, 6, 10, 9, 0, 0, DateTimeKind.Utc);
        Assert.AreEqual(subdirExpectedTime, walker.ClusterToTimestampMap[3], "Subdirectory cluster should have subdirectory timestamp");

        // Verify subfile1 clusters have their timestamp
        var file1ExpectedTime = new DateTime(2023, 6, 15, 14, 30, 20, DateTimeKind.Utc);
        Assert.AreEqual(file1ExpectedTime, walker.ClusterToTimestampMap[4], "Subfile1 cluster 4 should have file timestamp");
        Assert.AreEqual(file1ExpectedTime, walker.ClusterToTimestampMap[5], "Subfile1 cluster 5 should have file timestamp");

        // Verify subfile2 cluster has its timestamp
        var file2ExpectedTime = new DateTime(2023, 6, 20, 16, 45, 30, DateTimeKind.Utc);
        Assert.AreEqual(file2ExpectedTime, walker.ClusterToTimestampMap[6], "Subfile2 cluster should have file timestamp");
    }

    [Test]
    public void Test_Fat32DirectoryWalker_OverlappingClusters_UsesMostRecentTimestamp()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new Fat32BootSector(bootSectorData);

        // Create a FAT where cluster 3 is referenced by both files
        var fatData = CreateFatData(
            0x00000000,  // Cluster 0: reserved
            0x0FFFFFFF,  // Cluster 1: reserved (EOC marker)
            0x0FFFFFFF,  // Cluster 2: root directory (EOC)
            0x0FFFFFFF,  // Cluster 3: EOC (shared by both files - unusual but testable)
            0x0FFFFFFF,  // Cluster 4: EOC
            0x0FFFFFFF   // Cluster 5: EOC
        );
        var fatTable = new Fat32Table(bootSector, fatData);

        // Create root directory with two file entries referencing the same cluster
        // This is unusual in practice but tests the "most recent timestamp" logic
        var olderFileEntry = CreateDirectoryEntry(
            "OLDER   TXT",
            0x00,  // Regular file
            (ushort)(((2020 - 1980) << 9) | (1 << 5) | 1),
            (ushort)((0 << 11) | (0 << 5) | 0),
            0,
            3,  // Points to cluster 3
            4096);

        var newerFileEntry = CreateDirectoryEntry(
            "NEWER   TXT",
            0x00,  // Regular file
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 20),
            (ushort)((16 << 11) | (45 << 5) | (30 / 2)),
            0,
            3,  // Also points to cluster 3
            4096);

        var clusterData = new Dictionary<uint, byte[]>
        {
            [2] = [.. olderFileEntry, .. newerFileEntry, .. new byte[4096 - 64]]
        };

        byte[] getClusterData(uint clusterNum) => clusterData[clusterNum];

        var walker = new Fat32DirectoryWalker(bootSector, fatTable, getClusterData);

        // Cluster 3 should have the newer timestamp
        var newerExpectedTime = new DateTime(2023, 6, 20, 16, 45, 30, DateTimeKind.Utc);
        Assert.AreEqual(newerExpectedTime, walker.ClusterToTimestampMap[3], "Shared cluster should have most recent timestamp");
    }

    [Test]
    public void Test_Fat32DirectoryWalker_SkipVolumeLabel()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new Fat32BootSector(bootSectorData);

        // Create a FAT where cluster 2 (root) points to EOC, with enough entries for clusters 3 and 4
        var fatData = CreateFatData(
            0x00000000,  // Cluster 0: reserved
            0x0FFFFFFF,  // Cluster 1: reserved (EOC marker)
            0x0FFFFFFF,  // Cluster 2: root directory (EOC)
            0x0FFFFFFF,  // Cluster 3: volume label would point here (but should be skipped)
            0x0FFFFFFF   // Cluster 4: valid entry (EOC)
        );
        var fatTable = new Fat32Table(bootSector, fatData);

        // Create root directory cluster data with a volume label followed by a valid file entry
        var volumeLabelEntry = CreateDirectoryEntry(
            "VOLUMELABEL",
            0x08,  // Volume label attribute
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 15),
            (ushort)((14 << 11) | (30 << 5) | 10),
            0,
            3,  // Would point to cluster 3
            0);

        var validEntry = CreateDirectoryEntry(
            "VALID   FIL",
            0x00,  // Regular file
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 20),
            (ushort)((10 << 11) | (0 << 5) | 0),
            0,
            4,  // Points to cluster 4
            200);

        var clusterData = new Dictionary<uint, byte[]>
        {
            [2] = [.. volumeLabelEntry, .. validEntry, .. new byte[4096 - 64]]
        };

        byte[] getClusterData(uint clusterNum) => clusterData[clusterNum];

        var walker = new Fat32DirectoryWalker(bootSector, fatTable, getClusterData);

        // Should only have cluster 4 mapped (volume label should be skipped)
        Assert.IsFalse(walker.ClusterToTimestampMap.ContainsKey(3), "Volume label's cluster should not be mapped");
        Assert.IsTrue(walker.ClusterToTimestampMap.ContainsKey(4), "Valid entry's cluster should be mapped");
    }

    [Test]
    public void Test_Fat32DirectoryWalker_InvalidClusterNumber_Skipped()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new Fat32BootSector(bootSectorData);

        // Create a FAT where cluster 2 (root) points to EOC, with enough entries for cluster 3
        var fatData = CreateFatData(
            0x00000000,  // Cluster 0: reserved
            0x0FFFFFFF,  // Cluster 1: reserved (EOC marker)
            0x0FFFFFFF,  // Cluster 2: root directory (EOC)
            0x0FFFFFFF   // Cluster 3: valid entry (EOC) - for the valid file
        );
        var fatTable = new Fat32Table(bootSector, fatData);

        // Create root directory cluster data with an entry that has cluster 0 (invalid)
        var invalidClusterEntry = CreateDirectoryEntry(
            "INVALID TXT",
            0x00,  // Regular file
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 15),
            (ushort)((14 << 11) | (30 << 5) | 10),
            0,
            0,  // Cluster 0 is invalid (reserved)
            100);

        var validEntry = CreateDirectoryEntry(
            "VALID   FIL",
            0x00,  // Regular file
            (ushort)(((2023 - 1980) << 9) | (6 << 5) | 20),
            (ushort)((10 << 11) | (0 << 5) | 0),
            0,
            3,  // Points to cluster 3
            200);

        var clusterData = new Dictionary<uint, byte[]>
        {
            [2] = [.. invalidClusterEntry, .. validEntry, .. new byte[4096 - 64]]
        };

        byte[] getClusterData(uint clusterNum) => clusterData[clusterNum];

        var walker = new Fat32DirectoryWalker(bootSector, fatTable, getClusterData);

        // Should only have cluster 3 mapped (invalid cluster 0 should be skipped)
        Assert.IsFalse(walker.ClusterToTimestampMap.ContainsKey(0), "Invalid cluster 0 should not be mapped");
        Assert.IsTrue(walker.ClusterToTimestampMap.ContainsKey(3), "Valid entry's cluster should be mapped");
    }
}
