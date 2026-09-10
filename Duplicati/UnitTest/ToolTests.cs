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

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{

    /// <summary>
    /// Tests the tools.
    /// </summary>
    public class ToolTests : BasicSetupHelper
    {

        /// <summary>
        /// Tests that the remote synchronization tool doesn't do anything when the dry run option is used.
        /// </summary>
        [Test]
        [Category("Tools/RemoteSynchronization")]
        public async Task TestDryRunAsync()
        {
            var l1 = Path.Combine(TARGETFOLDER, "l1");
            var l2 = Path.Combine(TARGETFOLDER, "l2");

            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            await GenerateTestDataAsync(l1, 5, 0, 0, 1024).ConfigureAwait(false);

            var args = new string[] { $"file://{l1}", $"file://{l2}", "--confirm", "--dry-run" };

            var async_call = RemoteSynchronization.Program.MainAsync(args);
            var return_code = await async_call.ConfigureAwait(false);

            Assert.AreEqual(0, return_code, "Remote synchronization tool did not return 0.");
            Assert.IsFalse(DirectoriesAndContentsAreEqual(l1, l2), "Synchronized directories are equal");
            Assert.IsTrue(!Directory.EnumerateFiles(l2).Any(), "Destination directory is not empty");
        }

        /// <summary>
        /// Tests that the remote synchronization tool works with an empty source to an empty destination.
        /// </summary>
        [Test]
        [Category("Tools/RemoteSynchronization")]
        public async Task TestEmptySourceAndDestinationAsync()
        {
            var l1 = Path.Combine(TARGETFOLDER, "empty_src");
            var l2 = Path.Combine(TARGETFOLDER, "l2");

            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            var args = new string[] { $"file://{l1}", $"file://{l2}", "--confirm" };

            var async_call = RemoteSynchronization.Program.MainAsync(args);
            var return_code = await async_call.ConfigureAwait(false);

            Assert.AreEqual(0, return_code, "Remote synchronization tool did not return 0.");
            Assert.IsTrue(DirectoriesAndContentsAreEqual(l1, l2), "Synchronized directories are not equal");
        }

        /// <summary>
        /// Test that remote synchronizing an empty source to a non-empty destination deletes the destination files.
        /// </summary>
        [Test]
        [Category("Tools/RemoteSynchronization")]
        public async Task TestEmptySourceDeletesDestinationAsync()
        {
            var l1 = Path.Combine(TARGETFOLDER, "empty_src");
            var l2 = Path.Combine(TARGETFOLDER, "l2");

            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            await GenerateTestDataAsync(l2, 5, 0, 0, 1024).ConfigureAwait(false);

            var args = new string[] { $"file://{l1}", $"file://{l2}", "--confirm" };

            var async_call = RemoteSynchronization.Program.MainAsync(args);
            var return_code = await async_call.ConfigureAwait(false);

            Assert.AreEqual(0, return_code, "Remote synchronization tool did not return 0.");
            Assert.IsTrue(DirectoriesAndContentsAreEqual(l1, l2), "Synchronized directories are not equal");
        }

        /// <summary>
        /// Test that remote synchronizing an empty source to a non-empty destination renames the destination files when the `--retention` option is used.
        /// </summary>
        [Test]
        [Category("Tools/RemoteSynchronization")]
        public async Task TestEmptySourceRenamesDestinationAsync()
        {
            var l1 = Path.Combine(TARGETFOLDER, "empty_src");
            var l2 = Path.Combine(TARGETFOLDER, "l2");

            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            await GenerateTestDataAsync(l2, 5, 0, 0, 1024).ConfigureAwait(false);

            var filelist = Directory.EnumerateFiles(l2).ToList();
            var files = filelist.Select(x => File.ReadAllBytes(x)).ToList();

            var args = new string[] { $"file://{l1}", $"file://{l2}", "--confirm", "--retention" };

            var async_call = RemoteSynchronization.Program.MainAsync(args);
            var return_code = await async_call.ConfigureAwait(false);

            Assert.AreEqual(0, return_code, "Remote synchronization tool did not return 0.");

            var newfilelist = Directory.EnumerateFiles(l2).ToList();
            foreach (var (name, contents) in filelist.Zip(files))
            {
                var filename = Path.GetFileName(name);
                var newfilename = newfilelist.FirstOrDefault(x => x.EndsWith(filename));
                if (newfilename == null)
                {
                    Assert.Fail($"File {filename} was not renamed in the destination directory.");
                }
                else
                {
                    var newcontents = File.ReadAllBytes(newfilename);
                    Assert.AreEqual(contents, newcontents, "File contents are not equal");
                }
            }
        }

        [Test]
        public void TestFullResultOptionSupportsAliasAndConsoleOutput()
        {
            var option = new Options(new Dictionary<string, string?>()).SupportedCommands.FirstOrDefault(x => x.Name == "full-result");

            Assert.IsNotNull(option, "The full-result option was not registered.");
            Assert.IsTrue(option!.Aliases.Contains("full-results"), "The full-results alias was not registered.");

            using var writer = new StringWriter();
            using var singularConsole = new CommandLine.ConsoleOutput(writer, new Dictionary<string, string> { ["full-result"] = "true" });
            using var pluralConsole = new CommandLine.ConsoleOutput(writer, new Dictionary<string, string> { ["full-results"] = "true" });

            Assert.IsTrue(singularConsole.FullResults, "Console output did not honor the full-result option.");
            Assert.IsTrue(pluralConsole.FullResults, "Console output did not honor the full-results alias.");
        }

        /// <summary>
        /// Tests passing all arguments to the main method of the remote synchronization tool.
        /// </summary>
        [Test]
        [Category("Tools/RemoteSynchronization")]
        public void TestMainMethodParsesArgumentsCorrectly()
        {
            string[][] testCases =
            {
                ["source", "destination", "--parse-arguments-only"],
                ["source", "destination", "--parse-arguments-only", "--auto-create-folders"],
                ["source", "destination", "--parse-arguments-only", "--backend-retries", "5"],
                ["source", "destination", "--parse-arguments-only", "--backend-retry-delay", "1000"],
                ["source", "destination", "--parse-arguments-only", "--backend-retry-with-exponential-backoff"],
                ["source", "destination", "--parse-arguments-only", "--auto-create-folders=false"],
                ["source", "destination", "--parse-arguments-only", "--backend-retry-with-exponential-backoff=false"],
                ["source", "destination", "--parse-arguments-only", "--dry-run"],
                ["source", "destination", "--parse-arguments-only", "--force"],
                ["source", "destination", "--parse-arguments-only", "--dry-run", "--force"],
                ["source", "destination", "--parse-arguments-only", "--verify-contents"],
                ["source", "destination", "--parse-arguments-only", "--verify-get-after-put"],
                ["source", "destination", "--parse-arguments-only", "--retry", "3"],
                ["source", "destination", "--parse-arguments-only", "--log-level", "Debug"],
                ["source", "destination", "--parse-arguments-only", "--log-level", "Information"],
                ["source", "destination", "--parse-arguments-only", "--log-level", "Profiling"],
                ["source", "destination", "--parse-arguments-only", "--log-level", "Verbose"],
                ["source", "destination", "--parse-arguments-only", "--log-file", "somefile.log"],
                ["source", "destination", "--parse-arguments-only", "--progress"],
                ["source", "destination", "--parse-arguments-only", "--retention"],
                ["source", "destination", "--parse-arguments-only", "--confirm"],
                ["source", "destination", "--parse-arguments-only", "--global-options", "someglobalkey=someglobalvalue", "anotherglobalkey=anotherglobalvalue"],
                ["source", "destination", "--parse-arguments-only", "--src-options", "somesrckey=somesrcvalue", "anothersrckey=anothersrcvalue"],
                ["source", "destination", "--parse-arguments-only", "--dst-options", "somedstkey=somedstvalue", "anotherdstkey=anotherdstvalue"],
                [
                    "source", "destination", "--parse-arguments-only",
                    "--dry-run", "--force", "--verify-contents", "--retry", "3", "--log-level", "Debug", "--log-file", "somefile.log", "--progress", "--retention", "--confirm",
                    "--global-options", "someglobalkey=someglobalvalue", "anotherglobalkey=anotherglobalvalue",
                    "--src-options", "somesrckey=somesrcvalue", "anothersrckey=anothersrcvalue",
                    "--dst-options", "somedstkey=somedstvalue", "anotherdstkey=anotherdstvalue"
                ],
                [
                    "source", "destination", "--parse-arguments-only",
                    "--global-options", "somekey=somevalue=with=extra=equals", "anotherkey=anothervalue",
                    "--src-options", "somekey=somevalue=with=extra=equals", "anotherkey=anothervalue",
                    "--dst-options", "somekey=somevalue=with=extra=equals", "anotherkey=anothervalue"
                ],
                [
                    "source", "destination", "--parse-arguments-only",
                    "--global-options", "somekey=\"some value with spaces\"", "anotherkey=anothervalue",
                    "--src-options", "somekey=\"some value with spaces\"", "anotherkey=anothervalue",
                    "--dst-options", "somekey=\"some value with spaces\"", "anotherkey=anothervalue"
                ]
            };

            foreach (var args in testCases)
            {
                int result = RemoteSynchronization.Program.MainAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();
                Assert.AreEqual(0, result, $"Failed for args: {string.Join(" ", args)}");
            }

            int failed_result = RemoteSynchronization.Program.MainAsync(["source", "destination", "--bogus-option"]).ConfigureAwait(false).GetAwaiter().GetResult();
            Assert.AreEqual(1, failed_result, "Invalid option did not return 1");
        }

        /// <summary>
        /// Tests the original inded use of the remote synchronization tool on an empty destination.
        /// </summary>
        [Test]
        [Category("Tools/RemoteSynchronization")]
        public async Task TestRemoteSynchronizationAsync()
        {
            var l1 = Path.Combine(TARGETFOLDER, "l1");
            var l2 = Path.Combine(TARGETFOLDER, "l2");
            var l1r = Path.Combine(RESTOREFOLDER, "l1_restore");
            var l2r = Path.Combine(RESTOREFOLDER, "l2_restore");

            var options = TestOptions;

            var now = DateTime.Now;
            await GenerateTestDataAsync(DATAFOLDER, 5, 2, 2, 1024).ConfigureAwait(false);
            Console.WriteLine($"Generated test data in {DATAFOLDER} in {DateTime.Now - now}");

            // Create the directories if they do not exist
            foreach (var p in new string[] { l1, l2, l1r, l2r })
            {
                if (!SystemIO.IO_OS.DirectoryExists(p))
                    SystemIO.IO_OS.DirectoryCreate(p);
            }

            // Backup the first level
            using (var c = new Controller($"file://{l1}", options, null))
            {
                now = DateTime.Now;
                var results = await c.BackupAsync([DATAFOLDER]).ConfigureAwait(false);
                Assert.AreEqual(0, results.Errors.Count());
                Assert.AreEqual(0, results.Warnings.Count());
                Console.WriteLine($"Backed up {results.AddedFiles} files to {l1} in {DateTime.Now - now}");
            }

            // Call the tool
            now = DateTime.Now;
            var exe = RemoteSynchronization.Program.MainAsync;
            string[] args = [
                $"file://{l1}", $"file://{l2}",
                "--global-options", ..options.Select(x => $"{x.Key}={x.Value}"),
                // Pass along multi token options to test that the parser won't fail
                "--src-options", "ssh-accept-any-fingerprints=true", "ssh-keyfile=/path/to/keyfile",
                "--dst-options", "some-other-key=value", "another-key=value2",
                "--confirm"
            ];
            var async_call = exe(args);
            await async_call.ConfigureAwait(false);
            Assert.AreEqual(0, await async_call, "Remote synchronization tool did not return 0.");
            Console.WriteLine($"Remote synchronization tool returned 0 in {DateTime.Now - now}");

            // Verify that the directories are equal
            Assert.IsTrue(DirectoriesAndContentsAreEqual(l1, l2), "Synchronized directories are not equal");

            // Try to restore the first level
            options["restore-path"] = l1r;
            using (var c = new Controller($"file://{l1}", options, null))
            {
                now = DateTime.Now;
                var results = await c.RestoreAsync([]).ConfigureAwait(false);
                Assert.AreEqual(0, results.Errors.Count());
                Assert.AreEqual(0, results.Warnings.Count());
                Console.WriteLine($"Restored {results.RestoredFiles} files to {options["restore-path"]} in {DateTime.Now - now}");
            }
            Assert.IsTrue(DirectoriesAndContentsAreEqual(DATAFOLDER, l1r), "Restored first level files is not equal to original files");

            // Try to restore the second level
            options["restore-path"] = l2r;
            using (var c = new Controller($"file://{l2}", options, null))
            {
                now = DateTime.Now;
                var results = await c.RestoreAsync([]).ConfigureAwait(false);
                Assert.AreEqual(0, results.Errors.Count());
                Assert.AreEqual(0, results.Warnings.Count());
                Console.WriteLine($"Restored {results.RestoredFiles} files to {options["restore-path"]} in {DateTime.Now - now}");
            }
            Assert.IsTrue(DirectoriesAndContentsAreEqual(DATAFOLDER, l2r), "Restored second level files is not equal to original files");

            // Delete the l2r directory
            SystemIO.IO_OS.DirectoryDelete(l2r, true);

            // Delete one file from the l2 backup
            var files = Directory.EnumerateFiles(l2).ToList();
            File.Delete(files.First());

            // Check that the restore fails
            options["restore-path"] = l2r;
            using (var c = new Controller($"file://{l2}", options, null))
            {
                now = DateTime.Now;
                try
                {
                    var results = await c.RestoreAsync([]).ConfigureAwait(false);
                }
                catch (RemoteListVerificationException)
                {
                    Console.WriteLine($"Failed (as expected) to restore files to {options["restore-path"]} in {DateTime.Now - now}");
                }
            }

            // Run the tool again to copy the missing file
            now = DateTime.Now;
            async_call = exe(args);
            await async_call.ConfigureAwait(false);
            Assert.AreEqual(0, await async_call, "Remote synchronization tool did not return 0.");
            Console.WriteLine($"Remote synchronization tool returned 0 in {DateTime.Now - now}");

            // Try to restore the second level again
            options["restore-path"] = l2r;
            using (var c = new Controller($"file://{l2}", options, null))
            {
                now = DateTime.Now;
                var results = await c.RestoreAsync([]).ConfigureAwait(false);
                Assert.AreEqual(0, results.Errors.Count());
                Assert.AreEqual(0, results.Warnings.Count());
                Console.WriteLine($"Restored {results.RestoredFiles} files to {options["restore-path"]} in {DateTime.Now - now}");
            }

            Assert.IsTrue(DirectoriesAndContentsAreEqual(DATAFOLDER, l2r), "Restored second level files is not equal to original files");

            // Add some more files to the source
            await GenerateTestDataAsync(Path.Combine(DATAFOLDER, "brand_new_files"), 5, 2, 2, 1024).ConfigureAwait(false);

            // Backup the new files to l1
            using (var c = new Controller($"file://{l1}", options, null))
            {
                now = DateTime.Now;
                var results = await c.BackupAsync([DATAFOLDER]).ConfigureAwait(false);
                Assert.AreEqual(0, results.Errors.Count());
                Assert.AreEqual(0, results.Warnings.Count());
                Console.WriteLine($"Backed up {results.AddedFiles} files to {l1} in {DateTime.Now - now}");
            }

            // Run the tool again to copy the new files
            now = DateTime.Now;
            async_call = exe(args);
            await async_call.ConfigureAwait(false);
            Assert.AreEqual(0, await async_call, "Remote synchronization tool did not return 0.");
            Console.WriteLine($"Remote synchronization tool returned 0 in {DateTime.Now - now}");

            // Try to restore the second level again
            options["restore-path"] = l2r;
            using (var c = new Controller($"file://{l2}", options, null))
            {
                now = DateTime.Now;
                var results = await c.RestoreAsync([]).ConfigureAwait(false);
                Assert.AreEqual(0, results.Errors.Count());
                Assert.AreEqual(0, results.Warnings.Count());
                Console.WriteLine($"Restored {results.RestoredFiles} files to {options["restore-path"]} in {DateTime.Now - now}");
            }

            Assert.IsTrue(DirectoriesAndContentsAreEqual(DATAFOLDER, l2r), "Restored second level files is not equal to original files");

            // Delete the l2r directory
            SystemIO.IO_OS.DirectoryDelete(l2r, true);

            // Remove a directory from the source
            SystemIO.IO_OS.DirectoryDelete(Path.Combine(DATAFOLDER, "dir_0"), true);

            // Backup the new files to l1
            using (var c = new Controller($"file://{l1}", options, null))
            {
                now = DateTime.Now;
                var results = await c.BackupAsync([DATAFOLDER]).ConfigureAwait(false);
                Assert.AreEqual(0, results.Errors.Count());
                Assert.AreEqual(0, results.Warnings.Count());
                Console.WriteLine($"Backed up {results.AddedFiles} files to {l1} in {DateTime.Now - now}");
            }

            // Compact the backup
            using (var c = new Controller($"file://{l1}", options, null))
            {
                now = DateTime.Now;
                var results = await c.CompactAsync().ConfigureAwait(false);
                Assert.AreEqual(0, results.Errors.Count());
                Assert.AreEqual(0, results.Warnings.Count());
                Console.WriteLine($"Compacted backup in {DateTime.Now - now}");
            }

            // Run the tool again to copy the new files
            now = DateTime.Now;
            async_call = exe(args);
            var result = await async_call.ConfigureAwait(false);
            Assert.AreEqual(0, result, "Remote synchronization tool did not return 0.");

            // Try to restore the second level again
            options["restore-path"] = l2r;
            using (var c = new Controller($"file://{l2}", options, null))
            {
                now = DateTime.Now;
                var results = await c.RestoreAsync([]).ConfigureAwait(false);
                Assert.AreEqual(0, results.Errors.Count());
                Assert.AreEqual(0, results.Warnings.Count());
                Console.WriteLine($"Restored {results.RestoredFiles} files to {options["restore-path"]} in {DateTime.Now - now}");
            }

            Assert.IsTrue(DirectoriesAndContentsAreEqual(DATAFOLDER, l2r), "Restored second level files is not equal to original files");

            // Delete the l2r directory
            SystemIO.IO_OS.DirectoryDelete(l2r, true);

            // Perform a forced synchronization with retention to check that retention doesn't break a restore
            now = DateTime.Now;
            async_call = exe([.. args, "--force", "--retention"]);
            var res = await async_call.ConfigureAwait(false);
            Assert.AreEqual(0, res, "Remote synchronization tool did not return 0.");

            // Try to restore the second level again
            options["restore-path"] = l2r;
            using (var c = new Controller($"file://{l2}", options, null))
            {
                now = DateTime.Now;
                var results = await c.RestoreAsync([]).ConfigureAwait(false);
                Assert.AreEqual(0, results.Errors.Count());
                Assert.AreEqual(0, results.Warnings.Count());
                Console.WriteLine($"Restored {results.RestoredFiles} files to {options["restore-path"]} in {DateTime.Now - now}");
            }

            Assert.IsTrue(DirectoriesAndContentsAreEqual(DATAFOLDER, l2r), "Restored second level files is not equal to original files");
        }

        [Test]
        [Category("Tools/RemoteSynchronization")]
        [TestCase(true, false, "0")] // Fail first transfer on source.
        [TestCase(true, false, "1,3")] // Fail middle transfers on source.
        [TestCase(true, false, "4")] // Fail last transfer on source.
        [TestCase(true, false, "0,1,2,3,4")] // Fail all transfers on source.
        [TestCase(false, true, "0")] // Fail first transfer on destination.
        [TestCase(false, true, "1,3")] // Fail middle transfers on destination.
        [TestCase(false, true, "4")] // Fail last transfer on destination.
        [TestCase(false, true, "0,1,2,3,4")] // Fail all transfers on destination.
        public async Task TestRemoteSynchronizationWithFaultyBackendAsync(bool failSource, bool failDest, string failIndices)
        {
            var expect_to_fail = failIndices == "0,1,2,3,4";

            var l1 = Path.Combine(TARGETFOLDER, "l1");
            var l2 = Path.Combine(TARGETFOLDER, "l2");
            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            int backendCount = 0;
            var failIdxs = failIndices.Split(',').Select(int.Parse).ToList();
            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
            {
                if (action == DeterministicErrorBackend.BackendAction.GetBefore || action == DeterministicErrorBackend.BackendAction.PutBefore)
                {
                    var currentIdx = backendCount;
                    if (failIdxs.Count > 0 && currentIdx == failIdxs.First())
                    {
                        failIdxs.RemoveAt(0);
                        return true; // Simulate failure
                    }
                    else
                    {
                        backendCount++;
                    }
                }

                return false; // No failure
            };

            await GenerateTestDataAsync(l1, 5, 0, 0, 1024).ConfigureAwait(false);

            // Setup backend URLs with failure injection
            var protocol = new DeterministicErrorBackend().ProtocolKey;
            string srcUrl = failSource ? $"{protocol}://{l1}" : $"file://{l1}";
            string dstUrl = failDest ? $"{protocol}://{l2}" : $"file://{l2}";

            var args = new string[] {
                srcUrl, dstUrl,
                "--confirm",
                "--backend-retries", expect_to_fail ? "0" : "5",
                "--backend-retry-delay", "10",
                "--retry", "0" // This test only tests the LightWeightBackendManager's retry logic.
            };

            // Redirect standard error to a buffer if we expect to fail. If it doesn't fail, we can omit it.
            var originalError = Console.Error;
            StringWriter? errorBuffer = null;
            if (expect_to_fail)
            {
                errorBuffer = new StringWriter();
                Console.SetError(errorBuffer);
            }

            var async_call = RemoteSynchronization.Program.MainAsync(args);
            var return_code = await async_call.ConfigureAwait(false);

            // Expect nonzero return code if any backend fails less than the number of retries
            if (expect_to_fail)
            {
                try
                {
                    Assert.AreNotEqual(0, return_code, "Expected failure due to all transfers failing.");
                    Assert.IsFalse(DirectoriesAndContentsAreEqual(l1, l2), "Synchronized directories should not be equal due to failures.");
                }
                catch
                {
                    Console.SetError(originalError); // Restore standard error
                    await Console.Error.WriteLineAsync(errorBuffer?.ToString());
                    throw;
                }
            }
            else
            {
                Assert.AreEqual(0, return_code, "Expected success.");
                Assert.IsTrue(DirectoriesAndContentsAreEqual(l1, l2), "Synchronized directories are not equal.");
            }
        }

        /// <summary>
        /// Tests that the remote synchronization tool verifies the contents of the files.
        /// </summary>
        [Test]
        [Category("Tools/RemoteSynchronization")]
        public async Task TestVerifiesAsync()
        {
            var l1 = Path.Combine(TARGETFOLDER, "l1");
            var l2 = Path.Combine(TARGETFOLDER, "l2");

            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            await GenerateTestDataAsync(l1, 5, 0, 0, 1024).ConfigureAwait(false);

            var filenames = Directory.EnumerateFiles(l1).Take(2).ToList();
            var first_file = filenames.First();
            var second_file = filenames.Skip(1).First();

            File.Copy(first_file, Path.Combine(l2, Path.GetFileName(first_file)));
            File.Copy(second_file, Path.Combine(l2, Path.GetFileName(second_file)));

            // Touch the first file to give it a different timestamp
            File.SetLastWriteTime(first_file, DateTime.Now.AddMinutes(1));

            // Modify the second file to give it different contents, but the same size
            var second_file_contents = File.ReadAllBytes(second_file);
            second_file_contents[0] = (byte)(second_file_contents[0] + 1);
            File.WriteAllBytes(second_file, second_file_contents);

            var args = new string[] { $"file://{l1}", $"file://{l2}", "--confirm", "--verify-contents", "--verify-get-after-put" };

            var async_call = RemoteSynchronization.Program.MainAsync(args);
            var return_code = await async_call.ConfigureAwait(false);

            Assert.AreEqual(0, return_code, "Remote synchronization tool did not return 0.");
            Assert.IsTrue(DirectoriesAndContentsAreEqual(l1, l2), "Synchronized directories are not equal");
        }

        //
        // Helper methods
        //

        /// <summary>
        /// Compares two directories and their contents.
        /// </summary>
        /// <param name="d1">The first directory.</param>
        /// <param name="d2">The second directory.</param>
        /// <returns>`true` if the two directories contain exactly the same files and if the contents of the files are equivalent. `false` otherwise.</returns>
        public static bool DirectoriesAndContentsAreEqual(string d1, string d2)
        {
            // Recursively get the files in the two directories
            var f1s = Directory.EnumerateFiles(d1, "*", SearchOption.AllDirectories).Select(x => x[(d1.Length + 1)..]).OrderDescending();
            var f2s = Directory.EnumerateFiles(d2, "*", SearchOption.AllDirectories).Select(x => x[(d2.Length + 1)..]).OrderDescending();

            // If the two directories do not contain the same files, return false
            if (!f1s.SequenceEqual(f2s))
                return false;

            // Check that each file pair exist and have the same content
            foreach (var f1 in f1s)
            {
                var f1full = Path.Combine(d1, f1);
                if (!File.Exists(f1full))
                    return false;

                var f2full = Path.Combine(d2, f1);
                if (!File.Exists(f2full))
                    return false;

                var c1 = File.ReadAllText(f1full);
                var c2 = File.ReadAllText(f2full);

                if (!c1.SequenceEqual(c2))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Generates test data in the specified directory.
        /// </summary>
        /// <param name="dir">The directory to fill with the generated data.</param>
        /// <param name="n_files">How many files the directory should have.</param>
        /// <param name="n_dirs">How many subdirectories the directory should have.</param>
        /// <param name="n_levels">How deep the number of subdirectories within subdirectories should go.</param>
        /// <param name="max_file_size">The maximum size of the files to generate.</param>
        public static async Task GenerateTestDataAsync(string dir, int n_files, int n_dirs, int n_levels, int max_file_size)
        {
            if (!SystemIO.IO_OS.DirectoryExists(dir))
                SystemIO.IO_OS.DirectoryCreate(dir);

            var fs = Enumerable.Range(0, n_files)
                .Select(i => GenerateTestFileAsync(dir, i, max_file_size));
            var ds = n_levels > 0 ?
                Enumerable.Range(0, n_dirs)
                    .Select(i =>
                    {
                        var subdir = Path.Combine(dir, $"dir_{i}");
                        return GenerateTestDataAsync(subdir, n_files, n_dirs, n_levels - 1, max_file_size);
                    })
                : [];

            await Task.WhenAll([.. fs, .. ds]);
        }

        public static async Task GenerateTestFileAsync(string dir, int i, int max_file_size)
        {
            var rnd = new Random();
            var file = Path.Combine(dir, $"file_{i}.txt");
            var size = rnd.Next(1, max_file_size);
            var data = new byte[size];
            rnd.NextBytes(data);
            await File.WriteAllBytesAsync(file, data);
        }

        /// <summary>
        /// A test backend that does not know what the destination has room for, which is what a
        /// negative quota means. A Google Drive account whose "about" answer carries no quota
        /// fields reports exactly this.
        /// </summary>
        public class FileBackendWithUnknownQuota : Library.Backend.File
        {
            public FileBackendWithUnknownQuota() { }

            public FileBackendWithUnknownQuota(string url, Dictionary<string, string?> options) : base(url.Replace("unknownquotatest://", "file://"), options) { }

            public override string ProtocolKey => "unknownquotatest";

            public override Task<IQuotaInfo?> GetQuotaInfoAsync(CancellationToken token)
            {
                return Task.FromResult<IQuotaInfo?>(new QuotaInfo(-1, -1));
            }
        }

        /// <summary>
        /// A test backend that simulates a quota of 1KB for testing quota checks in the remote synchronization tool.
        /// </summary>
        public class FileBackendWith1Kb : Library.Backend.File
        {
            public FileBackendWith1Kb() { }

            public FileBackendWith1Kb(string url, Dictionary<string, string?> options) : base(url.Replace("quotatest://", "file://"), options) { }

            public override string ProtocolKey => "quotatest";

            public override Task<IQuotaInfo?> GetQuotaInfoAsync(CancellationToken token)
            {
                return Task.FromResult<IQuotaInfo?>(new QuotaInfo(1024 * 1024, 1024));
            }
        }

        /// <summary>
        /// Tests that the remote synchronization tool checks quota and fails if insufficient.
        /// </summary>
        [Test]
        [Category("Tools/RemoteSynchronization")]
        public async Task TestRemoteSynchronizationQuotaCheckFailsAsync()
        {
            var l1 = Path.Combine(TARGETFOLDER, "quota_src");
            var l2 = Path.Combine(TARGETFOLDER, "quota_dst");

            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            // Generate test data: 5 files of ~1KB each, total ~5KB
            await GenerateTestDataAsync(l1, 5, 0, 0, 2048).ConfigureAwait(false);

            // Register the test backend
            Library.DynamicLoader.BackendLoader.AddBackend(new FileBackendWith1Kb());

            // Use the test backend for destination with only 1KB free quota
            var args = new string[] { $"file://{l1}", $"quotatest://{l2}", "--confirm" };

            var async_call = RemoteSynchronization.Program.MainAsync(args);
            var return_code = await async_call.ConfigureAwait(false);

            Assert.AreNotEqual(0, return_code, "Remote synchronization should fail due to insufficient quota.");
        }

        /// <summary>
        /// Tests that the remote synchronization tool succeeds when quota is sufficient.
        /// </summary>
        [Test]
        [Category("Tools/RemoteSynchronization")]
        public async Task TestRemoteSynchronizationQuotaCheckSucceedsAsync()
        {
            var l1 = Path.Combine(TARGETFOLDER, "quota_src2");
            var l2 = Path.Combine(TARGETFOLDER, "quota_dst2");

            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            // Generate small test data: 1 file of ~500B
            await GenerateTestDataAsync(l1, 1, 0, 0, 512).ConfigureAwait(false);

            // Register the test backend
            Library.DynamicLoader.BackendLoader.AddBackend(new FileBackendWith1Kb());

            // Use the test backend for destination with 1KB free quota (sufficient)
            var args = new string[] { $"file://{l1}", $"quotatest://{l2}", "--confirm", "--dst-options", "freequota=1024" };

            var async_call = RemoteSynchronization.Program.MainAsync(args);
            var return_code = await async_call.ConfigureAwait(false);

            Assert.AreEqual(0, return_code, "Remote synchronization should succeed with sufficient quota.");
            Assert.IsTrue(DirectoriesAndContentsAreEqual(l1, l2), "Directories should be synchronized.");
        }

        /// <summary>
        /// Tests that a destination which does not report a quota is not read as a full one. A
        /// negative free space is how a backend says it does not know, which is not the same as
        /// saying there is no room, and the tool has nothing to compare against.
        /// </summary>
        [Test]
        [Category("Tools/RemoteSynchronization")]
        public async Task TestRemoteSynchronizationUnknownQuotaIsNotTreatedAsFullAsync()
        {
            var l1 = Path.Combine(TARGETFOLDER, "quota_src3");
            var l2 = Path.Combine(TARGETFOLDER, "quota_dst3");

            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            // Enough data that the required size is comfortably positive, so an unknown quota
            // read as a number would lose the comparison
            await GenerateTestDataAsync(l1, 5, 0, 0, 2048).ConfigureAwait(false);

            // Register the test backend
            Library.DynamicLoader.BackendLoader.AddBackend(new FileBackendWithUnknownQuota());

            var args = new string[] { $"file://{l1}", $"unknownquotatest://{l2}", "--confirm" };

            var async_call = RemoteSynchronization.Program.MainAsync(args);
            var return_code = await async_call.ConfigureAwait(false);

            Assert.AreEqual(0, return_code, "Remote synchronization should not stop for a destination that reports no quota.");
            Assert.IsTrue(DirectoriesAndContentsAreEqual(l1, l2), "Directories should be synchronized.");
        }

        /// <summary>
        /// A test backend that reports the same remote name more than once, which is what a broken
        /// backend module - or a remote holding two objects under one name - looks like to the
        /// synchronization tool. The mode is read from a backend option rather than a static, so a
        /// single run can have a broken source and an intact destination, or the other way round.
        /// </summary>
        public class DuplicateListingBackend : IBackend, IStreamingBackend
        {
            /// <summary>
            /// The protocol key this backend is registered under.
            /// </summary>
            public const string Key = "duplistingtest";

            /// <summary>
            /// The option that selects how the listing repeats itself: "exact" repeats the name as
            /// it is, "case" repeats it in upper case.
            /// </summary>
            public const string ModeOption = "duplicate-listing";

            /// <summary>
            /// The backend the listing is taken from.
            /// </summary>
            private readonly IStreamingBackend? m_backend;

            /// <summary>
            /// The mode read from <see cref="ModeOption"/>.
            /// </summary>
            private readonly string m_mode = "none";

            public DuplicateListingBackend() { }

            public DuplicateListingBackend(string url, Dictionary<string, string> options)
            {
                m_backend = (IStreamingBackend)Library.DynamicLoader.BackendLoader.GetBackend(url.Replace($"{Key}://", "file://"), options);
                if (options.TryGetValue(ModeOption, out var mode) && !string.IsNullOrWhiteSpace(mode))
                    m_mode = mode;
            }

            public string DisplayName => "Duplicate listing test backend";

            public string ProtocolKey => Key;

            public string Description => "A testing backend that reports the same remote name twice";

            public bool SupportsStreaming => m_backend?.SupportsStreaming ?? false;

            public IList<ICommandLineArgument> SupportedCommands
                => m_backend?.SupportedCommands ?? Library.DynamicLoader.BackendLoader.GetSupportedCommands("file://").ToList();

            public async IAsyncEnumerable<IFileEntry> ListAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancelToken)
            {
                await foreach (var entry in m_backend!.ListAsync(cancelToken).ConfigureAwait(false))
                {
                    yield return entry;

                    // The repeat is made up instead of read back from the filesystem, so a case
                    // difference shows up where filenames are not case sensitive as well
                    if (m_mode == "exact")
                        yield return entry;
                    else if (m_mode == "case")
                        yield return new FileEntry(entry.Name.ToUpperInvariant(), entry.Size, entry.LastAccess, entry.LastModification, entry.IsFolder, entry.IsArchived);
                }
            }

            public Task CreateFolderAsync(CancellationToken cancelToken) => m_backend!.CreateFolderAsync(cancelToken);
            public Task DeleteAsync(string remotename, CancellationToken cancelToken) => m_backend!.DeleteAsync(remotename, cancelToken);
            public Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken) => m_backend!.GetAsync(remotename, stream, cancelToken);
            public Task GetAsync(string remotename, string filename, CancellationToken cancelToken) => m_backend!.GetAsync(remotename, filename, cancelToken);
            public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => m_backend!.GetDNSNamesAsync(cancelToken);
            public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken) => m_backend!.PutAsync(remotename, stream, cancelToken);
            public Task PutAsync(string remotename, string filename, CancellationToken cancelToken) => m_backend!.PutAsync(remotename, filename, cancelToken);
            public Task TestAsync(bool alsoWrite, CancellationToken cancelToken) => m_backend!.TestAsync(alsoWrite, cancelToken);
            public void Dispose() => m_backend?.Dispose();
        }

        /// <summary>
        /// Builds a configuration for the synchronization runner with the defaults the commandline
        /// parser applies, so a test only has to state what it cares about. The runs are dry, as
        /// the listings are read before anything is transferred.
        /// </summary>
        private static Library.Main.Operation.RemoteSynchronization.RemoteSynchronizationConfig SyncConfig(string src, string dst, List<string>? srcOptions = null, List<string>? dstOptions = null, bool force = false)
            => new Library.Main.Operation.RemoteSynchronization.RemoteSynchronizationConfig(
                Src: src,
                Dst: dst,
                AutoCreateFolders: true,
                BackendRetries: 3,
                BackendRetryDelay: 1000,
                BackendRetryWithExponentialBackoff: true,
                Confirm: true,
                DryRun: true,
                DstOptions: dstOptions ?? [],
                Force: force,
                GlobalOptions: [],
                LogFile: "",
                LogLevel: "Information",
                ParseArgumentsOnly: false,
                Progress: false,
                Retention: false,
                Retry: 3,
                SrcOptions: srcOptions ?? [],
                VerifyContents: false,
                VerifyGetAfterPut: false);

        /// <summary>
        /// Runs the synchronization the way the server does, rather than through the commandline
        /// entry point, which turns every exception into an exit code.
        /// </summary>
        private static Task<int> RunSyncAsync(Library.Main.Operation.RemoteSynchronization.RemoteSynchronizationConfig config)
            => Library.Main.Operation.RemoteSynchronization.RemoteSynchronizationRunner.RunAsync(config, CancellationToken.None);

        /// <summary>
        /// A destination that reports the same name twice cannot be told apart by name, and a name
        /// is all this tool has to work with. It has to say so the way the backup path does, rather
        /// than fail with the dictionary exception that reached the user in issue #7285.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("Tools/RemoteSynchronization")]
        public async Task TestRemoteSynchronizationDuplicateDestinationListingIsReportedAsync(bool force)
        {
            var l1 = Path.Combine(TARGETFOLDER, "dupdst_src");
            var l2 = Path.Combine(TARGETFOLDER, "dupdst_dst");

            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            await GenerateTestDataAsync(l1, 3, 0, 0, 1024).ConfigureAwait(false);

            // The destination needs a file of its own, so there is something to report twice
            await GenerateTestDataAsync(l2, 1, 0, 0, 1024).ConfigureAwait(false);

            Library.DynamicLoader.BackendLoader.AddBackend(new DuplicateListingBackend());

            var config = SyncConfig($"file://{l1}", $"{DuplicateListingBackend.Key}://{l2}",
                dstOptions: [$"{DuplicateListingBackend.ModeOption}=exact"], force: force);

            var error = Assert.ThrowsAsync<RemoteListVerificationException>(async () => await RunSyncAsync(config).ConfigureAwait(false));

            Assert.AreEqual("DuplicateRemoteFiles", error!.HelpID);
            Assert.IsTrue(error.Message.Contains("destination"), $"The error did not say which side reported the duplicates: {error.Message}");
        }

        /// <summary>
        /// The same for the source, with an empty destination. That takes the shortcut which never
        /// builds the lookups, so the duplicates are only found if the listing itself is checked.
        /// </summary>
        [Test]
        [Category("Tools/RemoteSynchronization")]
        public async Task TestRemoteSynchronizationDuplicateSourceListingIsReportedAsync()
        {
            var l1 = Path.Combine(TARGETFOLDER, "dupsrc_src");
            var l2 = Path.Combine(TARGETFOLDER, "dupsrc_dst");

            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            await GenerateTestDataAsync(l1, 3, 0, 0, 1024).ConfigureAwait(false);

            Library.DynamicLoader.BackendLoader.AddBackend(new DuplicateListingBackend());

            var config = SyncConfig($"{DuplicateListingBackend.Key}://{l1}", $"file://{l2}",
                srcOptions: [$"{DuplicateListingBackend.ModeOption}=exact"]);

            var error = Assert.ThrowsAsync<RemoteListVerificationException>(async () => await RunSyncAsync(config).ConfigureAwait(false));

            Assert.AreEqual("DuplicateRemoteFiles", error!.HelpID);
            Assert.IsTrue(error.Message.Contains("source"), $"The error did not say which side reported the duplicates: {error.Message}");
        }

        /// <summary>
        /// Two names that differ only in case are duplicates on a remote that does not tell them
        /// apart, and two separate files anywhere else. The answer follows --case-insensitive-remote,
        /// which is what the backup path does with the same question, and the default is unchanged.
        /// </summary>
        [TestCase("src", true)]
        [TestCase("dst", true)]
        [TestCase("none", false)]
        [Category("Tools/RemoteSynchronization")]
        public async Task TestRemoteSynchronizationCaseOnlyDuplicatesFollowCaseInsensitiveRemoteAsync(string optionSide, bool expectFailure)
        {
            var l1 = Path.Combine(TARGETFOLDER, $"dupcase_src_{optionSide}");
            var l2 = Path.Combine(TARGETFOLDER, $"dupcase_dst_{optionSide}");

            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            // The source is left empty, so the run does not depend on any transfer
            await GenerateTestDataAsync(l2, 3, 0, 0, 1024).ConfigureAwait(false);

            Library.DynamicLoader.BackendLoader.AddBackend(new DuplicateListingBackend());

            var srcOptions = new List<string>();
            var dstOptions = new List<string> { $"{DuplicateListingBackend.ModeOption}=case" };
            if (optionSide == "src")
                srcOptions.Add("case-insensitive-remote=true");
            else if (optionSide == "dst")
                dstOptions.Add("case-insensitive-remote=true");

            var config = SyncConfig($"file://{l1}", $"{DuplicateListingBackend.Key}://{l2}", srcOptions, dstOptions);

            if (expectFailure)
            {
                var error = Assert.ThrowsAsync<RemoteListVerificationException>(async () => await RunSyncAsync(config).ConfigureAwait(false));
                Assert.AreEqual("DuplicateRemoteFiles", error!.HelpID);
            }
            else
            {
                Assert.AreEqual(0, await RunSyncAsync(config).ConfigureAwait(false),
                    "Names that differ in case are two files unless the remote is said to be case insensitive.");
            }
        }

        /// <summary>
        /// Tests that a missing destination folder is created when the tool is asked to. The
        /// exponential backoff flag is deliberately set to the opposite value: if the two are ever
        /// bound to the wrong constructor parameters again, this test fails.
        /// </summary>
        [Test]
        [Category("Tools/RemoteSynchronization")]
        public async Task TestAutoCreateFoldersCreatesMissingDestinationAsync()
        {
            var l1 = Path.Combine(TARGETFOLDER, "autocreate_src");
            var l2 = Path.Combine(TARGETFOLDER, "autocreate_dst");

            Directory.CreateDirectory(l1);
            await GenerateTestDataAsync(l1, 5, 0, 0, 1024).ConfigureAwait(false);

            Assert.IsFalse(Directory.Exists(l2), "The destination folder must not exist before the run.");

            // A retry delay of zero keeps the run instant if the folder is not created after all
            var args = new string[] {
                $"file://{l1}", $"file://{l2}", "--confirm",
                "--auto-create-folders=true",
                "--backend-retry-with-exponential-backoff=false",
                "--backend-retry-delay", "0"
            };

            var async_call = RemoteSynchronization.Program.MainAsync(args);
            var return_code = await async_call.ConfigureAwait(false);

            Assert.AreEqual(0, return_code, "Remote synchronization tool did not return 0.");
            Assert.IsTrue(Directory.Exists(l2), "The destination folder was not created.");
            Assert.IsTrue(DirectoriesAndContentsAreEqual(l1, l2), "Synchronized directories are not equal");
        }

        /// <summary>
        /// The other half: a missing destination folder is left alone when the tool is told not to
        /// create it, whatever the exponential backoff flag says.
        /// </summary>
        [Test]
        [Category("Tools/RemoteSynchronization")]
        public async Task TestAutoCreateFoldersDisabledLeavesMissingDestinationAsync()
        {
            var l1 = Path.Combine(TARGETFOLDER, "noautocreate_src");
            var l2 = Path.Combine(TARGETFOLDER, "noautocreate_dst");

            Directory.CreateDirectory(l1);
            await GenerateTestDataAsync(l1, 5, 0, 0, 1024).ConfigureAwait(false);

            Assert.IsFalse(Directory.Exists(l2), "The destination folder must not exist before the run.");

            var args = new string[] {
                $"file://{l1}", $"file://{l2}", "--confirm",
                "--auto-create-folders=false",
                "--backend-retry-with-exponential-backoff=true",
                "--backend-retry-delay", "0"
            };

            var async_call = RemoteSynchronization.Program.MainAsync(args);
            var return_code = await async_call.ConfigureAwait(false);

            Assert.AreNotEqual(0, return_code, "Remote synchronization should fail when the destination folder is missing and it is not allowed to create it.");
            Assert.IsFalse(Directory.Exists(l2), "The destination folder was created although it was not allowed.");
        }

        /// <summary>
        /// A missing source is not the same as an empty source. Creating it would make the listing
        /// come back empty, and an empty source means every file in the destination is deleted.
        /// </summary>
        [Test]
        [Category("Tools/RemoteSynchronization")]
        public async Task TestMissingSourceIsNotCreatedAndDestinationSurvivesAsync()
        {
            var l1 = Path.Combine(TARGETFOLDER, "missingsrc_src");
            var l2 = Path.Combine(TARGETFOLDER, "missingsrc_dst");

            Directory.CreateDirectory(l2);
            await GenerateTestDataAsync(l2, 5, 0, 0, 1024).ConfigureAwait(false);
            var before = Directory.GetFiles(l2).Length;

            Assert.IsFalse(Directory.Exists(l1), "The source folder must not exist before the run.");

            var args = new string[] {
                $"file://{l1}", $"file://{l2}", "--confirm",
                "--backend-retry-delay", "0"
            };

            var async_call = RemoteSynchronization.Program.MainAsync(args);
            var return_code = await async_call.ConfigureAwait(false);

            Assert.AreEqual(before, Directory.GetFiles(l2).Length, "The destination was emptied because a created source folder was read as an empty source.");
            Assert.IsFalse(Directory.Exists(l1), "The source folder was created, which turns a missing source into an empty one.");
            Assert.AreNotEqual(0, return_code, "Remote synchronization should fail when the source folder is missing.");
        }

    }
}