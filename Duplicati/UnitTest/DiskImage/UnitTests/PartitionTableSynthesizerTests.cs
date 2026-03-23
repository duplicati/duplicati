using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.General;
using Duplicati.Proprietary.DiskImage.Partition;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

public partial class DiskImageUnitTests : BasicSetupHelper
{

    [Test]
    public void Test_PartitionTableSynthesizer_SynthesizeMBR_ContainsValidBootSignature()
    {
        // Create a GeometryMetadata with known partition info
        var metadata = new GeometryMetadata
        {
            Disk = new DiskGeometry
            {
                SectorSize = 512,
                Size = 100 * MiB,
                Sectors = (int)(100 * MiB / 512)
            },
            PartitionTable = new PartitionTableGeometry
            {
                Type = PartitionTableType.MBR,
                SectorSize = 512
            },
            Partitions =
            [
                new PartitionGeometry
                {
                    Number = 1,
                    Type = PartitionType.Primary,
                    FilesystemType = FileSystemType.FAT32,
                    StartOffset = 512 * 2048, // Start at sector 2048
                    Size = 20 * MiB,
                    TableType = PartitionTableType.MBR
                },
                new PartitionGeometry
                {
                    Number = 2,
                    Type = PartitionType.Primary,
                    FilesystemType = FileSystemType.FAT32,
                    StartOffset = 512 * 2048 + 20 * MiB,
                    Size = 20 * MiB,
                    TableType = PartitionTableType.MBR
                }
            ]
        };

        // Synthesize MBR
        var mbrData = PartitionTableSynthesizer.SynthesizeMBR(metadata);

        Assert.IsNotNull(mbrData, "MBR data should not be null.");
        Assert.GreaterOrEqual(mbrData.Length, 512, "MBR should be at least one sector.");

        // Verify boot signature at offset 510-511 (0x55 0xAA)
        Assert.AreEqual(0x55, mbrData[510], "Boot signature first byte should be 0x55.");
        Assert.AreEqual(0xAA, mbrData[511], "Boot signature second byte should be 0xAA.");
    }

    [Test]
    public void Test_PartitionTableSynthesizer_SynthesizeGPT_ContainsValidSignature()
    {
        // Create a GeometryMetadata with known partition info
        var metadata = new GeometryMetadata
        {
            Disk = new DiskGeometry
            {
                SectorSize = 512,
                Size = 100 * MiB,
                Sectors = (int)(100 * MiB / 512)
            },
            PartitionTable = new PartitionTableGeometry
            {
                Type = PartitionTableType.GPT,
                SectorSize = 512,
                HasProtectiveMbr = true
            },
            Partitions =
            [
                new PartitionGeometry
                {
                    Number = 1,
                    Type = PartitionType.Primary,
                    FilesystemType = FileSystemType.FAT32,
                    StartOffset = 512 * 2048, // Start at sector 2048
                    Size = 20 * MiB,
                    TableType = PartitionTableType.GPT,
                    Name = "Partition 1"
                },
                new PartitionGeometry
                {
                    Number = 2,
                    Type = PartitionType.Primary,
                    FilesystemType = FileSystemType.FAT32,
                    StartOffset = 512 * 2048 + 20 * MiB,
                    Size = 20 * MiB,
                    TableType = PartitionTableType.GPT,
                    Name = "Partition 2"
                }
            ]
        };

        // Synthesize GPT
        var gptData = PartitionTableSynthesizer.SynthesizeGPT(metadata);

        Assert.IsNotNull(gptData, "GPT data should not be null.");
        Assert.Greater(gptData.Length, 512 * 2, "GPT data should include protective MBR and GPT header.");

        // Verify protective MBR boot signature at offset 510-511
        Assert.AreEqual(0x55, gptData[510], "Protective MBR boot signature first byte should be 0x55.");
        Assert.AreEqual(0xAA, gptData[511], "Protective MBR boot signature second byte should be 0xAA.");

        // Verify GPT signature "EFI PART" at sector 1 (offset 512)
        var expectedSignature = "EFI PART"u8.ToArray();
        for (int i = 0; i < 8; i++)
            Assert.AreEqual(expectedSignature[i], gptData[512 + i], $"GPT signature byte {i} should match.");
    }

