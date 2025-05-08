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
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class RepairWithMissingRemoteFiles : BasicSetupHelper
    {
        [Test]
        [Category("Targeted")]
        [TestCase(false, true, false)]
        [TestCase(false, false, false)]
        [TestCase(false, true, true)]
        [TestCase(false, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        [TestCase(true, false, true)]
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
        [TestCase(false, false, false)]
        [TestCase(false, true, true)]
        [TestCase(false, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        [TestCase(true, false, true)]
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
        [TestCase(false, false, false)]
        [TestCase(false, true, true)]
        [TestCase(false, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        [TestCase(true, false, true)]
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
        [TestCase(false, true, false)]
        [TestCase(false, false, false)]
        [TestCase(false, true, true)]
        [TestCase(false, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        [TestCase(true, false, true)]
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
        [TestCase(false, false, false)]
        [TestCase(false, true, true)]
        [TestCase(false, false, true)]
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
        }

        [Test]
        [Category("Targeted")]
        [TestCase(false, true, false)]
        [TestCase(false, false, false)]
        [TestCase(false, true, true)]
        [TestCase(false, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        [TestCase(true, false, true)]
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
                using var db = SQLiteLoader.LoadConnection(DBFILE, 0);
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

        // TODO: Add more tests to cover the following scenarios:

        // Test with deleted files as sources + run purge-broken-files
        // Test with partial data available + run purge-broken-files
        // Test where a single block in a large file is missing + run purge-broken-files
        // Test that fetching remote sources for blocks is working

    }
}

