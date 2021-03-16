#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// This class wraps a Stream but only allows reading a limited number of bytes from the underlying stream.
    /// This can be used to create split a source stream into multiple smaller buffers that can be used as independent streams.
    /// </summary>
    public class SubStream : Stream
    {
        private readonly Stream m_stream;
        private readonly long m_start;
        private readonly long m_length;

        public SubStream(Stream stream, long length)
            : this(stream, stream.Position, length)
        {
        }

        public SubStream(Stream stream, long start, long length)
        {
            this.m_stream = stream;
            this.m_start = start;

            // If the stream isn't long enough for this to be a full substream, then limit the length
            if (stream.Length >= start + length)
            {
                this.m_length = length;
            }
            else
            {
                this.m_length = stream.Length - start;
            }
        }

        public override bool CanRead => this.m_stream.CanRead;

        public override bool CanSeek => this.m_stream.CanSeek;

        public override bool CanWrite => false;

        public override long Length => this.m_length;

        public override long Position
        {
            get => this.m_stream.Position - this.m_start;
            set => this.m_stream.Position = value + this.m_start;
        }

        public override void Flush()
        {
            this.m_stream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            // Make sure reading is not allowed past the end of the given length
            if (count + this.Position > this.Length)
            {
                count = (int)(this.Length - this.Position);
            }

            return this.m_stream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    return this.m_stream.Seek(this.m_start + offset, SeekOrigin.Begin);
                case SeekOrigin.Current:
                    return this.m_stream.Seek(offset, SeekOrigin.Current);
                case SeekOrigin.End:
                    return this.m_stream.Seek(this.m_start + this.m_length + offset, SeekOrigin.Begin);
                default:
                    throw new ArgumentException("Unknown SeekOrigin", nameof(origin));
            }
        }

        public override void SetLength(long value)
        {
            // For this type, there isn't a clear meaning for what this should do, so this isn't implemented for now.
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            // This stream is read only for now. Not sure how to manage bounding writes withing the span allowed.
            throw new NotImplementedException();
        }
    }
}
