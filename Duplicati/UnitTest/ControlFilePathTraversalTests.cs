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

using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Verifies that a tampered dlist volume cannot make the control file
    /// restore write files outside the restore folder
    /// </summary>
    public class ControlFilePathTraversalTests : BasicSetupHelper
    {
        [Test]
        [Category("Targeted")]
        public async Task RestoreControlFilesRejectsTraversalEntries()
        {
            var testopts = TestOptions;
            testopts.Remove("passphrase");
            testopts["no-encryption"] = "true";

            const string benignName = "control.txt";
            const string escapeName = "escape.txt";
            var canary = "synthetic canary\n";

            File.WriteAllText(Path.Combine(DATAFOLDER, "a"), "some data");

            using (var controlFolder = new TempFolder())
            {
                var controlFile = Path.Combine(controlFolder, benignName);
                File.WriteAllText(controlFile, "benign control file");

                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { control_files = controlFile }), null))
                    TestUtils.AssertResults(await c.BackupAsync(new string[] { DATAFOLDER }));
            }

            // Inject traversal entries into the dlist volume
            var dlistPath = Directory.GetFiles(TARGETFOLDER, "*.dlist.zip", SearchOption.TopDirectoryOnly).Single();
            using (var archive = ZipFile.Open(dlistPath, ZipArchiveMode.Update))
            {
                foreach (var name in new[] { "extra/../" + escapeName, "extra/..\\" + escapeName, "extra/sub/" + escapeName, "extra/" + Path.GetFullPath(Path.Combine(BASEFOLDER, escapeName)) })
                {
                    var entry = archive.CreateEntry(name);
                    using (var s = entry.Open())
                    using (var sw = new StreamWriter(s))
                        sw.Write(canary);
                }
            }

            // Drop the local database so the modified volume is not rejected by the size check
            File.Delete(DBFILE);

            // Restore into a subfolder, so an escaped write would land in RESTOREFOLDER
            var restoreTarget = Path.Combine(RESTOREFOLDER, "target");
            Directory.CreateDirectory(restoreTarget);
            var escapedPath = Path.Combine(RESTOREFOLDER, escapeName);
            var baseEscapedPath = Path.Combine(BASEFOLDER, escapeName);

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { restore_path = restoreTarget }), null))
                TestUtils.AssertResults(await c.RestoreControlFilesAsync(["*"]));

            Assert.That(File.Exists(Path.Combine(restoreTarget, benignName)), Is.True, "Benign control file was not restored");
            Assert.That(File.Exists(escapedPath), Is.False, "Control file was written outside the restore folder");
            Assert.That(File.Exists(baseEscapedPath), Is.False, "Control file was written to an absolute path");
            Assert.That(File.Exists(Path.Combine(restoreTarget, escapeName)), Is.False, "Malformed control file entry was restored");
            Assert.That(Directory.GetFiles(restoreTarget, "*", SearchOption.AllDirectories).Length, Is.EqualTo(1), "Unexpected files in restore folder");
        }
    }
}
