#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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

namespace Duplicati.Library.SharpRSync
{
    /// <summary>
    /// This class reads and writes signature files in the same format as RDiff
    /// </summary>
    public class ChecksumFile
    {
        private const int MD4_LENGTH = 16;
        private int m_blocklen;
        private int m_stronglen;

        //This dictionary has the weak checksum as the key, and an index into the stronglist
        private Dictionary<uint, List<int>> m_checksums;
        //This list is ordered and each entry has the weak and strong checksum
        private List<KeyValuePair<uint, byte[]>> m_strongList;
        private Adler32Checksum m_adler;
        private System.Security.Cryptography.HashAlgorithm m_md4;
        //private System.Security.Cryptography.HashAlgorithm m_sha;

        /// <summary>
        /// Reads a ChecksumFile from a stream
        /// </summary>
        /// <param name="input">The stream to read from</param>
        public ChecksumFile(System.IO.Stream input)
        {
            m_checksums = new Dictionary<uint, List<int>>();
            m_strongList = new List<KeyValuePair<uint, byte[]>>();
            byte[] sig = new byte[4];
            if (Utility.ForceStreamRead(input, sig, 4) != 4)
                throw new Exception(Strings.ChecksumFile.EndofstreamBeforeSignatureError);
            for (int i = 0; i < sig.Length; i++)
                if (RDiffBinary.SIGNATURE_MAGIC[i] != sig[i])
                    throw new Exception(Strings.ChecksumFile.InvalidSignatureHeaderError);

            if (Utility.ForceStreamRead(input, sig, 4) != 4)
                throw new Exception(Strings.ChecksumFile.EndofstreamInBlocksizeError);
            m_blocklen = BitConverter.ToInt32(RDiffBinary.FixEndian(sig), 0);
            if (m_blocklen < 1 || m_blocklen > int.MaxValue / 2)
                throw new Exception(string.Format(Strings.ChecksumFile.InvalidBlocksizeError, m_blocklen));
            if (Utility.ForceStreamRead(input, sig, 4) != 4)
                throw new Exception(Strings.ChecksumFile.EndofstreamInStronglenError);
            m_stronglen = BitConverter.ToInt32(RDiffBinary.FixEndian(sig), 0);
            if (m_stronglen < 1 || m_stronglen > MD4_LENGTH)
                throw new Exception(string.Format(Strings.ChecksumFile.InvalidStrongsizeError, m_stronglen));

            while(true)
            {
                if (Utility.ForceStreamRead(input, sig, 4) != 4)
                    break;
                uint weak = BitConverter.ToUInt32(RDiffBinary.FixEndian(sig), 0); ;

                byte[] strongbuf = new byte[m_stronglen];
                if (Utility.ForceStreamRead(input, strongbuf, strongbuf.Length) != strongbuf.Length)
                    throw new Exception(Strings.ChecksumFile.EndofstreamInStrongSignatureError);

                if (!m_checksums.ContainsKey(weak))
                    m_checksums[weak] = new List<int>();
                m_checksums[weak].Add(m_strongList.Count);
                m_strongList.Add(new KeyValuePair<uint, byte[]>(weak, strongbuf));
            }
        }

        /// <summary>
        /// Constructs a new CheckSum file with default sizes
        /// </summary>
        public ChecksumFile()
            : this(Adler32Checksum.DEFAULT_BLOCK_SIZE, 8)
        {
        }

        /// <summary>
        /// Constructs a new CheckSum file
        /// <param name="blocklength">The length of a single block</param>
        /// <param name="stronglength">The number of bytes in the MD4 checksum</param>
        /// </summary>
        public ChecksumFile(int blocklength, int stronglength)
        {
            m_blocklen = blocklength;
            m_stronglen = stronglength;
            m_checksums = new Dictionary<uint, List<int>>();
            m_strongList = new List<KeyValuePair<uint, byte[]>>();
            m_md4 = System.Security.Cryptography.MD4.Create("MD4");
            m_md4.Initialize();

            //m_sha = System.Security.Cryptography.HashAlgorithm.Create("SHA256");
            //m_sha.Initialize();
        }

        /// <summary>
        /// Adds all checksums in an entire stream 
        /// </summary>
        /// <param name="stream">The stream to read checksums from</param>
        public void AddStream(System.IO.Stream stream)
        {
            byte[] buffer = new byte[m_blocklen];
            int a;
            while ((a = Utility.ForceStreamRead(stream, buffer, buffer.Length)) != 0)
            {
                if (a != buffer.Length)
                {
                    byte[] tmp = new byte[a];
                    Array.Copy(buffer, tmp, a);
                    AddChunk(tmp);
                }
                else
                    AddChunk(buffer);
            }
        }

        /// <summary>
        /// Adds a chunck of data to checksum list
        /// </summary>
        /// <param name="buffer">The data to add a checksum entry for</param>
        public void AddChunk(byte[] buffer)
        {
            m_adler = new Adler32Checksum(new System.IO.MemoryStream(buffer), m_blocklen);
            if (!m_md4.CanReuseTransform)
                m_md4 = System.Security.Cryptography.MD4.Create("MD4");
            m_md4.Initialize();

            byte[] strong = m_md4.ComputeHash(buffer);
            if (strong.Length > m_stronglen)
            {
                byte[] tmp = new byte[m_stronglen];
                Array.Copy(strong, tmp, tmp.Length);
                strong = tmp;
            }

            if (!m_checksums.ContainsKey(m_adler.Checksum))
                m_checksums[m_adler.Checksum] = new List<int>();
            m_checksums[m_adler.Checksum].Add(m_strongList.Count);
            m_strongList.Add(new KeyValuePair<uint, byte[]>(m_adler.Checksum, strong));
            
            //TODO: Figure out if we can get a complete file signature this way
            //m_sha.ComputeHash(buffer);
        }

        /// <summary>
        /// Writes a RDiff compatible file with the signatures
        /// </summary>
        /// <param name="output"></param>
        public void Save(System.IO.Stream output)
        {
            output.Write(RDiffBinary.SIGNATURE_MAGIC, 0, RDiffBinary.SIGNATURE_MAGIC.Length);
            output.Write(RDiffBinary.FixEndian(BitConverter.GetBytes(m_blocklen)), 0, 4);
            output.Write(RDiffBinary.FixEndian(BitConverter.GetBytes(m_stronglen)), 0, 4);

            foreach(KeyValuePair<uint, byte[]> k in m_strongList)
            {
                output.Write(RDiffBinary.FixEndian(BitConverter.GetBytes(k.Key)), 0, 4);
                output.Write(k.Value, 0, m_stronglen);
            }

            output.Flush();
        }

        /// <summary>
        /// Looks in the checksum table for a match to the weak entry
        /// </summary>
        /// <param name="weak">The entry to find a match for</param>
        /// <returns>A list of possible MD4 matches, and their indexes</returns>
        public List<KeyValuePair<int, byte[]>> FindChunk(uint weak)
        {
            if (!m_checksums.ContainsKey(weak))
                return null;
            else
            {
                List<KeyValuePair<int, byte[]>> lst = new List<KeyValuePair<int, byte[]>>();
                foreach (int i in m_checksums[weak])
                    lst.Add(new KeyValuePair<int, byte[]>(i, m_strongList[i].Value));
                return lst;
            }
        }

        /// <summary>
        /// Gets the number of bytes in each hashed block
        /// </summary>
        public int BlockLength { get { return m_blocklen; } }
        /// <summary>
        /// Gets the number of bytes in a signature file
        /// </summary>
        public int StrongLength { get { return m_stronglen; } }
    }
}
