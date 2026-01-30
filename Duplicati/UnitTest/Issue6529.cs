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
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests for GitHub Issue #6529: --changed-files and/or --deleted-files
    /// causes Unexpected difference in fileset version
    /// </summary>
    [TestFixture]
    public class Issue6529 : BasicSetupHelper
    {
        [Test]
        [Category("Targeted")]
        public void TestChangedFilesOptionDoesNotCauseFilesetMismatch()
        {
            // Setup: Create test files
            var testFile1 = Path.Combine(DATAFOLDER, "file1.txt");
            var testFile2 = Path.Combine(DATAFOLDER, "file2.txt");
            var testFile3 = Path.Combine(DATAFOLDER, "file3.txt");

            File.WriteAllText(testFile1, "Initial content 1");
            File.WriteAllText(testFile2, "Initial content 2");
            File.WriteAllText(testFile3, "Initial content 3");

            // Step 1: Do initial backup without --changed-files
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                var backupResults = c.Backup([DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Step 2: Modify one file
            File.WriteAllText(testFile2, "Modified content 2");

            // Step 3: Do backup with --changed-files pointing only to the modified file
            var changedFilesOptions = new Dictionary<string, string>(TestOptions)
            {
                ["changed-files"] = testFile2
            };

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, changedFilesOptions, null))
            {
                var backupResults = c.Backup([DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Step 4: Do another backup (this should NOT fail with fileset mismatch)
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                // This is where the bug manifests - the backup should succeed
                // but currently fails with "Unexpected difference in fileset version"
                var backupResults = c.Backup([DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count(),
                    "Backup after --changed-files should not cause fileset mismatch errors");
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Step 5: Verify data integrity by doing a test
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                var testResults = c.Test();
                Assert.AreEqual(0, testResults.Errors.Count());
            }
        }

        [Test]
        [Category("Targeted")]
        public void TestDeletedFilesOptionDoesNotCauseFilesetMismatch()
        {
            // Setup: Create test files
            var testFile1 = Path.Combine(DATAFOLDER, "file1.txt");
            var testFile2 = Path.Combine(DATAFOLDER, "file2.txt");
            var testFile3 = Path.Combine(DATAFOLDER, "file3.txt");

            File.WriteAllText(testFile1, "Initial content 1");
            File.WriteAllText(testFile2, "Initial content 2");
            File.WriteAllText(testFile3, "Initial content 3");

            // Step 1: Do initial backup
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                var backupResults = c.Backup([DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count());
            }

            // Step 2: Delete one file
            File.Delete(testFile2);

            // Step 3: Do backup with --changed-files and --deleted-files
            var changedFilesOptions = new Dictionary<string, string>(TestOptions)
            {
                ["changed-files"] = testFile1, // Just to trigger the changed-files path
                ["deleted-files"] = testFile2
            };

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, changedFilesOptions, null))
            {
                var backupResults = c.Backup([DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count());
            }

            // Step 4: Do another backup (this should NOT fail)
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                var backupResults = c.Backup([DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count(),
                    "Backup after --deleted-files should not cause fileset mismatch errors");
            }
        }

        [Test]
        [Category("Targeted")]
        public void TestMultipleChangedFilesBackupsInSequence()
        {
            // Setup: Create test files
            var testFile1 = Path.Combine(DATAFOLDER, "file1.txt");
            var testFile2 = Path.Combine(DATAFOLDER, "file2.txt");

            File.WriteAllText(testFile1, "Initial content 1");
            File.WriteAllText(testFile2, "Initial content 2");

            // Step 1: Initial backup
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                c.Backup([DATAFOLDER]);
            }

            // Step 2-4: Multiple sequential backups with --changed-files
            for (int i = 0; i < 3; i++)
            {
                // Modify file
                File.WriteAllText(testFile1, $"Modified content {i}");

                var changedFilesOptions = new Dictionary<string, string>(TestOptions)
                {
                    ["changed-files"] = testFile1
                };

                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, changedFilesOptions, null))
                {
                    var backupResults = c.Backup([DATAFOLDER]);
                    Assert.AreEqual(0, backupResults.Errors.Count(),
                        $"Backup iteration {i} with --changed-files should succeed");
                }
            }

            // Step 5: Final backup without --changed-files
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                var backupResults = c.Backup([DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count(),
                    "Final backup should succeed without fileset mismatch");
            }
        }
    }
}
