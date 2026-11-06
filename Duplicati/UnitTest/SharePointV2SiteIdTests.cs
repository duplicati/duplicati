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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library;
using Duplicati.Library.Backend;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Without --site-id, the SharePoint v2 backend finds the site from the "//" in the destination url
/// </summary>
[TestFixture]
public class SharePointV2SiteIdTests
{
    private const string SiteId = "contoso-my.sharepoint.com,1111,2222";

    /// <summary>
    /// Answers the Graph requests the backend makes for a personal site, without a network or a token
    /// </summary>
    private sealed class StubGraph : OAuthHttpMessageHandler
    {
        public ConcurrentQueue<string> Requests { get; } = new();

        // A client of its own: HttpClientHelper may still hold the factory of a server that an earlier test stopped
        public StubGraph()
            : base(new OAuthHelperHttpClient("test-authid", "sharepoint", "http://127.0.0.1:1/", new HttpClient()))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Enqueue(url);

            string? body = null;
            if (url.StartsWith("https://graph.microsoft.com/v1.0/sites/contoso-my.sharepoint.com:", StringComparison.Ordinal))
                body = $$"""{"id":"{{SiteId}}"}""";
            else if (url == $"https://graph.microsoft.com/v1.0/sites/{SiteId}/drive")
                body = """{"id":"drive","webUrl":"https://contoso-my.sharepoint.com/personal/someone_contoso_com/Documents"}""";
            else if (url.StartsWith($"https://graph.microsoft.com/v1.0/sites/{SiteId}/drive/root:", StringComparison.Ordinal))
                body = """{"value":[]}""";

            return Task.FromResult(body == null
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"error":{"code":"itemNotFound","message":"stub"}}""", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    [Test]
    public async Task TheSiteIsFoundFromTheUrlWithoutASiteIdOptionAsync()
    {
        var graph = new StubGraph();
        using var backend = new SharePointV2(
            "sharepoint://contoso-my.sharepoint.com/personal/someone_contoso_com//Documents/Vhds",
            new Dictionary<string, string?> { ["authid"] = "test-authid" },
            graph);

        var entries = await backend.ListAsync(CancellationToken.None).ToListAsync();

        Assert.AreEqual(0, entries.Count);
        Assert.AreEqual(
            $"https://graph.microsoft.com/v1.0/sites/{SiteId}/drive/root:/Vhds:/children",
            graph.Requests.Last(),
            string.Join(Environment.NewLine, graph.Requests));
    }

    [Test]
    public void AUrlThatNamesNoSiteStillNeedsTheSiteIdOption()
    {
        var graph = new StubGraph();
        using var backend = new SharePointV2(
            "sharepoint://elsewhere.example.com/Documents/Vhds",
            new Dictionary<string, string?> { ["authid"] = "test-authid" },
            graph);

        var ex = Assert.ThrowsAsync<Duplicati.Library.Interface.UserInformationException>(
            async () => await backend.ListAsync(CancellationToken.None).ToListAsync());
        Assert.AreEqual("SharePointMissingSiteId", ex!.HelpID);
    }

    [Test]
    public void ASiteIdOptionThatDiffersFromTheUrlIsAConflict()
    {
        var graph = new StubGraph();
        using var backend = new SharePointV2(
            "sharepoint://contoso-my.sharepoint.com/personal/someone_contoso_com//Documents/Vhds",
            new Dictionary<string, string?> { ["authid"] = "test-authid", ["site-id"] = "another-site" },
            graph);

        var ex = Assert.ThrowsAsync<Duplicati.Library.Interface.UserInformationException>(
            async () => await backend.ListAsync(CancellationToken.None).ToListAsync());
        Assert.AreEqual("SharePointConflictingSiteId", ex!.HelpID);
        Assert.IsFalse(graph.Requests.Any(x => x.Contains("/sites/another-site/", StringComparison.Ordinal)),
            string.Join(Environment.NewLine, graph.Requests));
    }

    /// <summary>
    /// BackendLoader creates the backend from the url and the options alone
    /// </summary>
    /// <remarks>
    /// Without an authid the constructor stops before it creates the http client, which would come
    /// from the factory that HttpClientHelper may still hold from a server an earlier test stopped.
    /// A constructor the loader cannot call fails with MissingMethodException instead.
    /// </remarks>
    [Test]
    public void TheLoaderCanStillCallTheConstructor()
    {
        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => Activator.CreateInstance(
            typeof(SharePointV2),
            "sharepoint://contoso-my.sharepoint.com/personal/someone_contoso_com//Documents/Vhds",
            new Dictionary<string, string?> { ["site-id"] = SiteId }));
        Assert.AreEqual("MissingAuthID", (ex!.InnerException as Duplicati.Library.Interface.UserInformationException)?.HelpID);
    }
}
