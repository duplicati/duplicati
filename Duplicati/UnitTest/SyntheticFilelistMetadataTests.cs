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
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Reproduction test for synthetic filelist missing metadata.
    ///
    /// The issue is that when a backup is interrupted and a synthetic filelist is created,
    /// some files in the synthetic dlist are missing "metahash" and "metasize" fields.
    /// This causes test/recreate to fail with NOT NULL constraint violation on Metahash.
    /// </summary>
    public class SyntheticFilelistMetadataTests : BasicSetupHelper
    {
        /// <summary>
        /// Test-only subclass of <see cref="LocalDatabase"/> that exposes a couple
        /// of surgical helpers used by these tests to manufacture the orphan-
        /// FileLookup state described in the forum issue. Keeping the helpers
        /// here (rather than on <see cref="LocalDatabase"/> itself) avoids
        /// adding test-only knobs to the production database class.
        /// </summary>
        private sealed class TestDatabase : LocalDatabase
        {
            /// <summary>
            /// Creates a new <see cref="TestDatabase"/> instance bound to the
            /// SQLite database at <paramref name="path"/>.
            /// </summary>
            /// <param name="path">Path to the SQLite database file.</param>
            /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
            /// <returns>A task that, when awaited, returns the new database instance.</returns>
            public static async Task<TestDatabase> CreateAsync(string path, CancellationToken token)
            {
                var db = new TestDatabase();
                db = (TestDatabase)await CreateLocalDatabaseAsync(path, "Test", true, db, token)
                    .ConfigureAwait(false);
                return db;
            }

            /// <summary>
            /// Removes the single FilesetEntry row identified by the
            /// composite (FilesetID, FileID) key. The matching FileLookup row
            /// is left in place; if no other FilesetEntry references it, it
            /// becomes an orphan – exactly the state that
            /// <see cref="Duplicati.Library.Main.Database.LocalBackupDatabase"/>
            /// produces when <c>RemoveDuplicatePathsFromFileset</c> /
            /// <c>FixDuplicatePathsInFilesets</c> trims duplicates.
            /// </summary>
            /// <param name="filesetId">The fileset ID of the entry to remove.</param>
            /// <param name="fileId">The file ID of the entry to remove.</param>
            /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
            /// <returns>The number of rows that were removed.</returns>
            public async Task<int> RemoveFilesetEntry(long filesetId, long fileId, CancellationToken token)
            {
                await using var cmd = await m_connection.CreateCommandAsync(@"
                    DELETE FROM ""FilesetEntry""
                    WHERE
                        ""FilesetID"" = @FilesetId
                        AND ""FileID"" = @FileId
                ", token)
                    .ConfigureAwait(false);

                return await cmd.SetTransaction(m_rtr)
                    .SetParameterValue("@FilesetId", filesetId)
                    .SetParameterValue("@FileId", fileId)
                    .ExecuteNonQueryAsync(true, token)
                    .ConfigureAwait(false);
            }

            /// <summary>
            /// Returns the FileID rows attached to a fileset together with
            /// their full path, sorted by FileID. Used by the tests to locate
            /// the FilesetEntry row that should be detached without composing
            /// raw SQL on the test side.
            /// </summary>
            /// <param name="filesetId">The fileset ID to query.</param>
            /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
            /// <returns>An async enumerable of (FileID, FullPath) pairs.</returns>
            public async IAsyncEnumerable<(long FileId, string FullPath)> GetFilesetEntries(long filesetId, [EnumeratorCancellation] CancellationToken token)
            {
                await using var cmd = await m_connection.CreateCommandAsync(@"
                    SELECT ""fe"".""FileID"", ""p"".""Prefix"" || ""f"".""Path"" AS ""FullPath""
                    FROM ""FilesetEntry"" ""fe""
                    INNER JOIN ""FileLookup"" ""f"" ON ""f"".""ID"" = ""fe"".""FileID""
                    INNER JOIN ""PathPrefix"" ""p"" ON ""p"".""ID"" = ""f"".""PrefixID""
                    WHERE ""fe"".""FilesetID"" = @FilesetId
                    ORDER BY ""fe"".""FileID""
                ", token)
                    .ConfigureAwait(false);

                cmd.SetTransaction(m_rtr)
                    .SetParameterValue("@FilesetId", filesetId);

                await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await rd.ReadAsync(token).ConfigureAwait(false))
                {
                    yield return (
                        rd.ConvertValueToInt64(0),
                        rd.ConvertValueToString(1) ?? string.Empty
                    );
                }
            }
        }

        /// <summary>
        /// Dumps a quick read-only snapshot of the FileLookup / Metadataset / FilesetEntry
        /// rows for the given path so we can see whether the conditions for the bug
        /// (multiple FileLookup rows for the same path, with one of them having
        /// orphaned/inconsistent metadata) actually exist.
        /// </summary>
        private static void DumpDatabaseState(string dbPath, string label)
        {
            TestContext.Progress.WriteLine($"--- DB STATE: {label} ---");
            using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=false;Mode=ReadOnly");
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT f.ID, p.Prefix || f.Path AS FullPath, f.BlocksetID, f.MetadataID,
                           CASE WHEN m.ID IS NULL THEN 'MISSING' ELSE 'OK' END AS MetaState,
                           m.BlocksetID AS MetaBlocksetID
                    FROM FileLookup f
                    JOIN PathPrefix p ON p.ID = f.PrefixID
                    LEFT JOIN Metadataset m ON m.ID = f.MetadataID
                    ORDER BY f.ID";
                using var rd = cmd.ExecuteReader();
                TestContext.Progress.WriteLine("FileLookup rows (FileID, Path, BlocksetID, MetadataID, MetaState, MetaBlocksetID):");
                while (rd.Read())
                {
                    TestContext.Progress.WriteLine(
                        $"  F{rd.GetInt64(0)} path='{rd.GetString(1)}' bs={rd.GetInt64(2)} md={rd.GetInt64(3)} meta={rd.GetString(4)} mbs={(rd.IsDBNull(5) ? "NULL" : rd.GetInt64(5).ToString())}");
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT fs.ID, fs.Timestamp, rv.Name, rv.State, COUNT(fe.FileID)
                    FROM Fileset fs
                    LEFT JOIN RemoteVolume rv ON rv.ID = fs.VolumeID
                    LEFT JOIN FilesetEntry fe ON fe.FilesetID = fs.ID
                    GROUP BY fs.ID
                    ORDER BY fs.Timestamp";
                using var rd = cmd.ExecuteReader();
                TestContext.Progress.WriteLine("Filesets (FilesetID, Timestamp, Volume, State, EntryCount):");
                while (rd.Read())
                {
                    TestContext.Progress.WriteLine(
                        $"  FS{rd.GetInt64(0)} ts={rd.GetInt64(1)} vol={(rd.IsDBNull(2) ? "?" : rd.GetString(2))} state={(rd.IsDBNull(3) ? "?" : rd.GetString(3))} entries={rd.GetInt64(4)}");
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT fe.FilesetID, fe.FileID, p.Prefix || f.Path AS FullPath
                    FROM FilesetEntry fe
                    JOIN FileLookup f ON f.ID = fe.FileID
                    JOIN PathPrefix p ON p.ID = f.PrefixID
                    ORDER BY fe.FilesetID, fe.FileID";
                using var rd = cmd.ExecuteReader();
                TestContext.Progress.WriteLine("FilesetEntry rows (FilesetID, FileID, FullPath):");
                while (rd.Read())
                {
                    TestContext.Progress.WriteLine(
                        $"  FS{rd.GetInt64(0)} -> F{rd.GetInt64(1)} path='{rd.GetString(2)}'");
                }
            }
        }

        /// <summary>
        /// Parses a dlist file and returns all file entries (not folders/symlinks).
        /// </summary>
        private static List<(string Path, string Hash, long Size, string Metahash, long Metasize)> ParseDlistFile(string dlistPath, Dictionary<string, string> options)
        {
            var result = new List<(string, string, long, string, long)>();
            var parsed = VolumeBase.ParseFilename(Path.GetFileName(dlistPath));
            using (var rd = new FilesetVolumeReader(parsed.CompressionModule, dlistPath, new Options(options)))
            {
                foreach (var f in rd.Files)
                {
                    if (f.Type == Duplicati.Library.Main.FilelistEntryType.File)
                    {
                        result.Add((f.Path, f.Hash, f.Size, f.Metahash, f.Metasize));
                    }
                }
            }
            return result;
        }

        [Test]
        [Category("Disruption")]
        [Category("SyntheticFilelist")]
        [Category("ExcludedFromCLI")]
        public async Task SyntheticFilelistShouldIncludeMetadata()
        {
            // Use no-encryption so we can read the dlist files directly
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["no-encryption"] = "true",
                ["dblock-size"] = "1mb",
                ["disable-file-scanner"] = "true",
                ["concurrency-fileprocessors"] = "1",
            };

            // 1. Create source data and run a complete backup
            Directory.CreateDirectory(this.DATAFOLDER);
            var sourceFile = Path.Combine(this.DATAFOLDER, "testfile.txt");
            File.WriteAllText(sourceFile, "initial content");

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var res = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, res.Errors.Count(), "First backup should succeed");
            }

            Assert.AreEqual(1, Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*").Count(),
                "Should have exactly one dlist after first backup");

            // 2. Change the file's timestamp (which changes metadata) but not content
            // This is the key condition: metadata changes, content does not.
            // The backup will create a new FileLookup row with the same BlocksetID
            // but a new MetadataID.
            var newTimestamp = DateTime.UtcNow.AddHours(1);
            File.SetLastWriteTimeUtc(sourceFile, newTimestamp);

            // Wait a bit to ensure timestamp is different
            Thread.Sleep(100);

            // 3. Start a second backup but fail the dlist upload to simulate interruption
            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            var failtarget = new DeterministicErrorBackend().ProtocolKey + "://" + this.TARGETFOLDER;
            bool failed = false;

            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                // Fail only the dlist upload
                if (action == DeterministicErrorBackend.BackendAction.PutBefore && remotename.Contains(".dlist."))
                {
                    failed = true;
                    return true;
                }
                return false;
            };

            using (var c = new Controller(failtarget, options, null))
            {
                Assert.Throws<DeterministicErrorBackend.DeterministicErrorBackendException>(() =>
                    c.Backup(new[] { this.DATAFOLDER }));
            }

            Assert.IsTrue(failed, "Expected dlist upload to fail");

            // The target should still only have one dlist (the first backup)
            Assert.AreEqual(1, Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*").Count(),
                "Should still have only one dlist after failed second backup");

            DumpDatabaseState(this.DBFILE, "After failed second backup");

            // Wait before the recovery backup to avoid timestamp issues
            Thread.Sleep(2000);

            // 4. Run a recovery backup. This should create a synthetic filelist
            // for the interrupted backup, then continue with the new backup.
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var res = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, res.Errors.Count(), "Recovery backup should succeed");
            }

            DumpDatabaseState(this.DBFILE, "After recovery backup");

            // We should now have 3 dlist files: original, synthetic, and new backup
            var dlistFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*").ToList();
            Assert.AreEqual(3, dlistFiles.Count,
                "Should have three dlists: original, synthetic, and recovery backup");

            // 5. Find the synthetic dlist (the middle one by creation time)
            var orderedDlists = dlistFiles
                .Select(f => new { Path = f, Time = File.GetCreationTimeUtc(f) })
                .OrderBy(x => x.Time)
                .ToList();

            var syntheticDlist = orderedDlists[1].Path;
            TestContext.Progress.WriteLine($"Synthetic dlist: {Path.GetFileName(syntheticDlist)}");

            // 6. Parse the synthetic dlist and verify all files have metahash and metasize
            var entries = ParseDlistFile(syntheticDlist, options);
            Assert.That(entries.Count, Is.GreaterThan(0), "Synthetic dlist should contain file entries");

            foreach (var entry in entries)
            {
                TestContext.Progress.WriteLine(
                    $"Entry: {entry.Path}, Hash: {entry.Hash}, Size: {entry.Size}, Metahash: {entry.Metahash ?? "NULL"}, Metasize: {entry.Metasize}");

                Assert.That(entry.Metahash, Is.Not.Null,
                    $"Synthetic dlist entry '{entry.Path}' is missing 'metahash'. " +
                    "This reproduces the bug where synthetic filelists miss metadata.");
            }
        }

        /// <summary>
        /// A stricter variant that also verifies the synthetic filelist against the database
        /// by running a test operation, which will fail if metahash is missing.
        /// </summary>
        [Test]
        [Category("Disruption")]
        [Category("SyntheticFilelist")]
        [Category("ExcludedFromCLI")]
        public async Task SyntheticFilelistShouldPassTestOperation()
        {
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["no-encryption"] = "true",
                ["dblock-size"] = "1mb",
                ["disable-file-scanner"] = "true",
                ["concurrency-fileprocessors"] = "1",
            };

            Directory.CreateDirectory(this.DATAFOLDER);
            var sourceFile = Path.Combine(this.DATAFOLDER, "testfile.txt");
            File.WriteAllText(sourceFile, "initial content");

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var res = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, res.Errors.Count());
            }

            File.SetLastWriteTimeUtc(sourceFile, DateTime.UtcNow.AddHours(1));
            Thread.Sleep(100);

            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            var failtarget = new DeterministicErrorBackend().ProtocolKey + "://" + this.TARGETFOLDER;
            bool failed = false;
            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                if (action == DeterministicErrorBackend.BackendAction.PutBefore && remotename.Contains(".dlist."))
                {
                    failed = true;
                    return true;
                }
                return false;
            };

            using (var c = new Controller(failtarget, options, null))
            {
                Assert.Throws<DeterministicErrorBackend.DeterministicErrorBackendException>(() =>
                    c.Backup(new[] { this.DATAFOLDER }));
            }

            Assert.IsTrue(failed, "Expected dlist upload to fail");

            Thread.Sleep(2000);

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var res = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, res.Errors.Count());
            }

            // Run test with full remote verification to catch missing metadata
            var testOptions = new Dictionary<string, string>(options)
            {
                ["full-remote-verification"] = "true",
                ["backup-test-samples"] = "1",
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, testOptions, null))
            {
                var testRes = c.Test(long.MaxValue);
                Assert.AreEqual(0, testRes.Errors.Count(),
                    "Test operation should not fail. Errors indicate synthetic filelist metadata is missing.");
            }
        }

        /// <summary>
        /// Reproduces the synthetic-filelist-missing-metadata bug by reaching
        /// the orphan-FileLookup state via the application's own database
        /// API: an interrupted backup leaves the file's FileLookup pointing
        /// to a Metadataset whose only block lives in the never-uploaded
        /// dblock. When the recovery backup's PreBackupVerify cleans up that
        /// missing dblock, the cascade in <see cref="LocalDatabase.RemoveRemoteVolumes"/>
        /// drops the Metadataset row but cannot drop the FileLookup row that
        /// has already had its FilesetEntry removed earlier (e.g. by
        /// <see cref="LocalBackupDatabase.RemoveDuplicatePathsFromFileset"/>
        /// being invoked on a fileset whose duplicate-path cleanup turned the
        /// row into an orphan).
        ///
        /// Manually invoke <see cref="LocalDatabase.ClearFilesetEntries"/>
        /// against the interrupted fileset between the failed backup and
        /// recovery so the FileLookup rows of that fileset survive without a
        /// FilesetEntry; PreBackupVerify of the recovery backup then deletes
        /// the corresponding Metadataset rows but leaves the orphan FileLookup
        /// rows intact. UploadSyntheticFilelist subsequently picks one of
        /// those orphans through MAX(fl2.ID) and produces a dlist entry with
        /// NULL Metahash – the bug reported in the forum.
        /// </summary>
        [Test]
        [Category("Disruption")]
        [Category("SyntheticFilelist")]
        [Category("ExcludedFromCLI")]
        public async Task SyntheticFilelistWithMultipleInterruptedBackups()
        {
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["no-encryption"] = "true",
                ["dblock-size"] = "1mb",
                ["disable-file-scanner"] = "true",
                ["concurrency-fileprocessors"] = "1",
            };

            Directory.CreateDirectory(this.DATAFOLDER);
            var sourceFile = Path.Combine(this.DATAFOLDER, "testfile.txt");
            File.WriteAllText(sourceFile, "initial content");

            // 1. Successful initial backup → FS1 with FilesetEntry → F2 (testfile.txt)
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var res = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, res.Errors.Count(), "First backup should succeed");
            }
            DumpDatabaseState(this.DBFILE, "After first successful backup");

            // 2. Interrupted backup that fails the dlist upload. After this,
            //    a Temporary fileset FS2 exists with FilesetEntry rows
            //    pointing to a fresh FileLookup F3 (different MetadataID).
            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            var failtarget = new DeterministicErrorBackend().ProtocolKey + "://" + this.TARGETFOLDER;

            File.SetLastWriteTimeUtc(sourceFile, DateTime.UtcNow.AddHours(1));
            Thread.Sleep(1000);

            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
                action == DeterministicErrorBackend.BackendAction.PutBefore && remotename.Contains(".dlist.");

            using (var c = new Controller(failtarget, options, null))
            {
                Assert.Throws<DeterministicErrorBackend.DeterministicErrorBackendException>(() =>
                    c.Backup(new[] { this.DATAFOLDER }),
                    "Interrupted backup should fail at the dlist upload");
            }
            DumpDatabaseState(this.DBFILE, "After interrupted backup");

            // 3. Manufacture the orphan-FileLookup state via the application's
            //    own database API:
            //
            //      a) Detach the FileLookup row of testfile.txt from the
            //         interrupted fileset (FS2) so it has no FilesetEntry
            //         anywhere. We keep the directory entry attached so the
            //         interrupted fileset still appears in
            //         GetIncompleteFilesets and the synthetic-filelist
            //         process is triggered later.
            //      b) Call RemoveRemoteVolumes for the dblock that holds the
            //         orphan's metadata block. RemoveRemoteVolumes drops the
            //         Metadataset / Blockset rows unconditionally but the
            //         orphan FileLookup row is skipped because the
            //         FileLookup-cleanup join requires a FilesetEntry to
            //         exist. The result: an orphan FileLookup whose
            //         MetadataID no longer resolves to a Metadataset.
            string interruptedDlistName;
            long orphanFileId;
            await using (var db = await TestDatabase.CreateAsync(this.DBFILE, default).ConfigureAwait(false))
            {
                long interruptedFilesetId = -1;
                await foreach (var fs in db.GetIncompleteFilesets(default).ConfigureAwait(false))
                    interruptedFilesetId = fs.Key;
                Assert.That(interruptedFilesetId, Is.GreaterThan(0),
                    "Should have located the interrupted fileset");
                interruptedDlistName = (await db.GetRemoteVolumeFromFilesetID(interruptedFilesetId, default).ConfigureAwait(false)).Name;

                // Find the FilesetEntry for testfile.txt in the interrupted
                // fileset. There should be exactly one such row (one for the
                // directory and one for the file; we want the file).
                long candidateFileId = -1;
                await foreach (var entry in db.GetFilesetEntries(interruptedFilesetId, default).ConfigureAwait(false))
                {
                    TestContext.Progress.WriteLine($"  FS{interruptedFilesetId} entry: F{entry.FileId} {entry.FullPath}");
                    if (entry.FullPath.EndsWith("testfile.txt"))
                        candidateFileId = entry.FileId;
                }
                Assert.That(candidateFileId, Is.GreaterThan(0), "Expected to find testfile.txt FileLookup in the interrupted fileset");
                orphanFileId = candidateFileId;

                TestContext.Progress.WriteLine($"Detaching F{candidateFileId} from FS{interruptedFilesetId} (will become orphan FileLookup)");
                var removed = await db.RemoveFilesetEntry(interruptedFilesetId, candidateFileId, default).ConfigureAwait(false);
                Assert.AreEqual(1, removed, "Expected exactly one FilesetEntry row to be removed");
                await db.Transaction.CommitAsync("test-detach-orphan", true, default).ConfigureAwait(false);
            }
            DumpDatabaseState(this.DBFILE, "After detaching the orphan FileLookup");

            // Identify the dblock that holds the orphan's metadata block.
            // The interrupted backup only changed metadata, so the dblock it
            // produced contains only the new metadata block – it is the
            // newest dblock by creation time.
            var allDblocks = Directory.EnumerateFiles(this.TARGETFOLDER, "*.dblock.*")
                .OrderBy(File.GetCreationTimeUtc)
                .ToList();
            TestContext.Progress.WriteLine($"Dblock candidates ({allDblocks.Count}): " +
                string.Join(", ", allDblocks.Select(Path.GetFileName)));
            Assert.That(allDblocks.Count, Is.GreaterThanOrEqualTo(2),
                "Expected at least two dblocks (one per backup attempt)");

            var dblockToRemove = allDblocks.Last();
            await using (var db = await TestDatabase.CreateAsync(this.DBFILE, default).ConfigureAwait(false))
            {
                TestContext.Progress.WriteLine($"Removing dblock from DB: {Path.GetFileName(dblockToRemove)}");
                await db.RemoveRemoteVolumes(new[] { Path.GetFileName(dblockToRemove) }, default)
                    .ConfigureAwait(false);
                await db.Transaction.CommitAsync("test-remove-dblock", true, default).ConfigureAwait(false);
            }
            // Also remove the file so the recovery's VerifyRemoteList won't
            // try to promote the volume back to Verified.
            File.Delete(dblockToRemove);
            DumpDatabaseState(this.DBFILE, "After RemoveRemoteVolumes for newest dblock");

            // 4. Recovery backup. With the interrupted fileset's FilesetEntry
            //    rows gone, the synthetic-filelist process still sees the
            //    interrupted dlist as lastTempFilelist and runs
            //    AppendFilesFromPreviousSet. The MAX(fl2.ID) lookup picks the
            //    orphan FileLookup F3, but its Metadataset M3 is no longer
            //    selected by any join (no FilesetEntry, so the LIST_FILESETS
            //    JOIN can't find Metadataset rows linked through FilesetEntry
            //    paths). The dlist entry gets NULL Metahash.
            DeterministicErrorBackend.ErrorGenerator = (_, _) => false;
            Thread.Sleep(2000);

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var res = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, res.Errors.Count(), "Recovery backup should succeed");
            }
            DumpDatabaseState(this.DBFILE, "After recovery backup");

            var dlistFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*").ToList();
            TestContext.Progress.WriteLine($"Dlists on backend: {dlistFiles.Count}");

            int missingMetahash = 0;
            foreach (var dlist in dlistFiles)
            {
                var entries = ParseDlistFile(dlist, options);
                foreach (var entry in entries)
                {
                    TestContext.Progress.WriteLine(
                        $"Dlist: {Path.GetFileName(dlist)}, Entry: {entry.Path}, Metahash: {entry.Metahash ?? "NULL"}, Metasize: {entry.Metasize}");
                    if (entry.Metahash == null)
                        missingMetahash++;
                }
            }

            Assert.AreEqual(0, missingMetahash,
                $"Found {missingMetahash} dlist entries with missing 'metahash'. " +
                "This reproduces the bug where AppendFilesFromPreviousSet picks an orphan FileLookup row.");

            // The original symptom from the forum: full remote verification
            // hits the NOT NULL constraint on Filelist.Metahash.
            var testOptions = new Dictionary<string, string>(options)
            {
                ["full-remote-verification"] = "true",
                ["backup-test-samples"] = "100",
            };
            using (var c = new Controller("file://" + this.TARGETFOLDER, testOptions, null))
            {
                var testRes = c.Test(long.MaxValue);
                Assert.AreEqual(0, testRes.Errors.Count(),
                    "Test operation should not fail after recovery from the interrupted backup");
            }
        }

        /// <summary>
        /// Verifies that the synthetic filelist is well-formed even after the
        /// previous complete backup is removed via the delete command.
        ///
        /// This exercises the <see cref="AppendFilesFromPreviousSet"/> path
        /// in a realistic maintenance workflow (interrupted backup → delete old
        /// versions → recovery backup) without directly manipulating the database.
        /// </summary>
        [Test]
        [Category("Disruption")]
        [Category("SyntheticFilelist")]
        [Category("ExcludedFromCLI")]
        public async Task SyntheticFilelistAfterDeletingOldVersions()
        {
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["no-encryption"] = "true",
                ["dblock-size"] = "1mb",
                ["disable-file-scanner"] = "true",
                ["concurrency-fileprocessors"] = "1",
            };

            Directory.CreateDirectory(this.DATAFOLDER);
            var sourceFile = Path.Combine(this.DATAFOLDER, "testfile.txt");
            File.WriteAllText(sourceFile, "initial content");

            // 1. Complete backup
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var res = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, res.Errors.Count());
            }

            // 2. Change metadata (timestamp) but not content
            File.SetLastWriteTimeUtc(sourceFile, DateTime.UtcNow.AddHours(1));
            Thread.Sleep(100);

            // 3. Interrupt second backup by failing the dlist upload
            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            var failtarget = new DeterministicErrorBackend().ProtocolKey + "://" + this.TARGETFOLDER;
            bool failed = false;
            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                if (action == DeterministicErrorBackend.BackendAction.PutBefore && remotename.Contains(".dlist."))
                {
                    failed = true;
                    return true;
                }
                return false;
            };

            using (var c = new Controller(failtarget, options, null))
            {
                Assert.Throws<DeterministicErrorBackend.DeterministicErrorBackendException>(() =>
                    c.Backup(new[] { this.DATAFOLDER }));
            }
            Assert.IsTrue(failed, "Expected dlist upload to fail");

            DumpDatabaseState(this.DBFILE, "After failed backup 2");

            Thread.Sleep(2000);

            // 4. Delete the first (complete) backup through the API.
            //    This causes the database to clean up FilesetEntry / FileLookup rows
            //    that are no longer referenced by any remaining fileset.
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var filesets = c.List().Filesets.ToList();
                Assert.That(filesets.Count, Is.GreaterThanOrEqualTo(1), "Should have at least one complete fileset to delete");
            }

            var deleteOptions = new Dictionary<string, string>(options)
            {
                ["version"] = "0",
                ["allow-full-removal"] = "true",
            };
            using (var c = new Controller("file://" + this.TARGETFOLDER, deleteOptions, null))
            {
                var delRes = c.Delete();
                Assert.AreEqual(0, delRes.Errors.Count(), "Delete should succeed");
            }

            DumpDatabaseState(this.DBFILE, "After deleting backup 1");

            Thread.Sleep(2000);

            // 5. Recovery backup – this should create a synthetic filelist for the
            //    interrupted backup even though the previous complete backup was deleted.
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var res = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, res.Errors.Count(), "Recovery backup should succeed");
            }

            DumpDatabaseState(this.DBFILE, "After recovery backup");

            // 6. Parse all dlists and ensure every file entry carries metadata.
            var dlistFiles = Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*").ToList();
            Assert.That(dlistFiles.Count, Is.GreaterThanOrEqualTo(2),
                "Should have at least the synthetic dlist and the new backup dlist");

            foreach (var dlist in dlistFiles)
            {
                var entries = ParseDlistFile(dlist, options);
                foreach (var entry in entries)
                {
                    TestContext.Progress.WriteLine(
                        $"Dlist: {Path.GetFileName(dlist)}, Entry: {entry.Path}, Metahash: {entry.Metahash ?? "NULL"}, Metasize: {entry.Metasize}");

                    Assert.That(entry.Metahash, Is.Not.Null,
                        $"Dlist '{Path.GetFileName(dlist)}' entry '{entry.Path}' is missing 'metahash'. " +
                        "Synthetic filelist logic should never select a FileLookup row whose metadata is missing.");
                }
            }

            // 7. Run Test with full remote verification to catch any deeper inconsistency.
            var testOptions = new Dictionary<string, string>(options)
            {
                ["full-remote-verification"] = "true",
                ["backup-test-samples"] = "1",
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, testOptions, null))
            {
                var testRes = c.Test(long.MaxValue);
                Assert.AreEqual(0, testRes.Errors.Count(),
                    "Test operation should not fail after recovery and delete workflow.");
            }
        }
    }
}
