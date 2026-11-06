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
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Jottacloud limits the number of requests and answers 429 Too Many Requests when the limit
/// is reached. These tests check that the backend reports the limit so the retries can respect
/// it, and that it holds back the following requests to the account while the limit lasts.
/// </summary>
[TestFixture]
public class JottacloudRateLimitTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails instead of reaching
    /// the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";

    private const string FolderXml = "<folder name=\"Backup\"><files></files></folder>";

    private const string RemoteName = "duplicati-b0001.dblock.zip.aes";

    /// <summary>
    /// Answers the token and user requests, refuses the first requests to the storage with a
    /// rate limit, and answers the rest
    /// </summary>
    private sealed class RateLimitingHandler(string username, int limitedRequests, HttpStatusCode limitStatus, RetryConditionHeaderValue? retryAfter) : HttpMessageHandler
    {
        private int m_storageRequests;

        /// <summary>
        /// The times the storage requests were sent, in order
        /// </summary>
        public List<TimeSpan> SentAt { get; } = new();

        /// <summary>
        /// Measures when the storage requests are sent
        /// </summary>
        public Stopwatch Clock { get; } = Stopwatch.StartNew();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Task.FromResult(Json("{\"access_token\":\"test-token\",\"expires\":3600}"));
            if (url.Contains("/userinfo", StringComparison.Ordinal))
                return Task.FromResult(Json($"{{\"username\":\"{username}\"}}"));

            lock (SentAt)
                SentAt.Add(Clock.Elapsed);

            if (Interlocked.Increment(ref m_storageRequests) <= limitedRequests)
            {
                var limited = new HttpResponseMessage(limitStatus) { Content = new StringContent("") };
                limited.Headers.RetryAfter = retryAfter;
                return Task.FromResult(limited);
            }

            if (request.Method == HttpMethod.Post)
            {
                var uploaded = $"<file name=\"{RemoteName}\"><currentRevision><state>COMPLETED</state></currentRevision></file>";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(uploaded, Encoding.UTF8, "text/xml") });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(FolderXml, Encoding.UTF8, "text/xml") });
        }

        private static HttpResponseMessage Json(string body)
            => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    /// <summary>
    /// Creates a backend for the account served by the handler. The rate limit is kept per
    /// account for all backend instances, so each test uses an account of its own.
    /// </summary>
    private static Jottacloud CreateBackend(RateLimitingHandler handler)
    {
        var options = new Dictionary<string, string?>
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl,
            ["jottacloud-threads"] = "1"
        };

        return new Jottacloud("jottacloud://Backup", options, new HttpClient(handler));
    }

    private static string NewAccount() => "rate-limit-" + Guid.NewGuid().ToString("N");

    private static async Task ListAll(Jottacloud backend, CancellationToken cancelToken)
    {
        await foreach (var _ in backend.ListAsync(cancelToken)) { }
    }

    [Test]
    public void ATooManyRequestsAnswerIsReportedAsARateLimit()
    {
        var handler = new RateLimitingHandler(NewAccount(), 1, HttpStatusCode.TooManyRequests, new RetryConditionHeaderValue(TimeSpan.FromSeconds(1)));
        using var backend = CreateBackend(handler);

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => ListAll(backend, CancellationToken.None));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        var rateLimit = ExceptionExtensions.FindRateLimit(ex);
        Assert.That(rateLimit, Is.Not.Null);
        Assert.That(rateLimit!.RetryAfter.Delta, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void ARateLimitedUploadIsReportedAsARateLimit()
    {
        var handler = new RateLimitingHandler(NewAccount(), 1, HttpStatusCode.TooManyRequests, null);
        using var backend = CreateBackend(handler);
        using var content = new MemoryStream(new byte[] { 1, 2, 3 });

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => backend.PutAsync(RemoteName, content, CancellationToken.None));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(ExceptionExtensions.FindRateLimit(ex), Is.Not.Null);
    }

    [Test]
    public void AServiceUnavailableAnswerWithoutRetryAfterIsNotARateLimit()
    {
        var handler = new RateLimitingHandler(NewAccount(), 1, HttpStatusCode.ServiceUnavailable, null);
        using var backend = CreateBackend(handler);

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => ListAll(backend, CancellationToken.None));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(ExceptionExtensions.FindRateLimit(ex), Is.Null);
    }

    [Test]
    public async Task TheNextRequestWaitsForTheRetryAfterDelay_Async()
    {
        var delay = TimeSpan.FromSeconds(2);
        var handler = new RateLimitingHandler(NewAccount(), 1, HttpStatusCode.TooManyRequests, new RetryConditionHeaderValue(delay));

        using (var backend = CreateBackend(handler))
            Assert.ThrowsAsync<HttpRequestException>(() => ListAll(backend, CancellationToken.None));

        // A new instance for the same account, as the retry of an operation gets
        using (var backend = CreateBackend(handler))
            await ListAll(backend, CancellationToken.None);

        Assert.That(handler.SentAt, Has.Count.EqualTo(2));
        Assert.That(handler.SentAt[1] - handler.SentAt[0], Is.GreaterThanOrEqualTo(delay - TimeSpan.FromMilliseconds(100)));
    }

    [Test]
    public void ARateLimitWithoutRetryAfterHoldsBackTheNextRequest()
    {
        var handler = new RateLimitingHandler(NewAccount(), 1, HttpStatusCode.TooManyRequests, null);
        using var backend = CreateBackend(handler);

        Assert.ThrowsAsync<HttpRequestException>(() => ListAll(backend, CancellationToken.None));

        // The default wait is a minute, so the next request is still held back when the call gives up
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        Assert.CatchAsync<OperationCanceledException>(() => ListAll(backend, cts.Token));
        Assert.That(handler.SentAt, Has.Count.EqualTo(1));
    }

    [Test]
    public void TheRetryDelayFollowsARetryAfterLongerThanTheBackoff()
    {
        var now = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        var baseDelay = TimeSpan.FromSeconds(10);
        HttpRequestException RateLimited(RetryConditionHeaderValue? retryAfter)
            => new("429", new TooManyRequestException(retryAfter!), HttpStatusCode.TooManyRequests);

        // No rate limit, or a rate limit without a usable value, keeps the configured delay
        Assert.That(Utility.GetRetryDelay(baseDelay, 2, true, new HttpRequestException("500"), now), Is.EqualTo(TimeSpan.FromSeconds(20)));
        Assert.That(Utility.GetRetryDelay(baseDelay, 2, true, RateLimited(null), now), Is.EqualTo(TimeSpan.FromSeconds(20)));

        // A longer Retry-After wins, as a delay or as a date
        Assert.That(Utility.GetRetryDelay(baseDelay, 1, false, RateLimited(new RetryConditionHeaderValue(TimeSpan.FromMinutes(5))), now), Is.EqualTo(TimeSpan.FromMinutes(5)));
        Assert.That(Utility.GetRetryDelay(baseDelay, 1, false, RateLimited(new RetryConditionHeaderValue(now.AddMinutes(3))), now), Is.EqualTo(TimeSpan.FromMinutes(3)));

        // A shorter one, or a date that has passed, keeps the backoff
        Assert.That(Utility.GetRetryDelay(baseDelay, 3, true, RateLimited(new RetryConditionHeaderValue(TimeSpan.FromSeconds(5))), now), Is.EqualTo(TimeSpan.FromSeconds(40)));
        Assert.That(Utility.GetRetryDelay(baseDelay, 1, false, RateLimited(new RetryConditionHeaderValue(now.AddMinutes(-3))), now), Is.EqualTo(baseDelay));

        // The rate limit is found when the failure is wrapped
        Assert.That(Utility.GetRetryDelay(baseDelay, 1, false, new AggregateException(RateLimited(new RetryConditionHeaderValue(TimeSpan.FromMinutes(2)))), now), Is.EqualTo(TimeSpan.FromMinutes(2)));
    }
}
