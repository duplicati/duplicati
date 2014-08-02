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

#region Usage instructions, README
/*************************************************************

 This code is an implementation of the AES Crypt tool:
 http://www.aescrypt.com

 The code is primarily ported using the file format description,
 using the Java code as an example where there were uncertainties.
 It is tested against the AES Crypt binaries, ensuring that the
 binaries and this code are compatible.

 I have NOT tested the version=0 and version=1 formats, they are
 implemented purely by looking at the file format specs.
 If you have test data for these version, please let me know
 if it works.

 Usage:
 There are simple static functions that you can call:
    SharpAESCrypt.Encrypt("password", "inputfile", "outputfile");
    SharpAESCrypt.Decrypt("password", "inputfile", "outputfile");
    SharpAESCrypt.Decrypt("password", inputStream, outputStream);
    SharpAESCrypt.Decrypt("password", inputStream, outputStream);

 You can control what headers are emitted using the static 
 variables:
     SharpAESCrypt.Extension_CreatedByIdentifier
     SharpAESCrypt.Extension_InsertCreateByIdentifier
     SharpAESCrypt.Extension_InsertTimeStamp
     SharpAESCrypt.Extension_InsertPlaceholder
     SharpAESCrypt.DefaultFileVersion 

 If you need more advanced processing, you can initiate an 
 instance and use it as a stream:
    Stream aesStream = new SharpAESCrypt(password, inputStream, mode);

 You can then modify the Version and Extensions properties on
 the instance. If you use the stream mode, make sure you call
 either FlushFinalBlock() or Dispose() when you are done.

 Have fun!

 **************************************************************/
#endregion

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Security.Cryptography;

namespace SharpAESCrypt
{
    /// <summary>
    /// Enumerates the possible modes for encryption and decryption
    /// </summary>
    public enum OperationMode
    {
        /// <summary>
        /// Indicates encryption, which means that the stream must be writeable
        /// </summary>
        Encrypt,
        /// <summary>
        /// Indicates decryption, which means that the stream must be readable
        /// </summary>
        Decrypt
    }

    #region Translateable strings
    /// <summary>
    /// Placeholder for translateable strings
    /// </summary>
    public static class Strings
    {
        #region Command line
        /// <summary>
        /// A string displayed when the program is invoked without the correct number of arguments
        /// </summary>
        public static string CommandlineUsage = "SharpAESCrypt e|d password fromPath toPath";
        /// <summary>
        /// A string displayed when an error occurs while running the commandline program
        /// </summary>
        public static string CommandlineError = "Error: {0}";
        /// <summary>
        /// A string displayed if the mode is neither e nor d 
        /// </summary>
        public static string CommandlineUnknownMode = "Invalid operation, must be (e)crypt or (d)ecrypt";
        #endregion

        #region Exception messages
        /// <summary>
        /// An exception message that indicates that the hash algorithm is not supported
        /// </summary>
        public static string UnsupportedHashAlgorithmReuse = "The hash algortihm does not support reuse";
        /// <summary>
        /// An exception message that indicates that the hash algorithm is not supported
        /// </summary>
        public static string UnsupportedHashAlgorithmBlocks = "The hash algortihm does not support multiple blocks";
        /// <summary>
        /// An exception message that indicates that the hash algorithm is not supported
        /// </summary>
        public static string UnsupportedHashAlgorithmBlocksize = "Unable to digest {0} bytes, as the hash algorithm only returns {1} bytes";
        /// <summary>
        /// An exception message that indicates that an unexpected end of stream was encountered
        /// </summary>
        public static string UnexpectedEndOfStream = "The stream was exhausted unexpectedly";
        /// <summary>
        /// An exception message that indicates that the stream does not support writing
        /// </summary>
        public static string StreamMustBeWriteAble = "When encrypting, the stream must be writeable";
        /// <summary>
        /// An exception messaget that indicates that the stream does not support reading
        /// </summary>
        public static string StreamMustBeReadAble = "When decrypting, the stream must be readable";
        /// <summary>
        /// An exception message that indicates that the mode is not one of the allowed enumerations
        /// </summary>
        public static string InvalidOperationMode = "Invalid mode supplied";

        /// <summary>
        /// An exception message that indicates that file is not in the correct format
        /// </summary>
        public static string InvalidFileFormat = "Invalid file format";
        /// <summary>
        /// An exception message that indicates that the header marker is invalid
        /// </summary>
        public static string InvalidHeaderMarker = "Invalid header marker";
        /// <summary>
        /// An exception message that indicates that the reserved field is not set to zero
        /// </summary>
        public static string InvalidReservedFieldValue = "Reserved field is not zero";
        /// <summary>
        /// An exception message that indicates that the detected file version is not supported
        /// </summary>
        public static string UnsupportedFileVersion = "Unsuported file version: {0}";
        /// <summary>
        /// An exception message that indicates that an extension had an invalid format
        /// </summary>
        public static string InvalidExtensionData = "Invalid extension data, separator (0x00) not found";
        /// <summary>
        /// An exception message that indicates that the format was accepted, but the password was not verified
        /// </summary>
        public static string InvalidPassword = "Invalid password or corrupted data";
        /// <summary>
        /// An exception message that indicates that the length of the file is incorrect
        /// </summary>
        public static string InvalidFileLength = "File length is invalid";

        /// <summary>
        /// An exception message that indicates that the version is readonly when decrypting
        /// </summary>
        public static string VersionReadonlyForDecryption = "Version is readonly when decrypting";
        /// <summary>
        /// An exception message that indicates that the file version is readonly once encryption has started
        /// </summary>
        public static string VersionReadonly = "Version cannot be changed after encryption has started";
        /// <summary>
        /// An exception message that indicates that the supplied version number is unsupported
        /// </summary>
        public static string VersionUnsupported = "The maximum allowed version is {0}";
        /// <summary>
        /// An exception message that indicates that the stream must support seeking
        /// </summary>
        public static string StreamMustSupportSeeking = "The stream must be seekable writing version 0 files";

