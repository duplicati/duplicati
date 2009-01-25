using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Duplicati.Library.Encryption
{
    internal class GPGStreamWrapper : Stream
    {
        private System.Diagnostics.Process m_p;
        private System.Threading.Thread m_t;
        private Stream m_basestream;

        /// <summary>
        /// Wraps a crypto stream, ensuring that it is correctly disposed
        /// </summary>
        /// <param name="basestream">The stream to wrape</param>
        public GPGStreamWrapper(System.Diagnostics.Process p, System.Threading.Thread t, Stream basestream)
        {
            if (p == null)
                throw new NullReferenceException("p");
            if (t == null)
                throw new NullReferenceException("t");
            
            m_p = p;
            m_t = t;
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
            if (m_p != null)
            {
                m_basestream.Close();

                if (!m_t.Join(5000))
                    throw new Exception("Failure while invoking GnuPG, program won't flush output");

                if (!m_p.WaitForExit(5000))
                    throw new Exception("Failure while invoking GnuPG, program won't terminate");

                if (m_p.StandardError.Peek() != -1)
                {
                    string errmsg = m_p.StandardError.ReadToEnd();
                    if (errmsg.Contains("decryption failed:"))
                        throw new Exception("Decryption failed: " + errmsg);
                }

                m_p.Dispose();
                m_p = null;

                m_t = null;
            }

            base.Dispose(disposing);
        }
    }
}
