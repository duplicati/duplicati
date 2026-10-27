using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest
{
    public class DryRunTests : BasicSetupHelper
    {
        [Test]
        public async Task TestBackupDryRunDoesNotUploadAsync()
        {
            var sourceFolder = Path.Combine(DATAFOLDER, "source");
            var backendFolder = Path.Combine(TARGETFOLDER, "backend");

            Directory.CreateDirectory(sourceFolder);
            Directory.CreateDirectory(backendFolder);

            // Create some test files
            File.WriteAllText(Path.Combine(sourceFolder, "test.txt"), "test content");

            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["dry-run"] = "true"
            };

            using (var controller = new Controller($"file://{backendFolder}", options, null))
                await controller.BackupAsync(new[] { sourceFolder });

            // Verify no files were uploaded
            var uploadedFiles = Directory.GetFiles(backendFolder);
            Assert.AreEqual(0, uploadedFiles.Length, "No files should be uploaded during dry-run");
        }

        [Test]
        public async Task TestDeleteAllRemoteFilesDryRunDoesNotDeleteAsync()
        {
            var sourceFolder = Path.Combine(DATAFOLDER, "source");
            var backendFolder = Path.Combine(TARGETFOLDER, "backend");

            Directory.CreateDirectory(sourceFolder);
            Directory.CreateDirectory(backendFolder);

            // Create some test files and run a backup
            File.WriteAllText(Path.Combine(sourceFolder, "test.txt"), "test content");

            using (var controller = new Controller($"file://{backendFolder}", this.TestOptions, null))
                await controller.BackupAsync(new[] { sourceFolder });

            var filesBefore = Directory.GetFiles(backendFolder).Length;
            Assert.Greater(filesBefore, 0, "Should have files in backend after backup");

            // Run delete-all-remote-files with dry-run
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["dry-run"] = "true"
            };

            using (var controller = new Controller($"file://{backendFolder}", options, null))
                await controller.DeleteAllRemoteFilesAsync();

            // Verify files still exist
            var filesAfter = Directory.GetFiles(backendFolder).Length;
            Assert.AreEqual(filesBefore, filesAfter, "Files should not be deleted during dry-run");
        }

        [Test]
        public async Task TestVerifyLocalListDryRunDoesNotDeleteAsync()
        {
            var sourceFolder = Path.Combine(DATAFOLDER, "source");
            var backendFolder = Path.Combine(TARGETFOLDER, "backend");

            Directory.CreateDirectory(sourceFolder);
            Directory.CreateDirectory(backendFolder);

            // Create some test files and run a backup
            File.WriteAllText(Path.Combine(sourceFolder, "test.txt"), "test content");

            using (var controller = new Controller($"file://{backendFolder}", this.TestOptions, null))
                await controller.BackupAsync(new[] { sourceFolder });

            var filesBefore = Directory.GetFiles(backendFolder).Length;
            Assert.Greater(filesBefore, 0);

            // Manually mark some files as "Uploading" in the database
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DBFILE};Pooling=false"))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE RemoteVolume SET State = 'Uploading' WHERE Type = 'Files'";
                    await command.ExecuteNonQueryAsync();
                }
            }

            // Run a backup with dry-run (which triggers VerifyLocalList)
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["dry-run"] = "true"
            };

            using (var controller = new Controller($"file://{backendFolder}", options, null))
                await controller.BackupAsync(new[] { sourceFolder });

            // Verify that the files were not deleted
            var filesAfter = Directory.GetFiles(backendFolder).Length;
            Assert.AreEqual(filesBefore, filesAfter, "Files should not be deleted during dry-run");
        }

        [Test]
        public async Task TestInterruptedBackupRecoveryDryRunDoesNotUploadAsync()
        {
            var sourceFolder = Path.Combine(DATAFOLDER, "source");
            var backendFolder = Path.Combine(TARGETFOLDER, "backend");

            Directory.CreateDirectory(sourceFolder);
            Directory.CreateDirectory(backendFolder);

            // Create some large files to ensure backup takes some time
            var data = new byte[10 * 1024 * 1024]; // 10MB
            new Random().NextBytes(data);
            File.WriteAllBytes(Path.Combine(sourceFolder, "test.bin"), data);

            // Start a backup and interrupt it
            using (var controller = new Controller($"file://{backendFolder}", this.TestOptions, null))
            {
                var backupTask = controller.BackupAsync(new[] { sourceFolder });
                await Task.Delay(2000); // Wait 2 seconds
                await controller.StopAsync();

                try
                {
                    await backupTask;
                }
                catch (AggregateException)
                {
                    // Expected
                }
            }

            // Now run recovery with dry-run
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["dry-run"] = "true"
            };

            var filesBefore = Directory.GetFiles(backendFolder).Length;

            using (var controller = new Controller($"file://{backendFolder}", options, null))
                await controller.BackupAsync(new[] { sourceFolder });

            // Verify no new files were uploaded
            var filesAfter = Directory.GetFiles(backendFolder).Length;
            Assert.AreEqual(filesBefore, filesAfter, "No files should be uploaded during dry-run recovery");
        }

        /// <summary>
        /// Reads the name and state of every remote volume the local database knows of.
        /// </summary>
        private async Task<string[]> ReadRemoteVolumeStatesAsync()
        {
            var result = new List<string>();
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DBFILE};Pooling=false"))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT Name, State FROM RemoteVolume ORDER BY Name";
                    using (var reader = await command.ExecuteReaderAsync())
                        while (await reader.ReadAsync())
                            result.Add($"{reader.GetString(0)}: {reader.GetString(1)}");
                }
            }

            return result.ToArray();
        }

        /// <summary>
        /// A purge with dry-run runs the compact step to report what it would do, but the
        /// bookkeeping of that step (new volumes registered as temporary or uploading, old
        /// volumes marked for deletion) must never be committed, as nothing was uploaded or
        /// deleted. If it is, the next real run deletes the original volumes and fails on the
        /// replacement volumes that were never uploaded.
        /// </summary>
        [Test]
        [Category("Purge")]
        public async Task TestPurgeDryRunDoesNotCommitCompactStateAsync()
        {
            var testopts = TestOptions;
            testopts["dblock-size"] = "20KB";
            testopts["blocksize"] = "1KB";
            testopts["threshold"] = "5";
            testopts["no-auto-compact"] = "true";

            const long filesize = 44971;
            var target = "file://" + TARGETFOLDER;
            var files = Enumerable.Range(1, 4).Select(i => Path.Combine(DATAFOLDER, $"f{i}")).ToArray();
            foreach (var file in files)
                TestUtils.WriteTestFile(file, filesize);

            using (var c = new Controller(target, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            // Make sure the next fileset gets a distinct timestamp
            await Task.Delay(TimeSpan.FromSeconds(2));

            File.Delete(files[0]);
            File.Delete(files[2]);
            using (var c = new Controller(target, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            // Drop the oldest version without compacting, so the remote storage holds wasted space
            // and the compact step of the purge has something to do
            using (var c = new Controller(target, testopts.Expand(new { version = 1 }), null))
                TestUtils.AssertResults(await c.DeleteAsync());

            var backendBefore = Directory.GetFiles(TARGETFOLDER).Select(Path.GetFileName).OrderBy(x => x).ToArray();
            var volumesBefore = await ReadRemoteVolumeStatesAsync();
            Assert.Greater(backendBefore.Length, 0, "Should have files in backend after backup");
            Assert.IsFalse(volumesBefore.Any(x => x.EndsWith(": Temporary") || x.EndsWith(": Uploading") || x.EndsWith(": Deleting")), "No volume should be in a transient state before the dry-run");

            var purgeopts = new Dictionary<string, string>(testopts)
            {
                ["dry-run"] = "true"
            };
            purgeopts.Remove("no-auto-compact");

            using (var c = new Controller(target, purgeopts, null))
            {
                var res = await c.PurgeFilesAsync(new Library.Utility.FilterExpression("*" + Path.DirectorySeparatorChar + "f2"));
                TestUtils.AssertResults(res);
                Assert.AreEqual(1, res.RewrittenFileLists, "The dry-run should report the fileset it would rewrite");
                Assert.IsNotNull(res.CompactResults, "The dry-run should report the compaction it would do");
                Assert.IsTrue(res.CompactResults.Dryrun, "The compaction should be reported as a dry-run");
                Assert.Greater(res.CompactResults.DeletedFileCount + res.CompactResults.UploadedFileCount, 0, "The dry-run compaction should have reported volumes it would delete or upload");
            }

            var backendAfter = Directory.GetFiles(TARGETFOLDER).Select(Path.GetFileName).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(backendBefore, backendAfter, "Remote files should not be changed during dry-run");

            var volumesAfter = await ReadRemoteVolumeStatesAsync();
            CollectionAssert.AreEqual(volumesBefore, volumesAfter, "The remote volume table should not be changed during dry-run");

            // The next real operations must work on the untouched state
            var realopts = new Dictionary<string, string>(testopts);
            realopts.Remove("no-auto-compact");
            using (var c = new Controller(target, realopts, null))
                TestUtils.AssertResults(await c.CompactAsync());

            using (var c = new Controller(target, realopts, null))
                TestUtils.AssertResults(await c.TestAsync(long.MaxValue));
        }

        /// <summary>
        /// A backup with dry-run that triggers the retention delete and the compact step must
        /// likewise leave the remote volume table untouched, even though those steps record the
        /// volumes they would delete and upload as they go.
        /// </summary>
        [Test]
        [Category("Compact")]
        public async Task TestBackupDryRunDoesNotCommitDeleteOrCompactStateAsync()
        {
            var testopts = TestOptions;
            testopts["dblock-size"] = "20KB";
            testopts["blocksize"] = "1KB";
            testopts["threshold"] = "5";

            const long filesize = 44971;
            var target = "file://" + TARGETFOLDER;
            var files = Enumerable.Range(1, 4).Select(i => Path.Combine(DATAFOLDER, $"f{i}")).ToArray();
            foreach (var file in files)
                TestUtils.WriteTestFile(file, filesize);

            using (var c = new Controller(target, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            // Make sure the next fileset gets a distinct timestamp
            await Task.Delay(TimeSpan.FromSeconds(2));

            File.Delete(files[0]);
            File.Delete(files[2]);
            using (var c = new Controller(target, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            var backendBefore = Directory.GetFiles(TARGETFOLDER).Select(Path.GetFileName).OrderBy(x => x).ToArray();
            var volumesBefore = await ReadRemoteVolumeStatesAsync();
            Assert.IsFalse(volumesBefore.Any(x => x.EndsWith(": Temporary") || x.EndsWith(": Uploading") || x.EndsWith(": Deleting")), "No volume should be in a transient state before the dry-run");

            await Task.Delay(TimeSpan.FromSeconds(2));

            // Keeping one version makes the dry-run delete the oldest fileset, and the
            // wasted space that leaves behind makes it run the compact step
            var dryrunopts = new Dictionary<string, string>(testopts)
            {
                ["dry-run"] = "true",
                ["keep-versions"] = "1"
            };

            using (var c = new Controller(target, dryrunopts, null))
            {
                var res = await c.BackupAsync(new[] { DATAFOLDER });
                TestUtils.AssertResults(res);
                Assert.IsNotNull(res.DeleteResults, "The dry-run should report the versions it would delete");
                Assert.AreEqual(1, res.DeleteResults.DeletedSets.Count(), "The dry-run should report the version it would delete");
                Assert.IsNotNull(res.DeleteResults.CompactResults, "The dry-run should report the compaction it would do");
                Assert.IsTrue(res.DeleteResults.CompactResults.Dryrun, "The compaction should be reported as a dry-run");
                Assert.Greater(res.DeleteResults.CompactResults.DeletedFileCount + res.DeleteResults.CompactResults.UploadedFileCount, 0, "The dry-run compaction should have reported volumes it would delete or upload");
            }

            var backendAfter = Directory.GetFiles(TARGETFOLDER).Select(Path.GetFileName).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(backendBefore, backendAfter, "Remote files should not be changed during dry-run");

            // The backup registers its own fileset volume as temporary before it knows whether it
            // will upload it, and that registration is committed even in dry-run. It is removed by
            // the remote verification of the next run, so it is left out here; the delete and
            // compact steps must not have left anything behind.
            var volumesAfter = (await ReadRemoteVolumeStatesAsync())
                .Where(x => !(x.Contains(".dlist.") && x.EndsWith(": Temporary")))
                .ToArray();
            CollectionAssert.AreEqual(volumesBefore, volumesAfter, "The remote volume table should not be changed during dry-run");

            // The next real operations must work on the untouched state
            using (var c = new Controller(target, testopts, null))
                TestUtils.AssertResults(await c.CompactAsync());

            using (var c = new Controller(target, testopts, null))
                TestUtils.AssertResults(await c.TestAsync(long.MaxValue));
        }
    }
}
