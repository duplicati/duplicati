#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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

namespace Duplicati.Encryption
{
    /// <summary>
    /// Implements AES encryption
    /// </summary>
    public class AESEncryption : EncryptionBase, IEncryption
    {
        /// <summary>
        /// The AES instance
        /// </summary>
        private System.Security.Cryptography.Rijndael m_instance;

        /// <summary>
        /// Constructs a new AES encryption/decyption instance
        /// </summary>
        /// <param name="key">The key used for encryption. The key gets stretched through SHA hashing to fit the key size requirements</param>
        public AESEncryption(string key)
        {
            m_instance = System.Security.Cryptography.Rijndael.Create();
            System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();
            int len = m_instance.IV.Length + m_instance.Key.Length;
            System.IO.MemoryStream ms = new System.IO.MemoryStream();

            //We stretch the key material by issuing cascading hash operations.
            //This somewhat alters the characteristics of the key, but as long
            //as the hashing is cryptographically safe and does not induce aliasing
            //with the encryption, it does not reduce the strength of the encryption
            byte[] tmp = System.Text.Encoding.UTF8.GetBytes(key);
            while (ms.Length < len)
            {
                if (!sha.CanReuseTransform)
                    sha = System.Security.Cryptography.SHA256.Create();
                sha.Initialize();

                tmp = sha.ComputeHash(tmp);
                ms.Write(tmp, 0, (int)Math.Min(tmp.Length, len - ms.Length));
            }

            byte[] realkey = new byte[m_instance.Key.Length];
            byte[] iv = new byte[m_instance.IV.Length];
            ms.Position = 0;
            if (ms.Read(iv, 0, iv.Length) != iv.Length)
                throw new Exception("Bad key stretch");
            if (ms.Read(realkey, 0, realkey.Length) != realkey.Length)
                throw new Exception("Bad key stretch");
            ms.Dispose();

            m_instance.IV = iv;
            m_instance.Key = realkey;
            m_instance.Mode = System.Security.Cryptography.CipherMode.CBC;
        }

        #region IEncryption Members

        public override void Encrypt(System.IO.Stream input, System.IO.Stream output)
        {
            if (m_instance.Mode == System.Security.Cryptography.CipherMode.ECB)
                throw new Exception("Useless encryption detected");

            System.Security.Cryptography.ICryptoTransform ct = m_instance.CreateEncryptor();
            using (System.Security.Cryptography.CryptoStream cs = new System.Security.Cryptography.CryptoStream(output, ct, System.Security.Cryptography.CryptoStreamMode.Write))
            {
                Core.Utility.CopyStream(input, cs);
                cs.FlushFinalBlock();
            }
        }

        public override void Decrypt(System.IO.Stream input, System.IO.Stream output)
        {
            if (m_instance.Mode == System.Security.Cryptography.CipherMode.ECB)
                throw new Exception("Useless encryption detected");

            System.Security.Cryptography.ICryptoTransform ct = m_instance.CreateDecryptor();
            using (System.Security.Cryptography.CryptoStream cs = new System.Security.Cryptography.CryptoStream(input, ct, System.Security.Cryptography.CryptoStreamMode.Read))
                Core.Utility.CopyStream(cs, output);
        }

        #endregion
    }
}
