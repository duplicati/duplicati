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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Tmds.DBus.Protocol;

namespace Duplicati.UnitTest
{
    public class Issue6070 : BasicSetupHelper
    {
        [Test]
        [Category("Targeted")]
        public void TestPurgeFindsCorrectBlockSize()
        {
            // 1. Prepare some data
            var longdata = new byte[32 * 1024 + 5];
            var shortdata = new byte[32];
            Random.Shared.NextBytes(longdata);
            Random.Shared.NextBytes(shortdata);

            // 2. Set options 
            var testopts = TestOptions.Expand(new
            {
                blocksize = "1kb",
                disable_replace_missing_metadata = true,
            });

            // 3. Backup the large file
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), longdata);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            var existingDIndexAndDblockFiles = Directory.GetFiles(TARGETFOLDER)
                .Where(f => f.Contains(".dindex.") || f.Contains(".dblock."))
                .ToHashSet();

            // 4. Backup the small file
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b"), shortdata);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // 5. Delete dindex and dblock files from second backup
            foreach (var file in Directory.GetFiles(TARGETFOLDER)
                .Where(f => f.Contains(".dindex.") || f.Contains(".dblock."))
                .Except(existingDIndexAndDblockFiles)
                .ToList())
            {
                File.Delete(file);
            }

            // 6. Delete blocksize override
            testopts = TestOptions.Expand(new
            {
                disable_replace_missing_metadata = true,
            });
            testopts.Remove("blocksize");

            // Note: The controller will modify the options dictionary,
            // so we need to create a new one to emulate command line
            // behavior where the options are not modified.            
            var opts1 = new Dictionary<string, string>(testopts);
            var opts2 = new Dictionary<string, string>(testopts);

            // 7. Recreate and expect an error due to missing data
            File.Delete(DBFILE);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, opts1, null))
                Assert.Throws<UserInformationException>(() => c.Repair());

            // 8. Run purge-broken-files
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, opts2, null))
                TestUtils.AssertResults(c.PurgeBrokenFiles(null));

        }
    }
}

