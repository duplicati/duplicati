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
using Duplicati.Library.DynamicLoader;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class Issue3231 : BasicSetupHelper
    {
        [Test]
        [Category("Targeted")]
        public async Task DirectRestoreIgnoresFilesUploadedByAConcurrentBackupAsync([Values] bool legacyRestore)
        {
            var testopts = TestOptions.Expand(new
            {
                no_encryption = true,
            });

            File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "Hello world");
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new string[] { DATAFOLDER }));

            // Make a second backup and hold back its files, so they can appear while the restore runs
            var firstFiles = Directory.GetFiles(TARGETFOLDER).ToHashSet();
            File.WriteAllText(Path.Combine(DATAFOLDER, "b.txt"), "Another file");
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await c.BackupAsync(new string[] { DATAFOLDER }));

            var heldBack = Path.Combine(BASEFOLDER, "heldback");
            Directory.CreateDirectory(heldBack);
            var secondFiles = Directory.GetFiles(TARGETFOLDER).Where(x => !firstFiles.Contains(x)).ToList();
            Assert.That(secondFiles, Is.Not.Empty);
            foreach (var f in secondFiles)
                File.Move(f, Path.Combine(heldBack, Path.GetFileName(f)));

            BackendLoader.AddBackend(new DeterministicErrorBackend());
            DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
            {
                // The concurrent backup uploads its files once the restore has listed the destination
                if (action == DeterministicErrorBackend.BackendAction.GetBefore)
                    foreach (var f in Directory.GetFiles(heldBack))
                        File.Move(f, Path.Combine(TARGETFOLDER, Path.GetFileName(f)));
                return false;
            };

            try
            {
                var restoreopts = new Dictionary<string, string>(testopts)
                {
                    ["no-local-db"] = "true",
                    ["restore-path"] = RESTOREFOLDER,
                    ["restore-legacy"] = legacyRestore ? "true" : "false",
                };
                using (var c = new Library.Main.Controller(new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER, restoreopts, null))
                    TestUtils.AssertResults(await c.RestoreAsync(new string[] { Path.Combine(DATAFOLDER, "a.txt") }));
            }
            finally
            {
                DeterministicErrorBackend.ErrorGenerator = null;
            }

            Assert.That(Directory.GetFiles(heldBack), Is.Empty, "The restore did not download a file");
            Assert.That(File.ReadAllText(Path.Combine(RESTOREFOLDER, "a.txt")), Is.EqualTo("Hello world"));
        }
    }
}
