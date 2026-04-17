using System;
using System.IO;
using System.Threading;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.General;
using Duplicati.UnitTest.DiskImage.Helpers;
using NUnit.Framework;

using Assert = NUnit.Framework.Legacy.ClassicAssert;
namespace Duplicati.UnitTest.DiskImage.UnitTests;

#nullable enable

// This class holds all of the general setup and teardown code for the DiskImage unit tests, including creating and initializing the test disk images used by multiple tests. This ensures that all tests have a consistent environment and reduces code duplication across test classes.

[TestFixture]
[Category("DiskImageUnit")]
[Platform("Win,MacOsX,Linux")]
public partial class DiskImageUnitTests : BasicSetupHelper
{
    // Class-level read-only disks (set up once for entire test class)
    private static IDiskImageHelper? s_diskHelper;

    // GPT disk with 2 FAT32 partitions
    private static string s_gptDiskPath = "";
    private static string s_gptDiskIdentifier = "";
    private static IRawDisk? s_gptRawDisk;
    private static long s_gptPartition1Offset;
    private static long s_gptPartition1Size;
    private static long s_gptPartition2Offset;
    private static long s_gptPartition2Size;

    // MBR disk with 2 FAT32 partitions
    private static string s_mbrDiskPath = "";
    private static string s_mbrDiskIdentifier = "";
    private static IRawDisk? s_mbrRawDisk;
    private static long s_mbrPartition1Offset;
    private static long s_mbrPartition1Size;
    private static long s_mbrPartition2Offset;
    private static long s_mbrPartition2Size;

    // Writable disk for tests that need to write (re-initialized before each test)
    private static string s_writableDiskPath = "";
    private static string s_writableDiskIdentifier = "";
    private static IRawDisk? s_writableRawDisk;

    // Per-test instance members
    private IRawDisk _writableRawDisk = null!;

    private const long MiB = 1024 * 1024;

    /// <summary>
    /// One-time setup for the entire test class.
    /// Creates read-only GPT and MBR disks with 2 FAT32 partitions each.
    /// </summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        base.BasicHelperSetUp();

        // Create the appropriate disk image helper for the current platform
        s_diskHelper = DiskImageHelperFactory.Create();

        // Check for admin privileges
        if (!s_diskHelper.HasRequiredPrivileges())
        {
            Assert.Ignore("DiskImage tests require administrator privileges");
        }

        var extension = DiskImageTestHelpers.GetPlatformDiskImageExtension();

        // Create GPT disk with 2 FAT32 partitions
        s_gptDiskPath = Path.Combine(BASEFOLDER, $"duplicati_gpt_class_test.{extension}");
        s_gptDiskIdentifier = DiskImageTestHelpers.CreateDiskWithPartitions(
            s_diskHelper, s_gptDiskPath, 100 * MiB, PartitionTableType.GPT, [(FileSystemType.FAT32, 50 * MiB), (FileSystemType.FAT32, 0)]);

        // Get partition info from GPT disk
        var gptPartitions = s_diskHelper.GetPartitions(s_gptDiskIdentifier);
        if (gptPartitions.Length >= 2)
        {
            s_gptPartition1Offset = gptPartitions[0].StartOffset;
            s_gptPartition1Size = gptPartitions[0].Size;
            s_gptPartition2Offset = gptPartitions[1].StartOffset;
            s_gptPartition2Size = gptPartitions[1].Size;
        }

        // Create and initialize raw disk for GPT (read-only)
        s_gptRawDisk = DiskImageTestHelpers.CreateRawDiskForIdentifier(s_gptDiskIdentifier);
        if (!s_gptRawDisk.InitializeAsync(true, CancellationToken.None).Result)
            throw new InvalidOperationException("Failed to initialize GPT raw disk");

        // Fill GPT partitions with well-known test data
        DiskImageTestHelpers.FillPartitionWithTestDataAsync(s_gptRawDisk, s_gptPartition1Offset, s_gptPartition1Size, CancellationToken.None).Wait();
        DiskImageTestHelpers.FillPartitionWithTestDataAsync(s_gptRawDisk, s_gptPartition2Offset, s_gptPartition2Size, CancellationToken.None).Wait();
        s_diskHelper.FlushDisk(s_gptDiskIdentifier);

