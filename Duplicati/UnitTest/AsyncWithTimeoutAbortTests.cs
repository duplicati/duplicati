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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Microsoft.SharePoint.Client;
using NUnit.Framework;
using Utility = Duplicati.Library.Utility.Utility;

namespace Duplicati.UnitTest;

/// <summary>
/// WithTimeout must stop waiting for an asynchronous call that does not look at the token, and
/// SharePoint must not hand out a context again whose query timed out and is still running,
/// as a ClientContext is not thread safe.
/// No network: the SharePoint context never sends its query.
/// </summary>
public class AsyncWithTimeoutAbortTests : BasicSetupHelper
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private static void AssertTimesOut(Task task)
    {
        Assert.That(Task.WhenAny(task, Task.Delay(Limit)).Result, Is.SameAs(task), "The call that ignores the token must stop being waited for");
        Assert.ThrowsAsync<TimeoutException>(() => task);
    }

    [Test]
    [Category("Utility")]
    public void AsyncCallThatIgnoresTheTokenTimesOut()
    {
        using var release = new CancellationTokenSource();
        try
        {
            AssertTimesOut(Utility.WithTimeout(Short, CancellationToken.None, _ => Task.Delay(Timeout.Infinite, release.Token)));
        }
        finally
        {
            release.Cancel();
        }
    }

    [Test]
    [Category("Utility")]
    public void AsyncCallWithResultThatIgnoresTheTokenTimesOut()
    {
        using var release = new CancellationTokenSource();
        try
        {
            AssertTimesOut(Utility.WithTimeout(Short, CancellationToken.None, async _ => { await Task.Delay(Timeout.Infinite, release.Token); return 1; }));
        }
        finally
        {
            release.Cancel();
        }
    }

    /// <summary>
    /// A context whose query never completes, like a server that stopped answering
    /// </summary>
    private sealed class StuckContext : ClientContext
    {
        public readonly TaskCompletionSource Release = new();

        public StuckContext() : base("https://example.invalid/sites/site") { }

        public override Task ExecuteQueryAsync() => Release.Task;
    }

    [Test]
    [Category("Utility")]
    public void SharePointDoesNotReuseAContextWhoseQueryTimedOut()
    {
        var contexts = new Queue<StuckContext>(new[] { new StuckContext(), new StuckContext() });
        var backend = new SharePointBackend("mssp://example.invalid/sites/site/folder", new Dictionary<string, string?>
        {
            ["auth-username"] = "user@example.invalid",
            ["auth-password"] = "password",
        })
        {
            ContextFactory = () => contexts.Dequeue()
        };

        var stuck = (StuckContext)backend.GetSpClientContextAsync(false, CancellationToken.None).Result;
        try
        {
            AssertTimesOut(backend.WrappedExecuteQueryOnContextAsync(stuck, "/sites/site/folder", true, Short, CancellationToken.None));

            var next = backend.GetSpClientContextAsync(false, CancellationToken.None).Result;
            Assert.That(next, Is.Not.SameAs(stuck), "The context whose query timed out must not be used again");
        }
        finally
        {
            stuck.Release.TrySetResult();
        }
    }
}
