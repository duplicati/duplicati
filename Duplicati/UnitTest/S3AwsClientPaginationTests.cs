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
/// Checks that the AWS S3 client requests every page of a truncated bucket listing
/// </summary>
[TestFixture]
public class S3AwsClientPaginationTests
{
    /// <summary>
    /// Serves a two-page listing of pfx/a.txt and pfx/b.txt and records the query of every request
    /// </summary>
    private sealed class PagedFakeS3 : IDisposable
    {
        /// <summary>
        /// Stops a client that keeps asking for more pages
        /// </summary>
        private const int MaxRequests = 5;

        private readonly HttpListener _listener = new();
        private readonly Task _loop;
        private readonly bool _sendNextMarker;

        public int Port { get; }
        public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

        public PagedFakeS3(bool sendNextMarker)
        {
            _sendNextMarker = sendNextMarker;
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

                var query = context.Request.QueryString;
                var request = new RecordedRequest(
                    context.Request.RawUrl ?? "",
                    query["list-type"],
                    query["continuation-token"],
                    query["marker"]);
                Requests.Enqueue(request);

                string body;
                if (Requests.Count > MaxRequests)
                    body = Page(null, false, null);
                else if (request.ListType == "2")
                    body = request.ContinuationToken == null
                        ? Page("pfx/a.txt", true, "<NextContinuationToken>token-1</NextContinuationToken>")
                        : request.ContinuationToken == "token-1"
                            ? Page("pfx/b.txt", false, null)
                            : Page(null, false, null);
                else
                    body = request.Marker == null
                        ? Page("pfx/a.txt", true, _sendNextMarker ? "<NextMarker>pfx/a.txt</NextMarker>" : null)
                        : request.Marker == "pfx/a.txt"
                            ? Page("pfx/b.txt", false, null)
                            : Page(null, false, null);

                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.ContentType = "application/xml";
                context.Response.StatusCode = 200;
                await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
                context.Response.Close();
            }
        }

        private static string Page(string? key, bool truncated, string? next)
            => "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">" +
                "<Name>test-bucket</Name><Prefix>pfx/</Prefix><MaxKeys>1000</MaxKeys>" +
                $"<KeyCount>{(key == null ? 0 : 1)}</KeyCount>" +
                $"<IsTruncated>{(truncated ? "true" : "false")}</IsTruncated>" +
                (next ?? "") +
                (key == null ? "" :
                    $"<Contents><Key>{key}</Key><LastModified>2026-01-01T00:00:00.000Z</LastModified>" +
                    "<ETag>\"d41d8cd98f00b204e9800998ecf8427e\"</ETag><Size>1</Size><StorageClass>STANDARD</StorageClass></Contents>") +
                "</ListBucketResult>";

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
            try { _loop.Wait(TimeSpan.FromSeconds(5)); } catch { }
        }
    }

    private sealed record RecordedRequest(string RawUrl, string? ListType, string? ContinuationToken, string? Marker);

    private static async Task<(List<string> Names, List<RecordedRequest> Requests)> ListAsync(bool sendNextMarker, Dictionary<string, string?> extraOptions)
    {
        using var server = new PagedFakeS3(sendNextMarker);
        var options = new Dictionary<string, string?>
        {
            ["aws-access-key-id"] = "test-access-key",
            ["aws-secret-access-key"] = "test-secret-key",
            ["s3-server-name"] = $"localhost:{server.Port}",
            ["use-ssl"] = "false",
            ["s3-client"] = "aws"
        };
        foreach (var kv in extraOptions)
            options[kv.Key] = kv.Value;

        List<string> names;
        using (var backend = new S3("s3://test-bucket/pfx", options))
            names = (await backend.ListAsync(CancellationToken.None).ToListAsync().ConfigureAwait(false)).Select(x => x.Name).ToList();

        return (names, server.Requests.ToList());
    }

    private static string Describe(List<RecordedRequest> requests)
        => string.Join(Environment.NewLine, requests.Select(x => x.RawUrl));

    [Test]
    [Category("Backend")]
    public async Task V2ListFollowsContinuationToken()
    {
        var (names, requests) = await ListAsync(false, new() { ["s3-list-api-version"] = "v2" }).ConfigureAwait(false);

        Assert.That(requests, Has.Count.EqualTo(2), Describe(requests));
        Assert.That(requests.Select(x => x.ListType), Is.All.EqualTo("2"), Describe(requests));
        Assert.That(requests[0].ContinuationToken, Is.Null, Describe(requests));
        Assert.That(requests[1].ContinuationToken, Is.EqualTo("token-1"), Describe(requests));
        Assert.That(names, Is.EqualTo(new[] { "a.txt", "b.txt" }));
    }

    [Test]
    [Category("Backend")]
    public async Task V1ListFollowsNextMarker()
    {
        var (names, requests) = await ListAsync(true, new()).ConfigureAwait(false);

        Assert.That(requests, Has.Count.EqualTo(2), Describe(requests));
        Assert.That(requests.Select(x => x.ListType), Is.All.Null, Describe(requests));
        Assert.That(requests[0].Marker, Is.Null, Describe(requests));
        Assert.That(requests[1].Marker, Is.EqualTo("pfx/a.txt"), Describe(requests));
        Assert.That(names, Is.EqualTo(new[] { "a.txt", "b.txt" }));
    }

    [Test]
    [Category("Backend")]
    public async Task V1RecursiveListContinuesAfterLastKeyWithoutNextMarker()
    {
        // S3 only returns NextMarker when a delimiter is given, and a recursive list sends none
        var (names, requests) = await ListAsync(false, new() { ["s3-recursive-list"] = "true" }).ConfigureAwait(false);

        Assert.That(requests, Has.Count.EqualTo(2), Describe(requests));
        Assert.That(requests[1].Marker, Is.EqualTo("pfx/a.txt"), Describe(requests));
        Assert.That(names, Is.EqualTo(new[] { "a.txt", "b.txt" }));
    }
}
