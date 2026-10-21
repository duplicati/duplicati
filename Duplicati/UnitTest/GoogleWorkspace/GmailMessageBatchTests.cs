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

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.GoogleWorkspace;
using Google;
using Google.Apis.Gmail.v1;
using Google.Apis.Http;
using Google.Apis.Services;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;
using static Duplicati.UnitTest.GoogleWorkspace.GoogleRetryHandlerTests;

namespace Duplicati.UnitTest.GoogleWorkspace;

/// <summary>
/// A Gmail batch answers 200 as a whole and reports each message's outcome inside the
/// multipart body, out of reach of the HTTP retry handler. These pin that a message that hit
/// the rate limit is fetched again, alone, that a message deleted in the meantime is skipped,
/// and that giving up is loud rather than silent.
///
/// Nothing here talks to Google. The Gmail client is built on a scripted handler that
/// answers with hand-made multipart bodies in the shape Google sends.
/// </summary>
[TestFixture]
public class GmailMessageBatchTests
{
    private const string BOUNDARY = "batch_test_boundary";

    private const string RATE_LIMIT_PART = """
        {"error":{"code":403,"message":"User-rate limit exceeded.  Retry after 2026-10-20T09:32:18.306Z","errors":[{"message":"User-rate limit exceeded.  Retry after 2026-10-20T09:32:18.306Z","domain":"global","reason":"rateLimitExceeded"}]}}
        """;

    private const string NOT_FOUND_PART = """
        {"error":{"code":404,"message":"Requested entity was not found.","errors":[{"message":"Requested entity was not found.","domain":"global","reason":"notFound"}]}}
        """;

    private const string BAD_REQUEST_PART = """
        {"error":{"code":400,"message":"Invalid id value","errors":[{"message":"Invalid id value","domain":"global","reason":"invalidArgument"}]}}
        """;

