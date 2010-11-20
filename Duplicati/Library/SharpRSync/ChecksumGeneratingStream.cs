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
    }
}
