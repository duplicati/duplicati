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
using Duplicati.Library.Interface;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The S3 backend could only use the access key it was given, not a role assumed through STS (#2504)
/// </summary>
[TestFixture]
public class S3AssumeRoleTests
{
    private const string TemporaryKeyId = "ASIATEMPKEY";
    private const string SessionToken = "temp-session-token";
    private const string RoleArn = "arn:aws:iam::123456789012:role/backup";

    /// <summary>
    /// Answers AssumeRole on /sts/ with temporary credentials, and a bucket listing on everything else.
    /// Records what each side was sent.
    /// </summary>
    private sealed class FakeAws : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;

        public int Port { get; }
        public TimeSpan CredentialLifetime { get; set; } = TimeSpan.FromHours(1);
        public ConcurrentQueue<string> StsBodies { get; } = new();
        public ConcurrentQueue<(string? Authorization, string? SecurityToken)> S3Requests { get; } = new();

        public FakeAws()
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

                string body;
                if ((context.Request.RawUrl ?? "").StartsWith("/sts/", StringComparison.Ordinal))
                {
                    using (var reader = new StreamReader(context.Request.InputStream))
                        StsBodies.Enqueue(Uri.UnescapeDataString((await reader.ReadToEndAsync().ConfigureAwait(false)).Replace('+', ' ')));
                    body =
                        "<AssumeRoleResponse xmlns=\"https://sts.amazonaws.com/doc/2011-06-15/\"><AssumeRoleResult>" +
                        $"<Credentials><AccessKeyId>{TemporaryKeyId}</AccessKeyId><SecretAccessKey>temp-secret</SecretAccessKey>" +
                        $"<SessionToken>{SessionToken}</SessionToken><Expiration>{DateTime.UtcNow.Add(CredentialLifetime):yyyy-MM-ddTHH:mm:ssZ}</Expiration></Credentials>" +
                        "<AssumedRoleUser><Arn>arn:aws:sts::123456789012:assumed-role/backup/duplicati</Arn><AssumedRoleId>AROATEST:duplicati</AssumedRoleId></AssumedRoleUser>" +
                        "</AssumeRoleResult><ResponseMetadata><RequestId>1</RequestId></ResponseMetadata></AssumeRoleResponse>";
                }
                else
                {
                    S3Requests.Enqueue((context.Request.Headers["Authorization"], context.Request.Headers["x-amz-security-token"]));
                    body =
                        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                        "<ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">" +
                        "<Name>test-bucket</Name><Prefix>pfx/</Prefix><KeyCount>0</KeyCount><MaxKeys>1000</MaxKeys>" +
                        "<IsTruncated>false</IsTruncated></ListBucketResult>";
                }

                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.ContentType = "text/xml";
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

    private static Dictionary<string, string?> CreateOptions(FakeAws server, Dictionary<string, string?> extraOptions)
    {
        var options = new Dictionary<string, string?>
        {
            ["aws-access-key-id"] = "test-access-key",
            ["aws-secret-access-key"] = "test-secret-key",
            ["s3-server-name"] = $"localhost:{server.Port}",
            ["use-ssl"] = "false",
            ["s3-client"] = "aws",
            ["s3-sts-endpoint"] = $"http://localhost:{server.Port}/sts/"
        };
        foreach (var kv in extraOptions)
            options[kv.Key] = kv.Value;
        return options;
    }

    private static async Task ListAsync(Dictionary<string, string?> options)
    {
        using var backend = new S3("s3://test-bucket/pfx", options);
        await backend.ListAsync(CancellationToken.None).ToListAsync().ConfigureAwait(false);
    }

    [Test]
    [Category("Backend")]
    public async Task RequestsAreSignedWithTheAssumedRole()
    {
        using var server = new FakeAws();
        await ListAsync(CreateOptions(server, new() { ["s3-assume-role-arn"] = RoleArn })).ConfigureAwait(false);

        Assert.That(server.StsBodies, Has.Count.EqualTo(1));
        var sts = server.StsBodies.Single();
        Assert.That(sts, Does.Contain("Action=AssumeRole"));
        Assert.That(sts, Does.Contain("RoleArn=" + RoleArn));
        Assert.That(sts, Does.Contain("RoleSessionName=duplicati"));

        Assert.That(server.S3Requests, Is.Not.Empty);
        foreach (var (authorization, securityToken) in server.S3Requests)
        {
            Assert.That(authorization, Does.Contain($"Credential={TemporaryKeyId}/"));
            Assert.That(securityToken, Is.EqualTo(SessionToken));
        }
    }

    [Test]
    [Category("Backend")]
    public async Task TheSessionNameAndExternalIdAreSent()
    {
        using var server = new FakeAws();
        await ListAsync(CreateOptions(server, new()
        {
            ["s3-assume-role-arn"] = RoleArn,
            ["s3-assume-role-session-name"] = "nightly",
            ["s3-assume-role-external-id"] = "shared-secret"
        })).ConfigureAwait(false);

        var sts = server.StsBodies.Single();
        Assert.That(sts, Does.Contain("RoleSessionName=nightly"));
        Assert.That(sts, Does.Contain("ExternalId=shared-secret"));
    }

    /// <summary>
    /// The temporary credentials expire, so a long backup asks for new ones before they do
    /// </summary>
    [Test]
    [Category("Backend")]
    public async Task CredentialsAboutToExpireAreRequestedAgain()
    {
        using var server = new FakeAws { CredentialLifetime = TimeSpan.FromMinutes(1) };
        using var backend = new S3("s3://test-bucket/pfx", CreateOptions(server, new() { ["s3-assume-role-arn"] = RoleArn }));

        await backend.ListAsync(CancellationToken.None).ToListAsync().ConfigureAwait(false);
        await backend.ListAsync(CancellationToken.None).ToListAsync().ConfigureAwait(false);

        // Within the last minutes every request asks again, so the count is not fixed
        Assert.That(server.StsBodies, Has.Count.GreaterThanOrEqualTo(2));
    }

    [Test]
    [Category("Backend")]
    public async Task CredentialsThatStayValidAreReused()
    {
        using var server = new FakeAws();
        using var backend = new S3("s3://test-bucket/pfx", CreateOptions(server, new() { ["s3-assume-role-arn"] = RoleArn }));

        await backend.ListAsync(CancellationToken.None).ToListAsync().ConfigureAwait(false);
        await backend.ListAsync(CancellationToken.None).ToListAsync().ConfigureAwait(false);

        Assert.That(server.StsBodies, Has.Count.EqualTo(1));
    }

    [Test]
    [Category("Backend")]
    public async Task WithoutARoleTheAccessKeyIsUsed()
    {
        using var server = new FakeAws();
        await ListAsync(CreateOptions(server, [])).ConfigureAwait(false);

        Assert.That(server.StsBodies, Is.Empty);
        Assert.That(server.S3Requests, Is.Not.Empty);
        foreach (var (authorization, securityToken) in server.S3Requests)
        {
            Assert.That(authorization, Does.Contain("Credential=test-access-key/"));
            Assert.That(securityToken, Is.Null);
        }
    }

    [Test]
    [Category("Backend")]
    public void TheMinioClientRefusesARole()
    {
        using var server = new FakeAws();
        var options = CreateOptions(server, new() { ["s3-assume-role-arn"] = RoleArn, ["s3-client"] = "minio" });

        var ex = Assert.Throws<UserInformationException>(() => new S3("s3://test-bucket/pfx", options).Dispose());
        Assert.That(ex!.HelpID, Is.EqualTo("S3AssumeRoleNotSupportedByMinio"));
    }
}
