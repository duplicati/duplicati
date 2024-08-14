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

namespace Duplicati.Library.Encryption
{

    /// <summary>
    /// Class used to encrypt and decrypt settings in a way that is backwards compatible
    /// with previous versions of Duplicati.
    /// </summary>
    public class EncryptedFieldHelper
    {

        /// <summary>
        /// Returns the key to be used for encryption, either coming from a self computed key
        /// which uses the deviceid hash, or from an environment variable set by the user
        /// </summary>
        private static string SelectKey()
        {
            // If the environment variable is set, it will take precedence over the self computed key
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SETTINGS_ENCRYPTION_KEY")) ?
                Environment.GetEnvironmentVariable("SETTINGS_ENCRYPTION_KEY")! : DeviceIDHelper.GetDeviceIDHash();
        }
        
        /// <summary>
        /// Decrypts a value from the database, if it is not encrypted, it will be returned as is.
        /// 
        /// If the value is encrypted, it will be decrypted using the key obtained from SelectKey.
        /// 
        /// To determine if its encrypted, it checks first for length criterias then it checks
        /// if the hashes of the content and the key match.
        /// </summary>
        /// <param name="value">data from the field</param>
        /// <returns>Unencrypted data of the field</returns>
        /// <exception cref="SettingsEncryptionKeyMismatchException"></exception>
        public static string Decrypt(string? value)
        {
            // Single call to SelectKey to avoid latency to obtain deviceid
            string settingsEncryptionKey = SelectKey();

            var hasher = HashFactory.CreateHasher(HashFactory.SHA256);

            // For clarity, HashSize is size in bits / 8 for bytes, then times two because an encrypted field
            // is prefixed with two hashes before 
            var hashSizeInBytes = hasher.HashSize / 8 * 2;

            if (value!.Length <= hashSizeInBytes * 2) return value;
            
            // Value may be encrypted, to ensure, we will parse everything after
            // the mark of hashesCombinedSize as content, hash it and check if matches prefix.

            var contentHash = value.Substring(0, hashSizeInBytes);
            var keyHash = value.Substring(hashSizeInBytes, hashSizeInBytes);
            var content = value.Substring(hashSizeInBytes * 2);

            if (contentHash == content.ComputeHashToHex(hasher))
            {
                // Content hashes match therefore it is probed as encrypted, the next
                // step is to verify the encryption keys hashes match.

                if (keyHash != settingsEncryptionKey.ComputeHashToHex(hasher))
                    throw new SettingsEncryptionKeyMismatchException();

                // Lets then decrypt it.
                return AESStringEncryption.DecryptFromHex(settingsEncryptionKey, content);
            }

            // if the hashes don't match, the lenght criteria can be ignored,
            // and it will be returned as is.
            return value;

        }

        /// <summary>
        /// Encrypts a value to be stored in the database.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string Encrypt(string value)
        {
            // Single call to SelectKey to avoid latency to obtain deviceid
            string settingsEncryptionKey = SelectKey();

            var hasher = HashFactory.CreateHasher(HashFactory.SHA256);
            var encrypted = AESStringEncryption.EncryptToHex(settingsEncryptionKey, value);

            using MemoryStream output = new();
            using StreamWriter sw = new(output);
            sw.Write(encrypted.ComputeHashToHex(hasher));
            sw.Write(settingsEncryptionKey.ComputeHashToHex(hasher));
            sw.Write(encrypted);
            sw.Flush();
            return Encoding.UTF8.GetString(output.ToArray());
        }

    }
}