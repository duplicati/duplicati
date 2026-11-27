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
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Utility;
using FluentFTP;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests that the certificate validator builds its chain with the intermediate
/// certificates the server sent, as the TLS stack does
/// </summary>
[TestFixture]
[Category("Certificates")]
public class SslServerChainTests
{
    /// <summary>
    /// A root, an intermediate signed by the root, and a server certificate for localhost signed by the intermediate
    /// </summary>
    private sealed record ServerChain(X509Certificate2 Root, X509Certificate2 Intermediate, X509Certificate2 Leaf) : IDisposable
    {
        public void Dispose()
        {
            Root.Dispose();
            Intermediate.Dispose();
            Leaf.Dispose();
        }
    }

    private static CertificateRequest CreateCaRequest(string subject, RSA key)
    {
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request;
    }

    private static ServerChain CreateServerChain()
    {
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(1);

        using var rootKey = RSA.Create(2048);
        using var root = CreateCaRequest("CN=Duplicati Test Root", rootKey).CreateSelfSigned(notBefore, notAfter);

        using var intermediateKey = RSA.Create(2048);
        using var intermediatePublic = CreateCaRequest("CN=Duplicati Test Intermediate", intermediateKey)
            .Create(root, notBefore, notAfter, [1, 2, 3, 4]);
        using var intermediate = intermediatePublic.CopyWithPrivateKey(intermediateKey);

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=localhost", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        leafRequest.CertificateExtensions.Add(san.Build());
        using var leafPublic = leafRequest.Create(intermediate, notBefore, notAfter, [5, 6, 7, 8]);
        using var leafWithKey = leafPublic.CopyWithPrivateKey(leafKey);

        // SChannel cannot use an ephemeral key for TLS, so round-trip the server certificate through a PFX
        return new ServerChain(
            X509CertificateLoader.LoadCertificate(root.RawData),
            X509CertificateLoader.LoadCertificate(intermediate.RawData),
            X509CertificateLoader.LoadPkcs12(leafWithKey.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable));
    }

