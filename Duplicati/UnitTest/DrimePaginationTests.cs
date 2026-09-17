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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.DrimeCloud;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

#nullable enable

namespace Duplicati.UnitTest;

[TestFixture]
public class DrimePaginationTests
{
    private const long FolderId = 10;
    private const string FolderHash = "folder-hash";

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<int> UnfilteredPages { get; } = new();
        public List<int> FilteredPages { get; } = new();
        public List<string> DecodedFilters { get; } = new();
        public int CountRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith($"/folders/{FolderId}/count", StringComparison.Ordinal))
            {
                CountRequests++;
                return Task.FromResult(Json("{\"count\":21}"));
            }

            if (!path.EndsWith("/drive/file-entries", StringComparison.Ordinal))
                return Task.FromResult(Json("{\"message\":\"unexpected request\"}", HttpStatusCode.NotFound));

            var query = ParseQuery(request.RequestUri.Query);
            var page = int.Parse(query["page"]);

            if (query.TryGetValue("type", out var type) && type == "folder")
                return Task.FromResult(Json(
                    $"{{\"data\":[{{\"id\":{FolderId},\"name\":\"backup\",\"type\":\"folder\",\"hash\":\"{FolderHash}\",\"file_size\":0}}]," +
                    "\"current_page\":1,\"last_page\":1,\"per_page\":1,\"total\":1}"));

            var filtered = query.ContainsKey("filters");
            if (filtered)
                DecodedFilters.Add(DecodeFilters(query["filters"]));
            (filtered ? FilteredPages : UnfilteredPages).Add(page);
            var id = filtered ? 19 + page : page;
            var timestamp = $"2026-09-{id:00}T00:00:00.000000Z";
            return Task.FromResult(Json(
                $"{{\"data\":[{{\"id\":{id},\"name\":\"file-{id}\",\"type\":\"file\",\"hash\":\"hash-{id}\"," +
                $"\"file_size\":{id},\"parent_id\":{FolderId},\"created_at\":\"{timestamp}\",\"updated_at\":\"{timestamp}\"}}]," +
                $"\"current_page\":{page},\"last_page\":21,\"per_page\":1,\"total\":21}}"));
        }

        private static Dictionary<string, string> ParseQuery(string query)
            => query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .ToDictionary(part => WebUtility.UrlDecode(part[0]), part => WebUtility.UrlDecode(part[1]));

        /// <summary>
        /// The backend encodes filters as JSON, then Base64, then URI-escapes
        /// the result, and finally URI-escapes the whole query value again.
        /// ParseQuery has undone the outer escaping; undo the remaining layers
        /// the way the Drime server would.
        /// </summary>
        private static string DecodeFilters(string value)
            => Encoding.UTF8.GetString(Convert.FromBase64String(Uri.UnescapeDataString(value)));

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    [Test]
    [Category("Backend")]
    public async Task FolderListingRestartsWithTimestampWindowBeforeDrimePageLimit()
    {
        using var handler = new StubHandler();
        using var backend = new DrimeBackend(
            "drimecloud://backup",
            new Dictionary<string, string?> { ["api-token"] = "test-token", ["page-size"] = "1" },
            handler);

        var entries = new List<string>();
        await foreach (var entry in backend.ListAsync(CancellationToken.None))
            entries.Add(entry.Name);

        Assert.AreEqual(21, entries.Count);
        CollectionAssert.AreEquivalent(Enumerable.Range(1, 21).Select(id => $"file-{id}"), entries);
        CollectionAssert.AreEqual(Enumerable.Range(1, 20), handler.UnfilteredPages);
        CollectionAssert.AreEqual(new[] { 1, 2 }, handler.FilteredPages);
        Assert.AreEqual(2, handler.CountRequests);
    }

    [Test]
    [Category("Backend")]
    public async Task FolderListingSendsDecodableCreatedAtWindowFilter()
    {
        using var handler = new StubHandler();
        using var backend = new DrimeBackend(
            "drimecloud://backup",
            new Dictionary<string, string?> { ["api-token"] = "test-token", ["page-size"] = "1" },
            handler);

        var entries = new List<string>();
        await foreach (var entry in backend.ListAsync(CancellationToken.None))
            entries.Add(entry.Name);

        Assert.AreEqual(21, entries.Count);

        // The first window ends with the entry created 2026-09-20, so every
        // filtered request must carry that timestamp as the inclusive bound.
        Assert.AreEqual(2, handler.DecodedFilters.Count);
        foreach (var json in handler.DecodedFilters)
        {
            using var document = JsonDocument.Parse(json);
            var filter = document.RootElement.EnumerateArray().Single();
            Assert.AreEqual("created_at", filter.GetProperty("key").GetString());
            Assert.AreEqual(">=", filter.GetProperty("operator").GetString());
            Assert.AreEqual("2026-09-20T00:00:00.000000Z", filter.GetProperty("value").GetString());
        }
    }
}
