// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Proprietary.DiskImage;
using Duplicati.Proprietary.DiskImage.General;
using Duplicati.UnitTest.DiskImage.Helpers;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest.DiskImage;

/// <summary>
/// Unit tests for the DiskImage backup and restore functionality.
/// These tests use disk image files managed to test the full backup and restore flow.
/// </summary>
[TestFixture]
[Category("DiskImage")]
[Platform("Win,MacOsX,Linux")]
public class DiskImageTests : BasicSetupHelper
{
    private string _sourceImagePath = null!;
    private string _restoreImagePath = null!;
    private string _sourceMountPath = null!;
    private string _restoreMountPath = null!;
    private IDiskImageHelper _diskHelper = null!;

    private const long MiB = 1024 * 1024;

    /// <summary>
    /// Sets up the test environment before each test.
    /// Creates the disk helper and temporary disk image paths.
    /// </summary>
    [SetUp]
    public void DiskImageSetUp()
    {
        // Create the appropriate disk image helper for the current platform
        _diskHelper = DiskImageHelperFactory.Create();

        // Check for admin privileges
        if (!_diskHelper.HasRequiredPrivileges())
        {
            Assert.Ignore("DiskImage tests require administrator privileges");
        }

        // Create temp disk image paths
        var extension = DiskImageTestHelpers.GetPlatformDiskImageExtension();
        _sourceImagePath = Path.Combine(DATAFOLDER, $"duplicati_test_source_{Guid.NewGuid()}.{extension}");
        _restoreImagePath = Path.Combine(DATAFOLDER, $"duplicati_test_restore_{Guid.NewGuid()}.{extension}");
        _sourceMountPath = Path.Combine(DATAFOLDER, $"mnt_source_{Guid.NewGuid()}");
        _restoreMountPath = Path.Combine(DATAFOLDER, $"mnt_restore_{Guid.NewGuid()}");
        Directory.CreateDirectory(_sourceMountPath);
        Directory.CreateDirectory(_restoreMountPath);
    }

    /// <summary>
    /// Cleans up the test environment after each test.
    /// Detaches and deletes disk image files.
    /// </summary>
    [TearDown]
    public void DiskImageTearDown()
    {
        // Cleanup disk images
        try
        {
            _diskHelper.CleanupDisk(_sourceImagePath);
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Warning: Failed to cleanup source disk image: {ex.Message}");
        }

        try
        {
            _diskHelper.CleanupDisk(_restoreImagePath);
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Warning: Failed to cleanup restore disk image: {ex.Message}");
        }

        try
        {
            _diskHelper.Dispose();
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Warning: Failed to dispose disk helper: {ex.Message}");
        }

        DiskImageTestHelpers.SafeDeleteFile(_sourceImagePath);
        DiskImageTestHelpers.SafeDeleteFile(_restoreImagePath);
    }

    #region GPT Single Partition

