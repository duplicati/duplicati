using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Main
{
    public class RestoreStatistics
    {
        private DateTime m_beginTime;
        private DateTime m_endTime;
        private long m_remoteCalls;
        private long m_bytesDownloaded;
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

        public long RemoteCalls
        {
            get { return m_remoteCalls; }
            set { m_remoteCalls = value; }
        }

        public long BytesDownloaded
        {
            get { return m_bytesDownloaded; }
            set { m_bytesDownloaded = value; }
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
            sb.AppendLine("Begin time       : " + this.BeginTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("End time         : " + this.EndTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("Duration         : " + this.Duration.ToString());
            sb.AppendLine("Remote calls     : " + this.RemoteCalls.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("Downloaded bytes : " + this.BytesDownloaded.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("Files restored   : " + this.FilesRestored.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("Restored size    : " + this.SizeOfRestoredFiles.ToString(System.Globalization.CultureInfo.InvariantCulture));

            return sb.ToString();
        }
    }
}
