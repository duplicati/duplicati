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

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using SharpAESCrypt;

namespace Duplicati.Library.Certificates;

/// <summary>
/// Helper class for serializing and deserializing certificates for database storage.
/// </summary>
public static class CertificateStorageHelper
{
    /// <summary>
    /// Serializes a certificate to a Base64-encoded string.
    /// </summary>
    /// <param name="certificate">The certificate to serialize.</param>
    /// <returns>A Base64-encoded string representation of the certificate.</returns>
    public static string SerializeCertificate(X509Certificate2 certificate)
    {
        if (certificate == null)
            throw new ArgumentNullException(nameof(certificate));

        return Convert.ToBase64String(certificate.RawData);
    }

    /// <summary>
    /// Deserializes a certificate from a Base64-encoded string.
    /// </summary>
    /// <param name="base64">The Base64-encoded certificate data.</param>
    /// <returns>The deserialized certificate.</returns>
    public static X509Certificate2 DeserializeCertificate(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            throw new ArgumentException("Certificate data cannot be null or empty.", nameof(base64));

        var bytes = Convert.FromBase64String(base64.Trim());
        return X509CertificateLoader.LoadCertificate(bytes);
    }

    /// <summary>
    /// Serializes a certificate with its private key to a password-protected Base64-encoded PFX string.
    /// </summary>
    /// <param name="certificate">The certificate to serialize.</param>
    /// <param name="password">The password to protect the PFX.</param>
    /// <returns>A Base64-encoded PFX string.</returns>
    public static string SerializeCertificateWithKey(X509Certificate2 certificate, string password)
    {
        if (certificate == null)
            throw new ArgumentNullException(nameof(certificate));

        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be null or empty.", nameof(password));

        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException("Certificate does not have a private key.");

        var pfxBytes = certificate.Export(X509ContentType.Pfx, password);
        return Convert.ToBase64String(pfxBytes);
    }

    /// <summary>
    /// Deserializes a certificate with its private key from a Base64-encoded PFX string.
    /// </summary>
    /// <param name="base64">The Base64-encoded PFX data.</param>
    /// <param name="password">The password to decrypt the PFX.</param>
    /// <returns>The deserialized certificate with private key.</returns>
    public static X509Certificate2 DeserializeCertificateWithKey(string base64, string password)
    {
        if (string.IsNullOrWhiteSpace(base64))
            throw new ArgumentException("Certificate data cannot be null or empty.", nameof(base64));

        var bytes = Convert.FromBase64String(base64.Trim());
        return X509CertificateLoader.LoadPkcs12(bytes, password, X509KeyStorageFlags.Exportable);
    }

    /// <summary>
    /// Encrypts a private key using a password.
    /// </summary>
    /// <param name="key">The ECDsa key to encrypt.</param>
    /// <param name="password">The password to encrypt with.</param>
    /// <returns>The encrypted private key as a Base64 string.</returns>
    public static string EncryptPrivateKey(ECDsa key, string password)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be null or empty.", nameof(password));

