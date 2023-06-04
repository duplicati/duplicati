using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using Duplicati.Library.Common;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class RestoreHandlerTests : BasicSetupHelper
    {
        [Test]
        [Category("RestoreHandler")]
        public void RestoreEmptyFile()
        {
            string folderPath = Path.Combine(this.DATAFOLDER, "folder");
            Directory.CreateDirectory(folderPath);
            string filePath = Path.Combine(folderPath, "empty_file");
            File.WriteAllBytes(filePath, new byte[] { });

            BackupTargetFolder();

            // Issue #4148 described a situation where the folders containing the empty file were not recreated properly.
            Dictionary<string, string> restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["dont-compress-restore-paths"] = "true"
            };
            RestoreFiles(new[] { filePath }, restoreOptions);

            // We need to strip the root part of the path.  Otherwise, Path.Combine will simply return the second argument
            // if it's determined to be an absolute path.
            string rootString = SystemIO.IO_OS.GetPathRoot(filePath);
            string newPathPart = filePath.Substring(rootString.Length);
            if (Platform.IsClientWindows)
            {
                // On Windows, the drive letter is included in the path when the dont-compress-restore-paths option is used.
                // The drive letter is assumed to be the first character of the path root (e.g., C:\).
                newPathPart = Path.Combine(rootString.Substring(0, 1), filePath.Substring(rootString.Length));
            }

            string restoredFilePath = Path.Combine(restoreOptions["restore-path"], newPathPart);
            Assert.IsTrue(File.Exists(restoredFilePath));
        }

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
            File.WriteAllBytes(filePath, new byte[] { 0 });

            // Protect access rules on the file.
            FileSecurity fileSecurity = File.GetAccessControl(filePath);
            fileSecurity.SetAccessRuleProtection(true, true);
            File.SetAccessControl(filePath, fileSecurity);

            BackupTargetFolder();

            // First, restore without restoring permissions.
            Dictionary<string, string> restoreOptions = new Dictionary<string, string>(this.TestOptions) { ["restore-path"] = this.RESTOREFOLDER };
            RestoreFiles(new[] { filePath }, restoreOptions);

            string restoredFilePath = Path.Combine(this.RESTOREFOLDER, "file");
            Assert.IsTrue(File.Exists(restoredFilePath));

            FileSecurity restoredFileSecurity = File.GetAccessControl(restoredFilePath);
            Assert.IsFalse(restoredFileSecurity.AreAccessRulesProtected);

            // Remove the restored file so that the later restore avoids the "Restore completed
            // without errors but no files were restored" warning.
            File.Delete(restoredFilePath);


            // Restore with restoring permissions.
            restoreOptions["overwrite"] = "true";
            restoreOptions["restore-permissions"] = "true";
            RestoreFiles(new[] { filePath }, restoreOptions);

            restoredFilePath = Path.Combine(this.RESTOREFOLDER, "file");
            Assert.IsTrue(File.Exists(restoredFilePath));

            restoredFileSecurity = File.GetAccessControl(restoredFilePath);
            Assert.IsTrue(restoredFileSecurity.AreAccessRulesProtected);
        }

        [Test]
        [Category("RestoreHandler")]
        public void RestoreOnlyVersion()
        {
            var fileName = "file.txt";
            var fileToBackup = Path.Combine(DATAFOLDER, fileName);
            var fileContents = "first version";
            File.WriteAllText(fileToBackup, fileContents);

            BackupTargetFolder();

            var restoreOptions = new Dictionary<string, string>(TestOptions) { ["restore-path"] = RESTOREFOLDER };
            // The first fileid is the directory containing the file
            RestoreFiles(new[] { "&" + Path.Combine(fileToBackup, "&fileid=2") }, restoreOptions);

            var restoredFilePath = Path.Combine(RESTOREFOLDER, fileName);
            Assert.IsTrue(File.Exists(restoredFilePath));
            Assert.AreEqual(fileContents, File.ReadAllText(restoredFilePath));
        }

        [Test]
        [Category("RestoreHandler")]
        public void RestoreSecondVersion()
        {
            var fileName = "file.txt";
            var fileToBackup = Path.Combine(DATAFOLDER, fileName);

            var firstVersionContents = "first version";
            File.WriteAllText(fileToBackup, firstVersionContents);
            BackupTargetFolder();

            var secondVersionContents = "second version";
            File.WriteAllText(fileToBackup, secondVersionContents);
            BackupTargetFolder();

            var restoreOptions = new Dictionary<string, string>(TestOptions) { ["restore-path"] = RESTOREFOLDER };
            RestoreFiles(new[] { "&" + Path.Combine(fileToBackup, "&fileid=3") }, restoreOptions);

            var restoredFilePath = Path.Combine(RESTOREFOLDER, fileName);
            Assert.IsTrue(File.Exists(restoredFilePath));
            Assert.AreEqual(secondVersionContents, File.ReadAllText(restoredFilePath));
        }

        [Test]
        [Category("RestoreHandler")]
        public void RestoreAllVersions()
        {
            var fileNameNoExtension = "file";
            var fileToBackup = Path.Combine(DATAFOLDER, fileNameNoExtension + ".txt");

            var firstVersionContents = "first version";
            File.WriteAllText(fileToBackup, firstVersionContents);
            var firstModifiedTime = File.GetLastWriteTimeUtc(fileToBackup).ToLocalTime();
            BackupTargetFolder();

            // When restoring multiple versions the timestamp used in the filename only goes down to a second.
            // Ensure there is at least 1 second difference between the time the two files are backed up
            System.Threading.Thread.Sleep(1000);

            var secondVersionContents = "second version";
            File.WriteAllText(fileToBackup, secondVersionContents);
            var secondModifiedTime = File.GetLastWriteTimeUtc(fileToBackup).ToLocalTime();
            BackupTargetFolder();

            var restoreOptions = new Dictionary<string, string>(TestOptions) { ["restore-path"] = RESTOREFOLDER };
            RestoreFiles(new[]
            {
                "&" + Path.Combine(fileToBackup, "&fileid=2"),
                "&" + Path.Combine(fileToBackup, "&fileid=3")
            }, restoreOptions);

            var firstRestoredFile = Path.Combine(RESTOREFOLDER, fileNameNoExtension + firstModifiedTime.ToString("_yyMMdd-HHmmss") + ".txt");
            Assert.IsTrue(File.Exists(firstRestoredFile));
            Assert.AreEqual(firstVersionContents, File.ReadAllText(firstRestoredFile));

            var secondRestoredFile = Path.Combine(RESTOREFOLDER, fileNameNoExtension + secondModifiedTime.ToString("_yyMMdd-HHmmss") + ".txt");
            Assert.IsTrue(File.Exists(secondRestoredFile));
            Assert.AreEqual(secondVersionContents, File.ReadAllText(secondRestoredFile));
        }

        private void BackupTargetFolder()
        {
            using (var controller = new Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                IBackupResults backupResults = controller.Backup(new[] { DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }
        }

        private void RestoreFiles(string[] paths, Dictionary<string, string> restoreOptions)
        {
            using (var controller = new Controller("file://" + TARGETFOLDER, restoreOptions, null))
            {
                IRestoreResults restoreResults = controller.Restore(paths);
                Assert.AreEqual(0, restoreResults.Errors.Count(), $"Errors: {string.Join(System.Environment.NewLine, restoreResults.Errors)}");
                Assert.AreEqual(0, restoreResults.Warnings.Count(), $"Warnings: {string.Join(System.Environment.NewLine, restoreResults.Warnings)}");
            }
        }
    }
}