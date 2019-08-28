using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Utility
{
    // StreamReadLimitLengthWrapper() based on code from Matt Smith (Oct 26 '15 at 20:38)
    // https://stackoverflow.com/questions/33354822/how-to-set-length-in-stream-without-truncated

    public class ReadLimitLengthStream : Stream
    {
        readonly Stream m_innerStream;
        readonly long m_endPosition;

        public ReadLimitLengthStream(Stream innerStream, long size)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            m_innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
            m_endPosition = m_innerStream.Position + size;
        }

        public override bool CanRead => m_innerStream.CanRead;

        public override bool CanSeek => m_innerStream.CanSeek;

        public override bool CanWrite => false;

        public override void Flush()
        {
            m_innerStream.Flush();
        }

        public override long Length => m_endPosition;

        public override long Position
        {
            get => m_innerStream.Position;
            set => m_innerStream.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            count = GetAllowedCount(count);
            return m_innerStream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return m_innerStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override bool CanTimeout => m_innerStream.CanTimeout;

        public override int ReadTimeout
        {
            get => m_innerStream.ReadTimeout;
            set => m_innerStream.ReadTimeout = value;
        }

        public override int WriteTimeout
        {
            get => m_innerStream.ReadTimeout;
            set => m_innerStream.ReadTimeout = value;
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            count = GetAllowedCount(count);
            return m_innerStream.BeginRead(buffer, offset, count, callback, state);
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            throw new NotSupportedException();
        }

        public override void Close()
        {
            // Since this wrapper does not own the underlying stream, we do not want it to close the underlying stream
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            return m_innerStream.CopyToAsync(destination, bufferSize, cancellationToken);
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            return m_innerStream.EndRead(asyncResult);
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return m_innerStream.FlushAsync(cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            count = GetAllowedCount(count);
            return m_innerStream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override int ReadByte()
        {
            var count = GetAllowedCount(1);
            if (count == 0)
            {
                return -1;
            }

            return m_innerStream.ReadByte();
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public override void WriteByte(byte value)
        {
            throw new NotSupportedException();
        }

        private int GetAllowedCount(int count)
        {
            long pos = m_innerStream.Position;
            long maxCount = m_endPosition - pos;
            if (count > maxCount)
            {
                return (int)maxCount;
            }

            return count;
        }
    }
}
