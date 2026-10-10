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
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs.Models;
using Duplicati.Library.Backend.AzureBlob;
using Duplicati.Library.Utility.Options;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The Azure client stops any request that takes longer than its network timeout, 100 seconds by
/// default, so a volume sent in one request failed on a slow connection (issue #6629). These tests
/// read the requests an upload sends.
/// </summary>
[TestFixture]
public class AzureBlobUploadBlockTests
{
    /// <summary>
    /// Answers every request as created, and keeps the query and the size of each body
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public readonly List<(string Query, long Length)> Requests = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var length = request.Content == null ? 0 : (await request.Content.ReadAsByteArrayAsync(cancellationToken)).LongLength;
            lock (Requests)
                Requests.Add((request.RequestUri!.Query, length));

            var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = new ByteArrayContent(Array.Empty<byte>()) };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"0x1\"");
            response.Content.Headers.LastModified = DateTimeOffset.UtcNow;
            return response;
        }
    }

    private static async Task<List<(string Query, long Length)>> UploadAsync(long size)
    {
        var handler = new RecordingHandler();
        var wrapper = new AzureBlobWrapper("account", null, "sv=2020-08-04&sig=test", "container", "", null,
            new HashSet<AccessTier>(), TimeoutOptionsHelper.Parse(new Dictionary<string, string?>()), 0, handler);

        using var source = new MemoryStream(new byte[size]);
        await wrapper.AddFileStream("duplicati-b0001.dblock.zip.aes", source, CancellationToken.None);
        return handler.Requests;
    }

    [Test]
    public async Task ALargeFileIsSentInBlocks_Async()
    {
        var size = 2 * AzureBlobWrapper.UploadBlockSize + 1024;
        var requests = await UploadAsync(size);

        var blocks = requests.Where(x => x.Query.Contains("comp=block&", StringComparison.Ordinal) || x.Query.EndsWith("comp=block", StringComparison.Ordinal)).ToList();
        Assert.That(requests.Select(x => x.Length), Has.All.LessThanOrEqualTo(AzureBlobWrapper.UploadBlockSize), "A request carried more than one block");
        Assert.That(blocks.Sum(x => x.Length), Is.EqualTo(size));
        Assert.That(requests.Count(x => x.Query.Contains("comp=blocklist", StringComparison.Ordinal)), Is.EqualTo(1));
    }

    [Test]
    public async Task ASmallFileIsStillSentInOneRequest_Async()
    {
        var requests = await UploadAsync(1024);

        Assert.That(requests, Has.Count.EqualTo(1));
        Assert.That(requests[0].Length, Is.EqualTo(1024));
    }
}
