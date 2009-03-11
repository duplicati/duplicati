using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Core
{
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
            if (Progress != null)
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
