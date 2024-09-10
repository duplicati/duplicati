// Copyright (C) 2024, The Duplicati Team
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
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
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
                IBackupResults backupResults = c.Backup([this.DATAFOLDER]);
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

            var dindexFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*dindex*").ToArray();
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

            foreach (var file in dindexFiles)
            {
                Assert.IsTrue(File.Exists(Path.Combine(this.TARGETFOLDER, file)));
            }
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

            var dindexFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*dindex*").ToArray();
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

            foreach (var file in dindexFiles)
            {
                Assert.IsTrue(File.Exists(Path.Combine(this.TARGETFOLDER, file)));
            }
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
    }
}
