using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class DisruptionTests : BasicSetupHelper
    {
        private void ModifySourceFiles()
        {
            int[] fileSizes = {10, 20, 30};
            foreach (int size in fileSizes)
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
            
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                // If we allow the backup to complete, the Fileset should be marked as a full.
                c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(1, c.List().Filesets.Count());
                Assert.AreEqual(1, c.List().Filesets.Single(x => x.Version == 0).IsFullBackup);

                this.ModifySourceFiles();
                Task<IBackupResults> backupTask = Task.Run(() => c.Backup(new[] {this.DATAFOLDER}));
                
                // Block for a small amount of time to allow the ITaskControl to be associated
                // with the Controller.  Otherwise, the call to Stop will simply be a no-op.
                Thread.Sleep(1000);
                
                // If we interrupt the backup, the most recent Fileset should be marked as partial.
                c.Stop(true);
                await backupTask;
                Assert.AreEqual(2, c.List().Filesets.Count());
                Assert.AreEqual(1, c.List().Filesets.Single(x => x.Version == 1).IsFullBackup);
                Assert.AreEqual(0, c.List().Filesets.Single(x => x.Version == 0).IsFullBackup);
            }
        }
    }
}