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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Duplicati.Library.Backend;
using Duplicati.Library.Interface;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Jottacloud explains a failed request in the response body: the JFS hosts with an XML document,
/// the allocation API and the upload hosts with a JSON document. The explanation and the request
/// id are added to the error, so a failure can be understood and looked up by Jottacloud support.
/// The bodies in these tests are the ones the services returned when the errors were provoked.
/// </summary>
[TestFixture]
public class JottacloudErrorReportingTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails instead of reaching
    /// the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";

    private const string AllocateUrl = "https://api.jottacloud.com/files/v1/allocate";
    private const string UploadUrl = "https://085-up-r.jotta.cloud/files/v1/upload/test-upload-id";

    /// <summary>
    /// Answers the token and user requests, allocates uploads, and answers every other request with the given failure
    /// </summary>
    private sealed class FailingHandler(string username, HttpStatusCode status, string body, string contentType) : HttpMessageHandler
    {
        public string Username => username;
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Task.FromResult(Respond(HttpStatusCode.OK, "{\"access_token\":\"test-token\",\"expires\":3600}", "application/json"));
            if (url.Contains("/userinfo", StringComparison.Ordinal))
                return Task.FromResult(Respond(HttpStatusCode.OK, $"{{\"username\":\"{username}\"}}", "application/json"));

            Requests.Add(request);
            if (url == AllocateUrl)
                return Task.FromResult(Respond(HttpStatusCode.Accepted, $"{{\"state\":\"INCOMPLETE\",\"upload_url\":\"{UploadUrl}\",\"resume_pos\":0}}", "application/json"));

            // The real handler links the response to its request, which tells the upload host apart
            var response = Respond(status, body, contentType);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Respond(HttpStatusCode status, string body, string contentType)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };
    }

    /// <summary>
    /// Creates a backend for a new account, so a rate limit in one test does not hold back another
    /// </summary>
    private static (Jottacloud Backend, FailingHandler Handler) CreateBackend(HttpStatusCode status, string body, string contentType)
    {
        var handler = new FailingHandler("user-" + Guid.NewGuid().ToString("N"), status, body, contentType);
        var options = new Dictionary<string, string?>
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl
        };

        return (new Jottacloud("jottacloud://Backup", options, new HttpClient(handler)), handler);
    }

    private static async Task Upload(Jottacloud backend)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("content"));
        await backend.PutAsync("duplicati-20261125T150000Z.dlist.zip.aes", stream, CancellationToken.None).ConfigureAwait(false);
    }

    [Test]
    public async Task ARenameSendsTheFullDestinationPath_Async()
    {
        var (backend, handler) = CreateBackend(HttpStatusCode.OK, "<file name=\"new.bin\"/>", "text/xml");
        using (backend)
            await backend.RenameAsync("old.bin", "new.bin", CancellationToken.None);

        var request = handler.Requests.Single();
        var username = handler.Username;
        Assert.That(request.RequestUri!.GetLeftPart(UriPartial.Path), Is.EqualTo($"https://jfs.jottacloud.com/jfs/{username}/Jotta/Archive/Backup/old.bin"));
        Assert.That(HttpUtility.ParseQueryString(request.RequestUri.Query)["mv"], Is.EqualTo($"/{username}/Jotta/Archive/Backup/new.bin"));
    }

    [Test]
    public void AnUploadThatDoesNotMatchTheAllocationIsExplained()
    {
        var (backend, _) = CreateBackend((HttpStatusCode)421,
            "{\"code\":421,\"message\":null,\"cause\":\"\",\"error_id\":\"CorruptUploadOpenApiException\",\"x-id\":\"067180364423\"}",
            "application/json");
        using var _b = backend;

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => Upload(backend));
        Assert.That(ex!.StatusCode, Is.EqualTo((HttpStatusCode)421));
        Assert.That(ex.Message, Does.Contain("did not match the size and MD5 hash"));
        Assert.That(ex.Message, Does.Contain("CorruptUploadOpenApiException"));
        Assert.That(ex.Message, Does.Contain("x-id: 067180364423"));
    }

    [Test]
    public void AnUploadWithoutAMatchingAllocationIsExplained()
    {
        var (backend, _) = CreateBackend((HttpStatusCode)420,
            "{\"code\":420,\"message\":null,\"cause\":\"\",\"error_id\":\"IncompleteUploadOpenApiException\",\"x-id\":\"398815571050\"}",
            "application/json");
        using var _b = backend;

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => Upload(backend));
        Assert.That(ex!.StatusCode, Is.EqualTo((HttpStatusCode)420));
        Assert.That(ex.Message, Does.Contain("no matching upload allocation"));
        Assert.That(ex.Message, Does.Contain("x-id: 398815571050"));
    }

    [Test]
    public void AConflictOnTheUploadHostIsExplained()
    {
        var (backend, _) = CreateBackend(HttpStatusCode.Conflict, "{\"message\":\"\"}", "application/json");
        using var _b = backend;

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => Upload(backend));
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(ex.Message, Does.Contain("allocation is no longer valid"));
    }

    [Test]
    public void AnErrorFromTheJfsHostIsExplained()
    {
        var (backend, _) = CreateBackend(HttpStatusCode.BadRequest,
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n\n<error>\n  <code>400</code>\n  <message>no.jotta.backup.errors.InvalidArgumentException: to: JFS file paths must contain the name of the file, path='/Backup/new.bin'</message>\n  <reason>Bad Request</reason>\n  <cause>to: JFS file paths must contain the name of the file, path='/Backup/new.bin'</cause>\n  <hostname></hostname>\n  <x-id>796025517767</x-id>\n</error>",
            "text/xml");
        using var _b = backend;

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => backend.RenameAsync("old.bin", "new.bin", CancellationToken.None));
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(ex.Message, Does.Contain("JFS file paths must contain the name of the file"));
        Assert.That(ex.Message, Does.Contain("x-id: 796025517767"));
    }

    [Test]
    public void AnExplainedRateLimitIsStillReportedAsARateLimit()
    {
        var (backend, _) = CreateBackend(HttpStatusCode.TooManyRequests, "{\"code\":429,\"message\":null,\"x-id\":\"123\"}", "application/json");
        using var _b = backend;

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => Upload(backend));
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(ex.InnerException, Is.InstanceOf<TooManyRequestException>());
        Assert.That(ex.Message, Does.Contain("limiting the number of requests"));
        Assert.That(ex.Message, Does.Contain("x-id: 123"));
    }

    [Test]
    public void AnErrorWithoutABodyKeepsTheStatusMessage()
    {
        var (backend, _) = CreateBackend(HttpStatusCode.InternalServerError, "", "text/plain");
        using var _b = backend;

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => Upload(backend));
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(ex.Message, Does.Not.Contain("Jottacloud reported"));
    }
}
