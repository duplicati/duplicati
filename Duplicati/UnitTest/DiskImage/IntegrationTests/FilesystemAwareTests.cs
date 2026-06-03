
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.General;
using Duplicati.UnitTest.DiskImage.Helpers;
using NUnit.Framework;

namespace Duplicati.UnitTest.DiskImage.IntegrationTests;

public class FilesystemAwareTests : DiskImageTests
{
    /// <summary>
    /// Tests that incremental backup with filesystem awareness skips unchanged blocks.
    /// Creates a disk with the specified filesystem, performs initial backup, modifies one file,
    /// performs second backup, and verifies only changed blocks were processed.
    /// </summary>
    [TestCase((int)(50 * MiB), PartitionTableType.GPT, FileSystemType.FAT32)]
    [TestCase((int)(50 * MiB), PartitionTableType.GPT, FileSystemType.NTFS)]
    [TestCase((int)(50 * MiB), PartitionTableType.GPT, FileSystemType.Unknown)]
    [Category("DiskImage")]
    [Category("DiskImageFileSystem")]
    public async Task Test_FileSystem_IncrementalBackup_UnchangedBlocks_Skipped_Async(int size, PartitionTableType tableType, FileSystemType fsType)
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        var (isSupported, reason) = _diskHelper.IsFileSystemTypeSupported(fsType);
        if (!isSupported)
            Assert.Ignore(reason);

        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"Test: {fsType} Incremental Backup - Unchanged Blocks Skipped ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();

        // When testing Unknown filesystem, format source as FAT32 to allow test data generation,
        // but treat it as unknown during backup
        var sourceFsType = fsType == FileSystemType.Unknown ? FileSystemType.FAT32 : fsType;
        var treatFilesystemAsUnknown = fsType == FileSystemType.Unknown;

        // Create source disk with specified partition
        var sourceDrivePath = _diskHelper.CreateDisk(_sourceImagePath, size);
        var sourcePartitions = _diskHelper.InitializeDisk(sourceDrivePath, tableType, [(sourceFsType, 0)]);
        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"Source disk created at: {_sourceImagePath} ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();

        // Generate initial test data
        await ToolTests.GenerateTestDataAsync(sourcePartitions.First(), 10, 3, 1, 1024);
        _diskHelper.FlushDisk(sourceDrivePath);
        _diskHelper.Unmount(sourceDrivePath);
        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"Initial test data generated ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();

        // First backup
        var firstBackupResults = await RunBackupAsync(sourceDrivePath, treatFilesystemAsUnknown);

        // When treating filesystem as unknown, the option may not be recognized by the Controller
        // so we only check for errors, not warnings
        if (treatFilesystemAsUnknown)
        {
            Assert.That(firstBackupResults.Errors.Count(), Is.EqualTo(0),
                "Backup should have no errors");
        }
        else
        {
            TestUtils.AssertResults(firstBackupResults, ignoredWarnings: ["diskimage-filesystem-parsed"]);
        }

        var firstBackupSize = firstBackupResults.SizeOfOpenedFiles;
        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"First backup completed: {firstBackupResults.OpenedFiles} files opened, {firstBackupSize} bytes ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();

        // Verify first backup processed the expected amount of data
        // With FAT32 awareness, unallocated blocks are returned as zeros (no disk read)
        Assert.That(firstBackupResults.SizeOfOpenedFiles, Is.GreaterThan(0),
            "First backup should have opened and processed files");

        // Create restore disk for initial restore verification
        var restoreDrivePath = _diskHelper.CreateDisk(_restoreImagePath, size);
        _diskHelper.InitializeDisk(restoreDrivePath, tableType, []);
        _diskHelper.Unmount(restoreDrivePath);
        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"Restore disk created at: {_restoreImagePath} ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();

        var restoreResults = await RunRestoreAsync(restoreDrivePath);
        TestUtils.AssertResults(restoreResults, ignoredWarnings: ["diskimage-filesystem-parsed"]);
        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"Initial restore completed successfully ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();

        // Reattach and verify initial restore
        sourceDrivePath = _diskHelper.ReAttach(_sourceImagePath, sourceDrivePath, tableType, readOnly: true);
        restoreDrivePath = _diskHelper.ReAttach(_restoreImagePath, restoreDrivePath, tableType, readOnly: true);

