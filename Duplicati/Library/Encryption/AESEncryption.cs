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
using Duplicati.Library.Interface;

namespace Duplicati.Library.Encryption
{
    /// <summary>
    /// Implements AES encryption
    /// </summary>
    public class AESEncryption : EncryptionBase
    {
        /// <summary>
        /// Toggles ignoring AES padding bytes; NOTE: this is not exposed as an option, but can be set using the environment variable DUPLICATI__AES_IGNORE_PADDING_BYTES
        /// </summary>
        private const string KEY_AES_IGNORE_PADDING_BYTES = "aes-ignore-padding-bytes";
        /// <summary>
        /// The key used to define the AES stream format
        /// </summary>
        private const string KEY_AES_VERSION = "aes-version";
        /// <summary>
        /// The key used to define the number of iterations used for the AES stream format v3
        /// </summary>
        private const string KEY_AES_V3_ITERATIONS = "aes-v3-iterations";
        /// <summary>
        /// The key used to define if the AES output should use a minimal header
        /// </summary>
        private const string KEY_AES_MINIMAL_HEADER = "aes-minimal-header";

        /// <summary>
        /// The key used to encrypt the data
        /// </summary>
        private readonly string m_key;

        /// <summary>
        /// The cached value for size overhead
        /// </summary>
        private static long m_cachedsizeoverhead = -1;

        /// <summary>
        /// Cached set of options for decryption
        /// </summary>
        private readonly SharpAESCrypt.DecryptionOptions m_decryptionOptions;

        /// <summary>
        /// Options to use for encryption
        /// </summary>
        private readonly SharpAESCrypt.EncryptionOptions m_encryptionOptions;

        /// <summary>
        /// Default constructor, used to read file extension and supported commands
        /// </summary>
        public AESEncryption()
        {
            m_key = null!;
            m_encryptionOptions = default!;
            m_decryptionOptions = default!;
        }

        /// <summary>
        /// Constructs a new AES encryption/decyption instance
        /// </summary>
        /// <param name="passphrase">The passphrase to use</param>
        /// <param name="minimalheader">Flag controlling if the encryption is done with a minimal header</param>
        public AESEncryption(string passphrase, bool minimalheader)
            : this(passphrase, new Dictionary<string, string>() { { KEY_AES_MINIMAL_HEADER, minimalheader.ToString() } })
        {
        }

        /// <summary>
        /// Constructs a new AES encryption/decyption instance
        /// </summary>
        public AESEncryption(string passphrase, IReadOnlyDictionary<string, string> options)
        {
            if (string.IsNullOrEmpty(passphrase))
                throw new ArgumentException(Strings.AESEncryption.EmptyKeyError, nameof(passphrase));

            m_key = passphrase;
            var encOpts = SharpAESCrypt.EncryptionOptions.Default;
            var decOpts = SharpAESCrypt.DecryptionOptions.Default;

            int? version = null;
            int? iterations = null;

            var minimalHeader = Utility.Utility.ParseBool(
                options.GetValueOrDefault(KEY_AES_MINIMAL_HEADER),
                Utility.Utility.ParseBool(
                    GetEnvValue(KEY_AES_MINIMAL_HEADER),
                    false
                )
            );

            var ignorePaddingBytes = Utility.Utility.ParseBool(
                options.GetValueOrDefault(KEY_AES_IGNORE_PADDING_BYTES),
                Utility.Utility.ParseBool(
                    GetEnvValue(KEY_AES_IGNORE_PADDING_BYTES),
                    false
                )
            );

            if (int.TryParse(GetEnvValue(KEY_AES_VERSION), out var tempInt))
                version = tempInt;
            if (int.TryParse(options.GetValueOrDefault(KEY_AES_VERSION), out tempInt))
                version = tempInt;

            if (int.TryParse(GetEnvValue(KEY_AES_V3_ITERATIONS), out tempInt))
                iterations = tempInt;
            if (int.TryParse(options.GetValueOrDefault(KEY_AES_V3_ITERATIONS), out tempInt))
                iterations = tempInt;


            if (minimalHeader)
                encOpts = encOpts with
                {
                    InsertCreatedByIdentifier = false,
                    InsertTimeStamp = false,
                    InsertPlaceholder = false
                };

            if (version.HasValue)
                encOpts = encOpts with { FileVersion = (byte)version.Value };
            if (iterations.HasValue)
                encOpts = encOpts with { KdfIterations = iterations.Value };
            if (ignorePaddingBytes)
                decOpts = decOpts with { IgnorePaddingBytes = true };

            m_encryptionOptions = encOpts;
            m_decryptionOptions = decOpts;
        }

