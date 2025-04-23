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
using System.Threading;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class Issue6127 : BasicSetupHelper
    {
        [Test]
        [Category("Targeted")]
        public void TestMultiRetentionOptions()
        {
            var testopts = TestOptions;
            testopts.Add("upload-unchanged-backups", "true");

            // Prepare some data
            var data = new byte[1024 * 1024 * 10];
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);

            // Make three backups
            for (var i = 0; i < 3; i++)
            {
                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                {
                    var res = c.Backup([DATAFOLDER]);
                    TestUtils.AssertResults(res);
                    Assert.AreEqual(i + 1, c.List(null).Filesets.Count());
                }
                Thread.Sleep(1000);
            }

            // Make sure we have something to delete
            Thread.Sleep(1000);

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 2, retention_policy = "1s:U" }), null))
                TestUtils.AssertResults(c.Delete());

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 2, retention_policy = "1s:U" }), null))
                Assert.AreEqual(1, c.List(null).Filesets.Count());
        }
    }
}

