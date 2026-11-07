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
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Duplicati.Library.Modules.Builtin;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The mail report had no options for the server certificate. The HTTP report got options to
/// accept any certificate, certificates with given hashes, and certificates whose revocation
/// cannot be checked, but a mail server whose certificate could not be checked for revocation
/// failed every report (forum 20747), and the general --ignore-revocation-failure did not reach it.
/// </summary>
public class SendMailCertificateOptionsTests
{
    /// <summary>
    /// A self-signed certificate for the test server, loaded so that the server can use its key
    /// </summary>
    private static X509Certificate2 CreateServerCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=duplicati-test-smtp", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
    }

    /// <summary>
    /// Accepts one connection over TLS and answers it as an SMTP server
    /// </summary>
    /// <returns>True if a message was delivered</returns>
    private static async Task<bool> ServeOneMailAsync(TcpListener listener, X509Certificate2 certificate)
    {
        try
        {
            using var tcp = await listener.AcceptTcpClientAsync();
            using var ssl = new SslStream(tcp.GetStream());
            await ssl.AuthenticateAsServerAsync(certificate);
            using var reader = new StreamReader(ssl, Encoding.ASCII);
            using var writer = new StreamWriter(ssl, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

            var delivered = false;
            await writer.WriteLineAsync("220 test ESMTP");
            while (await reader.ReadLineAsync() is { } line)
            {
                var command = line.ToUpperInvariant();
                if (command.StartsWith("EHLO") || command.StartsWith("HELO"))
                    await writer.WriteLineAsync("250 test");
                else if (command.StartsWith("DATA"))
                {
                    await writer.WriteLineAsync("354 go ahead");
                    while (await reader.ReadLineAsync() is { } data && data != ".") { }
                    delivered = true;
                    await writer.WriteLineAsync("250 queued");
                }
                else if (command.StartsWith("QUIT"))
                {
                    await writer.WriteLineAsync("221 bye");
                    break;
                }
                else
                    await writer.WriteLineAsync("250 ok");
            }

            return delivered;
        }
        catch (Exception ex) when (ex is IOException || ex is AuthenticationException || ex is ObjectDisposedException)
        {
            // The client refused the certificate
            return false;
        }
    }

    /// <summary>
    /// Sends a report for a failed backup through a test server
    /// </summary>
    /// <param name="extraOptions">The options to add, given the certificate of the server</param>
    /// <returns>True if the server got the report</returns>
    private static async Task<bool> SendReportAsync(Func<X509Certificate2, Dictionary<string, string>> extraOptions)
    {
        using var certificate = CreateServerCertificate();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = ServeOneMailAsync(listener, certificate);

            var options = new Dictionary<string, string>(extraOptions(certificate))
            {
                ["send-mail-to"] = "someone@example.com",
                ["send-mail-from"] = "duplicati@example.com",
                ["send-mail-url"] = $"smtps://127.0.0.1:{port}",
                ["send-mail-level"] = "all"
            };

            var module = new SendMail();
            module.Configure(options);
            var url = "file:///not-used";
            var paths = Array.Empty<string>();
            module.OnStart("Backup", ref url, ref paths);
            module.OnFinish(null, new Exception("Simulated failure"));

            listener.Stop();
            return await server.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    [Category("Targeted")]
    public async Task AnUntrustedCertificateIsRefusedByDefaultAsync()
        => Assert.That(await SendReportAsync(_ => []), Is.False);

    [Test]
    [Category("Targeted")]
    public async Task AnyCertificateIsAcceptedWhenAskedForAsync()
        => Assert.That(await SendReportAsync(_ => new() { ["send-mail-accept-any-ssl-certificate"] = "true" }), Is.True);

    [Test]
    [Category("Targeted")]
    public async Task ACertificateWithAGivenHashIsAcceptedAsync()
    {
        Assert.That(await SendReportAsync(c => new() { ["send-mail-accept-specified-ssl-hash"] = c.GetCertHashString() }), Is.True);

        // The hash of another certificate does not let the server's certificate through
        using var other = CreateServerCertificate();
        Assert.That(await SendReportAsync(_ => new() { ["send-mail-accept-specified-ssl-hash"] = other.GetCertHashString() }), Is.False);
    }
}
