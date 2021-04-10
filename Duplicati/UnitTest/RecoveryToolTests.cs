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
        [TestCase("false")]
        [TestCase("true")]
        public void Recover(string buildIndexWithFiles)
        {
            // Files to create in MB.
            int[] fileSizes = {10, 20, 30};
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
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Download the backend files.
            string downloadFolder = Path.Combine(this.RESTOREFOLDER, "downloadedFiles");
            Directory.CreateDirectory(downloadFolder);
            int status = CommandLine.RecoveryTool.Program.RealMain(new[] {"download", $"{backendURL}", $"{downloadFolder}", $"--passphrase={options["passphrase"]}"});
            Assert.AreEqual(0, status);

            // Create the index.
            status = CommandLine.RecoveryTool.Program.RealMain(new[] {"index", $"{downloadFolder}", $"--build-index-with-files={buildIndexWithFiles}"});
            Assert.AreEqual(0, status);

            // Restore to a different folder.
            string restoreFolder = Path.Combine(this.RESTOREFOLDER, "restoredFiles");
            Directory.CreateDirectory(restoreFolder);
            status = CommandLine.RecoveryTool.Program.RealMain(new[] {"restore", $"{downloadFolder}", $"--targetpath={restoreFolder}"});
            Assert.AreEqual(0, status);

            // Since this.DATAFOLDER is a folder, Path.GetFileName will return the name of the
            // last folder in the path.
            string baseFolder = Path.GetFileName(this.DATAFOLDER);

            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, Path.Combine(restoreFolder, baseFolder), false, "Verifying restore using RecoveryTool.");
        }
    }
}