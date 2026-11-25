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
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Renci.SshNet;

namespace Duplicati.UnitTest;

/// <summary>
/// The read-write timeout of an upload was only noticed on the next read of the source, and a
/// transfer that does not move makes no next read, so the upload was waited for forever
/// </summary>
[TestFixture]
[Category("Backend")]
public class StalledUploadTests
{
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The longest a test waits, so that an upload that never ends fails the test instead of hanging it
    /// </summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

    private static async Task<Exception?> WaitGuardedAsync(Task task)
    {
        if (await Task.WhenAny(task, Task.Delay(Guard)) != task)
            Assert.Fail($"The upload did not end within {Guard.TotalSeconds} seconds");

        try
        {
            await task;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>
    /// An upload that reads some of the source, and then waits until it is cancelled
    /// </summary>
    private static async Task StallAfterAsync(Stream source, int count, CancellationToken token)
    {
        await source.ReadExactlyAsync(new byte[count], token);
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
    }

    [Test]
    [TestCase(0)]
    [TestCase(500)]
    public async Task AnUploadThatStopsReadingTimesOut(int read)
    {
        using var source = new MemoryStream(new byte[1000]);
        var ex = await WaitGuardedAsync(Utility.WithUploadTimeout(source, Idle, CancellationToken.None, (s, ct) => StallAfterAsync(s, read, ct)));
        Assert.That(ex, Is.InstanceOf<TimeoutException>());
        Assert.That(source.CanRead, Is.True, "The source belongs to the caller");
    }

    [Test]
    public async Task AnUploadThatIsReadSlowlyIsNotCutOff()
    {
        using var source = new MemoryStream(new byte[8]);
        var ex = await WaitGuardedAsync(Utility.WithUploadTimeout(source, Idle, CancellationToken.None, async (s, ct) =>
        {
            for (var i = 0; i < 8; i++)
            {
                await Task.Delay(400, ct);
                await s.ReadExactlyAsync(new byte[1], ct);
            }
        }));
        Assert.That(ex, Is.Null);
    }

    /// <summary>
    /// After the last read the data is still on its way, or the server is storing it, and nothing tells whether it moves
    /// </summary>
    [Test]
    public async Task AnUploadThatIsReadToTheEndHasLongerToFinish()
    {
        using var source = new MemoryStream(new byte[1000]);
        var ex = await WaitGuardedAsync(Utility.WithUploadTimeout(source, Idle, CancellationToken.None, async (s, ct) =>
        {
            await s.CopyToAsync(Stream.Null, ct);
            await Task.Delay(Idle * 4, ct);
        }));
        Assert.That(ex, Is.Null);
    }

    [Test]
    public async Task AnUploadThatIsNeverAnsweredTimesOut()
    {
        using var source = new MemoryStream(new byte[1000]);
        var sw = Stopwatch.StartNew();
        var ex = await WaitGuardedAsync(Utility.WithUploadTimeout(source, Idle, CancellationToken.None, (s, ct) => StallAfterAsync(s, 1000, ct), answerTimeout: Idle * 3));
        Assert.That(ex, Is.InstanceOf<TimeoutException>());
        Assert.That(sw.Elapsed, Is.GreaterThan(Idle * 2.9));
    }

    [Test]
    public async Task ACancelledUploadIsNotATimeout()
    {
        using var source = new MemoryStream(new byte[1000]);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var ex = await WaitGuardedAsync(Utility.WithUploadTimeout(source, Idle, cts.Token, (s, ct) => StallAfterAsync(s, 500, ct)));
        Assert.That(ex, Is.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task AnUploadThatFailsPassesItsErrorOn()
    {
        using var source = new MemoryStream(new byte[1000]);
        var ex = await WaitGuardedAsync(Utility.WithUploadTimeout(source, Idle, CancellationToken.None, (_, _) => throw new InvalidOperationException("failed")));
        Assert.That(ex, Is.InstanceOf<InvalidOperationException>());
    }

    /// <summary>
    /// An S3 server that stops reading an uploaded object after the headers
    /// </summary>
    private sealed class StallingS3 : IDisposable
    {
        private readonly TcpListener m_listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource m_stop = new();

        /// <summary>
        /// Set when an upload has reached the server
        /// </summary>
        public TaskCompletionSource Uploading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StallingS3()
        {
            m_listener.Start();
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    var client = await m_listener.AcceptTcpClientAsync(m_stop.Token).ConfigureAwait(false);
                    _ = Task.Run(() => ServeAsync(client));
                }
            });
        }

        public int Port => ((IPEndPoint)m_listener.LocalEndpoint).Port;

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                var headers = new StringBuilder();
                var one = new byte[1];
                while (!headers.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    if (await stream.ReadAsync(one, m_stop.Token).ConfigureAwait(false) == 0)
                        return;
                    headers.Append((char)one[0]);
                }

                var text = headers.ToString();
                if (text.StartsWith("PUT", StringComparison.Ordinal))
                {
                    if (text.Contains("100-continue", StringComparison.OrdinalIgnoreCase))
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"), m_stop.Token).ConfigureAwait(false);
                    Uploading.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, m_stop.Token).ConfigureAwait(false);
                }

                var body = text.StartsWith("POST", StringComparison.Ordinal)
                    ? "<?xml version=\"1.0\" encoding=\"UTF-8\"?><InitiateMultipartUploadResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\"><Bucket>test-bucket</Bucket><Key>pfx/a.txt</Key><UploadId>1</UploadId></InitiateMultipartUploadResult>"
                    : "<?xml version=\"1.0\" encoding=\"UTF-8\"?><LocationConstraint xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">us-east-1</LocationConstraint>";
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/xml\r\nConnection: close\r\nContent-Length: {body.Length}\r\n\r\n{body}"), m_stop.Token).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            m_stop.Cancel();
            m_listener.Stop();
        }
    }

    [Test]
    [TestCase("aws")]
    [TestCase("minio")]
    public async Task AnS3UploadThatStallsTimesOut(string client)
    {
        using var server = new StallingS3();
        using var backend = new S3("s3://test-bucket/pfx", new Dictionary<string, string?>
        {
            ["aws-access-key-id"] = "test-access-key",
            ["aws-secret-access-key"] = "test-secret-key",
            ["s3-server-name"] = $"127.0.0.1:{server.Port}",
            ["use-ssl"] = "false",
            ["s3-client"] = client,
            ["read-write-timeout"] = "2s"
        });

        // More than the socket buffers hold, and for Minio, which reads a whole part into memory, more than one part
        using var data = new MemoryStream(new byte[64 * 1024 * 1024]);
        var upload = backend.PutAsync("a.txt", data, CancellationToken.None);
        var ex = await WaitGuardedAsync(upload);

        Assert.That(server.Uploading.Task.IsCompleted, Is.True, "The upload did not reach the server");
        Assert.That(ex, Is.InstanceOf<TimeoutException>());
    }

    private static SSHv2 CreateSsh()
        => new("ssh://127.0.0.1/path", new Dictionary<string, string?>
        {
            ["auth-username"] = "user",
            ["auth-password"] = "password",
            ["read-write-timeout"] = "1s"
        });

    /// <summary>
    /// SSH.NET takes no token for a transfer, and waits without a limit for the server to take more data
    /// </summary>
    [Test]
    public async Task AnSshTransferThatStallsIsLeftBehind()
    {
        using var backend = CreateSsh();
        using var stuck = new SftpClient("127.0.0.1", "user", "password");
        using var cts = new CancellationTokenSource(Idle);

        var ex = await WaitGuardedAsync(backend.WaitForTransferAsync(stuck, new TaskCompletionSource().Task, cts.Token));

        Assert.That(ex, Is.InstanceOf<OperationCanceledException>());
        Assert.That(() => stuck.IsConnected, Throws.InstanceOf<ObjectDisposedException>().After(5000, 50), "The connection should have been closed");
    }

    [Test]
    public async Task AnSshTransferThatFailsPassesItsErrorOn()
    {
        using var backend = CreateSsh();
        using var client = new SftpClient("127.0.0.1", "user", "password");

        var ex = await WaitGuardedAsync(backend.WaitForTransferAsync(client, Task.FromException(new InvalidOperationException("failed")), CancellationToken.None));

        Assert.That(ex, Is.InstanceOf<InvalidOperationException>());
        Assert.That(() => client.IsConnected, Is.False, "The connection should not have been closed");
    }
}
