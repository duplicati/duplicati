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
    public class ZipFallbackTest : BasicSetupHelper
    {
        [Test]
        [Category("Targeted")]
        public void RunCommands()
        {
            var testopts = TestOptions;
            testopts["zip-compression-method"] = "lzma";
            testopts["zip-compression-library"] = "SharpCompress";
            testopts["blocksize"] = "50kb";

            var data = new byte[1024 * 1024 * 10];
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var r = c.Backup(new string[] { DATAFOLDER });
                Assert.AreEqual(0, r.Errors.Count());
                Assert.AreEqual(0, r.Warnings.Count());
            }

            // Delete the local database
            File.Delete(DBFILE);

            // Switch back to built-in compression
            testopts.Remove("zip-compression-method");
            testopts["zip-compression-library"] = "Auto";

            // Recreate the database
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var r = c.Repair();
                Assert.AreEqual(0, r.Errors.Count());
                // Ensure that we get warnings for the fallback, one for the dlist and one for the dindex
                Assert.AreEqual(2, r.Warnings.Count());
                Assert.That(r.Warnings.All(x => x.Contains("lzma", StringComparison.OrdinalIgnoreCase)));
            }

            // Restore files
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { restore_path = RESTOREFOLDER }), null))
            {
                var r = c.Restore(null);
                Assert.AreEqual(0, r.Errors.Count());
                // Ensure that we get warnings for the fallback, one for the dblock file
                Assert.AreEqual(1, r.Warnings.Count());
                Assert.That(r.Warnings.All(x => x.Contains("lzma", StringComparison.OrdinalIgnoreCase)));
            }

            TestUtils.AssertDirectoryTreesAreEquivalent(DATAFOLDER, RESTOREFOLDER, true, "Restore");

        }
    }
}