        /// <summary>
        /// Gets the environment variable value for an option
        /// </summary>
        /// <param name="key">The key to get the value for</param>
        /// <returns>The value</returns>
        private static string? GetEnvValue(string key)
            => Environment.GetEnvironmentVariable("DUPLICATI__" + key.ToUpperInvariant().Replace('-', '_'));

        #region IEncryption Members

        /// <summary>
        /// The extension that the encryption implementation adds to the filename
        /// </summary>
        /// <value>The filename extension.</value>
        public override string FilenameExtension { get { return "aes"; } }
        /// <summary>
        /// A localized description of the encryption module
        /// </summary>
        /// <value>The description.</value>
        public override string Description { get { return string.Format(Strings.AESEncryption.Description_v2); } }
        /// <summary>
        /// A localized string describing the encryption module with a friendly name
        /// </summary>
        /// <value>The display name.</value>
        public override string DisplayName { get { return Strings.AESEncryption.DisplayName; } }
        /// <summary>
        /// Dispose the specified disposing.
        /// </summary>
        /// <param name="disposing">If set to <c>true</c> disposing.</param>
        protected override void Dispose(bool disposing) { }

        /// <summary>
        /// Returns the size in bytes of the overhead that will be added to a file of the given size when encrypted
        /// </summary>
        /// <param name="filesize">The size of the file to encrypt</param>
        /// <returns>The size of the overhead in bytes</returns>
        public override long SizeOverhead(long filesize)
        {
            if (m_cachedsizeoverhead != -1)
                return m_cachedsizeoverhead;

            //If we use 1, we trigger the blocksize.
            //As the AES algorithm does not alter the size,
            // the results are the same as for the real size,
            // but a single byte encryption is much faster.
            return m_cachedsizeoverhead = base.SizeOverhead(1);
        }

        /// <summary>
        /// Encrypts the stream
        /// </summary>
        /// <param name="input">The target stream</param>
        /// <returns>An encrypted stream that can be written to</returns>
        public override Stream Encrypt(Stream input)
            => new SharpAESCrypt.EncryptingStream(m_key, input, m_encryptionOptions);

        /// <summary>
        /// Decrypts the stream to the output stream
        /// </summary>
        /// <param name="input">The encrypted stream</param>
        /// <returns>The unencrypted stream</returns>
        public override Stream Decrypt(Stream input)
            => new SharpAESCrypt.DecryptingStream(m_key, input, m_decryptionOptions);

        /// <summary>
        /// Gets a list of supported commandline arguments
        /// </summary>
        /// <value>The supported commands.</value>
        public override IList<ICommandLineArgument> SupportedCommands => [
            new CommandLineArgument(
                KEY_AES_VERSION,
                CommandLineArgument.ArgumentType.Enumeration,
                Strings.AESEncryption.AesversionShort,
                Strings.AESEncryption.AesversionLong,
                SharpAESCrypt.EncryptionOptions.Default.FileVersion.ToString(),
                null,
                ["2", "3"]
            ),
            new CommandLineArgument(
                KEY_AES_V3_ITERATIONS,
                CommandLineArgument.ArgumentType.Integer,
                Strings.AESEncryption.Aesv3iterationsShort,
                Strings.AESEncryption.Aesv3iterationsLong,
                SharpAESCrypt.EncryptionOptions.Default.KdfIterations.ToString()
            ),
            new CommandLineArgument(
                KEY_AES_MINIMAL_HEADER,
                CommandLineArgument.ArgumentType.Boolean,
                Strings.AESEncryption.AesminimalheaderShort,
                Strings.AESEncryption.AesminimalheaderLong,
                "false"
            )
        ];

        #endregion
    }
}
