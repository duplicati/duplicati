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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library;
using Duplicati.Library.Backend;
using Duplicati.Library.Backend.Box;
using Duplicati.Library.Backend.GoogleDrive;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Tests the recursive folder delete (<see cref="IFolderDeleteEnabledBackend"/>) of the
/// backends that talk to a web API, with the API answered by a stub: which request
/// deletes the folder, that a folder that is already gone is not an error, and that a
/// path outside the backend folder is refused before any request is made.
/// </summary>
[TestFixture]
[Category("Backend")]
public class FolderDeleteBackendTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails
    /// instead of reaching the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";

    /// <summary>
    /// The paths no backend may delete: the backend folder itself and anything outside it
    /// </summary>
    private static readonly string[] RefusedPaths = ["", " ", "/gone", "gone/", "gone//inner", ".", "gone/../other", "..", "gone\\inner"];

    /// <summary>A request a backend sent</summary>
    private sealed record SentRequest(HttpMethod Method, string Url, string? Body);

    /// <summary>
    /// Answers the requests of a backend from a function, and records them
    /// </summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        /// <summary>The requests that change something</summary>
        public IEnumerable<SentRequest> Changes => Requests.Where(x => x.Method != HttpMethod.Get);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "{\"access_token\":\"test-token\",\"expires\":3600}");

            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SentRequest(request.Method, url, body));

            // As the handlers that send over a network do; the Dropbox client reads the request back from the response
            var response = responder(request);
            response.RequestMessage ??= request;
            return response;
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body)
        => new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Dictionary<string, string?> Options(params (string Key, string Value)[] extra)
    {
        var options = new Dictionary<string, string?>
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl
        };
        foreach (var (k, v) in extra)
            options[k] = v;
        return options;
    }

    [Test]
    public void ThePathCheckRefusesTheBackendFolderAndAnythingOutsideIt()
    {
        foreach (var path in RefusedPaths)
            Assert.Throws<ArgumentException>(() => BackendExtensions.ValidateFolderDeletePath(path), $"\"{path}\"");
        Assert.Throws<ArgumentException>(() => BackendExtensions.ValidateFolderDeletePath(null));

        Assert.DoesNotThrow(() => BackendExtensions.ValidateFolderDeletePath("gone"));
        Assert.DoesNotThrow(() => BackendExtensions.ValidateFolderDeletePath("gone/in ner/.hidden"));
    }

    #region Box

    private const string BoxApi = "https://api.box.com/2.0/folders";

    /// <summary>
    /// Answers Box with the tree target/gone/inner, with the ids 100, 200 and 300
    /// </summary>
    private static StubHandler BoxStub()
        => new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (request.Method == HttpMethod.Delete)
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            static string Folder(string id, string name) => $"{{\"total_count\":1,\"entries\":[{{\"type\":\"folder\",\"id\":\"{id}\",\"name\":\"{name}\"}}]}}";
            if (url.StartsWith($"{BoxApi}/0/items", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, Folder("100", "target"));
            if (url.StartsWith($"{BoxApi}/100/items", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, Folder("200", "gone"));
            if (url.StartsWith($"{BoxApi}/200/items", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, Folder("300", "inner"));

            return Json(HttpStatusCode.OK, "{\"total_count\":0,\"entries\":[]}");
        });

    [TestCase("gone", "200")]
    [TestCase("gone/inner", "300")]
    public async Task BoxDeletesTheFolderRecursivelyAsync(string path, string folderId)
    {
        var handler = BoxStub();
        using var backend = new BoxBackend("box://target", Options(), new HttpClient(handler));

        await backend.DeleteFolderRecursiveAsync(path, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { $"DELETE {BoxApi}/{folderId}?recursive=true" },
            handler.Changes.Select(x => $"{x.Method} {x.Url}").ToArray());
    }

    [Test]
    public async Task BoxEmptiesTheFolderFromTheTrashWhenAskedToAsync()
    {
        var handler = BoxStub();
        using var backend = new BoxBackend("box://target", Options(("box-delete-from-trash", "true")), new HttpClient(handler));

        await backend.DeleteFolderRecursiveAsync("gone", CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { $"DELETE {BoxApi}/200?recursive=true", $"DELETE {BoxApi}/200/trash" },
            handler.Changes.Select(x => $"{x.Method} {x.Url}").ToArray());
    }

    [Test]
    public async Task BoxTreatsAMissingFolderAsDeletedAsync()
    {
        var handler = BoxStub();
        using var backend = new BoxBackend("box://target", Options(), new HttpClient(handler));

        await backend.DeleteFolderRecursiveAsync("never-there", CancellationToken.None);

        Assert.IsEmpty(handler.Changes);
    }

    [Test]
    public void BoxRefusesAPathOutsideTheBackendFolder()
    {
        var handler = BoxStub();
        using var backend = new BoxBackend("box://target", Options(), new HttpClient(handler));

        foreach (var path in RefusedPaths)
            Assert.ThrowsAsync<ArgumentException>(() => backend.DeleteFolderRecursiveAsync(path, CancellationToken.None), $"\"{path}\"");
        Assert.IsEmpty(handler.Requests);
    }

    #endregion

    #region Dropbox

    private const string DropboxDeleteUrl = "https://api.dropboxapi.com/2/files/delete_v2";

    private static Dropbox DropboxBackend(StubHandler handler)
        // No colon in the authid, so the token is used directly and no OAuth call is made
        => new Dropbox("dropbox://Backup", Options(), new HttpClient(handler));

    [TestCase("gone", "/Backup/gone")]
    [TestCase("gone/inner", "/Backup/gone/inner")]
    public async Task DropboxDeletesTheFolderByPathAsync(string path, string expected)
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));
        using var backend = DropboxBackend(handler);

        await backend.DeleteFolderRecursiveAsync(path, CancellationToken.None);

        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        Assert.AreEqual(DropboxDeleteUrl, handler.Requests[0].Url);
        Assert.AreEqual(expected, JsonNode.Parse(handler.Requests[0].Body!)?["path"]?.GetValue<string>());
    }

    [Test]
    public async Task DropboxTreatsAMissingFolderAsDeletedAsync()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Conflict,
            "{\"error_summary\":\"path_lookup/not_found/..\",\"error\":{\".tag\":\"path_lookup\",\"path_lookup\":{\".tag\":\"not_found\"}}}"));
        using var backend = DropboxBackend(handler);

        await backend.DeleteFolderRecursiveAsync("never-there", CancellationToken.None);

        Assert.AreEqual(1, handler.Requests.Count);
    }

    [Test]
    public void DropboxReportsAFolderItCouldNotDelete()
    {
        // Dropbox answers every refused delete with a conflict; only a missing path means the folder is gone
        var handler = new StubHandler(_ => Json(HttpStatusCode.Conflict,
            "{\"error_summary\":\"too_many_write_operations/..\",\"error\":{\".tag\":\"too_many_write_operations\"}}"));
        using var backend = DropboxBackend(handler);

        var ex = NUnit.Framework.Assert.CatchAsync(() => backend.DeleteFolderRecursiveAsync("gone", CancellationToken.None));
        Assert.IsInstanceOf<DropboxException>(ex, "A refused delete was reported as a missing file or folder.");
        StringAssert.Contains("too_many_write_operations", ex!.Message);
    }

    [Test]
    public void DropboxRefusesAPathOutsideTheBackendFolder()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));
        using var backend = DropboxBackend(handler);

        foreach (var path in RefusedPaths)
            Assert.ThrowsAsync<ArgumentException>(() => backend.DeleteFolderRecursiveAsync(path, CancellationToken.None), $"\"{path}\"");
        Assert.IsEmpty(handler.Requests);
    }

    #endregion

    #region Google Drive

    /// <summary>
    /// Answers the Drive API with the tree target/gone/inner, with the ids
    /// folder-target, folder-gone and folder-inner
    /// </summary>
    private static StubHandler GoogleDriveStub()
        => new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("/drive/v2/about", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "{\"rootFolderId\":\"root\"}");

            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/drive/v2/files")
            {
                // The query is form encoded, so a space arrives as "+"
                var query = Uri.UnescapeDataString(request.RequestUri.Query).Replace('+', ' ');
                foreach (var (name, parent) in new[] { ("target", "root"), ("gone", "folder-target"), ("inner", "folder-gone") })
                    if (query.Contains($"title = '{name}'", StringComparison.Ordinal) && query.Contains($"'{parent}' in parents", StringComparison.Ordinal))
                        return Json(HttpStatusCode.OK, $"{{\"items\":[{{\"id\":\"folder-{name}\",\"title\":\"{name}\",\"mimeType\":\"application/vnd.google-apps.folder\"}}]}}");

                return Json(HttpStatusCode.OK, "{\"items\":[]}");
            }

            return Json(HttpStatusCode.OK, "{}");
        });

    [TestCase("gone", "folder-gone")]
    [TestCase("gone/inner", "folder-inner")]
    public async Task GoogleDriveDeletesTheFolderItemAsync(string path, string folderId)
    {
        var handler = GoogleDriveStub();
        using var backend = new GoogleDrive("googledrive://target", Options(), new HttpClient(handler));

        await backend.DeleteFolderRecursiveAsync(path, CancellationToken.None);

        var change = handler.Changes.Single();
        Assert.AreEqual(HttpMethod.Delete, change.Method);
        Assert.AreEqual($"/drive/v2/files/{folderId}", new Uri(change.Url).AbsolutePath);
    }

    [Test]
    public async Task GoogleDriveMovesTheFolderToTheTrashWhenAskedToAsync()
    {
        var handler = GoogleDriveStub();
        using var backend = new GoogleDrive("googledrive://target", Options(("googledrive-use-trash", "true")), new HttpClient(handler));

        await backend.DeleteFolderRecursiveAsync("gone", CancellationToken.None);

        var change = handler.Changes.Single();
        Assert.AreEqual(HttpMethod.Post, change.Method);
        Assert.AreEqual("/drive/v2/files/folder-gone/trash", new Uri(change.Url).AbsolutePath);
    }

    [Test]
    public async Task GoogleDriveTreatsAMissingFolderAsDeletedAsync()
    {
        var handler = GoogleDriveStub();
        using var backend = new GoogleDrive("googledrive://target", Options(), new HttpClient(handler));

        await backend.DeleteFolderRecursiveAsync("never-there", CancellationToken.None);

        Assert.IsEmpty(handler.Changes);
    }

    [Test]
    public void GoogleDriveRefusesAPathOutsideTheBackendFolder()
    {
        var handler = GoogleDriveStub();
        using var backend = new GoogleDrive("googledrive://target", Options(), new HttpClient(handler));

        foreach (var path in RefusedPaths)
            Assert.ThrowsAsync<ArgumentException>(() => backend.DeleteFolderRecursiveAsync(path, CancellationToken.None), $"\"{path}\"");
        Assert.IsEmpty(handler.Requests);
    }

    #endregion

    #region Microsoft Graph

    private const string SiteId = "contoso-my.sharepoint.com,1111,2222";
    private const string GraphDriveRoot = $"https://graph.microsoft.com/v1.0/sites/{SiteId}/drive/root:";

    /// <summary>
    /// Answers the Graph requests for a personal site without a network or a token, and
    /// answers a delete with the given status
    /// </summary>
    private sealed class StubGraph(HttpStatusCode deleteStatus) : OAuthHttpMessageHandler(new OAuthHelperHttpClient("test-authid", "sharepoint", "http://127.0.0.1:1/", new HttpClient()))
    {
        public List<string> Deletes { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (request.Method == HttpMethod.Delete)
            {
                // As sent: ToString shows an escaped space as a space again
                Deletes.Add(request.RequestUri.AbsoluteUri);
                return Task.FromResult(deleteStatus == HttpStatusCode.NotFound
                    ? Json(deleteStatus, """{"error":{"code":"itemNotFound","message":"stub"}}""")
                    : new HttpResponseMessage(deleteStatus));
            }

            string? body = null;
            if (url.StartsWith("https://graph.microsoft.com/v1.0/sites/contoso-my.sharepoint.com:", StringComparison.Ordinal))
                body = $$"""{"id":"{{SiteId}}"}""";
            else if (url == $"https://graph.microsoft.com/v1.0/sites/{SiteId}/drive")
                body = """{"id":"drive","webUrl":"https://contoso-my.sharepoint.com/personal/someone_contoso_com/Documents"}""";

            return Task.FromResult(body == null
                ? Json(HttpStatusCode.NotFound, """{"error":{"code":"itemNotFound","message":"stub"}}""")
                : Json(HttpStatusCode.OK, body));
        }
    }

    private static SharePointV2 GraphBackend(StubGraph graph)
        => new SharePointV2(
            "sharepoint://contoso-my.sharepoint.com/personal/someone_contoso_com//Documents/Backup",
            new Dictionary<string, string?> { ["authid"] = "test-authid" },
            graph);

    [TestCase("gone", "/Backup/gone")]
    [TestCase("gone/in ner", "/Backup/gone/in%20ner")]
    public async Task GraphDeletesTheFolderItemAsync(string path, string expected)
    {
        var graph = new StubGraph(HttpStatusCode.NoContent);
        using var backend = GraphBackend(graph);

        await backend.DeleteFolderRecursiveAsync(path, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { GraphDriveRoot + expected }, graph.Deletes);
    }

    [Test]
    public async Task GraphTreatsAMissingFolderAsDeletedAsync()
    {
        var graph = new StubGraph(HttpStatusCode.NotFound);
        using var backend = GraphBackend(graph);

        await backend.DeleteFolderRecursiveAsync("never-there", CancellationToken.None);

        Assert.AreEqual(1, graph.Deletes.Count);
    }

    [Test]
    public void GraphReportsAFolderItCouldNotDelete()
    {
        var graph = new StubGraph(HttpStatusCode.Forbidden);
        using var backend = GraphBackend(graph);

        Assert.CatchAsync(() => backend.DeleteFolderRecursiveAsync("gone", CancellationToken.None));
    }

    [Test]
    public void GraphRefusesAPathOutsideTheBackendFolder()
    {
        var graph = new StubGraph(HttpStatusCode.NoContent);
        using var backend = GraphBackend(graph);

        foreach (var path in RefusedPaths)
            Assert.ThrowsAsync<ArgumentException>(() => backend.DeleteFolderRecursiveAsync(path, CancellationToken.None), $"\"{path}\"");
        Assert.IsEmpty(graph.Deletes);
    }

    #endregion
}
