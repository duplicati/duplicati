// Copyright (C) 2025, The Duplicati Team
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
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Duplicati.Library.Certificates;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Unit tests for the certificate generation and storage functionality.
/// </summary>
[TestFixture]
[Category("Certificates")]
public class CertificateTests
{
    #region CertificateGenerator Tests

    [Test]
    public void GenerateCACertificate_CreatesValidCA()
    {
        // Act
        var caPair = CertificateGenerator.GenerateCACertificate();

        // Assert
        Assert.IsNotNull(caPair);
        Assert.IsNotNull(caPair.Certificate);
        Assert.IsNotNull(caPair.PrivateKey);

        var cert = caPair.Certificate;
        Assert.IsTrue(cert.HasPrivateKey);
        Assert.AreEqual("CN=Duplicati Local CA, O=Duplicati, C=US", cert.Subject);

        // Check basic constraints (must be CA)
        var basicConstraints = cert.Extensions["2.5.29.19"] as X509BasicConstraintsExtension;
        Assert.IsNotNull(basicConstraints);
        Assert.IsTrue(basicConstraints!.CertificateAuthority);
        Assert.IsTrue(basicConstraints.HasPathLengthConstraint);
        Assert.AreEqual(0, basicConstraints.PathLengthConstraint);

        // Check key usage
        var keyUsage = cert.Extensions["2.5.29.15"] as X509KeyUsageExtension;
        Assert.IsNotNull(keyUsage);
        Assert.IsTrue((keyUsage!.KeyUsages & X509KeyUsageFlags.KeyCertSign) == X509KeyUsageFlags.KeyCertSign);
        Assert.IsTrue((keyUsage.KeyUsages & X509KeyUsageFlags.CrlSign) == X509KeyUsageFlags.CrlSign);

        // Check validity (approximately 10 years)
        var validity = cert.NotAfter - cert.NotBefore;
        Assert.That(validity.TotalDays, Is.GreaterThan(365 * 9));
        Assert.That(validity.TotalDays, Is.LessThan(365 * 11));

        // Check that it's self-signed
        Assert.AreEqual(cert.Subject, cert.Issuer);
    }

    [Test]
    public void GenerateServerCertificate_CreatesValidServerCert()
    {
        // Arrange
        var caPair = CertificateGenerator.GenerateCACertificate();
        var hostnames = new[] { "localhost", "127.0.0.1", "test.example.com" };

        // Act
        var serverPair = CertificateGenerator.GenerateServerCertificate(
            caPair.Certificate,
            caPair.PrivateKey,
            hostnames);

        // Assert
        Assert.IsNotNull(serverPair);
        Assert.IsNotNull(serverPair.Certificate);
        Assert.IsNotNull(serverPair.PrivateKey);

        var cert = serverPair.Certificate;
        Assert.IsTrue(cert.HasPrivateKey);
        Assert.AreEqual("CN=Duplicati Server, O=Duplicati, C=US", cert.Subject);
        Assert.AreEqual(caPair.Certificate.Subject, cert.Issuer);

        // Check basic constraints (must NOT be CA)
        var basicConstraints = cert.Extensions["2.5.29.19"] as X509BasicConstraintsExtension;
        Assert.IsNotNull(basicConstraints);
        Assert.IsFalse(basicConstraints!.CertificateAuthority);

        // Check key usage
        var keyUsage = cert.Extensions["2.5.29.15"] as X509KeyUsageExtension;
        Assert.IsNotNull(keyUsage);
        Assert.IsTrue((keyUsage!.KeyUsages & X509KeyUsageFlags.DigitalSignature) == X509KeyUsageFlags.DigitalSignature);

        // Check extended key usage
        var enhancedKeyUsage = cert.Extensions["2.5.29.37"] as X509EnhancedKeyUsageExtension;
        Assert.IsNotNull(enhancedKeyUsage);
        Assert.That(enhancedKeyUsage!.EnhancedKeyUsages, Has.Some.Matches<Oid>(oid => oid.Value == "1.3.6.1.5.5.7.3.1"));

        // Check validity (approximately 90 days for server certificates)
        var validity = cert.NotAfter - cert.NotBefore;
        Assert.That(validity.TotalDays, Is.GreaterThan(85));
        Assert.That(validity.TotalDays, Is.LessThan(95));
    }

