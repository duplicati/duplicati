using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Main
{
    public class CommunicationStatistics
    {
        private long m_numberOfBytesUploaded;
        private long m_numberOfRemoteCalls;
        private long m_numberOfBytesDownloaded;

        private long m_numberOfErrors;
        private StringBuilder m_errorMessages = new StringBuilder();

        public long NumberOfBytesUploaded
        {
            get { return m_numberOfBytesUploaded; }
            set { m_numberOfBytesUploaded = value; }
        }

        public long NumberOfBytesDownloaded
        {
            get { return m_numberOfBytesDownloaded; }
            set { m_numberOfBytesDownloaded = value; }
        }

        public long NumberOfRemoteCalls
        {
            get { return m_numberOfRemoteCalls; }
            set { m_numberOfRemoteCalls = value; }
        }

        public void LogError(string errorMessage)
        {
            m_numberOfErrors++;
            m_errorMessages.AppendLine(errorMessage);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("BytesUploaded   : " + this.NumberOfBytesUploaded.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("BytesDownloaded : " + this.NumberOfBytesDownloaded.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("RemoteCalls     : " + this.NumberOfRemoteCalls.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");

            if (m_numberOfErrors > 0)
            {
                sb.Append("NumberOfErrors  : " + m_numberOfErrors .ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
                sb.Append("****************\r\n");
                sb.Append(m_errorMessages.ToString());
                sb.Append("****************\r\n");
            }

            return sb.ToString();
        }
    }
}
