// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Text;
using Duplicati.Proprietary.DiskImage.Filesystem.Ntfs;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest.DiskImage.Ntfs;

/// <summary>
/// Unit tests for the <see cref="NtfsBootSector"/> parser.
/// </summary>
[TestFixture]
[Category("DiskImageUnit")]
public class NtfsBootSectorTests
{
    /// <summary>
    /// Creates a valid NTFS boot sector with known values for testing.
    /// </summary>
    private static byte[] CreateValidBootSector()
    {
        var bootSector = new byte[512];

        // OEM ID at offset 0x03 (8 bytes: "NTFS    ")
        var oemId = Encoding.ASCII.GetBytes("NTFS    ");
        oemId.CopyTo(bootSector, 0x03);

        // Bytes per sector at offset 0x0B (512 = 0x0200)
        BinaryPrimitives.WriteUInt16LittleEndian(bootSector.AsSpan(0x0B, 2), 512);

        // Sectors per cluster at offset 0x0D (8 sectors)
        bootSector[0x0D] = 8;

        // Total sectors at offset 0x28 (2,000,000 sectors)
        BinaryPrimitives.WriteUInt64LittleEndian(bootSector.AsSpan(0x28, 8), 2_000_000);

        // MFT cluster number at offset 0x30 (cluster 100)
        BinaryPrimitives.WriteUInt64LittleEndian(bootSector.AsSpan(0x30, 8), 100);

        // MFT mirror cluster number at offset 0x38 (cluster 125000)
        BinaryPrimitives.WriteUInt64LittleEndian(bootSector.AsSpan(0x38, 8), 125_000);

        // Clusters per MFT record at offset 0x40 (-10 means 2^10 = 1024 bytes)
        bootSector[0x40] = unchecked((byte)-10);

        // Clusters per index block at offset 0x44 (-12 means 2^12 = 4096 bytes)
        bootSector[0x44] = unchecked((byte)-12);

        // Volume serial number at offset 0x48
        BinaryPrimitives.WriteUInt64LittleEndian(bootSector.AsSpan(0x48, 8), 0x123456789ABCDEF0);

        // Boot sector signature at offset 510: bytes 0x55, 0xAA on disk (0xAA55 as little-endian uint16)
        BinaryPrimitives.WriteUInt16LittleEndian(bootSector.AsSpan(510, 2), 0xAA55);

        return bootSector;
    }

