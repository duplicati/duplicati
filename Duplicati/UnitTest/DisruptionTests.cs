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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
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

        private async Task<IBackupResults> RunPartialBackup(Controller controller)
        {
            this.ModifySourceFiles();

            var stopped = new TaskCompletionSource<bool>();
            controller.OnOperationStarted = r =>
            {
                var pv = r as ITaskControlProvider;
                if (pv == null)
                    throw new Exception("Task control provider not found");
#if DEBUG
                pv.TaskControl.TestMethodCallback = (path) =>
                {
                    if (path.EndsWith(this.fileSizes[2] + "MB"))
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

            return await backupTask.ConfigureAwait(false);
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
                IBackupResults backupResults = await this.RunPartialBackup(c).ConfigureAwait(false);
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
                IPurgeFilesResults purgeResults = c.PurgeFiles(new Library.Utility.FilterExpression($"{this.DATAFOLDER}/*{this.fileSizes[0]}*"));
                Assert.AreEqual(0, purgeResults.Errors.Count());
                Assert.AreEqual(0, purgeResults.Warnings.Count());

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
                IRepairResults repairResults = c.Repair();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Count());

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
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
                firstBackupTime = c.List().Filesets.First().Time;
            }

            // Wait before the second backup so that we can more easily define the keep-time threshold
            // to lie between the first and second backups.
            Thread.Sleep(5000);
            DateTime secondBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                this.ModifySourceFiles();
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
                secondBackupTime = c.List().Filesets.First().Time;
            }

            // Run a partial backup.
            DateTime thirdBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = await this.RunPartialBackup(c).ConfigureAwait(false);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(1, backupResults.Warnings.Count());
                thirdBackupTime = c.List().Filesets.First().Time;
            }

            // Set the keep-time option so that the threshold lies between the first and second backups
            // and run the delete operation.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                options["keep-time"] = $"{(int)((DateTime.Now - firstBackupTime).TotalSeconds - (secondBackupTime - firstBackupTime).TotalSeconds / 2)}s";
                IDeleteResults deleteResults = c.Delete();
                Assert.AreEqual(0, deleteResults.Errors.Count());
                Assert.AreEqual(0, deleteResults.Warnings.Count());

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
                IBackupResults backupResults = await this.RunPartialBackup(c).ConfigureAwait(false);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(1, backupResults.Warnings.Count());
                DateTime fourthBackupTime = c.List().Filesets.First().Time;

                // Set the keep-time option so that the threshold lies after the most recent full backup
                // and run the delete operation.
                options["keep-time"] = "1s";
                IDeleteResults deleteResults = c.Delete();
                Assert.AreEqual(0, deleteResults.Errors.Count());
                Assert.AreEqual(0, deleteResults.Warnings.Count());

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
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
                firstBackupTime = c.List().Filesets.First().Time;
            }

            // Run a partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = await this.RunPartialBackup(c).ConfigureAwait(false);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(1, backupResults.Warnings.Count());
            }

            // Run a partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = await this.RunPartialBackup(c).ConfigureAwait(false);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(1, backupResults.Warnings.Count());
            }

            // Run a full backup.
            DateTime fourthBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                this.ModifySourceFiles();
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
                fourthBackupTime = c.List().Filesets.First().Time;
            }

            // Run a partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                options["keep-versions"] = "2";
                IBackupResults backupResults = await this.RunPartialBackup(c).ConfigureAwait(false);
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
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
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
            {
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Run a partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = await this.RunPartialBackup(c).ConfigureAwait(false);
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
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
                firstBackupTime = c.List().Filesets.First().Time;

                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(1, filesets.Count);

                this.ModifySourceFiles();
                backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
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
                IBackupResults backupResults = await this.RunPartialBackup(c).ConfigureAwait(false);
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
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
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
                backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
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
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());

                Assert.AreEqual(1, c.List().Filesets.Count());
                Assert.AreEqual(BackupType.FULL_BACKUP, c.List().Filesets.Single(x => x.Version == 0).IsFullBackup);
            }

            // Run a partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = await this.RunPartialBackup(c).ConfigureAwait(false);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(1, backupResults.Warnings.Count());
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
                IListResults lastResults = c.List("*");
                string[] partialVersionFiles = lastResults.Files.Select(x => x.Path).Where(x => !Utility.IsFolder(x, File.GetAttributes)).ToArray();
                Assert.GreaterOrEqual(partialVersionFiles.Length, 1);
                c.Restore(partialVersionFiles);

                foreach (string filepath in partialVersionFiles)
                {
                    string filename = Path.GetFileName(filepath);
                    TestUtils.AssertFilesAreEqual(filepath, Path.Combine(this.RESTOREFOLDER, filename ?? String.Empty), false, filename);
                }
            }

            // Recreating the database should preserve the backup types.
            File.Delete(this.DBFILE);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IRepairResults repairResults = c.Repair();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Count());

                Assert.AreEqual(2, c.List().Filesets.Count());
                Assert.AreEqual(BackupType.FULL_BACKUP, c.List().Filesets.Single(x => x.Version == 1).IsFullBackup);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, c.List().Filesets.Single(x => x.Version == 0).IsFullBackup);
            }

            // Run a complete backup. Listing the Filesets should include both full and partial backups.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
                Assert.AreEqual(3, c.List().Filesets.Count());

                Assert.AreEqual(BackupType.FULL_BACKUP, c.List().Filesets.Single(x => x.Version == 2).IsFullBackup);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, c.List().Filesets.Single(x => x.Version == 1).IsFullBackup);
                Assert.AreEqual(BackupType.FULL_BACKUP, c.List().Filesets.Single(x => x.Version == 0).IsFullBackup);
            }

            // Restore files from the full backup set.
            restoreOptions["overwrite"] = "true";
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                IListResults lastResults = c.List("*");
                string[] fullVersionFiles = lastResults.Files.Select(x => x.Path).Where(x => !Utility.IsFolder(x, File.GetAttributes)).ToArray();
                Assert.AreEqual(this.fileSizes.Length, fullVersionFiles.Length);

                IRestoreResults restoreResults = c.Restore(fullVersionFiles);
                Assert.AreEqual(0, restoreResults.Errors.Count());
                Assert.AreEqual(0, restoreResults.Warnings.Count());

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
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());

                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(1, filesets.Count);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[0].IsFullBackup);
            }

            // Interrupt a backup with "abort".
            this.ModifySourceFiles();
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                // ReSharper disable once AccessToDisposedClosure
                Task backupTask = Task.Run(() => c.Backup(new[] { this.DATAFOLDER }));

                // Block for a small amount of time to allow the ITaskControl to be associated
                // with the Controller. Otherwise, the call to Stop will simply be a no-op.
                Thread.Sleep(1000);

                c.Abort();
                try
                {
                    await backupTask.ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                }
            }

            // The next backup should proceed without issues.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());

                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(2, filesets.Count);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[1].IsFullBackup);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[0].IsFullBackup);
            }

            // Restore from the backup that followed the interruption.
            Dictionary<string, string> restoreOptions = new Dictionary<string, string>(options) { ["restore-path"] = this.RESTOREFOLDER };
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                IListResults lastResults = c.List("*");
                string[] fullVersionFiles = lastResults.Files.Select(x => x.Path).Where(x => !Utility.IsFolder(x, File.GetAttributes)).ToArray();
                Assert.AreEqual(this.fileSizes.Length, fullVersionFiles.Length);

                IRestoreResults restoreResults = c.Restore(fullVersionFiles);
                Assert.AreEqual(0, restoreResults.Errors.Count());
                Assert.AreEqual(0, restoreResults.Warnings.Count());

                foreach (string filepath in fullVersionFiles)
                {
                    string filename = Path.GetFileName(filepath);
                    TestUtils.AssertFilesAreEqual(filepath, Path.Combine(this.RESTOREFOLDER, filename ?? String.Empty), false, filename);
                }
            }
        }

        [Test]
        [Category("Disruption")]
        public void TestFailedUploadWithNoRetries()
        {
            var testopts = TestOptions;
            testopts["number-of-retries"] = "0";
            testopts["dblock-size"] = "10mb";

            // Make a base backup
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new string[] { DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Make a new backup that fails uploading a dblock file
            ModifySourceFiles();

            // Deterministic error backend
            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            var failtarget = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;

            var hasFailed = false;
            var secondUploadStarted = false;
            var secondUploadCompleted = false;

            // Fail the compact after the first dblock put is completed
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

            using (var c = new Library.Main.Controller(failtarget, testopts, null))
                Assert.Throws<DeterministicErrorBackend.DeterministicErrorBackendException>(() => c.Backup(new string[] { DATAFOLDER }));

            Assert.That(secondUploadStarted, Is.True, "Second upload was not started");
            Assert.That(secondUploadCompleted, Is.True, "Second upload was not started");

            // Create a regular backup
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new string[] { DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Verify that all is in order
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { full_remote_verification = true }), null))
            {
                var r = c.Test(long.MaxValue);
                Assert.AreEqual(0, r.Errors.Count());
                Assert.AreEqual(0, r.Warnings.Count());
                Assert.IsFalse(r.Verifications.Any(p => p.Value.Any()));
            }

            // Test that we can recreate
            var recreatedDatabaseFile = Path.Combine(BASEFOLDER, "recreated-database.sqlite");
            if (File.Exists(recreatedDatabaseFile))
                File.Delete(recreatedDatabaseFile);

            testopts["dbpath"] = recreatedDatabaseFile;

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IRepairResults repairResults = c.Repair();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Count());
            }

            // Check that we have 3 versions
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IListResults listResults = c.List();
                Assert.AreEqual(0, listResults.Errors.Count());
                Assert.AreEqual(0, listResults.Warnings.Count());
                Assert.AreEqual(3, listResults.Filesets.Count());
            }
        }
    }
}