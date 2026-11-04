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
/// The XMPP report gave the client no certificate validation callback, and the client then
/// accepts any server certificate, so the report and the account password could be sent to
/// anyone who answered for the server. The report now validates the certificate, with the same
/// options as the HTTP report to accept a self-signed one.
/// </summary>
public class SendJabberMessageCertificateTests
{
    private const string StreamHeader = "<?xml version='1.0'?><stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams' id='test' from='127.0.0.1' version='1.0'>";

    /// <summary>
    /// A self-signed certificate for the test server, loaded so that the server can use its key
    /// </summary>
    private static X509Certificate2 CreateServerCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=duplicati-test-xmpp", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
    }

    /// <summary>
    /// Reads from the stream until the text read so far contains the given text
    /// </summary>
    private static async Task ReadUntilAsync(Stream stream, string text)
    {
        var buffer = new byte[4096];
        var received = new StringBuilder();
        while (!received.ToString().Contains(text, StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
                throw new EndOfStreamException();
            received.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }
    }

    /// <summary>
    /// Accepts one connection as an XMPP server that requires STARTTLS, and reports whether
    /// the client went on over TLS after seeing the server's certificate
    /// </summary>
    /// <returns>True if the client accepted the certificate</returns>
    private static async Task<bool> ServeOneClientAsync(TcpListener listener, X509Certificate2 certificate)
    {
        try
        {
            using var tcp = await listener.AcceptTcpClientAsync();
            var plain = tcp.GetStream();
            await ReadUntilAsync(plain, "<stream:stream");
            await plain.WriteAsync(Encoding.UTF8.GetBytes(StreamHeader + "<stream:features><starttls xmlns='urn:ietf:params:xml:ns:xmpp-tls'><required/></starttls></stream:features>"));
            await ReadUntilAsync(plain, "starttls");
            await plain.WriteAsync(Encoding.UTF8.GetBytes("<proceed xmlns='urn:ietf:params:xml:ns:xmpp-tls'/>"));

            using var ssl = new SslStream(plain);
            await ssl.AuthenticateAsServerAsync(certificate);
            // A client that accepted the certificate opens a new stream over TLS
            await ReadUntilAsync(ssl, "<stream:stream");
            return true;
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
    /// <returns>True if the client accepted the server's certificate</returns>
    private static async Task<bool> SendReportAsync(Func<X509Certificate2, Dictionary<string, string>> extraOptions)
    {
        using var certificate = CreateServerCertificate();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = ServeOneClientAsync(listener, certificate);

            var options = new Dictionary<string, string>(extraOptions(certificate))
            {
                ["send-xmpp-to"] = "someone@127.0.0.1",
                ["send-xmpp-username"] = $"duplicati@127.0.0.1:{port}",
                ["send-xmpp-password"] = "password",
                ["send-xmpp-level"] = "all"
            };

            var module = new SendJabberMessage();
            module.Configure(options);
            var url = "file:///not-used";
            var paths = Array.Empty<string>();
            module.OnStart("Backup", ref url, ref paths);
            // The test server stops after the TLS handshake, so sending fails either way
            module.OnFinish(null, new Exception("Simulated failure"));

            // The report is sent before OnFinish returns, so a client that accepted the certificate
            // has opened its new stream by now. One that refused it may leave the connection open.
            listener.Stop();
            var finished = await Task.WhenAny(server, Task.Delay(TimeSpan.FromSeconds(10)));
            return finished == server && await server;
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
        => Assert.That(await SendReportAsync(_ => new() { ["send-xmpp-accept-any-ssl-certificate"] = "true" }), Is.True);

    [Test]
    [Category("Targeted")]
    public async Task ACertificateWithAGivenHashIsAcceptedAsync()
    {
        Assert.That(await SendReportAsync(c => new() { ["send-xmpp-accept-specified-ssl-hash"] = c.GetCertHashString() }), Is.True);

        // The hash of another certificate does not let the server's certificate through
        using var other = CreateServerCertificate();
        Assert.That(await SendReportAsync(_ => new() { ["send-xmpp-accept-specified-ssl-hash"] = other.GetCertHashString() }), Is.False);
    }
}
