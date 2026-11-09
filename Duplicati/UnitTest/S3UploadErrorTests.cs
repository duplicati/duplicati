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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Checks that an upload the S3 server refuses fails, so it is retried and not taken as stored
/// </summary>
[TestFixture]
public class S3UploadErrorTests
{
    /// <summary>
    /// Answers every upload of an object with AccessDenied, and every other request as S3 would
    /// </summary>
    private sealed class RefusingFakeS3 : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;

        public int Port { get; }
        public ConcurrentQueue<string> Requests { get; } = new();

        public RefusingFakeS3()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            _listener.Prefixes.Add($"http://localhost:{Port}/");
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

                var request = context.Request;
                await request.InputStream.CopyToAsync(Stream.Null).ConfigureAwait(false);
                Requests.Enqueue($"{request.HttpMethod} {request.Url?.PathAndQuery}");

                string reply = "";
                if (request.HttpMethod == "PUT")
                {
                    context.Response.StatusCode = 403;
                    reply = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Error><Code>AccessDenied</Code><Message>Access Denied</Message><Resource>/test-bucket/pfx/a.txt</Resource><RequestId>1</RequestId></Error>";
                }
                else
                {
                    context.Response.StatusCode = 200;
                    if (request.Url?.Query.Contains("location") == true)
                        reply = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><LocationConstraint xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">us-east-1</LocationConstraint>";
                }

                var bytes = Encoding.UTF8.GetBytes(reply);
                if (bytes.Length > 0)
                    context.Response.ContentType = "application/xml";
                await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
            try { _loop.Wait(TimeSpan.FromSeconds(5)); } catch { }
        }
    }

    [Test]
    [Category("Backend")]
    [TestCase("aws")]
    [TestCase("minio")]
    public void ARefusedUploadFails(string client)
    {
        using var server = new RefusingFakeS3();
        var options = new Dictionary<string, string?>
        {
            ["aws-access-key-id"] = "test-access-key",
            ["aws-secret-access-key"] = "test-secret-key",
            ["s3-server-name"] = $"localhost:{server.Port}",
            ["use-ssl"] = "false",
            ["s3-client"] = client,
        };

        using var backend = new S3("s3://test-bucket/pfx", options);
        using var data = new MemoryStream(Encoding.UTF8.GetBytes("contents"));
        Assert.That(async () => await backend.PutAsync("a.txt", data, CancellationToken.None).ConfigureAwait(false),
            Throws.Exception, string.Join(Environment.NewLine, server.Requests));
        Assert.That(server.Requests, Has.Some.StartsWith("PUT /test-bucket/pfx/a.txt"), "The upload reached the server");
    }
}
