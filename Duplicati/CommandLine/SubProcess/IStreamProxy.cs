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
using System.Threading.Tasks;

namespace Duplicati.CommandLine.SubProcess
{
    /// <summary>
    /// Interface for passing a stream by reference to another process
    /// </summary>
    public interface IStreamProxy : IDisposable
    {
        /// <summary>
        /// Reads a chunck of bytes from a stream
        /// </summary>
        /// <param name="maxsize">The maximum number of bytes to read</param>
        /// <returns>The data read.</returns>
        Task<byte[]> ReadAsync(long maxsize);

        /// <summary>
        /// Writes bytes to the stream
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="data">The data to write</param>
        Task WriteAsync(byte[] data);

        /// <summary>
        /// Sets the position of the stream, if it is seekable
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="position">The position to set.</param>
        Task SetPositionAsync(long position);

        /// <summary>
        /// Gets the position of the stream if it can be read
        /// </summary>
        /// <returns>The position of the stream.</returns>
        Task<long> GetPositionAsync();

        /// <summary>
        /// Gets a value indicating if the stream is seekable
        /// </summary>
        /// <returns>The seekabke value.</returns>
        Task<bool> GetCanSeekAsync();

        /// <summary>
        /// Gets the length of the stream, if possible.
        /// </summary>
        /// <returns>The length of the stream.</returns>
        Task<long> GetLengthAsync();

        /// <summary>
        /// Flushes the underlying stream
        /// </summary>
        /// <returns>An awaitable task.</returns>
        Task FlushAsync();
    }
}
