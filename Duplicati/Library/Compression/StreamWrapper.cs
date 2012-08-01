using System.IO;

namespace Duplicati.Library.Compression
{

    /// <summary>
    /// CompressionStream wrapper to prevent closing the base stream when disposing the entry stream
    /// </summary>
    public class StreamWrapper : Utility.OverrideableStream
    {
        public delegate void DisposingHandler(StreamWrapper sender);

        public event DisposingHandler Disposing;

        public StreamWrapper(Stream stream)
            : base(stream)
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing || m_basestream == null) return;

            if (Disposing != null)
                Disposing(this);

            m_basestream.Dispose();
            m_basestream = null;
        }

        public long GetSize()
        {
            return m_basestream == null ? 0 : m_basestream.Length;
        }
    }
}