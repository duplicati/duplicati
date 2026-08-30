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
using Duplicati.Library.Backend;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// A delete that reaches Filejump but whose answer does not reach us leaves the
/// entry id in the file cache, and the lookup hands that id back without asking
/// the server, so every retry deletes something that is already gone. This is the
/// same defect that was fixed for the sibling backend in
/// https://github.com/duplicati/duplicati/pull/7221 - the two speak the same API.
/// </summary>
[TestFixture]
public class FilejumpDeleteRetryTests
{
    /// <summary>
    /// An empty path puts the backend at the drive root, which needs no folder lookup
    /// </summary>
    private const string Url = "filejump://";

    /// <summary>
    /// The name the tests delete
    /// </summary>
    private const string RemoteName = "duplicati-b0123456789.dblock.zip.aes";

    /// <summary>
    /// A second name, used to tell a cache that lost one entry from a cache that
    /// was thrown away whole
    /// </summary>
    private const string OtherName = "duplicati-b9876543210.dblock.zip.aes";

    /// <summary>
    /// The id the listing reports for the first name it holds
    /// </summary>
    private const long EntryId = 4711;

    /// <summary>
    /// What the API answers when the entry ids do not resolve. The shape is the one
    /// the sibling backend was measured returning for the same endpoint.
    /// </summary>
    private const string EntryIdsInvalidBody =
        "{\"message\":\"The selected entry ids is invalid.\"," +
        "\"errors\":{\"entryIds\":[\"The selected entry ids is invalid.\"]}}";

    /// <summary>
    /// Serves a listing and a delete endpoint, and records what was asked of it
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        /// <summary>
        /// The names the listing reports, changed by the tests to model the server
        /// state after a delete that the client never saw the answer to
        /// </summary>
        public List<string> Listed { get; } = new() { RemoteName };

        /// <summary>
        /// Runs for each delete request, and decides what that request answers
        /// </summary>
        public Func<int, HttpResponseMessage>? DeleteResponse { get; set; }

        /// <summary>
        /// The number of listing requests received
        /// </summary>
        public int Listings { get; private set; }

        /// <summary>
        /// The body of every delete request received, in order
        /// </summary>
        public List<string> DeleteBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/file-entries/delete", StringComparison.Ordinal))
            {
                DeleteBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                return DeleteResponse?.Invoke(DeleteBodies.Count) ?? Json(HttpStatusCode.OK, "{\"status\":\"success\"}");
            }

            if (path.EndsWith("/drive/file-entries", StringComparison.Ordinal))
            {
                Listings++;
                var entries = Listed.Select((x, i) =>
                    $"{{\"id\":{EntryId + i},\"name\":\"{x}\",\"type\":\"file\",\"url\":\"files/{EntryId + i}\",\"file_size\":17}}");
                return Json(HttpStatusCode.OK,
                    $"{{\"data\":[{string.Join(",", entries)}],\"current_page\":1,\"per_page\":100,\"next_page\":null}}");
            }

            return Json(HttpStatusCode.NotFound, "{\"message\":\"unexpected request\"}");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static Filejump CreateBackend(StubHandler handler)
        => new(Url, new Dictionary<string, string?> { ["api-token"] = "test-token" }, handler);

    /// <summary>
    /// Models the retry the caller performs: the first delete throws, the file is
    /// gone on the server, and the second attempt has to find that out.
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task RetryAfterFailedDeleteRelistsInsteadOfResendingTheStaleId()
    {
        using var handler = new StubHandler
        {
            // The request reaches the server, the answer does not reach us
            DeleteResponse = attempt => attempt == 1 ? throw new TimeoutException("The operation has timed out.") : null!
        };
        using var backend = CreateBackend(handler);

        Assert.CatchAsync<TimeoutException>(async () =>
            await backend.DeleteAsync(RemoteName, CancellationToken.None));

        Assert.AreEqual(1, handler.DeleteBodies.Count);
        Assert.IsTrue(handler.DeleteBodies[0].Contains($"\"entryIds\":[{EntryId}]"), handler.DeleteBodies[0]);

        // The first attempt removed it, so the server no longer reports it
        handler.Listed.Clear();
        var listingsBeforeRetry = handler.Listings;

        await backend.DeleteAsync(RemoteName, CancellationToken.None);

        Assert.Greater(handler.Listings, listingsBeforeRetry, "the retry has to ask the server again");
        Assert.AreEqual(1, handler.DeleteBodies.Count, "the retry must not delete an entry id that is gone");
    }

    /// <summary>
    /// Deleting an entry that is already gone is reported as a validation failure on
    /// entryIds rather than as a 404, and BackendManager only runs its
    /// list-and-confirm recovery for a FileMissingException.
    /// </summary>
    [Test]
    [Category("Backend")]
    public void EntryIdValidationErrorIsReportedAsFileMissing()
    {
        using var handler = new StubHandler
        {
            DeleteResponse = _ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(EntryIdsInvalidBody, Encoding.UTF8, "application/json")
            }
        };
        using var backend = CreateBackend(handler);

        Assert.CatchAsync<FileMissingException>(async () =>
            await backend.DeleteAsync(RemoteName, CancellationToken.None));
    }

    /// <summary>
    /// A delete that the server answered has to leave the id behind, or the next
    /// operation on that name asks for something that is no longer there.
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task ASuccessfulDeleteForgetsThatName()
    {
        using var handler = new StubHandler();
        using var backend = CreateBackend(handler);

        await backend.DeleteAsync(RemoteName, CancellationToken.None);

        // The server no longer holds it, and the name must not be served from the cache
        handler.Listed.Clear();
        await backend.DeleteAsync(RemoteName, CancellationToken.None);

        Assert.AreEqual(1, handler.DeleteBodies.Count, "the id must not be sent a second time");
    }

    /// <summary>
    /// Throwing the cache away is for the failure path only, so a delete that the
    /// server answered has to leave the other names in place
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task SuccessfulDeleteKeepsTheRestOfTheCache()
    {
        using var handler = new StubHandler();
        handler.Listed.Add(OtherName);
        using var backend = CreateBackend(handler);

        await backend.DeleteAsync(RemoteName, CancellationToken.None);
        var listings = handler.Listings;

        await backend.DeleteAsync(OtherName, CancellationToken.None);

        Assert.AreEqual(listings, handler.Listings, "the second name was still cached");
        Assert.AreEqual(2, handler.DeleteBodies.Count);
        Assert.IsTrue(handler.DeleteBodies[1].Contains($"\"entryIds\":[{EntryId + 1}]"), handler.DeleteBodies[1]);
    }
}
