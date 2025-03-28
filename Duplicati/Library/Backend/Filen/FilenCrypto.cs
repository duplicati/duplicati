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

using System.Security.Cryptography;
using System.Text;

namespace Duplicati.Library.Backend.Filen;

/// <summary>
/// Represents the root keys for the account.
/// </summary>
/// <param name="MasterKey">The master key for encryption.</param>
/// <param name="Password">The password for the account.</param>
public sealed record AccountRootKeys(DerivedKey MasterKey, string Password);

/// <summary>
/// Represents a derived key.
/// </summary>
/// <param name="Key">The source key
/// <param name="Derived">The derived key.</param>
public sealed record DerivedKey(string Key, byte[] Derived)
{
    /// <summary>
    /// Creates a new derived key.
    /// </summary>
    /// <param name="key">The key to derive.</param>
    /// <returns>The derived key.</returns>
    public static DerivedKey Create(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty.", nameof(key));
        return new DerivedKey(key, FilenCrypto.DeriveKeyFromPassword(key, key, 1, 256));
    }

    /// <summary>
    /// Decrypts the a metadata string
    /// </summary>
    /// <param name="metadata">The metadata to decrypt.</param>
    /// <returns>The dectypred string</returns>
    public string DecryptMetadata(string metadata)
        => FilenCrypto.DecryptMetadata(metadata, this);

    /// <summary>
    /// Encrypts a metadata string.
    /// </summary>
    /// <param name="metadata">The metadata to encrypt.</param>
    /// <returns>The encrypted string.</returns>
    public string EncryptMetadata(string metadata)
        => FilenCrypto.EncryptMetadata(metadata, this);

    /// <summary>
    /// Encrypts a chunk of data.
    /// </summary>
    /// <param name="data">The data to encrypt</param>
    /// <returns>The encrypted data.</returns>
    public byte[] EncryptData(Span<byte> data)
        => FilenCrypto.EncryptData(data, this);

    /// <summary>
    /// Decrypts a chunk of data.
    /// </summary>
    /// <param name="version">The version of the encryption.</param>
    /// <param name="data">The data to decrypt.</param>
    /// <returns>The decrypted data.</returns>
    public byte[] DecryptData(int version, Span<byte> data)
        => FilenCrypto.DecryptData(version, data, this);
}

/// <summary>
/// Provides cryptographic functions for the Filen backend.
/// </summary>
public static class FilenCrypto
{
    /// <summary>
    /// The size of the GCM tag.
    /// </summary>
    public const int GcmTagSize = 16;

    /// <summary>
    /// The log tag for the class.
    /// </summary>
    private static string LOGTAG = Logging.Log.LogTagFromType(typeof(FilenCrypto));
    /// <summary>
    /// The base64 charset used for generating random strings.
    /// </summary>
    private static readonly string Base64Charset = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    /// <summary>
    /// Generates/derives the password and master key based on the auth version. Auth Version 1 is deprecated and no longer in use.
    /// </summary>
    /// <param name="rawPassword">The raw user password.</param>
    /// <param name="authVersion">The authentication version.</param>
    /// <param name="salt">The salt value.</param>
    /// <returns>A Task containing the derived password and master keys.</returns>
    public static AccountRootKeys GeneratePasswordAndMasterKeyBasedOnAuthVersion(string rawPassword, int authVersion, string salt)
    {
        if (authVersion != 2)
            throw new NotSupportedException("Auth version 2 the only supported version.");

        string derivedKey = Convert.ToHexString(DeriveKeyFromPassword(rawPassword, salt, 200000, 512)).ToLowerInvariant();
        string derivedPassword = derivedKey.Substring(derivedKey.Length / 2);
        string derivedMasterKey = derivedKey.Substring(0, derivedKey.Length / 2);

        return new AccountRootKeys(DerivedKey.Create(derivedMasterKey), CreateSHA512Hash(derivedPassword));
    }

