using Duplicati.Library.Common;
using Duplicati.Library.Main;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using Duplicati.Library.Common.IO;

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
            /* TODO-DNC
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
                c.Backup(new[] {this.DATAFOLDER});
            }

            // First, restore without restoring permissions.
            Dictionary<string, string> restoreOptions = new Dictionary<string, string>(this.TestOptions) {["restore-path"] = this.RESTOREFOLDER};
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                c.Restore(new[] {filePath});
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
                c.Restore(new[] {filePath});
                string restoredFilePath = Path.Combine(this.RESTOREFOLDER, "file");
                Assert.IsTrue(File.Exists(restoredFilePath));

                FileSecurity restoredFileSecurity = File.GetAccessControl(restoredFilePath);
                Assert.IsTrue(restoredFileSecurity.AreAccessRulesProtected);
            }
            */
        }
    }
}