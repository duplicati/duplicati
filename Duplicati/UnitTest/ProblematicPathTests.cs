using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duplicati.Library.Common;
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
        public void DirectoriesWithWildcards()
        {
            const string file = "file";
            List<string> directories = new List<string>();

            // Keep expected match counts since they'll differ between
            // Linux and Windows.
            var questionMarkWildcardShouldMatchCount = 0;
            var verbatimAsteriskShouldMatchCount = 0;

            const string asterisk = "*";
            string dirWithAsterisk = Path.Combine(this.DATAFOLDER, asterisk);
            // Windows does not support literal asterisks in paths.
            if (!Platform.IsClientWindows)
            {
                SystemIO.IO_OS.DirectoryCreate(dirWithAsterisk);
                WriteFile(SystemIO.IO_OS.PathCombine(dirWithAsterisk, file), new byte[] {0});
                directories.Add(dirWithAsterisk);
                questionMarkWildcardShouldMatchCount++;
                verbatimAsteriskShouldMatchCount++;
            }

            const string questionMark = "?";
            string dirWithQuestionMark = Path.Combine(this.DATAFOLDER, questionMark);
            // Windows does not support literal question marks in paths.
            if (!Platform.IsClientWindows)
            {
                SystemIO.IO_OS.DirectoryCreate(dirWithQuestionMark);
                WriteFile(SystemIO.IO_OS.PathCombine(dirWithQuestionMark, file), new byte[] { 1 });
                directories.Add(dirWithQuestionMark);
                questionMarkWildcardShouldMatchCount++;
            }

            // Include at least one single character directory in Windows
            // for a '?' wildcard can match on
            const string singleCharacterDir = "X";
            string dirWithSingleCharacter = Path.Combine(this.DATAFOLDER, singleCharacterDir);
            SystemIO.IO_OS.DirectoryCreate(dirWithSingleCharacter);
            WriteFile(SystemIO.IO_OS.PathCombine(dirWithSingleCharacter, file), new byte[] { 2 });
            directories.Add(dirWithSingleCharacter);
            questionMarkWildcardShouldMatchCount++;

            const string dir = "dir";
            string normalDir = Path.Combine(this.DATAFOLDER, dir);
            SystemIO.IO_OS.DirectoryCreate(normalDir);
            WriteFile(SystemIO.IO_OS.PathCombine(normalDir, file), new byte[] {3});
            directories.Add(normalDir);

            // Backup all files.
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Restore all files.
            Dictionary<string, string> restoreOptions = new Dictionary<string, string>(options) {["restore-path"] = this.RESTOREFOLDER};
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                IRestoreResults restoreResults = c.Restore(null);
                Assert.AreEqual(0, restoreResults.Errors.Count());
                Assert.AreEqual(0, restoreResults.Warnings.Count());

                foreach (string directory in directories)
                {
                    string directoryName = SystemIO.IO_OS.PathGetFileName(directory);
                    foreach (string expectedFilePath in SystemIO.IO_OS.EnumerateFiles(directory))
                    {
                        string fileName = SystemIO.IO_OS.PathGetFileName(expectedFilePath);
                        string restoredFilePath = SystemIO.IO_OS.PathCombine(this.RESTOREFOLDER, directoryName, fileName);
                        Assert.IsTrue(TestUtils.CompareFiles(expectedFilePath, restoredFilePath, expectedFilePath, false));
                    }
                }

                // List results using * should return a match for each directory.
                IListResults listResults = c.List(SystemIO.IO_OS.PathCombine(dirWithAsterisk, file));
                Assert.AreEqual(0, listResults.Errors.Count());
                Assert.AreEqual(0, listResults.Warnings.Count());
                Assert.AreEqual(directories.Count, listResults.Files.Count());

                listResults = c.List(SystemIO.IO_OS.PathCombine(dirWithQuestionMark, file));
                Assert.AreEqual(0, listResults.Errors.Count());
                Assert.AreEqual(0, listResults.Warnings.Count());
                // List results using ? should return 3 matches in Linux,
                // one for the directory with '*' and one for the directory
                // with '?', plus one for directory 'X'; but should return
                // 1 matches in Windows just for directory 'X'.
                Assert.AreEqual(questionMarkWildcardShouldMatchCount, listResults.Files.Count());
            }

            SystemIO.IO_OS.DirectoryDelete(this.RESTOREFOLDER, true);

            // Restore one file at a time using the verbatim identifier.
            foreach (string directory in directories)
            {
                foreach (string expectedFilePath in SystemIO.IO_OS.EnumerateFiles(directory))
                {
                    using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                    {
                        string verbatimFilePath = "@" + expectedFilePath;

                        // Verify that list result using verbatim identifier contains only one file.
                        IListResults listResults = c.List(verbatimFilePath);
                        Assert.AreEqual(0, listResults.Errors.Count());
                        Assert.AreEqual(0, listResults.Warnings.Count());
                        Assert.AreEqual(1, listResults.Files.Count());
                        Assert.AreEqual(expectedFilePath, listResults.Files.Single().Path);

                        IRestoreResults restoreResults = c.Restore(new[] {verbatimFilePath});
                        Assert.AreEqual(0, restoreResults.Errors.Count());
                        Assert.AreEqual(0, restoreResults.Warnings.Count());

                        string fileName = SystemIO.IO_OS.PathGetFileName(expectedFilePath);
                        string restoredFilePath = SystemIO.IO_OS.PathCombine(this.RESTOREFOLDER, fileName);
                        Assert.IsTrue(TestUtils.CompareFiles(expectedFilePath, restoredFilePath, expectedFilePath, false));

                        SystemIO.IO_OS.FileDelete(restoredFilePath);
                    }
                }
            }

            // Backup with asterisk in include filter should include all directories.
            FilterExpression filter = new FilterExpression(dirWithAsterisk);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER}, filter);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
                Assert.AreEqual(directories.Count, backupResults.ExaminedFiles);
            }

            // Backup with verbatim asterisk in include filter should include
            // one directory in Linux and zero directories in Windows.
            filter = new FilterExpression("@" + SystemIO.IO_OS.PathCombine(dirWithAsterisk, file));
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER}, filter);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
                Assert.AreEqual(verbatimAsteriskShouldMatchCount, backupResults.ExaminedFiles);
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