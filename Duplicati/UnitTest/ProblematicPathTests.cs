using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Utility = Duplicati.Library.Utility.Utility;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class ProblematicPathTests : BasicSetupHelper
    {
        private static void WriteFile(string path, byte[] contents)
        {
            using (FileStream fileStream = SystemIO.IO_OS.FileOpenWrite(path))
            {
                Utility.CopyStream(new MemoryStream(contents), fileStream);
            }
        }

        [Test]
        [Category("ProblematicPath")]
        public void ExcludeProblematicPaths()
        {
            // A normal path that will be backed up.
            string normalFilePath = Path.Combine(this.DATAFOLDER, "normal");
            File.WriteAllBytes(normalFilePath, new byte[] {0, 1, 2});

            // A long path to exclude.
            string longFile = SystemIO.IO_OS.PathCombine(this.DATAFOLDER, new string('y', 255));
            WriteFile(longFile, new byte[] {0, 1});

            // A folder that ends with a dot to exclude.
            string folderWithDot = Path.Combine(this.DATAFOLDER, "folder_with_dot.");
            SystemIO.IO_OS.DirectoryCreate(folderWithDot);

            // A folder that ends with a space to exclude.
            string folderWithSpace = Path.Combine(this.DATAFOLDER, "folder_with_space ");
            SystemIO.IO_OS.DirectoryCreate(folderWithSpace);

            // A file that ends with a dot to exclude.
            string fileWithDot = Path.Combine(this.DATAFOLDER, "file_with_dot.");
            WriteFile(fileWithDot, new byte[] {0, 1});

            // A file that ends with a space to exclude.
            string fileWithSpace = Path.Combine(this.DATAFOLDER, "file_with_space ");
            WriteFile(fileWithSpace, new byte[] {0, 1});

            FilterExpression filter = new FilterExpression(longFile, false);
            filter = FilterExpression.Combine(filter, new FilterExpression(Util.AppendDirSeparator(folderWithDot), false));
            filter = FilterExpression.Combine(filter, new FilterExpression(Util.AppendDirSeparator(folderWithSpace), false));
            filter = FilterExpression.Combine(filter, new FilterExpression(fileWithDot, false));
            filter = FilterExpression.Combine(filter, new FilterExpression(fileWithSpace, false));

            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER}, filter);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IListResults listResults = c.List("*");
                Assert.AreEqual(0, listResults.Errors.Count());
                Assert.AreEqual(0, listResults.Warnings.Count());

                string[] backedUpPaths = listResults.Files.Select(x => x.Path).ToArray();
                Assert.AreEqual(2, backedUpPaths.Length);
                Assert.Contains(Util.AppendDirSeparator(this.DATAFOLDER), backedUpPaths);
                Assert.Contains(normalFilePath, backedUpPaths);
            }
        }

        [Test]
        [Category("ProblematicPath")]
        public void LongPath()
        {
            string folderPath = Path.Combine(this.DATAFOLDER, new string('x', 10));
            SystemIO.IO_OS.DirectoryCreate(folderPath);

            string fileName = new string('y', 255);
            string filePath = SystemIO.IO_OS.PathCombine(folderPath, fileName);
            byte[] fileBytes = {0, 1, 2};
            WriteFile(filePath, fileBytes);

            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            Dictionary<string, string> restoreOptions = new Dictionary<string, string>(this.TestOptions) {["restore-path"] = this.RESTOREFOLDER};
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                IRestoreResults restoreResults = c.Restore(new[] {filePath});
                Assert.AreEqual(0, restoreResults.Errors.Count());
                Assert.AreEqual(0, restoreResults.Warnings.Count());
            }

            string restoreFilePath = SystemIO.IO_OS.PathCombine(this.RESTOREFOLDER, fileName);
            Assert.IsTrue(SystemIO.IO_OS.FileExists(restoreFilePath));

            MemoryStream restoredStream = new MemoryStream();
            using (FileStream fileStream = SystemIO.IO_OS.FileOpenRead(restoreFilePath))
            {
                Utility.CopyStream(fileStream, restoredStream);
            }

            Assert.AreEqual(fileBytes, restoredStream.ToArray());
        }

        [Test]
        [Category("ProblematicPath")]
        [TestCase("ends_with_dot.")]
        [TestCase("ends_with_dots..")]
        [TestCase("ends_with_space ")]
        [TestCase("ends_with_spaces  ")]
        public void ProblematicSuffixes(string pathComponent)
        {
            string folderPath = SystemIO.IO_OS.PathCombine(this.DATAFOLDER, pathComponent);
            SystemIO.IO_OS.DirectoryCreate(folderPath);

            string filePath = SystemIO.IO_OS.PathCombine(folderPath, pathComponent);
            byte[] fileBytes = {0, 1, 2};
            WriteFile(filePath, fileBytes);

            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            Dictionary<string, string> restoreOptions = new Dictionary<string, string>(this.TestOptions) {["restore-path"] = this.RESTOREFOLDER};
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                IRestoreResults restoreResults = c.Restore(new[] {filePath});
                Assert.AreEqual(0, restoreResults.Errors.Count());
                Assert.AreEqual(0, restoreResults.Warnings.Count());
            }

            string restoreFilePath = SystemIO.IO_OS.PathCombine(this.RESTOREFOLDER, pathComponent);
            Assert.IsTrue(SystemIO.IO_OS.FileExists(restoreFilePath));

            MemoryStream restoredStream = new MemoryStream();
            using (FileStream fileStream = SystemIO.IO_OS.FileOpenRead(restoreFilePath))
            {
                Utility.CopyStream(fileStream, restoredStream);
            }

            Assert.AreEqual(fileBytes, restoredStream.ToArray());
        }
    }
}