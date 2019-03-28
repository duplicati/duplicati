//  Copyright (C) 2019, The Duplicati Team
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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.CommandLine.SubProcess
{
    /// <summary>
    /// Class that wraps an <see cref="IStreamProxy"/> instance and provides a <see cref="Stream"/>
    /// </summary>
    public class ProxyStreamMapper : Stream
    {
        /// <summary>
        /// The proxy to wrap
        /// </summary>
        private IStreamProxy m_stream;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.CommandLine.SubProcess.ProxyStreamMapper"/> class.
        /// </summary>
        /// <param name="source">The stream interface to mirror.</param>
        public ProxyStreamMapper(IStreamProxy source)
        {
            m_stream = source ?? throw new ArgumentNullException(nameof(source));
        }

        public override bool CanRead => true;
        public override bool CanSeek => m_stream.GetCanSeekAsync().Result;
        public override bool CanWrite => true;
        public override long Length => m_stream.GetLengthAsync().Result;
        public override long Position { get => m_stream.GetPositionAsync().Result; set => m_stream.SetPositionAsync(value).Wait(); }
        public override void Flush()
        {
            FlushAsync().Wait();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count).Result;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.End)
                offset = Length - offset;
            else if (origin == SeekOrigin.Current)
                offset += Position;

            return Position = offset;
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAsync(buffer, offset, count).Wait();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var t = await m_stream.ReadAsync(count);
            Array.Copy(t, 0, buffer, offset, t.Length);
            return t.Length;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (offset == 0 && count == buffer.Length)
                return m_stream.WriteAsync(buffer);
            var tmp = new byte[count];
            Array.Copy(buffer, offset, tmp, 0, count);
            return m_stream.WriteAsync(tmp);
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return m_stream.FlushAsync();
        }

        protected override void Dispose(bool disposing)
        {
            m_stream.Dispose();
            base.Dispose(disposing);
        }
    }
}
