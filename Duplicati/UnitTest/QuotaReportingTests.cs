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

        /// <summary>
        /// The destination that stores files but cannot say what it has room for.
        /// </summary>
        private const string UnknownQuotaTarget = "unknownquota://destination";

        [SetUp]
        public void RegisterBackend()
        {
            NoFreeSpaceBackend.Folder = TARGETFOLDER;
            BackendLoader.AddBackend(new NoFreeSpaceBackend());
            BackendLoader.AddBackend(new UnknownQuotaBackend());
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

        /// <summary>
        /// There are two quotas, and --quota-disable only turns off one of them. Its own help text
        /// says so: "Disable the quota reported by the backend. The option --quota-size can still be
        /// used to set a manual quota".
        /// </summary>
        [Test]
        [Category("Quota")]
        public async Task AManualQuotaAppliesEvenWhenTheBackendQuotaIsDisabledAsync()
        {
            // One byte, so the backup is over the manual quota whatever it ends up weighing
            var testopts = TestOptions.Expand(new { no_encryption = true, quota_disable = true, quota_size = "1b" });
            CreateSourceData();

            using (var c = new Controller(FullTarget, testopts, null))
            {
                var results = await c.BackupAsync([DATAFOLDER]);

                Assert.That(results.Errors.Any(x => x.Contains("Assigned quota")), Is.True,
                    "The manual quota was not applied: "
                        + string.Join(System.Environment.NewLine, results.Errors.Concat(results.Warnings)));

                Assert.That(results.Errors.Concat(results.Warnings).Any(x => x.Contains("Backend quota")), Is.False,
                    "The backend quota was reported although it was disabled");
            }
        }

        /// <summary>
        /// The other half of the option, and the guard on the change above: a destination with no
        /// room left says nothing when its quota is disabled.
        /// </summary>
        [Test]
        [Category("Quota")]
        public async Task ADisabledBackendQuotaStaysSilentAsync()
        {
            var testopts = TestOptions.Expand(new { no_encryption = true, quota_disable = true });
            CreateSourceData();

            using (var c = new Controller(FullTarget, testopts, null))
            {
                var results = await c.BackupAsync([DATAFOLDER]);
                var quotaMessages = results.Errors.Concat(results.Warnings).Where(x => x.Contains("quota")).ToList();

                Assert.That(quotaMessages, Is.Empty,
                    "A disabled backend quota was still reported: "
                        + string.Join(System.Environment.NewLine, quotaMessages));
            }
        }

        /// <summary>
        /// A manual quota that is nowhere near being reached says nothing either, so the check is
        /// applied rather than merely being loud.
        /// </summary>
        [Test]
        [Category("Quota")]
        public async Task AManualQuotaThatIsNotReachedSaysNothingAsync()
        {
            var testopts = TestOptions.Expand(new { no_encryption = true, quota_disable = true, quota_size = "100mb" });
            CreateSourceData();

            using (var c = new Controller(FullTarget, testopts, null))
            {
                var results = await c.BackupAsync([DATAFOLDER]);
                var quotaMessages = results.Errors.Concat(results.Warnings).Where(x => x.Contains("quota")).ToList();

                Assert.That(quotaMessages, Is.Empty,
                    "A backup well inside its manual quota reported one anyway: "
                        + string.Join(System.Environment.NewLine, quotaMessages));
            }
        }

        /// <summary>
        /// The manual quota is also what the run reports it used, which is what reaches the command
        /// line summary and the JSON result. Skipping the check left this at its default of zero.
        /// </summary>
        [Test]
        [Category("Quota")]
        public async Task TheManualQuotaIsReportedWhenTheBackendQuotaIsDisabledAsync()
        {
            var testopts = TestOptions.Expand(new { no_encryption = true, quota_disable = true, quota_size = "100mb" });
            CreateSourceData();

            using (var c = new Controller(FullTarget, testopts, null))
            {
                var results = await c.BackupAsync([DATAFOLDER]);
                var stats = (Library.Interface.IParsedBackendStatistics)results.BackendStatistics;

                Assert.That(stats.AssignedQuotaSpace, Is.EqualTo(100 * 1024 * 1024L),
                    "The manual quota was not recorded on the result");
            }
        }

        /// <summary>
        /// A destination that cannot say what it has room for is not a destination with no room:
        /// there is no quota to be close to exceeding, so nothing is reported. A Google shared
        /// drive is exactly this, and reporting the signed-in user's own drive instead is what
        /// issue #4230 was about.
        /// </summary>
        [Test]
        [Category("Quota")]
        public async Task ABackupToADestinationWithNoQuotaSaysNothingAboutQuotaAsync()
        {
            // The most sensitive setting there is: any free space the destination claimed would
            // have to exceed the whole backup to pass without a warning
            var testopts = TestOptions.Expand(new { no_encryption = true, quota_warning_threshold = 100 });
            CreateSourceData();

            using (var c = new Controller(UnknownQuotaTarget, testopts, null))
            {
                var results = await c.BackupAsync([DATAFOLDER]);
                var quotaMessages = results.Errors.Concat(results.Warnings).Where(x => x.Contains("quota")).ToList();

                Assert.That(quotaMessages, Is.Empty,
                    "A destination that reported no quota was treated as one with no room left: "
                        + string.Join(System.Environment.NewLine, quotaMessages));
            }
        }
    }
}
