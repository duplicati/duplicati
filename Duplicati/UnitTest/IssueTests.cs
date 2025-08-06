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
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

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
                // Make room for a backup
                Thread.Sleep(2000);

                // Delete f2 and compact
                File.Delete(file2);
                using (var c = new Controller(target, testopts, null))
                {
                    var backupResults = c.Backup(new[] { DATAFOLDER });
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

            // TODO check if the original files are untocuched

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
        [Category("Restore"), Category("Bug")]
        public void Issue6068FolderMetadata([Values] bool restoreLegacy)
        {
            // Reproduction of Issue #6068
            // The folder metadata is not restored

            var testopts = new Dictionary<string, string>(TestOptions);

            var original_dir_name = "some_original_dir";
            var original_dir = Path.Combine(DATAFOLDER, original_dir_name);
            var restored_dir = Path.Combine(RESTOREFOLDER, original_dir_name);
            Directory.CreateDirectory(original_dir);

            // Backup the files
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup([DATAFOLDER]);
                TestUtils.AssertResults(backupResults);
            }

            // Sleep to ensure timestamps are different
            System.Threading.Tasks.Task.Delay(1000).Wait();

            // Attempt to restore to another folder
            testopts["restore-path"] = RESTOREFOLDER;
            testopts["restore-legacy"] = restoreLegacy.ToString().ToLower();
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var restoreResults = c.Restore([]);
                Assert.That(restoreResults.RestoredFiles, Is.EqualTo(0), "No files should have been restored.");
            }

            // Assert that the original folders date modified and date created are the same as the restored folder
            var original_dir_info = new DirectoryInfo(original_dir);
            var restored_dir_info = new DirectoryInfo(restored_dir);
            Assert.That(original_dir_info.CreationTime, Is.EqualTo(restored_dir_info.CreationTime), "Creation time should be equal");
            Assert.That(original_dir_info.LastWriteTime, Is.EqualTo(restored_dir_info.LastWriteTime), "Last write time should be equal");
        }


        [Test]
        [Category("Test"), Category("Bug")]
        public void IssueXHashNullInTest()
        {
            // Reproduction of the issue outlined in https://forum.duplicati.com/t/value-cannot-be-null-parameter-hash-cannot-be-null/20958
            // This test is to ensure that a null hash in the database does not cause issues during backup and repair operations.
            var testopts = new Dictionary<string, string>(TestOptions)
            {
                ["full-remote-verification"] = "true"
            };
            testopts.Remove("backup-test-samples");

            // Create some test files
            Directory.CreateDirectory(DATAFOLDER);
            string f = Path.Combine(DATAFOLDER, "some_file");
            TestUtils.WriteTestFile($"{f}_0.txt", 1024);

            // Backup the files
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup([DATAFOLDER]);
                TestUtils.AssertResults(backupResults);
            }

            // Set one of the hashes in the database to null
            {
                using var con = new SqliteConnection($"Data source={DBFILE};Pooling=false");
                con.Open();
                using var cmd = con.CreateCommand();
                using var transaction = con.BeginTransaction();
                cmd.Transaction = transaction;
                cmd.CommandText = @"UPDATE Remotevolume SET Hash = NULL WHERE Name LIKE '%.dblock.%'";
                cmd.ExecuteNonQuery();
                transaction.Commit();
            }

            // Add another file and perform the backup again.
            // This would fail prior to the fix.
            TestUtils.WriteTestFile($"{f}_1.txt", 1024);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup([DATAFOLDER]);
                TestUtils.AssertResults(backupResults);
            }

            // Delete and recreate the database to ensure the null hash is not present anymore
            File.Delete(DBFILE);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IRepairResults repairResults = c.Repair();
                TestUtils.AssertResults(repairResults);
            }

            // Double check that the null hash is not present anymore
            {
                using var con = new SqliteConnection($"Data source={DBFILE};Pooling=false");
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = @"SELECT COUNT(*) FROM Remotevolume WHERE Hash IS NULL";
                var count = (long)cmd.ExecuteScalar();
                Assert.That(count, Is.EqualTo(0), "There should be no null hashes in the database.");
            }

            // Perform a final test backup to ensure everything is working correctly
            TestUtils.WriteTestFile($"{f}_2.txt", 1024);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup([DATAFOLDER]);
                TestUtils.AssertResults(backupResults);
            }
        }


        [Test]
        [Category("Restore"), Category("Bug")]
        public void Issue6068FileAndFolderAttributesAndPermissions([Values] bool restorePermissions, [Values] bool restoreLegacy, [Values] bool skip_metadata)
        {
            // This test is to verify that permissions are restored correctly
            // if the restore-permissions option is set, and that the
            // attributes are restored regardless. Some discussion is in the
            // related PR #6079

            // Set the options according to the test parameters
            var testopts = new Dictionary<string, string>(TestOptions)
            {
                ["restore-permissions"] = restorePermissions.ToString().ToLower(),
                ["restore-legacy"] = restoreLegacy.ToString().ToLower(),
                ["skip-metadata"] = skip_metadata.ToString().ToLower()
            };

            // The attributes to test
            var attrs = new FileAttributes[] {
                FileAttributes.ReadOnly,
                FileAttributes.Hidden,
                FileAttributes.System,
                // TODO The Archive attribute has been disabled for now, as the CI doesn't behave as expected, but it works locally.
                //FileAttributes.Archive
            };

            // Create the generated directories and files
            var original_dir = Path.Combine(DATAFOLDER, "some_original_dir");

            var dirs = attrs
                .Select(x => Path.Combine(original_dir, $"dir_{x}"))
                .Prepend(original_dir)
                .ToArray();

            foreach (var dir in dirs)
                Directory.CreateDirectory(dir);

            foreach (var attr in attrs)
                foreach (var dir in dirs)
                {
                    var file = Path.Combine(dir, $"file_{attr}");
                    TestUtils.WriteTestFile(file, 1024);
                    File.SetAttributes(file, attr);
                }

            foreach (var (attr, dir) in attrs.Zip(dirs.Skip(1)))
                _ = new DirectoryInfo(dir)
                {
                    Attributes = attr
                };

            // Create OS specific files and directories
            var os_special_dir = Path.Combine(original_dir, "os_special_dir");
            Directory.CreateDirectory(os_special_dir);
            var default_dir_attrs = new DirectoryInfo(os_special_dir).Attributes;
            var os_special_file = Path.Combine(os_special_dir, "os_special_file");
            TestUtils.WriteTestFile(os_special_file, 1024);
            var default_file_attrs = File.GetAttributes(os_special_file);

            if (OperatingSystem.IsWindows())
            {
                // Set only the current user to have access to both the file and the directory
                var dir_info = new DirectoryInfo(os_special_dir);
                var rules_dir = dir_info.GetAccessControl();
                rules_dir.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount));
                rules_dir.AddAccessRule(new FileSystemAccessRule(Environment.UserName, FileSystemRights.FullControl, AccessControlType.Allow));
                rules_dir.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.ReadAndExecute, AccessControlType.Allow));
                dir_info.SetAccessControl(rules_dir);

                var file_info = new FileInfo(os_special_file);
                var rules_file = file_info.GetAccessControl();
                rules_file.AddAccessRule(new FileSystemAccessRule(Environment.UserName, FileSystemRights.FullControl, AccessControlType.Allow));
                rules_file.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.ReadAndExecute, AccessControlType.Allow));
                file_info.SetAccessControl(rules_file);
            }
            else // Mac and Linux
            {
                // Set the dir to 700 and the file to 600
                _ = new DirectoryInfo(os_special_dir)
                {
                    UnixFileMode = UnixFileMode.UserExecute | UnixFileMode.UserRead | UnixFileMode.UserWrite
                };
                _ = new FileInfo(os_special_file)
                {
                    UnixFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                };
            }

            try
            {
                // Backup the files
                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                {
                    IBackupResults backupResults = c.Backup([DATAFOLDER]);
                    TestUtils.AssertResults(backupResults);
                }

                // Restore the files
                testopts["restore-path"] = RESTOREFOLDER;
                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                {
                    var restoreResults = c.Restore([]);
                }

                // Check the folder attributes
                foreach (var dir in dirs.Skip(1))
                {
                    var restored_dir = dir.Replace(DATAFOLDER, RESTOREFOLDER);
                    var original_attrs = new DirectoryInfo(dir).Attributes;
                    var restored_attrs = new DirectoryInfo(restored_dir).Attributes;
                    if (skip_metadata && original_attrs != default_dir_attrs)
                        Assert.That(original_attrs, Is.Not.EqualTo(restored_attrs), $"Directory attributes should not be equal for directory: {dir}");
                    else
                        Assert.That(original_attrs, Is.EqualTo(restored_attrs), $"Directory attributes should be equal for directory: {dir}");
                }

                // Check the file attributes
                foreach (var attr in attrs)
                    foreach (var dir in dirs)
                    {
                        var filename = $"file_{attr}";
                        var original_file = Path.Combine(dir, filename);
                        var restored_file = original_file.Replace(DATAFOLDER, RESTOREFOLDER);
                        // TODO we ignore the Archive attribute, as the CI sets this sporadically
                        var original_attrs = File.GetAttributes(original_file);
                        var restored_attrs = File.GetAttributes(restored_file);
                        if (skip_metadata && original_attrs != default_file_attrs)
                            Assert.That(original_attrs & ~FileAttributes.Archive, Is.Not.EqualTo(restored_attrs & ~FileAttributes.Archive), $"File attributes should not be equal for original file: {original_file}");
                        else
                            Assert.That(original_attrs & ~FileAttributes.Archive, Is.EqualTo(restored_attrs & ~FileAttributes.Archive), $"File attributes should be equal for original file: {original_file}");
                    }

                // Check the OS specific file and directory
                if (OperatingSystem.IsWindows())
                {
                    var original_dir_info = new DirectoryInfo(os_special_dir);
                    var restored_dir_info = new DirectoryInfo(os_special_dir.Replace(DATAFOLDER, RESTOREFOLDER));
                    var original_dir_rules = original_dir_info.GetAccessControl().GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount));
                    var restored_dir_rules = restored_dir_info.GetAccessControl().GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount));

                    // Disable only on Windows warning, as the if ensures this.
