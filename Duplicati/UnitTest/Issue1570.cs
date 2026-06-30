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
using Duplicati.Library.Main;
using NUnit.Framework;
using Utility = Duplicati.Library.Utility.Utility;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Duplicati.Library.Main.Database.Local;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Reproduction test for issue #1570.
    ///
    /// The issue is that during the first backup, if the process is interrupted
    /// before the dlist (fileset) is uploaded, resuming the backup should not
    /// re-upload files that were already successfully processed. The local database
    /// should retain knowledge of uploaded blocks so the resumed backup can continue
    /// efficiently without treating already-processed files as entirely new.
    ///
    /// See: https://github.com/duplicati/duplicati/issues/1570
    /// </summary>
    public class Issue1570 : BasicSetupHelper
    {
        // Files to create in MB.
        private readonly int[] fileSizes = { 5, 10, 15 };

        /// <summary>
        /// Creates test source files.
        /// </summary>
        private void CreateSourceFiles()
        {
            foreach (int size in this.fileSizes)
            {
                byte[] data = new byte[size * 1024 * 1024];
                Random rng = new Random(size); // deterministic seed for reproducibility
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(this.DATAFOLDER, size + "MB"), data);
            }
        }

        /// <summary>
        /// Returns the combined size of all dblock files in the target folder.
        /// </summary>
        private long GetDblockTotalSize()
        {
            return Directory.EnumerateFiles(this.TARGETFOLDER, "*.dblock.*")
                .Select(f => new FileInfo(f).Length)
                .Sum();
        }

        /// <summary>
        /// Returns the number of dlist files in the target folder.
        /// </summary>
        private int GetDlistCount()
        {
            return Directory.EnumerateFiles(this.TARGETFOLDER, "*.dlist.*").Count();
        }

        [Test]
        [Category("Disruption")]
        [Category("Bug")]
        public async Task FirstBackupInterruptedAndResumedAsync()
        {
            // Use a small dblock size so multiple volumes are created.
            Dictionary<string, string> options = new Dictionary<string, string>(this.TestOptions)
            {
                ["dblock-size"] = "5mb",
                ["no-encryption"] = "true",
                ["number-of-retries"] = "0",
            };

            this.CreateSourceFiles();

            // --- Step 1: Run a first backup that fails during dlist upload ---
            // Register the deterministic error backend
            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            var failtarget = new DeterministicErrorBackend().ProtocolKey + "://" + this.TARGETFOLDER;
            var failed = false;

            // Fail when uploading the dlist file, but allow all dblock uploads
            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                if (action == DeterministicErrorBackend.BackendAction.PutBefore && remotename.Contains(".dlist."))
                {
                    failed = true;
                    return true;
                }
                return false;
            };

            using (var c = new Controller(failtarget, options, null))
            {
                var ex = Assert.CatchAsync(async () => await c.BackupAsync(new[] { this.DATAFOLDER }));
                Assert.That(ex, Is.Not.Null, "Backup should have thrown an exception");
            }

            Assert.That(failed, Is.True, "Failed to trigger the dlist upload failure");

            // Wait a moment to ensure the resumed backup's operation timestamp is
            // strictly after the synthetic filelist timestamp created from the
            // interrupted backup. Fileset filenames have one-second resolution, so
            // a sub-second gap between backups can otherwise place the synthetic
            // fileset timestamp ahead of the current operation timestamp.
            await Task.Delay(TimeSpan.FromSeconds(1));

            // Verify that dblock files were uploaded but no dlist exists
            var dblockFilesAfterFail = Directory.EnumerateFiles(this.TARGETFOLDER, "*.dblock.*").ToList();
            Assert.That(dblockFilesAfterFail.Count, Is.GreaterThan(0), "At least one dblock file should have been uploaded");

            long dblockSizeAfterFail = this.GetDblockTotalSize();
            TestContext.WriteLine("Dblock files after failed dlist: {0}, total size: {1}", dblockFilesAfterFail.Count, dblockSizeAfterFail);

            // The total source data size (5 + 10 + 15 = 30 MB).
            var totalSourceSize = this.fileSizes.Sum() * 1024L * 1024L;

            // After the dlist failure there should be a meaningful amount of data on the remote.
            Assert.That(dblockSizeAfterFail, Is.GreaterThan(totalSourceSize / 4),
                "A significant portion of the source data should have been uploaded before the dlist failure");

            var dlistCountAfterFail = this.GetDlistCount();
            TestContext.WriteLine("Dlist count after failed dlist: {0}", dlistCountAfterFail);

            // --- Step 2: Resume the first backup without modifying source files ---
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var resumeResults = await c.BackupAsync(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, resumeResults.Errors.Count(), "Resumed backup should not have errors");
                Assert.That(resumeResults.PartialBackup, Is.False, "Resumed backup should be a full backup");

                // All files should be examined
                Assert.That(resumeResults.ExaminedFiles, Is.EqualTo(this.fileSizes.Length),
                    "All source files should be examined during the resumed backup");
            }

            // Verify that the total dblock size did not grow excessively.
            // This is the key check for issue #1570: data already uploaded during
            // the failed attempt should not be duplicated on resume.
            var dblockSizeAfterResume = this.GetDblockTotalSize();
            TestContext.WriteLine("Dblock total size after failed dlist: {0}", dblockSizeAfterFail);
            TestContext.WriteLine("Dblock total size after resume:      {0}", dblockSizeAfterResume);
            TestContext.WriteLine("Dblock size increase:                {0}", dblockSizeAfterResume - dblockSizeAfterFail);

            // If Duplicati correctly re-uses already-uploaded blocks, the dblock
            // size increase should be very small (metadata overhead only).
            // If it re-uploads everything, the increase would be ~totalSourceSize.
            var sizeIncrease = dblockSizeAfterResume - dblockSizeAfterFail;
            Assert.That(sizeIncrease, Is.LessThan(totalSourceSize / 4),
                "Dblock size should not increase significantly on resume. " +
                "An increase close to the source data size indicates that already-uploaded blocks were duplicated. " +
                "This is the core concern raised in issue #1570.");

            // Sanity check: overall dblock size should still be reasonable
            Assert.That(dblockSizeAfterResume, Is.LessThan(totalSourceSize * 2),
                "Total dblock size after resume should be within reasonable bounds.");

            // --- Step 3: Verify the backup is complete and restorable ---
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var filesets = (await c.ListAsync()).Filesets.ToList();
                Assert.That(filesets.Count, Is.GreaterThanOrEqualTo(1), "There should be at least one completed fileset");

                // At least one fileset should be a full backup
                Assert.That(filesets.Any(f => f.IsFullBackup == BackupType.FULL_BACKUP), Is.True,
                    "There should be at least one full backup");

                var listResults = await c.ListAsync("*");
                var backedUpFiles = listResults.Files
                    .Select(x => x.Path)
                    .Where(x => !Utility.IsFolder(x, File.GetAttributes))
                    .ToArray();

                Assert.That(backedUpFiles.Length, Is.EqualTo(this.fileSizes.Length),
                    "All source files should be present in the backup");
            }

            // --- Step 4: Restore and verify all files ---
            var restoreOptions = new Dictionary<string, string>(options)
            {
                ["restore-path"] = this.RESTOREFOLDER
            };
            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                var listResults = await c.ListAsync("*");
                var allFiles = listResults.Files
                    .Select(x => x.Path)
                    .Where(x => !Utility.IsFolder(x, File.GetAttributes))
                    .ToArray();

                var restoreResults = await c.RestoreAsync(allFiles);
                Assert.That(restoreResults.RestoredFiles, Is.EqualTo(this.fileSizes.Length),
                    "All files should be restored");

                foreach (string filepath in allFiles)
                {
                    string filename = Path.GetFileName(filepath);
                    string originalPath = filepath; // path inside DATAFOLDER
                    string restoredPath = Path.Combine(this.RESTOREFOLDER, filename ?? string.Empty);
                    TestUtils.AssertFilesAreEqual(originalPath, restoredPath, false, filename);
                }
            }

            // --- Step 5: Verify database can be recreated from remote ---
            File.Delete(this.DBFILE);
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                TestUtils.AssertResults(await c.RepairAsync());

                var listResults = await c.ListAsync();
                Assert.That(listResults.Filesets.Count(), Is.GreaterThanOrEqualTo(1),
                    "Recreated database should have at least one fileset");
            }
        }
    }
}
