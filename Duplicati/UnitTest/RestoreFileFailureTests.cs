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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A file that cannot be restored is reported and skipped; the files after it are still
    /// restored. The restore used to stop at the first such file.
    /// </summary>
    public class RestoreFileFailureTests : BasicSetupHelper
    {
        /// <summary>
        /// The file that fails. It is the largest, and the restore takes the files largest first,
        /// so every other file comes after it.
        /// </summary>
        private const string FailingFile = "large";

        /// <summary>
        /// The files that are expected to be restored.
        /// </summary>
        private static readonly string[] OtherFiles = Enumerable.Range(0, 10).Select(i => $"file{i}").ToArray();

        /// <summary>
        /// The block size. Small, so that each file has several blocks and the block requests
        /// the restore sends ahead are still on their way when a file fails.
        /// </summary>
        private const int BlockSize = 1024;

        /// <summary>
        /// Writes the source files and backs them up.
        /// </summary>
        private async Task BackupAsync()
        {
            var rng = new Random(42);
            var data = new byte[BlockSize * 20];
            rng.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, FailingFile), data);

            foreach (var name in OtherFiles)
            {
                data = new byte[BlockSize * 5];
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(DATAFOLDER, name), data);
            }

            using var c = new Controller("file://" + TARGETFOLDER, BackupOptions(), null);
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
        }

        private Dictionary<string, string> BackupOptions()
            => new Dictionary<string, string>(TestOptions) { ["blocksize"] = $"{BlockSize}b" };

        /// <summary>
        /// The restore runs one file processor, so a failure that ends the processor ends the
        /// restore. The blocks come from the backup rather than from the source files, and
        /// several requests are sent ahead of the one being written.
        /// </summary>
        private Dictionary<string, string> RestoreOptions()
            => new Dictionary<string, string>(BackupOptions())
            {
                ["restore-path"] = RESTOREFOLDER,
                ["restore-legacy"] = "false",
                ["restore-file-processors"] = "1",
                ["restore-with-local-blocks"] = "false",
                ["restore-channel-buffer-size"] = "4",
            };

        /// <summary>
        /// Runs the restore and returns its results, the files it reported as not restored and
        /// the block and volume count errors it logged.
        /// </summary>
        private async Task<(RestoreResults Results, List<string> CountErrors)> RestoreAsync(Dictionary<string, string> options)
        {
            var countErrors = new ConcurrentQueue<string>();
            RestoreResults? results = null;
            using var c = new Controller("file://" + TARGETFOLDER, options, null);
            c.OnOperationStarted += r => results = (RestoreResults)r;

            using (Library.Logging.Log.StartScope(e =>
            {
                // Logged when the blocks a failed file never used are still counted as needed
                // at the end of the restore
                if (e.Id == "BlockCountError" || e.Id == "VolumeCountError")
                    countErrors.Enqueue($"{e.Id}: {e.FormattedMessage}");
            }))
            {
                // A file that fails with block responses still on the way can leave the
                // processor and the block manager waiting on each other, so a hang fails the
                // test rather than stalling it
                var restoreTask = Task.Run(async () => await c.RestoreAsync(["*"]));
                if (await Task.WhenAny(restoreTask, Task.Delay(TimeSpan.FromMinutes(2))) != restoreTask)
                {
                    await c.AbortAsync();
                    Assert.Fail("The restore did not finish within two minutes");
                }

                try
                {
                    await restoreTask;
                }
                catch (Exception ex)
                {
                    Assert.Fail($"The restore failed instead of skipping the file: {ex}");
                }
            }

            return (results!, countErrors.ToList());
        }

        /// <summary>
        /// Checks that every other file was restored with its content, that only the failing
        /// file was reported and that the blocks it did not use were given back.
        /// </summary>
        private void AssertOnlyTheFailingFileIsMissing(RestoreResults results, List<string> countErrors)
        {
            NUnit.Framework.Assert.Multiple(() =>
            {
                foreach (var name in OtherFiles)
                {
                    var restored = Path.Combine(RESTOREFOLDER, name);
                    Assert.IsTrue(File.Exists(restored), $"{name} was not restored");
                    if (File.Exists(restored))
                        CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(DATAFOLDER, name)), File.ReadAllBytes(restored), $"{name} was restored with the wrong content");
                }

                CollectionAssert.AreEqual(new[] { Path.Combine(RESTOREFOLDER, FailingFile) }, results.BrokenLocalFiles, "The files reported as not restored");
                CollectionAssert.IsEmpty(countErrors, "The blocks of the failed file were still counted as needed at the end");
            });
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task AFileThatCannotBeCreatedDoesNotStopTheRestore()
        {
            await BackupAsync();

            // A folder where the file should go, so the file cannot be created
            Directory.CreateDirectory(Path.Combine(RESTOREFOLDER, FailingFile));

            var (results, countErrors) = await RestoreAsync(RestoreOptions());

            AssertOnlyTheFailingFileIsMissing(results, countErrors);
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task AFileThatFailsAfterItsBlocksAreRequestedDoesNotStopTheRestore()
        {
            await BackupAsync();

            // The database claims the file is far larger than any disk holds, so reserving its
            // size fails. That happens after the first block requests for the file have been
            // sent, so their responses are still on the way when the file fails.
            const long tooLarge = 1L << 50;
            using (var con = await SQLiteLoader.LoadConnectionAsync(DBFILE))
            using (var cmd = con.CreateCommand())
            {
                var updated = await cmd
                    .SetCommandAndParameters(@"
                        UPDATE ""Blockset"" SET ""Length"" = @Length
                        WHERE ""ID"" = (
                            SELECT ""BlocksetID"" FROM ""File"" WHERE ""Path"" = @Path
                        )")
                    .SetParameterValue("@Length", tooLarge)
                    .SetParameterValue("@Path", Path.Combine(DATAFOLDER, FailingFile))
                    .ExecuteNonQueryAsync();
                Assert.AreEqual(1, updated, "The file's blockset was not found");
            }

            var options = RestoreOptions();
            options["restore-preallocate-size"] = "true";
            var (results, countErrors) = await RestoreAsync(options);

            var failing = Path.Combine(RESTOREFOLDER, FailingFile);
            if (!results.BrokenLocalFiles.Contains(failing) && File.Exists(failing) && new FileInfo(failing).Length == tooLarge)
            {
                File.Delete(failing);
                Assert.Ignore("The file system accepted the size as a sparse file, so the file did not fail");
            }

            AssertOnlyTheFailingFileIsMissing(results, countErrors);
        }
    }
}
