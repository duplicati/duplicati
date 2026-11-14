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
/// On a shared drive, a permanent delete needs the Manager role, so a user with
/// the Content manager role gets 403 on every delete. That role can still move a
/// file to the trash, so the googledrive-use-trash option sends the trash request
/// instead. Reported as issue #4896.
/// </summary>
[TestFixture]
public class GoogleDriveUseTrashTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails
    /// instead of reaching the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";

    private const string FileName = "duplicati-20230226T000000Z.dlist.zip.aes";

    /// <summary>A request the backend sent to the Drive API</summary>
    private sealed record SentRequest(HttpMethod Method, Uri Uri, string? Body);

    /// <summary>
    /// Answers the Drive API with one folder and one file, and records every
    /// request that is not a listing or the token exchange
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<SentRequest> Changes { get; } = new();
        public List<string> ListQueries { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Json("{\"access_token\":\"test-token\",\"expires\":3600}");

            if (url.Contains("/drive/v2/about", StringComparison.Ordinal))
                return Json("{\"rootFolderId\":\"root\"}");

            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/drive/v2/files")
            {
                // The query is form encoded, so a space arrives as "+"
                var query = Uri.UnescapeDataString(request.RequestUri.Query).Replace('+', ' ');
                ListQueries.Add(query);

                if (query.Contains("title = 'target'", StringComparison.Ordinal))
                    return Json("{\"items\":[{\"id\":\"folder-1\",\"title\":\"target\",\"mimeType\":\"application/vnd.google-apps.folder\"}]}");

                return Json($"{{\"items\":[{{\"id\":\"file-1\",\"title\":\"{FileName}\",\"mimeType\":\"application/octet-stream\",\"fileSize\":10}}]}}");
            }

            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Changes.Add(new SentRequest(request.Method, request.RequestUri!, body));
            return Json("{}");
        }

        private static HttpResponseMessage Json(string body)
            => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static (GoogleDrive Backend, StubHandler Handler) Create(params (string Key, string Value)[] extra)
    {
        var options = new Dictionary<string, string?>
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl
        };
        foreach (var (k, v) in extra)
            options[k] = v;

        var handler = new StubHandler();
        return (new GoogleDrive("googledrive://target", options, new HttpClient(handler)), handler);
    }

    private static async Task DeleteAfterListingAsync(GoogleDrive backend)
    {
        await foreach (var _ in backend.ListAsync(CancellationToken.None)) { }
        await backend.DeleteAsync(FileName, CancellationToken.None);
    }

    /// <summary>
    /// With the option on, the file is moved to the trash: a POST to
    /// files/{id}/trash with supportsAllDrives=true and no body, and no DELETE
    /// </summary>
    [TestCase(null)]
    [TestCase("shared-drive-1")]
    [Category("Backend")]
    public async Task WithTheOptionADeleteMovesTheFileToTheTrash(string? teamDriveId)
    {
        var extra = new List<(string, string)> { ("googledrive-use-trash", "true") };
        if (teamDriveId != null)
            extra.Add(("googledrive-teamdrive-id", teamDriveId));

        var (backend, handler) = Create(extra.ToArray());
        using var _b = backend;

        await DeleteAfterListingAsync(backend);

        Assert.AreEqual(1, handler.Changes.Count, string.Join(", ", handler.Changes.Select(x => $"{x.Method} {x.Uri}")));
        var sent = handler.Changes[0];
        Assert.AreEqual(HttpMethod.Post, sent.Method);
        Assert.AreEqual("https://www.googleapis.com/drive/v2/files/file-1/trash?supportsAllDrives=true", sent.Uri.ToString());
        Assert.That(sent.Body, Is.Null.Or.Empty);
    }

    /// <summary>
    /// Green before and after: without the option the file is still deleted for good
    /// </summary>
    [TestCase(null, "")]
    [TestCase("false", "")]
    [TestCase("false", "shared-drive-1")]
    [Category("Backend")]
    public async Task WithoutTheOptionADeleteIsPermanent(string? useTrash, string teamDriveId)
    {
        var extra = new List<(string, string)>();
        if (useTrash != null)
            extra.Add(("googledrive-use-trash", useTrash));
        if (teamDriveId != "")
            extra.Add(("googledrive-teamdrive-id", teamDriveId));

        var (backend, handler) = Create(extra.ToArray());
        using var _b = backend;

        await DeleteAfterListingAsync(backend);

        Assert.AreEqual(1, handler.Changes.Count, string.Join(", ", handler.Changes.Select(x => $"{x.Method} {x.Uri}")));
        var sent = handler.Changes[0];
        Assert.AreEqual(HttpMethod.Delete, sent.Method);
        Assert.AreEqual("/drive/v2/files/file-1", sent.Uri.AbsolutePath);
        Assert.That(sent.Uri.Query, Does.Not.Contain("trash"));
    }

    /// <summary>
    /// A trashed file stays in Drive, so the listing has to leave it out, or the
    /// deleted file would come back on the next verification. Every listing the
    /// backend makes, including the name lookups a delete and an upload use, asks
    /// Drive for untrashed items only.
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task EveryListingLeavesOutTrashedFiles()
    {
        var (backend, handler) = Create(("googledrive-use-trash", "true"));
        using var _b = backend;

        // Without a listing first, the delete looks the name up on its own
        await backend.DeleteAsync(FileName, CancellationToken.None);
        await foreach (var _ in backend.ListAsync(CancellationToken.None)) { }

        Assert.That(handler.ListQueries, Is.Not.Empty);
        foreach (var q in handler.ListQueries)
            Assert.That(q, Does.Contain("trashed=false"), q);
    }

    /// <summary>
    /// The option is offered to the user, off by default
    /// </summary>
    [Test]
    [Category("Backend")]
    public void TheOptionIsListedAndOffByDefault()
    {
        var arg = new GoogleDrive().SupportedCommands.SingleOrDefault(x => x.Name == "googledrive-use-trash");
        Assert.IsNotNull(arg);
        Assert.AreEqual("false", arg!.DefaultValue);
    }
}
