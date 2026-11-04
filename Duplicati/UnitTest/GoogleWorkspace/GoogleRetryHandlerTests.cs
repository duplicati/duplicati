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
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.GoogleWorkspace;
using Google;
using Google.Apis.Http;
using Google.Apis.Requests;
using Google.Apis.Util;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

namespace Duplicati.UnitTest.GoogleWorkspace;

/// <summary>
/// Gmail reports its per-user quota as 403 Forbidden, the same status it uses for a missing
/// permission. These pin that the retry handler tells the two apart by the error body, that
/// it retries the statuses Google documents as transient, and that it leaves daily quotas
/// and real permission errors alone.
///
/// Nothing here talks to Google. The handler sits in the same message handler the client
/// library uses, in front of a scripted handler that plays back canned responses.
/// </summary>
[TestFixture]
public class GoogleRetryHandlerTests
{
    /// <summary>
    /// The body Gmail sent in the incident that prompted the handler, verbatim apart from
    /// the project number.
    /// </summary>
    public const string GMAIL_QUOTA_BODY = """
        {"error":{"code":403,"message":"Quota exceeded for quota metric 'Queries' and limit 'Previous quota: Units per minute per user' of service 'gmail.googleapis.com' for consumer 'project_number:123456789012'.","errors":[{"message":"Quota exceeded for quota metric 'Queries' and limit 'Previous quota: Units per minute per user' of service 'gmail.googleapis.com' for consumer 'project_number:123456789012'.","domain":"global","reason":"rateLimitExceeded"}],"status":"RESOURCE_EXHAUSTED"}}
        """;

    private const string FORBIDDEN_BODY = """
        {"error":{"code":403,"message":"The caller does not have permission","errors":[{"message":"The caller does not have permission","domain":"global","reason":"forbidden"}],"status":"PERMISSION_DENIED"}}
        """;

    private const string DAILY_LIMIT_BODY = """
        {"error":{"code":403,"message":"Daily Limit Exceeded. The quota will be reset at midnight Pacific Time (PT).","errors":[{"message":"Daily Limit Exceeded.","domain":"usageLimits","reason":"dailyLimitExceeded"}]}}
        """;

    private const string NEW_FORMAT_RATE_LIMIT_BODY = """
        {"error":{"code":403,"message":"Quota exceeded for quota metric 'Read requests' and limit 'Read requests per minute per user' of service 'drive.googleapis.com'.","status":"RESOURCE_EXHAUSTED","details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"RATE_LIMIT_EXCEEDED","domain":"googleapis.com"}]}}
        """;

    /// <summary>
    /// Plays back scripted responses and records every request it saw.
    /// </summary>
    public sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<string?, HttpResponseMessage>> _responses = new();

        public List<string?> RequestBodies { get; } = new();

        public ScriptedHandler Enqueue(HttpStatusCode statusCode, string? body)
            => Enqueue(_ => Response(statusCode, body));

        public ScriptedHandler Enqueue(Func<string?, HttpResponseMessage> response)
        {
            _responses.Enqueue(response);
            return this;
        }

