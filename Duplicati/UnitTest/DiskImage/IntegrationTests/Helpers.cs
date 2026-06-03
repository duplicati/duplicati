using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Proprietary.DiskImage.General;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest.DiskImage.IntegrationTests;

public partial class DiskImageTests : BasicSetupHelper
{
    /// <summary>
    /// Runs a backup operation using the Controller.
    /// </summary>
    /// <param name="physicalDrivePath">The physical drive path to backup.</param>
    /// <param name="treatFilesystemAsUnknown">If true, treats filesystem as unknown (forces raw block-based backup).</param>
    /// <returns>The backup results.</returns>
    protected Task<IBackupResults> RunBackupAsync(string physicalDrivePath, bool treatFilesystemAsUnknown = false)
    {
        var options = new Dictionary<string, string>(TestOptions);
        options["enable-module"] = "diskimage";
        options["concurrency-fileprocessors"] = "1";

        if (!treatFilesystemAsUnknown)
        {
            options["diskimage-filesystem-parsed"] = "true";
        }

        using var c = new Controller("file://" + TARGETFOLDER, options, null);
        var sourceUrl = $"@/testdisk|diskimage://{physicalDrivePath}";
        return c.BackupAsync(new[] { sourceUrl });
    }

    /// <summary>
    /// Runs a restore operation using the Controller.
    /// </summary>
    /// <param name="restoreDrivePath">The physical drive path to restore to.</param>
    /// <returns>The restore results.</returns>
    protected Task<IRestoreResults> RunRestoreAsync(string restoreDrivePath)
    {
        var options = new Dictionary<string, string>(TestOptions);
        options["restore-path"] = $"@diskimage://{restoreDrivePath}";
        options["overwrite"] = "true";
        options["restore-file-processors"] = "1";

        using var c = new Controller("file://" + TARGETFOLDER, options, null);
        return c.RestoreAsync(new[] { "*" });
    }

    protected static IEnumerable<string> GetNonSystemFiles(string directoryPath)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint
        };

        return Directory.EnumerateFiles(directoryPath, "*", options)
            .Where(f => !string.IsNullOrEmpty(f))
            .Select(f => Path.GetRelativePath(directoryPath, f))
            .OrderBy(f => f);
    }

    /// <summary>
    /// Recursively compares two directories for structural and content equality.
    /// </summary>
    /// <param name="sourcePath">The source directory path.</param>
    /// <param name="restorePath">The restored directory path.</param>
    protected void CompareDirectories(string sourcePath, string restorePath)
    {
        // Get all files in source, except for system files, which cannot be read directly
        var sourceFiles = GetNonSystemFiles(sourcePath).ToList();
        var restoreFiles = GetNonSystemFiles(restorePath).ToList();

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
    protected async Task VerifyGeometryMetadataAsync(string target, Dictionary<string, string> options, (FileSystemType, long)[] partitions)
    {
        await TestContext.Progress.WriteLineAsync("Verifying geometry metadata...");

        using (var c = new Controller("file://" + target, options, null))
        {
            var listResults = await c.ListAsync("*");
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
                    var restoreResults = await restoreController.RestoreAsync(new[] { geometryFile!.Path });
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

                    await TestContext.Progress.WriteLineAsync($"Geometry metadata verified: Disk size={geometry.Disk.Size}, " +
                        $"Sector size={geometry.Disk.SectorSize}, Partitions={geometry.Partitions.Count}, " +
                        $"Table type={geometry.PartitionTable!.Type}");

                    for (int i = 0; i < geometry.Partitions.Count; i++)
                    {
                        var partition = geometry.Partitions[i];
                        await TestContext.Progress.WriteLineAsync($"Partition {i}: Type={partition.Type}, Start={partition.StartOffset}, Size={partition.Size}");

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
    protected void VerifyPartitionTableMatches(string sourceDrivePath, string restoreDrivePath)
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
}