        /// <summary>
        /// An exception message that indicates that the requsted operation is unsupported
        /// </summary>
        public static string CannotReadWhileEncrypting = "Cannot read while encrypting";
        /// <summary>
        /// An exception message that indicates that the requsted operation is unsupported
        /// </summary>
        public static string CannotWriteWhileDecrypting = "Cannot read while decrypting";

        /// <summary>
        /// An exception message that indicates that the data has been altered
        /// </summary>
        public static string DataHMACMismatch = "Message has been altered, do not trust content";
        /// <summary>
        /// An exception message that indicates that the data has been altered or the password is invalid
        /// </summary>
        public static string DataHMACMismatch_v0 = "Invalid password or content has been altered";

        /// <summary>
        /// An exception message that indicates that the system is missing a text encoding
        /// </summary>
        public static string EncodingNotSupported = "The required encoding (UTF-16LE) is not supported on this system";
        #endregion
    }
    #endregion

    /// <summary>
    /// Provides a stream wrapping an AESCrypt file for either encryption or decryption.
    /// The file format declare support for 2^64 bytes encrypted data, but .Net has trouble 
    /// with files more than 2^63 bytes long, so this module 'only' supports 2^63 bytes 
    /// (long vs ulong).
    /// </summary>
    public class SharpAESCrypt : Stream
    {
        #region Shared constant values
        /// <summary>
        /// The header in an AESCrypt file
        /// </summary>
        private readonly byte[] MAGIC_HEADER = Encoding.UTF8.GetBytes("AES");

        /// <summary>
        /// The maximum supported file version
        /// </summary>
        public const byte MAX_FILE_VERSION = 2;

        /// <summary>
        /// The size of the block unit used by the algorithm in bytes
        /// </summary>
        private const int BLOCK_SIZE = 16;
        /// <summary>
        /// The size of the IV, in bytes, which is the same as the blocksize for AES
        /// </summary>
        private const int IV_SIZE = 16;
        /// <summary>
        /// The size of the key. For AES-256 that is 256/8 = 32
        /// </summary>
        private const int KEY_SIZE = 32;
        /// <summary>
        /// The size of the SHA-256 output, which matches the KEY_SIZE
        /// </summary>
        private const int HASH_SIZE = 32;
        #endregion

        #region Private instance variables
        /// <summary>
        /// The stream being encrypted or decrypted
        /// </summary>
        private Stream m_stream;
        /// <summary>
        /// The mode of operation
        /// </summary>
        private OperationMode m_mode;
        /// <summary>
        /// The cryptostream used to perform bulk encryption
        /// </summary>
        private CryptoStream m_crypto;
        /// <summary>
        /// The HMAC used for validating data
        /// </summary>
        private HMAC m_hmac;
        /// <summary>
        /// The length of the data modulus <see cref="BLOCK_SIZE"/>
        /// </summary>
        private int m_length;
        /// <summary>
        /// The setup helper instance
        /// </summary>
        private SetupHelper m_helper;
        /// <summary>
        /// The list of extensions read from or written to the stream
        /// </summary>
        private List<KeyValuePair<string, byte[]>> m_extensions;
        /// <summary>
        /// The file format version
        /// </summary>
        private byte m_version = MAX_FILE_VERSION;
        /// <summary>
        /// True if the header is written, false otherwise. Used only for encryption.
        /// </summary>
        private bool m_hasWrittenHeader = false;
        /// <summary>
        /// True if the footer has been written, false otherwise. Used only for encryption.
        /// </summary>
        private bool m_hasFlushedFinalBlock = false;
        /// <summary>
        /// The size of the payload, including padding. Used only for decryption.
        /// </summary>
        private long m_payloadLength;
        /// <summary>
        /// The number of bytes read from the encrypted stream. Used only for decryption.
        /// </summary>
        private long m_readcount;
        /// <summary>
        /// The number of padding bytes. Used only for decryption.
        /// </summary>
        private byte m_paddingSize;
        /// <summary>
        /// True if the header HMAC has been read and verified, false otherwise. Used only for decryption.
        /// </summary>
        private bool m_hasReadFooter = false;
        #endregion

        #region Private helper functions and properties
        /// <summary>
        /// Helper property to ensure that the crypto stream is initialized before being used
        /// </summary>
        private CryptoStream Crypto
        {
            get
            {
                if (m_crypto == null)
                    WriteEncryptionHeader();
                return m_crypto;
            }
        }

        /// <summary>
        /// Helper function to read and validate the header
        /// </summary>
        private void ReadEncryptionHeader(string password)
        {
            byte[] tmp = new byte[MAGIC_HEADER.Length + 2];
            if (m_stream.Read(tmp, 0, tmp.Length) != tmp.Length)
                throw new InvalidDataException(Strings.InvalidHeaderMarker);

            for (int i = 0; i < MAGIC_HEADER.Length; i++)
                if (MAGIC_HEADER[i] != tmp[i])
                    throw new InvalidDataException(Strings.InvalidHeaderMarker);

            m_version = tmp[MAGIC_HEADER.Length];
            if (m_version > MAX_FILE_VERSION)
                throw new InvalidDataException(string.Format(Strings.UnsupportedFileVersion, m_version));

            if (m_version == 0)
            {
                m_paddingSize = tmp[MAGIC_HEADER.Length + 1];
                if (m_paddingSize >= BLOCK_SIZE)
                    throw new InvalidDataException(Strings.InvalidHeaderMarker);
            }
            else if (tmp[MAGIC_HEADER.Length + 1] != 0)
                throw new InvalidDataException(Strings.InvalidReservedFieldValue);

            //Extensions are only supported in v2+
            if (m_version >= 2)
            {
                int extensionLength = 0;
                do
                {
                    byte[] tmpLength = RepeatRead(m_stream, 2);
                    extensionLength = (((int)tmpLength[0]) << 8) | (tmpLength[1]);

                    if (extensionLength != 0)
                    {
                        byte[] data = RepeatRead(m_stream, extensionLength);
                        int separatorIndex = Array.IndexOf<byte>(data, 0);
                        if (separatorIndex < 0)
                            throw new InvalidDataException(Strings.InvalidExtensionData);

                        string key = System.Text.Encoding.UTF8.GetString(data, 0, separatorIndex);
                        byte[] value = new byte[data.Length - separatorIndex - 1];
                        Array.Copy(data, separatorIndex + 1, value, 0, value.Length);

                        m_extensions.Add(new KeyValuePair<string, byte[]>(key, value));
                    }

                } while (extensionLength > 0);
            }

            byte[] iv1 = RepeatRead(m_stream, IV_SIZE);
            m_helper = new SetupHelper(m_mode, password, iv1);

            if (m_version >= 1)
            {
                byte[] hmac1 = m_helper.DecryptAESKey2(RepeatRead(m_stream, IV_SIZE + KEY_SIZE));
                byte[] hmac2 = RepeatRead(m_stream, hmac1.Length);
                for (int i = 0; i < hmac1.Length; i++)
                    if (hmac1[i] != hmac2[i])
                        throw new CryptographicException(Strings.InvalidPassword);

                m_payloadLength = m_stream.Length - m_stream.Position - (HASH_SIZE + 1);
            }
            else
            {
                m_helper.SetBulkKeyToKey1();

                m_payloadLength = m_stream.Length - m_stream.Position - HASH_SIZE;
            }

            if (m_payloadLength % BLOCK_SIZE != 0)
                throw new CryptographicException(Strings.InvalidFileLength);
        }

