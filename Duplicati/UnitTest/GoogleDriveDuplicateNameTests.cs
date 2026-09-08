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
using Duplicati.Library.Backend.GoogleDrive;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Google Drive keeps a permanent id for every file and treats the name as a
/// label, so two files in one folder can carry the same name. The backend keeps
/// a name to entries cache to resolve them, and every copy has to stay in it:
/// a delete has to remove all of them, and a read has to pick a definite one.
/// The listing, on the other hand, has to answer with one entry per name, or the
/// remote verification refuses to run at all. Reported as issue #5846.
/// </summary>
[TestFixture]
public class GoogleDriveDuplicateNameTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails
    /// instead of reaching the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";

    /// <summary>The folder the backend is pointed at</summary>
    private const string FolderId = "folder-1";

    /// <summary>
    /// Answers the Drive API from a fixed set of items, and records the deletes
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string[] _items;

        /// <summary>The file ids that were asked to be deleted, in order</summary>
        public List<string> Deleted { get; } = new();

        public StubHandler(string[] items)
            => _items = items;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Task.FromResult(Json("{\"access_token\":\"test-token\",\"expires\":3600}"));

            if (url.Contains("/drive/v2/about", StringComparison.Ordinal))
                return Task.FromResult(Json("{\"rootFolderId\":\"root\"}"));

            if (request.Method == HttpMethod.Delete)
            {
                // ".../drive/v2/files/<id>" with the query string after it
                var path = request.RequestUri!.AbsolutePath;
                Deleted.Add(path[(path.LastIndexOf('/') + 1)..]);
                return Task.FromResult(Json("{}"));
            }

            if (url.Contains("/drive/v2/files", StringComparison.Ordinal))
            {
                // The query is form encoded, so a space arrives as "+"
                var query = Uri.UnescapeDataString(request.RequestUri!.Query).Replace('+', ' ');

                // The folder the backend is configured for is looked up by name first
                if (query.Contains("title = 'target'", StringComparison.Ordinal))
                    return Task.FromResult(Json(Response([Folder(FolderId, "target")])));

                return Task.FromResult(Json(Response(_items)));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static string Response(IEnumerable<string> items)
            => $"{{\"items\":[{string.Join(",", items)}]}}";

        private static HttpResponseMessage Json(string body)
            => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static string Folder(string id, string title)
        => $"{{\"id\":\"{id}\",\"title\":\"{title}\",\"mimeType\":\"application/vnd.google-apps.folder\"}}";

    /// <summary>
    /// A file as the Drive listing reports it
    /// </summary>
    /// <param name="id">The permanent id of the file</param>
    /// <param name="title">The name, which is only a label and may repeat</param>
    /// <param name="size">The size in bytes</param>
    /// <param name="created">The creation date, which is what decides between copies</param>
    private static string File(string id, string title, long size, string created)
        => $"{{\"id\":\"{id}\",\"title\":\"{title}\",\"mimeType\":\"application/octet-stream\","
            + $"\"fileSize\":{size},\"createdDate\":\"{created}\",\"modifiedDate\":\"{created}\"}}";

    private static (GoogleDrive Backend, StubHandler Handler) Create(params string[] items)
    {
        var handler = new StubHandler(items);
        var backend = new GoogleDrive("googledrive://target", new Dictionary<string, string?>
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl
        }, new HttpClient(handler));

        return (backend, handler);
    }

    private static async Task<string[]> ListNamesAsync(GoogleDrive backend)
    {
        var names = new List<string>();
        await foreach (var e in backend.ListAsync(CancellationToken.None))
            names.Add(e.Name);
        return names.ToArray();
    }

    /// <summary>
    /// The point of the issue: a name that Drive holds twice must reach the rest
    /// of Duplicati once, or the remote verification stops every operation with
    /// "Found remote files reported as duplicates"
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task ANameHeldTwiceIsListedOnce()
    {
        var (backend, _) = Create(
            File("id-old", "duplicate.dblock.zip", 10, "2024-01-01T00:00:00Z"),
            File("id-new", "duplicate.dblock.zip", 20, "2024-06-01T00:00:00Z"),
            File("id-other", "single.dblock.zip", 30, "2024-01-01T00:00:00Z"));
        using var _b = backend;

        var names = await ListNamesAsync(backend);

        Assert.AreEqual(2, names.Length, $"got: {string.Join(", ", names)}");
        Assert.That(names, Does.Contain("duplicate.dblock.zip"));
        Assert.That(names, Does.Contain("single.dblock.zip"));
    }

    /// <summary>
    /// Which copy is reported has to be the same one a read would fetch, and that
    /// is the newest
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task TheCopyThatIsListedIsTheNewestOne()
    {
        var (backend, _) = Create(
            File("id-old", "duplicate.dblock.zip", 10, "2024-01-01T00:00:00Z"),
            File("id-new", "duplicate.dblock.zip", 20, "2024-06-01T00:00:00Z"));
        using var _b = backend;

        var sizes = new List<long>();
        await foreach (var e in backend.ListAsync(CancellationToken.None))
            sizes.Add(e.Size);

        Assert.AreEqual(1, sizes.Count);
        Assert.AreEqual(20, sizes[0], "the older copy was reported");
    }

    /// <summary>
    /// The reason every copy has to stay in the cache: a delete removes the name,
    /// which means all of the files wearing it. Keeping only one would leave the
    /// other behind on the destination forever.
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task ADeleteAfterAListingRemovesEveryCopy()
    {
        var (backend, handler) = Create(
            File("id-old", "duplicate.dblock.zip", 10, "2024-01-01T00:00:00Z"),
            File("id-new", "duplicate.dblock.zip", 20, "2024-06-01T00:00:00Z"));
        using var _b = backend;

        // The listing is what fills the cache, and the delete reads it back
        await ListNamesAsync(backend);
        await backend.DeleteAsync("duplicate.dblock.zip", CancellationToken.None);

        Assert.AreEqual(2, handler.Deleted.Count, $"deleted: {string.Join(", ", handler.Deleted)}");
        Assert.That(handler.Deleted, Does.Contain("id-old"));
        Assert.That(handler.Deleted, Does.Contain("id-new"));
    }

    /// <summary>
    /// Green before and after: names that appear once are reported as they always
    /// were, so the change reaches no further than the fault did
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task NamesThatAppearOnceAreUnchanged()
    {
        var (backend, _) = Create(
            File("id-a", "a.dblock.zip", 10, "2024-01-01T00:00:00Z"),
            File("id-b", "b.dblock.zip", 20, "2024-02-01T00:00:00Z"),
            File("id-c", "c.dblock.zip", 30, "2024-03-01T00:00:00Z"));
        using var _b = backend;

        var names = await ListNamesAsync(backend);

        Assert.AreEqual(3, names.Length, $"got: {string.Join(", ", names)}");
        Assert.That(names, Does.Contain("a.dblock.zip"));
        Assert.That(names, Does.Contain("b.dblock.zip"));
        Assert.That(names, Does.Contain("c.dblock.zip"));
    }
}
