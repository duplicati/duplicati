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
using Duplicati.Library.Backend.Box;
using Duplicati.Library.Interface;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Tests the file operations of the Box backend on names that contain a path, which is
/// how a sync addresses a file below the backend folder. Box knows a file by its id and
/// the folder it is in, so the path has to be turned into a folder and a file name, and
/// a file in one folder must not be taken for a file of the same name in another.
/// </summary>
[TestFixture]
[Category("Backend")]
public class BoxSubFolderFileTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails
    /// instead of reaching the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";
    private const string ItemsUrlPrefix = "https://api.box.com/2.0/folders/";
    private const string FilesUrlPrefix = "https://api.box.com/2.0/files/";
    private const string UploadUrlPrefix = "https://upload.box.com/api/2.0/files/";

    /// <summary>A request the backend sent, other than a listing</summary>
    private sealed record Change(HttpMethod Method, string Url, string Body);

    /// <summary>
    /// Answers folder listings from a fixed layout of folder id to items, accepts uploads,
    /// renames and deletes, and records them
    /// </summary>
    private sealed class StubHandler(Dictionary<string, string[]> folders) : HttpMessageHandler
    {
        public List<Change> Changes { get; } = [];

        /// <summary>The ids of the folders that were listed, in order</summary>
        public List<string> Listings { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Json("{\"access_token\":\"test-token\",\"expires\":3600}");

            if (request.Method == HttpMethod.Get && url.StartsWith(ItemsUrlPrefix, StringComparison.Ordinal))
            {
                var id = url[ItemsUrlPrefix.Length..].Split('/')[0];
                Listings.Add(id);
                var entries = folders.TryGetValue(id, out var e) ? e : [];
                return Json($"{{\"total_count\":{entries.Length},\"entries\":[{string.Join(",", entries)}]}}");
            }

            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Changes.Add(new Change(request.Method, url, body));

            if (url.StartsWith(UploadUrlPrefix, StringComparison.Ordinal))
            {
                // A new version keeps the id of the file; a new file gets a new one
                var target = url[UploadUrlPrefix.Length..].Split('/')[0];
                return Json($"{{\"total_count\":1,\"entries\":[{File(target == "content" ? "new-file" : target, "uploaded", 1)}]}}");
            }

            if (request.Method == HttpMethod.Put && url.StartsWith(FilesUrlPrefix, StringComparison.Ordinal))
                return Json(File(url[FilesUrlPrefix.Length..], "renamed", 1));

            if (request.Method == HttpMethod.Delete)
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body)
            => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static string Folder(string id, string name)
        => $"{{\"type\":\"folder\",\"id\":\"{id}\",\"name\":\"{name}\"}}";

    private static string File(string id, string name, long size)
        => $"{{\"type\":\"file\",\"id\":\"{id}\",\"name\":\"{name}\",\"size\":{size}}}";

    /// <summary>
    /// The backend is configured for "target" (id 100), which holds:
    ///   root.txt (300), same.txt (310)
    ///   sub/ (200): old.txt (500), same.txt (510)
    ///   sub/inner/ (400): empty
    ///   trash/ (600): empty
    /// </summary>
    private static (BoxBackend Backend, StubHandler Handler) Create()
    {
        var handler = new StubHandler(new Dictionary<string, string[]>
        {
            ["0"] = [Folder("100", "target")],
            ["100"] = [Folder("200", "sub"), Folder("600", "trash"), File("300", "root.txt", 7), File("310", "same.txt", 7)],
            ["200"] = [Folder("400", "inner"), File("500", "old.txt", 7), File("510", "same.txt", 7)],
            ["400"] = [],
            ["600"] = []
        });

        return (new BoxBackend("box://target", new Dictionary<string, string?>
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl
        }, new HttpClient(handler)), handler);
    }

    private static Task PutAsync(BoxBackend backend, string remotename)
        => backend.PutAsync(remotename, new MemoryStream(Encoding.UTF8.GetBytes("content")), CancellationToken.None);

    private static async Task ListAllAsync(BoxBackend backend, string? path)
    {
        await foreach (var _ in backend.ListAsync(path, CancellationToken.None).ConfigureAwait(false)) { }
    }

    [Test]
    public async Task AnUploadIntoASubFolderUsesTheFolderAndTheFileNameAsync()
    {
        var (backend, handler) = Create();
        using var _ = backend;

        await PutAsync(backend, "sub/inner/new.txt");

        var upload = handler.Changes.Single();
        Assert.AreEqual($"{UploadUrlPrefix}content", upload.Url, "A file the folder does not have is a new file");
        StringAssert.Contains("{\"name\":\"new.txt\",\"parent\":{\"id\":\"400\"}}", upload.Body);
        StringAssert.DoesNotContain("sub/inner", upload.Body);
    }

    [Test]
    public async Task AnUploadOverAFileInASubFolderIsANewVersionOfThatFileAsync()
    {
        var (backend, handler) = Create();
        using var _ = backend;

        await PutAsync(backend, "sub/old.txt");

        Assert.AreEqual($"{UploadUrlPrefix}500/content", handler.Changes.Single().Url);
    }

    [Test]
    public async Task AnUploadOverAFileNamedInAnotherCaseIsANewVersionOfThatFileAsync()
    {
        // Box does not allow two names in a folder that differ only in case, so a new
        // file of that name would be refused as a name in use
        var (backend, handler) = Create();
        using var _ = backend;

        await PutAsync(backend, "SUB/Old.TXT");

        Assert.AreEqual($"{UploadUrlPrefix}500/content", handler.Changes.Single().Url);
    }

    [Test]
    public async Task ADeleteFindsAFileNamedInAnotherCaseAsync()
    {
        var (backend, handler) = Create();
        using var _ = backend;

        await backend.DeleteAsync("sub/OLD.txt", CancellationToken.None);

        var delete = handler.Changes.Single();
        Assert.AreEqual(HttpMethod.Delete, delete.Method);
        StringAssert.StartsWith($"{FilesUrlPrefix}500", delete.Url);
    }

    [Test]
    public void AnUploadIntoAMissingFolderFailsAsAMissingFolder()
    {
        // Creating the folder is the caller's to ask for, as for the backend folder itself
        var (backend, handler) = Create();
        using var _ = backend;

        Assert.ThrowsAsync<FolderMissingException>(() => PutAsync(backend, "nope/new.txt"));
        Assert.IsEmpty(handler.Changes);
    }

    [Test]
    public async Task UploadsIntoOneFolderFindTheFolderOnceAsync()
    {
        var (backend, handler) = Create();
        using var _ = backend;

        await PutAsync(backend, "sub/inner/one.txt");
        var listingsAfterFirst = handler.Listings.Count;
        await PutAsync(backend, "sub/inner/two.txt");
        await PutAsync(backend, "sub/inner/one.txt");

        Assert.AreEqual(listingsAfterFirst, handler.Listings.Count, "The folder was looked up again: " + string.Join(", ", handler.Listings));
        CollectionAssert.AreEqual(
            new[] { $"{UploadUrlPrefix}content", $"{UploadUrlPrefix}content", $"{UploadUrlPrefix}new-file/content" },
            handler.Changes.Select(x => x.Url).ToArray(),
            "The file uploaded first was not known when it was uploaded again");
    }

    [Test]
    public async Task AFileIsNotTakenForOneOfTheSameNameInTheFolderListedLastAsync()
    {
        var (backend, handler) = Create();
        using var _ = backend;

        // The sub-folder is listed, and then a file in the backend folder is uploaded
        await ListAllAsync(backend, "sub");
        await PutAsync(backend, "same.txt");
        Assert.AreEqual($"{UploadUrlPrefix}310/content", handler.Changes.Last().Url, "The file of the other folder was overwritten");

        // A file the sub-folder does not have is still an existing file of the backend folder
        await ListAllAsync(backend, "sub");
        await PutAsync(backend, "root.txt");
        Assert.AreEqual($"{UploadUrlPrefix}300/content", handler.Changes.Last().Url, "An existing file was uploaded as a new file");

        // And the other way round
        await ListAllAsync(backend, null);
        await PutAsync(backend, "sub/same.txt");
        Assert.AreEqual($"{UploadUrlPrefix}510/content", handler.Changes.Last().Url, "The file of the other folder was overwritten");
    }

    [Test]
    public async Task ADeleteIsNotOfTheFileOfTheSameNameInTheFolderListedLastAsync()
    {
        var (backend, handler) = Create();
        using var _ = backend;

        await ListAllAsync(backend, "sub");
        await backend.DeleteAsync("same.txt", CancellationToken.None);
        Assert.AreEqual($"DELETE {FilesUrlPrefix}310", $"{handler.Changes.Last().Method} {handler.Changes.Last().Url}");

        await ListAllAsync(backend, null);
        await backend.DeleteAsync("sub/same.txt", CancellationToken.None);
        Assert.AreEqual($"DELETE {FilesUrlPrefix}510", $"{handler.Changes.Last().Method} {handler.Changes.Last().Url}");
    }

    [Test]
    public async Task ADeletedFileIsANewFileWhenItIsUploadedAgainAsync()
    {
        var (backend, handler) = Create();
        using var _ = backend;

        await backend.DeleteAsync("root.txt", CancellationToken.None);
        await PutAsync(backend, "root.txt");

        Assert.AreEqual($"{UploadUrlPrefix}content", handler.Changes.Last().Url, "The upload was a new version of the deleted file");
    }

    [Test]
    public async Task ARenameInASubFolderSendsTheFileNameAsync()
    {
        var (backend, handler) = Create();
        using var _ = backend;

        await backend.RenameAsync("sub/old.txt", "sub/new.txt", CancellationToken.None);

        var rename = handler.Changes.Single();
        Assert.AreEqual($"PUT {FilesUrlPrefix}500", $"{rename.Method} {rename.Url}");
        StringAssert.Contains("\"name\":\"new.txt\"", rename.Body);
        StringAssert.DoesNotContain("\"id\"", rename.Body, "A rename within a folder moved the file");
    }

    [Test]
    public async Task ARenameIntoAnotherFolderMovesTheFileAsync()
    {
        var (backend, handler) = Create();
        using var _ = backend;

        await backend.RenameAsync("sub/old.txt", "trash/kept.txt", CancellationToken.None);

        var rename = handler.Changes.Single();
        Assert.AreEqual($"PUT {FilesUrlPrefix}500", $"{rename.Method} {rename.Url}");
        StringAssert.Contains("\"name\":\"kept.txt\"", rename.Body);
        StringAssert.Contains("\"parent\":{\"id\":\"600\"}", rename.Body);
    }

    [Test]
    public async Task ARenameInTheBackendFolderIsUnchangedAsync()
    {
        var (backend, handler) = Create();
        using var _ = backend;

        await backend.RenameAsync("root.txt", "renamed.txt", CancellationToken.None);
        // The renamed file is known under its new name
        await PutAsync(backend, "renamed.txt");

        Assert.AreEqual($"PUT {FilesUrlPrefix}300", $"{handler.Changes[0].Method} {handler.Changes[0].Url}");
        StringAssert.Contains("\"name\":\"renamed.txt\"", handler.Changes[0].Body);
        Assert.AreEqual($"{UploadUrlPrefix}300/content", handler.Changes[1].Url);
    }
}
