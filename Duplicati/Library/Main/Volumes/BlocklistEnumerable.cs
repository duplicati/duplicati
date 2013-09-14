using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Interface;
using System.IO;

namespace Duplicati.Library.Main.Volumes
{
    internal class BlocklistEnumerable : IEnumerable<string>
    {
        private class BlocklistEnumerator : IEnumerator<string>
        {
            private ICompression m_compression;
            private string m_filename;
            private string m_current;
            private System.IO.Stream m_stream;
            private readonly byte[] m_buffer;

            public BlocklistEnumerator(ICompression compression, string filename, long hashsize)
            {
                m_compression = compression;
                m_filename = filename;
                m_buffer = new byte[hashsize];
                this.Reset();
            }

            public string Current { get { return m_current; } }
            public void Dispose()
            {
                if (m_stream != null)
                    try { m_stream.Dispose(); }
                    finally { m_stream = null; }
            }

            object System.Collections.IEnumerator.Current { get { return this.Current; } }

            public bool MoveNext()
            {
                long s = Library.Utility.Utility.ForceStreamRead(m_stream, m_buffer, m_buffer.Length);
                if (s == 0)
                    return false;

                if (s != m_buffer.Length)
                    throw new InvalidDataException("Premature End-of-stream encountered while reading blocklist hashes");

                m_current = Convert.ToBase64String(m_buffer);
                return true;
            }

            public void Reset()
            {
                this.Dispose();
                m_current = null;
                m_stream = m_compression.OpenRead(m_filename);
            }
        }

        private ICompression m_compression;
        private string m_filename;
        private long m_hashsize;

        public BlocklistEnumerable(ICompression compression, string filename, long hashsize)
        {
            m_compression = compression;
            m_filename = filename;
            m_hashsize = hashsize;
        }

        public IEnumerator<string> GetEnumerator() { return new BlocklistEnumerator(m_compression, m_filename, m_hashsize); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return this.GetEnumerator(); }
    }
}
