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

using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The check of the local database at the start of a backup can take a long time on a large
    /// database, and it was shown as "Verifying backend data", so it looked like a check of the
    /// remote data (#4671). It has a phase of its own, and the check of the remote data follows.
    /// </summary>
    [TestFixture]
    public class DatabaseVerifyPhaseTests : BasicSetupHelper
    {
        /// <summary>
        /// Records every phase the operation enters, in order
        /// </summary>
        private sealed class PhaseRecorder : IMessageSink
        {
            public ConcurrentQueue<OperationPhase> Phases { get; } = new();

            public void BackendEvent(BackendActionType action, BackendEventType type, string path, long size) { }
            public void SetBackendProgress(IBackendProgress progress) { }
            public void SetOperationProgress(IOperationProgress progress)
                => progress.PhaseChanged += (phase, _) => Phases.Enqueue(phase);
            public void WriteMessage(LogEntry entry) { }
        }

        [Test]
        [Category("Targeted")]
        public async Task TheDatabaseCheckHasItsOwnPhaseBeforeTheRemoteCheckAsync()
        {
            File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "some data");
            using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            File.WriteAllText(Path.Combine(DATAFOLDER, "b.txt"), "more data");
            var recorder = new PhaseRecorder();
            using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, recorder))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            var phases = recorder.Phases.ToList();
            var all = string.Join(", ", phases);
            var database = phases.IndexOf(OperationPhase.Backup_VerifyDatabase);
            var remote = phases.IndexOf(OperationPhase.Backup_PreBackupVerify);
            var files = phases.IndexOf(OperationPhase.Backup_ProcessingFiles);

            Assert.That(database, Is.GreaterThanOrEqualTo(0), "The database check should have a phase: " + all);
            Assert.That(remote, Is.GreaterThan(database), "The remote check should follow the database check: " + all);
            Assert.That(files, Is.GreaterThan(remote), "The files should be processed after both checks: " + all);
        }
    }
}
