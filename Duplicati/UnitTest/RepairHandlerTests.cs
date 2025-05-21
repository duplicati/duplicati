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
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class RepairHandlerTests : BasicSetupHelper
    {
        [SetUp]
        public void SetUp()
        {
            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, "file"), new byte[] { 0 });
        }

        public void ModifyFile()
        {
            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, "file"), new byte[] { 1 });
        }

        [Test]
        [Category("RepairHandler")]
        [TestCase(150 * 1024)]
        // With 10kib blocksize, we can have 10240/32 = 320 hashes
        // Test near limits of blocklist to check split blocklist
        [TestCase(319 * 10240)]
        [TestCase(320 * 10240)]
        [TestCase(320 * 10240 + 5)]
        [TestCase(320 * 10240 * 2)]
        [TestCase(320 * 10240 * 2 + 5)]
        [TestCase(320 * 10240 * 3)]
        [TestCase(320 * 10240 * 3 + 5)]
        public void RepairMissingBlocklistHashes(int dataSize)
        {
            byte[] data = new byte[dataSize];
            Random rng = new Random();
            for (int k = 0; k < 2; k++)
            {
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(this.DATAFOLDER, $"{k}"), data);
            }

            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = c.Backup([this.DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Mimic a damaged database that needs to be repaired.
            const string selectStatement = @"SELECT BlocksetID, ""Index"", Hash FROM BlocklistHash ORDER BY Hash ASC";
            List<int> expectedBlocksetIDs = new List<int>();
            List<int> expectedIndexes = new List<int>();
            List<string> expectedHashes = new List<string>();
            using (IDbConnection connection = SQLiteLoader.LoadConnection(options["dbpath"], 0))
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
                            expectedHashes.Add(reader.ConvertValueToString(2));
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
            using (IDbConnection connection = SQLiteLoader.LoadConnection(options["dbpath"], 0))
            {
                using (IDbCommand command = connection.CreateCommand())
                {
                    using (IDataReader reader = command.ExecuteReader(selectStatement))
                    {
                        while (reader.Read())
                        {
                            repairedBlocksetIDs.Add(reader.GetInt32(0));
                            repairedIndexes.Add(reader.GetInt32(1));
                            repairedHashes.Add(reader.ConvertValueToString(2));
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
                var backupResults = c.Backup([this.DATAFOLDER]);
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
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions) { ["no-encryption"] = noEncryption };
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = c.Backup([this.DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            var dindexFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*.dindex.*").ToArray();
            Assert.Greater(dindexFiles.Length, 0);
            foreach (var f in dindexFiles)
            {
                File.Delete(f);
            }

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var repairResults = c.Repair();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Count());
            }

            var recreatedIndexFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*dindex*").ToArray();
            Assert.AreEqual(dindexFiles.Length, recreatedIndexFiles.Length);
        }

        [Test]
        [Category("RepairHandler"), Category("Targeted")]
        public void RepairMissingIndexFilesBlocklist()
        {
            // See issue #3202
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["blocksize"] = "1KB",
                ["no-encryption"] = "true"
            };
            var filename = Path.Combine(this.DATAFOLDER, "file");
            using (var s = File.Create(filename))
            {
                var size = 1024 * 32 + 1; // Blocklist size + 1
                s.Write(new byte[size], 0, size);
            }

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
                using (var s = File.OpenWrite(filename))
                {
                    // Change first byte
                    s.WriteByte(1);
                }
                backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            var dindexFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*.dindex.*").ToArray();
            Assert.Greater(dindexFiles.Length, 0);
            foreach (var f in dindexFiles)
            {
                File.Delete(f);
            }

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var repairResults = c.Repair();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Count());
            }

            var recreatedIndexFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*dindex*").ToArray();
            Assert.AreEqual(dindexFiles.Length, recreatedIndexFiles.Length);
        }

        [Test]
        [Category("RepairHandler"), Category("Targeted")]
        public void RecreateWithDefectIndexBlock()
        {
            // See issue #3202
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["blocksize"] = "1KB",
                ["no-encryption"] = "true"
            };
            var filename = Path.Combine(this.DATAFOLDER, "file");
            using (var s = File.Create(filename))
            {
                var size = 1024 * 32 + 1; // Blocklist size + 1
                s.Write(new byte[size], 0, size);
            }

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
                using (var s = File.OpenWrite(filename))
                {
                    // Change first byte
                    s.WriteByte(1);
                }
                backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            var dindexFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*dindex*").ToArray();
            Assert.Greater(dindexFiles.Length, 0);

            // Corrupt the first index file
            using (var tmp = new TempFile())
            {
                using (var zip = new ZipArchive(File.Open(tmp, FileMode.Create, FileAccess.ReadWrite), ZipArchiveMode.Create))
                using (var sourceZip = new ZipArchive(File.Open(dindexFiles[0], FileMode.Open, FileAccess.ReadWrite)))
                {
                    foreach (var entry in sourceZip.Entries)
                    {
                        using (var s = entry.Open())
                        {
                            var newEntry = zip.CreateEntry(entry.FullName);
                            using (var d = newEntry.Open())
                            {
                                if (entry.FullName.StartsWith("list/"))
                                {
                                    using (var ms = new MemoryStream())
                                    {
                                        s.CopyTo(ms);
                                        ms.Position = 0;
                                        ms.WriteByte(42);
                                        ms.Position = 0;
                                        ms.CopyTo(d);
                                    }
                                }
                                else
                                {
                                    s.CopyTo(d);
                                }
                            }
                        }
                    }
                }

                File.Copy(tmp, dindexFiles[0], true);
            }

            // Delete database and recreate
            File.Delete(options["dbpath"]);

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var repairResults = c.Repair();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(1, repairResults.Warnings.Count());
            }

            File.Delete(dindexFiles[0]);

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var repairResults = c.Repair();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Count());
            }

            // Delete database and recreate
            File.Delete(options["dbpath"]);

            // No errors with recreated index file
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var repairResults = c.Repair();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Count());
            }

        }

        [Test]
        [Category("RepairHandler"), Category("Targeted")]
        public void AutoCleanupRepairDoesNotLockDatabase()
        {
            // See issue #3635, #4631
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["blocksize"] = "1KB",
                ["no-encryption"] = "true",
                ["auto-cleanup"] = "true"
            };
            var delaytime = TimeSpan.FromSeconds(3);
            var filename = Path.Combine(this.DATAFOLDER, "file");
            using (var s = File.Create(filename))
                s.SetLength(1024 * 38); // Random size

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = c.Backup([this.DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            var dblockFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*dblock*").ToArray();
            Assert.Greater(dblockFiles.Length, 0);
            var sourcename = Path.GetFileName(dblockFiles.First());
            var p = VolumeBase.ParseFilename(sourcename);
            var guid = VolumeWriterBase.GenerateGuid();
            var time = p.Time.Ticks == 0 ? p.Time : p.Time.AddSeconds(1);
            var newname = VolumeBase.GenerateFilename(p.FileType, p.Prefix, guid, time, p.CompressionModule, p.EncryptionModule);

            File.Copy(Path.Combine(this.TARGETFOLDER, sourcename), Path.Combine(this.TARGETFOLDER, newname));

            System.Threading.Thread.Sleep(delaytime);
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = c.Backup([this.DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count());
                // 1 extra file + 1 warning
                Assert.AreEqual(2, backupResults.Warnings.Count());
            }

            // Auto-cleanup should have removed the renamed file
            Assert.IsFalse(File.Exists(Path.Combine(this.TARGETFOLDER, newname)));

            // Insert the extra file back
            File.Copy(Path.Combine(this.TARGETFOLDER, sourcename), Path.Combine(this.TARGETFOLDER, newname));

            // Delete the database
            File.Delete(options["dbpath"]);

            // Recreate with an extra volume
            System.Threading.Thread.Sleep(delaytime);
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = c.Backup([this.DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count());
                // First will recreate (4 files + 1 warning)
                Assert.AreEqual(5, backupResults.Warnings.Count());
            }
        }

        [Test]
        [Category("RepairHandler")]
        [TestCase("true")]
        [TestCase("false")]
        public void RepairExtraIndexFiles(string noEncryption)
        {
            // Extra index files will be added to the database and should have a correct link established
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions) { ["no-encryption"] = noEncryption };
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                TestUtils.AssertResults(backupResults);
            }

            string[] dindexFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*dindex*").ToArray();
            Assert.Greater(dindexFiles.Length, 0);
            string origFile = dindexFiles.First();
            // Duplicate index file
            string dupFile = Path.Combine(TARGETFOLDER, Path.GetFileName(origFile).Replace(".dindex", "1.dindex"));
            File.Copy(origFile, dupFile);

            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IRepairResults repairResults = c.Repair();
                TestUtils.AssertResults(repairResults);
            }

            // Check database block link
            using (LocalDatabase db = new LocalDatabase(DBFILE, "Test", true, 0))
            {
                var indexVolumeId = db.GetRemoteVolumeID(Path.GetFileName(origFile));
                var duplicateVolumeId = db.GetRemoteVolumeID(Path.GetFileName(dupFile));
                Assert.AreNotEqual(-1, indexVolumeId);
                Assert.AreNotEqual(-1, duplicateVolumeId);

                using (var cmd = db.Connection.CreateCommand())
                {
                    var sql = @"SELECT ""BlockVolumeID"" FROM ""IndexBlockLink"" WHERE ""IndexVolumeID"" = @VolumeId";
                    var linkedIndexId = cmd.SetCommandAndParameters(sql).SetParameterValue("@VolumeId", indexVolumeId).ExecuteScalarInt64(-1);
                    var linkedDuplicateId = cmd.SetCommandAndParameters(sql).SetParameterValue("@VolumeId", duplicateVolumeId).ExecuteScalarInt64(-1);
                    Assert.AreEqual(linkedIndexId, linkedDuplicateId);
                }
            }
        }

        [Test]
        [Category("RepairHandler")]
        public void RepairMissingDlistFile()
        {
            // Make two backups
            var options = this.TestOptions;
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));

            ModifyFile();
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));

            // Find and delete a dlist file
            var dlistFile = Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*").First();
            File.Delete(dlistFile);

            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IRepairResults repairResults = c.Repair();
                TestUtils.AssertResults(repairResults);
                Assert.AreEqual(2, Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*").Count());
            }
        }

        [Test]
        [Category("RepairHandler")]
        [TestCase("true")]
        [TestCase("false")]
        public void RepairMissingDlistVolume(bool deleteRemoteFile)
        {
            // Make two backups
            var options = this.TestOptions;
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));

            ModifyFile();
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));

            // Find a dlist file
            var dlistFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*")
                .OrderByDescending(x => x)
                .AsEnumerable();

            // Randomly delete the first or last file
            if (Random.Shared.Next(2) == 0)
                dlistFiles = dlistFiles.Reverse();

            var dlistFile = dlistFiles.First();
            using (var con = SQLiteLoader.LoadConnection(options["dbpath"], 0))
            using (var cmd = con.CreateCommand("DELETE FROM RemoteVolume WHERE Name = @Name"))
                Assert.AreEqual(1, cmd.SetParameterValue("@Name", Path.GetFileName(dlistFile)).ExecuteNonQuery());

            if (deleteRemoteFile)
                File.Delete(dlistFile);

            // Should catch this in validation
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                Assert.Throws<DatabaseInconsistencyException>(() => c.Backup(new[] { this.DATAFOLDER }));

            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IRepairResults repairResults = c.Repair();
                TestUtils.AssertResults(repairResults);
                Assert.AreEqual(2, Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*").Count());
                Assert.AreEqual(2, c.List().Filesets.Count());
            }

            // Check that the entry was recreated
            using (var con = SQLiteLoader.LoadConnection(options["dbpath"], 0))
            using (var cmd = con.CreateCommand("SELECT COUNT(*) FROM RemoteVolume WHERE Name LIKE '%.dlist.%' AND State != @State"))
                Assert.AreEqual(2, cmd.SetParameterValue("@State", RemoteVolumeState.Deleted.ToString()).ExecuteScalarInt64(-1));

            // Delete the database and check that the result is correct
            File.Delete(options["dbpath"]);

            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IRepairResults repairResults = c.Repair();
                TestUtils.AssertResults(repairResults);
                Assert.AreEqual(2, c.List().Filesets.Count());

                var r = c.Test(long.MaxValue);
                Assert.AreEqual(0, r.Errors.Count());
                Assert.AreEqual(0, r.Warnings.Count());
                Assert.IsFalse(r.Verifications.Any(p => p.Value.Any()));
            }
        }

        [Test]
        [Category("RepairHandler")]
        public void RepairMissingFilesetVolume()
        {
            // Make two backups
            var options = this.TestOptions;
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));

            // Make room for a new backup
            Thread.Sleep(2000);

            ModifyFile();
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));

            // Remove a fileset
            using (var con = SQLiteLoader.LoadConnection(options["dbpath"], 0))
            using (var cmd = con.CreateCommand())
            {
                var filesetId = cmd.ExecuteScalarInt64("SELECT Id FROM Fileset ORDER BY Id DESC LIMIT 1");
                Assert.AreEqual(1, cmd.SetCommandAndParameters("DELETE FROM Fileset WHERE Id = @FilesetId")
                    .SetParameterValue("@FilesetId", filesetId)
                    .ExecuteNonQuery());

                // No longer needed because the recreate will wipe the table before restoring
                // cmd.SetCommandAndParameters("DELETE FROM FilesetEntry WHERE FilesetId = @FilesetId")
                //     .SetParameterValue("@FilesetId", filesetId)
                //     .ExecuteNonQuery();
            }

            // Should catch this in validation
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                Assert.Throws<DatabaseInconsistencyException>(() => c.Backup(new[] { this.DATAFOLDER }));

            // Since we have removed the entry that is being checked here, we need to disable the check
            var repairOptions = new Dictionary<string, string>(options) { ["repair-ignore-outdated-database"] = "true" };
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, repairOptions, null))
            {
                IRepairResults repairResults = c.Repair();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Where(x => x.IndexOf("RemoteFilesNewerThanLocalDatabase", StringComparison.OrdinalIgnoreCase) < 0).Count());
                Assert.AreEqual(2, Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*").Count());
                Assert.AreEqual(2, c.List().Filesets.Count());
            }

            // Check that entry was recreated
            using (var con = SQLiteLoader.LoadConnection(options["dbpath"], 0))
            using (var cmd = con.CreateCommand())
                Assert.AreEqual(2, cmd.ExecuteScalarInt64("SELECT COUNT(*) FROM Fileset"));
        }

        [Test]
        [Category("RepairHandler")]
        public void ManufactureMissingFiles()
        {
            // Make two backups
            var options = this.TestOptions;
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));

            // Make room for a new backup
            Thread.Sleep(2000);

            ModifyFile();
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));

            // Remove a fileset
            long filesetEntries;
            long fileLookupEntries;
            using (var con = SQLiteLoader.LoadConnection(options["dbpath"], 0))
            using (var cmd = con.CreateCommand())
            {
                filesetEntries = cmd.ExecuteScalarInt64("SELECT COUNT(*) FROM FilesetEntry");
                fileLookupEntries = cmd.ExecuteScalarInt64("SELECT COUNT(*) FROM FileLookup");
                var fileId = cmd.ExecuteScalarInt64("SELECT FileId FROM FilesetEntry INNER JOIN FileLookup ON FilesetEntry.FileID = FileLookup.Id WHERE FileLookup.BlocksetID != -100 ORDER BY FilesetId DESC LIMIT 1");
                Assert.AreEqual(1, cmd.SetCommandAndParameters("DELETE FROM FileLookup WHERE Id = @FileId").SetParameterValue("@FileId", fileId).ExecuteNonQuery());
            }

            // Should catch this in validation
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                Assert.Throws<DatabaseInconsistencyException>(() => c.Backup(new[] { this.DATAFOLDER }));

            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var repairResults = c.Repair();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Where(x => x.IndexOf("RemoteFilesNewerThanLocalDatabase", StringComparison.OrdinalIgnoreCase) < 0).Count());
                Assert.AreEqual(2, Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*").Count());
                Assert.AreEqual(2, c.List().Filesets.Count());
            }

            // Check that entry was recreated
            using (var con = SQLiteLoader.LoadConnection(options["dbpath"], 0))
            using (var cmd = con.CreateCommand())
            {
                Assert.AreEqual(filesetEntries, cmd.ExecuteScalarInt64("SELECT COUNT(*) FROM FilesetEntry"));
                Assert.AreEqual(fileLookupEntries, cmd.ExecuteScalarInt64("SELECT COUNT(*) FROM FileLookup"));
            }
        }
    }
}