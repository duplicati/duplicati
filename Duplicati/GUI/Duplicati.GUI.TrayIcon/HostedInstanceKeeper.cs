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

        public HostedInstanceKeeper(string[] args)
        {
            m_runner = new System.Threading.Thread(ThreadRunner);
            m_runner.Start(args);

            Duplicati.Server.Program.ServerStartedEvent.WaitOne(TimeSpan.FromSeconds(5), true);
        }

        private void ThreadRunner(object a)
        {
            Duplicati.Server.Program.Main((string[])a);
        }

        public void Dispose()
        {
            m_runner.Abort();
            m_runner.Join(TimeSpan.FromSeconds(10));
        }
    }
}
