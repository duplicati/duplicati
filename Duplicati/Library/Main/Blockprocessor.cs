using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Duplicati.Library.Main
{
    public class Blockprocessor : IDisposable
    {
        private Stream m_stream;
        private byte[] m_buffer;

        public Blockprocessor(Stream stream, byte[] buffer)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            if (buffer == null)
                throw new ArgumentNullException("buffer");

            m_stream = stream;
            m_buffer = buffer;
        }

        public int Readblock()
        {
            var bytesleft = m_buffer.Length;
            var bytesread = 0;
            var read = 1;

            while (bytesleft > 0 && read > 0)
            {
                read = m_stream.Read(m_buffer, bytesread, bytesleft);
                bytesleft -= read;
                bytesread += read;
            }

            return bytesread;
        }
        
        public long Length { get { return m_stream.Length; } }

        public void Dispose()
        {
            if (m_stream != null)
                m_stream.Dispose();
            m_stream = null;
            m_buffer = null;
        }
    }
}
