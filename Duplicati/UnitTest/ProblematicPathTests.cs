using System;
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
        /// <summary>
        ///     This is a helper class that removes problematic paths that the built-in classes
        ///     have trouble with (e.g., paths that end with a dot or space on Windows).
        /// </summary>
        private class DisposablePath : IDisposable
        {
            private readonly string path;

            public DisposablePath(string path)
            {
                this.path = path;
            }

            public void Dispose()
            {
                if (SystemIO.IO_OS.FileExists(this.path))
                {
                    SystemIO.IO_OS.FileDelete(this.path);
                }

                if (SystemIO.IO_OS.DirectoryExists(this.path))
                {
                    SystemIO.IO_OS.DirectoryDelete(this.path);
                }
            }
        }

        private static void WriteFile(string path, byte[] contents)
        {
            using (FileStream fileStream = SystemIO.IO_OS.FileOpenWrite(path))
            {
                Utility.CopyStream(new MemoryStream(contents), fileStream);
            }
        }

        [Test]
        [Category("ProblematicPath")]
        public void FilterProblematicPaths()
        {
            // A normal path that will be backed up.
            string normalFilePath = Path.Combine(this.DATAFOLDER, "normal");
            File.WriteAllBytes(normalFilePath, new byte[] {0, 1, 2});

            // A long path to exclude.
            string longFile = SystemIO.IO_OS.PathCombine(this.DATAFOLDER, new string('y', 255));
            using (new DisposablePath(longFile))
            {
                WriteFile(longFile, new byte[] {0, 1});

                // A folder that ends with a dot to exclude.
                string folderWithDot = Path.Combine(this.DATAFOLDER, "folder_with_dot.");
                SystemIO.IO_OS.DirectoryCreate(folderWithDot);
                using (new DisposablePath(folderWithDot))
                {
                    // A folder that ends with a space to exclude.
                    string folderWithSpace = Path.Combine(this.DATAFOLDER, "folder_with_space ");
                    SystemIO.IO_OS.DirectoryCreate(folderWithSpace);
                    using (new DisposablePath(folderWithSpace))
                    {
                        // A file that ends with a dot to exclude.
                        string fileWithDot = Path.Combine(this.DATAFOLDER, "file_with_dot.");
                        using (new DisposablePath(fileWithDot))
                        {
                            WriteFile(fileWithDot, new byte[] {0, 1});

                            // A file that ends with a space to exclude.
                            string fileWithSpace = Path.Combine(this.DATAFOLDER, "file_with_space ");
                            using (new DisposablePath(fileWithSpace))
                            {
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
                                    Assert.Contains(Util.AppendDirSeparator(this.DATAFOLDER), backedUpPaths);
                                    Assert.Contains(normalFilePath, backedUpPaths);
                                }
                            }
                        }
                    }
                }
            }
        }

        [Test]
        [Category("ProblematicPath")]
        public void LongPath()
        {
            string folderPath = Path.Combine(this.DATAFOLDER, new string('x', 10));
            SystemIO.IO_OS.DirectoryCreate(folderPath);
            using (new DisposablePath(folderPath))
            {
                string fileName = new string('y', 255);
                string filePath = SystemIO.IO_OS.PathCombine(folderPath, fileName);
                using (new DisposablePath(filePath))
                {
                    byte[] fileBytes = {0, 1, 2};
                    WriteFile(filePath, fileBytes);

                    Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions);
                    using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
                    {
                        IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});
                        Assert.AreEqual(0, backupResults.Errors.Count());
                        Assert.AreEqual(0, backupResults.Warnings.Count());
                    }

                    string restoreFilePath = SystemIO.IO_OS.PathCombine(this.RESTOREFOLDER, fileName);
                    using (new DisposablePath(restoreFilePath))
                    {
                        Dictionary<string, string> restoreOptions = new Dictionary<string, string>(this.TestOptions) {["restore-path"] = this.RESTOREFOLDER};
                        using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                        {
                            IRestoreResults restoreResults = c.Restore(new[] {filePath});
                            Assert.AreEqual(0, restoreResults.Errors.Count());
                            Assert.AreEqual(0, restoreResults.Warnings.Count());
                        }

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
            using (new DisposablePath(folderPath))
            {
                string filePath = SystemIO.IO_OS.PathCombine(folderPath, pathComponent);
                using (new DisposablePath(filePath))
                {
                    byte[] fileBytes = {0, 1, 2};
                    WriteFile(filePath, fileBytes);

                    Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions);
                    using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
                    {
                        IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});
                        Assert.AreEqual(0, backupResults.Errors.Count());
                        Assert.AreEqual(0, backupResults.Warnings.Count());
                    }

                    string restoreFilePath = SystemIO.IO_OS.PathCombine(this.RESTOREFOLDER, pathComponent);
                    using (new DisposablePath(restoreFilePath))
                    {
                        Dictionary<string, string> restoreOptions = new Dictionary<string, string>(this.TestOptions) {["restore-path"] = this.RESTOREFOLDER};
                        using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                        {
                            IRestoreResults restoreResults = c.Restore(new[] {filePath});
                            Assert.AreEqual(0, restoreResults.Errors.Count());
                            Assert.AreEqual(0, restoreResults.Warnings.Count());
                        }

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
        }
    }
}