        public static HttpResponseMessage Response(HttpStatusCode statusCode, string? body)
            => new HttpResponseMessage(statusCode) { Content = new StringContent(body ?? "", System.Text.Encoding.UTF8, "application/json") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(body);
            if (_responses.Count == 0)
                throw new InvalidOperationException($"No scripted response left for request {RequestBodies.Count}");
            return _responses.Dequeue()(body);
        }
    }

    /// <summary>
    /// A back-off that never waits, so the tests only measure decisions.
    /// </summary>
    public sealed class NoWaitBackOff(int maxNumOfRetries) : IBackOff
    {
        public int MaxNumOfRetries => maxNumOfRetries;

        public TimeSpan GetNextBackOff(int currentRetry)
            => currentRetry > maxNumOfRetries ? TimeSpan.MinValue : TimeSpan.Zero;
    }

    private static (ConfigurableHttpClient Client, ScriptedHandler Handler) CreateClient()
    {
        var scripted = new ScriptedHandler();
        var client = new ConfigurableHttpClient(new ConfigurableMessageHandler(scripted));
        client.MessageHandler.NumTries = GoogleRetryHandler.NumTries;
        var retry = new GoogleRetryHandler(new NoWaitBackOff(GoogleRetryHandler.NumTries));
        client.MessageHandler.AddUnsuccessfulResponseHandler(retry);
        client.MessageHandler.AddExceptionHandler(retry);
        return (client, scripted);
    }

    private static Task<HttpResponseMessage> SendAsync(ConfigurableHttpClient client)
        => client.GetAsync("https://gmail.googleapis.com/gmail/v1/users/me/messages");

    [Test]
    public async Task GmailQuotaForbidden_IsRetriedUntilItSucceeds()
    {
        var (client, handler) = CreateClient();
        handler
            .Enqueue(HttpStatusCode.Forbidden, GMAIL_QUOTA_BODY)
            .Enqueue(HttpStatusCode.Forbidden, GMAIL_QUOTA_BODY)
            .Enqueue(HttpStatusCode.OK, "{}");

        using var response = await SendAsync(client);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(3, handler.RequestBodies.Count);
    }

    [Test]
    public async Task PermissionForbidden_IsNotRetried()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.Forbidden, FORBIDDEN_BODY);

        using var response = await SendAsync(client);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreEqual(1, handler.RequestBodies.Count);
        // The body is still there for the client library's own error report
        Assert.AreEqual(FORBIDDEN_BODY, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task DailyLimit_IsNotRetried()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.Forbidden, DAILY_LIMIT_BODY);

        using var response = await SendAsync(client);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreEqual(1, handler.RequestBodies.Count);
    }

    [TestCase(429)]
    [TestCase(500)]
    [TestCase(502)]
    [TestCase(503)]
    [TestCase(504)]
    public async Task TransientStatus_IsRetried(int statusCode)
    {
        var (client, handler) = CreateClient();
        handler
            .Enqueue((HttpStatusCode)statusCode, "")
            .Enqueue(HttpStatusCode.OK, "{}");

        using var response = await SendAsync(client);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(2, handler.RequestBodies.Count);
    }

    [TestCase(400)]
    [TestCase(401)]
    [TestCase(404)]
    public async Task ClientError_IsNotRetried(int statusCode)
    {
        var (client, handler) = CreateClient();
        handler.Enqueue((HttpStatusCode)statusCode, GMAIL_QUOTA_BODY);

        using var response = await SendAsync(client);

        Assert.AreEqual((HttpStatusCode)statusCode, response.StatusCode);
        Assert.AreEqual(1, handler.RequestBodies.Count);
    }

    [Test]
    public async Task PersistentQuota_GivesUpAfterTheAllowedAttempts()
    {
        var (client, handler) = CreateClient();
        for (var i = 0; i < GoogleRetryHandler.NumTries + 5; i++)
            handler.Enqueue(HttpStatusCode.Forbidden, GMAIL_QUOTA_BODY);

        using var response = await SendAsync(client);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreEqual(GoogleRetryHandler.NumTries, handler.RequestBodies.Count);
    }

    [Test]
    public async Task Install_RaisesTheAttemptLimitAndRetries()
    {
        var scripted = new ScriptedHandler()
            .Enqueue(HttpStatusCode.ServiceUnavailable, "")
            .Enqueue(HttpStatusCode.OK, "{}");
        var client = new ConfigurableHttpClient(new ConfigurableMessageHandler(scripted));

        GoogleRetryHandler.Install(client);

        Assert.AreEqual(GoogleRetryHandler.NumTries, client.MessageHandler.NumTries);
        using var response = await SendAsync(client);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(2, scripted.RequestBodies.Count);
    }

    [Test]
    public void DefaultBackOff_FitsTheAttemptLimit()
    {
        var backOff = GoogleRetryHandler.CreateBackOff();
        var total = TimeSpan.Zero;
        var retries = 0;
        for (var attempt = 1; attempt < GoogleRetryHandler.NumTries; attempt++)
        {
            var delay = backOff.GetNextBackOff(attempt);
            if (delay < TimeSpan.Zero || delay > GoogleRetryHandler.MaxWait)
                break;
            total += delay;
            retries++;
        }

        // A per-minute quota needs well over a minute of total waiting to be sure to clear
        Assert.GreaterOrEqual(retries, 7);
        Assert.Greater(total, TimeSpan.FromMinutes(2));
    }

    [Test]
    public void ParseErrorBody_ReadsLegacyFormat()
    {
        var (reasons, message) = GoogleServiceErrors.ParseErrorBody(GMAIL_QUOTA_BODY);

        CollectionAssert.Contains(reasons, "rateLimitExceeded");
        CollectionAssert.Contains(reasons, "RESOURCE_EXHAUSTED");
        StringAssert.StartsWith("Quota exceeded for quota metric 'Queries'", message!);
    }

    [Test]
    public void ParseErrorBody_ReadsNewFormat()
    {
        var (reasons, message) = GoogleServiceErrors.ParseErrorBody(NEW_FORMAT_RATE_LIMIT_BODY);

        CollectionAssert.Contains(reasons, "RATE_LIMIT_EXCEEDED");
        Assert.IsTrue(GoogleServiceErrors.IsRetryable(HttpStatusCode.Forbidden, reasons, message));
    }

    [Test]
    public void ParseErrorBody_KeepsNonJsonAsMessage()
    {
        var (reasons, message) = GoogleServiceErrors.ParseErrorBody("<html>Rate Limit Exceeded</html>");

        Assert.IsEmpty(reasons);
        Assert.AreEqual("<html>Rate Limit Exceeded</html>", message);
        Assert.IsTrue(GoogleServiceErrors.IsRetryable(HttpStatusCode.Forbidden, reasons, message));
        Assert.IsFalse(GoogleServiceErrors.IsRetryable(HttpStatusCode.Forbidden, reasons, "Access denied"));
    }

    [Test]
    public void IsRetryable_RefusesDailyQuotaOnAnyStatus()
    {
        Assert.IsFalse(GoogleServiceErrors.IsRetryable(HttpStatusCode.TooManyRequests, ["dailyLimitExceeded"], "Daily Limit Exceeded"));
        Assert.IsFalse(GoogleServiceErrors.IsRetryable(HttpStatusCode.Forbidden, [], "Quota exceeded for quota metric 'Queries' and limit 'Queries per day'"));
        Assert.IsTrue(GoogleServiceErrors.IsRetryable(HttpStatusCode.Forbidden, [], "Quota exceeded for quota metric 'Queries' and limit 'Queries per minute per user'"));
    }

    /// <summary>
    /// A handler that lets a number of requests hang until their attempt is cancelled, and
    /// answers every request after those.
    /// </summary>
    private sealed class StallingHandler(int stalls) : HttpMessageHandler
    {
        private int _requests;

        public int Requests => _requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _requests) <= stalls)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return ScriptedHandler.Response(HttpStatusCode.OK, "{}");
        }
    }

    private static ConfigurableHttpClient CreateTimedClient(HttpMessageHandler handler, TimeSpan attemptTimeout)
    {
        var client = new ConfigurableHttpClient(new ConfigurableMessageHandler(handler));
        PerAttemptTimeoutHandler.Install(client, attemptTimeout);
        client.MessageHandler.NumTries = GoogleRetryHandler.NumTries;
        var retry = new GoogleRetryHandler(new NoWaitBackOff(GoogleRetryHandler.NumTries));
        client.MessageHandler.AddUnsuccessfulResponseHandler(retry);
        client.MessageHandler.AddExceptionHandler(retry);
        return client;
    }

    [Test]
    public void InstallTimeout_SwitchesOffTheClientTimeoutAndTimesEachAttempt()
    {
        var client = new ConfigurableHttpClient(new ConfigurableMessageHandler(new ScriptedHandler()));

        PerAttemptTimeoutHandler.Install(client, TimeSpan.FromSeconds(42));

        // The client's own timeout would bound the retries and their pauses as a whole
        Assert.AreEqual(Timeout.InfiniteTimeSpan, client.Timeout);
        var handler = client.MessageHandler.InnerHandler as PerAttemptTimeoutHandler;
        Assert.IsNotNull(handler, "The attempt timeout sits below the message handler, where the retries are decided");
        Assert.AreEqual(TimeSpan.FromSeconds(42), handler!.AttemptTimeout);
    }

    [Test]
    public async Task StalledAttempt_IsRetriedLikeAConnectionFailure()
    {
        var stalling = new StallingHandler(2);
        var client = CreateTimedClient(stalling, TimeSpan.FromMilliseconds(200));

        using var response = await SendAsync(client);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(3, stalling.Requests);
    }

    [Test]
    public void StalledRequest_FailsAsATimeoutAndNeverAsACancellation()
    {
        var stalling = new StallingHandler(int.MaxValue);
        var client = CreateTimedClient(stalling, TimeSpan.FromMilliseconds(50));

        var ex = Assert.CatchAsync<Exception>(async () => await SendAsync(client));

        Assert.IsFalse(ex is OperationCanceledException, $"A timeout of the client is not the caller cancelling, got {ex!.GetType().Name}");
        Assert.IsInstanceOf<HttpRequestException>(ex);
        Assert.IsInstanceOf<TimeoutException>(ex!.InnerException);
        Assert.AreEqual(GoogleRetryHandler.NumTries, stalling.Requests, "The retry policy, not the client's timeout, decides when to give up");
    }

    [Test]
    public void StalledRequest_ReportsTheCallersCancellation()
    {
        var stalling = new StallingHandler(int.MaxValue);
        var client = CreateTimedClient(stalling, TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.CatchAsync<OperationCanceledException>(async () => await client.GetAsync("https://www.googleapis.com/drive/v3/files/x?alt=media", cts.Token));

        Assert.AreEqual(1, stalling.Requests, "The caller's cancellation is not retried");
    }

    private static GoogleApiException DriveError(HttpStatusCode status, string? reason, string message)
        => new("drive", message)
        {
            HttpStatusCode = status,
            Error = reason == null ? null : new RequestError { Code = (int)status, Message = message, Errors = [new SingleError { Reason = reason, Message = message, Domain = "global" }] },
        };

    [Test]
    public void IsContentNotDownloadable_RecognisesTheDriveReasons()
    {
        Assert.IsTrue(GoogleServiceErrors.IsContentNotDownloadable(DriveError(HttpStatusCode.Forbidden, "cannotDownloadRevision", "This revision cannot be downloaded by the authenticated user.")));
        Assert.IsTrue(GoogleServiceErrors.IsContentNotDownloadable(DriveError(HttpStatusCode.Forbidden, "cannotDownloadFile", "The user does not have permission to download this file.")));
        Assert.IsTrue(GoogleServiceErrors.IsContentNotDownloadable(DriveError(HttpStatusCode.Forbidden, "cannotDownloadAbusiveFile", "This file has been identified as malware or spam and cannot be downloaded.")));
        // The media endpoints wrap the error document in an array, and the reason then only survives in the message
        Assert.IsTrue(GoogleServiceErrors.IsContentNotDownloadable(DriveError(HttpStatusCode.Forbidden, null, "This revision cannot be downloaded by the authenticated user. (reason: cannotDownloadRevision)")));

        Assert.IsFalse(GoogleServiceErrors.IsContentNotDownloadable(DriveError(HttpStatusCode.Forbidden, "forbidden", "The caller does not have permission")));
        Assert.IsFalse(GoogleServiceErrors.IsContentNotDownloadable(DriveError(HttpStatusCode.Forbidden, "userRateLimitExceeded", "User rate limit exceeded")));
        Assert.IsFalse(GoogleServiceErrors.IsContentNotDownloadable(DriveError(HttpStatusCode.NotFound, "cannotDownloadRevision", "mismatched status")));
        Assert.IsFalse(GoogleServiceErrors.IsContentNotDownloadable(new HttpRequestException("cannotDownloadRevision", null, HttpStatusCode.Forbidden)));
    }

    [Test]
    public void AsRevisionRefusal_SkipsTheRevisionAsInformation()
    {
        var refused = DriveError(HttpStatusCode.Forbidden, "cannotDownloadRevision", "This revision cannot be downloaded by the authenticated user.");
        var skipped = GoogleServiceErrors.AsRevisionRefusal(refused, "revision r1 of file A");
        Assert.IsInstanceOf<Duplicati.Library.Interface.SourceEntrySkippedException>(skipped);
        Assert.AreSame(refused, skipped.InnerException);
        StringAssert.Contains("revision r1 of file A", skipped.Message);

        var gone = DriveError(HttpStatusCode.NotFound, "notFound", "Revision not found: r1");
        Assert.IsInstanceOf<Duplicati.Library.Interface.SourceEntrySkippedException>(GoogleServiceErrors.AsRevisionRefusal(gone, "revision r1 of file A"));

        var other = DriveError(HttpStatusCode.InternalServerError, "backendError", "Backend Error");
        Assert.AreSame(other, GoogleServiceErrors.AsRevisionRefusal(other, "revision r1 of file A"), "Anything else is reported as it is");
    }

    [Test]
    public void AsContentRefusal_MapsToWhatTheEngineClassifies()
    {
        var refused = DriveError(HttpStatusCode.Forbidden, "cannotDownloadRevision", "This revision cannot be downloaded by the authenticated user.");
        var denied = GoogleServiceErrors.AsContentRefusal(refused, "revision r1 of file A");
        Assert.IsInstanceOf<UnauthorizedAccessException>(denied);
        Assert.AreSame(refused, denied.InnerException, "The Google error stays attached for the verbose log");
        StringAssert.Contains("revision r1 of file A", denied.Message);

        var gone = DriveError(HttpStatusCode.NotFound, "notFound", "Revision not found: r1");
        var notFound = GoogleServiceErrors.AsContentRefusal(gone, "revision r1 of file A");
        Assert.IsInstanceOf<System.IO.FileNotFoundException>(notFound);
        Assert.AreSame(gone, notFound.InnerException);

        var other = DriveError(HttpStatusCode.InternalServerError, "backendError", "Backend Error");
        Assert.AreSame(other, GoogleServiceErrors.AsContentRefusal(other, "revision r1 of file A"), "Anything else is reported as it is");

        var permission = DriveError(HttpStatusCode.Forbidden, "forbidden", "The caller does not have permission");
        Assert.AreSame(permission, GoogleServiceErrors.AsContentRefusal(permission, "revision r1 of file A"), "A 403 without a download reason is not a download refusal");
    }
}
