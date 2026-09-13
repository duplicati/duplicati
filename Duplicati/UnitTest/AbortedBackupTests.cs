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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A file backend whose uploads stall until they are cancelled or released, the way a
    /// destination that has stopped answering behaves. Whether the stall observes the
    /// cancellation token is a switch, so a test can stand in for a backend that does and for
    /// one that does not.
    /// </summary>
    public class StallingBackend : IBackend, IStreamingBackend
    {
        public const string Key = "stall";

        public static volatile bool Stall;
        /// <summary>When set, the stall ignores the cancellation token, like a transfer that does not observe it</summary>
        public static volatile bool IgnoreCancellation;
        public static readonly ManualResetEventSlim Release = new(false);
        public static int StalledPuts;
        /// <summary>The number of uploads that have entered the backend and not yet left it</summary>
        public static int PutsInFlight;

        private IStreamingBackend m_backend;

        public StallingBackend() { }

        public StallingBackend(string url, Dictionary<string, string> options)
        {
            var u = new Library.Utility.RelaxedUri(url).SetScheme("file").ToString();
            m_backend = (IStreamingBackend)Library.DynamicLoader.BackendLoader.GetBackend(u, options);
        }

        private static async Task StallAsync(string remotename, CancellationToken cancelToken)
        {
            if (!Stall)
                return;
            Interlocked.Increment(ref StalledPuts);
            if (IgnoreCancellation)
                await Task.Run(() => Release.Wait()).ConfigureAwait(false);
            else
                await Task.Delay(Timeout.Infinite, cancelToken).ConfigureAwait(false);
        }

        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            Interlocked.Increment(ref PutsInFlight);
            try
            {
                await StallAsync(remotename, cancelToken).ConfigureAwait(false);
                await m_backend.PutAsync(remotename, stream, cancelToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref PutsInFlight);
            }
        }
        public Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken) => m_backend.GetAsync(remotename, stream, cancelToken);
        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancelToken) => m_backend.ListAsync(cancelToken);
        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            Interlocked.Increment(ref PutsInFlight);
            try
            {
                await StallAsync(remotename, cancelToken).ConfigureAwait(false);
                await m_backend.PutAsync(remotename, filename, cancelToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref PutsInFlight);
            }
        }
        public Task GetAsync(string remotename, string filename, CancellationToken cancelToken) => m_backend.GetAsync(remotename, filename, cancelToken);
        public Task DeleteAsync(string remotename, CancellationToken cancelToken) => m_backend.DeleteAsync(remotename, cancelToken);
        public Task TestAsync(bool alsoWrite, CancellationToken cancelToken) => m_backend.TestAsync(alsoWrite, cancelToken);
        public Task CreateFolderAsync(CancellationToken cancelToken) => m_backend.CreateFolderAsync(cancelToken);
        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => m_backend.GetDNSNamesAsync(cancelToken);
        public string DisplayName => "Stalling test backend";
        public string ProtocolKey => Key;
        public IList<ICommandLineArgument> SupportedCommands
            => m_backend?.SupportedCommands ?? Library.DynamicLoader.BackendLoader.GetSupportedCommands("file://").ToList();
        public string Description => "A testing backend whose uploads stall until cancelled";
        public bool SupportsStreaming => m_backend?.SupportsStreaming ?? false;
        public void Dispose() => m_backend?.Dispose();
    }

    [TestFixture]
    public class AbortedBackupTests : BasicSetupHelper
    {
        /// <summary>
        /// "Stop now" on a backup whose uploads are stuck. With an upload that observes its
        /// cancellation token the backend call ends itself and everything queued behind it
        /// unwinds. With one that does not - a transfer stuck in a socket write, a stream copy
        /// that was never given the token - the backend manager has to stop waiting for it,
        /// or the queue behind it, and with it the whole backup, never returns.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("Targeted")]
        public async Task AbortedBackupWithStalledUploadsReturnsAsync(bool uploadIgnoresCancellation)
        {
            var rng = new Random(42);
            var data = new byte[64 * 1024];
            for (var i = 0; i < 40; i++)
            {
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(this.DATAFOLDER, $"file{i}"), data);
            }

            Library.DynamicLoader.BackendLoader.AddBackend(new StallingBackend());
            StallingBackend.IgnoreCancellation = uploadIgnoresCancellation;
            StallingBackend.Release.Reset();
            StallingBackend.StalledPuts = 0;
            StallingBackend.PutsInFlight = 0;
            StallingBackend.Stall = true;

            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["dblock-size"] = "100kb",
                ["blocksize"] = "4kb",
                // Two uploads in flight, so the block producers queue up behind them
                ["asynchronous-upload-limit"] = "2",
                ["number-of-retries"] = "0",
                ["snapshot-policy"] = "off",
            };

            try
            {
                using var c = new Controller(StallingBackend.Key + "://" + this.TARGETFOLDER, options, null);
                var backupTask = Task.Run(async () => await c.BackupAsync(new[] { this.DATAFOLDER }));

                // Wait until both upload slots are stuck, then give the producers time to queue up behind them
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (StallingBackend.StalledPuts < 2 && sw.Elapsed < TimeSpan.FromMinutes(1) && !backupTask.IsCompleted)
                    await Task.Delay(100);
                Assert.IsFalse(backupTask.IsCompleted, "The backup finished before its uploads stalled");
                Assert.AreEqual(2, StallingBackend.StalledPuts, "The uploads did not stall within a minute");
                await Task.Delay(3000);

                await c.AbortAsync();

                var stopped = await Task.WhenAny(backupTask, Task.Delay(TimeSpan.FromMinutes(2))) == backupTask;
                Assert.IsTrue(stopped, "The abort did not make the backup return within two minutes");
                Assert.ThrowsAsync<TaskCanceledException>(async () => await backupTask, "An aborted backup should end with the cancellation, not with a result");
            }
            finally
            {
                StallingBackend.Stall = false;
                // Let an abandoned upload finish, and wait until it has. It writes into the
                // target folder after the backup has returned, and the teardown removes that
                // folder; on Windows a file that is still open cannot be deleted.
                StallingBackend.Release.Set();
                var drain = System.Diagnostics.Stopwatch.StartNew();
                while (StallingBackend.PutsInFlight > 0 && drain.Elapsed < TimeSpan.FromSeconds(30))
                    await Task.Delay(50);
            }
        }
    }
}
