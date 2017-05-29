using System;
using System.IO;

namespace Duplicati.Library.Compression
{
    /// <summary>
    /// CompressionStream wrapper to prevent closing the base stream when disposing the entry stream
    /// </summary>
    public class StreamWrapper : Utility.OverrideableStream
    {
        /// <summary>
        /// The callback to invoke when the stream is disposed
        /// </summary>
        private readonly Action<StreamWrapper> m_onCloseCallback;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Compression.StreamWrapper"/> class.
        /// </summary>
        /// <param name="stream">Stream.</param>
        /// <param name="onClose">On close.</param>
        public StreamWrapper(Stream stream, Action<StreamWrapper> onClose)
            : base(stream)
        {
            m_onCloseCallback = onClose;
        }

        /// <summary>
        /// Disposes the stream, and calls the close handler if required
        /// </summary>
        /// <param name="disposing">If set to <c>true</c>, this call originates from the dispose method.</param>
        protected override void Dispose(bool disposing)
        {
            if (!disposing || m_basestream == null) return;

            m_basestream.Dispose();
            m_basestream = null;

            if (m_onCloseCallback != null)
                m_onCloseCallback(this);
        }

        public long GetSize()
        {
            return m_basestream == null ? 0 : m_basestream.Length;
        }
    }
}
