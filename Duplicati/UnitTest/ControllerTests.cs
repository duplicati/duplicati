using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class ControllerTests : BasicSetupHelper
    {
        [Test]
        [Category("Controller")]
        public void DeleteConfigurationWithSameBackend()
        {
            string filePath = Path.Combine(this.DATAFOLDER, "file");
            File.WriteAllBytes(filePath, new byte[] {0, 1, 2});

            Dictionary<string, string> firstOptions = new Dictionary<string, string>(this.TestOptions);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, firstOptions, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Keep track of the backend files from the first backup configuration so that we can
            // check that they remain after we remove the backend files from the second backup
            // configuration.
            string[] firstBackupFiles = Directory.GetFiles(this.TARGETFOLDER);
            Assert.Greater(firstBackupFiles.Length, 0);

            Dictionary<string, string> secondOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["dbpath"] = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
            };
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, secondOptions, null))
            {
                // An exception should be thrown due to unrecognized files in the target folder.
                // ReSharper disable once AccessToDisposedClosure
                Assert.That(() => c.Backup(new[] {this.DATAFOLDER}), Throws.Exception);
            }

            // We should be able to safely remove backend files from the second backup by referring
            // to the local database.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, secondOptions, null))
            {
                IListRemoteResults listResults = c.DeleteAllRemoteFiles();
                Assert.AreEqual(0, listResults.Errors.Count());
                Assert.AreEqual(0, listResults.Warnings.Count());
            }

            // After we delete backend files from the second backup configuration, those from the first
            // configuration should remain (see issues #2678, #3845, and #4244).
            foreach (string file in firstBackupFiles)
            {
                Assert.IsTrue(File.Exists(file));
            }
        }
    }
}