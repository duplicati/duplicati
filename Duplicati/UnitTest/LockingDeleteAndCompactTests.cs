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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Operation;
using Duplicati.Library.Utility;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest
{
    public class LockingDeleteAndCompactTests : BasicSetupHelper
    {
        private sealed class FakeLockingBackendManager : IBackendManager
        {
            public bool SupportsObjectLocking => true;

            public Task SetObjectLockUntilAsync(string remotename, DateTime lockUntilUtc, CancellationToken cancelToken)
                => Task.CompletedTask;

            public Task<DateTime?> GetObjectLockUntilAsync(string remotename, CancellationToken cancelToken)
                => Task.FromResult<DateTime?>(null);

            public Task WaitForEmptyAsync(CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task WaitForEmptyAsync(LocalDatabase database, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public void Dispose() { }

            #region Unused interface members
            public Task PutAsync(Duplicati.Library.Main.Volumes.VolumeWriterBase blockVolume, Duplicati.Library.Main.Volumes.IndexVolumeWriter? indexVolume, Func<Task>? indexVolumeFinished, bool waitForComplete, Func<Task>? onDbUpdate, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task PutVerificationFileAsync(string remotename, TempFile tempFile, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task<System.Collections.Generic.IEnumerable<Duplicati.Library.Interface.IFileEntry>> ListAsync(CancellationToken cancelToken) => throw new NotImplementedException();
            public TempFile DecryptFile(TempFile volume, string volume_name, Options options) => throw new NotImplementedException();
            public Task DeleteAsync(string remotename, long size, bool waitForComplete, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task<IQuotaInfo?> GetQuotaInfoAsync(CancellationToken cancelToken) => throw new NotImplementedException();
            public Task<(TempFile File, string Hash, long Size)> GetWithInfoAsync(string remotename, string hash, long size, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task<TempFile> GetAsync(string remotename, string hash, long size, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task<TempFile> GetDirectAsync(string remotename, string hash, long size, CancellationToken cancelToken) => throw new NotImplementedException();
            public System.Collections.Generic.IAsyncEnumerable<(TempFile File, string Hash, long Size, string Name)> GetFilesOverlappedAsync(System.Collections.Generic.IEnumerable<IRemoteVolume> volumes, CancellationToken cancelToken) => throw new NotImplementedException();
            public Task FlushPendingMessagesAsync(LocalDatabase database, CancellationToken cancellationToken) => Task.CompletedTask;
            public void UpdateThrottleValues(long maxUploadPrSecond, long maxDownloadPrSecond) => throw new NotImplementedException();
            #endregion
        }

        private static void SleepUntilNextSecond(DateTime prevTimestamp)
        {
            // Sleep until the next second to make sure the next backup is not the same.
            var nextSecond = prevTimestamp.AddMilliseconds(-prevTimestamp.Millisecond).AddSeconds(2);
            var remainder = Math.Max(2000, (nextSecond - DateTime.Now).TotalMilliseconds);
            if (remainder > 0)
                Thread.Sleep(TimeSpan.FromMilliseconds(remainder));
        }

        [Test]
        [Category("DeleteHandler")]
        public async Task DeleteSkipsLockedRemoteFilesetVolume()
        {
            var backupOpts = TestOptions;
            var target = "file://" + TARGETFOLDER;
            var file1 = Path.Combine(DATAFOLDER, "f1");
            var file2 = Path.Combine(DATAFOLDER, "f2");

            TestUtils.WriteTestFile(file1, 1024);

            string oldestFilesetVolumeName;
            using (var c = new Controller(target, backupOpts, null))
            {
                var b1 = c.Backup(new[] { DATAFOLDER });
                Assert.That(b1.Errors, Is.Empty, "Backup 1 had errors");

                SleepUntilNextSecond(b1.BeginTime);
                TestUtils.WriteTestFile(file2, 1024);

                var b2 = c.Backup(new[] { DATAFOLDER });
                Assert.That(b2.Errors, Is.Empty, "Backup 2 had errors");

                // Find the oldest fileset volume, and lock it.
                var token = CancellationToken.None;

                await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(DBFILE, null, true, null, token).ConfigureAwait(false))
                {
                    var filesets = await db.FilesetTimes(token)
                        .ToArrayAsync(cancellationToken: token)
                        .ConfigureAwait(false);

                    Assert.That(filesets.Length, Is.GreaterThanOrEqualTo(2), "Expected at least 2 filesets");
                    var oldestFilesetId = filesets.Last().Key;
                    var oldestFilesetVolume = await db.GetRemoteVolumeFromFilesetID(oldestFilesetId, token).ConfigureAwait(false);
                    oldestFilesetVolumeName = oldestFilesetVolume.Name;
                }

                Assert.That(oldestFilesetVolumeName, Is.Not.Null.And.Not.Empty, "Oldest fileset volume name was empty");

                await using (var lockDb = await LocalLockDatabase.CreateAsync(DBFILE, null, token).ConfigureAwait(false))
                {
                    await lockDb.UpdateRemoteVolumeLockExpiration(oldestFilesetVolumeName, DateTime.UtcNow.AddHours(1), token).ConfigureAwait(false);
                    await lockDb.Transaction.CommitAsync(token: token).ConfigureAwait(false);
                }
            }

            // Run delete in a separate controller instance to avoid retention settings impacting the backup steps.
            var deleteOpts = new System.Collections.Generic.Dictionary<string, string>(backupOpts)
            {
                ["keep-versions"] = "1",
                ["no-auto-compact"] = "true",
            };

            using (var cdelete = new Controller(target, deleteOpts, null))
            {
                var deleteResults = cdelete.Delete();
                Assert.That(deleteResults.Errors, Is.Empty, "Delete had errors");
                Assert.That(deleteResults.Warnings.Any(x => x.Contains("fileset volume has an active lock", StringComparison.Ordinal)),
                    Is.True,
                    "Expected a warning about skipping deletion due to lock");

                var remotePath = Path.Combine(TARGETFOLDER, oldestFilesetVolumeName);
                Assert.That(File.Exists(remotePath), Is.True, $"Expected locked remote fileset volume to remain on backend: {remotePath}");
            }
        }

        [Test]
        [Category("DeleteHandler")]
        public async Task DeleteHonorsFilesetLocksFromBackupUntilExpiration()
        {
            // These values are chosen to give the backup and delete operations enough time to complete
            // while still leaving a window where the lock is active. They can be adjusted in CI if
            // the environment is significantly slower or faster.
            const string LockDurationOptionValue = "10s";
            var delayBetweenBackups = TimeSpan.FromSeconds(5);
            var lockPollInterval = TimeSpan.FromSeconds(1);
            var lockWaitTimeout = TimeSpan.FromSeconds(60);

            var backupOpts = new System.Collections.Generic.Dictionary<string, string>(TestOptions);
            backupOpts["remote-file-lock-duration"] = LockDurationOptionValue;
            backupOpts["no-auto-compact"] = "true";

            var target = "file://" + TARGETFOLDER;
            var file1 = Path.Combine(DATAFOLDER, "f1");
            var file2 = Path.Combine(DATAFOLDER, "f2");

            TestUtils.WriteTestFile(file1, 1024);

            long oldestFilesetId;
            DateTime oldestFilesetTime;
            var token = CancellationToken.None;

            var fakeLockingBackend = new FakeLockingBackendManager();

            async Task ApplyLocksForFilesetTimestamp(DateTime filesetTimeLocal, CancellationToken ct)
            {
                var lockOptionsDict = new System.Collections.Generic.Dictionary<string, string?>(
                    backupOpts.ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value)
                );
                // Make sure the lock options are present with exact keys.
                lockOptionsDict["remote-file-lock-duration"] = LockDurationOptionValue;
                lockOptionsDict["dbpath"] = DBFILE;

                var lockingOptions = new Options(lockOptionsDict);
                Assert.That(lockingOptions.RemoteFileLockDuration, Is.Not.Null, "Expected remote-file-lock-duration to be set for lock operation");

                await using var lockDb = await LocalLockDatabase.CreateAsync(DBFILE, null, ct).ConfigureAwait(false);

                // Apply locks to all volumes associated with this fileset. Because the file:// backend
                // does not implement ILockingBackend, we simulate the backend lock operation and
                // only verify the database lock state.
                var handler = new SetLocksHandler(lockingOptions, new SetLockResults(), new[] { filesetTimeLocal });
                await handler.RunAsync(fakeLockingBackend, lockDb, new[] { filesetTimeLocal }).ConfigureAwait(false);
            }

            var b1BeginTime = DateTime.MinValue;
            using (var c1 = new Controller(target, backupOpts, null))
            {
                var b1 = c1.Backup(new[] { DATAFOLDER });
                Assert.That(b1.Errors, Is.Empty, "Backup 1 had errors");
                b1BeginTime = b1.BeginTime;
            }

            // Capture the latest fileset (the one created by backup 1) and apply locks.
            await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(DBFILE, null, true, null, token).ConfigureAwait(false))
            {
                var filesets = await db.FilesetTimes(token)
                    .ToArrayAsync(cancellationToken: token)
                    .ConfigureAwait(false);

                Assert.That(filesets.Length, Is.GreaterThanOrEqualTo(1), "Expected at least 1 fileset after first backup");
                var latest = filesets.First();
                oldestFilesetId = latest.Key;
                oldestFilesetTime = latest.Value;
            }

            await ApplyLocksForFilesetTimestamp(oldestFilesetTime, token).ConfigureAwait(false);

            // Ensure the second backup has a distinct fileset timestamp.
            SleepUntilNextSecond(b1BeginTime);

            // Optional delay between backups to more clearly separate their lock windows.
            await Task.Delay(delayBetweenBackups).ConfigureAwait(false);

            TestUtils.WriteTestFile(file2, 1024);
            using (var c2 = new Controller(target, backupOpts, null))
            {
                var b2 = c2.Backup(new[] { DATAFOLDER });
                Assert.That(b2.Errors, Is.Empty, "Backup 2 had errors");
            }

            // Apply locks for the second backup as well.
            await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(DBFILE, null, true, null, token).ConfigureAwait(false))
            {
                var filesets = await db.FilesetTimes(token)
                    .ToArrayAsync(cancellationToken: token)
                    .ConfigureAwait(false);

                Assert.That(filesets.Length, Is.GreaterThanOrEqualTo(2), "Expected at least 2 filesets after second backup");
                var newest = filesets.First().Value;
                await ApplyLocksForFilesetTimestamp(newest, token).ConfigureAwait(false);
            }

            async Task<bool> IsFilesetLockedAsync(long filesetId, CancellationToken ct)
            {
                await using var deleteDb = await LocalDeleteDatabase.CreateAsync(DBFILE, "LockCheck", null, ct).ConfigureAwait(false);
                return await deleteDb.HasAnyLockedFiles(filesetId, DateTime.UtcNow, ct).ConfigureAwait(false);
            }

            // Verify that the oldest fileset has at least one active lock immediately after the backups.
            Assert.That(await IsFilesetLockedAsync(oldestFilesetId, token).ConfigureAwait(false),
                Is.True,
                "Expected oldest fileset to have an active lock after backup when file-lock-duration is configured");

            // First delete attempt while the lock is still active should skip deleting the oldest fileset.
            var deleteOpts = new System.Collections.Generic.Dictionary<string, string>(backupOpts)
            {
                ["keep-versions"] = "1",
                ["no-auto-compact"] = "true",
            };

            using (var cdelete = new Controller(target, deleteOpts, null))
            {
                var deleteResults = cdelete.Delete();
                Assert.That(deleteResults.Errors, Is.Empty, "Delete (with active lock) had errors");
                Assert.That(deleteResults.DeletedSets, Is.Empty, "Expected no filesets to be deleted while lock is active");
                Assert.That(deleteResults.Warnings.Any(x => x.Contains("Skipping deletion of fileset version", StringComparison.Ordinal)),
                    Is.True,
                    "Expected warning about skipping fileset deletion due to active lock");
            }

            // Wait until the lock for the oldest fileset has expired before attempting deletion again.
            var startWait = DateTime.UtcNow;
            while (await IsFilesetLockedAsync(oldestFilesetId, token).ConfigureAwait(false))
            {
                if (DateTime.UtcNow - startWait > lockWaitTimeout)
                    Assert.Fail($"Timed out waiting for lock on fileset {oldestFilesetId} to expire");

                await Task.Delay(lockPollInterval).ConfigureAwait(false);
            }

            // Second delete attempt after the lock has expired should now delete the oldest fileset.
            var finalDeleteOpts = new System.Collections.Generic.Dictionary<string, string>(backupOpts)
            {
                ["keep-versions"] = "1",
                ["no-auto-compact"] = "true",
            };

            using (var cdelete = new Controller(target, finalDeleteOpts, null))
            {
                var deleteResults = cdelete.Delete();
                Assert.That(deleteResults.Errors, Is.Empty, "Delete (after lock expiration) had errors");

                var deletedSets = deleteResults.DeletedSets?.ToArray() ?? Array.Empty<Tuple<long, DateTime>>();
                Assert.That(deletedSets.Length, Is.EqualTo(1), "Expected exactly one fileset to be deleted after lock expiration");
                Assert.That(deletedSets[0].Item2, Is.EqualTo(oldestFilesetTime),
                    "Expected the deleted fileset to be the oldest (initial) version");
            }
        }

        [Test]
        [Category("Compact")]
        public async Task CompactDetectsAndAvoidsLockedCompactableVolume()
        {
            var testopts = TestOptions;
            testopts["backup-test-samples"] = "0";
            testopts["number-of-retries"] = "0";
            testopts["dblock-size"] = "20KB";
            testopts["blocksize"] = "1KB";
            testopts["threshold"] = "5";
            testopts["no-auto-compact"] = "true";
            // Force compaction to be considered by making all volumes qualify as "small",
            // and setting the max small-file count to 0.
            testopts["small-file-size"] = "1TB";
            testopts["small-file-max-count"] = "0";

            var target = "file://" + TARGETFOLDER;

            var file1 = Path.Combine(DATAFOLDER, "f1");
            var file2 = Path.Combine(DATAFOLDER, "f2");
            var file3 = Path.Combine(DATAFOLDER, "f3");
            var file4 = Path.Combine(DATAFOLDER, "f4");
            var file5 = Path.Combine(DATAFOLDER, "f5");
            var file6 = Path.Combine(DATAFOLDER, "f6");

            const long fileSize = 45 * 1024;

            using (var c = new Controller(target, testopts, null))
            {
                // Create multiple versions and remove some files to generate compactable volumes.
                TestUtils.WriteTestFile(file1, fileSize);
                TestUtils.WriteTestFile(file2, fileSize);
                var b1 = c.Backup(new[] { DATAFOLDER });
                Assert.That(b1.Errors, Is.Empty, "Backup 1 had errors");

                SleepUntilNextSecond(b1.BeginTime);
                TestUtils.WriteTestFile(file3, fileSize);
                TestUtils.WriteTestFile(file4, fileSize);

                var b2 = c.Backup(new[] { DATAFOLDER });
                Assert.That(b2.Errors, Is.Empty, "Backup 2 had errors");

                SleepUntilNextSecond(b2.BeginTime);
                TestUtils.WriteTestFile(file5, fileSize);
                TestUtils.WriteTestFile(file6, fileSize);

                var b3 = c.Backup(new[] { DATAFOLDER });
                Assert.That(b3.Errors, Is.Empty, "Backup 3 had errors");

                SleepUntilNextSecond(b3.BeginTime);
                File.Delete(file1);
                File.Delete(file3);
                File.Delete(file5);

                var b4 = c.Backup(new[] { DATAFOLDER });
                Assert.That(b4.Errors, Is.Empty, "Backup 4 had errors");

                var token = CancellationToken.None;
                var optionsObj = new Options(testopts);
                string? lockedCandidateName;

                await using (var db = await LocalDeleteDatabase.CreateAsync(DBFILE, "CompactLockTest", null, token).ConfigureAwait(false))
                {
                    var report = await db.GetCompactReport(optionsObj.VolumeSize, optionsObj.Threshold, optionsObj.SmallFileSize, optionsObj.SmallFileMaxCount, token).ConfigureAwait(false);
                    lockedCandidateName = report.CompactableVolumes.FirstOrDefault();
                }

                if (string.IsNullOrWhiteSpace(lockedCandidateName))
                    Assert.Inconclusive("No compactable volumes were found; cannot validate lock warning behavior.");

                await using (var lockDb = await LocalLockDatabase.CreateAsync(DBFILE, null, token).ConfigureAwait(false))
                {
                    await lockDb.UpdateRemoteVolumeLockExpiration(lockedCandidateName, DateTime.UtcNow.AddHours(1), token).ConfigureAwait(false);
                    await lockDb.Transaction.CommitAsync(token: token).ConfigureAwait(false);
                }

                var compactResults = c.Compact();
                Assert.That(compactResults.Errors, Is.Empty, "Compact had errors");
                Assert.That(compactResults.Messages.Any(x => x.Contains("selected for compaction but has an active lock", StringComparison.Ordinal) && x.Contains(lockedCandidateName, StringComparison.Ordinal)),
                    Is.True,
                    "Expected warning about compacting a locked volume");
            }
        }
    }
}
