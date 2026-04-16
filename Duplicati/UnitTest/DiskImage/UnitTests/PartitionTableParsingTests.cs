using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Partition;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

public partial class DiskImageUnitTests : BasicSetupHelper
{

    [Test]
    public async Task Test_GPT_ParseFromRealDisk_ReturnsCorrectPartitions()
    {
        var sectorSize = s_gptRawDisk!.SectorSize;

        // Read enough sectors to include protective MBR, GPT header, and partition entries
        var sectorsToRead = 34;
        using var stream = await s_gptRawDisk.ReadSectorsAsync(0, sectorsToRead, CancellationToken.None);
        var diskBytes = new byte[sectorSize * sectorsToRead];
        await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

        // Parse the GPT from bytes
        var gpt = new GPT(null);
        var parsed = await gpt.ParseAsync(diskBytes, sectorSize, CancellationToken.None);

        Assert.IsTrue(parsed, "GPT should parse successfully from real disk bytes.");

        // Verify partition count - we created 2 partitions
        var partitions = new List<IPartition>();
        await foreach (var partition in gpt.EnumeratePartitions(CancellationToken.None))
        {
            partitions.Add(partition);
        }

        Assert.AreEqual(2, partitions.Count, "Should have 2 partitions.");

        // Verify partition properties
        var firstPartition = partitions[0];
        Assert.IsNotNull(firstPartition, "First partition should exist.");
        Assert.AreEqual(1, firstPartition.PartitionNumber, "Partition number should be 1.");
        Assert.Greater(firstPartition.StartOffset, 0, "StartOffset should be greater than 0.");
        Assert.Greater(firstPartition.Size, 0, "Size should be greater than 0.");

        // Verify sector alignment
        Assert.AreEqual(0, firstPartition.StartOffset % sectorSize,
            "StartOffset should be sector-aligned.");
        Assert.AreEqual(0, firstPartition.Size % sectorSize,
            "Size should be sector-aligned.");

        gpt.Dispose();
    }

    [Test]
    public async Task Test_MBR_ParseFromRealDisk_ReturnsCorrectPartitions()
    {
        var sectorSize = s_mbrRawDisk!.SectorSize;

        // Read the MBR sector (first 512 bytes)
        using var stream = await s_mbrRawDisk.ReadSectorsAsync(0, 1, CancellationToken.None);
        var mbrBytes = new byte[sectorSize];
        await stream.ReadAtLeastAsync(mbrBytes, mbrBytes.Length, cancellationToken: CancellationToken.None);

        // Parse the MBR from bytes
        var mbr = new MBR(null);
        var parsed = await mbr.ParseAsync(mbrBytes, sectorSize, CancellationToken.None);

        Assert.IsTrue(parsed, "MBR should parse successfully from real disk bytes.");

        // Verify partition entries - we created 2 partitions
        Assert.AreEqual(2, mbr.NumPartitionEntries, "Should have 2 partition entries.");

        // Verify via EnumeratePartitions
        var partitions = new List<IPartition>();
        await foreach (var partition in mbr.EnumeratePartitions(CancellationToken.None))
        {
            partitions.Add(partition);
        }

        Assert.AreEqual(2, partitions.Count, "Should enumerate 2 partitions.");

        // Verify partition properties
        var firstPartition = partitions[0];
        Assert.IsNotNull(firstPartition, "First partition should exist.");
        Assert.AreEqual(1, firstPartition.PartitionNumber, "Partition number should be 1.");
        Assert.Greater(firstPartition.StartOffset, 0, "StartOffset should be greater than 0.");
        Assert.Greater(firstPartition.Size, 0, "Size should be greater than 0.");

        // Verify sector alignment
        Assert.AreEqual(0, firstPartition.StartOffset % sectorSize,
            "StartOffset should be sector-aligned.");
        Assert.AreEqual(0, firstPartition.Size % sectorSize,
            "Size should be sector-aligned.");

        mbr.Dispose();
    }

    [Test]
    public async Task Test_GPT_EnumeratePartitions_ReturnsCorrectCount()
    {
        var sectorSize = s_gptRawDisk!.SectorSize;

        using var stream = await s_gptRawDisk.ReadSectorsAsync(0, 34, CancellationToken.None);
        var diskBytes = new byte[sectorSize * 34];
        await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

        var gpt = new GPT(null);
        await gpt.ParseAsync(diskBytes, sectorSize, CancellationToken.None);

        var count = 0;
        await foreach (var partition in gpt.EnumeratePartitions(CancellationToken.None))
        {
            count++;
            Assert.IsNotNull(partition, "Partition should not be null.");
            Assert.Greater(partition.PartitionNumber, 0, "Partition number should be positive.");
        }

        Assert.AreEqual(2, count, "Should enumerate exactly 2 partitions.");

        gpt.Dispose();
    }

    [Test]
    public async Task Test_MBR_EnumeratePartitions_ReturnsCorrectCount()
    {
        var sectorSize = s_mbrRawDisk!.SectorSize;

        using var stream = await s_mbrRawDisk.ReadSectorsAsync(0, 1, CancellationToken.None);
        var mbrBytes = new byte[sectorSize];
        await stream.ReadAtLeastAsync(mbrBytes, mbrBytes.Length, cancellationToken: CancellationToken.None);

        var mbr = new MBR(null);
        await mbr.ParseAsync(mbrBytes, sectorSize, CancellationToken.None);

        var count = 0;
        await foreach (var partition in mbr.EnumeratePartitions(CancellationToken.None))
        {
            count++;
            Assert.IsNotNull(partition, "Partition should not be null.");
            Assert.Greater(partition.PartitionNumber, 0, "Partition number should be positive.");
        }

        Assert.AreEqual(2, count, "Should enumerate exactly 2 partitions.");

        mbr.Dispose();
    }

