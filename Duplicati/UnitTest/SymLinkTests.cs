using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Snapshots;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class SymLinkTests : BasicSetupHelper
    {
        [Test]
        [Category("SymLink")]
        public void SymLinkPolicy()
        {
            // Create symlink target directory
            const string targetDirName = "target";
            var targetDir = systemIO.PathCombine(this.DATAFOLDER, targetDirName);
            systemIO.DirectoryCreate(targetDir);
            // Create files in symlink target directory
            var fileNames = new[] { "a.txt", "b.txt", "c.txt" };
            foreach (var file in fileNames)
            {
                var targetFile = systemIO.PathCombine(targetDir, file);
                WriteFile(targetFile, Encoding.Default.GetBytes(file));
            }

            // Create actual symlink directory linking to the target directory
            const string symlinkDirName = "symlink";
            var symlinkDir = systemIO.PathCombine(this.DATAFOLDER, symlinkDirName);
            try
            {
                systemIO.CreateSymlink(symlinkDir, targetDir, asDir: true);
            }
            catch (Exception e)
            {
                // If client cannot create symlinks, mark test as ignored
                Assert.Ignore($"Client could not create a symbolic link.  Error reported: {e.Message}");
            }

            // Perform backups using all symlink policies and verify restores
            var symlinkPolicies = new[] { Options.SymlinkStrategy.Store, Options.SymlinkStrategy.Follow, Options.SymlinkStrategy.Ignore };
            Dictionary<string, string> restoreOptions = new Dictionary<string, string>(this.TestOptions) { ["restore-path"] = this.RESTOREFOLDER };
            foreach (var symlinkPolicy in symlinkPolicies)
            {
                // Backup all files with given symlink policy
                Dictionary<string, string> backupOptions = new Dictionary<string, string>(this.TestOptions) { ["symlink-policy"] = symlinkPolicy.ToString() };
                using (Controller c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
                {
                    IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                    Assert.AreEqual(0, backupResults.Errors.Count());
                    Assert.AreEqual(0, backupResults.Warnings.Count());
                }
                // Restore all files
                using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                {
                    IRestoreResults restoreResults = c.Restore(null);
                    Assert.AreEqual(0, restoreResults.Errors.Count());
                    Assert.AreEqual(0, restoreResults.Warnings.Count());

                    // Verify that symlink policy was followed
                    var restoreSymlinkDir = systemIO.PathCombine(this.RESTOREFOLDER, symlinkDirName);
                    switch (symlinkPolicy)
                    {
                        case Options.SymlinkStrategy.Store:
                            // Restore should contain an actual symlink to the original target
                            Assert.That(systemIO.IsSymlink(restoreSymlinkDir), Is.True);
                            var restoredSymlinkFullPath = systemIO.PathGetFullPath(systemIO.GetSymlinkTarget(restoreSymlinkDir));
                            var symlinkTargetFullPath = systemIO.PathGetFullPath(targetDir);
                            Assert.That(restoredSymlinkFullPath, Is.EqualTo(symlinkTargetFullPath));
                            break;
                        case Options.SymlinkStrategy.Follow:
                            // Restore should contain a regular directory with copies of the files in the symlink target
                            Assert.That(systemIO.IsSymlink(restoreSymlinkDir), Is.False);
                            AssertDirectoryTreesAreEquivalent(targetDir, restoreSymlinkDir, $"Symlink policy: {Options.SymlinkStrategy.Store}");
                            break;
                        case Options.SymlinkStrategy.Ignore:
                            // Restore should not contain the symlink at all
                            Assert.That(systemIO.DirectoryExists(restoreSymlinkDir), Is.False);
                            break;
                    }
                    // Prepare for a fresh backup and restore cycle
                    foreach (var dir in systemIO.EnumerateDirectories(this.RESTOREFOLDER))
                    {
                        systemIO.DirectoryDelete(dir, true);
                    }
                    foreach (var file in systemIO.EnumerateFiles(this.RESTOREFOLDER))
                    {
                        systemIO.FileDelete(file);
                    }
                    c.DeleteAllRemoteFiles();
                    if (systemIO.FileExists(this.DBFILE))
                    {
                        systemIO.FileDelete(this.DBFILE);
                    }
                    if (systemIO.FileExists($"{this.DBFILE}-journal"))
                    {
                        systemIO.FileDelete($"{this.DBFILE}-journal");
                    }
                }
            }
        }

        /// <summary>
        /// Asserts that the two directories are equivalent; i.e., they they contain the same subdirectories and files, recursively.
        /// </summary>
        private static void AssertDirectoryTreesAreEquivalent(string expectedDir, string actualDir, string contextMessage)
        {
            var localMessage = $"{contextMessage}, in directories {expectedDir} and {actualDir}";
            var expectedSubdirs = systemIO.EnumerateDirectories(expectedDir).OrderBy(systemIO.PathGetFileName);
            var actualSubdirs = systemIO.EnumerateDirectories(actualDir).OrderBy(systemIO.PathGetFileName);
            Assert.That(expectedSubdirs.Select(systemIO.PathGetFileName), Is.EquivalentTo(actualSubdirs.Select(systemIO.PathGetFileName)), localMessage);
            var expectedSubdirsEnumerator = expectedSubdirs.GetEnumerator();
            var actualSubdirsEnumerator = actualSubdirs.GetEnumerator();
            while (expectedSubdirsEnumerator.MoveNext() && actualSubdirsEnumerator.MoveNext())
            {
                AssertDirectoryTreesAreEquivalent(expectedSubdirsEnumerator.Current, actualSubdirsEnumerator.Current, contextMessage);
            }
            var expectedFiles = systemIO.EnumerateFiles(expectedDir).OrderBy(systemIO.PathGetFileName);
            var actualFiles = systemIO.EnumerateFiles(actualDir).OrderBy(systemIO.PathGetFileName);
            Assert.That(expectedFiles.Select(systemIO.PathGetFileName), Is.EquivalentTo(actualFiles.Select(systemIO.PathGetFileName)), localMessage);
            var expectedFilesEnumerator = expectedFiles.GetEnumerator();
            var actualFilesEnumerator = actualFiles.GetEnumerator();
            while (expectedFilesEnumerator.MoveNext() && actualFilesEnumerator.MoveNext())
            {
                AssertFilesAreEqual(expectedFilesEnumerator.Current, actualFilesEnumerator.Current, contextMessage);
            }
        }

        /// <summary>
        /// Asserts that two files are the same by comparing their size and their contents.
        /// </summary>
        private static void AssertFilesAreEqual(string expectedFile, string actualFile, string contextMessage)
        {
            using (var expectedFileStream = systemIO.FileOpenRead(expectedFile))
            using (var actualFileStream = systemIO.FileOpenRead(actualFile))
            {
                Assert.That(expectedFileStream.Length, Is.EqualTo(actualFileStream.Length), $"{contextMessage}, file size mismatch for {expectedFile} and {actualFile}");
                for (long i = 0; i < expectedFileStream.Length; i++)
                {
                    var expectedByte = expectedFileStream.ReadByte();
                    var actualByte = actualFileStream.ReadByte();
                    // Only generate message if byte comparison fails
                    if (expectedByte != actualByte)
                    {
                        var message =
                            $"{contextMessage}, file contents mismatch at position {i} for {expectedFile} and {actualFile}";
                        Assert.That(actualByte, Is.EqualTo(expectedByte), message);
                    }
                }
            }
        }
    }
}
