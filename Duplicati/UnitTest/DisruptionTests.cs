using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using NUnit.Framework;
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

        public override void SetUp()
        {
            base.SetUp();
            this.ModifySourceFiles();
        }

        [Test]
        [Category("Disruption")]
        public async Task StopAfterCurrentFile()
        {
            // Choose a dblock size that is small enough so that more than one volume is needed.
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions) {["dblock-size"] = "1mb"};

            // Run a complete backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(1, c.List().Filesets.Count());
                Assert.AreEqual(BackupType.FULL_BACKUP, c.List().Filesets.Single(x => x.Version == 0).IsFullBackup);
            }

            // Modify the source files and interrupt a backup.
            this.ModifySourceFiles();
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                // ReSharper disable once AccessToDisposedClosure
                Task<IBackupResults> backupTask = Task.Run(() => c.Backup(new[] {this.DATAFOLDER}));

                // Block for a small amount of time to allow the ITaskControl to be associated
                // with the Controller.  Otherwise, the call to Stop will simply be a no-op.
                Thread.Sleep(1000);

                // If we interrupt the backup, the most recent Fileset should be marked as partial.
                c.Stop(true);
                await backupTask;
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

            // Run a complete backup.  Listing the Filesets should omit the previous partial backup.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(2, c.List().Filesets.Count());
                Assert.AreEqual(BackupType.FULL_BACKUP, c.List().Filesets.Single(x => x.Version == 1).IsFullBackup);
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