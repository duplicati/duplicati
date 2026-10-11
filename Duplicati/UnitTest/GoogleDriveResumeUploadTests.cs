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
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Tests the resume path of the resumable upload. After a failed chunk the
/// uploader asks the server how much it has, and the server answers that
/// question with a 308 status, which is not a success code and so must not
/// be treated as an error.
/// </summary>
[TestFixture]
public class GoogleDriveResumeUploadTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails
    /// instead of reaching the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";

    /// <summary>The url handed out for the upload session</summary>
    private const string SessionUrl = "http://upload.invalid/session-1";

    /// <summary>
    /// Answers the Drive API for an empty folder, fails the first chunk,
    /// reports the given committed range on the resume query, and records
    /// the chunks that were sent
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        /// <summary>The number of bytes the server reports as received, or null to report nothing</summary>
        private readonly long? m_committedBytes;

        /// <summary>The Content-Range header of each request to the session url, in order</summary>
        public List<string?> ContentRanges { get; } = new();

        /// <summary>The size of the body of each request to the session url, in order</summary>
        public List<long> BodySizes { get; } = new();

        /// <summary>The number of upload sessions that were started</summary>
        public int SessionsStarted { get; private set; }

        public StubHandler(long? committedBytes)
        {
            m_committedBytes = committedBytes;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "{\"access_token\":\"test-token\",\"expires\":3600}");

            if (url.StartsWith(SessionUrl, StringComparison.Ordinal))
            {
                var contentRange = request.Content != null && request.Content.Headers.TryGetValues("Content-Range", out var values)
                    ? values.FirstOrDefault()
                    : null;
                ContentRanges.Add(contentRange);
                BodySizes.Add(request.Content == null
                    ? 0
                    : (await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false)).LongLength);

                // The first chunk fails with a retryable error
                if (ContentRanges.Count == 1)
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

                // The resume query asks for the committed range without sending data
                if (contentRange != null && contentRange.StartsWith("bytes */", StringComparison.Ordinal))
                {
                    var resp = new HttpResponseMessage((HttpStatusCode)308);
                    if (m_committedBytes != null)
                        resp.Headers.TryAddWithoutValidation("Range", $"bytes=0-{m_committedBytes.Value - 1}");
                    return resp;
                }

                return Json(HttpStatusCode.OK, "{\"id\":\"new-id\",\"title\":\"uploaded.txt\",\"mimeType\":\"application/octet-stream\"}");
            }

            if (request.Headers.Contains("X-Upload-Content-Type"))
            {
                SessionsStarted++;
                var resp = Json(HttpStatusCode.OK, "{}");
                resp.Headers.Location = new Uri(SessionUrl);
                return resp;
            }

            if (url.Contains("/drive/v2/about", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "{\"rootFolderId\":\"root\"}");

            if (url.Contains("/drive/v2/files", StringComparison.Ordinal))
            {
                // The query is form encoded, so a space arrives as "+"
                var query = Uri.UnescapeDataString(request.RequestUri!.Query).Replace('+', ' ');

                // The folder the backend is configured for is looked up by name first
                if (query.Contains("title = 'target'", StringComparison.Ordinal))
                    return Json(HttpStatusCode.OK, "{\"items\":[{\"id\":\"folder-1\",\"title\":\"target\",\"mimeType\":\"application/vnd.google-apps.folder\"}]}");

                return Json(HttpStatusCode.OK, "{\"items\":[]}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
            => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static (GoogleDrive Backend, StubHandler Handler) Create(long? committedBytes)
    {
        var handler = new StubHandler(committedBytes);
        var backend = new GoogleDrive("googledrive://target", new Dictionary<string, string?>
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl
        }, new HttpClient(handler));

        return (backend, handler);
    }

    /// <summary>
    /// The server has part of the file, so the upload continues from the
    /// byte after the committed range
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task UploadResumesFromTheCommittedRange()
    {
        var (backend, handler) = Create(400);
        using var _b = backend;

        using var stream = new MemoryStream(new byte[1000]);
        await backend.PutAsync("uploaded.txt", stream, CancellationToken.None);

        Assert.AreEqual(1, handler.SessionsStarted, "the session should not be restarted");
        Assert.AreEqual(3, handler.ContentRanges.Count, "expected the failed chunk, the resume query and the final chunk");
        Assert.AreEqual("bytes 0-999/1000", handler.ContentRanges[0]);
        Assert.AreEqual("bytes */1000", handler.ContentRanges[1]);
        Assert.AreEqual(0, handler.BodySizes[1], "the resume query must not send data");
        Assert.AreEqual("bytes 400-999/1000", handler.ContentRanges[2]);
        Assert.AreEqual(600, handler.BodySizes[2]);
    }

    /// <summary>
    /// A 308 without a Range header means nothing reached the server,
    /// so the upload starts over from the first byte
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task UploadRestartsWhenNothingWasCommitted()
    {
        var (backend, handler) = Create(null);
        using var _b = backend;

        using var stream = new MemoryStream(new byte[1000]);
        await backend.PutAsync("uploaded.txt", stream, CancellationToken.None);

        Assert.AreEqual(1, handler.SessionsStarted, "the session should not be restarted");
        Assert.AreEqual(3, handler.ContentRanges.Count, "expected the failed chunk, the resume query and the full chunk");
        Assert.AreEqual("bytes */1000", handler.ContentRanges[1]);
        Assert.AreEqual("bytes 0-999/1000", handler.ContentRanges[2]);
        Assert.AreEqual(1000, handler.BodySizes[2]);
    }
}
