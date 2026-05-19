using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

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
                    command.CommandText = "UPDATE RemoteVolume SET State = \"Uploading\" WHERE Type = \"Files\"";
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
    }
}
