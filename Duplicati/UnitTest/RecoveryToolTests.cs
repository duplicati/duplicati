using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class RecoveryToolTests : BasicSetupHelper
    {
        private string originalCurrentDirectory;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            this.originalCurrentDirectory = Directory.GetCurrentDirectory();
        }

        [TearDown]
        public override void TearDown()
        {
            // Since the RecoveryTool changes the current directory, we will reset it so that
            // the teardown methods do not complain about the paths being used by another process.
            if (this.originalCurrentDirectory != null)
            {
                Directory.SetCurrentDirectory(this.originalCurrentDirectory);
            }

            base.TearDown();
        }

        [Test]
        [Category("RecoveryTool")]
        public void RecoveryRestorePrefix()
        {
            string sourceDirsep;
            long fileCount;
            string largestprefix;
            largestprefix = CommandLine.RecoveryTool.Restore.GetLargestPrefix(new string[] { }, out sourceDirsep, out fileCount);
            Assert.AreEqual("", largestprefix);
            // sourceDirsep unspecified
            Assert.AreEqual(0, fileCount);

            largestprefix = CommandLine.RecoveryTool.Restore.GetLargestPrefix(new string[] { "" }, out sourceDirsep, out fileCount);
            Assert.AreEqual("", largestprefix);
            // sourceDirsep unspecified
            Assert.AreEqual(1, fileCount);

            // Windows paths
            largestprefix = CommandLine.RecoveryTool.Restore.GetLargestPrefix(new string[] {
                "C:\\Users\\User\\Pictures\\a.png"
                }, out sourceDirsep, out fileCount);
            Assert.AreEqual("C:\\Users\\User\\Pictures\\", largestprefix);
            Assert.AreEqual("\\", sourceDirsep);
            Assert.AreEqual(1, fileCount);

            largestprefix = CommandLine.RecoveryTool.Restore.GetLargestPrefix(new string[] {
                "C:\\Users\\User\\Pictures\\a.png",
                "C:\\Users\\User\\Pictures\\b.jpg",
                "C:\\Users\\User\\Pictures\\c.txt"
                }, out sourceDirsep, out fileCount);
            Assert.AreEqual("C:\\Users\\User\\Pictures\\", largestprefix);
            Assert.AreEqual("\\", sourceDirsep);
            Assert.AreEqual(3, fileCount);

            largestprefix = CommandLine.RecoveryTool.Restore.GetLargestPrefix(new string[] {
                "C:\\Users\\User\\Pictures\\a.png",
                "C:\\Users\\User\\Pictures\\b.jpg",
                "C:\\Users\\User2\\Pictures\\b.jpg"
                }, out sourceDirsep, out fileCount);
            Assert.AreEqual("C:\\Users\\", largestprefix);
            Assert.AreEqual("\\", sourceDirsep);
            Assert.AreEqual(3, fileCount);

            largestprefix = CommandLine.RecoveryTool.Restore.GetLargestPrefix(new string[] {
                "C:\\Users\\User\\Pictures\\a.png",
                "C:\\Users\\User\\Pictures\\b.jpg",
                "C:\\Data\\a.png"
                }, out sourceDirsep, out fileCount);
            Assert.AreEqual("C:\\", largestprefix);
            Assert.AreEqual("\\", sourceDirsep);
            Assert.AreEqual(3, fileCount);

            largestprefix = CommandLine.RecoveryTool.Restore.GetLargestPrefix(new string[] {
                "C:\\Users\\User\\Pictures\\a.png",
                "D:\\Users\\User\\Pictures\\a.jpg"
                }, out sourceDirsep, out fileCount);
            Assert.AreEqual("", largestprefix);
            Assert.AreEqual("\\", sourceDirsep);
            Assert.AreEqual(2, fileCount);

            largestprefix = CommandLine.RecoveryTool.Restore.GetLargestPrefix(new string[] {
                "C:\\Users\\User\\Pictures\\a.png",
                ""
                }, out sourceDirsep, out fileCount);
            Assert.AreEqual("", largestprefix);
            Assert.AreEqual("\\", sourceDirsep);
            Assert.AreEqual(2, fileCount);

            // Unix paths
            largestprefix = CommandLine.RecoveryTool.Restore.GetLargestPrefix(new string[] {
                "/home/user/pictures/a.png"
                }, out sourceDirsep, out fileCount);
            Assert.AreEqual("/home/user/pictures/", largestprefix);
            Assert.AreEqual("/", sourceDirsep);
            Assert.AreEqual(1, fileCount);

            largestprefix = CommandLine.RecoveryTool.Restore.GetLargestPrefix(new string[] {
                "/home/user/pictures/a.png",
                "/home/user/pictures/b.jpg",
                "/home/user/pictures/c.txt"
                }, out sourceDirsep, out fileCount);
            Assert.AreEqual("/home/user/pictures/", largestprefix);
            Assert.AreEqual("/", sourceDirsep);
            Assert.AreEqual(3, fileCount);

            largestprefix = CommandLine.RecoveryTool.Restore.GetLargestPrefix(new string[] {
                "/home/user/pictures/a.png",
                "/home/user/pictures/b.jpg",
                "/home/user2/pictures/c.txt"
                }, out sourceDirsep, out fileCount);
            Assert.AreEqual("/home/", largestprefix);
            Assert.AreEqual("/", sourceDirsep);
            Assert.AreEqual(3, fileCount);

            largestprefix = CommandLine.RecoveryTool.Restore.GetLargestPrefix(new string[] {
                "/home/user/pictures/a.png",
                "/media/data/a.png"
                }, out sourceDirsep, out fileCount);
            Assert.AreEqual("/", largestprefix);
            Assert.AreEqual("/", sourceDirsep);
            Assert.AreEqual(2, fileCount);

            largestprefix = CommandLine.RecoveryTool.Restore.GetLargestPrefix(new string[] {
                "/home/user/pictures/a.png",
                ""
                }, out sourceDirsep, out fileCount);
            Assert.AreEqual("", largestprefix);
            Assert.AreEqual("/", sourceDirsep);
            Assert.AreEqual(2, fileCount);
        }

        [Test]
        [Category("RecoveryTool")]
        public void RecoveryRestorePathMap()
        {
            // Windows source paths
            string restorePath = CommandLine.RecoveryTool.Restore.MapToRestorePath("C:\\Users\\User\\Pictures\\a.png", "C:\\Users\\User\\Pictures\\", "restore", "\\");
            // Replace the alt directory separator with normal directory separator to compare paths,
            // otherwise the path returned by Path.Combine does not match
            restorePath = restorePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            Assert.AreEqual(Path.Combine("restore", "a.png"), restorePath);
            restorePath = CommandLine.RecoveryTool.Restore.MapToRestorePath("C:\\Users\\User\\Pictures\\a.png", "C:\\Users\\", "restore", "\\");
            restorePath = restorePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            Assert.AreEqual(Path.Combine("restore", "User", "Pictures", "a.png"), restorePath);
            restorePath = CommandLine.RecoveryTool.Restore.MapToRestorePath("C:\\Users\\User\\Pictures\\a.png", "C:\\Users\\", "restore" + Path.DirectorySeparatorChar, "\\");
            restorePath = restorePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            Assert.AreEqual(Path.Combine("restore", "User", "Pictures", "a.png"), restorePath);
            restorePath = CommandLine.RecoveryTool.Restore.MapToRestorePath("C:\\Users\\User\\Pictures\\a.png", "", "restore", "\\");
            restorePath = restorePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            Assert.AreEqual(Path.Combine("restore", "C", "Users", "User", "Pictures", "a.png"), restorePath);
            // This is an absolute restore path on Windows and a relative path with backslashes on Unix
            restorePath = CommandLine.RecoveryTool.Restore.MapToRestorePath("C:\\Users\\User\\Pictures\\a.png", "", "C:\\Users\\User\\restore", "\\");
            restorePath = restorePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            Assert.AreEqual(Path.Combine("C:\\Users\\User\\restore", "C", "Users", "User", "Pictures", "a.png"), restorePath);

            // Unix source paths
            restorePath = CommandLine.RecoveryTool.Restore.MapToRestorePath("/home/user/pictures/a.png", "/home/user/pictures/", "restore", "/");
            restorePath = restorePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            Assert.AreEqual(Path.Combine("restore", "a.png"), restorePath);
            restorePath = CommandLine.RecoveryTool.Restore.MapToRestorePath("/home/user/pictures/a.png", "/home/user/pictures/", "restore" + Path.DirectorySeparatorChar, "/");
            restorePath = restorePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            Assert.AreEqual(Path.Combine("restore", "a.png"), restorePath);
            restorePath = CommandLine.RecoveryTool.Restore.MapToRestorePath("/home/user/pictures/a.png", "/home/", "restore", "/");
            restorePath = restorePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            Assert.AreEqual(Path.Combine("restore", "user", "pictures", "a.png"), restorePath);
            restorePath = CommandLine.RecoveryTool.Restore.MapToRestorePath("/home/user/pictures/a.png", "/", "restore", "/");
            restorePath = restorePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            Assert.AreEqual(Path.Combine("restore", "home", "user", "pictures", "a.png"), restorePath);
            // This is an absolute restore path on Windows and a relative path with backslashes on Unix
            restorePath = CommandLine.RecoveryTool.Restore.MapToRestorePath("/home/user/pictures/a.png", "/", "C:\\Users\\User\\restore", "/");
            restorePath = restorePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            Assert.AreEqual(Path.Combine("C:\\Users\\User\\restore", "home", "user", "pictures", "a.png"), restorePath);
        }

        [Test]
        [Category("RecoveryTool")]
        [TestCase("false")]
        [TestCase("true")]
        public void Recover(string buildIndexWithFiles)
        {
            // Files to create in MB.
            int[] fileSizes = { 0, 10, 20, 30 };
            foreach (int size in fileSizes)
            {
                byte[] data = new byte[size * 1024 * 1024];
                Random rng = new Random();
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(this.DATAFOLDER, size + "MB"), data);
            }

            const string subdirectoryName = "subdirectory";
            string subdirectoryPath = Path.Combine(this.DATAFOLDER, subdirectoryName);
            Directory.CreateDirectory(subdirectoryPath);
            foreach (int size in fileSizes)
            {
                byte[] data = new byte[size * 1024 * 1024];
                Random rng = new Random();
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(subdirectoryPath, size + "MB"), data);
            }

            // Run a backup.
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions);
            string backendURL = "file://" + this.TARGETFOLDER;
            using (Controller c = new Controller(backendURL, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Download the backend files.
            string downloadFolder = Path.Combine(this.RESTOREFOLDER, "downloadedFiles");
            Directory.CreateDirectory(downloadFolder);
            int status = CommandLine.RecoveryTool.Program.RealMain(new[] { "download", $"{backendURL}", $"{downloadFolder}", $"--passphrase={options["passphrase"]}" });
            Assert.AreEqual(0, status);

            // Create the index.
            status = CommandLine.RecoveryTool.Program.RealMain(new[] { "index", $"{downloadFolder}", $"--build-index-with-files={buildIndexWithFiles}" });
            Assert.AreEqual(0, status);

            // Restore to a different folder.
            string restoreFolder = Path.Combine(this.RESTOREFOLDER, "restoredFiles");
            Directory.CreateDirectory(restoreFolder);
            status = CommandLine.RecoveryTool.Program.RealMain(new[] { "restore", $"{downloadFolder}", $"--targetpath={restoreFolder}" });
            Assert.AreEqual(0, status);

            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, restoreFolder, false, "Verifying restore using RecoveryTool.");
        }
    }
}