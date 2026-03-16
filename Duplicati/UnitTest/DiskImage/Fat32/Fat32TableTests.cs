// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Duplicati.Proprietary.DiskImage.Filesystem;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest.DiskImage
{
    /// <summary>
    /// Unit tests for the <see cref="Fat32Table"/> FAT table reader.
    /// </summary>
    [TestFixture]
    [Category("DiskImageUnit")]
    public class Fat32TableTests
    {
        /// <summary>
        /// Creates a valid FAT32 boot sector with known values for testing.
        /// </summary>
        private static byte[] CreateValidBootSector()
        {
            var bootSector = new byte[512];

            // Bytes per sector at offset 0x0B (512 = 0x0200)
            BinaryPrimitives.WriteUInt16LittleEndian(bootSector.AsSpan(0x0B, 2), 512);

            // Sectors per cluster at offset 0x0D (8 sectors)
            bootSector[0x0D] = 8;

            // Reserved sector count at offset 0x0E (32 sectors)
            BinaryPrimitives.WriteUInt16LittleEndian(bootSector.AsSpan(0x0E, 2), 32);

            // Number of FATs at offset 0x10 (2 FATs)
            bootSector[0x10] = 2;

            // Total sectors 32 at offset 0x20 (1,000,000 sectors = ~512MB)
            BinaryPrimitives.WriteUInt32LittleEndian(bootSector.AsSpan(0x20, 4), 1_000_000);

            // FAT size 32 at offset 0x24 (244 sectors per FAT)
            BinaryPrimitives.WriteUInt32LittleEndian(bootSector.AsSpan(0x24, 4), 244);

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

        [Test]
        public void Test_Fat32Table_FreeCluster_NotAllocated()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            // Create FAT with cluster 2 as free
            var fatData = CreateFatData(0, 0, Fat32Table.FREE_CLUSTER);
            var fatTable = new Fat32Table(bootSector, fatData);

            Assert.IsFalse(fatTable.IsClusterAllocated(2), "Free cluster should not be allocated");
        }

        [Test]
        public void Test_Fat32Table_AllocatedCluster_IsAllocated()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            // Create FAT with cluster 2 pointing to cluster 3 (allocated)
            var fatData = CreateFatData(0, 0, 3, Fat32Table.EOC_MIN);
            var fatTable = new Fat32Table(bootSector, fatData);

            Assert.IsTrue(fatTable.IsClusterAllocated(2), "Cluster with valid next-cluster value should be allocated");
            Assert.IsTrue(fatTable.IsClusterAllocated(3), "Cluster with EOC should be allocated");
        }

        [Test]
        public void Test_Fat32Table_EndOfChain_IsAllocated()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            // Test all EOC values (0x0FFFFFF8 - 0x0FFFFFFF)
            for (uint eocValue = Fat32Table.EOC_MIN; eocValue <= Fat32Table.EOC_MAX; eocValue++)
            {
                var fatData = CreateFatData(0, 0, eocValue);
                var fatTable = new Fat32Table(bootSector, fatData);

                Assert.IsTrue(fatTable.IsClusterAllocated(2), $"Cluster with EOC value 0x{eocValue:X8} should be allocated");
            }
        }

        [Test]
        public void Test_Fat32Table_BadCluster_NotAllocated()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            // Create FAT with cluster 2 as bad
            var fatData = CreateFatData(0, 0, Fat32Table.BAD_CLUSTER);
            var fatTable = new Fat32Table(bootSector, fatData);

            Assert.IsFalse(fatTable.IsClusterAllocated(2), "Bad cluster should not be allocated");
        }

        [Test]
        public void Test_Fat32Table_GetClusterChain_SingleCluster()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            // Create FAT with cluster 2 having EOC immediately
            var fatData = CreateFatData(0, 0, Fat32Table.EOC_MIN);
            var fatTable = new Fat32Table(bootSector, fatData);

            var chain = fatTable.GetClusterChain(2);

            Assert.AreEqual(1, chain.Count, "Single cluster chain should have 1 entry");
            Assert.AreEqual(2u, chain[0], "Chain should contain the start cluster");
        }

        [Test]
        public void Test_Fat32Table_GetClusterChain_MultipleClusters()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            // Create FAT with chain: 2 -> 3 -> 4 -> EOC
            var fatData = CreateFatData(0, 0, 3, 4, Fat32Table.EOC_MIN);
            var fatTable = new Fat32Table(bootSector, fatData);

            var chain = fatTable.GetClusterChain(2);

            Assert.AreEqual(3, chain.Count, "Chain should have 3 entries");
            Assert.AreEqual(2u, chain[0], "First entry should be cluster 2");
            Assert.AreEqual(3u, chain[1], "Second entry should be cluster 3");
            Assert.AreEqual(4u, chain[2], "Third entry should be cluster 4");
        }

        [Test]
        public void Test_Fat32Table_GetClusterChain_CircularDetection()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            // Create FAT with circular chain: 2 -> 3 -> 2 (circular)
            var fatData = CreateFatData(0, 0, 3, 2);
            var fatTable = new Fat32Table(bootSector, fatData);

            var ex = Assert.Throws<InvalidOperationException>(() => fatTable.GetClusterChain(2));
            StringAssert.Contains("Circular", ex!.Message);
        }

        [Test]
        public void Test_Fat32Table_AllocationBitmap_MatchesFatEntries()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            // Create FAT with mixed entries:
            // 0: reserved (typically EOC or 0)
            // 1: reserved (typically EOC or 0)
            // 2: free
            // 3: allocated (points to 4)
            // 4: EOC (end of chain)
            // 5: bad cluster
            var fatData = CreateFatData(
                Fat32Table.FREE_CLUSTER,  // cluster 0
                Fat32Table.FREE_CLUSTER,  // cluster 1
                Fat32Table.FREE_CLUSTER,  // cluster 2 (free)
                4,                        // cluster 3 (allocated, points to 4)
                Fat32Table.EOC_MIN,       // cluster 4 (end of chain)
                Fat32Table.BAD_CLUSTER    // cluster 5 (bad)
            );
            var fatTable = new Fat32Table(bootSector, fatData);

            // Reserved clusters 0 and 1 - treated as free (not allocated)
            Assert.IsFalse(fatTable.IsClusterAllocated(0), "Cluster 0 should not be allocated");
            Assert.IsFalse(fatTable.IsClusterAllocated(1), "Cluster 1 should not be allocated");

            // Cluster 2 is free
            Assert.IsFalse(fatTable.IsClusterAllocated(2), "Cluster 2 (free) should not be allocated");

            // Cluster 3 is allocated (points to next cluster)
            Assert.IsTrue(fatTable.IsClusterAllocated(3), "Cluster 3 (allocated) should be allocated");

            // Cluster 4 is EOC
            Assert.IsTrue(fatTable.IsClusterAllocated(4), "Cluster 4 (EOC) should be allocated");

            // Cluster 5 is bad
            Assert.IsFalse(fatTable.IsClusterAllocated(5), "Cluster 5 (bad) should not be allocated");

            // Verify counts
            Assert.AreEqual(6u, fatTable.TotalClusters, "Total clusters should be 6");
            Assert.AreEqual(2u, fatTable.AllocatedClusters, "Allocated clusters should be 2 (clusters 3 and 4)");
            Assert.AreEqual(4u, fatTable.FreeClusters, "Free clusters should be 4 (clusters 0, 1, 2, and 5 is counted as free since it's bad)");
        }

        [Test]
        public void Test_Fat32Table_EntryMask_IgnoresUpperBits()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            // Create FAT with cluster 2 having upper 4 bits set (should be masked)
            // 0xF0000003 with mask 0x0FFFFFFF = 0x00000003 (points to cluster 3)
            uint entryWithUpperBits = 0xF0000003;
            var fatData = CreateFatData(0, 0, entryWithUpperBits, Fat32Table.EOC_MIN);
            var fatTable = new Fat32Table(bootSector, fatData);

            var nextCluster = fatTable.GetNextCluster(2);

            Assert.AreEqual(3u, nextCluster, "Upper 4 bits should be masked, leaving value 3");
            Assert.IsTrue(fatTable.IsClusterAllocated(2), "Cluster should be allocated even with upper bits set");
        }

        [Test]
        public void Test_Fat32Table_GetNextCluster_ReturnsMaskedValue()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            // Create FAT where cluster 2 points to cluster 5 (0x00000005)
            var fatData = CreateFatData(0, 0, 5, Fat32Table.EOC_MIN, Fat32Table.EOC_MIN, Fat32Table.EOC_MIN);
            var fatTable = new Fat32Table(bootSector, fatData);

            var nextCluster = fatTable.GetNextCluster(2);

            Assert.AreEqual(5u, nextCluster, "GetNextCluster should return the next cluster number");
        }

        [Test]
        public void Test_Fat32Table_ClusterOutOfRange_Throws()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            var fatData = CreateFatData(0, 0, Fat32Table.EOC_MIN);
            var fatTable = new Fat32Table(bootSector, fatData);

            // Try to access cluster beyond the FAT size
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => fatTable.IsClusterAllocated(100));
            StringAssert.Contains("out of range", ex!.Message);
        }

        [Test]
        public void Test_Fat32Table_GetClusterChain_StartClusterOutOfRange_Throws()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            var fatData = CreateFatData(0, 0, Fat32Table.EOC_MIN);
            var fatTable = new Fat32Table(bootSector, fatData);

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => fatTable.GetClusterChain(100));
            StringAssert.Contains("out of range", ex!.Message);
        }

        [Test]
        public void Test_Fat32Table_GetClusterChain_InvalidClusterInChain_Throws()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            // Create FAT where cluster 2 points to cluster 3 which is free (invalid in a chain)
            var fatData = CreateFatData(0, 0, 3, Fat32Table.FREE_CLUSTER);
            var fatTable = new Fat32Table(bootSector, fatData);

            var ex = Assert.Throws<InvalidOperationException>(() => fatTable.GetClusterChain(2));
            StringAssert.Contains("Invalid cluster", ex!.Message);
        }

        [Test]
        public void Test_Fat32Table_Constructor_NullFatData_Throws()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            var ex = Assert.Throws<ArgumentNullException>(() => new Fat32Table(bootSector, null!));
            Assert.AreEqual("fatData", ex!.ParamName);
        }

        [Test]
        public void Test_Fat32Table_EmptyClusterChain()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            // Create FAT with cluster 0 having EOC
            var fatData = CreateFatData(Fat32Table.EOC_MIN);
            var fatTable = new Fat32Table(bootSector, fatData);

            var chain = fatTable.GetClusterChain(0);

            Assert.AreEqual(1, chain.Count, "Single cluster chain should have 1 entry");
            Assert.AreEqual(0u, chain[0], "Chain should contain cluster 0");
        }

        [Test]
        public void Test_Fat32Table_LongClusterChain()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            // Create a chain of 10 clusters: 2 -> 3 -> 4 -> ... -> 11 -> EOC
            var entries = new List<uint> { 0, 0 }; // clusters 0 and 1
            for (uint i = 2; i <= 10; i++)
            {
                entries.Add(i + 1); // each cluster points to the next
            }
            entries.Add(Fat32Table.EOC_MIN); // cluster 11 is EOC

            var fatData = CreateFatData(entries.ToArray());
            var fatTable = new Fat32Table(bootSector, fatData);

            var chain = fatTable.GetClusterChain(2);

            Assert.AreEqual(10, chain.Count, "Chain should have 10 entries");
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual((uint)(i + 2), chain[i], $"Chain entry {i} should be cluster {i + 2}");
            }
        }
    }
}
