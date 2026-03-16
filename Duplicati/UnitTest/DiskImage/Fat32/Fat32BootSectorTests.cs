// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Text;
using Duplicati.Proprietary.DiskImage.Filesystem;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest.DiskImage
{
    /// <summary>
    /// Unit tests for the <see cref="Fat32BootSector"/> parser.
    /// </summary>
    [TestFixture]
    [Category("DiskImageUnit")]
    public class Fat32BootSectorTests
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
            var fsType = Encoding.ASCII.GetBytes("FAT32   ");
            fsType.CopyTo(bootSector, 0x52);

            // Boot sector signature at offset 510: bytes 0x55, 0xAA on disk (0xAA55 as little-endian uint16)
            BinaryPrimitives.WriteUInt16LittleEndian(bootSector.AsSpan(510, 2), 0xAA55);

            return bootSector;
        }

        [Test]
        public void Test_Fat32BootSector_ParseValidBootSector()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            Assert.AreEqual(512, bootSector.BytesPerSector);
            Assert.AreEqual(8, bootSector.SectorsPerCluster);
            Assert.AreEqual(32, bootSector.ReservedSectorCount);
            Assert.AreEqual(2, bootSector.NumberOfFats);
            Assert.AreEqual(1_000_000u, bootSector.TotalSectors32);
            Assert.AreEqual(244u, bootSector.FatSize32);
            Assert.AreEqual(2u, bootSector.RootCluster);
            Assert.AreEqual(1, bootSector.FsInfoSector);
        }

        [Test]
        public void Test_Fat32BootSector_InvalidSignature_Throws()
        {
            var bootSectorData = CreateValidBootSector();
            // Change the signature to an invalid value
            BinaryPrimitives.WriteUInt16LittleEndian(bootSectorData.AsSpan(510, 2), 0x1234);

            var ex = Assert.Throws<ArgumentException>(() => new Fat32BootSector(bootSectorData));
            StringAssert.Contains("signature", ex!.Message);
        }

        [Test]
        public void Test_Fat32BootSector_InvalidBytesPerSector_Throws()
        {
            var bootSectorData = CreateValidBootSector();
            // Set bytes per sector to 768 (not a power of 2)
            BinaryPrimitives.WriteUInt16LittleEndian(bootSectorData.AsSpan(0x0B, 2), 768);

            var ex = Assert.Throws<ArgumentException>(() => new Fat32BootSector(bootSectorData));
            StringAssert.Contains("BytesPerSector", ex!.Message);
        }

        [Test]
        public void Test_Fat32BootSector_InvalidSectorsPerCluster_Throws()
        {
            var bootSectorData = CreateValidBootSector();
            // Set sectors per cluster to 3 (not a power of 2)
            bootSectorData[0x0D] = 3;

            var ex = Assert.Throws<ArgumentException>(() => new Fat32BootSector(bootSectorData));
            StringAssert.Contains("SectorsPerCluster", ex!.Message);
        }

        [Test]
        public void Test_Fat32BootSector_DerivedProperties_Correct()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            // ClusterSize = BytesPerSector * SectorsPerCluster = 512 * 8 = 4096
            Assert.AreEqual(4096, bootSector.ClusterSize);

            // FatStartOffset = ReservedSectorCount * BytesPerSector = 32 * 512 = 16384
            Assert.AreEqual(16384, bootSector.FatStartOffset);

            // DataStartOffset = (ReservedSectorCount + NumberOfFats * FatSize32) * BytesPerSector
            // = (32 + 2 * 244) * 512 = (32 + 488) * 512 = 520 * 512 = 266240
            Assert.AreEqual(266240, bootSector.DataStartOffset);

            // TotalDataClusters = (TotalSectors32 - (ReservedSectorCount + NumberOfFats * FatSize32)) / SectorsPerCluster
            // = (1,000,000 - (32 + 2 * 244)) / 8 = (1,000,000 - 520) / 8 = 999,480 / 8 = 124935
            Assert.AreEqual(124935u, bootSector.TotalDataClusters);
        }

        [Test]
        public void Test_Fat32BootSector_ClusterToByteOffset_Correct()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            // Cluster 2 should be at DataStartOffset (first data cluster)
            Assert.AreEqual(bootSector.DataStartOffset, bootSector.ClusterToByteOffset(2));

            // Cluster 3 should be at DataStartOffset + ClusterSize
            Assert.AreEqual(bootSector.DataStartOffset + bootSector.ClusterSize, bootSector.ClusterToByteOffset(3));

            // Cluster 10 should be at DataStartOffset + 8 * ClusterSize
            Assert.AreEqual(bootSector.DataStartOffset + 8 * bootSector.ClusterSize, bootSector.ClusterToByteOffset(10));
        }

        [Test]
        public void Test_Fat32BootSector_TooSmallBuffer_Throws()
        {
            var smallBuffer = new byte[256];

            var ex = Assert.Throws<ArgumentException>(() => new Fat32BootSector(smallBuffer));
            StringAssert.Contains("512", ex!.Message);
        }

        [Test]
        public void Test_Fat32BootSector_ClusterToByteOffset_InvalidCluster_Throws()
        {
            var bootSectorData = CreateValidBootSector();
            var bootSector = new Fat32BootSector(bootSectorData);

            var ex = Assert.Throws<ArgumentException>(() => bootSector.ClusterToByteOffset(0));
            StringAssert.Contains("Cluster numbers start at 2", ex!.Message);

            ex = Assert.Throws<ArgumentException>(() => bootSector.ClusterToByteOffset(1));
            StringAssert.Contains("Cluster numbers start at 2", ex!.Message);
        }

        [Test]
        public void Test_Fat32BootSector_NumberOfFats_Zero_Throws()
        {
            var bootSectorData = CreateValidBootSector();
            bootSectorData[0x10] = 0; // Set NumberOfFats to 0

            var ex = Assert.Throws<ArgumentException>(() => new Fat32BootSector(bootSectorData));
            StringAssert.Contains("NumberOfFats", ex!.Message);
        }

        [Test]
        public void Test_Fat32BootSector_InvalidFilesystemType_Throws()
        {
            var bootSectorData = CreateValidBootSector();
            // Change filesystem type to something other than FAT32
            var fsType = Encoding.ASCII.GetBytes("FAT16   ");
            fsType.CopyTo(bootSectorData, 0x52);

            var ex = Assert.Throws<ArgumentException>(() => new Fat32BootSector(bootSectorData));
            StringAssert.Contains("FAT32", ex!.Message);
        }

        [Test]
        public void Test_Fat32BootSector_ValidBytesPerSectorValues()
        {
            ushort[] validValues = { 512, 1024, 2048, 4096 };

            foreach (var bytesPerSector in validValues)
            {
                var bootSectorData = CreateValidBootSector();
                BinaryPrimitives.WriteUInt16LittleEndian(bootSectorData.AsSpan(0x0B, 2), bytesPerSector);

                var bootSector = new Fat32BootSector(bootSectorData);
                Assert.AreEqual(bytesPerSector, bootSector.BytesPerSector);
            }
        }

        [Test]
        public void Test_Fat32BootSector_ValidSectorsPerClusterValues()
        {
            byte[] validValues = { 1, 2, 4, 8, 16, 32, 64, 128 };

            foreach (var sectorsPerCluster in validValues)
            {
                var bootSectorData = CreateValidBootSector();
                bootSectorData[0x0D] = sectorsPerCluster;

                var bootSector = new Fat32BootSector(bootSectorData);
                Assert.AreEqual(sectorsPerCluster, bootSector.SectorsPerCluster);
            }
        }

        [Test]
        public void Test_Fat32BootSector_LargerBuffer_Works()
        {
            // Test that a buffer larger than 512 bytes works (e.g., 4KB sector size)
            var bootSectorData = new byte[4096];
            var validData = CreateValidBootSector();
            validData.CopyTo(bootSectorData, 0);

            var bootSector = new Fat32BootSector(bootSectorData);
            Assert.AreEqual(512, bootSector.BytesPerSector);
        }
    }
}
