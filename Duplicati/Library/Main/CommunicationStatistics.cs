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

namespace Duplicati.Library.Main
{
    public class CommunicationStatistics
    {
        private object m_lock = new object();
        private bool m_verboseErrors = false;
        private long m_numberOfBytesUploaded;
        private long m_numberOfRemoteCalls;
        private long m_numberOfBytesDownloaded;
        private DuplicatiOperationMode m_operationMode;

        private long m_numberOfErrors;
        private StringBuilder m_errorMessages = new StringBuilder();

        private long m_numberOfWarnings;
        private StringBuilder m_warningMessages = new StringBuilder();

        private bool m_verboseRetryErrors = false;
        private long m_lastRetryOperationNo = -1;
        private long m_numberOfRetriedOperations;
        private long m_numberOfRetries;
        private StringBuilder m_retryMessages = new StringBuilder();

        public CommunicationStatistics(DuplicatiOperationMode operationMode)
        {
            m_operationMode = operationMode;
        }

        public DuplicatiOperationMode OperationMode { get { return m_operationMode; } }

        public bool VerboseErrors
        {
            get { return m_verboseErrors; }
            set { m_verboseErrors = value; }
        }

        public bool VerboseRetryErrors
        {
            get { return m_verboseRetryErrors; }
            set { m_verboseRetryErrors = value; }
        }

        public long NumberOfBytesUploaded
        {
            get { return m_numberOfBytesUploaded; }
        }

        public long NumberOfBytesDownloaded
        {
            get { return m_numberOfBytesDownloaded; }
        }

        public long NumberOfRemoteCalls
        {
            get { return m_numberOfRemoteCalls; }
        }

        public void AddBytesUploaded(long value)
        {
            lock (m_lock)
                m_numberOfBytesUploaded += value;
        }

        public void AddBytesDownloaded(long value)
        {
            lock (m_lock)
                m_numberOfBytesDownloaded += value;
        }

        public void AddNumberOfRemoteCalls(long value)
        {
            lock (m_lock)
                m_numberOfRemoteCalls += value;
        }

        public void LogError(string errorMessage, Exception ex)
        {
            lock (m_lock)
            {
                m_numberOfErrors++;
                m_errorMessages.AppendLine(errorMessage);
                if (m_verboseErrors)
                    while (ex != null)
                    {
                        m_errorMessages.AppendLine(ex.ToString());
                        ex = ex.InnerException;
                    }
            }
        }

        public void LogRetryAttempt(string errorMessage, Exception ex)
        {
            m_numberOfRetries++;
            if (m_lastRetryOperationNo != m_numberOfRemoteCalls + 1)
                m_numberOfRetriedOperations++;
            m_lastRetryOperationNo = m_numberOfRemoteCalls;

            if (m_verboseErrors || m_verboseRetryErrors)
            {
                m_retryMessages.AppendLine(errorMessage);
                if (m_verboseErrors) 
                    while (ex != null)
                    {
                        m_retryMessages.AppendLine(ex.ToString());
                        ex = ex.InnerException;
                    }
            }
        }

        public override string ToString()
        {
            //TODO: Figure out how to translate this without breaking the output parser
            StringBuilder sb = new StringBuilder();
            sb.Append("Executable      : " + Utility.Utility.getEntryAssembly().FullName + "\r\n");
            sb.Append("Library         : " + System.Reflection.Assembly.GetExecutingAssembly().FullName + "\r\n");
            sb.Append("OperationName   : " + this.OperationMode.ToString() + "\r\n");
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

            if (m_numberOfWarnings > 0)
            {
                sb.Append("NumberOfWarnings: " + m_numberOfWarnings.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
                sb.Append("****************\r\n");
                sb.Append(m_warningMessages.ToString());
                sb.Append("****************\r\n");
            }

            if (m_numberOfRetries > 0)
            {
                sb.Append("NumberOfRetries : " + m_numberOfRetries.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
                sb.Append("RetryOperations : " + m_numberOfRetriedOperations.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
                if (m_retryMessages.Length > 0)
                {
                    sb.Append("****************\r\n");
                    sb.Append(m_retryMessages.ToString());
                    sb.Append("****************\r\n");
                }
            }

            return sb.ToString();
        }

        public void LogWarning(string warningMessage, Exception ex)
        {
            lock (m_lock)
            {
                m_numberOfWarnings++;
                m_warningMessages.AppendLine(warningMessage);
                if (m_verboseErrors)
                    while (ex != null)
                    {
                        m_warningMessages.AppendLine(ex.ToString());
                        ex = ex.InnerException;
                    }
            }
        }
    }
}
