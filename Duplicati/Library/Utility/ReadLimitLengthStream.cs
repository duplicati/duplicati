// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
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
    /// This can be used to create a stream that exposes only a small window of a source stream.
    /// Having multiple copies of this wrapper on the same base stream at the same time is not ideal
    /// (as the position will move around), but creating multiple of these streams one after another
    /// can be used to create a sliding window over a base stream.
    /// </summary>
    public class ReadLimitLengthStream : Stream
    {
        private readonly Stream m_innerStream;
        private readonly long m_start;
        private readonly long m_length;

        public ReadLimitLengthStream(Stream innerStream, long length)
            : this(innerStream, 0, length)
        {
        }

        public ReadLimitLengthStream(Stream innerStream, long start, long length)
        {
            if (innerStream == null)
            {
                throw new ArgumentNullException(nameof(innerStream));
            }
            if (start < 0 || start > innerStream.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }
            if (length < 0 || innerStream.Length < start + length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            m_innerStream = innerStream;
            m_start = start;
            m_length = length;

            if (m_start != 0)
            {
                // Make sure the stream is starting at the expected point
                if (m_innerStream.Position != m_start)
                {
                    if (m_innerStream.CanSeek)
                    {
                        m_innerStream.Seek(m_start, SeekOrigin.Begin);
                    }
                    else if (m_innerStream.Position < m_start)
                    {
                        // If the underlying stream doesn't support seeking,
                        // this will instead simulate the seek by reading until
                        // the underlying stream is at the start position.
                        long bytesToRead = m_start - m_innerStream.Position;
                        for (long i = 0; i < bytesToRead; i++)
                        {
                            m_innerStream.ReadByte();
                        }
                    }
                    else
                    {
                        throw new ArgumentException("Cannot seek stream to starting position", nameof(innerStream));
                    }
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
                    m_innerStream.Seek(m_start + offset, SeekOrigin.Begin);
                    break;
                case SeekOrigin.Current:
                    m_innerStream.Seek(offset, SeekOrigin.Current);
                    break;
                case SeekOrigin.End:
                    m_innerStream.Seek(m_start + m_length + offset, SeekOrigin.Begin);
                    break;
                default:
                    throw new ArgumentException("Unknown SeekOrigin", nameof(origin));
            }

            return this.Position;
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
            if (m_innerStream.Position < m_start)
            {
                // The stream is positioned before the starting point.
                // This state is exposed externally as having Position==0,
                // so if possible, seek to the start and then read from there.
                if (CanSeek)
                {
                    this.Position = 0;
                }
                else
                {
                    // If the underlying stream doesn't support seeking though,
                    // this will instead simulate the seek by reading until the underlying stream is at the start position.
                    long bytesToRead = m_start - m_innerStream.Position;
                    for (long i = 0; i < bytesToRead; i++)
                    {
                        m_innerStream.ReadByte();
                    }
                }
            }

            long pos = this.Position;
            if (pos >= m_length)
            {
                // The stream is at or past the end of the limit.
                // Nothing should be read.
                return 0;
            }

            long maxCount = m_length - pos;
            if (count > maxCount)
            {
                return (int)maxCount;
            }

            return count;
        }

        private long ClampInnerStreamPosition(long position)
        {
            // Note that this allows this stream to have positions in the range 0 to m_length.
            // Reading at m_length should return nothing.
            if (position < m_start)
            {
                return m_start;
            }

            long maxPosition = m_start + m_length;
            if (position > maxPosition)
            {
                return maxPosition;
            }

            return position;
        }
    }
}
