using System;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.General;
using NUnit.Framework;

namespace Duplicati.UnitTest.DiskImage.IntegrationTests;

public class FullRoundTripTests : DiskImageTests
{
    #region GPT Single Partition

    [Test, Category("DiskImage")]
    public Task Test_GPT_FAT32_Async() =>
        FullRoundTripAsync((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.FAT32, 0)]);

    [Test, Category("DiskImage")]
    public Task Test_GPT_NTFS_Async() =>
        FullRoundTripAsync((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.NTFS, 0)]);

    [Test, Category("DiskImage")]
    public Task Test_GPT_APFS_Async() =>
        FullRoundTripAsync((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.APFS, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_HFSPlus_Async() =>
        FullRoundTripAsync((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.HFSPlus, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_ExFAT_Async() =>
        FullRoundTripAsync((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.ExFAT, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_Ext2_Async() =>
        FullRoundTripAsync((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.Ext2, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_Ext3_Async() =>
        FullRoundTripAsync((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.Ext3, 0)]);

    [Test, Category("DiskImage")]
    public Task Test_GPT_Ext4_Async() =>
        FullRoundTripAsync((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.Ext4, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_XFS_Async() =>
        FullRoundTripAsync((int)(310 * MiB), PartitionTableType.GPT, [(FileSystemType.XFS, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_Btrfs_Async() =>
        FullRoundTripAsync((int)(110 * MiB), PartitionTableType.GPT, [(FileSystemType.Btrfs, 0)]);

    #endregion

    #region MBR Single Partition

    // APFS is only supported on GPT partition tables.

    [Test, Category("DiskImage")]
    public Task Test_MBR_FAT32_Async() =>
        FullRoundTripAsync((int)(50 * MiB), PartitionTableType.MBR, [(FileSystemType.FAT32, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_HFSPlus_Async() =>
        FullRoundTripAsync((int)(50 * MiB), PartitionTableType.MBR, [(FileSystemType.HFSPlus, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_ExFAT_Async() =>
        FullRoundTripAsync((int)(50 * MiB), PartitionTableType.MBR, [(FileSystemType.ExFAT, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_NTFS_Async() =>
        FullRoundTripAsync((int)(50 * MiB), PartitionTableType.MBR, [(FileSystemType.NTFS, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Ext2_Async() =>
        FullRoundTripAsync((int)(50 * MiB), PartitionTableType.MBR, [(FileSystemType.Ext2, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Ext3_Async() =>
        FullRoundTripAsync((int)(50 * MiB), PartitionTableType.MBR, [(FileSystemType.Ext3, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Ext4_Async() =>
        FullRoundTripAsync((int)(50 * MiB), PartitionTableType.MBR, [(FileSystemType.Ext4, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_XFS_Async() =>
        FullRoundTripAsync((int)(310 * MiB), PartitionTableType.MBR, [(FileSystemType.XFS, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Btrfs_Async() =>
        FullRoundTripAsync((int)(110 * MiB), PartitionTableType.MBR, [(FileSystemType.Btrfs, 0)]);

    #endregion

    #region Unknown Partition Table

    [Test, Category("DiskImage")]
    public Task Test_Unknown_NoPartitions_Async() =>
        FullRoundTripAsync((int)(50 * MiB), PartitionTableType.Unknown, []);

    #endregion

    #region GPT Two Partitions

    [Test, Category("DiskImage")]
    public Task Test_GPT_FAT32_FAT32_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.FAT32, 0)
        ]);

    [Test, Category("DiskImage")]
    public Task Test_GPT_APFS_APFS_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.APFS, 50 * MiB),
            (FileSystemType.APFS, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_HFSPlus_HFSPlus_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.HFSPlus, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_APFS_HFSPlus_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.APFS, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_FAT32_APFS_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.APFS, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_ExFAT_HFSPlus_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.ExFAT, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_FAT32_ExFAT_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.ExFAT, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_NTFS_FAT32_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.NTFS, 50 * MiB),
            (FileSystemType.FAT32, 0)
        ]);

    [Test, Category("DiskImage")]
    public Task Test_GPT_NTFS_NTFS_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.NTFS, 50 * MiB),
            (FileSystemType.NTFS, 0)
        ]);

    [Test, Category("DiskImage")]
    public Task Test_GPT_Ext4_Ext4_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.Ext4, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_Ext4_FAT32_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.FAT32, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_Ext4_XFS_Async() =>
        FullRoundTripAsync((int)(360 * MiB), PartitionTableType.GPT, [
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.XFS, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_XFS_FAT32_Async() =>
        FullRoundTripAsync((int)(360 * MiB), PartitionTableType.GPT, [
            (FileSystemType.XFS, 310 * MiB),
            (FileSystemType.FAT32, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_Btrfs_FAT32_Async() =>
        FullRoundTripAsync((int)(160 * MiB), PartitionTableType.GPT, [
            (FileSystemType.Btrfs, 110 * MiB),
            (FileSystemType.FAT32, 0)
        ]);

    #endregion

    #region MBR Two Partitions

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_FAT32_FAT32_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.MBR, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.FAT32, 0)
        ]);

    // APFS is only supported on GPT partition tables.

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_HFSPlus_HFSPlus_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.MBR, [
            (FileSystemType.HFSPlus, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_FAT32_ExFAT_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.MBR, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.ExFAT, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_FAT32_HFSPlus_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.MBR, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_NTFS_FAT32_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.MBR, [
            (FileSystemType.NTFS, 50 * MiB),
            (FileSystemType.FAT32, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Ext4_Ext4_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.MBR, [
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.Ext4, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_FAT32_Ext4_Async() =>
        FullRoundTripAsync((int)(100 * MiB), PartitionTableType.MBR, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.Ext4, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_XFS_Ext4_Async() =>
        FullRoundTripAsync((int)(360 * MiB), PartitionTableType.MBR, [
            (FileSystemType.XFS, 310 * MiB),
            (FileSystemType.Ext4, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Btrfs_Ext4_Async() =>
        FullRoundTripAsync((int)(160 * MiB), PartitionTableType.MBR, [
            (FileSystemType.Btrfs, 110 * MiB),
            (FileSystemType.Ext4, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_XFS_Btrfs_Async() =>
        FullRoundTripAsync((int)(420 * MiB), PartitionTableType.MBR, [
            (FileSystemType.XFS, 310 * MiB),
            (FileSystemType.Btrfs, 0)
        ]);

    #endregion

    #region GPT Three Partitions

    [Test, Category("DiskImage")]
    public Task Test_GPT_FAT32_FAT32_FAT32_Async() =>
        FullRoundTripAsync((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.FAT32, 0)
        ]);

    [Test, Category("DiskImage")]
    public Task Test_GPT_APFS_APFS_APFS_Async() =>
        FullRoundTripAsync((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.APFS, 50 * MiB),
            (FileSystemType.APFS, 50 * MiB),
            (FileSystemType.APFS, 0)
        ]);

    [Test, Category("DiskImage")]
    public Task Test_GPT_NTFS_NTFS_NTFS_Async() =>
        FullRoundTripAsync((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.NTFS, 50 * MiB),
            (FileSystemType.NTFS, 50 * MiB),
            (FileSystemType.NTFS, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_HFSPlus_HFSPlus_HFSPlus_Async() =>
        FullRoundTripAsync((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.HFSPlus, 50 * MiB),
            (FileSystemType.HFSPlus, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_APFS_HFSPlus_APFS_Async() =>
        FullRoundTripAsync((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.APFS, 50 * MiB),
            (FileSystemType.HFSPlus, 50 * MiB),
            (FileSystemType.APFS, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_FAT32_APFS_HFSPlus_Async() =>
        FullRoundTripAsync((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.APFS, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_ExFAT_APFS_HFSPlus_Async() =>
        FullRoundTripAsync((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.ExFAT, 50 * MiB),
            (FileSystemType.APFS, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);

    [Test, Category("DiskImage")]
    public Task Test_GPT_Ext4_Ext4_Ext4_Async() =>
        FullRoundTripAsync((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.Ext4, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_FAT32_Ext4_XFS_Async() =>
        FullRoundTripAsync((int)(410 * MiB), PartitionTableType.GPT, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.XFS, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_Ext4_XFS_Btrfs_Async() =>
        FullRoundTripAsync((int)(470 * MiB), PartitionTableType.GPT, [
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.XFS, 310 * MiB),
            (FileSystemType.Btrfs, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_XFS_Ext4_Ext4_Async() =>
        FullRoundTripAsync((int)(410 * MiB), PartitionTableType.GPT, [
            (FileSystemType.XFS, 310 * MiB),
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.Ext4, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_Btrfs_Ext4_FAT32_Async() =>
        FullRoundTripAsync((int)(210 * MiB), PartitionTableType.GPT, [
            (FileSystemType.Btrfs, 110 * MiB),
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.FAT32, 0)
        ]);

    #endregion

    #region MBR Three Partitions

    // APFS is only supported on GPT partition tables.

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_FAT32_FAT32_FAT32_Async() =>
        FullRoundTripAsync((int)(150 * MiB), PartitionTableType.MBR, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.FAT32, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_HFSPlus_HFSPlus_HFSPlus_Async() =>
        FullRoundTripAsync((int)(150 * MiB), PartitionTableType.MBR, [
            (FileSystemType.HFSPlus, 50 * MiB),
            (FileSystemType.HFSPlus, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_FAT32_ExFAT_HFSPlus_Async() =>
        FullRoundTripAsync((int)(150 * MiB), PartitionTableType.MBR, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.ExFAT, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);

    // GPT NTFS for Windows - local-only (tested via single partition)
    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_NTFS_FAT32_ExFAT_Async() =>
        FullRoundTripAsync((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.NTFS, 50 * MiB),
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.ExFAT, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Ext4_Ext4_Ext4_Async() =>
        FullRoundTripAsync((int)(150 * MiB), PartitionTableType.MBR, [
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.Ext4, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_FAT32_Ext4_XFS_Async() =>
        FullRoundTripAsync((int)(410 * MiB), PartitionTableType.MBR, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.XFS, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Ext4_XFS_Btrfs_Async() =>
        FullRoundTripAsync((int)(470 * MiB), PartitionTableType.MBR, [
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.XFS, 310 * MiB),
            (FileSystemType.Btrfs, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_XFS_Btrfs_Ext4_Async() =>
        FullRoundTripAsync((int)(470 * MiB), PartitionTableType.MBR, [
            (FileSystemType.XFS, 310 * MiB),
            (FileSystemType.Btrfs, 110 * MiB),
            (FileSystemType.Ext4, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Btrfs_Ext4_XFS_Async() =>
        FullRoundTripAsync((int)(470 * MiB), PartitionTableType.MBR, [
            (FileSystemType.Btrfs, 110 * MiB),
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.XFS, 0)
        ]);

    #endregion

    public async Task FullRoundTripAsync(int size, PartitionTableType tableType, (FileSystemType, long)[] partitions)
    {
        // Check if all specified file system types are supported before starting the test
        foreach (var (fsType, partSize) in partitions)
        {
            var (isSupported, reason) = _diskHelper.IsFileSystemTypeSupported(fsType);
            if (!isSupported)
                Assert.Ignore(reason);
        }

        await TestContext.Progress.WriteLineAsync("Test: Full Round-Trip Backup + Restore");

        var sourceDrivePath = _diskHelper.CreateDisk(_sourceImagePath, size);
        await TestContext.Progress.WriteLineAsync($"Source Disk created at: {_sourceImagePath}");

        var sourcePartitions = _diskHelper.InitializeDisk(sourceDrivePath, tableType, partitions);
        await TestContext.Progress.WriteLineAsync($"Source Disk initialized with partition(s): {string.Join(", ", sourcePartitions)}");

        // Populate source partition with test data
        foreach (var partition in sourcePartitions)
            await ToolTests.GenerateTestDataAsync(partition, 10, 5, 2, 1024);
        _diskHelper.FlushDisk(sourceDrivePath);
        _diskHelper.Unmount(sourceDrivePath);
        await TestContext.Progress.WriteLineAsync($"Test data generated on source partition(s)");

        // Backup
        var backupResults = await RunBackupAsync(sourceDrivePath);
        TestUtils.AssertResults(backupResults);
        await TestContext.Progress.WriteLineAsync($"Backup completed successfully");

        // Setup restore target disk image with same geometry
        var restoreDrivePath = _diskHelper.CreateDisk(_restoreImagePath, size);
        _diskHelper.InitializeDisk(restoreDrivePath, PartitionTableType.GPT, []);
        _diskHelper.Unmount(restoreDrivePath);
        await TestContext.Progress.WriteLineAsync($"Restore disk image created at: {_restoreImagePath}");

        // Restore
        var restoreResults = await RunRestoreAsync(restoreDrivePath);
        TestUtils.AssertResults(restoreResults);
        await TestContext.Progress.WriteLineAsync($"Restore completed successfully");

        // Reattach drives in readonly
        sourceDrivePath = _diskHelper.ReAttach(_sourceImagePath, sourceDrivePath, tableType, readOnly: true);
        restoreDrivePath = _diskHelper.ReAttach(_restoreImagePath, restoreDrivePath, tableType, readOnly: true);
        await TestContext.Progress.WriteLineAsync($"Source and restore disks re-attached as read-only for verification");

        // Verify partition table matches. Mount before verification, to make disks online on Windows.
        string[] restorePartitions = [];
        if (tableType != PartitionTableType.Unknown)
        {
            var fsTypes = partitions.Select(p => p.Item1).ToArray();
            sourcePartitions = _diskHelper.Mount(sourceDrivePath, _sourceMountPath, readOnly: true, fileSystemTypes: fsTypes);
            restorePartitions = _diskHelper.Mount(restoreDrivePath, _restoreMountPath, readOnly: true, fileSystemTypes: fsTypes);
        }
        VerifyPartitionTableMatches(sourceDrivePath, restoreDrivePath);
        await TestContext.Progress.WriteLineAsync($"Partition table verified to match source");

        // Verify data matches byte-for-byte
        foreach (var (sourcePartition, restorePartition) in sourcePartitions.Zip(restorePartitions, (s, r) => (s, r)))
            CompareDirectories(sourcePartition, restorePartition);

        await TestContext.Progress.WriteLineAsync($"Restored data verified to match source");
    }
}