using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Duplicati.Library.Main;
using Microsoft.Data.Sqlite;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using System.Threading.Tasks;

namespace Duplicati.UnitTest
{
    public class Issue6552 : BasicSetupHelper
    {
        [Test]
        [Category("Issue6552")]
        public async Task RecreateDbShouldNotCreateInvalidDeletedBlockReferencesAsync()
        {
            // Arrange
            var testopts = TestOptions;
            testopts["backup-test-samples"] = "0";
            testopts["no-encryption"] = "true"; // Easier to manipulate files

            var sourceFolder = Path.Combine(DATAFOLDER, "source");
            Directory.CreateDirectory(sourceFolder);

            // Step 1: Create file A.txt and backup
            File.WriteAllText(Path.Combine(sourceFolder, "A.txt"), "Content A");

            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = await c.BackupAsync(new[] { sourceFolder });
                Assert.AreEqual(0, res.Errors.Count());
            }

            // Save the dindex file from first backup
            var dindexFiles = Directory.GetFiles(TARGETFOLDER, "*.dindex.*");
            Assert.IsTrue(dindexFiles.Length > 0, "Should have at least one dindex file");
            var savedDindexPath = Path.Combine(DATAFOLDER, Path.GetFileName(dindexFiles[0]));
            File.Copy(dindexFiles[0], savedDindexPath);

            // Identify the dblock and dlist from the first backup
            var firstDblock = Directory.GetFiles(TARGETFOLDER, "*.dblock.*").First();
            var firstDlist = Directory.GetFiles(TARGETFOLDER, "*.dlist.*").First();

            // Step 2: Delete A.txt, create B.txt and backup
            File.Delete(Path.Combine(sourceFolder, "A.txt"));
            File.WriteAllText(Path.Combine(sourceFolder, "B.txt"), "Content B - different content");

            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = await c.BackupAsync(new[] { sourceFolder });
                Assert.AreEqual(0, res.Errors.Count());
            }

            // Step 3: Delete the dblock file that the saved dindex references
            // and add the old dindex back

            // Delete the dblock associated with the first backup
            if (File.Exists(firstDblock))
            {
                File.Delete(firstDblock);
            }

            // Delete the dlist associated with the first backup to ensure blocks are orphaned
            if (File.Exists(firstDlist))
            {
                File.Delete(firstDlist);
            }

            // Delete the database to force recreate
            File.Delete(testopts["dbpath"]);

            // Add the old dindex file back (which references a dblock that may not exist)
            File.Copy(savedDindexPath, Path.Combine(TARGETFOLDER, Path.GetFileName(savedDindexPath)), true);

            // Step 4: Recreate the database
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                // This should complete but may create invalid references
                try
                {
                    var res = await c.RepairAsync();
                }
                catch (Exception ex)
                {
                    // Recreate might fail, but we want to check the DB state
                    await TestContext.Progress.WriteLineAsync($"Repair failed: {ex.Message}");
                }
            }

            // Step 5: Check for invalid DeletedBlock references
            using (var connection = new SqliteConnection($"Data Source={testopts["dbpath"]};Pooling=false"))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();

                // Check for DeletedBlock entries with invalid VolumeID
                command.CommandText = @"
                    SELECT COUNT(*)
                    FROM DeletedBlock
                    WHERE VolumeID NOT IN (SELECT ID FROM RemoteVolume)
                ";

                var invalidCount = (long)await command.ExecuteScalarAsync();

                // This assertion will fail with the current bug
                Assert.AreEqual(0, invalidCount,
                    $"Found {invalidCount} DeletedBlock entries with invalid VolumeID references");
            }
        }
    }
}
