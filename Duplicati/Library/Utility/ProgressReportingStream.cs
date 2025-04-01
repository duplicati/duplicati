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
using System.Text;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// This is a stream wrapper that reports the current progress of reading or writing the stream to the supplied delegate
    /// </summary>
    public class ProgressReportingStream : OverrideableStream
    {
        private readonly Action<long> m_progress;
        private long m_streamOffset;

        public ProgressReportingStream(System.IO.Stream basestream, Action<long> progress)
            : base(basestream)
        {
            m_streamOffset = 0;
            m_progress = progress;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReportProgress();
            
            int r = base.Read(buffer, offset, count);
            m_streamOffset += r;

            ReportProgress();
            
            return r;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ReportProgress();
            
            base.Write(buffer, offset, count);
            m_streamOffset += count;

            ReportProgress();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        private void ReportProgress()
        {
            if (m_progress != null)
                m_progress(m_streamOffset);
        }
    }
}
