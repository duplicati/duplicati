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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Main;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests for Issue #2499: deletes fail on a full copy-on-write file system
    /// until the file is truncated.
    /// https://github.com/duplicati/duplicati/issues/2499
    /// </summary>
    public class Issue2499 : BasicSetupHelper
    {
        [TearDown]
        public void ResetErrorBackend()
        {
            DeterministicErrorBackend.ErrorGenerator = null;
        }

        /// <summary>
        /// Makes two backups, then deletes the newest version through a backend
        /// that refuses to delete the dlist file while it is not empty, like a full
        /// ZFS or Btrfs volume does.
        /// </summary>
        /// <param name="truncate">The value of --truncate-before-delete</param>
        /// <returns>The dlist file name, the delete exception (if any), the number of reported errors and the observed puts to that file</returns>
        private async Task<(string FileName, System.Exception? Error, int ErrorCount, List<long> PutSizes)> RunDeleteOnFullStorageAsync(bool truncate)
        {
            var testopts = TestOptions.Expand(new
            {
                no_encryption = true,
                number_of_retries = 0,
                blocksize = "10kb",
                dblock_size = "50kb",
                no_auto_compact = true
            });

            var testFile = Path.Combine(DATAFOLDER, "testfile.txt");
            File.WriteAllText(testFile, "Test data for issue 2499");
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new string[] { DATAFOLDER }));

            File.WriteAllText(testFile, "Modified test data for issue 2499");
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new string[] { DATAFOLDER }));

            var fileToDelete = Path.GetFileName(Directory.GetFiles(TARGETFOLDER, "*.dlist.*").OrderBy(f => f).Last());
            var remotePath = Path.Combine(TARGETFOLDER, fileToDelete);
            var putSizes = new List<long>();

            BackendLoader.AddBackend(new DeterministicErrorBackend());
            DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
            {
                if (remotename != fileToDelete)
                    return false;
                if (action == DeterministicErrorBackend.BackendAction.PutAfter)
                    putSizes.Add(new FileInfo(remotePath).Length);
                // Simulate a full CoW volume: deleting a non-empty file fails
                return action == DeterministicErrorBackend.BackendAction.DeleteBefore
                    && File.Exists(remotePath) && new FileInfo(remotePath).Length > 0;
            };

            var deleteOpts = testopts.Expand(new
            {
                version = "0",
                allow_full_removal = true,
                truncate_before_delete = truncate
            });

            System.Exception? error = null;
            var errorCount = 0;
            try
            {
                using (var c = new Controller(new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER, deleteOpts, null))
                    errorCount = (await c.DeleteAsync()).Errors.Count();
            }
            catch (System.Exception ex)
            {
                error = ex;
            }
            finally
            {
                DeterministicErrorBackend.ErrorGenerator = null;
            }

            return (fileToDelete, error, errorCount, putSizes);
        }

        [Test]
        [Category("Targeted")]
        public async Task DeleteOnFullStorageFailsWithoutTruncateAsync()
        {
            var (fileName, _, _, putSizes) = await RunDeleteOnFullStorageAsync(false);

            Assert.That(putSizes, Is.Empty, "The file must not be overwritten unless the option is set");
            Assert.That(new FileInfo(Path.Combine(TARGETFOLDER, fileName)).Length, Is.GreaterThan(0), "The file should be left untouched");
        }

        [Test]
        [Category("Targeted")]
        public async Task DeleteOnFullStorageTruncatesAndRetriesAsync()
        {
            var (fileName, error, errorCount, putSizes) = await RunDeleteOnFullStorageAsync(true);

            Assert.That(error, Is.Null, "Delete should succeed after the file is truncated");
            Assert.That(errorCount, Is.Zero, "Delete should report no errors after the file is truncated");
            Assert.That(putSizes, Is.EqualTo(new List<long> { 0 }), "The file should be overwritten once with an empty file");
            Assert.That(File.Exists(Path.Combine(TARGETFOLDER, fileName)), Is.False, "The file should be deleted");
        }
    }
}
