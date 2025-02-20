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
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace Duplicati.UnitTest
{
    public class IssueTests : BasicSetupHelper
    {

        [Test]
        [Category("Targeted")]
        public void Issue5023ReferencedFileMissing([Values] bool compactBeforeRecreate)
        {
            // Reproduction for part of issue #5023
            // Error during repair: "Remote file referenced as x by y, but not found in list, registering a missing remote file"
            // Can be caused by interrupted index upload followed by compact before the repair

            var testopts = new Dictionary<string, string>(TestOptions)
            {
                ["number-of-retries"] = "0",
                ["dblock-size"] = "20KB",
                ["threshold"] = "1",
                ["keep-versions"] = "1",
                ["no-auto-compact"] = "true",
                ["no-encryption"] = "true"
            };

            const long filesize = 1024;

            // First backup OK to create valid database
            string target = "file://" + TARGETFOLDER;
            string file1 = Path.Combine(DATAFOLDER, "f1");
            TestUtils.WriteTestFile(file1, filesize);
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
            }
            // Sleep to ensure timestamps are different
            Thread.Sleep(1000);
            // Fail after index file put
            target = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
            string interruptedName = "";
            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                // Fail dindex upload
                if (action == DeterministicErrorBackend.BackendAction.PutAfter && remotename.Contains("dindex"))
                {
                    interruptedName = remotename;
                    return true;
                }
                return false;
            };
            BackendLoader.AddBackend(new DeterministicErrorBackend());
            string file2 = Path.Combine(DATAFOLDER, "f2");
            TestUtils.WriteTestFile(file2, filesize);
            var uploadEx = Assert.Catch(() =>
            {
                using (var c = new Library.Main.Controller(target, testopts, null))
                {
                    IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                }
            });

            while (uploadEx is AggregateException && uploadEx.InnerException is not null)
                uploadEx = uploadEx.InnerException;

            Assert.That(uploadEx, Is.TypeOf<DeterministicErrorBackend.DeterministicErrorBackendException>());

            Console.WriteLine("Interrupted after upload of {0}", interruptedName);
            // Sleep to ensure timestamps are different
            Thread.Sleep(1000);
            // Complete upload
            target = "file://" + TARGETFOLDER;
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
            }
            // At this point there are two index files for the last dblock
            Console.WriteLine("Target folder contents (expect extra index file):");
            Console.WriteLine(string.Join("\n", from v in Directory.EnumerateFiles(TARGETFOLDER) select Path.GetFileName(v)));

            // Note: If there is a recreate here (between the above extra file creation and the following compact),
            // the bug does not occur as the extra index file will be recorded in the database and properly deleted by compact

            if (compactBeforeRecreate)
            {
                // Delete f2 and compact
                File.Delete(file2);
                using (var c = new Library.Main.Controller(target, testopts, null))
                {
                    IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                    TestUtils.AssertResults(backupResults);
                    ICompactResults compactResults = c.Compact();
                    TestUtils.AssertResults(compactResults);
                    Assert.Greater(compactResults.DeletedFileCount, 0);
                }
                // At this point there are two index files for the last dblock
                Console.WriteLine("Target folder contents (expect extra index file):");
                Console.WriteLine(string.Join("\n", from v in Directory.EnumerateFiles(TARGETFOLDER) select Path.GetFileName(v)));
            }

            // Database recreate should work (fails after compact)
            File.Delete(DBFILE);
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IRepairResults repairResults = c.Repair();
                TestUtils.AssertResults(repairResults);
            }
        }


        [Test]
        [Category("Restore"), Category("Bug")]
        public void Issue5825RestoreNoOverwrite([Values] bool legacy, [Values] bool local_blocks)
        {
            // Reproduction of Issue #5825
            // The logic in the previous version was to create a timestamped version of the file being restored to, if it already exists.
            // It appears the new restore flow is not correctly doing the same.
            // See forum thread: https://forum.duplicati.com/t/save-different-versions-with-timestamp-in-file-name-broken/19805

            var testopts = new Dictionary<string, string>(TestOptions)
            {
                ["restore-legacy"] = legacy.ToString().ToLower(),
                ["restore-with-local-blocks"] = local_blocks.ToString().ToLower()
            };
            int test_filesize = 1024;

            // TODO tjek om de gamle filer forbliver urørte.

            var original_dir = Path.Combine(DATAFOLDER, "some_original_dir");
            Directory.CreateDirectory(original_dir);
            string f0 = Path.Combine(original_dir, "some_file");
            TestUtils.WriteTestFile(f0, test_filesize);

            // Backup the files
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup([DATAFOLDER]);
                TestUtils.AssertResults(backupResults);
            }

            // Attempt to restore the file
            testopts["restore-path"] = RESTOREFOLDER;
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var restoreResults = c.Restore([f0]);
                Assert.That(restoreResults.RestoredFiles, Is.EqualTo(1), "File should have been restored");
            }

            // Verify that the files are equal
            string f1 = Path.Combine(RESTOREFOLDER, "some_file");
            Assert.That(File.ReadAllBytes(f0), Is.EqualTo(File.ReadAllBytes(f1)), "Restored file should be equal to original file");

            // Modify the restored file
            TestUtils.WriteTestFile(f1, test_filesize);

            // Restore the file again, with overwrite.
            testopts["overwrite"] = "true";
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var restoreResults = c.Restore([f0]);
                Assert.That(restoreResults.RestoredFiles, Is.EqualTo(1), "File should have been restored");
            }

            // Verify that the files are equal
            Assert.That(File.ReadAllBytes(f0), Is.EqualTo(File.ReadAllBytes(f1)), "Restored file should be equal to original file");

            // Save the timestamp of the file
            var timestamp = File.GetLastWriteTime(f1);

            // Touch the file
            File.SetLastWriteTime(f1, DateTime.Now);

            // Check that the timestamp has changed, but the file is still equal
            Assert.That(File.GetLastWriteTime(f1), Is.Not.EqualTo(timestamp), "Timestamp should have changed");
            Assert.That(File.ReadAllBytes(f0), Is.EqualTo(File.ReadAllBytes(f1)), "Restored file should be equal to original file");

            // Restore the file again, with overwrite, should restore the timestamp of the file
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var restoreResults = c.Restore([f0]);
                Assert.That(restoreResults.RestoredFiles, Is.EqualTo(0), "File should not have been restored, only the metadata.");
            }

            // Verify that the files are equal and the timestamp is restored
            Assert.That(File.ReadAllBytes(f0), Is.EqualTo(File.ReadAllBytes(f1)), "Restored file should be equal to original file");
            Assert.That(File.GetLastWriteTime(f1), Is.EqualTo(timestamp), "Timestamp should be restored");

            // Modify the restored file
            TestUtils.WriteTestFile(f1, test_filesize);
            var f1_original = File.ReadAllBytes(f1);

            // Restore the file again, without overwrite.
            testopts["overwrite"] = "false";
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var restoreResults = c.Restore([f0]);
                Assert.That(restoreResults.RestoredFiles, Is.EqualTo(1), "File should have been restored");
            }

            // Verify that f1 is still the same
            Assert.That(File.ReadAllBytes(f1), Is.EqualTo(f1_original), "The first restored file should remain untouched");

            // Verify that there exists a new file with a timestamp
            var files = Directory.GetFiles(RESTOREFOLDER, "*", SearchOption.TopDirectoryOnly);
            Assert.That(files.Length, Is.EqualTo(2), "There should be two files in the folder");
            var f2 = files.FirstOrDefault(v => v != f1);

            // Modify the new restored file as well
            TestUtils.WriteTestFile(f2, test_filesize);
            var f2_original = File.ReadAllBytes(f2);

            // Restore the file again, without overwrite.
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var restoreResults = c.Restore([f0]);
                Assert.That(restoreResults.RestoredFiles, Is.EqualTo(1), "File should have been restored");
            }

            // Verify that f1 and f2 are still the same
            Assert.That(File.ReadAllBytes(f1), Is.EqualTo(f1_original), "The first restored file should remain untouched");
            Assert.That(File.ReadAllBytes(f2), Is.EqualTo(f2_original), "The second restored file should remain untouched");

            // Verify that there exists a new file with a timestamp
            files = Directory.GetFiles(RESTOREFOLDER, "*", SearchOption.TopDirectoryOnly);
            Assert.That(files.Length, Is.EqualTo(3), "There should be three files in the folder");

            // Verify that the files are equal
            var f3 = files.FirstOrDefault(v => v != f1 && v != f2);
            Assert.That(File.ReadAllBytes(f0), Is.EqualTo(File.ReadAllBytes(f3)), "Restored file should be equal to original file");

            // Modify the second file to match the original file - should not restore any files
            File.WriteAllBytes(f2, File.ReadAllBytes(f0));

            // Restore the file again, without overwrite.
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var restoreResults = c.Restore([f0]);
                Assert.That(restoreResults.RestoredFiles, Is.EqualTo(0), "File should not have been restored");
            }

            // Verify that f1 is still untouched
            Assert.That(File.ReadAllBytes(f1), Is.EqualTo(f1_original), "The first restored file should remain untouched");
        }


        [Test]
        [Category("Restore"), Category("Bug")]
        public void Issue5826RestoreMissingFolder([Values] bool compressRestorePaths, [Values] bool restoreLegacy)
        {
            // Reproduction of Issue #5826
            // With the new restore engine, it looks like a file will not be restored if the folder is not selected for restore.
            // See forum issue for details: https://forum.duplicati.com/t/could-not-find-a-part-of-the-path/19775

            var testopts = new Dictionary<string, string>(TestOptions)
            {
                ["dont-compress-restore-paths"] = (!compressRestorePaths).ToString().ToLower(),
                ["restore-legacy"] = restoreLegacy.ToString().ToLower()
            };

            var original_dir = Path.Combine(DATAFOLDER, "some_original_dir");
            Directory.CreateDirectory(original_dir);
            string f = Path.Combine(original_dir, "some_file");
            TestUtils.WriteTestFile(f, 1024 * 20);

            // Backup the files
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup([DATAFOLDER]);
                TestUtils.AssertResults(backupResults);
            }

            // Attempt to restore a file without selecting its parent folder
            testopts["restore-path"] = RESTOREFOLDER;
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var restoreResults = c.Restore([f]);
                Assert.That(restoreResults.RestoredFiles, Is.EqualTo(1), "File should have been restored.");
                Assert.That(restoreResults.Warnings.Count(), Is.EqualTo(compressRestorePaths ? 0 : 1), "Warning should be generated for missing folder");
            }
        }


        [Test]
        [Category("Restore"), Category("Bug")]
        public void Issue5886RestoreModifiedMiddleBlock()
        {
            var blocksize = 1024;
            var n_blocks = 5;
            var testopts = new Dictionary<string, string>(TestOptions)
            {
                ["blocksize"] = $"{blocksize}b",
                ["overwrite"] = "true"
            };

            var original_dir = Path.Combine(DATAFOLDER, "some_original_dir");
            Directory.CreateDirectory(original_dir);
            string f = Path.Combine(original_dir, "some_file");
            TestUtils.WriteTestFile(f, n_blocks * blocksize);

            // Backup the files
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup([DATAFOLDER]);
                TestUtils.AssertResults(backupResults);
            }

            var original_contents = File.ReadAllBytes(f);
            var new_block = new byte[blocksize];
            new Random(5888).NextBytes(new_block);
            var new_contents = new byte[n_blocks * blocksize];

            for (int i = 0; i < n_blocks; i++)
            {
                for (int j = i; j < n_blocks; j++)
                {
                    // Modify the blocks
                    for (int k = 0; k < n_blocks; k++)
                    {
                        if (k == i || k == j)
                        {
                            Array.Copy(new_block, 0, new_contents, k * blocksize, blocksize);
                        }
                        else
                        {
                            Array.Copy(original_contents, k * blocksize, new_contents, k * blocksize, blocksize);
                        }
                    }

                    // Write the modified file
                    File.WriteAllBytes(f, new_contents);
                    var restored_contents = File.ReadAllBytes(f);
                    Assert.That(restored_contents, Is.Not.EqualTo(original_contents), "Restored file should not be equal to original file");

                    // Attempt to restore the file
                    using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                    {
                        var restoreResults = c.Restore([f]);
                        Assert.That(restoreResults.RestoredFiles, Is.EqualTo(1), "File should have been restored.");
                    }

                    // Verify that the files are equal
                    restored_contents = File.ReadAllBytes(f);
                    Assert.That(restored_contents, Is.EqualTo(original_contents), "Restored file should be equal to original file");
                }
            }
        }

        [Test]
        [Category("Restore"), Category("Bug")]
        public void Issue5957RestoreModifiedBlockUseLocalBlocks([Values] bool use_local, [Values] bool overwrite)
        {
            var blocksize = 1024;
            var testopts = new Dictionary<string, string>(TestOptions)
            {
                ["blocksize"] = $"{blocksize}b",
                ["restore-with-local-blocks"] = use_local.ToString().ToLower(),
                ["overwrite"] = overwrite.ToString().ToLower()
            };

            var original_dir = Path.Combine(DATAFOLDER, "some_original_dir");
            Directory.CreateDirectory(original_dir);
            string f = Path.Combine(original_dir, "some_file");
            TestUtils.WriteTestFile(f, 3 * blocksize);

            // Backup the files
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup([DATAFOLDER]);
                TestUtils.AssertResults(backupResults);
            }

            var original_contents = File.ReadAllBytes(f);

            using (var stream = File.Open(f, FileMode.Open, FileAccess.ReadWrite))
            {
                stream.Seek(0, SeekOrigin.Begin);
                byte[] buffer = new byte[1];
                stream.Read(buffer, 0, 1);
                buffer[0] = (byte)~buffer[0];
                stream.Seek(0, SeekOrigin.Begin);
                stream.Write(buffer, 0, 1);
            }

            var restored_contents = File.ReadAllBytes(f);
            Assert.That(restored_contents, Is.Not.EqualTo(original_contents), "Restored file should not be equal to original file");

            // Attempt to restore the file
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var restoreResults = c.Restore([f]);
                Assert.That(restoreResults.RestoredFiles, Is.EqualTo(1), "File should have been restored.");
            }

            // Verify that the files are equal
            // List files in folder
            if (overwrite)
            {
                restored_contents = File.ReadAllBytes(f);
            }
            else
            {
                var files = Directory.GetFiles(original_dir, "*", SearchOption.TopDirectoryOnly);
                Assert.That(files.Length, Is.EqualTo(2), "There should be two files in the folder");
                var f2 = files.FirstOrDefault(v => v != f);
                restored_contents = File.ReadAllBytes(f2);
            }
            Assert.That(restored_contents, Is.EqualTo(original_contents), "Restored file should be equal to original file");
        }


        [Test]
        [Category("Disruption"), Category("Bug")]
        public void TestSystematicErrors5023()
        {
            // Attempt to recreate other bugs from #5023, but not successful
            var testopts = new Dictionary<string, string>(TestOptions)
            {
                ["number-of-retries"] = "0",
                ["dblock-size"] = "20KB",
                ["threshold"] = "1",
                ["keep-versions"] = "5",
                ["no-encryption"] = "true",
                ["disable-synthetic-filelist"] = "true"
            };
            //testopts["rebuild-missing-dblock-files"] = "true";
            string target = "file://" + TARGETFOLDER;
            string targetError = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
            int maxFiles = 10;
            List<string> files = new List<string>();
            bool failed = false;
            long accessCounter = 0;
            long errorIdx = 0;
            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                ++accessCounter;
                if (accessCounter >= errorIdx)
                {
                    return true;
                }
                return false;
            };
            for (int i = 0; i < maxFiles; ++i)
            {
                string f = Path.Combine(DATAFOLDER, "f" + i);
                TestUtils.WriteTestFile(f, 1024 * 20);
                files.Add(f);
            }
            // Initial backup
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
            }
            while (errorIdx < (maxFiles + 2))
            {
                if (errorIdx % 10 == 0)
                {
                    TestContext.WriteLine("Error index {0}", errorIdx);
                }
                accessCounter = 0;
                try
                {
                    using (var c = new Library.Main.Controller(targetError, testopts, null))
                    {
                        IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                        TestUtils.AssertResults(backupResults);
                    }
                }
                catch (AssertionException) { throw; }
                catch { }
                Thread.Sleep(1000);
                try
                {
                    using (var c = new Library.Main.Controller(target, testopts, null))
                    {
                        IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                        TestUtils.AssertResults(backupResults);
                    }
                }
                catch (UserInformationException e)
                {
                    TestContext.WriteLine("Error at index {0}: {1}", errorIdx, e.Message);
                    if (e.HelpID == "MissingRemoteFiles" || e.HelpID == "ExtraRemoteFiles")
                    {
                        using (var c = new Library.Main.Controller(target, testopts, null))
                        {
                            IRepairResults repairResults = c.Repair();
                            TestUtils.AssertResults(repairResults);
                        }
                    }
                    failed = true;
                }
                Thread.Sleep(1000);
                foreach (string f in files)
                {
                    TestUtils.WriteTestFile(f, 1024 * 20);
                }
                ++errorIdx;
            }
            TestContext.WriteLine("Ran {0} iterations", errorIdx);
            Assert.IsFalse(failed);
        }

        [Test, Sequential]
        [Category("Targeted"), Category("Bug"), Category("Non-critical")]
        [TestCase(false, true), TestCase(true, true), TestCase(true, false)]
        public void Issue5038MissingListBlocklist(bool sameVersion, bool blockFirst)
        {
            // Backup containing the blocklist of a file BEFORE the file causes a dindex with missing blocklist entry
            // This is not critical, because it only requires extra block volume downloads
            var testopts = new Dictionary<string, string>(TestOptions)
            {
                ["no-encryption"] = "true"
            };

            string filename = Path.Combine(DATAFOLDER, "file");
            // Start with z to process blockfile after file (at least on some systems)
            string blockfile = Path.Combine(DATAFOLDER, blockFirst ? "block" : "zblock");
            string target = "file://" + TARGETFOLDER;

            byte[] block1 = new byte[10 * 1024];
            for (int i = 0; i < block1.Length; ++i)
            {
                block1[i] = 1;
            }
            byte[] block2 = new byte[10 * 1024];
            for (int i = 0; i < block1.Length; ++i)
            {
                block1[i] = 2;
            }

            HashAlgorithm blockhasher = Library.Utility.HashFactory.CreateHasher(new Options(testopts).BlockHashAlgorithm);

            var hash1 = blockhasher.ComputeHash(block1, 0, block1.Length);
            var hash2 = blockhasher.ComputeHash(block2, 0, block2.Length);

            byte[] blockfileContent = hash1.Concat(hash2).ToArray();
            TestUtils.WriteFile(blockfile, blockfileContent);
            if (!sameVersion)
            {
                // Backup blockfile first
                using (var c = new Library.Main.Controller(target, testopts, null))
                {
                    IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                    TestUtils.AssertResults(backupResults);
                }
            }

            byte[] combined = block1.Concat(block2).ToArray();
            TestUtils.WriteFile(filename, combined);
            // Backup file that would produce blockfile
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
            }

            // Recreate database downloads block volume
            File.Delete(DBFILE);
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IRepairResults repairResults = c.Repair();
                TestUtils.AssertResults(repairResults);
                Assert.IsNull(repairResults.Messages.FirstOrDefault(v => v.Contains("ProcessingRequiredBlocklistVolumes")),
                    "Blocklist download pass was required");
            }
        }
    }
}
