using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// Represents a filestream for a temporary file, which will be deleted when disposed
    /// </summary>
    public class TempFileStream : Stream
    {
        private Stream m_stream;
        private TempFile m_file;

        public TempFileStream()
            : this(new TempFile())
        {
        }

        public TempFileStream(string file)
            : this(new TempFile(file))
        {
        }

        public TempFileStream(TempFile file)
        {
            m_file = file;
            m_stream = System.IO.File.Open(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        }

        public override bool CanRead
        {
            get { return m_stream.CanRead; }
        }

        public override bool CanSeek
        {
            get { return m_stream.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return m_stream.CanWrite; }
        }

        public override void Flush()
        {
            m_stream.Flush();
        }

        public override long Length
        {
            get { return m_stream.Length; }
        }

        public override long Position
        {
            get
            {
                return m_stream.Position;
            }
            set
            {
                m_stream.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return m_stream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return m_stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            m_stream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            m_stream.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                if (m_stream != null)
                {
                    m_stream.Dispose();
                    m_stream = null;
                }
                if (m_file != null)
                {
                    m_file.Dispose();
                    m_file = null;
                }
            }
        }
    }
}
