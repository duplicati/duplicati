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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.GoogleCloudStorage;
using Duplicati.Library.Backend.GoogleDrive;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Google answers both a missing permission and a per-user rate limit with
/// 403 Forbidden, and only the error body tells them apart. The backends used
/// to drop that body, so a log showed a bare "403 (Forbidden)" and a rate limit
/// was handled like a refusal; Cloud Storage even reported it as a missing
/// bucket. These pin that the reason reaches the exception message, that a rate
/// limit is marked with <see cref="TooManyRequestException"/> so it is retried,
/// and that a real permission error is still a plain failure.
///
/// Nothing here talks to Google. The backends send every request through a
/// stub handler that answers the token request and fails everything else.
/// </summary>
[TestFixture]
public class GoogleApiErrorReasonTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails
    /// instead of reaching the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";

    /// <summary>
    /// The per-user rate limit, as the Drive API documents it
    /// </summary>
    private const string UserRateLimitBody = """
        {"error":{"errors":[{"domain":"usageLimits","reason":"userRateLimitExceeded","message":"User Rate Limit Exceeded"}],"code":403,"message":"User Rate Limit Exceeded"}}
        """;

    /// <summary>
    /// The project rate limit, as the Drive API documents it
    /// </summary>
    private const string RateLimitBody = """
        {"error":{"errors":[{"domain":"usageLimits","reason":"rateLimitExceeded","message":"Rate Limit Exceeded"}],"code":403,"message":"Rate Limit Exceeded"}}
        """;

    /// <summary>
    /// A missing permission, as the Drive API documents it
    /// </summary>
    private const string PermissionBody = """
        {"error":{"errors":[{"domain":"global","reason":"insufficientFilePermissions","message":"The user does not have sufficient permissions for file abc."}],"code":403,"message":"The user does not have sufficient permissions for file abc."}}
        """;

    /// <summary>
    /// Answers the token request and fails every other request with the given
    /// status and body
    /// </summary>
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        /// <summary>How many requests were failed</summary>
        public int Failed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"access_token\":\"test-token\",\"expires\":3600}"));

            Failed++;
            return Task.FromResult(Json(status, body));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
            => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static Dictionary<string, string?> Options() => new()
    {
        ["authid"] = "test-authid",
        ["oauth-url"] = OAuthUrl
    };

    private static Exception ListDrive(StubHandler handler)
    {
        var backend = new GoogleDrive("googledrive://target", Options(), new HttpClient(handler));
        var ex = Assert.CatchAsync(async () => await backend.ListAsync(CancellationToken.None).ToListAsync());
        Assert.Greater(handler.Failed, 0, "The failing response was never requested");
        return ex!;
    }

    private static Exception ListCloudStorage(StubHandler handler)
    {
        var backend = new GoogleCloudStorage("gcs://bucket/prefix", Options(), new HttpClient(handler));
        var ex = Assert.CatchAsync(async () => await backend.ListAsync(CancellationToken.None).ToListAsync());
        Assert.Greater(handler.Failed, 0, "The failing response was never requested");
        return ex!;
    }

    private static bool IsRateLimited(Exception ex)
        => ExceptionExtensions.FlattenException(ex).Any(x => x is TooManyRequestException);

    /// <summary>
    /// Red before the fix: the message was the bare status line. The reason is
    /// what tells the reader of a log which of the many 403s it was.
    /// </summary>
    [TestCase(UserRateLimitBody, "userRateLimitExceeded")]
    [TestCase(RateLimitBody, "rateLimitExceeded")]
    [TestCase(PermissionBody, "insufficientFilePermissions")]
    public void DriveFailureNamesTheGoogleReason(string body, string reason)
    {
        var ex = ListDrive(new StubHandler(HttpStatusCode.Forbidden, body));

        StringAssert.Contains(reason, ex.Message);
        StringAssert.Contains("403", ex.Message);
    }

    /// <summary>
    /// Red before the fix: a rate limit carried nothing that set it apart from
    /// a refusal. It now carries the exception the backends already use for a
    /// rate limit, and keeps the 403 status for callers that look at it.
    /// </summary>
    [TestCase(UserRateLimitBody)]
    [TestCase(RateLimitBody)]
    public void DriveRateLimitIsMarkedForRetry(string body)
    {
        var ex = ListDrive(new StubHandler(HttpStatusCode.Forbidden, body));

        Assert.IsTrue(IsRateLimited(ex), $"Not marked as a rate limit: {ex}");
        Assert.IsInstanceOf<HttpRequestException>(ex);
        Assert.AreEqual(HttpStatusCode.Forbidden, ((HttpRequestException)ex).StatusCode);
    }

    /// <summary>
    /// Green before and after: a missing permission does not clear by waiting,
    /// so it stays an ordinary failure with its 403 status.
    /// </summary>
    [Test]
    public void DrivePermissionErrorIsNotARateLimit()
    {
        var ex = ListDrive(new StubHandler(HttpStatusCode.Forbidden, PermissionBody));

        Assert.IsFalse(IsRateLimited(ex), $"Marked as a rate limit: {ex}");
        Assert.IsInstanceOf<HttpRequestException>(ex);
        Assert.AreEqual(HttpStatusCode.Forbidden, ((HttpRequestException)ex).StatusCode);
    }

    /// <summary>
    /// Green before and after: a body that is not a Google error leaves the
    /// failure as it was.
    /// </summary>
    [Test]
    public void DriveFailureWithoutAGoogleBodyIsUnchanged()
    {
        var ex = ListDrive(new StubHandler(HttpStatusCode.Forbidden, "<html>Forbidden</html>"));

        Assert.IsFalse(IsRateLimited(ex), $"Marked as a rate limit: {ex}");
        Assert.IsInstanceOf<HttpRequestException>(ex);
        Assert.AreEqual(HttpStatusCode.Forbidden, ((HttpRequestException)ex).StatusCode);
    }

    /// <summary>
    /// Red before the fix: Cloud Storage turned every 403 on a listing into a
    /// missing bucket, so a rate limit sent the caller off to create the bucket.
    /// A rate limit now reaches the caller as a rate limit, with its reason.
    /// </summary>
    [Test]
    public void CloudStorageRateLimitIsNotAMissingBucket()
    {
        var ex = ListCloudStorage(new StubHandler(HttpStatusCode.Forbidden, RateLimitBody));

        Assert.IsNotInstanceOf<FolderMissingException>(ex);
        Assert.IsTrue(IsRateLimited(ex), $"Not marked as a rate limit: {ex}");
        StringAssert.Contains("rateLimitExceeded", ex.Message);
    }

    /// <summary>
    /// Green before and after: any other 403 on a listing is still reported as
    /// a missing bucket, as it always was.
    /// </summary>
    [Test]
    public void CloudStoragePermissionErrorIsStillAMissingBucket()
    {
        var ex = ListCloudStorage(new StubHandler(HttpStatusCode.Forbidden, PermissionBody));

        Assert.IsInstanceOf<FolderMissingException>(ex);
    }
}
