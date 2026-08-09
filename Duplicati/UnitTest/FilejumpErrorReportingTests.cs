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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Every listing, delete and folder lookup in the Filejump backend hands its
/// response to the same error handler, so what that handler does with a failed
/// request is what the user ends up seeing.
/// </summary>
[TestFixture]
public class FilejumpErrorReportingTests
{
    private static HttpResponseMessage Respond(HttpStatusCode status, string body, string mediaType = "application/json")
        => new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    [Test]
    [Category("Backend")]
    public void ErrorBodyWithoutMessageIsReportedAsHttpFailure()
    {
        // Nothing in the body to report, so the status is all there is to go on
        using var response = Respond(HttpStatusCode.InternalServerError, "{}");

        var ex = Assert.CatchAsync<HttpRequestException>(async () => await Filejump.EnsureSuccessStatusCode(response));

        Assert.AreEqual(HttpStatusCode.InternalServerError, ex!.StatusCode);
    }

    [Test]
    [Category("Backend")]
    public void ErrorBodyWithOnlyAStatusFieldIsReportedAsHttpFailure()
    {
        using var response = Respond(HttpStatusCode.ServiceUnavailable, "{\"status\":\"error\"}");

        var ex = Assert.CatchAsync<HttpRequestException>(async () => await Filejump.EnsureSuccessStatusCode(response));

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex!.StatusCode);
    }

    [Test]
    [Category("Backend")]
    public void NonJsonErrorBodyIsReportedAsHttpFailure()
    {
        // A gateway in front of the API answers with html, not with the shape
        // the backend expects
        using var response = Respond(HttpStatusCode.BadGateway,
            "<html><head><title>502 Bad Gateway</title></head><body>502 Bad Gateway</body></html>", "text/html");

        var ex = Assert.CatchAsync<HttpRequestException>(async () => await Filejump.EnsureSuccessStatusCode(response));

        Assert.AreEqual(HttpStatusCode.BadGateway, ex!.StatusCode);
    }

    [Test]
    [Category("Backend")]
    public void ErrorMessageFromTheServerIsReported()
    {
        using var response = Respond((HttpStatusCode)422, "{\"message\":\"Storage quota exceeded\"}");

        var ex = Assert.CatchAsync<InvalidOperationException>(async () => await Filejump.EnsureSuccessStatusCode(response));

        StringAssert.Contains("Storage quota exceeded", ex!.Message);
    }

    [Test]
    [Category("Backend")]
    public async Task SuccessfulResponseIsAccepted()
    {
        using var response = Respond(HttpStatusCode.OK, "{\"status\":\"success\"}");

        await Filejump.EnsureSuccessStatusCode(response);
    }
}
