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

#nullable enable

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A compact can run for a long time, but it never reported any progress,
    /// so the overall progress stood still and it looked as if it had stalled (#3397).
    /// </summary>
    [TestFixture]
    public class CompactProgressTests : BasicSetupHelper
    {
        /// <summary>
        /// Samples the overall progress, as the server reads it, whenever the backend does something
        /// </summary>
        private sealed class ProgressSampler : IMessageSink
        {
            private volatile IOperationProgress? m_progress;

            public ConcurrentQueue<(OperationPhase Phase, float Progress)> Samples { get; } = new();

            public void BackendEvent(BackendActionType action, BackendEventType type, string path, long size)
                => Sample();

            public void SetBackendProgress(IBackendProgress progress) { }
            public void SetOperationProgress(IOperationProgress progress) => m_progress = progress;
            public void WriteMessage(LogEntry entry) { }

            public void Sample()
            {
                var progress = m_progress;
                if (progress == null)
                    return;
                progress.UpdateOverall(out var phase, out var pg, out _, out _, out _, out _, out _);
                Samples.Enqueue((phase, pg));
            }
        }

        private System.Collections.Generic.Dictionary<string, string> m_options = null!;

        /// <summary>
        /// Makes a backup, then removes every other file so each volume is left half used
        /// </summary>
        private async Task BackupAndThinOutAsync()
        {
            m_options = TestOptions.Expand(new
            {
                keep_versions = 1,
                no_auto_compact = true,
                zip_compression_level = 0,
                blocksize = "100kb",
                dblock_size = "1mb",
                no_encryption = true,
            });

            for (var i = 0; i < 30; i++)
            {
                var data = new byte[300 * 1024];
                Random.Shared.NextBytes(data);
                File.WriteAllBytes(Path.Combine(DATAFOLDER, $"a{i}"), data);
            }

            using (var c = new Controller("file://" + TARGETFOLDER, m_options, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            for (var i = 0; i < 30; i += 2)
                File.Delete(Path.Combine(DATAFOLDER, $"a{i}"));
        }

        [Test]
        [Category("Targeted")]
        public async Task CompactReportsProgressAsItGoesAsync()
        {
            await BackupAndThinOutAsync();
            using (var c = new Controller("file://" + TARGETFOLDER, m_options, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            var sampler = new ProgressSampler();
            using (var c = new Controller("file://" + TARGETFOLDER, m_options, sampler))
            {
                var result = await c.CompactAsync();
                TestUtils.AssertResults(result);
                Assert.That(result.DownloadedFileCount, Is.GreaterThan(1), "The compact did not repack any volumes");
            }
            sampler.Sample();

            var values = sampler.Samples.Select(x => x.Progress).ToArray();
            Assert.That(values, Has.Some.GreaterThan(0f).And.LessThan(1f), "The compact reported no progress while it ran");
            Assert.That(values, Is.Ordered, "The compact progress went backwards");
            Assert.That(values.Last(), Is.EqualTo(1f), "The compact did not report that it finished");
        }

        [Test]
        [Category("Targeted")]
        public async Task CompactDuringBackupReportsProgressInItsRangeAsync()
        {
            await BackupAndThinOutAsync();

            // The second backup deletes the first version, which then compacts the volumes it left half used
            m_options["no-auto-compact"] = "false";
            var sampler = new ProgressSampler();
            using (var c = new Controller("file://" + TARGETFOLDER, m_options, sampler))
            {
                var result = await c.BackupAsync([DATAFOLDER]);
                TestUtils.AssertResults(result);
                Assert.That(result.CompactResults?.DownloadedFileCount, Is.GreaterThan(1), "The backup did not repack any volumes");
            }

            var estimates = sampler.Samples
                .Where(x => x.Phase == OperationPhase.Backup_Delete)
                .Select(x => OverallProgressEstimator.Estimate(x.Phase, x.Progress, 0, 0, 0, false, 0, false))
                .ToArray();

            Assert.That(estimates, Is.Not.Empty, "No samples were taken while the backup deleted and compacted");
            Assert.That(estimates, Is.All.InRange(0.95f, 0.98f), "The compact progress left the range the backup gives it");
            Assert.That(estimates, Has.Some.GreaterThan(0.95f), "The compact reported no progress during the backup");
        }
    }
}
