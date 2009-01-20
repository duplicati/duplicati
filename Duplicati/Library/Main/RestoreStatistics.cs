using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Main
{
    public class RestoreStatistics : CommunicationStatistics
    {
        private DateTime m_beginTime;
        private DateTime m_endTime;
        private long m_filesRestored;
        private long m_sizeOfRestoredFiles;

        public RestoreStatistics()
        {
            m_beginTime = m_endTime = DateTime.Now;
        }

        public DateTime BeginTime
        {
            get { return m_beginTime; }
            set { m_beginTime = value; }
        }

        public DateTime EndTime
        {
            get { return m_endTime; }
            set { m_endTime = value; }
        }

        public TimeSpan Duration
        {
            get { return m_beginTime - m_endTime; }
        }

        public long FilesRestored
        {
            get { return m_filesRestored; }
            set { m_filesRestored = value; }
        }

        public long SizeOfRestoredFiles
        {
            get { return m_sizeOfRestoredFiles; }
            set { m_sizeOfRestoredFiles = value; }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("BeginTime       : " + this.BeginTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("EndTime         : " + this.EndTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("Duration         : " + this.Duration.ToString());
            sb.AppendLine("Files restored   : " + this.FilesRestored.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("Restored size    : " + this.SizeOfRestoredFiles.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(base.ToString());
            return sb.ToString();
        }
    }
}