#pragma warning disable CA1416
                    static bool cmp_elem(FileSystemAccessRule a, FileSystemAccessRule b) =>
                        a.IdentityReference.Value == b.IdentityReference.Value &&
                        a.FileSystemRights == b.FileSystemRights &&
                        a.AccessControlType == b.AccessControlType;
#pragma warning restore CA1416

                    static bool cmp_coll(AuthorizationRuleCollection a, AuthorizationRuleCollection b) =>
                        a.Count == b.Count &&
                        a.Cast<FileSystemAccessRule>().Zip(b.Cast<FileSystemAccessRule>(), cmp_elem).All(x => x);

                    if (skip_metadata && original_dir_info.Attributes != default_dir_attrs)
                        Assert.That(original_dir_info.Attributes, Is.Not.EqualTo(restored_dir_info.Attributes), "Directory attributes should not be equal");
                    else
                        Assert.That(original_dir_info.Attributes, Is.EqualTo(restored_dir_info.Attributes), "Directory attributes should be equal");

                    var dir_permissions_equal = cmp_coll(original_dir_rules, restored_dir_rules);
                    if (restorePermissions && !skip_metadata)
                        Assert.That(dir_permissions_equal, Is.True, "Directory permissions should be equal");
                    else
                        Assert.That(dir_permissions_equal, Is.False, "Directory permissions should not be equal");

                    var original_file_info = new FileInfo(os_special_file);
                    var restored_file_info = new FileInfo(os_special_file.Replace(DATAFOLDER, RESTOREFOLDER));
                    var original_file_rules = original_file_info.GetAccessControl().GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount));
                    var restored_file_rules = restored_file_info.GetAccessControl().GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount));

                    if (skip_metadata && original_file_info.Attributes != default_file_attrs)
                        Assert.That(original_file_info.Attributes, Is.Not.EqualTo(restored_file_info.Attributes), "File attributes should not be equal");
                    else
                        Assert.That(original_file_info.Attributes, Is.EqualTo(restored_file_info.Attributes), "File attributes should be equal");

                    var file_permissions_equal = cmp_coll(original_file_rules, restored_file_rules);
                    if (restorePermissions && !skip_metadata)
                        Assert.That(file_permissions_equal, Is.True, "File permissions should be equal");
                    else
                        Assert.That(file_permissions_equal, Is.False, "File permissions should not be equal");
                }
                else
                {
                    var original_dir_info = new DirectoryInfo(os_special_dir);
                    var restored_dir_info = new DirectoryInfo(os_special_dir.Replace(DATAFOLDER, RESTOREFOLDER));

                    if (skip_metadata && original_dir_info.Attributes != default_dir_attrs)
                        Assert.That(original_dir_info.Attributes, Is.Not.EqualTo(restored_dir_info.Attributes), "Directory attributes should not be equal");
                    else
                        Assert.That(original_dir_info.Attributes, Is.EqualTo(restored_dir_info.Attributes), "Directory attributes should be equal");

                    if (restorePermissions && !skip_metadata)
                        Assert.That(original_dir_info.UnixFileMode, Is.EqualTo(restored_dir_info.UnixFileMode), "Directory permissions should be equal");
                    else
                        Assert.That(original_dir_info.UnixFileMode, Is.Not.EqualTo(restored_dir_info.UnixFileMode), "Directory permissions should not be equal");

                    var original_file_info = new FileInfo(os_special_file);
                    var restored_file_info = new FileInfo(os_special_file.Replace(DATAFOLDER, RESTOREFOLDER));

                    if (skip_metadata && original_file_info.Attributes != default_file_attrs)
                        Assert.That(original_file_info.Attributes, Is.Not.EqualTo(restored_file_info.Attributes), "File attributes should not be equal");
                    else
                        Assert.That(original_file_info.Attributes, Is.EqualTo(restored_file_info.Attributes), "File attributes should be equal");

                    if (restorePermissions && !skip_metadata)
                        Assert.That(original_file_info.UnixFileMode, Is.EqualTo(restored_file_info.UnixFileMode), "File permissions should be equal");
                    else
                        Assert.That(original_file_info.UnixFileMode, Is.Not.EqualTo(restored_file_info.UnixFileMode), "File permissions should not be equal");
                }
            }
            finally
            {
                // Revert the attributes so the cleanup can proceed
                foreach (var dir in dirs)
                {
                    _ = new DirectoryInfo(dir)
                    {
                        Attributes = FileAttributes.Normal | FileAttributes.Directory
                    };
                    var restored_dir = dir.Replace(DATAFOLDER, RESTOREFOLDER);
                    if (Directory.Exists(restored_dir))
                        _ = new DirectoryInfo(restored_dir)
                        {
                            Attributes = FileAttributes.Normal | FileAttributes.Directory
                        };
                }

                foreach (var attr in attrs)
                    foreach (var dir in dirs)
                    {
                        var file = Path.Combine(dir, $"file_{attr}");
                        File.SetAttributes(file, FileAttributes.Normal);
                        var restored_file = file.Replace(DATAFOLDER, RESTOREFOLDER);
                        if (File.Exists(restored_file))
                            File.SetAttributes(restored_file, FileAttributes.Normal);
                    }

                // Special dirs can be ignored, as they should only block other
                // users from accessing the files
            }
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
            block1.AsSpan().Fill(1);
            byte[] block2 = new byte[10 * 1024];
            block2.AsSpan().Fill(2);

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