    /// <summary>
    /// The chain policy of the TLS stack, with the given root as the trusted root instead of the OS trust store
    /// </summary>
    private static X509ChainPolicy TrustingPolicy(X509Certificate2 root)
    {
        var policy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck
        };
        policy.CustomTrustStore.Add(root);
        return policy;
    }

    /// <summary>
    /// Builds the chain as the TLS stack hands it to the validation callback: the certificates
    /// the server sent are in the extra store of the policy
    /// </summary>
    private static X509Chain BuildTlsChain(ServerChain certificates, X509Certificate2 trustedRoot)
    {
        var chain = new X509Chain { ChainPolicy = TrustingPolicy(trustedRoot) };
        chain.ChainPolicy.ExtraStore.Add(certificates.Intermediate);
        chain.Build(certificates.Leaf);
        return chain;
    }

    [TestCase(null)]
    [TestCase("00FF00FF")]
    public void ACertificateWhoseChainTheTlsStackBuiltIsAccepted(string? otherHash)
    {
        using var certificates = CreateServerChain();
        using var chain = BuildTlsChain(certificates, certificates.Root);
        Assert.That(chain.ChainStatus, Is.Empty, "The TLS stack builds the chain with the certificates the server sent");

        var validator = new SslCertificateValidator(false, otherHash == null ? null : [otherHash], true);
        Assert.That(validator.ValidateServerCertificate(this, certificates.Leaf, chain, SslPolicyErrors.None), Is.True);
    }

    [Test]
    public void ACertificateFromAnUntrustedRootIsStillRejected()
    {
        using var certificates = CreateServerChain();
        using var otherChain = CreateServerChain();
        // The chain ends at a root that is not trusted, but the callback is told there was no error
        using var chain = BuildTlsChain(certificates, otherChain.Root);

        var validator = new SslCertificateValidator(false, null, true);
        Assert.That(validator.ValidateServerCertificate(this, certificates.Leaf, chain, SslPolicyErrors.None), Is.False);
    }

    [Test]
    public void AnIntermediateFoundOnlyInTheChainElementsIsUsed()
    {
        using var certificates = CreateServerChain();
        using var chain = BuildTlsChain(certificates, certificates.Root);
        Assert.That(chain.ChainStatus, Is.Empty, "The TLS stack builds the chain with the certificates the server sent");

        // The TLS stack can find an intermediate elsewhere than in the certificates the server sent,
        // and then it is only in the elements of the chain, not in the extra store of the policy
        chain.ChainPolicy.ExtraStore.Clear();
        using (var policyOnly = new X509Chain { ChainPolicy = chain.ChainPolicy.Clone() })
            Assert.That(policyOnly.Build(certificates.Leaf), Is.False, "The policy alone does not reach the root");

        var validator = new SslCertificateValidator(false, null, true);
        Assert.That(validator.ValidateServerCertificate(this, certificates.Leaf, chain, SslPolicyErrors.None), Is.True);
    }

    [Test]
    public async Task AServerThatSendsItsIntermediateIsAcceptedOverTls()
    {
        using var certificates = CreateServerChain();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using var tcp = await listener.AcceptTcpClientAsync(cts.Token);
                using var ssl = new SslStream(tcp.GetStream(), false);
                try
                {
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificateContext = SslStreamCertificateContext.Create(certificates.Leaf, [certificates.Intermediate], offline: true),
                        EnabledSslProtocols = SslProtocols.None
                    }, cts.Token);
                }
                catch (Exception)
                {
                    // A rejected certificate is reported on the client side
                }
            });

            var validator = new SslCertificateValidator(false, null, true);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            using var clientSsl = new SslStream(client.GetStream(), false);
            await clientSsl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                CertificateChainPolicy = TrustingPolicy(certificates.Root),
                RemoteCertificateValidationCallback = validator.ValidateServerCertificate,
                EnabledSslProtocols = SslProtocols.None
            }, cts.Token);

            Assert.That(clientSsl.IsAuthenticated, Is.True);
            await server;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public void FtpWithoutCertificateOptionsAcceptsWhatTheTlsStackAccepted()
    {
        using var certificates = CreateServerChain();
        var ftp = new FTP("ftp://localhost/folder", new Dictionary<string, string?>());
        var e = new FtpSslValidationEventArgs
        {
            Certificate = certificates.Leaf,
            Chain = null,
            PolicyErrors = SslPolicyErrors.None
        };

        ftp.HandleValidateCertificate(null!, e);
        Assert.That(e.Accept, Is.True);
    }

    [TestCase(SslPolicyErrors.RemoteCertificateChainErrors)]
    [TestCase(SslPolicyErrors.RemoteCertificateNameMismatch)]
    public void FtpWithoutCertificateOptionsStillReportsARejectedCertificate(SslPolicyErrors errors)
    {
        using var certificates = CreateServerChain();
        var ftp = new FTP("ftp://localhost/folder", new Dictionary<string, string?>());
        var e = new FtpSslValidationEventArgs
        {
            Certificate = certificates.Leaf,
            Chain = null,
            PolicyErrors = errors
        };

        Assert.Throws<SslCertificateValidator.InvalidCertificateException>(() => ftp.HandleValidateCertificate(null!, e));
        Assert.That(e.Accept, Is.False);
    }

    /// <summary>
    /// The event a custom TLS stream, such as Bouncy Castle, gives: FluentFTP reports any error as a name mismatch
    /// </summary>
    private static FtpSslValidationEventArgs CustomStreamEvent(X509Certificate2 certificate, X509Chain chain, string message)
        => new FtpSslValidationEventArgs
        {
            Certificate = certificate,
            Chain = chain,
            PolicyErrors = SslPolicyErrors.RemoteCertificateNameMismatch,
            PolicyErrorMessage = message
        };

    [Test]
    public void FtpCustomStreamChainErrorIsReportedAsAChainError()
    {
        using var certificates = CreateServerChain();
        using var otherChain = CreateServerChain();
        using var chain = BuildTlsChain(certificates, otherChain.Root);
        Assert.That(chain.ChainStatus, Is.Not.Empty);

        var ftp = new FTP("ftp://localhost/folder", new Dictionary<string, string?>());
        var e = CustomStreamEvent(certificates.Leaf, chain, "Server certificate chain validation failed: UntrustedRoot");

        var ex = Assert.Throws<SslCertificateValidator.InvalidCertificateException>(() => ftp.HandleValidateCertificate(null!, e));
        Assert.That(ex!.SslError, Is.EqualTo(SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.That(e.Accept, Is.False);
    }

    [Test]
    public void FtpCustomStreamNameMismatchIsReportedAsANameMismatch()
    {
        using var certificates = CreateServerChain();
        using var chain = BuildTlsChain(certificates, certificates.Root);
        Assert.That(chain.ChainStatus, Is.Empty);

        var ftp = new FTP("ftp://example.com/folder", new Dictionary<string, string?>());
        var e = CustomStreamEvent(certificates.Leaf, chain, "Server certificate does not match the TLS target host.");

        var ex = Assert.Throws<SslCertificateValidator.InvalidCertificateException>(() => ftp.HandleValidateCertificate(null!, e));
        Assert.That(ex!.SslError, Is.EqualTo(SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.That(e.Accept, Is.False);
    }

    [Test]
    public void FtpCustomStreamReportsBothAChainErrorAndANameMismatch()
    {
        using var certificates = CreateServerChain();
        using var otherChain = CreateServerChain();
        using var chain = BuildTlsChain(certificates, otherChain.Root);

        var ftp = new FTP("ftp://example.com/folder", new Dictionary<string, string?>());
        var e = CustomStreamEvent(certificates.Leaf, chain, "Server certificate chain validation failed: UntrustedRoot; Server certificate does not match the TLS target host.");

        var ex = Assert.Throws<SslCertificateValidator.InvalidCertificateException>(() => ftp.HandleValidateCertificate(null!, e));
        Assert.That(ex!.SslError, Is.EqualTo(SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch));
    }
}
