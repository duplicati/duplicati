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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.GoogleDrive;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// An upload that times out on the client can still be finished by Drive, and
/// the file it made only shows up in a listing some time later. The retry does
/// not see it, so it creates a second file with the same name. A delete has to
/// remove both, or one is left on the destination for good. Seen in the Google
/// Drive live tests, where a folder that should be empty kept one file.
/// </summary>
[TestFixture]
public class GoogleDriveInterruptedUploadTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails
    /// instead of reaching the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";

    /// <summary>The prefix of the urls handed out for the upload sessions</summary>
    private const string SessionUrl = "http://upload.invalid/session-";

    /// <summary>The folder the backend is pointed at</summary>
    private const string FolderId = "folder-1";

    /// <summary>The name that is uploaded</summary>
    private const string RemoteName = "uploaded.dblock.zip";

    /// <summary>
    /// Holds the files of one Drive folder. The first upload is cut off before
    /// its answer arrives, but the file it made is kept and shows up in the
    /// listing once <see cref="RevealInterruptedUpload"/> is called.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        /// <summary>The files a listing reports, by id</summary>
        private readonly Dictionary<string, string> m_visible = new();

        /// <summary>The file the interrupted upload made, which a listing does not report yet</summary>
        private (string Id, string Title)? m_hidden;

        /// <summary>The number of upload sessions that were started</summary>
        private int m_sessions;

        /// <summary>Whether the first upload is cut off</summary>
        private readonly bool m_interruptFirstUpload;

        public StubHandler(bool interruptFirstUpload)
            => m_interruptFirstUpload = interruptFirstUpload;

        /// <summary>The method of each request that started an upload session, in order</summary>
        public List<HttpMethod> UploadMethods { get; } = new();

        /// <summary>The file ids that were asked to be deleted, in order</summary>
        public List<string> Deleted { get; } = new();

        /// <summary>The ids of the files the folder holds, whether a listing reports them or not</summary>
        public IEnumerable<string> Remaining
            => m_visible.Keys.Concat(m_hidden == null ? [] : [m_hidden.Value.Id]);

        /// <summary>Lets the listing report the file the interrupted upload made</summary>
        public void RevealInterruptedUpload()
        {
            if (m_hidden != null)
                m_visible[m_hidden.Value.Id] = m_hidden.Value.Title;
            m_hidden = null;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Task.FromResult(Json("{\"access_token\":\"test-token\",\"expires\":3600}"));

            if (url.Contains("/drive/v2/about", StringComparison.Ordinal))
                return Task.FromResult(Json("{\"rootFolderId\":\"root\"}"));

            if (url.StartsWith(SessionUrl, StringComparison.Ordinal))
            {
                var id = "id-" + url[SessionUrl.Length..];

                // The first upload reaches Drive, which makes the file, but the
                // answer never arrives before the client gives up on it
                if (m_interruptFirstUpload && id == "id-1")
                {
                    m_hidden = (id, RemoteName);
                    throw new TaskCanceledException("The operation was canceled.");
                }

                m_visible[id] = RemoteName;
                return Task.FromResult(Json(File(id, RemoteName)));
            }

            if (request.Headers.Contains("X-Upload-Content-Type"))
            {
                UploadMethods.Add(request.Method);
                m_sessions++;
                var resp = Json("{}");
                resp.Headers.Location = new Uri(SessionUrl + m_sessions);
                return Task.FromResult(resp);
            }

            if (request.Method == HttpMethod.Delete)
            {
                // ".../drive/v2/files/<id>" with the query string after it
                var path = request.RequestUri!.AbsolutePath;
                var id = path[(path.LastIndexOf('/') + 1)..];
                Deleted.Add(id);
                return Task.FromResult(m_visible.Remove(id)
                    ? Json("{}")
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (url.Contains("/drive/v2/files", StringComparison.Ordinal))
            {
                // The query is form encoded, so a space arrives as "+"
                var query = Uri.UnescapeDataString(request.RequestUri!.Query).Replace('+', ' ');
                var title = Regex.Match(query, "title = '([^']*)'");

                // The folder the backend is configured for is looked up by name first
                if (title.Success && title.Groups[1].Value == "target")
                    return Task.FromResult(Json($"{{\"items\":[{{\"id\":\"{FolderId}\",\"title\":\"target\",\"mimeType\":\"application/vnd.google-apps.folder\"}}]}}"));

                var items = m_visible
                    .Where(x => !title.Success || x.Value == title.Groups[1].Value)
                    .Select(x => File(x.Key, x.Value));
                return Task.FromResult(Json($"{{\"items\":[{string.Join(",", items)}]}}"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static string File(string id, string title)
            => $"{{\"id\":\"{id}\",\"title\":\"{title}\",\"mimeType\":\"application/octet-stream\",\"fileSize\":3}}";

        private static HttpResponseMessage Json(string body)
            => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static (GoogleDrive Backend, StubHandler Handler) Create(bool interruptFirstUpload)
    {
        var handler = new StubHandler(interruptFirstUpload);
        var backend = new GoogleDrive("googledrive://target", new Dictionary<string, string?>
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl
        }, new HttpClient(handler));

        return (backend, handler);
    }

    /// <summary>
    /// Uploads the name twice, the way a caller retries a failed upload: the
    /// first attempt is cut off, the second one succeeds
    /// </summary>
    private static async Task UploadWithRetryAsync(GoogleDrive backend)
    {
        Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            using var first = new MemoryStream(new byte[3]);
            await backend.PutAsync(RemoteName, first, CancellationToken.None);
        });

        using var second = new MemoryStream(new byte[3]);
        await backend.PutAsync(RemoteName, second, CancellationToken.None);
    }

    /// <summary>
    /// The retry cannot know about the file Drive made, so it creates a new one;
    /// a delete of the name afterwards has to take both away
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task ADeleteAfterARetriedUploadRemovesTheInterruptedCopy()
    {
        var (backend, handler) = Create(true);
        using var _b = backend;

        await UploadWithRetryAsync(backend);

        // Both attempts started a new file, as neither could see an existing one
        CollectionAssert.AreEqual(new[] { HttpMethod.Post, HttpMethod.Post }, handler.UploadMethods);

        handler.RevealInterruptedUpload();
        await backend.DeleteAsync(RemoteName, CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { "id-1", "id-2" }, handler.Deleted);
        CollectionAssert.IsEmpty(handler.Remaining, "a file was left on the destination");
    }

    /// <summary>
    /// The order the backend tester uses: a listing between the upload and the
    /// delete, taken before the interrupted copy shows up, still leaves the
    /// delete to take both away
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task ADeleteAfterAnEarlierListingRemovesTheInterruptedCopy()
    {
        var (backend, handler) = Create(true);
        using var _b = backend;

        await UploadWithRetryAsync(backend);

        var names = new List<string>();
        await foreach (var e in backend.ListAsync(CancellationToken.None))
            names.Add(e.Name);
        CollectionAssert.AreEqual(new[] { RemoteName }, names);

        handler.RevealInterruptedUpload();
        await backend.DeleteAsync(RemoteName, CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { "id-1", "id-2" }, handler.Deleted);
        CollectionAssert.IsEmpty(handler.Remaining, "a file was left on the destination");
    }

    /// <summary>
    /// Green before and after: a name held once is deleted with one request,
    /// whether or not a listing filled the cache first
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task ADeleteOfASingleUploadRemovesOneFile()
    {
        var (backend, handler) = Create(false);
        using var _b = backend;

        using var stream = new MemoryStream(new byte[3]);
        await backend.PutAsync(RemoteName, stream, CancellationToken.None);
        await backend.DeleteAsync(RemoteName, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "id-1" }, handler.Deleted);
        CollectionAssert.IsEmpty(handler.Remaining);
    }
}