    private sealed class ScriptedClientFactory(HttpMessageHandler handler) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => handler;
    }

    private static (GmailService Service, ScriptedHandler Handler) CreateService()
    {
        var scripted = new ScriptedHandler();
        var service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientFactory = new ScriptedClientFactory(scripted),
            ApplicationName = "Duplicati.UnitTest"
        });
        return (service, scripted);
    }

    private static string MessagePart(string id)
        => $$"""{"id":"{{id}}","threadId":"thread-{{id}}","sizeEstimate":123}""";

    /// <summary>
    /// Builds a batch response in the shape Google sends: one application/http part per
    /// request, each wrapping a complete HTTP response, in request order.
    /// </summary>
    private static HttpResponseMessage BatchResponse(params (HttpStatusCode Status, string Json)[] parts)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            var (status, json) = parts[i];
            sb.Append("--").Append(BOUNDARY).Append("\r\n");
            sb.Append("Content-Type: application/http\r\n");
            sb.Append($"Content-ID: <response-{i + 1}>\r\n\r\n");
            sb.Append($"HTTP/1.1 {(int)status} {status}\r\n");
            sb.Append("Content-Type: application/json; charset=UTF-8\r\n");
            sb.Append($"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n");
            sb.Append(json).Append("\r\n");
        }
        sb.Append("--").Append(BOUNDARY).Append("--\r\n");

        var content = new StringContent(sb.ToString());
        content.Headers.ContentType = new MediaTypeHeaderValue("multipart/mixed");
        content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", BOUNDARY));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>
    /// The message ids a batch request asks for, in order.
    /// </summary>
    private static List<string> RequestedIds(string? batchBody)
        => Regex.Matches(batchBody ?? "", @"/messages/([^?\s]+)").Select(m => m.Groups[1].Value).ToList();

    private static Task<List<Google.Apis.Gmail.v1.Data.Message>> FetchAsync(GmailService service, IReadOnlyCollection<string> ids, int maxRetries)
        => GmailMessageBatch.GetMetadataAsync(service, "user@example.com", ids, new NoWaitBackOff(maxRetries), CancellationToken.None);

    [Test]
    public async Task AllMessagesSucceed_OneBatch()
    {
        var (service, handler) = CreateService();
        handler.Enqueue(_ => BatchResponse((HttpStatusCode.OK, MessagePart("a")), (HttpStatusCode.OK, MessagePart("b"))));

        var messages = await FetchAsync(service, ["a", "b"], 3);

        Assert.AreEqual(1, handler.RequestBodies.Count);
        CollectionAssert.AreEquivalent(new[] { "a", "b" }, messages.Select(m => m.Id));
        CollectionAssert.AreEqual(new[] { "a", "b" }, RequestedIds(handler.RequestBodies[0]));
    }

    [Test]
    public async Task RateLimitedMessage_IsFetchedAgainAlone()
    {
        var (service, handler) = CreateService();
        handler
            .Enqueue(_ => BatchResponse((HttpStatusCode.OK, MessagePart("a")), (HttpStatusCode.Forbidden, RATE_LIMIT_PART), (HttpStatusCode.OK, MessagePart("c"))))
            .Enqueue(_ => BatchResponse((HttpStatusCode.OK, MessagePart("b"))));

        var messages = await FetchAsync(service, ["a", "b", "c"], 3);

        Assert.AreEqual(2, handler.RequestBodies.Count);
        CollectionAssert.AreEqual(new[] { "b" }, RequestedIds(handler.RequestBodies[1]));
        CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, messages.Select(m => m.Id));
    }

    [Test]
    public async Task DeletedMessage_IsSkipped()
    {
        var (service, handler) = CreateService();
        handler.Enqueue(_ => BatchResponse((HttpStatusCode.OK, MessagePart("a")), (HttpStatusCode.NotFound, NOT_FOUND_PART)));

        var messages = await FetchAsync(service, ["a", "b"], 3);

        Assert.AreEqual(1, handler.RequestBodies.Count);
        CollectionAssert.AreEquivalent(new[] { "a" }, messages.Select(m => m.Id));
    }

    [Test]
    public async Task OtherError_SkipsTheMessageAndKeepsTheRest()
    {
        var (service, handler) = CreateService();
        handler.Enqueue(_ => BatchResponse((HttpStatusCode.BadRequest, BAD_REQUEST_PART), (HttpStatusCode.OK, MessagePart("b"))));

        var messages = await FetchAsync(service, ["a", "b"], 3);

        Assert.AreEqual(1, handler.RequestBodies.Count);
        CollectionAssert.AreEquivalent(new[] { "b" }, messages.Select(m => m.Id));
    }

    [Test]
    public void PersistentRateLimit_GivesUpLoudly()
    {
        var (service, handler) = CreateService();
        for (var i = 0; i < 10; i++)
            handler.Enqueue(_ => BatchResponse((HttpStatusCode.Forbidden, RATE_LIMIT_PART)));

        var ex = Assert.ThrowsAsync<GoogleApiException>(() => FetchAsync(service, ["a"], 2));

        Assert.AreEqual(HttpStatusCode.Forbidden, ex!.HttpStatusCode);
        StringAssert.Contains("user@example.com", ex.Message);
        // The first attempt and two retries
        Assert.AreEqual(3, handler.RequestBodies.Count);
    }

    [Test]
    public async Task ManyMessages_AreSentInBatchesOfFifty()
    {
        var ids = Enumerable.Range(0, 120).Select(i => $"m{i}").ToList();
        var (service, handler) = CreateService();
        for (var i = 0; i < 3; i++)
            handler.Enqueue(body => BatchResponse(RequestedIds(body).Select(id => (HttpStatusCode.OK, MessagePart(id))).ToArray()));

        var messages = await FetchAsync(service, ids, 3);

        Assert.AreEqual(3, handler.RequestBodies.Count);
        CollectionAssert.AreEqual(new[] { 50, 50, 20 }, handler.RequestBodies.Select(b => RequestedIds(b).Count));
        CollectionAssert.AreEquivalent(ids, messages.Select(m => m.Id));
    }
}
