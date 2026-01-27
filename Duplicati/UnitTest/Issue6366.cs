using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    public class Issue6366 : BasicSetupHelper
    {
        [Test]
        [Category("Purge")]
        [Category("Performance")]
        public async Task TestPurgeBrokenFilesPerformance()
        {
            var blocksize = 1024 * 10;
            var numberOfVersions = 50; // Simulate many versions as in the issue
            var filesPerVersion = 200; // Many files per version

            var testopts = TestOptions;
            testopts["blocksize"] = blocksize.ToString() + "b";
            testopts["no-backend-verification"] = "true";

            // Create initial set of files
            var allFiles = new List<string>();
            for (int i = 0; i < filesPerVersion; i++)
            {
                var filename = Path.Combine(DATAFOLDER, $"testfile_{i}.dat");
                File.WriteAllBytes(filename, new byte[blocksize * 2]);
                allFiles.Add(filename);
            }

            // Create multiple backup versions
            string firstDblockFile = null;
            for (int version = 0; version < numberOfVersions; version++)
            {
                // Modify some files to create new versions
                foreach (var file in allFiles.Take(10))
                {
                    File.WriteAllBytes(file, new byte[blocksize * 2 + version]);
                }

                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                {
                    var res = c.Backup(new string[] { DATAFOLDER });
                    Assert.AreEqual(0, res.Errors.Count());
                }

                // Capture the first dblock file
                if (version == 0)
                {
                    firstDblockFile = SystemIO.IO_OS
                        .GetFiles(TARGETFOLDER, "*.dblock.zip.aes")
                        .First();
                }

                // Ensure timestamps are different
                Thread.Sleep(1000);
            }

            // Delete a dblock file to create broken files
            if (firstDblockFile != null)
            {
                File.Delete(firstDblockFile);
            }
            else
            {
                Assert.Fail("Could not find dblock file to delete");
            }

            // Measure purge-broken-files execution time
            var stopwatch = Stopwatch.StartNew();

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var task = Task.Run(() => c.PurgeBrokenFiles(null));
                if (await Task.WhenAny(task, Task.Delay(60000)) == task)
                {
                    var brk = await task;
                    Assert.AreEqual(0, brk.Errors.Count());
                    Assert.AreEqual(0, brk.Warnings.Count());
                }
                else
                {
                    c.Abort();
                    Assert.Fail("PurgeBrokenFiles timed out");
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"PurgeBrokenFiles took: {stopwatch.Elapsed.TotalSeconds} seconds");

            // The operation should complete in a reasonable time
            // With the fix, this should be under 60 seconds
            // Without the fix, this could take hours
            Assert.Less(stopwatch.Elapsed.TotalSeconds, 60,
                $"PurgeBrokenFiles took too long: {stopwatch.Elapsed}");
        }

        [Test]
        [Category("Purge")]
        public void TestPurgeBrokenFilesReducedStatistics()
        {
            var blocksize = 1024 * 10;
            var numberOfVersions = 3;
            var filesPerVersion = 10;

            var testopts = TestOptions;
            testopts["blocksize"] = blocksize.ToString() + "b";
            testopts["reduced-purge-statistics"] = "true";

            // Create initial set of files
            var allFiles = new List<string>();
            var rnd = new Random(42);
            for (int i = 0; i < filesPerVersion; i++)
            {
                var filename = Path.Combine(DATAFOLDER, $"testfile_{i}.dat");
                var bytes = new byte[blocksize * 2];
                rnd.NextBytes(bytes);
                File.WriteAllBytes(filename, bytes);
                allFiles.Add(filename);
            }

            // Create multiple backup versions
            string dblockToDelete = null;
            for (int version = 0; version < numberOfVersions; version++)
            {
                // Modify ALL files to create new versions with unique content
                foreach (var file in allFiles)
                {
                    var bytes = new byte[blocksize * 2 + version * 100];
                    rnd.NextBytes(bytes);
                    File.WriteAllBytes(file, bytes);
                }

                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                {
                    var res = c.Backup(new string[] { DATAFOLDER });
                    Assert.AreEqual(0, res.Errors.Count());
                }

                // Capture the dblock file from the second version (index 1)
                if (version == 1)
                {
                    // Find the new dblock file. 
                    // We need to distinguish it from previous ones.
                    // Since we sleep, timestamps might help, or we can just list all and pick one we haven't seen?
                    // Simpler: just pick the one created most recently.
                    var dblocks = SystemIO.IO_OS.GetFiles(TARGETFOLDER, "*.dblock.zip.aes");
                    dblockToDelete = dblocks.OrderByDescending(f => new FileInfo(f).CreationTime).First();
                }

                Thread.Sleep(1000); // Ensure filesystem timestamp granularity
            }

            // Delete the dblock file
            if (dblockToDelete != null)
            {
                File.Delete(dblockToDelete);
            }
            else
            {
                Assert.Fail("Could not find dblock file to delete");
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var brk = c.PurgeBrokenFiles(null);
                Assert.AreEqual(0, brk.Errors.Count());
                Assert.AreEqual(0, brk.Warnings.Count());

                Assert.IsNotNull(brk.PurgeResults);
                Assert.AreEqual(-1, brk.PurgeResults.RemovedFileSize);
            }
        }
    }
}
