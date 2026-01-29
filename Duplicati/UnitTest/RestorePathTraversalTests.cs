using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    public class RestorePathTraversalTests : BasicSetupHelper
    {
        [Test]
        [Category("RestoreHandler")]
        public void RestoreToSymlinkTarget()
        {
            if (OperatingSystem.IsWindows())
                Assert.Ignore("Symlink tests are not supported on Windows");

            // 1. Setup source data
            var sourceFolder = Path.Combine(this.DATAFOLDER, "source");
            var subDir = Path.Combine(sourceFolder, "subdir");
            Directory.CreateDirectory(subDir);
            var filePath = Path.Combine(subDir, "file.txt");
            File.WriteAllText(filePath, "secret data");

            // 2. Backup
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, this.TestOptions, null))
            {
                var backupResults = c.Backup(new[] { sourceFolder });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.Greater(backupResults.ExaminedFiles, 0);
            }

            // 3. Setup malicious restore target
            var restoreTarget = this.RESTOREFOLDER;
            var outsideTarget = Path.Combine(this.DATAFOLDER, "outside");
            Directory.CreateDirectory(outsideTarget);

            // Create a symlink in the restore target: restoreTarget/subdir -> outsideTarget
            var symlinkPath = Path.Combine(restoreTarget, "subdir");
            // Ensure restoreTarget exists
            Directory.CreateDirectory(restoreTarget);

            // Create the symlink
            SystemIO.IO_OS.CreateSymlink(symlinkPath, outsideTarget, true);

            // 4. Restore
            var restoreOptions = new Dictionary<string, string>(this.TestOptions);
            restoreOptions["restore-path"] = restoreTarget;
            restoreOptions["overwrite"] = "true";

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                // We expect a UserInformationException due to path traversal detection
                var ex = NUnit.Framework.Assert.Throws<UserInformationException>(() => c.Restore(null));
                NUnit.Framework.Assert.That(ex.Message.Contains("Path traversal detected"), "Expected path traversal error message");
            }

            // 5. Verify vulnerability
            // If vulnerable, the file will be in outsideTarget/file.txt
            var escapedFile = Path.Combine(outsideTarget, "file.txt");
            var isVulnerable = File.Exists(escapedFile);

            var symlinkExists = (File.GetAttributes(symlinkPath) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;

            Console.WriteLine($"Escaped file exists: {isVulnerable}");
            Console.WriteLine($"Symlink exists: {symlinkExists}");

            Assert.IsFalse(isVulnerable, "File escaped to outside folder via symlink!");
        }

        [Test]
        [Category("RestoreHandler")]
        public void RestoreSymlinkPointingOutside()
        {
            if (OperatingSystem.IsWindows())
                Assert.Ignore("Symlink tests are not supported on Windows");

            // 1. Setup source data with a symlink pointing outside
            var sourceFolder = Path.Combine(this.DATAFOLDER, "source");
            Directory.CreateDirectory(sourceFolder);
            var outsideTarget = Path.Combine(this.DATAFOLDER, "outside");
            Directory.CreateDirectory(outsideTarget);

            var symlinkPath = Path.Combine(sourceFolder, "link_outside");
            SystemIO.IO_OS.CreateSymlink(symlinkPath, outsideTarget, true);

            // 2. Backup
            using (var c = new Controller("file://" + this.TARGETFOLDER, this.TestOptions, null))
            {
                var backupResults = c.Backup(new[] { sourceFolder });
                Assert.AreEqual(0, backupResults.Errors.Count());
            }

            // 3. Restore to a new location
            var restoreTarget = this.RESTOREFOLDER;
            var restoreOptions = new Dictionary<string, string>(this.TestOptions);
            restoreOptions["restore-path"] = restoreTarget;
            restoreOptions["skip-metadata"] = "false";

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                var restoreResults = c.Restore(null);
                Assert.AreEqual(0, restoreResults.Errors.Count());
            }

            // 4. Verify symlink was NOT created
            var restoredLink = Path.Combine(restoreTarget, "source", "link_outside");

            // Check if it exists as a file, directory, or reparse point
            var linkExists = false;
            try
            {
                var attr = File.GetAttributes(restoredLink); // Throws if not found
                linkExists = true;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }

            Assert.IsFalse(linkExists, "Symlink pointing outside should not be created");
        }

        [Test]
        [Category("RestoreHandler")]
        public void RestoreRelativePathInDlist()
        {
            // 1. Setup source data
            var sourceFolder = Path.Combine(this.DATAFOLDER, "source");
            Directory.CreateDirectory(sourceFolder);
            var filePath = Path.Combine(sourceFolder, "file.txt");
            File.WriteAllText(filePath, "data");

            var options = new Dictionary<string, string>(this.TestOptions);
            options["no-encryption"] = "true";
            options.Remove("passphrase");

            // 2. Backup
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] { sourceFolder });
                Assert.AreEqual(0, backupResults.Errors.Count());
            }

            // 3. Manipulate dlist
            var dlistFiles = Directory.GetFiles(this.TARGETFOLDER, "*dlist*");
            Assert.AreEqual(1, dlistFiles.Length);
            var dlistPath = dlistFiles[0];

            // Unzip, modify, zip
            var tempDir = Path.Combine(this.DATAFOLDER, "temp_dlist");
            Directory.CreateDirectory(tempDir);
            ZipFile.ExtractToDirectory(dlistPath, tempDir);
            File.Delete(dlistPath);

            var filelistPath = Directory.GetFiles(tempDir, "*filelist*").FirstOrDefault();
            Assert.IsNotNull(filelistPath);

            var json = File.ReadAllText(filelistPath);
            // Replace path with relative path traversal
            // We replace "file.txt" with "../evil.txt"
            // This creates a path like "/path/to/source/../evil.txt" which contains traversal
            json = json.Replace("file.txt", "../evil.txt");
            File.WriteAllText(filelistPath, json);

            // Re-zip
            ZipFile.CreateFromDirectory(tempDir, dlistPath);

            // 4. Recreate database
            File.Delete(this.DBFILE);

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var repairResults = c.Repair();

                // We expect warnings about invalid path
                var foundWarning = repairResults.Warnings.Any(w => w.Contains("Path traversal detected") || w.Contains("Invalid path"));

                if (!foundWarning)
                {
                    Console.WriteLine("Warnings found:");
                    foreach (var w in repairResults.Warnings)
                        Console.WriteLine(w);
                }

                Assert.IsTrue(foundWarning, "Expected warning about invalid path in dlist");
            }
        }
    }
}
