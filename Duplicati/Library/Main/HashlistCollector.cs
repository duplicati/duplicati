using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Duplicati.Library.Main
{
    public class HashlistCollector : IDisposable
    {
        private class StreamEnumerable : IEnumerable<string>
        {
            private Stream m_stream;

            public StreamEnumerable(Stream stream)
            {
                m_stream = stream;
            }

            private class StreamEnumerator : IEnumerator<string>, System.Collections.IEnumerator
            {
                private StreamReader m_reader;
                private string m_current;
                private Stream m_stream;

                public StreamEnumerator(Stream stream)
                {
                    m_stream = stream;
                    this.Reset();
                }

                public string Current
                {
                    get { return m_current; }
                }

                public void Dispose()
                {
                    m_reader = null;
                    m_stream = null;
                    m_current = null;
                }

                object System.Collections.IEnumerator.Current
                {
                    get { return this.Current; }
                }

                public bool MoveNext()
                {
                    m_current = m_reader.ReadLine();
                    return m_current != null;
                }

                public void Reset()
                {
                    m_stream.Position = 0;
                    m_current = null;
                    m_reader = new StreamReader(m_stream);
                }

                public IEnumerator<string> GetEnumerator()
                {
                    return new StreamEnumerator(m_stream);
                }

            }

            public IEnumerator<string> GetEnumerator()
            {
                return new StreamEnumerator(m_stream);
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return this.GetEnumerator();
            }
        }

        private Utility.TempFile m_file;
        private StreamWriter m_writer;
        private Stream m_stream;
        private long m_count;

        public HashlistCollector()
        {
            m_file = new Utility.TempFile();
            m_stream = new MemoryStream();
            m_writer = new StreamWriter(m_stream);
            m_count = 0;
        }

        public void Add(string hash)
        {
            m_writer.WriteLine(hash);
            if (m_stream is MemoryStream && m_stream.Length > (1024 * 1024 * 100))
            {
                m_file = new Utility.TempFile();
                m_writer.Flush();
                Stream fs = System.IO.File.OpenWrite(m_file);
                m_stream.CopyTo(fs);
                m_writer.Dispose();
                m_stream.Dispose();

                m_stream = fs;
                m_writer = new StreamWriter(m_stream);
            }

            m_count++;
        }

        public long Count { get { return m_count; } }

        public IEnumerable<string> Hashes 
        { 
            get 
            {
                m_writer.Flush();
                return new StreamEnumerable(m_stream); 
            }
        }

        public void Dispose()
        {
            if (m_writer != null)
            {
                m_writer.Dispose();
                m_writer = null;
            }

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