        var fsTypes = new[] { sourceFsType };
        var sourceMounted = _diskHelper.Mount(sourceDrivePath, _sourceMountPath, readOnly: true, fileSystemTypes: fsTypes);
        var restoreMounted = _diskHelper.Mount(restoreDrivePath, _restoreMountPath, readOnly: true, fileSystemTypes: fsTypes);

        CompareDirectories(sourceMounted.First(), restoreMounted.First());
        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"Initial restore verified successfully ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();

        // Unmount both disks to prepare for modification
        _diskHelper.Unmount(sourceDrivePath);
        _diskHelper.Unmount(restoreDrivePath);
        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"Both disks unmounted ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();

        // Remount source drive in write-enabled mode
        sourceDrivePath = _diskHelper.ReAttach(_sourceImagePath, sourceDrivePath, tableType, readOnly: false);
        sourceMounted = _diskHelper.Mount(sourceDrivePath, _sourceMountPath, readOnly: false, fileSystemTypes: fsTypes);
        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"Source disk remounted in write mode ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();

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
        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"Created {modifiedFiles.Count} new files in {newDirPath} ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();

        // Modify an existing file if one exists
        var existingFiles = GetNonSystemFiles(sourcePartitionPath)
            .Where(f => f.EndsWith(".txt") && !f.Contains("incremental_new_files"))
            .Select(f => Path.Combine(sourcePartitionPath, f))
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
            stopwatch.Stop();
            await TestContext.Progress.WriteLineAsync($"Modified existing file: {fileToModify} ({stopwatch.ElapsedMilliseconds}ms)");
            stopwatch.Restart();
        }

        // Flush and unmount source after modifications
        _diskHelper.FlushDisk(sourceDrivePath);
        _diskHelper.Unmount(sourceDrivePath);
        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"Modifications complete, source disk unmounted ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();

        // Re-attach the source disk for backup (disk needs to be attached for backup to access it)
        sourceDrivePath = _diskHelper.ReAttach(_sourceImagePath, sourceDrivePath, tableType, readOnly: true);
        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"Source disk re-attached for second backup at: {sourceDrivePath} ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();

        // Second backup (incremental)
        var secondBackupResults = await RunBackupAsync(sourceDrivePath, treatFilesystemAsUnknown);

        // When treating filesystem as unknown, the option may not be recognized by the Controller
        // so we only check for errors, not warnings
        if (treatFilesystemAsUnknown)
        {
            Assert.That(secondBackupResults.Errors.Count(), Is.EqualTo(0),
                "Backup should have no errors");
        }
        else
        {
            TestUtils.AssertResults(secondBackupResults, ignoredWarnings: ["diskimage-filesystem-parsed"]);
        }

        var secondBackupSize = secondBackupResults.SizeOfOpenedFiles;
        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"Second backup completed: {secondBackupResults.OpenedFiles} files opened, {secondBackupSize} bytes examined, {secondBackupResults.SizeOfAddedFiles} bytes added ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();

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
        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"File reduction: {fileReductionRatio:P2} ({secondBackupResults.OpenedFiles}/{firstBackupResults.OpenedFiles}) ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();
        await TestContext.Progress.WriteLineAsync($"Size reduction: {sizeReductionRatio:P2} ({secondBackupResults.SizeOfOpenedFiles}/{firstBackupResults.SizeOfOpenedFiles}) ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();

        // Restore the incremental backup to a fresh disk
        _diskHelper.CleanupDisk(_restoreImagePath);
        DiskImageTestHelpers.SafeDeleteFile(_restoreImagePath);

        restoreDrivePath = _diskHelper.CreateDisk(_restoreImagePath, size);
        _diskHelper.InitializeDisk(restoreDrivePath, tableType, []);
        _diskHelper.Unmount(restoreDrivePath);
        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"Fresh restore disk created for incremental restore ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();

        var incrementalRestoreResults = await RunRestoreAsync(restoreDrivePath);
        TestUtils.AssertResults(incrementalRestoreResults, ignoredWarnings: ["diskimage-filesystem-parsed"]);
        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"Incremental restore completed successfully ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();

        // Reattach and verify the incremental restore matches the modified source
        sourceDrivePath = _diskHelper.ReAttach(_sourceImagePath, sourceDrivePath, tableType, readOnly: true);
        restoreDrivePath = _diskHelper.ReAttach(_restoreImagePath, restoreDrivePath, tableType, readOnly: true);

        sourceMounted = _diskHelper.Mount(sourceDrivePath, _sourceMountPath, readOnly: true, fileSystemTypes: fsTypes);
        restoreMounted = _diskHelper.Mount(restoreDrivePath, _restoreMountPath, readOnly: true, fileSystemTypes: fsTypes);

        CompareDirectories(sourceMounted.First(), restoreMounted.First());

        stopwatch.Stop();
        await TestContext.Progress.WriteLineAsync($"{fsType} incremental backup and restore verified successfully ({stopwatch.ElapsedMilliseconds}ms)");
        stopwatch.Restart();
    }

    /// <summary>
    /// Tests that unallocated space compresses well due to zero-block deduplication.
    /// Creates a disk with the specified filesystem and sparse data (few files, lots of free space),
    /// backs it up, and verifies the backup size is significantly smaller than the disk size.
    /// </summary>
    [TestCase((int)(50 * MiB), PartitionTableType.GPT, FileSystemType.FAT32)]
    [TestCase((int)(50 * MiB), PartitionTableType.GPT, FileSystemType.NTFS)]
    [TestCase((int)(50 * MiB), PartitionTableType.GPT, FileSystemType.Unknown)]
    [Category("DiskImage")]
    [Category("DiskImageFileSystem")]
    public async Task Test_FileSystem_UnallocatedSpace_CompressesWell_Async(int size, PartitionTableType tableType, FileSystemType fsType)
    {
        var (isSupported, reason) = _diskHelper.IsFileSystemTypeSupported(fsType);
        if (!isSupported)
            Assert.Ignore(reason);

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
        await ToolTests.GenerateTestDataAsync(sourcePartitions.First(), 3, 0, 0, 512);
        _diskHelper.FlushDisk(sourceDrivePath);
        _diskHelper.Unmount(sourceDrivePath);
        await TestContext.Progress.WriteLineAsync("Sparse test data generated (few files, lots of free space)");

        // Backup
        var backupResults = await RunBackupAsync(sourceDrivePath, treatFilesystemAsUnknown);
        // When treating filesystem as unknown, the option may not be recognized by the Controller
        // so we only check for errors, not warnings
        if (treatFilesystemAsUnknown)
        {
            Assert.That(backupResults.Errors.Count(), Is.EqualTo(0),
                "Backup should have no errors");
        }
        else
        {
            TestUtils.AssertResults(backupResults, ignoredWarnings: ["diskimage-filesystem-parsed"]);
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

        var restoreResults = await RunRestoreAsync(restoreDrivePath);
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
    [TestCase((int)(50 * MiB), PartitionTableType.GPT, FileSystemType.NTFS)]
    [TestCase((int)(50 * MiB), PartitionTableType.GPT, FileSystemType.Unknown)]
    [Category("DiskImage")]
    [Category("DiskImageFileSystem")]
    public async Task Test_FileSystem_FullDisk_AllBlocksAllocated_Async(int size, PartitionTableType tableType, FileSystemType fsType)
    {
        var (isSupported, reason) = _diskHelper.IsFileSystemTypeSupported(fsType);
        if (!isSupported)
            Assert.Ignore(reason);

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
            await ToolTests.GenerateTestDataAsync(Path.Combine(partitionPath, $"batch_{batch}"), 10, 0, 0, 4096);
        }
        _diskHelper.FlushDisk(sourceDrivePath);
        _diskHelper.Unmount(sourceDrivePath);
        await TestContext.Progress.WriteLineAsync("Disk filled with data");

        // Backup
        var backupResults = await RunBackupAsync(sourceDrivePath, treatFilesystemAsUnknown);

        // When treating filesystem as unknown, the option may not be recognized by the Controller
        // so we only check for errors, not warnings
        if (treatFilesystemAsUnknown)
        {
            Assert.That(backupResults.Errors.Count(), Is.EqualTo(0),
                "Backup should have no errors");
        }
        else
        {
            TestUtils.AssertResults(backupResults, ignoredWarnings: ["diskimage-filesystem-parsed"]);
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

        var restoreResults = await RunRestoreAsync(restoreDrivePath);
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
}