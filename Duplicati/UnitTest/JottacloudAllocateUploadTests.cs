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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Uploads to Jottacloud are first allocated through the newer API and the content is then sent
/// to the URL the allocation returns, as the Jottacloud clients do. An option switches back to
/// the single request to the legacy upload host.
/// </summary>
[TestFixture]
public class JottacloudAllocateUploadTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails instead of reaching
    /// the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";

    private const string RemoteName = "duplicati-b0001.dblock.zip.aes";
    private const string AllocateUrl = "https://api.jottacloud.com/files/v1/allocate";
    private const string UploadUrl = "https://up-test.jottacloud.com/files/v1/upload/test-upload-id";

    private static readonly byte[] Content = Enumerable.Range(0, 10000).Select(i => (byte)(i * 13)).ToArray();
    private static readonly string ContentMd5 = Convert.ToHexString(MD5.HashData(Content)).ToLowerInvariant();

    /// <summary>
    /// A request the stub received
    /// </summary>
    private sealed record Received(HttpMethod Method, string Url, string? Authorization, string? Range, byte[] Body);

    /// <summary>
    /// Answers the token and user requests, the allocation and the upload
    /// </summary>
    private sealed class UploadHandler : HttpMessageHandler
    {
        public string State { get; init; } = "INCOMPLETE";
        public long ResumePos { get; init; }
        public string? ReportedMd5 { get; init; }
        public List<Received> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "{\"access_token\":\"test-token\",\"expires\":3600}");
            if (url.Contains("/userinfo", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "{\"username\":\"test-user\"}");

            var body = request.Content == null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add(new Received(request.Method, url, request.Headers.Authorization?.ToString(), request.Headers.Range?.ToString(), body));

            if (url == AllocateUrl)
                return Json(HttpStatusCode.OK, new JObject
                {
                    ["name"] = RemoteName,
                    ["path"] = "/test-user/Jotta/Archive/Backup/" + RemoteName,
                    ["state"] = State,
                    ["upload_id"] = "test-upload-id",
                    ["upload_url"] = UploadUrl,
                    ["bytes"] = Content.Length,
                    ["resume_pos"] = ResumePos
                }.ToString());

            if (url == UploadUrl)
                return Json(HttpStatusCode.Created, new JObject
                {
                    ["path"] = "/test-user/Jotta/Archive/Backup/" + RemoteName,
                    ["content_id"] = "test-content",
                    ["bytes"] = Content.Length,
                    ["md5"] = ReportedMd5 ?? ContentMd5,
                    ["modified"] = 0
                }.ToString());

            if (url.StartsWith("https://up.jottacloud.com/jfs/", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent($"<file name=\"{RemoteName}\"><currentRevision><state>COMPLETED</state></currentRevision></file>", Encoding.UTF8, "text/xml")
                };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static Jottacloud CreateBackend(UploadHandler handler, bool legacyUpload)
    {
        var options = new Dictionary<string, string?>
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl,
            ["jottacloud-legacy-upload"] = legacyUpload ? "true" : "false"
        };

        return new Jottacloud("jottacloud://Backup", options, new HttpClient(handler));
    }

    private static async Task Upload(Jottacloud backend)
    {
        using var stream = new MemoryStream(Content);
        await backend.PutAsync(RemoteName, stream, CancellationToken.None).ConfigureAwait(false);
    }

    [Test]
    public async Task AnUploadIsAllocatedAndThenSent_Async()
    {
        var handler = new UploadHandler();
        using (var backend = CreateBackend(handler, false))
            await Upload(backend);

        Assert.That(handler.Requests.Select(x => x.Url), Is.EqualTo(new[] { AllocateUrl, UploadUrl }));

        var allocate = handler.Requests[0];
        Assert.That(allocate.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(allocate.Authorization, Is.EqualTo("Bearer test-token"));
        // Read the timestamps as the strings that were sent, not as dates
        var json = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(Encoding.UTF8.GetString(allocate.Body), new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })!;
        Assert.That((string?)json["path"], Is.EqualTo("/jfs/Jotta/Archive/Backup/" + RemoteName));
        Assert.That((long?)json["bytes"], Is.EqualTo(Content.Length));
        Assert.That((string?)json["md5"], Is.EqualTo(ContentMd5));
        Assert.That((string?)json["created"], Does.Match(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$"));
        Assert.That((string?)json["modified"], Is.EqualTo((string?)json["created"]));

        var upload = handler.Requests[1];
        Assert.That(upload.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(upload.Range, Is.Null);
        Assert.That(upload.Body, Is.EqualTo(Content));
    }

    [Test]
    public async Task ContentTheServerAlreadyHasIsNotSentAgain_Async()
    {
        var handler = new UploadHandler { State = "COMPLETED" };
        using (var backend = CreateBackend(handler, false))
            await Upload(backend);

        Assert.That(handler.Requests.Select(x => x.Url), Is.EqualTo(new[] { AllocateUrl }));
    }

    [Test]
    public async Task AnInterruptedUploadIsResumed_Async()
    {
        const int resumePos = 4000;
        var handler = new UploadHandler { ResumePos = resumePos };
        using (var backend = CreateBackend(handler, false))
            await Upload(backend);

        var upload = handler.Requests.Single(x => x.Url == UploadUrl);
        Assert.That(upload.Range, Is.EqualTo($"bytes={resumePos}-{Content.Length - 1}"));
        Assert.That(upload.Body, Is.EqualTo(Content.Skip(resumePos).ToArray()));
    }

    [Test]
    public void AnUploadTheServerStoredDifferentlyFails()
    {
        var handler = new UploadHandler { ReportedMd5 = new string('0', 32) };
        using var backend = CreateBackend(handler, false);

        Assert.ThrowsAsync<HttpRequestException>(() => Upload(backend));
    }

    [Test]
    public async Task TheLegacyUploadSendsASingleRequest_Async()
    {
        var handler = new UploadHandler();
        using (var backend = CreateBackend(handler, true))
            await Upload(backend);

        var upload = handler.Requests.Single();
        Assert.That(upload.Url, Does.StartWith("https://up.jottacloud.com/jfs/test-user/Jotta/Archive/Backup/" + RemoteName));
        Assert.That(upload.Url, Does.Contain("umode=nomultipart"));
        Assert.That(upload.Body, Is.EqualTo(Content));
    }
}
