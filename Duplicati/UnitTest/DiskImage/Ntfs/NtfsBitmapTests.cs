// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using Duplicati.Proprietary.DiskImage.Filesystem.Ntfs;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest.DiskImage.Ntfs;

/// <summary>
/// Unit tests for the <see cref="NtfsBitmap"/> class.
/// </summary>
[TestFixture]
[Category("DiskImageUnit")]
public class NtfsBitmapTests
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

        // Total sectors at offset 0x28 (1000 sectors = 125 clusters)
        BinaryPrimitives.WriteUInt64LittleEndian(bootSector.AsSpan(0x28, 8), 1000);

        // MFT cluster number at offset 0x30 (cluster 2)
        BinaryPrimitives.WriteUInt64LittleEndian(bootSector.AsSpan(0x30, 8), 2);

        // MFT mirror cluster number at offset 0x38 (cluster 100)
        BinaryPrimitives.WriteUInt64LittleEndian(bootSector.AsSpan(0x38, 8), 100);

        // Clusters per MFT record at offset 0x40 (-10 means 2^10 = 1024 bytes)
        bootSector[0x40] = unchecked((byte)-10);

        // Clusters per index block at offset 0x44 (-12 means 2^12 = 4096 bytes)
        bootSector[0x44] = unchecked((byte)-12);

        // Volume serial number at offset 0x48
        BinaryPrimitives.WriteUInt64LittleEndian(bootSector.AsSpan(0x48, 8), 0x123456789ABCDEF0);

        // Boot sector signature at offset 510
        BinaryPrimitives.WriteUInt16LittleEndian(bootSector.AsSpan(510, 2), 0xAA55);

        return bootSector;
    }

    [Test]
    public void Test_NtfsBitmap_FreeCluster_NotAllocated()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        // Create all-zero bitmap (all clusters free)
        var bitmapData = new byte[(bootSector.TotalClusters + 7) / 8];

        var bitmap = new NtfsBitmap(bootSector, bitmapData);

        Assert.IsFalse(bitmap.IsClusterAllocated(0), "Cluster 0 should be free");
        Assert.IsFalse(bitmap.IsClusterAllocated(10), "Cluster 10 should be free");
        Assert.IsFalse(bitmap.IsClusterAllocated(100), "Cluster 100 should be free");
    }

    [Test]
    public void Test_NtfsBitmap_AllocatedCluster_IsAllocated()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        // Create bitmap with specific bits set
        var bitmapData = new byte[(bootSector.TotalClusters + 7) / 8];
        // Set bit 0 (cluster 0) - LSB of byte 0
        bitmapData[0] = 0x01;
        // Set bit 10 (cluster 10) - bit 2 of byte 1
        bitmapData[1] = 0x04;

        var bitmap = new NtfsBitmap(bootSector, bitmapData);

        Assert.IsTrue(bitmap.IsClusterAllocated(0), "Cluster 0 should be allocated");
        Assert.IsTrue(bitmap.IsClusterAllocated(10), "Cluster 10 should be allocated");
        Assert.IsFalse(bitmap.IsClusterAllocated(1), "Cluster 1 should be free");
        Assert.IsFalse(bitmap.IsClusterAllocated(5), "Cluster 5 should be free");
    }

    [Test]
    public void Test_NtfsBitmap_MixedAllocation()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        // Create bitmap with mix of allocated and free clusters
        var bitmapData = new byte[(bootSector.TotalClusters + 7) / 8];
        // Byte 0: bits 0, 2, 4, 6 allocated = 0x55
        bitmapData[0] = 0x55;
        // Byte 1: bits 1, 3, 5, 7 allocated = 0xAA
        bitmapData[1] = 0xAA;

        var bitmap = new NtfsBitmap(bootSector, bitmapData);

        // 8 bits set in first two bytes
        Assert.AreEqual(8, bitmap.AllocatedClusters, "Should have 8 allocated clusters");
        Assert.AreEqual(bootSector.TotalClusters - 8, bitmap.FreeClusters, "Free clusters should match");
    }

    [Test]
    public void Test_NtfsBitmap_AllAllocated()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        // Create all-ones bitmap
        var bitmapData = new byte[(bootSector.TotalClusters + 7) / 8];
        for (var i = 0; i < bitmapData.Length; i++)
        {
            bitmapData[i] = 0xFF;
        }

        var bitmap = new NtfsBitmap(bootSector, bitmapData);

        Assert.AreEqual(0, bitmap.FreeClusters, "All clusters should be allocated");
        Assert.AreEqual(bootSector.TotalClusters, bitmap.AllocatedClusters, "Allocated clusters should equal total");
        Assert.IsTrue(bitmap.IsClusterAllocated(0), "Cluster 0 should be allocated");
        Assert.IsTrue(bitmap.IsClusterAllocated((int)bootSector.TotalClusters - 1), "Last cluster should be allocated");
    }

    [Test]
    public void Test_NtfsBitmap_ClusterOutOfRange_Throws()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        var bitmapData = new byte[(bootSector.TotalClusters + 7) / 8];
        var bitmap = new NtfsBitmap(bootSector, bitmapData);

        // Negative cluster number
        Assert.Throws<ArgumentOutOfRangeException>(() => bitmap.IsClusterAllocated(-1));

        // Cluster number beyond total clusters
        Assert.Throws<ArgumentOutOfRangeException>(() => bitmap.IsClusterAllocated(bootSector.TotalClusters));
        Assert.Throws<ArgumentOutOfRangeException>(() => bitmap.IsClusterAllocated(bootSector.TotalClusters + 100));
    }

    [Test]
    public void Test_NtfsBitmap_TotalClusters_Correct()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        var bitmapData = new byte[(bootSector.TotalClusters + 7) / 8];
        var bitmap = new NtfsBitmap(bootSector, bitmapData);

        // Total clusters should be 1000 sectors / 8 sectors per cluster = 125
        Assert.AreEqual(125, bitmap.TotalClusters, "Total clusters should match boot sector");
    }

    [Test]
    public void Test_NtfsBitmap_BitOrdering()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        // Create bitmap testing LSB-first ordering
        var bitmapData = new byte[4]; // 32 clusters worth
        // Bit 0 of byte 0 -> cluster 0
        bitmapData[0] = 0x01;
        // Bit 7 of byte 0 -> cluster 7
        // Bit 0 of byte 1 -> cluster 8
        bitmapData[1] = 0x01;

        var bitmap = new NtfsBitmap(bootSector, bitmapData);

        Assert.IsTrue(bitmap.IsClusterAllocated(0), "Bit 0 of byte 0 should be cluster 0");
        Assert.IsFalse(bitmap.IsClusterAllocated(1), "Bit 1 should not be set");
        Assert.IsFalse(bitmap.IsClusterAllocated(7), "Bit 7 should not be set");
        Assert.IsTrue(bitmap.IsClusterAllocated(8), "Bit 0 of byte 1 should be cluster 8");

        // Test bit 7 of byte 0
        bitmapData[0] = 0x80;
        bitmapData[1] = 0x00;
        var bitmap2 = new NtfsBitmap(bootSector, bitmapData);

        Assert.IsFalse(bitmap2.IsClusterAllocated(0), "Bit 0 should not be set");
        Assert.IsTrue(bitmap2.IsClusterAllocated(7), "Bit 7 of byte 0 should be cluster 7");
    }

    [Test]
    public void Test_NtfsBitmap_FixupArray_Applied()
    {
        // Create a fake MFT record with fixup array
        var recordSize = 1024; // Standard MFT record size
        var recordData = new byte[recordSize];

        // FILE magic at offset 0
        BinaryPrimitives.WriteUInt32LittleEndian(recordData.AsSpan(0, 4), 0x454C4946);

        // Fixup offset at 0x04 (offset 48 = 0x30)
        BinaryPrimitives.WriteUInt16LittleEndian(recordData.AsSpan(0x04, 2), 0x30);

        // Fixup count at 0x06 (3 entries = 2 sectors + sequence number)
        BinaryPrimitives.WriteUInt16LittleEndian(recordData.AsSpan(0x06, 2), 3);

        // Set up fixup array at offset 0x30
        // First entry is sequence number
        BinaryPrimitives.WriteUInt16LittleEndian(recordData.AsSpan(0x30, 2), 0x1234);
        // Second entry is original value for sector 1
        BinaryPrimitives.WriteUInt16LittleEndian(recordData.AsSpan(0x32, 2), 0xABCD);
        // Third entry is original value for sector 2
        BinaryPrimitives.WriteUInt16LittleEndian(recordData.AsSpan(0x34, 2), 0xEF01);

        // Place sequence numbers at end of sectors (positions 510 and 1022)
        BinaryPrimitives.WriteUInt16LittleEndian(recordData.AsSpan(510, 2), 0x1234);
        BinaryPrimitives.WriteUInt16LittleEndian(recordData.AsSpan(1022, 2), 0x1234);

        // Apply fixup
        NtfsBitmap.ApplyFixupArray(recordData);

        // Verify the original values were restored
        Assert.AreEqual(0xABCD, BinaryPrimitives.ReadUInt16LittleEndian(recordData.AsSpan(510, 2)), "Sector 1 original value should be restored");
        Assert.AreEqual(0xEF01, BinaryPrimitives.ReadUInt16LittleEndian(recordData.AsSpan(1022, 2)), "Sector 2 original value should be restored");
    }

    [Test]
    public void Test_NtfsBitmap_FixupArray_Mismatch_Throws()
    {
        // Create a fake MFT record with incorrect fixup values
        var recordSize = 1024;
        var recordData = new byte[recordSize];

        // FILE magic
        BinaryPrimitives.WriteUInt32LittleEndian(recordData.AsSpan(0, 4), 0x454C4946);

        // Fixup offset
        BinaryPrimitives.WriteUInt16LittleEndian(recordData.AsSpan(0x04, 2), 0x30);

        // Fixup count
        BinaryPrimitives.WriteUInt16LittleEndian(recordData.AsSpan(0x06, 2), 3);

        // Sequence number
        BinaryPrimitives.WriteUInt16LittleEndian(recordData.AsSpan(0x30, 2), 0x1234);

        // Put wrong sequence number at end of sector 1
        BinaryPrimitives.WriteUInt16LittleEndian(recordData.AsSpan(510, 2), 0x5678);

        // This should throw due to mismatch
        Assert.Throws<InvalidDataException>(() => NtfsBitmap.ApplyFixupArray(recordData));
    }

    [Test]
    public void Test_NtfsBitmap_DataRunParsing_SingleRun()
    {
        // Data run: header 0x11 means 1 byte for length, 1 byte for offset
        // Run length: 0x10 (16 clusters)
        // Run offset: 0x05 (cluster 5, absolute since first run)
        var data = new byte[] { 0x11, 0x10, 0x05, 0x00 };

        var runs = NtfsBitmap.ParseDataRuns(data.AsSpan(), 0);

        Assert.AreEqual(1, runs.Count, "Should have 1 run");
        Assert.AreEqual(5, runs[0].startCluster, "Start cluster should be 5");
        Assert.AreEqual(16, runs[0].clusterCount, "Run length should be 16 clusters");
    }

    [Test]
    public void Test_NtfsBitmap_DataRunParsing_MultipleRuns()
    {
        // Multiple data runs
        // Run 1: 0x11, 0x0A, 0x05 -> 10 clusters starting at cluster 5
        // Run 2: 0x11, 0x14, 0x0A -> 20 clusters starting at cluster 15 (5 + 10)
        // Terminator: 0x00
        var data = new byte[] { 0x11, 0x0A, 0x05, 0x11, 0x14, 0x0A, 0x00 };

        var runs = NtfsBitmap.ParseDataRuns(data.AsSpan(), 0);

        Assert.AreEqual(2, runs.Count, "Should have 2 runs");
        Assert.AreEqual(5, runs[0].startCluster, "First run should start at cluster 5");
        Assert.AreEqual(10, runs[0].clusterCount, "First run should be 10 clusters");
        Assert.AreEqual(15, runs[1].startCluster, "Second run should start at cluster 15");
        Assert.AreEqual(20, runs[1].clusterCount, "Second run should be 20 clusters");
    }

    [Test]
    public void Test_NtfsBitmap_DataRunParsing_NegativeOffset()
    {
        // Test negative offset (for fragmented files that go backwards)
        // Run 1: 0x11, 0x0A, 0x64 -> 10 clusters starting at cluster 100
        // Run 2: 0x11, 0x05, 0xF6 -> 5 clusters, offset = -10 (0xF6 as signed byte)
        // Terminator: 0x00
        var data = new byte[] { 0x11, 0x0A, 0x64, 0x11, 0x05, 0xF6, 0x00 };

        var runs = NtfsBitmap.ParseDataRuns(data.AsSpan(), 0);

        Assert.AreEqual(2, runs.Count, "Should have 2 runs");
        Assert.AreEqual(100, runs[0].startCluster, "First run should start at cluster 100");
        Assert.AreEqual(90, runs[1].startCluster, "Second run should start at cluster 90 (100 - 10)");
    }

    [Test]
    public void Test_NtfsBitmap_DataRunParsing_SparseRun()
    {
        // Test sparse run (zero offset means sparse/unallocated extent)
        // 0x11, 0x0A, 0x00 -> 10 clusters with zero offset = sparse
        var data = new byte[] { 0x11, 0x0A, 0x00, 0x00 };

        var runs = NtfsBitmap.ParseDataRuns(data.AsSpan(), 0);

        Assert.AreEqual(1, runs.Count, "Should have 1 run");
        Assert.AreEqual(0, runs[0].startCluster, "Sparse run should have startCluster = 0");
        Assert.AreEqual(10, runs[0].clusterCount, "Run should be 10 clusters");
    }

    [Test]
    public void Test_NtfsBitmap_DataRunParsing_MultibyteValues()
    {
        // Test data run with multi-byte values
        // 0x23, 0x40, 0x42, 0x0F, 0x00 ->
        // 3 bytes for length, 2 bytes for offset
        // Length: 0x000F4240 = 1,000,000 clusters
        // Offset: 0x0040 = 64
        var data = new byte[] { 0x23, 0x40, 0x42, 0x0F, 0x40, 0x00, 0x00 };

        var runs = NtfsBitmap.ParseDataRuns(data.AsSpan(), 0);

        Assert.AreEqual(1, runs.Count, "Should have 1 run");
        Assert.AreEqual(64, runs[0].startCluster, "Start cluster should be 64");
        Assert.AreEqual(1000000, runs[0].clusterCount, "Run should be 1,000,000 clusters");
    }

    [Test]
    public void Test_NtfsBitmap_DataRunParsing_InvalidHeader_Throws()
    {
        // Invalid header: 0x90 means 0 bytes for length (invalid)
        var data = new byte[] { 0x90, 0x10, 0x05, 0x00 };

        Assert.Throws<InvalidDataException>(() => NtfsBitmap.ParseDataRuns(data.AsSpan(), 0));
    }

    [Test]
    public void Test_NtfsBitmap_NullBitmapData_Throws()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        Assert.Throws<ArgumentNullException>(() => new NtfsBitmap(bootSector, null!));
    }
}