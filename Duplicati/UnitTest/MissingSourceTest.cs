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
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class MissingSourceTest : BasicSetupHelper
    {
        /// <summary>
        /// A source path that is guaranteed not to exist
        /// </summary>
        private string MissingFolder => Path.Combine(BASEFOLDER, "this-source-does-not-exist") + Path.DirectorySeparatorChar;

        /// <summary>
        /// Writes a file into the (existing, but empty) DATAFOLDER so it is a valid, non-empty source
        /// </summary>
        private void PopulateDataFolder()
        {
            TestUtils.WriteTestFile(Path.Combine(DATAFOLDER, "file.txt"), 1024);
        }

        [Test]
        [Category("Targeted")]
        public async Task BackupContinuesWhenSourceIsMissingByDefaultAsync()
        {
            // With the default options, a missing source should only emit a warning and continue,
            // as long as at least one valid source remains.
            PopulateDataFolder();
            var testopts = TestOptions.Expand(new { });

            using var controller = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null);
            var result = await controller.BackupAsync(new[] { DATAFOLDER, MissingFolder });

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Warnings.Any(w => w.Contains("does not exist")), Is.True,
                "A warning should be emitted for the missing source");
        }

        [Test]
        [Category("Targeted")]
        public void BackupAbortsWhenSourceIsMissingAndAbortOptionSet()
        {
            // When abort-if-source-missing is set, a missing source should abort the backup.
            PopulateDataFolder();
            var testopts = TestOptions.Expand(new { abort_if_source_missing = true });

            using var controller = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null);
            var ex = Assert.ThrowsAsync<UserInformationException>(() => controller.BackupAsync(new[] { DATAFOLDER, MissingFolder }));
            Assert.That(ex!.HelpID, Is.EqualTo("SourceIsMissing"));
        }

        [Test]
        [Category("Targeted")]
        public async Task BackupDoesNotWarnWhenSourceIsMissingAndAllowOptionSetAsync()
        {
            // When allow-missing-source is set, a missing source should be silently skipped.
            PopulateDataFolder();
            var testopts = TestOptions.Expand(new { allow_missing_source = true });

            using var controller = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null);
            var result = await controller.BackupAsync(new[] { DATAFOLDER, MissingFolder });

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Warnings.Any(w => w.Contains("does not exist")), Is.False,
                "No missing-source warning should be emitted when allow-missing-source is set");
        }

        /// <summary>
        /// Backs up the sources, removes one of them, adds a file to the data folder so the second
        /// backup makes a new version, and backs up again
        /// </summary>
        /// <returns>The result of the second backup, and the paths in the latest version</returns>
        private async Task<(IBackupResults Result, string[] Paths)> BackUpRemoveAndBackUpAsync(string[] sources, string removed, object options)
        {
            var testopts = TestOptions.Expand(options);
            using (var controller = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(await controller.BackupAsync(sources));

            if (Directory.Exists(removed))
                Directory.Delete(removed, true);
            else
                File.Delete(removed);
            TestUtils.WriteTestFile(Path.Combine(DATAFOLDER, "added.txt"), 512);

            IBackupResults result;
            using (var controller = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                result = await controller.BackupAsync(sources);

            using (var controller = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var listed = await controller.ListAsync("*");
                TestUtils.AssertResults(listed);
                return (result, listed.Files.Select(x => x.Path).ToArray());
            }
        }

        [Test]
        [Category("Targeted")]
        public async Task FilesOfMissingFolderSourceAreKeptWhenKeepOptionSetAsync()
        {
            PopulateDataFolder();
            var other = Path.Combine(BASEFOLDER, "other-source") + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(Path.Combine(other, "sub"));
            TestUtils.WriteTestFile(Path.Combine(other, "sub", "kept.txt"), 1024);

            var (result, paths) = await BackUpRemoveAndBackUpAsync([DATAFOLDER, other], other, new { allow_missing_source = true, keep_missing_sources = true });

            Assert.That(result.DeletedFiles, Is.Zero, "The files of the missing source were recorded as deleted");
            Assert.That(paths, Does.Contain(Path.Combine(other, "sub", "kept.txt")));
            Assert.That(paths, Does.Contain(Path.Combine(other, "sub") + Path.DirectorySeparatorChar));
            Assert.That(paths, Does.Contain(Path.Combine(DATAFOLDER, "added.txt")), "The second backup did not make a new version");
        }

        [Test]
        [Category("Targeted")]
        public async Task MissingFileSourceIsKeptWhenKeepOptionSetAsync()
        {
            PopulateDataFolder();
            var otherFolder = Path.Combine(BASEFOLDER, "other-source");
            Directory.CreateDirectory(otherFolder);
            var file = Path.Combine(otherFolder, "kept.txt");
            TestUtils.WriteTestFile(file, 1024);
            TestUtils.WriteTestFile(Path.Combine(otherFolder, "not-a-source.txt"), 1024);

            var (result, paths) = await BackUpRemoveAndBackUpAsync([DATAFOLDER, file], file, new { keep_missing_sources = true });

            Assert.That(result.Warnings.Any(w => w.Contains("does not exist")), Is.True, "The missing source is still warned about by default");
            Assert.That(result.DeletedFiles, Is.Zero);
            Assert.That(paths, Does.Contain(file));
            Assert.That(paths, Does.Not.Contain(Path.Combine(otherFolder, "not-a-source.txt")));
        }

        [Test]
        [Category("Targeted")]
        public async Task FilesOfMissingSourceAreDeletedWithoutKeepOptionAsync()
        {
            PopulateDataFolder();
            var other = Path.Combine(BASEFOLDER, "other-source") + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(other);
            TestUtils.WriteTestFile(Path.Combine(other, "gone.txt"), 1024);

            var (result, paths) = await BackUpRemoveAndBackUpAsync([DATAFOLDER, other], other, new { allow_missing_source = true });

            Assert.That(result.DeletedFiles, Is.GreaterThan(0));
            Assert.That(paths, Does.Not.Contain(Path.Combine(other, "gone.txt")));
        }

        [Test]
        [Category("Targeted")]
        public async Task MissingSourceInsideAnotherSourceIsNotKeptAsync()
        {
            // A folder removed from inside another source is gone from that source as well,
            // so it must not come back from the previous version
            PopulateDataFolder();
            var inner = Path.Combine(DATAFOLDER, "inner") + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(inner);
            TestUtils.WriteTestFile(Path.Combine(inner, "gone.txt"), 1024);

            var (_, paths) = await BackUpRemoveAndBackUpAsync([DATAFOLDER, inner], inner, new { allow_missing_source = true, keep_missing_sources = true });

            Assert.That(paths, Does.Not.Contain(Path.Combine(inner, "gone.txt")));
            Assert.That(paths, Does.Not.Contain(inner));
        }

        [Test]
        [Category("Targeted")]
        public void BackupAbortsWhenAllSourcesMissing()
        {
            // If every source is missing, the backup would be empty, so it should abort even
            // with the default (non-abort) options.
            var testopts = TestOptions.Expand(new { });

            using var controller = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null);
            var ex = Assert.ThrowsAsync<UserInformationException>(() => controller.BackupAsync(new[] { MissingFolder }));
            Assert.That(ex!.HelpID, Is.EqualTo("NoSources"));
        }
    }
}
