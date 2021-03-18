using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Utility
{
    // StreamReadLimitLengthWrapper() based on code from Matt Smith (Oct 26 '15 at 20:38)
    // https://stackoverflow.com/questions/33354822/how-to-set-length-in-stream-without-truncated

    /// <summary>
    /// This class wraps a Stream but only allows reading a limited number of bytes from the underlying stream.
    /// This can be used to create split a source stream into multiple smaller buffers that can be used as independent streams.
    /// </summary>
    public class ReadLimitLengthStream : Stream
    {
        private readonly Stream m_innerStream;
        private readonly long m_start;
        private readonly long m_length;

        public ReadLimitLengthStream(Stream innerStream, long length)
            : this(innerStream, innerStream.Position, length)
        {
        }

        public ReadLimitLengthStream(Stream innerStream, long start, long length)
        {
            if (start < 0 || start >= innerStream.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }
            if (length < 0 || innerStream.Length < start + length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            m_innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
            m_start = start;
            m_length = length;

            // Make sure the stream is starting at the expected point
            if (m_innerStream.Position != m_start)
            {
                if (m_innerStream.CanSeek)
                {
                    m_innerStream.Seek(m_start, SeekOrigin.Begin);
                }
                else
                {
                    throw new ArgumentException("Cannot seek stream to starting position", nameof(innerStream));
                }
            }
        }

        public override bool CanRead => m_innerStream.CanRead;

        public override bool CanSeek => m_innerStream.CanSeek;

        public override bool CanWrite => false;

        public override void Flush()
        {
            // NOOP
        }

        public override long Length => m_length;

        public override long Position
        {
            get => ClampInnerStreamPosition(m_innerStream.Position) - m_start;
            set => m_innerStream.Position = ClampInnerStreamPosition(value + m_start);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            count = GetAllowedCount(count);
            return m_innerStream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    return m_innerStream.Seek(m_start + offset, SeekOrigin.Begin);
                case SeekOrigin.Current:
                    return m_innerStream.Seek(offset, SeekOrigin.Current);
                case SeekOrigin.End:
                    return m_innerStream.Seek(m_start + m_length + offset, SeekOrigin.Begin);
                default:
                    throw new ArgumentException("Unknown SeekOrigin", nameof(origin));
            }
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
            return Task.CompletedTask;
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
            long pos = m_innerStream.Position - m_start;
            long maxCount = m_length - pos;
            if (pos < 0 || maxCount < 0)
            {
                // The stream is somehow positioned before the starting point or after the end.
                // Nothing can be read.
                return 0;
            }

            if (count > maxCount)
            {
                return (int)maxCount;
            }

            return count;
        }

        private long ClampInnerStreamPosition(long position)
        {
            if (position < m_start)
            {
                return m_start;
            }

            if (position > m_start + m_length)
            {
                return m_start + m_length;
            }

            return position;
            }
    }
}
