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
using System.IO;
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

/// <summary>
/// Before an upload, the Drime backend lists the target folder to find out whether the
/// name is taken, as Drime does not overwrite. Other uploads of the same backup run at
/// the same time and add files to that folder while it is being listed (#7474).
/// </summary>
[TestFixture]
public class DrimeUploadDuringListingTests
{
    private const long FolderId = 10;
    private const string FolderHash = "folder-hash";
    private static readonly DateTimeOffset BaseDate = new(2026, 10, 12, 13, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A folder with three files, where two more files are uploaded by other uploads after
    /// the folder count was read and before the listing pages are read.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly List<(long Id, string Name, DateTimeOffset Created)> _entries =
        [
            (1, "file-1", BaseDate),
            (2, "file-2", BaseDate.AddSeconds(1)),
            (3, "file-3", BaseDate.AddSeconds(2)),
            (4, "added-by-another-upload-1", BaseDate.AddSeconds(10)),
            (5, "added-by-another-upload-2", BaseDate.AddSeconds(11))
        ];

        public int CountRequests { get; private set; }
        public List<string> DeleteBodies { get; } = new();
        public List<string> Uploads { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith($"/folders/{FolderId}/count", StringComparison.Ordinal))
            {
                // The first count is read before the other uploads finish, later ones after
                CountRequests++;
                return Json(CountRequests == 1 ? "{\"count\":3}" : "{\"count\":5}");
            }

            if (path.EndsWith("/file-entries/delete", StringComparison.Ordinal))
            {
                DeleteBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                return Json("{\"status\":\"success\"}");
            }

            if (path.EndsWith("/uploads", StringComparison.Ordinal))
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var name = _entries.Select(e => e.Name).Append("new-volume").FirstOrDefault(n => body.Contains($"filename={n}", StringComparison.Ordinal)
                    || body.Contains($"filename=\"{n}\"", StringComparison.Ordinal)) ?? "unknown";
                Uploads.Add(name);
                return Json($"{{\"status\":\"success\",\"fileEntry\":{{\"id\":100,\"name\":\"{name}\",\"type\":\"file\",\"hash\":\"h100\",\"file_size\":4}}}}");
            }

            if (!path.EndsWith("/drive/file-entries", StringComparison.Ordinal))
                return Json("{\"message\":\"unexpected request\"}", HttpStatusCode.NotFound);

            if (request.RequestUri.Query.Contains("type=folder", StringComparison.Ordinal))
                return Json(
                    $"{{\"data\":[{{\"id\":{FolderId},\"name\":\"backup\",\"type\":\"folder\",\"hash\":\"{FolderHash}\",\"file_size\":0}}]," +
                    "\"current_page\":1,\"last_page\":1,\"per_page\":1,\"total\":1}");

            var data = _entries.Select(entry => new
            {
                id = entry.Id,
                name = entry.Name,
                type = "file",
                hash = $"hash-{entry.Id}",
                file_size = entry.Id,
                parent_id = FolderId,
                created_at = FormatDate(entry.Created),
                updated_at = FormatDate(entry.Created),
                deleted_at = (string?)null
            });
            return Json(JsonSerializer.Serialize(new
            {
                data,
                current_page = 1,
                last_page = 1,
                per_page = 100,
                total = _entries.Count
            }));
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static string FormatDate(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    private static DrimeBackend CreateBackend(StubHandler handler)
        => new("drimecloud://backup",
            new Dictionary<string, string?> { ["api-token"] = "test-token", ["page-size"] = "100" },
            handler);

    private static MemoryStream SmallFile()
        => new(Encoding.UTF8.GetBytes("data"));

    [Test]
    [Category("Backend")]
    public async Task UploadSucceedsWhenOtherUploadsAddFilesDuringTheCheck()
    {
        using var handler = new StubHandler();
        using var backend = CreateBackend(handler);

        await backend.PutAsync("new-volume", SmallFile(), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "new-volume" }, handler.Uploads);
        Assert.IsEmpty(handler.DeleteBodies, "Nothing was named new-volume, so nothing should be deleted");
    }

    [Test]
    [Category("Backend")]
    public async Task UploadStillDeletesAFileWithTheSameNameFirst()
    {
        using var handler = new StubHandler();
        using var backend = CreateBackend(handler);

        await backend.PutAsync("file-2", SmallFile(), CancellationToken.None);

        Assert.AreEqual(1, handler.DeleteBodies.Count);
        using var body = JsonDocument.Parse(handler.DeleteBodies[0]);
        CollectionAssert.AreEqual(new[] { 2L }, body.RootElement.GetProperty("entryIds").EnumerateArray().Select(e => e.GetInt64()));
        CollectionAssert.AreEqual(new[] { "file-2" }, handler.Uploads);
    }

    [Test]
    [Category("Backend")]
    public async Task ListingStillRejectsFilesAddedWhileListing()
    {
        using var handler = new StubHandler();
        using var backend = CreateBackend(handler);

        Exception? caught = null;
        try
        {
            await foreach (var _ in backend.ListAsync(CancellationToken.None))
            {
            }
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.IsNotNull(caught, "The listing that Duplicati verifies must stay strict");
        StringAssert.Contains("more unique active IDs than the separate folder count", caught!.Message);
    }
}
