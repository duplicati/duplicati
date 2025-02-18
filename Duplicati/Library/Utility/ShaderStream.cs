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

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// A small utility stream that allows to keep streams open and counts the bytes sent through.
    /// </summary>
    public class ShaderStream : System.IO.Stream
    {
        private readonly System.IO.Stream m_baseStream;
        private readonly bool m_keepBaseOpen;
        private long m_read = 0;
        private long m_written = 0;

        public ShaderStream(System.IO.Stream baseStream, bool keepBaseOpen)
        {
            if (baseStream == null)
                throw new ArgumentNullException(nameof(baseStream));
            this.m_baseStream = baseStream;
            this.m_keepBaseOpen = keepBaseOpen;
        }

        public long TotalBytesRead { get { return m_read; } }
        public long TotalBytesWritten { get { return m_written; } }

        public override bool CanRead { get { return m_baseStream.CanRead; } }
        public override bool CanSeek { get { return m_baseStream.CanSeek; } }
        public override bool CanWrite { get { return m_baseStream.CanWrite; } }
        public override long Length { get { return m_baseStream.Length; } }
        public override long Position
        {
            get { return m_baseStream.Position; }
            set { m_baseStream.Position = value; }
        }
        public override void Flush() { m_baseStream.Flush(); }
        public override long Seek(long offset, System.IO.SeekOrigin origin) { return m_baseStream.Seek(offset, origin); }
        public override void SetLength(long value) { m_baseStream.SetLength(value); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int r = m_baseStream.Read(buffer, offset, count);
            m_read += r;
            return r;
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            m_baseStream.Write(buffer, offset, count);
            m_written += count;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !m_keepBaseOpen)
                m_baseStream.Close();
            base.Dispose(disposing);
        }
    }
}
