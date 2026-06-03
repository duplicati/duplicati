// Copyright (C) 2026, The Duplicati Team
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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Main;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Utility = Duplicati.Library.Utility.Utility;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class ProblematicPathTests : BasicSetupHelper
    {
        [Test]
        [Category("ProblematicPath")]
        public async Task DirectoriesWithWildcardsAsync()
        {
            const string file = "file";
            var directories = new List<string>();

            // Keep expected match counts since they'll differ between
            // Linux and Windows.
            var questionMarkWildcardShouldMatchCount = 0;
            var verbatimAsteriskShouldMatchCount = 0;

            const string asterisk = "*";
            var dirWithAsterisk = Path.Combine(this.DATAFOLDER, asterisk);
            // Windows does not support literal asterisks in paths.
            if (!OperatingSystem.IsWindows())
            {
                SystemIO.IO_OS.DirectoryCreate(dirWithAsterisk);
                TestUtils.WriteFile(SystemIO.IO_OS.PathCombine(dirWithAsterisk, file), new byte[] { 0 });
                directories.Add(dirWithAsterisk);
                questionMarkWildcardShouldMatchCount++;
                verbatimAsteriskShouldMatchCount++;
            }

            const string questionMark = "?";
            var dirWithQuestionMark = Path.Combine(this.DATAFOLDER, questionMark);
            // Windows does not support literal question marks in paths.
            if (!OperatingSystem.IsWindows())
            {
                SystemIO.IO_OS.DirectoryCreate(dirWithQuestionMark);
                TestUtils.WriteFile(SystemIO.IO_OS.PathCombine(dirWithQuestionMark, file), new byte[] { 1 });
                directories.Add(dirWithQuestionMark);
                questionMarkWildcardShouldMatchCount++;
            }

            // Include at least one single character directory in Windows
            // for a '?' wildcard can match on
            const string singleCharacterDir = "X";
            var dirWithSingleCharacter = Path.Combine(this.DATAFOLDER, singleCharacterDir);
            SystemIO.IO_OS.DirectoryCreate(dirWithSingleCharacter);
            TestUtils.WriteFile(SystemIO.IO_OS.PathCombine(dirWithSingleCharacter, file), new byte[] { 2 });
            directories.Add(dirWithSingleCharacter);
            questionMarkWildcardShouldMatchCount++;

            const string dir = "dir";
            var normalDir = Path.Combine(this.DATAFOLDER, dir);
            SystemIO.IO_OS.DirectoryCreate(normalDir);
            TestUtils.WriteFile(SystemIO.IO_OS.PathCombine(normalDir, file), new byte[] { 3 });
            directories.Add(normalDir);

            // Backup all files.
            var options = new Dictionary<string, string>(this.TestOptions);
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = await c.BackupAsync(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Restore all files.
            var restoreOptions = new Dictionary<string, string>(options) { ["restore-path"] = this.RESTOREFOLDER };
            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                var restoreResults = await c.RestoreAsync(null);
                Assert.AreEqual(0, restoreResults.Errors.Count());
                Assert.AreEqual(0, restoreResults.Warnings.Count());

                TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Restore");

                // List results using * should return a match for each directory.
                var listResults = await c.ListAsync(SystemIO.IO_OS.PathCombine(dirWithAsterisk, file));
                Assert.AreEqual(0, listResults.Errors.Count());
                Assert.AreEqual(0, listResults.Warnings.Count());
                Assert.AreEqual(directories.Count, listResults.Files.Count());

                listResults = await c.ListAsync(SystemIO.IO_OS.PathCombine(dirWithQuestionMark, file));
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
            foreach (var directory in directories)
            {
                foreach (var expectedFilePath in SystemIO.IO_OS.EnumerateFiles(directory))
                {
                    using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                    {
                        var verbatimFilePath = "@" + expectedFilePath;

                        // Verify that list result using verbatim identifier contains only one file.
                        var listResults = await c.ListAsync(verbatimFilePath);
                        Assert.AreEqual(0, listResults.Errors.Count());
                        Assert.AreEqual(0, listResults.Warnings.Count());
                        Assert.AreEqual(1, listResults.Files.Count());
                        Assert.AreEqual(expectedFilePath, listResults.Files.Single().Path);

                        var restoreResults = await c.RestoreAsync(new[] { verbatimFilePath });
                        Assert.AreEqual(0, restoreResults.Errors.Count());
                        Assert.AreEqual(0, restoreResults.Warnings.Count());

                        var fileName = SystemIO.IO_OS.PathGetFileName(expectedFilePath);
                        var restoredFilePath = SystemIO.IO_OS.PathCombine(this.RESTOREFOLDER, fileName);
                        TestUtils.AssertFilesAreEqual(expectedFilePath, restoredFilePath, false, expectedFilePath);

                        SystemIO.IO_OS.FileDelete(restoredFilePath);
                    }
                }
            }

            // Backup with asterisk in include filter should include all directories.
            var filter = new FilterExpression(dirWithAsterisk);
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = await c.BackupAsync(new[] { this.DATAFOLDER }, filter);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
                Assert.AreEqual(directories.Count, backupResults.ExaminedFiles);
            }

            // Block for a small amount of time to avoid clock issues when quickly running successive backups.
            System.Threading.Thread.Sleep(1000);

            // Backup with verbatim asterisk in include filter should include
            // one directory in Linux and zero directories in Windows.
            filter = new FilterExpression("@" + SystemIO.IO_OS.PathCombine(dirWithAsterisk, file));
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = await c.BackupAsync(new[] { this.DATAFOLDER }, filter);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
                Assert.AreEqual(verbatimAsteriskShouldMatchCount, backupResults.ExaminedFiles);
            }
        }

        [Test]
        [Category("ProblematicPath")]
        public async Task ExcludeProblematicPathsAsync()
        {
            // A normal path that will be backed up.
            var normalFilePath = Path.Combine(this.DATAFOLDER, "normal");
            File.WriteAllBytes(normalFilePath, new byte[] { 0, 1, 2 });

            // A long path to exclude.
            var longFile = SystemIO.IO_OS.PathCombine(this.DATAFOLDER, new string('y', 255));
            TestUtils.WriteFile(longFile, new byte[] { 0, 1 });

            // A folder that ends with a dot to exclude.
            var folderWithDot = Path.Combine(this.DATAFOLDER, "folder_with_dot.");
            SystemIO.IO_OS.DirectoryCreate(folderWithDot);

            // A folder that ends with a space to exclude.
            var folderWithSpace = Path.Combine(this.DATAFOLDER, "folder_with_space ");
            SystemIO.IO_OS.DirectoryCreate(folderWithSpace);

            // A file that ends with a dot to exclude.
            var fileWithDot = Path.Combine(this.DATAFOLDER, "file_with_dot.");
            TestUtils.WriteFile(fileWithDot, new byte[] { 0, 1 });

            // A file that ends with a space to exclude.
            var fileWithSpace = Path.Combine(this.DATAFOLDER, "file_with_space ");
            TestUtils.WriteFile(fileWithSpace, new byte[] { 0, 1 });

            var filter = new FilterExpression(longFile, false);
            filter = FilterExpression.Combine(filter, new FilterExpression(Util.AppendDirSeparator(folderWithDot), false));
            filter = FilterExpression.Combine(filter, new FilterExpression(Util.AppendDirSeparator(folderWithSpace), false));
            filter = FilterExpression.Combine(filter, new FilterExpression(fileWithDot, false));
            filter = FilterExpression.Combine(filter, new FilterExpression(fileWithSpace, false));

            var options = new Dictionary<string, string>(this.TestOptions);
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = await c.BackupAsync(new[] { this.DATAFOLDER }, filter);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var listResults = await c.ListAsync("*");
                Assert.AreEqual(0, listResults.Errors.Count());
                Assert.AreEqual(0, listResults.Warnings.Count());

                var backedUpPaths = listResults.Files.Select(x => x.Path).ToArray();
                Assert.AreEqual(2, backedUpPaths.Length);
                Assert.Contains(Util.AppendDirSeparator(this.DATAFOLDER), backedUpPaths);
                Assert.Contains(normalFilePath, backedUpPaths);
            }
        }

        [Test]
        [Category("ProblematicPath")]
        public async Task LongPathAsync()
        {
            var folderPath = Path.Combine(this.DATAFOLDER, new string('x', 10));
            SystemIO.IO_OS.DirectoryCreate(folderPath);

            var fileName = new string('y', 255);
            var filePath = SystemIO.IO_OS.PathCombine(folderPath, fileName);
            byte[] fileBytes = { 0, 1, 2 };
            TestUtils.WriteFile(filePath, fileBytes);

            var options = new Dictionary<string, string>(this.TestOptions);
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = await c.BackupAsync(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            var restoreOptions = new Dictionary<string, string>(this.TestOptions) { ["restore-path"] = this.RESTOREFOLDER };
            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                var restoreResults = await c.RestoreAsync(new[] { filePath });
                Assert.AreEqual(0, restoreResults.Errors.Count());
                Assert.AreEqual(0, restoreResults.Warnings.Count());
            }

            var restoreFilePath = SystemIO.IO_OS.PathCombine(this.RESTOREFOLDER, fileName);
            Assert.IsTrue(SystemIO.IO_OS.FileExists(restoreFilePath));

            var restoredStream = new MemoryStream();
            using (FileStream fileStream = SystemIO.IO_OS.FileOpenRead(restoreFilePath))
            {
                Utility.CopyStream(fileStream, restoredStream);
            }

            Assert.AreEqual(fileBytes, restoredStream.ToArray());
        }

        [Test]
        [Category("ProblematicPath")]
        [TestCase("ends_with_dot.", false)]
        [TestCase("ends_with_dots..", false)]
        [TestCase("ends_with_space ", false)]
        [TestCase("ends_with_spaces  ", false)]
        [TestCase("ends_with_newline\n", true)]
        public async Task ProblematicSuffixesAsync(string pathComponent, bool skipOnWindows)
        {
            if (OperatingSystem.IsWindows() && skipOnWindows)
            {
                return;
            }

            var folderPath = SystemIO.IO_OS.PathCombine(this.DATAFOLDER, pathComponent);
            SystemIO.IO_OS.DirectoryCreate(folderPath);

            var filePath = SystemIO.IO_OS.PathCombine(folderPath, pathComponent);
            byte[] fileBytes = [0, 1, 2];
            TestUtils.WriteFile(filePath, fileBytes);

            var options = new Dictionary<string, string>(this.TestOptions);
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = await c.BackupAsync(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            var restoreOptions = new Dictionary<string, string>(this.TestOptions) { ["restore-path"] = this.RESTOREFOLDER };

            // Restore just the file.
            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                var restoreResults = await c.RestoreAsync(new[] { filePath });
                Assert.AreEqual(0, restoreResults.Errors.Count());
                Assert.AreEqual(0, restoreResults.Warnings.Count());
            }

            var restoreFilePath = SystemIO.IO_OS.PathCombine(this.RESTOREFOLDER, pathComponent);
            TestUtils.AssertFilesAreEqual(filePath, restoreFilePath, true, pathComponent);
            SystemIO.IO_OS.FileDelete(restoreFilePath);

            // Restore the entire directory.
            var pathSpec = $"[{Regex.Escape(Util.AppendDirSeparator(this.DATAFOLDER))}.*]";
            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                var restoreResults = await c.RestoreAsync(new[] { pathSpec });
                Assert.AreEqual(0, restoreResults.Errors.Count());
                Assert.AreEqual(0, restoreResults.Warnings.Count());
            }

            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, pathComponent);
        }
    }
}