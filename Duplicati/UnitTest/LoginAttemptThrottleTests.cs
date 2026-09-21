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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.WebserverCore.Exceptions;
using Duplicati.WebserverCore.Services;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests that failed password verifications delay the following attempts,
/// to slow down brute-force login attempts.
/// </summary>
[TestFixture]
[Category("Authentication")]
public class LoginAttemptThrottleTests
{
    private static readonly TimeSpan BASE_DELAY = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MAX_DELAY = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RESET_PERIOD = TimeSpan.FromMinutes(15);

    // Allow some slack for timer resolution
    private static readonly TimeSpan SLACK = TimeSpan.FromMilliseconds(30);

    private static async Task<TimeSpan> MeasureAsync(Func<Task> action)
    {
        var sw = Stopwatch.StartNew();
        await action();
        return sw.Elapsed;
    }

    [Test]
    public async Task SuccessfulAttemptsAreNotDelayedAsync()
    {
        var throttle = new LoginAttemptThrottle(BASE_DELAY, MAX_DELAY, RESET_PERIOD, 10);
        var elapsed = await MeasureAsync(async () =>
        {
            for (var i = 0; i < 5; i++)
                Assert.IsTrue(await throttle.VerifyAsync(() => true, CancellationToken.None));
        });

        Assert.Less(elapsed, BASE_DELAY);
    }

    [Test]
    public async Task FailedAttemptDelaysNextAttemptAsync()
    {
        var throttle = new LoginAttemptThrottle(BASE_DELAY, MAX_DELAY, RESET_PERIOD, 10);

        // The failed attempt itself is answered immediately
        var first = await MeasureAsync(async () => Assert.IsFalse(await throttle.VerifyAsync(() => false, CancellationToken.None)));
        Assert.Less(first, BASE_DELAY);

        // ... but the next attempt has to wait, even if it has the correct password
        var second = await MeasureAsync(async () => Assert.IsTrue(await throttle.VerifyAsync(() => true, CancellationToken.None)));
        Assert.GreaterOrEqual(second, BASE_DELAY - SLACK);
    }

    [Test]
    public async Task DelayGrowsAndIsCappedAsync()
    {
        var throttle = new LoginAttemptThrottle(BASE_DELAY, MAX_DELAY, RESET_PERIOD, 10);

        // Delays before attempts 2..5 are: 200, 400, 500 (capped), 500 (capped)
        var elapsed = await MeasureAsync(async () =>
        {
            for (var i = 0; i < 5; i++)
                Assert.IsFalse(await throttle.VerifyAsync(() => false, CancellationToken.None));
        });

        Assert.GreaterOrEqual(elapsed, TimeSpan.FromMilliseconds(1600) - SLACK);
        Assert.Less(elapsed, TimeSpan.FromMilliseconds(1600 + 800));
    }

    [Test]
    public async Task SuccessResetsDelayAsync()
    {
        var throttle = new LoginAttemptThrottle(BASE_DELAY, MAX_DELAY, RESET_PERIOD, 10);
        Assert.IsFalse(await throttle.VerifyAsync(() => false, CancellationToken.None));
        Assert.IsFalse(await throttle.VerifyAsync(() => false, CancellationToken.None));
        Assert.IsTrue(await throttle.VerifyAsync(() => true, CancellationToken.None));

        var elapsed = await MeasureAsync(async () => Assert.IsTrue(await throttle.VerifyAsync(() => true, CancellationToken.None)));
        Assert.Less(elapsed, BASE_DELAY);

        // The failure count starts over, so the delay is back to the base delay
        Assert.IsFalse(await throttle.VerifyAsync(() => false, CancellationToken.None));
        elapsed = await MeasureAsync(async () => Assert.IsTrue(await throttle.VerifyAsync(() => true, CancellationToken.None)));
        Assert.GreaterOrEqual(elapsed, BASE_DELAY - SLACK);
        Assert.Less(elapsed, BASE_DELAY * 2);
    }

    [Test]
    public async Task FailureCountResetsAfterQuietPeriodAsync()
    {
        var throttle = new LoginAttemptThrottle(BASE_DELAY, MAX_DELAY, TimeSpan.FromMilliseconds(300), 10);
        Assert.IsFalse(await throttle.VerifyAsync(() => false, CancellationToken.None));
        Assert.IsFalse(await throttle.VerifyAsync(() => false, CancellationToken.None));

        await Task.Delay(TimeSpan.FromMilliseconds(600));

        // Without the reset, this would be the third failure with a capped delay
        Assert.IsFalse(await throttle.VerifyAsync(() => false, CancellationToken.None));
        var elapsed = await MeasureAsync(async () => Assert.IsTrue(await throttle.VerifyAsync(() => true, CancellationToken.None)));
        Assert.Less(elapsed, BASE_DELAY * 2);
    }

    [Test]
    public async Task ParallelAttemptsAreSerializedAsync()
    {
        var throttle = new LoginAttemptThrottle(BASE_DELAY, MAX_DELAY, RESET_PERIOD, 10);
        var concurrent = 0;
        var maxConcurrent = 0;

        var elapsed = await MeasureAsync(() => Task.WhenAll(Enumerable.Range(0, 4).Select(_ => throttle.VerifyAsync(() =>
        {
            maxConcurrent = Math.Max(maxConcurrent, Interlocked.Increment(ref concurrent));
            Thread.Sleep(10);
            Interlocked.Decrement(ref concurrent);
            return false;
        }, CancellationToken.None))));

        Assert.AreEqual(1, maxConcurrent);
        // Delays between the four attempts: 200 + 400 + 500
        Assert.GreaterOrEqual(elapsed, TimeSpan.FromMilliseconds(1100) - SLACK);
    }

    [Test]
    public async Task TooManyPendingAttemptsAreRejectedAsync()
    {
        var throttle = new LoginAttemptThrottle(BASE_DELAY, MAX_DELAY, RESET_PERIOD, 2);
        using var release = new ManualResetEventSlim(false);

        var first = Task.Run(() => throttle.VerifyAsync(() => { release.Wait(); return true; }, CancellationToken.None));
        var second = Task.Run(() => throttle.VerifyAsync(() => true, CancellationToken.None));

        // Wait for both attempts to be pending
        await Task.Delay(100);

        Assert.ThrowsAsync<TooManyRequestsException>(() => throttle.VerifyAsync(() => true, CancellationToken.None));

        release.Set();
        Assert.IsTrue(await first);
        Assert.IsTrue(await second);

        // Once the queue drains, new attempts are accepted again
        Assert.IsTrue(await throttle.VerifyAsync(() => true, CancellationToken.None));
    }

    [Test]
    public async Task CancelledWaitDoesNotLeakPendingSlotAsync()
    {
        var throttle = new LoginAttemptThrottle(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), RESET_PERIOD, 1);
        Assert.IsFalse(await throttle.VerifyAsync(() => false, CancellationToken.None));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Assert.CatchAsync<OperationCanceledException>(() => throttle.VerifyAsync(() => true, cts.Token));

        // The slot must be free again, so this is delayed rather than rejected
        using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Assert.CatchAsync<OperationCanceledException>(() => throttle.VerifyAsync(() => true, cts2.Token));
    }
}
