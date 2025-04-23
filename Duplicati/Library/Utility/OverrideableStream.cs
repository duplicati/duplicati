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
using System.Threading.Tasks;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// This class is a wrapper for a stream.
    /// It's only purpose is to free other wrappers from implementing the boilerplate functions,
    /// and allow the derived classes to override single functions.
    /// </summary>
    public class OverrideableStream : Stream
    {
        /// <summary>
        /// The base stream that is wrapped
        /// </summary>
        protected Stream m_basestream;

        /// <summary>
        /// Creates a new <see cref="OverrideableStream"/> instance
        /// </summary>
        /// <param name="basestream">The stream to wrap</param>
        /// <exception cref="ArgumentNullException"></exception>
        public OverrideableStream(Stream basestream)
        {
            m_basestream = basestream ?? throw new ArgumentNullException(nameof(basestream));
        }

        /// <inheritdoc/>
        public override bool CanRead => m_basestream.CanRead;

        /// <inheritdoc/>
        public override bool CanSeek => m_basestream.CanSeek;

        /// <inheritdoc/>
        public override bool CanWrite => m_basestream.CanWrite;

        /// <inheritdoc/>
        public override void Flush() => m_basestream.Flush();

        /// <inheritdoc/>
        public override long Length => m_basestream.Length;

        /// <inheritdoc/>
        public override long Position
        {
            get => m_basestream.Position;
            set => m_basestream.Position = value;
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
            => m_basestream.Read(buffer, offset, count);

        /// <inheritdoc/>
        public override long Seek(long offset, System.IO.SeekOrigin origin)
            => m_basestream.Seek(offset, origin);

        /// <inheritdoc/>
        public override void SetLength(long value)
            => m_basestream.SetLength(value);

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
            => m_basestream.Write(buffer, offset, count);

        /// <inheritdoc/>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
            => m_basestream.ReadAsync(buffer, offset, count, cancellationToken);

        /// <inheritdoc/>
        public override Task WriteAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
            => m_basestream.WriteAsync(buffer, offset, count, cancellationToken);

        /// <inheritdoc/>
        public override Task FlushAsync(System.Threading.CancellationToken cancellationToken)
            => m_basestream.FlushAsync(cancellationToken);

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            m_basestream?.Dispose();
            m_basestream = null;
            base.Dispose(disposing);
        }

    }
}
