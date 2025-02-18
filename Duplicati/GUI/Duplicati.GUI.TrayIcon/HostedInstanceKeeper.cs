// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

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
        private readonly System.Threading.Thread m_runner;
        private System.Exception m_runnerException = null;
        public event Action InstanceShutdown;

        public HostedInstanceKeeper(string[] args)
        {
            m_runner = new System.Threading.Thread((dummy_arg) =>
            {
                try
                {
                    //When running the hosted instance we do not really care what port we are using,
                    // so we just throw a few out there and try them
                    if (args == null || !args.Any(x => x.Trim().StartsWith("--" + Duplicati.Server.WebServerLoader.OPTION_PORT + "=", StringComparison.OrdinalIgnoreCase)))
                        args = (args ?? new string[0]).Union(new string[] { "--" + Duplicati.Server.WebServerLoader.OPTION_PORT + "=8200,8300,8400,8500,8600,8700,8800,8900,8989" }).ToArray();

                    Duplicati.Server.Program.Main(args);
                }
                catch (Exception ex)
                {
                    m_runnerException = ex;
                    Duplicati.Server.Program.ServerStartedEvent?.Set();
                    Duplicati.Server.Program.ApplicationExitEvent?.Set();
                }
                finally
                {
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
                    throw new Duplicati.Library.Interface.UserInformationException("Server crashed on startup", "HostedStartupErrorCrash", m_runnerException);
                else
                    throw new Duplicati.Library.Interface.UserInformationException("Hosted server startup timed out", "HostedStartupError");
            }

            if (m_runnerException != null)
                throw new Duplicati.Library.Interface.UserInformationException("Server crashed on startup", "HostedStartupErrorCrash", m_runnerException);
        }

        public void Dispose()
        {
            try
            {
                Duplicati.Server.Program.ApplicationExitEvent.Set();
                if (!m_runner.Join(TimeSpan.FromSeconds(10)))
                {
                    m_runner.Interrupt();
                    m_runner.Join(TimeSpan.FromSeconds(10));
                }
            }
            catch
            {
            }
        }
    }
}
