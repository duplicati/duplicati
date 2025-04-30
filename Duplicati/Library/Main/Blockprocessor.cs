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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Duplicati.Library.Main
{
    public class Blockprocessor : IDisposable
    {
        private Stream m_stream;
        private byte[] m_buffer;
        private bool m_depleted = false;

        public Blockprocessor(Stream stream, byte[] buffer)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            m_stream = stream;
            m_buffer = buffer;
        }

        public int Readblock()
        {
            if (m_depleted)
                return 0;
            
            int bytesRead = Duplicati.Library.Utility.Utility.ForceStreamRead(this.m_stream, this.m_buffer, this.m_buffer.Length);
            m_depleted = this.m_buffer.Length > bytesRead;

            return bytesRead;
        }
        
        public long Length { get { return m_stream.Length; } }

        public void Dispose()
        {
            if (m_stream != null)
                m_stream.Dispose();
            m_stream = null;
            m_buffer = null;
        }
    }
}
