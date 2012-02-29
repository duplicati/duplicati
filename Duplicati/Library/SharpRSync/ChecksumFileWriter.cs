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

namespace Duplicati.Library.SharpRSync
{
    /// <summary>
    /// This class writes signature files in the same format as RDiff
    /// </summary>
    public class ChecksumFileWriter
    {
        /// <summary>
        /// The number of bytes to include in each checksum
        /// </summary>
        public const int DEFAULT_BLOCK_SIZE = 2048;
        /// <summary>
        /// The default number of bytes to use from the strong hash
        /// </summary>
        public const int DEFAULT_STRONG_LEN = 8;
        /// <summary>
        /// The default number of bytes generated per input block
        /// </summary>
        public const int DEFAULT_BYTES_PER_BLOCK = DEFAULT_STRONG_LEN + 4;
        /// <summary>
        /// The number of bytes used for the rdiff header
        /// </summary>
        public static readonly int HEADER_SIZE = RDiffBinary.SIGNATURE_MAGIC.Length + 4 + 4;

        /// <summary>
        /// The length of a datablock
        /// </summary>
        private int m_blocklen;
        /// <summary>
        /// The number of bytes used for storing the strong signature
        /// </summary>
        private int m_stronglen;
        /// <summary>
        /// The re-used MD4 algorithm
        /// </summary>
        private System.Security.Cryptography.HashAlgorithm m_hashAlgorithm;
        /// <summary>
        /// The stream into which the signature data is written
        /// </summary>
        private System.IO.Stream m_outstream;

        /// <summary>
        /// Constructs a new CheckSum file with default sizes
        /// </summary>
        /// <param name="outputstream">The stream into which the checksum data is written</param>
        public ChecksumFileWriter(System.IO.Stream outputstream)
            : this(outputstream, DEFAULT_BLOCK_SIZE, DEFAULT_STRONG_LEN)
        {
        }

        /// <summary>
        /// Constructs a new CheckSum file
        /// <param name="blocklength">The length of a single block</param>
        /// <param name="stronglength">The number of bytes in the MD4 checksum</param>
        /// <param name="outputstream">The stream into which the checksum data is written</param>
        /// </summary>
        public ChecksumFileWriter(System.IO.Stream outputstream, int blocklength, int stronglength)
        {
            if (outputstream == null)
                throw new ArgumentNullException("outputstream");

            m_blocklen = blocklength;
            m_stronglen = stronglength;
            m_outstream = outputstream;
            m_hashAlgorithm = MD4Helper.Create();

            m_hashAlgorithm.Initialize();

            m_outstream.Write(RDiffBinary.SIGNATURE_MAGIC, 0, RDiffBinary.SIGNATURE_MAGIC.Length);
            m_outstream.Write(RDiffBinary.FixEndian(BitConverter.GetBytes(m_blocklen)), 0, 4);
            m_outstream.Write(RDiffBinary.FixEndian(BitConverter.GetBytes(m_stronglen)), 0, 4);
        }

        /// <summary>
        /// Adds a chunck of data to checksum list
        /// </summary>
        /// <param name="buffer">The data to add a checksum entry for</param>
        public void AddChunk(byte[] buffer)
        {
            AddChunk(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Adds a chunck of data to checksum list
        /// </summary>
        /// <param name="buffer">The data to add a checksum entry for</param>
        /// <param name="index">The index in the buffer to start reading from</param>
        /// <param name="count">The number of bytes to extract from the array</param>
        public void AddChunk(byte[] buffer, int index, int count)
        {
            if (!m_hashAlgorithm.CanReuseTransform)
                m_hashAlgorithm = Mono.Security.Cryptography.MD4.Create("MD4");

            m_outstream.Write(RDiffBinary.FixEndian(BitConverter.GetBytes(Adler32Checksum.Calculate(buffer, index, count))), 0, 4);
            m_outstream.Write(m_hashAlgorithm.ComputeHash(buffer, index, count), 0, m_stronglen);
        }

        /// <summary>
        /// Adds all checksums in an entire stream 
        /// </summary>
        /// <param name="stream">The stream to read checksums from</param>
        public void AddStream(System.IO.Stream stream)
        {
            byte[] buffer = new byte[m_blocklen];
            int a;

            //These streams do not fragment data
            if (stream is System.IO.FileStream || stream is System.IO.MemoryStream)
            {
                while ((a = stream.Read(buffer, 0, buffer.Length)) != 0)
                    AddChunk(buffer, 0, a);
            }
            else
            {
                while ((a = Utility.ForceStreamRead(stream, buffer, buffer.Length)) != 0)
                    AddChunk(buffer, 0, a);
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
        /// <summary>
        /// Gets the number of bytes generated per input block
        /// </summary>
        public int BytesPrBlock { get { return m_stronglen + 4; } }

        /// <summary>
        /// Returns the number of bytes generated when processing the specified amount of bytes
        /// </summary>
        /// <param name="filesize">The size of the file to process</param>
        /// <returns>The expected size of the signature file</returns>
        public int BytesGeneratedForSignature(long filesize)
        {
            return (int)(SharpRSync.ChecksumFileWriter.HEADER_SIZE +
                (((filesize + m_blocklen - 1) / m_blocklen) * (m_stronglen + 4)));
        }
    }
}
