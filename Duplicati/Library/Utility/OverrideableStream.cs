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

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// This class is a wrapper for a stream.
    /// It's only purpose is to free other wrappers from implementing the boilerplate functions,
    /// and allow the derived classes to override single functions.
    /// </summary>
    public class OverrideableStream : Stream
    {
        protected System.IO.Stream m_basestream;

        public OverrideableStream(Stream basestream)
        {
            if (basestream == null)
                throw new ArgumentNullException(nameof(basestream));
            m_basestream = basestream;
        }

        public override bool CanRead
        {
            get { return m_basestream.CanRead; }
        }

        public override bool CanSeek
        {
            get { return m_basestream.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return m_basestream.CanWrite; }
        }

        public override void Flush()
        {
            m_basestream.Flush();
        }

        public override long Length
        {
            get { return m_basestream.Length; }
        }

        public override long Position
        {
            get
            {
                return m_basestream.Position;
            }
            set
            {
                m_basestream.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return m_basestream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, System.IO.SeekOrigin origin)
        {
            return m_basestream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            m_basestream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            m_basestream.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (m_basestream != null)
                m_basestream.Dispose();
            m_basestream = null;
            base.Dispose(disposing);
        }
        
    }
}
