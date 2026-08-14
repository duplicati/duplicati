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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// An upload to pCloud is judged by the result code in the body. That code is an
/// int defaulting to zero, which is what pCloud uses for success, so a body that
/// does not carry one at all reads as a completed upload.
/// </summary>
[TestFixture]
public class PCloudUploadStatusTests
{
    /// <summary>
    /// No path in the url, so the folder id is resolved without a request and the
    /// upload is the only call the stub has to answer
    /// </summary>
    private const string Url = "pcloud://api.pcloud.com/";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly string _mediaType;

        public StubHandler(HttpStatusCode status, string body, string mediaType = "application/json")
        {
            _status = status;
            _body = body;
            _mediaType = mediaType;
        }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, _mediaType)
            });
        }
    }

    private static pCloudBackend CreateBackend(StubHandler handler)
        => new(Url, new Dictionary<string, string?> { ["authid"] = "test-auth-id" }, handler);

    private static Stream Volume()
        => new MemoryStream(Encoding.UTF8.GetBytes("the contents of a backup volume"));

    [Test]
    [Category("Backend")]
    public async Task FailedUploadIsNotReportedAsSuccess()
    {
        // The body is json, so it deserializes without complaint, and the result
        // code it does not contain reads as zero
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "{\"error\":\"upstream failure\"}");
        using var backend = CreateBackend(handler);
        await using var volume = Volume();

        var ex = Assert.CatchAsync<HttpRequestException>(async () =>
            await backend.PutAsync("duplicati-b0123456789.dblock.zip.aes", volume, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.InternalServerError, ex!.StatusCode);
        Assert.AreEqual(1, handler.Calls);
    }

    [Test]
    [Category("Backend")]
    public async Task NonJsonErrorBodyIsReportedAsHttpFailure()
    {
        var handler = new StubHandler(HttpStatusCode.BadGateway,
            "<html><head><title>502 Bad Gateway</title></head><body>502 Bad Gateway</body></html>", "text/html");
        using var backend = CreateBackend(handler);
        await using var volume = Volume();

        var ex = Assert.CatchAsync<HttpRequestException>(async () =>
            await backend.PutAsync("duplicati-b0123456789.dblock.zip.aes", volume, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadGateway, ex!.StatusCode);
    }

    [Test]
    [Category("Backend")]
    public async Task SuccessfulUploadStillSucceeds()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"result\":0,\"fileids\":[1234]}");
        using var backend = CreateBackend(handler);
        await using var volume = Volume();

        await backend.PutAsync("duplicati-b0123456789.dblock.zip.aes", volume, CancellationToken.None);

        Assert.AreEqual(1, handler.Calls);
    }
}
