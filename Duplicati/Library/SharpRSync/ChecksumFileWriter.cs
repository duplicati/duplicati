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
            : this(outputstream, Adler32Checksum.DEFAULT_BLOCK_SIZE, 8)
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
            m_hashAlgorithm = Mono.Security.Cryptography.MD4.Create("MD4");

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
    }
}
