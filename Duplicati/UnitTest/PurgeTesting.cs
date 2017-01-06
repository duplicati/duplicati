//  Copyright (C) 2016, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class PurgeTesting : BasicSetupHelper
    {
        public override void PrepareSourceData()
        {
            base.PrepareSourceData();

            Directory.CreateDirectory(DATAFOLDER);
            Directory.CreateDirectory(TARGETFOLDER);
        }

        [Test]
        [Category("Purge")]
        public void PurgeTest()
        {
            PrepareSourceData();

            var blocksize = 1024 * 10;
            var basedatasize = 0;

            var testopts = TestOptions;
            testopts["verbose"] = "true";
            testopts["blocksize"] = blocksize.ToString() + "b";

            var filenames = BorderTests.WriteTestFilesToFolder(DATAFOLDER, blocksize, basedatasize).Select(x => "a" + x.Key).ToList();

            var round1 = filenames.Take(filenames.Count / 3).ToArray();
            var round2 = filenames.Take((filenames.Count / 3) * 2).ToArray();
            var round3 = filenames;

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup(new string[] { DATAFOLDER }, new Library.Utility.FilterExpression(round1.Select(x => "*" + Path.DirectorySeparatorChar + x)));
                Assert.AreEqual(res.AddedFiles, round1.Length);
            }

            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(3));
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup(new string[] { DATAFOLDER }, new Library.Utility.FilterExpression(round2.Select(x => "*" + Path.DirectorySeparatorChar + x)));
                Assert.AreEqual(res.AddedFiles, round2.Length - round1.Length);
            }

            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(3));
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup(new string[] { DATAFOLDER });
                Assert.AreEqual(res.AddedFiles, filenames.Count - round2.Length);
            }

            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(3));
            var last_ts = DateTime.Now;

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { list_sets_only = true }), null))
            {
                var inf = c.List();
                var filesets = inf.Filesets.Count();
                Assert.AreEqual(3, filesets, "Incorrect number of initial filesets");
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var filecount = c.List("*").Files.Count();
                Assert.AreEqual(filenames.Count + 1, filecount, "Incorrect number of initial files");
            }

            var allversion_candidate = round1.First();
            var single_version_candidate = round1.Skip(1).First();

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.PurgeFiles(new Library.Utility.FilterExpression("*" + Path.DirectorySeparatorChar + allversion_candidate));
                Assert.AreEqual(3, res.RewrittenFileLists, "Incorrect number of rewritten filesets after all-versions purge");
                Assert.AreEqual(3, res.RemovedFileCount, "Incorrect number of removed files after all-versions purge");
            }

            for (var i = 0; i < 3; i++)
            {
                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = i }), null))
                {
                    var res = c.PurgeFiles(new Library.Utility.FilterExpression("*" + Path.DirectorySeparatorChar + single_version_candidate));
                    Assert.AreEqual(1, res.RewrittenFileLists, "Incorrect number of rewritten filesets after single-versions purge");
                    Assert.AreEqual(1, res.RemovedFileCount, "Incorrect number of removed files after single-versions purge");
                }
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.PurgeFiles(new Library.Utility.FilterExpression(round2.Skip(round1.Length).Take(2).Select(x => "*" + Path.DirectorySeparatorChar + x)));
                Assert.AreEqual(2, res.RewrittenFileLists, "Incorrect number of rewritten filesets after 2-versions purge");
                Assert.AreEqual(4, res.RemovedFileCount, "Incorrect number of removed files after 2-versions purge");
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.PurgeFiles(new Library.Utility.FilterExpression(round3.Skip(round2.Length).Take(2).Select(x => "*" + Path.DirectorySeparatorChar + x)));
                Assert.AreEqual(1, res.RewrittenFileLists, "Incorrect number of rewritten filesets after 1-versions purge");
                Assert.AreEqual(2, res.RemovedFileCount, "Incorrect number of removed files after 1-versions purge");
            }

            // Since we make the operations back-to-back, the purge timestamp can drift beyond the current time
            var wait_target = last_ts.AddSeconds(6) - DateTime.Now;
            if (wait_target.TotalMilliseconds > 0)
                System.Threading.Thread.Sleep(wait_target);

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var listinfo = c.List("*");
                var filecount = listinfo.Files.Count();
                listinfo = c.List();
                var filesets = listinfo.Filesets.Count();

                Assert.AreEqual(3, filesets, "Incorrect number of filesets after purge");
                Assert.AreEqual(filenames.Count - 6 + 1, filecount, "Incorrect number of files after purge");
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                c.Backup(new string[] { DATAFOLDER });

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var listinfo = c.List("*");
                var files = listinfo.Files.ToArray();
                var filecount = files.Length;
                listinfo = c.List();
                var filesets = listinfo.Filesets.ToArray();

                Console.WriteLine("Listing final version information");

                Console.WriteLine("Versions:");
                Console.WriteLine("  " + string.Join(Environment.NewLine + "  ", filesets.Select(x => string.Format("{0}: {1}, {2} {3}", x.Version, x.Time, x.FileCount, x.FileSizes))));
                Console.WriteLine("Files:");
                Console.WriteLine("  " + string.Join(Environment.NewLine + "  ", files.Select(x => string.Format("{0}: {1}", x.Path, string.Join(" - ", x.Sizes.Select(y => y.ToString()))))));

                Assert.AreEqual(4, filesets.Length, "Incorrect number of filesets after final backup");
                Assert.AreEqual(filenames.Count + 1, filecount, "Incorrect number of files after final backup");
            }
        }

        [Test]
        [Category("Purge")]
        public void PurgeBrokenFilesTest()
        {
            PrepareSourceData();

            var blocksize = 1024 * 10;
            var basedatasize = 0;

            var testopts = TestOptions;
            testopts["verbose"] = "true";
            testopts["blocksize"] = blocksize.ToString() + "b";

            var filenames = BorderTests.WriteTestFilesToFolder(DATAFOLDER, blocksize, basedatasize).Select(x => "a" + x.Key).ToList();

            var round1 = filenames.Take(filenames.Count / 3).ToArray();
            var round2 = filenames.Take((filenames.Count / 3) * 2).ToArray();
            var round3 = filenames;

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup(new string[] { DATAFOLDER }, new Library.Utility.FilterExpression(round1.Select(x => "*" + Path.DirectorySeparatorChar + x)));
                Assert.AreEqual(res.AddedFiles, round1.Length);
            }

            var dblock_file = Directory
                .GetFiles(TARGETFOLDER, "*.dblock.zip.aes")
                .Select(x => new FileInfo(x))
                .OrderBy(x => x.LastWriteTimeUtc)
                .Select(x => x.FullName)
                .First();

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup(new string[] { DATAFOLDER }, new Library.Utility.FilterExpression(round2.Select(x => "*" + Path.DirectorySeparatorChar + x)));
                Assert.AreEqual(round2.Length - round1.Length, res.AddedFiles);
            }
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup(new string[] { DATAFOLDER });
                Assert.AreEqual(filenames.Count - round2.Length, res.AddedFiles);
            }

            var last_ts = DateTime.Now;

            File.Delete(dblock_file);

            long[] affectedfiles;

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var brk = c.ListBrokenFiles(null);
                var sets = brk.BrokenFiles.Count();
                var files = brk.BrokenFiles.Sum(x => x.Item3.Count());
                Assert.AreEqual(3, sets);
                Assert.True(files > 0);

                affectedfiles = brk.BrokenFiles.OrderBy(x => x.Item1).Select(x => x.Item3.LongCount()).ToArray();
            }

            for (var i = 0; i < 3; i++)
                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = i }), null))
                {
                    var brk = c.ListBrokenFiles(null);
                    var sets = brk.BrokenFiles.Count();
                    var files = brk.BrokenFiles.Sum(x => x.Item3.Count());
                    Assert.AreEqual(1, sets);
                    Assert.AreEqual(affectedfiles[i], files);
                }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var brk = c.PurgeBrokenFiles(null);

                var modFilesets = 0L;
                if (brk.DeleteResults != null)
                    modFilesets += brk.DeleteResults.DeletedSets.Count();
                if (brk.PurgeResults != null)
                    modFilesets += brk.PurgeResults.RewrittenFileLists;

                Assert.AreEqual(3, modFilesets);
            }
        }

    }
}
