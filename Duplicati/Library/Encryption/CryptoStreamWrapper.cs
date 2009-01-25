using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Duplicati.Library.Encryption
{
    internal class CryptoStreamWrapper : Stream
    {
        private bool m_hasFlushed = false;
        private System.Security.Cryptography.CryptoStream m_basestream;

        /// <summary>
        /// Wraps a crypto stream, ensuring that it is correctly disposed
        /// </summary>
        /// <param name="basestream">The stream to wrape</param>
        public CryptoStreamWrapper(System.Security.Cryptography.CryptoStream basestream)
        {
            if (basestream == null)
                throw new NullReferenceException("basestream");
            m_basestream = basestream;
        }

        public override bool CanRead
        {
            get { return m_basestream.CanRead; }
        }

        public override bool CanSeek
        {
            get { return m_basestream.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return m_basestream.CanWrite; }
        }

        public override void Flush()
        {
            m_basestream.Flush();
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
                m_basestream.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return m_basestream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, System.IO.SeekOrigin origin)
        {
            return m_basestream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            m_basestream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            m_basestream.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            //This is actually the only thing we want to wrap :(
            if (!m_hasFlushed && m_basestream.CanWrite)
            {
                m_basestream.FlushFinalBlock();
                m_hasFlushed = true;
            }

            base.Dispose(disposing);
        }
    }
}
