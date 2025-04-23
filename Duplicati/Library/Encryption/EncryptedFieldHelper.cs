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

#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
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
    /// Key instance, isolating the current key and its hash
    /// </summary>
    /// <param name="Key">The key to use</param>
    /// <param name="Hash">The key hash</param>
    /// <param name="IsBlacklisted">If the key is blacklisted</param>
    public sealed record KeyInstance(string Key, string Hash, bool IsBlacklisted)
    {
        /// <summary>
        /// Creates a new key instance
        /// </summary>
        /// <param name="key">The key to use</param>
        /// <returns>The key instance</returns>
        public static KeyInstance CreateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key), Strings.EncryptedFieldHelper.KeyEmptyError);
            if (key.Length < 8)
                throw new ArgumentException(Strings.EncryptedFieldHelper.KeyTooShortError, nameof(key));

            using var hasher = HashFactory.CreateHasher(HashFactory.SHA256);
            return new KeyInstance(key, key.ComputeHashToHex(hasher), IsKeyBlacklisted(key));
        }

        /// <summary>
        /// Creates a key instance if the key is valid
        /// </summary>
        /// <param name="key">The key to create</param>
        /// <returns>The key instance or <c>null</c> if the key is invalid</returns>
        public static KeyInstance? CreateKeyIfValid(string? key)
            => string.IsNullOrWhiteSpace(key) ? null : CreateKey(key);
    }

    /// <summary>
    /// Checks if a key is blacklisted
    /// </summary>
    /// <param name="key">The key to check</param>
    /// <returns><c>true</c> if the key is blacklisted; <c>false</c> otherwise</returns>
    public static bool IsKeyBlacklisted(string key)
        => DeviceIDHelper.EMPTY_DEVICE_ID_HASHES.Contains(key);

    /// <summary>
    /// Prefix used to identify an encrypted field
    /// </summary>
    public const string HEADER_PREFIX = "enc-v1:";

    /// <summary>
    /// The name of the enviroment variable that holds the encryption key
    /// </summary>
    public const string ENVIROMENT_VARIABLE_NAME = "SETTINGS_ENCRYPTION_KEY";

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
    /// <param name="key">The key to use for decryption</param>
    /// <returns>Unencrypted data of the field</returns>
    [return: NotNullIfNotNull("value")]
    public static string? Decrypt(string? value, KeyInstance? key)
    {
        // If the value is not encrypted, it will be returned as is.
        if (string.IsNullOrEmpty(value) || !value.StartsWith(HEADER_PREFIX))
            return value;

        if (key == null)
            throw new SettingsEncryptionKeyMissingException();

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

            if (keyHash != key.Hash)
                throw new SettingsEncryptionKeyMismatchException();

            // Lets then decrypt it.
            return AESStringEncryption.DecryptFromHex(key.Key, content);
        }

        // if the hashes don't match, the lenght criteria can be ignored,
        // and it will be returned as is.
        return value;

    }

    /// <summary>
    /// Encrypts a value to be stored in the database.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="key">The key to use for encryption</param>
    /// <returns>The encrypted string</returns>
    public static string Encrypt(string value, KeyInstance? key)
    {
        if (key == null)
            throw new SettingsEncryptionKeyMissingException();

        if (key.IsBlacklisted)
            throw new InvalidOperationException(Strings.EncryptedFieldHelper.KeyBlacklistedError);

        using var hasher = HashFactory.CreateHasher(HashFactory.SHA256);
        var encrypted = AESStringEncryption.EncryptToHex(key.Key, value);

        var sb = new StringBuilder();
        sb.Append(HEADER_PREFIX);
        sb.Append(encrypted.ComputeHashToHex(hasher));
        sb.Append(key.Hash);
        sb.Append(encrypted);

        return sb.ToString();
    }

}