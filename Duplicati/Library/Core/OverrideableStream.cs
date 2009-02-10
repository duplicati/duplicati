using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Duplicati.Library.Core
{
    /// <summary>
    /// This class is a wrapper for a stream.
    /// It's only purpose is to free other wrappers from implementing the boilerplate functions,
    /// and allow the derived classes to override single functions.
    /// </summary>
    public class OverrideableStream : Stream
    {
        protected System.IO.Stream m_basestream;

        public OverrideableStream(Stream basestream)
        {
            if (basestream == null)
                throw new ArgumentNullException("basestream");
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
            if (m_basestream != null)
                m_basestream.Dispose();
            m_basestream = null;
            base.Dispose(disposing);
        }
        
    }
}