    [Test]
    public void GenerateServerCertificate_IncludesAllSANs()
    {
        // Arrange
        var caPair = CertificateGenerator.GenerateCACertificate();
        var hostnames = new[] { "localhost", "127.0.0.1", "::1", "test.example.com", "192.168.1.100" };

        // Act
        var serverPair = CertificateGenerator.GenerateServerCertificate(
            caPair.Certificate,
            caPair.PrivateKey,
            hostnames);

        // Assert
        var sanExtension = serverPair.Certificate.Extensions["2.5.29.17"] as X509SubjectAlternativeNameExtension;
        Assert.IsNotNull(sanExtension);

        var dnsNames = sanExtension!.EnumerateDnsNames().ToList();
        var ipAddresses = sanExtension.EnumerateIPAddresses().ToList();

        Assert.That(dnsNames, Contains.Item("localhost"));
        Assert.That(dnsNames, Contains.Item("test.example.com"));

        Assert.That(ipAddresses, Has.Some.Matches<IPAddress>(ip => ip.ToString() == "127.0.0.1"));
        Assert.That(ipAddresses, Has.Some.Matches<IPAddress>(ip => ip.ToString() == "::1"));
        Assert.That(ipAddresses, Has.Some.Matches<IPAddress>(ip => ip.ToString() == "192.168.1.100"));
    }

