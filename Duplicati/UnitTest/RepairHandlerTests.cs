using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class RepairHandlerTests : BasicSetupHelper
    {
        public override void SetUp()
        {
            base.SetUp();
            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, "file"), new byte[] {0});
        }

        [Test]
        [Category("RepairHandler")]
        public void RepairMissingBlocklistHashes()
        {
            byte[] data = new byte[150 * 1024];
            Random rng = new Random();
            for (int k = 0; k < 2; k++)
            {
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(this.DATAFOLDER, $"{k}"), data);
            }

            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Mimic a damaged database that needs to be repaired.
            const string selectStatement = @"SELECT BlocksetID, ""Index"", Hash FROM BlocklistHash ORDER BY Hash ASC";
            List<int> expectedBlocksetIDs = new List<int>();
            List<int> expectedIndexes = new List<int>();
            List<string> expectedHashes = new List<string>();
            using (IDbConnection connection = SQLiteLoader.LoadConnection(options["dbpath"]))
            {
                // Read the contents of the BlocklistHash table so that we can
                // compare them to the contents after the repair operation.
                using (IDbCommand command = connection.CreateCommand())
                {
                    using (IDataReader reader = command.ExecuteReader(selectStatement))
                    {
                        while (reader.Read())
                        {
                            expectedBlocksetIDs.Add(reader.GetInt32(0));
                            expectedIndexes.Add(reader.GetInt32(1));
                            expectedHashes.Add(reader.GetString(2));
                        }
                    }
                }

                using (IDbCommand command = connection.CreateCommand())
                {
                    command.ExecuteNonQuery(@"DELETE FROM BlocklistHash");
                    using (IDataReader reader = command.ExecuteReader(selectStatement))
                    {
                        Assert.IsFalse(reader.Read());
                    }
                }
            }

            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IRepairResults repairResults = c.Repair();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Count());
            }

            List<int> repairedBlocksetIDs = new List<int>();
            List<int> repairedIndexes = new List<int>();
            List<string> repairedHashes = new List<string>();
            using (IDbConnection connection = SQLiteLoader.LoadConnection(options["dbpath"]))
            {
                using (IDbCommand command = connection.CreateCommand())
                {
                    using (IDataReader reader = command.ExecuteReader(selectStatement))
                    {
                        while (reader.Read())
                        {
                            repairedBlocksetIDs.Add(reader.GetInt32(0));
                            repairedIndexes.Add(reader.GetInt32(1));
                            repairedHashes.Add(reader.GetString(2));
                        }
                    }
                }
            }

            CollectionAssert.AreEqual(expectedBlocksetIDs, repairedBlocksetIDs);
            CollectionAssert.AreEqual(expectedIndexes, repairedIndexes);
            CollectionAssert.AreEqual(expectedHashes, repairedHashes);

            // A subsequent backup should run without errors.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }
        }

        [Test]
        [Category("RepairHandler")]
        [TestCase("true")]
        [TestCase("false")]
        public void RepairMissingIndexFiles(string noEncryption)
        {
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions) {["no-encryption"] = noEncryption};
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            string[] dindexFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*dindex*").ToArray();
            Assert.Greater(dindexFiles.Length, 0);
            foreach (string f in dindexFiles)
            {
                File.Delete(f);
            }

            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IRepairResults repairResults = c.Repair();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Count());
            }

            foreach (string file in dindexFiles)
            {
                Assert.IsTrue(File.Exists(Path.Combine(this.TARGETFOLDER, file)));
            }
        }
    }
}