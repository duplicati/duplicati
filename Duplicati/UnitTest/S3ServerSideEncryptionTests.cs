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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Interface;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Checks that --s3-server-side-encryption asks the server to encrypt the objects it stores (#2193),
/// with both S3 clients, for uploads and for the copy a rename makes
/// </summary>
[TestFixture]
public class S3ServerSideEncryptionTests
{
    private const string EncryptionHeader = "x-amz-server-side-encryption";
    private const string KmsKeyHeader = "x-amz-server-side-encryption-aws-kms-key-id";

    /// <summary>
    /// Records the method, path and headers of every request, and answers as S3 would
    /// </summary>
    private sealed class FakeS3 : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;

        public int Port { get; }
        public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

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
                var headers = request.Headers.AllKeys
                    .Where(x => x != null)
                    .ToDictionary(x => x!.ToLowerInvariant(), x => request.Headers[x] ?? "");
                using var body = new MemoryStream();
                await request.InputStream.CopyToAsync(body).ConfigureAwait(false);
                Requests.Enqueue(new RecordedRequest(request.HttpMethod, request.Url?.AbsolutePath ?? "", request.Url?.Query ?? "", headers));

                var etag = "\"" + Convert.ToHexString(MD5.HashData(body.ToArray())).ToLowerInvariant() + "\"";
                string reply = "";
                if (request.Url?.Query.Contains("location") == true)
                    reply = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><LocationConstraint xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">us-east-1</LocationConstraint>";
                else if (request.HttpMethod == "PUT" && headers.ContainsKey("x-amz-copy-source"))
                    reply = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><CopyObjectResult><LastModified>2026-01-01T00:00:00.000Z</LastModified><ETag>\"d41d8cd98f00b204e9800998ecf8427e\"</ETag></CopyObjectResult>";

                var bytes = Encoding.UTF8.GetBytes(reply);
                context.Response.StatusCode = 200;
                context.Response.AddHeader("ETag", etag);
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

    private sealed record RecordedRequest(string Method, string Path, string Query, Dictionary<string, string> Headers)
    {
        public bool IsUpload => Method == "PUT" && Path.EndsWith("/a.txt") && !Headers.ContainsKey("x-amz-copy-source");
        public bool IsCopy => Method == "PUT" && Path.EndsWith("/b.txt") && Headers.ContainsKey("x-amz-copy-source");
        public override string ToString() => $"{Method} {Path}{Query} {string.Join(", ", Headers.Where(x => x.Key.StartsWith("x-amz")).Select(x => $"{x.Key}={x.Value}"))}";
    }

    private static Dictionary<string, string?> Options(int port, string client, Dictionary<string, string?> extra)
    {
        var options = new Dictionary<string, string?>
        {
            ["aws-access-key-id"] = "test-access-key",
            ["aws-secret-access-key"] = "test-secret-key",
            ["s3-server-name"] = $"localhost:{port}",
            ["use-ssl"] = "false",
            ["s3-client"] = client,
        };
        foreach (var kv in extra)
            options[kv.Key] = kv.Value;
        return options;
    }

    /// <summary>
    /// Uploads a.txt and renames it to b.txt, and returns the upload and the copy the rename made
    /// </summary>
    private static async Task<(RecordedRequest Upload, RecordedRequest Copy, string All)> UploadAndRenameAsync(string client, Dictionary<string, string?> extra)
    {
        using var server = new FakeS3();
        using (var backend = new S3("s3://test-bucket/pfx", Options(server.Port, client, extra)))
        {
            using (var data = new MemoryStream(Encoding.UTF8.GetBytes("contents")))
                await backend.PutAsync("a.txt", data, CancellationToken.None).ConfigureAwait(false);
            await backend.RenameAsync("a.txt", "b.txt", CancellationToken.None).ConfigureAwait(false);
        }

        var requests = server.Requests.ToList();
        var all = string.Join(Environment.NewLine, requests);
        var upload = requests.SingleOrDefault(x => x.IsUpload);
        var copy = requests.SingleOrDefault(x => x.IsCopy);
        Assert.That(upload, Is.Not.Null, "No upload of a.txt: " + all);
        Assert.That(copy, Is.Not.Null, "No copy to b.txt: " + all);
        return (upload!, copy!, all);
    }

    [Test]
    [Category("Backend")]
    [TestCase("aws")]
    [TestCase("minio")]
    public async Task WithoutTheOptionNoEncryptionIsRequestedAsync(string client)
    {
        var (upload, copy, all) = await UploadAndRenameAsync(client, new()).ConfigureAwait(false);

        Assert.That(upload.Headers.ContainsKey(EncryptionHeader), Is.False, all);
        Assert.That(copy.Headers.ContainsKey(EncryptionHeader), Is.False, all);
    }

    [Test]
    [Category("Backend")]
    [TestCase("aws")]
    [TestCase("minio")]
    public async Task S3ManagedKeysAreRequestedForUploadsAndCopiesAsync(string client)
    {
        var (upload, copy, all) = await UploadAndRenameAsync(client, new() { ["s3-server-side-encryption"] = "AES256" }).ConfigureAwait(false);

        foreach (var request in new[] { upload, copy })
        {
            Assert.That(request.Headers.GetValueOrDefault(EncryptionHeader), Is.EqualTo("AES256"), all);
            Assert.That(request.Headers.ContainsKey(KmsKeyHeader), Is.False, all);
        }
    }

    [Test]
    [Category("Backend")]
    [TestCase("aws")]
    [TestCase("minio")]
    public async Task AKmsKeyIsRequestedForUploadsAndCopiesAsync(string client)
    {
        var (upload, copy, all) = await UploadAndRenameAsync(client, new()
        {
            ["s3-server-side-encryption"] = "aws:kms",
            ["s3-server-side-encryption-kms-key-id"] = "arn:aws:kms:us-east-1:111122223333:key/test-key",
        }).ConfigureAwait(false);

        foreach (var request in new[] { upload, copy })
        {
            Assert.That(request.Headers.GetValueOrDefault(EncryptionHeader), Is.EqualTo("aws:kms"), all);
            Assert.That(request.Headers.GetValueOrDefault(KmsKeyHeader), Is.EqualTo("arn:aws:kms:us-east-1:111122223333:key/test-key"), all);
        }
    }

    [Test]
    [Category("Backend")]
    [TestCase("aws", "AES256")]
    [TestCase("aws", "")]
    [TestCase("minio", "AES256")]
    public void AKmsKeyWithoutKmsEncryptionIsRefused(string client, string encryption)
    {
        var error = Assert.Throws<UserInformationException>(() => new S3("s3://test-bucket/pfx", Options(1, client, new()
        {
            ["s3-server-side-encryption"] = encryption,
            ["s3-server-side-encryption-kms-key-id"] = "test-key",
        })).Dispose());
        Assert.That(error!.HelpID, Is.EqualTo("S3KmsKeyWithoutKmsEncryption"));
    }

    [Test]
    [Category("Backend")]
    [TestCase("aws")]
    [TestCase("minio")]
    public void AnUnknownEncryptionIsRefused(string client)
    {
        var error = Assert.Throws<UserInformationException>(() => new S3("s3://test-bucket/pfx", Options(1, client, new()
        {
            ["s3-server-side-encryption"] = "rot13",
        })).Dispose());
        Assert.That(error!.HelpID, Is.EqualTo("S3UnknownServerSideEncryption"));
    }
}
