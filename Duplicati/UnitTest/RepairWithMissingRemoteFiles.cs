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
using System.IO;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class RepairWithMissingRemoteFiles : BasicSetupHelper
    {
        // TODO: Add more tests to cover the following scenarios:
        // Test where a single block in a large file is missing + run purge-broken-files
        // Test that fetching remote sources for blocks is working

        [Test]
        [Category("Targeted")]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(true, false, false)]
        public void TestRepairWithMissingDblocks(bool deleteDblockFiles, bool deleteIndexFiles, bool deleteDlistFiles)
        {
            var testopts = TestOptions.Expand(new { blocksize = "1kb", no_encryption = true, rebuild_missing_dblock_files = true, number_of_retries = 0 });

            // 1. Make a backup of a single file (1 block + 3 bytes for a block with less data)
            var data = new byte[1024 + 3];
            data.AsSpan().Fill((byte)'a');
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // 2. Delete files
            if (deleteDblockFiles)
            {
                var dblockFiles = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dblockFiles)
                    File.Delete(file);
            }

            if (deleteIndexFiles)
            {
                var dindexFiles = Directory.GetFiles(TARGETFOLDER, "*.dindex.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dindexFiles)
                    File.Delete(file);
            }

            if (deleteDlistFiles)
            {
                var dlistFiles = Directory.GetFiles(TARGETFOLDER, "*.dlist.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dlistFiles)
                    File.Delete(file);
            }

            // 3. Try to repair the remote destination
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());

            // 4. Test that the database is now valid
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Test());
        }

        [Test]
        [Category("Targeted")]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(true, false, false)]
        public void TestRepairWithMissingDblocksAndMultipleSources(bool deleteDblockFiles, bool deleteIndexFiles, bool deleteDlistFiles)
        {
            var testopts = TestOptions.Expand(new { blocksize = "1kb", no_encryption = true, rebuild_missing_dblock_files = true });

            // 1. Make some shared data
            var data = new byte[1024 + 3];
            data.AsSpan().Fill((byte)'a');
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b"), data.AsSpan().Slice(0, 1024).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "c"), data.AsSpan().Slice(0, 1024).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "d"), data.AsSpan().Slice(1024, 3).ToArray());
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // 2. Delete files
            if (deleteDblockFiles)
            {
                var dblockFiles = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dblockFiles)
                    File.Delete(file);
            }

            if (deleteIndexFiles)
            {
                var dindexFiles = Directory.GetFiles(TARGETFOLDER, "*.dindex.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dindexFiles)
                    File.Delete(file);
            }

            if (deleteDlistFiles)
            {
                var dlistFiles = Directory.GetFiles(TARGETFOLDER, "*.dlist.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dlistFiles)
                    File.Delete(file);
            }

            // 3. Try to repair the remote destination
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                TestUtils.AssertResults(c.Repair());
                TestUtils.AssertResults(c.Test());
            }
        }

        [Test]
        [Category("Targeted")]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(true, false, false)]
        public void TestRepairWithDataFillingMultipleBlocklists(bool deleteDblockFiles, bool deleteIndexFiles, bool deleteDlistFiles)
        {
            var testopts = TestOptions.Expand(new { blocksize = "1kb", no_encryption = true, rebuild_missing_dblock_files = true });

            // 1. Make some shared data 1kb blocks, 32 bytes hash, 3 blocklists, and 5 bytes extra
            var data = new byte[1024 * 32 * 3 + 5];
            data.AsSpan().Fill((byte)'a');
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b"), data.AsSpan().Slice(1024, 3).ToArray());
            Random.Shared.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "c"), data);
            Random.Shared.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "d"), data.AsSpan().Slice(0, 1028).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "e"), data.AsSpan().Slice(4098, 67).ToArray());
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // 2. Delete files
            if (deleteDblockFiles)
            {
                var dblockFiles = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dblockFiles)
                    File.Delete(file);
            }

            if (deleteIndexFiles)
            {
                var dindexFiles = Directory.GetFiles(TARGETFOLDER, "*.dindex.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dindexFiles)
                    File.Delete(file);
            }

            if (deleteDlistFiles)
            {
                var dlistFiles = Directory.GetFiles(TARGETFOLDER, "*.dlist.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dlistFiles)
                    File.Delete(file);
            }

            // 3. Try to repair the remote destination
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                TestUtils.AssertResults(c.Repair());
                TestUtils.AssertResults(c.Test());
            }
        }

        [Test]
        [Category("Targeted")]
        [TestCase(true, true, true)]
        [TestCase(true, false, false)]
        public void TestRepairWithSmallFileOnly(bool deleteDblockFiles, bool deleteIndexFiles, bool deleteDlistFiles)
        {
            var testopts = TestOptions.Expand(new { blocksize = "1kb", no_encryption = true, rebuild_missing_dblock_files = true });

            // 1. Make some shared data
            var data = new byte[3];
            data.AsSpan().Fill((byte)'a');
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // 2. Delete files
            if (deleteDblockFiles)
            {
                var dblockFiles = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dblockFiles)
                    File.Delete(file);
            }

            if (deleteIndexFiles)
            {
                var dindexFiles = Directory.GetFiles(TARGETFOLDER, "*.dindex.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dindexFiles)
                    File.Delete(file);
            }

            if (deleteDlistFiles)
            {
                var dlistFiles = Directory.GetFiles(TARGETFOLDER, "*.dlist.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dlistFiles)
                    File.Delete(file);
            }

            // 3. Try to repair the remote destination
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                TestUtils.AssertResults(c.Repair());
                TestUtils.AssertResults(c.Test());
            }
        }

        [Test]
        [Category("Targeted")]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        [TestCase(true, false, true)]
        public void TestRepairWithDataFillingMultipleBlocklistsRandom(bool deleteDblockFiles, bool deleteIndexFiles, bool deleteDlistFiles)
        {
            var testopts = TestOptions.Expand(new { blocksize = "1kb", no_encryption = true, rebuild_missing_dblock_files = true });

            // 1. Make some shared data 1kb blocks, 32 bytes hash, 3 blocklists, and 1028 bytes extra
            var data = new byte[1024 * 32 * 3 + 1028];
            Random.Shared.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // 2. Delete files
            if (deleteDblockFiles)
            {
                var dblockFiles = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dblockFiles)
                    File.Delete(file);
            }

            if (deleteIndexFiles)
            {
                var dindexFiles = Directory.GetFiles(TARGETFOLDER, "*.dindex.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dindexFiles)
                    File.Delete(file);
            }

            if (deleteDlistFiles)
            {
                var dlistFiles = Directory.GetFiles(TARGETFOLDER, "*.dlist.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dlistFiles)
                    File.Delete(file);
            }

            // 3. Try to repair the remote destination
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                TestUtils.AssertResults(c.Repair());
                TestUtils.AssertResults(c.Test());
            }

            // 4. Verify that restore works
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { restore_path = RESTOREFOLDER }), null))
                TestUtils.AssertResults(c.Restore(null));

            TestUtils.AssertDirectoryTreesAreEquivalent(DATAFOLDER, RESTOREFOLDER, !Library.Utility.Utility.ParseBoolOption(testopts, "skip-metadata"), "Restore");
        }

        [Test]
        [Category("Targeted")]
        [TestCase(true, true, true)]
        [TestCase(true, false, false)]
        public void TestRepairWithDryRun(bool deleteDblockFiles, bool deleteIndexFiles, bool deleteDlistFiles)
        {
            var testopts = TestOptions.Expand(new { blocksize = "1kb", no_encryption = true, rebuild_missing_dblock_files = true });

            // 1. Make some random data
            var data = new byte[1028];
            Random.Shared.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // 2. Delete files
            if (deleteDblockFiles)
            {
                var dblockFiles = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dblockFiles)
                    File.Delete(file);
            }

            if (deleteIndexFiles)
            {
                var dindexFiles = Directory.GetFiles(TARGETFOLDER, "*.dindex.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dindexFiles)
                    File.Delete(file);
            }

            if (deleteDlistFiles)
            {
                var dlistFiles = Directory.GetFiles(TARGETFOLDER, "*.dlist.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dlistFiles)
                    File.Delete(file);
            }

            (long remoteVolumes, long duplicatedBlocks, List<long> blockVolumeIds, List<(long, long)> indexVolumeLinks) GetDbSignature()
            {
                using var db = SQLiteLoader.LoadConnection(DBFILE);
                using var cmd = db.CreateCommand();

                var remoteVolumes = cmd.ExecuteScalarInt64("SELECT COUNT(*) FROM RemoteVolume");
                var duplicatedBlocks = cmd.ExecuteScalarInt64("SELECT COUNT(*) FROM DuplicateBlock");
                var blockVolumeIds = cmd.ExecuteReaderEnumerable("SELECT VolumeID FROM Block").Select(x => x.ConvertValueToInt64(0)).ToList();
                var indexVolumeLinks = cmd.ExecuteReaderEnumerable("SELECT IndexVolumeID, BlockVolumeID FROM IndexBlockLink").Select(x => (x.ConvertValueToInt64(0), x.ConvertValueToInt64(1))).ToList();
                return (remoteVolumes, duplicatedBlocks, blockVolumeIds, indexVolumeLinks);
            }

            var preSignature = GetDbSignature();

            // 3. Try to repair the remote destination
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { dry_run = true }), null))
                TestUtils.AssertResults(c.Repair());

            var postSignature = GetDbSignature();

            Assert.AreEqual(preSignature.remoteVolumes, postSignature.remoteVolumes, "Remote volumes count should be the same after dry run repair.");
            Assert.AreEqual(preSignature.duplicatedBlocks, postSignature.duplicatedBlocks, "Duplicated blocks count should be the same after dry run repair.");
            Assert.AreEqual(preSignature.blockVolumeIds.Count, postSignature.blockVolumeIds.Count, "Block volume IDs count should be the same after dry run repair.");
            Assert.AreEqual(preSignature.indexVolumeLinks.Count, postSignature.indexVolumeLinks.Count, "Index volume links count should be the same after dry run repair.");

            Assert.IsTrue(preSignature.blockVolumeIds.All(x => postSignature.blockVolumeIds.Contains(x)), "All block volume IDs should be present after dry run repair.");
            Assert.IsTrue(preSignature.indexVolumeLinks.All(x => postSignature.indexVolumeLinks.Contains(x)), "All index volume links should be present after dry run repair.");
        }

        [Test]
        [Category("Targeted")]
        public void TestRepairWithNoBlockAvailableFails()
        {
            var testopts = TestOptions.Expand(new { blocksize = "1kb", no_encryption = true, rebuild_missing_dblock_files = true });

            // 1. Make some shared data 1kb blocks, 32 bytes hash, 3 blocklists, and 1028 bytes extra
            var data = new byte[1024 * 32 * 3 + 1028];
            Random.Shared.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // 2. Delete files
            var dblockFiles = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly).ToList();
            foreach (var file in dblockFiles)
                File.Delete(file);

            // Destroy source data
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), new byte[0]);
            Directory.SetLastWriteTime(DATAFOLDER, DateTime.UtcNow.AddSeconds(1));

            // 3. Try to repair the remote destination
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Repair();
                Assert.IsTrue(res.Errors.Where(x => x.Contains("purge-broken-files")).Count() == 1, "Repair should fail with missing block data.");
            }
        }

        [Test]
        [Category("Targeted")]
        public void TestPartialRepairPossibleWithMissingMetadata()
        {
            var testopts = TestOptions.Expand(new
            {
                blocksize = "1kb",
                no_encryption = true,
                rebuild_missing_dblock_files = true
            });

            // 1. Make some shared data 1kb blocks, 32 bytes hash, 3 blocklists, and 1028 bytes extra
            var data = new byte[1024 * 32 * 3 + 1028];
            Random.Shared.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // 2. Delete files
            var dblockFiles = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly).ToList();
            foreach (var file in dblockFiles)
                File.Delete(file);

            // Destroy some source data
            Directory.SetLastWriteTime(DATAFOLDER, DateTime.UtcNow.AddSeconds(1));

            // 3. Try to repair the remote destination
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Repair();
                Assert.IsTrue(res.Warnings.Where(x => x.Contains("purge-broken-files")).Count() == 1, "Repair should warn about missing block data.");

                // Ensure that the partial repair did not clean up fully
                Assert.Throws<RemoteListVerificationException>(() => c.Test());

                // Purge broken files should be possible
                TestUtils.AssertResults(c.PurgeBrokenFiles(null));
                TestUtils.AssertResults(c.Test(int.MaxValue));
            }

            // 4. Verify that restore works
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { restore_path = RESTOREFOLDER }), null))
                TestUtils.AssertResults(c.Restore(null));

            TestUtils.AssertDirectoryTreesAreEquivalent(DATAFOLDER, RESTOREFOLDER, !Library.Utility.Utility.ParseBoolOption(testopts, "skip-metadata"), "Restore");
        }

        [Test]
        [Category("Targeted")]
        [TestCase(false)]
        [TestCase(true)]
        public void TestPartialRepairPossibleWithDeletedFiles(bool deleteLargeFile)
        {
            var testopts = TestOptions.Expand(new
            {
                blocksize = "1kb",
                no_encryption = true,
                rebuild_missing_dblock_files = true
            });

            // 1. Make some shared data 1kb blocks, 32 bytes hash, 3 blocklists, and 1028 bytes extra
            var data = new byte[1024 * 32 * 3 + 1028];
            Random.Shared.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b"), data.AsSpan().Slice(0, 1024).ToArray());

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // 2. Delete files
            var dblockFiles = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly).ToList();
            foreach (var file in dblockFiles)
                File.Delete(file);

            // Destroy some source data (also destroys the metadata)
            if (deleteLargeFile)
                File.Delete(Path.Combine(DATAFOLDER, "a"));
            else
                File.Delete(Path.Combine(DATAFOLDER, "b"));

            // 3. Try to repair the remote destination
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Repair();
                Assert.IsTrue(res.Warnings.Where(x => x.Contains("purge-broken-files")).Count() == 1, "Repair should warn about missing block data.");

                // Ensure that the partial repair did not clean up fully
                Assert.Throws<RemoteListVerificationException>(() => c.Test());

                // Purge broken files should be possible
                TestUtils.AssertResults(c.PurgeBrokenFiles(null));
                TestUtils.AssertResults(c.Test(int.MaxValue));

                var files = c.List("*").Files.ToList();
                Console.WriteLine("Files: " + string.Join(", ", files.Select(x => x.Path)));
            }

            // 4. Verify that restore works
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { restore_path = RESTOREFOLDER }), null))
                TestUtils.AssertResults(c.Restore(null));

            Assert.That(File.Exists(Path.Combine(RESTOREFOLDER, "a")), Is.EqualTo(!deleteLargeFile), "Restored file 'a' should be restored.");
            Assert.That(File.Exists(Path.Combine(RESTOREFOLDER, "b")), Is.EqualTo(true), "File 'b' should always be restored.");
            if (!deleteLargeFile)
                Assert.That(File.ReadAllBytes(Path.Combine(RESTOREFOLDER, "a")), Is.EqualTo(data), "Restored file 'a' should be the same as the original.");
            else
                Assert.That(File.ReadAllBytes(Path.Combine(RESTOREFOLDER, "b")), Is.EqualTo(data.AsSpan().Slice(0, 1024).ToArray()), "Restored file 'b' should be the same as the original.");
        }

        [Test]
        [Category("Targeted")]
        [TestCase(0)] // Large file only
        [TestCase(1)] // Small file only
        [TestCase(2)] // Both files
        [TestCase(3)] // Metadata only
        public void TestPartialRepairPossibleWithPartialData(int scenario)
        {
            var testopts = TestOptions.Expand(new { blocksize = "1kb", no_encryption = true, rebuild_missing_dblock_files = true });

            // 1. Make some shared data 1kb blocks, 32 bytes hash, 3 blocklists, and 1028 bytes extra
            var data = new byte[1024 * 32 * 3 + 1028];
            Random.Shared.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b"), data.AsSpan().Slice(0, 1024).ToArray());

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // 2. Delete files
            var dblockFiles = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly).ToList();
            foreach (var file in dblockFiles)
                File.Delete(file);

            var corruptLargeFile = scenario == 0 || scenario == 2;
            var corruptSmallFile = scenario == 1 || scenario == 2;
            var corruptBothFiles = corruptLargeFile && corruptSmallFile;
            var destroyMetadata = scenario == 3;

            // Destroy some source data (keep metadata)
            var prev = data[1];
            data[1] = 1;
            if (corruptLargeFile)
            {
                var timestamp = File.GetLastWriteTime(Path.Combine(DATAFOLDER, "a"));
                File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
                File.SetLastWriteTime(Path.Combine(DATAFOLDER, "a"), timestamp);
            }

            if (corruptSmallFile)
            {
                var timestamp = File.GetLastWriteTime(Path.Combine(DATAFOLDER, "b"));
                File.WriteAllBytes(Path.Combine(DATAFOLDER, "b"), data.AsSpan().Slice(0, 1024).ToArray());
                File.SetLastWriteTime(Path.Combine(DATAFOLDER, "b"), timestamp);
            }
            data[1] = prev;

            // Make sure that destroying the metadata does not destroy the files
            if (destroyMetadata)
            {
                File.SetLastWriteTime(Path.Combine(DATAFOLDER, "a"), DateTime.UtcNow.AddSeconds(1));
                File.SetLastWriteTime(Path.Combine(DATAFOLDER, "b"), DateTime.UtcNow.AddSeconds(1.1));
            }

            // 3. Try to repair the remote destination
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Repair();

                if (corruptBothFiles || destroyMetadata)
                {
                    Assert.IsTrue(res.Warnings.Where(x => x.Contains("purge-broken-files")).Count() == 1, "Repair should warn about missing block data.");

                    // Ensure that the partial repair did not clean up fully
                    Assert.Throws<RemoteListVerificationException>(() => c.Test());
                }
                else
                {
                    TestUtils.AssertResults(res);
                    TestUtils.AssertResults(c.Test(int.MaxValue));
                }

                // Purge broken files should be possible
                TestUtils.AssertResults(c.PurgeBrokenFiles(null));
                TestUtils.AssertResults(c.Test(int.MaxValue));
            }

            // 4. Verify that restore works
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { restore_path = RESTOREFOLDER }), null))
            {
                var res = c.Restore(null);
                if (corruptBothFiles)
                {
                    // We get a warning because neither file can be restored
                    Assert.That(res.Errors.Count(), Is.EqualTo(0), "Restore should not fail.");
                }
                else
                {
                    TestUtils.AssertResults(res);
                }
            }

            Assert.That(File.Exists(Path.Combine(RESTOREFOLDER, "a")), Is.EqualTo(!corruptBothFiles), "File 'a' should be restored.");
            Assert.That(File.Exists(Path.Combine(RESTOREFOLDER, "b")), Is.EqualTo(!corruptBothFiles), "File 'b' should be restored.");
            if (!corruptLargeFile)
                Assert.That(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(RESTOREFOLDER, "a"))), Is.EqualTo(Convert.ToBase64String(data)), "Restored file 'a' should be the same as the original.");
            else if (!corruptBothFiles)
                Assert.That(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(RESTOREFOLDER, "b"))), Is.EqualTo(Convert.ToBase64String(data.AsSpan().Slice(0, 1024))), "Restored file 'b' should be the same as the original.");
        }

        [Test]
        [Category("Targeted")]
        [TestCase(1)]
        [TestCase(5)]
        [TestCase(10)]
        public void TestRepairPossibleWithMultipleDblockFiles(int dblocksToDelete)
        {
            var testopts = TestOptions.Expand(new
            {
                blocksize = "1kb",
                no_encryption = true,
                rebuild_missing_dblock_files = true,
                zip_compression_level = 0,
                dblock_size = "10kb"
            });

            // 1. Make some shared data 1kb blocks, 32 bytes hash, 3 blocklists, and 1028 bytes extra
            var data = new byte[1024 * 32 * 3 + 1028];
            Random.Shared.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // 2. Delete files
            var dblockFiles = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly).ToArray();
            Random.Shared.Shuffle(dblockFiles);
            foreach (var file in dblockFiles.Take(dblocksToDelete))
                File.Delete(file);

            // 3. Try to repair the remote destination
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                TestUtils.AssertResults(c.Repair());
                TestUtils.AssertResults(c.Test());
            }

            // 4. Verify that restore works
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { restore_path = RESTOREFOLDER }), null))
                TestUtils.AssertResults(c.Restore(null));

            TestUtils.AssertDirectoryTreesAreEquivalent(DATAFOLDER, RESTOREFOLDER, !Library.Utility.Utility.ParseBoolOption(testopts, "skip-metadata"), "Restore");
        }
    }
}

