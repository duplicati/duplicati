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

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Duplicati.Library.Interface;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class Issue5097 : BasicSetupHelper
    {
        [Test]
        [Category("Targeted")]
        public void RunCommands()
        {
            var testopts = TestOptions;
            testopts["upload-unchanged-backups"] = "true";
            testopts["keep-versions"] = "2";
            testopts["number-of-retries"] = "0";
            testopts["blocksize"] = "50kb";

            var data = new byte[1024 * 1024 * 10];
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var r = c.Backup(new string[] { DATAFOLDER });
                Assert.AreEqual(0, r.Errors.Count());
                Assert.AreEqual(0, r.Warnings.Count());
            }

            var firstdlistfile = Directory.GetFiles(TARGETFOLDER, "*dlist*").FirstOrDefault();

            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b"), data);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var r = c.Backup(new string[] { DATAFOLDER });
                Assert.AreEqual(0, r.Errors.Count());
                Assert.AreEqual(0, r.Warnings.Count());
            }

            var seconddlistfile = Directory.GetFiles(TARGETFOLDER, "*dlist*").FirstOrDefault(x => x != firstdlistfile);

            // Corrupt the second dlist file
            using (var fs = File.OpenWrite(seconddlistfile))
            {
                fs.Seek(fs.Length / 2, SeekOrigin.End);
                fs.WriteByte(42);
            }

            // Delete the local database
            File.Delete(DBFILE);

            // Repair the database, expect an error
            Assert.Catch<CryptographicException>(() =>
            {
                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                {
                    var r = c.Repair();
                    Assert.AreEqual(0, r.Errors.Count());
                    Assert.AreEqual(0, r.Warnings.Count());
                }
            });

            // Make a backup, this should fail
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "c"), data);
            var uix = Assert.Catch<UserInformationException>(() =>
            {
                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                {
                    var r = c.Backup(new string[] { DATAFOLDER });
                    Assert.AreEqual(1, r.Errors.Count());
                    Assert.AreEqual(0, r.Warnings.Count());
                }
            });

            Assert.That(uix.Message, Does.Contain("database was attempted repaired"));
        }
    }
}
