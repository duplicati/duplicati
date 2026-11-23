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
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Dto.V2;
using Duplicati.WebserverCore.Endpoints.V2;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The user interface can send a test email with the email report settings,
/// before a backup sends a real report.
/// </summary>
public partial class ServerApiIntegrationTests
{
    /// <summary>
    /// A plain SMTP server on the loopback interface that records the messages it gets
    /// </summary>
    private sealed class TestSmtpServer : IDisposable
    {
        private readonly TcpListener m_listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource m_cancel = new();
        private readonly Task m_serving;

        /// <summary>The subject lines of the delivered messages</summary>
        public ConcurrentQueue<string> Subjects { get; } = new();
        /// <summary>The user names and passwords used to log in</summary>
        public ConcurrentQueue<(string User, string Password)> Logins { get; } = new();

        public TestSmtpServer()
        {
            m_listener.Start();
            m_serving = ServeAsync();
        }

        public string Url => $"smtp://127.0.0.1:{((IPEndPoint)m_listener.LocalEndpoint).Port}";

        private async Task ServeAsync()
        {
            while (!m_cancel.IsCancellationRequested)
            {
                TcpClient tcp;
                try { tcp = await m_listener.AcceptTcpClientAsync(m_cancel.Token).ConfigureAwait(false); }
                catch { return; }

                using (tcp)
                {
                    try { await HandleAsync(tcp.GetStream()).ConfigureAwait(false); }
                    catch (IOException) { }
                }
            }
        }

        private async Task HandleAsync(Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

            await writer.WriteLineAsync("220 test ESMTP").ConfigureAwait(false);
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                var command = line.ToUpperInvariant();
                if (command.StartsWith("EHLO", StringComparison.Ordinal))
                {
                    await writer.WriteLineAsync("250-test").ConfigureAwait(false);
                    await writer.WriteLineAsync("250 AUTH PLAIN").ConfigureAwait(false);
                }
                else if (command.StartsWith("AUTH PLAIN", StringComparison.Ordinal))
                {
                    var encoded = line.Length > "AUTH PLAIN ".Length ? line.Substring("AUTH PLAIN ".Length) : null;
                    if (string.IsNullOrWhiteSpace(encoded))
                    {
                        await writer.WriteLineAsync("334 ").ConfigureAwait(false);
                        encoded = await reader.ReadLineAsync().ConfigureAwait(false);
                    }
                    var parts = Encoding.UTF8.GetString(Convert.FromBase64String(encoded!)).Split('\0');
                    Logins.Enqueue((parts[1], parts[2]));
                    await writer.WriteLineAsync("235 ok").ConfigureAwait(false);
                }
                else if (command.StartsWith("DATA", StringComparison.Ordinal))
                {
                    await writer.WriteLineAsync("354 go ahead").ConfigureAwait(false);
                    while (await reader.ReadLineAsync().ConfigureAwait(false) is { } data && data != ".")
                        if (data.StartsWith("Subject: ", StringComparison.OrdinalIgnoreCase))
                            Subjects.Enqueue(data.Substring("Subject: ".Length));
                    await writer.WriteLineAsync("250 queued").ConfigureAwait(false);
                }
                else if (command.StartsWith("QUIT", StringComparison.Ordinal))
                {
                    await writer.WriteLineAsync("221 bye").ConfigureAwait(false);
                    return;
                }
                else
                {
                    await writer.WriteLineAsync("250 ok").ConfigureAwait(false);
                }
            }
        }

