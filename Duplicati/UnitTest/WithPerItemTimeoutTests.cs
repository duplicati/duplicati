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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Utility = Duplicati.Library.Utility.Utility;

namespace Duplicati.UnitTest;

/// <summary>
/// WithPerItemTimeout must limit only the wait for each item from the source: the time the
/// caller spends on an item must not count, a timeout must be a TimeoutException, and a
/// cancellation by the caller must stay a cancellation.
/// </summary>
public class WithPerItemTimeoutTests : BasicSetupHelper
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    private static async IAsyncEnumerable<int> Items(int count, TimeSpan delayBefore, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < count; i++)
        {
            await Task.Delay(delayBefore, ct).ConfigureAwait(false);
            yield return i;
        }
    }

    private static async Task<List<int>> ReadAsync(IAsyncEnumerable<int> source, TimeSpan perItemWork, CancellationToken ct = default)
    {
        var result = new List<int>();
        await foreach (var item in Utility.WithPerItemTimeout(source, Timeout, ct).ConfigureAwait(false))
        {
            result.Add(item);
            await Task.Delay(perItemWork, CancellationToken.None).ConfigureAwait(false);
        }
        return result;
    }

    [Test]
    [Category("Utility")]
    public void TimeSpentOnAnItemDoesNotCount()
    {
        var items = ReadAsync(Items(3, TimeSpan.Zero), TimeSpan.FromSeconds(1)).Result;
        Assert.That(items, Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    [Category("Utility")]
    public void SlowSourceTimesOut()
    {
        Assert.ThrowsAsync<TimeoutException>(() => ReadAsync(Items(3, TimeSpan.FromSeconds(30)), TimeSpan.Zero));
    }

    [Test]
    [Category("Utility")]
    public void CallerCancellationIsNotATimeout()
    {
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        Assert.CatchAsync<OperationCanceledException>(() => ReadAsync(Items(3, TimeSpan.FromSeconds(30)), TimeSpan.Zero, caller.Token));
    }
}
