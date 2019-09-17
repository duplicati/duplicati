#region Disclaimer / License
// Copyright (C) 2019, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
//
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duplicati.Library.Interface;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class ParityTesting : BasicSetupHelper
    {
        [Test]
        [Category("Parity")]
        public void ParityCreationTest()
        {
            SetUp();

            var blocksize = 1024 * 10;
            var basedatasize = 0;

            var testopts = TestOptions;
            testopts["parity-file-redundancy"] = "5";
            testopts["blocksize"] = $"{blocksize}b";

            var filenames = BorderTests.WriteTestFilesToFolder(DATAFOLDER, blocksize, basedatasize).Select(x => $"a{x.Key}").ToList();

            var round1 = filenames.Take(filenames.Count / 3).ToArray();
            var round2 = filenames.Take((filenames.Count / 3) * 2).ToArray();

            using (var c = new Library.Main.Controller($"file://{TARGETFOLDER}", testopts, null))
            {
                var res = c.Backup(new[] { DATAFOLDER }, new Library.Utility.FilterExpression(round1.Select(x => $"*{Path.DirectorySeparatorChar}{x}")));
                Assert.AreEqual(res.AddedFiles, round1.Length);
            }

            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(2));
            using (var c = new Library.Main.Controller($"file://{TARGETFOLDER}", testopts, null))
            {
                var res = c.Backup(new[] { DATAFOLDER }, new Library.Utility.FilterExpression(round2.Select(x => $"*{Path.DirectorySeparatorChar}{x}")));
                Assert.AreEqual(res.AddedFiles, round2.Length - round1.Length);
            }

            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(2));
            using (var c = new Library.Main.Controller($"file://{TARGETFOLDER}", testopts, null))
            {
                var res = c.Backup(new[] { DATAFOLDER });
                Assert.AreEqual(res.AddedFiles, filenames.Count - round2.Length);
            }

            // verify number of backend files
            List<string> allFiles = Directory.GetFiles(TARGETFOLDER).Where(x => x.EndsWith(".par2.zip")).ToList();
            List<string> parityFiles = Directory.GetFiles(TARGETFOLDER).Where(x => !x.EndsWith(".par2.zip")).ToList();
            var isFileCountCorrect = allFiles.Count == parityFiles.Count;
            Assert.IsTrue(isFileCountCorrect, "Incorrect number of matching data-to-parity files after backup");
            Assert.AreEqual(18, allFiles.Count + parityFiles.Count, "Incorrect number of files after backup");

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var listinfo = c.List("*");
                var files = listinfo.Files.ToArray();
                var filecount = files.Length;
                listinfo = c.List();
                var filesets = listinfo.Filesets.ToArray();

                Console.WriteLine("Listing final version information");

                Console.WriteLine("Versions:");
                Console.WriteLine("  " + string.Join(Environment.NewLine + "  ", filesets.Select(x => $"{x.Version}: {x.Time}, {x.FileCount} {x.FileSizes}")));
                Console.WriteLine("Files:");
                Console.WriteLine("  " + string.Join(Environment.NewLine + "  ", files.Select(x => $"{x.Path}: {string.Join(" - ", x.Sizes.Select(y => y.ToString()))}")));

                Assert.AreEqual(3, filesets.Length, "Incorrect number of filesets after final backup");
                Assert.AreEqual(filenames.Count + 1, filecount, "Incorrect number of files after final backup");
            }
        }

        [Test]
        [Category("Parity")]
        public void ParityRepairTest()
        {
            SetUp();

            var blocksize = 1024 * 10;
            var basedatasize = 0;

            var testopts = TestOptions;
            testopts["parity-file-redundancy"] = "5";
            testopts["blocksize"] = $"{blocksize}b";

            var filenames = BorderTests.WriteTestFilesToFolder(DATAFOLDER, blocksize, basedatasize).Select(x => $"a{x.Key}").ToList();

            using (var c = new Library.Main.Controller($"file://{TARGETFOLDER}", testopts, null))
            {
                var res = c.Backup(new[] { DATAFOLDER }, new Library.Utility.FilterExpression(filenames.Select(x => $"*{Path.DirectorySeparatorChar}{x}")));
                Assert.AreEqual(res.AddedFiles, filenames.Count);
            }

            // corrupt remote files
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(2));
            foreach (var remoteDataFile in Directory.GetFiles(TARGETFOLDER, "*.zip.aes"))
            {
                Random rnd = new Random();
                int location = rnd.Next(0, remoteDataFile.Length);
                //TestUtils.ReplaceFileData(remoteDataFile, location, BitConverter.GetBytes((UInt32)0xDEADBEEF));
            }

            // restore files using corrupted backend files
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(2));
            using (var c = new Library.Main.Controller($"file://{TARGETFOLDER}", testopts.Expand(new { restore_path = RESTOREFOLDER, no_local_blocks = true }), null))
            {
                IRestoreResults r = c.Restore(null);
                Assert.AreEqual(filenames.Count, r.RestoredFiles);
            }

            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(2));

            TestUtils.VerifyDir(DATAFOLDER, RESTOREFOLDER, !Library.Utility.Utility.ParseBoolOption(testopts, "skip-metadata"));

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var listinfo = c.List("*");
                var files = listinfo.Files.ToArray();
                var filecount = files.Length;
                listinfo = c.List();
                var filesets = listinfo.Filesets.ToArray();

                Console.WriteLine("Listing final version information");

                Console.WriteLine("Versions:");
                Console.WriteLine("  " + string.Join(Environment.NewLine + "  ", filesets.Select(x => $"{x.Version}: {x.Time}, {x.FileCount} {x.FileSizes}")));
                Console.WriteLine("Files:");
                Console.WriteLine("  " + string.Join(Environment.NewLine + "  ", files.Select(x => $"{x.Path}: {string.Join(" - ", x.Sizes.Select(y => y.ToString()))}")));

                Assert.AreEqual(1, filesets.Length, "Incorrect number of filesets after final backup");
                Assert.AreEqual(filenames.Count, filecount, "Incorrect number of files after final backup");
            }
        }

    }
}
