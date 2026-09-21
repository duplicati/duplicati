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
using System.Globalization;
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
using StringAssert = NUnit.Framework.Legacy.StringAssert;

#nullable enable

namespace Duplicati.UnitTest;

[TestFixture]
public class DrimePaginationTests
{
    private const long FolderId = 10;
    private const string FolderHash = "folder-hash";
    private static readonly DateTimeOffset BaseDate = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed record StubEntry(long Id, string Name, DateTimeOffset CreatedAt, string? DeletedAt = null);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly IReadOnlyList<StubEntry> _entries;
        private readonly long _count;
        private readonly bool _trashBoundaryOnFilteredListing;

        public List<int> UnfilteredPages { get; } = new();
        public List<int> FilteredPages { get; } = new();
        public List<string> DecodedFilters { get; } = new();
        public int CountRequests { get; private set; }

        public StubHandler(
            IEnumerable<StubEntry>? entries = null,
            long? count = null,
            bool trashBoundaryOnFilteredListing = false)
        {
            _entries = (entries ?? CreateActiveEntries(21)).OrderBy(entry => entry.CreatedAt).ToList();
            _count = count ?? _entries.LongCount(entry => string.IsNullOrWhiteSpace(entry.DeletedAt));
            _trashBoundaryOnFilteredListing = trashBoundaryOnFilteredListing;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith($"/folders/{FolderId}/count", StringComparison.Ordinal))
            {
                CountRequests++;
                return Task.FromResult(Json($"{{\"count\":{_count}}}"));
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
            DateTimeOffset? lower = null;
            if (filtered)
            {
                var decoded = DecodeFilters(query["filters"]);
                DecodedFilters.Add(decoded);
                using var document = JsonDocument.Parse(decoded);
                lower = DateTimeOffset.Parse(
                    document.RootElement.EnumerateArray().Single().GetProperty("value").GetString()!,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            }

            (filtered ? FilteredPages : UnfilteredPages).Add(page);
            var perPage = int.Parse(query["perPage"], CultureInfo.InvariantCulture);
            var windowEntries = _entries
                .Where(entry => !lower.HasValue || entry.CreatedAt >= lower.Value)
                .Select(entry => filtered && _trashBoundaryOnFilteredListing && entry.Id == 20
                    ? entry with { DeletedAt = "2026-09-30T00:00:00.000000Z" }
                    : entry)
                .ToList();
            var pageEntries = windowEntries.Skip((page - 1) * perPage).Take(perPage).ToList();
            var lastPage = Math.Max(1, (windowEntries.Count + perPage - 1) / perPage);
            var data = pageEntries.Select(entry => new
            {
                id = entry.Id,
                name = entry.Name,
                type = "file",
                hash = $"hash-{entry.Id}",
                file_size = entry.Id,
                parent_id = FolderId,
                created_at = FormatDate(entry.CreatedAt),
                updated_at = FormatDate(entry.CreatedAt),
                deleted_at = entry.DeletedAt
            });
            return Task.FromResult(Json(JsonSerializer.Serialize(new
            {
                data,
                current_page = page,
                last_page = lastPage,
                per_page = perPage,
                total = windowEntries.Count
            })));
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

    private static List<StubEntry> CreateActiveEntries(int count)
        => Enumerable.Range(1, count)
            .Select(id => new StubEntry(id, $"file-{id}", BaseDate.AddDays(id - 1)))
            .ToList();

    private static string FormatDate(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

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

    [Test]
    [Category("Backend")]
    public async Task FolderListingSupportsActiveCountWhenListingIncludesTrash()
    {
        var sourceEntries = CreateActiveEntries(19);
        sourceEntries.Add(new StubEntry(9001, "trashed-file", BaseDate.AddDays(19), "2026-09-30T00:00:00.000000Z"));
        sourceEntries.Add(new StubEntry(20, "file-20", BaseDate.AddDays(20)));
        sourceEntries.Add(new StubEntry(21, "file-21", BaseDate.AddDays(21)));
        using var handler = new StubHandler(sourceEntries, count: 21);
        using var backend = new DrimeBackend(
            "drimecloud://backup",
            new Dictionary<string, string?> { ["api-token"] = "test-token", ["page-size"] = "1" },
            handler);

        var listed = new List<string>();
        await foreach (var entry in backend.ListAsync(CancellationToken.None))
            listed.Add(entry.Name);

        Assert.AreEqual(21, listed.Count);
        CollectionAssert.DoesNotContain(listed, "trashed-file");
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, handler.FilteredPages);
        Assert.AreEqual(2, handler.CountRequests);
    }

    [Test]
    [Category("Backend")]
    public async Task FolderListingRejectsCountIncludingTrashWhenListingExcludesIt()
    {
        using var handler = new StubHandler(count: 22);
        using var backend = new DrimeBackend(
            "drimecloud://backup",
            new Dictionary<string, string?> { ["api-token"] = "test-token", ["page-size"] = "1" },
            handler);

        var exception = await CaptureListingExceptionAsync(backend);

        StringAssert.Contains("Trash semantics cannot be reconciled safely", exception.Message);
        Assert.AreEqual(1, handler.CountRequests);
    }

    [Test]
    [Category("Backend")]
    public async Task FolderListingRejectsCountAndListingIncludingTrash()
    {
        var entries = CreateActiveEntries(19);
        entries.Add(new StubEntry(9001, "trashed-file", BaseDate.AddDays(19), "2026-09-30T00:00:00.000000Z"));
        entries.Add(new StubEntry(20, "file-20", BaseDate.AddDays(20)));
        entries.Add(new StubEntry(21, "file-21", BaseDate.AddDays(21)));
        using var handler = new StubHandler(entries, count: 22);
        using var backend = new DrimeBackend(
            "drimecloud://backup",
            new Dictionary<string, string?> { ["api-token"] = "test-token", ["page-size"] = "1" },
            handler);

        var exception = await CaptureListingExceptionAsync(backend);

        StringAssert.Contains("Trash semantics cannot be reconciled safely", exception.Message);
        Assert.AreEqual(1, handler.CountRequests);
    }

    [Test]
    [Category("Backend")]
    public async Task FolderListingRejectsSelfConsistentPrematureTerminalMetadata()
    {
        using var handler = new StubHandler(CreateActiveEntries(100), count: 5219);
        using var backend = new DrimeBackend(
            "drimecloud://backup",
            new Dictionary<string, string?> { ["api-token"] = "test-token", ["page-size"] = "100" },
            handler);

        var exception = await CaptureListingExceptionAsync(backend);

        StringAssert.Contains("Trash semantics cannot be reconciled safely", exception.Message);
        Assert.AreEqual(1, handler.CountRequests);
    }

    [Test]
    [Category("Backend")]
    public async Task FolderListingRejectsDeletionStateChangeForRepeatedId()
    {
        using var handler = new StubHandler(trashBoundaryOnFilteredListing: true);
        using var backend = new DrimeBackend(
            "drimecloud://backup",
            new Dictionary<string, string?> { ["api-token"] = "test-token", ["page-size"] = "1" },
            handler);

        var exception = await CaptureListingExceptionAsync(backend);

        StringAssert.Contains("Metadata changed for a repeated entry ID", exception.Message);
    }

    [Test]
    [Category("Backend")]
    public async Task FolderListingAllowsProgressThroughTrashBeyondActiveCountBudget()
    {
        var entries = Enumerable.Range(1, 1001)
            .Select(id => new StubEntry(10_000 + id, $"trashed-{id}", BaseDate.AddMinutes(id), "2026-12-31T00:00:00.000000Z"))
            .Append(new StubEntry(1, "active-file", BaseDate.AddMinutes(1002)))
            .ToList();
        using var handler = new StubHandler(entries, count: 1);
        using var backend = new DrimeBackend(
            "drimecloud://backup",
            new Dictionary<string, string?> { ["api-token"] = "test-token", ["page-size"] = "1" },
            handler);

        var listed = new List<string>();
        await foreach (var entry in backend.ListAsync(CancellationToken.None))
            listed.Add(entry.Name);

        CollectionAssert.AreEqual(new[] { "active-file" }, listed);
        Assert.Greater(handler.UnfilteredPages.Count + handler.FilteredPages.Count, 1000);
        Assert.AreEqual(2, handler.CountRequests);
    }

    private static async Task<Exception> CaptureListingExceptionAsync(DrimeBackend backend)
    {
        try
        {
            await foreach (var _ in backend.ListAsync(CancellationToken.None))
            {
            }
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new AssertionException("Expected the Drime listing to fail.");
    }
}
