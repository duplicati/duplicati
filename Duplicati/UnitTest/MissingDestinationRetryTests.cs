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
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A restore lists its destination once, without retries, before it starts, so a destination
    /// folder that is not there is reported at once rather than after every retry (#4644
    /// mentions the wait). The remote operations of the restore itself still retry, as a network
    /// share that is briefly unreachable looks the same as a missing folder.
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
                ["restore-path"] = RESTOREFOLDER,
            };

        /// <summary>
        /// Runs the operation and returns how many remote operations it retried, and the error it
        /// failed with.
        /// </summary>
        private static async Task<(int Retries, Exception? Error)> CountRetriesAsync(Func<Task> operation)
        {
            var retries = 0;
            Exception? error = null;
            using (Library.Logging.Log.StartScope(e =>
            {
                if (e.Id != null && e.Id.StartsWith("Retry", StringComparison.Ordinal))
                    System.Threading.Interlocked.Increment(ref retries);
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

            return (retries, error);
        }

        /// <summary>
        /// Backs up a file, then moves the destination away, as a disconnected drive would
        /// </summary>
        /// <returns>The folder the destination was moved to</returns>
        private async Task<string> BackupAndMoveDestinationAsync()
        {
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), [1, 2, 3]);
            using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            var moved = TARGETFOLDER.TrimEnd(Path.DirectorySeparatorChar) + "-moved";
            Directory.Move(TARGETFOLDER, moved);
            return moved;
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task ARestoreFromAMissingDestinationFailsAtOnce()
        {
            var moved = await BackupAndMoveDestinationAsync();
            try
            {
                var (retries, error) = await CountRetriesAsync(async () =>
                {
                    using var c = new Controller("file://" + TARGETFOLDER, RetryOptions(), null);
                    await c.RestoreAsync(["*"]);
                });

                Assert.IsInstanceOf<FolderMissingException>(error, $"The restore ended with {error}");
                Assert.AreEqual(0, retries, "The missing destination was retried");
            }
            finally
            {
                Directory.Move(moved, TARGETFOLDER);
            }
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task ARestoreWithoutBackendVerificationStillRetries()
        {
            var moved = await BackupAndMoveDestinationAsync();
            try
            {
                // Without the check before the restore, the remote operations of the restore meet
                // the missing folder, and they keep retrying it
                var options = RetryOptions();
                options["no-backend-verification"] = "true";

                var (retries, _) = await CountRetriesAsync(async () =>
                {
                    using var c = new Controller("file://" + TARGETFOLDER, options, null);
                    await c.RestoreAsync(["*"]);
                });

                Assert.Greater(retries, 1, "The restore did not retry the missing destination");
            }
            finally
            {
                Directory.Move(moved, TARGETFOLDER);
            }
        }
    }
}
