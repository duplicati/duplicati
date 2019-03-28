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
using System.Threading.Tasks;

namespace Duplicati.CommandLine.SubProcess
{
    /// <summary>
    /// Class that wraps a stream and provides an <see cref="IStreamProxy"/> interface to it
    /// </summary>
    public class StreamProxyMapper : IStreamProxy
    {
        /// <summary>
        /// The stream we are wrapping
        /// </summary>
        private Stream m_stream;

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="T:Duplicati.CommandLine.SubProcess.DynamicLoaderProxy.StreamProxyMapper"/> class.
        /// </summary>
        /// <param name="source">The stream to wrap.</param>
        public StreamProxyMapper(Stream source)
        {
            m_stream = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <inheritdoc />
        public Task FlushAsync()
        {
            return m_stream.FlushAsync();
        }

        /// <inheritdoc />
        public Task<bool> GetCanSeekAsync()
        {
            return Task.FromResult(m_stream.CanSeek);
        }

        /// <inheritdoc />
        public Task<long> GetLengthAsync()
        {
            return Task.FromResult(m_stream.Length);
        }

        /// <inheritdoc />
        public Task<long> GetPositionAsync()
        {
            return Task.FromResult(m_stream.Position);
        }

        /// <inheritdoc />
        public async Task<byte[]> ReadAsync(long maxsize)
        {
            var buf = new byte[maxsize];
            var r = await m_stream.ReadAsync(buf, 0, buf.Length);
            Array.Resize(ref buf, r);
            return buf;
        }

        /// <inheritdoc />
        public Task SetPositionAsync(long position)
        {
            m_stream.Position = position;
            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public Task WriteAsync(byte[] data)
        {
            return m_stream.WriteAsync(data, 0, data.Length);
        }

        public void Dispose()
        {
            m_stream.Dispose();
        }
    }
}
