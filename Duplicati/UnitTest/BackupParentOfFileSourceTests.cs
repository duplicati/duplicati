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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// When a file is chosen as a source, the folder it is in is recorded too, but not the other
    /// entries of that folder. A restore that has to create the folder then gives it the recorded
    /// metadata, as it does for a folder that is chosen as a source (#6863)
    /// </summary>
    [TestFixture]
    public class BackupParentOfFileSourceTests : BasicSetupHelper
    {
        private static readonly DateTime FolderTime = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);

        private string Folder => Path.Combine(this.DATAFOLDER, "path");
        private string ChosenFile => Path.Combine(this.Folder, "file.txt");

        private void CreateSource()
        {
            Directory.CreateDirectory(Path.Combine(this.Folder, "sub"));
            File.WriteAllText(this.ChosenFile, "chosen");
            File.WriteAllText(Path.Combine(this.Folder, "other.txt"), "other");
            Directory.SetLastWriteTimeUtc(this.Folder, FolderTime);
        }

        private async Task<string[]> BackUpAndListAsync(Dictionary<string, string> options)
        {
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.ChosenFile }));

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var listed = await c.ListAsync("*");
                TestUtils.AssertResults(listed);
                return listed.Files.Select(x => x.Path).ToArray();
            }
        }

        [Test]
        [TestCase(false)]
        [TestCase(true)]
        public async Task TheFolderOfAChosenFileIsRecordedAndRestoredAsync(bool excludeEmptyFolders)
        {
            CreateSource();
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["exclude-empty-folders"] = excludeEmptyFolders.ToString()
            };

            var listed = await BackUpAndListAsync(options);
            Assert.That(listed, Is.EquivalentTo(new[] { Library.Common.IO.Util.AppendDirSeparator(this.Folder), this.ChosenFile }),
                "The folder of the chosen file is recorded, and nothing else in it");

            // The restore has to create the folder again
            Directory.Delete(this.Folder, true);
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            Assert.That(File.ReadAllText(this.ChosenFile), Is.EqualTo("chosen"));
            Assert.That(File.Exists(Path.Combine(this.Folder, "other.txt")), Is.False);
            Assert.That(Directory.GetLastWriteTimeUtc(this.Folder), Is.EqualTo(FolderTime),
                "The folder gets its recorded metadata when the restore creates it");
        }

        [Test]
        public async Task AFolderExcludedByAFilterIsNotRecordedAsync()
        {
            CreateSource();
            var options = new Dictionary<string, string>(this.TestOptions);
            var filter = new Library.Utility.FilterExpression("-" + Library.Common.IO.Util.AppendDirSeparator(this.Folder));

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.ChosenFile }, filter));

            // The filter also leaves out the chosen file in the folder, as before
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var listed = await c.ListAsync("*");
                Assert.That(listed.Files.Select(x => x.Path), Does.Not.Contain(Library.Common.IO.Util.AppendDirSeparator(this.Folder)));
            }
        }

        [Test]
        public async Task AFolderThatIsAlsoChosenIsRecordedOnceAsync()
        {
            CreateSource();
            var options = new Dictionary<string, string>(this.TestOptions);
            var folder = Library.Common.IO.Util.AppendDirSeparator(this.Folder);

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.ChosenFile, folder }));

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var listed = (await c.ListAsync("*")).Files.Select(x => x.Path).ToArray();
                Assert.That(listed.Count(x => x == folder), Is.EqualTo(1));
                Assert.That(listed, Does.Contain(Path.Combine(this.Folder, "other.txt")));
            }
        }
    }
}
