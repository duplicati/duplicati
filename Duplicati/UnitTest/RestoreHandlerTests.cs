using System;
using Duplicati.Library.Common;
using Duplicati.Library.Main;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using Duplicati.Library.Interface;

namespace Duplicati.UnitTest
{
    public class RestoreHandlerTests : BasicSetupHelper
    {
        [Test]
        [Category("RestoreHandler")]
        public void RestoreInheritanceBreaks()
        {
            if (!Platform.IsClientWindows)
            {
                return;
            }

            string folderPath = Path.Combine(this.DATAFOLDER, "folder");
            Directory.CreateDirectory(folderPath);
            string filePath = Path.Combine(folderPath, "file");
            File.WriteAllBytes(filePath, new byte[] {0});

            // Protect access rules on the file.
            FileSecurity fileSecurity = File.GetAccessControl(filePath);
            fileSecurity.SetAccessRuleProtection(true, true);
            File.SetAccessControl(filePath, fileSecurity);

            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // First, restore without restoring permissions.
            Dictionary<string, string> restoreOptions = new Dictionary<string, string>(this.TestOptions) {["restore-path"] = this.RESTOREFOLDER};
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                IRestoreResults restoreResults = c.Restore(new[] {filePath});
                Assert.AreEqual(0, restoreResults.Errors.Count());
                Assert.AreEqual(0, restoreResults.Warnings.Count());

                string restoredFilePath = Path.Combine(this.RESTOREFOLDER, "file");
                Assert.IsTrue(File.Exists(restoredFilePath));

                FileSecurity restoredFileSecurity = File.GetAccessControl(restoredFilePath);
                Assert.IsFalse(restoredFileSecurity.AreAccessRulesProtected);
            }

            // Restore with restoring permissions.
            restoreOptions["overwrite"] = "true";
            restoreOptions["restore-permissions"] = "true";
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                IRestoreResults restoreResults = c.Restore(new[] {filePath});
                Assert.AreEqual(0, restoreResults.Errors.Count());
                foreach (var warning in restoreResults.Warnings)
                {
                    Console.WriteLine(warning);
                }
                Assert.AreEqual(0, restoreResults.Warnings.Count());

                string restoredFilePath = Path.Combine(this.RESTOREFOLDER, "file");
                Assert.IsTrue(File.Exists(restoredFilePath));

                FileSecurity restoredFileSecurity = File.GetAccessControl(restoredFilePath);
                Assert.IsTrue(restoredFileSecurity.AreAccessRulesProtected);
            }
        }
    }
}