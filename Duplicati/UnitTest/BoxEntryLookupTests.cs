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
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Looking up a single entry is how a remote source or destination is browsed.
/// The Box lookup passed its own configured path to the path builder instead of
/// the requested one, so it either found nothing or answered with the root.
/// These tests drive the backend against stubbed Box API responses, so no
/// network or credentials are needed.
/// </summary>
[TestFixture]
public class BoxEntryLookupTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails
    /// instead of reaching the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";
    private const string ItemsUrlPrefix = "https://api.box.com/2.0/folders/";

    /// <summary>
    /// Answers folder listings from a fixed layout of folder id to items
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string[]> _folders;

        public StubHandler(Dictionary<string, string[]> folders)
            => _folders = folders;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Task.FromResult(Json("{\"access_token\":\"test-token\",\"expires\":3600}"));

            if (url.StartsWith(ItemsUrlPrefix, StringComparison.Ordinal))
            {
                var id = url[ItemsUrlPrefix.Length..].Split('/')[0];
                var entries = _folders.TryGetValue(id, out var e) ? e : [];
                return Task.FromResult(Json($"{{\"total_count\":{entries.Length},\"entries\":[{string.Join(",", entries)}]}}"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body)
            => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static string Folder(string id, string name)
        => $"{{\"type\":\"folder\",\"id\":\"{id}\",\"name\":\"{name}\"}}";

    private static string File(string id, string name, long size)
        => $"{{\"type\":\"file\",\"id\":\"{id}\",\"name\":\"{name}\",\"size\":{size}}}";

    private static BoxBackend CreateBackend(Dictionary<string, string[]> folders, string url)
        => new BoxBackend(url, new Dictionary<string, string?>
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl
        }, new HttpClient(new StubHandler(folders)));

    /// <summary>
    /// The backend is configured for "target", which holds a folder and a file
    /// </summary>
    private static BoxBackend CreateBackendWithTargetFolder()
        => CreateBackend(new Dictionary<string, string[]>
        {
            ["0"] = [Folder("100", "target")],
            ["100"] = [Folder("200", "child"), File("300", "note.txt", 7)],
            ["200"] = []
        }, "box://target");

    [Test]
    [Category("Backend")]
    public async Task RequestedFolderIsReturned()
    {
        using var backend = CreateBackendWithTargetFolder();

        var entry = await backend.GetEntryAsync("child", CancellationToken.None);

        Assert.IsNotNull(entry, "The requested folder should be found");
        Assert.IsTrue(entry!.IsFolder);
        Assert.AreEqual("child/", entry.Name, "A folder name ends with a separator");
    }

    [Test]
    [Category("Backend")]
    public async Task RequestedFileIsReturned()
    {
        using var backend = CreateBackendWithTargetFolder();

        var entry = await backend.GetEntryAsync("note.txt", CancellationToken.None);

        Assert.IsNotNull(entry, "The requested file should be found");
        Assert.IsFalse(entry!.IsFolder);
        Assert.AreEqual("note.txt", entry.Name);
    }

    [Test]
    [Category("Backend")]
    public async Task ConfiguredFolderIsReturnedForAnEmptyPath()
    {
        // An empty path means the folder the backend is configured for, which is
        // what the browsing endpoint asks for first
        using var backend = CreateBackendWithTargetFolder();

        var entry = await backend.GetEntryAsync("", CancellationToken.None);

        Assert.IsNotNull(entry, "The configured folder should be found");
        Assert.IsTrue(entry!.IsFolder);
    }

    [Test]
    [Category("Backend")]
    public async Task MissingEntryIsReportedAsMissing()
    {
        using var backend = CreateBackendWithTargetFolder();

        var entry = await backend.GetEntryAsync("nope", CancellationToken.None);

        Assert.IsNull(entry, "An entry that does not exist should be reported as missing, not as an error");
    }

    [Test]
    [Category("Backend")]
    public async Task RequestedFolderIsReturnedForABackendAtTheRoot()
    {
        // Without a folder in the url the requested path was dropped entirely
        // and the root was answered for every request
        using var backend = CreateBackend(new Dictionary<string, string[]>
        {
            ["0"] = [Folder("200", "child")],
            ["200"] = []
        }, "box://");

        var entry = await backend.GetEntryAsync("child", CancellationToken.None);

        Assert.IsNotNull(entry, "The requested folder should be found");
        Assert.AreEqual("child/", entry!.Name, "The requested folder should be answered, not the root");
    }
}
