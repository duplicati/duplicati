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
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A filelist is only written once the first backup finishes, so interrupting it leaves a
    /// destination holding blocks and index files and nothing else. Recreating a database from
    /// that used to be refused, which left the backup unrecoverable: the backup asked for a
    /// repair and the repair said there was nothing to repair from. Reported as issue #3546.
    /// </summary>
    public class RecreateWithoutFilelistTests : BasicSetupHelper
    {
        private string Target => "file://" + TARGETFOLDER;

        /// <summary>
        /// The part of the warning that says the destination held no filelists.
        /// </summary>
        private const string MissingFilelistWarning = "No filelists found on the remote destination";

        private int CountRemote(string kind)
            => Directory.GetFiles(TARGETFOLDER).Count(x => Path.GetFileName(x).Contains(kind));

        /// <summary>
        /// Leaves the destination in the state an interrupted first backup leaves it in: the
        /// blocks and index files are there, and the filelist that is written last is not.
        /// Done by removing the filelist rather than by killing a backup, so the state is the
        /// same every run.
        /// </summary>
        private async Task BackupThenRemoveTheFilelistAsync()
        {
            Directory.CreateDirectory(Path.Combine(DATAFOLDER, "folder"));
            for (var i = 0; i < 5; i++)
                File.WriteAllText(Path.Combine(DATAFOLDER, "folder", $"file-{i}.txt"), $"contents {i}");

            using (var c = new Controller(Target, TestOptions, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            foreach (var f in Directory.GetFiles(TARGETFOLDER).Where(x => Path.GetFileName(x).Contains("dlist")))
                File.Delete(f);

            File.Delete(DBFILE);

            Assert.That(CountRemote("dblock"), Is.GreaterThan(0), "The backup left no blocks to recover");
            Assert.That(CountRemote("dlist"), Is.EqualTo(0), "The filelist was not removed");
        }

        /// <summary>
        /// The repair that the backup tells the user to run has to work, and has to say that the
        /// filelists were missing: a destination that did once hold them has lost something.
        /// </summary>
        [Test]
        [Category("RepairHandler")]
        public async Task ARepairSucceedsWithoutAnyFilelistAsync()
        {
            await BackupThenRemoveTheFilelistAsync();

            using var c = new Controller(Target, TestOptions, null);
            var results = await c.RepairAsync();
            TestUtils.AssertResults(results, [MissingFilelistWarning]);

            Assert.That(results.Warnings.Any(x => x.Contains(MissingFilelistWarning)), Is.True,
                "The repair recreated the database without saying the filelists were missing");
        }

        /// <summary>
        /// And it has to leave the blocks that were already uploaded usable, rather than starting
        /// the upload over. This fails if the recreate discards what it could not describe.
        /// </summary>
        [Test]
        [Category("RepairHandler")]
        public async Task TheRecoveredBackupReusesWhatWasAlreadyUploadedAsync()
        {
            await BackupThenRemoveTheFilelistAsync();
            var blocksBefore = CountRemote("dblock");

            using (var c = new Controller(Target, TestOptions, null))
                TestUtils.AssertResults(await c.RepairAsync(), [MissingFilelistWarning]);

            using (var c = new Controller(Target, TestOptions, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            Assert.That(CountRemote("dblock"), Is.EqualTo(blocksBefore),
                "The backup uploaded the data again instead of reusing the blocks that were already there");
            Assert.That(CountRemote("dlist"), Is.GreaterThan(0), "The recovered backup wrote no filelist");
        }

        /// <summary>
        /// A destination with nothing in it at all is still an error. Without this, removing the
        /// refusal altogether would pass the tests above.
        /// </summary>
        [Test]
        [Category("RepairHandler")]
        public void ARepairOfAnEmptyDestinationStillFails()
        {
            using var c = new Controller(Target, TestOptions, null);
            Assert.ThrowsAsync<UserInformationException>(async () => await c.RepairAsync());
        }
    }
}
