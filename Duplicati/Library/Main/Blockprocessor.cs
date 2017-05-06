using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Duplicati.Library.Main
{
    public class BlockProcessor : IDisposable
    {
        private Stream m_stream;

        public BlockProcessor(Stream stream, int bufferSizeBytes)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            m_stream = new BufferedStream(stream, bufferSizeBytes);
        }

        public int ReadBlock(byte[] buffer)
        {
            var bytesleft = buffer.Length;
            var bytesread = 0;
            var read = 1;

            while (bytesleft > 0 && read > 0)
            {
                read = m_stream.Read(buffer, bytesread, bytesleft);
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
        }
    }
}
