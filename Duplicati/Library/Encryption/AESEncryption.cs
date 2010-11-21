#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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
        /// The commandline switch used to specify that AES Crypt is not allowed
        /// </summary>
        private const string COMMAND_LINE_NO_AES_CRYPT = "aes-encryption-dont-use-aescrypt";
        /// <summary>
        /// The commandline switch used to specify that fallback decryption is not allowed
        /// </summary>
        private const string COMMAND_LINE_NO_FALLBACK = "aes-encryption-dont-allow-fallback";
        #endregion

        /// <summary>
        /// True if AESCrypt is disabled, false otherwise
        /// </summary>
        private bool m_disableAESCrypt = false;
        /// <summary>
        /// True if fallback is allowed, false otherwise
        /// </summary>
        private bool m_allowFallback = true;
        /// <summary>
        /// The key used to encrypt the data
        /// </summary>
        private string m_key;

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
            if (options.ContainsKey(COMMAND_LINE_NO_AES_CRYPT))
                m_disableAESCrypt = Utility.Utility.ParseBool(options[COMMAND_LINE_NO_AES_CRYPT], true);
            if (options.ContainsKey(COMMAND_LINE_NO_FALLBACK))
                m_allowFallback = !Utility.Utility.ParseBool(options[COMMAND_LINE_NO_FALLBACK], true);

            if (m_disableAESCrypt && !m_allowFallback)
                throw new ArgumentException(string.Format(Strings.AESEncryption.OptionsAreMutuallyExclusiveError, COMMAND_LINE_NO_AES_CRYPT, COMMAND_LINE_NO_FALLBACK));

            m_key = passphrase;
        }

        private System.Security.Cryptography.SymmetricAlgorithm GenerateAESEncryptor(string key)
        {
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

            //TODO: Is it better to change the IV for each compressed file?
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

            return crypto;
        }

        #region IEncryption Members

        public override string FilenameExtension { get { return "aes"; } }
        public override string Description { get { return string.Format(Strings.AESEncryption.Description, COMMAND_LINE_NO_AES_CRYPT); } }
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
            if (m_disableAESCrypt)
                return new CryptoStream(input, GenerateAESEncryptor(m_key).CreateEncryptor(), System.Security.Cryptography.CryptoStreamMode.Write);
            else
                return new SharpAESCrypt.SharpAESCrypt(m_key, input, SharpAESCrypt.OperationMode.Encrypt);

        }

        public override Stream Decrypt(Stream input)
        {
            if (m_disableAESCrypt)
                return new System.Security.Cryptography.CryptoStream(input, GenerateAESEncryptor(m_key).CreateDecryptor(), System.Security.Cryptography.CryptoStreamMode.Read);

            try
            {
                return new SharpAESCrypt.SharpAESCrypt(m_key, input, SharpAESCrypt.OperationMode.Decrypt);
            }
            catch (InvalidDataException)
            {
                if (m_allowFallback)
                {
                    input.Seek(0, SeekOrigin.Begin);
                    return new System.Security.Cryptography.CryptoStream(input, GenerateAESEncryptor(m_key).CreateDecryptor(), System.Security.Cryptography.CryptoStreamMode.Read);
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
                    new CommandLineArgument(COMMAND_LINE_NO_AES_CRYPT, CommandLineArgument.ArgumentType.Boolean, Strings.AESEncryption.AesencryptiondontallowfallbackShort, string.Format(Strings.AESEncryption.AesencryptiondontallowfallbackLong, COMMAND_LINE_NO_FALLBACK), "false"),
                    new CommandLineArgument(COMMAND_LINE_NO_FALLBACK, CommandLineArgument.ArgumentType.Boolean, Strings.AESEncryption.AesencryptiondontuseaescryptShort, string.Format(Strings.AESEncryption.AesencryptiondontuseaescryptLong, COMMAND_LINE_NO_AES_CRYPT), "false")
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
    }
}
