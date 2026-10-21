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
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// A Jottacloud server can stop sending in the middle of a response without closing the
/// connection. These tests answer the backend from a stub that does that, and check that the
/// configured timeouts end the call instead of leaving it waiting forever.
/// </summary>
[TestFixture]
public class JottacloudStalledResponseTests
{
    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails instead of reaching
    /// the real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";

    private const int ChunkSize = 1024;
    private const int Chunks = 4;
    private const string RemoteName = "duplicati-b0001.dblock.zip.aes";

    /// <summary>
    /// How long a call may take before the test decides no timeout fired. The timeouts under test
    /// are one second.
    /// </summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    private static readonly byte[] Content = Enumerable.Range(0, ChunkSize * Chunks).Select(i => (byte)(i * 7)).ToArray();

    private static readonly string FileInfoXml =
        $"<file name=\"{RemoteName}\"><currentRevision><state>COMPLETED</state><size>{Content.Length}</size><modified>2026-10-04-T00:00:00Z</modified></currentRevision></file>";

    private static readonly string FolderXml =
        $"<folder name=\"Backup\"><files>{FileInfoXml}</files></folder>";

    /// <summary>
    /// Releases the reads still blocked when a test ends, so a failing test does not leave
    /// threads waiting for the rest of the run
    /// </summary>
    private CancellationTokenSource m_release = new();

    [SetUp]
    public void SetUp() => m_release = new CancellationTokenSource();

    [TearDown]
    public void TearDown()
    {
        m_release.Cancel();
        m_release.Dispose();
    }

    private enum StallPoint
    {
        None,
        ListBody,
        InfoBody,
        DownloadBody,
        ChunkBody,
        ChunkHeaders
    }

    /// <summary>
    /// Sends the start of a response, then neither sends more nor ends it
    /// </summary>
    private sealed class StallingStream(byte[] prefix, CancellationToken release) : Stream
    {
        private int m_position;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (m_position < prefix.Length)
            {
                var count = Math.Min(buffer.Length, prefix.Length - m_position);
                prefix.AsMemory(m_position, count).CopyTo(buffer);
                m_position += count;
                return count;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, release);
            await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Answers the token, user, listing, file info and download requests, and stalls at one point
    /// </summary>
    private sealed class StallingHandler(StallPoint stall, CancellationToken release) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith(OAuthUrl, StringComparison.Ordinal))
                return Json("{\"access_token\":\"test-token\",\"expires\":3600}");
            if (url.Contains("/userinfo", StringComparison.Ordinal))
                return Json("{\"username\":\"test-user\"}");

            if (!url.Contains(RemoteName, StringComparison.Ordinal))
                return Xml(FolderXml, stall == StallPoint.ListBody);

