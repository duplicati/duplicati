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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.AliyunOSS;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The delete of the Aliyun OSS backend made a blocking call inside the timeout wrapper, so the
/// timeout did not apply, and it treated the 204 that OSS answers a delete with as a failure.
/// The requests go to a local server.
/// </summary>
[TestFixture]
[Category("Backend")]
public class AliyunOSSDeleteTests
{
    private static readonly byte[] Content = Enumerable.Range(0, 1000).Select(x => (byte)x).ToArray();

    private sealed class FakeOss : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;
        private readonly CancellationTokenSource _stop = new();

        public int Port { get; }
        /// <summary>
        /// Whether a delete is left without an answer
        /// </summary>
        public bool Stall { get; init; }
        public ConcurrentQueue<string> Deletes { get; } = new();

        public FakeOss(bool stall)
        {
            Stall = stall;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _loop = Task.Run(ServeAsync);
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception) when (!_listener.IsListening)
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    if (context.Request.HttpMethod == "DELETE")
                    {
                        Deletes.Enqueue(context.Request.Url?.AbsolutePath ?? "");
                        if (Stall)
                        {
                            try { await Task.Delay(Timeout.Infinite, _stop.Token).ConfigureAwait(false); } catch { }
                            return;
                        }
                        // What OSS answers a delete with, whether the object was there or not
                        context.Response.StatusCode = 204;
                    }
                    else if (context.Request.HttpMethod == "GET" && context.Request.Url?.AbsolutePath == "/bucket/folder/a.zip")
                    {
                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "application/octet-stream";
                        await context.Response.OutputStream.WriteAsync(Content).ConfigureAwait(false);
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                    }
                    context.Response.Close();
                });
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _listener.Close();
            try { _loop.Wait(TimeSpan.FromSeconds(5)); } catch { }
            _stop.Dispose();
        }
    }

    private static OSS CreateBackend(int port, string shortTimeout = "30s")
        => new("aliyunoss://folder", new Dictionary<string, string?>
        {
            ["oss-bucket-name"] = "bucket",
            ["oss-endpoint"] = $"http://127.0.0.1:{port}",
            ["oss-access-key-id"] = "test-id",
            ["oss-access-key-secret"] = "test-secret",
            ["short-timeout"] = shortTimeout,
        });

    [Test]
    public void ADeleteThatGetsNoAnswerTimesOut()
    {
        using var server = new FakeOss(stall: true);
        var backend = CreateBackend(server.Port, "2s");

        var elapsed = Stopwatch.StartNew();
        Assert.ThrowsAsync<TimeoutException>(() => backend.DeleteAsync("a.zip", CancellationToken.None));
        Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        Assert.That(server.Deletes, Has.Count.EqualTo(1), "The delete should have reached the server");
    }

    [Test]
    public async Task ADownloadCompletesWithoutWaitingForTheTimeout_Async()
    {
        using var server = new FakeOss(stall: false);
        var backend = CreateBackend(server.Port, "20s");

        using var target = new System.IO.MemoryStream();
        var elapsed = Stopwatch.StartNew();
        await backend.GetAsync("a.zip", target, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)), "The download waited for the timeout");
        Assert.That(target.ToArray(), Is.EqualTo(Content));
    }

    [Test]
    public async Task ADeleteAnsweredWithNoContentIsNotReportedAsFailed_Async()
    {
        using var server = new FakeOss(stall: false);
        var backend = CreateBackend(server.Port);

        var messages = new List<Library.Logging.LogEntry>();
        using (Library.Logging.Log.StartIsolatingScope(true))
        using (Library.Logging.Log.StartScope(messages.Add))
            await backend.DeleteAsync("a.zip", CancellationToken.None);

        Assert.That(server.Deletes, Is.EqualTo(new[] { "/bucket/folder/a.zip" }));
        Assert.That(messages.Where(x => x.FormattedMessage.Contains("failed", StringComparison.OrdinalIgnoreCase)), Is.Empty,
            "A successful delete was logged as failed");
    }
}