        /// <summary>
        /// Writes the header to the output stream and sets up the crypto stream
        /// </summary>
        private void WriteEncryptionHeader()
        {
            m_stream.Write(MAGIC_HEADER, 0, MAGIC_HEADER.Length);
            m_stream.WriteByte(m_version);
            m_stream.WriteByte(0); //Reserved or length % 16
            if (m_version >= 2)
            {
                foreach (KeyValuePair<string, byte[]> ext in m_extensions)
                    WriteExtension(ext.Key, ext.Value);
                m_stream.Write(new byte[] { 0, 0 }, 0, 2); //No more extensions
            }

            m_stream.Write(m_helper.IV1, 0, m_helper.IV1.Length);

            if (m_version == 0)
                m_helper.SetBulkKeyToKey1();
            else
            {
                //Generate and encrypt bulk key and its HMAC
                byte[] tmpKey = m_helper.EncryptAESKey2();
                m_stream.Write(tmpKey, 0, tmpKey.Length);
                tmpKey = m_helper.CalculateKeyHmac();
                m_stream.Write(tmpKey, 0, tmpKey.Length);
            }

            m_hmac = m_helper.GetHMAC();
            
            //Insert the HMAC before the stream to calculate the HMAC for the ciphertext
            m_crypto = new CryptoStream(new CryptoStream(new StreamHider(m_stream, 0), m_hmac, CryptoStreamMode.Write), m_helper.CreateCryptoStream(m_mode), CryptoStreamMode.Write);
            m_hasWrittenHeader = true;
        }

        /// <summary>
        /// Writes an extension to the output stream, see:
        /// http://www.aescrypt.com/aes_file_format.html
        /// </summary>
        /// <param name="identifier">The extension identifier</param>
        /// <param name="value">The data to set in the extension</param>
        private void WriteExtension(string identifier, byte[] value)
        {
            byte[] name = System.Text.Encoding.UTF8.GetBytes(identifier);
            if (value == null)
                value = new byte[0];
            
            uint size = (uint)(name.Length + 1 + value.Length);
            m_stream.WriteByte((byte)((size >> 8) & 0xff));
            m_stream.WriteByte((byte)(size & 0xff));
            m_stream.Write(name, 0, name.Length);
            m_stream.WriteByte(0);
            m_stream.Write(value, 0, value.Length);
        }

        #endregion

        #region Private utility classes and functions
        /// <summary>
        /// Internal helper class used to encapsulate the setup process
        /// </summary>
        private class SetupHelper : IDisposable
        {
            /// <summary>
            /// The MAC adress to use in case the network interface enumeration fails
            /// </summary>
            private static readonly byte[] DEFAULT_MAC = { 0x01, 0x23, 0x45, 0x67, 0x89, 0xab, 0xcd, 0xef };

            /// <summary>
            /// The hashing algorithm used to digest data
            /// </summary>
            private const string HASH_ALGORITHM = "SHA-256";

            /// <summary>
            /// The algorithm used to encrypt and decrypt data
            /// </summary>
            private const string CRYPT_ALGORITHM = "Rijndael";

            /// <summary>
            /// The algorithm used to generate random data
            /// </summary>
            private const string RAND_ALGORITHM = "SHA1PRNG";

            /// <summary>
            /// The algorithm used to calculate the HMAC
            /// </summary>
            private const string HMAC_ALGORITHM = "HmacSHA256";

            /// <summary>
            /// The encoding scheme for the password.
            /// UTF-16 should mean UTF-16LE, but Mono rejects the full name.
            /// A check is made when using the encoding, that it is indeed UTF-16LE.
            /// </summary>
            private const string PASSWORD_ENCODING = "utf-16";

            /// <summary>
            /// The encryption instance
            /// </summary>
            private SymmetricAlgorithm m_crypt;
            /// <summary>
            /// The hash instance
            /// </summary>
            private HashAlgorithm m_hash;
            /// <summary>
            /// The random number generator instance
            /// </summary>
            private RandomNumberGenerator m_rand;
            /// <summary>
            /// The HMAC algorithm
            /// </summary>
            private HMAC m_hmac;

            /// <summary>
            /// The IV used to encrypt/decrypt the bulk key
            /// </summary>
            private byte[] m_iv1;
            /// <summary>
            /// The private key used to encrypt/decrypt the bulk key
            /// </summary>
            private byte[] m_aesKey1;
            /// <summary>
            /// The IV used to encrypt/decrypt bulk data
            /// </summary>
            private byte[] m_iv2;
            /// <summary>
            /// The key used to encrypt/decrypt bulk data
            /// </summary>
            private byte[] m_aesKey2;

