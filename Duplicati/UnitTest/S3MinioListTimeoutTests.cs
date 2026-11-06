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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Utility.Options;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The list timeout must cover the listing itself. The MinIO client returns an observable at
/// once and sends the list requests only when it is read, so a timeout around creating the
/// observable does not limit a server that stops answering a list request.
/// The server is a local socket that answers the bucket check, and either answers a listing
/// with one file or never answers it.
/// </summary>
public class S3MinioListTimeoutTests : BasicSetupHelper
{
    private const string ListResult = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\"><Name>bucket</Name><Prefix>folder/</Prefix><KeyCount>1</KeyCount><MaxKeys>1000</MaxKeys><IsTruncated>false</IsTruncated><Contents><Key>folder/file</Key><LastModified>2026-01-01T00:00:00.000Z</LastModified><ETag>&quot;x&quot;</ETag><Size>3</Size><StorageClass>STANDARD</StorageClass></Contents></ListBucketResult>";

    private static async Task ServeAsync(TcpListener listener, bool answerList, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var socket = await listener.AcceptSocketAsync(token).ConfigureAwait(false);
            _ = Task.Run(async () =>
            {
                using var _ = socket;
                var buffer = new byte[16 * 1024];
                var request = new StringBuilder();
                while (!token.IsCancellationRequested)
                {
                    var read = await socket.ReceiveAsync(buffer, SocketFlags.None, token).ConfigureAwait(false);
                    if (read == 0)
                        return;
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    var text = request.ToString();
                    var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (end < 0)
                        continue;
                    request.Remove(0, end + 4);

                    if (text.StartsWith("HEAD ", StringComparison.Ordinal))
                    {
                        await socket.SendAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"), SocketFlags.None, token).ConfigureAwait(false);
                    }
                    else if (answerList)
                    {
                        var body = Encoding.UTF8.GetBytes(ListResult);
                        await socket.SendAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/xml\r\nContent-Length: {body.Length}\r\n\r\n"), SocketFlags.None, token).ConfigureAwait(false);
                        await socket.SendAsync(body, SocketFlags.None, token).ConfigureAwait(false);
                    }
                }
            }, token);
        }
    }

    private static Task<List<Library.Interface.IFileEntry>> ListAsync(bool answerList, CancellationToken stop)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        stop.Register(listener.Stop);
        _ = ServeAsync(listener, answerList, stop);

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var client = new S3MinioClient("key", "secret", "us-east-1", $"127.0.0.1:{port}", null, false,
            new TimeoutOptionsHelper.Timeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30)),
            new Dictionary<string, string?>(), "", null);
        return client.ListBucketAsync("bucket", "folder", false, CancellationToken.None).ToListAsync().AsTask();
    }

    [Test]
    [Category("Utility")]
    public void ListStopsAtTheListTimeout()
    {
        using var stop = new CancellationTokenSource();
        try
        {
            var list = ListAsync(false, stop.Token);
            Assert.That(Task.WhenAny(list, Task.Delay(TimeSpan.FromSeconds(15))).Result, Is.SameAs(list), "The listing must stop at the list timeout");
            Assert.ThrowsAsync<TimeoutException>(() => list);
        }
        finally
        {
            stop.Cancel();
        }
    }

    [Test]
    [Category("Utility")]
    public void ListReturnsTheFiles()
    {
        using var stop = new CancellationTokenSource();
        try
        {
            var files = ListAsync(true, stop.Token).Result;
            Assert.That(files.Select(x => x.Name), Is.EqualTo(new[] { "file" }));
        }
        finally
        {
            stop.Cancel();
        }
    }
}
