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
/// Tests the Content-Range header sent by the resumable upload, which must be
/// well-formed for a zero-byte file, as such a file has no byte range to describe
/// </summary>
[TestFixture]
public class GoogleDriveZeroByteUploadTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails
    /// instead of reaching the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";

    /// <summary>The url handed out for the upload session</summary>
    private const string SessionUrl = "http://upload.invalid/session-1";

    /// <summary>
    /// Answers the Drive API for an empty folder, and records the uploaded chunks
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        /// <summary>The Content-Range header of each chunk, in order</summary>
        public List<string?> ContentRanges { get; } = new();

        /// <summary>The size of the body of each chunk, in order</summary>
        public List<long> BodySizes { get; } = new();

        /// <summary>The number of upload sessions that were started</summary>
        public int SessionsStarted { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Json("{\"access_token\":\"test-token\",\"expires\":3600}");

            if (url.StartsWith(SessionUrl, StringComparison.Ordinal))
            {
                ContentRanges.Add(request.Content != null && request.Content.Headers.TryGetValues("Content-Range", out var values)
                    ? values.FirstOrDefault()
                    : null);
                BodySizes.Add(request.Content == null
                    ? 0
                    : (await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false)).LongLength);

                return Json("{\"id\":\"new-id\",\"title\":\"uploaded.txt\",\"mimeType\":\"application/octet-stream\"}");
            }

            if (request.Headers.Contains("X-Upload-Content-Type"))
            {
                SessionsStarted++;
                var resp = Json("{}");
                resp.Headers.Location = new Uri(SessionUrl);
                return resp;
            }

            if (url.Contains("/drive/v2/about", StringComparison.Ordinal))
                return Json("{\"rootFolderId\":\"root\"}");

            if (url.Contains("/drive/v2/files", StringComparison.Ordinal))
            {
                // The query is form encoded, so a space arrives as "+"
                var query = Uri.UnescapeDataString(request.RequestUri!.Query).Replace('+', ' ');

                // The folder the backend is configured for is looked up by name first
                if (query.Contains("title = 'target'", StringComparison.Ordinal))
                    return Json("{\"items\":[{\"id\":\"folder-1\",\"title\":\"target\",\"mimeType\":\"application/vnd.google-apps.folder\"}]}");

                return Json("{\"items\":[]}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body)
            => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static (GoogleDrive Backend, StubHandler Handler) Create()
    {
        var handler = new StubHandler();
        var backend = new GoogleDrive("googledrive://target", new Dictionary<string, string?>
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl
        }, new HttpClient(handler));

        return (backend, handler);
    }

    /// <summary>
    /// A zero-byte file has no last byte, so the range "0-(length - 1)" would be
    /// "bytes 0--1/0", which is rejected before the request is ever sent
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task AZeroByteFileIsUploaded()
    {
        var (backend, handler) = Create();
        using var _b = backend;

        using var stream = new MemoryStream();
        await backend.PutAsync("uploaded.txt", stream, CancellationToken.None);

        Assert.AreEqual(1, handler.SessionsStarted);
        Assert.AreEqual(1, handler.ContentRanges.Count, "the empty file should be sent as a single request");
        Assert.AreEqual("bytes */0", handler.ContentRanges[0]);
        Assert.AreEqual(0, handler.BodySizes[0]);
    }

    /// <summary>
    /// The range of a file with content is unchanged
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task AFileWithContentIsUploadedWithItsByteRange()
    {
        var (backend, handler) = Create();
        using var _b = backend;

        using var stream = new MemoryStream(new byte[10]);
        await backend.PutAsync("uploaded.txt", stream, CancellationToken.None);

        Assert.AreEqual(1, handler.ContentRanges.Count);
        Assert.AreEqual("bytes 0-9/10", handler.ContentRanges[0]);
        Assert.AreEqual(10, handler.BodySizes[0]);
    }
}
