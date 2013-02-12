#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// This is a stream wrapper that reports the current progress of reading or writing the stream to the supplied delegate
    /// </summary>
    public class ProgressReportingStream : OverrideableStream
    {
        public delegate void ProgressDelegate(int progress);
        public event ProgressDelegate Progress;
        private int m_lastPg;
        private long m_expectedSize;
        private long m_streamOffset;

        public ProgressReportingStream(System.IO.Stream basestream, long expectedSize)
            : base(basestream)
        {
            m_lastPg = -1;
            m_expectedSize = expectedSize;
            m_streamOffset = 0;
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
            /*if (Progress != null)
                Progress(100);*/
            base.Dispose(disposing);
        }

        private void ReportProgress()
        {
            if (Progress != null && m_expectedSize > 0)
            {
                int pg = (int)(((m_streamOffset) / (double)m_expectedSize) * 100);
                if (pg != m_lastPg)
                {
                    m_lastPg = pg;
                    Progress(pg);
                }
            }
        }
    }
}