            /// <summary>
            /// Initialize the setup
            /// </summary>
            /// <param name="mode">The mode to prepare for</param>
            /// <param name="password">The password used to encrypt or decrypt</param>
            /// <param name="iv">The IV used, set to null if encrypting</param>
            public SetupHelper(OperationMode mode, string password, byte[] iv)
            {
                m_crypt = SymmetricAlgorithm.Create(CRYPT_ALGORITHM);
                
                //Not sure how to insert this with the CRYPT_ALGORITHM string
                m_crypt.Padding = PaddingMode.None;
                m_crypt.Mode = CipherMode.CBC;

                m_hash = HashAlgorithm.Create(HASH_ALGORITHM);
                m_rand = RandomNumberGenerator.Create(/*RAND_ALGORITHM*/);
                m_hmac = HMAC.Create(HMAC_ALGORITHM);

                if (mode == OperationMode.Encrypt)
                {
                    m_iv1 = GenerateIv1();
                    m_aesKey1 = GenerateAESKey1(EncodePassword(password));
                    m_iv2 = GenerateIv2();
                    m_aesKey2 = GenerateAESKey2();
                }
                else
                {
                    m_iv1 = iv;
                    m_aesKey1 = GenerateAESKey1(EncodePassword(password));
                }
            }

            /// <summary>
            /// Encodes the password in UTF-16LE, 
            /// used to fix missing support for the full encoding 
            /// name under Mono. Verifies that the encoding is correct.
            /// </summary>
            /// <param name="password">The password to encode as a byte array</param>
            /// <returns>The password encoded as a byte array</returns>
            private byte[] EncodePassword(string password)
            {
                Encoding e = Encoding.GetEncoding(PASSWORD_ENCODING);
                
                byte[] preamb = e == null ? null : e.GetPreamble();
                if (preamb == null || preamb.Length != 2)
                    throw new SystemException(Strings.EncodingNotSupported);

                if (preamb[0] == 0xff && preamb[1] == 0xfe)
                    return e.GetBytes(password);
                else if (preamb[0] == 0xfe && preamb[1] == 0xff)
                {
                    //We have a Big Endian, convert to Little endian
                    byte[] tmp = e.GetBytes(password);
                    if (tmp.Length % 2 != 0)
                        throw new SystemException(Strings.EncodingNotSupported);

                    for (int i = 0; i < tmp.Length; i += 2)
                    {
                        byte x = tmp[i];
                        tmp[i] = tmp[i + 1];
                        tmp[i + 1] = x;
                    }

                    return tmp;
                }
                else
                    throw new SystemException(Strings.EncodingNotSupported);
            }

            /// <summary>
            /// Gets the IV used to encrypt the bulk data key
            /// </summary>
            public byte[] IV1
            {
                get { return m_iv1; }
            }


            /// <summary>
            /// Creates the iv used for encrypting the actual key and IV.
            /// This IV is calculated using the network MAC adress as input.
            /// </summary>
            /// <returns>An IV</returns>
            private byte[] GenerateIv1()
            {
                byte[] iv = new byte[IV_SIZE];
                long time = DateTime.Now.Ticks;
                byte[] mac = null;

                try
                {
                    System.Net.NetworkInformation.NetworkInterface[] interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                    for (int i = 0; i < interfaces.Length; i++)
                        if (i != System.Net.NetworkInformation.NetworkInterface.LoopbackInterfaceIndex)
                        {
                            mac = interfaces[i].GetPhysicalAddress().GetAddressBytes();
                            break;
                        }
                }
                catch
                {
                    //Not much to do, just go with default MAC
                }

                if (mac == null)
                    mac = DEFAULT_MAC;

                for (int i = 0; i < 8; i++)
                    iv[i] = (byte)((time >> (i * 8)) & 0xff);

                Array.Copy(mac, 0, iv, 8, Math.Min(mac.Length, iv.Length - 8));
                return DigestRandomBytes(iv, 256);
            }

            /// <summary>
            /// Generates a key based on the IV and the password.
            /// This key is used to encrypt the actual key and IV.
            /// </summary>
            /// <param name="password">The password supplied</param>
            /// <returns>The key generated</returns>
            private byte[] GenerateAESKey1(byte[] password)
            {
                if (!m_hash.CanReuseTransform)
                    throw new CryptographicException(Strings.UnsupportedHashAlgorithmReuse);
                if (!m_hash.CanTransformMultipleBlocks)
                    throw new CryptographicException(Strings.UnsupportedHashAlgorithmBlocks);

                if (KEY_SIZE < m_hash.HashSize / 8)
                    throw new CryptographicException(string.Format(Strings.UnsupportedHashAlgorithmBlocksize, KEY_SIZE, m_hash.HashSize / 8));

                byte[] key = new byte[KEY_SIZE];
                Array.Copy(m_iv1, key, m_iv1.Length);

                for (int i = 0; i < 8192; i++)
                {
                    m_hash.Initialize();
                    m_hash.TransformBlock(key, 0, key.Length, key, 0);
                    m_hash.TransformFinalBlock(password, 0, password.Length);
                    key = m_hash.Hash;
                }

                return key;
            }

            /// <summary>
            /// Generates a random IV for encrypting data
            /// </summary>
            /// <returns>A random IV</returns>
            private byte[] GenerateIv2()
            {
                m_crypt.GenerateIV();
                return DigestRandomBytes(m_crypt.IV, 256);
            }

            /// <summary>
            /// Generates a random key for encrypting data
            /// </summary>
            /// <returns></returns>
            private byte[] GenerateAESKey2()
            {
                m_crypt.GenerateKey();
                return DigestRandomBytes(m_crypt.Key, 32);
            }

            /// <summary>
            /// Encrypts the key and IV used to encrypt data with the initial key and IV.
            /// </summary>
            /// <returns>The encrypted AES Key (including IV)</returns>
            public byte[] EncryptAESKey2()
            {
                using(MemoryStream ms = new MemoryStream())
                using (CryptoStream cs = new CryptoStream(ms, m_crypt.CreateEncryptor(m_aesKey1, m_iv1), CryptoStreamMode.Write))
                {
                    cs.Write(m_iv2, 0, m_iv2.Length);
                    cs.Write(m_aesKey2, 0, m_aesKey2.Length);
                    cs.FlushFinalBlock();

                    return ms.ToArray();
                }
            }

