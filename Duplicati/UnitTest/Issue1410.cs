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
using System;using System.IO;using NUnit.Framework;using System.Linq;using System.Collections.Generic;

namespace Duplicati.UnitTest
{
    [TestFixture]    public class Issue1410 : BasicSetupHelper
    {        [TestFixtureSetUp()]        public override void PrepareSourceData()        {            base.PrepareSourceData();            Directory.CreateDirectory(DATAFOLDER);            Directory.CreateDirectory(TARGETFOLDER);        }        [Test]        public void RunCommands()        {            var data = new byte[1024 * 1024 * 10];            //File.WriteAllText(Path.Combine(DATAFOLDER, "a"), "hi");            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))                c.Backup(new string[] { DATAFOLDER });            new Random().NextBytes(data);            //File.WriteAllText(Path.Combine(DATAFOLDER, "b"), "there");            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b"), data);            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))                c.Backup(new string[] { DATAFOLDER });            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions.Expand(new { version = 0 }), null))            {                var r = c.List("*");                Console.WriteLine("Newest before deleting:");                Console.WriteLine(string.Join(Environment.NewLine, r.Files.Select(x => x.Path)));                Assert.AreEqual(3, r.Files.Count());            }            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions.Expand(new { version = 0, no_local_db = true }), null))            {                var r = c.List("*");                Console.WriteLine("Newest without db:");                Console.WriteLine(string.Join(Environment.NewLine, r.Files.Select(x => x.Path)));                Assert.AreEqual(3, r.Files.Count());            }            File.Delete(DBFILE);            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))                c.Repair();            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))                Assert.AreEqual(c.List().Filesets.Count(), 2);            using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions.Expand(new { version = 1 }), null))            {                var r = c.List("*");                Console.WriteLine("Oldest after delete:");                Console.WriteLine(string.Join(Environment.NewLine, r.Files.Select(x => x.Path)));                Assert.AreEqual(2, r.Files.Count());            }                                using(var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions.Expand(new { version = 0 }), null))            {                var r = c.List("*");                Console.WriteLine("Newest after delete:");                Console.WriteLine(string.Join(Environment.NewLine, r.Files.Select(x => x.Path)));                Assert.AreEqual(3, r.Files.Count());            }        }
    }
}

