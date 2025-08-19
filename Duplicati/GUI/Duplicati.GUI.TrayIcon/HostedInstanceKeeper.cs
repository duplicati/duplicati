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
using System.Linq;
using System.Threading;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.GUI.TrayIcon
{
    /// <summary>
    /// We keep the hosted instance here to allow the TrayIcon executable to load without requiring the Server.exe
    /// </summary>
    internal class HostedInstanceKeeper : IDisposable
    {
        private readonly Thread m_runner;
        private Exception m_runnerException = null;
        public Action InstanceShutdown;
        private int _InstanceShutdownInvoked = 0;

        public readonly IApplicationSettings applicationSettings;

        public HostedInstanceKeeper(IApplicationSettings applicationSettings, string[] args)
        {
            this.applicationSettings = applicationSettings;
            m_runner = new System.Threading.Thread(_ =>
            {
                try
                {
                    //When running the hosted instance we do not really care what port we are using,
                    // so we just throw a few out there and try them
                    if (args == null || !args.Any(x => x.Trim().StartsWith("--" + Server.WebServerLoader.OPTION_PORT + "=", StringComparison.OrdinalIgnoreCase)))
                        args = (args ?? new string[0]).Union(new string[] { "--" + Server.WebServerLoader.OPTION_PORT + "=8200,8300,8400,8500,8600,8700,8800,8900,8989" }).ToArray();

                    Server.Program.Main(applicationSettings, args);
                }
                catch (Exception ex)
                {
                    m_runnerException = ex;
                    Server.Program.ServerStartedEvent?.Set();
                    applicationSettings.SignalApplicationExit();
                }
                finally
                {
                    try
                    {
                        if (Interlocked.CompareExchange(ref _InstanceShutdownInvoked, 1, 0) == 0)
                            InstanceShutdown?.Invoke();
                    }
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

            if (!Server.Program.ServerStartedEvent.WaitOne(TimeSpan.FromSeconds(100), true))
            {
                if (m_runnerException != null)
                    throw new Library.Interface.UserInformationException("Server crashed on startup", "HostedStartupErrorCrash", m_runnerException);
                else
                    throw new Library.Interface.UserInformationException("Hosted server startup timed out", "HostedStartupError");
            }

            if (m_runnerException != null)
                throw new Library.Interface.UserInformationException("Server crashed on startup", "HostedStartupErrorCrash", m_runnerException);
        }

        public void Dispose()
        {
            try
            {
                applicationSettings.SignalApplicationExit();
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
