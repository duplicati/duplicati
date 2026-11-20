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
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A repair with --dry-run reports what it would do and changes nothing
    /// </summary>
    public class RepairDryRunTests : BasicSetupHelper
    {
        /// <summary>
        /// Without a local database, a repair recreates it. The dry run recreates it in a temporary file,
        /// and must not then look for the database it did not write.
        /// </summary>
        [Test]
        [Category("Repair")]
        public async Task ADryRunRecreateWithoutADatabaseCompletes_Async()
        {
            var options = new Dictionary<string, string>(TestOptions) { ["no-encryption"] = "true" };
            File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "content");
            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]).ConfigureAwait(false));

            File.Delete(DBFILE);
            var remoteFiles = Directory.GetFiles(TARGETFOLDER);

            using (var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(options) { ["dry-run"] = "true" }, null))
                TestUtils.AssertResults(await c.RepairAsync().ConfigureAwait(false));

            Assert.That(File.Exists(DBFILE), Is.False, "The dry run should not write the database");
            Assert.That(Directory.GetFiles(TARGETFOLDER), Is.EquivalentTo(remoteFiles), "The dry run should not change the destination");
        }
    }
}
