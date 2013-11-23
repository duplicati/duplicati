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
    public class AESEncryption : EncryptionBase
    {
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
            if(string.IsNullOrEmpty(passphrase))
                throw new ArgumentException(Strings.AESEncryption.EmptyKeyError, "passphrase");
                
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
            return new SharpAESCrypt.SharpAESCrypt(m_key, input, SharpAESCrypt.OperationMode.Decrypt);
        }

        public override IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                });
            }
        }

        #endregion
    }
}
