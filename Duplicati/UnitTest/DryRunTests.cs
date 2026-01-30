using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    public class DryRunTests : BasicSetupHelper
    {
        [Test]
        public void TestBackupDryRunDoesNotUpload()
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
                controller.Backup(new[] { sourceFolder });

            // Verify no files were uploaded
            var uploadedFiles = Directory.GetFiles(backendFolder);
            Assert.AreEqual(0, uploadedFiles.Length, "No files should be uploaded during dry-run");
        }

        [Test]
        public void TestDeleteAllRemoteFilesDryRunDoesNotDelete()
        {
            var sourceFolder = Path.Combine(DATAFOLDER, "source");
            var backendFolder = Path.Combine(TARGETFOLDER, "backend");

            Directory.CreateDirectory(sourceFolder);
            Directory.CreateDirectory(backendFolder);

            // Create some test files and run a backup
            File.WriteAllText(Path.Combine(sourceFolder, "test.txt"), "test content");

            using (var controller = new Controller($"file://{backendFolder}", this.TestOptions, null))
            {
                controller.Backup(new[] { sourceFolder });
            }

            // Count files before dry-run delete
            var filesBefore = Directory.GetFiles(backendFolder).Length;
            Assert.Greater(filesBefore, 0, "Should have files in backend after backup");

            // Run delete-all-remote-files with dry-run
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["dry-run"] = "true"
            };

            using (var controller = new Controller($"file://{backendFolder}", options, null))
            {
                controller.DeleteAllRemoteFiles();
            }

            // Verify files still exist
            var filesAfter = Directory.GetFiles(backendFolder).Length;
            Assert.AreEqual(filesBefore, filesAfter, "Files should not be deleted during dry-run");
        }

        [Test]
        public void TestVerifyLocalListDryRunDoesNotDelete()
        {
            var sourceFolder = Path.Combine(DATAFOLDER, "source");
            var backendFolder = Path.Combine(TARGETFOLDER, "backend");

            Directory.CreateDirectory(sourceFolder);
            Directory.CreateDirectory(backendFolder);

            // Create some test files and run a backup
            File.WriteAllText(Path.Combine(sourceFolder, "test.txt"), "test content");

            using (var controller = new Controller($"file://{backendFolder}", this.TestOptions, null))
            {
                controller.Backup(new[] { sourceFolder });
            }

            var filesBefore = Directory.GetFiles(backendFolder).Length;
            Assert.Greater(filesBefore, 0);

            // Manually mark some files as "Uploading" in the database
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DBFILE};Pooling=false"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE RemoteVolume SET State = \"Uploading\" WHERE Type = \"Files\"";
                    command.ExecuteNonQuery();
                }
            }

            // Run a backup with dry-run (which triggers VerifyLocalList)
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["dry-run"] = "true"
            };

            using (var controller = new Controller($"file://{backendFolder}", options, null))
            {
                controller.Backup(new[] { sourceFolder });
            }

            // Verify that the files were not deleted
            var filesAfter = Directory.GetFiles(backendFolder).Length;
            Assert.AreEqual(filesBefore, filesAfter, "Files should not be deleted during dry-run");
        }

        [Test]
        public void TestInterruptedBackupRecoveryDryRunDoesNotUpload()
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
                var backupTask = Task.Run(() => controller.Backup(new[] { sourceFolder }));
                Thread.Sleep(2000); // Wait 2 seconds
                controller.Stop();

                try
                {
                    backupTask.Wait();
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
            {
                controller.Backup(new[] { sourceFolder });
            }

            // Verify no new files were uploaded
            var filesAfter = Directory.GetFiles(backendFolder).Length;
            Assert.AreEqual(filesBefore, filesAfter, "No files should be uploaded during dry-run recovery");
        }
    }
}
