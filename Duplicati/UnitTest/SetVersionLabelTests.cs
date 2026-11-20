// Copyright (C) 2026, The Duplicati Team
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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Setting the label of a backup version stores it in the local database, except in a dry run
    /// </summary>
    public class SetVersionLabelTests : BasicSetupHelper
    {
        [Test]
        [Category("VersionLabel")]
        [TestCase(true)]
        [TestCase(false)]
        public async Task ADryRunDoesNotStoreTheLabel_Async(bool dryrun)
        {
            var options = new Dictionary<string, string>(TestOptions) { ["no-encryption"] = "true" };
            File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "content");
            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]).ConfigureAwait(false));

            using (var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(options)
            {
                ["version"] = "0",
                ["version-name"] = "before the upgrade",
                ["dry-run"] = dryrun ? "true" : "false"
            }, null))
            {
                var results = await c.SetVersionLabelAsync().ConfigureAwait(false);
                TestUtils.AssertResults(results);
                Assert.That(results.Label, Is.EqualTo("before the upgrade"), "The results should say which label is meant");
            }

            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            {
                var label = (await c.ListAsync().ConfigureAwait(false)).Filesets.Single().Label;
                if (dryrun)
                    Assert.That(label, Is.Null.Or.Empty, "A dry run should not store the label");
                else
                    Assert.That(label, Is.EqualTo("before the upgrade"));
            }
        }
    }
}
