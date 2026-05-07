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
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Reproduction test for https://github.com/duplicati/duplicati/issues/6820
    /// </summary>
    public class Issue6820 : BasicSetupHelper
    {
        private void WriteSourceFiles(int seed = 0, int fileCount = 10_000)
        {
            // Create many small files so the FilesetEntry table has many rows,
            // making AppendFilesFromPreviousSet a meaningful amount of work.
            var rnd = new Random(seed);
            Directory.CreateDirectory(this.DATAFOLDER);
            // Spread across subdirectories to keep directory listings reasonable.
            var batchSize = 1000;
            for (int i = 0; i < fileCount; i++)
            {
                if (i % batchSize == 0)
                    Directory.CreateDirectory(Path.Combine(this.DATAFOLDER, $"dir_{i / batchSize:D4}"));
                var data = new byte[rnd.Next(64, 256)];
                rnd.NextBytes(data);
                File.WriteAllBytes(
                    Path.Combine(this.DATAFOLDER, $"dir_{i / batchSize:D4}", $"file_{i:D6}.bin"),
                    data);
            }
        }

        /// <summary>
        /// Lists the RemoteVolume rows of type Files that are still in Temporary or Uploading state.
        /// </summary>
        private static List<(long Id, string Name, string State)> GetIncompleteDlistVolumes(string dbPath)
        {
            var result = new List<(long, string, string)>();
            using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=false");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT ID, Name, State FROM RemoteVolume
                WHERE Type = 'Files' AND State IN ('Temporary', 'Uploading')
                ORDER BY ID";
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                result.Add((rd.GetInt64(0), rd.GetString(1), rd.GetString(2)));
            return result;
        }

        /// <summary>
        /// Simulates the "stuck" state from issue 6820:
        ///   - Demote the last uploaded dlist to Temporary.
        ///   - Clone that dlist + its Fileset + its FilesetEntry rows with a new filename
        ///     (timestamped 1 second later) so that there are TWO Temporary dlist rows,
        ///     reproducing the exact state observed in the bug report.
        /// Returns the filename of the cloned (second) Temporary dlist.
        /// </summary>
        private static string InjectDoubleTemporaryDlistState(string dbPath)
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=false");
            conn.Open();

            // Find the most recent Files volume (the last backup's dlist)
            long volumeId;
            string name;
            long filesetId;
            long operationId;
            long timestamp;
            long isFullBackup;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT rv.ID, rv.Name, f.ID, f.OperationID, f.Timestamp, f.IsFullBackup
                    FROM RemoteVolume rv
                    INNER JOIN Fileset f ON f.VolumeID = rv.ID
                    WHERE rv.Type = 'Files'
                    ORDER BY f.Timestamp DESC
                    LIMIT 1";
                using var rd = cmd.ExecuteReader();
                if (!rd.Read())
                    throw new InvalidOperationException("No Files RemoteVolume found in the database");
                volumeId = rd.GetInt64(0);
                name = rd.GetString(1);
                filesetId = rd.GetInt64(2);
                operationId = rd.GetInt64(3);
                timestamp = rd.GetInt64(4);
                isFullBackup = rd.GetInt64(5);
            }

            // Mark it as Temporary (simulating: previous backup was interrupted).
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE RemoteVolume SET State = 'Temporary', Size = -1 WHERE ID = @ID";
                cmd.Parameters.AddWithValue("@ID", volumeId);
                cmd.ExecuteNonQuery();
            }

            // Create a second Temporary dlist, 1 second later, as if a previous attempt at
            // "completing previous backup" got halfway through creating the synthetic filelist.
            var newTs = timestamp + 1;
            var newName = RegenerateDlistFilename(name, newTs);

            long newVolumeId;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO RemoteVolume
                        (OperationID, Name, Type, State, Size, VerificationCount, DeleteGraceTime, ArchiveTime, LockExpirationTime)
                    VALUES
                        (@OperationID, @Name, 'Files', 'Temporary', -1, 0, 0, 0, 0);
                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@OperationID", operationId);
                cmd.Parameters.AddWithValue("@Name", newName);
                newVolumeId = (long)cmd.ExecuteScalar();
            }

            long newFilesetId;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO Fileset (OperationID, Timestamp, VolumeID, IsFullBackup)
                    VALUES (@OperationID, @Timestamp, @VolumeID, @IsFullBackup);
                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@OperationID", operationId);
                cmd.Parameters.AddWithValue("@Timestamp", newTs);
                cmd.Parameters.AddWithValue("@VolumeID", newVolumeId);
                cmd.Parameters.AddWithValue("@IsFullBackup", isFullBackup);
                newFilesetId = (long)cmd.ExecuteScalar();
            }

            return newName;
        }

        /// <summary>
        /// Given a dlist filename like duplicati-20260328T120000Z.dlist.zip(.aes),
        /// returns a filename with the timestamp replaced.
        /// </summary>
        private static string RegenerateDlistFilename(string existingName, long newUnixSeconds)
        {
            var newTs = DateTimeOffset.FromUnixTimeSeconds(newUnixSeconds).UtcDateTime;
            var stamp = newTs.ToString("yyyyMMdd'T'HHmmss'Z'");
            var idxDash = existingName.IndexOf('-');
            var idxDot = existingName.IndexOf('.', idxDash);
            if (idxDash < 0 || idxDot < 0)
                throw new ArgumentException($"Unexpected dlist name format: {existingName}");
            var prefix = existingName.Substring(0, idxDash + 1); // "duplicati-"
            var suffix = existingName.Substring(idxDot); // ".dlist.zip[.aes]"
            return prefix + stamp + suffix;
        }

        [Test]
        [Category("Disruption")]
        [Category("Issue6820")]
        [Category("ExcludedFromCLI")]
        public async Task StuckCompletingPreviousBackupWithTwoTemporaryDlists()
        {
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["dblock-size"] = "10mb",
                ["disable-file-scanner"] = "true",
                ["concurrency-fileprocessors"] = "1",
            };

            // 1. Two successful backups to build some FilesetEntry history.
            this.WriteSourceFiles(seed: 1);
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var res = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, res.Errors.Count(), "First backup should succeed");
            }

            this.WriteSourceFiles(seed: 2);
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var res = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, res.Errors.Count(), "Second backup should succeed");
            }

            var beforeInject = GetIncompleteDlistVolumes(this.DBFILE);
            TestContext.Progress.WriteLine($"Incomplete dlists before injection: {beforeInject.Count}");
            Assert.AreEqual(0, beforeInject.Count,
                "After successful backups, no dlist should be Temporary");

            // Wait a few seconds so that any injected-future timestamps (the
            // cloned dlist gets current_time + 1) are still in the past by the
            // time the next backup runs. BackupHandler refuses to run if a
            // previous fileset's timestamp is in the future.
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

            // 2. Inject the "stuck" state: two Temporary dlists + matching Fileset rows.
            var newName = InjectDoubleTemporaryDlistState(this.DBFILE);
            TestContext.Progress.WriteLine($"Injected second Temporary dlist name: {newName}");

            var afterInject = GetIncompleteDlistVolumes(this.DBFILE);
            TestContext.Progress.WriteLine($"Incomplete dlists after injection: {afterInject.Count}");
            foreach (var v in afterInject)
                TestContext.Progress.WriteLine($"  ID={v.Id} Name={v.Name} State={v.State}");
            Assert.AreEqual(2, afterInject.Count,
                "After injecting, there should be exactly two Temporary dlist rows " +
                "(reproducing the state reported in issue 6820)");

            // Remove the last successful dlist from the target folder so the backup
            // has to actually process the "previous backup was interrupted" path
            // rather than discover that the file already exists on the backend.
            foreach (var v in afterInject)
            {
                var path = Path.Combine(this.TARGETFOLDER, v.Name);
                if (File.Exists(path))
                {
                    TestContext.Progress.WriteLine($"Removing backend dlist: {v.Name}");
                    File.Delete(path);
                }
            }

            // 3. Run another backup and require it to complete within a reasonable timeout.
            //    With the bug, "Completing previous backup..." hangs
            this.WriteSourceFiles(seed: 3);

            // Route the recovery backup's log to a dedicated file so we can
            // confirm it reached UploadSyntheticFilelist / AppendFilesFromPreviousSet
            // and measure how long the INSERT actually took.
            var recoveryLogPath = Path.Combine(BASEFOLDER, "recovery.log");
            if (File.Exists(recoveryLogPath))
                File.Delete(recoveryLogPath);
            var recoveryOptions = new Dictionary<string, string>(options)
            {
                ["log-file"] = recoveryLogPath,
                ["log-file-log-level"] = nameof(Duplicati.Library.Logging.LogMessageType.Profiling),
                // Force every individual SQL query through the logging Timer so
                // we can see which one is causing the issue.
                ["profile-all-database-queries"] = "true",
            };

            var timeout = TimeSpan.FromMinutes(3);
            IBackupResults backupResults = null;
            Exception backupException = null;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var backupTask = Task.Run(() =>
            {
                try
                {
                    using var c = new Controller("file://" + this.TARGETFOLDER, recoveryOptions, null);
                    backupResults = c.Backup(new[] { this.DATAFOLDER });
                }
                catch (Exception ex)
                {
                    backupException = ex;
                }
            });

            var completed = await Task.WhenAny(backupTask, Task.Delay(timeout)).ConfigureAwait(false);
            stopwatch.Stop();

            TestContext.Progress.WriteLine(
                $"Recovery backup finished after {stopwatch.Elapsed.TotalSeconds:F2}s");

            // Report whether we actually took the synthetic-filelist path,
            // and compute the longest silent gap in the Profiling-level log
            // (no log lines for N seconds). That gap is where the unlogged
            // AppendFilesFromPreviousSet SQL runs - the core of issue 6820.
            TimeSpan longestGap = TimeSpan.Zero;
            string longestGapContext = "";
            bool syntheticTaken = false;
            if (File.Exists(recoveryLogPath))
            {
                var lines = File.ReadAllLines(recoveryLogPath);
                syntheticTaken = lines.Any(l => l.Contains("PreviousBackupFilelistUpload"));
                TestContext.Progress.WriteLine(
                    syntheticTaken
                        ? "Synthetic filelist path was taken"
                        : "WARNING: Synthetic filelist path was NOT taken - the test did not " +
                          "exercise the code path that hangs in production.");

                // Parse timestamps of the form "2026-05-05 14.38.38 +02" at the
                // start of each line, and find the largest gap between consecutive
                // timestamped lines. Only applicable after PreviousBackupFilelistUpload.
                DateTime? lastTs = null;
                string lastLine = null;
                bool seenSyntheticMarker = false;
                foreach (var line in lines)
                {
                    if (!seenSyntheticMarker)
                    {
                        if (line.Contains("PreviousBackupFilelistUpload"))
                            seenSyntheticMarker = true;
                        continue;
                    }
                    if (line.Length < 23) continue;
                    // Timestamps use "yyyy-MM-dd HH.mm.ss" (dots, not colons, for time).
                    if (!DateTime.TryParseExact(
                            line.Substring(0, 19),
                            "yyyy-MM-dd HH.mm.ss",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None,
                            out var ts))
                        continue;
                    if (lastTs.HasValue)
                    {
                        var gap = ts - lastTs.Value;
                        if (gap > longestGap)
                        {
                            longestGap = gap;
                            longestGapContext = $"{lastTs:HH:mm:ss} -> {ts:HH:mm:ss}: {(lastLine == null ? "" : lastLine.Substring(0, Math.Min(120, lastLine.Length)))}";
                        }
                    }
                    lastTs = ts;
                    lastLine = line;
                }

                TestContext.Progress.WriteLine(
                    $"Longest silent gap after synthetic-filelist begin: {longestGap.TotalSeconds:F1}s");
                if (longestGap.TotalMilliseconds > 0)
                    TestContext.Progress.WriteLine($"  preceded by: {longestGapContext}");

                // Copy the log out of the cleaned-up test folder for post-mortem.
                try
                {
                    var keptLogPath = Path.Combine(Path.GetTempPath(), $"Issue6820.recovery.{DateTime.Now:yyyyMMddHHmmss}.log");
                    File.Copy(recoveryLogPath, keptLogPath, true);
                    TestContext.Progress.WriteLine($"Recovery log preserved at: {keptLogPath}");
                }
                catch { }
            }
            else
            {
                TestContext.Progress.WriteLine(
                    "WARNING: Recovery log file was not created - cannot verify code path");
            }

            Assert.IsTrue(syntheticTaken,
                "Test did not exercise UploadSyntheticFilelist.Run - the reproduction is " +
                "not hitting the code path that hangs in production (issue 6820).");

            if (completed != backupTask)
            {
                // Report the state we're stuck in, for easier diagnosis.
                var stuckState = GetIncompleteDlistVolumes(this.DBFILE);
                TestContext.Progress.WriteLine(
                    "Backup did not complete within the timeout. " +
                    "Incomplete dlist rows at timeout:");
                foreach (var v in stuckState)
                    TestContext.Progress.WriteLine($"  ID={v.Id} Name={v.Name} State={v.State}");
                Assert.Fail(
                    $"Backup did not complete within {timeout.TotalSeconds:F0}s - " +
                    "reproduces 'stuck in completing previous backup' (issue 6820). " +
                    "The bug manifests as a hang in UploadSyntheticFilelist / " +
                    "AppendFilesFromPreviousSet with 100% CPU on a single core.");
            }

            if (backupException != null)
                throw backupException;

            Assert.IsNotNull(backupResults, "Backup results should not be null after completion");
            Assert.AreEqual(0, backupResults.Errors.Count(),
                "Backup should not report errors after recovering from two Temporary dlists");

            // Final state: no dlist should remain Temporary. The recovery code should
            // have either uploaded or marked for deletion the previously-Temporary rows.
            var finalState = GetIncompleteDlistVolumes(this.DBFILE);
            TestContext.Progress.WriteLine($"Final incomplete dlist rows: {finalState.Count}");
            foreach (var v in finalState)
                TestContext.Progress.WriteLine($"  ID={v.Id} Name={v.Name} State={v.State}");
            Assert.AreEqual(0, finalState.Count,
                "After a successful backup, no dlist should remain in Temporary/Uploading state");
        }
    }
}
