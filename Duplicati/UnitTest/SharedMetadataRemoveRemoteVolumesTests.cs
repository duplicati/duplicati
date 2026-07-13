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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database.Local;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Integration tests that reproduce the "Detected N file(s) in FilesetEntry
    /// without corresponding FileLookup entry" error through real backup operations.
    ///
    /// The bug occurs when <see cref="LocalDatabase.RemoveRemoteVolumesAsync"/> removes
    /// a dblock volume whose data blocks belong to files that share metadata (Metadataset)
    /// with files in other filesets. The metadata-based deletion logic over-deletes
    /// shared metadata, orphaning FilesetEntry records.
    /// </summary>
    [TestFixture]
    public class SharedMetadataRemoveRemoteVolumesTests : BasicSetupHelper
    {
        /// <summary>
        /// Reproduces the issue by:
        /// 1. Running a successful backup (Fileset 0) with files that share metadata
        /// 2. Adding a new file with identical metadata (same size, timestamps) but different
        ///    data content, and running a backup that is interrupted during dblock upload,
        ///    leaving a dblock volume in Uploading state (registered in DB, not on remote)
        /// 3. Running a third backup — its PreBackupVerify calls VerifyRemoteList which
        ///    detects the Uploading dblock (not on remote) and calls RemoveRemoteVolumesAsync
        ///    to clean it up
        ///
        /// Before the fix: Step 3 throws ConstraintException because removing the Uploading
        /// dblock incorrectly captures shared metadata (via the OR clause in
        /// metadataFilesetQuery), causing FilesetEntry records from Fileset 0 to be orphaned.
        ///
        /// After the fix: Step 3 succeeds — only the data blocks and their directly
        /// associated FileLookup/FilesetEntry records are removed; shared metadata and
        /// files in other filesets are preserved.
        /// </summary>
        [Test]
        [Category("Disruption")]
        public async Task InterruptedBackupWithSharedMetadataDoesNotCorruptDatabaseAsync()
        {
            var testopts = TestOptions;
            testopts["blocksize"] = "1kb";
            testopts["dblock-size"] = "50kb";
            testopts["number-of-retries"] = "0";
            testopts["no-encryption"] = "true";

            // Create files with DIFFERENT data content but IDENTICAL metadata (same size,
            // same timestamps, same permissions). This ensures:
            // - Each file has unique data blocks (no data deduplication between files)
            // - All files share the same Metadataset (metadata is deduplicated by hash)
            //
            // The shared metadata is the precondition for the bug: when the Uploading dblock
            // containing file D's data blocks is removed, the OR clause in metadataFilesetQuery
            // captures the shared Metadataset because file D references it, even though the
            // metadata blockset itself is on a different (valid) volume.
            var fixedTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            for (var i = 0; i < 3; i++)
            {
                var path = Path.Combine(DATAFOLDER, $"file-{i}.txt");
                var data = new byte[2048];
                new Random(i + 1).NextBytes(data);
                File.WriteAllBytes(path, data);
                File.SetLastWriteTime(path, fixedTime);
                File.SetCreationTime(path, fixedTime);
            }

            // Step 1: Run a successful backup — this creates Fileset 0
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            using (var c = new Controller("file://" + TARGETFOLDER, testopts.Expand(new { full_remote_verification = true }), null))
                TestUtils.AssertResults(await c.TestAsync(long.MaxValue));

            // Step 2: Add a new file with IDENTICAL metadata (same size, same timestamps)
            // but DIFFERENT data content. This file will share the Metadataset with the
            // existing files, but its data blocks will be unique and go on a new dblock.
            Thread.Sleep(1000);

            var newFilePath = Path.Combine(DATAFOLDER, "file-new.txt");
            var newData = new byte[2048];
            new Random(999).NextBytes(newData);
            File.WriteAllBytes(newFilePath, newData);
            File.SetLastWriteTime(newFilePath, fixedTime);
            File.SetCreationTime(newFilePath, fixedTime);

            // Interrupt the backup by failing the dblock upload
            BackendLoader.AddBackend(new DeterministicErrorBackend());
            var failtarget = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;

            var dblockUploadFailed = false;
            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                if (action == DeterministicErrorBackend.BackendAction.PutBefore && remotename.Contains(".dblock."))
                {
                    dblockUploadFailed = true;
                    return true;
                }
                return false;
            };

            using (var c = new Controller(failtarget, testopts, null))
            {
                var ex = Assert.CatchAsync(async () => await c.BackupAsync(new[] { DATAFOLDER }));
                UnwrapAggregateException(ref ex);
                Assert.That(ex, Is.TypeOf<DeterministicErrorBackend.DeterministicErrorBackendException>(),
                    $"Expected DeterministicErrorBackendException, got: {ex?.GetType().Name}: {ex?.Message}");
            }

            Assert.That(dblockUploadFailed, Is.True, "The dblock upload should have failed");
            DeterministicErrorBackend.ErrorGenerator = null;

            // Verify preconditions: metadata is shared across filesets, and the Uploading
            // dblock contains data blocks for a file that shares that metadata
            Console.WriteLine("=== Database state after interruption ===");
            using (var db = await LocalDatabase.CreateLocalDatabaseAsync(testopts["dbpath"], "test", true, null, CancellationToken.None))
            {
                Console.WriteLine(TestUtils.DumpTable(db.Connection.CreateCommand(), "Remotevolume", null));

                Console.WriteLine("--- Shared metadata across filesets ---");
                using (var cmd = db.Connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT ""Metadataset"".""ID"", COUNT(DISTINCT ""FilesetEntry"".""FilesetID"") AS fileset_count, COUNT(*) AS file_count
                        FROM ""Metadataset""
                        INNER JOIN ""FileLookup"" ON ""FileLookup"".""MetadataID"" = ""Metadataset"".""ID""
                        INNER JOIN ""FilesetEntry"" ON ""FilesetEntry"".""FileID"" = ""FileLookup"".""ID""
                        GROUP BY ""Metadataset"".""ID""
                        HAVING fileset_count > 1
                    ";
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        Console.WriteLine($"  MetadataID={reader.GetInt64(0)}, filesets={reader.GetInt64(1)}, files={reader.GetInt64(2)}");
                }

                Console.WriteLine("--- Files using blocks on Uploading/Temporary volumes ---");
                using (var cmd2 = db.Connection.CreateCommand())
                {
                    cmd2.CommandText = @"
                        SELECT DISTINCT ""FileLookup"".""ID"", ""FileLookup"".""Path"", ""FileLookup"".""MetadataID""
                        FROM ""FileLookup""
                        INNER JOIN ""BlocksetEntry"" ON ""BlocksetEntry"".""BlocksetID"" = ""FileLookup"".""BlocksetID""
                        INNER JOIN ""Block"" ON ""Block"".""ID"" = ""BlocksetEntry"".""BlockID""
                        INNER JOIN ""Remotevolume"" ON ""Remotevolume"".""ID"" = ""Block"".""VolumeID""
                        WHERE ""Remotevolume"".""State"" IN ('Temporary', 'Uploading')
                    ";
                    using var reader2 = await cmd2.ExecuteReaderAsync();
                    while (await reader2.ReadAsync())
                        Console.WriteLine($"  File ID={reader2.GetInt64(0)}, Path={reader2.GetString(1)}, MetadataID={reader2.GetInt64(2)}");
                }
            }

            // Step 3: Run a new backup. The PreBackupVerify step will:
            //   a) List remote files
            //   b) Find the Uploading dblock volume in the DB that is NOT on the remote
            //   c) Call RemoveRemoteVolumesAsync to clean up the orphaned volume
            //
            // Before the fix: This throws ConstraintException (or causes TestAsync failures)
            //   because the Uploading dblock contains data blocks for file-new.txt which
            //   shares a Metadataset with files in Fileset 0, and the metadata-based
            //   deletion over-deletes shared metadata.
            // After the fix: This succeeds and the backup completes normally.
            Thread.Sleep(1000);

            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            // Step 4: Verify the database is consistent
            using (var c = new Controller("file://" + TARGETFOLDER, testopts.Expand(new { full_remote_verification = true }), null))
                TestUtils.AssertResults(await c.TestAsync(long.MaxValue));

            // Step 5: Verify we have the expected number of filesets
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var listResults = await c.ListAsync();
                TestUtils.AssertResults(listResults);
                Assert.GreaterOrEqual(listResults.Filesets.Count(), 2,
                    $"Expected at least 2 filesets, got {listResults.Filesets.Count()}");
            }
        }

        /// <summary>
        /// Reproduces the issue with multiple filesets sharing metadata and a larger
        /// interrupted backup. Two successful backups create filesets with shared metadata,
        /// then a third backup is interrupted, leaving dblock volumes in Uploading state.
        /// The cleanup must not corrupt the database.
        /// </summary>
        [Test]
        [Category("Disruption")]
        public async Task InterruptedBackupWithMultipleFilesetsAndSharedMetadataDoesNotCorruptDatabaseAsync()
        {
            var testopts = TestOptions;
            testopts["blocksize"] = "1kb";
            testopts["dblock-size"] = "50kb";
            testopts["number-of-retries"] = "0";
            testopts["no-encryption"] = "true";

            var fixedTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            // Create initial files with different content but identical metadata
            for (var i = 0; i < 3; i++)
            {
                var path = Path.Combine(DATAFOLDER, $"file-{i}.txt");
                var data = new byte[2048];
                new Random(i + 1).NextBytes(data);
                File.WriteAllBytes(path, data);
                File.SetLastWriteTime(path, fixedTime);
                File.SetCreationTime(path, fixedTime);
            }

            // First successful backup
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            // Second successful backup — modify a file so we get 2 filesets with shared metadata
            Thread.Sleep(1000);
            {
                var path = Path.Combine(DATAFOLDER, "file-0.txt");
                var data = new byte[2048];
                new Random(100).NextBytes(data);
                File.WriteAllBytes(path, data);
                File.SetLastWriteTime(path, fixedTime);
                File.SetCreationTime(path, fixedTime);
            }

            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            // Now interrupt a third backup with new files that share metadata
            Thread.Sleep(1000);
            for (var i = 3; i < 6; i++)
            {
                var path = Path.Combine(DATAFOLDER, $"file-{i}.txt");
                var data = new byte[2048];
                new Random(i + 1).NextBytes(data);
                File.WriteAllBytes(path, data);
                File.SetLastWriteTime(path, fixedTime);
                File.SetCreationTime(path, fixedTime);
            }

            BackendLoader.AddBackend(new DeterministicErrorBackend());
            var failtarget = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;

            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                if (action == DeterministicErrorBackend.BackendAction.PutBefore && remotename.Contains(".dblock."))
                    return true;
                return false;
            };

            using (var c = new Controller(failtarget, testopts, null))
            {
                var ex = Assert.CatchAsync(async () => await c.BackupAsync(new[] { DATAFOLDER }));
                UnwrapAggregateException(ref ex);
                Assert.That(ex, Is.TypeOf<DeterministicErrorBackend.DeterministicErrorBackendException>());
            }

            DeterministicErrorBackend.ErrorGenerator = null;

            // The cleanup backup must succeed without ConstraintException
            Thread.Sleep(1000);
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            using (var c = new Controller("file://" + TARGETFOLDER, testopts.Expand(new { full_remote_verification = true }), null))
                TestUtils.AssertResults(await c.TestAsync(long.MaxValue));
        }

        [TearDown]
        public void TearDown()
        {
            DeterministicErrorBackend.ErrorGenerator = null;
        }

        private static void UnwrapAggregateException(ref Exception ex)
        {
            while (ex is AggregateException ae && ae.InnerException != null)
                ex = ae.InnerException;
        }
    }
}
