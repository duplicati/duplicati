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
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A destination with no room left stops a backup, but says nothing about whether a restore
    /// can be read out of it. Reported as issue #3672, where restoring from a read-only mount
    /// finished correctly and reported an error anyway.
    /// </summary>
    [TestFixture]
    public class QuotaReportingTests : BasicSetupHelper
    {
        /// <summary>
        /// The destination the fake backend keeps its files in.
        /// </summary>
        private const string FullTarget = "nofreespace://destination";

        [SetUp]
        public void RegisterBackend()
        {
            NoFreeSpaceBackend.Folder = TARGETFOLDER;
            BackendLoader.AddBackend(new NoFreeSpaceBackend());
        }

        private void CreateSourceData()
        {
            Directory.CreateDirectory(Path.Combine(DATAFOLDER, "folder"));
            File.WriteAllText(Path.Combine(DATAFOLDER, "folder", "file.txt"), "some data");
        }

        /// <summary>
        /// A restore reads and never writes, so the destination being full cannot affect it.
        /// </summary>
        [Test]
        [Category("Quota")]
        public async Task ARestoreFromAFullDestinationReportsNoQuotaErrorAsync()
        {
            var testopts = TestOptions.Expand(new { no_encryption = true, quota_warning_threshold = 0 });
            CreateSourceData();

            using (var c = new Controller(FullTarget, testopts, null))
                await c.BackupAsync([DATAFOLDER]);

            var restoreFolder = Path.Combine(BASEFOLDER, "restored");
            Directory.CreateDirectory(restoreFolder);

            var restoreopts = TestOptions.Expand(new { no_encryption = true, quota_warning_threshold = 0, restore_path = restoreFolder });
            using (var c = new Controller(FullTarget, restoreopts, null))
            {
                var results = await c.RestoreAsync(null);
                var quotaErrors = results.Errors.Where(x => x.Contains("quota")).ToList();

                Assert.That(quotaErrors, Is.Empty,
                    "A restore reported a quota error for a destination it only reads from: "
                        + string.Join(System.Environment.NewLine, quotaErrors));
            }
        }

        /// <summary>
        /// The same destination has to keep stopping a backup, which does write to it. This fails
        /// if the fix removes the quota check rather than confining it to what can be affected.
        /// </summary>
        [Test]
        [Category("Quota")]
        public async Task ABackupToAFullDestinationStillReportsAQuotaErrorAsync()
        {
            var testopts = TestOptions.Expand(new { no_encryption = true });
            CreateSourceData();

            using (var c = new Controller(FullTarget, testopts, null))
            {
                var results = await c.BackupAsync([DATAFOLDER]);

                Assert.That(results.Errors.Any(x => x.Contains("quota")), Is.True,
                    "A backup to a destination with no room left reported no quota error");
            }
        }
    }
}