    [Test]
    public async Task Test_PartitionTableSynthesizer_MBRRoundTrip_PreservesPartitionData()
    {
        // Create a GeometryMetadata with known partition info
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
                Type = PartitionTableType.MBR,
                SectorSize = sectorSize
            },
            Partitions =
            [
                new PartitionGeometry
                {
                    Number = 1,
                    Type = PartitionType.Primary,
                    FilesystemType = FileSystemType.FAT32,
                    StartOffset = sectorSize * 2048,
                    Size = 20 * MiB,
                    TableType = PartitionTableType.MBR
                },
                new PartitionGeometry
                {
                    Number = 2,
                    Type = PartitionType.Primary,
                    FilesystemType = FileSystemType.FAT32,
                    StartOffset = sectorSize * 2048 + 20 * MiB,
                    Size = 20 * MiB,
                    TableType = PartitionTableType.MBR
                }
            ]
        };

        // Synthesize MBR
        var mbrData = PartitionTableSynthesizer.SynthesizeMBR(metadata);

        // Parse with PartitionTableFactory
        var partitionTable = await PartitionTableFactory.CreateAsync(mbrData, sectorSize, CancellationToken.None);

        Assert.IsNotNull(partitionTable, "Partition table should be detected.");
        Assert.AreEqual(PartitionTableType.MBR, partitionTable!.TableType, "Should detect MBR partition table.");

        // Verify partition count
        var partitions = new List<IPartition>();
        await foreach (var partition in partitionTable.EnumeratePartitions(CancellationToken.None))
            partitions.Add(partition);

        Assert.AreEqual(2, partitions.Count, "Should have 2 partitions.");

        // Verify partition properties match original metadata
        for (int i = 0; i < partitions.Count; i++)
        {
            var originalPart = metadata.Partitions![i];
            var parsedPart = partitions[i];

            Assert.AreEqual(originalPart.Number, parsedPart.PartitionNumber, $"Partition {i + 1} number should match.");
            Assert.AreEqual(originalPart.StartOffset, parsedPart.StartOffset, $"Partition {i + 1} start offset should match.");
            Assert.AreEqual(originalPart.Size, parsedPart.Size, $"Partition {i + 1} size should match.");
        }

        partitionTable.Dispose();
    }

    [Test]
    public async Task Test_PartitionTableSynthesizer_GPTRoundTrip_PreservesPartitionData()
    {
        // Create a GeometryMetadata with known partition info
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
                SectorSize = sectorSize,
                HasProtectiveMbr = true
            },
            Partitions =
            [
                new PartitionGeometry
                {
                    Number = 1,
                    Type = PartitionType.Primary,
                    FilesystemType = FileSystemType.FAT32,
                    StartOffset = sectorSize * 2048,
                    Size = 20 * MiB,
                    TableType = PartitionTableType.GPT,
                    Name = "Test Partition 1"
                },
                new PartitionGeometry
                {
                    Number = 2,
                    Type = PartitionType.Primary,
                    FilesystemType = FileSystemType.FAT32,
                    StartOffset = sectorSize * 2048 + 20 * MiB,
                    Size = 20 * MiB,
                    TableType = PartitionTableType.GPT,
                    Name = "Test Partition 2"
                }
            ]
        };

        // Synthesize GPT
        var gptData = PartitionTableSynthesizer.SynthesizeGPT(metadata);

        // Parse with PartitionTableFactory
        var partitionTable = await PartitionTableFactory.CreateAsync(gptData, sectorSize, CancellationToken.None);

        Assert.IsNotNull(partitionTable, "Partition table should be detected.");
        Assert.AreEqual(PartitionTableType.GPT, partitionTable!.TableType, "Should detect GPT partition table.");

        // Verify partition count
        var partitions = new List<IPartition>();
        await foreach (var partition in partitionTable.EnumeratePartitions(CancellationToken.None))
            partitions.Add(partition);

        Assert.AreEqual(2, partitions.Count, "Should have 2 partitions.");

        // Verify partition properties match original metadata
        for (int i = 0; i < partitions.Count; i++)
        {
            var originalPart = metadata.Partitions![i];
            var parsedPart = partitions[i];

            Assert.AreEqual(originalPart.Number, parsedPart.PartitionNumber, $"Partition {i + 1} number should match.");
            Assert.AreEqual(originalPart.StartOffset, parsedPart.StartOffset, $"Partition {i + 1} start offset should match.");
            Assert.AreEqual(originalPart.Size, parsedPart.Size, $"Partition {i + 1} size should match.");
        }

        partitionTable.Dispose();
    }

    [Test]
    public void Test_PartitionTableSynthesizer_SynthesizePartitionTable_MBR_ReturnsMBRData()
    {
        var metadata = new GeometryMetadata
        {
            Disk = new DiskGeometry { SectorSize = 512, Size = 50 * MiB },
            PartitionTable = new PartitionTableGeometry { Type = PartitionTableType.MBR },
            Partitions =
            [
                new PartitionGeometry
                {
                    Number = 1,
                    Type = PartitionType.Primary,
                    StartOffset = 512 * 2048,
                    Size = 20 * MiB,
                    TableType = PartitionTableType.MBR
                }
            ]
        };

        var result = PartitionTableSynthesizer.SynthesizePartitionTable(metadata);

        Assert.IsNotNull(result, "Should return MBR data.");
        Assert.AreEqual(0x55, result![510], "Should have valid boot signature.");
        Assert.AreEqual(0xAA, result[511], "Should have valid boot signature.");
    }

    [Test]
    public void Test_PartitionTableSynthesizer_SynthesizePartitionTable_GPT_ReturnsGPTData()
    {
        var metadata = new GeometryMetadata
        {
            Disk = new DiskGeometry { SectorSize = 512, Size = 50 * MiB, Sectors = (int)(50 * MiB / 512) },
            PartitionTable = new PartitionTableGeometry { Type = PartitionTableType.GPT },
            Partitions =
            [
                new PartitionGeometry
                {
                    Number = 1,
                    Type = PartitionType.Primary,
                    StartOffset = 512 * 2048,
                    Size = 20 * MiB,
                    TableType = PartitionTableType.GPT
                }
            ]
        };

        var result = PartitionTableSynthesizer.SynthesizePartitionTable(metadata);

        Assert.IsNotNull(result, "Should return GPT data.");
        // Check for "EFI PART" signature at sector 1
        var expectedSignature = "EFI PART"u8.ToArray();
        for (int i = 0; i < 8; i++)
            Assert.AreEqual(expectedSignature[i], result![512 + i], $"GPT signature byte {i} should match.");
    }

    [Test]
    public void Test_PartitionTableSynthesizer_SynthesizePartitionTable_NullPartitionTable_ReturnsNull()
    {
        var metadata = new GeometryMetadata
        {
            Disk = new DiskGeometry { SectorSize = 512 },
            PartitionTable = null
        };

        var result = PartitionTableSynthesizer.SynthesizePartitionTable(metadata);

        Assert.IsNull(result, "Should return null when PartitionTable is null.");
    }

    [Test]
    public void Test_PartitionTableSynthesizer_SynthesizePartitionTable_UnknownType_ReturnsNull()
    {
        var metadata = new GeometryMetadata
        {
            Disk = new DiskGeometry { SectorSize = 512 },
            PartitionTable = new PartitionTableGeometry { Type = PartitionTableType.Unknown }
        };

        var result = PartitionTableSynthesizer.SynthesizePartitionTable(metadata);

        Assert.IsNull(result, "Should return null for Unknown partition table type.");
    }

}