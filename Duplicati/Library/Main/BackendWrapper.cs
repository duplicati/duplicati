using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// Internal class that ensures retry operations and tracks statistics
    /// </summary>
    internal class BackendWrapper : Backend.IBackendInterface 
    {
        private Backend.IBackendInterface m_backend;
        private CommunicationStatistics m_statistics;
        
        private int m_retries;
        private TimeSpan m_retrydelay;

        public BackendWrapper(CommunicationStatistics statistics, Backend.IBackendInterface backend, Dictionary<string, string> options)
        {
            m_statistics = statistics;
            m_backend = backend;
            m_retries = 5;

            if (options.ContainsKey("number-of-retries"))
                int.TryParse(options["number-of-retries"], out m_retries);

            m_retrydelay = new TimeSpan(TimeSpan.TicksPerSecond * 10);
            if (options.ContainsKey("retry-delay"))
                try { Core.Timeparser.ParseTimeSpan(options["retry-delay"]); }
                catch { }
        }

        #region IBackendInterface Members

        public string DisplayName
        {
            get { return m_backend.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return m_backend.ProtocolKey; }
        }

        public List<Duplicati.Library.Backend.FileEntry> List()
        {
            int retries = m_retries;

            do
            {
                try
                {
                    m_statistics.NumberOfRemoteCalls++;
                    return m_backend.List();
                }
                catch (Exception ex)
                {
                    m_statistics.LogError(ex.Message);

                    retries--;
                    if (retries > 0 && m_retrydelay.Ticks > 0)
                        System.Threading.Thread.Sleep(m_retrydelay);
                }
            } while (retries > 0);

            throw new Exception("Failed to retrieve file listing");
        }

        public void Put(string remotename, string filename)
        {
            int retries = m_retries;
            bool success = false;

            do
            {
                try
                {
                    m_statistics.NumberOfRemoteCalls++;
                    m_backend.Put(remotename, filename);
                    success = true;
                }
                catch (Exception ex)
                {
                    m_statistics.LogError(ex.Message);

                    retries--;
                    if (retries > 0 && m_retrydelay.Ticks > 0)
                        System.Threading.Thread.Sleep(m_retrydelay);
                }
            } while (!success && retries > 0);

            if (!success)
                throw new Exception("Failed to upload file");

            m_statistics.NumberOfBytesUploaded += new System.IO.FileInfo(filename).Length;

        }

        public void Get(string remotename, string filename)
        {
            int retries = m_retries;
            bool success = false;

            do
            {
                try
                {
                    m_statistics.NumberOfRemoteCalls++;
                    m_backend.Get(remotename, filename);
                    success = true;
                }
                catch (Exception ex)
                {
                    m_statistics.LogError(ex.Message);

                    retries--;
                    if (retries > 0 && m_retrydelay.Ticks > 0)
                        System.Threading.Thread.Sleep(m_retrydelay);
                }
            } while (!success && retries > 0);

            if (!success)
                throw new Exception("Failed to download file");

            m_statistics.NumberOfBytesDownloaded += new System.IO.FileInfo(filename).Length;
        }

        public void Delete(string remotename)
        {

            int retries = m_retries;
            bool success = false;

            do
            {
                try
                {
                    m_statistics.NumberOfRemoteCalls++;
                    m_backend.Delete(remotename);
                    success = true;
                }
                catch (Exception ex)
                {
                    m_statistics.LogError(ex.Message);

                    retries--;
                    if (retries > 0 && m_retrydelay.Ticks > 0)
                        System.Threading.Thread.Sleep(m_retrydelay);
                }
            } while (!success && retries > 0);

            if (!success)
                throw new Exception("Failed to delete file");
        }

        #endregion
    }
}
