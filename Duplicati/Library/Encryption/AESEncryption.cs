#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Library.Interface;
using System.IO;
using System.Security.Cryptography;

namespace Duplicati.Library.Encryption
{
    /// <summary>
    /// Implements AES encryption
    /// </summary>
    public class AESEncryption : EncryptionBase, IEncryptionGUI, IGUIMiniControl
    {
        #region Commandline option constants
        /// <summary>
        /// The commandline switch used to specify that fallback decryption is not allowed
        /// </summary>
        private const string COMMAND_LINE_NO_FALLBACK = "aes-encryption-dont-allow-fallback";
        #endregion

        /// <summary>
        /// True if fallback is allowed, false otherwise
        /// </summary>
        private bool m_allowFallback = true;
        /// <summary>
        /// The key used to encrypt the data
        /// </summary>
        private string m_key;
        /// <summary>
        /// A flag indicating if the fallback is specified or default
        /// </summary>
        private bool m_defaultFallback;

        /// <summary>
        /// Default constructor, used to read file extension and supported commands
        /// </summary>
        public AESEncryption()
        {
        }

        /// <summary>
        /// Constructs a new AES encryption/decyption instance
        /// </summary>
        /// <param name="key">The key used for encryption. The key gets stretched through SHA hashing to fit the key size requirements</param>
        public AESEncryption(string passphrase, Dictionary<string, string> options)
        {
            m_allowFallback = !Utility.Utility.ParseBoolOption(options, COMMAND_LINE_NO_FALLBACK);
            m_defaultFallback = !options.ContainsKey(COMMAND_LINE_NO_FALLBACK);
            m_key = passphrase;
        }

        #region IEncryption Members

        public override string FilenameExtension { get { return "aes"; } }
        public override string Description { get { return string.Format(Strings.AESEncryption.Description_v2); } }
        public override string DisplayName { get { return Strings.AESEncryption.DisplayName; } }
        protected override void Dispose(bool disposing) { m_key = null; }

        public override long SizeOverhead(long filesize)
        {
            //If we use 1, we trigger the blocksize.
            //As the AES algorithm does not alter the size,
            // the results are the same as for the real size,
            // but a single byte encryption is much faster.
            return base.SizeOverhead(1);
        }

        public override Stream Encrypt(Stream input)
        {
            return new SharpAESCrypt.SharpAESCrypt(m_key, input, SharpAESCrypt.OperationMode.Encrypt);
        }

        public override Stream Decrypt(Stream input)
        {
            try
            {
                return new SharpAESCrypt.SharpAESCrypt(m_key, input, SharpAESCrypt.OperationMode.Decrypt);
            }
            catch (InvalidDataException iex)
            {
                if (m_allowFallback)
                {
                    input.Seek(0, SeekOrigin.Begin);

                    return
                        new ProtectedCryptoStream(
                            new System.Security.Cryptography.CryptoStream(
                                input,
                                GenerateOldAESDecryptor(m_key),
                                System.Security.Cryptography.CryptoStreamMode.Read
                            ),
                            iex,
                            m_defaultFallback
                        );
                }
                else
                    throw;
            }
        }

        public override IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(COMMAND_LINE_NO_FALLBACK, CommandLineArgument.ArgumentType.Boolean, Strings.AESEncryption.AesencryptiondontallowfallbackShort, Strings.AESEncryption.AesencryptiondontallowfallbackLong_v2, "false")
                });
            }
        }

        #endregion

        #region IGUIControl Members

        public string PageTitle
        {
            get { return this.DisplayName; }
        }

        public string PageDescription
        {
            get { return this.Description; }
        }

        public System.Windows.Forms.Control GetControl(IDictionary<string, string> applicationSettings, IDictionary<string, string> options)
        {
            return new System.Windows.Forms.Control();
        }

        public void Leave(System.Windows.Forms.Control control)
        {
        }

        public bool Validate(System.Windows.Forms.Control control)
        {
            return true;
        }

        public string GetConfiguration(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            return null;
        }

        #endregion

        #region Previous decryption scheme support

        private ICryptoTransform GenerateOldAESDecryptor(string key)
        {
            //Note this encryption scheme is not supported.
            //It is still present because it allows Duplicati to
            // read files that were created before the AESCrypt format
            // was used.
            SymmetricAlgorithm crypto = Rijndael.Create();
            SHA256 sha = SHA256.Create();
            int len = crypto.IV.Length + crypto.Key.Length;
            System.IO.MemoryStream ms = new System.IO.MemoryStream();

            //We stretch the key material by issuing cascading hash operations.
            //This somewhat alters the characteristics of the key, but as long
            //as the hashing is cryptographically safe and does not induce aliasing
            //with the encryption, it does not reduce the strength of the encryption
            byte[] tmp = Encoding.UTF8.GetBytes(key);
            while (ms.Length < len)
            {
                if (!sha.CanReuseTransform)
                    sha = SHA256.Create();
                sha.Initialize();

                tmp = sha.ComputeHash(tmp);
                ms.Write(tmp, 0, (int)Math.Min(tmp.Length, len - ms.Length));
            }

            //Note this code is deprecated as it encrypts all files with the same key
            byte[] realkey = new byte[crypto.Key.Length];
            byte[] iv = new byte[crypto.IV.Length];
            ms.Position = 0;
            if (ms.Read(iv, 0, iv.Length) != iv.Length)
                throw new Exception(Strings.AESEncryption.BadKeyStretchError);
            if (ms.Read(realkey, 0, realkey.Length) != realkey.Length)
                throw new Exception(Strings.AESEncryption.BadKeyStretchError);
            ms.Dispose();

            crypto.IV = iv;
            crypto.Key = realkey;
            crypto.Mode = CipherMode.CBC;
            crypto.Padding = PaddingMode.PKCS7;

            return crypto.CreateDecryptor();
        }

        /// <summary>
        /// Helper class that protects reading from the stream to allow throwing a custom exception
        /// </summary>
        private class ProtectedCryptoStream : Library.Utility.OverrideableStream
        {
            /// <summary>
            /// Another exception to report instead of the real one.
            /// This enables fallback decryption attempts, but will show the non-fallback error message if it fails
            /// </summary>
            private Exception m_reportException;

            /// <summary>
            /// A flag indicating if the fallback was requested explicit or default
            /// </summary>
            private bool m_defaultFallback;

            /// <summary>
            /// Initializes a new instance of the <see cref="ProtectedCryptoStream"/> class.
            /// </summary>
            /// <param name="stream">The stream to protect</param>
            /// <param name="reportException">The exception reported on read error</param>
            /// <param name="defaultFallback">A flag indicating if the fallback was requested explicit or default</param>
            public ProtectedCryptoStream(System.IO.Stream stream, Exception reportException, bool defaultFallback)
                : base(stream)
            {
                m_reportException = reportException;
                m_defaultFallback = defaultFallback;
            }

            /// <summary>
            /// Reads the specified number of bytes into the buffer
            /// </summary>
            /// <param name="buffer">The buffer to read the data into</param>
            /// <param name="offset">The offset into the buffer to start writing</param>
            /// <param name="count">The number of bytes to read</param>
            /// <returns>The number of bytes read</returns>
            public override int Read(byte[] buffer, int offset, int count)
            {
                try
                {
                    return base.Read(buffer, offset, count);
                }
                catch (CryptographicException ex)
                {
                    if (m_defaultFallback)
                        throw m_reportException;
                    else
                        throw new CryptographicException(m_reportException.Message + Environment.NewLine + ex.Message, m_reportException);
                }
            }
        }

        #endregion
    }
}