        var pkcs8Bytes = key.ExportPkcs8PrivateKey();
        try
        {
            var encryptedBytes = EncryptBytes(pkcs8Bytes, password);
            return Convert.ToBase64String(encryptedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8Bytes);
        }
    }

    /// <summary>
    /// Decrypts a private key using a password.
    /// </summary>
    /// <param name="encryptedBase64">The encrypted private key as a Base64 string.</param>
    /// <param name="password">The password to decrypt with.</param>
    /// <returns>The decrypted ECDsa key.</returns>
    public static ECDsa DecryptPrivateKey(string encryptedBase64, string password)
    {
        if (string.IsNullOrWhiteSpace(encryptedBase64))
            throw new ArgumentException("Encrypted key data cannot be null or empty.", nameof(encryptedBase64));

        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be null or empty.", nameof(password));

        var encryptedBytes = Convert.FromBase64String(encryptedBase64.Trim());
        var decryptedBytes = DecryptBytes(encryptedBytes, password);

        try
        {
            var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(decryptedBytes, out _);
            return key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decryptedBytes);
        }
    }

    /// <summary>
    /// Serializes a CA certificate pair (certificate and private key) for storage.
    /// </summary>
    /// <param name="caPair">The CA certificate pair.</param>
    /// <param name="password">The password to encrypt the private key.</param>
    /// <returns>A tuple containing the serialized certificate and encrypted key.</returns>
    public static (string CertificateBase64, string EncryptedKeyBase64) SerializeCACertificatePair(
        CACertificatePair caPair,
        string password)
    {
        if (caPair == null)
            throw new ArgumentNullException(nameof(caPair));

        var certBase64 = SerializeCertificate(caPair.Certificate);
        var encryptedKey = EncryptPrivateKey(caPair.PrivateKey, password);

        return (certBase64, encryptedKey);
    }

    /// <summary>
    /// Deserializes a CA certificate pair from storage.
    /// </summary>
    /// <param name="certificateBase64">The Base64-encoded certificate.</param>
    /// <param name="encryptedKeyBase64">The encrypted private key.</param>
    /// <param name="password">The password to decrypt the private key.</param>
    /// <returns>The CA certificate pair.</returns>
    public static CACertificatePair DeserializeCACertificatePair(
        string certificateBase64,
        string encryptedKeyBase64,
        string password)
    {
        var certificate = DeserializeCertificate(certificateBase64);
        var privateKey = DecryptPrivateKey(encryptedKeyBase64, password);

        return new CACertificatePair(certificate, privateKey);
    }

    /// <summary>
    /// Serializes a server certificate pair (certificate and private key) for storage.
    /// </summary>
    /// <param name="serverPair">The server certificate pair.</param>
    /// <param name="password">The password to protect the PFX.</param>
    /// <returns>A Base64-encoded PFX string.</returns>
    public static string SerializeServerCertificatePair(ServerCertificatePair serverPair, string password)
    {
        if (serverPair == null)
            throw new ArgumentNullException(nameof(serverPair));

        // On some platforms (e.g., macOS), X509CertificateLoader.LoadPkcs12 returns a certificate
        // with HasPrivateKey=true because the platform associates the key with the certificate.
        // To avoid "The certificate already has an associated private key" error, we load just
        // the certificate without any key before copying the private key.
        using var certOnly = X509CertificateLoader.LoadCertificate(serverPair.Certificate.RawData);
        using var certWithKey = certOnly.CopyWithPrivateKey(serverPair.PrivateKey);
        return SerializeCertificateWithKey(certWithKey, password);
    }

    /// <summary>
    /// Deserializes a server certificate pair from storage.
    /// </summary>
    /// <param name="base64">The Base64-encoded PFX data.</param>
    /// <param name="password">The password to decrypt the PFX.</param>
    /// <returns>The server certificate pair.</returns>
    public static ServerCertificatePair DeserializeServerCertificatePair(string base64, string password)
    {
        var certificate = DeserializeCertificateWithKey(base64, password);

        using var key = certificate.GetECDsaPrivateKey()
            ?? throw new InvalidOperationException("Certificate does not contain an ECDsa private key.");

        // Export and re-import to get a separate key instance
        var keyParams = key.ExportECPrivateKey();
        try
        {
            var newKey = ECDsa.Create();
            newKey.ImportECPrivateKey(keyParams, out _);
            return new ServerCertificatePair(certificate, newKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyParams);
        }
    }

    /// <summary>
    /// Exports a certificate to PEM format.
    /// </summary>
    /// <param name="certificate">The certificate to export.</param>
    /// <returns>The certificate in PEM format.</returns>
    public static string ExportToPem(X509Certificate2 certificate)
    {
        if (certificate == null)
            throw new ArgumentNullException(nameof(certificate));

        var base64 = Convert.ToBase64String(certificate.RawData);
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN CERTIFICATE-----");

        for (var i = 0; i < base64.Length; i += 64)
        {
            var lineLength = Math.Min(64, base64.Length - i);
            builder.AppendLine(base64[i..(i + lineLength)]);
        }

        builder.AppendLine("-----END CERTIFICATE-----");
        return builder.ToString();
    }

    /// <summary>
    /// Imports a certificate from PEM format.
    /// </summary>
    /// <param name="pem">The PEM-encoded certificate.</param>
    /// <returns>The imported certificate.</returns>
    public static X509Certificate2 ImportFromPem(string pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
            throw new ArgumentException("PEM data cannot be null or empty.", nameof(pem));

        // Extract the Base64 content between the PEM headers
        var lines = pem.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var base64Lines = lines
            .Where(l => !l.StartsWith("-----BEGIN", StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.StartsWith("-----END", StringComparison.OrdinalIgnoreCase))
            .Where(l => !string.IsNullOrWhiteSpace(l));

        var base64 = string.Concat(base64Lines);
        var bytes = Convert.FromBase64String(base64);

        return X509CertificateLoader.LoadCertificate(bytes);
    }

    #region Private Encryption Methods

    /// <summary>
    /// Encrypts data using SharpAESCrypt.
    /// </summary>
    private static byte[] EncryptBytes(byte[] data, string password)
    {
        using var inputStream = new MemoryStream(data);
        using var outputStream = new MemoryStream();
        AESCrypt.Encrypt(password, inputStream, outputStream, EncryptionOptions.Default with
        {
            InsertPlaceholder = false,
            InsertCreatedByIdentifier = false,
            InsertTimeStamp = false,
            LeaveOpen = true
        });
        return outputStream.ToArray();
    }

    /// <summary>
    /// Decrypts data using SharpAESCrypt.
    /// </summary>
    private static byte[] DecryptBytes(byte[] data, string password)
    {
        using var inputStream = new MemoryStream(data);
        using var outputStream = new MemoryStream();
        AESCrypt.Decrypt(password, inputStream, outputStream, DecryptionOptions.Default with
        {
            LeaveOpen = true
        });
        return outputStream.ToArray();
    }

    #endregion

    #region Password Generation

    /// <summary>
    /// Generates a secure random password for PFX bundles.
    /// </summary>
    /// <returns>A cryptographically secure random password.</returns>
    public static string GeneratePfxPassword()
        => Utility.Utility.Base64PlainToBase64Url(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).TrimEnd('=');

    #endregion
}
