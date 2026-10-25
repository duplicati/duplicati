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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.GoogleDrive;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The chunks of a resumable upload are sent without the status check that
/// adds Google's error reason to a failure, and only a server error was
/// retried. Google asks for a rate limit, which it reports as 403 or 429, to
/// be retried, and for the upload to resume from what the server reports it
/// has. These pin that a rate-limited chunk is retried and resumed, and that
/// any other refused chunk fails at once with the reason Google gave.
///
/// Nothing here talks to Google. The backend sends every request through a
/// stub handler that plays an empty folder, an upload session, and a first
/// chunk that fails with the given status and body.
/// </summary>
[TestFixture]
public class GoogleUploadErrorReasonTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails
    /// instead of reaching the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";

    /// <summary>The url handed out for the upload session</summary>
    private const string SessionUrl = "http://upload.invalid/session-1";

    /// <summary>
    /// The per-user rate limit, as the Drive API documents it
    /// </summary>
    private const string UserRateLimitBody = """
        {"error":{"errors":[{"domain":"usageLimits","reason":"userRateLimitExceeded","message":"User Rate Limit Exceeded"}],"code":403,"message":"User Rate Limit Exceeded"}}
        """;

    /// <summary>
    /// The rate limit reported with 429, as the Drive API documents it
    /// </summary>
    private const string TooManyRequestsBody = """
        {"error":{"errors":[{"domain":"usageLimits","reason":"rateLimitExceeded","message":"Rate Limit Exceeded"}],"code":429,"message":"Rate Limit Exceeded"}}
        """;

    /// <summary>
    /// A missing permission, as the Drive API documents it
    /// </summary>
    private const string PermissionBody = """
        {"error":{"errors":[{"domain":"global","reason":"insufficientFilePermissions","message":"The user does not have sufficient permissions for file abc."}],"code":403,"message":"The user does not have sufficient permissions for file abc."}}
        """;

    /// <summary>
    /// Answers the Drive API for an empty folder, fails the first chunk with
    /// the given status and body, reports nothing committed on the resume
    /// query, and records the requests sent to the upload session
    /// </summary>
    private sealed class StubHandler(HttpStatusCode failStatus, string failBody) : HttpMessageHandler
    {
        /// <summary>The Content-Range header of each request to the session url, in order</summary>
        public List<string?> ContentRanges { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"access_token\":\"test-token\",\"expires\":3600}"));

            if (url.StartsWith(SessionUrl, StringComparison.Ordinal))
            {
                var contentRange = request.Content != null && request.Content.Headers.TryGetValues("Content-Range", out var values)
                    ? values.FirstOrDefault()
                    : null;
                ContentRanges.Add(contentRange);

                if (ContentRanges.Count == 1)
                    return Task.FromResult(Json(failStatus, failBody));

                // The resume query asks for the committed range without sending data
                if (contentRange != null && contentRange.StartsWith("bytes */", StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage((HttpStatusCode)308));

                return Task.FromResult(Json(HttpStatusCode.OK, "{\"id\":\"new-id\",\"title\":\"uploaded.txt\",\"mimeType\":\"application/octet-stream\"}"));
            }

            if (request.Headers.Contains("X-Upload-Content-Type"))
            {
                var resp = Json(HttpStatusCode.OK, "{}");
                resp.Headers.Location = new Uri(SessionUrl);
                return Task.FromResult(resp);
            }

            if (url.Contains("/drive/v2/about", StringComparison.Ordinal))
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"rootFolderId\":\"root\"}"));

            if (url.Contains("/drive/v2/files", StringComparison.Ordinal))
            {
                // The query is form encoded, so a space arrives as "+"
                var query = Uri.UnescapeDataString(request.RequestUri!.Query).Replace('+', ' ');

                // The folder the backend is configured for is looked up by name first
                if (query.Contains("title = 'target'", StringComparison.Ordinal))
                    return Task.FromResult(Json(HttpStatusCode.OK, "{\"items\":[{\"id\":\"folder-1\",\"title\":\"target\",\"mimeType\":\"application/vnd.google-apps.folder\"}]}"));

                return Task.FromResult(Json(HttpStatusCode.OK, "{\"items\":[]}"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
            => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static GoogleDrive Create(StubHandler handler)
        => new GoogleDrive("googledrive://target", new Dictionary<string, string?>
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl
        }, new HttpClient(handler));

    /// <summary>
    /// Red before the fix: only a server error was retried, so a rate limit
    /// failed the upload. The chunk is now retried after asking the server
    /// what it has, as for a server error.
    /// </summary>
    [TestCase(HttpStatusCode.Forbidden, UserRateLimitBody)]
    [TestCase(HttpStatusCode.TooManyRequests, TooManyRequestsBody)]
    [TestCase(HttpStatusCode.TooManyRequests, "")]
    [Category("Backend")]
    public async Task RateLimitedChunkIsResumed(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        using var backend = Create(handler);

        using var stream = new MemoryStream(new byte[1000]);
        await backend.PutAsync("uploaded.txt", stream, CancellationToken.None);

        Assert.AreEqual(3, handler.ContentRanges.Count, "expected the refused chunk, the resume query and the full chunk");
        Assert.AreEqual("bytes */1000", handler.ContentRanges[1]);
        Assert.AreEqual("bytes 0-999/1000", handler.ContentRanges[2]);
    }

    /// <summary>
    /// Red before the fix: the failure only said "Unexpected status code: 403".
    /// It now names the reason Google gave, and a missing permission is still
    /// not retried and not marked as a rate limit.
    /// </summary>
    [Test]
    [Category("Backend")]
    public void RefusedChunkNamesTheGoogleReason()
    {
        var handler = new StubHandler(HttpStatusCode.Forbidden, PermissionBody);
        using var backend = Create(handler);

        using var stream = new MemoryStream(new byte[1000]);
        var ex = Assert.CatchAsync(async () => await backend.PutAsync("uploaded.txt", stream, CancellationToken.None));

        StringAssert.Contains("insufficientFilePermissions", ex!.Message);
        Assert.IsInstanceOf<HttpRequestException>(ex);
        Assert.AreEqual(HttpStatusCode.Forbidden, ((HttpRequestException)ex).StatusCode);
        Assert.IsFalse(ExceptionExtensions.FlattenException(ex).Any(x => x is TooManyRequestException), $"Marked as a rate limit: {ex}");
        Assert.AreEqual(1, handler.ContentRanges.Count, "a refused chunk must not be retried");
    }
}
