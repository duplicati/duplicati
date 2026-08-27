using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.General;
using Duplicati.Proprietary.DiskImage.Partition;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

#nullable enable

public partial class DiskImageUnitTests : BasicSetupHelper
{
    [Test]
    public void Test_PartitionCreator_FindFreeSpace_FirstFit()
    {
        // Two occupied ranges: [1 MiB, 11 MiB) and [21 MiB, 31 MiB)
        var existing = new[]
        {
            (Start: 1 * MiB, Size: 10 * MiB),
            (Start: 21 * MiB, Size: 10 * MiB)
        };

        // A 5 MiB partition fits in the gap starting at 11 MiB (already aligned)
        var result = PartitionCreator.FindFreeSpace(existing, 1 * MiB, 100 * MiB, 5 * MiB, PartitionCreator.DefaultAlignment);
        Assert.AreEqual(11 * MiB, result, "Should find the first aligned gap after the first partition.");
    }

    [Test]
    public void Test_PartitionCreator_FindFreeSpace_PrefersSourceOffset()
    {
        var existing = new[]
        {
            (Start: 1 * MiB, Size: 10 * MiB)
        };

        // The preferred range [40 MiB, 50 MiB) is free
        var result = PartitionCreator.FindFreeSpace(existing, 1 * MiB, 100 * MiB, 10 * MiB, PartitionCreator.DefaultAlignment, preferredStart: 40 * MiB);
        Assert.AreEqual(40 * MiB, result, "Should honor the preferred start offset when the range is free.");
    }

    [Test]
    public void Test_PartitionCreator_FindFreeSpace_PreferredOccupiedFallsBackToFirstFit()
    {
        var existing = new[]
        {
            (Start: 1 * MiB, Size: 10 * MiB),
            (Start: 40 * MiB, Size: 20 * MiB)
        };

        // The preferred range overlaps the second partition
        var result = PartitionCreator.FindFreeSpace(existing, 1 * MiB, 100 * MiB, 10 * MiB, PartitionCreator.DefaultAlignment, preferredStart: 40 * MiB);
        Assert.AreEqual(11 * MiB, result, "Should fall back to the first fitting gap.");
    }

    [Test]
    public void Test_PartitionCreator_FindFreeSpace_NoSpaceReturnsNull()
    {
        var existing = new[]
        {
            (Start: 1 * MiB, Size: 98 * MiB)
        };

        var result = PartitionCreator.FindFreeSpace(existing, 1 * MiB, 100 * MiB, 10 * MiB, PartitionCreator.DefaultAlignment);
        Assert.IsNull(result, "Should return null when no gap fits the required size.");
    }

    [Test]
    public void Test_PartitionCreator_FindFreeSpace_AlignsStart()
    {
        // Occupied range ends at an unaligned offset
        var existing = new[]
        {
            (Start: 512L, Size: 1000L)
        };

        var result = PartitionCreator.FindFreeSpace(existing, 512, 100 * MiB, 10 * MiB, PartitionCreator.DefaultAlignment);
        Assert.AreEqual(PartitionCreator.DefaultAlignment, result, "Should align the start offset up to the alignment boundary.");
    }

    [Test]
    public void Test_PartitionCreator_TryWriteMbrEntry_PreservesExistingContent()
    {
        var sectorSize = 512;
        var mbr = new byte[sectorSize];

        // Fill boot code with a marker and add the boot signature
        for (var i = 0; i < 446; i++)
            mbr[i] = 0xAB;
        mbr[510] = 0x55;
        mbr[511] = 0xAA;

        // Occupy slot 1 with an existing entry (type byte set)
        mbr[446 + 4] = 0x0B;

        var result = PartitionCreator.TryWriteMbrEntry(mbr, sectorSize, slot: 2, startOffset: 2 * MiB, size: 10 * MiB, FileSystemType.NTFS, PartitionType.Primary);

        Assert.IsTrue(result, "Should write the entry into the free slot.");
        Assert.AreEqual(0xAB, mbr[0], "Boot code must be preserved.");
        Assert.AreEqual(0xAB, mbr[445], "Boot code must be preserved.");
        Assert.AreEqual(0x0B, mbr[446 + 4], "The existing entry must be preserved.");
        Assert.AreEqual(0x55, mbr[510], "Boot signature must be preserved.");
        Assert.AreEqual(0xAA, mbr[511], "Boot signature must be preserved.");

        // Verify the new entry at slot 2
        var offset = 446 + 16;
        Assert.AreNotEqual(0, mbr[offset + 4], "Partition type byte should be set.");
        Assert.AreEqual((uint)(2 * MiB / sectorSize), BinaryPrimitives.ReadUInt32LittleEndian(mbr.AsSpan(offset + 8, 4)), "Start LBA should match.");
        Assert.AreEqual((uint)(10 * MiB / sectorSize), BinaryPrimitives.ReadUInt32LittleEndian(mbr.AsSpan(offset + 12, 4)), "Size in sectors should match.");
    }

    [Test]
    public void Test_PartitionCreator_TryWriteMbrEntry_FailsOnOccupiedSlot()
    {
        var mbr = new byte[512];
        mbr[446 + 4] = 0x0B; // Slot 1 occupied

        var result = PartitionCreator.TryWriteMbrEntry(mbr, 512, slot: 1, startOffset: 2 * MiB, size: 10 * MiB, FileSystemType.NTFS, PartitionType.Primary);

        Assert.IsFalse(result, "Should fail when the slot is occupied.");
    }

