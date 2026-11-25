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
using Duplicati.Library.Main.Operation.Backup;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A backup holds up to (compressors + uploads) volumes in the temporary folder at once. When
    /// the folder is too small for that, such as a RAM disk, the backup failed part way through
    /// without saying why (#4157). The numbers the user did not set are lowered to fit, and the
    /// backup warns when they still do not fit.
    /// </summary>
    [TestFixture]
    public class TemporarySpaceLimiterTests : BasicSetupHelper
    {
        private const long MB = 1024 * 1024;

        [Test]
        [Category("Targeted")]
        public void EnoughSpaceKeepsTheNumbers()
        {
            var plan = TemporarySpaceLimiter.Decide(1000 * MB, 50 * MB, 6, false, 4, false);

            Assert.That((plan.Compressors, plan.Uploads, plan.Fits), Is.EqualTo((6, 4, true)));
        }

        [Test]
        [Category("Targeted")]
        public void TooLittleSpaceLowersTheLargerNumberFirst()
        {
            // 6 + 4 volumes of 500 MB need 5 GB; 4 GB holds 8 of them
            var plan = TemporarySpaceLimiter.Decide(4000 * MB, 500 * MB, 6, false, 4, false);

            Assert.That((plan.Compressors, plan.Uploads, plan.Fits), Is.EqualTo((4, 4, true)));
            Assert.That(plan.Needed, Is.EqualTo(4000 * MB));
        }

        [Test]
        [Category("Targeted")]
        public void NumbersTheUserSetAreKept()
        {
            var plan = TemporarySpaceLimiter.Decide(2000 * MB, 500 * MB, 6, true, 4, false);

            Assert.That((plan.Compressors, plan.Uploads, plan.Fits), Is.EqualTo((6, 1, false)));
        }

        [Test]
        [Category("Targeted")]
        public void NoNumberGoesBelowOne()
        {
            var plan = TemporarySpaceLimiter.Decide(100 * MB, 500 * MB, 6, false, 4, false);

            Assert.That((plan.Compressors, plan.Uploads, plan.Fits), Is.EqualTo((1, 1, false)));
        }

        /// <summary>
        /// Volumes far larger than any disk cannot fit, so the backup lowers the compressors, warns,
        /// and still runs: a backup with few changes writes only one small volume
        /// </summary>
        [Test]
        [Category("Targeted")]
        public async Task ABackupWithTooLittleTemporarySpaceLowersTheCompressorsAndWarnsAsync()
        {
            File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "some data");
            var options = new Dictionary<string, string>(TestOptions) { ["dblock-size"] = "1000TB" };
            options.Remove("concurrency-compressors");

            using var c = new Controller("file://" + TARGETFOLDER, options, null);
            var result = await c.BackupAsync([DATAFOLDER]);

            Assert.That(result.Errors, Is.Empty);
            var warnings = result.Warnings.Where(x => x.Contains("TemporarySpaceLow")).ToList();
            Assert.That(warnings, Has.Count.EqualTo(System.Environment.ProcessorCount >= 4 ? 2 : 1), string.Join("; ", result.Warnings));
            Assert.That(warnings.Last(), Does.Contain("1 compressors and 1 uploads"));
            if (System.Environment.ProcessorCount >= 4)
                Assert.That(warnings.First(), Does.Contain("runs with 1 compressors and 1 uploads instead"));
        }

        [Test]
        [Category("Targeted")]
        public async Task ABackupWithEnoughTemporarySpaceDoesNotWarnAsync()
        {
            File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "some data");

            using var c = new Controller("file://" + TARGETFOLDER, TestOptions, null);
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
        }
    }
}