    [Test, Category("DiskImage")]
    public Task Test_GPT_FAT32() =>
        FullRoundTrip((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.FAT32, 0)]);

    [Test, Category("DiskImage")]
    public Task Test_GPT_NTFS()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("Test_GPT_NTFS is only supported on Windows.");
        return FullRoundTrip((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.NTFS, 0)]);
    }

    [Test, Category("DiskImage")]
    public Task Test_GPT_APFS()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("APFS is only supported on macOS.");
        return FullRoundTrip((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.APFS, 0)]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_HFSPlus()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("HFSPlus is only supported on macOS.");
        return FullRoundTrip((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.HFSPlus, 0)]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_ExFAT() =>
        FullRoundTrip((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.ExFAT, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_Ext2()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Ext2 is only supported on Linux.");
        return FullRoundTrip((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.Ext2, 0)]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_Ext3()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Ext3 is only supported on Linux.");
        return FullRoundTrip((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.Ext3, 0)]);
    }

    [Test, Category("DiskImage")]
    public Task Test_GPT_Ext4()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Ext4 is only supported on Linux.");
        return FullRoundTrip((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.Ext4, 0)]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_XFS()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("XFS is only supported on Linux.");
        return FullRoundTrip((int)(310 * MiB), PartitionTableType.GPT, [(FileSystemType.XFS, 0)]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_Btrfs()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Btrfs is only supported on Linux.");
        return FullRoundTrip((int)(110 * MiB), PartitionTableType.GPT, [(FileSystemType.Btrfs, 0)]);
    }

    #endregion

    #region MBR Single Partition

    // APFS is only supported on GPT partition tables.

    [Test, Category("DiskImage")]
    public Task Test_MBR_FAT32() =>
        FullRoundTrip((int)(50 * MiB), PartitionTableType.MBR, [(FileSystemType.FAT32, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_HFSPlus()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("HFSPlus is only supported on macOS.");
        return FullRoundTrip((int)(50 * MiB), PartitionTableType.MBR, [(FileSystemType.HFSPlus, 0)]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_ExFAT() =>
        FullRoundTrip((int)(50 * MiB), PartitionTableType.MBR, [(FileSystemType.ExFAT, 0)]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_NTFS()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("Test_MBR_NTFS is only supported on Windows.");
        return FullRoundTrip((int)(50 * MiB), PartitionTableType.MBR, [(FileSystemType.NTFS, 0)]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Ext2()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Ext2 is only supported on Linux.");
        return FullRoundTrip((int)(50 * MiB), PartitionTableType.MBR, [(FileSystemType.Ext2, 0)]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Ext3()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Ext3 is only supported on Linux.");
        return FullRoundTrip((int)(50 * MiB), PartitionTableType.MBR, [(FileSystemType.Ext3, 0)]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Ext4()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Ext4 is only supported on Linux.");
        return FullRoundTrip((int)(50 * MiB), PartitionTableType.MBR, [(FileSystemType.Ext4, 0)]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_XFS()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("XFS is only supported on Linux.");
        return FullRoundTrip((int)(310 * MiB), PartitionTableType.MBR, [(FileSystemType.XFS, 0)]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Btrfs()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Btrfs is only supported on Linux.");
        return FullRoundTrip((int)(110 * MiB), PartitionTableType.MBR, [(FileSystemType.Btrfs, 0)]);
    }

    #endregion

    #region Unknown Partition Table

    [Test, Category("DiskImage")]
    public Task Test_Unknown_NoPartitions() =>
        FullRoundTrip((int)(50 * MiB), PartitionTableType.Unknown, []);

    #endregion

    #region GPT Two Partitions

    [Test, Category("DiskImage")]
    public Task Test_GPT_FAT32_FAT32() =>
        FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.FAT32, 0)
        ]);

    [Test, Category("DiskImage")]
    public Task Test_GPT_APFS_APFS()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("APFS is only supported on macOS.");
        return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.APFS, 50 * MiB),
            (FileSystemType.APFS, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_HFSPlus_HFSPlus()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("HFSPlus is only supported on macOS.");
        return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.HFSPlus, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_APFS_HFSPlus()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("APFS and HFSPlus are only supported on macOS.");
        return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.APFS, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_FAT32_APFS()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("APFS is only supported on macOS.");
        return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.APFS, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_ExFAT_HFSPlus()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("HFSPlus is only supported on macOS.");
        return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.ExFAT, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_FAT32_ExFAT() =>
        FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.ExFAT, 0)
        ]);

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_NTFS_FAT32()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("This test is only supported on Windows.");
        return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.NTFS, 50 * MiB),
            (FileSystemType.FAT32, 0)
        ]);
    }

    [Test, Category("DiskImage")]
    public Task Test_GPT_NTFS_NTFS()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("This test is only supported on Windows.");
        return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.NTFS, 50 * MiB),
            (FileSystemType.NTFS, 0)
        ]);
    }

    [Test, Category("DiskImage")]
    public Task Test_GPT_Ext4_Ext4()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Ext4 is only supported on Linux.");
        return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.Ext4, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_Ext4_FAT32()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("This test is only supported on Linux.");
        return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.FAT32, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_Ext4_XFS()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("This test is only supported on Linux.");
        return FullRoundTrip((int)(360 * MiB), PartitionTableType.GPT, [
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.XFS, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_XFS_FAT32()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("This test is only supported on Linux.");
        return FullRoundTrip((int)(360 * MiB), PartitionTableType.GPT, [
            (FileSystemType.XFS, 310 * MiB),
            (FileSystemType.FAT32, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_Btrfs_FAT32()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("This test is only supported on Linux.");
        return FullRoundTrip((int)(160 * MiB), PartitionTableType.GPT, [
            (FileSystemType.Btrfs, 110 * MiB),
            (FileSystemType.FAT32, 0)
        ]);
    }

    #endregion

    #region MBR Two Partitions

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_FAT32_FAT32() =>
        FullRoundTrip((int)(100 * MiB), PartitionTableType.MBR, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.FAT32, 0)
        ]);

    // APFS is only supported on GPT partition tables.

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_HFSPlus_HFSPlus()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("HFSPlus is only supported on macOS.");
        return FullRoundTrip((int)(100 * MiB), PartitionTableType.MBR, [
            (FileSystemType.HFSPlus, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);
    }


    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_FAT32_ExFAT()
    {
        return FullRoundTrip((int)(100 * MiB), PartitionTableType.MBR, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.ExFAT, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_FAT32_HFSPlus()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("HFSPlus is only supported on macOS.");
        return FullRoundTrip((int)(100 * MiB), PartitionTableType.MBR, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_NTFS_FAT32()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("This test is only supported on Windows.");
        return FullRoundTrip((int)(100 * MiB), PartitionTableType.MBR, [
            (FileSystemType.NTFS, 50 * MiB),
            (FileSystemType.FAT32, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Ext4_Ext4()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Ext4 is only supported on Linux.");
        return FullRoundTrip((int)(100 * MiB), PartitionTableType.MBR, [
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.Ext4, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_FAT32_Ext4()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("This test is only supported on Linux.");
        return FullRoundTrip((int)(100 * MiB), PartitionTableType.MBR, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.Ext4, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_XFS_Ext4()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("This test is only supported on Linux.");
        return FullRoundTrip((int)(360 * MiB), PartitionTableType.MBR, [
            (FileSystemType.XFS, 310 * MiB),
            (FileSystemType.Ext4, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Btrfs_Ext4()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("This test is only supported on Linux.");
        return FullRoundTrip((int)(160 * MiB), PartitionTableType.MBR, [
            (FileSystemType.Btrfs, 110 * MiB),
            (FileSystemType.Ext4, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_XFS_Btrfs()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("This test is only supported on Linux.");
        return FullRoundTrip((int)(420 * MiB), PartitionTableType.MBR, [
            (FileSystemType.XFS, 310 * MiB),
            (FileSystemType.Btrfs, 0)
        ]);
    }

    #endregion

    #region GPT Three Partitions

    [Test, Category("DiskImage")]
    public Task Test_GPT_FAT32_FAT32_FAT32()
    {
        return FullRoundTrip((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.FAT32, 0)
        ]);
    }

    [Test, Category("DiskImage")]
    public Task Test_GPT_APFS_APFS_APFS()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("APFS is only supported on macOS.");
        return FullRoundTrip((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.APFS, 50 * MiB),
            (FileSystemType.APFS, 50 * MiB),
            (FileSystemType.APFS, 0)
        ]);
    }

    [Test, Category("DiskImage")]
    public Task Test_GPT_NTFS_NTFS_NTFS()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("NTFS is only supported on Windows.");
        return FullRoundTrip((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.NTFS, 50 * MiB),
            (FileSystemType.NTFS, 50 * MiB),
            (FileSystemType.NTFS, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_HFSPlus_HFSPlus_HFSPlus()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("HFSPlus is only supported on macOS.");
        return FullRoundTrip((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.HFSPlus, 50 * MiB),
            (FileSystemType.HFSPlus, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_APFS_HFSPlus_APFS()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("APFS and HFSPlus are only supported on macOS.");
        return FullRoundTrip((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.APFS, 50 * MiB),
            (FileSystemType.HFSPlus, 50 * MiB),
            (FileSystemType.APFS, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_FAT32_APFS_HFSPlus()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("APFS and HFSPlus are only supported on macOS.");
        return FullRoundTrip((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.APFS, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_ExFAT_APFS_HFSPlus()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("APFS and HFSPlus are only supported on macOS.");
        return FullRoundTrip((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.ExFAT, 50 * MiB),
            (FileSystemType.APFS, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);
    }

    [Test, Category("DiskImage")]
    public Task Test_GPT_Ext4_Ext4_Ext4()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Ext4 is only supported on Linux.");
        return FullRoundTrip((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.Ext4, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_FAT32_Ext4_XFS()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("This test is only supported on Linux.");
        return FullRoundTrip((int)(410 * MiB), PartitionTableType.GPT, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.XFS, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_Ext4_XFS_Btrfs()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("This test is only supported on Linux.");
        return FullRoundTrip((int)(470 * MiB), PartitionTableType.GPT, [
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.XFS, 310 * MiB),
            (FileSystemType.Btrfs, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_XFS_Ext4_Ext4()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("This test is only supported on Linux.");
        return FullRoundTrip((int)(410 * MiB), PartitionTableType.GPT, [
            (FileSystemType.XFS, 310 * MiB),
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.Ext4, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_Btrfs_Ext4_FAT32()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("This test is only supported on Linux.");
        return FullRoundTrip((int)(210 * MiB), PartitionTableType.GPT, [
            (FileSystemType.Btrfs, 110 * MiB),
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.FAT32, 0)
        ]);
    }

    #endregion

    #region MBR Three Partitions

    // APFS is only supported on GPT partition tables.

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_FAT32_FAT32_FAT32()
    {
        return FullRoundTrip((int)(150 * MiB), PartitionTableType.MBR, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.FAT32, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_HFSPlus_HFSPlus_HFSPlus()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("HFSPlus is only supported on macOS.");
        return FullRoundTrip((int)(150 * MiB), PartitionTableType.MBR, [
            (FileSystemType.HFSPlus, 50 * MiB),
            (FileSystemType.HFSPlus, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_FAT32_ExFAT_HFSPlus()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("HFSPlus is only supported on macOS.");
        return FullRoundTrip((int)(150 * MiB), PartitionTableType.MBR, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.ExFAT, 50 * MiB),
            (FileSystemType.HFSPlus, 0)
        ]);
    }

    // GPT NTFS for Windows - local-only (tested via single partition)
    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_GPT_NTFS_FAT32_ExFAT()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("This test is only supported on Windows.");
        return FullRoundTrip((int)(150 * MiB), PartitionTableType.GPT, [
            (FileSystemType.NTFS, 50 * MiB),
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.ExFAT, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Ext4_Ext4_Ext4()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Ext4 is only supported on Linux.");
        return FullRoundTrip((int)(150 * MiB), PartitionTableType.MBR, [
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.Ext4, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_FAT32_Ext4_XFS()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("This test is only supported on Linux.");
        return FullRoundTrip((int)(410 * MiB), PartitionTableType.MBR, [
            (FileSystemType.FAT32, 50 * MiB),
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.XFS, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Ext4_XFS_Btrfs()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("This test is only supported on Linux.");
        return FullRoundTrip((int)(470 * MiB), PartitionTableType.MBR, [
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.XFS, 310 * MiB),
            (FileSystemType.Btrfs, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_XFS_Btrfs_Ext4()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("This test is only supported on Linux.");
        return FullRoundTrip((int)(470 * MiB), PartitionTableType.MBR, [
            (FileSystemType.XFS, 310 * MiB),
            (FileSystemType.Btrfs, 110 * MiB),
            (FileSystemType.Ext4, 0)
        ]);
    }

    [Test, Category("DiskImage"), Category("DiskImageLocal")]
    public Task Test_MBR_Btrfs_Ext4_XFS()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("This test is only supported on Linux.");
        return FullRoundTrip((int)(470 * MiB), PartitionTableType.MBR, [
            (FileSystemType.Btrfs, 110 * MiB),
            (FileSystemType.Ext4, 50 * MiB),
            (FileSystemType.XFS, 0)
        ]);
    }

    #endregion

    public async Task FullRoundTrip(int size, PartitionTableType tableType, (FileSystemType, long)[] partitions)
    {
        await TestContext.Progress.WriteLineAsync("Test: Full Round-Trip Backup + Restore");

        var sourceDrivePath = _diskHelper.CreateDisk(_sourceImagePath, size);
        await TestContext.Progress.WriteLineAsync($"Source Disk created at: {_sourceImagePath}");

        var sourcePartitions = _diskHelper.InitializeDisk(sourceDrivePath, tableType, partitions);
        await TestContext.Progress.WriteLineAsync($"Source Disk initialized with partition(s): {string.Join(", ", sourcePartitions)}");

        // Populate source partition with test data
        foreach (var partition in sourcePartitions)
            await ToolTests.GenerateTestData(partition, 10, 5, 2, 1024);
        _diskHelper.FlushDisk(sourceDrivePath);
        _diskHelper.Unmount(sourceDrivePath);
        await TestContext.Progress.WriteLineAsync($"Test data generated on source partition(s)");

        // Backup
        var backupResults = RunBackup(sourceDrivePath);
        TestUtils.AssertResults(backupResults);
        await TestContext.Progress.WriteLineAsync($"Backup completed successfully");

        // Setup restore target disk image with same geometry
        var restoreDrivePath = _diskHelper.CreateDisk(_restoreImagePath, size);
        _diskHelper.InitializeDisk(restoreDrivePath, PartitionTableType.GPT, []);
        _diskHelper.Unmount(restoreDrivePath);
        await TestContext.Progress.WriteLineAsync($"Restore disk image created at: {_restoreImagePath}");

        // Restore
        var restoreResults = RunRestore(restoreDrivePath);
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

    [Test]
    [Category("DiskImage")]
    public async Task Test_GeometryMetadata_Verification()
    {
        await TestContext.Progress.WriteLineAsync("Test: Full Round-Trip Backup + Restore");

        var sourceDrivePath = _diskHelper.CreateDisk(_sourceImagePath, 100 * MiB);
        await TestContext.Progress.WriteLineAsync($"Source Disk created at: {_sourceImagePath}");

        (FileSystemType, long)[] partitions = [(FileSystemType.FAT32, 50 * MiB), (FileSystemType.FAT32, 0)];
        var sourcePartitions = _diskHelper.InitializeDisk(sourceDrivePath, PartitionTableType.GPT, partitions);
        _diskHelper.Unmount(sourceDrivePath);
        await TestContext.Progress.WriteLineAsync($"Source Disk initialized with partition(s): {string.Join(", ", sourcePartitions)}");

        // Backup
        var backupResults = RunBackup(sourceDrivePath);
        TestUtils.AssertResults(backupResults);

        // List backup contents and verify geometry.json is present
        using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
        {
            var listResults = c.List("*");
            TestUtils.AssertResults(listResults);

            // Check that geometry.json is in the backup
            var files = listResults.Files;
            Assert.That(files, Is.Not.Null);
            var geometryFile = files.FirstOrDefault(f => f.Path.EndsWith("geometry.json"));
            Assert.That(geometryFile, Is.Not.Null, "geometry.json should be present in the backup");
        }

        // Verify geometry metadata contains correct information
        VerifyGeometryMetadata(TARGETFOLDER, TestOptions, partitions);
    }

    [Test]
    [Category("DiskImage")]
    public async Task Test_SourceProvider_Enumeration()
    {
        TestContext.Progress.WriteLine("Test: SourceProvider Enumeration");

        // Setup: Create GPT disk with one FAT32 partition
        var sourceDrivePath = _diskHelper.CreateDisk(_sourceImagePath, (100 * MiB));
        var sourcePartitions = _diskHelper.InitializeDisk(sourceDrivePath, PartitionTableType.GPT, [(FileSystemType.FAT32, 0)]);
        TestContext.Progress.WriteLine($"Source disk image created at: {_sourceImagePath}");

        // Directly instantiate SourceProvider
        // Note: When directly instantiating SourceProvider, we don't use the @ prefix
        // The @ prefix is only for Controller to recognize it as a remote source
        var sourceUrl = $"diskimage://{sourceDrivePath}";
        using var provider = new SourceProvider(sourceUrl, "", new Dictionary<string, string?>());

        // Initialize the provider
        await provider.Initialize(CancellationToken.None);

        // Enumerate entries
        var diskEntries = new List<ISourceProviderEntry>();
        await foreach (var entry in provider.Enumerate(CancellationToken.None))
        {
            diskEntries.Add(entry);
            TestContext.Progress.WriteLine($"Found entry: {entry.Path} (IsFolder: {entry.IsFolder}, Size: {entry.Size})");
        }

        // Verify hierarchy: disk → partition → filesystem → files
        Assert.That(diskEntries.Count, Is.GreaterThan(0), "Should have at least one entry (the disk)");

        // Fetch the rest of the enumaration.
        // Check for disk entry
        var diskEntry = diskEntries.FirstOrDefault(e => e.IsRootEntry);
        Assert.That(diskEntry, Is.Not.Null, "Should have a root disk entry");

        var entries = new List<ISourceProviderEntry>();
        await foreach (var entry in diskEntry!.Enumerate(CancellationToken.None))
            entries.Add(entry);

        TestContext.Progress.WriteLine($"Total entries found: {entries.Count}");

        // Check for geometry.json
        var geometryEntry = entries.FirstOrDefault(e => e.Path.EndsWith("geometry.json"));
        Assert.That(geometryEntry, Is.Not.Null, "Should have geometry.json entry");

        // Check for partition entries
        var partitionEntries = entries.Where(e => e.Path.Contains("part_")).ToList();
        Assert.That(partitionEntries.Count, Is.GreaterThan(0), "Should have at least one partition entry");

        // Add all of the filesystems.
        var fsEntries = new List<ISourceProviderEntry>();
        foreach (var partitionEntry in partitionEntries)
            await foreach (var entry in partitionEntry.Enumerate(CancellationToken.None))
                fsEntries.Add(entry);

        // Check for filesystem entries
        Assert.That(fsEntries.All(x => x.Path.Contains("fs_")));
        Assert.That(fsEntries.Count, Is.GreaterThan(0), "Should have at least one filesystem entry");
    }

    [Test]
    [Category("DiskImage")]
    public async Task Test_AutoUnmountOption_RestoreWhileOnline()
    {
        TestContext.Progress.WriteLine("Test: Auto Unmount Option - Restore While Disk Online");

        // Setup source disk image: 100MB, GPT, single FAT32 partition
        var sourceDrivePath = _diskHelper.CreateDisk(_sourceImagePath, (100 * MiB));
        var sourcePartitions = _diskHelper.InitializeDisk(sourceDrivePath, PartitionTableType.GPT, [(FileSystemType.FAT32, 0)]);
        TestContext.Progress.WriteLine($"Source disk image created at: {_sourceImagePath}");

        // Generate some test data
        await ToolTests.GenerateTestData(sourcePartitions.First(), 10, 5, 2, 1024);
        _diskHelper.FlushDisk(sourceDrivePath);
        _diskHelper.Unmount(sourceDrivePath);
        TestContext.Progress.WriteLine($"Test data generated on source partition");

        // Backup
        var backupResults = RunBackup(sourceDrivePath);
        TestUtils.AssertResults(backupResults);
        TestContext.Progress.WriteLine($"Backup completed successfully");

        // Create and attach disk image
        var restoreDrivePath = _diskHelper.CreateDisk(_restoreImagePath, (100 * MiB));

        // Initialize disk (the restore will overwrite this, but we need it formatted)
        _diskHelper.InitializeDisk(restoreDrivePath, PartitionTableType.GPT, [(FileSystemType.FAT32, 50 * MiB), (FileSystemType.FAT32, 0)]);
        _diskHelper.Mount(restoreDrivePath, _restoreMountPath);
        TestContext.Progress.WriteLine($"Restore disk image created and kept online at: {_restoreImagePath}");

        // First attempt: Restore without auto-unmount option should fail
        // because the disk is online and in use
        TestContext.Progress.WriteLine("Attempting restore without auto-unmount (should fail)...");
        var options = new Dictionary<string, string>(TestOptions)
        {
            ["restore-path"] = $"@diskimage://{restoreDrivePath}",
            ["overwrite"] = "true",
            ["restore-file-processors"] = "1",
            // Explicitly disable auto-unmount (though it's disabled by default)
            ["diskimage-restore-auto-unmount"] = "false"
        };

        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
        {
            try
            {
                var results = c.Restore(["*"]);

                // Verify that the restore failed (has errors)
                Assert.That(results.Errors.Any(), Is.True,
                    "Restore should fail when target disk is online and auto-unmount is not enabled");
                TestContext.Progress.WriteLine($"Restore failed as expected with errors: {string.Join(", ", results.Errors)}");
            }
            catch (IOException)
            {
                // Assumed to fail
            }
            catch (UserInformationException)
            {
                // Assumed to fail
            }
        }

        // Second attempt: Restore with auto-unmount option should succeed
        TestContext.Progress.WriteLine("Attempting restore with auto-unmount enabled (should succeed)...");

        options["diskimage-restore-auto-unmount"] = "true";
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
        {
            var results = c.Restore(["*"]);

            Assert.That(!results.Errors.Any() && results.Warnings.Count() <= 1);
        }
        TestContext.Progress.WriteLine($"Restore completed successfully with auto-unmount option");

        // Mount before verification, to make disks online on Windows.
        sourcePartitions = _diskHelper.Mount(sourceDrivePath, _sourceMountPath, readOnly: true);
        var restorePartitions = _diskHelper.Mount(restoreDrivePath, _restoreMountPath, readOnly: true);

        // Verify partition table matches
        VerifyPartitionTableMatches(sourceDrivePath, restoreDrivePath);

        // Verify data matches byte-for-byte
        var sourcePartition = sourcePartitions.First();
        var restorePartition = restorePartitions.First();
        CompareDirectories(sourcePartition, restorePartition);
    }

    #region Helper Methods

    /// <summary>
    /// Runs a backup operation using the Controller.
    /// </summary>
    /// <param name="physicalDrivePath">The physical drive path to backup.</param>
    /// <param name="treatFilesystemAsUnknown">If true, treats filesystem as unknown (forces raw block-based backup).</param>
    /// <returns>The backup results.</returns>
    private IBackupResults RunBackup(string physicalDrivePath, bool treatFilesystemAsUnknown = false)
    {
        var options = new Dictionary<string, string>(TestOptions);
        options["enable-module"] = "diskimage";
        options["concurrency-fileprocessors"] = "1";

        if (treatFilesystemAsUnknown)
        {
            options["diskimage-filesystem-unknown"] = "true";
        }

        using var c = new Controller("file://" + TARGETFOLDER, options, null);
        var sourceUrl = $"@/testdisk|diskimage://{physicalDrivePath}";
        return c.Backup(new[] { sourceUrl });
    }

    /// <summary>
    /// Runs a restore operation using the Controller.
    /// </summary>
    /// <param name="restoreDrivePath">The physical drive path to restore to.</param>
    /// <returns>The restore results.</returns>
    private IRestoreResults RunRestore(string restoreDrivePath)
    {
        var options = new Dictionary<string, string>(TestOptions);
        options["restore-path"] = $"@diskimage://{restoreDrivePath}";
        options["overwrite"] = "true";
        options["restore-file-processors"] = "1";

        using var c = new Controller("file://" + TARGETFOLDER, options, null);
        var results = c.Restore(new[] { "*" });

        return results;
    }

    /// <summary>
    /// Recursively compares two directories for structural and content equality.
    /// </summary>
    /// <param name="sourcePath">The source directory path.</param>
    /// <param name="restorePath">The restored directory path.</param>
    private void CompareDirectories(string sourcePath, string restorePath)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint
        };

        // Get all files in source, except for system files, which cannot be read directly
        var sourceFiles = Directory.EnumerateFiles(sourcePath, "*", options)
            .Select(f => Path.GetRelativePath(sourcePath, f))
            .Where(f => !string.IsNullOrEmpty(f))
            .OrderBy(f => f)
            .ToList();
        var restoreFiles = Directory.EnumerateFiles(restorePath, "*", options)
            .Select(f => Path.GetRelativePath(restorePath, f))
            .Where(f => !string.IsNullOrEmpty(f))
            .OrderBy(f => f)
            .ToList();

        Assert.That(sourceFiles.Count, Is.EqualTo(restoreFiles.Count),
            $"Number of files in source and restore should match. Source: {sourceFiles.Count}, Restore: {restoreFiles.Count}");
        Assert.That(sourceFiles.Count, Is.GreaterThan(0),
            "Source partition should contain files to verify");
        Assert.That(restoreFiles.Count, Is.GreaterThan(0),
            "Restore partition should contain files to verify");

        // Check that all source files exist in restore
        foreach (var relativePath in sourceFiles)
        {
            Assert.That(restoreFiles, Does.Contain(relativePath),
                $"Restored drive should contain file: {relativePath}");
        }

        // Compare file contents byte-by-byte
        foreach (var relativeSourceFile in sourceFiles)
        {
            var sourceFile = Path.Combine(sourcePath, relativeSourceFile);
            var restoreFile = Path.Combine(restorePath, relativeSourceFile);

            if (File.Exists(restoreFile))
            {
                var sourceBytes = File.ReadAllBytes(sourceFile);
                var restoreBytes = File.ReadAllBytes(restoreFile);

                Assert.That(restoreBytes.Length, Is.EqualTo(sourceBytes.Length),
                    $"File size mismatch for {sourceFile}");
                Assert.That(restoreBytes, Is.EqualTo(sourceBytes),
                    $"File content mismatch for {sourceFile}");
            }
        }

        TestContext.Progress.WriteLine($"Verified {sourceFiles.Count} files match between source and restored drives");
    }

    /// <summary>
    /// Verifies that the geometry metadata is present and valid.
    /// </summary>
    /// <param name="target">The backup target URL.</param>
    /// <param name="options">The options to use when accessing the backup.</param>
    /// <param name="partitions">The expected partitions and their sizes.</param>
    private void VerifyGeometryMetadata(string target, Dictionary<string, string> options, (FileSystemType, long)[] partitions)
    {
        TestContext.Progress.WriteLine("Verifying geometry metadata...");

        using (var c = new Controller("file://" + target, options, null))
        {
            var listResults = c.List("*");
            Assert.That(listResults.Files, Is.Not.Null);

            var geometryFile = listResults.Files.FirstOrDefault(f => f.Path.EndsWith("geometry.json"));
            Assert.That(geometryFile, Is.Not.Null, "geometry.json should be present in the backup");

            // Restore and parse the geometry.json file
            var geometryPath = Path.Combine(RESTOREFOLDER, $"geometry.json");
            try
            {
                var restoreOptions = new Dictionary<string, string>(options)
                {
                    ["restore-path"] = RESTOREFOLDER,
                    ["overwrite"] = "true"
                };

                using (var restoreController = new Controller("file://" + target, restoreOptions, null))
                {
                    var restoreResults = restoreController.Restore(new[] { geometryFile!.Path });
                    TestUtils.AssertResults(restoreResults);
                }

                // Read and parse the geometry.json
                if (File.Exists(geometryPath))
                {
                    var json = File.ReadAllText(geometryPath);
                    var geometry = GeometryMetadata.FromJson(json);

                    Assert.That(geometry, Is.Not.Null, "geometry.json should deserialize successfully");
                    Assert.That(geometry!.Version, Is.GreaterThan(0), "Geometry version should be set");
                    Assert.That(geometry.Disk, Is.Not.Null, "Disk geometry should be present");
                    Assert.That(geometry.Disk!.Size, Is.GreaterThan(0), "Disk size should be greater than 0");
                    Assert.That(geometry.Disk.SectorSize, Is.GreaterThan(0), "Sector size should be greater than 0");
                    Assert.That(geometry.PartitionTable, Is.Not.Null, "Partition table should be present");
                    Assert.That(geometry.Partitions, Is.Not.Null, "Partitions list should be present");
                    Assert.That(geometry.Partitions!.Count, Is.GreaterThan(0), "Should have at least one partition");

                    TestContext.Progress.WriteLine($"Geometry metadata verified: Disk size={geometry.Disk.Size}, " +
                        $"Sector size={geometry.Disk.SectorSize}, Partitions={geometry.Partitions.Count}, " +
                        $"Table type={geometry.PartitionTable!.Type}");

                    for (int i = 0; i < geometry.Partitions.Count; i++)
                    {
                        var partition = geometry.Partitions[i];
                        TestContext.Progress.WriteLine($"Partition {i}: Type={partition.Type}, Start={partition.StartOffset}, Size={partition.Size}");

                        Assert.That(partition.Size, Is.GreaterThan(0), $"Partition {i} size should be greater than 0");
                        if (partitions[i].Item2 > 0)
                        {
                            Assert.That(partition.Size, Is.EqualTo(partitions[i].Item2), $"Partition {i} size should match expected");
                        }
                        Assert.That(partition.StartOffset, Is.GreaterThanOrEqualTo(0), $"Partition {i} start offset should be non-negative");
                        Assert.That(partition.Type, Is.Not.Null, $"Partition {i} type should be present");
                        Assert.That(partition.FilesystemType, Is.Not.Null, $"Partition {i} filesystem type should be present");
                        // Type shouldn't be checked until implemented.
                        //Assert.That(partition.FilesystemType, Is.EqualTo(partitions[i].Item1), $"Partition {i} filesystem type should be known");
                    }
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(geometryPath))
                    {
                        File.Delete(geometryPath);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    /// <summary>
    /// Verifies that the restored partition table matches the source.
    /// </summary>
    /// <param name="sourceDrivePath">The physical drive path of the source disk.</param>
    /// <param name="restoreDrivePath">The physical drive path of the restored disk.</param>
    private void VerifyPartitionTableMatches(string sourceDrivePath, string restoreDrivePath)
    {
        TestContext.Progress.WriteLine("Verifying partition table matches...");

        // Get source disk details
        var sourceTable = _diskHelper.GetPartitionTable(sourceDrivePath);
        TestContext.Progress.WriteLine("Source disk details:\n" + sourceTable);

        // Get restore disk details
        var restoreTable = _diskHelper.GetPartitionTable(restoreDrivePath);
        TestContext.Progress.WriteLine("Restore disk details:\n" + restoreTable);

        Assert.AreEqual(sourceTable.Type, restoreTable.Type, "Partition table types should match");
        Assert.AreEqual(sourceTable.SectorSize, restoreTable.SectorSize, "Sector sizes should match");
        Assert.AreEqual(sourceTable.Size, restoreTable.Size, "Size of the disks should match");

        // Retrieve partition information
        var sourcePartitions = _diskHelper.GetPartitions(sourceDrivePath);
        var restorePartitions = _diskHelper.GetPartitions(restoreDrivePath);

        // Verify partition counts match
        Assert.AreEqual(sourcePartitions.Length, restorePartitions.Length, "Number of partitions should match");

        // Verify partitions match
        for (int i = 0; i < sourcePartitions.Length; i++)
        {
            Assert.AreEqual(sourcePartitions[i].Type, restorePartitions[i].Type, $"Partition {i} types should match");
            Assert.AreEqual(sourcePartitions[i].StartOffset, restorePartitions[i].StartOffset, $"Partition {i} offsets should match");
            Assert.AreEqual(sourcePartitions[i].Size, restorePartitions[i].Size, $"Partition {i} sizes should match");
        }

        TestContext.Progress.WriteLine($"Partition table verified.");
    }

    #endregion

    #region Filesystem-Aware Integration Tests

    /// <summary>
    /// Tests that incremental backup with filesystem awareness skips unchanged blocks.
    /// Creates a disk with the specified filesystem, performs initial backup, modifies one file,
    /// performs second backup, and verifies only changed blocks were processed.
    /// </summary>
    [TestCase((int)(50 * MiB), PartitionTableType.GPT, FileSystemType.FAT32)]
    [TestCase((int)(50 * MiB), PartitionTableType.GPT, FileSystemType.Unknown)]
    [Category("DiskImage")]
    [Category("DiskImageFileSystem")]
    public async Task Test_FileSystem_IncrementalBackup_UnchangedBlocks_Skipped(int size, PartitionTableType tableType, FileSystemType fsType)
    {
        await TestContext.Progress.WriteLineAsync($"Test: {fsType} Incremental Backup - Unchanged Blocks Skipped");

        // When testing Unknown filesystem, format source as FAT32 to allow test data generation,
        // but treat it as unknown during backup
        var sourceFsType = fsType == FileSystemType.Unknown ? FileSystemType.FAT32 : fsType;
        var treatFilesystemAsUnknown = fsType == FileSystemType.Unknown;

        // Create source disk with specified partition
        var sourceDrivePath = _diskHelper.CreateDisk(_sourceImagePath, size);
        var sourcePartitions = _diskHelper.InitializeDisk(sourceDrivePath, tableType, [(sourceFsType, 0)]);
        await TestContext.Progress.WriteLineAsync($"Source disk created at: {_sourceImagePath}");

        // Generate initial test data
        await ToolTests.GenerateTestData(sourcePartitions.First(), 10, 3, 1, 1024);
        _diskHelper.FlushDisk(sourceDrivePath);
        _diskHelper.Unmount(sourceDrivePath);
        await TestContext.Progress.WriteLineAsync("Initial test data generated");

        // First backup
        var firstBackupResults = RunBackup(sourceDrivePath, treatFilesystemAsUnknown);

        // When treating filesystem as unknown, the option may not be recognized by the Controller
        // so we only check for errors, not warnings
        if (treatFilesystemAsUnknown)
        {
            Assert.That(firstBackupResults.Errors.Count(), Is.EqualTo(0),
                "Backup should have no errors");
        }
        else
        {
            TestUtils.AssertResults(firstBackupResults);
        }

        var firstBackupSize = firstBackupResults.SizeOfOpenedFiles;
        await TestContext.Progress.WriteLineAsync($"First backup completed: {firstBackupResults.OpenedFiles} files opened, {firstBackupSize} bytes");

        // Verify first backup processed the expected amount of data
        // With FAT32 awareness, unallocated blocks are returned as zeros (no disk read)
        Assert.That(firstBackupResults.SizeOfOpenedFiles, Is.GreaterThan(0),
            "First backup should have opened and processed files");

        // Create restore disk for initial restore verification
        var restoreDrivePath = _diskHelper.CreateDisk(_restoreImagePath, size);
        _diskHelper.InitializeDisk(restoreDrivePath, tableType, []);
        _diskHelper.Unmount(restoreDrivePath);
        await TestContext.Progress.WriteLineAsync($"Restore disk created at: {_restoreImagePath}");

        var restoreResults = RunRestore(restoreDrivePath);
        TestUtils.AssertResults(restoreResults);
        await TestContext.Progress.WriteLineAsync("Initial restore completed successfully");

        // Reattach and verify initial restore
        sourceDrivePath = _diskHelper.ReAttach(_sourceImagePath, sourceDrivePath, tableType, readOnly: true);
        restoreDrivePath = _diskHelper.ReAttach(_restoreImagePath, restoreDrivePath, tableType, readOnly: true);

        var fsTypes = new[] { sourceFsType };
        var sourceMounted = _diskHelper.Mount(sourceDrivePath, _sourceMountPath, readOnly: true, fileSystemTypes: fsTypes);
        var restoreMounted = _diskHelper.Mount(restoreDrivePath, _restoreMountPath, readOnly: true, fileSystemTypes: fsTypes);

        CompareDirectories(sourceMounted.First(), restoreMounted.First());
        await TestContext.Progress.WriteLineAsync("Initial restore verified successfully");

        // Unmount both disks to prepare for modification
        _diskHelper.Unmount(sourceDrivePath);
        _diskHelper.Unmount(restoreDrivePath);
        await TestContext.Progress.WriteLineAsync("Both disks unmounted");

        // Remount source drive in write-enabled mode
        sourceDrivePath = _diskHelper.ReAttach(_sourceImagePath, sourceDrivePath, tableType, readOnly: false);
        sourceMounted = _diskHelper.Mount(sourceDrivePath, _sourceMountPath, readOnly: false, fileSystemTypes: fsTypes);
        await TestContext.Progress.WriteLineAsync("Source disk remounted in write mode");

        // Modify a few files on the source - create new files and modify existing ones
        var sourcePartitionPath = sourceMounted.First();
        var modifiedFiles = new List<string>();

        // Create a new directory with some new files
        var newDirPath = Path.Combine(sourcePartitionPath, "incremental_new_files");
        Directory.CreateDirectory(newDirPath);
        for (int i = 0; i < 3; i++)
        {
            var newFilePath = Path.Combine(newDirPath, $"new_file_{i}.txt");
            var content = $"This is new content for incremental backup test - file {i} - {Guid.NewGuid()}";
            await File.WriteAllTextAsync(newFilePath, content);
            // Set a recent modification time to ensure the backup detects the change
            File.SetLastWriteTimeUtc(newFilePath, DateTime.UtcNow);
            modifiedFiles.Add(newFilePath);
        }
        // Also update directory timestamp
        Directory.SetLastWriteTimeUtc(newDirPath, DateTime.UtcNow);
        await TestContext.Progress.WriteLineAsync($"Created {modifiedFiles.Count} new files in {newDirPath}");

        // Modify an existing file if one exists
        var existingFiles = Directory.GetFiles(sourcePartitionPath, "*.txt", SearchOption.AllDirectories)
            .Where(f => !f.Contains("incremental_new_files"))
            .ToArray();
        if (existingFiles.Length > 0)
        {
            var fileToModify = existingFiles[0];
            var originalContent = await File.ReadAllTextAsync(fileToModify);
            var modifiedContent = originalContent + $"\n\nModified for incremental backup test - {Guid.NewGuid()}";
            await File.WriteAllTextAsync(fileToModify, modifiedContent);
            // Update modification time to ensure backup detects the change
            File.SetLastWriteTimeUtc(fileToModify, DateTime.UtcNow.AddSeconds(1));
            modifiedFiles.Add(fileToModify);
            await TestContext.Progress.WriteLineAsync($"Modified existing file: {fileToModify}");
        }

        // Flush and unmount source after modifications
        _diskHelper.FlushDisk(sourceDrivePath);
        _diskHelper.Unmount(sourceDrivePath);
        await TestContext.Progress.WriteLineAsync("Modifications complete, source disk unmounted");

        // Re-attach the source disk for backup (disk needs to be attached for backup to access it)
        sourceDrivePath = _diskHelper.ReAttach(_sourceImagePath, sourceDrivePath, tableType, readOnly: true);
        await TestContext.Progress.WriteLineAsync($"Source disk re-attached for second backup at: {sourceDrivePath}");

        // Second backup (incremental)
        var secondBackupResults = RunBackup(sourceDrivePath, treatFilesystemAsUnknown);

        // When treating filesystem as unknown, the option may not be recognized by the Controller
        // so we only check for errors, not warnings
        if (treatFilesystemAsUnknown)
        {
            Assert.That(secondBackupResults.Errors.Count(), Is.EqualTo(0),
                "Backup should have no errors");
        }
        else
        {
            TestUtils.AssertResults(secondBackupResults);
        }

        var secondBackupSize = secondBackupResults.SizeOfOpenedFiles;
        await TestContext.Progress.WriteLineAsync($"Second backup completed: {secondBackupResults.OpenedFiles} files opened, {secondBackupSize} bytes examined, {secondBackupResults.SizeOfAddedFiles} bytes added");

        if (fsType is not FileSystemType.Unknown)
        {
            // Verify the second backup processed significantly fewer files than the first
            // The second backup should only process modified/new files, not all files
            Assert.That(secondBackupResults.OpenedFiles, Is.LessThan(firstBackupResults.OpenedFiles),
                "Incremental backup should open fewer files than the initial backup");
            Assert.That(secondBackupResults.SizeOfOpenedFiles, Is.LessThan(firstBackupResults.SizeOfOpenedFiles),
                "Incremental backup should examine less data than the initial backup");
        }

        // Also verify that the second backup actually modified some files
        Assert.That(secondBackupResults.ModifiedFiles, Is.GreaterThan(0),
            "Incremental backup should have added new or modified files");

        // Calculate and log the reduction ratio
        var fileReductionRatio = (double)secondBackupResults.OpenedFiles / firstBackupResults.OpenedFiles;
        var sizeReductionRatio = (double)secondBackupResults.SizeOfOpenedFiles / firstBackupResults.SizeOfOpenedFiles;
        await TestContext.Progress.WriteLineAsync($"File reduction: {fileReductionRatio:P2} ({secondBackupResults.OpenedFiles}/{firstBackupResults.OpenedFiles})");
        await TestContext.Progress.WriteLineAsync($"Size reduction: {sizeReductionRatio:P2} ({secondBackupResults.SizeOfOpenedFiles}/{firstBackupResults.SizeOfOpenedFiles})");

        // Restore the incremental backup to a fresh disk
        _diskHelper.CleanupDisk(_restoreImagePath);
        DiskImageTestHelpers.SafeDeleteFile(_restoreImagePath);

        restoreDrivePath = _diskHelper.CreateDisk(_restoreImagePath, size);
        _diskHelper.InitializeDisk(restoreDrivePath, tableType, []);
        _diskHelper.Unmount(restoreDrivePath);
        await TestContext.Progress.WriteLineAsync($"Fresh restore disk created for incremental restore");

        var incrementalRestoreResults = RunRestore(restoreDrivePath);
        TestUtils.AssertResults(incrementalRestoreResults);
        await TestContext.Progress.WriteLineAsync("Incremental restore completed successfully");

        // Reattach and verify the incremental restore matches the modified source
        sourceDrivePath = _diskHelper.ReAttach(_sourceImagePath, sourceDrivePath, tableType, readOnly: true);
        restoreDrivePath = _diskHelper.ReAttach(_restoreImagePath, restoreDrivePath, tableType, readOnly: true);

        sourceMounted = _diskHelper.Mount(sourceDrivePath, _sourceMountPath, readOnly: true, fileSystemTypes: fsTypes);
        restoreMounted = _diskHelper.Mount(restoreDrivePath, _restoreMountPath, readOnly: true, fileSystemTypes: fsTypes);

        CompareDirectories(sourceMounted.First(), restoreMounted.First());

        await TestContext.Progress.WriteLineAsync($"{fsType} incremental backup and restore verified successfully");
    }

    /// <summary>
    /// Tests that unallocated space compresses well due to zero-block deduplication.
    /// Creates a disk with the specified filesystem and sparse data (few files, lots of free space),
    /// backs it up, and verifies the backup size is significantly smaller than the disk size.
    /// </summary>
    [TestCase((int)(50 * MiB), PartitionTableType.GPT, FileSystemType.FAT32)]
    [TestCase((int)(50 * MiB), PartitionTableType.GPT, FileSystemType.Unknown)]
    [Category("DiskImage")]
    [Category("DiskImageFileSystem")]
    public async Task Test_FileSystem_UnallocatedSpace_CompressesWell(int size, PartitionTableType tableType, FileSystemType fsType)
    {
        await TestContext.Progress.WriteLineAsync($"Test: {fsType} Unallocated Space Compression");

        // When testing Unknown filesystem, format source as FAT32 to allow test data generation,
        // but treat it as unknown during backup
        var sourceFsType = fsType == FileSystemType.Unknown ? FileSystemType.FAT32 : fsType;
        var treatFilesystemAsUnknown = fsType == FileSystemType.Unknown;

        // Create source disk with specified partition
        var sourceDrivePath = _diskHelper.CreateDisk(_sourceImagePath, size);
        var sourcePartitions = _diskHelper.InitializeDisk(sourceDrivePath, tableType, [(sourceFsType, 0)]);
        await TestContext.Progress.WriteLineAsync($"Source disk created at: {_sourceImagePath}");

        // Generate minimal test data (sparse data - only a few small files)
        // This leaves most of the 50MB as unallocated space
        await ToolTests.GenerateTestData(sourcePartitions.First(), 3, 0, 0, 512);
        _diskHelper.FlushDisk(sourceDrivePath);
        _diskHelper.Unmount(sourceDrivePath);
        await TestContext.Progress.WriteLineAsync("Sparse test data generated (few files, lots of free space)");

        // Backup
        var backupResults = RunBackup(sourceDrivePath, treatFilesystemAsUnknown);
        // When treating filesystem as unknown, the option may not be recognized by the Controller
        // so we only check for errors, not warnings
        if (treatFilesystemAsUnknown)
        {
            Assert.That(backupResults.Errors.Count(), Is.EqualTo(0),
                "Backup should have no errors");
        }
        else
        {
            TestUtils.AssertResults(backupResults);
        }
        await TestContext.Progress.WriteLineAsync($"Backup completed: {backupResults.SizeOfOpenedFiles} bytes examined");

        // Calculate the actual backup size from the target folder
        var backupFiles = Directory.GetFiles(TARGETFOLDER, "*", SearchOption.AllDirectories);
        long actualBackupSize = backupFiles.Sum(f => new FileInfo(f).Length);
        await TestContext.Progress.WriteLineAsync($"Actual backup size on disk: {actualBackupSize} bytes");

        // The backup should be significantly smaller than the full disk size
        // With zero-block deduplication, unallocated clusters return zeros which compress very well
        var compressionRatio = (double)actualBackupSize / size;
        await TestContext.Progress.WriteLineAsync($"Compression ratio: {compressionRatio:P2} ({actualBackupSize}/{size})");

        // Backup should be less than 50% of disk size due to zero deduplication
        // (This is a conservative estimate - actual ratio should be much better)
        Assert.That(compressionRatio, Is.LessThan(0.5),
            "Backup with sparse data should be significantly smaller than full disk size due to zero-block deduplication");

        // Create restore disk and verify restore works
        var restoreDrivePath = _diskHelper.CreateDisk(_restoreImagePath, size);
        _diskHelper.InitializeDisk(restoreDrivePath, tableType, []);
        _diskHelper.Unmount(restoreDrivePath);

        var restoreResults = RunRestore(restoreDrivePath);
        TestUtils.AssertResults(restoreResults);

        // Reattach and verify
        sourceDrivePath = _diskHelper.ReAttach(_sourceImagePath, sourceDrivePath, tableType, readOnly: true);
        restoreDrivePath = _diskHelper.ReAttach(_restoreImagePath, restoreDrivePath, tableType, readOnly: true);

        var fsTypes = new[] { fsType };
        var sourceMounted = _diskHelper.Mount(sourceDrivePath, _sourceMountPath, readOnly: true, fileSystemTypes: fsTypes);
        var restoreMounted = _diskHelper.Mount(restoreDrivePath, _restoreMountPath, readOnly: true, fileSystemTypes: fsTypes);

        CompareDirectories(sourceMounted.First(), restoreMounted.First());

        await TestContext.Progress.WriteLineAsync($"{fsType} unallocated space compression verified successfully");
    }

    /// <summary>
    /// Tests that a completely full disk has all blocks marked as allocated.
    /// Creates a disk filled with data and verifies the backup reads the full disk.
    /// </summary>
    [TestCase((int)(50 * MiB), PartitionTableType.GPT, FileSystemType.FAT32)]
    [TestCase((int)(50 * MiB), PartitionTableType.GPT, FileSystemType.Unknown)]
    [Category("DiskImage")]
    [Category("DiskImageFileSystem")]
    public async Task Test_FileSystem_FullDisk_AllBlocksAllocated(int size, PartitionTableType tableType, FileSystemType fsType)
    {
        await TestContext.Progress.WriteLineAsync($"Test: {fsType} Full Disk - All Blocks Allocated");

        // When testing Unknown filesystem, format source as FAT32 to allow test data generation,
        // but treat it as unknown during backup
        var sourceFsType = fsType == FileSystemType.Unknown ? FileSystemType.FAT32 : fsType;
        var treatFilesystemAsUnknown = fsType == FileSystemType.Unknown;

        // Create source disk with specified partition (must be at least 32MB for FAT32)
        var sourceDrivePath = _diskHelper.CreateDisk(_sourceImagePath, size);
        var sourcePartitions = _diskHelper.InitializeDisk(sourceDrivePath, tableType, [(sourceFsType, 0)]);
        await TestContext.Progress.WriteLineAsync($"Source disk created at: {_sourceImagePath} ({size} bytes)");

        // Fill the disk with data (create enough files to fill most of the space)
        var partitionPath = sourcePartitions.First();

        // Generate many files to fill the disk
        // Using larger files to fill space quickly
        // Adjust batch count based on filesystem type - Unknown (raw) mode may need different amounts
        var batchCount = fsType == FileSystemType.Unknown ? 5 : 10;
        for (int batch = 0; batch < batchCount; batch++)
        {
            await ToolTests.GenerateTestData(Path.Combine(partitionPath, $"batch_{batch}"), 10, 0, 0, 4096);
        }
        _diskHelper.FlushDisk(sourceDrivePath);
        _diskHelper.Unmount(sourceDrivePath);
        await TestContext.Progress.WriteLineAsync("Disk filled with data");

        // Backup
        var backupResults = RunBackup(sourceDrivePath, treatFilesystemAsUnknown);

        // When treating filesystem as unknown, the option may not be recognized by the Controller
        // so we only check for errors, not warnings
        if (treatFilesystemAsUnknown)
        {
            Assert.That(backupResults.Errors.Count(), Is.EqualTo(0),
                "Backup should have no errors");
        }
        else
        {
            TestUtils.AssertResults(backupResults);
        }

        await TestContext.Progress.WriteLineAsync($"Backup completed: {backupResults.SizeOfOpenedFiles} bytes examined, {backupResults.SizeOfAddedFiles} bytes added");

        // With a full disk, most blocks should be allocated
        // The backup should have examined a significant portion of the disk
        // Note: We examine files (blocks), and with a full disk, SizeOfOpenedFiles
        // should be close to the partition size
        Assert.That(backupResults.SizeOfOpenedFiles, Is.GreaterThan(size * 0.5),
            "Full disk backup should examine most of the disk size");

        // Create restore disk and verify restore
        var restoreDrivePath = _diskHelper.CreateDisk(_restoreImagePath, size);
        _diskHelper.InitializeDisk(restoreDrivePath, tableType, []);
        _diskHelper.Unmount(restoreDrivePath);

        var restoreResults = RunRestore(restoreDrivePath);
        TestUtils.AssertResults(restoreResults);

        // Reattach and verify
        sourceDrivePath = _diskHelper.ReAttach(_sourceImagePath, sourceDrivePath, tableType, readOnly: true);
        restoreDrivePath = _diskHelper.ReAttach(_restoreImagePath, restoreDrivePath, tableType, readOnly: true);

        var fsTypes = new[] { fsType };
        var sourceMounted = _diskHelper.Mount(sourceDrivePath, _sourceMountPath, readOnly: true, fileSystemTypes: fsTypes);
        var restoreMounted = _diskHelper.Mount(restoreDrivePath, _restoreMountPath, readOnly: true, fileSystemTypes: fsTypes);

        CompareDirectories(sourceMounted.First(), restoreMounted.First());

        await TestContext.Progress.WriteLineAsync($"{fsType} full disk backup verified successfully");
    }

    #endregion
}
