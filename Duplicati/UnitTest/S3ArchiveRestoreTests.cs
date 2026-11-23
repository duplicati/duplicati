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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Interface;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The S3 backend reads whether an object is in archive storage from its metadata, and asks for
/// it to be restored with a restore request (#6228). The requests go to a local server.
/// </summary>
[TestFixture]
[Category("Backend")]
public class S3ArchiveRestoreTests
{
    /// <summary>
    /// A server that answers the metadata and restore requests for a few objects
    /// </summary>
    private sealed class FakeS3 : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;

        public int Port { get; }
        /// <summary>
        /// The headers the metadata request returns, by object name
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> Objects { get; } = new();
        /// <summary>
        /// The objects a restore was asked for already
        /// </summary>
        public HashSet<string> RestoreInProgress { get; } = new();
        /// <summary>
        /// The restore requests, with their bodies
        /// </summary>
        public ConcurrentQueue<(string Path, string Body)> Restores { get; } = new();

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

                var request = context.Request;
                var path = request.Url?.AbsolutePath ?? "";
                var name = path.Substring(path.LastIndexOf('/') + 1);
                using var reader = new StreamReader(request.InputStream);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                var response = context.Response;

                if (request.HttpMethod == "HEAD" && Objects.TryGetValue(name, out var headers))
                {
                    response.StatusCode = 200;
                    response.AddHeader("ETag", "\"d41d8cd98f00b204e9800998ecf8427e\"");
                    response.AddHeader("Last-Modified", "Thu, 01 Jan 2026 00:00:00 GMT");
                    foreach (var header in headers)
                        response.AddHeader(header.Key, header.Value);
                }
                else if (request.HttpMethod == "POST" && request.Url?.Query.Contains("restore") == true)
                {
                    Restores.Enqueue((path, body));
                    if (RestoreInProgress.Contains(name))
                    {
                        response.StatusCode = 409;
                        var error = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Error><Code>RestoreAlreadyInProgress</Code><Message>Object restore is already in progress</Message></Error>");
                        response.ContentType = "application/xml";
                        await response.OutputStream.WriteAsync(error).ConfigureAwait(false);
                    }
                    else
                    {
                        response.StatusCode = 202;
                    }
                }
                else
                {
                    response.StatusCode = 404;
                }

                response.Close();
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
            try { _loop.Wait(TimeSpan.FromSeconds(5)); } catch { }
        }
    }

    private static S3 CreateBackend(int port, Dictionary<string, string?>? extra = null)
    {
        var options = new Dictionary<string, string?>
        {
            ["aws-access-key-id"] = "test-access-key",
            ["aws-secret-access-key"] = "test-secret-key",
            ["s3-server-name"] = $"localhost:{port}",
            ["use-ssl"] = "false",
            ["s3-client"] = "aws",
            ["number-of-retries"] = "0",
        };
        foreach (var kv in extra ?? new())
            options[kv.Key] = kv.Value;
        return new S3("s3://test-bucket/pfx", options);
    }

    [Test]
    public async Task TheStateIsReadFromTheStorageClassAndTheRestoreHeader_Async()
    {
        using var server = new FakeS3();
        server.Objects["standard.zip"] = new() { ["x-amz-storage-class"] = "STANDARD" };
        server.Objects["instant.zip"] = new() { ["x-amz-storage-class"] = "GLACIER_IR" };
        server.Objects["glacier.zip"] = new() { ["x-amz-storage-class"] = "GLACIER" };
        server.Objects["deep.zip"] = new() { ["x-amz-storage-class"] = "DEEP_ARCHIVE" };
        server.Objects["pending.zip"] = new() { ["x-amz-storage-class"] = "GLACIER", ["x-amz-restore"] = "ongoing-request=\"true\"" };
        server.Objects["restored.zip"] = new() { ["x-amz-storage-class"] = "GLACIER", ["x-amz-restore"] = "ongoing-request=\"false\", expiry-date=\"Fri, 01 Jan 2027 00:00:00 GMT\"" };

        using var backend = CreateBackend(server.Port);
        async Task<ArchiveRestoreState> State(string name) => await backend.GetArchiveRestoreStateAsync(name, CancellationToken.None);

        Assert.That(await State("standard.zip"), Is.EqualTo(ArchiveRestoreState.Available));
        Assert.That(await State("instant.zip"), Is.EqualTo(ArchiveRestoreState.Available), "Glacier Instant Retrieval is read directly");
        Assert.That(await State("glacier.zip"), Is.EqualTo(ArchiveRestoreState.Archived));
        Assert.That(await State("deep.zip"), Is.EqualTo(ArchiveRestoreState.Archived));
        Assert.That(await State("pending.zip"), Is.EqualTo(ArchiveRestoreState.RestoreInProgress));
        Assert.That(await State("restored.zip"), Is.EqualTo(ArchiveRestoreState.Available), "A restored copy can be read until it expires");
    }

    [Test]
    public async Task TheRestoreRequestCarriesTheDaysAndTier_Async()
    {
        using var server = new FakeS3();
        using var backend = CreateBackend(server.Port, new() { ["s3-archive-restore-days"] = "3", ["s3-archive-restore-tier"] = "Bulk" });

        await backend.RequestArchiveRestoreAsync("glacier.zip", CancellationToken.None);

        Assert.That(server.Restores, Has.Count.EqualTo(1));
        Assert.That(server.Restores.TryPeek(out var restore), Is.True);
        Assert.That(restore.Path, Is.EqualTo("/test-bucket/pfx/glacier.zip"));
        Assert.That(restore.Body, Does.Contain("<Days>3</Days>"));
        Assert.That(restore.Body, Does.Contain("<Tier>Bulk</Tier>"));
    }

    [Test]
    public void ARestoreAlreadyInProgressIsNotAnError()
    {
        using var server = new FakeS3();
        server.RestoreInProgress.Add("pending.zip");
        using var backend = CreateBackend(server.Port);

        Assert.DoesNotThrowAsync(() => backend.RequestArchiveRestoreAsync("pending.zip", CancellationToken.None));
        Assert.That(server.Restores, Has.Count.EqualTo(1));
    }

    [Test]
    public void TheMinioClientSaysItCannotBringFilesBack()
    {
        using var server = new FakeS3();
        using var backend = CreateBackend(server.Port, new() { ["s3-client"] = "minio" });

        var ex = Assert.ThrowsAsync<UserInformationException>(() => backend.RequestArchiveRestoreAsync("glacier.zip", CancellationToken.None));
        Assert.That(ex!.HelpID, Is.EqualTo("S3ArchiveRestoreNotSupported"));
    }
}
