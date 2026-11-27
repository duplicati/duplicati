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
/// The Minio client failed with "Object reference not set to an instance of an object." when the
/// server answered an upload with an error that is not an S3 error document, such as the HTML page
/// nginx answers with when an upload is too large (#3471)
/// </summary>
[TestFixture]
[Category("Backend")]
public class S3ErrorAnswerTests
{
    private const string NginxTooLarge = "<html>\r\n<head><title>413 Request Entity Too Large</title></head>\r\n<body>\r\n<center><h1>413 Request Entity Too Large</h1></center>\r\n<hr><center>nginx</center>\r\n</body>\r\n</html>\r\n";

    /// <summary>
    /// A server that answers every upload with the given error, and everything else with an empty success
    /// </summary>
    private sealed class FailingS3 : IDisposable
    {
        private readonly HttpListener m_listener = new();

        public int Port { get; }

        public FailingS3(HttpStatusCode status, string contentType, string body)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            m_listener.Prefixes.Add($"http://localhost:{Port}/");
            m_listener.Start();
            _ = Task.Run(async () =>
            {
                while (m_listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await m_listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch (Exception) when (!m_listener.IsListening)
                    {
                        return;
                    }

                    await context.Request.InputStream.CopyToAsync(Stream.Null).ConfigureAwait(false);
                    var reply = "";
                    if (context.Request.HttpMethod == "PUT")
                    {
                        context.Response.StatusCode = (int)status;
                        context.Response.ContentType = contentType;
                        reply = body;
                    }
                    else if (context.Request.Url?.Query.Contains("location") == true)
                    {
                        reply = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><LocationConstraint xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">us-east-1</LocationConstraint>";
                    }

                    await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(reply)).ConfigureAwait(false);
                    context.Response.Close();
                }
            });
        }

        public void Dispose()
        {
            m_listener.Stop();
            m_listener.Close();
        }
    }

    private static Exception? Upload(string client, FailingS3 server)
    {
        using var backend = new S3("s3://test-bucket/pfx", new Dictionary<string, string?>
        {
            ["aws-access-key-id"] = "test-access-key",
            ["aws-secret-access-key"] = "test-secret-key",
            ["s3-server-name"] = $"localhost:{server.Port}",
            ["use-ssl"] = "false",
            ["s3-client"] = client
        });

        using var data = new MemoryStream(new byte[1000]);
        return Assert.CatchAsync<Exception>(() => backend.PutAsync("a.txt", data, CancellationToken.None));
    }

    [Test]
    [TestCase("aws")]
    [TestCase("minio")]
    public void AnUploadThatIsTooLargeForAProxyTellsWhy(string client)
    {
        using var server = new FailingS3(HttpStatusCode.RequestEntityTooLarge, "text/html", NginxTooLarge);

        var ex = Upload(client, server);

        Assert.That(ex, Is.Not.InstanceOf<NullReferenceException>(), ex?.ToString());
        Assert.That(ex!.Message, Does.Contain("RequestEntityTooLarge"), ex.ToString());
    }

    [Test]
    public void TheMinioErrorHoldsTheStartOfTheAnswer()
    {
        using var server = new FailingS3(HttpStatusCode.RequestEntityTooLarge, "text/html", NginxTooLarge);

        var ex = Upload("minio", server);

        Assert.That(ex!.Message, Does.Contain("413 Request Entity Too Large").And.Contain("nginx"), ex.ToString());
        Assert.That(ex.Message, Does.Not.Contain("<"), "The markup should be left out");
    }

    [Test]
    [TestCase("aws")]
    [TestCase("minio")]
    public void AnS3ErrorDocumentIsReadAsBefore(string client)
    {
        using var server = new FailingS3(HttpStatusCode.Forbidden, "application/xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Error><Code>AccessDenied</Code><Message>Access Denied by the test</Message><RequestId>1</RequestId></Error>");

        var ex = Upload(client, server);

        Assert.That(ex!.Message, Does.Contain("Access Denied by the test"), ex.ToString());
        Assert.That(ex.Message, Does.Not.Contain("The server answered"), ex.ToString());
    }
}
