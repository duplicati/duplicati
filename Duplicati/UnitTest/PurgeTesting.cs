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
using Duplicati.Library.Common.IO;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest
{
    public class PurgeTesting : BasicSetupHelper
    {
        [Test]
        [Category("Purge")]
        public async Task PurgeTestAsync()
        {
            var blocksize = 1024 * 10;
            var basedatasize = 0;

            var testopts = TestOptions;
            testopts["blocksize"] = blocksize.ToString() + "b";

            var filenames = BorderTests.WriteTestFilesToFolder(DATAFOLDER, blocksize, basedatasize).Select(x => "a" + x.Key).ToList();

            var round1 = filenames.Take(filenames.Count / 3).ToArray();
            var round2 = filenames.Take((filenames.Count / 3) * 2).ToArray();
            var round3 = filenames;

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = await c.BackupAsync(new string[] { DATAFOLDER }, new Library.Utility.FilterExpression(round1.Select(x => "*" + Path.DirectorySeparatorChar + x)));
                Assert.AreEqual(0, res.Errors.Count());
                Assert.AreEqual(0, res.Warnings.Count());
                Assert.AreEqual(res.AddedFiles, round1.Length);
            }

            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5));
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = await c.BackupAsync(new string[] { DATAFOLDER }, new Library.Utility.FilterExpression(round2.Select(x => "*" + Path.DirectorySeparatorChar + x)));
                Assert.AreEqual(0, res.Errors.Count());
                Assert.AreEqual(0, res.Warnings.Count());
                Assert.AreEqual(res.AddedFiles, round2.Length - round1.Length);
            }

            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5));
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = await c.BackupAsync(new string[] { DATAFOLDER });
                Assert.AreEqual(0, res.Errors.Count());
                Assert.AreEqual(0, res.Warnings.Count());
                Assert.AreEqual(res.AddedFiles, filenames.Count - round2.Length);
            }

            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5));
            var last_ts = DateTime.Now;

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { list_sets_only = true }), null))
            {
                var inf = await c.ListAsync();
                Assert.AreEqual(0, inf.Errors.Count());
                Assert.AreEqual(0, inf.Warnings.Count());
                var filesets = inf.Filesets.Count();
                Assert.AreEqual(3, filesets, "Incorrect number of initial filesets");
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var listResults = await c.ListAsync("*");
                Assert.AreEqual(0, listResults.Errors.Count());
                Assert.AreEqual(0, listResults.Warnings.Count());
                var filecount = listResults.Files.Count();
                Assert.AreEqual(filenames.Count + 1, filecount, "Incorrect number of initial files");
            }

            var allversion_candidate = round1.First();
            var single_version_candidate = round1.Skip(1).First();

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = await c.PurgeFilesAsync(new Library.Utility.FilterExpression("*" + Path.DirectorySeparatorChar + allversion_candidate));
                Assert.AreEqual(0, res.Errors.Count());
                Assert.AreEqual(0, res.Warnings.Count());
                Assert.AreEqual(3, res.RewrittenFileLists, "Incorrect number of rewritten filesets after all-versions purge");
                Assert.AreEqual(3, res.RemovedFileCount, "Incorrect number of removed files after all-versions purge");
            }

            for (var i = 0; i < 3; i++)
            {
                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = i }), null))
                {
                    var res = await c.PurgeFilesAsync(new Library.Utility.FilterExpression(Path.Combine(this.DATAFOLDER, single_version_candidate)));
                    Assert.AreEqual(0, res.Errors.Count());
                    Assert.AreEqual(0, res.Warnings.Count());
                    Assert.AreEqual(1, res.RewrittenFileLists, "Incorrect number of rewritten filesets after single-versions purge");
                    Assert.AreEqual(1, res.RemovedFileCount, "Incorrect number of removed files after single-versions purge");
                }
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = await c.PurgeFilesAsync(new Library.Utility.FilterExpression(round2.Skip(round1.Length).Take(2).Select(x => "*" + Path.DirectorySeparatorChar + x)));
                Assert.AreEqual(0, res.Errors.Count());
                Assert.AreEqual(0, res.Warnings.Count());
                Assert.AreEqual(2, res.RewrittenFileLists, "Incorrect number of rewritten filesets after 2-versions purge");
                Assert.AreEqual(4, res.RemovedFileCount, "Incorrect number of removed files after 2-versions purge");
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = await c.PurgeFilesAsync(new Library.Utility.FilterExpression(round3.Skip(round2.Length).Take(2).Select(x => "*" + Path.DirectorySeparatorChar + x)));
                Assert.AreEqual(0, res.Errors.Count());
                Assert.AreEqual(0, res.Warnings.Count());
                Assert.AreEqual(1, res.RewrittenFileLists, "Incorrect number of rewritten filesets after 1-versions purge");
                Assert.AreEqual(2, res.RemovedFileCount, "Incorrect number of removed files after 1-versions purge");
            }

            // Since we make the operations back-to-back, the purge timestamp can drift beyond the current time
            var wait_target = last_ts.AddSeconds(10) - DateTime.Now;
            if (wait_target.TotalMilliseconds > 0)
                System.Threading.Thread.Sleep(wait_target);

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var listinfo = await c.ListAsync("*");
                Assert.AreEqual(0, listinfo.Errors.Count());
                Assert.AreEqual(0, listinfo.Warnings.Count());
                var filecount = listinfo.Files.Count();
                listinfo = await c.ListAsync();
                Assert.AreEqual(0, listinfo.Errors.Count());
                Assert.AreEqual(0, listinfo.Warnings.Count());
                var filesets = listinfo.Filesets.Count();

                Assert.AreEqual(3, filesets, "Incorrect number of filesets after purge");
                Assert.AreEqual(filenames.Count - 6 + 1, filecount, "Incorrect number of files after purge");
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var backupResults = await c.BackupAsync(new string[] { DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var listinfo = await c.ListAsync("*");
                Assert.AreEqual(0, listinfo.Errors.Count());
                Assert.AreEqual(0, listinfo.Warnings.Count());
                var files = listinfo.Files.ToArray();
                var filecount = files.Length;
                listinfo = await c.ListAsync();
                Assert.AreEqual(0, listinfo.Errors.Count());
                Assert.AreEqual(0, listinfo.Warnings.Count());
                var filesets = listinfo.Filesets.ToArray();

                Console.WriteLine("Listing final version information");

                Console.WriteLine("Versions:");
                Console.WriteLine("  " + string.Join(Environment.NewLine + "  ", filesets.Select(x => $"{x.Version}: {x.Time}, {x.FileCount} {x.FileSizes}")));
                Console.WriteLine("Files:");
                Console.WriteLine("  " + string.Join(Environment.NewLine + "  ", files.Select(x => $"{x.Path}: {string.Join(" - ", x.Sizes.Select(y => y.ToString()))}")));

                Assert.AreEqual(4, filesets.Length, "Incorrect number of filesets after final backup");
                Assert.AreEqual(filenames.Count + 1, filecount, "Incorrect number of files after final backup");
            }
        }

        /// <summary>
        /// A purge limited to a version or a time that matches no fileset must stop, not
        /// fall back to purging every version.
        /// </summary>
        [Test]
        [Category("Purge")]
        public async Task PurgeWithASelectionThatMatchesNothingPurgesNothingAsync()
        {
            var testopts = new Dictionary<string, string>(TestOptions)
            {
                ["upload-unchanged-backups"] = "true"
            };
            var keep = Path.Combine(DATAFOLDER, "b.txt");
            var target = Path.Combine(DATAFOLDER, "a.txt");
            File.WriteAllText(target, "a");
            File.WriteAllText(keep, "b");

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            // Purging a version rewrites it one second after its original timestamp, and that has to
            // stay before the next version, so the second backup must be at least two seconds later
            // than the first (fileset timestamps have a resolution of one second)
            DateTime first;
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                first = (await c.ListFilesetsAsync()).Filesets.Single().Time;
            while (DateTime.UtcNow < first.ToUniversalTime().AddSeconds(2))
                await Task.Delay(100);
            File.WriteAllText(target, "a, changed");
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            var filter = new Library.Utility.FilterExpression("*" + Path.DirectorySeparatorChar + "a.txt", true);
            var folder = Util.AppendDirSeparator(DATAFOLDER);

            async Task<string[]> NamesInVersionAsync(int version)
            {
                using var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version }), null);
                var entries = await c.ListFolderAsync(new[] { folder }, 0, 0, false);
                return entries.Entries.Items.Select(x => Path.GetFileName(x.Path)).OrderBy(x => x).ToArray();
            }

            DateTime oldest;
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                oldest = (await c.ListFilesetsAsync()).Filesets.Min(x => x.Time);

            // Only versions 0 and 1 exist
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 5 }), null))
            {
                var ex = Assert.ThrowsAsync<Library.Interface.UserInformationException>(async () => await c.PurgeFilesAsync(filter), "A version that does not exist should not purge anything");
                Assert.AreEqual("NoFilesetFoundForTimeOrVersion", ex.HelpID);
            }
            CollectionAssert.AreEqual(new[] { "a.txt", "b.txt" }, await NamesInVersionAsync(0), "Version 0 should be untouched after a purge of a version that does not exist");
            CollectionAssert.AreEqual(new[] { "a.txt", "b.txt" }, await NamesInVersionAsync(1), "Version 1 should be untouched after a purge of a version that does not exist");

            // No fileset is at or before a day before the first backup
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { time = Library.Utility.Utility.SerializeDateTime(oldest.AddDays(-1).ToUniversalTime()) }), null))
            {
                var ex = Assert.ThrowsAsync<Library.Interface.UserInformationException>(async () => await c.PurgeFilesAsync(filter), "A time before every backup should not purge anything");
                Assert.AreEqual("NoFilesetFoundForTimeOrVersion", ex.HelpID);
            }
            CollectionAssert.AreEqual(new[] { "a.txt", "b.txt" }, await NamesInVersionAsync(0), "Version 0 should be untouched after a purge at a time before every backup");
            CollectionAssert.AreEqual(new[] { "a.txt", "b.txt" }, await NamesInVersionAsync(1), "Version 1 should be untouched after a purge at a time before every backup");

            // A version that exists is purged, and only that one
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 1 }), null))
                TestUtils.AssertResults(await c.PurgeFilesAsync(filter));
            CollectionAssert.AreEqual(new[] { "a.txt", "b.txt" }, await NamesInVersionAsync(0), "Version 0 should keep the file when only version 1 is purged");
            CollectionAssert.AreEqual(new[] { "b.txt" }, await NamesInVersionAsync(1), "Version 1 should have lost the purged file");
        }

        [Test]
        [Category("Purge")]
        public async Task PurgeBrokenFilesTestAsync()
        {
            var blocksize = 1024 * 10;
            var basedatasize = 0;

            var testopts = TestOptions;
            testopts["blocksize"] = blocksize.ToString() + "b";

            var filenames = BorderTests.WriteTestFilesToFolder(DATAFOLDER, blocksize, basedatasize).Select(x => "a" + x.Key).ToList();

            var round1 = filenames.Take(filenames.Count / 3).ToArray();
            var round2 = filenames.Take((filenames.Count / 3) * 2).ToArray();

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = await c.BackupAsync(new string[] { DATAFOLDER }, new Library.Utility.FilterExpression(round1.Select(x => "*" + Path.DirectorySeparatorChar + x)));
                Assert.AreEqual(0, res.Errors.Count());
                Assert.AreEqual(0, res.Warnings.Count());
                Assert.AreEqual(res.AddedFiles, round1.Length);
            }

            var dblock_file = SystemIO.IO_OS
                .GetFiles(TARGETFOLDER, "*.dblock.zip.aes")
                .Select(x => new FileInfo(x))
                .OrderBy(x => x.LastWriteTimeUtc)
                .Select(x => x.FullName)
                .First();

            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5));
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = await c.BackupAsync(new string[] { DATAFOLDER }, new Library.Utility.FilterExpression(round2.Select(x => "*" + Path.DirectorySeparatorChar + x)));
                Assert.AreEqual(0, res.Errors.Count());
                Assert.AreEqual(0, res.Warnings.Count());
                Assert.AreEqual(round2.Length - round1.Length, res.AddedFiles);
            }

            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5));
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = await c.BackupAsync(new string[] { DATAFOLDER });
                Assert.AreEqual(0, res.Errors.Count());
                Assert.AreEqual(0, res.Warnings.Count());
                Assert.AreEqual(filenames.Count - round2.Length, res.AddedFiles);
            }

            File.Delete(dblock_file);
            var last_ts = DateTime.Now;

            long[] affectedfiles;

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var brk = await c.ListBrokenFilesAsync(null);
                Assert.AreEqual(0, brk.Errors.Count());
                Assert.AreEqual(0, brk.Warnings.Count());
                var sets = brk.BrokenFiles.Count();
                var files = brk.BrokenFiles.Sum(x => x.Item3.Count());
                Assert.AreEqual(3, sets);
                Assert.True(files > 0);

                affectedfiles = brk.BrokenFiles.OrderBy(x => x.Item1).Select(x => x.Item3.LongCount()).ToArray();
            }

            for (var i = 0; i < 3; i++)
                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = i }), null))
                {
                    var brk = await c.ListBrokenFilesAsync(null);
                    Assert.AreEqual(0, brk.Errors.Count());
                    Assert.AreEqual(0, brk.Warnings.Count());
                    var sets = brk.BrokenFiles.Count();
                    var files = brk.BrokenFiles.Sum(x => x.Item3.Count());
                    Assert.AreEqual(1, sets);
                    Assert.AreEqual(affectedfiles[i], files);
                }

            // A dry-run should run without exceptions (see issue #4379).
            Dictionary<string, string> dryRunOptions = new Dictionary<string, string>(testopts) { ["dry-run"] = "true" };
            using (var c = new Controller("file://" + this.TARGETFOLDER, dryRunOptions, null))
            {
                var purgeResults = await c.PurgeBrokenFilesAsync(null);
                Assert.AreEqual(0, purgeResults.Errors.Count());
                // The deleted dblock also held metadata, so the affected files are recovered by
                // assigning replacement metadata, which is reported as a warning
                Assert.AreEqual(1, purgeResults.Warnings.Count(x => x.Contains("MetadataReplacedWithEmpty")));
                Assert.AreEqual(0, purgeResults.Warnings.Count(x => !x.Contains("MetadataReplacedWithEmpty")));
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var brk = await c.PurgeBrokenFilesAsync(null);
                Assert.AreEqual(0, brk.Errors.Count());
                Assert.AreEqual(1, brk.Warnings.Count(x => x.Contains("MetadataReplacedWithEmpty")));
                Assert.AreEqual(0, brk.Warnings.Count(x => !x.Contains("MetadataReplacedWithEmpty")));

                var modFilesets = 0L;
                if (brk.DeleteResults != null)
                    modFilesets += brk.DeleteResults.DeletedSets.Count();
                if (brk.PurgeResults != null)
                    modFilesets += brk.PurgeResults.RewrittenFileLists;

                Assert.AreEqual(3, modFilesets);
            }


            // Since we make the operations back-to-back, the purge timestamp can drift beyond the current time
            var wait_target = last_ts.AddSeconds(10) - DateTime.Now;
            if (wait_target.TotalMilliseconds > 0)
                System.Threading.Thread.Sleep(wait_target);

            // A subsequent backup should be successful.
            using (var c = new Controller("file://" + this.TARGETFOLDER, testopts, null))
            {
                var backupResults = await c.BackupAsync(new[] { this.DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }
        }
    }
}
