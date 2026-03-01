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

using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Duplicati.Library.Certificates;

/// <summary>
/// Represents a certificate authority with its certificate and private key.
/// </summary>
/// <param name="Certificate">The CA certificate.</param>
/// <param name="PrivateKey">The CA private key.</param>
public record CACertificatePair(X509Certificate2 Certificate, ECDsa PrivateKey);

/// <summary>
/// Represents a server certificate with its certificate and private key.
/// </summary>
/// <param name="Certificate">The server certificate.</param>
/// <param name="PrivateKey">The server private key.</param>
public record ServerCertificatePair(X509Certificate2 Certificate, ECDsa PrivateKey);

/// <summary>
/// Generates certificates for HTTPS server using ECDSA P-256 keys.
/// </summary>
public static class CertificateGenerator
{
    /// <summary>
    /// Time a CA is valid, for now 10 years.
    /// </summary>
    private static readonly TimeSpan CA_VALIDITY = TimeSpan.FromDays(365 * 10);
    /// <summary>
    /// Time a server certificate is valid, for now 90 days to satisfy Browser limits.
    /// </summary>
    private static readonly TimeSpan SERVER_VALIDITY = TimeSpan.FromDays(90);
    /// <summary>
    /// Common name for the CA certificate.
    /// </summary>
    private static readonly string CA_COMMON_NAME = $"{AutoUpdater.AutoUpdateSettings.AppName} Local CA";
    /// <summary>
    /// Common name for the server certificate.
    /// </summary>
    private static readonly string SERVER_COMMON_NAME = $"{AutoUpdater.AutoUpdateSettings.AppName} Server";

    /// <summary>
    /// Generates a self-signed CA certificate with CA constraints.
    /// </summary>
    /// <returns>A CA certificate pair containing the certificate and private key.</returns>
    public static CACertificatePair GenerateCACertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var request = new CertificateRequest(
            $"CN={CA_COMMON_NAME}, O=Duplicati, C=US",
            key,
            HashAlgorithmName.SHA256);

        // Add CA basic constraints with pathLenConstraint=0 (this CA cannot sign other CAs)
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: true,
                hasPathLengthConstraint: true,
                pathLengthConstraint: 0,
                critical: true));

        // Key usage for CA
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                critical: true));

        // Subject Key Identifier
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(
                request.PublicKey,
                critical: false));

        // Not before now, not after validity
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5); // Allow for clock skew
        var notAfter = DateTimeOffset.UtcNow.Add(CA_VALIDITY);

        // Create self-signed certificate
        var certificate = request.CreateSelfSigned(notBefore, notAfter);

        // Export and re-import to make it exportable with private key
        // Use a temporary password to avoid unprotected key material in memory
        var tempPassword = CertificateStorageHelper.GeneratePfxPassword();
        var export = certificate.Export(X509ContentType.Pfx, tempPassword);
        var certWithKey = X509CertificateLoader.LoadPkcs12(export, tempPassword);
        CryptographicOperations.ZeroMemory(export);

        // Create a new ECDsa instance with the same key parameters for the return value
        // since we're disposing the original key
        var keyParams = key.ExportECPrivateKey();
        var newKey = ECDsa.Create();
        newKey.ImportECPrivateKey(keyParams, out _);
        CryptographicOperations.ZeroMemory(keyParams);

        return new CACertificatePair(certWithKey, newKey);
    }

    /// <summary>
    /// Generates a server certificate signed by the CA.
    /// </summary>
    /// <param name="caCert">The CA certificate.</param>
    /// <param name="caKey">The CA private key.</param>
    /// <param name="hostnames">The hostnames and IP addresses for the Subject Alternative Names (SANs).</param>
    /// <returns>A server certificate pair containing the certificate and private key.</returns>
    public static ServerCertificatePair GenerateServerCertificate(
        X509Certificate2 caCert,
        ECDsa caKey,
        IEnumerable<string> hostnames)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var request = new CertificateRequest(
            $"CN={SERVER_COMMON_NAME}, O=Duplicati, C=US",
            key,
            HashAlgorithmName.SHA256);

        // Basic constraints - not a CA
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));

        // Key usage for server
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        // Extended key usage for TLS server
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection
                {
                    new Oid("1.3.6.1.5.5.7.3.1", "Server Authentication")
                },
                critical: false));

        // Subject Key Identifier
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(
                request.PublicKey,
                critical: false));

        // Authority Key Identifier
        // Only need to check for the Subject Key Identifier extension or public key availability
        var skiExtension = caCert.Extensions["2.5.29.14"];
        if (skiExtension != null || caCert.GetECDsaPublicKey() != null)
        {
            request.CertificateExtensions.Add(
                AuthorityKeyIdentifierExtension.Create(
                    caCert,
                    false));
        }

        // Subject Alternative Names (SANs)
        var sanBuilder = new SubjectAlternativeNameBuilder();
        foreach (var hostname in hostnames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IPAddress.TryParse(hostname, out var ipAddress))
                sanBuilder.AddIpAddress(ipAddress);
            else
                sanBuilder.AddDnsName(hostname);
        }
        request.CertificateExtensions.Add(sanBuilder.Build());

        // Not before now (with clock skew buffer), not after validity
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = DateTimeOffset.UtcNow.Add(SERVER_VALIDITY);

        // Get the CA certificate with private key for signing
        X509Certificate2 caCertWithKey;
        IDisposable? certToDispose = null;
        if (caCert.HasPrivateKey)
        {
            caCertWithKey = caCert;
        }
        else
        {
            caCertWithKey = caCert.CopyWithPrivateKey(caKey);
            certToDispose = caCertWithKey;
        }

        try
        {
            // Create certificate signed by CA
            var serialNumber = RandomNumberGenerator.GetBytes(8);

            var certificate = request.Create(
                caCertWithKey,
                notBefore,
                notAfter,
                serialNumber);

            // Export and re-import with private key
            var certWithKey = certificate.CopyWithPrivateKey(key);

            // Export to PFX format to get a usable certificate with private key
            // Use a temporary password to avoid unprotected key material in memory
            var tempPassword = CertificateStorageHelper.GeneratePfxPassword();
            var export = certWithKey.Export(X509ContentType.Pfx, tempPassword);
            var finalCert = X509CertificateLoader.LoadPkcs12(export, tempPassword);
            CryptographicOperations.ZeroMemory(export);

            // Create a new ECDsa instance with the same key parameters
            var keyParams = key.ExportECPrivateKey();
            var newKey = ECDsa.Create();
            newKey.ImportECPrivateKey(keyParams, out _);
            CryptographicOperations.ZeroMemory(keyParams);

            return new ServerCertificatePair(finalCert, newKey);
        }
        finally
        {
            certToDispose?.Dispose();
        }
    }

    /// <summary>
    /// Creates a PFX bundle containing the server certificate, its private key, and the CA certificate chain.
    /// </summary>
    /// <param name="serverCert">The server certificate.</param>
    /// <param name="caCert">The CA certificate.</param>
    /// <param name="password">The password to protect the PFX file.</param>
    /// <returns>The PFX bundle as a byte array.</returns>
    public static byte[] CreatePfxBundle(
        X509Certificate2 serverCert,
        X509Certificate2 caCert,
        string password)
    {
        // Create a collection with both certificates
        var collection = new X509Certificate2Collection
        {
            serverCert,
            caCert
        };

        // Export as PFX with the private key
        return collection.Export(X509ContentType.Pfx, password)!;
    }

    /// <summary>
    /// Creates a PFX bundle containing the server certificate, its private key, and the CA certificate chain.
    /// </summary>
    /// <param name="serverCert">The server certificate pair.</param>
    /// <param name="caCert">The CA certificate pair.</param>
    /// <param name="password">The password to protect the PFX file.</param>
    /// <returns>The PFX bundle as a byte array.</returns>
    public static byte[] CreatePfxBundle(
        ServerCertificatePair serverCert,
        CACertificatePair caCert,
        string password)
    {
        return CreatePfxBundle(serverCert.Certificate, caCert.Certificate, password);
    }

    /// <summary>
    /// Checks if a certificate is expired or will expire within the specified number of days.
    /// </summary>
    /// <param name="certificate">The certificate to check.</param>
    /// <param name="daysBeforeExpiration">Number of days before expiration to consider as expiring.</param>
    /// <returns>True if the certificate is expired or will expire within the specified days.</returns>
    public static bool IsExpiringSoon(X509Certificate2 certificate, int daysBeforeExpiration = 30)
    {
        return certificate.NotAfter < DateTimeOffset.UtcNow.AddDays(daysBeforeExpiration);
    }
}

