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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.WebserverCore.Abstractions;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests for the loop the Agent uses to run the webserver it hosts
    /// </summary>
    [TestFixture]
    public class AgentRunServerTests
    {
        private sealed class TestApplicationSettings : IApplicationSettings
        {
            private readonly CancellationTokenSource _applicationExitEvent = new();
            public Action? StartOrStopUsageReporter { get; set; }
            public string DataFolder => string.Empty;
            public string Origin { get; set; } = "UnitTest";
            public CancellationToken ApplicationExit => _applicationExitEvent.Token;
            public void SignalApplicationExit() => _applicationExitEvent.Cancel();
            public ISecretProvider? SecretProvider { get; set; }
            public ISecretProvider? DefaultSecretProvider { get; set; }
            public bool SettingsEncryptionKeyProvidedExternally { get; set; }
        }

        [Test]
        [Category("Agent")]
        public async Task ServerExitAfterExitSignalStopsTheLoop()
        {
            var applicationSettings = new TestApplicationSettings();
            var runs = 0;

            // Simulate a server that is asked to exit, e.g. by "shutdown" on the ping-pong channel
            int RunServer(IApplicationSettings settings, string[] args)
            {
                Interlocked.Increment(ref runs);
                settings.SignalApplicationExit();
                return 0;
            }

            using var cts = new CancellationTokenSource();
            // Run on the thread pool, as the loop does not yield while it restarts the server
            var task = Task.Run(() => Agent.Program.RunServerAsync(applicationSettings, [], cts.Token, RunServer));
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5))) == task;

            // Stop the loop if it is still running
            cts.Cancel();
            await task;

            Assert.IsTrue(completed, $"The loop kept restarting the server after it exited ({runs} runs)");
            Assert.AreEqual(1, runs);
        }
    }
}