    [Test]
    public async Task Test_GPT_GetPartitionAsync_ReturnsCorrectPartition()
    {
        var sectorSize = s_gptRawDisk!.SectorSize;

        using var stream = await s_gptRawDisk.ReadSectorsAsync(0, 34, CancellationToken.None);
        var diskBytes = new byte[sectorSize * 34];
        await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

        var gpt = new GPT(null);
        await gpt.ParseAsync(diskBytes, sectorSize, CancellationToken.None);

        // Test GetPartitionAsync for partition 1 (should exist)
        var partition1 = await gpt.GetPartitionAsync(1, CancellationToken.None);
        Assert.IsNotNull(partition1, "Partition 1 should exist.");
        Assert.AreEqual(1, partition1!.PartitionNumber, "Partition number should be 1.");

        // Test GetPartitionAsync for partition 2 (should exist)
        var partition2 = await gpt.GetPartitionAsync(2, CancellationToken.None);
        Assert.IsNotNull(partition2, "Partition 2 should exist.");
        Assert.AreEqual(2, partition2!.PartitionNumber, "Partition number should be 2.");

        // Test GetPartitionAsync for partition 3 (should not exist)
        var partition3 = await gpt.GetPartitionAsync(3, CancellationToken.None);
        Assert.IsNull(partition3, "Partition 3 should not exist.");

        // Test GetPartitionAsync for invalid partition number 0
        var partition0 = await gpt.GetPartitionAsync(0, CancellationToken.None);
        Assert.IsNull(partition0, "Partition 0 should not exist (invalid number).");

        gpt.Dispose();
    }

    [Test]
    public async Task Test_MBR_GetPartitionAsync_ReturnsCorrectPartition()
    {
        var sectorSize = s_mbrRawDisk!.SectorSize;

        using var stream = await s_mbrRawDisk.ReadSectorsAsync(0, 1, CancellationToken.None);
        var mbrBytes = new byte[sectorSize];
        await stream.ReadAtLeastAsync(mbrBytes, mbrBytes.Length, cancellationToken: CancellationToken.None);

        var mbr = new MBR(null);
        await mbr.ParseAsync(mbrBytes, sectorSize, CancellationToken.None);

        // Test GetPartitionAsync for partition 1 (should exist)
        var partition1 = await mbr.GetPartitionAsync(1, CancellationToken.None);
        Assert.IsNotNull(partition1, "Partition 1 should exist.");
        Assert.AreEqual(1, partition1!.PartitionNumber, "Partition number should be 1.");

        // Test GetPartitionAsync for partition 2 (should exist)
        var partition2 = await mbr.GetPartitionAsync(2, CancellationToken.None);
        Assert.IsNotNull(partition2, "Partition 2 should exist.");
        Assert.AreEqual(2, partition2!.PartitionNumber, "Partition number should be 2.");

        // Test GetPartitionAsync for partition 3 (should not exist)
        var partition3 = await mbr.GetPartitionAsync(3, CancellationToken.None);
        Assert.IsNull(partition3, "Partition 3 should not exist.");

        // Test GetPartitionAsync for invalid partition number 0
        var partition0 = await mbr.GetPartitionAsync(0, CancellationToken.None);
        Assert.IsNull(partition0, "Partition 0 should not exist (invalid number).");

        mbr.Dispose();
    }

    [Test]
    public async Task Test_GPT_PartitionAlignment_IsSectorAligned()
    {
        var sectorSize = s_gptRawDisk!.SectorSize;

        using var stream = await s_gptRawDisk.ReadSectorsAsync(0, 34, CancellationToken.None);
        var diskBytes = new byte[sectorSize * 34];
        await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

        var gpt = new GPT(null);
        await gpt.ParseAsync(diskBytes, sectorSize, CancellationToken.None);

        // Verify all partitions are sector-aligned
        await foreach (var partition in gpt.EnumeratePartitions(CancellationToken.None))
        {
            Assert.AreEqual(0, partition.StartOffset % sectorSize,
                $"Partition {partition.PartitionNumber} StartOffset ({partition.StartOffset}) should be sector-aligned (sector size: {sectorSize}).");
            Assert.AreEqual(0, partition.Size % sectorSize,
                $"Partition {partition.PartitionNumber} Size ({partition.Size}) should be sector-aligned (sector size: {sectorSize}).");
        }

        gpt.Dispose();
    }

    [Test]
    public async Task Test_MBR_PartitionAlignment_IsSectorAligned()
    {
        var sectorSize = s_mbrRawDisk!.SectorSize;

        using var stream = await s_mbrRawDisk.ReadSectorsAsync(0, 1, CancellationToken.None);
        var mbrBytes = new byte[sectorSize];
        await stream.ReadAtLeastAsync(mbrBytes, mbrBytes.Length, cancellationToken: CancellationToken.None);

        var mbr = new MBR(null);
        await mbr.ParseAsync(mbrBytes, sectorSize, CancellationToken.None);

        // Verify all partitions are sector-aligned
        await foreach (var partition in mbr.EnumeratePartitions(CancellationToken.None))
        {
            Assert.AreEqual(0, partition.StartOffset % sectorSize,
                $"Partition {partition.PartitionNumber} StartOffset ({partition.StartOffset}) should be sector-aligned (sector size: {sectorSize}).");
            Assert.AreEqual(0, partition.Size % sectorSize,
                $"Partition {partition.PartitionNumber} Size ({partition.Size}) should be sector-aligned (sector size: {sectorSize}).");
        }

        mbr.Dispose();
    }

}