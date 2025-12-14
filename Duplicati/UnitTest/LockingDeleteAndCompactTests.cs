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
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class LockingDeleteAndCompactTests : BasicSetupHelper
    {
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
        public async Task DeleteSkipsFilesetIfBlockOrIndexVolumeLocked()
        {
            var backupOpts = TestOptions;
            var target = "file://" + TARGETFOLDER;

            var file1 = Path.Combine(DATAFOLDER, "f1");
            var file2 = Path.Combine(DATAFOLDER, "f2");

            TestUtils.WriteTestFile(file1, 2048);

            long oldestFilesetId;
            string oldestFilesetVolumeName;
            string lockedBlockOrIndexVolume;

            using (var c = new Controller(target, backupOpts, null))
            {
                var b1 = c.Backup(new[] { DATAFOLDER });
                Assert.That(b1.Errors, Is.Empty, "Backup 1 had errors");

                SleepUntilNextSecond(b1.BeginTime);
                TestUtils.WriteTestFile(file2, 2048);

                var b2 = c.Backup(new[] { DATAFOLDER });
                Assert.That(b2.Errors, Is.Empty, "Backup 2 had errors");

                var token = CancellationToken.None;
                await using var db = await LocalDatabase.CreateLocalDatabaseAsync(DBFILE, null, true, null, token).ConfigureAwait(false);

                var filesets = await db.FilesetTimes(token)
                    .ToArrayAsync(cancellationToken: token)
                    .ConfigureAwait(false);

                Assert.That(filesets.Length, Is.GreaterThanOrEqualTo(2), "Expected at least 2 filesets");
                oldestFilesetId = filesets.Last().Key;

                var filesetVolume = await db.GetRemoteVolumeFromFilesetID(oldestFilesetId, token).ConfigureAwait(false);
                oldestFilesetVolumeName = filesetVolume.Name;

                // Find a referenced block or index volume for this fileset.
                await using var lockDb = await LocalLockDatabase.CreateAsync(DBFILE, null, token).ConfigureAwait(false);
                var referencedVolumeNames = await lockDb
                    .GetRemoteVolumesDependingOnFilesets([oldestFilesetId], token)
                    .Select(x => x.Name)
                    .ToArrayAsync(cancellationToken: token)
                    .ConfigureAwait(false);

                lockedBlockOrIndexVolume = null;
                foreach (var name in referencedVolumeNames)
                {
                    var rv = await db.GetRemoteVolume(name, token).ConfigureAwait(false);
                    if (rv.Type == RemoteVolumeType.Blocks || rv.Type == RemoteVolumeType.Index)
                    {
                        lockedBlockOrIndexVolume = name;
                        break;
                    }
                }

                Assert.That(lockedBlockOrIndexVolume, Is.Not.Null.And.Not.Empty, "Failed to locate a block/index volume for the oldest fileset");

                // Lock the dependency volume.
                await lockDb.UpdateRemoteVolumeLockExpiration(lockedBlockOrIndexVolume, DateTime.UtcNow.AddHours(1), token).ConfigureAwait(false);
                await lockDb.Transaction.CommitAsync(token: token).ConfigureAwait(false);
            }

            // Run delete with retention, but expect it to be blocked by the locked dependency.
            var deleteOpts = new System.Collections.Generic.Dictionary<string, string>(backupOpts)
            {
                ["keep-versions"] = "1",
                ["no-auto-compact"] = "true",
            };

            using (var cdelete = new Controller(target, deleteOpts, null))
            {
                var deleteResults = cdelete.Delete();
                Assert.That(deleteResults.Errors, Is.Empty, "Delete had errors");
                Assert.That(deleteResults.DeletedSets, Is.Empty, "Expected no filesets to be deleted due to locked dependency");
                Assert.That(deleteResults.Warnings.Any(x => x.Contains("Skipping deletion of fileset version", StringComparison.Ordinal)),
                    Is.True,
                    "Expected warning about skipping fileset deletion due to locked block/index dependency");

                var remotePath = Path.Combine(TARGETFOLDER, oldestFilesetVolumeName);
                Assert.That(File.Exists(remotePath), Is.True, $"Expected fileset volume to remain on backend: {remotePath}");
            }
        }

        [Test]
        [Category("Compact")]
        public async Task CompactWarnsOnLockedCompactableVolume()
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
                string lockedCandidateName;

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
                Assert.That(compactResults.Warnings.Any(x => x.Contains("selected for compaction but has an active lock", StringComparison.Ordinal) && x.Contains(lockedCandidateName, StringComparison.Ordinal)),
                    Is.True,
                    "Expected warning about compacting a locked volume");
            }
        }
    }
}
