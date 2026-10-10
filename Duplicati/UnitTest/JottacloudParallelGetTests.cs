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
using System.Net.Http.Headers;
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
/// With more than one thread, the Jottacloud backend downloads a file in chunks, several at a time,
/// and writes them in order. These tests answer its requests from a stub, so the order in which the
/// chunks arrive can be chosen.
/// </summary>
[TestFixture]
public class JottacloudParallelGetTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails instead of reaching
    /// the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";

    private const int ChunkSize = 1024;
    private const int Chunks = 4;
    private const string RemoteName = "duplicati-b0001.dblock.zip.aes";

    private static readonly byte[] Content = Enumerable.Range(0, ChunkSize * Chunks).Select(i => (byte)(i * 7)).ToArray();

    /// <summary>
    /// Answers the token, user and file requests, and holds every chunk but the first until the
    /// first one is being written
    /// </summary>
    private sealed class ChunkHandler : HttpMessageHandler
    {
        public readonly TaskCompletionSource FirstChunkWriting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource OtherChunksSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int m_otherChunksSent;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Json("{\"access_token\":\"test-token\",\"expires\":3600}");
            if (url.Contains("/userinfo", StringComparison.Ordinal))
                return Json("{\"username\":\"test-user\"}");
            if (!url.Contains(RemoteName, StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var range = request.Headers.Range?.Ranges.SingleOrDefault();
            if (range == null)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"<file name=\"{RemoteName}\"><currentRevision><state>COMPLETED</state><size>{Content.Length}</size><modified>2026-10-04-T00:00:00Z</modified></currentRevision></file>",
                        Encoding.UTF8, "text/xml")
                };

            var from = range.From!.Value;
            var to = range.To!.Value;
            if (from != 0)
                await FirstChunkWriting.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            var content = new ByteArrayContent(Content, (int)from, (int)(to - from + 1));
            content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, Content.Length);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };

            if (from != 0 && Interlocked.Increment(ref m_otherChunksSent) == Chunks - 1)
                OtherChunksSent.TrySetResult();

            return response;
        }

        private static HttpResponseMessage Json(string body)
            => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    /// <summary>
    /// Holds the first write until all the other chunks have been received
    /// </summary>
    private sealed class SlowFirstWriteStream(ChunkHandler handler) : MemoryStream
    {
        private bool m_written;

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (!m_written)
            {
                m_written = true;
                handler.FirstChunkWriting.TrySetResult();
                await handler.OtherChunksSent.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                // Lets the chunk tasks finish reading what was sent to them
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            await base.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ChunksThatFinishWhileTheFirstIsWrittenAreStillWritten_Async()
    {
        var handler = new ChunkHandler();
        using var backend = new Jottacloud("jottacloud://Backup", new Dictionary<string, string?>
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl,
            ["jottacloud-threads"] = Chunks.ToString(),
            ["jottacloud-chunksize"] = $"{ChunkSize}b"
        }, new HttpClient(handler));

        using var target = new SlowFirstWriteStream(handler);
        await backend.GetAsync(RemoteName, target, CancellationToken.None);

        NUnit.Framework.Assert.That(target.ToArray(), Is.EqualTo(Content));
    }

    /// <summary>
    /// The backend loader picks the constructor by argument count, so the one the tests use must
    /// not become the one it finds.
    /// </summary>
    [Test]
    public void TheLoaderStillFindsTheTwoArgumentConstructor()
    {
        using var backend = (IBackend)Activator.CreateInstance(
            typeof(Jottacloud), "jottacloud://Backup", new Dictionary<string, string?>
            {
                ["authid"] = "test-authid",
                ["oauth-url"] = OAuthUrl
            })!;

        Assert.AreEqual("jottacloud", backend.ProtocolKey);
    }
}
