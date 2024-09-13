// Copyright (C) 2024, The Duplicati Team
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

using System.IO;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// A small utility stream that allows to keep streams open and counts the bytes sent through.
    /// </summary>
    public class ShaderStream : WrappingStream
    {
        private readonly bool m_keepBaseOpen;
        private long m_read = 0;
        private long m_written = 0;

        public ShaderStream(Stream baseStream, bool keepBaseOpen)
            : base(baseStream)
        {
            m_keepBaseOpen = keepBaseOpen;
        }

        public long TotalBytesRead { get { return m_read; } }
        public long TotalBytesWritten { get { return m_written; } }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int r = BaseStream.Read(buffer, offset, count);
            m_read += r;
            return r;
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            BaseStream.Write(buffer, offset, count);
            m_written += count;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !m_keepBaseOpen)
                BaseStream.Close();
            base.Dispose(disposing);
        }
    }
}