            /// <summary>
            /// Calculates the HMAC for the encrypted key
            /// </summary>
            /// <param name="data">The encrypted data to calculate the HMAC from</param>
            /// <returns>The HMAC value</returns>
            public byte[] CalculateKeyHmac()
            {
                m_hmac.Initialize();
                m_hmac.Key = m_aesKey1;
                return m_hmac.ComputeHash(EncryptAESKey2());
            }

            /// <summary>
            /// Performs repeated hashing of the data in the byte[] combined with random data.
            /// The update is performed on the input data, which is also returned.
            /// </summary>
            /// <param name="bytes">The bytes to start the digest operation with</param>
            /// <param name="repetitions">The number of repetitions to perform</param>
            /// <param name="hash">The hashing algorithm instance</param>
            /// <returns>The digested input data, which is the same array as passed in</returns>
            private byte[] DigestRandomBytes(byte[] bytes, int repetitions)
            {
                if (bytes.Length > (m_hash.HashSize / 8))
                    throw new CryptographicException(string.Format(Strings.UnsupportedHashAlgorithmBlocksize, bytes.Length, m_hash.HashSize / 8));

                if (!m_hash.CanReuseTransform)
                    throw new CryptographicException(Strings.UnsupportedHashAlgorithmReuse);
                if (!m_hash.CanTransformMultipleBlocks)
                    throw new CryptographicException(Strings.UnsupportedHashAlgorithmBlocks);

                m_hash.Initialize();
                m_hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                for (int i = 0; i < repetitions; i++)
                {
                    m_rand.GetBytes(bytes);
                    m_hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                }

                m_hash.TransformFinalBlock(bytes, 0, 0);
                Array.Copy(m_hash.Hash, bytes, bytes.Length);
                return bytes;
            }

            /// <summary>
            /// Generates the CryptoTransform element used to encrypt/decrypt the bulk data
            /// </summary>
            /// <param name="mode">The operation mode</param>
            /// <returns>An ICryptoTransform instance</returns>
            public ICryptoTransform CreateCryptoStream(OperationMode mode)
            {
                if (mode == OperationMode.Encrypt)
                    return m_crypt.CreateEncryptor(m_aesKey2, m_iv2);
                else
                    return m_crypt.CreateDecryptor(m_aesKey2, m_iv2);
            }

            /// <summary>
            /// Creates a fresh HMAC calculation algorithm
            /// </summary>
            /// <returns>An HMAC algortihm using AES Key 2</returns>
            public HMAC GetHMAC()
            {
                HMAC h = HMAC.Create(HMAC_ALGORITHM);
                h.Key = m_aesKey2;
                return h;
            }

            /// <summary>
            /// Decrypts the bulk key and IV
            /// </summary>
            /// <param name="data">The encrypted IV followed by the key</param>
            /// <returns>The HMAC value for the key</returns>
            public byte[] DecryptAESKey2(byte[] data)
            {
                using (MemoryStream ms = new MemoryStream(data))
                using (CryptoStream cs = new CryptoStream(ms, m_crypt.CreateDecryptor(m_aesKey1, m_iv1), CryptoStreamMode.Read))
                {
                    m_iv2 = RepeatRead(cs, IV_SIZE);
                    m_aesKey2 = RepeatRead(cs, KEY_SIZE);
                }

                m_hmac.Initialize();
                m_hmac.Key = m_aesKey1;
                m_hmac.TransformFinalBlock(data, 0, data.Length);
                return m_hmac.Hash;
            }

            /// <summary>
            /// Sets iv2 and aesKey2 to iv1 and aesKey1 respectively.
            /// Used only for files with version = 0
            /// </summary>
            public void SetBulkKeyToKey1()
            {
                m_iv2 = m_iv1;
                m_aesKey2 = m_aesKey1;
            }

            #region IDisposable Members

            /// <summary>
            /// Disposes all members 
            /// </summary>
            public void Dispose()
            {
                if (m_crypt != null)
                {
                    if (m_aesKey1 != null)
                        Array.Clear(m_aesKey1, 0 , m_aesKey1.Length);
                    if (m_iv1 != null)
                        Array.Clear(m_iv1, 0, m_iv1.Length);
                    if (m_aesKey2 != null)
                        Array.Clear(m_aesKey2, 0, m_aesKey2.Length);
                    if (m_iv2 != null)
                        Array.Clear(m_iv2, 0, m_iv2.Length);

                    m_aesKey1 = null;
                    m_iv1 = null;
                    m_aesKey2 = null;
                    m_iv2 = null;

                    m_hash = null;
                    m_hmac = null;
                    m_rand = null;
                    m_crypt = null;
                }
            }

            #endregion
        }

        /// <summary>
        /// Internal helper class, used to hide the trailing bytes from the cryptostream
        /// </summary>
        private class StreamHider : Stream
        {
            /// <summary>
            /// The wrapped stream
            /// </summary>
            private Stream m_stream;

            /// <summary>
            /// The number of bytes to hide
            /// </summary>
            private int m_hiddenByteCount;

            /// <summary>
            /// Constructs the stream wrapper to hide the desired bytes
            /// </summary>
            /// <param name="stream">The stream to wrap</param>
            /// <param name="count">The number of bytes to hide</param>
            public StreamHider(Stream stream, int count)
            {
                m_stream = stream;
                m_hiddenByteCount = count;
            }

            #region Basic Stream implementation stuff
            public override bool CanRead { get { return m_stream.CanRead; } }
            public override bool CanSeek { get { return m_stream.CanSeek; } }
            public override bool CanWrite { get { return m_stream.CanWrite; } }
            public override void Flush() { m_stream.Flush(); }
            public override long Length { get { return m_stream.Length; } }
            public override long Seek(long offset, SeekOrigin origin) { return m_stream.Seek(offset, origin); }
            public override void SetLength(long value) { m_stream.SetLength(value); }
            public override long Position { get { return m_stream.Position; } set { m_stream.Position = value; } }
            public override void Write(byte[] buffer, int offset, int count) { m_stream.Write(buffer, offset, count); }
            #endregion