            var range = request.Headers.Range?.Ranges.SingleOrDefault();
            if (range != null)
            {
                var from = range.From!.Value;
                var to = range.To!.Value;
                if (stall == StallPoint.ChunkHeaders && from != 0)
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, release);
                    await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
                }

                var bytes = Content.AsSpan((int)from, (int)(to - from + 1)).ToArray();
                HttpContent content = stall == StallPoint.ChunkBody && from != 0
                    ? new StreamContent(new StallingStream(bytes[..16], release))
                    : new ByteArrayContent(bytes);
                content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, Content.Length);
                return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
            }

            if (url.Contains("mode=bin", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = stall == StallPoint.DownloadBody
                        ? new StreamContent(new StallingStream(Content[..16], release))
                        : new ByteArrayContent(Content)
                };

            return Xml(FileInfoXml, stall == StallPoint.InfoBody);
        }

        private HttpResponseMessage Xml(string body, bool stalled)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            HttpContent content = stalled
                ? new StreamContent(new StallingStream(bytes[..(bytes.Length / 2)], release))
                : new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        private static HttpResponseMessage Json(string body)
            => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private Jottacloud CreateBackend(StallPoint stall, int threads, Dictionary<string, string?> timeouts)
    {
        var options = new Dictionary<string, string?>
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl,
            ["jottacloud-threads"] = threads.ToString(),
            ["jottacloud-chunksize"] = $"{ChunkSize}b"
        };
        foreach (var kv in timeouts)
            options[kv.Key] = kv.Value;

        return new Jottacloud("jottacloud://Backup", options, new HttpClient(new StallingHandler(stall, m_release.Token)));
    }

    /// <summary>
    /// Runs the call on the thread pool, so a read that blocks synchronously cannot block the
    /// test itself, and returns what it threw
    /// </summary>
    private static async Task<Exception?> RunWithGuard(Func<Task> call)
    {
        var task = Task.Run(call);
        var winner = await Task.WhenAny(task, Task.Delay(Guard)).ConfigureAwait(false);
        Assert.AreSame(task, winner, $"The call was still waiting after {Guard.TotalSeconds} s, so no timeout ended it");

        try
        {
            await task.ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static readonly Dictionary<string, string?> ShortReadWriteTimeout = new() { ["read-write-timeout"] = "1s" };

    [Test]
    public async Task AListingThatStallsTimesOut_Async()
    {
        using var backend = CreateBackend(StallPoint.ListBody, 1, ShortReadWriteTimeout);

        var error = await RunWithGuard(async () => { await foreach (var _ in backend.ListAsync(CancellationToken.None)) { } });

        NUnit.Framework.Assert.That(error, Is.InstanceOf<TimeoutException>());
    }

    [Test]
    public async Task ADownloadThatStallsTimesOut_Async()
    {
        using var backend = CreateBackend(StallPoint.DownloadBody, 1, ShortReadWriteTimeout);
        using var target = new MemoryStream();

        var error = await RunWithGuard(() => backend.GetAsync(RemoteName, target, CancellationToken.None));

        NUnit.Framework.Assert.That(error, Is.InstanceOf<TimeoutException>());
    }

    [Test]
    public async Task AFileInfoThatStallsBeforeAParallelDownloadTimesOut_Async()
    {
        using var backend = CreateBackend(StallPoint.InfoBody, Chunks, ShortReadWriteTimeout);
        using var target = new MemoryStream();

        var error = await RunWithGuard(() => backend.GetAsync(RemoteName, target, CancellationToken.None));

        NUnit.Framework.Assert.That(error, Is.InstanceOf<TimeoutException>());
    }

    [Test]
    public async Task AChunkBodyThatStallsTimesOut_Async()
    {
        using var backend = CreateBackend(StallPoint.ChunkBody, Chunks, ShortReadWriteTimeout);
        using var target = new MemoryStream();

        var error = await RunWithGuard(() => backend.GetAsync(RemoteName, target, CancellationToken.None));

        NUnit.Framework.Assert.That(error, Is.InstanceOf<TimeoutException>());
    }

    [Test]
    public async Task AChunkWhoseHeadersNeverArriveTimesOut_Async()
    {
        using var backend = CreateBackend(StallPoint.ChunkHeaders, Chunks, new() { ["short-timeout"] = "1s" });
        using var target = new MemoryStream();

        var error = await RunWithGuard(() => backend.GetAsync(RemoteName, target, CancellationToken.None));

        NUnit.Framework.Assert.That(error, Is.InstanceOf<TimeoutException>());
    }

    /// <summary>
    /// The timeouts are activity timeouts, so a server that answers in full still succeeds
    /// </summary>
    [TestCase(1)]
    [TestCase(Chunks)]
    public async Task ADownloadThatDoesNotStallStillSucceeds_Async(int threads)
    {
        using var backend = CreateBackend(StallPoint.None, threads, ShortReadWriteTimeout);
        using var target = new MemoryStream();

        var error = await RunWithGuard(() => backend.GetAsync(RemoteName, target, CancellationToken.None));

        NUnit.Framework.Assert.That(error, Is.Null);
        NUnit.Framework.Assert.That(target.ToArray(), Is.EqualTo(Content));
    }
}
