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
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A destination folder that is not there is reported after one attempt by the operations
    /// that do not create it, rather than after every retry (#4644 mentions the wait). A backup
    /// still retries, as a network share that is briefly unreachable looks the same.
    /// </summary>
    public class MissingDestinationRetryTests : BasicSetupHelper
    {
        /// <summary>
        /// Retries with a short delay, so that a test that retries does not take long.
        /// </summary>
        private Dictionary<string, string> RetryOptions()
            => new Dictionary<string, string>(TestOptions)
            {
                ["number-of-retries"] = "3",
                ["retry-delay"] = "1s",
            };

        /// <summary>
        /// Runs the operation and returns how many times it listed the destination, and the error
        /// it failed with.
        /// </summary>
        private static async Task<(int Attempts, Exception? Error)> CountListAttemptsAsync(Func<Task> operation)
        {
            var attempts = 0;
            Exception? error = null;
            using (Library.Logging.Log.StartScope(e =>
            {
                if (e.Id == "RetryList")
                    System.Threading.Interlocked.Increment(ref attempts);
            }))
            {
                try
                {
                    await operation();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            }

            return (attempts, error);
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task ARestoreFromAMissingDestinationDoesNotRetry()
        {
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), [1, 2, 3]);
            using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            var moved = TARGETFOLDER.TrimEnd(Path.DirectorySeparatorChar) + "-moved";
            Directory.Move(TARGETFOLDER, moved);
            try
            {
                var options = RetryOptions();
                options["restore-path"] = RESTOREFOLDER;

                var (attempts, error) = await CountListAttemptsAsync(async () =>
                {
                    using var c = new Controller("file://" + TARGETFOLDER, options, null);
                    await c.RestoreAsync(["*"]);
                });

                Assert.IsInstanceOf<FolderMissingException>(error, $"The restore ended with {error}");
                Assert.AreEqual(1, attempts, "The destination was listed again after it was found missing");
            }
            finally
            {
                Directory.Move(moved, TARGETFOLDER);
            }
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task ABackupToAMissingDestinationStillRetries()
        {
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), [1, 2, 3]);
            Directory.Delete(TARGETFOLDER, true);

            // Without creating the folder, so it stays missing
            var options = RetryOptions();
            options["disable-autocreate-folder"] = "true";

            // The database of a backup that fails this way stays open after the backup, which
            // would fail the teardown that deletes the shared one, so it gets its own
            options["dbpath"] = Path.Combine(BASEFOLDER, $"missing-destination-{Guid.NewGuid():N}.sqlite");

            var (attempts, error) = await CountListAttemptsAsync(async () =>
            {
                using var c = new Controller("file://" + TARGETFOLDER, options, null);
                await c.BackupAsync([DATAFOLDER]);
            });

            Assert.IsInstanceOf<FolderMissingException>(error, $"The backup ended with {error}");
            Assert.AreEqual(4, attempts, "The backup did not retry the missing destination");
        }
    }
}
