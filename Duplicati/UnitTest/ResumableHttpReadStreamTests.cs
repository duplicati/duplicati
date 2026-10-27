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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests the stream that reads an HTTP body straight from the connection and resumes it
/// with range requests when the connection fails
/// </summary>
[TestFixture]
public class ResumableHttpReadStreamTests
{
    /// <summary>
    /// A body stream that yields the bytes of a range, and then either ends, fails, or
    /// stalls, as the test scripts it
    /// </summary>
    private sealed class ScriptedBody(byte[] data, long from, long serveUntil, string? failWith, bool stallAfter) : Stream
    {
        private long _next = from;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_next >= serveUntil)
            {
                if (failWith != null)
                    throw new IOException(failWith);
                if (stallAfter)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return 0;
                }
                return 0;
            }

            // Serve in small pieces, so a failure lands in the middle of a read sequence
            var count = (int)Math.Min(buffer.Length, Math.Min(7, serveUntil - _next));
            new ReadOnlyMemory<byte>(data, (int)_next, count).CopyTo(buffer);
            _next += count;
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();

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
    /// Content that serves a scripted body with the headers of a real response
    /// </summary>
    private sealed class ScriptedContent(Stream body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => body.CopyToAsync(stream);

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult(body);

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    /// <summary>
    /// What one request answered with: the status and how much of the body it served
    /// before ending, failing or stalling
    /// </summary>
    private sealed record Answer(HttpStatusCode Status, long ServeUntil, string? FailWith = null, bool Stall = false, bool IgnoreRange = false, bool StateLength = true);

    /// <summary>
    /// A server that answers each request from a script and records the requests it saw
    /// </summary>
    private sealed class Server(byte[] data, params Answer[] answers)
    {
        public readonly List<long> Offsets = [];
        private int _calls;

        public Task<HttpResponseMessage> OpenAsync(long offset, CancellationToken cancellationToken)
        {
            Offsets.Add(offset);
            var answer = answers[Math.Min(_calls++, answers.Length - 1)];

            if (!((int)answer.Status >= 200 && (int)answer.Status < 300))
            {
                var failed = new HttpResponseMessage(answer.Status) { Content = new StringContent("refused") };
                if (answer.Status == HttpStatusCode.NotFound)
                    throw new HttpRequestException("not found", null, answer.Status);
                return Task.FromResult(failed);
            }

            var from = answer.IgnoreRange ? 0 : offset;
            var status = offset > 0 && !answer.IgnoreRange ? HttpStatusCode.PartialContent : HttpStatusCode.OK;
            var body = new ScriptedBody(data, from, Math.Min(answer.ServeUntil, data.Length), answer.FailWith, answer.Stall);
            var response = new HttpResponseMessage(status) { Content = new ScriptedContent(body) };
            if (answer.StateLength)
            {
                response.Content.Headers.ContentLength = data.Length - from;
                if (status == HttpStatusCode.PartialContent)
                    response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, data.Length - 1, data.Length);
            }

            return Task.FromResult(response);
        }
    }

    private static byte[] Data(int length)
    {
        var rng = new Random(42);
        var data = new byte[length];
        rng.NextBytes(data);
        return data;
    }

    private static TimeSpan NoDelay(int attempt) => TimeSpan.Zero;

    private static async Task<byte[]> ReadAllAsync(Stream stream, int chunk = 13)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[chunk];
        int read;
        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            ms.Write(buffer, 0, read);
        return ms.ToArray();
    }

    [Test]
    [Category("Utility")]
    public async Task ReadsWholeBodyWithOneRequest()
    {
        var data = Data(100);
        var server = new Server(data, new Answer(HttpStatusCode.OK, long.MaxValue));
        using var stream = new ResumableHttpReadStream(server.OpenAsync, null, TimeSpan.FromSeconds(5), "the body", resumeDelay: NoDelay);

        await stream.EnsureOpenedAsync(CancellationToken.None);
        Assert.IsTrue(stream.HasKnownLength);
        Assert.AreEqual(100, stream.Length);

        var read = await ReadAllAsync(stream);
        Assert.AreEqual(data, read);
        Assert.AreEqual(new[] { 0L }, server.Offsets);
        Assert.AreEqual(100, stream.Position);
    }

    [Test]
    [Category("Utility")]
    public async Task ResumesFromWhereTheConnectionFailed()
    {
        var data = Data(100);
        var server = new Server(data,
            new Answer(HttpStatusCode.OK, 40, FailWith: "connection reset"),
            new Answer(HttpStatusCode.OK, long.MaxValue));
        using var stream = new ResumableHttpReadStream(server.OpenAsync, null, TimeSpan.FromSeconds(5), "the body", resumeDelay: NoDelay);

        var read = await ReadAllAsync(stream);
        Assert.AreEqual(data, read);

        // The bytes handed out before the failure are not asked for again
        Assert.AreEqual(2, server.Offsets.Count);
        Assert.AreEqual(0, server.Offsets[0]);
        Assert.That(server.Offsets[1], Is.GreaterThan(0).And.LessThanOrEqualTo(40));
    }

    [Test]
    [Category("Utility")]
    public async Task ResumesWhenTheBodyEndsEarly()
    {
        var data = Data(100);
        var server = new Server(data,
            new Answer(HttpStatusCode.OK, 60),
            new Answer(HttpStatusCode.OK, long.MaxValue));
        using var stream = new ResumableHttpReadStream(server.OpenAsync, null, TimeSpan.FromSeconds(5), "the body", resumeDelay: NoDelay);

        var read = await ReadAllAsync(stream);
        Assert.AreEqual(data, read);
        Assert.AreEqual(new[] { 0L, 60L }, server.Offsets);
    }

    [Test]
    [Category("Utility")]
    public async Task ResumesWhenTheConnectionStalls()
    {
        var data = Data(100);
        var server = new Server(data,
            new Answer(HttpStatusCode.OK, 30, Stall: true),
            new Answer(HttpStatusCode.OK, long.MaxValue));
        using var stream = new ResumableHttpReadStream(server.OpenAsync, null, TimeSpan.FromMilliseconds(200), "the body", resumeDelay: NoDelay);

        var read = await ReadAllAsync(stream);
        Assert.AreEqual(data, read);
        Assert.AreEqual(new[] { 0L, 30L }, server.Offsets);
    }

    [Test]
    [Category("Utility")]
    public async Task TakesTheLengthFromTheCallerWhenTheResponseStatesNone()
    {
        var data = Data(100);
        var server = new Server(data,
            new Answer(HttpStatusCode.OK, 50, StateLength: false),
            new Answer(HttpStatusCode.OK, long.MaxValue, StateLength: false));
        using var stream = new ResumableHttpReadStream(server.OpenAsync, 100, TimeSpan.FromSeconds(5), "the body", resumeDelay: NoDelay);

        Assert.IsTrue(stream.HasKnownLength);
        Assert.AreEqual(100, stream.Length);

        // The early end is recognized from the caller's length and resumed
        var read = await ReadAllAsync(stream);
        Assert.AreEqual(data, read);
        Assert.AreEqual(new[] { 0L, 50L }, server.Offsets);
    }

    [Test]
    [Category("Utility")]
    public async Task AcceptsTheEndWhenNoLengthIsKnown()
    {
        var data = Data(100);
        var server = new Server(data, new Answer(HttpStatusCode.OK, 50, StateLength: false));
        using var stream = new ResumableHttpReadStream(server.OpenAsync, null, TimeSpan.FromSeconds(5), "the body", resumeDelay: NoDelay);

        await stream.EnsureOpenedAsync(CancellationToken.None);
        Assert.IsFalse(stream.HasKnownLength);
        Assert.Throws<NotSupportedException>(() => _ = stream.Length);

        var read = await ReadAllAsync(stream);
        Assert.AreEqual(data.Take(50).ToArray(), read);
        Assert.AreEqual(new[] { 0L }, server.Offsets);
    }

    [Test]
    [Category("Utility")]
    public void FailsInsteadOfEndingEarlyWhenResumesAreExhausted()
    {
        var data = Data(100);
        var server = new Server(data, new Answer(HttpStatusCode.OK, 40, FailWith: "connection reset"));
        using var stream = new ResumableHttpReadStream(server.OpenAsync, null, TimeSpan.FromSeconds(5), "the body", maxResumeAttempts: 3, resumeDelay: NoDelay);

        var ex = Assert.ThrowsAsync<IOException>(async () => await ReadAllAsync(stream));
        Assert.IsTrue(ex!.Message.Contains("given up"), ex.Message);
        // The first request and three resumes
        Assert.AreEqual(4, server.Offsets.Count);
    }

    [Test]
    [Category("Utility")]
    public void FailsWhenTheServerIgnoresTheRange()
    {
        var data = Data(100);
        var server = new Server(data,
            new Answer(HttpStatusCode.OK, 40, FailWith: "connection reset"),
            new Answer(HttpStatusCode.OK, long.MaxValue, IgnoreRange: true));
        using var stream = new ResumableHttpReadStream(server.OpenAsync, null, TimeSpan.FromSeconds(5), "the body", maxResumeAttempts: 1, resumeDelay: NoDelay);

        // A refused resume is final: it is not retried, and it is reported as an I/O failure
        var ex = Assert.CatchAsync<IOException>(async () => await ReadAllAsync(stream));
        Assert.IsTrue(ex!.Message.Contains("not honored"), ex.Message);
        Assert.AreEqual(2, server.Offsets.Count);
    }

    [Test]
    [Category("Utility")]
    public void ReportsARefusedRequestWhenOpened()
    {
        var data = Data(100);
        var server = new Server(data, new Answer(HttpStatusCode.NotFound, 0));
        using var stream = new ResumableHttpReadStream(server.OpenAsync, null, TimeSpan.FromSeconds(5), "the body", resumeDelay: NoDelay);

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await stream.EnsureOpenedAsync(CancellationToken.None));
        Assert.AreEqual(HttpStatusCode.NotFound, ex!.StatusCode);
        // A refusal is the server's answer, not a connection failure, so it is not retried
        Assert.AreEqual(1, server.Offsets.Count);
    }

    [Test]
    [Category("Utility")]
    public void FailsWhenAResumeIsRefused()
    {
        var data = Data(100);
        var server = new Server(data,
            new Answer(HttpStatusCode.OK, 40, FailWith: "connection reset"),
            new Answer(HttpStatusCode.NotFound, 0));
        using var stream = new ResumableHttpReadStream(server.OpenAsync, null, TimeSpan.FromSeconds(5), "the body", resumeDelay: NoDelay);

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await ReadAllAsync(stream));
        Assert.AreEqual(HttpStatusCode.NotFound, ex!.StatusCode);
    }

    [Test]
    [Category("Utility")]
    public void DoesNotResumeWhenTheCallerCancels()
    {
        var data = Data(100);
        var server = new Server(data, new Answer(HttpStatusCode.OK, 30, Stall: true));
        using var stream = new ResumableHttpReadStream(server.OpenAsync, null, Timeout.InfiniteTimeSpan, "the body", resumeDelay: NoDelay);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            var buffer = new byte[64];
            while (await stream.ReadAsync(buffer, cts.Token) > 0) { }
        });
        Assert.AreEqual(1, server.Offsets.Count);
    }

    [Test]
    [Category("Utility")]
    public async Task SynchronousReadWorks()
    {
        var data = Data(100);
        var server = new Server(data,
            new Answer(HttpStatusCode.OK, 40, FailWith: "connection reset"),
            new Answer(HttpStatusCode.OK, long.MaxValue));
        using var stream = new ResumableHttpReadStream(server.OpenAsync, null, TimeSpan.FromSeconds(5), "the body", resumeDelay: NoDelay);

        using var ms = new MemoryStream();
        await Task.Run(() => stream.CopyTo(ms, 11));
        Assert.AreEqual(data, ms.ToArray());
    }
}
