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
using System.Threading;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class ParityTesting : BasicSetupHelper
    {
        [Test]
        [Category("Parity")]
        public void ParityCreationTest()
        {
            var blocksize = 1024 * 10;
            var basedatasize = 0;

            var testopts = TestOptions;
            testopts["enable-parity-file"] = "true";
            testopts["blocksize"] = $"{blocksize}b";

            var filenames = BorderTests.WriteTestFilesToFolder(DATAFOLDER, blocksize, basedatasize).Select(x => "a" + x.Key).ToList();

            var round1 = filenames.Take(filenames.Count / 3).ToArray();
            var round2 = filenames.Take((filenames.Count / 3) * 2).ToArray();

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup(new[] { DATAFOLDER }, new Library.Utility.FilterExpression(round1.Select(x => "*" + Path.DirectorySeparatorChar + x)));
                Assert.AreEqual(res.AddedFiles, round1.Length);
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup(new[] { DATAFOLDER }, new Library.Utility.FilterExpression(round2.Select(x => "*" + Path.DirectorySeparatorChar + x)));
                Assert.AreEqual(res.AddedFiles, round2.Length - round1.Length);
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup(new[] { DATAFOLDER });
                Assert.AreEqual(res.AddedFiles, filenames.Count - round2.Length);
            }

            Thread.Sleep(TimeSpan.FromSeconds(3));

            List<string> allFiles = Directory.GetFiles(TARGETFOLDER).Where(x => x.EndsWith(".par2.zip")).ToList();
            List<string> parityFiles = Directory.GetFiles(TARGETFOLDER).Where(x => !x.EndsWith(".par2.zip")).ToList();
            var isFileCountCorrect = allFiles.Count == parityFiles.Count;
            Assert.IsTrue(isFileCountCorrect, "Incorrect number of matching data-to-parity files after backup");
            Assert.AreEqual(18, allFiles.Count + parityFiles.Count, "Incorrect number of files after backup");
        }


    }
}
