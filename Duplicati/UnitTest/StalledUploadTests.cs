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
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library;
using Duplicati.Library.Backend;
using Duplicati.Library.Backend.DrimeCloud;
using Duplicati.Library.Backend.GoogleDrive;
using Duplicati.Library.Backend.OpenStack;
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
    /// The time an upload has to complete after the source has been read, longer than any test waits
    /// </summary>
    private static readonly TimeSpan Completion = TimeSpan.FromMinutes(10);

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
        var ex = await WaitGuardedAsync(Utility.WithUploadTimeout(source, Idle, Completion, CancellationToken.None, (s, ct) => StallAfterAsync(s, read, ct)));
        Assert.That(ex, Is.InstanceOf<TimeoutException>());
        Assert.That(source.CanRead, Is.True, "The source belongs to the caller");
    }

    [Test]
    public async Task AnUploadThatIsReadSlowlyIsNotCutOff()
    {
        using var source = new MemoryStream(new byte[8]);
        var ex = await WaitGuardedAsync(Utility.WithUploadTimeout(source, Idle, Completion, CancellationToken.None, async (s, ct) =>
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
        var ex = await WaitGuardedAsync(Utility.WithUploadTimeout(source, Idle, Completion, CancellationToken.None, async (s, ct) =>
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
        var ex = await WaitGuardedAsync(Utility.WithUploadTimeout(source, Idle, Idle * 3, CancellationToken.None, (s, ct) => StallAfterAsync(s, 1000, ct)));
        Assert.That(ex, Is.InstanceOf<TimeoutException>());
        Assert.That(sw.Elapsed, Is.GreaterThan(Idle * 2.9));
    }

    [Test]
    public async Task ACancelledUploadIsNotATimeout()
    {
        using var source = new MemoryStream(new byte[1000]);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var ex = await WaitGuardedAsync(Utility.WithUploadTimeout(source, Idle, Completion, cts.Token, (s, ct) => StallAfterAsync(s, 500, ct)));
        Assert.That(ex, Is.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task AnUploadThatFailsPassesItsErrorOn()
    {
        using var source = new MemoryStream(new byte[1000]);
        var ex = await WaitGuardedAsync(Utility.WithUploadTimeout(source, Idle, Completion, CancellationToken.None, (_, _) => throw new InvalidOperationException("failed")));
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

    /// <summary>
    /// Answers the requests a backend makes before an upload, and stops at the upload without reading it, as a server whose connection stalls
    /// </summary>
    /// <param name="answer">Answers a request, or returns <c>null</c> for the upload</param>
    private sealed class StallingUploadHandler(Func<HttpRequestMessage, HttpResponseMessage?> answer) : HttpMessageHandler
    {
        /// <summary>
        /// Set when the upload has reached the handler
        /// </summary>
        public TaskCompletionSource Uploading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Set to read all of the upload before stopping, as a server that takes the data and never answers
        /// </summary>
        public bool ReadUpload { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = answer(request);
            if (response != null)
                return response;

            Uploading.TrySetResult();
            if (ReadUpload)
                await request.Content!.CopyToAsync(Stream.Null, cancellationToken).ConfigureAwait(false);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new UnreachableException();
        }
    }

    private static HttpResponseMessage JsonAnswer(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>
    /// Uploads through the backend and checks that the stalled upload ends with a timeout
    /// </summary>
    private static async Task AssertUploadTimesOutAsync(StallingUploadHandler handler, Func<Stream, Task> upload)
    {
        using var data = new MemoryStream(new byte[1024 * 1024]);
        var ex = await WaitGuardedAsync(upload(data));

        Assert.That(handler.Uploading.Task.IsCompleted, Is.True, $"The upload did not reach the server: {ex}");
        Assert.That(ex, Is.InstanceOf<TimeoutException>());
    }

    private static readonly Dictionary<string, string?> ShortReadWriteTimeout = new() { ["read-write-timeout"] = "2s" };

    private static Dictionary<string, string?> WithShortTimeout(Dictionary<string, string?> options)
    {
        foreach (var (key, value) in ShortReadWriteTimeout)
            options[key] = value;
        return options;
    }

    [Test]
    public async Task ATahoeUploadThatStallsTimesOut()
    {
        var handler = new StallingUploadHandler(request => request.Method == HttpMethod.Put ? null : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var backend = new TahoeBackend("tahoe://example.invalid/uri/URI:DIR2:aaa:bbb/", WithShortTimeout([]), handler);
        await AssertUploadTimesOutAsync(handler, data => backend.PutAsync("a.txt", data, CancellationToken.None));
    }

    /// <summary>
    /// The server takes all of the upload and never answers, so only the upload completion timeout can end it
    /// </summary>
    [Test]
    public async Task ATahoeUploadThatIsNeverAnsweredTimesOut()
    {
        var handler = new StallingUploadHandler(request => request.Method == HttpMethod.Put ? null : new HttpResponseMessage(HttpStatusCode.NotFound)) { ReadUpload = true };
        var options = WithShortTimeout([]);
        options["upload-completion-timeout"] = "2s";
        using var backend = new TahoeBackend("tahoe://example.invalid/uri/URI:DIR2:aaa:bbb/", options, handler);

        using var data = new MemoryStream(new byte[1024 * 1024]);
        var ex = await WaitGuardedAsync(backend.PutAsync("a.txt", data, CancellationToken.None));

        Assert.That(data.Position, Is.EqualTo(data.Length), $"The upload was not read to its end: {ex}");
        Assert.That(ex, Is.InstanceOf<TimeoutException>());
    }

    [Test]
    public async Task AnOpenStackUploadThatStallsTimesOut()
    {
        const string AuthUri = "https://keystone.invalid/v2.0";
        var handler = new StallingUploadHandler(request =>
        {
            if (request.RequestUri!.ToString().StartsWith(AuthUri, StringComparison.Ordinal))
                return JsonAnswer("{\"access\":{\"token\":{\"id\":\"test-token\"},\"serviceCatalog\":[{\"type\":\"object-store\",\"endpoints\":[{\"publicURL\":\"https://swift.invalid/v1/AUTH_test\"}]}]}}");
            return request.Method == HttpMethod.Put ? null : new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var backend = new OpenStackStorage("openstack://container", WithShortTimeout(new()
        {
            ["auth-username"] = "user",
            ["auth-password"] = "pass",
            ["openstack-tenant-name"] = "tenant",
            ["openstack-authuri"] = AuthUri
        }), handler);
        await AssertUploadTimesOutAsync(handler, data => backend.PutAsync("a.txt", data, CancellationToken.None));
    }

    [Test]
    public async Task APCloudUploadThatStallsTimesOut()
    {
        var handler = new StallingUploadHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/uploadfile", StringComparison.Ordinal) ? null : JsonAnswer("{\"result\":0}"));
        using var backend = new pCloudBackend("pcloud://api.pcloud.com/", WithShortTimeout(new() { ["authid"] = "test-token" }), handler);
        await AssertUploadTimesOutAsync(handler, data => backend.PutAsync("a.txt", data, CancellationToken.None));
    }

    [Test]
    public async Task ADuplicatiUploadThatStallsTimesOut()
    {
        var handler = new StallingUploadHandler(request => request.RequestUri!.AbsolutePath.StartsWith("/credentials", StringComparison.Ordinal)
            ? JsonAnswer(DuplicatiBackendTestServer.GatewayCredentials)
            : request.RequestUri.AbsolutePath.StartsWith("/put/", StringComparison.Ordinal) ? null : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var backend = new Duplicati.Library.Backend.Duplicati.DuplicatiBackend("duplicati://storage.example.com", WithShortTimeout(new()
        {
            [Duplicati.Library.Backend.Duplicati.DuplicatiBackend.BACKUP_ID_OPTION] = "backup-1",
            [Duplicati.Library.Backend.Duplicati.DuplicatiBackend.AUTH_API_ID_OPTION] = "org-1",
            [Duplicati.Library.Backend.Duplicati.DuplicatiBackend.AUTH_API_KEY_OPTION] = "key-1",
            ["machine-id"] = "machine-1",
        }), handler);
        await AssertUploadTimesOutAsync(handler, data => backend.PutAsync("a.txt", data, CancellationToken.None));
    }

    /// <summary>
    /// A host that cannot be resolved, so a request that is not stubbed fails instead of reaching a real OAuth service
    /// </summary>
    private const string OAuthUrl = "http://oauth.invalid/token";

    private static HttpResponseMessage? OAuthAnswer(HttpRequestMessage request)
        => request.RequestUri!.ToString().StartsWith(OAuthUrl, StringComparison.Ordinal)
            ? JsonAnswer("{\"access_token\":\"test-token\",\"expires\":3600}")
            : null;

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public async Task AJottacloudUploadThatStallsTimesOut(bool legacyUpload)
    {
        const string UploadUrl = "https://up-test.jottacloud.com/files/v1/upload/test-upload-id";
        var handler = new StallingUploadHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (OAuthAnswer(request) is { } token)
                return token;
            if (url.Contains("/userinfo", StringComparison.Ordinal))
                return JsonAnswer("{\"username\":\"test-user\"}");
            if (url == "https://api.jottacloud.com/files/v1/allocate")
                return JsonAnswer("{\"name\":\"a.txt\",\"path\":\"/test-user/Jotta/Archive/Backup/a.txt\",\"state\":\"INCOMPLETE\",\"upload_id\":\"test-upload-id\",\"upload_url\":\"" + UploadUrl + "\",\"bytes\":1048576,\"resume_pos\":0}");
            if (url == UploadUrl || url.StartsWith("https://up.jottacloud.com/jfs/", StringComparison.Ordinal))
                return null;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var backend = new Jottacloud("jottacloud://Backup", WithShortTimeout(new()
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl,
            ["jottacloud-legacy-upload"] = legacyUpload ? "true" : "false"
        }), new HttpClient(handler));
        await AssertUploadTimesOutAsync(handler, data => backend.PutAsync("a.txt", data, CancellationToken.None));
    }

    [Test]
    public async Task AGoogleDriveUploadThatStallsTimesOut()
    {
        const string SessionUrl = "http://upload.invalid/session-1";
        var handler = new StallingUploadHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (OAuthAnswer(request) is { } token)
                return token;
            if (url.StartsWith(SessionUrl, StringComparison.Ordinal))
                return null;
            if (request.Headers.Contains("X-Upload-Content-Type"))
            {
                var session = JsonAnswer("{}");
                session.Headers.Location = new Uri(SessionUrl);
                return session;
            }
            if (url.Contains("/drive/v2/about", StringComparison.Ordinal))
                return JsonAnswer("{\"rootFolderId\":\"root\"}");
            if (url.Contains("/drive/v2/files", StringComparison.Ordinal))
                return Uri.UnescapeDataString(request.RequestUri.Query).Replace('+', ' ').Contains("title = 'target'", StringComparison.Ordinal)
                    ? JsonAnswer("{\"items\":[{\"id\":\"folder-1\",\"title\":\"target\",\"mimeType\":\"application/vnd.google-apps.folder\"}]}")
                    : JsonAnswer("{\"items\":[]}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var backend = new GoogleDrive("googledrive://target", WithShortTimeout(new()
        {
            ["authid"] = "test-authid",
            ["oauth-url"] = OAuthUrl
        }), new HttpClient(handler));
        await AssertUploadTimesOutAsync(handler, data => backend.PutAsync("a.txt", data, CancellationToken.None));
    }

    /// <summary>
    /// Answers the Microsoft Graph requests of a SharePoint site, and stalls on the upload
    /// </summary>
    private sealed class StallingGraph : OAuthHttpMessageHandler
    {
        private const string SiteId = "contoso-my.sharepoint.com,1111,2222";
        private const string SessionUrl = "https://upload.invalid/session-1";

        public TaskCompletionSource Uploading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly HttpClient m_tokenClient;

        public StallingGraph()
            : this(new HttpClient())
        {
        }

        private StallingGraph(HttpClient tokenClient)
            : base(new OAuthHelperHttpClient("test-authid", "sharepoint", "http://127.0.0.1:1/", tokenClient))
        {
            m_tokenClient = tokenClient;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                m_tokenClient.Dispose();
            base.Dispose(disposing);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith("https://graph.microsoft.com/v1.0/sites/contoso-my.sharepoint.com:", StringComparison.Ordinal))
                return JsonAnswer($$"""{"id":"{{SiteId}}"}""");
            if (url == $"https://graph.microsoft.com/v1.0/sites/{SiteId}/drive")
                return JsonAnswer("""{"id":"drive","webUrl":"https://contoso-my.sharepoint.com/personal/someone_contoso_com/Documents"}""");
            if (url.EndsWith(":/createUploadSession", StringComparison.Ordinal))
                return JsonAnswer($$"""{"uploadUrl":"{{SessionUrl}}"}""");
            if (request.Method == HttpMethod.Delete)
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (!url.EndsWith(":/content", StringComparison.Ordinal) && !url.StartsWith(SessionUrl, StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"error":{"code":"itemNotFound","message":"stub"}}""", Encoding.UTF8, "application/json") };

            Uploading.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new UnreachableException();
        }
    }

    /// <summary>
    /// A small file is sent in one request, and a large one in the fragments of an upload session
    /// </summary>
    [Test]
    [TestCase(1024 * 1024)]
    [TestCase(5 * 1000 * 1000)]
    public async Task AMicrosoftGraphUploadThatStallsTimesOut(int size)
    {
        using var graph = new StallingGraph();
        using var backend = new SharePointV2(
            "sharepoint://contoso-my.sharepoint.com/personal/someone_contoso_com//Documents/Vhds",
            WithShortTimeout(new() { ["authid"] = "test-authid" }),
            graph);

        using var data = new MemoryStream(new byte[size]);
        var ex = await WaitGuardedAsync(backend.PutAsync("a.txt", data, CancellationToken.None));

        Assert.That(graph.Uploading.Task.IsCompleted, Is.True, $"The upload did not reach the server: {ex}");

        // A failed fragment cancels the upload session, and is reported with the fragment it was
        if (size >= 4 * 1000 * 1000)
        {
            Assert.That(ex, Is.InstanceOf<Duplicati.Library.Backend.MicrosoftGraph.UploadSessionException>());
            ex = ex!.InnerException;
        }
        Assert.That(ex, Is.InstanceOf<TimeoutException>());
    }

    [Test]
    public async Task ADrimeUploadThatStallsTimesOut()
    {
        var handler = new StallingUploadHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/uploads", StringComparison.Ordinal))
                return null;
            if (path.EndsWith("/count", StringComparison.Ordinal))
                return JsonAnswer("{\"count\":0}");
            if (path.EndsWith("/drive/file-entries", StringComparison.Ordinal))
                return JsonAnswer(request.RequestUri.Query.Contains("type=folder", StringComparison.Ordinal)
                    ? "{\"data\":[{\"id\":7,\"name\":\"backup\",\"type\":\"folder\",\"hash\":\"folder-hash\",\"file_size\":0}],\"current_page\":1,\"last_page\":1,\"per_page\":1,\"total\":1}"
                    : "{\"data\":[],\"current_page\":1,\"last_page\":1,\"per_page\":50,\"total\":0}");
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"message\":\"unexpected request\"}", Encoding.UTF8, "application/json") };
        });
        using var backend = new DrimeBackend("drimecloud://backup", WithShortTimeout(new() { ["api-token"] = "test-token" }), handler);
        await AssertUploadTimesOutAsync(handler, data => backend.PutAsync("a.txt", data, CancellationToken.None));
    }

    [Test]
    public async Task AFilejumpUploadThatStallsTimesOut()
    {
        var handler = new StallingUploadHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/uploads", StringComparison.Ordinal))
                return null;
            if (path.EndsWith("/drive/file-entries", StringComparison.Ordinal))
                return JsonAnswer(request.RequestUri.Query.Contains("type=folder", StringComparison.Ordinal)
                    ? "{\"data\":[{\"id\":7,\"name\":\"backup\",\"type\":\"folder\"}],\"per_page\":50}"
                    : "{\"data\":[],\"per_page\":50}");
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        });
        using var backend = new Filejump("filejump://backup", WithShortTimeout(new() { ["api-token"] = "test-token" }), handler);
        await AssertUploadTimesOutAsync(handler, data => backend.PutAsync("a.txt", data, CancellationToken.None));
    }
}