/// <summary>
/// Helper class for creating Authority Key Identifier extensions.
/// </summary>
internal static class AuthorityKeyIdentifierExtension
{
    /// <summary>
    /// Creates an Authority Key Identifier extension from a CA certificate.
    /// </summary>
    /// <param name="caCert">The CA certificate.</param>
    /// <param name="critical">Whether the extension is critical.</param>
    /// <returns>An Authority Key Identifier extension.</returns>
    public static X509Extension Create(X509Certificate2 caCert, bool critical)
    {
        // Calculate the Subject Key Identifier from the CA certificate
        var subjectKeyId = GetSubjectKeyIdentifier(caCert);

        if (subjectKeyId == null)
        {
            // Return empty extension if no subject key ID available
            return new X509Extension(
                "2.5.29.35",
                new byte[] { 0x30, 0x00 }, // Empty SEQUENCE
                critical);
        }

        // Build the AuthorityKeyIdentifier ASN.1 structure
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();

        // keyIdentifier [0] IMPLICIT OCTET STRING OPTIONAL
        // Write as context-specific tagged octet string
        writer.WriteOctetString(subjectKeyId, new Asn1Tag(TagClass.ContextSpecific, 0, true));

        writer.PopSequence();

        return new X509Extension(
            "2.5.29.35", // Authority Key Identifier OID
            writer.Encode(),
            critical);
    }

    /// <summary>
    /// Gets the Subject Key Identifier from a certificate.
    /// </summary>
    /// <param name="certificate">The certificate to get the SKI from.</param>
    /// <returns>The Subject Key Identifier as a byte array, or null if not available.</returns>
    private static byte[]? GetSubjectKeyIdentifier(X509Certificate2 certificate)
    {
        var skiExtension = certificate.Extensions["2.5.29.14"];
        if (skiExtension is X509SubjectKeyIdentifierExtension ski && ski.SubjectKeyIdentifier != null)
            return Convert.FromHexString(ski.SubjectKeyIdentifier);

        // Calculate SKI from public key if not present
        using var key = certificate.GetECDsaPublicKey();
        if (key != null)
            return SHA1.HashData(key.ExportSubjectPublicKeyInfo());

        return null;
    }
}
