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
using NUnit.Framework;
using NUnit.Framework.Internal;
using Duplicati.Library.DynamicLoader;

namespace Duplicati.UnitTest
{
    public class Issue6235 : BasicSetupHelper
    {
        [Test]
        [Category("Targeted")]
        public void RepairWithDlistRetries([Values(1, 2)] int keepVersions)
        {
            var testopts = TestOptions.Expand(new
            {
                no_encryption = true,
                number_of_retries = 5,
                retry_delay = "0s",
                keep_versions = keepVersions,
                upload_unchanged_backups = true,
            });

            // Create a file 
            File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "Hello world");

            // Make a backup
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));

            BackendLoader.AddBackend(new DeterministicErrorBackend());
            var count = 0;
            DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
            {
                if (action.IsPutOperation && remotename.Contains(".dlist."))
                {
                    count++;
                    if (count < 2)
                        return true;
                }
                return false;
            };

            // Make a backup where two attemps to upload the dlist file fails
            using (var c = new Library.Main.Controller(new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));

            Assert.That(count, Is.EqualTo(3), "Did not retry the dlist upload");

            // Check that repair works
            using (var c = new Library.Main.Controller(new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());

            // Check that recreate works
            File.Delete(DBFILE);
            DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
            {
                if (action.IsGetOperation && remotename.Contains(".dblock."))
                    return true;
                return false;
            };

            using (var c = new Library.Main.Controller(new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());
        }
    }
}