            /// <summary>
            /// The overridden read function that ensures that the caller cannot see the hidden bytes
            /// </summary>
            /// <param name="buffer">The buffer to read into</param>
            /// <param name="offset">The offset into the buffer</param>
            /// <param name="count">The number of bytes to read</param>
            /// <returns>The number of bytes read</returns>
            public override int Read(byte[] buffer, int offset, int count)
            {
                long allowedCount = Math.Max(0, Math.Min(count, m_stream.Length - (m_stream.Position + m_hiddenByteCount)));
                if (allowedCount == 0)
                    return 0;
                else
                    return m_stream.Read(buffer, offset, (int)allowedCount);
            }
        }

        /// <summary>
        /// Helper function to support reading from streams that chunck data.
        /// Will keep reading a stream until <paramref name="count"/> bytes have been read.
        /// Throws an exception if the stream is exhausted before <paramref name="count"/> bytes are read.
        /// </summary>
        /// <param name="stream">The stream to read from</param>
        /// <param name="count">The number of bytes to read</param>
        /// <returns>The data read</returns>
        internal static byte[] RepeatRead(Stream stream, int count)
        {
            byte[] tmp = new byte[count];
            while (count > 0)
            {
                int r = stream.Read(tmp, tmp.Length - count, count);
                count -= r;
                if (r == 0 && count != 0)
                    throw new InvalidDataException(Strings.UnexpectedEndOfStream);
            }

            return tmp;
        }

        #endregion

        #region Public static API

        #region Default extension control variables
        /// <summary>
        /// The name inserted as the creator software in the extensions when creating output
        /// </summary>
        public static string Extension_CreatedByIdentifier = string.Format("SharpAESCrypt v{0}", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);

        /// <summary>
        /// A value indicating if the extension data should contain the creator software
        /// </summary>
        public static bool Extension_InsertCreateByIdentifier = true;

        /// <summary>
        /// A value indicating if the extensions data should contain timestamp data
        /// </summary>
        public static bool Extension_InsertTimeStamp = false;

        /// <summary>
        /// A value indicating if the extensions data should contain an empty block as suggested by the file format
        /// </summary>
        public static bool Extension_InsertPlaceholder = true;
        #endregion

        /// <summary>
        /// The file version to use when creating a new file
        /// </summary>
        public static byte DefaultFileVersion = MAX_FILE_VERSION;

        /// <summary>
        /// Encrypts a stream using the supplied password
        /// </summary>
        /// <param name="password">The password to decrypt with</param>
        /// <param name="input">The stream with unencrypted data</param>
        /// <param name="output">The encrypted output stream</param>
        public static void Encrypt(string password, Stream input, Stream output)
        {
            int a;
            byte[] buffer = new byte[1024 * 4];
            SharpAESCrypt c = new SharpAESCrypt(password, output, OperationMode.Encrypt);
            while ((a = input.Read(buffer, 0, buffer.Length)) != 0)
                c.Write(buffer, 0, a);
            c.FlushFinalBlock();
        }

        /// <summary>
        /// Decrypts a stream using the supplied password
        /// </summary>
        /// <param name="password">The password to encrypt with</param>
        /// <param name="input">The stream with encrypted data</param>
        /// <param name="output">The unencrypted output stream</param>
        public static void Decrypt(string password, Stream input, Stream output)
        {
            int a;
            byte[] buffer = new byte[1024 * 4];
            SharpAESCrypt c = new SharpAESCrypt(password, input, OperationMode.Decrypt);
            while ((a = c.Read(buffer, 0, buffer.Length)) != 0)
                output.Write(buffer, 0, a);
        }

        /// <summary>
        /// Encrypts a file using the supplied password
        /// </summary>
        /// <param name="password">The password to encrypt with</param>
        /// <param name="input">The file with unencrypted data</param>
        /// <param name="output">The encrypted output file</param>
        public static void Encrypt(string password, string inputfile, string outputfile)
        {
            using (FileStream infs = File.OpenRead(inputfile))
            using (FileStream outfs = File.Create(outputfile))
                Encrypt(password, infs, outfs);
        }

        /// <summary>
        /// Decrypts a file using the supplied password
        /// </summary>
        /// <param name="password">The password to decrypt with</param>
        /// <param name="input">The file with encrypted data</param>
        /// <param name="output">The unencrypted output file</param>
        public static void Decrypt(string password, string inputfile, string outputfile)
        {
            using (FileStream infs = File.OpenRead(inputfile))
            using (FileStream outfs = File.Create(outputfile))
                Decrypt(password, infs, outfs);
        }
        #endregion

        #region Public instance API
        /// <summary>
        /// Constructs a new AESCrypt instance, operating on the supplied stream
        /// </summary>
        /// <param name="password">The password used for encryption or decryption</param>
        /// <param name="stream">The stream to operate on, must be writeable for encryption, and readable for decryption</param>
        /// <param name="mode">The mode of operation, either OperationMode.Encrypt or OperationMode.Decrypt</param>
        public SharpAESCrypt(string password, Stream stream, OperationMode mode)
        {
            //Basic input checks
            if (stream == null)
                throw new ArgumentNullException("stream");
            if (password == null)
                throw new ArgumentNullException("password");
            if (mode != OperationMode.Encrypt && mode != OperationMode.Decrypt)
                throw new ArgumentException(Strings.InvalidOperationMode, "mode");
            if (mode == OperationMode.Encrypt && !stream.CanWrite)
                throw new ArgumentException(Strings.StreamMustBeWriteAble, "stream");
            if (mode == OperationMode.Decrypt && !stream.CanRead)
                throw new ArgumentException(Strings.StreamMustBeReadAble, "stream");

            m_mode = mode;
            m_stream = stream;
            m_extensions = new List<KeyValuePair<string, byte[]>>();

            if (mode == OperationMode.Encrypt)
            {
                this.Version = DefaultFileVersion;

                m_helper = new SetupHelper(mode, password, null);

                //Setup default extensions
                if (Extension_InsertCreateByIdentifier)
                    m_extensions.Add(new KeyValuePair<string, byte[]>("CREATED-BY", System.Text.Encoding.UTF8.GetBytes(Extension_CreatedByIdentifier)));

                if (Extension_InsertTimeStamp)
                {
                    m_extensions.Add(new KeyValuePair<string, byte[]>("CREATED-DATE", System.Text.Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("yyyy-MM-dd"))));
                    m_extensions.Add(new KeyValuePair<string, byte[]>("CREATED-TIME", System.Text.Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("hh-mm-ss"))));
                }

