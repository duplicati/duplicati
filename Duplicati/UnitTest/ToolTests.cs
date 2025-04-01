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

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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
        [Category("Tools")]
        public void TestDryRun()
        {
            var l1 = Path.Combine(TARGETFOLDER, "l1");
            var l2 = Path.Combine(TARGETFOLDER, "l2");

            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            GenerateTestData(l1, 5, 0, 0, 1024).Wait();

            var args = new string[] { $"file://{l1}", $"file://{l2}", "--confirm", "--dry-run" };

            var async_call = RemoteSynchronization.Program.Main(args);
            var return_code = async_call.ConfigureAwait(false).GetAwaiter().GetResult();

            Assert.AreEqual(0, return_code, "Remote synchronization tool did not return 0.");
            Assert.IsFalse(DirectoriesAndContentsAreEqual(l1, l2), "Synchronized directories are equal");
            Assert.IsTrue(!Directory.EnumerateFiles(l2).Any(), "Destination directory is not empty");
        }

        /// <summary>
        /// Tests that the remote synchronization tool works with an empty source to an empty destination.
        /// </summary>
        [Test]
        [Category("Tools")]
        public void TestEmptySourceAndDestination()
        {
            var l1 = Path.Combine(TARGETFOLDER, "empty_src");
            var l2 = Path.Combine(TARGETFOLDER, "l2");

            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            var args = new string[] { $"file://{l1}", $"file://{l2}", "--confirm" };

            var async_call = RemoteSynchronization.Program.Main(args);
            var return_code = async_call.ConfigureAwait(false).GetAwaiter().GetResult();

            Assert.AreEqual(0, return_code, "Remote synchronization tool did not return 0.");
            Assert.IsTrue(DirectoriesAndContentsAreEqual(l1, l2), "Synchronized directories are not equal");
        }

        /// <summary>
        /// Test that remote synchronizing an empty source to a non-empty destination deletes the destination files.
        /// </summary>
        [Test]
        [Category("Tools")]
        public void TestEmptySourceDeletesDestination()
        {
            var l1 = Path.Combine(TARGETFOLDER, "empty_src");
            var l2 = Path.Combine(TARGETFOLDER, "l2");

            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            GenerateTestData(l2, 5, 0, 0, 1024).Wait();

            var args = new string[] { $"file://{l1}", $"file://{l2}", "--confirm" };

            var async_call = RemoteSynchronization.Program.Main(args);
            var return_code = async_call.ConfigureAwait(false).GetAwaiter().GetResult();

            Assert.AreEqual(0, return_code, "Remote synchronization tool did not return 0.");
            Assert.IsTrue(DirectoriesAndContentsAreEqual(l1, l2), "Synchronized directories are not equal");
        }

        /// <summary>
        /// Test that remote synchronizing an empty source to a non-empty destination renames the destination files when the `--retention` option is used.
        /// </summary>
        [Test]
        [Category("Tools")]
        public void TestEmptySourceRenamesDestination()
        {
            var l1 = Path.Combine(TARGETFOLDER, "empty_src");
            var l2 = Path.Combine(TARGETFOLDER, "l2");

            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            GenerateTestData(l2, 5, 0, 0, 1024).Wait();

            var filelist = Directory.EnumerateFiles(l2).ToList();
            var files = filelist.Select(x => File.ReadAllBytes(x)).ToList();

            var args = new string[] { $"file://{l1}", $"file://{l2}", "--confirm", "--retention" };

            var async_call = RemoteSynchronization.Program.Main(args);
            var return_code = async_call.ConfigureAwait(false).GetAwaiter().GetResult();

            Assert.AreEqual(0, return_code, "Remote synchronization tool did not return 0.");

            var newfilelist = Directory.EnumerateFiles(l2).ToList();
            foreach (var (name, contents) in filelist.Zip(files))
            {
                var filename = Path.GetFileName(name);
                var newcontents = File.ReadAllBytes(newfilelist.FirstOrDefault(x => x.EndsWith(filename)));
                Assert.AreEqual(contents, newcontents, "File contents are not equal");
            }
        }

        /// <summary>
        /// Tests passing all arguments to the main method of the remote synchronization tool.
        /// </summary>
        [Test]
        public void TestMainMethodParsesArgumentsCorrectly()
        {
            string[][] testCases =
            {
                ["source", "destination", "--parse-arguments-only"],
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
            };

            foreach (var args in testCases)
            {
                int result = RemoteSynchronization.Program.Main(args).ConfigureAwait(false).GetAwaiter().GetResult();
                Assert.AreEqual(0, result, $"Failed for args: {string.Join(" ", args)}");
            }

            int failed_result = RemoteSynchronization.Program.Main(["source", "destination", "--bogus-option"]).ConfigureAwait(false).GetAwaiter().GetResult();
            Assert.AreEqual(1, failed_result, "Invalid option did not return 1");
        }

        /// <summary>
        /// Tests the original inded use of the remote synchronization tool on an empty destination.
        /// </summary>
        [Test]
        [Category("Tools")]
        public void TestRemoteSynchronization()
        {
            var l1 = Path.Combine(TARGETFOLDER, "l1");
            var l2 = Path.Combine(TARGETFOLDER, "l2");
            var l1r = Path.Combine(RESTOREFOLDER, "l1_restore");
            var l2r = Path.Combine(RESTOREFOLDER, "l2_restore");

            var options = TestOptions;

            var now = DateTime.Now;
            GenerateTestData(DATAFOLDER, 10, 3, 3, 1024 * 1024).Wait();
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
                var results = c.Backup([DATAFOLDER]);
                Assert.AreEqual(0, results.Errors.Count());
                Assert.AreEqual(0, results.Warnings.Count());
                Console.WriteLine($"Backed up {results.AddedFiles} files to {l1} in {DateTime.Now - now}");
            }

            // Call the tool
            now = DateTime.Now;
            var exe = RemoteSynchronization.Program.Main;
            string[] args = [
                $"file://{l1}", $"file://{l2}",
                "--global-options", ..options.Select(x => $"{x.Key}={x.Value}"),
                // Pass along multi token options to test that the parser won't fail
                "--src-options", "ssh-accept-any-fingerprints=true", "ssh-keyfile=/path/to/keyfile",
                "--dst-options", "some-other-key=value", "another-key=value2",
                "--confirm"
            ];
            var async_call = exe(args);
            async_call.Wait();
            Assert.AreEqual(0, async_call.Result, "Remote synchronization tool did not return 0.");
            Console.WriteLine($"Remote synchronization tool returned 0 in {DateTime.Now - now}");

            // Verify that the directories are equal
            Assert.IsTrue(DirectoriesAndContentsAreEqual(l1, l2), "Synchronized directories are not equal");

            // Try to restore the first level
            options["restore-path"] = l1r;
            using (var c = new Controller($"file://{l1}", options, null))
            {
                now = DateTime.Now;
                var results = c.Restore([Path.Combine(DATAFOLDER, "*")]);
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
                var results = c.Restore([]);
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
                    var results = c.Restore([]);
                }
                catch (RemoteListVerificationException)
                {
                    Console.WriteLine($"Failed (as expected) to restore files to {options["restore-path"]} in {DateTime.Now - now}");
                }
            }

            // Run the tool again to copy the missing file
            now = DateTime.Now;
            async_call = exe(args);
            async_call.Wait();
            Assert.AreEqual(0, async_call.Result, "Remote synchronization tool did not return 0.");
            Console.WriteLine($"Remote synchronization tool returned 0 in {DateTime.Now - now}");

            // Try to restore the second level again
            options["restore-path"] = l2r;
            using (var c = new Controller($"file://{l2}", options, null))
            {
                now = DateTime.Now;
                var results = c.Restore([]);
                Assert.AreEqual(0, results.Errors.Count());
                Assert.AreEqual(0, results.Warnings.Count());
                Console.WriteLine($"Restored {results.RestoredFiles} files to {options["restore-path"]} in {DateTime.Now - now}");
            }

            Assert.IsTrue(DirectoriesAndContentsAreEqual(DATAFOLDER, l2r), "Restored second level files is not equal to original files");

            // Add some more files to the source
            GenerateTestData(Path.Combine(DATAFOLDER, "brand_new_files"), 5, 2, 2, 1024 * 1024).Wait();

            // Backup the new files to l1
            using (var c = new Controller($"file://{l1}", options, null))
            {
                now = DateTime.Now;
                var results = c.Backup([DATAFOLDER]);
                Assert.AreEqual(0, results.Errors.Count());
                Assert.AreEqual(0, results.Warnings.Count());
                Console.WriteLine($"Backed up {results.AddedFiles} files to {l1} in {DateTime.Now - now}");
            }

            // Run the tool again to copy the new files
            now = DateTime.Now;
            async_call = exe(args);
            async_call.Wait();
            Assert.AreEqual(0, async_call.Result, "Remote synchronization tool did not return 0.");
            Console.WriteLine($"Remote synchronization tool returned 0 in {DateTime.Now - now}");

            // Try to restore the second level again
            options["restore-path"] = l2r;
            using (var c = new Controller($"file://{l2}", options, null))
            {
                now = DateTime.Now;
                var results = c.Restore([]);
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
                var results = c.Backup([DATAFOLDER]);
                Assert.AreEqual(0, results.Errors.Count());
                Assert.AreEqual(0, results.Warnings.Count());
                Console.WriteLine($"Backed up {results.AddedFiles} files to {l1} in {DateTime.Now - now}");
            }

            // Compact the backup
            using (var c = new Controller($"file://{l1}", options, null))
            {
                now = DateTime.Now;
                var results = c.Compact();
                Assert.AreEqual(0, results.Errors.Count());
                Assert.AreEqual(0, results.Warnings.Count());
                Console.WriteLine($"Compacted backup in {DateTime.Now - now}");
            }

            // Run the tool again to copy the new files
            now = DateTime.Now;
            async_call = exe(args);
            async_call.Wait();
            Assert.AreEqual(0, async_call.Result, "Remote synchronization tool did not return 0.");

            // Try to restore the second level again
            options["restore-path"] = l2r;
            using (var c = new Controller($"file://{l2}", options, null))
            {
                now = DateTime.Now;
                var results = c.Restore([]);
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
            async_call.Wait();
            Assert.AreEqual(0, async_call.Result, "Remote synchronization tool did not return 0.");

            // Try to restore the second level again
            options["restore-path"] = l2r;
            using (var c = new Controller($"file://{l2}", options, null))
            {
                now = DateTime.Now;
                var results = c.Restore([]);
                Assert.AreEqual(0, results.Errors.Count());
                Assert.AreEqual(0, results.Warnings.Count());
                Console.WriteLine($"Restored {results.RestoredFiles} files to {options["restore-path"]} in {DateTime.Now - now}");
            }

            Assert.IsTrue(DirectoriesAndContentsAreEqual(DATAFOLDER, l2r), "Restored second level files is not equal to original files");
        }

        /// <summary>
        /// Tests that the remote synchronization tool verifies the contents of the files.
        /// </summary>
        [Test]
        [Category("Tools")]
        public void TestVerifies()
        {
            var l1 = Path.Combine(TARGETFOLDER, "l1");
            var l2 = Path.Combine(TARGETFOLDER, "l2");

            Directory.CreateDirectory(l1);
            Directory.CreateDirectory(l2);

            GenerateTestData(l1, 5, 0, 0, 1024).Wait();

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

            var async_call = RemoteSynchronization.Program.Main(args);
            var return_code = async_call.ConfigureAwait(false).GetAwaiter().GetResult();

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
        public static async Task GenerateTestData(string dir, int n_files, int n_dirs, int n_levels, int max_file_size)
        {
            if (!SystemIO.IO_OS.DirectoryExists(dir))
                SystemIO.IO_OS.DirectoryCreate(dir);

            var fs = Enumerable.Range(0, n_files)
                .Select(i => GenerateTestFile(dir, i, max_file_size));
            var ds = n_levels > 0 ?
                Enumerable.Range(0, n_dirs)
                    .Select(i =>
                    {
                        var subdir = Path.Combine(dir, $"dir_{i}");
                        return GenerateTestData(subdir, n_files, n_dirs, n_levels - 1, max_file_size);
                    })
                : [];

            await Task.WhenAll([.. fs, .. ds]);
        }

        public static async Task GenerateTestFile(string dir, int i, int max_file_size)
        {
            var rnd = new Random();
            var file = Path.Combine(dir, $"file_{i}.txt");
            var size = rnd.Next(1, max_file_size);
            var data = new byte[size];
            rnd.NextBytes(data);
            await File.WriteAllBytesAsync(file, data);
        }

    }
}