        // Create MBR disk with 2 FAT32 partitions
        s_mbrDiskPath = Path.Combine(BASEFOLDER, $"duplicati_mbr_class_test.{extension}");
        s_mbrDiskIdentifier = DiskImageTestHelpers.CreateDiskWithPartitions(
            s_diskHelper, s_mbrDiskPath, 100 * MiB, PartitionTableType.MBR, [(FileSystemType.FAT32, 50 * MiB), (FileSystemType.FAT32, 0)]);

        // Get partition info from MBR disk
        var mbrPartitions = s_diskHelper.GetPartitions(s_mbrDiskIdentifier);
        if (mbrPartitions.Length >= 2)
        {
            s_mbrPartition1Offset = mbrPartitions[0].StartOffset;
            s_mbrPartition1Size = mbrPartitions[0].Size;
            s_mbrPartition2Offset = mbrPartitions[1].StartOffset;
            s_mbrPartition2Size = mbrPartitions[1].Size;
        }

        // Create and initialize raw disk for MBR (read-only)
        s_mbrRawDisk = DiskImageTestHelpers.CreateRawDiskForIdentifier(s_mbrDiskIdentifier);
        if (!s_mbrRawDisk.InitializeAsync(true, CancellationToken.None).Result)
            throw new InvalidOperationException("Failed to initialize MBR raw disk");

        // Fill MBR partitions with well-known test data
        DiskImageTestHelpers.FillPartitionWithTestDataAsync(s_mbrRawDisk, s_mbrPartition1Offset, s_mbrPartition1Size, CancellationToken.None).Wait();
        DiskImageTestHelpers.FillPartitionWithTestDataAsync(s_mbrRawDisk, s_mbrPartition2Offset, s_mbrPartition2Size, CancellationToken.None).Wait();
        s_diskHelper.FlushDisk(s_mbrDiskIdentifier);

        // Create writable disk path (will be created per-test)
        s_writableDiskPath = Path.Combine(BASEFOLDER, $"duplicati_writable_class_test.{extension}");

        // Create new writable disk (100 MiB, uninitialized)
        s_writableDiskIdentifier = s_diskHelper.CreateDisk(s_writableDiskPath, 100 * MiB);
        s_diskHelper.Unmount(s_writableDiskIdentifier);

        // Create raw disk interface (with write access)
        s_writableRawDisk = DiskImageTestHelpers.CreateRawDiskForIdentifier(s_writableDiskIdentifier);
        if (!s_writableRawDisk.InitializeAsync(true, CancellationToken.None).Result)
            throw new InvalidOperationException("Failed to initialize writable raw disk");
    }

    /// <summary>
    /// One-time teardown for the entire test class.
    /// Cleans up all class-level disk resources.
    /// </summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        // Dispose read-only raw disks
        s_gptRawDisk?.Dispose();
        s_mbrRawDisk?.Dispose();
        s_writableRawDisk?.Dispose();

        // Cleanup class-level disks - this properly detaches and deletes the disk images
        // Using CleanupDisk ensures the disk is properly detached before the file is deleted
        if (s_diskHelper != null)
        {
            s_diskHelper.CleanupDisk(s_gptDiskPath, s_gptDiskIdentifier);
            s_diskHelper.CleanupDisk(s_mbrDiskPath, s_mbrDiskIdentifier);
            s_diskHelper.CleanupDisk(s_writableDiskPath, s_writableDiskIdentifier);
        }

        s_diskHelper?.Dispose();
    }

    /// <summary>
    /// Sets up the test environment before each test that needs a writable disk.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        if (s_writableRawDisk != null)
        {
            _writableRawDisk = s_writableRawDisk;
        }
    }

    /// <summary>
    /// Cleans up after each test.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        // No longer unmounting per-test to improve performance
    }

}