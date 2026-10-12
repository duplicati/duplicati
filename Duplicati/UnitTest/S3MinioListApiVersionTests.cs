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
using System.Linq;
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
/// Checks that --s3-list-api-version selects the list request the Minio client sends
/// </summary>
[TestFixture]
public class S3MinioListApiVersionTests
{
    /// <summary>
    /// Records the URL of every request and answers each with an empty bucket listing
    /// </summary>
    private sealed class FakeS3 : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;

        public int Port { get; }
        public ConcurrentQueue<string> RequestUrls { get; } = new();

        public FakeS3()
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

                RequestUrls.Enqueue($"{context.Request.HttpMethod} {context.Request.RawUrl}");
                var bytes = Encoding.UTF8.GetBytes(
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "<ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">" +
                    "<Name>test-bucket</Name><Prefix>pfx/</Prefix><KeyCount>0</KeyCount><MaxKeys>1000</MaxKeys>" +
                    "<IsTruncated>false</IsTruncated></ListBucketResult>");
                context.Response.ContentType = "application/xml";
                context.Response.StatusCode = 200;
                // The bucket existence check is a HEAD request, which must not carry a body
                if (context.Request.HttpMethod != "HEAD")
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

    private static async Task<string> ListAndGetListRequestUrlAsync(Dictionary<string, string?> extraOptions)
    {
        using var server = new FakeS3();
        var options = new Dictionary<string, string?>
        {
            ["aws-access-key-id"] = "test-access-key",
            ["aws-secret-access-key"] = "test-secret-key",
            ["s3-server-name"] = $"localhost:{server.Port}",
            ["use-ssl"] = "false",
            ["s3-client"] = "minio",
            ["s3-location-constraint"] = "us-east-1"
        };
        foreach (var kv in extraOptions)
            options[kv.Key] = kv.Value;

        using (var backend = new S3("s3://test-bucket/pfx", options))
            await backend.ListAsync(CancellationToken.None).ToListAsync().ConfigureAwait(false);

        var all = string.Join(Environment.NewLine, server.RequestUrls);
        var listRequests = server.RequestUrls.Where(x => x.StartsWith("GET ") && x.Contains("prefix=")).ToList();
        Assert.That(listRequests, Has.Count.EqualTo(1), all);
        return listRequests.Single();
    }

    [Test]
    [Category("Backend")]
    public async Task ListUsesV1ApiWhenS3ListApiVersionIsV1()
    {
        var url = await ListAndGetListRequestUrlAsync(new() { ["s3-list-api-version"] = "v1" }).ConfigureAwait(false);

        Assert.That(url, Does.Not.Contain("list-type=2"));
    }

    [Test]
    [Category("Backend")]
    public async Task ListUsesV2ApiWhenS3ListApiVersionIsV2()
    {
        var url = await ListAndGetListRequestUrlAsync(new() { ["s3-list-api-version"] = "v2" }).ConfigureAwait(false);

        Assert.That(url, Does.Contain("list-type=2"));
    }

    [Test]
    [Category("Backend")]
    public async Task ListUsesV1ApiWhenLegacyListApiVersionIsV1()
    {
        var url = await ListAndGetListRequestUrlAsync(new() { ["list-api-version"] = "v1" }).ConfigureAwait(false);

        Assert.That(url, Does.Not.Contain("list-type=2"));
    }

    [Test]
    [Category("Backend")]
    public async Task ListKeepsMinioDefaultV2ApiWithoutS3ListApiVersion()
    {
        var url = await ListAndGetListRequestUrlAsync(new()).ConfigureAwait(false);

        Assert.That(url, Does.Contain("list-type=2"));
    }
}
