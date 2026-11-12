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
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Utility.Options;
using Duplicati.Proprietary.Office365;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests the limits on the requests sent to a single mailbox
/// </summary>
[TestFixture]
public class Office365MailboxGateTests
{
    private const string GRAPH = "https://graph.test/v1.0";

    /// <summary>
    /// A fake Graph that keeps each request in flight until the test lets it go
    /// </summary>
    private sealed class HoldingHandler : HttpMessageHandler
    {
        private readonly object _lock = new();
        private readonly List<(string Url, TaskCompletionSource<HttpResponseMessage> Completion)> _pending = [];

        /// <summary>
        /// The requests that are in flight
        /// </summary>
        public int Pending(string fragment)
        {
            lock (_lock)
                return _pending.Count(x => x.Url.Contains(fragment, StringComparison.Ordinal));
        }

        /// <summary>
        /// Answers the requests in flight that match the fragment
        /// </summary>
        public void Complete(string fragment)
        {
            List<TaskCompletionSource<HttpResponseMessage>> matches;
            lock (_lock)
            {
                matches = _pending.Where(x => x.Url.Contains(fragment, StringComparison.Ordinal)).Select(x => x.Completion).ToList();
                _pending.RemoveAll(x => x.Url.Contains(fragment, StringComparison.Ordinal));
            }

            foreach (var match in matches)
                match.TrySetResult(Ok());
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            lock (_lock)
                _pending.Add((request.RequestUri!.AbsoluteUri, completion));
            return completion.Task;
        }
    }

