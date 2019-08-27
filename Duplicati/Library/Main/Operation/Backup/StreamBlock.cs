//  Copyright (C) 2016, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using CoCoL;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation.Backup
{
    internal struct StreamProcessResult
    {
        public string Streamhash { get; internal set; }
        public long Streamlength { get; internal set; }
        public long Blocksetid { get; internal set; }
    }

    internal struct StreamBlock
    {
        public string Path;
        public Stream Stream;
        public bool IsMetadata;
        public CompressionHint Hint;
        public TaskCompletionSource<StreamProcessResult> Result;

        public static async Task<StreamProcessResult> ProcessStream(IWriteChannel<StreamBlock> channel, string path, Stream stream, bool isMetadata, CompressionHint hint)
        {
            var tcs = new TaskCompletionSource<StreamProcessResult>();

            // limit the stream length to that found now, a fixed point in time
            var limitedStream = new StreamReadLimitLengthWrapper(stream, stream.Length);
            
            var streamBlock = new StreamBlock
            {
                Path = path,
                Stream = limitedStream,
                IsMetadata = isMetadata,
                Hint = hint,
                Result = tcs
            };
            
            await channel.WriteAsync(streamBlock);
            
            return await tcs.Task.ConfigureAwait(false);
        }
    }

    // StreamReadLimitLengthWrapper() based on code from Matt Smith (Oct 26 '15 at 20:38)
    // https://stackoverflow.com/questions/33354822/how-to-set-length-in-stream-without-truncated
    sealed class StreamReadLimitLengthWrapper : Stream
    {
        readonly Stream m_innerStream;
        readonly long m_endPosition;

        public StreamReadLimitLengthWrapper(Stream innerStream, long size)
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
