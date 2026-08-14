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
        public async Task DisablePipedStreamingAsync()
        {
            var filePath = Path.Combine(this.DATAFOLDER, "file");
            File.WriteAllBytes(filePath, new byte[] { 0 });

            var options = new Dictionary<string, string>(this.TestOptions);
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                await c.BackupAsync(new[] { this.DATAFOLDER });

            var restoreOptions = new Dictionary<string, string>(this.TestOptions) { ["restore-path"] = this.RESTOREFOLDER };
            // This is now the default behavior, so we cannot explicitly disable it
            //restoreOptions["disable-piped-streaming"] = "true";
            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                var restoreResults = await c.RestoreAsync(new[] { filePath });
                Assert.AreEqual(0, restoreResults.Errors.Count());
                Assert.AreEqual(0, restoreResults.Warnings.Count());
            }

            var restoredFilePath = Path.Combine(this.RESTOREFOLDER, "file");
            Assert.IsTrue(File.Exists(restoredFilePath));
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreLocalBlocksRetargetPartialTrailingBlockAsync()
        {
            // Regression test for the byte-offset used when copying verified
            // blocks from an existing target file into a retargeted file
            // (CopyOldTargetBlocksToNewTargetAsync). The offset must be the
            // block index times the fixed block size, not times the block's
            // own size. This only mattered for a file's trailing partial block,
            // where the block size is smaller than the fixed block size.

            const int blocksize = 1024;
            const string blocksizeArg = "1kb";
            // Size the file to 2.5 blocks so the last block is partial (512 bytes).
            var data = new byte[(blocksize * 2) + (blocksize / 2)];
            new Random(42).NextBytes(data);

            var filePath = Path.Combine(this.DATAFOLDER, "file");
            File.WriteAllBytes(filePath, data);

            var backupOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["blocksize"] = blocksizeArg,
                ["restore-legacy"] = "false"
            };
            using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            var restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["blocksize"] = blocksizeArg,
                ["restore-path"] = this.RESTOREFOLDER,
                ["restore-legacy"] = "false",
                ["restore-with-local-blocks"] = "true"
            };
            var restoredFilePath = Path.Combine(this.RESTOREFOLDER, "file");

            // First restore so that a target file exists at the restore path.
            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { filePath }));
            Assert.IsTrue(File.Exists(restoredFilePath));

            // Corrupt the first (full) block of the existing target while leaving
            // the trailing partial block intact. VerifyTargetBlocks then marks the
            // first block as missing and the last (partial) block as verified,
            // which triggers retargeting to a new file and copies the verified
            // blocks - including the partial one - into it.
            var corrupted = (byte[])data.Clone();
            for (var i = 0; i < blocksize; i++)
                corrupted[i] ^= 0xFF;
            File.WriteAllBytes(restoredFilePath, corrupted);

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                var restoreResults = await c.RestoreAsync(new[] { filePath });
                // With the wrong offset the partial block lands at the wrong
                // position, the file hash no longer matches and the restore
                // reports an error instead of producing the file.
                Assert.AreEqual(0, restoreResults.Errors.Count());
            }

            // The retargeted file is a sibling of the original target and must
            // contain the exact original data.
            var retargeted = Directory.GetFiles(this.RESTOREFOLDER)
                .Where(p => !string.Equals(p, restoredFilePath, StringComparison.Ordinal))
                .Where(p => new FileInfo(p).Length == data.Length)
                .FirstOrDefault(p => File.ReadAllBytes(p).SequenceEqual(data));
            Assert.IsNotNull(retargeted, "Expected a retargeted copy containing the original file content");
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreEmptyFileAsync()
        {
            var folderPath = Path.Combine(this.DATAFOLDER, "folder");
            Directory.CreateDirectory(folderPath);
            var filePath = Path.Combine(folderPath, "empty_file");
            File.WriteAllBytes(filePath, new byte[] { });

            var options = new Dictionary<string, string>(this.TestOptions);
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = await c.BackupAsync(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Issue #4148 described a situation where the folders containing the empty file were not recreated properly.
            var restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["dont-compress-restore-paths"] = "true"
            };
            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                var restoreResults = await c.RestoreAsync(new[] { filePath });
                Assert.AreEqual(0, restoreResults.Errors.Count());
                // TODO The expected warning is expected, as the 'dont-compress-restore-paths' option results in a warning about a folder not being created before restoring a file.
                Assert.AreEqual(1, restoreResults.Warnings.Count());
            }

            // We need to strip the root part of the path. Otherwise, Path.Combine will simply return the second argument
            // if it's determined to be an absolute path.
            var rootString = SystemIO.IO_OS.GetPathRoot(filePath);
            var newPathPart = filePath.Substring(rootString.Length);
            if (OperatingSystem.IsWindows())
            {
                // On Windows, the drive letter is included in the path when the dont-compress-restore-paths option is used.
                // The drive letter is assumed to be the first character of the path root (e.g., C:\).
                newPathPart = Path.Combine(rootString.Substring(0, 1), filePath.Substring(rootString.Length));
            }

            var restoredFilePath = Path.Combine(restoreOptions["restore-path"], newPathPart);
            Assert.IsTrue(File.Exists(restoredFilePath));
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreInheritanceBreaksAsync()
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
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // First, restore without restoring permissions.
            var restoreOptions = new Dictionary<string, string>(this.TestOptions) { ["restore-path"] = this.RESTOREFOLDER };
            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                TestUtils.AssertResults(await c.RestoreAsync(new[] { filePath }));

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
                TestUtils.AssertResults(await c.RestoreAsync(new[] { filePath }));

                var restoredFilePath = Path.Combine(this.RESTOREFOLDER, "file");
                Assert.IsTrue(File.Exists(restoredFilePath));

                var restoredFileSecurity = new FileInfo(restoredFilePath).GetAccessControl();
                Assert.IsTrue(restoredFileSecurity.AreAccessRulesProtected);
            }
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreVolumeCacheAsync([Values("0b", "1mb", "5mb", "1gb", null)] string? cache_size, [Values("0", "1", null)] string? channel_size)
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
            TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));

            // Start a 30 second timeout
            var timeout_task = System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(30));

            var restore_task = System.Threading.Tasks.Task.Run(async () =>
            {
                TestUtils.AssertResults(await c.RestoreAsync(["*"]));
            });

            var t = await System.Threading.Tasks.Task.WhenAny(timeout_task, restore_task);

            if (t == timeout_task)
            {
                await c.AbortAsync().ConfigureAwait(false);
                await restore_task; // Ensure we wait for the restore task to complete
                Assert.Fail("Restore timed out");
            }
            else if (t == restore_task)
                // Throw any exceptions it might have
                await t.ConfigureAwait(false);

            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Restoring with different volume cache sizes");
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreInternalProfilingLogsCacheUsageAsync()
        {
            var sourceFilePath = Path.Combine(this.DATAFOLDER, "profiled-restore.bin");
            File.WriteAllBytes(sourceFilePath, new byte[256 * 1024]);

            var backupOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["dblock-size"] = "100kb",
                ["blocksize"] = "10kb"
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            var restoreOptions = new Dictionary<string, string>(backupOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["restore-legacy"] = "false",
                ["restore-with-local-blocks"] = "false",
                ["restore-volume-cache-hint"] = "1mb",
                ["internal-profiling"] = "true"
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            var logContents = File.ReadAllText(this.LOGFILE);
            Assert.That(logContents, Does.Contain("Max used cache size:"));
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreWithoutLocalDataAsync([Values("true", "false")] string noLocalDb, [Values("true", "false")] string patchWithLocalBlocks)
        {
            var file1Path = Path.Combine(this.DATAFOLDER, "file1");
            File.WriteAllBytes(file1Path, new byte[] { 1, 2, 3 });

            var file2Path = Path.Combine(this.DATAFOLDER, "file2");
            File.WriteAllBytes(file2Path, new byte[] { 3, 4, 6 });

            var folderPath = Path.Combine(this.DATAFOLDER, "folder");
            Directory.CreateDirectory(folderPath);
            systemIO.FileCopy(file1Path, Path.Combine(folderPath, "file1 copy"), true);

            using (var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            var restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["no-local-db"] = noLocalDb,
                ["restore-with-local-blocks"] = patchWithLocalBlocks
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Restoring without local data");
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreThreeVersionsWithoutLocalDbAsync([Values(0, 1, 2)] int version)
        {
            // Create three versions with different file contents
            var file1Path = Path.Combine(this.DATAFOLDER, "file1.txt");
            var file2Path = Path.Combine(this.DATAFOLDER, "file2.txt");

            // Version 0 (oldest): Create initial files
            File.WriteAllText(file1Path, "version 0 content");
            File.WriteAllText(file2Path, "version 0 file2");
            using (var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // Version 1: Modify file1, keep file2
            File.WriteAllText(file1Path, "version 1 content");
            using (var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // Version 2 (newest): Modify both files
            File.WriteAllText(file1Path, "version 2 content");
            File.WriteAllText(file2Path, "version 2 file2");
            using (var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // Prepare expected content based on version being restored
            // Note: In Duplicati, version 0 is the MOST RECENT backup
            string expectedFile1Content;
            string expectedFile2Content;
            switch (version)
            {
                case 0: // Most recent (version 2 in our creation order)
                    expectedFile1Content = "version 2 content";
                    expectedFile2Content = "version 2 file2";
                    break;
                case 1: // Second most recent (version 1 in our creation order)
                    expectedFile1Content = "version 1 content";
                    expectedFile2Content = "version 0 file2";
                    break;
                case 2: // Oldest (version 0 in our creation order)
                    expectedFile1Content = "version 0 content";
                    expectedFile2Content = "version 0 file2";
                    break;
                default:
                    throw new ArgumentException("Invalid version");
            }

            // Restore with --version and --no-local-db
            var restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["version"] = version.ToString(),
                ["no-local-db"] = "true"
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            // Verify restored files match the expected version
            var restoredFile1Path = Path.Combine(this.RESTOREFOLDER, "file1.txt");
            var restoredFile2Path = Path.Combine(this.RESTOREFOLDER, "file2.txt");

            Assert.IsTrue(File.Exists(restoredFile1Path), "Restored file1 should exist");
            Assert.IsTrue(File.Exists(restoredFile2Path), "Restored file2 should exist");
            Assert.AreEqual(expectedFile1Content, File.ReadAllText(restoredFile1Path), $"File1 content mismatch for version {version}");
            Assert.AreEqual(expectedFile2Content, File.ReadAllText(restoredFile2Path), $"File2 content mismatch for version {version}");
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreOtherProcessIsUsingFileAsync()
        {
            var file1Path = Path.Combine(this.DATAFOLDER, "file1");
            byte[] original_contents = [1, 2, 3];
            File.WriteAllBytes(file1Path, original_contents);

            var opts = new Dictionary<string, string>(this.TestOptions);
            opts["overwrite"] = "true";

            using var c = new Controller("file://" + this.TARGETFOLDER, opts, null);

            var res_backup = await c.BackupAsync([this.DATAFOLDER]);
            TestUtils.AssertResults(res_backup);

            File.WriteAllBytes(file1Path, [4, 5, 6]);

            using (var fs = new FileStream(file1Path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var res_failing = await c.RestoreAsync(["*"]);
                Assert.AreEqual(4, res_failing.Errors.Count());
                var first_error = res_failing.Errors.First();
                Assert.IsTrue(
                    first_error.Contains("IOException: The process cannot access the file")
                    &&
                    first_error.EndsWith(" because it is being used by another process.")
                );
            }

            var res_restore = await c.RestoreAsync(["*"]);
            TestUtils.AssertResults(res_restore);
            Assert.AreEqual(original_contents, File.ReadAllBytes(file1Path));
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreVolumeCacheDiskPressureAsync()
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
            TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));

            var timeout_task = System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(120));
            RestoreResults? result = null;

            var restore_task = System.Threading.Tasks.Task.Run(async () =>
            {
                result = (RestoreResults)await c.RestoreAsync(["*"]);
            });

            var t = await System.Threading.Tasks.Task.WhenAny(timeout_task, restore_task);
            if (t == timeout_task)
            {
                await c.AbortAsync().ConfigureAwait(false);
                await restore_task;
                Assert.Fail("Restore timed out");
            }
            else
            {
                await t.ConfigureAwait(false);
            }

            Assert.IsNotNull(result);
            Assert.That(result!.CachePressureEvictions, Is.GreaterThan(0), "Expected disk-pressure evictions with 999tb min-free");
            Assert.That(result.TotalVolumesAccessed, Is.GreaterThan(0), "Expected at least one volume to be accessed");
            Assert.That(result.Warnings.Count(), Is.GreaterThanOrEqualTo(1), "Expected at least one CachePressure warning");

            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Restoring with disk pressure eviction");
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreSkipFilesLargerThanAsync([Values("true", "false")] string restoreLegacy)
        {
            var smallFilePath = Path.Combine(this.DATAFOLDER, "small");
            File.WriteAllBytes(smallFilePath, new byte[1 * 1024]); // 1 KB

            var largeFilePath = Path.Combine(this.DATAFOLDER, "large");
            File.WriteAllBytes(largeFilePath, new byte[100 * 1024]); // 100 KB

            var backupOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["dblock-size"] = "1mb",
                ["blocksize"] = "10kb"
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // Skip files larger than 10 KB: the small file (1 KB) is restored, the large file (100 KB) is not.
            var restoreOptions = new Dictionary<string, string>(backupOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["restore-legacy"] = restoreLegacy,
                ["skip-files-larger-than"] = "10kb"
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            var restoredSmallFilePath = Path.Combine(this.RESTOREFOLDER, "small");
            var restoredLargeFilePath = Path.Combine(this.RESTOREFOLDER, "large");

            Assert.IsTrue(File.Exists(restoredSmallFilePath), "The small file should have been restored");
            Assert.IsFalse(File.Exists(restoredLargeFilePath), "The large file should not have been restored");
        }

        /// <summary>
        /// An abort is a requested shutdown, so it should not report errors of its own, should
        /// not blame the backup for a download it cancelled itself, and should not leave the
        /// volumes it had cached behind on disk.
        /// </summary>
        [Test]
        [Category("RestoreHandler")]
        public async Task AbortedRestoreIsCleanAsync()
        {
            const int volumesBeforeAbort = 3;

            var backupOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["dblock-size"] = "200kb",
                ["blocksize"] = "4kb"
            };

            // Enough distinct data to fill several volumes, so some are still cached when the
            // abort lands
            var rng = new Random(42);
            var data = new byte[32 * 1024];
            for (var i = 0; i < 40; i++)
            {
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(this.DATAFOLDER, $"file{i}"), data);
            }

            using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // The decrypted volumes are created with `new TempFile()`, which resolves through
            // TempFolder.SystemTempPath rather than through --tempdir, so that is what has to
            // be redirected for the test to be able to count what is left behind.
            var tempdir = Path.Combine(BASEFOLDER, "aborted-restore-temp");
            Directory.CreateDirectory(tempdir);
            var previousTempPath = Library.Utility.TempFolder.SystemTempPath;
            Library.Utility.TempFolder.SystemTempPath = tempdir;

            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            var downloaded = 0;
            var enoughDownloaded = new TaskCompletionSource();

            // Used as a hook rather than as a source of errors: it always answers "no error",
            // but it slows every volume download down so the restore cannot outrun the abort,
            // and it signals once enough volumes have been fetched. Without this the abort
            // would land at a different point on every machine.
            DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
            {
                if (!remotename.Contains(".dblock.", StringComparison.Ordinal))
                    return false;

                if (action != DeterministicErrorBackend.BackendAction.GetBefore)
                    return false;

                // Signalled before the delay rather than after it, so the abort is guaranteed
                // to land while a download is still in flight
                if (System.Threading.Interlocked.Increment(ref downloaded) >= volumesBeforeAbort)
                    enoughDownloaded.TrySetResult();

                System.Threading.Thread.Sleep(1500);
                return false;
            };

            try
            {
                var restoreOptions = new Dictionary<string, string>(this.TestOptions)
                {
                    ["dblock-size"] = "200kb",
                    ["blocksize"] = "4kb",
                    ["restore-path"] = this.RESTOREFOLDER,
                    ["restore-legacy"] = "false",
                    // Force the blocks to come from the destination rather than from the source
                    ["restore-with-local-blocks"] = "false",
                    // Several downloaders so that requests are queued behind the slow one and
                    // the abort has something in flight to cancel
                    ["restore-volume-downloaders"] = "4",
                    ["restore-volume-decryptors"] = "1",
                    ["restore-volume-decompressors"] = "1",
                    ["restore-file-processors"] = "1",
                    ["tempdir"] = tempdir
                    // restore-volume-cache-hint is deliberately left unset, which means
                    // unlimited: the volumes stay cached, which is the state this test is about.
                };

                var beforeRestore = Directory.GetFiles(tempdir, "*", SearchOption.AllDirectories).ToHashSet(StringComparer.Ordinal);

                var restoreErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();
                var cachedVolumes = 0;
                // Spelled out rather than taken from the types, which are internal to
                // Duplicati.Library.Main
                const string restoreNamespace = "Duplicati.Library.Main.Operation.Restore.";
                const string volumeManagerTag = restoreNamespace + "VolumeManager";

                Library.Main.RestoreResults? restoreResults = null;
                using var c = new Controller(new DeterministicErrorBackend().ProtocolKey + "://" + this.TARGETFOLDER, restoreOptions, null);
                c.OnOperationStarted += r => restoreResults = (Library.Main.RestoreResults)r;

                using (Library.Logging.Log.StartScope(e =>
                {
                    // Scoped to the restore process network. Controller logs FailedOperation of
                    // its own on an abort, and how an aborted operation is classified is a
                    // separate matter from what the network reports.
                    if (e.Level == Library.Logging.LogMessageType.Error
                        && e.Tag != null && e.Tag.StartsWith(restoreNamespace, StringComparison.Ordinal))
                        restoreErrors.Enqueue($"{e.Tag.Split('.').Last()}/{e.Id}: {e.FormattedMessage}");

                    // Guards against a vacuous pass: if nothing was ever cached there is
                    // nothing for the abort to leave behind.
                    if (e.Tag == volumeManagerTag && e.FormattedMessage != null
                        && e.FormattedMessage.StartsWith("Caching volume", StringComparison.Ordinal))
                        System.Threading.Interlocked.Increment(ref cachedVolumes);
                }))
                {
                    var restoreTask = Task.Run(async () => await c.RestoreAsync(new[] { "*" }));

                    var trigger = await Task.WhenAny(enoughDownloaded.Task, restoreTask, Task.Delay(TimeSpan.FromMinutes(1))).ConfigureAwait(false);
                    if (trigger == restoreTask)
                        Assert.Ignore("The restore finished before it could be aborted; the download hook is not slowing it down enough");
                    if (trigger != enoughDownloaded.Task)
                        Assert.Fail($"The restore did not download {volumesBeforeAbort} volumes within a minute");

                    await c.AbortAsync().ConfigureAwait(false);

                    var stopped = await Task.WhenAny(restoreTask, Task.Delay(TimeSpan.FromMinutes(1))).ConfigureAwait(false) == restoreTask;
                    Assert.IsTrue(stopped, "The abort did not stop the restore within a minute");

                    try
                    {
                        await restoreTask.ConfigureAwait(false);
                        Assert.Fail("The aborted restore was expected to fault");
                    }
                    catch (Exception)
                    {
                        // The type is deliberately not asserted: which task in the network faults
                        // first decides it, and classifying an aborted restore is a separate matter.
                    }
                }

                // No GC.Collect anywhere: the point is that the volumes are disposed
                // explicitly, and a forced collection would let a finalizer do that job.
                var leftover = Directory.GetFiles(tempdir, "*", SearchOption.AllDirectories)
                    .Where(x => !beforeRestore.Contains(x))
                    .ToList();

                // Checked on its own first: if nothing was ever cached the remaining
                // assertions could pass without proving anything.
                Assert.Greater(cachedVolumes, 0, "No volume was ever cached, so the test would pass without proving anything");

                // The three claims are independent, so report all of them rather than only
                // whichever happens to be checked first.
                NUnit.Framework.Assert.Multiple(() =>
                {
                    Assert.AreEqual(0, restoreErrors.Count,
                        $"An aborted restore reported errors:{Environment.NewLine}{string.Join(Environment.NewLine, restoreErrors)}");

                    Assert.AreEqual(0, restoreResults!.BrokenRemoteFiles.Count(),
                        $"An aborted restore blamed the backup for {string.Join(", ", restoreResults.BrokenRemoteFiles)}");

                    Assert.AreEqual(0, leftover.Count,
                        $"An aborted restore left temporary files behind:{Environment.NewLine}{string.Join(Environment.NewLine, leftover)}");
                });
            }
            finally
            {
                DeterministicErrorBackend.ErrorGenerator = null;
                Library.Utility.TempFolder.SystemTempPath = previousTempPath;
            }
        }

        /// <summary>
        /// A stop is a request to finish the current file and then stop, so the restore has to
        /// notice it while it is running rather than carry on to the end of the backup.
        /// </summary>
        [Test]
        [Category("RestoreHandler")]
        public async Task StoppedRestoreStopsAsync()
        {
            const int fileCount = 40;
            const int volumesBeforeStop = 3;

            var backupOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["dblock-size"] = "200kb",
                ["blocksize"] = "4kb"
            };

            // Enough distinct data to fill several volumes, so the restore still has plenty of
            // work left when the stop arrives
            var rng = new Random(42);
            var data = new byte[32 * 1024];
            for (var i = 0; i < fileCount; i++)
            {
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(this.DATAFOLDER, $"file{i}"), data);
            }

            using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            var downloaded = 0;
            var enoughDownloaded = new TaskCompletionSource();

            // Used as a hook rather than as a source of errors: it always answers "no error",
            // but it slows every volume download down so the restore cannot outrun the stop,
            // and it signals once enough volumes have been fetched.
            DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
            {
                if (action != DeterministicErrorBackend.BackendAction.GetBefore
                    || !remotename.Contains(".dblock.", StringComparison.Ordinal))
                    return false;

                if (System.Threading.Interlocked.Increment(ref downloaded) >= volumesBeforeStop)
                    enoughDownloaded.TrySetResult();

                System.Threading.Thread.Sleep(1500);
                return false;
            };

            try
            {
                var restoreOptions = new Dictionary<string, string>(this.TestOptions)
                {
                    ["dblock-size"] = "200kb",
                    ["blocksize"] = "4kb",
                    ["restore-path"] = this.RESTOREFOLDER,
                    ["restore-legacy"] = "false",
                    // Force the blocks to come from the destination rather than from the source
                    ["restore-with-local-blocks"] = "false",
                    ["restore-volume-downloaders"] = "4",
                    ["restore-volume-decryptors"] = "1",
                    ["restore-volume-decompressors"] = "1",
                    ["restore-file-processors"] = "1"
                };

                var restoreErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();
                // Spelled out rather than taken from the types, which are internal to
                // Duplicati.Library.Main
                const string restoreNamespace = "Duplicati.Library.Main.Operation.Restore.";

                Library.Main.RestoreResults? restoreResults = null;
                using var c = new Controller(new DeterministicErrorBackend().ProtocolKey + "://" + this.TARGETFOLDER, restoreOptions, null);
                c.OnOperationStarted += r => restoreResults = (Library.Main.RestoreResults)r;

                using (Library.Logging.Log.StartScope(e =>
                {
                    // Scoped to the restore process network; how a stopped operation is
                    // classified is a separate matter from what the network reports.
                    if (e.Level == Library.Logging.LogMessageType.Error
                        && e.Tag != null && e.Tag.StartsWith(restoreNamespace, StringComparison.Ordinal))
                        restoreErrors.Enqueue($"{e.Tag.Split('.').Last()}/{e.Id}: {e.FormattedMessage}");
                }))
                {
                    var restoreTask = Task.Run(async () => await c.RestoreAsync(new[] { "*" }));

                    var trigger = await Task.WhenAny(enoughDownloaded.Task, restoreTask, Task.Delay(TimeSpan.FromMinutes(1))).ConfigureAwait(false);
                    if (trigger == restoreTask)
                        Assert.Ignore("The restore finished before it could be stopped; the download hook is not slowing it down enough");
                    if (trigger != enoughDownloaded.Task)
                        Assert.Fail($"The restore did not download {volumesBeforeStop} volumes within a minute");

                    await c.StopAsync().ConfigureAwait(false);

                    // Bounded: a stop that does not stop the restore has to fail the test rather
                    // than hang the run, since NUnit has no per-test timeout to fall back on.
                    var grace = TimeSpan.FromMinutes(2);
                    var stopped = await Task.WhenAny(restoreTask, Task.Delay(grace)).ConfigureAwait(false) == restoreTask;
                    Assert.IsTrue(stopped, $"The restore did not stop within {grace.TotalMinutes:F0} minutes of being stopped");

                    // A stopped restore is a requested shutdown, so it is expected to return
                    // rather than fault.
                    await restoreTask.ConfigureAwait(false);
                }

                NUnit.Framework.Assert.Multiple(() =>
                {
                    Assert.Less(restoreResults!.RestoredFiles, fileCount,
                        $"The restore was stopped after {volumesBeforeStop} volumes but still restored all {fileCount} files, so the stop was ignored");

                    Assert.AreEqual(0, restoreErrors.Count,
                        $"A stopped restore reported errors:{Environment.NewLine}{string.Join(Environment.NewLine, restoreErrors)}");

                    Assert.AreEqual(0, restoreResults.BrokenRemoteFiles.Count(),
                        $"A stopped restore blamed the backup for {string.Join(", ", restoreResults.BrokenRemoteFiles)}");
                });
            }
            finally
            {
                DeterministicErrorBackend.ErrorGenerator = null;
            }
        }

        /// <summary>
        /// An abort must not record the files it was in the middle of as having failed to
        /// restore. This runs over an existing restore target, which is what takes the file
        /// processor through the paths that inspect the file already on disk.
        /// </summary>
        [Test]
        [Category("RestoreHandler")]
        public async Task AbortedRestoreOverExistingFilesDoesNotBlameThemAsync()
        {
            const int fileCount = 40;
            const int volumesBeforeAbort = 3;

            var backupOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["dblock-size"] = "200kb",
                ["blocksize"] = "4kb"
            };

            var rng = new Random(42);
            var data = new byte[32 * 1024];
            for (var i = 0; i < fileCount; i++)
            {
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(this.DATAFOLDER, $"file{i}"), data);
            }

            using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            var restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["dblock-size"] = "200kb",
                ["blocksize"] = "4kb",
                ["restore-path"] = this.RESTOREFOLDER,
                ["restore-legacy"] = "false",
                ["restore-with-local-blocks"] = "false",
                ["restore-volume-downloaders"] = "4",
                ["restore-volume-decryptors"] = "1",
                ["restore-volume-decompressors"] = "1",
                ["restore-file-processors"] = "1"
            };

            // First restore in full, so the second one has targets on disk to inspect. Without
            // existing targets the file processor never opens them and the paths under test are
            // not reached at all.
            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            // Corrupt every restored file so the second restore has real work to do on each of
            // them rather than verifying them and moving on.
            foreach (var path in Directory.GetFiles(this.RESTOREFOLDER, "*", SearchOption.AllDirectories))
            {
                var bytes = File.ReadAllBytes(path);
                for (var i = 0; i < Math.Min(bytes.Length, 4096); i++)
                    bytes[i] ^= 0xFF;
                File.WriteAllBytes(path, bytes);
            }

            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            var downloaded = 0;
            var enoughDownloaded = new TaskCompletionSource();

            // A hook rather than a source of errors: it always answers "no error", but delays
            // each volume fetch and signals before the delay, so the abort lands while the
            // restore is still working rather than wherever the machine's speed puts it.
            DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
            {
                if (action != DeterministicErrorBackend.BackendAction.GetBefore
                    || !remotename.Contains(".dblock.", StringComparison.Ordinal))
                    return false;

                if (System.Threading.Interlocked.Increment(ref downloaded) >= volumesBeforeAbort)
                    enoughDownloaded.TrySetResult();

                System.Threading.Thread.Sleep(1500);
                return false;
            };

            try
            {
                var restoreErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();
                // Spelled out rather than taken from the types, which are internal to
                // Duplicati.Library.Main
                const string restoreNamespace = "Duplicati.Library.Main.Operation.Restore.";

                Library.Main.RestoreResults? restoreResults = null;
                using var c2 = new Controller(new DeterministicErrorBackend().ProtocolKey + "://" + this.TARGETFOLDER, restoreOptions, null);
                c2.OnOperationStarted += r => restoreResults = (Library.Main.RestoreResults)r;

                using (Library.Logging.Log.StartScope(e =>
                {
                    if (e.Level == Library.Logging.LogMessageType.Error
                        && e.Tag != null && e.Tag.StartsWith(restoreNamespace, StringComparison.Ordinal))
                        restoreErrors.Enqueue($"{e.Tag.Split('.').Last()}/{e.Id}: {e.FormattedMessage}");
                }))
                {
                    var restoreTask = Task.Run(async () => await c2.RestoreAsync(new[] { "*" }));

                    var trigger = await Task.WhenAny(enoughDownloaded.Task, restoreTask, Task.Delay(TimeSpan.FromMinutes(1))).ConfigureAwait(false);
                    if (trigger == restoreTask)
                        Assert.Ignore("The restore finished before it could be aborted; the download hook is not slowing it down enough");
                    if (trigger != enoughDownloaded.Task)
                        Assert.Fail($"The restore did not download {volumesBeforeAbort} volumes within a minute");

                    await c2.AbortAsync().ConfigureAwait(false);

                    // Bounded: an abort that does not stop the restore has to fail the test
                    // rather than hang the run.
                    var grace = TimeSpan.FromMinutes(2);
                    var stopped = await Task.WhenAny(restoreTask, Task.Delay(grace)).ConfigureAwait(false) == restoreTask;
                    Assert.IsTrue(stopped, $"The abort did not stop the restore within {grace.TotalMinutes:F0} minutes");

                    try { await restoreTask.ConfigureAwait(false); }
                    catch (Exception) { /* the abort is expected to fault the operation */ }
                }

                NUnit.Framework.Assert.Multiple(() =>
                {
                    Assert.AreEqual(0, restoreResults!.BrokenLocalFiles.Count(),
                        $"An aborted restore reported files it never finished as having failed to restore: {string.Join(", ", restoreResults.BrokenLocalFiles)}");

                    Assert.AreEqual(0, restoreResults.BrokenRemoteFiles.Count(),
                        $"An aborted restore blamed the backup for {string.Join(", ", restoreResults.BrokenRemoteFiles)}");

                    Assert.AreEqual(0, restoreErrors.Count,
                        $"An aborted restore reported errors:{Environment.NewLine}{string.Join(Environment.NewLine, restoreErrors)}");
                });
            }
            finally
            {
                DeterministicErrorBackend.ErrorGenerator = null;
            }
        }
    }
}