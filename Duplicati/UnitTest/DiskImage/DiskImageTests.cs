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
    /// These tests use disk image files managed to test the full backup and restore flow.
    /// </summary>
    [TestFixture]
    [Category("DiskImage")]
    [Platform("Win,MacOsX")]
    public class DiskImageTests : BasicSetupHelper
    {
        private string _sourceImagePath = null!;
        private string _restoreImagePath = null!;
        private string _sourceMountPath = null!;
        private string _restoreMountPath = null!;
        private DiskImage.IDiskImageHelper _diskHelper = null!;

        private const long MiB = 1024 * 1024;

        /// <summary>
        /// Sets up the test environment before each test.
        /// Creates the disk helper and temporary disk image paths.
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

            // Create temp disk image paths
            var extension = OperatingSystem.IsWindows() ? "vhdx" : "dmg";
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

            Directory.Delete(_sourceMountPath, true);
            Directory.Delete(_restoreMountPath, true);
        }

        #region GPT Single Partition

        [Test, Category("DiskImage")]
        public Task Test_GPT_FAT32() =>
            FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [(FileSystemType.FAT32, 0)]);

        [Test, Category("DiskImage")]
        public Task Test_GPT_NTFS()
        {
            if (!OperatingSystem.IsWindows())
                Assert.Ignore("Test_GPT_NTFS is only supported on Windows.");
            return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [(FileSystemType.NTFS, 0)]);
        }

        [Test, Category("DiskImage")]
        public Task Test_GPT_APFS()
        {
            if (!OperatingSystem.IsMacOS())
                Assert.Ignore("APFS is only supported on macOS.");
            return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [(FileSystemType.APFS, 0)]);
        }

        [Test, Category("DiskImage")]
        public Task Test_GPT_HFSPlus()
        {
            if (!OperatingSystem.IsMacOS())
                Assert.Ignore("HFSPlus is only supported on macOS.");
            return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [(FileSystemType.HFSPlus, 0)]);
        }

        [Test, Category("DiskImage")]
        public Task Test_GPT_ExFAT() =>
            FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [(FileSystemType.ExFAT, 0)]);

        #endregion

        #region MBR Single Partition

        // APFS is only supported on GPT partition tables.

        [Test, Category("DiskImage")]
        public Task Test_MBR_FAT32() =>
            FullRoundTrip((int)(100 * MiB), PartitionTableType.MBR, [(FileSystemType.FAT32, 0)]);


        [Test, Category("DiskImage")]
        public Task Test_MBR_HFSPlus()
        {
            if (!OperatingSystem.IsMacOS())
                Assert.Ignore("HFSPlus is only supported on macOS.");
            return FullRoundTrip((int)(100 * MiB), PartitionTableType.MBR, [(FileSystemType.HFSPlus, 0)]);
        }

        [Test, Category("DiskImage")]
        public Task Test_MBR_ExFAT() =>
            FullRoundTrip((int)(100 * MiB), PartitionTableType.MBR, [(FileSystemType.ExFAT, 0)]);

        [Test, Category("DiskImage")]
        public Task Test_MBR_NTFS()
        {
            if (!OperatingSystem.IsWindows())
                Assert.Ignore("Test_MBR_NTFS is only supported on Windows.");
            return FullRoundTrip((int)(100 * MiB), PartitionTableType.MBR, [(FileSystemType.NTFS, 0)]);
        }

        #endregion

        #region Unknown Partition Table

        [Test, Category("DiskImage")]
        public Task Test_Unknown_NoPartitions() =>
            FullRoundTrip((int)(100 * MiB), PartitionTableType.Unknown, []);

        #endregion

        #region GPT Two Partitions

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

        [Test, Category("DiskImage")]
        public Task Test_GPT_HFSPlus_HFSPlus()
        {
            if (!OperatingSystem.IsMacOS())
                Assert.Ignore("HFSPlus is only supported on macOS.");
            return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
                (FileSystemType.HFSPlus, 50 * MiB),
                (FileSystemType.HFSPlus, 0)
            ]);
        }

        [Test, Category("DiskImage")]
        public Task Test_GPT_APFS_HFSPlus()
        {
            if (!OperatingSystem.IsMacOS())
                Assert.Ignore("APFS and HFSPlus are only supported on macOS.");
            return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
                (FileSystemType.APFS, 50 * MiB),
                (FileSystemType.HFSPlus, 0)
            ]);
        }

        [Test, Category("DiskImage")]
        public Task Test_GPT_FAT32_APFS()
        {
            if (!OperatingSystem.IsMacOS())
                Assert.Ignore("APFS is only supported on macOS.");
            return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
                (FileSystemType.FAT32, 50 * MiB),
                (FileSystemType.APFS, 0)
            ]);
        }

        [Test, Category("DiskImage")]
        public Task Test_GPT_ExFAT_HFSPlus()
        {
            if (!OperatingSystem.IsMacOS())
                Assert.Ignore("HFSPlus is only supported on macOS.");
            return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
                (FileSystemType.ExFAT, 50 * MiB),
                (FileSystemType.HFSPlus, 0)
            ]);
        }

        [Test, Category("DiskImage")]
        public Task Test_GPT_FAT32_ExFAT() =>
            FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
                (FileSystemType.FAT32, 50 * MiB),
                (FileSystemType.ExFAT, 0)
            ]);

        [Test, Category("DiskImage")]
        public Task Test_GPT_NTFS_FAT32()
        {
            if (!OperatingSystem.IsWindows())
                Assert.Ignore("This test is only supported on Windows.");
            return FullRoundTrip((int)(100 * MiB), PartitionTableType.GPT, [
                (FileSystemType.NTFS, 50 * MiB),
                (FileSystemType.FAT32, 0)
            ]);
        }

        #endregion

        #region MBR Two Partitions

        // APFS is only supported on GPT partition tables.

        [Test, Category("DiskImage")]
        public Task Test_MBR_HFSPlus_HFSPlus()
        {
            if (!OperatingSystem.IsMacOS())
                Assert.Ignore("HFSPlus is only supported on macOS.");
            return FullRoundTrip((int)(100 * MiB), PartitionTableType.MBR, [
                (FileSystemType.HFSPlus, 50 * MiB),
                (FileSystemType.HFSPlus, 0)
            ]);
        }


        [Test, Category("DiskImage")]
        public Task Test_MBR_FAT32_ExFAT()
        {
            return FullRoundTrip((int)(100 * MiB), PartitionTableType.MBR, [
                (FileSystemType.FAT32, 50 * MiB),
                (FileSystemType.ExFAT, 0)
            ]);
        }

        [Test, Category("DiskImage")]
        public Task Test_MBR_FAT32_HFSPlus()
        {
            if (!OperatingSystem.IsMacOS())
                Assert.Ignore("HFSPlus is only supported on macOS.");
            return FullRoundTrip((int)(100 * MiB), PartitionTableType.MBR, [
                (FileSystemType.FAT32, 50 * MiB),
                (FileSystemType.HFSPlus, 0)
            ]);
        }

        [Test, Category("DiskImage")]
        public Task Test_MBR_NTFS_FAT32()
        {
            if (!OperatingSystem.IsWindows())
                Assert.Ignore("This test is only supported on Windows.");
            return FullRoundTrip((int)(100 * MiB), PartitionTableType.MBR, [
                (FileSystemType.NTFS, 50 * MiB),
                (FileSystemType.FAT32, 0)
            ]);
        }

        #endregion

        #region GPT Three Partitions

        [Test, Category("DiskImage")]
        public Task Test_GPT_Three_APFS()
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
        public Task Test_GPT_Three_HFSPlus()
        {
            if (!OperatingSystem.IsMacOS())
                Assert.Ignore("HFSPlus is only supported on macOS.");
            return FullRoundTrip((int)(150 * MiB), PartitionTableType.GPT, [
                (FileSystemType.HFSPlus, 50 * MiB),
                (FileSystemType.HFSPlus, 50 * MiB),
                (FileSystemType.HFSPlus, 0)
            ]);
        }

        [Test, Category("DiskImage")]
        public Task Test_GPT_Three_Mixed_Mac()
        {
            if (!OperatingSystem.IsMacOS())
                Assert.Ignore("APFS and HFSPlus are only supported on macOS.");
            return FullRoundTrip((int)(150 * MiB), PartitionTableType.GPT, [
                (FileSystemType.APFS, 50 * MiB),
                (FileSystemType.HFSPlus, 50 * MiB),
                (FileSystemType.APFS, 0)
            ]);
        }

        [Test, Category("DiskImage")]
        public Task Test_GPT_Three_Mixed_Diverse()
        {
            if (!OperatingSystem.IsMacOS())
                Assert.Ignore("APFS and HFSPlus are only supported on macOS.");
            return FullRoundTrip((int)(150 * MiB), PartitionTableType.GPT, [
                (FileSystemType.FAT32, 50 * MiB),
                (FileSystemType.APFS, 50 * MiB),
                (FileSystemType.HFSPlus, 0)
            ]);
        }

        [Test, Category("DiskImage")]
        public Task Test_GPT_Three_Mixed_ExFAT()
        {
            if (!OperatingSystem.IsMacOS())
                Assert.Ignore("APFS and HFSPlus are only supported on macOS.");
            return FullRoundTrip((int)(150 * MiB), PartitionTableType.GPT, [
                (FileSystemType.ExFAT, 50 * MiB),
                (FileSystemType.APFS, 50 * MiB),
                (FileSystemType.HFSPlus, 0)
            ]);
        }

        #endregion

        #region MBR Three Partitions

        // APFS is only supported on GPT partition tables.

        [Test, Category("DiskImage")]
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

        [Test, Category("DiskImage")]
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

        [Test, Category("DiskImage")]
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
            await TestContext.Progress.WriteLineAsync($"Restore disk image created at: {_restoreImagePath}");

            // Restore
            var restoreResults = RunRestore(restoreDrivePath);
            TestUtils.AssertResults(restoreResults);
            await TestContext.Progress.WriteLineAsync($"Restore completed successfully");

            // Reattach drives in readonly
            sourceDrivePath = _diskHelper.ReAttach(_sourceImagePath, sourceDrivePath, tableType, readOnly: true);
            restoreDrivePath = _diskHelper.ReAttach(_restoreImagePath, restoreDrivePath, tableType, readOnly: true);
            await TestContext.Progress.WriteLineAsync($"Source and restore disks re-attached as read-only for verification");

            // Verify partition table matches
            VerifyPartitionTableMatches(sourceDrivePath, restoreDrivePath);
            sourcePartitions = _diskHelper.Mount(sourceDrivePath, _sourceMountPath, readOnly: true);
            var restorePartitions = _diskHelper.Mount(restoreDrivePath, _restoreMountPath, readOnly: true);
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

            var sourcePartitions = _diskHelper.InitializeDisk(sourceDrivePath, PartitionTableType.GPT, [(FileSystemType.FAT32, 50 * MiB)]);
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
            VerifyGeometryMetadata(TARGETFOLDER, TestOptions);
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

            // Verify partition table matches
            VerifyPartitionTableMatches(sourceDrivePath, restoreDrivePath);

            // Verify data matches byte-for-byte
            sourcePartitions = _diskHelper.Mount(sourceDrivePath, _sourceMountPath, readOnly: true);
            var restorePartitions = _diskHelper.Mount(restoreDrivePath, _restoreMountPath, readOnly: true);
            var sourcePartition = sourcePartitions.First();
            var restorePartition = restorePartitions.First();
            CompareDirectories(sourcePartition, restorePartition);
        }

        #region Helper Methods

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
        private void VerifyGeometryMetadata(string target, Dictionary<string, string> options)
        {
            // TODO make it check the expected geometry as well.
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
    }
}