    /// <summary>
    /// Creates a SHA512 hash of the input string.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <returns>The SHA512 hash as a hexadecimal string.</returns>
    private static string CreateSHA512Hash(string input)
        => Convert.ToHexString(
            SHA512.HashData(
                Encoding.UTF8.GetBytes(input)))
            .ToLowerInvariant();

    /// <summary>
    /// Generates a random string (utf-8 compatible).
    /// </summary>
    /// <param name="length">The length of the string.</param>
    /// <returns>The random string.</returns>
    public static string GenerateRandomString(int length = 32)
        => new(GenerateRandomBytes(length).Select(b => Base64Charset[b % Base64Charset.Length]).ToArray());

    /// <summary>
    /// Generates random bytes.
    /// </summary>
    /// <param name="length">The length of the bytes.</param>
    /// <returns>The random bytes.</returns>
    public static byte[] GenerateRandomBytes(int length = 32)
    {
        var buffer = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(buffer);
        return buffer;
    }

    /// <summary>
    /// Derives a key from a password.
    /// </summary>
    /// <param name="password">The password.</param>
    /// <param name="salt">The salt.</param>
    /// <param name="iterations">The number of iterations.</param>
    /// <param name="bitLength">The bit length of the returned key.</param>
    /// <returns>The derived key.</returns>
    public static byte[] DeriveKeyFromPassword(string password, string salt, int iterations, int bitLength)
    {
        using var deriveBytes = new Rfc2898DeriveBytes(password, Encoding.UTF8.GetBytes(salt), iterations, HashAlgorithmName.SHA512);
        return deriveBytes.GetBytes(bitLength / 8);
    }

    /// <summary>
    /// Hashes a chunk of data using SHA512.
    /// </summary>
    /// <param name="input">The input data.</param>
    /// <returns>The SHA512 hash as a hexadecimal string.</returns>
    public static string HashChunk(Span<byte> input)
        => Convert.ToHexString(SHA512.HashData(input)).ToLowerInvariant();

    /// <summary>
    /// Hashes a string using SHA1 and SHA512.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <returns>The hash as a hexadecimal string.</returns>
    public static string HashFn(Span<byte> input)
        => Convert.ToHexString(SHA1.HashData(SHA512.HashData(input))).ToLowerInvariant();

    /// <summary>
    /// Hashes a string using SHA1 and SHA512.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <returns>The hash as a hexadecimal string.</returns>
    public static string HashFn(string input)
        => HashFn(Encoding.UTF8.GetBytes(input.ToLowerInvariant()));

    /// <summary>
    /// Encrypts a metadata string.
    /// </summary>
    /// <param name="metadata">The metadata to encrypt.</param>
    /// <param name="key">The key to use for encryption.</param>
    /// <returns>The encrypted metadata.</returns>
    public static string EncryptMetadata(string metadata, DerivedKey key)
    {
        var iv = GenerateRandomString(12);
        var combined = AesGcmEncrypt(
            key.Derived,
            Encoding.UTF8.GetBytes(iv),
            Encoding.UTF8.GetBytes(metadata));
        return $"002{iv}{Convert.ToBase64String(combined)}";
    }

