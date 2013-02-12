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
    /// This class represents a readable stream which generates a signature stream while being read
    /// </summary>
    public class ChecksumGeneratingStream : System.IO.Stream
    {
        /// <summary>
        /// The checksum output stream
        /// </summary>
        private ChecksumFileWriter m_outstream;

        /// <summary>
        /// The stream being read
        /// </summary>
        private System.IO.Stream m_basestream;

        /// <summary>
        /// A buffer for storing checksum data
        /// </summary>
        private byte[] m_checksumBuffer;

        /// <summary>
        /// The current index into the checksum buffer
        /// </summary>
        private int m_checksumBufferIndex;

        /// <summary>
        /// Constructs a new checksum generating stream
        /// </summary>
        /// <param name="signatureOutput">The stream into which signature data is written</param>
        /// <param name="inputData">The source data stream</param>
        public ChecksumGeneratingStream(System.IO.Stream signatureOutput, System.IO.Stream inputData)
        {
            if (!signatureOutput.CanWrite)
                throw new ArgumentException("signatureOutput");
            if (!inputData.CanRead)
                throw new ArgumentException("inputData");

            m_outstream = new ChecksumFileWriter(signatureOutput);
            m_basestream = inputData;

            m_checksumBuffer = new byte[m_outstream.BlockLength];
            m_checksumBufferIndex = 0;
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void Flush()
        {
            if (m_basestream != null)
            {
                m_basestream.Dispose();
                if (m_checksumBufferIndex != 0)
                    m_outstream.AddChunk(m_checksumBuffer, 0, m_checksumBufferIndex);
            }

            m_outstream = null;
            m_basestream = null;
        }

        public override long Length
        {
            get { return m_basestream.Length; }
        }

        public override long Position
        {
            get
            {
                return m_basestream.Position;
            }
            set
            {
                if (m_basestream.Position != value)
                    throw new InvalidOperationException();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int a = m_basestream.Read(buffer, offset, count);

            int remains = a;
            while (remains > 0)
            {
                int bytesToWrite = Math.Min(remains, m_checksumBuffer.Length - m_checksumBufferIndex);
                Array.Copy(buffer, offset, m_checksumBuffer, m_checksumBufferIndex, bytesToWrite);
                m_checksumBufferIndex += bytesToWrite;
                if (m_checksumBufferIndex == m_checksumBuffer.Length)
                {
                    m_outstream.AddChunk(m_checksumBuffer);
                    m_checksumBufferIndex = 0;
                }
                remains -= bytesToWrite;
                offset += bytesToWrite;
            }

            return a;
        }

        public override long Seek(long offset, System.IO.SeekOrigin origin)
        {
            throw new InvalidOperationException();
        }

        public override void SetLength(long value)
        {
            throw new InvalidOperationException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Flush();

            base.Dispose(disposing);
        }

        /// <summary>
        /// Returns the number of bytes generated when processing the specified amount of bytes
        /// </summary>
        /// <param name="filesize">The size of the file to process</param>
        /// <returns>The expected size of the signature file</returns>
        public int BytesGeneratedForSignature(long filesize)
        {
            return m_outstream.BytesGeneratedForSignature(filesize);
        }
    }
}