    [Test]
    public void Test_NtfsBootSector_ParseValidBootSector()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        Assert.AreEqual(512, bootSector.BytesPerSector);
        Assert.AreEqual(8, bootSector.SectorsPerCluster);
        Assert.AreEqual(2_000_000ul, bootSector.TotalSectors);
        Assert.AreEqual(100ul, bootSector.MftClusterNumber);
        Assert.AreEqual(125_000ul, bootSector.MftMirrorClusterNumber);
        Assert.AreEqual(-10, bootSector.ClustersPerMftRecord);
        Assert.AreEqual(-12, bootSector.ClustersPerIndexBlock);
        Assert.AreEqual(0x123456789ABCDEF0ul, bootSector.VolumeSerialNumber);
    }

    [Test]
    public void Test_NtfsBootSector_InvalidSignature_Throws()
    {
        var bootSectorData = CreateValidBootSector();
        // Change the signature to an invalid value
        BinaryPrimitives.WriteUInt16LittleEndian(bootSectorData.AsSpan(510, 2), 0x1234);

        var ex = Assert.Throws<ArgumentException>(() => new NtfsBootSector(bootSectorData));
        StringAssert.Contains("signature", ex!.Message);
    }

    [Test]
    public void Test_NtfsBootSector_InvalidOemId_Throws()
    {
        var bootSectorData = CreateValidBootSector();
        // Change OEM ID to "FAT32   "
        var oemId = Encoding.ASCII.GetBytes("FAT32   ");
        oemId.CopyTo(bootSectorData, 0x03);

        var ex = Assert.Throws<ArgumentException>(() => new NtfsBootSector(bootSectorData));
        StringAssert.Contains("OEM ID", ex!.Message);
    }

    [Test]
    public void Test_NtfsBootSector_InvalidBytesPerSector_Throws()
    {
        var bootSectorData = CreateValidBootSector();
        // Set bytes per sector to 768 (not a power of 2)
        BinaryPrimitives.WriteUInt16LittleEndian(bootSectorData.AsSpan(0x0B, 2), 768);

        var ex = Assert.Throws<ArgumentException>(() => new NtfsBootSector(bootSectorData));
        StringAssert.Contains("BytesPerSector", ex!.Message);
    }

    [Test]
    public void Test_NtfsBootSector_InvalidSectorsPerCluster_Throws()
    {
        var bootSectorData = CreateValidBootSector();
        // Set sectors per cluster to 3 (not a power of 2)
        bootSectorData[0x0D] = 3;

        var ex = Assert.Throws<ArgumentException>(() => new NtfsBootSector(bootSectorData));
        StringAssert.Contains("SectorsPerCluster", ex!.Message);
    }

    [Test]
    public void Test_NtfsBootSector_DerivedProperties_Correct()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        // ClusterSize = BytesPerSector * SectorsPerCluster = 512 * 8 = 4096
        Assert.AreEqual(4096, bootSector.ClusterSize);

        // MftRecordSize = 2^10 = 1024 (ClustersPerMftRecord = -10)
        Assert.AreEqual(1024, bootSector.MftRecordSize);

        // MftByteOffset = MftClusterNumber * ClusterSize = 100 * 4096 = 409600
        Assert.AreEqual(409600, bootSector.MftByteOffset);

        // TotalClusters = TotalSectors / SectorsPerCluster = 2,000,000 / 8 = 250,000
        Assert.AreEqual(250_000, bootSector.TotalClusters);

        // TotalSize = TotalSectors * BytesPerSector = 2,000,000 * 512 = 1,024,000,000
        Assert.AreEqual(1_024_000_000, bootSector.TotalSize);
    }

    [Test]
    public void Test_NtfsBootSector_NegativeClustersPerMftRecord()
    {
        var bootSectorData = CreateValidBootSector();
        // -10 should yield 1024-byte records (2^10)
        bootSectorData[0x40] = unchecked((byte)-10);

        var bootSector = new NtfsBootSector(bootSectorData);
        Assert.AreEqual(1024, bootSector.MftRecordSize);
    }

    [Test]
    public void Test_NtfsBootSector_PositiveClustersPerMftRecord()
    {
        var bootSectorData = CreateValidBootSector();
        // 2 should yield 2 * ClusterSize = 2 * 4096 = 8192 bytes
        bootSectorData[0x40] = 2;

        var bootSector = new NtfsBootSector(bootSectorData);
        Assert.AreEqual(8192, bootSector.MftRecordSize);
    }

    [Test]
    public void Test_NtfsBootSector_TooSmallBuffer_Throws()
    {
        var smallBuffer = new byte[256];

        var ex = Assert.Throws<ArgumentException>(() => new NtfsBootSector(smallBuffer));
        StringAssert.Contains("512", ex!.Message);
    }

    [Test]
    public void Test_NtfsBootSector_ValidBytesPerSectorValues()
    {
        ushort[] validValues = { 512, 1024, 2048, 4096 };

        foreach (var bytesPerSector in validValues)
        {
            var bootSectorData = CreateValidBootSector();
            BinaryPrimitives.WriteUInt16LittleEndian(bootSectorData.AsSpan(0x0B, 2), bytesPerSector);

            var bootSector = new NtfsBootSector(bootSectorData);
            Assert.AreEqual(bytesPerSector, bootSector.BytesPerSector);
        }
    }

    [Test]
    public void Test_NtfsBootSector_ValidSectorsPerClusterValues()
    {
        byte[] validValues = { 1, 2, 4, 8, 16, 32, 64, 128 };

        foreach (var sectorsPerCluster in validValues)
        {
            var bootSectorData = CreateValidBootSector();
            bootSectorData[0x0D] = sectorsPerCluster;

            var bootSector = new NtfsBootSector(bootSectorData);
            Assert.AreEqual(sectorsPerCluster, bootSector.SectorsPerCluster);
        }
    }

    [Test]
    public void Test_NtfsBootSector_LargerBuffer_Works()
    {
        // Test that a buffer larger than 512 bytes works (e.g., 4KB sector size)
        var bootSectorData = new byte[4096];
        var validData = CreateValidBootSector();
        validData.CopyTo(bootSectorData, 0);

        var bootSector = new NtfsBootSector(bootSectorData);
        Assert.AreEqual(512, bootSector.BytesPerSector);
    }
}
