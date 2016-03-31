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

        [Test]
        public void RunMixedBlockFile_1()
        {
            PrepareSourceData();
            RunCommands(1024 * 10, modifyOptions: opts => {
                opts["block-hash-algorithm"] = "MD5";
                opts["file-hash-algorithm"] = "SHA1";
            });
        }

        [Test]
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

            var data = new byte[basedatasize + 500];
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data.Take(basedatasize).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a0"), data.Take(0).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a1"), data.Take(1).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a-p1"), data.Take(basedatasize + 1).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a-p2"), data.Take(basedatasize+ 2).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a-p500"), data.Take(basedatasize + 500).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a-m1"), data.Take(basedatasize - 1).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a-m2"), data.Take(basedatasize - 2).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a-m500"), data.Take(basedatasize - 500).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a-s1"), data.Take(blocksize / 4 + 6).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a-s2"), data.Take(blocksize / 10 + 6).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a-l1"), data.Take(blocksize * 4 + 6).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a-l2"), data.Take(blocksize * 10 + 6).ToArray());

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                c.Backup(new string[] { DATAFOLDER });

            // After the first backup we remove the --blocksize argument as that should be auto-set
            testopts.Remove("blocksize");

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0 }), null))
            {
                var r = c.List("*");
                Console.WriteLine("In first backup:");
                Console.WriteLine(string.Join(Environment.NewLine, r.Files.Select(x => x.Path)));
            }

            new Random().NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b"), data.Take(basedatasize).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b0"), data.Take(0).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b1"), data.Take(1).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b-p1"), data.Take(basedatasize + 1).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b-p2"), data.Take(basedatasize + 2).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b-p500"), data.Take(basedatasize + 500).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b-m1"), data.Take(basedatasize - 1).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b-m2"), data.Take(basedatasize - 2).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b-m500"), data.Take(basedatasize - 500).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b-s1"), data.Take(blocksize / 4 + 6).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b-s2"), data.Take(blocksize / 10 + 6).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b-l1"), data.Take(blocksize * 4 + 6).ToArray());
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b-l2"), data.Take(blocksize * 10 + 6).ToArray());

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                c.Backup(new string[] { DATAFOLDER });

            new Random().NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "c"), data.Take(basedatasize).ToArray());
            new Random().NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "c0"), data.Take(0).ToArray());
            new Random().NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "c1"), data.Take(1).ToArray());
            new Random().NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "c-p1"), data.Take(basedatasize + 1).ToArray());
            new Random().NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "c-p2"), data.Take(basedatasize + 2).ToArray());
            new Random().NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "c-p500"), data.Take(basedatasize + 500).ToArray());
            new Random().NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "c-m1"), data.Take(basedatasize - 1).ToArray());
            new Random().NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "c-m2"), data.Take(basedatasize - 2).ToArray());
            new Random().NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "c-m500"), data.Take(basedatasize - 500).ToArray());
            new Random().NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "c-s1"), data.Take(blocksize / 4 + 6).ToArray());
            new Random().NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "c-s2"), data.Take(blocksize / 10 + 6).ToArray());
            new Random().NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "c-l1"), data.Take(blocksize * 4 + 6).ToArray());
            new Random().NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "c-l2"), data.Take(blocksize * 10 + 6).ToArray());

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                c.Backup(new string[] { DATAFOLDER });
            
            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0 }), null))
            {
                var r = c.List("*");
                //Console.WriteLine("Newest before deleting:");
                //Console.WriteLine(string.Join(Environment.NewLine, r.Files.Select(x => x.Path)));
                Assert.AreEqual(40, r.Files.Count());
            }

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0, no_local_db = true }), null))
            {
                var r = c.List("*");
                //Console.WriteLine("Newest without db:");
                //Console.WriteLine(string.Join(Environment.NewLine, r.Files.Select(x => x.Path)));
                Assert.AreEqual(40, r.Files.Count());
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
                Assert.AreEqual(14, r.Files.Count());
            }

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 1 }), null))
            {
                var r = c.List("*");
                //Console.WriteLine("V1 after delete:");
                //Console.WriteLine(string.Join(Environment.NewLine, r.Files.Select(x => x.Path)));
                Assert.AreEqual(27, r.Files.Count());
            }

            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0 }), null))
            {
                var r = c.List("*");
                //Console.WriteLine("Newest after delete:");
                //Console.WriteLine(string.Join(Environment.NewLine, r.Files.Select(x => x.Path)));
                Assert.AreEqual(40, r.Files.Count());
            }
        }    
    }
}

