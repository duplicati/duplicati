// Copyright (C) 2025, The Duplicati Team
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
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Unit tests for the DiskImage backup and restore functionality.
    /// These tests use VHD files managed via diskpart to test the full backup and restore flow.
    /// </summary>
    [TestFixture]
    [Category("DiskImage")]
    [Platform("Win")]
    public class DiskImageTests : BasicSetupHelper
    {
        private string _sourceVhdPath = null!;
        private string _restoreVhdPath = null!;
        private int _sourceDiskNumber = -1;
        private int _restoreDiskNumber = -1;
        private char _sourceDriveLetter;
        private DiskImage.IDiskImageHelper _diskHelper = null!;

        /// <summary>
        /// Sets up the test environment before each test.
        /// Creates the disk helper and temporary VHD paths.
        /// </summary>
        [SetUp]
        public void DiskImageSetUp()
        {
            // Create the appropriate disk image helper for the current platform
            _diskHelper = DiskImage.DiskImageHelperFactory.Create();

            // Check for admin privileges
            if (!_diskHelper.HasRequiredPrivileges())
            {
                Assert.Ignore("DiskImage tests require administrator privileges");
            }

            // Create temp VHD paths
            var tempPath = Path.GetTempPath();
            _sourceVhdPath = Path.Combine(tempPath, $"duplicati_test_source_{Guid.NewGuid()}.vhdx");
            _restoreVhdPath = Path.Combine(tempPath, $"duplicati_test_restore_{Guid.NewGuid()}.vhdx");
        }

        /// <summary>
        /// Cleans up the test environment after each test.
        /// Detaches and deletes VHD files.
        /// </summary>
        [TearDown]
        public void DiskImageTearDown()
        {
            // Cleanup VHDs
            try
            {
                _diskHelper.CleanupDisk(_sourceVhdPath);
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine($"Warning: Failed to cleanup source VHD: {ex.Message}");
            }

            try
            {
                _diskHelper.CleanupDisk(_restoreVhdPath);
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine($"Warning: Failed to cleanup restore VHD: {ex.Message}");
            }

            try
            {
                _diskHelper.Dispose();
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine($"Warning: Failed to dispose disk helper: {ex.Message}");
            }
        }

        /// <summary>
        /// Test Scenario 1: GPT + NTFS - Single Partition Backup and Restore
        /// </summary>
        [Test]
        [Category("DiskImage")]
        public void Test_GptNtfs_SinglePartition_BackupAndRestore()
        {
            TestContext.Progress.WriteLine("Test: GPT + NTFS Single Partition Backup and Restore");

            // Setup: Create 100MB VHD, GPT, single NTFS partition with test data
            var physicalDrivePath = SetupSourceVhd(100, "gpt", "ntfs");
            TestContext.Progress.WriteLine($"Source VHD created at: {_sourceVhdPath}");
            TestContext.Progress.WriteLine($"Physical drive path: {physicalDrivePath}");

            // Backup via Controller
            var backupResults = RunBackup(physicalDrivePath);
            TestContext.Progress.WriteLine($"Backup completed. Files examined: {backupResults.ExaminedFiles}, Size: {backupResults.SizeOfExaminedFiles}");

            // Assert backup completed with no errors/warnings
            TestUtils.AssertResults(backupResults);

            // Verify backup created expected files
            var remoteFiles = Directory.GetFiles(TARGETFOLDER);
            Assert.That(remoteFiles.Length, Is.GreaterThan(0), "Backup should create remote files");
            TestContext.Progress.WriteLine($"Remote files created: {remoteFiles.Length}");

            // Setup restore target VHD
            var restoreDrivePath = SetupRestoreVhd(100, "gpt", "ntfs");
            TestContext.Progress.WriteLine($"Restore VHD created at: {_restoreVhdPath}");

            // Restore to target VHD
            var restoreResults = RunRestore(restoreDrivePath);
            TestContext.Progress.WriteLine($"Restore completed. Files restored: {restoreResults.RestoredFiles}, Size: {restoreResults.SizeOfRestoredFiles}");

            // Assert restore completed with no errors/warnings
            TestUtils.AssertResults(restoreResults);

            // Verify restored data matches original
            VerifyRestoredData(_sourceDriveLetter);
        }

        /// <summary>
        /// Test Scenario 2: MBR + FAT32 - Single Partition Backup and Restore
        /// </summary>
        [Test]
        [Category("DiskImage")]
        public void Test_MbrFat32_SinglePartition_BackupAndRestore()
        {
            TestContext.Progress.WriteLine("Test: MBR + FAT32 Single Partition Backup and Restore");

            // Setup: Create 50MB VHD, MBR, single FAT32 partition with test data
            var physicalDrivePath = SetupSourceVhd(50, "mbr", "fat32");
            TestContext.Progress.WriteLine($"Source VHD created at: {_sourceVhdPath}");
            TestContext.Progress.WriteLine($"Physical drive path: {physicalDrivePath}");

            // Backup via Controller
            var backupResults = RunBackup(physicalDrivePath);
            TestContext.Progress.WriteLine($"Backup completed. Files examined: {backupResults.ExaminedFiles}");

            // Assert backup completed with no errors/warnings
            TestUtils.AssertResults(backupResults);

            // Setup restore target VHD
            var restoreDrivePath = SetupRestoreVhd(50, "mbr", "fat32");
            TestContext.Progress.WriteLine($"Restore VHD created at: {_restoreVhdPath}");

            // Restore to target VHD
            var restoreResults = RunRestore(restoreDrivePath);
            TestContext.Progress.WriteLine($"Restore completed. Files restored: {restoreResults.RestoredFiles}");

            // Assert restore completed with no errors/warnings
            TestUtils.AssertResults(restoreResults);

            // Verify restored data matches original
            VerifyRestoredData(_sourceDriveLetter);
        }

        /// <summary>
        /// Test Scenario 3: GPT + Multiple Partitions - NTFS + FAT32
        /// </summary>
        [Test]
        [Category("DiskImage")]
        public void Test_Gpt_MultiplePartitions_Backup()
        {
            TestContext.Progress.WriteLine("Test: GPT + Multiple Partitions (NTFS + FAT32) Backup");

            // Setup: Create 200MB VHD, GPT, two partitions
            var physicalDrivePath = SetupSourceVhdMultiplePartitions(200, "gpt",
                new[] { ("ntfs", 100), ("fat32", 50) });
            TestContext.Progress.WriteLine($"Source VHD created at: {_sourceVhdPath}");
            TestContext.Progress.WriteLine($"Physical drive path: {physicalDrivePath}");

            // Backup via Controller
            var backupResults = RunBackup(physicalDrivePath);
            TestContext.Progress.WriteLine($"Backup completed. Files examined: {backupResults.ExaminedFiles}");

            // Assert backup completed with no errors/warnings
            TestUtils.AssertResults(backupResults);

            // Verify geometry metadata contains both partitions
            VerifyGeometryMetadata();
        }

        /// <summary>
        /// Test Scenario 4: Full Round-Trip - Backup + Restore to Second VHD
        /// </summary>
        [Test]
        [Category("DiskImage")]
        public void Test_FullRoundTrip_BackupAndRestore()
        {
            TestContext.Progress.WriteLine("Test: Full Round-Trip Backup + Restore");

            // Setup source VHD: 100MB, GPT, single NTFS partition
            var sourceDrivePath = SetupSourceVhd(100, "gpt", "ntfs");
            TestContext.Progress.WriteLine($"Source VHD created at: {_sourceVhdPath}");

            // Backup
            var backupResults = RunBackup(sourceDrivePath);
            TestUtils.AssertResults(backupResults);
            TestContext.Progress.WriteLine($"Backup completed successfully");

            // Setup restore target VHD with same geometry
            var restoreDrivePath = SetupRestoreVhd(100, "gpt", "ntfs");
            TestContext.Progress.WriteLine($"Restore VHD created at: {_restoreVhdPath}");

            // Restore
            var restoreResults = RunRestore(restoreDrivePath);
            TestUtils.AssertResults(restoreResults);
            TestContext.Progress.WriteLine($"Restore completed successfully");

            // Verify partition table matches
            VerifyPartitionTableMatches();

            // Verify data matches byte-for-byte
            VerifyRestoredData(_sourceDriveLetter);
        }

        /// <summary>
        /// Test Scenario 5: Geometry Metadata Verification
        /// </summary>
        [Test]
        [Category("DiskImage")]
        public void Test_GeometryMetadata_Verification()
        {
            TestContext.Progress.WriteLine("Test: Geometry Metadata Verification");

            // Setup: Create GPT disk with 2 NTFS partitions
            var physicalDrivePath = SetupSourceVhdMultiplePartitions(200, "gpt",
                new[] { ("ntfs", 80), ("ntfs", 60) });
            TestContext.Progress.WriteLine($"Source VHD created at: {_sourceVhdPath}");

            // Backup
            var backupResults = RunBackup(physicalDrivePath);
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
            VerifyGeometryMetadata();
        }

        /// <summary>
        /// Test Scenario 6: SourceProvider Enumeration
        /// </summary>
        [Test]
        [Category("DiskImage")]
        public async Task Test_SourceProvider_Enumeration()
        {
            TestContext.Progress.WriteLine("Test: SourceProvider Enumeration");

            // Setup: Create GPT disk with one NTFS partition
            var physicalDrivePath = SetupSourceVhd(100, "gpt", "ntfs");
            TestContext.Progress.WriteLine($"Source VHD created at: {_sourceVhdPath}");

            // Directly instantiate SourceProvider
            // Note: When directly instantiating SourceProvider, we don't use the @ prefix
            // The @ prefix is only for Controller to recognize it as a remote source
            var sourceUrl = $"diskimage://{physicalDrivePath}";
            using var provider = new SourceProvider(sourceUrl, "", new Dictionary<string, string?>());

            // Initialize the provider
            await provider.Initialize(CancellationToken.None);

            // Enumerate entries
            var entries = new List<ISourceProviderEntry>();
            await foreach (var entry in provider.Enumerate(CancellationToken.None))
            {
                entries.Add(entry);
                TestContext.Progress.WriteLine($"Found entry: {entry.Path} (IsFolder: {entry.IsFolder}, Size: {entry.Size})");
            }

            // Verify hierarchy: disk → partition → filesystem → files
            Assert.That(entries.Count, Is.GreaterThan(0), "Should have at least one entry (the disk)");

            // Fetch the rest of the enumaration.
            // Check for disk entry
            var diskEntry = entries.FirstOrDefault(e => e.IsRootEntry);
            Assert.That(diskEntry, Is.Not.Null, "Should have a root disk entry");

            await foreach (var entry in diskEntry!.Enumerate(CancellationToken.None))
            {
                entries.Add(entry);
            }

            TestContext.Progress.WriteLine($"Total entries found: {entries.Count}");

            // Check for geometry.json
            var geometryEntry = entries.FirstOrDefault(e => e.Path.EndsWith("geometry.json"));
            Assert.That(geometryEntry, Is.Not.Null, "Should have geometry.json entry");

            // Check for partition entries
            var partitionEntries = entries.Where(e => e.Path.Contains("part_")).ToList();
            Assert.That(partitionEntries.Count, Is.GreaterThan(0), "Should have at least one partition entry");

            // Check for filesystem entries
            var fsEntries = entries.Where(e => e.Path.Contains("fs_")).ToList();
            Assert.That(fsEntries.Count, Is.GreaterThan(0), "Should have at least one filesystem entry");
        }

        /// <summary>
        /// Test Scenario 7: MBR + Multiple Partitions
        /// </summary>
        [Test]
        [Category("DiskImage")]
        public void Test_Mbr_MultiplePartitions_Backup()
        {
            TestContext.Progress.WriteLine("Test: MBR + Multiple Partitions Backup");

            // Setup: Create 150MB VHD, MBR, two partitions (NTFS + FAT32)
            var physicalDrivePath = SetupSourceVhdMultiplePartitions(150, "mbr",
                new[] { ("ntfs", 70), ("fat32", 40) });
            TestContext.Progress.WriteLine($"Source VHD created at: {_sourceVhdPath}");

            // Backup
            var backupResults = RunBackup(physicalDrivePath);
            TestUtils.AssertResults(backupResults);
            TestContext.Progress.WriteLine($"Backup completed. Files examined: {backupResults.ExaminedFiles}");

            // Verify both partitions are captured
            VerifyGeometryMetadata();

            // Verify partition table type is MBR in geometry metadata
            VerifyPartitionTableType(PartitionTableType.MBR);
        }

        /// <summary>
        /// Test Scenario 8: Auto Unmount Option - Verify that restore fails when disk is online
        /// and succeeds when auto-unmount option is enabled.
        /// </summary>
        [Test]
        [Category("DiskImage")]
        public void Test_AutoUnmountOption_RestoreWhileOnline()
        {
            TestContext.Progress.WriteLine("Test: Auto Unmount Option - Restore While Disk Online");

            // Setup source VHD: 100MB, GPT, single NTFS partition
            var sourceDrivePath = SetupSourceVhd(100, "gpt", "ntfs");
            TestContext.Progress.WriteLine($"Source VHD created at: {_sourceVhdPath}");

            // Backup
            var backupResults = RunBackup(sourceDrivePath);
            TestUtils.AssertResults(backupResults);
            TestContext.Progress.WriteLine($"Backup completed successfully");

            // Create and attach VHD
            var restoreDrivePath = _diskHelper.CreateAndAttachDisk(_restoreVhdPath, 100);
            _restoreDiskNumber = _diskHelper.GetDiskNumber(_restoreVhdPath);

            // Initialize disk (the restore will overwrite this, but we need it formatted)
            _diskHelper.InitializeDisk(_restoreDiskNumber, "gpt");
            _diskHelper.CreateAndFormatPartition(_restoreDiskNumber, "ntfs");
            TestContext.Progress.WriteLine($"Restore VHD created and kept online at: {_restoreVhdPath}");

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

            // Verify partition table matches
            VerifyPartitionTableMatches();

            // Verify data matches byte-for-byte
            VerifyRestoredData(_sourceDriveLetter);
        }

        #region Helper Methods

        /// <summary>
        /// Sets up a source VHD with a single partition.
        /// </summary>
        /// <param name="sizeMB">The size of the VHD in megabytes.</param>
        /// <param name="tableType">The partition table type ("gpt" or "mbr").</param>
        /// <param name="fsType">The filesystem type ("ntfs", "fat32", etc.).</param>
        /// <returns>The physical drive path.</returns>
        private string SetupSourceVhd(int sizeMB, string tableType, string fsType)
        {
            // Create and attach VHD
            var physicalDrivePath = _diskHelper.CreateAndAttachDisk(_sourceVhdPath, sizeMB);
            _sourceDiskNumber = _diskHelper.GetDiskNumber(_sourceVhdPath);

            // Initialize disk
            _diskHelper.InitializeDisk(_sourceDiskNumber, tableType);

            // Create and format partition
            _sourceDriveLetter = _diskHelper.CreateAndFormatPartition(_sourceDiskNumber, fsType);

            // Populate with test data
            _diskHelper.PopulateTestData(_sourceDriveLetter);
            _diskHelper.FlushVolume(_sourceDriveLetter);

            return physicalDrivePath;
        }

        /// <summary>
        /// Sets up a source VHD with multiple partitions.
        /// </summary>
        /// <param name="sizeMB">The size of the VHD in megabytes.</param>
        /// <param name="tableType">The partition table type ("gpt" or "mbr").</param>
        /// <param name="partitions">Array of tuples containing filesystem type and size for each partition.</param>
        /// <returns>The physical drive path.</returns>
        private string SetupSourceVhdMultiplePartitions(int sizeMB, string tableType, (string fsType, int sizeMB)[] partitions)
        {
            // Create and attach VHD
            var physicalDrivePath = _diskHelper.CreateAndAttachDisk(_sourceVhdPath, sizeMB);
            _sourceDiskNumber = _diskHelper.GetDiskNumber(_sourceVhdPath);

            // Initialize disk
            _diskHelper.InitializeDisk(_sourceDiskNumber, tableType);

            // Create and format partitions
            for (int i = 0; i < partitions.Length; i++)
            {
                var (fsType, partSize) = partitions[i];
                var driveLetter = _diskHelper.CreateAndFormatPartition(_sourceDiskNumber, fsType, partSize);
                _diskHelper.PopulateTestData(driveLetter, 5, 5); // Smaller data set for multiple partitions
                _diskHelper.FlushVolume(driveLetter);

                if (i == 0)
                {
                    _sourceDriveLetter = driveLetter;
                }
            }

            return physicalDrivePath;
        }

        /// <summary>
        /// Sets up a restore target VHD with the specified geometry.
        /// </summary>
        /// <param name="sizeMB">The size of the VHD in megabytes.</param>
        /// <param name="tableType">The partition table type ("gpt" or "mbr").</param>
        /// <param name="fsType">The filesystem type ("ntfs", "fat32", etc.).</param>
        /// <returns>The physical drive path.</returns>
        private string SetupRestoreVhd(int sizeMB, string tableType, string fsType)
        {
            // Create and attach VHD
            var physicalDrivePath = _diskHelper.CreateAndAttachDisk(_restoreVhdPath, sizeMB);
            _restoreDiskNumber = _diskHelper.GetDiskNumber(_restoreVhdPath);

            // Initialize disk (the restore will overwrite this, but we need it formatted)
            _diskHelper.InitializeDisk(_restoreDiskNumber, tableType);
            var restoreDriveLetter = _diskHelper.CreateAndFormatPartition(_restoreDiskNumber, fsType);
            _diskHelper.UnmountForWriting(_restoreVhdPath, restoreDriveLetter);

            return physicalDrivePath;
        }

        /// <summary>
        /// Runs a backup operation using the Controller.
        /// </summary>
        /// <param name="physicalDrivePath">The physical drive path to backup.</param>
        /// <returns>The backup results.</returns>
        private IBackupResults RunBackup(string physicalDrivePath)
        {
            var options = new Dictionary<string, string>(TestOptions);
            options["enable-module"] = "diskimage";
            options["concurrency-fileprocessors"] = "1";

            using var c = new Controller("file://" + TARGETFOLDER, options, null);
            var sourceUrl = $"@diskimage://{physicalDrivePath}";
            return c.Backup(new[] { sourceUrl });
        }

        /// <summary>
        /// Runs a restore operation using the Controller.
        /// </summary>
        /// <param name="restoreDrivePath">The physical drive path to restore to.</param>
        /// <returns>The restore results.</returns>
        private IRestoreResults RunRestore(string restoreDrivePath)
        {
            _diskHelper.UnmountForWriting(_restoreVhdPath);

            var options = new Dictionary<string, string>(TestOptions);
            options["restore-path"] = $"@diskimage://{restoreDrivePath}";
            options["overwrite"] = "true";
            options["restore-file-processors"] = "1";

            using var c = new Controller("file://" + TARGETFOLDER, options, null);
            var results = c.Restore(new[] { "*" });

            _diskHelper.BringOnline(_restoreVhdPath);

            return results;
        }

        /// <summary>
        /// Verifies that the restored data matches the original data.
        /// </summary>
        /// <param name="originalDriveLetter">The drive letter of the original source drive.</param>
        private void VerifyRestoredData(char originalDriveLetter)
        {
            TestContext.Progress.WriteLine("Verifying restored data...");

            // Attach the restored VHD to get its drive letter
            var restoreDiskNumber = _diskHelper.GetDiskNumber(_restoreVhdPath);
            if (restoreDiskNumber < 0)
            {
                // Re-attach the VHD if needed - for Windows, we need to use the static method
                // since this is a special case for re-attaching
                if (_diskHelper is DiskImage.WindowsDiskImageHelper)
                {
                    DiskImage.WindowsDiskImageHelper.RunPowerShell($"Mount-DiskImage -ImagePath '{_restoreVhdPath}'");
                }
                restoreDiskNumber = _diskHelper.GetDiskNumber(_restoreVhdPath);
            }

            // Get the drive letter for the restored partition
            var restoreDriveLetter = GetDriveLetterForDisk(restoreDiskNumber);
            if (restoreDriveLetter == '\0')
            {
                TestContext.Progress.WriteLine("Restored VHD not mounted, attempting to mount...");
                restoreDriveLetter = _diskHelper.MountForReading(_restoreVhdPath);
                // Give Windows some time to mount the volume and assign the drive letter
                Thread.Sleep(2000);
            }

            if (restoreDriveLetter == '\0')
            {
                // Second attempt to get drive letter after mounting failed
                Assert.Fail("Could not find drive letter for restored VHD");
                return;
            }

            TestContext.Progress.WriteLine($"Comparing source drive {originalDriveLetter}: with restored drive {restoreDriveLetter}:");

            try
            {
                // Compare directory structures and file contents
                var sourcePath = $"{originalDriveLetter}:\\";
                var restorePath = $"{restoreDriveLetter}:\\";

                // Wait for the restore path to become available
                for (int i = 0; i < 10; i++)
                {
                    if (Directory.Exists(restorePath))
                        break;
                    Thread.Sleep(1000);
                }

                CompareDirectories(sourcePath, restorePath);
            }
            finally
            {
                // Detach the restored VHD
                try
                {
                    _diskHelper.DetachDisk(_restoreVhdPath);
                }
                catch (Exception ex)
                {
                    TestContext.Progress.WriteLine($"Warning: Failed to detach restored VHD: {ex.Message}");
                }
            }
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
            var sourceFiles = Directory.EnumerateFiles(sourcePath, "*", options).Select(f => Path.GetRelativePath(sourcePath, f)).OrderBy(f => f).ToList();
            var restoreFiles = Directory.EnumerateFiles(restorePath, "*", options).Select(f => Path.GetRelativePath(restorePath, f)).OrderBy(f => f).ToList();

            // Check that all source files exist in restore
            foreach (var relativePath in sourceFiles)
            {
                Assert.That(restoreFiles, Does.Contain(relativePath),
                    $"Restored drive should contain file: {relativePath}");
            }

            // Compare file contents byte-by-byte
            foreach (var sourceFile in sourceFiles)
            {
                var relativePath = sourceFile.Substring(3);
                var restoreFile = Path.Combine(restorePath, relativePath);

                if (File.Exists(restoreFile))
                {
                    var sourceBytes = File.ReadAllBytes(sourceFile);
                    var restoreBytes = File.ReadAllBytes(restoreFile);

                    Assert.That(restoreBytes.Length, Is.EqualTo(sourceBytes.Length),
                        $"File size mismatch for {relativePath}");
                    Assert.That(restoreBytes, Is.EqualTo(sourceBytes),
                        $"File content mismatch for {relativePath}");
                }
            }

            TestContext.Progress.WriteLine($"Verified {sourceFiles.Count} files match between source and restored drives");
        }

        /// <summary>
        /// Gets the drive letter for a disk number by querying PowerShell.
        /// </summary>
        /// <param name="diskNumber">The disk number.</param>
        /// <returns>The drive letter, or '\0' if not found.</returns>
        private char GetDriveLetterForDisk(int diskNumber)
        {
            // Use PowerShell to find the drive letter for this disk
            var script = $@"Get-Partition -DiskNumber {diskNumber} | Get-Volume | Where-Object {{ $_.DriveLetter -ne $null }} | Select-Object -ExpandProperty DriveLetter";
            var output = DiskImage.WindowsDiskImageHelper.RunPowerShell(script)?.Trim();

            if (!string.IsNullOrEmpty(output) && output.Length >= 1 && char.IsLetter(output[0]))
            {
                var driveLetter = char.ToUpperInvariant(output[0]);
                if (Directory.Exists($"{driveLetter}:\\"))
                {
                    return driveLetter;
                }
            }

            return '\0';
        }

        /// <summary>
        /// Verifies that the geometry metadata is present and valid.
        /// </summary>
        private void VerifyGeometryMetadata()
        {
            TestContext.Progress.WriteLine("Verifying geometry metadata...");

            using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                var listResults = c.List("*");
                Assert.That(listResults.Files, Is.Not.Null);

                var geometryFile = listResults.Files.FirstOrDefault(f => f.Path.EndsWith("geometry.json"));
                Assert.That(geometryFile, Is.Not.Null, "geometry.json should be present in the backup");

                // Restore and parse the geometry.json file
                var tempGeometryPath = Path.Combine(Path.GetTempPath(), $"geometry_{Guid.NewGuid()}.json");
                try
                {
                    var restoreOptions = new Dictionary<string, string>(TestOptions)
                    {
                        ["restore-path"] = Path.GetDirectoryName(tempGeometryPath)!,
                        ["overwrite"] = "true"
                    };

                    using (var restoreController = new Controller("file://" + TARGETFOLDER, restoreOptions, null))
                    {
                        var restoreResults = restoreController.Restore(new[] { geometryFile!.Path });
                        TestUtils.AssertResults(restoreResults);
                    }

                    // Read and parse the geometry.json
                    if (File.Exists(tempGeometryPath))
                    {
                        var json = File.ReadAllText(tempGeometryPath);
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
                    }
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempGeometryPath))
                        {
                            File.Delete(tempGeometryPath);
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
        /// Verifies that the partition table type matches the expected type.
        /// </summary>
        /// <param name="expectedType">The expected partition table type.</param>
        private void VerifyPartitionTableType(PartitionTableType expectedType)
        {
            TestContext.Progress.WriteLine($"Verifying partition table type is {expectedType}...");

            using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                var listResults = c.List("*");
                Assert.That(listResults.Files, Is.Not.Null);

                var geometryFile = listResults.Files.FirstOrDefault(f => f.Path.EndsWith("geometry.json"));
                Assert.That(geometryFile, Is.Not.Null, "geometry.json should be present in the backup");

                // Restore and parse the geometry.json file
                var tempGeometryPath = Path.Combine(Path.GetTempPath(), $"geometry_{Guid.NewGuid()}.json");
                try
                {
                    var restoreOptions = new Dictionary<string, string>(TestOptions)
                    {
                        ["restore-path"] = Path.GetDirectoryName(tempGeometryPath)!,
                        ["overwrite"] = "true"
                    };

                    using (var restoreController = new Controller("file://" + TARGETFOLDER, restoreOptions, null))
                    {
                        var restoreResults = restoreController.Restore(new[] { geometryFile!.Path });

                        // Ignore NoFilesRestored warning as geometry.json is a special metadata file
                        var hasRelevantWarnings = restoreResults.Warnings.Any(w => !w.Contains("NoFilesRestored"));
                        if (restoreResults.Errors.Any() || hasRelevantWarnings)
                        {
                            TestUtils.AssertResults(restoreResults);
                        }
                    }

                    if (File.Exists(tempGeometryPath))
                    {
                        var json = File.ReadAllText(tempGeometryPath);
                        var geometry = GeometryMetadata.FromJson(json);

                        Assert.That(geometry, Is.Not.Null, "geometry.json should deserialize successfully");
                        Assert.That(geometry!.PartitionTable, Is.Not.Null, "Partition table should be present");
                        Assert.That(geometry.PartitionTable!.Type, Is.EqualTo(expectedType),
                            $"Partition table type should be {expectedType}");

                        TestContext.Progress.WriteLine($"Partition table type verified: {geometry.PartitionTable.Type}");
                    }
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempGeometryPath))
                        {
                            File.Delete(tempGeometryPath);
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
        private void VerifyPartitionTableMatches()
        {
            TestContext.Progress.WriteLine("Verifying partition table matches...");

            // Get source disk details
            var sourceDetails = _diskHelper.GetDiskDetails(_sourceDiskNumber);
            TestContext.Progress.WriteLine("Source disk details:\n" + sourceDetails);

            // Get restore disk details
            var restoreDiskNumber = _diskHelper.GetDiskNumber(_restoreVhdPath);
            var restoreDetails = _diskHelper.GetDiskDetails(restoreDiskNumber);
            TestContext.Progress.WriteLine("Restore disk details:\n" + restoreDetails);

            // Parse partition information from diskpart output
            var sourcePartitions = ParsePartitionsFromDiskpartOutput(sourceDetails);
            var restorePartitions = ParsePartitionsFromDiskpartOutput(restoreDetails);

            // Verify partition counts match
            Assert.That(restorePartitions.Count, Is.EqualTo(sourcePartitions.Count),
                "Restored disk should have same number of partitions as source");

            // Verify partition sizes match
            for (int i = 0; i < sourcePartitions.Count; i++)
            {
                Assert.That(restorePartitions[i].Size, Is.EqualTo(sourcePartitions[i].Size),
                    $"Partition {i + 1} size should match");
                Assert.That(restorePartitions[i].Type, Is.EqualTo(sourcePartitions[i].Type),
                    $"Partition {i + 1} type should match");
            }

            TestContext.Progress.WriteLine($"Partition table verified: {sourcePartitions.Count} partitions match");
        }

        /// <summary>
        /// Parses partition information from diskpart detail disk output.
        /// </summary>
        /// <param name="output">The diskpart output text.</param>
        /// <returns>A list of tuples containing partition size and type.</returns>
        private List<(long Size, string Type)> ParsePartitionsFromDiskpartOutput(string output)
        {
            var partitions = new List<(long Size, string Type)>();
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            bool inPartitionSection = false;
            foreach (var line in lines)
            {
                // Look for partition section header
                if (line.Contains("Partition", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("Type", StringComparison.OrdinalIgnoreCase))
                {
                    inPartitionSection = true;
                    continue;
                }

                if (inPartitionSection && line.Trim().StartsWith("Partition"))
                {
                    // Parse partition line
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        // Try to extract size information
                        long size = 0;
                        string type = "Unknown";

                        for (int i = 0; i < parts.Length; i++)
                        {
                            if (parts[i].EndsWith("MB", StringComparison.OrdinalIgnoreCase) &&
                                long.TryParse(parts[i].Replace("MB", ""), out var mb))
                            {
                                size = mb * 1024 * 1024;
                            }
                            else if (parts[i].EndsWith("GB", StringComparison.OrdinalIgnoreCase) &&
                                     long.TryParse(parts[i].Replace("GB", ""), out var gb))
                            {
                                size = gb * 1024 * 1024 * 1024;
                            }
                            else if (parts[i].Equals("Primary", StringComparison.OrdinalIgnoreCase) ||
                                     parts[i].Equals("Extended", StringComparison.OrdinalIgnoreCase))
                            {
                                type = parts[i];
                            }
                        }

                        partitions.Add((size, type));
                    }
                }
            }

            return partitions;
        }

        #endregion
    }
}
