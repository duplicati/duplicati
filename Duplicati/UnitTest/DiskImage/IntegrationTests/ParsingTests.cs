using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Proprietary.DiskImage;
using Duplicati.Proprietary.DiskImage.General;
using NUnit.Framework;

namespace Duplicati.UnitTest.DiskImage.IntegrationTests;

#nullable enable

public class ParsingTests : DiskImageTests
{
    [Test]
    [Category("DiskImage")]
    public async Task Test_GeometryMetadata_Verification_Async()
    {
        await TestContext.Progress.WriteLineAsync("Test: Full Round-Trip Backup + Restore");

        var sourceDrivePath = _diskHelper.CreateDisk(_sourceImagePath, 100 * MiB);
        await TestContext.Progress.WriteLineAsync($"Source Disk created at: {_sourceImagePath}");

        (FileSystemType, long)[] partitions = [(FileSystemType.FAT32, 50 * MiB), (FileSystemType.FAT32, 0)];
        var sourcePartitions = _diskHelper.InitializeDisk(sourceDrivePath, PartitionTableType.GPT, partitions);
        _diskHelper.Unmount(sourceDrivePath);
        await TestContext.Progress.WriteLineAsync($"Source Disk initialized with partition(s): {string.Join(", ", sourcePartitions)}");

        // Backup
        var backupResults = await RunBackupAsync(sourceDrivePath);
        TestUtils.AssertResults(backupResults, ignoredWarnings: ["diskimage-filesystem-parsed"]);

        // List backup contents and verify geometry.json is present
        using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
        {
            var listResults = await c.ListAsync("*");
            TestUtils.AssertResults(listResults);

            // Check that geometry.json is in the backup
            var files = listResults.Files;
            Assert.That(files, Is.Not.Null);
            var geometryFile = files.FirstOrDefault(f => f.Path.EndsWith("geometry.json"));
            Assert.That(geometryFile, Is.Not.Null, "geometry.json should be present in the backup");
        }

        // Verify geometry metadata contains correct information
        await VerifyGeometryMetadataAsync(TARGETFOLDER, TestOptions, partitions);
    }

    [Test]
    [Category("DiskImage")]
    public async Task Test_SourceProvider_Enumeration_Async()
    {
        await TestContext.Progress.WriteLineAsync("Test: SourceProvider Enumeration");

        // Setup: Create GPT disk with one FAT32 partition
        var sourceDrivePath = _diskHelper.CreateDisk(_sourceImagePath, (100 * MiB));
        var sourcePartitions = _diskHelper.InitializeDisk(sourceDrivePath, PartitionTableType.GPT, [(FileSystemType.FAT32, 0)]);
        await TestContext.Progress.WriteLineAsync($"Source disk image created at: {_sourceImagePath}");

        // Directly instantiate SourceProvider
        // Note: When directly instantiating SourceProvider, we don't use the @ prefix
        // The @ prefix is only for Controller to recognize it as a remote source
        var sourceUrl = $"diskimage://{sourceDrivePath}";
        using var provider = new SourceProvider(sourceUrl, "", new Dictionary<string, string?>());

        // Initialize the provider
        await provider.InitializeAsync(CancellationToken.None);

        // Enumerate entries
        var diskEntries = new List<ISourceProviderEntry>();
        await foreach (var entry in provider.EnumerateAsync(CancellationToken.None))
        {
            diskEntries.Add(entry);
            await TestContext.Progress.WriteLineAsync($"Found entry: {entry.Path} (IsFolder: {entry.IsFolder}, Size: {entry.Size})");
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

        await TestContext.Progress.WriteLineAsync($"Total entries found: {entries.Count}");

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
    public async Task Test_AutoUnmountOption_RestoreWhileOnline_Async()
    {
        await TestContext.Progress.WriteLineAsync("Test: Auto Unmount Option - Restore While Disk Online");

        // Setup source disk image: 100MB, GPT, single FAT32 partition
        var sourceDrivePath = _diskHelper.CreateDisk(_sourceImagePath, (100 * MiB));
        var sourcePartitions = _diskHelper.InitializeDisk(sourceDrivePath, PartitionTableType.GPT, [(FileSystemType.FAT32, 0)]);
        await TestContext.Progress.WriteLineAsync($"Source disk image created at: {_sourceImagePath}");

        // Generate some test data
        await ToolTests.GenerateTestDataAsync(sourcePartitions.First(), 10, 5, 2, 1024);
        _diskHelper.FlushDisk(sourceDrivePath);
        _diskHelper.Unmount(sourceDrivePath);
        await TestContext.Progress.WriteLineAsync($"Test data generated on source partition");

        // Backup
        var backupResults = await RunBackupAsync(sourceDrivePath);
        TestUtils.AssertResults(backupResults, ignoredWarnings: ["diskimage-filesystem-parsed"]);
        await TestContext.Progress.WriteLineAsync($"Backup completed successfully");

        // Create and attach disk image
        var restoreDrivePath = _diskHelper.CreateDisk(_restoreImagePath, (100 * MiB));

        // Initialize disk (the restore will overwrite this, but we need it formatted)
        _diskHelper.InitializeDisk(restoreDrivePath, PartitionTableType.GPT, [(FileSystemType.FAT32, 50 * MiB), (FileSystemType.FAT32, 0)]);
        _diskHelper.Mount(restoreDrivePath, _restoreMountPath);
        await TestContext.Progress.WriteLineAsync($"Restore disk image created and kept online at: {_restoreImagePath}");

        // First attempt: Restore without auto-unmount option should fail
        // because the disk is online and in use
        await TestContext.Progress.WriteLineAsync("Attempting restore without auto-unmount (should fail)...");
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
                var results = await c.RestoreAsync(["*"]);

                // Verify that the restore failed (has errors)
                Assert.That(results.Errors.Any(), Is.True,
                    "Restore should fail when target disk is online and auto-unmount is not enabled");
                await TestContext.Progress.WriteLineAsync($"Restore failed as expected with errors: {string.Join(", ", results.Errors)}");
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
        await TestContext.Progress.WriteLineAsync("Attempting restore with auto-unmount enabled (should succeed)...");

        options["diskimage-restore-auto-unmount"] = "true";
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
        {
            var results = await c.RestoreAsync(["*"]);

            Assert.That(!results.Errors.Any() && results.Warnings.Count() <= 1);
        }
        await TestContext.Progress.WriteLineAsync($"Restore completed successfully with auto-unmount option");

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
}