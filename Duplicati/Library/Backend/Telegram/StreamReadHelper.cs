using System.IO;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Backend
{
    /// <summary>
    ///     Private helper class to fix a bug with the StreamReader
    /// </summary>
    internal class StreamReadHelper : OverrideableStream
    {
        /// <summary>
        ///     Once the stream has returned 0 as the read count it is disposed,
        ///     and subsequent read requests will throw an ObjectDisposedException
        /// </summary>
        private bool m_empty;

        /// <summary>
        ///     Basic initialization, just pass the stream to the super class
        /// </summary>
        /// <param name="stream"></param>
        public StreamReadHelper(Stream stream) : base(stream)
        { }

        /// <summary>
        ///     Override the read function to make sure that we only return less than the requested amount of data if the stream is exhausted
        /// </summary>
        /// <param name="buffer">The buffer to place data in</param>
        /// <param name="offset">The offset into the buffer to start at</param>
        /// <param name="count">The number of bytes to read</param>
        /// <returns>The number of bytes read</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            var readCount = 0;
            int a;

            while (!m_empty && count > 0)
            {
                a = base.Read(buffer, offset, count);
                readCount += a;
                count -= a;
                offset += a;
                m_empty = a == 0;
            }

            return readCount;
        }
    }
}