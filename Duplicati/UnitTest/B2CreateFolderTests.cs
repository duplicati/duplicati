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

#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.Backblaze;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A sync job makes sure each folder exists on the destination by asking the backend to create it.
/// B2 has no folders, and creating the bucket again fails when it already exists.
/// </summary>
[TestFixture]
[Category("Backend")]
public class B2CreateFolderTests
{
    private const string BucketName = "mybucket";

    /// <summary>
    /// Answers the B2 API for one account, holding the bucket or not
    /// </summary>
    private sealed class StubB2(bool bucketExists) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = request.RequestUri!.AbsolutePath.Split('/').Last();
            Calls.Add(call);

            return Task.FromResult(call switch
            {
                "b2_authorize_account" => Json(HttpStatusCode.OK,
                    "{\"accountId\":\"acc\",\"apiUrl\":\"https://api.b2.invalid\",\"authorizationToken\":\"token\",\"downloadUrl\":\"https://download.b2.invalid\"}"),
                "b2_list_buckets" => Json(HttpStatusCode.OK, bucketExists
                    ? $"{{\"buckets\":[{{\"accountId\":\"acc\",\"bucketId\":\"b1\",\"bucketName\":\"{BucketName}\",\"bucketType\":\"allPrivate\"}}]}}"
                    : "{\"buckets\":[]}"),
                "b2_create_bucket" => bucketExists
                    ? Json(HttpStatusCode.BadRequest, "{\"status\":400,\"code\":\"duplicate_bucket_name\",\"message\":\"Bucket name is already in use.\"}")
                    : Json(HttpStatusCode.OK, $"{{\"accountId\":\"acc\",\"bucketId\":\"b1\",\"bucketName\":\"{BucketName}\",\"bucketType\":\"allPrivate\"}}"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static B2 CreateBackend(string path, StubB2 stub)
        => new($"b2://{BucketName}/{path}", new Dictionary<string, string?>
        {
            ["b2-accountid"] = "acc",
            ["b2-applicationkey"] = "key"
        }, stub);

    [TestCase("")]
    [TestCase("sync/sub folder")]
    public async Task AFolderInAnExistingBucketNeedsNothingCreated(string path)
    {
        var stub = new StubB2(bucketExists: true);
        using var backend = CreateBackend(path, stub);

        await backend.CreateFolderAsync(CancellationToken.None);

        Assert.That(stub.Calls, Does.Not.Contain("b2_create_bucket"));
    }

    [Test]
    public async Task AMissingBucketIsCreated()
    {
        var stub = new StubB2(bucketExists: false);
        using var backend = CreateBackend("backup", stub);

        await backend.CreateFolderAsync(CancellationToken.None);

        Assert.That(stub.Calls, Does.Contain("b2_create_bucket"));
    }

    [Test]
    public void BackendIsCreatedThroughTheLoader()
    {
        // The loader creates the backend with two arguments through reflection
        using var backend = Library.DynamicLoader.BackendLoader.GetBackend($"b2://{BucketName}/backup",
            new Dictionary<string, string> { ["b2-accountid"] = "acc", ["b2-applicationkey"] = "key" });

        Assert.That(backend, Is.Not.Null);
    }
}
