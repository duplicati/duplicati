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
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.Filen;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// A download from Filen is decrypted as it arrives. The response status was not
/// checked, so whatever the server sent on an error was handed to the decrypter
/// and reported as a cryptographic failure instead of the error itself.
/// </summary>
[TestFixture]
public class FilenDownloadStatusTests
{
    /// <summary>
    /// The file key doubles as the AES key, so it has to be 32 characters
    /// </summary>
    private const string FileKey = "0123456789abcdef0123456789abcdef";
    private const string Region = "de-1";
    private const string Bucket = "filen-1";
    private const string FileUuid = "11111111-2222-3333-4444-555555555555";

    /// <summary>
    /// What a gateway or CDN sends when it is not able to serve the request. The
    /// body is longer than an IV plus a GCM tag, so it reaches the tag check.
    /// </summary>
    private const string ServerErrorBody =
        "<html>\r\n<head><title>503 Service Temporarily Unavailable</title></head>\r\n" +
        "<body>\r\n<center><h1>503 Service Temporarily Unavailable</h1></center>\r\n</body>\r\n</html>";

    /// <summary>
    /// Answers the two calls the login makes, and hands the download whatever the
    /// test asked for. The gateway and egest hosts are picked at random from eight
    /// candidates each, so the requests are told apart by path.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _downloadStatus;
        private readonly HttpContent _downloadContent;

        public StubHandler(HttpStatusCode downloadStatus, HttpContent downloadContent)
        {
            _downloadStatus = downloadStatus;
            _downloadContent = downloadContent;
        }

        public int DownloadCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/v3/auth/info", StringComparison.Ordinal))
                return Task.FromResult(Json("{\"status\":true,\"data\":{\"authVersion\":2,\"salt\":\"736f6d6573616c74\"}}"));

            if (path.EndsWith("/v3/user/masterKeys", StringComparison.Ordinal))
                return Task.FromResult(Json("{\"status\":true,\"data\":{\"masterKeys\":\"\"}}"));

            if (path.EndsWith($"/{Region}/{Bucket}/{FileUuid}/0", StringComparison.Ordinal))
            {
                DownloadCalls++;
                return Task.FromResult(new HttpResponseMessage(_downloadStatus) { Content = _downloadContent });
            }

            throw new InvalidOperationException($"Unexpected request to {request.RequestUri}");
        }

        private static HttpResponseMessage Json(string body)
            => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static Task<FilenClient> CreateClientAsync(StubHandler handler)
        => FilenClient.CreateClientAsync(new HttpClient(handler), "user@example.com", "password", null, "test-api-key", CancellationToken.None);

    private static FilenFileEntry CreateEntry(long size)
        => new()
        {
            Uuid = FileUuid,
            Name = "duplicati-b0123456789.dblock.zip.aes",
            IsFolder = false,
            Size = size,
            LastModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedTimestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Region = Region,
            Bucket = Bucket,
            Chunks = 1,
            FileKey = FileKey,
            Version = 2,
            MimeType = "application/octet-stream"
        };

    [Test]
    [Category("Backend")]
    public async Task ServerErrorIsReportedAsHttpFailure()
    {
        // An error page is not ciphertext, so decrypting it fails the tag check
        // and the status the server actually sent is lost
        var handler = new StubHandler(HttpStatusCode.ServiceUnavailable, new StringContent(ServerErrorBody, Encoding.UTF8, "text/html"));
        using var client = await CreateClientAsync(handler);
        using var target = new MemoryStream();

        var ex = Assert.CatchAsync<HttpRequestException>(async () =>
            await client.DownloadAndDecryptToStreamAsync(CreateEntry(1024), target, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex!.StatusCode);
        Assert.AreEqual(1, handler.DownloadCalls);
    }

    [Test]
    [Category("Backend")]
    public async Task ShortErrorBodyIsReportedAsHttpFailure()
    {
        // A body shorter than the IV never reaches the tag check, it fails while
        // being sliced apart, which says even less about what went wrong
        var handler = new StubHandler(HttpStatusCode.BadGateway, new StringContent("bad gateway", Encoding.UTF8, "text/plain"));
        using var client = await CreateClientAsync(handler);
        using var target = new MemoryStream();

        var ex = Assert.CatchAsync<HttpRequestException>(async () =>
            await client.DownloadAndDecryptToStreamAsync(CreateEntry(1024), target, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadGateway, ex!.StatusCode);
    }

    [Test]
    [Category("Backend")]
    public async Task SuccessfulDownloadIsStillDecrypted()
    {
        var payload = Encoding.UTF8.GetBytes("the contents of a backup volume");
        var encrypted = FilenCrypto.EncryptData(payload, DerivedKey.Create(FileKey));

        var handler = new StubHandler(HttpStatusCode.OK, new ByteArrayContent(encrypted));
        using var client = await CreateClientAsync(handler);
        using var target = new MemoryStream();

        await client.DownloadAndDecryptToStreamAsync(CreateEntry(payload.Length), target, CancellationToken.None);

        Assert.AreEqual(payload, target.ToArray());
    }
}
