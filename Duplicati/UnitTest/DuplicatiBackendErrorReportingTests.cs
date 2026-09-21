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
using Duplicati.Library.Backend.Duplicati;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The Duplicati storage server explains why a request failed in the response body, and
/// reports when authenticating whether uploads or the whole account are disabled. These
/// tests cover that the backend passes both on to the user.
/// </summary>
[TestFixture]
public class DuplicatiBackendErrorReportingTests
{
    /// <summary>
    /// A message handler that answers requests from a table of paths
    /// </summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _responses = new();
        public List<string> Requests { get; } = [];

        /// <summary>
        /// Queues a response for a path; the last queued response is repeated
        /// </summary>
        public FakeServer On(string path, HttpStatusCode status, string body, string mediaType = "text/plain")
        {
            if (!_responses.TryGetValue(path, out var queue))
                _responses[path] = queue = new Queue<Func<HttpResponseMessage>>();
            queue.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            foreach (var (prefix, queue) in _responses)
                if (path.StartsWith(prefix, StringComparison.Ordinal))
                    return Task.FromResult((queue.Count > 1 ? queue.Dequeue() : queue.Peek())());

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotImplemented) { Content = new StringContent(string.Empty) });
        }
    }

    private const string GatewayCredentials = "{\"mode\":\"gateway\",\"uploadsDisabled\":false,\"accountDisabled\":false}";
    private const string QuotaExceededCredentials = "{\"mode\":\"gateway\",\"uploadsDisabled\":true,\"uploadsDisabledReason\":\"quota-exceeded\",\"accountDisabled\":false}";
    private const string DisabledGatewayCredentials = "{\"mode\":\"gateway\",\"uploadsDisabled\":true,\"uploadsDisabledReason\":\"account-disabled\",\"accountDisabled\":true,\"accountDisabledReason\":\"no-quota\",\"accountDisabledMessage\":\"Account subscription does not have access to storage\"}";
    private const string DisabledDirectCredentials = "{\"mode\":\"direct\",\"uploadsDisabled\":true,\"uploadsDisabledReason\":\"account-disabled\",\"accountDisabled\":true,\"accountDisabledReason\":\"provider-account-inactive\",\"accountDisabledMessage\":\"The iDrive storage account for this organization is disabled\"}";

    private static DuplicatiBackend CreateBackend(FakeServer server)
        => new("duplicati://storage.example.com", new Dictionary<string, string?>
        {
            [DuplicatiBackend.BACKUP_ID_OPTION] = "backup-1",
            [DuplicatiBackend.AUTH_API_ID_OPTION] = "org-1",
            [DuplicatiBackend.AUTH_API_KEY_OPTION] = "key-1",
            ["machine-id"] = "machine-1",
        }, server);

    private static HttpResponseMessage Respond(HttpStatusCode status, string body, string mediaType = "text/plain")
        => new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private static Task PutAsync(DuplicatiBackend backend)
        => backend.PutAsync("file.zip", new MemoryStream(new byte[] { 1, 2, 3 }), CancellationToken.None);

    [Test]
    [Category("Backend")]
    public void PlainTextErrorMessageFromTheServerIsReported()
    {
        using var response = Respond(HttpStatusCode.Forbidden, "API key is read-only");

        var ex = Assert.CatchAsync<HttpRequestException>(async () => await DuplicatiBackend.EnsureSuccessAsync(response, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.Forbidden, ex!.StatusCode);
        StringAssert.Contains("API key is read-only", ex.Message);
        StringAssert.Contains("403", ex.Message);
    }

    [Test]
    [Category("Backend")]
    public void JsonErrorMessageFromTheServerIsReported()
    {
        using var response = Respond(HttpStatusCode.InternalServerError, "{\"error\":\"Internal server error\"}", "application/json");

        var ex = Assert.CatchAsync<HttpRequestException>(async () => await DuplicatiBackend.EnsureSuccessAsync(response, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.InternalServerError, ex!.StatusCode);
        StringAssert.Contains("Internal server error", ex.Message);
        StringAssert.DoesNotContain("{", ex.Message);
    }

    [Test]
    [Category("Backend")]
    public void HtmlErrorBodyIsNotReported()
    {
        // A proxy in front of the server answers with html, which says nothing to the user
        using var response = Respond(HttpStatusCode.BadGateway,
            "<html><head><title>502 Bad Gateway</title></head><body>502 Bad Gateway</body></html>", "text/html");

        var ex = Assert.CatchAsync<HttpRequestException>(async () => await DuplicatiBackend.EnsureSuccessAsync(response, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadGateway, ex!.StatusCode);
        StringAssert.DoesNotContain("<html>", ex.Message);
    }

    [Test]
    [Category("Backend")]
    public void LongErrorBodyIsTruncated()
    {
        using var response = Respond(HttpStatusCode.InternalServerError, new string('x', 5000));

        var ex = Assert.CatchAsync<HttpRequestException>(async () => await DuplicatiBackend.EnsureSuccessAsync(response, CancellationToken.None));

        Assert.Less(ex!.Message.Length, 1000);
    }

    [Test]
    [Category("Backend")]
    public async Task SuccessfulResponseIsAccepted()
    {
        using var response = Respond(HttpStatusCode.OK, "OK");

        await DuplicatiBackend.EnsureSuccessAsync(response, CancellationToken.None);
    }

    [Test]
    [Category("Backend")]
    public void MissingFileIsReportedAsMissing()
    {
        var server = new FakeServer()
            .On("/credentials", HttpStatusCode.OK, GatewayCredentials, "application/json")
            .On("/get/", HttpStatusCode.NotFound, "Not found");
        using var backend = CreateBackend(server);

        Assert.CatchAsync<FileMissingException>(async () => await backend.GetAsync("file.zip", new MemoryStream(), CancellationToken.None));
    }

    [Test]
    [Category("Backend")]
    public void FailureWithoutAccountStateReportsTheServerMessage()
    {
        var server = new FakeServer()
            .On("/credentials", HttpStatusCode.OK, GatewayCredentials, "application/json")
            .On("/put/", HttpStatusCode.InternalServerError, "Internal server error");
        using var backend = CreateBackend(server);

        var ex = Assert.CatchAsync<HttpRequestException>(async () => await PutAsync(backend));

        Assert.AreEqual(HttpStatusCode.InternalServerError, ex!.StatusCode);
        StringAssert.Contains("Internal server error", ex.Message);
    }

    [Test]
    [Category("Backend")]
    public void FailedUploadIsExplainedWhenQuotaIsExceeded()
    {
        var server = new FakeServer()
            .On("/credentials", HttpStatusCode.OK, QuotaExceededCredentials, "application/json")
            .On("/put/", HttpStatusCode.Forbidden, "Quota exceeded");
        using var backend = CreateBackend(server);

        var ex = Assert.CatchAsync<UserInformationException>(async () => await PutAsync(backend));

        Assert.AreEqual(DuplicatiBackend.HELP_ID_UPLOADS_DISABLED, ex!.HelpID);
        StringAssert.Contains("quota is exceeded", ex.Message);
        StringAssert.Contains("Quota exceeded", ex.Message);
        Assert.IsInstanceOf<HttpRequestException>(ex.InnerException);
    }

    [Test]
    [Category("Backend")]
    public void FailedDownloadIsNotBlamedOnTheQuota()
    {
        // Only uploads are disabled when the quota is exceeded, so other failures stand as they are
        var server = new FakeServer()
            .On("/credentials", HttpStatusCode.OK, QuotaExceededCredentials, "application/json")
            .On("/get/", HttpStatusCode.InternalServerError, "Internal server error");
        using var backend = CreateBackend(server);

        var ex = Assert.CatchAsync<HttpRequestException>(async () => await backend.GetAsync("file.zip", new MemoryStream(), CancellationToken.None));

        StringAssert.Contains("Internal server error", ex!.Message);
    }

    [Test]
    [Category("Backend")]
    public void RejectedUploadAsksTheServerForTheAccountStateAgain()
    {
        // The quota ran out after authenticating, so the first state does not explain the failure
        var server = new FakeServer()
            .On("/credentials", HttpStatusCode.OK, GatewayCredentials, "application/json")
            .On("/credentials", HttpStatusCode.OK, QuotaExceededCredentials, "application/json")
            .On("/put/", HttpStatusCode.Forbidden, "Quota exceeded");
        using var backend = CreateBackend(server);
        backend.AccountStateRefreshInterval = TimeSpan.Zero;

        var ex = Assert.CatchAsync<UserInformationException>(async () => await PutAsync(backend));

        Assert.AreEqual(DuplicatiBackend.HELP_ID_UPLOADS_DISABLED, ex!.HelpID);
        Assert.AreEqual(2, server.Requests.FindAll(x => x == "/credentials").Count);
    }

    [Test]
    [Category("Backend")]
    public void RejectedUploadDoesNotAskAgainWithinTheRefreshInterval()
    {
        var server = new FakeServer()
            .On("/credentials", HttpStatusCode.OK, GatewayCredentials, "application/json")
            .On("/put/", HttpStatusCode.Forbidden, "Quota exceeded");
        using var backend = CreateBackend(server);

        var ex = Assert.CatchAsync<HttpRequestException>(async () => await PutAsync(backend));

        StringAssert.Contains("Quota exceeded", ex!.Message);
        Assert.AreEqual(1, server.Requests.FindAll(x => x == "/credentials").Count);
    }

    [Test]
    [Category("Backend")]
    public void RefusedCredentialsForDisabledAccountAreExplained()
    {
        // In direct mode the server refuses credentials for a disabled account with a 403 that carries the state
        var server = new FakeServer()
            .On("/credentials", HttpStatusCode.Forbidden, DisabledDirectCredentials, "application/json");
        using var backend = CreateBackend(server);

        var ex = Assert.CatchAsync<UserInformationException>(async () => await PutAsync(backend));

        Assert.AreEqual(DuplicatiBackend.HELP_ID_ACCOUNT_DISABLED, ex!.HelpID);
        StringAssert.Contains("account is disabled", ex.Message);
        StringAssert.Contains("The iDrive storage account for this organization is disabled", ex.Message);
        Assert.AreEqual(HttpStatusCode.Forbidden, (ex.InnerException as HttpRequestException)?.StatusCode);
    }

    [Test]
    [Category("Backend")]
    public void RefusedCredentialsWithoutAccountStateReportTheServerMessage()
    {
        var server = new FakeServer()
            .On("/credentials", HttpStatusCode.Unauthorized, "Invalid API key");
        using var backend = CreateBackend(server);

        var ex = Assert.CatchAsync<HttpRequestException>(async () => await PutAsync(backend));

        Assert.AreEqual(HttpStatusCode.Unauthorized, ex!.StatusCode);
        StringAssert.Contains("Invalid API key", ex.Message);
    }

    [Test]
    [Category("Backend")]
    public void FailedOperationIsExplainedWhenAccountIsDisabled()
    {
        var server = new FakeServer()
            .On("/credentials", HttpStatusCode.OK, DisabledGatewayCredentials, "application/json")
            .On("/list", HttpStatusCode.Forbidden, "Gateway mode is disabled");
        using var backend = CreateBackend(server);

        var ex = Assert.CatchAsync<UserInformationException>(async () =>
        {
            await foreach (var _ in backend.ListAsync(CancellationToken.None))
            { }
        });

        Assert.AreEqual(DuplicatiBackend.HELP_ID_ACCOUNT_DISABLED, ex!.HelpID);
        StringAssert.Contains("account is disabled", ex.Message);
        StringAssert.Contains("Account subscription does not have access to storage", ex.Message);
        StringAssert.Contains("Gateway mode is disabled", ex.Message);
    }

    [Test]
    [Category("Backend")]
    public async Task DisabledAccountStillAllowsOperationsThatSucceed()
    {
        // The gateway still serves reads for an account without quota, so restores keep working
        var server = new FakeServer()
            .On("/credentials", HttpStatusCode.OK, DisabledGatewayCredentials, "application/json")
            .On("/list", HttpStatusCode.OK, "[]", "application/json");
        using var backend = CreateBackend(server);

        var count = 0;
        await foreach (var _ in backend.ListAsync(CancellationToken.None))
            count++;

        Assert.AreEqual(0, count);
    }
}