                if (Extension_InsertPlaceholder)
                    m_extensions.Add(new KeyValuePair<string, byte[]>("", new byte[127])); //Suggested extension space
                
                //We defer creation of the cryptostream until it is needed, 
                // so the caller can change version, extensions, etc. 
                // before we write the header
                m_crypto = null;
            }
            else
            {
                //Read and validate
                ReadEncryptionHeader(password);

                m_hmac = m_helper.GetHMAC();

                //Insert the HMAC before the decryption so the HMAC is calculated for the ciphertext
                m_crypto = new CryptoStream(new CryptoStream(new StreamHider(m_stream, m_version == 0 ? HASH_SIZE : (HASH_SIZE + 1)), m_hmac, CryptoStreamMode.Read), m_helper.CreateCryptoStream(m_mode), CryptoStreamMode.Read);
            }
        }

        /// <summary>
        /// Gets or sets the version number.
        /// Note that this can only be set when encrypting, 
        /// and must be done before encryption has started.
        /// See <value>MAX_FILE_VERSION</value> for the maximum supported version.
        /// Note that version 0 requires a seekable stream.
        /// </summary>
        public byte Version
        {
            get { return m_version; }
            set
            {
                if (m_mode == OperationMode.Decrypt)
                    throw new InvalidOperationException(Strings.VersionReadonlyForDecryption);
                if (m_mode == OperationMode.Encrypt && m_crypto != null)
                    throw new InvalidOperationException(Strings.VersionReadonly);
                if (value > MAX_FILE_VERSION)
                    throw new ArgumentOutOfRangeException(string.Format(Strings.VersionUnsupported, MAX_FILE_VERSION));
                if (value == 0 && !m_stream.CanSeek)
                    throw new InvalidOperationException(Strings.StreamMustSupportSeeking);

                m_version = value;
            }
        }

        /// <summary>
        /// Provides access to the extensions found in the file.
        /// This collection cannot be updated when decrypting, 
        /// nor after the encryption has started.
        /// </summary>
        public IList<KeyValuePair<string, byte[]>> Extensions
        {
            get
            {
                if (m_mode == OperationMode.Decrypt || (m_mode == OperationMode.Encrypt && m_crypto != null))
                    return m_extensions.AsReadOnly();
                else
                    return m_extensions;
            }
        }

        #region Basic stream implementation stuff, all mapped directly to the cryptostream
        public override bool CanRead { get { return Crypto.CanRead; } }
        public override bool CanSeek { get { return Crypto.CanSeek; } }
        public override bool CanWrite { get { return Crypto.CanWrite; } }
        public override void Flush() { Crypto.Flush(); }
        public override long Length { get { return Crypto.Length; } }
        public override long Position
        {
            get { return Crypto.Position; }
            set { Crypto.Position = value; }
        }
        public override long Seek(long offset, System.IO.SeekOrigin origin) { return Crypto.Seek(offset, origin); }
        public override void SetLength(long value) { Crypto.SetLength(value); }
        #endregion

        /// <summary>
        /// Reads unencrypted data from the underlying stream
        /// </summary>
        /// <param name="buffer">The buffer to read data into</param>
        /// <param name="offset">The offset into the buffer</param>
        /// <param name="count">The number of bytes to read</param>
        /// <returns>The number of bytes read</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (m_mode != OperationMode.Decrypt)
                throw new InvalidOperationException(Strings.CannotReadWhileEncrypting);

            if (m_hasReadFooter)
                return 0;

            count = Crypto.Read(buffer, offset, count);
            
            //TODO: If the cryptostream supporting seeking in future versions of .Net, 
            // this counter system does not work
            m_readcount += count;
            m_length = (m_length + count) % BLOCK_SIZE;

            if (!m_hasReadFooter && m_readcount == m_payloadLength)
            {
                m_hasReadFooter = true;

                //Verify the data
                if (m_version >= 1)
                {
                    int l = m_stream.ReadByte();
                    if (l < 0)
                        throw new InvalidDataException(Strings.UnexpectedEndOfStream);
                    m_paddingSize = (byte)l;
                    if (m_paddingSize > BLOCK_SIZE)
                        throw new InvalidDataException(Strings.InvalidFileLength);
                }

                if (m_paddingSize > 0)
                    count -= (BLOCK_SIZE - m_paddingSize);

                if (m_length % BLOCK_SIZE != 0 || m_readcount % BLOCK_SIZE != 0)
                    throw new InvalidDataException(Strings.InvalidFileLength);

                //Required because we want to read the hash, 
                // so FlushFinalBlock need to be called.
                //We cannot call FlushFinalBlock directly because it may
                // have been called by the read operation.
                //The StreamHider makes sure that the underlying stream 
                // is not closed
                Crypto.Close();

                byte[] hmac1 = m_hmac.Hash;
                byte[] hmac2 = RepeatRead(m_stream, hmac1.Length);
                for (int i = 0; i < hmac1.Length; i++)
                    if (hmac1[i] != hmac2[i])
                        throw new InvalidDataException(m_version == 0 ? Strings.DataHMACMismatch_v0 : Strings.DataHMACMismatch);
            }

            return count;
        }

        /// <summary>
        /// Writes unencrypted data into an encrypted stream
        /// </summary>
        /// <param name="buffer">The data to write</param>
        /// <param name="offset">The offset into the buffer</param>
        /// <param name="count">The number of bytes to write</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (m_mode != OperationMode.Encrypt)
                throw new InvalidOperationException(Strings.CannotWriteWhileDecrypting);

            m_length = (m_length + count) % BLOCK_SIZE;
            Crypto.Write(buffer, offset, count);
        }

        /// <summary>
        /// Flushes any remaining data to the stream
        /// </summary>
        public void FlushFinalBlock()
        {
            if (!m_hasFlushedFinalBlock)
            {
                if (m_mode == OperationMode.Encrypt)
                {
                    if (!m_hasWrittenHeader)
                        WriteEncryptionHeader();

                    byte lastLen = (byte)(m_length %= BLOCK_SIZE);

                    //Apply PaddingMode.PKCS7 manually, the original AES crypt uses non-standard padding
                    if (lastLen != 0)
                    {
                        byte[] padding = new byte[BLOCK_SIZE - lastLen];
                        for (int i = 0; i < padding.Length; i++)
                            padding[i] = (byte)padding.Length;
                        Write(padding, 0, padding.Length);
                    }

                    //Not required without padding, but throws exception if the stream is used incorrectly
                    Crypto.FlushFinalBlock();
                    //The StreamHider makes sure the underlying stream is not closed.
                    Crypto.Close();

                    byte[] hmac = m_hmac.Hash;

                    if (m_version == 0)
                    {
                        m_stream.Write(hmac, 0, hmac.Length);
                        long pos = m_stream.Position;
                        m_stream.Seek(MAGIC_HEADER.Length + 1, SeekOrigin.Begin);
                        m_stream.WriteByte(lastLen);
                        m_stream.Seek(pos, SeekOrigin.Begin);
                        m_stream.Flush();
                    }
                    else
                    {
                        m_stream.WriteByte(lastLen);
                        m_stream.Write(hmac, 0, hmac.Length);
                        m_stream.Flush();
                    }
                }
            }
        }

        /// <summary>
        /// Releases all resources used by the instance, and flushes any data currently held, into the stream
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                if (m_mode == OperationMode.Encrypt && !m_hasFlushedFinalBlock)
                    FlushFinalBlock();
                
                if (m_crypto != null)
                    m_crypto.Dispose();
                m_crypto = null;

                if (m_stream != null)
                    m_stream.Dispose();
                m_stream = null;
                m_extensions = null;
                if (m_helper != null)
                    m_helper.Dispose();
                m_helper = null;
                m_hmac = null;
            }
        }

        #endregion

        /// <summary>
        /// Main function, used when compiled as a standalone executable
        /// </summary>
        /// <param name="args">Commandline arguments</param>
        public static void Main(string[] args)
        {
			if (args.Length < 4) 
            {
				Console.WriteLine(Strings.CommandlineUsage);
				return;
			}

            try
            {
                if (args[0].StartsWith("e", StringComparison.InvariantCultureIgnoreCase))
                    Encrypt(args[1], args[2], args[3]);
                else if (args[0].StartsWith("d", StringComparison.InvariantCultureIgnoreCase))
                    Decrypt(args[1], args[2], args[3]);
#if DEBUG
                else if (args[0].StartsWith("u"))
                    Unittest();
#endif
                else
                    Console.WriteLine(Strings.CommandlineUnknownMode);
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format(Strings.CommandlineError, ex.ToString()));
            }
        }

        #region Unittest code
