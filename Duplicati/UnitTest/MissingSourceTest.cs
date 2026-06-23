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
