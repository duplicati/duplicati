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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using NUnit.Framework;
using IFileEntry = Duplicati.Library.Interface.IFileEntry;
using Utility = Duplicati.Library.Utility.Utility;

namespace Duplicati.UnitTest
{
    public class DisruptionTests : BasicSetupHelper
    {
        // Files to create in MB.
        private readonly int[] fileSizes = { 10, 20, 30 };

        private void ModifySourceFiles()
        {
            foreach (int size in this.fileSizes)
            {
                byte[] data = new byte[size * 1024 * 1024];
                Random rng = new Random();
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(this.DATAFOLDER, size + "MB"), data);
            }
        }

        private async Task<(IBackupResults Result, List<string> ModifiedFiles)> RunPartialBackup(Controller controller)
        {
            this.ModifySourceFiles();

            var stopped = new TaskCompletionSource<bool>();
            var modifiedFiles = new List<string>();
            controller.OnOperationStarted = r =>
            {
                var pv = r as ITaskControlProvider;
                if (pv == null)
                    throw new Exception("Task control provider not found");
#if DEBUG
                pv.TaskControl.TestMethodCallback = (path) =>
                {
                    modifiedFiles.Add(path);
                    if (modifiedFiles.Count == 3)
                    {
                        Thread.Sleep(500);
                        controller.Stop();
                        stopped.TrySetResult(true);
                    }
                };
#endif
            };

            // ReSharper disable once AccessToDisposedClosure
            Task<IBackupResults> backupTask = Task.Run(() => controller.Backup(new[] { this.DATAFOLDER }));

            var t = await Task.WhenAny(backupTask, stopped.Task).ConfigureAwait(false);
            if (t != stopped.Task)
                throw new Exception("Backup task completed before we could stop it");

            return (await backupTask.ConfigureAwait(false), modifiedFiles);
        }

        [SetUp]
        public void SetUp()
        {
            this.ModifySourceFiles();
        }

        [Test]
        [Category("Disruption")]
        public async Task FilesetFiles()
        {
#if !DEBUG
            Assert.Ignore("This test requires DEBUG to be defined");
#endif
            // Choose a dblock size that is small enough so that more than one volume is needed.
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions)
            {
                ["dblock-size"] = "10mb",

                // This allows us to inspect the dlist files without needing the BackendManager (which is inaccessible here) to decrypt them.
                ["no-encryption"] = "true",
                ["disable-file-scanner"] = "true",
                ["concurrency-fileprocessors"] = "1",
            };

            // Run a full backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Inject some spacing to allow for the purged fileset
            Thread.Sleep(2000);

