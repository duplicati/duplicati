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
using System.Diagnostics;
using System.Threading;
using Duplicati.GUI.TrayIcon;
using Duplicati.Library.Interface;
using Duplicati.WebserverCore.Abstractions;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// When the tray icon exits, it asks its hosted server to stop and waits for it. A server that is stuck in its
/// shutdown was interrupted, which breaks whichever wait it is in, and it ran on a foreground thread, so if it
/// was still stuck the process kept running with no tray icon (#4533).
/// </summary>
[Category("Targeted")]
public class TrayHostedServerStopTests
{
    private sealed class Settings : IApplicationSettings
    {
        private readonly CancellationTokenSource _exit = new();

        public Action? StartOrStopUsageReporter { get; set; }
        public string DataFolder => string.Empty;
        public string Origin { get; set; } = "Tray icon";
        public CancellationToken ApplicationExit => _exit.Token;
        public void SignalApplicationExit() => _exit.Cancel();
        public ISecretProvider? SecretProvider { get; set; }
        public ISecretProvider? DefaultSecretProvider { get; set; }
        public bool SettingsEncryptionKeyProvidedExternally { get; set; }
    }

    [Test]
    public void ServerThatStopsWhenAskedIsWaitedFor()
    {
        var settings = new Settings();
        using var started = new ManualResetEvent(false);
        var stopped = false;

        var keeper = new HostedInstanceKeeper(settings, () =>
        {
            started.Set();
            settings.ApplicationExit.WaitHandle.WaitOne();
            Thread.Sleep(200);
            stopped = true;
        }, started, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        var shutdown = 0;
        keeper.InstanceShutdown = () => shutdown++;
        keeper.Dispose();

        Assert.That(stopped, Is.True, "Dispose returned before the server stopped");
        Assert.That(shutdown, Is.EqualTo(1));
    }

    [Test]
    public void StuckServerIsNotInterruptedAndDoesNotKeepTheProcessRunning()
    {
        var settings = new Settings();
        using var started = new ManualResetEvent(false);
        using var release = new ManualResetEventSlim(false);
        Thread? runner = null;
        var interrupted = false;

        var keeper = new HostedInstanceKeeper(settings, () =>
        {
            runner = Thread.CurrentThread;
            started.Set();
            try
            {
                // A shutdown step that does not finish when the server is asked to stop
                release.Wait();
            }
            catch (ThreadInterruptedException)
            {
                interrupted = true;
            }
        }, started, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));

        try
        {
            var sw = Stopwatch.StartNew();
            keeper.Dispose();
            sw.Stop();

            Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(8)), "Dispose waited too long for a stuck server");
            Assert.That(interrupted, Is.False, "The stuck server was interrupted");
            Assert.That(runner, Is.Not.Null);
            Assert.That(runner!.IsAlive, Is.True);
            Assert.That(runner.IsBackground, Is.True, "A stuck server on a foreground thread keeps the process running");
        }
        finally
        {
            release.Set();
            runner?.Join(TimeSpan.FromSeconds(10));
        }
    }
}
