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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.Filen;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The file cache is only tidied once the request has come back. A delete or a
/// rename that reaches the server but whose answer does not reach us therefore
/// leaves the old entry in place, and the lookup answers from it without asking
/// the server again. The sibling backends had the same shape - see
/// https://github.com/duplicati/duplicati/pull/7221.
/// </summary>
[TestFixture]
public class FilenCacheInvalidationTests
{
    private const string BaseUrl = "https://gateway.invalid";
    private const string FolderUuid = "11111111-1111-1111-1111-111111111111";
    private const string FileUuid = "22222222-2222-2222-2222-222222222222";
    private const string RemoteName = "duplicati-b0123456789.dblock.zip.aes";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Serves the folder listing and the delete and rename endpoints, and counts
    /// how often the folder was read
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly DerivedKey _key;

        public StubHandler(DerivedKey key)
            => _key = key;

        /// <summary>
        /// The number of folder listings served
        /// </summary>
        public int Listings { get; private set; }

        /// <summary>
        /// The number of delete or rename requests received
        /// </summary>
        public int Writes { get; private set; }

        /// <summary>
        /// Runs for each delete or rename, and decides what it answers
        /// </summary>
        public Func<int, HttpResponseMessage>? WriteResponse { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/dir/content", StringComparison.Ordinal))
            {
                Listings++;

                // The client decrypts the metadata with the account key, so the
                // listing has to be written with the same one
                var metadata = _key.EncryptMetadata(JsonSerializer.Serialize(new
                {
                    name = RemoteName,
                    size = 17,
                    mime = "application/octet-stream",
                    key = "0123456789abcdef0123456789abcdef",
                    lastModified = 1700000000000L,
                    creation = 1700000000000L
                }));

                var file = JsonSerializer.Serialize(new
                {
                    uuid = FileUuid,
                    metadata,
                    rm = "rm",
                    chunks = 1,
                    size = 17,
                    bucket = "bucket",
                    region = "region",
                    parent = FolderUuid,
                    version = 2
                });

                return Task.FromResult(Json($"{{\"status\":true,\"data\":{{\"folders\":[],\"uploads\":[{file}]}}}}"));
            }

            Writes++;
            return Task.FromResult(WriteResponse?.Invoke(Writes) ?? Json("{\"status\":true,\"data\":null}"));
        }

        private static HttpResponseMessage Json(string body)
            => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static (FilenClient Client, StubHandler Handler) Create(Func<int, HttpResponseMessage>? onWrite = null)
    {
        var key = DerivedKey.Create("0123456789abcdef0123456789abcdef");
        var handler = new StubHandler(key) { WriteResponse = onWrite };
        var client = FilenClient.CreateForTesting(new HttpClient(handler), BaseUrl, key);
        return (client, handler);
    }

    /// <summary>
    /// The lookup has to be answered from the listing at least once, or none of
    /// the rest of this is testing what it says it is
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task TheListingPopulatesTheCache()
    {
        var (client, handler) = Create();
        using var _ = client;

        var first = await client.GetFileEntryAsync(FolderUuid, RemoteName, Timeout, CancellationToken.None);
        Assert.IsNotNull(first);
        Assert.AreEqual(FileUuid, first!.Uuid);
        Assert.AreEqual(1, handler.Listings);

        await client.GetFileEntryAsync(FolderUuid, RemoteName, Timeout, CancellationToken.None);
        Assert.AreEqual(1, handler.Listings, "the second lookup came from the cache");
    }

    /// <summary>
    /// The delete may have landed even though the answer did not arrive, so the
    /// uuid the cache holds cannot be trusted afterwards
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task ARetryAfterAFailedDeleteAsksTheServerAgain()
    {
        var (client, handler) = Create(attempt =>
            attempt == 1 ? throw new TimeoutException("The operation has timed out.") : null!);
        using var _ = client;

        await client.GetFileEntryAsync(FolderUuid, RemoteName, Timeout, CancellationToken.None);
        Assert.AreEqual(1, handler.Listings);

        Assert.CatchAsync<TimeoutException>(async () =>
            await client.DeleteFileAsync(FileUuid, true, CancellationToken.None));

        await client.GetFileEntryAsync(FolderUuid, RemoteName, Timeout, CancellationToken.None);

        Assert.AreEqual(2, handler.Listings, "the lookup after a failed delete has to ask the server");
    }

    /// <summary>
    /// Throwing the cache away is for the failure path; a delete that was answered
    /// still just forgets the one name
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task ASuccessfulDeleteStillForgetsTheName()
    {
        var (client, handler) = Create();
        using var _ = client;

        await client.GetFileEntryAsync(FolderUuid, RemoteName, Timeout, CancellationToken.None);
        await client.DeleteFileAsync(FileUuid, true, CancellationToken.None);

        await client.GetFileEntryAsync(FolderUuid, RemoteName, Timeout, CancellationToken.None);

        Assert.AreEqual(2, handler.Listings, "the deleted name must not be answered from the cache");
    }

    /// <summary>
    /// A rename that landed leaves the cache answering with the name that is gone
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task AFailedRenameDoesNotLeaveTheOldNameCached()
    {
        var (client, handler) = Create(attempt =>
            attempt == 1 ? throw new TimeoutException("The operation has timed out.") : null!);
        using var _ = client;

        var entry = await client.GetFileEntryAsync(FolderUuid, RemoteName, Timeout, CancellationToken.None);
        Assert.IsNotNull(entry);
        Assert.AreEqual(1, handler.Listings);

        Assert.CatchAsync<TimeoutException>(async () =>
            await client.RenameFileAsync(entry!, "duplicati-b9999999999.dblock.zip.aes", CancellationToken.None));

        await client.GetFileEntryAsync(FolderUuid, RemoteName, Timeout, CancellationToken.None);

        Assert.AreEqual(2, handler.Listings, "the lookup after a failed rename has to ask the server");
    }
}
