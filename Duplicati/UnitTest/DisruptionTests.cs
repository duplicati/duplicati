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
        private readonly int[] fileSizes = {10, 20, 30};

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

        private async Task RunPartialBackup(Controller controller)
        {
            this.ModifySourceFiles();

            // ReSharper disable once AccessToDisposedClosure
            Task backupTask = Task.Run(() => controller.Backup(new[] {this.DATAFOLDER}));

            // Block for a small amount of time to allow the ITaskControl to be associated
            // with the Controller.  Otherwise, the call to Stop will simply be a no-op.
            Thread.Sleep(1000);

            controller.Stop(true);
            await backupTask.ConfigureAwait(false);
        }

        public override void SetUp()
        {
            base.SetUp();
            this.ModifySourceFiles();
        }

        [Test]
        [Category("Disruption")]
        public async Task FilesetFiles()
        {
            // Choose a dblock size that is small enough so that more than one volume is needed.
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions)
            {
                ["dblock-size"] = "10mb",

                // This allows us to inspect the dlist files without needing the BackendManager (which is inaccessible here) to decrypt them.
                ["no-encryption"] = "true"
            };

            // Run a full backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                c.Backup(new[] {this.DATAFOLDER});
            }

            // Run a partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                await this.RunPartialBackup(c).ConfigureAwait(false);
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
                c.PurgeFiles(new Library.Utility.FilterExpression($"*{this.fileSizes[0]}*"));
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
                c.Repair();
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
            // Choose a dblock size that is small enough so that more than one volume is needed.
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions) {["dblock-size"] = "10mb"};

            // First, run two complete backups followed by a partial backup.  We will then set the keep-time
            // option so that the threshold lies between the first and second backups.
            DateTime firstBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                c.Backup(new[] {this.DATAFOLDER});
                firstBackupTime = c.List().Filesets.First().Time;
            }

            // Wait before the second backup so that we can more easily define the keep-time threshold
            // to lie between the first and second backups.
            Thread.Sleep(5000);
            DateTime secondBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                this.ModifySourceFiles();
                c.Backup(new[] {this.DATAFOLDER});
                secondBackupTime = c.List().Filesets.First().Time;
            }

            // Run a partial backup.
            DateTime thirdBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                await this.RunPartialBackup(c).ConfigureAwait(false);
                thirdBackupTime = c.List().Filesets.First().Time;
            }

            // Set the keep-time option so that the threshold lies between the first and second backups
            // and run the delete operation.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                options["keep-time"] = $"{(int) ((DateTime.Now - firstBackupTime).TotalSeconds - (secondBackupTime - firstBackupTime).TotalSeconds / 2)}s";
                c.Delete();

                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(2, filesets.Count);
                Assert.AreEqual(secondBackupTime, filesets[1].Time);
                Assert.AreEqual(BackupType.FULL_BACKUP, filesets[1].IsFullBackup);
                Assert.AreEqual(thirdBackupTime, filesets[0].Time);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, filesets[0].IsFullBackup);
            }

            // Run another partial backup.  We will then verify that a full backup is retained
            // even when all the "recent" backups are partial.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                await this.RunPartialBackup(c).ConfigureAwait(false);
                DateTime fourthBackupTime = c.List().Filesets.First().Time;

                // Set the keep-time option so that the threshold lies after the most recent full backup
                // and run the delete operation.
                options["keep-time"] = "1s";
                c.Delete();

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
            // Choose a dblock size that is small enough so that more than one volume is needed.
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions) {["dblock-size"] = "10mb"};

            // Run a full backup.
            DateTime firstBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                c.Backup(new[] {this.DATAFOLDER});
                firstBackupTime = c.List().Filesets.First().Time;
            }

            // Run a partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                await this.RunPartialBackup(c).ConfigureAwait(false);
            }

            // Run a partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                await this.RunPartialBackup(c).ConfigureAwait(false);
            }

            // Run a full backup.
            DateTime fourthBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                this.ModifySourceFiles();
                c.Backup(new[] {this.DATAFOLDER});
                fourthBackupTime = c.List().Filesets.First().Time;
            }

            // Run a partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                options["keep-versions"] = "2";
                await this.RunPartialBackup(c).ConfigureAwait(false);
                DateTime fifthBackupTime = c.List().Filesets.First().Time;

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
                c.Backup(new[] {this.DATAFOLDER});
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
        public async Task RetentionPolicyRetention()
        {
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions)
            {
                // Choose a dblock size that is small enough so that more than one volume is needed.
                ["dblock-size"] = "10mb",

                // This test assumes that we can perform 3 backups within 1 minute.
                ["retention-policy"] = "1m:59s,U:1m",
                ["no-backend-verification"] = "true"
            };

            DateTime firstBackupTime;
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                c.Backup(new[] {this.DATAFOLDER});
                firstBackupTime = c.List().Filesets.First().Time;

                List<IListResultFileset> filesets = c.List().Filesets.ToList();
                Assert.AreEqual(1, filesets.Count);

                this.ModifySourceFiles();
                c.Backup(new[] {this.DATAFOLDER});
                DateTime secondBackupTime = c.List().Filesets.First().Time;

                // Since the most recent backup is not considered in the retention logic, the only backup in the first time frame
                // is the initial one.  As a result, we should have 2 backups.
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
                await this.RunPartialBackup(c).ConfigureAwait(false);
                thirdBackupTime = c.List().Filesets.First().Time;

                // Since the most recent backup is not considered in the retention logic, there are no backups in the first time
                // frame.  The original 2 backups have now spilled over to the U:1m specification.  Since we keep the first
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
                c.Backup(new[] {this.DATAFOLDER});
                fourthBackupTime = c.List().Filesets.First().Time;

                // Since the most recent backup is not considered in the retention logic, the third backup is the only backup
                // in the first time frame.  There is no further spillover, so we simply add the fourth backup to the
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
                c.Backup(new[] {this.DATAFOLDER});
                DateTime fifthBackupTime = c.List().Filesets.First().Time;

                // Since the most recent backup is not considered in the retention logic, we now have two backups in the
                // first time frame: the third (partial) and fourth (full).  Since the first backup in each interval is
                // kept, we would typically keep just the third backup.  However, since we should not discard a full
                // backup in favor of a partial one, we keep the fourth as well.  We also still have the initial backup.
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
                c.Backup(new[] {this.DATAFOLDER});
                DateTime sixthBackupTime = c.List().Filesets.First().Time;

                // Since the most recent backup is not considered in the retention logic, we now have three backups in the
                // second time frame: the third (partial), fourth (full), and fifth (full).  Since we keep up to the first
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
            // Choose a dblock size that is small enough so that more than one volume is needed.
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions) {["dblock-size"] = "10mb"};

            // Run a complete backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(1, c.List().Filesets.Count());
                Assert.AreEqual(BackupType.FULL_BACKUP, c.List().Filesets.Single(x => x.Version == 0).IsFullBackup);
            }

            // Run a partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                await this.RunPartialBackup(c).ConfigureAwait(false);

                // If we interrupt the backup, the most recent Fileset should be marked as partial.
                Assert.AreEqual(2, c.List().Filesets.Count());
                Assert.AreEqual(BackupType.FULL_BACKUP, c.List().Filesets.Single(x => x.Version == 1).IsFullBackup);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, c.List().Filesets.Single(x => x.Version == 0).IsFullBackup);
            }

            // Restore files from the partial backup set.
            Dictionary<string, string> restoreOptions = new Dictionary<string, string>(options) {["restore-path"] = this.RESTOREFOLDER};
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                IListResults lastResults = c.List("*");
                string[] partialVersionFiles = lastResults.Files.Select(x => x.Path).Where(x => !Utility.IsFolder(x, File.GetAttributes)).ToArray();
                Assert.GreaterOrEqual(partialVersionFiles.Length, 1);
                c.Restore(partialVersionFiles);

                foreach (string filepath in partialVersionFiles)
                {
                    string filename = Path.GetFileName(filepath);
                    Assert.IsTrue(TestUtils.CompareFiles(filepath, Path.Combine(this.RESTOREFOLDER, filename ?? String.Empty), filename, false));
                }
            }

            // Recreating the database should preserve the backup types.
            File.Delete(this.DBFILE);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                c.Repair();
                Assert.AreEqual(2, c.List().Filesets.Count());
                Assert.AreEqual(BackupType.FULL_BACKUP, c.List().Filesets.Single(x => x.Version == 1).IsFullBackup);
                Assert.AreEqual(BackupType.PARTIAL_BACKUP, c.List().Filesets.Single(x => x.Version == 0).IsFullBackup);
            }

            // Run a complete backup.  Listing the Filesets should include both full and partial backups.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                c.Backup(new[] {this.DATAFOLDER});
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
                c.Restore(fullVersionFiles);

                foreach (string filepath in fullVersionFiles)
                {
                    string filename = Path.GetFileName(filepath);
                    Assert.IsTrue(TestUtils.CompareFiles(filepath, Path.Combine(this.RESTOREFOLDER, filename ?? String.Empty), filename, false));
                }
            }
        }
    }
}