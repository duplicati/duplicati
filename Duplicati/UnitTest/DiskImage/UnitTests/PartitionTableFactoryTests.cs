using System;
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
    public async Task Test_PartitionTableFactory_GPTBytes_DetectsGPT()
    {
        var sectorSize = 512;
        var diskBytes = new byte[sectorSize * 4];

        // MBR (sector 0) - Protective MBR with GPT signature
        diskBytes[510] = 0x55;
        diskBytes[511] = 0xAA;
        diskBytes[450] = 0xEE;

        // GPT header (sector 1) - "EFI PART" signature
        var gptSignature = "EFI PART"u8.ToArray();
        Buffer.BlockCopy(gptSignature, 0, diskBytes, sectorSize, 8);

        var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

        Assert.IsNotNull(partitionTable, "Partition table should be detected.");
        Assert.AreEqual(PartitionTableType.GPT, partitionTable!.TableType, "Should detect GPT partition table.");
    }

    [Test]
    public async Task Test_PartitionTableFactory_MBRBytes_DetectsMBR()
    {
        var sectorSize = 512;
        var diskBytes = new byte[sectorSize];

        diskBytes[510] = 0x55;
        diskBytes[511] = 0xAA;
        diskBytes[450] = 0x83;

        var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

        Assert.IsNotNull(partitionTable, "Partition table should be detected.");
        Assert.AreEqual(PartitionTableType.MBR, partitionTable!.TableType, "Should detect MBR partition table.");
    }

    [Test]
    public async Task Test_PartitionTableFactory_InvalidBytes_ReturnsUnknown()
    {
        var sectorSize = 512;
        var diskBytes = new byte[sectorSize];

        var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

        Assert.IsNotNull(partitionTable, "Partition table should be created even for unknown type.");
        Assert.AreEqual(PartitionTableType.Unknown, partitionTable!.TableType, "Should detect Unknown partition table for invalid bytes.");
    }

    [Test]
    public async Task Test_PartitionTableFactory_ProtectiveMBRType_DetectsGPT()
    {
        var sectorSize = 512;
        var diskBytes = new byte[sectorSize * 4];

        diskBytes[510] = 0x55;
        diskBytes[511] = 0xAA;
        diskBytes[450] = 0xEE;

        var gptSignature = "EFI PART"u8.ToArray();
        Buffer.BlockCopy(gptSignature, 0, diskBytes, sectorSize, 8);

        var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

        Assert.IsNotNull(partitionTable, "Partition table should be detected.");
        Assert.AreEqual(PartitionTableType.GPT, partitionTable!.TableType, "Should detect GPT when protective MBR type (0xEE) is present.");
    }

    [Test]
    public async Task Test_PartitionTableFactory_ProtectiveMBRWithoutGptHeader_FallsBackToMBR()
    {
        var sectorSize = 512;
        var diskBytes = new byte[sectorSize * 2];

        diskBytes[510] = 0x55;
        diskBytes[511] = 0xAA;
        diskBytes[450] = 0xEE;

        var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

        Assert.IsNotNull(partitionTable, "Partition table should be detected.");
        Assert.AreEqual(PartitionTableType.MBR, partitionTable!.TableType, "Should fall back to MBR when GPT header is invalid.");
    }

    [Test]
    public async Task Test_PartitionTableFactory_GPTFromRealDisk_DetectsGPT()
    {
        var sectorSize = s_gptRawDisk!.SectorSize;
        var sectorsToRead = 34;
        using var stream = await s_gptRawDisk.ReadSectorsAsync(0, sectorsToRead, CancellationToken.None);
        var diskBytes = new byte[sectorSize * sectorsToRead];
        await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

        var partitionTable = await PartitionTableFactory.CreateAsync(diskBytes, sectorSize, CancellationToken.None);

        Assert.IsNotNull(partitionTable, "Partition table should be detected from real disk.");
        Assert.AreEqual(PartitionTableType.GPT, partitionTable!.TableType, "Should detect GPT from real disk bytes.");
    }

}