    [Test]
    public void Test_PartitionCreator_TryWriteMbrEntry_FailsBeyondMbrLimits()
    {
        var mbr = new byte[512];

        // Start beyond the 32-bit LBA range
        var result = PartitionCreator.TryWriteMbrEntry(mbr, 512, slot: 1, startOffset: 3L * 1024 * 1024 * 1024 * 1024, size: 10 * MiB, FileSystemType.NTFS, PartitionType.Primary);

        Assert.IsFalse(result, "Should fail when the partition is beyond the MBR addressable range.");
    }

    [Test]
    public async Task Test_PartitionCreator_GptEntryRoundtrip()
    {
        var sectorSize = 512;
        var metadata = new GeometryMetadata
        {
            Disk = new DiskGeometry
            {
                SectorSize = sectorSize,
                Size = 100 * MiB,
                Sectors = (int)(100 * MiB / sectorSize)
            },
            PartitionTable = new PartitionTableGeometry
            {
                Type = PartitionTableType.GPT,
                SectorSize = sectorSize
            },
            Partitions =
            [
                new PartitionGeometry
                {
                    Number = 1,
                    Type = PartitionType.Primary,
                    FilesystemType = FileSystemType.FAT32,
                    StartOffset = 1 * MiB,
                    Size = 20 * MiB,
                    TableType = PartitionTableType.GPT
                }
            ]
        };

        // Synthesize a GPT table with one partition
        var gptData = PartitionTableSynthesizer.SynthesizeGPT(metadata);

        // Add a second partition entry to the entries area (starts at LBA 2)
        var entriesOffset = 2 * sectorSize;
        var entriesByteSize = 128 * PartitionConstants.GptPartitionEntrySize;
        var entries = gptData[entriesOffset..(entriesOffset + entriesByteSize)];

        var newStartLba = 30 * MiB / sectorSize;
        var newEndLba = newStartLba + (10 * MiB / sectorSize) - 1;
        var newGuid = Guid.NewGuid();

        var written = PartitionCreator.TryWriteGptEntry(entries, PartitionConstants.GptPartitionEntrySize, slot: 2, startLba: newStartLba, endLba: newEndLba, PartitionType.Primary, newGuid, "Restored");
        Assert.IsTrue(written, "Should write the entry into the free GPT slot.");

        // Writing to the same slot again must fail
        var rewritten = PartitionCreator.TryWriteGptEntry(entries, PartitionConstants.GptPartitionEntrySize, slot: 2, startLba: newStartLba, endLba: newEndLba, PartitionType.Primary, newGuid, "Restored");
        Assert.IsFalse(rewritten, "Should fail when the GPT slot is occupied.");

        // Patch the header with the updated entries CRC and a fresh header CRC
        var headerSector = gptData[sectorSize..(2 * sectorSize)];
        PartitionCreator.PatchGptHeader(headerSector, entries, entriesByteSize);

        // Copy the patched data back into the disk image
        entries.CopyTo(gptData, entriesOffset);
        headerSector.CopyTo(gptData, sectorSize);

        // Verify the header CRC is valid
        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(gptData.AsSpan(sectorSize + 16, 4));
        var crcBuffer = gptData[sectorSize..(sectorSize + PartitionConstants.GptHeaderSize)];
        BinaryPrimitives.WriteUInt32LittleEndian(crcBuffer.AsSpan(16, 4), 0u);
        var computedCrc = Crc32.Calculate(crcBuffer, 0, PartitionConstants.GptHeaderSize);
        Assert.AreEqual(computedCrc, storedCrc, "The patched GPT header CRC should be valid.");

        // Verify the entries CRC is valid
        var storedEntriesCrc = BinaryPrimitives.ReadUInt32LittleEndian(gptData.AsSpan(sectorSize + 88, 4));
        var computedEntriesCrc = Crc32.Calculate(gptData, entriesOffset, entriesByteSize);
        Assert.AreEqual(computedEntriesCrc, storedEntriesCrc, "The patched GPT entries CRC should be valid.");

        // Parse the modified table and verify both partitions are present
        var table = await PartitionTableFactory.CreateAsync(gptData, sectorSize, CancellationToken.None);
        Assert.IsNotNull(table, "The modified GPT should still parse.");
        Assert.AreEqual(PartitionTableType.GPT, table!.TableType, "The table should be detected as GPT.");

        var partitions = await table.EnumeratePartitions(CancellationToken.None).ToListAsync();
        Assert.AreEqual(2, partitions.Count, "The table should contain both partitions.");

        var added = partitions.FirstOrDefault(p => p.PartitionNumber == 2);
        Assert.IsNotNull(added, "The added partition should be present.");
        Assert.AreEqual((long)newStartLba * sectorSize, added!.StartOffset, "The added partition start offset should match.");
        Assert.AreEqual(10 * MiB, added.Size, "The added partition size should match.");
        Assert.AreEqual(newGuid, added.VolumeGuid, "The added partition GUID should match.");
        Assert.AreEqual("Restored", added.Name, "The added partition name should match.");
    }
}
