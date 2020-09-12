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
        [TestCase(Options.SymlinkStrategy.Store)]
        [TestCase(Options.SymlinkStrategy.Follow)]
        [TestCase(Options.SymlinkStrategy.Ignore)]
        public void SymLinkPolicy(Options.SymlinkStrategy symlinkPolicy)
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
                TestUtils.WriteFile(targetFile, Encoding.Default.GetBytes(file));
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

            // Backup all files with given symlink policy
            Dictionary<string, string> restoreOptions = new Dictionary<string, string>(this.TestOptions) { ["restore-path"] = this.RESTOREFOLDER };
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
                        TestUtils.AssertDirectoryTreesAreEquivalent(targetDir, restoreSymlinkDir, true, $"Symlink policy: {Options.SymlinkStrategy.Store}");
                        break;
                    case Options.SymlinkStrategy.Ignore:
                        // Restore should not contain the symlink at all
                        Assert.That(systemIO.DirectoryExists(restoreSymlinkDir), Is.False);
                        break;
                }
            }
        }
    }
}
