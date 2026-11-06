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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Utility = Duplicati.Library.Utility.Utility;

namespace Duplicati.UnitTest;

/// <summary>
/// WithTimeout must report its own timeout as a TimeoutException, however the wrapped call
/// reacts to the cancelled token, and must leave a cancellation by the caller a cancellation,
/// so that callers that stop quietly on cancellation do not mistake one for the other.
/// </summary>
public class WithTimeoutCancellationTests : BasicSetupHelper
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    // Throws OperationCanceledException, not TaskCanceledException, as many libraries do
    private static async Task PollUntilCancelled(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(20, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static void PollUntilCancelledSync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            Thread.Sleep(20);
        }
    }

    [Test]
    [Category("Utility")]
    public void AsyncCallThatThrowsOperationCanceledTimesOut()
    {
        Assert.ThrowsAsync<TimeoutException>(() => Utility.WithTimeout(Short, CancellationToken.None, PollUntilCancelled));
    }

    [Test]
    [Category("Utility")]
    public void AsyncCallWithResultThatThrowsOperationCanceledTimesOut()
    {
        Assert.ThrowsAsync<TimeoutException>(() => Utility.WithTimeout(Short, CancellationToken.None, async ct => { await PollUntilCancelled(ct); return 1; }));
    }

    [Test]
    [Category("Utility")]
    public void SyncCallThatThrowsOperationCanceledTimesOut()
    {
        Assert.ThrowsAsync<TimeoutException>(() => Utility.WithTimeout(Short, CancellationToken.None, PollUntilCancelledSync));
    }

    [Test]
    [Category("Utility")]
    public void SyncCallWithResultThatThrowsOperationCanceledTimesOut()
    {
        Assert.ThrowsAsync<TimeoutException>(() => Utility.WithTimeout(Short, CancellationToken.None, ct => { PollUntilCancelledSync(ct); return 1; }));
    }

    [Test]
    [Category("Utility")]
    public void SyncCallThatIgnoresTheTokenTimesOut()
    {
        // A blocking call that never looks at the token cannot be interrupted, but the wait for it must still end
        Assert.ThrowsAsync<TimeoutException>(() => Utility.WithTimeout(Short, CancellationToken.None, _ => Thread.Sleep(Long)));
    }

    [Test]
    [Category("Utility")]
    public void SyncCallWithResultThatIgnoresTheTokenTimesOut()
    {
        Assert.ThrowsAsync<TimeoutException>(() => Utility.WithTimeout(Short, CancellationToken.None, _ => { Thread.Sleep(Long); return 1; }));
    }

    [Test]
    [Category("Utility")]
    public void CallerCancellationOfASyncCallThatIgnoresTheTokenIsNotATimeout()
    {
        using var caller = new CancellationTokenSource(Short);
        Assert.CatchAsync<OperationCanceledException>(() => Utility.WithTimeout(Long, caller.Token, _ => Thread.Sleep(Long)));
    }

    [Test]
    [Category("Utility")]
    public void CallerCancellationOfAnAsyncCallIsNotATimeout()
    {
        using var caller = new CancellationTokenSource(Short);
        Assert.CatchAsync<OperationCanceledException>(() => Utility.WithTimeout(Long, caller.Token, ct => Task.Delay(Timeout.Infinite, ct)));
    }

    [Test]
    [Category("Utility")]
    public void CallerCancellationOfAnAsyncCallWithResultIsNotATimeout()
    {
        using var caller = new CancellationTokenSource(Short);
        Assert.CatchAsync<OperationCanceledException>(() => Utility.WithTimeout(Long, caller.Token, async ct => { await Task.Delay(Timeout.Infinite, ct); return 1; }));
    }

    [Test]
    [Category("Utility")]
    public void CallerCancellationOfASyncCallIsNotATimeout()
    {
        using var caller = new CancellationTokenSource(Short);
        Assert.CatchAsync<OperationCanceledException>(() => Utility.WithTimeout(Long, caller.Token, PollUntilCancelledSync));
    }

    [Test]
    [Category("Utility")]
    public void CallerCancellationOfASyncCallWithResultIsNotATimeout()
    {
        using var caller = new CancellationTokenSource(Short);
        Assert.CatchAsync<OperationCanceledException>(() => Utility.WithTimeout(Long, caller.Token, ct => { PollUntilCancelledSync(ct); return 1; }));
    }
}
