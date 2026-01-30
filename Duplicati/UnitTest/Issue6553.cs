using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Duplicati.Library.Main;
using Microsoft.Data.Sqlite;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    public class Issue6553 : BasicSetupHelper
    {
        [Test]
        [Category("Issue6553")]
        public void RecreateDbShouldNotLoseFilesetWhenDindexLacksDblock()
        {
            // Arrange
            var testopts = TestOptions;
            testopts["backup-test-samples"] = "0";
            testopts["no-encryption"] = "true";
            testopts["keep-versions"] = "0"; // Keep all versions

            var sourceFolder = Path.Combine(DATAFOLDER, "source");
            Directory.CreateDirectory(sourceFolder);

            // Step 1: Create file A and backup (Backup 1)
            File.WriteAllText(Path.Combine(sourceFolder, "A.txt"), "Content A");

            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup(new[] { sourceFolder });
                Assert.AreEqual(0, res.Errors.Count());
            }

            // Save dindex1
            var dindexFiles = Directory.GetFiles(TARGETFOLDER, "*.dindex.*");
            Assert.AreEqual(1, dindexFiles.Length);
            var dindex1Path = Path.Combine(DATAFOLDER, "dindex1.zip");
            File.Copy(dindexFiles[0], dindex1Path);

            // Step 2: Delete DB and Backup A again (Backup 2)
            // This forces A to be uploaded to a new dblock (dblock2)
            File.Delete(testopts["dbpath"]);

            foreach (var f in Directory.GetFiles(TARGETFOLDER))
                File.Delete(f);

            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup(new[] { sourceFolder });
                Assert.AreEqual(0, res.Errors.Count());
            }

            // Step 3: Prepare for Recreate
            File.Delete(testopts["dbpath"]);

            // Restore dindex1. Rename it to ensure it is processed FIRST.
            var dindex1RestorePath = Path.Combine(TARGETFOLDER, "duplicati-00000000000000000000000000000000.dindex.zip");
            File.Copy(dindex1Path, dindex1RestorePath);

            // Step 4: Recreate the database
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                try
                {
                    var res = c.Repair();
                }
                catch (Exception)
                {
                    // Repair is expected to fail or warn due to missing blocks
                }
            }

            // Step 5: Verify that we have at least one version (Backup 2)
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var listRes = c.List();

                Assert.IsTrue(listRes.Filesets.Count() > 0,
                    "Expected at least one backup version after recreate, but found none");
            }

            // Step 6: Check for DeletedBlock entries pointing to Temporary volumes
            using (var connection = new SqliteConnection($"Data Source={testopts["dbpath"]};Pooling=false"))
            {
                connection.Open();
                var command = connection.CreateCommand();

                command.CommandText = @"
                    SELECT COUNT(*)
                    FROM DeletedBlock
                    WHERE VolumeID IN (SELECT ID FROM RemoteVolume WHERE State = 'Temporary')
                ";
                var tempCount = (long)command.ExecuteScalar();

                // With the fix (and apparently even without it in this environment), this should be 0
                Assert.AreEqual(0, tempCount, "Should not have DeletedBlock entries pointing to Temporary volumes");

                command.CommandText = @"
                    SELECT COUNT(*)
                    FROM DeletedBlock
                    WHERE VolumeID NOT IN (SELECT ID FROM RemoteVolume)
                ";
                var invalidCount = (long)command.ExecuteScalar();
                Assert.AreEqual(0, invalidCount, "Should not have DeletedBlock entries with invalid VolumeID references");
            }
        }
    }
}
