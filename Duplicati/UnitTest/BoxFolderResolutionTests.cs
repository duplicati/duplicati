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
using Duplicati.Library.Backend.Box;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The Box folder lookup lists the parent folder and creates the folder when it
/// is not in the listing. When the listing does not show a folder that exists,
/// Box answers the create with "409 item_name_in_use" and the backend reported
/// the folder as missing. These tests drive the backend against stubbed Box API
/// responses, so no network or credentials are needed.
/// </summary>
[TestFixture]
public class BoxFolderResolutionTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails
    /// instead of reaching the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";
    private const string ItemsUrlPrefix = "https://api.box.com/2.0/folders/";
    private const string CreateFolderUrl = "https://api.box.com/2.0/folders";

    /// <summary>
    /// Serves canned responses and records the requests that were made
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<string> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            Requests.Add($"{request.Method} {url}");

            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"access_token\":\"test-token\",\"expires\":3600}"));

            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body)
        => new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>
    /// Builds a folder item listing, as returned by GET /folders/{id}/items
    /// </summary>
    private static string Listing(long totalCount, params string[] entries)
        => $"{{\"total_count\":{totalCount},\"entries\":[{string.Join(",", entries)}]}}";

    private static string Item(string type, string id, string name)
        => $"{{\"type\":\"{type}\",\"id\":\"{id}\",\"name\":\"{name}\"}}";

    /// <summary>
    /// Builds a Box error body for a name that is already in use
    /// </summary>
    /// <param name="conflicts">The raw json for context_info.conflicts, or null to omit it</param>
    private static string NameInUseError(string? conflicts)
    {
        var context = conflicts == null ? "" : $",\"context_info\":{{\"conflicts\":{conflicts}}}";
        return $"{{\"type\":\"error\",\"status\":409,\"code\":\"item_name_in_use\",\"message\":\"Item with the same name already exists\"{context}}}";
    }

    private static BoxBackend CreateBackend(StubHandler handler, string url = "box://target")
        => new BoxBackend(url, new Dictionary<string, string?>
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl
        }, new HttpClient(handler));

    /// <summary>
    /// Enumerates the backend listing, which resolves the folder id first
    /// </summary>
    private static async Task<List<IFileEntry>> ListAsync(BoxBackend backend)
    {
        var result = new List<IFileEntry>();
        await foreach (var entry in backend.ListAsync(CancellationToken.None).ConfigureAwait(false))
            result.Add(entry);
        return result;
    }

    private static bool RequestedItemsOf(StubHandler handler, string folderId)
        => handler.Requests.Any(x => x.StartsWith($"GET {ItemsUrlPrefix}{folderId}/items", StringComparison.Ordinal));

    [Test]
    [Category("Backend")]
    public async Task FolderIsFoundWhenTheListingStartsWithAFile()
    {
        // Box is not asked for a specific sort order, so a file can precede the
        // folder; that must not end the folder scan
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith($"{ItemsUrlPrefix}0/items", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, Listing(2, Item("file", "f1", "aaa.txt"), Item("folder", "100", "target")));

            return Json(HttpStatusCode.OK, Listing(0));
        });

        using var backend = CreateBackend(handler);
        var entries = await ListAsync(backend);

        Assert.AreEqual(0, entries.Count);
        Assert.IsTrue(RequestedItemsOf(handler, "100"),
            "The folder that is present in the listing should be used");
    }

    [Test]
    [Category("Backend")]
    public async Task FolderIsFoundWhenTheNameDiffersInCasing()
    {
        // Box.com does not allow sibling names that differ only in casing, and
        // reports the name as used when creating one, so a folder that differs
        // only in casing is the folder that was asked for
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith($"{ItemsUrlPrefix}0/items", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, Listing(1, Item("folder", "500", "Target")));

            return Json(HttpStatusCode.OK, Listing(0));
        });

        using var backend = CreateBackend(handler, "box://target");
        await ListAsync(backend);

        Assert.IsTrue(RequestedItemsOf(handler, "500"),
            "A folder that differs only in casing should be used instead of being created");
    }

    [Test]
    [Category("Backend")]
    public async Task FolderIsFoundWhenAPageReturnsFewerEntriesThanRequested()
    {
        // Box may return fewer entries than the requested limit while more
        // remain, so the next offset must follow the entries actually received
        const int firstPageSize = 150;
        const int totalCount = 201;
        const int targetIndex = 175;

        var all = Enumerable.Range(0, totalCount)
            .Select(i => Item("folder", $"{1000 + i}", i == targetIndex ? "target" : $"other-{i}"))
            .ToArray();

        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith($"{ItemsUrlPrefix}0/items", StringComparison.Ordinal))
            {
                var offset = int.Parse(System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["offset"] ?? "0");
                var page = all.Skip(offset).Take(offset == 0 ? firstPageSize : totalCount).ToArray();
                return Json(HttpStatusCode.OK, Listing(totalCount, page));
            }

            return Json(HttpStatusCode.OK, Listing(0));
        });

        using var backend = CreateBackend(handler);
        await ListAsync(backend);

        Assert.IsTrue(RequestedItemsOf(handler, $"{1000 + targetIndex}"),
            "A short first page must not cause the remaining entries to be skipped");
    }

    [Test]
    [Category("Backend")]
    public async Task NameInUseResolvesTheConflictingFolder()
    {
        // The listing does not show the folder, but Box reports the name as
        // taken and names the conflicting item
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (request.Method == HttpMethod.Post && url == CreateFolderUrl)
                return Json(HttpStatusCode.Conflict, NameInUseError($"[{Item("folder", "200", "Target")}]"));

            return Json(HttpStatusCode.OK, Listing(0));
        });

        using var backend = CreateBackend(handler);
        await backend.CreateFolderAsync(CancellationToken.None);
        await ListAsync(backend);

        Assert.IsTrue(RequestedItemsOf(handler, "200"),
            "The folder id reported by Box should be used");
    }

    [Test]
    [Category("Backend")]
    public async Task NameInUseResolvesByListingAgainWhenNoConflictIsReported()
    {
        // Without the conflicting item in the error, the folder has to be
        // looked up again; it is now visible
        var listCalls = 0;
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (request.Method == HttpMethod.Post && url == CreateFolderUrl)
                return Json(HttpStatusCode.Conflict, NameInUseError(null));

            if (url.StartsWith($"{ItemsUrlPrefix}0/items", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, ++listCalls == 1
                    ? Listing(0)
                    : Listing(1, Item("folder", "300", "target")));

            return Json(HttpStatusCode.OK, Listing(0));
        });

        using var backend = CreateBackend(handler);
        await backend.CreateFolderAsync(CancellationToken.None);
        await ListAsync(backend);

        Assert.IsTrue(RequestedItemsOf(handler, "300"),
            "The folder should be resolved by listing the parent again");
    }

    [Test]
    [Category("Backend")]
    public void NameInUseByAFileIsStillReported()
    {
        // A file with the same name means the folder really cannot be created,
        // so the file id must not be mistaken for the folder
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (request.Method == HttpMethod.Post && url == CreateFolderUrl)
                return Json(HttpStatusCode.Conflict, NameInUseError($"[{Item("file", "400", "target")}]"));

            return Json(HttpStatusCode.OK, Listing(0));
        });

        using var backend = CreateBackend(handler);
        var ex = Assert.CatchAsync<UserInformationException>(async () => await backend.CreateFolderAsync(CancellationToken.None));

        StringAssert.Contains("item_name_in_use", ex!.Message);
        Assert.IsFalse(RequestedItemsOf(handler, "400"),
            "A conflicting file must not be used as the folder");
    }

    [Test]
    [Category("Backend")]
    public void BackendIsCreatedThroughTheLoader()
    {
        // The loader creates the backend with two arguments through reflection,
        // which only finds a constructor with a matching number of parameters
        using var backend = Library.DynamicLoader.BackendLoader.GetBackend("box://target",
            new Dictionary<string, string> { ["authid"] = "test-authid" });

        Assert.IsNotNull(backend, "The Box backend should be creatable through the loader");
    }

    [TestCase(HttpStatusCode.Forbidden, "{\"type\":\"error\",\"status\":403,\"code\":\"access_denied\",\"message\":\"Access denied\"}",
        "Box.com ErrorResponse: 403 - access_denied: Access denied")]
    [TestCase(HttpStatusCode.Conflict, "{\"type\":\"error\",\"status\":409,\"code\":\"item_name_in_use\",\"message\":\"Item with the same name already exists\",\"context_info\":{\"conflicts\":{\"type\":\"file\",\"id\":\"1\",\"name\":\"x\"}}}",
        "Box.com ErrorResponse: 409 - item_name_in_use: Item with the same name already exists")]
    [Category("Backend")]
    public void ErrorMessagesAreUnchanged(HttpStatusCode status, string body, string expected)
    {
        // The reported errors must keep their wording, including when Box sends
        // a single conflicting item instead of a list. Catching
        // UserInformationException also covers the existing handlers, as the
        // Box errors are reported through a subclass of it.
        var handler = new StubHandler(_ => Json(status, body));

        using var backend = CreateBackend(handler);
        var ex = Assert.CatchAsync<UserInformationException>(async () => await ListAsync(backend));

        Assert.AreEqual(expected, ex!.Message);
    }
}