#if DEBUG
        /// <summary>
        /// Performs a unittest to ensure that the program performs as expected
        /// </summary>
        private static void Unittest()
        {
            const int MIN_SIZE = 1024 * 5;
            const int MAX_SIZE = 1024 * 1024 * 100; //100mb
            const int REPETIONS = 1000;

            bool allpass = true;

            Random rnd = new Random();
            Console.WriteLine("Running unittest");

            //Test each supported version
            for (byte v = 0; v <= MAX_FILE_VERSION; v++)
            {
                SharpAESCrypt.DefaultFileVersion = v;

                //Test boundary 0 and around the block/keysize margins
                for (int i = 0; i < MIN_SIZE; i++)
                    using (MemoryStream ms = new MemoryStream())
                    {
                        byte[] tmp = new byte[i];
                        rnd.NextBytes(tmp);
                        ms.Write(tmp, 0, tmp.Length);
                        allpass &= Unittest(string.Format("Testing version {0} with length = {1} => ", v, ms.Length), ms);
                    }
            }

            SharpAESCrypt.DefaultFileVersion = MAX_FILE_VERSION;
            Console.WriteLine(string.Format("Initial tests complete, running bulk tests with v{0}", SharpAESCrypt.DefaultFileVersion));

            for (int i = 0; i < REPETIONS; i++)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] tmp = new byte[rnd.Next(MIN_SIZE, MAX_SIZE)];
                    rnd.NextBytes(tmp);
                    ms.Write(tmp, 0, tmp.Length);
                    allpass |= Unittest(string.Format("Testing bulk {0} of {1} with length = {2} => ", i, REPETIONS, ms.Length), ms);
                }
            }

            if (allpass)
            {
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine("**** All unittests passed ****");
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Helper function to 
        /// </summary>
        /// <param name="message">A message printed to the console</param>
        /// <param name="input">The stream to test with</param>
        private static bool Unittest(string message, MemoryStream input)
        {
            Console.Write(message);

            const string PASSWORD_CHARS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!\"#¤%&/()=?`*'^¨-_.:,;<>|";
            const int MIN_LEN = 1;
            const int MAX_LEN = 25;

            try
            {
                Random rnd = new Random();
                char[] pwdchars = new char[rnd.Next(MIN_LEN, MAX_LEN)];
                for (int i = 0; i < pwdchars.Length; i++)
                    pwdchars[i] = PASSWORD_CHARS[rnd.Next(0, PASSWORD_CHARS.Length)];

                input.Position = 0;

                using (MemoryStream enc = new MemoryStream())
                using (MemoryStream dec = new MemoryStream())
                {
                    Encrypt(new string(pwdchars), input, enc);
                    enc.Position = 0;
                    Decrypt(new string(pwdchars), enc, dec);

                    dec.Position = 0;
                    input.Position = 0;

                    if (dec.Length != input.Length)
                        throw new Exception(string.Format("Length differ {0} vs {1}", dec.Length, input.Length));

                    for (int i = 0; i < dec.Length; i++)
                        if (dec.ReadByte() != input.ReadByte())
                            throw new Exception(string.Format("Streams differ at byte {0}", i));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED: " + ex.Message);
                return false;
            }

            Console.WriteLine("OK!");
            return true;
        }
#endif
        #endregion
    }
}