    /// <summary>
    /// A fake Graph that answers from a callback and records when each request arrived
    /// </summary>
    private sealed class RecordingHandler(Func<string, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly object _lock = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        /// <summary>
        /// The requests received, with the time they arrived
        /// </summary>
        public List<(string Url, TimeSpan At)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            int seen;
            lock (_lock)
            {
                seen = Requests.Count(x => x.Url == url);
                Requests.Add((url, _clock.Elapsed));
            }

            return Task.FromResult(respond(url, seen));
        }
    }

    private static HttpResponseMessage Ok()
        => new(HttpStatusCode.OK) { Content = new StringContent("{\"value\":1}", Encoding.UTF8, "application/json") };

    private static APIHelper CreateApi(HttpMessageHandler handler)
        => Office365TestSupport.CreateApiHelper(handler, GRAPH, new TimeoutOptionsHelper.Timeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)));

    private static Task<HttpResponseMessage> SendAsync(APIHelper api, string url)
        => api.SendWithRetryAsync(_ => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, url)), HttpCompletionOption.ResponseHeadersRead, null, null, CancellationToken.None);

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(10))
                Assert.Fail($"Timed out waiting for: {what}");
            await Task.Delay(10);
        }
    }

    [Test]
    [Category("Office365")]
    [TestCase("https://graph.test/v1.0/users/u1/messages/m1/$value", "users/u1")]
    [TestCase("https://graph.test/v1.0/users/U1/mailFolders('inbox')/messages/delta?$top=10", "users/U1")]
    [TestCase("https://graph.test/v1.0/users/a%40example.com/contacts", "users/a@example.com")]
    [TestCase("https://graph.test/v1.0/groups/g1/conversations", "groups/g1")]
    [TestCase("https://graph.test/v1.0/users/u1/drive/root/children", null)]
    [TestCase("https://graph.test/v1.0/users/u1/chats", null)]
    [TestCase("https://graph.test/v1.0/users/u1", null)]
    [TestCase("https://graph.test/v1.0/users", null)]
    [TestCase("https://graph.test/v1.0/chats/c1/messages", null)]
    public void MailboxIsFoundFromTheRequest(string url, string? expected)
        => Assert.That(MailboxRequestGate.TryGetMailboxKey(new Uri(url)), Is.EqualTo(expected));

    [Test]
    [Category("Office365")]
    public async Task RequestsForOneMailboxAreCappedAsync()
    {
        var handler = new HoldingHandler();
        using var api = CreateApi(handler);

        var first = Enumerable.Range(0, 10).Select(i => SendAsync(api, $"{GRAPH}/users/u1/messages/m{i}")).ToList();
        // The same mailbox, addressed with different casing
        var sameMailbox = SendAsync(api, $"{GRAPH}/users/U1/contacts");
        var other = Enumerable.Range(0, 3).Select(i => SendAsync(api, $"{GRAPH}/users/u2/messages/m{i}")).ToList();
        var drive = Enumerable.Range(0, 10).Select(i => SendAsync(api, $"{GRAPH}/users/u1/drive/items/d{i}")).ToList();

        await WaitForAsync(() => handler.Pending("/users/u2/") == 3 && handler.Pending("/drive/") == 10 && handler.Pending("/users/u1/messages/") == MailboxRequestGate.MAX_CONCURRENT_REQUESTS, "the requests to be sent");
        await Task.Delay(100);
        Assert.That(handler.Pending("/users/u1/messages/"), Is.EqualTo(MailboxRequestGate.MAX_CONCURRENT_REQUESTS), "Only four requests are in flight for the mailbox");
        Assert.That(handler.Pending("/users/U1/contacts"), Is.EqualTo(0), "The cap is shared by all requests for the mailbox");
        Assert.That(handler.Pending("/users/u2/"), Is.EqualTo(3), "Another mailbox has its own cap");
        Assert.That(handler.Pending("/drive/"), Is.EqualTo(10), "Requests that are not for a mailbox are not capped");

        // Answering does not free the slots, as the bodies are still to be read
        handler.Complete("/users/u1/messages/");
        var done = new List<Task<HttpResponseMessage>>();
        while (done.Count < MailboxRequestGate.MAX_CONCURRENT_REQUESTS)
        {
            var next = await Task.WhenAny(first.Except(done));
            done.Add(next);
        }
        await Task.Delay(100);
        Assert.That(handler.Pending("/users/u1/messages/") + handler.Pending("/users/U1/contacts"), Is.EqualTo(0), "A slot is kept until the response is disposed");

        var response = await done[0];
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("{\"value\":1}"));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        response.Dispose();
        await WaitForAsync(() => handler.Pending("/users/u1/messages/") + handler.Pending("/users/U1/contacts") == 1, "one more request to be sent");

        // Let everything finish
        foreach (var task in done.Skip(1))
            (await task).Dispose();
        var all = first.Append(sameMailbox).Concat(other).Concat(drive).ToList();
        var finishing = Task.Run(async () =>
        {
            foreach (var task in all)
                (await task).Dispose();
        });
        while (!finishing.IsCompleted)
        {
            handler.Complete("/");
            await Task.Delay(10);
        }
        await finishing;
    }

    [Test]
    [Category("Office365")]
    public async Task ThrottlingHoldsBackAllRequestsForTheMailboxAsync()
    {
        var retryAfter = TimeSpan.FromMilliseconds(600);
        var handler = new RecordingHandler((url, seen) =>
        {
            if (url.EndsWith("/users/u1/messages/m1", StringComparison.Ordinal) && seen == 0)
            {
                var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                throttled.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
                return throttled;
            }

            return Ok();
        });
        using var api = CreateApi(handler);

        var throttledRequest = SendAsync(api, $"{GRAPH}/users/u1/messages/m1");
        await WaitForAsync(() => handler.Requests.Count == 1, "the throttled answer");
        await Task.Delay(50);

        // Sent while the mailbox is throttled
        var heldBack = SendAsync(api, $"{GRAPH}/users/u1/contacts/c1");
        using (await SendAsync(api, $"{GRAPH}/users/u2/messages/m1")) { }
        using (await SendAsync(api, $"{GRAPH}/users/u1/drive/items/d1")) { }

        using (await heldBack) { }
        using (await throttledRequest) { }

        var throttledAt = handler.Requests[0].At;
        TimeSpan At(string fragment, int index) => handler.Requests.Where(x => x.Url.Contains(fragment, StringComparison.Ordinal)).ElementAt(index).At;

        // Timers may fire a little early, so leave some slack
        var earliest = throttledAt + retryAfter - TimeSpan.FromMilliseconds(50);
        Assert.That(At("/users/u1/contacts/c1", 0), Is.GreaterThanOrEqualTo(earliest), "Another request for the throttled mailbox waits");
        Assert.That(At("/users/u1/messages/m1", 1), Is.GreaterThanOrEqualTo(earliest), "The throttled request is retried after the wait");
        Assert.That(At("/users/u2/messages/m1", 0), Is.LessThan(earliest), "Another mailbox is not held back");
        Assert.That(At("/users/u1/drive/items/d1", 0), Is.LessThan(earliest), "Requests that are not for a mailbox are not held back");
        Assert.That(handler.Requests.Count(x => x.Url.Contains("/users/u1/contacts/c1", StringComparison.Ordinal)), Is.EqualTo(1), "The held back request is not throttled itself");
    }
}
