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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// A WebDAV server that requires TLS client authentication only lets in a client that presents
/// a certificate during the handshake, so these tests run a real TLS listener and look at what
/// the backend presented. Reported as issue #3069.
/// </summary>
[TestFixture]
public class WebDavClientCertificateTests
{
    private const string PfxPassword = "test-pfx-password";

    private string m_tempFolder = null!;

    [SetUp]
    public void SetUp()
    {
        m_tempFolder = Path.Combine(Path.GetTempPath(), "duplicati-webdav-cert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_tempFolder);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_tempFolder, true); }
        catch { }
    }

    private static X509Certificate2 CreateCertificate(string subject, string ekuOid)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid(ekuOid) }, false));
        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // SChannel cannot use an ephemeral key for TLS, so round-trip through a PFX
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx, PfxPassword), PfxPassword, X509KeyStorageFlags.Exportable);
    }

    /// <summary>
    /// Accepts one TLS connection that asks for a client certificate, answers the request with
    /// "201 Created" and returns the certificate the client presented, if any.
    /// </summary>
    private static async Task<X509Certificate2?> ServeOneRequestAsync(TcpListener listener, X509Certificate2 serverCertificate, CancellationToken cancellationToken)
    {
        using var tcp = await listener.AcceptTcpClientAsync(cancellationToken);
        using var ssl = new SslStream(tcp.GetStream(), false);
        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = serverCertificate,
            ClientCertificateRequired = true,
            RemoteCertificateValidationCallback = (_, _, _, _) => true,
            EnabledSslProtocols = SslProtocols.None
        }, cancellationToken);

        var presented = ssl.RemoteCertificate == null ? null : X509CertificateLoader.LoadCertificate(ssl.RemoteCertificate.GetRawCertData());

        // Read the request headers before answering
        var buffer = new byte[4096];
        var received = new StringBuilder();
        while (!received.ToString().Contains("\r\n\r\n"))
        {
            var read = await ssl.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            received.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        var response = Encoding.ASCII.GetBytes("HTTP/1.1 201 Created\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await ssl.WriteAsync(response, cancellationToken);
        await ssl.FlushAsync(cancellationToken);
        return presented;
    }

    private async Task<X509Certificate2?> CreateFolderAndGetPresentedCertificateAsync(Dictionary<string, string?> extraOptions)
    {
        using var serverCertificate = CreateCertificate("CN=localhost", "1.3.6.1.5.5.7.3.1");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var server = ServeOneRequestAsync(listener, serverCertificate, cts.Token);

            var options = new Dictionary<string, string?>
            {
                ["use-ssl"] = "true",
                ["accept-any-ssl-certificate"] = "true"
            };
            foreach (var kv in extraOptions)
                options[kv.Key] = kv.Value;

            using (var backend = new WEBDAV($"webdav://127.0.0.1:{port}/folder/", options))
                await backend.CreateFolderAsync(cts.Token);

            return await server;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task PresentsConfiguredClientCertificate()
    {
        using var clientCertificate = CreateCertificate("CN=duplicati-webdav-client", "1.3.6.1.5.5.7.3.2");
        var pfxPath = Path.Combine(m_tempFolder, "client.pfx");
        System.IO.File.WriteAllBytes(pfxPath, clientCertificate.Export(X509ContentType.Pfx, PfxPassword));

        using var presented = await CreateFolderAndGetPresentedCertificateAsync(new Dictionary<string, string?>
        {
            ["webdav-client-certificate"] = pfxPath,
            ["webdav-client-certificate-password"] = PfxPassword
        });

        Assert.IsNotNull(presented, "The backend did not present a client certificate");
        Assert.AreEqual(clientCertificate.Thumbprint, presented!.Thumbprint);
    }

    [Test]
    public async Task PresentsNoClientCertificateByDefault()
    {
        using var presented = await CreateFolderAndGetPresentedCertificateAsync(new Dictionary<string, string?>());
        Assert.IsNull(presented, "The backend presented a client certificate without being asked to");
    }

    [Test]
    public void WrongPasswordIsReportedToTheUser()
    {
        using var clientCertificate = CreateCertificate("CN=duplicati-webdav-client", "1.3.6.1.5.5.7.3.2");
        var pfxPath = Path.Combine(m_tempFolder, "client.pfx");
        System.IO.File.WriteAllBytes(pfxPath, clientCertificate.Export(X509ContentType.Pfx, PfxPassword));

        var ex = Assert.Throws<UserInformationException>(() => new WEBDAV("webdav://127.0.0.1/folder/", new Dictionary<string, string?>
        {
            ["use-ssl"] = "true",
            ["webdav-client-certificate"] = pfxPath,
            ["webdav-client-certificate-password"] = "not-the-password"
        }).Dispose());

        StringAssert.Contains(pfxPath, ex!.Message);
        StringAssert.DoesNotContain("not-the-password", ex.Message);
    }
}
