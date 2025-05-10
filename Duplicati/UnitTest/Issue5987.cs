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

using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class Issue5987 : BasicSetupHelper
    {
        [Test]
        [Category("Targeted")]
        [TestCase(true)]
        [TestCase(false)]
        public void TestRestoreWithMissingDblocks(bool deleteIndexFiles)
        {
            var testopts = TestOptions.Expand(new { blocksize = "1kb", no_encryption = true, rebuild_missing_dblock_files = true });

            // 1. Make a backup of a single file
            File.WriteAllText(Path.Combine(DATAFOLDER, "a"), "abc");
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // 2. Delete the dblock file
            var dblockFiles = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly).ToList();
            foreach (var file in dblockFiles)
                File.Delete(file);

            if (deleteIndexFiles)
            {
                var dindexFiles = Directory.GetFiles(TARGETFOLDER, "*.dindex.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var file in dindexFiles)
                    File.Delete(file);
            }

            // 3. Try to repair the remote destination
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Repair();
                Assert.That(res.Errors.Count(), Is.EqualTo(0), "Repair should not have errors");
                TestUtils.AssertResults(c.Test());
            }
        }
    }
}

