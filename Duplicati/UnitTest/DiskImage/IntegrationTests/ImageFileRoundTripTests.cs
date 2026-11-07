using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.General;
using Duplicati.Proprietary.DiskImage.Partition;
using Duplicati.UnitTest.DiskImage.Helpers;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest.DiskImage.IntegrationTests;

/// <summary>
/// Round trips that back up a disk and restore it to a raw image file instead of a disk.
/// </summary>
public class ImageFileRoundTripTests : DiskImageTests
{
    // The file extension selects the image format: raw, VHD or VHDX

    [Test, Category("DiskImage")]
    public Task Test_ImageFile_GPT_FAT32_RawBlocks_Async([Values("img", "vhd", "vhdx")] string extension) =>
        ImageFileRoundTripAsync((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.FAT32, 0)], rawBlocks: true, extension);

    [Test, Category("DiskImage")]
    public Task Test_ImageFile_GPT_FAT32_FilesystemParsed_Async([Values("img", "vhd", "vhdx")] string extension) =>
        ImageFileRoundTripAsync((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.FAT32, 0)], rawBlocks: false, extension);

    [Test, Category("DiskImage")]
    public Task Test_ImageFile_MBR_FAT32_FAT32_RawBlocks_Async([Values("img", "vhd", "vhdx")] string extension) =>
        ImageFileRoundTripAsync((int)(100 * MiB), PartitionTableType.MBR, [(FileSystemType.FAT32, 50 * MiB), (FileSystemType.FAT32, 0)], rawBlocks: true, extension);

    [Test, Category("DiskImage")]
    public Task Test_ImageFile_GPT_NTFS_FilesystemParsed_Async([Values("img", "vhd", "vhdx")] string extension) =>
        ImageFileRoundTripAsync((int)(50 * MiB), PartitionTableType.GPT, [(FileSystemType.NTFS, 0)], rawBlocks: false, extension);

    [Test, Category("DiskImage")]
    public Task Test_ImageFile_GPT_Ext4_Ext4_RawBlocks_Async([Values("img", "vhd", "vhdx")] string extension) =>
        ImageFileRoundTripAsync((int)(100 * MiB), PartitionTableType.GPT, [(FileSystemType.Ext4, 50 * MiB), (FileSystemType.Ext4, 0)], rawBlocks: true, extension);

    /// <summary>
    /// Runs a restore to an image file using the Controller.
    /// </summary>
    /// <param name="imagePath">The path of the image file to restore to.</param>
    /// <returns>The restore results.</returns>
    private Task<IRestoreResults> RunImageFileRestoreAsync(string imagePath)
    {
        var options = new Dictionary<string, string>(TestOptions);
        options["restore-path"] = $"@diskimage-file://{imagePath}";
        options["restore-file-processors"] = "1";

        using var c = new Controller("file://" + TARGETFOLDER, options, null);
        return c.RestoreAsync(new[] { "*" });
    }

    /// <summary>
    /// Backs up a disk, restores it to an image file, and verifies the image.
    /// Raw block backups are compared byte for byte, partition by partition. For
    /// filesystem-parsed backups the free space is not restored, so the image is
    /// attached and the files are compared instead.
    /// </summary>
    /// <param name="size">The size of the source disk.</param>
    /// <param name="tableType">The partition table type of the source disk.</param>
    /// <param name="partitions">The partitions of the source disk.</param>
    /// <param name="rawBlocks">If <c>true</c>, the backup treats the filesystems as unknown and stores raw blocks.</param>
    /// <param name="extension">The extension of the image file, which selects its format.</param>
    private async Task ImageFileRoundTripAsync(int size, PartitionTableType tableType, (FileSystemType, long)[] partitions, bool rawBlocks, string extension)
    {
        foreach (var (fsType, _) in partitions)
        {
            var (isSupported, reason) = _diskHelper.IsFileSystemTypeSupported(fsType);
            if (!isSupported)
                Assert.Ignore(reason);
        }

        // The restored files are compared by attaching the image, which Windows
        // can only do for VHD and VHDX files, and Linux and macOS only for raw images
        var isVirtualDisk = extension is "vhd" or "vhdx";
        if (!rawBlocks && OperatingSystem.IsWindows() != isVirtualDisk)
            Assert.Ignore($"This platform cannot attach .{extension} images to compare the restored files.");

        var sourceDrivePath = _diskHelper.CreateDisk(_sourceImagePath, size);
        var sourcePartitions = _diskHelper.InitializeDisk(sourceDrivePath, tableType, partitions);
        foreach (var partition in sourcePartitions)
            await ToolTests.GenerateTestDataAsync(partition, 10, 5, 2, 1024);
        _diskHelper.FlushDisk(sourceDrivePath);
        _diskHelper.Unmount(sourceDrivePath);

        var backupResults = await RunBackupAsync(sourceDrivePath, treatFilesystemAsUnknown: rawBlocks);
        TestUtils.AssertResults(backupResults, ignoredWarnings: ["diskimage-filesystem-parsed"]);

        // The restore needs no device and no privileges
        var imagePath = Path.Combine(DATAFOLDER, $"duplicati_test_restored_{Guid.NewGuid()}.{extension}");
        try
        {
            var restoreResults = await RunImageFileRestoreAsync(imagePath);
            TestUtils.AssertResults(restoreResults);

            Assert.IsTrue(File.Exists(imagePath), "The image file should be written.");
            Assert.IsFalse(File.Exists(imagePath + ".partial"), "The partial file should be renamed.");

            sourceDrivePath = _diskHelper.ReAttach(_sourceImagePath, sourceDrivePath, tableType, readOnly: true);

            using (var sourceDisk = DiskImageTestHelpers.CreateRawDiskForIdentifier(sourceDrivePath))
            using (var imageDisk = new ImageFile(imagePath, imagePath, createNew: false, sparse: true, format: null))
            {
                Assert.IsNull(await sourceDisk.InitializeAsync(false, CancellationToken.None), "The source disk should open.");
                Assert.IsNull(await imageDisk.InitializeAsync(false, CancellationToken.None), "The image should open.");
                Assert.AreEqual(sourceDisk.Size, imageDisk.Size, "The image should have the size of the source disk.");

                var sourceTable = await ReadPartitionsAsync(sourceDisk, tableType);
                var imageTable = await ReadPartitionsAsync(imageDisk, tableType);
                Assert.AreEqual(sourceTable.Count, imageTable.Count, "The image should have the partitions of the source disk.");
                foreach (var (source, image) in sourceTable.Zip(imageTable))
                {
                    Assert.AreEqual(source.PartitionNumber, image.PartitionNumber, "Partition number");
                    Assert.AreEqual(source.StartOffset, image.StartOffset, $"Partition {source.PartitionNumber} offset");
                    Assert.AreEqual(source.Size, image.Size, $"Partition {source.PartitionNumber} size");

                    if (rawBlocks)
                        await CompareRangeAsync(sourceDisk, imageDisk, source.StartOffset, source.Size);
                }
            }

            if (!rawBlocks)
            {
                var fsTypes = partitions.Select(p => p.Item1).ToArray();
                var imageDrivePath = _diskHelper.AttachImage(imagePath, tableType, readOnly: true);
                try
                {
                    sourcePartitions = _diskHelper.Mount(sourceDrivePath, _sourceMountPath, readOnly: true, fileSystemTypes: fsTypes);
                    var restorePartitions = _diskHelper.Mount(imageDrivePath, _restoreMountPath, readOnly: true, fileSystemTypes: fsTypes);
                    foreach (var (sourcePartition, restorePartition) in sourcePartitions.Zip(restorePartitions))
                        CompareDirectories(sourcePartition, restorePartition);
                }
                finally
                {
                    _diskHelper.CleanupDisk(imagePath, imageDrivePath);
                }
            }
        }
        finally
        {
            DiskImageTestHelpers.SafeDeleteFile(imagePath);
            DiskImageTestHelpers.SafeDeleteFile(imagePath + ".partial");
        }
    }

    /// <summary>
    /// Reads the partitions of a disk.
    /// </summary>
    private static async Task<List<IPartition>> ReadPartitionsAsync(IRawDisk disk, PartitionTableType tableType)
    {
        using var table = await PartitionTableFactory.CreateAsync(disk, CancellationToken.None);
        Assert.IsNotNull(table, $"{disk.DevicePath} should have a partition table.");
        Assert.AreEqual(tableType, table!.TableType, $"{disk.DevicePath} should have a {tableType} partition table.");

        var partitions = new List<IPartition>();
        await foreach (var p in table.EnumeratePartitions(CancellationToken.None))
            partitions.Add(p);
        return partitions.OrderBy(p => p.PartitionNumber).ToList();
    }

    /// <summary>
    /// Compares a byte range of two disks.
    /// </summary>
    private static async Task CompareRangeAsync(IRawDisk source, IRawDisk image, long offset, long length)
    {
        const int chunk = (int)MiB;
        var a = new byte[chunk];
        var b = new byte[chunk];
        for (long position = 0; position < length; position += chunk)
        {
            var count = (int)Math.Min(chunk, length - position);
            await source.ReadBytesAsync(offset + position, a.AsMemory(0, count), CancellationToken.None);
            await image.ReadBytesAsync(offset + position, b.AsMemory(0, count), CancellationToken.None);
            if (!a.AsSpan(0, count).SequenceEqual(b.AsSpan(0, count)))
                Assert.Fail($"The image differs from the source disk in the range {offset + position}-{offset + position + count}.");
        }
    }
}
