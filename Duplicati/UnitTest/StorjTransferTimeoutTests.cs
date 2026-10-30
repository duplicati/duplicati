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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.Storj;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// uplink.NET runs a Storj transfer on its own thread and takes no token, so a transfer that
/// stopped moving on the network was waited for forever. The transfers stand in for uplink.NET's,
/// which cannot run without the native library and a satellite.
/// </summary>
[TestFixture]
public class StorjTransferTimeoutTests
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The longest a test waits for the transfer wait to return, so that a wait that never
    /// returns fails the test instead of hanging it
    /// </summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private static async Task<Exception?> WaitGuardedAsync(Task wait)
    {
        if (await Task.WhenAny(wait, Task.Delay(Guard)) != wait)
            Assert.Fail($"The transfer wait did not return within {Guard.TotalSeconds} seconds");

        try
        {
            await wait;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Test]
    public async Task ATransferThatStopsMovingTimesOut()
    {
        var stalled = new TaskCompletionSource();
        var cancelled = false;
        var sw = Stopwatch.StartNew();

        var ex = await WaitGuardedAsync(Storj.WaitForTransferAsync(stalled.Task, () => 0, () => cancelled = true, IdleTimeout, CancellationToken.None));

        Assert.That(ex, Is.InstanceOf<TimeoutException>());
        Assert.That(sw.Elapsed, Is.GreaterThanOrEqualTo(IdleTimeout));
        Assert.That(cancelled, Is.True, "The transfer was not told to stop");
    }

    [Test]
    public async Task ASlowTransferThatKeepsMovingIsNotCutOff()
    {
        long moved = 0;
        var cancelled = false;
        var transfer = Task.Run(async () =>
        {
            // Moves a little every 300 ms for three times the timeout
            for (var i = 0; i < 10; i++)
            {
                await Task.Delay(300);
                Interlocked.Increment(ref moved);
            }
        });

        var ex = await WaitGuardedAsync(Storj.WaitForTransferAsync(transfer, () => Interlocked.Read(ref moved), () => cancelled = true, IdleTimeout, CancellationToken.None));

        Assert.That(ex, Is.Null);
        Assert.That(Interlocked.Read(ref moved), Is.EqualTo(10));
        Assert.That(cancelled, Is.False);
    }

    [Test]
    public async Task ATransferThatStopsAfterMovingTimesOut()
    {
        long moved = 0;
        var stalled = new TaskCompletionSource();
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 4; i++)
            {
                await Task.Delay(300);
                Interlocked.Increment(ref moved);
            }
        });
        var sw = Stopwatch.StartNew();

        var ex = await WaitGuardedAsync(Storj.WaitForTransferAsync(stalled.Task, () => Interlocked.Read(ref moved), () => { }, IdleTimeout, CancellationToken.None));

        Assert.That(ex, Is.InstanceOf<TimeoutException>());
        Assert.That(sw.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(1200) + IdleTimeout - TimeSpan.FromMilliseconds(300)));
    }

    [Test]
    public async Task ACancelledTransferIsNotWaitedFor()
    {
        var stalled = new TaskCompletionSource();
        var cancelled = false;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var ex = await WaitGuardedAsync(Storj.WaitForTransferAsync(stalled.Task, () => 0, () => cancelled = true, Timeout.InfiniteTimeSpan, cts.Token));

        Assert.That(ex, Is.InstanceOf<OperationCanceledException>());
        Assert.That(cancelled, Is.True, "The transfer was not told to stop");
    }

    [Test]
    public async Task WithoutATimeoutAStillTransferIsWaitedFor()
    {
        var transfer = Task.Delay(TimeSpan.FromSeconds(2));

        var ex = await WaitGuardedAsync(Storj.WaitForTransferAsync(transfer, () => 0, () => { }, TimeSpan.Zero, CancellationToken.None));

        Assert.That(ex, Is.Null);
    }

    [Test]
    public async Task AFailedTransferPassesItsErrorOn()
    {
        var transfer = Task.Run(() => throw new InvalidOperationException("failed"));

        var ex = await WaitGuardedAsync(Storj.WaitForTransferAsync(transfer, () => 0, () => { }, IdleTimeout, CancellationToken.None));

        Assert.That(ex, Is.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public async Task ATransferThatEndedBeforeItStartedIsNotWaitedFor()
    {
        // uplink.NET returns no task when the transfer failed while it was being set up
        var ex = await WaitGuardedAsync(Storj.WaitForTransferAsync(null, () => 0, () => { }, IdleTimeout, CancellationToken.None));

        Assert.That(ex, Is.Null);
    }
}
