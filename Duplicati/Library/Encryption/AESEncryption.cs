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
        /// The commandline option supplied if an explicit thread level should be set (--aes-set-threadlevel)
        /// </summary>
        private const string COMMANDLINE_SET_THREADLEVEL = "aes-set-threadlevel";

        /// <summary>
        /// The default thread level
        /// </summary>
        private static readonly string DEFAULT_THREAD_LEVEL = Math.Min(4, Environment.ProcessorCount).ToString();

        /// <summary>
        /// The key used to encrypt the data
        /// </summary>
        private string m_key;

        /// <summary>
        /// The cached value for size overhead
        /// </summary>
        private static long m_cachedsizeoverhead = -1;

        /// <summary>
        /// The thread level to pass to SharpAESCrypt. 0 for default.
        /// </summary>
        private static int m_usethreadlevel = 0;

        /// <summary>
        /// Default constructor, used to read file extension and supported commands
        /// </summary>
        public AESEncryption()
        {
        }

        /// <summary>
        /// Constructs a new AES encryption/decyption instance
        /// </summary>
        public AESEncryption(string passphrase, Dictionary<string, string> options)
        {
            if (string.IsNullOrEmpty(passphrase))
                throw new ArgumentException(Strings.AESEncryption.EmptyKeyError, nameof(passphrase));

            m_key = passphrase;

            string strTL;
            options.TryGetValue(COMMANDLINE_SET_THREADLEVEL, out strTL);

            if (string.IsNullOrWhiteSpace(strTL))
                strTL = DEFAULT_THREAD_LEVEL;

            int useTL;
            if (int.TryParse(strTL, out useTL))
            {
                // finally set thread level in a range of 0 (default) to 4
                m_usethreadlevel = Math.Max(0, (Math.Min(4, useTL)));
            }
        }

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
        protected override void Dispose(bool disposing) { m_key = null; }

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
        {
            var cryptoStream = new SharpAESCrypt.SharpAESCrypt(m_key, input, SharpAESCrypt.OperationMode.Encrypt);
            if (m_usethreadlevel != 0) cryptoStream.MaxCryptoThreads = m_usethreadlevel;
            return cryptoStream;
        }

        /// <summary>
        /// Decrypts the stream to the output stream
        /// </summary>
        /// <param name="input">The encrypted stream</param>
        /// <returns>The unencrypted stream</returns>
        public override Stream Decrypt(Stream input)
        {
            var cryptoStream = new SharpAESCrypt.SharpAESCrypt(m_key, input, SharpAESCrypt.OperationMode.Decrypt);
            if (m_usethreadlevel != 0) cryptoStream.MaxCryptoThreads = m_usethreadlevel;
            return cryptoStream;
        }

        /// <summary>
        /// Gets a list of supported commandline arguments
        /// </summary>
        /// <value>The supported commands.</value>
        public override IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(
                        COMMANDLINE_SET_THREADLEVEL,
                        CommandLineArgument.ArgumentType.Enumeration,
                        Strings.AESEncryption.AessetthreadlevelShort,
                        Strings.AESEncryption.AessetthreadlevelLong,
                        DEFAULT_THREAD_LEVEL,
                        null,
                        new string[] {"0", "1", "2", "3", "4"}
                        ),
                });
            }
        }

        #endregion
    }
}