    /// <summary>
    /// Decrypts the a metadata string
    /// </summary>
    /// <param name="metadata">The metadata to decrypt.</param>
    /// <param name="key">The derived key.</param>
    /// <returns>The dectypred string</returns>
    public static string DecryptMetadata(string metadata, DerivedKey key)
    {
        string sliced = metadata.Substring(0, Math.Min(8, metadata.Length));

        if (sliced == "U2FsdGVk")
            throw new NotImplementedException("Version 1 decryption is not implemented.");

        string version = metadata.Substring(0, Math.Min(3, metadata.Length));

        if (version == "002")
        {
            var keyBuffer = key.Derived;
            var ivBuffer = Encoding.UTF8.GetBytes(metadata.Substring(3, 12));
            var encrypted = Convert.FromBase64String(metadata.Substring(15));

            return Encoding.UTF8.GetString(AesGcmDecrypt(keyBuffer, ivBuffer, encrypted));
        }
        else if (version == "003")
        {
            Logging.Log.WriteVerboseMessage(LOGTAG, "EncV3NotTested", "Encryption version 3 encoutered for metadata, but this is not tested.");
            if (key.Key.Length != 64)
                throw new ArgumentException($"Invalid key length {key.Key.Length}. Expected 64 (hex).", nameof(key));

            var keyBuffer = Convert.FromHexString(key.Key);
            var ivBuffer = Convert.FromHexString(metadata.Substring(3, 12)); // Sliced from 3, length 24
            var encrypted = Convert.FromBase64String(metadata.Substring(15));

            return Encoding.UTF8.GetString(AesGcmDecrypt(keyBuffer, ivBuffer, encrypted));
        }
        else
        {
            throw new ArgumentException($"Invalid metadata version {version}", nameof(metadata));
        }
    }

    /// <summary>
    /// Encrypts a chunk of data.
    /// </summary>
    /// <param name="data">The data to encrypt.</param>
    /// <param name="key">The key to use for encryption.</param>
    /// <returns>The encrypted data.</returns>
    public static byte[] EncryptData(Span<byte> data, DerivedKey key)
    {
        var tag = new byte[GcmTagSize];
        var iv = GenerateRandomBytes(12);
        var combined = new byte[iv.Length + data.Length + tag.Length];
        iv.CopyTo(combined.AsSpan());

        using var aes = new AesGcm(Encoding.UTF8.GetBytes(key.Key), GcmTagSize);
        aes.Encrypt(iv, data, combined.AsSpan().Slice(iv.Length, data.Length), tag);
        tag.CopyTo(combined.AsSpan(iv.Length + data.Length));
        return combined;
    }

    /// <summary>
    /// Decrypts a chunk of data.
    /// </summary>
    /// <param name="version">The version of the encryption.</param>
    /// <param name="data">The data to decrypt.</param>
    /// <param name="key">The key to use for decryption.</param>
    /// <returns>The decrypted data.</returns>
    public static byte[] DecryptData(int version, Span<byte> data, DerivedKey key)
    {
        if (version != 2)
            throw new NotSupportedException("Only version 2 is supported.");

        var iv = data.Slice(0, 12);
        var cipherText = data.Slice(12);
        return AesGcmDecrypt(Encoding.UTF8.GetBytes(key.Key), iv, cipherText);
    }

    /// <summary>
    /// Performs AES-GCM decryption.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="iv">The initialization vector.</param>
    /// <param name="encrypted">The encrypted data.</param>
    /// <returns>The decrypted data.</returns>
    public static byte[] AesGcmDecrypt(Span<byte> key, Span<byte> iv, Span<byte> encrypted)
    {
        using (var aesGcm = new AesGcm(key, GcmTagSize))
        {
            // The authentication tag is the last 16 bytes of the encrypted data.
            var authTag = encrypted.Slice(encrypted.Length - GcmTagSize);
            // The ciphertext is the encrypted data without the authentication tag.
            var cipherText = encrypted.Slice(0, encrypted.Length - GcmTagSize);

            var plainText = new byte[cipherText.Length];
            aesGcm.Decrypt(iv, cipherText, authTag, plainText);

            return plainText;
        }
    }

    /// <summary>
    /// Performs AES-GCM encryption.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="iv">The initialization vector.</param>
    /// <param name="data">The data to encrypt.</param>
    /// <returns>The encrypted data.</returns>
    public static byte[] AesGcmEncrypt(Span<byte> key, Span<byte> iv, Span<byte> data)
    {
        using (var aesGcm = new AesGcm(key, GcmTagSize))
        {
            var result = new byte[data.Length + GcmTagSize];
            aesGcm.Encrypt(iv, data, result.AsSpan().Slice(0, data.Length), result.AsSpan().Slice(data.Length));
            return result;
        }
    }
}
