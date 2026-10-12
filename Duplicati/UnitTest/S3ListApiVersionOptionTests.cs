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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using Duplicati.Library.Utility;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Checks that --s3-list-api-version selects the list request the AWS client sends
/// </summary>
[TestFixture]
public class S3ListApiVersionOptionTests
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

                RequestUrls.Enqueue(context.Request.RawUrl ?? "");
                var bytes = Encoding.UTF8.GetBytes(
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "<ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">" +
                    "<Name>test-bucket</Name><Prefix>pfx/</Prefix><KeyCount>0</KeyCount><MaxKeys>1000</MaxKeys>" +
                    "<IsTruncated>false</IsTruncated></ListBucketResult>");
                context.Response.ContentType = "application/xml";
                context.Response.StatusCode = 200;
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

    private sealed class LogSink : ILogDestination
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public void WriteMessage(LogEntry entry)
        {
            Entries.Enqueue(entry);
        }
    }

    private static Dictionary<string, string?> CreateOptions(FakeS3 server, Dictionary<string, string?> extraOptions)
    {
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
        return options;
    }

    private static async Task<string> ListAndGetRequestUrlAsync(Dictionary<string, string?> extraOptions)
    {
        using var server = new FakeS3();
        var options = CreateOptions(server, extraOptions);

        using (var backend = new S3("s3://test-bucket/pfx", options))
            await backend.ListAsync(CancellationToken.None).ToListAsync().ConfigureAwait(false);

        Assert.That(server.RequestUrls, Has.Count.EqualTo(1), string.Join(Environment.NewLine, server.RequestUrls));
        return server.RequestUrls.Single();
    }

    [Test]
    [Category("Backend")]
    public async Task ListUsesV1ApiByDefault()
    {
        var url = await ListAndGetRequestUrlAsync(new()).ConfigureAwait(false);

        Assert.That(url, Does.StartWith("/test-bucket/"));
        Assert.That(url, Does.Not.Contain("list-type=2"));
    }

    [Test]
    [Category("Backend")]
    public async Task ListUsesV2ApiWhenS3ListApiVersionIsV2()
    {
        var url = await ListAndGetRequestUrlAsync(new() { ["s3-list-api-version"] = "v2" }).ConfigureAwait(false);

        Assert.That(url, Does.StartWith("/test-bucket/"));
        Assert.That(url, Does.Contain("list-type=2"));
    }

    [Test]
    [Category("Backend")]
    public async Task ListUsesV2ApiWhenLegacyListApiVersionIsV2()
    {
        var url = await ListAndGetRequestUrlAsync(new() { ["list-api-version"] = "v2" }).ConfigureAwait(false);

        Assert.That(url, Does.Contain("list-type=2"));
    }

    [Test]
    [Category("Backend")]
    public async Task S3ListApiVersionTakesPrecedenceOverLegacyOption()
    {
        var url = await ListAndGetRequestUrlAsync(new() { ["s3-list-api-version"] = "v1", ["list-api-version"] = "v2" }).ConfigureAwait(false);

        Assert.That(url, Does.Not.Contain("list-type=2"));
    }

    [Test]
    [Category("Backend")]
    public async Task ListRemoteWarnsThatLegacyListApiVersionIsDeprecated()
    {
        using var server = new FakeS3();
        using var tempFolder = new TempFolder();
        var options = CreateOptions(server, new() { ["list-api-version"] = "v2" })
            .ToDictionary(x => x.Key, x => x.Value ?? "");
        options["dbpath"] = Path.Combine(tempFolder, "missing.sqlite");
        options["no-encryption"] = "true";

        var sink = new LogSink();
        using (Log.StartScope(sink, LogMessageType.Warning))
        using (var controller = new Controller("s3://test-bucket/pfx", options, null))
            await controller.ListRemoteAsync().ConfigureAwait(false);

        Assert.That(server.RequestUrls.Any(x => x.Contains("list-type=2")), Is.True, string.Join(Environment.NewLine, server.RequestUrls));
        var warnings = sink.Entries.Where(x => x.Level == LogMessageType.Warning).Select(x => x.FormattedMessage).ToList();
        Assert.That(warnings, Has.Some.Contains("--list-api-version").And.Contains("s3-list-api-version"), string.Join(Environment.NewLine, warnings));
    }
}
