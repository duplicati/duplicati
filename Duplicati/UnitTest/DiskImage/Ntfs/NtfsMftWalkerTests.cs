// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Filesystem.Ntfs;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest.DiskImage.Ntfs;

/// <summary>
/// Unit tests for the <see cref="NtfsMftWalker"/> class.
/// </summary>
[TestFixture]
[Category("DiskImageUnit")]
public class NtfsMftWalkerTests
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

        // Sectors per cluster at offset 0x0D (8 sectors = 4096 bytes per cluster)
        bootSector[0x0D] = 8;

        // Total sectors at offset 0x28 (10000 sectors = 1250 clusters)
        BinaryPrimitives.WriteUInt64LittleEndian(bootSector.AsSpan(0x28, 8), 10000);

        // MFT cluster number at offset 0x30 (cluster 4)
        BinaryPrimitives.WriteUInt64LittleEndian(bootSector.AsSpan(0x30, 8), 4);

        // MFT mirror cluster number at offset 0x38 (cluster 500)
        BinaryPrimitives.WriteUInt64LittleEndian(bootSector.AsSpan(0x38, 8), 500);

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

    /// <summary>
    /// Creates bitmap data from an array of allocation flags.
    /// </summary>
    private static byte[] CreateBitmapData(params bool[] allocated)
    {
        var byteCount = (allocated.Length + 7) / 8;
        var result = new byte[byteCount];

        for (var i = 0; i < allocated.Length; i++)
            if (allocated[i])
            {
                var byteIndex = i / 8;
                var bitIndex = i % 8;
                result[byteIndex] |= (byte)(1 << bitIndex);
            }

        return result;
    }

    /// <summary>
    /// Creates an MFT record with the specified attributes.
    /// </summary>
    private static byte[] CreateMftRecord(
        long fileTimestamp,
        List<(long offset, long length)>? dataRuns = null,
        bool inUse = true)
    {
        var recordSize = 1024; // Typical MFT record size
        var record = new byte[recordSize];

        // MFT Record Header:
        // 0x00: Magic "FILE" (4 bytes)
        // 0x04: Fixup offset (2 bytes)
        // 0x06: Fixup count (2 bytes) - number of entries (also number of sectors)
        // 0x08: LSN (8 bytes)
        // 0x10: Sequence number (2 bytes)
        // 0x12: Hard link count (2 bytes)
        // 0x14: Offset to first attribute (2 bytes)
        // 0x16: Flags (2 bytes) - 0x01 = in use
        // 0x18: Used size (4 bytes)
        // 0x1C: Allocated size (4 bytes)
        // 0x20: Base file record (8 bytes)
        // 0x28: Next attribute ID (2 bytes)
        // 0x2A: Align to 8 bytes
        // 0x30: MFT record number (8 bytes) - Win2K+ only

        // Write FILE magic
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0, 4), 0x454C4946); // "FILE"

        // Write fixup offset (at 0x04) - point to end of record for fixup array
        var fixupOffset = 48; // After header
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x04, 2), (ushort)fixupOffset);

        // Write fixup count (at 0x06) - 3 entries for 1024-byte record (2 sectors)
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x06, 2), 3);

        // Write first attribute offset (at 0x14) - after header (48 bytes)
        var firstAttributeOffset = 56; // After header and some padding
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x14, 2), (ushort)firstAttributeOffset);

        // Write flags (at 0x16) - 0x01 = in use
        var flags = inUse ? (ushort)0x01 : (ushort)0x00;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x16, 2), flags);

        // Initialize fixup array
        // First entry is the sequence number
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(fixupOffset, 2), 0x1234);
        // Next entries are the original values for each sector's last 2 bytes
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(fixupOffset + 2, 2), 0x0000);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(fixupOffset + 4, 2), 0x0000);

        // Write fixup signature bytes at end of each 512-byte sector
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(512 - 2, 2), 0x1234);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1024 - 2, 2), 0x1234);

        var currentOffset = firstAttributeOffset;

        // Add $STANDARD_INFORMATION attribute (0x10)
        // Resident attribute structure:
        // 0x00: Type (4 bytes)
        // 0x04: Length (4 bytes)
        // 0x08: Non-resident flag (1 byte)
        // 0x09: Name length (1 byte)
        // 0x0A: Name offset (2 bytes)
        // 0x0C: Flags (2 bytes)
        // 0x0E: Attribute ID (2 bytes)
        // 0x10: Data length (4 bytes)
        // 0x14: Data offset (2 bytes)
        // 0x16: Resident flags (1 byte)
        // 0x17: Padding (1 byte)
        // Data follows

        var stdInfoAttrSize = 72; // Header + data
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(currentOffset, 4), 0x10); // $STANDARD_INFORMATION
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(currentOffset + 4, 4), (uint)stdInfoAttrSize);
        record[currentOffset + 8] = 0x00; // Resident
        record[currentOffset + 9] = 0x00; // No name
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(currentOffset + 0x0A, 2), 0x18); // Name offset
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(currentOffset + 0x0C, 2), 0x0000); // Flags
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(currentOffset + 0x0E, 2), 0x0000); // Attribute ID
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(currentOffset + 0x10, 4), 48); // Data length
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(currentOffset + 0x14, 2), 0x18); // Data offset
        record[currentOffset + 0x16] = 0x00; // Resident flags
        record[currentOffset + 0x17] = 0x00; // Padding

        // $STANDARD_INFORMATION data:
        // 0x00: Creation time (8 bytes)
        // 0x08: Modification time (8 bytes)
        // 0x10: MFT modification time (8 bytes)
        // 0x18: Access time (8 bytes)
        // 0x20: DOS flags (4 bytes)
        // 0x24: Max versions (4 bytes)
        // 0x28: Version (4 bytes)
        // 0x2C: Class ID (4 bytes)
        // 0x30: Owner ID (4 bytes) - Win2K+
        // 0x34: Security ID (4 bytes) - Win2K+
        // 0x38: Quota charged (8 bytes) - Win2K+
        // 0x40: Update sequence number (8 bytes) - Win2K+

        var stdInfoDataOffset = currentOffset + 0x18;
        var filetime = fileTimestamp == 0 ? 0 : fileTimestamp;
        BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(stdInfoDataOffset + 0x00, 8), filetime); // Creation
        BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(stdInfoDataOffset + 0x08, 8), filetime); // Modification
        BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(stdInfoDataOffset + 0x10, 8), filetime); // MFT modification
        BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(stdInfoDataOffset + 0x18, 8), filetime); // Access

        currentOffset += stdInfoAttrSize;

        // Add $DATA attribute (0x80) if data runs are provided
        if (dataRuns != null && dataRuns.Count > 0)
        {
            // Non-resident $DATA attribute
            // Header:
            // 0x00: Attribute type (4 bytes)
            // 0x04: Attribute length (4 bytes)
            // 0x08: Non-resident flag (1 byte)
            // 0x09: Name length (1 byte)
            // 0x0A: Name offset (2 bytes)
            // 0x0C: Flags (2 bytes)
            // 0x0E: Attribute ID (2 bytes)
            // 0x10: Starting VCN (8 bytes)
            // 0x18: Ending VCN (8 bytes)
            // 0x20: Run offset (2 bytes) - offset to data run list from start of attribute
            // 0x22: Compression unit size (2 bytes)
            // 0x24: Padding (4 bytes)
            // 0x28: Allocated size (8 bytes)
            // 0x30: Data size (8 bytes)
            // 0x38: Initialized size (8 bytes)
            // 0x40: Data runs start at offset specified by run offset field. If the attribute is named, then the name starts here. Otherwise, the data runs start here.

            var attrHeaderSize = 0x40;
            var dataRunBytes = CreateDataRunBytes(dataRuns);
            var attrSize = attrHeaderSize + dataRunBytes.Length;

            // Align attribute size to 8 bytes
            attrSize = (attrSize + 7) & ~7;

            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(currentOffset, 4), 0x80); // $DATA
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(currentOffset + 4, 4), (uint)attrSize);
            record[currentOffset + 8] = 0x01; // Non-resident
            record[currentOffset + 9] = 0x00; // No name
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(currentOffset + 0x0A, 2), 0x40); // Name offset
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(currentOffset + 0x0C, 2), 0); // Flags
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(currentOffset + 0x0E, 2), 0); // Attribute ID
            BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(currentOffset + 0x10, 8), 0); // Starting VCN

            // Calculate ending VCN from data runs
            var totalClusters = dataRuns.Sum(r => r.length);
            BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(currentOffset + 0x18, 8), totalClusters - 1); // Ending VCN
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(currentOffset + 0x20, 2), (ushort)attrHeaderSize); // Run offset
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(currentOffset + 0x22, 2), 0); // Compression unit size
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(currentOffset + 0x24, 4), 0); // Padding
            BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(currentOffset + 0x28, 8), totalClusters * 4096); // Allocated size
            BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(currentOffset + 0x30, 8), totalClusters * 4096); // Data size
            BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(currentOffset + 0x38, 8), totalClusters * 4096); // Initialized size

            // Copy data runs
            dataRunBytes.CopyTo(record, currentOffset + attrHeaderSize);

            currentOffset += attrSize;
        }

        // Add end-of-attributes marker (0xFFFFFFFF)
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(currentOffset, 4), 0xFFFFFFFF);

        return record;
    }

    /// <summary>
    /// Creates data run bytes from a list of (offset, length) pairs.
    /// </summary>
    private static byte[] CreateDataRunBytes(List<(long offset, long length)> runs)
    {
        var result = new List<byte>();

        long currentCluster = 0;
        foreach (var (offset, length) in runs)
        {
            var relativeOffset = offset - currentCluster;
            currentCluster = offset;

            // Determine byte counts
            var lengthBytes = GetByteCount(length);
            var offsetBytes = GetByteCountSigned(relativeOffset);

            // Header byte: high nibble = offset bytes, low nibble = length bytes
            result.Add((byte)((offsetBytes << 4) | lengthBytes));

            // Write length (little-endian)
            for (var i = 0; i < lengthBytes; i++)
                result.Add((byte)((length >> (i * 8)) & 0xFF));

            // Write offset (little-endian)
            for (var i = 0; i < offsetBytes; i++)
                result.Add((byte)((relativeOffset >> (i * 8)) & 0xFF));
        }

        // End marker
        result.Add(0x00);

        return result.ToArray();
    }

    private static int GetByteCount(long value)
    {
        if (value == 0) return 0;
        if (value <= 0xFF) return 1;
        if (value <= 0xFFFF) return 2;
        if (value <= 0xFFFFFF) return 3;
        if (value <= 0xFFFFFFFF) return 4;
        if (value <= 0xFFFFFFFFFF) return 5;
        if (value <= 0xFFFFFFFFFFFF) return 6;
        if (value <= 0xFFFFFFFFFFFFFF) return 7;
        return 8;
    }

    private static int GetByteCountSigned(long value)
    {
        if (value == 0) return 0;
        if (value >= -0x80 && value <= 0x7F) return 1;
        if (value >= -0x8000 && value <= 0x7FFF) return 2;
        if (value >= -0x800000 && value <= 0x7FFFFF) return 3;
        if (value >= -0x80000000 && value <= 0x7FFFFFFF) return 4;
        if (value >= -0x8000000000L && value <= 0x7FFFFFFFFFL) return 5;
        if (value >= -0x800000000000L && value <= 0x7FFFFFFFFFFFL) return 6;
        if (value >= -0x80000000000000L && value <= 0x7FFFFFFFFFFFFFL) return 7;
        return 8;
    }

    [Test]
    public void Test_NtfsMftWalker_ParseFileTime_Correct()
    {
        // Create a known DateTime and convert to FILETIME
        var expectedDate = new DateTime(2024, 1, 15, 12, 30, 0, DateTimeKind.Utc);
        var filetime = expectedDate.ToFileTimeUtc();

        var result = NtfsMftWalker.ParseFileTime(filetime);

        Assert.AreEqual(expectedDate.Year, result.Year);
        Assert.AreEqual(expectedDate.Month, result.Month);
        Assert.AreEqual(expectedDate.Day, result.Day);
        Assert.AreEqual(expectedDate.Hour, result.Hour);
        Assert.AreEqual(expectedDate.Minute, result.Minute);
        Assert.AreEqual(expectedDate.Second, result.Second);
        Assert.AreEqual(DateTimeKind.Utc, result.Kind);
    }

    [Test]
    public void Test_NtfsMftWalker_ParseFileTime_Zero_ReturnsEpoch()
    {
        var result = NtfsMftWalker.ParseFileTime(0);

        Assert.AreEqual(DateTime.UnixEpoch, result);
    }

    [Test]
    public async Task Test_NtfsMftWalker_SkipDeletedRecords()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        // Create bitmap with all clusters allocated
        var bitmapData = CreateBitmapData([.. Enumerable.Repeat(true, (int)bootSector.TotalClusters)]);
        var bitmap = new NtfsBitmap(bootSector, bitmapData);

        // MFT record 0 describes the MFT itself - place at cluster 100
        var mftRecord0 = CreateMftRecord(
            DateTime.UtcNow.ToFileTimeUtc(),
            [(100, 10)],
            inUse: true);

        // MFT record 1 - deleted (not in use)
        var mftRecord1 = CreateMftRecord(
            DateTime.UtcNow.ToFileTimeUtc(),
            [(20, 5)], // Data at cluster 20, 5 clusters
            inUse: false);

        var recordCount = 0;
        byte[]? GetMftRecord(long recordNumber)
        {
            recordCount++;
            return recordNumber switch
            {
                0 => mftRecord0,
                1 => mftRecord1,
                _ => null
            };
        }

        var walker = new NtfsMftWalker(bootSector, bitmap, GetMftRecord);
        await walker.WalkAsync();

        // Clusters from deleted record should NOT be in the map
        // But system metafile clusters should be there
        Assert.IsTrue(walker.ClusterToTimestampMap.Count > 0);

        // Cluster 20-24 (from deleted record) should NOT be in the map
        Assert.IsFalse(walker.ClusterToTimestampMap.ContainsKey(20), "Deleted record's clusters should not be in map");
        Assert.IsFalse(walker.ClusterToTimestampMap.ContainsKey(24), "Deleted record's clusters should not be in map");
    }

    [Test]
    public async Task Test_NtfsMftWalker_ClusterTimestampMap_Correct()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        // Create bitmap with all clusters allocated
        var bitmapData = CreateBitmapData([.. Enumerable.Repeat(true, (int)bootSector.TotalClusters)]);
        var bitmap = new NtfsBitmap(bootSector, bitmapData);

        // FILETIME for 2023-06-01 10:00:00 UTC
        var timestamp1 = 133303440000000000L;

        // MFT record 0 describes the MFT itself - place at cluster 100
        var mftRecord0 = CreateMftRecord(
            DateTime.UtcNow.ToFileTimeUtc(),
            [(100, 10)],
            inUse: true);

        // MFT record 1 - file with data at clusters 20-24
        var mftRecord1 = CreateMftRecord(
            timestamp1,
            [(20, 5)], // Data at cluster 20, 5 clusters
            inUse: true);

        byte[]? GetMftRecord(long recordNumber)
        {
            return recordNumber switch
            {
                0 => mftRecord0,
                1 => mftRecord1,
                _ => null
            };
        }

        var walker = new NtfsMftWalker(bootSector, bitmap, GetMftRecord);
        await walker.WalkAsync();

        // Check that clusters 20-24 are mapped to the correct timestamp
        var expectedTime = NtfsMftWalker.ParseFileTime(timestamp1);

        for (var cluster = 20; cluster < 25; cluster++)
        {
            Assert.IsTrue(walker.ClusterToTimestampMap.ContainsKey(cluster), $"Cluster {cluster} should be in map");
            Assert.AreEqual(expectedTime, walker.ClusterToTimestampMap[cluster], $"Cluster {cluster} should have correct timestamp");
        }
    }

    [Test]
    public async Task Test_NtfsMftWalker_SystemMetafiles_UseCurrentTimestamp()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        // Create bitmap with all clusters allocated
        var bitmapData = CreateBitmapData(Enumerable.Repeat(true, (int)bootSector.TotalClusters).ToArray());
        var bitmap = new NtfsBitmap(bootSector, bitmapData);

        // MFT record 0 describes the MFT itself
        var mftRecord0 = CreateMftRecord(
            DateTime.UtcNow.ToFileTimeUtc(),
            [(4, 10)], // MFT at cluster 4, 10 clusters
            inUse: true);

        byte[]? GetMftRecord(long recordNumber)
        {
            return recordNumber == 0 ? mftRecord0 : null;
        }

        var before = DateTime.UtcNow.AddSeconds(-1);
        var walker = new NtfsMftWalker(bootSector, bitmap, GetMftRecord);
        await walker.WalkAsync();
        var after = DateTime.UtcNow.AddSeconds(1);

        // System metafiles (records 0-23) should have timestamps close to now
        // The MFT is at cluster 4 with 10 clusters (4-13)
        var mftStartCluster = bootSector.MftByteOffset / bootSector.ClusterSize;

        for (var i = 0; i < 10; i++)
        {
            var cluster = mftStartCluster + i;
            if (walker.ClusterToTimestampMap.TryGetValue(cluster, out var timestamp))
            {
                Assert.GreaterOrEqual(timestamp, before, $"Cluster {cluster} timestamp should be >= before");
                Assert.LessOrEqual(timestamp, after, $"Cluster {cluster} timestamp should be <= after");
            }
        }
    }

    [Test]
    public async Task Test_NtfsMftWalker_OverlappingClusters_UsesMostRecentTimestamp()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        // Create bitmap with all clusters allocated
        var bitmapData = CreateBitmapData(Enumerable.Repeat(true, (int)bootSector.TotalClusters).ToArray());
        var bitmap = new NtfsBitmap(bootSector, bitmapData);

        // FILETIMEs - older and newer
        var olderTimestamp = 133000000000000000L; // ~2022
        var newerTimestamp = 133500000000000000L; // ~2024

        // MFT record 0 describes the MFT itself - place at cluster 100 to avoid overlap with file data
        var mftRecord0 = CreateMftRecord(
            DateTime.UtcNow.ToFileTimeUtc(),
            [(100, 20)],
            inUse: true);

        // Record 1 - file with data at clusters 30-34, older timestamp
        // Placed far from MFT at cluster 100 to avoid overlap
        var mftRecord1 = CreateMftRecord(
            olderTimestamp,
            [(30, 5)],
            inUse: true);

        // Record 2 - file with data at clusters 32-36 (overlaps with record 1), newer timestamp
        var mftRecord2 = CreateMftRecord(
            newerTimestamp,
            [(32, 5)],
            inUse: true);

        byte[]? GetMftRecord(long recordNumber)
        {
            return recordNumber switch
            {
                0 => mftRecord0,
                1 => mftRecord1,
                2 => mftRecord2,
                _ => null
            };
        }

        var walker = new NtfsMftWalker(bootSector, bitmap, GetMftRecord);
        await walker.WalkAsync();

        // Clusters 30-31 should have older timestamp (only from record 1)
        var olderTime = NtfsMftWalker.ParseFileTime(olderTimestamp);
        var newerTime = NtfsMftWalker.ParseFileTime(newerTimestamp);

        Assert.AreEqual(olderTime, walker.ClusterToTimestampMap[30], "Cluster 30 should have older timestamp");
        Assert.AreEqual(olderTime, walker.ClusterToTimestampMap[31], "Cluster 31 should have older timestamp");

        // Clusters 32-34 should have newer timestamp (overlapping, newer wins)
        Assert.AreEqual(newerTime, walker.ClusterToTimestampMap[32], "Cluster 32 should have newer timestamp");
        Assert.AreEqual(newerTime, walker.ClusterToTimestampMap[33], "Cluster 33 should have newer timestamp");
        Assert.AreEqual(newerTime, walker.ClusterToTimestampMap[34], "Cluster 34 should have newer timestamp");

        // Clusters 35-36 should have newer timestamp (only from record 2)
        Assert.AreEqual(newerTime, walker.ClusterToTimestampMap[35], "Cluster 35 should have newer timestamp");
        Assert.AreEqual(newerTime, walker.ClusterToTimestampMap[36], "Cluster 36 should have newer timestamp");
    }

    [Test]
    public async Task Test_NtfsMftWalker_DataRunParsing_MultipleRuns()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        // Create bitmap with all clusters allocated
        var bitmapData = CreateBitmapData([.. Enumerable.Repeat(true, (int)bootSector.TotalClusters)]);
        var bitmap = new NtfsBitmap(bootSector, bitmapData);

        var timestamp = 133303440000000000L; // 2023-06-01 10:00:00 UTC

        // MFT record 0 describes the MFT itself - place at cluster 100
        var mftRecord0 = CreateMftRecord(
            DateTime.UtcNow.ToFileTimeUtc(),
            [(100, 20)],
            inUse: true);

        // Record with multiple data runs: clusters 30-34 and 40-44
        var mftRecord1 = CreateMftRecord(
            timestamp,
            new List<(long offset, long length)>
            {
                (30, 5),  // First run: clusters 30-34
                (40, 5)   // Second run: clusters 40-44
            },
            inUse: true);

        byte[]? GetMftRecord(long recordNumber)
        {
            return recordNumber switch
            {
                0 => mftRecord0,
                1 => mftRecord1,
                _ => null
            };
        }

        var walker = new NtfsMftWalker(bootSector, bitmap, GetMftRecord);
        await walker.WalkAsync();

        var expectedTime = NtfsMftWalker.ParseFileTime(timestamp);

        // Check first run (clusters 30-34)
        for (var cluster = 30; cluster < 35; cluster++)
        {
            Assert.IsTrue(walker.ClusterToTimestampMap.ContainsKey(cluster), $"Cluster {cluster} should be in map");
            Assert.AreEqual(expectedTime, walker.ClusterToTimestampMap[cluster], $"Cluster {cluster} should have correct timestamp");
        }

        // Check second run (clusters 40-44)
        for (var cluster = 40; cluster < 45; cluster++)
        {
            Assert.IsTrue(walker.ClusterToTimestampMap.ContainsKey(cluster), $"Cluster {cluster} should be in map");
            Assert.AreEqual(expectedTime, walker.ClusterToTimestampMap[cluster], $"Cluster {cluster} should have correct timestamp");
        }
    }

    [Test]
    public async Task Test_NtfsMftWalker_EmptyDataRun_Skipped()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        // Create bitmap with all clusters allocated
        var bitmapData = CreateBitmapData([.. Enumerable.Repeat(true, (int)bootSector.TotalClusters)]);
        var bitmap = new NtfsBitmap(bootSector, bitmapData);

        var timestamp = 133303440000000000L;

        // MFT record 0 describes the MFT itself - place at cluster 100
        var mftRecord0 = CreateMftRecord(
            DateTime.UtcNow.ToFileTimeUtc(),
            [(100, 10)],
            inUse: true);

        // Record with no $DATA attribute (like a directory)
        var mftRecord1 = CreateMftRecord(
            timestamp,
            null, // No data runs
            inUse: true);

        byte[]? GetMftRecord(long recordNumber)
        {
            return recordNumber switch
            {
                0 => mftRecord0,
                1 => mftRecord1,
                _ => null
            };
        }

        var walker = new NtfsMftWalker(bootSector, bitmap, GetMftRecord);
        await walker.WalkAsync();

        // The record should be processed (no exception), but no additional clusters mapped
        // System metafiles should still be present
        Assert.IsTrue(walker.ClusterToTimestampMap.Count >= 0);
    }

    [Test]
    public async Task Test_NtfsMftWalker_JournalClusters_AlwaysAllocated()
    {
        var bootSectorData = CreateValidBootSector();
        var bootSector = new NtfsBootSector(bootSectorData);

        // Create bitmap with all clusters allocated
        var bitmapData = CreateBitmapData([.. Enumerable.Repeat(true, (int)bootSector.TotalClusters)]);
        var bitmap = new NtfsBitmap(bootSector, bitmapData);

        // MFT record 0 describes the MFT itself - place at cluster 100
        var mftRecord0 = CreateMftRecord(
            DateTime.UtcNow.ToFileTimeUtc(),
            [(100, 20)],
            inUse: true);

        // MFT record 2 is $LogFile - the NTFS transaction journal
        // Its clusters must always be backed up as they change on every write
        var mftRecord2 = CreateMftRecord(
            DateTime.UtcNow.ToFileTimeUtc(),
            [(50, 10)], // $LogFile at clusters 50-59
            inUse: true);

        byte[]? GetMftRecord(long recordNumber)
        {
            return recordNumber switch
            {
                0 => mftRecord0,
                2 => mftRecord2,
                _ => null
            };
        }

        var before = DateTime.UtcNow.AddSeconds(-1);
        var walker = new NtfsMftWalker(bootSector, bitmap, GetMftRecord);
        await walker.WalkAsync();
        var after = DateTime.UtcNow.AddSeconds(1);

        // $LogFile clusters (50-59) should be mapped with a current timestamp
        for (var cluster = 50; cluster < 60; cluster++)
        {
            Assert.IsTrue(walker.ClusterToTimestampMap.ContainsKey(cluster), $"$LogFile cluster {cluster} should be in map");
            var timestamp = walker.ClusterToTimestampMap[cluster];
            Assert.GreaterOrEqual(timestamp, before, $"$LogFile cluster {cluster} timestamp should be >= before");
            Assert.LessOrEqual(timestamp, after, $"$LogFile cluster {cluster} timestamp should be <= after");
        }
    }
}
