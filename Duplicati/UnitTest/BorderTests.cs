//  Copyright (C) 2015, The Duplicati Team
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
using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class BorderTests : BasicSetupHelper
    {
        public override void PrepareSourceData()
        {
            base.PrepareSourceData();

            Directory.CreateDirectory(DATAFOLDER);
            Directory.CreateDirectory(TARGETFOLDER);
        }

        [Test]
        public void Run10k()
        {
            PrepareSourceData();
            RunCommands(1024 * 10);
        }

        [Test]
        public void Run100k()
        {
            PrepareSourceData();
            RunCommands(1024 * 100);
        }

        [Test]
        public void Run12345_1()
        {
            PrepareSourceData();
            RunCommands(12345);
        }

        [Test]
        public void Run12345_2()
        {
            PrepareSourceData();
            RunCommands(12345, 1024 * 1024 * 10);
        }

        [Test]
        public void RunMD5()
        {
            PrepareSourceData();
            RunCommands(1024 * 10, modifyOptions: opts => {
                opts["block-hash-algorithm"] = "MD5";
                opts["file-hash-algorithm"] = "MD5";
            });
        }
            
        [Test]
        public void RunSHA384()
        {
            PrepareSourceData();
            RunCommands(1024 * 10, modifyOptions: opts => {
                opts["block-hash-algorithm"] = "SHA384";
                opts["file-hash-algorithm"] = "SHA384";
            });
        }

        //[Test]
        public void RunMixedBlockFile_1()
        {
            PrepareSourceData();
            RunCommands(1024 * 10, modifyOptions: opts => {
                opts["block-hash-algorithm"] = "MD5";
                opts["file-hash-algorithm"] = "SHA1";
            });
        }

        //[Test]
        public void RunMixedBlockFile_2()
        {
            PrepareSourceData();
            RunCommands(1024 * 10, modifyOptions: opts => {
                opts["block-hash-algorithm"] = "MD5";
                opts["file-hash-algorithm"] = "SHA256";
            });
        }


        private void RunCommands(int blocksize, int basedatasize = 0, Action<Dictionary<string, string>> modifyOptions = null)
        {
            var testopts = TestOptions;
            testopts["verbose"] = "true";
            testopts["blocksize"] = blocksize.ToString() + "b";
            if (modifyOptions != null)
                modifyOptions(testopts);

            if (basedatasize <= 0)
                basedatasize = blocksize * 1024;

            var filenames = new Dictionary<string, int>();
            filenames[""] = basedatasize;
            filenames["-0"] = 0;
            filenames["-1"] = 1;

            filenames["-p1"] = basedatasize + 1;
            filenames["-p2"] = basedatasize + 2;
            filenames["-p500"] = basedatasize + 500;
            filenames["-m1"] = basedatasize - 1;
            filenames["-m2"] = basedatasize - 2;
            filenames["-m500"] = basedatasize - 500;

            filenames["-s1"] = blocksize / 4 + 6;
            filenames["-s2"] = blocksize / 10 + 6;
            filenames["-l1"] = blocksize * 4 + 6;
            filenames["-l2"] = blocksize * 10 + 6;

            filenames["-bm1"] = blocksize - 1;
            filenames["-b"] = blocksize;
            filenames["-bp1"] = blocksize + 1;

            var data = new byte[filenames.Select(x => x.Value).Max()];

            foreach(var k in filenames)
                File.WriteAllBytes(Path.Combine(DATAFOLDER, "a" + k.Key), data.Take(k.Value).ToArray());

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                c.Backup(new string[] { DATAFOLDER });

            // After the first backup we remove the --blocksize argument as that should be auto-set
            testopts.Remove("blocksize");
            testopts.Remove("block-hash-algorithm");
            testopts.Remove("file-hash-algorithm");

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0 }), null))
            {
                var r = c.List("*");
                //Console.WriteLine("In first backup:");
                //Console.WriteLine(string.Join(Environment.NewLine, r.Files.Select(x => x.Path)));
            }

            new Random().NextBytes(data);
            foreach(var k in filenames)
                File.WriteAllBytes(Path.Combine(DATAFOLDER, "b" + k.Key), data.Take(k.Value).ToArray());

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                c.Backup(new string[] { DATAFOLDER });

            var rn = new Random();
            foreach(var k in filenames)
            {
                rn.NextBytes(data);
                File.WriteAllBytes(Path.Combine(DATAFOLDER, "c" + k.Key), data.Take(k.Value).ToArray());
            }
            

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                c.Backup(new string[] { DATAFOLDER });
            
            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0 }), null))
            {
                var r = c.List("*");
                //Console.WriteLine("Newest before deleting:");
                //Console.WriteLine(string.Join(Environment.NewLine, r.Files.Select(x => x.Path)));
                Assert.AreEqual((filenames.Count * 3) + 1, r.Files.Count());
            }

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0, no_local_db = true }), null))
            {
                var r = c.List("*");
                //Console.WriteLine("Newest without db:");
                //Console.WriteLine(string.Join(Environment.NewLine, r.Files.Select(x => x.Path)));
                Assert.AreEqual((filenames.Count * 3) + 1, r.Files.Count());
            }


            File.Delete(DBFILE);
            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                c.Repair();

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                Assert.AreEqual(3, c.List().Filesets.Count());

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 2 }), null))
            {
                var r = c.List("*");
                //Console.WriteLine("V2 after delete:");
                //Console.WriteLine(string.Join(Environment.NewLine, r.Files.Select(x => x.Path)));
                Assert.AreEqual((filenames.Count * 1) + 1, r.Files.Count());
            }

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 1 }), null))
            {
                var r = c.List("*");
                //Console.WriteLine("V1 after delete:");
                //Console.WriteLine(string.Join(Environment.NewLine, r.Files.Select(x => x.Path)));
                Assert.AreEqual((filenames.Count * 2) + 1, r.Files.Count());
            }

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0 }), null))
            {
                var r = c.List("*");
                //Console.WriteLine("Newest after delete:");
                //Console.WriteLine(string.Join(Environment.NewLine, r.Files.Select(x => x.Path)));
                Assert.AreEqual((filenames.Count * 3) + 1, r.Files.Count());
            }

            if (Directory.Exists(RESTOREFOLDER))
                Directory.Delete(RESTOREFOLDER, true);
            Directory.CreateDirectory(RESTOREFOLDER);

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { restore_path = RESTOREFOLDER, no_local_blocks = true }), null))
            {
                var r = c.Restore(null);
                Assert.AreEqual(filenames.Count * 3, r.FilesRestored);
            }

            TestUtils.VerifyDir(DATAFOLDER, RESTOREFOLDER, true);

            using(var tf = new Library.Utility.TempFolder())
            {
                using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { restore_path = (string)tf, no_local_blocks = true }), null))
                {
                    var r = c.Restore(new string[] { Path.Combine(DATAFOLDER, "a") + "*" });
                    Assert.AreEqual(filenames.Count, r.FilesRestored);
                }
            }

        }    
    }
}

