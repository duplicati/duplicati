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

using System.Collections.Generic;
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
/// Deleting a file from Tahoe-LAFS sends the request and throws the response
/// away. Listing does the same thing but checks the status first, and the two
/// carry the same catch clause, so a delete that the server refused looks
/// exactly like one that worked.
/// </summary>
[TestFixture]
public class TahoeDeleteStatusTests
{
    /// <summary>
    /// The constructor insists on a dircap in the path
    /// </summary>
    private const string Url = "tahoe://example.invalid/uri/URI:DIR2:aaaaaaaaaaaaaaaaaaaaaaaaaa:bbbbbbbbbbbbbbbbbbbbbbbbbb/";

    /// <summary>
    /// Answers a delete with one status and a listing of the folder with another
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly HttpStatusCode _folderStatus;

        public StubHandler(HttpStatusCode status, HttpStatusCode folderStatus = HttpStatusCode.OK)
        {
            _status = status;
            _folderStatus = folderStatus;
        }

        public int Calls { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastMethod = request.Method;
            Requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery}");

            // As Tahoe-LAFS 1.20.0 answers: 404 "No such child: <name>" for a file or folder that is not there
            var status = request.Method == HttpMethod.Delete ? _status : _folderStatus;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.NotFound ? "No such child" : string.Empty, Encoding.UTF8, "text/plain")
            });
        }
    }

    private static TahoeBackend CreateBackend(StubHandler handler)
        => new(Url, new Dictionary<string, string?>(), handler);

    [Test]
    [Category("Backend")]
    public void FailedDeleteIsNotReportedAsSuccess()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError);
        using var backend = CreateBackend(handler);

        var ex = Assert.CatchAsync<HttpRequestException>(async () =>
            await backend.DeleteAsync("duplicati-b0123456789.dblock.zip.aes", CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.InternalServerError, ex!.StatusCode);
        Assert.AreEqual(1, handler.Calls);
        Assert.AreEqual(HttpMethod.Delete, handler.LastMethod);
    }

    /// <summary>
    /// A file that is no longer in an existing folder is reported as missing, so a delete of a file that is already
    /// gone is checked against the listing and counted as done, as for the other backends
    /// </summary>
    [Test]
    [Category("Backend")]
    public void MissingFileInAnExistingFolderIsReportedAsFileMissing()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, folderStatus: HttpStatusCode.OK);
        using var backend = CreateBackend(handler);

        Assert.CatchAsync<FileMissingException>(async () =>
            await backend.DeleteAsync("duplicati-b0123456789.dblock.zip.aes", CancellationToken.None));
        NUnit.Framework.Assert.That(handler.Requests, Has.Count.EqualTo(2));
        NUnit.Framework.Legacy.StringAssert.StartsWith("DELETE ", handler.Requests[0]);
        NUnit.Framework.Legacy.StringAssert.StartsWith("GET ", handler.Requests[1]);
        NUnit.Framework.Legacy.StringAssert.EndsWith("?t=json", handler.Requests[1]);
    }

    /// <summary>
    /// When the folder cannot be listed either, it is the folder that is missing
    /// </summary>
    [Test]
    [Category("Backend")]
    public void MissingFolderIsReportedAsFolderMissing()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, folderStatus: HttpStatusCode.NotFound);
        using var backend = CreateBackend(handler);

        Assert.CatchAsync<FolderMissingException>(async () =>
            await backend.DeleteAsync("duplicati-b0123456789.dblock.zip.aes", CancellationToken.None));
    }

    [Test]
    [Category("Backend")]
    public async Task SuccessfulDeleteStillSucceeds()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        using var backend = CreateBackend(handler);

        await backend.DeleteAsync("duplicati-b0123456789.dblock.zip.aes", CancellationToken.None);

        Assert.AreEqual(1, handler.Calls);
    }
}