    [Test]
    public void GenerateServerCertificate_ChainVerifies()
    {
        // Arrange
        var caPair = CertificateGenerator.GenerateCACertificate();
        var hostnames = new[] { "localhost" };

        var serverPair = CertificateGenerator.GenerateServerCertificate(
            caPair.Certificate,
            caPair.PrivateKey,
            hostnames);

        // Act & Assert - Build chain
        using var chain = new X509Chain();
        chain.ChainPolicy.ExtraStore.Add(caPair.Certificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

        var chainBuilt = chain.Build(serverPair.Certificate);
        Assert.IsTrue(chainBuilt, "Certificate chain should verify");
        Assert.That(chain.ChainElements, Has.Count.GreaterThan(1));
        Assert.AreEqual(caPair.Certificate.Thumbprint, chain.ChainElements[1].Certificate.Thumbprint);
    }

    [Test]
    [Platform(Exclude = "MacOsX", Reason = "macOS keychain export limitations")]
    public void CreatePfxBundle_CreatesValidPfx()
    {
        // Arrange
        var caPair = CertificateGenerator.GenerateCACertificate();
        var hostnames = new[] { "localhost" };
        var serverPair = CertificateGenerator.GenerateServerCertificate(
            caPair.Certificate,
            caPair.PrivateKey,
            hostnames);

        var password = "testpassword123";

        // Act
        var pfxBytes = CertificateGenerator.CreatePfxBundle(serverPair, caPair, password);

        // Assert
        Assert.IsNotNull(pfxBytes);
        Assert.That(pfxBytes.Length, Is.GreaterThan(0));

        // Try to load the PFX
        var loadedCert = X509CertificateLoader.LoadPkcs12(pfxBytes, password);
        Assert.IsNotNull(loadedCert);
        Assert.AreEqual(serverPair.Certificate.Thumbprint, loadedCert.Thumbprint);
    }

    [Test]
    public void IsExpiringSoon_ReturnsTrueForExpiredCertificate()
    {
        // Create a certificate that expired yesterday
        var caPair = CertificateGenerator.GenerateCACertificate();
        Assert.IsFalse(CertificateGenerator.IsExpiringSoon(caPair.Certificate, 30));
    }

    #endregion

    #region HostnameDetector Tests

    [Test]
    public void DetectHostnames_IncludesAlwaysIncludedHostnames()
    {
        // Act
        var hostnames = HostnameDetector.DetectHostnames();

        // Assert
        Assert.That(hostnames, Contains.Item("localhost"));
        Assert.That(hostnames, Contains.Item("127.0.0.1"));
        Assert.That(hostnames, Contains.Item("::1"));
    }

    [Test]
    public void DetectHostnames_IncludesMachineName()
    {
        // Act
        var hostnames = HostnameDetector.DetectHostnames();

        // Assert
        Assert.That(hostnames, Contains.Item(Environment.MachineName));
    }

    [Test]
    public void DetectHostnames_NoDuplicates()
    {
        // Act
        var hostnames = HostnameDetector.DetectHostnames();

        // Assert
        var distinctCount = hostnames.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.AreEqual(distinctCount, hostnames.Count);
    }

    [Test]
    public void ValidateHostnames_AcceptsValidHostnames()
    {
        // Arrange
        var input = new[] { "localhost", "example.com", "192.168.1.1", "::1" };

        // Act
        var result = HostnameDetector.ValidateHostnames(input);

        // Assert
        Assert.That(result, Contains.Item("localhost"));
        Assert.That(result, Contains.Item("example.com"));
        Assert.That(result, Contains.Item("192.168.1.1"));
        Assert.That(result, Contains.Item("::1"));
    }

    [Test]
    public void ValidateHostnames_RejectsInvalidHostnames()
    {
        // Arrange
        var input = new[] { "", "   ", "not valid", "also@invalid" };

        // Act
        var result = HostnameDetector.ValidateHostnames(input);

        // Assert
        Assert.IsEmpty(result);
    }

    [Test]
    public void ValidateHostnames_TrimsWhitespace()
    {
        // Arrange
        var input = new[] { "  localhost  ", "  example.com  " };

        // Act
        var result = HostnameDetector.ValidateHostnames(input);

        // Assert
        Assert.That(result, Contains.Item("localhost"));
        Assert.That(result, Contains.Item("example.com"));
    }

    #endregion

    #region CertificateStorageHelper Tests

    [Test]
    public void SerializeDeserializeCertificate_RoundTripsCorrectly()
    {
        // Arrange
        var caPair = CertificateGenerator.GenerateCACertificate();
        var originalCert = caPair.Certificate;

        // Act
        var serialized = CertificateStorageHelper.SerializeCertificate(originalCert);
        var deserialized = CertificateStorageHelper.DeserializeCertificate(serialized);

        // Assert
        Assert.AreEqual(originalCert.Thumbprint, deserialized.Thumbprint);
        Assert.AreEqual(originalCert.Subject, deserialized.Subject);
        Assert.AreEqual(originalCert.Issuer, deserialized.Issuer);
    }

    [Test]
    [Platform(Exclude = "MacOsX", Reason = "macOS keychain export limitations")]
    public void SerializeDeserializeCertificateWithKey_RoundTripsCorrectly()
    {
        // Arrange
        var caPair = CertificateGenerator.GenerateCACertificate();
        var password = "testpassword123";

        // Act
        var serialized = CertificateStorageHelper.SerializeCertificateWithKey(caPair.Certificate, password);
        var deserialized = CertificateStorageHelper.DeserializeCertificateWithKey(serialized, password);

        // Assert
        Assert.AreEqual(caPair.Certificate.Thumbprint, deserialized.Thumbprint);
        Assert.IsTrue(deserialized.HasPrivateKey);
    }

    [Test]
    public void EncryptDecryptPrivateKey_RoundTripsCorrectly()
    {
        // Arrange
        var caPair = CertificateGenerator.GenerateCACertificate();
        var originalKey = caPair.PrivateKey;
        var password = "encryptionpassword";

        // Act
        var encrypted = CertificateStorageHelper.EncryptPrivateKey(originalKey, password);
        var decrypted = CertificateStorageHelper.DecryptPrivateKey(encrypted, password);

        // Assert
        var originalParams = originalKey.ExportParameters(true);
        var decryptedParams = decrypted.ExportParameters(true);

        Assert.AreEqual(Convert.ToBase64String(originalParams.D!),
            Convert.ToBase64String(decryptedParams.D!));
    }

    [Test]
    public void SerializeDeserializeCACertificatePair_RoundTripsCorrectly()
    {
        // Arrange
        var caPair = CertificateGenerator.GenerateCACertificate();
        var password = "testpassword123";

        // Act
        var (certBase64, encryptedKey) = CertificateStorageHelper.SerializeCACertificatePair(caPair, password);
        var deserialized = CertificateStorageHelper.DeserializeCACertificatePair(certBase64, encryptedKey, password);

        // Assert
        Assert.AreEqual(caPair.Certificate.Thumbprint, deserialized.Certificate.Thumbprint);

        var originalParams = caPair.PrivateKey.ExportParameters(true);
        var deserializedParams = deserialized.PrivateKey.ExportParameters(true);
        Assert.AreEqual(Convert.ToBase64String(originalParams.D!),
            Convert.ToBase64String(deserializedParams.D!));
    }

    [Test]
    [Platform(Exclude = "MacOsX", Reason = "macOS keychain export limitations")]
    public void SerializeDeserializeServerCertificatePair_RoundTripsCorrectly()
    {
        // Arrange
        var caPair = CertificateGenerator.GenerateCACertificate();
        var hostnames = new[] { "localhost" };
        var serverPair = CertificateGenerator.GenerateServerCertificate(
            caPair.Certificate,
            caPair.PrivateKey,
            hostnames);
        var password = "testpassword123";

        // Act
        var serialized = CertificateStorageHelper.SerializeServerCertificatePair(serverPair, password);
        var deserialized = CertificateStorageHelper.DeserializeServerCertificatePair(serialized, password);

        // Assert
        Assert.AreEqual(serverPair.Certificate.Thumbprint, deserialized.Certificate.Thumbprint);
        Assert.IsTrue(deserialized.Certificate.HasPrivateKey);
    }

    [Test]
    public void ExportImportPem_RoundTripsCorrectly()
    {
        // Arrange
        var caPair = CertificateGenerator.GenerateCACertificate();
        var originalCert = caPair.Certificate;

        // Act
        var pem = CertificateStorageHelper.ExportToPem(originalCert);
        var imported = CertificateStorageHelper.ImportFromPem(pem);

        // Assert
        Assert.AreEqual(originalCert.Thumbprint, imported.Thumbprint);
        Assert.IsTrue(pem.StartsWith("-----BEGIN CERTIFICATE-----"));
        Assert.IsTrue(pem.EndsWith("-----END CERTIFICATE-----" + Environment.NewLine));
    }

    [Test]
    [Platform(Exclude = "MacOsX", Reason = "macOS keychain export limitations")]
    public void SerializeCertificateWithKey_WrongPassword_ThrowsException()
    {
        // Arrange
        var caPair = CertificateGenerator.GenerateCACertificate();
        var correctPassword = "correctpassword";
        var wrongPassword = "wrongpassword";

        var serialized = CertificateStorageHelper.SerializeCertificateWithKey(caPair.Certificate, correctPassword);

        // Act & Assert
        Assert.Throws<CryptographicException>(() =>
            CertificateStorageHelper.DeserializeCertificateWithKey(serialized, wrongPassword));
    }

    [Test]
    public void DecryptPrivateKey_WrongPassword_ThrowsException()
    {
        // Arrange
        var caPair = CertificateGenerator.GenerateCACertificate();
        var correctPassword = "correctpassword";
        var wrongPassword = "wrongpassword";

        var encrypted = CertificateStorageHelper.EncryptPrivateKey(caPair.PrivateKey, correctPassword);

        // Act & Assert
        Assert.Throws<SharpAESCrypt.WrongPasswordException>(() =>
            CertificateStorageHelper.DecryptPrivateKey(encrypted, wrongPassword));
    }

    #endregion

    #region CATrustInstallerFactory Tests

    [Test]
    public void CreateInstaller_ReturnsInstaller()
    {
        // Act
        var installer = CATrustInstallerFactory.CreateInstaller(null, null, null);

        // Assert
        Assert.IsNotNull(installer);
        Assert.IsTrue(installer.CanInstall());
    }

    [Test]
    public void IsPlatformSupported_ReturnsTrue()
    {
        // Act & Assert
        Assert.IsTrue(CATrustInstallerFactory.IsPlatformSupported());
    }

    #endregion
}
