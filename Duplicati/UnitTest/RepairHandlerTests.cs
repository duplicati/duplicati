// Copyright (C) 2026, The Duplicati Team
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
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

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
        public async Task RepairMissingBlocklistHashesAsync(int dataSize)
        {
            var data = new byte[dataSize];
            var rng = new Random();
            for (var k = 0; k < 2; k++)
            {
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(this.DATAFOLDER, $"{k}"), data);
            }

            var options = new Dictionary<string, string>(this.TestOptions);
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = await c.BackupAsync([this.DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Mimic a damaged database that needs to be repaired.
            const string selectStatement = @"SELECT BlocksetID, ""Index"", Hash FROM BlocklistHash ORDER BY Hash ASC";
            var expectedBlocksetIDs = new List<int>();
            var expectedIndexes = new List<int>();
            var expectedHashes = new List<string>();
            using (var connection = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
            {
                // Read the contents of the BlocklistHash table so that we can
                // compare them to the contents after the repair operation.
                using (var command = connection.CreateCommand())
                using (var reader = command.ExecuteReader(selectStatement))
                    while (reader.Read())
                    {
                        expectedBlocksetIDs.Add(reader.GetInt32(0));
                        expectedIndexes.Add(reader.GetInt32(1));
                        expectedHashes.Add(reader.ConvertValueToString(2));
                    }

                using (var command = connection.CreateCommand())
                {
                    command.ExecuteNonQuery(@"DELETE FROM BlocklistHash");
                    using (IDataReader reader = command.ExecuteReader(selectStatement))
                    {
                        Assert.IsFalse(reader.Read());
                    }
                }
            }

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var repairResults = await c.RepairAsync();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Count());
            }

            var repairedBlocksetIDs = new List<int>();
            var repairedIndexes = new List<int>();
            var repairedHashes = new List<string>();
            using (var connection = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
            {
                using (var command = connection.CreateCommand())
                using (var reader = command.ExecuteReader(selectStatement))
                    while (reader.Read())
                    {
                        repairedBlocksetIDs.Add(reader.GetInt32(0));
                        repairedIndexes.Add(reader.GetInt32(1));
                        repairedHashes.Add(reader.ConvertValueToString(2));
                    }
            }

            CollectionAssert.AreEqual(expectedBlocksetIDs, repairedBlocksetIDs);
            CollectionAssert.AreEqual(expectedIndexes, repairedIndexes);
            CollectionAssert.AreEqual(expectedHashes, repairedHashes);

            // A subsequent backup should run without errors.
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = await c.BackupAsync([this.DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }
        }

        [Test]
        [Category("RepairHandler")]
        [TestCase("true")]
        [TestCase("false")]
        public async Task RepairMissingIndexFilesAsync(string noEncryption)
        {
            var options = new Dictionary<string, string>(this.TestOptions) { ["no-encryption"] = noEncryption };
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = await c.BackupAsync([this.DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            var dindexFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*.dindex.*").ToArray();
            Assert.Greater(dindexFiles.Length, 0);
            foreach (var f in dindexFiles)
                File.Delete(f);

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var repairResults = await c.RepairAsync();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Count());
            }

            var recreatedIndexFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*dindex*").ToArray();
            Assert.AreEqual(dindexFiles.Length, recreatedIndexFiles.Length);
        }

        [Test]
        [Category("RepairHandler"), Category("Targeted")]
        public async Task RepairMissingIndexFilesBlocklistAsync()
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
                await s.WriteAsync(new byte[size], 0, size);
            }

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = await c.BackupAsync(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
                using (var s = File.OpenWrite(filename))
                {
                    // Change first byte
                    s.WriteByte(1);
                }
                backupResults = await c.BackupAsync(new[] { this.DATAFOLDER });
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
                var repairResults = await c.RepairAsync();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Count());
            }

            var recreatedIndexFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*dindex*").ToArray();
            Assert.AreEqual(dindexFiles.Length, recreatedIndexFiles.Length);
        }

        [Test]
        [Category("RepairHandler"), Category("Targeted")]
        public async Task RecreateWithDefectIndexBlockAsync()
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
                await s.WriteAsync(new byte[size], 0, size);
            }

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = await c.BackupAsync(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
                using (var s = File.OpenWrite(filename))
                {
                    // Change first byte
                    s.WriteByte(1);
                }
                backupResults = await c.BackupAsync(new[] { this.DATAFOLDER });
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
                                        await s.CopyToAsync(ms);
                                        ms.Position = 0;
                                        ms.WriteByte(42);
                                        ms.Position = 0;
                                        await ms.CopyToAsync(d);
                                    }
                                }
                                else
                                {
                                    await s.CopyToAsync(d);
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
                var repairResults = await c.RepairAsync();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(1, repairResults.Warnings.Count());
            }

            File.Delete(dindexFiles[0]);

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var repairResults = await c.RepairAsync();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Count());
            }

            // Delete database and recreate
            File.Delete(options["dbpath"]);

            // No errors with recreated index file
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var repairResults = await c.RepairAsync();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Count());
            }

        }

        [Test]
        [Category("RepairHandler"), Category("Targeted")]
        public async Task AutoCleanupRepairDoesNotLockDatabaseAsync()
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
                var backupResults = await c.BackupAsync([this.DATAFOLDER]);
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
                var backupResults = await c.BackupAsync([this.DATAFOLDER]);
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
                var backupResults = await c.BackupAsync([this.DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count());
                // First will recreate (4 files + 1 warning)
                Assert.AreEqual(5, backupResults.Warnings.Count());
            }
        }

        [Test]
        [Category("RepairHandler")]
        [TestCase("true")]
        [TestCase("false")]
        public async Task RepairExtraIndexFilesAsync(string noEncryption)
        {
            // Extra index files will be added to the database and should have a correct link established
            var options = new Dictionary<string, string>(this.TestOptions) { ["no-encryption"] = noEncryption };
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            var dindexFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*dindex*").ToArray();
            Assert.Greater(dindexFiles.Length, 0);
            var origFile = dindexFiles.First();
            // Duplicate index file
            var dupFile = Path.Combine(TARGETFOLDER, Path.GetFileName(origFile).Replace(".dindex", "1.dindex"));
            File.Copy(origFile, dupFile);

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.RepairAsync());

            // Check database block link
            using (var db = await LocalDatabase.CreateLocalDatabaseAsync(DBFILE, "Test", true, null, CancellationToken.None).ConfigureAwait(false))
            {
                var indexVolumeId = await db
                    .GetRemoteVolumeIDAsync(Path.GetFileName(origFile), CancellationToken.None)
                    .ConfigureAwait(false);
                var duplicateVolumeId = await db
                    .GetRemoteVolumeIDAsync(Path.GetFileName(dupFile), CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.AreNotEqual(-1, indexVolumeId);
                Assert.AreNotEqual(-1, duplicateVolumeId);

                using (var cmd = db.Connection.CreateCommand())
                {
                    var sql = @"SELECT ""BlockVolumeID"" FROM ""IndexBlockLink"" WHERE ""IndexVolumeID"" = @VolumeId";
                    var linkedIndexId = await cmd
                        .SetCommandAndParameters(sql)
                        .SetParameterValue("@VolumeId", indexVolumeId)
                        .ExecuteScalarInt64Async(-1, CancellationToken.None)
                        .ConfigureAwait(false);
                    var linkedDuplicateId = await cmd
                        .SetCommandAndParameters(sql)
                        .SetParameterValue("@VolumeId", duplicateVolumeId)
                        .ExecuteScalarInt64Async(-1, CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.AreEqual(linkedIndexId, linkedDuplicateId);
                }
            }
        }

        [Test]
        [Category("RepairHandler")]
        public async Task RepairMissingDlistFileAsync()
        {
            // Make two backups
            var options = this.TestOptions;
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            ModifyFile();
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // Find and delete a dlist file
            var dlistFile = Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*").First();
            File.Delete(dlistFile);

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IRepairResults repairResults = await c.RepairAsync();
                TestUtils.AssertResults(repairResults);
                Assert.AreEqual(2, Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*").Count());
            }
        }

        [Test]
        [Category("RepairHandler")]
        [TestCase("true")]
        [TestCase("false")]
        public async Task RepairMissingDlistVolumeAsync(bool deleteRemoteFile)
        {
            // Make two backups
            var options = this.TestOptions;
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            ModifyFile();
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // Find a dlist file
            var dlistFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*")
                .OrderByDescending(x => x)
                .AsEnumerable();

            // Randomly delete the first or last file
            if (Random.Shared.Next(2) == 0)
                dlistFiles = dlistFiles.Reverse();

            var dlistFile = dlistFiles.First();
            using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
            using (var cmd = con.CreateCommand("DELETE FROM RemoteVolume WHERE Name = @Name"))
                Assert.AreEqual(1, await cmd.SetParameterValue("@Name", Path.GetFileName(dlistFile)).ExecuteNonQueryAsync());

            if (deleteRemoteFile)
                File.Delete(dlistFile);

            // Should catch this in validation
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                Assert.ThrowsAsync<DatabaseInconsistencyException>(async () => await c.BackupAsync(new[] { this.DATAFOLDER }));

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var repairResults = await c.RepairAsync();
                TestUtils.AssertResults(repairResults);
                Assert.AreEqual(2, Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*").Count());
                Assert.AreEqual(2, (await c.ListAsync()).Filesets.Count());
            }

            // Check that the entry was recreated
            using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
            using (var cmd = con.CreateCommand("SELECT COUNT(*) FROM RemoteVolume WHERE Name LIKE '%.dlist.%' AND State != @State"))
                Assert.AreEqual(2, cmd.SetParameterValue("@State", RemoteVolumeState.Deleted.ToString()).ExecuteScalarInt64(-1));

            // Delete the database and check that the result is correct
            File.Delete(options["dbpath"]);

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var repairResults = await c.RepairAsync();
                TestUtils.AssertResults(repairResults);
                Assert.AreEqual(2, (await c.ListAsync()).Filesets.Count());

                var r = await c.TestAsync(long.MaxValue);
                Assert.AreEqual(0, r.Errors.Count());
                Assert.AreEqual(0, r.Warnings.Count());
                Assert.IsFalse(r.Verifications.Any(p => p.Value.Any()));
            }
        }

        [Test]
        [Category("RepairHandler")]
        public async Task RepairMissingFilesetVolumeAsync()
        {
            // Make two backups
            var options = this.TestOptions;
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // Make room for a new backup
            Thread.Sleep(2000);

            ModifyFile();
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // Remove a fileset
            using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
            using (var cmd = con.CreateCommand())
            {
                var filesetId = cmd.ExecuteScalarInt64("SELECT Id FROM Fileset ORDER BY Id DESC LIMIT 1");
                Assert.AreEqual(1, await cmd.SetCommandAndParameters("DELETE FROM Fileset WHERE Id = @FilesetId")
                    .SetParameterValue("@FilesetId", filesetId)
                    .ExecuteNonQueryAsync());

                // No longer needed because the recreate will wipe the table before restoring
                // cmd.SetCommandAndParameters("DELETE FROM FilesetEntry WHERE FilesetId = @FilesetId")
                //     .SetParameterValue("@FilesetId", filesetId)
                //     .ExecuteNonQuery();
            }

            // Should catch this in validation
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                Assert.ThrowsAsync<DatabaseInconsistencyException>(() => c.BackupAsync(new[] { this.DATAFOLDER }));

            // Since we have removed the entry that is being checked here, we need to disable the check
            var repairOptions = new Dictionary<string, string>(options) { ["repair-ignore-outdated-database"] = "true" };
            using (var c = new Controller("file://" + this.TARGETFOLDER, repairOptions, null))
            {
                var repairResults = await c.RepairAsync();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Where(x => x.IndexOf("RemoteFilesNewerThanLocalDatabase", StringComparison.OrdinalIgnoreCase) < 0).Count());
                Assert.AreEqual(2, Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*").Count());
                Assert.AreEqual(2, (await c.ListAsync()).Filesets.Count());
            }

            // Check that entry was recreated
            using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
            using (var cmd = con.CreateCommand())
                Assert.AreEqual(2, cmd.ExecuteScalarInt64("SELECT COUNT(*) FROM Fileset"));
        }

        [Test]
        [Category("RepairHandler")]
        public async Task ManufactureMissingFilesAsync()
        {
            // Make two backups
            var options = this.TestOptions;
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // Make room for a new backup
            Thread.Sleep(2000);

            ModifyFile();
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // Remove a fileset
            long filesetEntries;
            long fileLookupEntries;
            using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
            using (var cmd = con.CreateCommand())
            {
                filesetEntries = cmd.ExecuteScalarInt64("SELECT COUNT(*) FROM FilesetEntry");
                fileLookupEntries = cmd.ExecuteScalarInt64("SELECT COUNT(*) FROM FileLookup");
                var fileId = cmd.ExecuteScalarInt64("SELECT FileId FROM FilesetEntry INNER JOIN FileLookup ON FilesetEntry.FileID = FileLookup.Id WHERE FileLookup.BlocksetID != -100 ORDER BY FilesetId DESC LIMIT 1");
                Assert.AreEqual(1, await cmd.SetCommandAndParameters("DELETE FROM FileLookup WHERE Id = @FileId").SetParameterValue("@FileId", fileId).ExecuteNonQueryAsync());
            }

            // Should catch this in validation
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                Assert.ThrowsAsync<DatabaseInconsistencyException>(() => c.BackupAsync(new[] { this.DATAFOLDER }));

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var repairResults = await c.RepairAsync();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Where(x => x.IndexOf("RemoteFilesNewerThanLocalDatabase", StringComparison.OrdinalIgnoreCase) < 0).Count());
                Assert.AreEqual(2, Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*").Count());
                Assert.AreEqual(2, (await c.ListAsync()).Filesets.Count());
            }

            // Check that entry was recreated
            using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
            using (var cmd = con.CreateCommand())
            {
                Assert.AreEqual(filesetEntries, cmd.ExecuteScalarInt64("SELECT COUNT(*) FROM FilesetEntry"));
                Assert.AreEqual(fileLookupEntries, cmd.ExecuteScalarInt64("SELECT COUNT(*) FROM FileLookup"));
            }
        }

        [Test]
        [Category("RepairHandler")]
        public async Task RepairReplacesZeroLengthMetadataAsync()
        {
            var options = new Dictionary<string, string>(this.TestOptions);
            File.WriteAllText(Path.Combine(this.DATAFOLDER, "a.txt"), "a");
            File.WriteAllText(Path.Combine(this.DATAFOLDER, "b.txt"), "b");
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
            using (var cmd = con.CreateCommand())
            {
                var lookupId = cmd.ExecuteScalarInt64("SELECT ID FROM FileLookup LIMIT 1");
                var metaId = cmd.SetCommandAndParameters("SELECT MetadataID FROM FileLookup WHERE ID = @Id")
                    .SetParameterValue("@Id", lookupId).ExecuteScalarInt64(-1);
                var blocksetId = cmd.SetCommandAndParameters("SELECT BlocksetID FROM Metadataset WHERE ID = @Id")
                    .SetParameterValue("@Id", metaId).ExecuteScalarInt64(-1);
                await cmd.SetCommandAndParameters("UPDATE Blockset SET Length = 0 WHERE ID = @Id")
                    .SetParameterValue("@Id", blocksetId).ExecuteNonQueryAsync();
            }

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var res = await c.RepairAsync();
                Assert.AreEqual(0, res.Errors.Count());
                Assert.AreEqual(0, res.Warnings.Count());
            }
            using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
            using (var cmd = con.CreateCommand())
            {
                var remaining = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM Metadataset JOIN Blockset ON Metadataset.BlocksetID = Blockset.ID WHERE Blockset.Length = 0");
                Assert.AreEqual(0, remaining, "Zero-length metadata should have been replaced");
            }

            // The repaired database must pass consistency verification, including the file list check.
            var vopts = new Options(options);
            await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(options["dbpath"], "verify", true, null, CancellationToken.None).ConfigureAwait(false))
                await db.VerifyConsistencyAsync(vopts.Blocksize, vopts.BlockhashSize, true, CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        [Category("RepairHandler")]
        public async Task RepairRelinksEmptyMetadataBlockWhenBlocksetEntryMissingAsync()
        {
            // The empty ({}) metadata blockset can lose its BlocksetEntry (and length) while the
            // backing block still exists in the database. The metadata repair must relink the
            // existing block (a database only operation, no remote access) so folders survive the
            // file list consistency check. skip-metadata makes every entry, including the folder,
            // share the empty metadata blockset.
            var testopts = new Dictionary<string, string>(this.TestOptions) { ["skip-metadata"] = "true" };
            Directory.CreateDirectory(Path.Combine(this.DATAFOLDER, "sub"));
            File.WriteAllText(Path.Combine(this.DATAFOLDER, "sub", "a.txt"), "a");
            using (var c = new Controller("file://" + this.TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            var dbpath = testopts["dbpath"];

            // Break the shared empty-metadata blockset: drop its BlocksetEntry and zero its length,
            // while leaving the backing {} block in the Block table for the repair to relink.
            long emptyBlocksetId;
            using (var con = await SQLiteLoader.LoadConnectionAsync(dbpath))
            using (var cmd = con.CreateCommand())
            {
                emptyBlocksetId = cmd.ExecuteScalarInt64("SELECT BlocksetID FROM Metadataset LIMIT 1");
                Assert.Greater(emptyBlocksetId, -1, "Expected a metadata blockset");

                cmd.SetCommandAndParameters("DELETE FROM BlocksetEntry WHERE BlocksetID = @Id")
                    .SetParameterValue("@Id", emptyBlocksetId).ExecuteNonQuery();
                cmd.SetCommandAndParameters("UPDATE Blockset SET Length = 0 WHERE ID = @Id")
                    .SetParameterValue("@Id", emptyBlocksetId).ExecuteNonQuery();

                var brokenBefore = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM Metadataset JOIN Blockset ON Metadataset.BlocksetID = Blockset.ID WHERE Blockset.Length = 0");
                Assert.Greater(brokenBefore, 0, "Test setup should produce zero-length metadata entries");
            }

            var opts = new Options(testopts);
            await using (var db = await LocalRepairDatabase.CreateRepairDatabaseAsync(dbpath, null, CancellationToken.None).ConfigureAwait(false))
            {
                await db.FixEmptyMetadatasetsAsync(opts, CancellationToken.None).ConfigureAwait(false);
            }

            using (var con = await SQLiteLoader.LoadConnectionAsync(dbpath))
            using (var cmd = con.CreateCommand())
            {
                // The empty-metadata blockset must have its block entry and length restored.
                var entries = cmd.SetCommandAndParameters("SELECT COUNT(*) FROM BlocksetEntry WHERE BlocksetID = @Id")
                    .SetParameterValue("@Id", emptyBlocksetId).ExecuteScalarInt64(0);
                Assert.Greater(entries, 0, "The missing block entry should have been relinked");

                var length = cmd.SetCommandAndParameters("SELECT Length FROM Blockset WHERE ID = @Id")
                    .SetParameterValue("@Id", emptyBlocksetId).ExecuteScalarInt64(-1);
                Assert.Greater(length, 0, "The blockset length should have been restored");

                var nullMeta = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM FileLookup WHERE MetadataID IS NULL");
                Assert.AreEqual(0, nullMeta, "No files should have a NULL metadata id after repair");
            }

            // The database must be consistent after the repair completes, including the file list
            // verification that requires folder metadata blocksets to have a real block entry.
            await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(dbpath, "verify", true, null, CancellationToken.None).ConfigureAwait(false))
                await db.VerifyConsistencyAsync(opts.Blocksize, opts.BlockhashSize, true, CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        [Category("RepairHandler")]
        public async Task FixEmptyMetadataSkipsWhenReplacementDisabledAsync()
        {
            // With --disable-replace-missing-metadata, FixEmptyMetadatasets must not attempt to
            // replace or consolidate metadata (that is the purge-broken-files job); it should leave
            // the zero-length entries untouched and not throw.
            var testopts = new Dictionary<string, string>(this.TestOptions)
            {
                ["skip-metadata"] = "true",
                ["disable-replace-missing-metadata"] = "true"
            };
            File.WriteAllText(Path.Combine(this.DATAFOLDER, "a.txt"), "a");
            File.WriteAllText(Path.Combine(this.DATAFOLDER, "b.txt"), "b");
            using (var c = new Controller("file://" + this.TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            var dbpath = testopts["dbpath"];

            long brokenBefore;
            using (var con = await SQLiteLoader.LoadConnectionAsync(dbpath))
            using (var cmd = con.CreateCommand())
            {
                cmd.ExecuteNonQuery(@"
                    DELETE FROM BlocksetEntry
                    WHERE BlocksetID IN (SELECT BlocksetID FROM Metadataset)");
                cmd.ExecuteNonQuery(@"
                    UPDATE Blockset
                    SET Length = 0
                    WHERE ID IN (SELECT BlocksetID FROM Metadataset)");
                brokenBefore = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM Metadataset JOIN Blockset ON Metadataset.BlocksetID = Blockset.ID WHERE Blockset.Length = 0");
                Assert.Greater(brokenBefore, 0, "Test setup should produce zero-length metadata entries");
            }

            var opts = new Options(testopts);
            await using (var db = await LocalRepairDatabase.CreateRepairDatabaseAsync(dbpath, null, CancellationToken.None).ConfigureAwait(false))
            {
                // Should complete without throwing and without modifying the entries.
                await db.FixEmptyMetadatasetsAsync(opts, CancellationToken.None).ConfigureAwait(false);
            }

            using (var con = await SQLiteLoader.LoadConnectionAsync(dbpath))
            using (var cmd = con.CreateCommand())
            {
                var brokenAfter = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM Metadataset JOIN Blockset ON Metadataset.BlocksetID = Blockset.ID WHERE Blockset.Length = 0");
                Assert.AreEqual(brokenBefore, brokenAfter, "Zero-length metadata entries should be left untouched for purge-broken-files to handle");
            }
        }

        [Test]
        [Category("RepairHandler")]
        public async Task FixEmptyMetadataFailsForFoldersWhenNoReplacementBlockAsync()
        {
            // When a folder/symlink has missing metadata and no empty ({}) metadata block exists to
            // rebuild from, the repair cannot keep the entry consistently (the file list check
            // requires a real metadata block). It must fail with actionable guidance rather than
            // produce an inconsistent database.
            var testopts = new Dictionary<string, string>(this.TestOptions);
            Directory.CreateDirectory(Path.Combine(this.DATAFOLDER, "sub"));
            File.WriteAllText(Path.Combine(this.DATAFOLDER, "sub", "a.txt"), "a");
            using (var c = new Controller("file://" + this.TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            var dbpath = testopts["dbpath"];

            // Break the metadata of the folder entries and remove all block entries so no {} block
            // can be located for a rebuild.
            using (var con = await SQLiteLoader.LoadConnectionAsync(dbpath))
            using (var cmd = con.CreateCommand())
            {
                cmd.ExecuteNonQuery($@"
                    DELETE FROM BlocksetEntry
                    WHERE BlocksetID IN (
                        SELECT M.BlocksetID
                        FROM Metadataset M
                        JOIN FileLookup F ON F.MetadataID = M.ID
                        WHERE F.BlocksetID = {LocalDatabase.FOLDER_BLOCKSET_ID})");

                cmd.ExecuteNonQuery($@"
                    UPDATE Blockset
                    SET Length = 0
                    WHERE ID IN (
                        SELECT M.BlocksetID
                        FROM Metadataset M
                        JOIN FileLookup F ON F.MetadataID = M.ID
                        WHERE F.BlocksetID = {LocalDatabase.FOLDER_BLOCKSET_ID})");

                var brokenBefore = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM Metadataset JOIN Blockset ON Metadataset.BlocksetID = Blockset.ID WHERE Blockset.Length = 0");
                Assert.Greater(brokenBefore, 0, "Test setup should produce zero-length folder metadata entries");
            }

            var opts = new Options(testopts);
            await using (var db = await LocalRepairDatabase.CreateRepairDatabaseAsync(dbpath, null, CancellationToken.None).ConfigureAwait(false))
            {
                var ex = Assert.ThrowsAsync<UserInformationException>(async () =>
                    await db.FixEmptyMetadatasetsAsync(opts, CancellationToken.None).ConfigureAwait(false));
                Assert.AreEqual("MetadataRepairFailed", ex.HelpID);
            }
        }

        [Test]
        [Category("RepairHandler")]
        public async Task BackupSeedsAndPinsEmptyMetadataBlockAsync()
        {
            // The backup should always seed the empty ({}) metadata block, and deleting all versions
            // plus compacting must not evict it, so it remains available for database-only repairs.
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["keep-versions"] = "1",
                ["no-auto-compact"] = "false",
                ["threshold"] = "0"
            };
            File.WriteAllText(Path.Combine(this.DATAFOLDER, "a.txt"), "a");
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            var opts = new Options(options);
            var emptyMeta = Duplicati.Library.Main.Utility.WrapMetadata(new Dictionary<string, string>(), opts);
            string emptyBlockHash;
            using (var blockhasher = HashFactory.CreateHasher(opts.BlockHashAlgorithm))
                emptyBlockHash = Convert.ToBase64String(blockhasher.ComputeHash(emptyMeta.Blob));
            var emptyBlockSize = emptyMeta.Blob.Length;

            // The empty metadata block must be present right after the backup.
            using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
            using (var cmd = con.CreateCommand())
            {
                var count = cmd.SetCommandAndParameters("SELECT COUNT(*) FROM Block WHERE Hash = @Hash AND Size = @Size")
                    .SetParameterValue("@Hash", emptyBlockHash)
                    .SetParameterValue("@Size", emptyBlockSize)
                    .ExecuteScalarInt64(0);
                Assert.Greater(count, 0, "The empty metadata block should be seeded by the backup");
            }

            // Make several more backups with changing content so old versions are pruned and the
            // volumes holding the original data become compactable.
            for (var i = 0; i < 3; i++)
            {
                File.WriteAllText(Path.Combine(this.DATAFOLDER, "a.txt"), $"data-{i}-{new string('x', 2048)}");
                using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                    TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));
            }

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                await c.CompactAsync();

            // After pruning and compaction, the pinned empty metadata block must still exist and not
            // be marked as deleted.
            using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
            using (var cmd = con.CreateCommand())
            {
                var count = cmd.SetCommandAndParameters("SELECT COUNT(*) FROM Block WHERE Hash = @Hash AND Size = @Size")
                    .SetParameterValue("@Hash", emptyBlockHash)
                    .SetParameterValue("@Size", emptyBlockSize)
                    .ExecuteScalarInt64(0);
                Assert.Greater(count, 0, "The empty metadata block should be pinned and survive compaction");

                var deleted = cmd.SetCommandAndParameters("SELECT COUNT(*) FROM DeletedBlock WHERE Hash = @Hash AND Size = @Size")
                    .SetParameterValue("@Hash", emptyBlockHash)
                    .SetParameterValue("@Size", emptyBlockSize)
                    .ExecuteScalarInt64(0);
                Assert.AreEqual(0, deleted, "The pinned empty metadata block should never be marked deleted");
            }
        }
    }
}