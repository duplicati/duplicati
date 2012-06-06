using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.GUI.TrayIcon
{    
    /// <summary>
    /// We keep the hosted instance here to allow the TrayIcon executable to load without requiring the Server.exe
    /// </summary>
    internal class HostedInstanceKeeper : IDisposable
    {
        private System.Threading.Thread m_runner;
        private System.Exception m_runnerException = null;

        public HostedInstanceKeeper(string[] args)
        {
            m_runner = new System.Threading.Thread(ThreadRunner);
            m_runner.Start(args);

            if (!Duplicati.Server.Program.ServerStartedEvent.WaitOne(TimeSpan.FromSeconds(100), true))
            {
                if (m_runnerException != null)
                    throw m_runnerException;
                else
                    throw new Exception("Hosted server startup timed out");
            }
        }

        private void ThreadRunner(object a)
        {
            try
            {
                Duplicati.Server.Program.Main((string[])a);
            } catch (Exception ex) {
                m_runnerException = ex;
            }
            
        }

        public void Dispose()
        {
            Duplicati.Server.Program.ApplicationExitEvent.Set();
            if (!m_runner.Join(TimeSpan.FromSeconds(10)))
            {
                m_runner.Abort();
                m_runner.Join(TimeSpan.FromSeconds(10));
            }
        }
    }
}
