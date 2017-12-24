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

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// A small utility stream that allows to keep streams open and counts the bytes sent through. </summary>
    /// </summary>
    public class ShaderStream : System.IO.Stream
    {
        private readonly System.IO.Stream m_baseStream;
        private readonly bool m_keepBaseOpen;
        private long m_read = 0;
        private long m_written = 0;

        public ShaderStream(System.IO.Stream baseStream, bool keepBaseOpen)
        {
            this.m_baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
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