        public void Dispose()
        {
            m_cancel.Cancel();
            m_listener.Stop();
            try { m_serving.Wait(TimeSpan.FromSeconds(5)); } catch { }
            m_cancel.Dispose();
        }
    }

    private static async Task<TestEmailResponseDto> SendTestEmailAsync(HttpClient httpClient, TestEmailRequestDto request)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/v2/report/test-email", request, JsonOptions).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TestEmailResponseDto>(JsonOptions).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The test email response was empty");
    }

    [Test]
    [Category("Integration")]
    public async Task TestEmailIsSentWithTheGivenSettings_Async()
    {
        using var smtp = new TestSmtpServer();
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var systemInfo = JsonNode.Parse(await httpClient.GetStringAsync("/api/v1/systeminfo").ConfigureAwait(false));
            Assert.That(systemInfo?["APIExtensions"]?.AsArray().Select(x => x?.ToString()), Does.Contain(TestEmail.ApiExtension));

            var result = await SendTestEmailAsync(httpClient, new TestEmailRequestDto
            {
                Options = new()
                {
                    ["--send-mail-to"] = "someone@example.com",
                    ["send-mail-from"] = "duplicati@example.com",
                    ["send-mail-url"] = smtp.Url,
                    ["send-mail-username"] = "user",
                    ["send-mail-password"] = "typed-password",
                    // Not a send-mail option, so it is ignored
                    ["dbpath"] = "/not/used",
                }
            }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(smtp.Subjects, Is.EqualTo(new[] { "Duplicati test email" }));
                Assert.That(smtp.Logins, Is.EqualTo(new[] { ("user", "typed-password") }));
                Assert.That(result.Data?.Log, Has.Some.Contains(smtp.Url));
                Assert.That(result.Data?.Log, Has.None.Contains("typed-password"), "The protocol log must not be returned");
            });
        }).ConfigureAwait(false);
    }

    [Test]
    [Category("Integration")]
    public async Task TestEmailUsesTheStoredPasswordForAMaskedValue_Async()
    {
        using var smtp = new TestSmtpServer();
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            Directory.CreateDirectory(this.DATAFOLDER);
            var backupId = await CreateBackupAsync(httpClient, "integration-passphrase", extraSettings:
            [
                new() { Name = "--send-mail-password", Value = "stored-backup-password" },
            ]).ConfigureAwait(false);
            (await httpClient.PatchAsJsonAsync("/api/v1/serversettings", new Dictionary<string, string?>
            {
                ["--send-mail-to"] = "global@example.com",
                ["--send-mail-url"] = smtp.Url,
                ["--send-mail-username"] = "global-user",
            }).ConfigureAwait(false)).EnsureSuccessStatusCode();

            // A backup inherits the global settings, and the password is the masked one the client got
            var result = await SendTestEmailAsync(httpClient, new TestEmailRequestDto
            {
                BackupId = backupId,
                IncludeGlobalOptions = true,
                Options = new() { ["send-mail-password"] = Connection.PASSWORD_PLACEHOLDER }
            }).ConfigureAwait(false);
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(smtp.Logins, Is.EqualTo(new[] { ("global-user", "stored-backup-password") }));

            // Without the global settings there is no recipient
            var withoutGlobal = await SendTestEmailAsync(httpClient, new TestEmailRequestDto
            {
                BackupId = backupId,
                Options = new() { ["send-mail-password"] = Connection.PASSWORD_PLACEHOLDER }
            }).ConfigureAwait(false);
            Assert.That(withoutGlobal.Success, Is.False);
            Assert.That(withoutGlobal.StatusCode, Is.EqualTo("missing-recipient"));

            // A masked value with nothing stored is refused rather than sent as the password
            var missing = await SendTestEmailAsync(httpClient, new TestEmailRequestDto
            {
                Options = new()
                {
                    ["send-mail-to"] = "someone@example.com",
                    ["send-mail-password"] = Connection.PASSWORD_PLACEHOLDER
                }
            }).ConfigureAwait(false);
            Assert.That(missing.Success, Is.False);
            Assert.That(missing.StatusCode, Is.EqualTo("password-not-found"));
        }).ConfigureAwait(false);
    }

    [Test]
    [Category("Integration")]
    public async Task TestEmailReportsAFailingServer_Async()
    {
        // A port that was free a moment ago, so the connection is refused
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var result = await SendTestEmailAsync(httpClient, new TestEmailRequestDto
            {
                Options = new()
                {
                    ["send-mail-to"] = "someone@example.com",
                    ["send-mail-url"] = $"smtp://127.0.0.1:{port}",
                }
            }).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo("send-failed"));
            Assert.That(result.Error, Is.Not.Empty);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Accepts one connection and never answers it, like a server behind a firewall that drops packets
    /// </summary>
    /// <returns>The server URL, and a task that completes when the client closes the connection</returns>
    private static (string Url, Task Closed, TcpListener Listener) StartSilentSmtpServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var closed = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            var buffer = new byte[1024];
            try
            {
                // Waits for the client to close the connection
                while (await client.GetStream().ReadAsync(buffer).ConfigureAwait(false) > 0) { }
            }
            catch (IOException) { }
        });
        return ($"smtp://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}", closed, listener);
    }

    [Test]
    [Category("Targeted")]
    public async Task CancellingATestEmailStopsWaitingForTheServer_Async()
    {
        var (url, closed, listener) = StartSilentSmtpServer();
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var send = new Duplicati.Library.Modules.Builtin.SendMail().SendTestMessageAsync(new Dictionary<string, string?>
            {
                ["send-mail-to"] = "someone@example.com",
                // A second server must not be tried once the send is cancelled
                ["send-mail-url"] = $"{url};{url}",
            }, cancel.Token);

            // Without the cancellation the client waits a minute for the greeting
            Assert.That(async () => await send.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(await Task.WhenAny(closed, Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(false), Is.SameAs(closed),
                "The connection to the server should be closed");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    [Category("Integration")]
    public async Task AbortingTheTestEmailRequestClosesTheServerConnection_Async()
    {
        var (url, closed, listener) = StartSilentSmtpServer();
        try
        {
            await WithAuthenticatedServerAsync(async httpClient =>
            {
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                Assert.That(async () => await httpClient.PostAsJsonAsync("/api/v2/report/test-email", new TestEmailRequestDto
                {
                    Options = new() { ["send-mail-to"] = "someone@example.com", ["send-mail-url"] = url }
                }, JsonOptions, cancel.Token).ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>());

                // The server stops sending when the request is aborted, instead of waiting for the mail server
                Assert.That(await Task.WhenAny(closed, Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(false), Is.SameAs(closed),
                    "The server should close the connection to the mail server when the request is aborted");
            }).ConfigureAwait(false);
        }
        finally
        {
            listener.Stop();
        }
    }
}
