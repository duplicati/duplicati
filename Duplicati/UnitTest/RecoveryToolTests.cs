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
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class RecoveryToolTests : BasicSetupHelper
    {
        private string originalCurrentDirectory;

        [SetUp]
        public void SetUp()
        {
            this.originalCurrentDirectory = Directory.GetCurrentDirectory();
        }

        [TearDown]
        public void TearDown()
        {
            // Since the RecoveryTool changes the current directory, we will reset it so that
            // the teardown methods do not complain about the paths being used by another process.
            if (this.originalCurrentDirectory != null)
            {
                Directory.SetCurrentDirectory(this.originalCurrentDirectory);
            }
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
        [TestCase("false", "true")]
        [TestCase("true", "false")]
        [TestCase("false", "false")]
        public async Task RecoverAsync(string buildIndexWithFiles, bool reducedMemoryUsage)
        {
            // Files to create in MB.
            int[] fileSizes = [0, 10, 20, 30];
            foreach (int size in fileSizes)
            {
                var data = new byte[size * 1024 * 1024];
                var rng = new Random();
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(this.DATAFOLDER, size + "MB"), data);
            }

            const string subdirectoryName = "subdirectory";
            var subdirectoryPath = Path.Combine(this.DATAFOLDER, subdirectoryName);
            Directory.CreateDirectory(subdirectoryPath);
            foreach (int size in fileSizes)
            {
                var data = new byte[size * 1024 * 1024];
                var rng = new Random();
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(subdirectoryPath, size + "MB"), data);
            }

            // Run a backup.
            var options = new Dictionary<string, string>(this.TestOptions);
            var backendURL = "file://" + this.TARGETFOLDER;
            using (var c = new Controller(backendURL, options, null))
            {
                var backupResults = await c.BackupAsync(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Download the backend files.
            var downloadFolder = Path.Combine(this.RESTOREFOLDER, "downloadedFiles");
            Directory.CreateDirectory(downloadFolder);
            var status = CommandLine.RecoveryTool.Program.Main(new[] { "download", $"{backendURL}", $"{downloadFolder}", $"--passphrase={options["passphrase"]}" });
            Assert.AreEqual(0, status);

            // Create the index.
            status = CommandLine.RecoveryTool.Program.Main(new[] { "index", $"{downloadFolder}", $"--build-index-with-files={buildIndexWithFiles}" });
            Assert.AreEqual(0, status);

            // Restore to a different folder.
            var restoreFolder = Path.Combine(this.RESTOREFOLDER, "restoredFiles");
            Directory.CreateDirectory(restoreFolder);
            status = CommandLine.RecoveryTool.Program.Main(new[] { "restore", $"{downloadFolder}", $"--targetpath={restoreFolder}", $"--reduce-memory-use={reducedMemoryUsage}" });
            Assert.AreEqual(0, status);

            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, restoreFolder, false, "Verifying restore using RecoveryTool.");
        }

        [Test]
        [Category("RecoveryTool")]
        [TestCase(false)]
        [TestCase(true)]
        public async Task RecompressAsync(bool noEncryption)
        {
            // Files to create in MB.
            int[] fileSizes = [10, 20, 30];
            foreach (int size in fileSizes)
            {
                var data = new byte[size * 1024 * 1024];
                var rng = new Random();
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(this.DATAFOLDER, size + "MB"), data);
            }
            // Run a backup.
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["no-encryption"] = noEncryption.ToString()
            };
            var backendURL = "file://" + this.TARGETFOLDER;
            using (var c = new Controller(backendURL, options, null))
            {
                var backupResults = await c.BackupAsync(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }
            // Recompress.
            var downloadFolder = Path.Combine(this.RESTOREFOLDER, "downloadedFiles");
            Directory.CreateDirectory(downloadFolder);
            var status = CommandLine.RecoveryTool.Program.Main(new[] { "recompress", "zip", $"{backendURL}", this.RESTOREFOLDER, $"--passphrase={options["passphrase"]}" });
            Assert.AreEqual(0, status);
        }

        [Test]
        [Category("RecoveryTool")]
        [TestCase(false)]
        [TestCase(true)]
        public async Task RecompressAndRecreateDatabaseAsync(bool noEncryption)
        {
            // Create a small dataset.
            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, "file.txt"), new byte[] { 1, 2, 3 });

            // Run a backup with default zip compression.
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["no-encryption"] = noEncryption.ToString()
            };
            var backendURL = "file://" + this.TARGETFOLDER;
            using (var c = new Controller(backendURL, options, null))
            {
                var backupResults = await c.BackupAsync(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Delete the local database.
            File.Delete(options["dbpath"]);

            // Recompress remote files from zip to tzstd and reupload.
            var recompressFolder = Path.Combine(this.RESTOREFOLDER, "recompressed");
            Directory.CreateDirectory(recompressFolder);
            var status = CommandLine.RecoveryTool.Program.Main(new[] { "recompress", "tzstd", backendURL, recompressFolder, "--reupload", $"--passphrase={options["passphrase"]}" });
            Assert.AreEqual(0, status);

            // Verify that the remote now contains tzstd files and no zip files.
            var remoteFiles = Directory.GetFiles(this.TARGETFOLDER);
            Assert.That(remoteFiles.Any(f => f.EndsWith(".tzstd", StringComparison.OrdinalIgnoreCase)), Is.True, "Remote should contain tzstd files after recompress.");
            Assert.That(remoteFiles.Any(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)), Is.False, "Remote should not contain zip files after recompress.");

            // Recreate the local database via Repair.
            using (var c = new Controller(backendURL, options, null))
            {
                var repairResults = await c.RepairAsync();
                Assert.AreEqual(0, repairResults.Errors.Count());
                Assert.AreEqual(0, repairResults.Warnings.Count());
            }

            // The recreated database should have compression-module set to tzstd,
            // because that is the actual compression format now used on the remote.
            using (var db = await Duplicati.Library.Main.Database.LocalDatabase.CreateLocalDatabaseAsync(options["dbpath"], "Test", true, null, System.Threading.CancellationToken.None))
            {
                var dbOptions = await db.GetDbOptionsAsync(System.Threading.CancellationToken.None);
                Assert.That(dbOptions.ContainsKey("compression-module"), Is.True, "Database should contain compression-module option.");
                // This assertion replicates the reported issue: after recreate the database
                // still stores the old compression module (zip) instead of the actual one (tzstd).
                Assert.That(dbOptions["compression-module"], Is.EqualTo("tzstd"), "Recreated database should reflect the actual compression module used by remote files.");
            }
        }

    }
}