            // Run a partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                (var backupResults, _) = await this.RunPartialBackup(c).ConfigureAwait(false);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(1, backupResults.Warnings.Count());
            }

            Dictionary<DateTime, int> GetBackupTypesFromRemoteFiles(Controller c, out List<string> filelistFiles)
            {
                Dictionary<DateTime, int> map = new Dictionary<DateTime, int>();
                filelistFiles = new List<string>();

                IListRemoteResults remoteFiles = c.ListRemote();
                foreach (IFileEntry file in remoteFiles.Files)
                {
                    IParsedVolume volume = VolumeBase.ParseFilename(file);
                    if (volume != null && volume.FileType == RemoteVolumeType.Files)
                    {
                        string dlistFile = Path.Combine(this.TARGETFOLDER, volume.File.Name);
                        filelistFiles.Add(dlistFile);
                        VolumeBase.FilesetData filesetData = VolumeReaderBase.GetFilesetData(volume.CompressionModule, dlistFile, new Options(options));
                        map[volume.Time] = filesetData.IsFullBackup ? BackupType.FULL_BACKUP : BackupType.PARTIAL_BACKUP;
                    }
                }

                return map;
            }

            // Purge a file and verify that the fileset file exists in the new dlist files.
            List<string> dlistFiles;
            Dictionary<DateTime, int> backupTypeMap;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                TestUtils.AssertResults(c.PurgeFiles(new Library.Utility.FilterExpression($"{this.DATAFOLDER}/*{this.fileSizes[0]}*")));

                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(2, filesets.Count);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets.Single(x => x.Version == 1).IsFullBackup);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, filesets.Single(x => x.Version == 0).IsFullBackup);

                backupTypeMap = GetBackupTypesFromRemoteFiles(c, out dlistFiles);
            }

            int[] backupTypes = backupTypeMap.OrderByDescending(x => x.Key).Select(x => x.Value).ToArray();
            Assert.AreEqual(2, backupTypes.Length);
            Assert.AreEqual(BackupType.FULL_BACKUP, backupTypes[1]);
            Assert.AreEqual(BackupType.PARTIAL_BACKUP, backupTypes[0]);

            // Remove the dlist files.
            foreach (string dlistFile in dlistFiles)
            {
                File.Delete(dlistFile);
            }

            // Run a repair and verify that the fileset file exists in the new dlist files.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                TestUtils.AssertResults(c.Repair());

                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(2, filesets.Count);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets.Single(x => x.Version == 1).IsFullBackup);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, filesets.Single(x => x.Version == 0).IsFullBackup);

                backupTypeMap = GetBackupTypesFromRemoteFiles(c, out _);
            }

            backupTypes = backupTypeMap.OrderByDescending(x => x.Key).Select(x => x.Value).ToArray();
            Assert.AreEqual(2, backupTypes.Length);
            Assert.AreEqual(BackupType.FULL_BACKUP, backupTypes[1]);
            Assert.AreEqual(BackupType.PARTIAL_BACKUP, backupTypes[0]);
        }

        [Test]
        [Category("Disruption")]
        public async Task KeepTimeRetention()
        {
#if !DEBUG
            Assert.Ignore("This test requires DEBUG to be defined");
#endif

            // Choose a dblock size that is small enough so that more than one volume is needed.
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions)
            {
                ["dblock-size"] = "10mb",
                ["disable-file-scanner"] = "true",
                ["concurrency-fileprocessors"] = "1",
            };

            // First, run two complete backups followed by a partial backup. We will then set the keep-time
            // option so that the threshold lies between the first and second backups.
            DateTime firstBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));
                firstBackupTime = c.List().Filesets.First().Time;
            }

            // Wait before the second backup so that we can more easily define the keep-time threshold
            // to lie between the first and second backups.
            Thread.Sleep(5000);
            DateTime secondBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                this.ModifySourceFiles();
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));
                secondBackupTime = c.List().Filesets.First().Time;
            }

            // Run a partial backup.
            DateTime thirdBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                (var backupResults, _) = await this.RunPartialBackup(c).ConfigureAwait(false);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(1, backupResults.Warnings.Count());
                thirdBackupTime = c.List().Filesets.First().Time;
            }

            // Set the keep-time option so that the threshold lies between the first and second backups
            // and run the delete operation.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                options["keep-time"] = $"{(int)((DateTime.Now - firstBackupTime).TotalSeconds - (secondBackupTime - firstBackupTime).TotalSeconds / 2)}s";
                TestUtils.AssertResults(c.Delete());

                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(2, filesets.Count);
                Assert.AreEqual(secondBackupTime, filesets[1].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[1].IsFullBackup);
                Assert.AreEqual(thirdBackupTime, filesets[0].Time);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, filesets[0].IsFullBackup);
            }

            // Run another partial backup. We will then verify that a full backup is retained
            // even when all the "recent" backups are partial.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                (var backupResults, _) = await this.RunPartialBackup(c).ConfigureAwait(false);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(1, backupResults.Warnings.Count());
                DateTime fourthBackupTime = c.List().Filesets.First().Time;

                // Set the keep-time option so that the threshold lies after the most recent full backup
                // and run the delete operation.
                options["keep-time"] = "1s";
                TestUtils.AssertResults(c.Delete());

                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(3, filesets.Count);
                Assert.AreEqual(secondBackupTime, filesets[2].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[2].IsFullBackup);
                Assert.AreEqual(thirdBackupTime, filesets[1].Time);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, filesets[1].IsFullBackup);
                Assert.AreEqual(fourthBackupTime, filesets[0].Time);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, filesets[0].IsFullBackup);
            }
        }

        [Test]
        [Category("Disruption")]
        public async Task KeepVersionsRetention()
        {
#if !DEBUG
            Assert.Ignore("This test requires DEBUG to be defined");
#endif

            // Choose a dblock size that is small enough so that more than one volume is needed.
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions)
            {
                ["dblock-size"] = "10mb",
                ["disable-file-scanner"] = "true",
                ["concurrency-fileprocessors"] = "1",
            };

            // Run a full backup.
            DateTime firstBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));
                firstBackupTime = c.List().Filesets.First().Time;
            }

            // Run a partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                (var backupResults, _) = await this.RunPartialBackup(c).ConfigureAwait(false);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(1, backupResults.Warnings.Count());
            }

            // Run a partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                (var backupResults, _) = await this.RunPartialBackup(c).ConfigureAwait(false);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(1, backupResults.Warnings.Count());
            }

            // Run a full backup.
            DateTime fourthBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                this.ModifySourceFiles();
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));
                fourthBackupTime = c.List().Filesets.First().Time;
            }

            // Run a partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                options["keep-versions"] = "2";
                (var backupResults, _) = await this.RunPartialBackup(c).ConfigureAwait(false);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(1, backupResults.Warnings.Count());
                DateTime fifthBackupTime = c.List().Filesets.First().Time;

                // Since we stopped the operation, files were not deleted automatically
                c.Delete();

                // Partial backups that are followed by a full backup can be deleted.
                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(3, filesets.Count);
                Assert.AreEqual(firstBackupTime, filesets[2].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[2].IsFullBackup);
                Assert.AreEqual(fourthBackupTime, filesets[1].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[1].IsFullBackup);
                Assert.AreEqual(fifthBackupTime, filesets[0].Time);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, filesets[0].IsFullBackup);
            }

            // Run a full backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                this.ModifySourceFiles();
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));
                DateTime sixthBackupTime = c.List().Filesets.First().Time;

                // Since the last backup was full, we can now expect to have just the 2 most recent full backups.
                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(2, filesets.Count);
                Assert.AreEqual(fourthBackupTime, filesets[1].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[1].IsFullBackup);
                Assert.AreEqual(sixthBackupTime, filesets[0].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[0].IsFullBackup);
            }
        }

        [Test]
        [Category("Disruption")]
        public async Task ListWithoutLocalDb()
        {
#if !DEBUG
            Assert.Ignore("This test requires DEBUG to be defined");
#endif

            // Choose a dblock size that is small enough so that more than one volume is needed.
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions)
            {
                ["dblock-size"] = "10mb",
                ["no-local-db"] = "true",
                ["disable-file-scanner"] = "true",
                ["concurrency-fileprocessors"] = "1",
            };

            // Run a full backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));

            // Run a partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                (var backupResults, _) = await this.RunPartialBackup(c).ConfigureAwait(false);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(1, backupResults.Warnings.Count());

                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(2, filesets.Count);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[1].IsFullBackup);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, filesets[0].IsFullBackup);
            }
        }

        [Test]
        [Category("Disruption")]
        public async Task RetentionPolicyRetention()
        {
#if !DEBUG
            Assert.Ignore("This test requires DEBUG to be defined");
#endif

            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions)
            {
                // Choose a dblock size that is small enough so that more than one volume is needed.
                ["dblock-size"] = "10mb",

                // This test assumes that we can perform 3 backups within 1 minute.
                ["retention-policy"] = "1m:59s,U:1m",
                ["no-backend-verification"] = "true",
                ["disable-file-scanner"] = "true",
                ["concurrency-fileprocessors"] = "1"
            };

            DateTime firstBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));
                firstBackupTime = c.List().Filesets.First().Time;

                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(1, filesets.Count);

                this.ModifySourceFiles();
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));
                DateTime secondBackupTime = c.List().Filesets.First().Time;

                // Since the most recent backup is not considered in the retention logic, the only backup in the first time frame
                // is the initial one. As a result, we should have 2 backups.
                filesets = c.List().Filesets.ToList();
                Assert.AreEqual(2, filesets.Count);
                Assert.AreEqual(firstBackupTime, filesets[1].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[1].IsFullBackup);
                Assert.AreEqual(secondBackupTime, filesets[0].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[0].IsFullBackup);
            }

            // Wait so that the next backups fall in the next retention interval.
            Thread.Sleep(new TimeSpan(0, 0, 1, 0));

            DateTime thirdBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                (var backupResults, _) = await this.RunPartialBackup(c).ConfigureAwait(false);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(1, backupResults.Warnings.Count());
                thirdBackupTime = c.List().Filesets.First().Time;

                // Since we stopped the backup, files were not deleted automatically
                c.Delete();

                // Since the most recent backup is not considered in the retention logic, there are no backups in the first time
                // frame. The original 2 backups have now spilled over to the U:1m specification. Since we keep the first
                // backup in the interval, we should be left with the first backup, as well as the third partial one.
                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(2, filesets.Count);
                Assert.AreEqual(firstBackupTime, filesets[1].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[1].IsFullBackup);
                Assert.AreEqual(thirdBackupTime, filesets[0].Time);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, filesets[0].IsFullBackup);
            }

            DateTime fourthBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                this.ModifySourceFiles();
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));
                fourthBackupTime = c.List().Filesets.First().Time;

                // Since the most recent backup is not considered in the retention logic, the third backup is the only backup
                // in the first time frame. There is no further spillover, so we simply add the fourth backup to the
                // collection of retained backups.
                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(3, filesets.Count);
                Assert.AreEqual(firstBackupTime, filesets[2].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[2].IsFullBackup);
                Assert.AreEqual(thirdBackupTime, filesets[1].Time);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, filesets[1].IsFullBackup);
                Assert.AreEqual(fourthBackupTime, filesets[0].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[0].IsFullBackup);

                this.ModifySourceFiles();
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));
                DateTime fifthBackupTime = c.List().Filesets.First().Time;

                // Since the most recent backup is not considered in the retention logic, we now have two backups in the
                // first time frame: the third (partial) and fourth (full). Since the first backup in each interval is
                // kept, we would typically keep just the third backup. However, since we should not discard a full
                // backup in favor of a partial one, we keep the fourth as well. We also still have the initial backup.
                filesets = c.List().Filesets.ToList();
                Assert.AreEqual(4, filesets.Count);
                Assert.AreEqual(firstBackupTime, filesets[3].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[3].IsFullBackup);
                Assert.AreEqual(thirdBackupTime, filesets[2].Time);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, filesets[2].IsFullBackup);
                Assert.AreEqual(fourthBackupTime, filesets[1].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[1].IsFullBackup);
                Assert.AreEqual(fifthBackupTime, filesets[0].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[0].IsFullBackup);
            }

            // Wait so that the next backups fall in the next retention interval.
            Thread.Sleep(new TimeSpan(0, 0, 1, 0));

            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                this.ModifySourceFiles();
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
                DateTime sixthBackupTime = c.List().Filesets.First().Time;

                // Since the most recent backup is not considered in the retention logic, we now have three backups in the
                // second time frame: the third (partial), fourth (full), and fifth (full). Since we keep up to the first
                // full backup in each time frame, we now drop the fifth backup.
                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(4, filesets.Count);
                Assert.AreEqual(firstBackupTime, filesets[3].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[3].IsFullBackup);
                Assert.AreEqual(thirdBackupTime, filesets[2].Time);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, filesets[2].IsFullBackup);
                Assert.AreEqual(fourthBackupTime, filesets[1].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[1].IsFullBackup);
                Assert.AreEqual(sixthBackupTime, filesets[0].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[0].IsFullBackup);
            }
        }

        [Test]
        [Category("Disruption")]
        public async Task StopAfterCurrentFile()
        {
#if !DEBUG
            Assert.Ignore("This test requires DEBUG to be defined");
#endif

            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions)
            {
                // Choose a dblock size that is small enough so that more than one volume is needed.
                ["dblock-size"] = "10mb",
                ["disable-file-scanner"] = "true",
                ["concurrency-fileprocessors"] = "1"
            };

            // Run a complete backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));

                Assert.AreEqual(1, c.List().Filesets.Count());
                Assert.AreEqual(BackupType.FULL_BACKUP, c.List().Filesets.Single(x => x.Version == 0).IsFullBackup);
            }

            // Run a partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                (var backupResults, var modified) = await this.RunPartialBackup(c).ConfigureAwait(false);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(1, backupResults.Warnings.Count());
                if (backupResults.ModifiedFiles == 0)
                    throw new Exception($"No files were modified, likely the stop was issued too early, list is: {string.Join(", ", modified)}");
                Assert.That(backupResults.ModifiedFiles, Is.GreaterThan(0), "No files were added, likely the stop was issued too early");
                Assert.That(backupResults.ModifiedFiles, Is.LessThan(fileSizes.Length), "All files were added, likely the stop was issued too late");

                // If we interrupt the backup, the most recent Fileset should be marked as partial.
                Assert.AreEqual(2, c.List().Filesets.Count());
                Assert.AreEqual(BackupType.FULL_BACKUP, c.List().Filesets.Single(x => x.Version == 1).IsFullBackup);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, c.List().Filesets.Single(x => x.Version == 0).IsFullBackup);
            }

            // Restore files from the partial backup set.
            Dictionary<string, string> restoreOptions = new Dictionary<string, string>(options) { ["restore-path"] = this.RESTOREFOLDER };
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                var lastResults = c.List("*");
                var partialVersionFiles = lastResults.Files.Select(x => x.Path).Where(x => !Utility.IsFolder(x, File.GetAttributes)).ToArray();
                Assert.GreaterOrEqual(partialVersionFiles.Length, 1);
                c.Restore(partialVersionFiles);

                foreach (string filepath in partialVersionFiles)
                {
                    var filename = Path.GetFileName(filepath);
                    TestUtils.AssertFilesAreEqual(filepath, Path.Combine(this.RESTOREFOLDER, filename ?? String.Empty), false, filename);
                }
            }

            // Recreating the database should preserve the backup types.
            File.Delete(this.DBFILE);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                TestUtils.AssertResults(c.Repair());

                Assert.AreEqual(2, c.List().Filesets.Count());
                Assert.AreEqual(BackupType.FULL_BACKUP, c.List().Filesets.Single(x => x.Version == 1).IsFullBackup);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, c.List().Filesets.Single(x => x.Version == 0).IsFullBackup);
            }

            // Run a complete backup. Listing the Filesets should include both full and partial backups.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));
                Assert.AreEqual(3, c.List().Filesets.Count());

                Assert.AreEqual(BackupType.FULL_BACKUP, c.List().Filesets.Single(x => x.Version == 2).IsFullBackup);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, c.List().Filesets.Single(x => x.Version == 1).IsFullBackup);
                Assert.AreEqual(BackupType.FULL_BACKUP, c.List().Filesets.Single(x => x.Version == 0).IsFullBackup);
            }

            // Restore files from the full backup set.
            restoreOptions["overwrite"] = "true";
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                var lastResults = c.List("*");
                var fullVersionFiles = lastResults.Files.Select(x => x.Path).Where(x => !Utility.IsFolder(x, File.GetAttributes)).ToArray();
                Assert.AreEqual(this.fileSizes.Length, fullVersionFiles.Length);

                TestUtils.AssertResults(c.Restore(fullVersionFiles));

                foreach (string filepath in fullVersionFiles)
                {
                    string filename = Path.GetFileName(filepath);
                    TestUtils.AssertFilesAreEqual(filepath, Path.Combine(this.RESTOREFOLDER, filename ?? String.Empty), false, filename);
                }
            }
        }

        [Test]
        [Category("Disruption")]
        public async Task StopNow()
        {
#if !DEBUG
            Assert.Ignore("This test requires DEBUG to be defined");
#endif

            // Choose a dblock size that is small enough so that more than one volume is needed.
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions)
            {
                ["dblock-size"] = "10mb",
                ["disable-synthetic-filelist"] = "true",
                ["disable-file-scanner"] = "true",
                ["concurrency-fileprocessors"] = "1",
            };

            // Run a complete backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));

                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(1, filesets.Count);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[0].IsFullBackup);
            }

            // Interrupt a backup with "abort".
            this.ModifySourceFiles();
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var startedTcs = new TaskCompletionSource<bool>();
                c.OnOperationStarted += results =>
                {
                    ((BackupResults)results).OperationProgressUpdater.PhaseChanged += (phase, previousPhase) =>
                    {
                        if (phase == OperationPhase.Backup_ProcessingFiles)
                            startedTcs.SetResult(true);
                    };
                };

                Task backupTask = Task.Run(() => c.Backup([DATAFOLDER]));

                Task completedTask = await Task.WhenAny(startedTcs.Task, Task.Delay(5000));
                if (completedTask == startedTcs.Task)
                {
                    c.Abort();
                    try
                    {
                        await backupTask.ConfigureAwait(false);
                        Assert.Fail("Expected OperationCanceledException but none was thrown.");
                    }
                    catch (OperationCanceledException)
                    {
                        // Success, exception thrown as expected
                    }
                }
                else
                {
                    Assert.Fail("Operation did not reach ready state within timeout.");
                }
            }

            // The next backup should proceed without issues.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));

                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(2, filesets.Count);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[1].IsFullBackup);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[0].IsFullBackup);
            }

            // Restore from the backup that followed the interruption.
            Dictionary<string, string> restoreOptions = new Dictionary<string, string>(options) { ["restore-path"] = this.RESTOREFOLDER };
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                var lastResults = c.List("*");
                var fullVersionFiles = lastResults.Files.Select(x => x.Path).Where(x => !Utility.IsFolder(x, File.GetAttributes)).ToArray();
                Assert.AreEqual(this.fileSizes.Length, fullVersionFiles.Length);

                TestUtils.AssertResults(c.Restore(fullVersionFiles));
                foreach (string filepath in fullVersionFiles)
                {
                    string filename = Path.GetFileName(filepath);
                    TestUtils.AssertFilesAreEqual(filepath, Path.Combine(this.RESTOREFOLDER, filename ?? String.Empty), false, filename);
                }
            }
        }

        [Test]
        [Category("Disruption")]
        public void TestUploadExceptionWithNoRetries()
        {
            var testopts = TestOptions;
            testopts["number-of-retries"] = "0";
            testopts["dblock-size"] = "10mb";

            // Make a base backup
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));

            // Ensure that the target folder only has a single dlist file
            Assert.AreEqual(1, Directory.EnumerateFiles(TARGETFOLDER, "*.dlist.*").Count(), "There should be only one dlist file in the target folder");

            // Make a new backup that fails uploading a dblock file
            ModifySourceFiles();

            // Deterministic error backend
            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            var failtarget = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;

            var hasFailed = false;
            var secondUploadStarted = false;
            var secondUploadCompleted = false;

            // Fail the compact after the first dblock put is completed
            var uploads = new List<string>();
            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                if (action.IsGetOperation)
                {
                    return true;
                }

                if (!hasFailed && action == DeterministicErrorBackend.BackendAction.PutBefore)
                {
                    // We only fail one upload, but there are no retries
                    hasFailed = true;

                    // Make sure we can start a second upload
                    Thread.Sleep(1000);
                    return true;
                }

                if (action == DeterministicErrorBackend.BackendAction.PutBefore)
                    secondUploadStarted = true;
                if (action == DeterministicErrorBackend.BackendAction.PutAfter)
                    secondUploadCompleted = true;

                return false;
            };

            using (var c = new Controller(failtarget, testopts, null))
                Assert.Throws<DeterministicErrorBackend.DeterministicErrorBackendException>(() => c.Backup(new string[] { DATAFOLDER }));

            Assert.That(secondUploadStarted, Is.True, "Second upload was not started");
            Assert.That(secondUploadCompleted, Is.True, "Second upload was not started");
            Assert.That(hasFailed, Is.True, "Failed to fail the upload");
            Assert.That(!uploads.Any(x => x.Contains("dlist")), Is.True, "Upload of dlist file was not skipped");

            // Ensure that the target folder only has a single dlist file
            Assert.AreEqual(1, Directory.EnumerateFiles(TARGETFOLDER, "*.dlist.*").Count(), $"There should be only one dlist file in the target folder: {string.Join(", ", uploads)}");

            // Create a regular backup
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));

            // Verify that all is in order
            using (var c = new Controller("file://" + TARGETFOLDER, testopts.Expand(new { full_remote_verification = true }), null))
                try
                {
                    TestUtils.AssertResults(c.Test(long.MaxValue));
                }
                catch (TestUtils.TestVerificationException e)
                {
                    using var db = new LocalDatabase(testopts["dbpath"], "test", true);
                    using var cmd = db.Connection.CreateCommand();

                    var sb = new StringBuilder();
                    sb.AppendLine(e.Message);
                    sb.AppendLine(TestUtils.DumpTable(cmd, "File", null));
                    sb.AppendLine(TestUtils.DumpTable(cmd, "FilesetEntry", null));
                    sb.AppendLine(TestUtils.DumpTable(cmd, "Fileset", null));

                    sb.AppendLine("Files in the source folder:");
                    foreach (var fe in Directory.EnumerateFileSystemEntries(this.DATAFOLDER))
                        sb.AppendLine($"File: {fe}");

                    Assert.Fail(sb.ToString());
                }

            // Test that we can recreate
            var recreatedDatabaseFile = Path.Combine(BASEFOLDER, "recreated-database.sqlite");
            if (File.Exists(recreatedDatabaseFile))
                File.Delete(recreatedDatabaseFile);

            testopts["dbpath"] = recreatedDatabaseFile;

            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());

            // Check that we have 3 versions
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var listResults = c.List();
                TestUtils.AssertResults(listResults);
                Assert.AreEqual(3, listResults.Filesets.Count());
            }
        }

        [Test]
        [Category("Disruption")]
        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        public void TestUploadExceptionOnFirstDlistWithRepair(bool before, bool failOnLastDblock)
        {
            var testopts = TestOptions;
            testopts["number-of-retries"] = "0";
            testopts["dblock-size"] = "10mb";

            // Deterministic error backend
            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            var failtarget = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
            var operation = before
                ? DeterministicErrorBackend.BackendAction.PutBefore
                : DeterministicErrorBackend.BackendAction.PutAfter;

            // We expect to upload 7 volumes, and fail on the last one
            var count = 7;
            var failed = false;

            // Fail when uploading the dlist file
            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                if (failOnLastDblock)
                {
                    if (action == operation && remotename.Contains(".dblock."))
                    {
                        if (Interlocked.Decrement(ref count) == 0)
                        {
                            // Allow others to progress
                            Thread.Sleep(2000);
                            failed = true;
                            return true;
                        }
                    }
                }
                else
                {
                    if (action == operation && remotename.Contains(".dlist."))
                    {
                        failed = true;
                        return true;
                    }
                }

                return false;
            };

            using (var c = new Controller(failtarget, testopts, null))
                Assert.Throws<DeterministicErrorBackend.DeterministicErrorBackendException>(() => c.Backup(new string[] { DATAFOLDER }));

            Assert.That(failed, Is.True, "Failed to fail the upload");

            // Make spacing for the next backup
            Thread.Sleep(3000);

            // Issue repair
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());

            // Verify that all is in order
            using (var c = new Controller("file://" + TARGETFOLDER, testopts.Expand(new { full_remote_verification = true }), null))
                TestUtils.AssertResults(c.Test(long.MaxValue));

            // If we fail on the dlist after the upload, it is promoted to a regular backup
            var expectedFilesets = before || failOnLastDblock ? 2 : 1;

            // Make a new backup, should continue as normal
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var backupResults = c.Backup(new string[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                Assert.AreEqual(expectedFilesets, c.List().Filesets.Count());
            }

            // Test that we can recreate
            var recreatedDatabaseFile = Path.Combine(BASEFOLDER, "recreated-database.sqlite");
            if (File.Exists(recreatedDatabaseFile))
                File.Delete(recreatedDatabaseFile);

            testopts["dbpath"] = recreatedDatabaseFile;

            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());

            // Check that we have the correct versions
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var listResults = c.List();
                TestUtils.AssertResults(listResults);
                Assert.AreEqual(expectedFilesets, listResults.Filesets.Count());
            }
        }

        [Test]
        [Category("Disruption")]
        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        public void TestUploadExceptionOnFirstDlist(bool before, bool failOnLastDblock)
        {
            var testopts = TestOptions;
            testopts["number-of-retries"] = "0";
            testopts["dblock-size"] = "10mb";

            // Deterministic error backend
            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            var failtarget = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
            var operation = before
                ? DeterministicErrorBackend.BackendAction.PutBefore
                : DeterministicErrorBackend.BackendAction.PutAfter;

            // We expect to upload 7 volumes, and fail on the last one
            var count = 7;
            var failed = false;

            // Fail when uploading the dlist file
            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                if (failOnLastDblock)
                {
                    if (action == operation && remotename.Contains(".dblock."))
                    {
                        if (Interlocked.Decrement(ref count) == 0)
                        {
                            // Allow others to progress
                            Thread.Sleep(2000);
                            failed = true;
                            return true;
                        }
                    }
                }
                else
                {
                    if (action == operation && remotename.Contains(".dlist."))
                    {
                        failed = true;
                        return true;
                    }
                }

                return false;
            };

            using (var c = new Controller(failtarget, testopts, null))
                Assert.Throws<DeterministicErrorBackend.DeterministicErrorBackendException>(() => c.Backup(new string[] { DATAFOLDER }));

            Assert.That(failed, Is.True, "Failed to fail the upload");

            // Make spacing for the next backup
            Thread.Sleep(3000);

            // If we fail on the dlist after the upload, it is promoted to a regular backup
            var expectedFilesets = before || failOnLastDblock ? 2 : 1;

            // Make a new backup, should continue as normal
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new string[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                Assert.AreEqual(expectedFilesets, c.List().Filesets.Count());
            }

            // Verify that all is in order
            using (var c = new Controller("file://" + TARGETFOLDER, testopts.Expand(new { full_remote_verification = true }), null))
                TestUtils.AssertResults(c.Test(long.MaxValue));

            // Test that we can recreate
            var recreatedDatabaseFile = Path.Combine(BASEFOLDER, "recreated-database.sqlite");
            if (File.Exists(recreatedDatabaseFile))
                File.Delete(recreatedDatabaseFile);

            testopts["dbpath"] = recreatedDatabaseFile;

            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());

            // Check that we have 2 versions
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var listResults = c.List();
                TestUtils.AssertResults(listResults);
                Assert.AreEqual(expectedFilesets, listResults.Filesets.Count());
            }
        }

        [Test]
        [Category("Disruption")]
        [TestCase(true)]
        [TestCase(false)]
        public void TestRepairWithNoDlist(bool before)
        {
            var testopts = TestOptions;
            testopts["number-of-retries"] = "0";
            testopts["dblock-size"] = "10mb";

            // Deterministic error backend
            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            var failtarget = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
            var operation = before
                ? DeterministicErrorBackend.BackendAction.PutBefore
                : DeterministicErrorBackend.BackendAction.PutAfter;

            // Fail when uploading the first dlist file
            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                if (action == operation && remotename.Contains(".dlist."))
                    return true;

                return false;
            };

            using (var c = new Controller(failtarget, testopts, null))
                Assert.Throws<DeterministicErrorBackend.DeterministicErrorBackendException>(() => c.Backup(new string[] { DATAFOLDER }));

            // Test that we can repair
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());

            // Check that we have 1 version
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var listResults = c.List();
                TestUtils.AssertResults(listResults);
                Assert.AreEqual(1, listResults.Filesets.Count());
            }
        }


        [Test]
        [Category("Disruption")]
        [TestCase(true, true, 3, false)]
        [TestCase(false, false, 3, false)]
        [TestCase(true, false, 3, false)]
        [TestCase(false, true, 3, false)]
        [TestCase(true, true, 3, true)]
        [TestCase(false, true, 3, true)]
        [TestCase(false, false, 3, true)]
        [TestCase(true, false, 3, true)]
        public void TestMultiExceptionOnDlist(bool before, bool modifyInBetween, int runs, bool withBase)
        {
            var testopts = TestOptions;
            testopts["number-of-retries"] = "0";
            testopts["dblock-size"] = "10mb";
            // We always have at least 1 backup at the end
            var expectedFilesets = 1;
            // If the files are modified, this will create multiple versions
            if (modifyInBetween)
            {
                // If we prevent the file from being uploaded,
                // it will be a temporary file, and a new synthetic file
                // will be attempted, but that fails as well
                // If it is after, then the upload completes, 
                // and it will be promoted to a regular uploaded backup
                expectedFilesets += before ? 1 : runs;

                // If we modify with a base, then that creates a version as well
                if (withBase)
                    expectedFilesets += 1;
            }
            else if (before && !withBase)
            {
                // Because an incomplete fileset is treated as a partial fileset,
                // the final non-interrupted backup will see the syntheticly
                // created fileset as a partial fileset, and create a new non-partial
                // fileset, even though there are no changes between the two
                // This does not happen with a base, because it simply does not
                // attempt to upload the failing filesets
                expectedFilesets += 1;
            }

            if (withBase)
            {
                // Make a regular backup
                using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                    TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));
            }


            // Deterministic error backend
            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            var failtarget = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
            var operation = before
                ? DeterministicErrorBackend.BackendAction.PutBefore
                : DeterministicErrorBackend.BackendAction.PutAfter;

            // Fail when uploading the first dlist file
            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                if (action == operation && remotename.Contains(".dlist."))
                    return true;

                return false;
            };

            for (var i = 0; i < runs; i++)
            {
                if (modifyInBetween)
                    ModifySourceFiles();

                using (var c = new Controller(failtarget, testopts, null))
                    try
                    {
                        c.Backup(new string[] { DATAFOLDER });
                    }
                    catch (DeterministicErrorBackend.DeterministicErrorBackendException)
                    {
                    }

                // Prevent clashes in timestamps
                Thread.Sleep(3000);
            }

            if (modifyInBetween)
                ModifySourceFiles();

            // Make a new backup, should continue as normal
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var backupResults = c.Backup(new string[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                Assert.AreEqual(expectedFilesets, c.List().Filesets.Count());
            }

            // Verify that all is in order
            using (var c = new Controller("file://" + TARGETFOLDER, testopts.Expand(new { full_remote_verification = true }), null))
                TestUtils.AssertResults(c.Test(long.MaxValue));

            // Test that we can recreate
            var recreatedDatabaseFile = Path.Combine(BASEFOLDER, "recreated-database.sqlite");
            if (File.Exists(recreatedDatabaseFile))
                File.Delete(recreatedDatabaseFile);

            testopts["dbpath"] = recreatedDatabaseFile;

            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());

            // Check that we have 2 versions
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var listResults = c.List();
                TestUtils.AssertResults(listResults);
                Assert.AreEqual(expectedFilesets, listResults.Filesets.Count());
            }
        }

        private class LogSink : IMessageSink
        {
            public List<string> StartedFiles { get; } = new List<string>();
            public List<string> CompletedFiles { get; } = new List<string>();
            public void BackendEvent(BackendActionType action, BackendEventType type, string path, long size)
            {
                if (action == BackendActionType.Put && type == BackendEventType.Started)
                {
                    this.StartedFiles.Add(path);
                }
                if (action == BackendActionType.Put && type == BackendEventType.Completed)
                {
                    this.CompletedFiles.Add(path);
                }
            }

            public void SetBackendProgress(IBackendProgress progress)
            {
            }

            public void SetOperationProgress(IOperationProgress progress)
            {
            }

            public void WriteMessage(LogEntry entry)
            {
            }
        }

        [Test]
        [Category("Disruption")]
        public void TestDblockUploadWithOperationCancellation()
        {
            TestUploadWithOperationCancellation(".dblock.");
        }


        [Test]
        [Category("Disruption")]
        public void TestDindexUploadWithOperationCancellation()
        {
            TestUploadWithOperationCancellation(".dindex.");
        }

        [Test]
        [Category("Disruption")]
        public void TestDlistUploadWithOperationCancellation()
        {
            TestUploadWithOperationCancellation(".dlist.");
        }

        private void TestUploadWithOperationCancellation(string filefragment)
        {
            var testopts = TestOptions;
            testopts["number-of-retries"] = "1";
            testopts["dblock-size"] = "10mb";

            // Make a base backup
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));

            // Make a new backup that fails uploading a dblock file
            ModifySourceFiles();

            // Deterministic error backend
            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            var failtarget = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
            var uploadCount = 0;
            var failstep = DeterministicErrorBackend.BackendAction.PutBefore;

            // Fail the upload before
            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                if (action == failstep && remotename.Contains(filefragment))
                {
                    // Fail once with a cancellation
                    if (Interlocked.Increment(ref uploadCount) == 1)
                        throw new OperationCanceledException();
                }

                return false;
            };

            using (var c = new Controller(failtarget, testopts, null))
            {
                var sink = new LogSink();
                c.AppendSink(sink);
                var res = c.Backup(new string[] { DATAFOLDER });
                if (uploadCount == 0)
                    Assert.Fail("Upload count was not incremented");

                Assert.AreEqual(2, c.List().Filesets.Count());
                Assert.AreEqual(2, Directory.GetFiles(TARGETFOLDER, "*.dlist.*", SearchOption.TopDirectoryOnly).Count());
                Assert.AreEqual(7 * 2 + 1, sink.CompletedFiles.Count); // 7 completed (dblock + dindex) + 1 dlist
                Assert.AreEqual(7 * 2 + 2, sink.StartedFiles.Count); // 1 retry
                Assert.AreEqual(sink.CompletedFiles.Count(x => x.Contains(".dblock.")), sink.CompletedFiles.Count(x => x.Contains(".dindex.")));
                Assert.AreEqual(1, res.BackendStatistics.RetryAttempts);
            }

            uploadCount = 0;
            failstep = DeterministicErrorBackend.BackendAction.PutAfter;

            ModifySourceFiles();
            using (var c = new Controller(failtarget, testopts, null))
            {
                var sink = new LogSink();
                c.AppendSink(sink);
                var res = c.Backup(new string[] { DATAFOLDER });
                if (uploadCount == 0)
                    Assert.Fail("Upload count was not incremented");

                Assert.AreEqual(3, c.List().Filesets.Count());
                Assert.AreEqual(3, Directory.GetFiles(TARGETFOLDER, "*.dlist.*", SearchOption.TopDirectoryOnly).Count());
                Assert.AreEqual(7 * 2 + 1, sink.CompletedFiles.Count); // 7 completed (dblock + dindex) + 1 dlist
                Assert.AreEqual(7 * 2 + 2, sink.StartedFiles.Count); // 1 retry
                Assert.AreEqual(sink.CompletedFiles.Count(x => x.Contains(".dblock.")), sink.CompletedFiles.Count(x => x.Contains(".dindex.")));
                Assert.AreEqual(1, res.BackendStatistics.RetryAttempts);
            }

            ModifySourceFiles();
            // Create a regular backup
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));

            // Verify that all is in order
            using (var c = new Controller("file://" + TARGETFOLDER, testopts.Expand(new { full_remote_verification = true }), null))
                TestUtils.AssertResults(c.Test(long.MaxValue));

            // Test that we can recreate
            var recreatedDatabaseFile = Path.Combine(BASEFOLDER, "recreated-database.sqlite");
            if (File.Exists(recreatedDatabaseFile))
                File.Delete(recreatedDatabaseFile);

            testopts["dbpath"] = recreatedDatabaseFile;

            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());

            // Check that we have 3 versions
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var listResults = c.List();
                TestUtils.AssertResults(listResults);
                Assert.AreEqual(4, listResults.Filesets.Count());
            }
        }
    }
}