// Copyright (C) 2025, The Duplicati Team
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
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    public class RestoreHandlerTests : BasicSetupHelper
    {

        [Test]
        [Category("RestoreHandler")]
        public void DisablePipedStreaming()
        {
            string filePath = Path.Combine(this.DATAFOLDER, "file");
            File.WriteAllBytes(filePath, new byte[] { 0 });

            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                c.Backup(new[] { this.DATAFOLDER });
            }

            Dictionary<string, string> restoreOptions = new Dictionary<string, string>(this.TestOptions) { ["restore-path"] = this.RESTOREFOLDER };
            // This is now the default behavior, so we cannot explicitly disable it
            //restoreOptions["disable-piped-streaming"] = "true";
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                IRestoreResults restoreResults = c.Restore(new[] { filePath });
                Assert.AreEqual(0, restoreResults.Errors.Count());
                Assert.AreEqual(0, restoreResults.Warnings.Count());
            }

            string restoredFilePath = Path.Combine(this.RESTOREFOLDER, "file");
            Assert.IsTrue(File.Exists(restoredFilePath));
        }

        [Test]
        [Category("RestoreHandler")]
        public void RestoreEmptyFile()
        {
            string folderPath = Path.Combine(this.DATAFOLDER, "folder");
            Directory.CreateDirectory(folderPath);
            string filePath = Path.Combine(folderPath, "empty_file");
            File.WriteAllBytes(filePath, new byte[] { });

            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Issue #4148 described a situation where the folders containing the empty file were not recreated properly.
            Dictionary<string, string> restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["dont-compress-restore-paths"] = "true"
            };
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                IRestoreResults restoreResults = c.Restore(new[] { filePath });
                Assert.AreEqual(0, restoreResults.Errors.Count());
                // TODO The expected warning is expected, as the 'dont-compress-restore-paths' option results in a warning about a folder not being created before restoring a file.
                Assert.AreEqual(1, restoreResults.Warnings.Count());
            }

            // We need to strip the root part of the path. Otherwise, Path.Combine will simply return the second argument
            // if it's determined to be an absolute path.
            string rootString = SystemIO.IO_OS.GetPathRoot(filePath);
            string newPathPart = filePath.Substring(rootString.Length);
            if (OperatingSystem.IsWindows())
            {
                // On Windows, the drive letter is included in the path when the dont-compress-restore-paths option is used.
                // The drive letter is assumed to be the first character of the path root (e.g., C:\).
                newPathPart = Path.Combine(rootString.Substring(0, 1), filePath.Substring(rootString.Length));
            }

            string restoredFilePath = Path.Combine(restoreOptions["restore-path"], newPathPart);
            Assert.IsTrue(File.Exists(restoredFilePath));
        }

        [Test]
        [Category("RestoreHandler")]
        public void RestoreInheritanceBreaks()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var folderPath = Path.Combine(this.DATAFOLDER, "folder");
            Directory.CreateDirectory(folderPath);
            var filePath = Path.Combine(folderPath, "file");
            File.WriteAllBytes(filePath, new byte[] { 0 });

            // Protect access rules on the file.
            var fileSecurity = new FileInfo(filePath).GetAccessControl();
            fileSecurity.SetAccessRuleProtection(true, true);
            new FileInfo(filePath).SetAccessControl(fileSecurity);

            var options = new Dictionary<string, string>(this.TestOptions);
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));

            // First, restore without restoring permissions.
            var restoreOptions = new Dictionary<string, string>(this.TestOptions) { ["restore-path"] = this.RESTOREFOLDER };
            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                TestUtils.AssertResults(c.Restore(new[] { filePath }));

                var restoredFilePath = Path.Combine(this.RESTOREFOLDER, "file");
                Assert.IsTrue(File.Exists(restoredFilePath));

                var restoredFileSecurity = new FileInfo(restoredFilePath).GetAccessControl();
                Assert.IsFalse(restoredFileSecurity.AreAccessRulesProtected);

                // Remove the restored file so that the later restore avoids the "Restore completed
                // without errors but no files were restored" warning.
                File.Delete(restoredFilePath);
            }

            // Restore with restoring permissions.
            restoreOptions["overwrite"] = "true";
            restoreOptions["restore-permissions"] = "true";
            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                TestUtils.AssertResults(c.Restore(new[] { filePath }));

                var restoredFilePath = Path.Combine(this.RESTOREFOLDER, "file");
                Assert.IsTrue(File.Exists(restoredFilePath));

                var restoredFileSecurity = new FileInfo(restoredFilePath).GetAccessControl();
                Assert.IsTrue(restoredFileSecurity.AreAccessRulesProtected);
            }
        }

        [Test]
        [Category("RestoreHandler")]
        public async System.Threading.Tasks.Task RestoreVolumeCache([Values("0b", "1mb", "5mb", "1gb", null)] string? cache_size, [Values("0", "1", null)] string? channel_size)
        {
            var opts = TestOptions;
            opts["dblock-size"] = "1mb";
            opts["blocksize"] = "1kb";
            if (cache_size != null)
                opts["restore-volume-cache-hint"] = cache_size;
            if (channel_size != null)
                opts["restore-channel-buffer-size"] = channel_size;
            opts["restore-legacy"] = "false";
            opts["restore-file-processors"] = "4";
            opts["restore-volume-decryptors"] = "4";
            opts["restore-volume-decompressors"] = "4";
            opts["restore-volume-downloaders"] = "4";
            opts["restore-path"] = RESTOREFOLDER;

            // Write a bunch of files
            Random rng = new();
            byte[] data = new byte[1024];
            for (int i = 0; i < 1000; i++)
            {
                rng.NextBytes(data);
                var filePath = Path.Combine(this.DATAFOLDER, $"file{i}");
                File.WriteAllBytes(filePath, data);
            }

            using var c = new Controller("file://" + this.TARGETFOLDER, opts, null);
            TestUtils.AssertResults(c.Backup([this.DATAFOLDER]));

            // Start a 30 second timeout
            var timeout_task = System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(30));

            var restore_task = System.Threading.Tasks.Task.Run(() =>
            {
                TestUtils.AssertResults(c.Restore(["*"]));
            });

            var t = await System.Threading.Tasks.Task.WhenAny(timeout_task, restore_task);

            if (t == timeout_task)
            {
                c.Abort();
                await restore_task; // Ensure we wait for the restore task to complete
                Assert.Fail("Restore timed out");
            }
            else if (t == restore_task)
                // Throw any exceptions it might have
                t.GetAwaiter().GetResult();

            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Restoring with different volume cache sizes");
        }

        [Test]
        [Category("RestoreHandler")]
        public void RestoreWithoutLocalData([Values("true", "false")] string noLocalDb, [Values("true", "false")] string patchWithLocalBlocks)
        {
            var file1Path = Path.Combine(this.DATAFOLDER, "file1");
            File.WriteAllBytes(file1Path, new byte[] { 1, 2, 3 });

            var file2Path = Path.Combine(this.DATAFOLDER, "file2");
            File.WriteAllBytes(file2Path, new byte[] { 3, 4, 6 });

            var folderPath = Path.Combine(this.DATAFOLDER, "folder");
            Directory.CreateDirectory(folderPath);
            systemIO.FileCopy(file1Path, Path.Combine(folderPath, "file1 copy"), true);

            using (Controller c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null))
                TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));

            var restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["no-local-db"] = noLocalDb,
                ["restore-with-local-blocks"] = patchWithLocalBlocks
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(c.Restore(new[] { "*" }));

            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Restoring without local data");
        }

        [Test]
        [Category("RestoreHandler")]
        public void RestoreOtherProcessIsUsingFile()
        {
            var file1Path = Path.Combine(this.DATAFOLDER, "file1");
            byte[] original_contents = [1, 2, 3];
            File.WriteAllBytes(file1Path, original_contents);

            var opts = new Dictionary<string, string>(this.TestOptions);
            opts["overwrite"] = "true";

            using var c = new Controller("file://" + this.TARGETFOLDER, opts, null);

            var res_backup = c.Backup([this.DATAFOLDER]);
            TestUtils.AssertResults(res_backup);

            File.WriteAllBytes(file1Path, [4, 5, 6]);

            using (var fs = new FileStream(file1Path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var res_failing = c.Restore(["*"]);
                Assert.AreEqual(4, res_failing.Errors.Count());
                var first_error = res_failing.Errors.First();
                Assert.IsTrue(
                    first_error.Contains("IOException: The process cannot access the file")
                    &&
                    first_error.EndsWith(" because it is being used by another process.")
                );
            }

            var res_restore = c.Restore(["*"]);
            TestUtils.AssertResults(res_restore);
            Assert.AreEqual(original_contents, File.ReadAllBytes(file1Path));
        }

        [Test]
        [Category("RestoreHandler")]
        public async System.Threading.Tasks.Task RestoreVolumeCacheDiskPressure()
        {
            var opts = TestOptions;
            opts["dblock-size"] = "1mb";
            opts["blocksize"] = "1kb";
            // No restore-volume-cache-hint → unlimited mode (-1 sentinel).
            // Set restore-volume-cache-min-free to an absurdly large value so
            // every volume arrival triggers disk-pressure eviction.
            opts["restore-volume-cache-min-free"] = "999tb";
            opts["restore-legacy"] = "false";
            opts["restore-path"] = RESTOREFOLDER;

            // Write enough data to create at least 10 dblock volumes (dblock-size=1mb),
            // so that the eviction loop is entered multiple times and the CachePressure
            // warning threshold (5 evictions) is reliably crossed.
            Random rng = new();
            for (int i = 0; i < 20; i++)
            {
                var data = new byte[512 * 1024]; // 512 KB each → ~10 MB total → ~10 dblock volumes
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(this.DATAFOLDER, $"file{i}"), data);
            }

            using var c = new Controller("file://" + this.TARGETFOLDER, opts, null);
            TestUtils.AssertResults(c.Backup([this.DATAFOLDER]));

            var timeout_task = System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(120));
            RestoreResults? result = null;

            var restore_task = System.Threading.Tasks.Task.Run(() =>
            {
                result = (RestoreResults)c.Restore(["*"]);
            });

            var t = await System.Threading.Tasks.Task.WhenAny(timeout_task, restore_task);
            if (t == timeout_task)
            {
                c.Abort();
                await restore_task;
                Assert.Fail("Restore timed out");
            }
            else
            {
                t.GetAwaiter().GetResult();
            }

            Assert.IsNotNull(result);
            Assert.That(result!.CachePressureEvictions, Is.GreaterThan(0), "Expected disk-pressure evictions with 999tb min-free");
            Assert.That(result.TotalVolumesAccessed, Is.GreaterThan(0), "Expected at least one volume to be accessed");
            Assert.That(result.Warnings.Count(), Is.GreaterThanOrEqualTo(1), "Expected at least one CachePressure warning");

            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Restoring with disk pressure eviction");
        }
    }
}