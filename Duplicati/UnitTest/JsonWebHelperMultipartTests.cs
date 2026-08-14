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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// A multipart post is the upload path for Box.com. The response status was not
/// checked, so a failed upload was handed to the JSON parser and reported as a
/// missing file id instead of the error the server sent.
/// </summary>
[TestFixture]
public class JsonWebHelperMultipartTests
{
    private const string UploadUrl = "https://upload.example.com/files/content";

    /// <summary>
    /// The shape of an upload response, mirroring the way Box.com reports the
    /// uploaded file. An error body deserializes into this without failing.
    /// </summary>
    private sealed class UploadResponse
    {
        public UploadEntry[]? Entries { get; set; }
    }

    private sealed class UploadEntry
    {
        public string? Id { get; set; }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
    }

    /// <summary>
    /// Stands in for a backend, recording what the error handler is given
    /// </summary>
    private sealed class TestClient : JsonWebHelperHttpClient, IDisposable
    {
        private readonly HttpClient _client;

        public TestClient(HttpClient httpClient) : base(httpClient)
            => _client = httpClient;

        public void Dispose()
            => _client.Dispose();

        /// <summary>
        /// Set to false to act like a backend that does not recognize the error
        /// </summary>
        public bool ReportError { get; set; } = true;

        public int HandlerCalls { get; private set; }
        public HttpStatusCode? HandledStatus { get; private set; }
        public string? HandledBody { get; private set; }

        public override async Task AttemptParseAndThrowExceptionAsync(Exception ex, HttpResponseMessage? responseContext, CancellationToken cancellationToken)
        {
            HandlerCalls++;
            HandledStatus = responseContext?.StatusCode;
            if (responseContext != null)
                HandledBody = await responseContext.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (ReportError)
                throw new UserInformationException("The server reported an error", "test");
        }

        public Task<UploadResponse> UploadAsync(CancellationToken cancellationToken)
        {
            var parts = new MultipartFormDataContent
            {
                { new StringContent("{\"name\":\"file.txt\"}", Encoding.UTF8, "application/json"), "attributes" },
                { new ByteArrayContent([1, 2, 3]), "file", "file.txt" }
            };

            return PostMultipartAndGetJsonDataAsync<UploadResponse>(UploadUrl, cancellationToken, parts);
        }
    }

    private static TestClient CreateClient(HttpStatusCode status, string body)
        => new TestClient(new HttpClient(new StubHandler(status, body)));

    private const string ErrorBody = "{\"type\":\"error\",\"status\":409,\"code\":\"item_name_in_use\",\"message\":\"Item with the same name already exists\"}";

    [Test]
    [Category("Backend")]
    public void FailedUploadIsReportedByTheBackend()
    {
        // The error body parses into the expected response type without any
        // entries, so the failure has to be caught from the status
        using var client = CreateClient(HttpStatusCode.Conflict, ErrorBody);

        var ex = Assert.CatchAsync<UserInformationException>(async () => await client.UploadAsync(CancellationToken.None));

        StringAssert.Contains("The server reported an error", ex!.Message);
        Assert.AreEqual(1, client.HandlerCalls, "The error should be handed to the backend exactly once");
    }

    [Test]
    [Category("Backend")]
    public void FailedUploadPassesTheResponseToTheBackend()
    {
        // The backend reads the error body to build its message, so the response
        // must still be readable when it is called
        using var client = CreateClient(HttpStatusCode.Conflict, ErrorBody);

        Assert.CatchAsync<UserInformationException>(async () => await client.UploadAsync(CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.Conflict, client.HandledStatus);
        StringAssert.Contains("item_name_in_use", client.HandledBody ?? "");
    }

    [Test]
    [Category("Backend")]
    public void FailedUploadReportsTheHttpErrorWhenTheBackendDoesNot()
    {
        // A backend that does not recognize the error leaves the request error in place
        using var client = CreateClient(HttpStatusCode.InternalServerError, "not json");
        client.ReportError = false;

        var ex = Assert.CatchAsync<HttpRequestException>(async () => await client.UploadAsync(CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.InternalServerError, ex!.StatusCode);
    }

    [Test]
    [Category("Backend")]
    public async Task SuccessfulUploadIsReturned()
    {
        using var client = CreateClient(HttpStatusCode.OK, "{\"entries\":[{\"id\":\"1234\"}]}");

        var result = await client.UploadAsync(CancellationToken.None);

        Assert.AreEqual("1234", result.Entries?[0].Id);
        Assert.AreEqual(0, client.HandlerCalls, "A successful upload should not invoke the error handler");
    }
}
