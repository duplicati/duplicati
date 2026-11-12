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
    /// To browse a backup for a direct restore, the database is rebuilt with the paths only
    /// (repairupdate with only_paths). Such a database has no record of the block volumes, so a
    /// restore with it failed with "Found 2 remote files that are not recorded in local storage".
    /// The restore builds its own temporary database instead, as when there is no local database
    /// </summary>
    [TestFixture]
    public class RestoreFromPartiallyRecreatedDatabaseTests : BasicSetupHelper
    {
        [Test]
        [TestCase(false)]
        [TestCase(true)]
        public async Task AFolderIsRestoredWithADatabaseRebuiltWithThePathsOnlyAsync(bool legacyRestore)
        {
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["no-encryption"] = "true"
            };
            var documents = Path.Combine(this.DATAFOLDER, "Documents");
            Directory.CreateDirectory(documents);
            File.WriteAllText(Path.Combine(documents, "payload.txt"), "payload");
            File.WriteAllText(Path.Combine(this.DATAFOLDER, "peer.txt"), "peer");

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // As the select-files page of ngclient does for a temporary job
            File.Delete(this.DBFILE);
            var pathsOnly = new Dictionary<string, string>(options)
            {
                ["repair-only-paths"] = "true"
            };
            using (var c = new Controller("file://" + this.TARGETFOLDER, pathsOnly, null))
                TestUtils.AssertResults(await c.UpdateDatabaseWithVersionsAsync());

            var restoreOptions = new Dictionary<string, string>(options)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["restore-legacy"] = legacyRestore.ToString()
            };
            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { Library.Common.IO.Util.AppendDirSeparator(documents) + "*" }));

            var restored = Directory.GetFiles(this.RESTOREFOLDER, "*", SearchOption.AllDirectories);
            Assert.That(restored.Select(Path.GetFileName), Is.EqualTo(new[] { "payload.txt" }));
            Assert.That(File.ReadAllText(restored[0]), Is.EqualTo("payload"));
        }
    }
}
