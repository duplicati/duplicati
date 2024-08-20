// Copyright (C) 2024, The Duplicati Team
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
using System.IO;
using System.Text;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Encryption;

/// <summary>
/// Class used to encrypt and decrypt settings in a way that is backwards compatible
/// with previous versions of Duplicati.
/// </summary>
public static class EncryptedFieldHelper
{
    /// <summary>
    /// Holds the key to be used for encryption, either from a self computed key
    /// which uses the deviceid hash, or from an environment variable set by the user
    /// </summary>
    private static readonly string ActiveKey = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SETTINGS_ENCRYPTION_KEY"))
        ? DeviceIDHelper.GetDeviceIDHash()
        : Environment.GetEnvironmentVariable("SETTINGS_ENCRYPTION_KEY");

    /// <summary>
    /// Hash of the key to be used for encryption
    /// </summary>
    private static readonly string KeyHash;

    /// <summary>
    /// Static constructor to compute the hash of the key
    /// </summary>
    static EncryptedFieldHelper()
    {
        using var hasher = HashFactory.CreateHasher(HashFactory.SHA256);
        KeyHash = ActiveKey.ComputeHashToHex(hasher);
    }

    /// <summary>
    /// Prefix used to identify an encrypted field
    /// </summary>
    private const string HEADER_PREFIX = "enc-v1:";

    /// <summary>
    /// Checks if a value is an encrypted string
    /// </summary>
    /// <param name="value">The value to decrypt</param>
    /// <returns><c>true</c> if the string is encrypted; <c>false</c> otherwise</returns>
    public static bool IsEncryptedString(string value)
        => !string.IsNullOrWhiteSpace(value) && value.StartsWith(HEADER_PREFIX);

    /// <summary>
    /// Decrypts a value from the database, if it is not encrypted, it will be returned as is.
    /// 
    /// If the value is encrypted, it will be decrypted using the key obtained from ActiveKey.
    /// 
    /// The check for encryption is done by checking the prefix of the string.
    /// An additional check is done by hashing the content and comparing it to the hash
    /// </summary>
    /// <param name="value">data from the field</param>
    /// <returns>Unencrypted data of the field</returns>
    public static string Decrypt(string? value)
    {
        // If the value is not encrypted, it will be returned as is.
        if (string.IsNullOrEmpty(value) || !value.StartsWith(HEADER_PREFIX))
            return value;

        value = value.Substring(HEADER_PREFIX.Length);

        using var hasher = HashFactory.CreateHasher(HashFactory.SHA256);

        // For clarity, HashSize is size in bits / 8 for bytes, then times two because an encrypted field
        // is prefixed with two hashes before 
        var hashSizeInBytes = hasher.HashSize / 8 * 2;

        // Value may be encrypted, to ensure, we will parse everything after
        // the mark of hashesCombinedSize as content, hash it and check if matches prefix.

        var contentHash = value.Substring(0, hashSizeInBytes);
        var keyHash = value.Substring(hashSizeInBytes, hashSizeInBytes);
        var content = value.Substring(hashSizeInBytes * 2);

        if (contentHash == content.ComputeHashToHex(hasher))
        {
            // Content hashes match therefore it is probed as encrypted, the next
            // step is to verify the encryption keys hashes match.

            if (keyHash != KeyHash)
                throw new SettingsEncryptionKeyMismatchException();

            // Lets then decrypt it.
            return AESStringEncryption.DecryptFromHex(ActiveKey, content);
        }

        // if the hashes don't match, the lenght criteria can be ignored,
        // and it will be returned as is.
        return value;

    }

    /// <summary>
    /// Encrypts a value to be stored in the database.
    /// </summary>
    /// <param name="value"></param>
    /// <returns>The encrypted string</returns>
    public static string Encrypt(string value)
    {
        using var hasher = HashFactory.CreateHasher(HashFactory.SHA256);
        var encrypted = AESStringEncryption.EncryptToHex(ActiveKey, value);

        var sb = new StringBuilder();
        sb.Append(HEADER_PREFIX);
        sb.Append(encrypted.ComputeHashToHex(hasher));
        sb.Append(KeyHash);
        sb.Append(encrypted);

        return sb.ToString();
    }

}