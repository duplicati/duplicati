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
        public event Action InstanceShutdown;

        public HostedInstanceKeeper(string[] args)
        {
            m_runner = new System.Threading.Thread((dummy_arg) => {
                try
                {
                    //When running the hosted instance we do not really care what port we are using,
                    // so we just throw a few out there and try them
                    if (args == null || !args.Any(x => x.Trim().StartsWith("--" + Duplicati.Server.WebServer.Server.OPTION_PORT + "=", StringComparison.InvariantCultureIgnoreCase)))
                        args = (args ?? new string[0]).Union(new string[] { "--" + Duplicati.Server.WebServer.Server.OPTION_PORT + "=8200,8300,8400,8500,8600,8700,8800,8900,8989" }).ToArray();
                        
                    Duplicati.Server.Program.Main(args);
                } catch (Exception ex) {
                    m_runnerException = ex;
                    Duplicati.Server.Program.ServerStartedEvent.Set();
                } finally {
                    if (InstanceShutdown != null)
                        try { InstanceShutdown(); }
                        catch (Exception shutex)
                        {
                            if (m_runnerException != null)
                                m_runnerException = new AggregateException(m_runnerException, shutex);
                            else
                                m_runnerException = shutex;
                        }
                }

            });
            
            m_runner.Start();

            if (!Duplicati.Server.Program.ServerStartedEvent.WaitOne(TimeSpan.FromSeconds(100), true))
            {
                if (m_runnerException != null)
                    throw m_runnerException;
                else
                    throw new Duplicati.Library.Interface.UserInformationException("Hosted server startup timed out");
            }

            if (m_runnerException != null)
                throw m_runnerException;
        }

        public void Dispose()
        {
            try
            {
                Duplicati.Server.Program.ApplicationExitEvent.Set();
                if (!m_runner.Join(TimeSpan.FromSeconds(10)))
                {
                    m_runner.Abort();
                    m_runner.Join(TimeSpan.FromSeconds(10));
                }
            }
            catch
            {
            }
        }
    }
}
