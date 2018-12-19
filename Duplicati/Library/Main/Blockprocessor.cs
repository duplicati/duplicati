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
        private bool m_depleted = false;

        public Blockprocessor(Stream stream, byte[] buffer)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            m_stream = stream;
            m_buffer = buffer;
        }

        public int Readblock()
        {
            if (m_depleted)
                return 0;
            
            int bytesRead = Duplicati.Library.Utility.Utility.ForceStreamRead(this.m_stream, this.m_buffer, this.m_buffer.Length);
            m_depleted = this.m_buffer.Length > bytesRead;

            return bytesRead;
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
