// Copyright (C) 2026, The Duplicati Team
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
        /// <summary>
        /// The log tag for messages from this class
        /// </summary>
        private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<HostedInstanceKeeper>();

        /// <summary>
        /// The time the hosted server is given to stop after it is asked to
        /// </summary>
        private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(20);

        private readonly Thread m_runner;
        private readonly TimeSpan m_stopTimeout;
        private Exception m_runnerException = null;
        public Action InstanceShutdown;
        private int _InstanceShutdownInvoked = 0;

        public readonly IApplicationSettings applicationSettings;

        public HostedInstanceKeeper(IApplicationSettings applicationSettings, string[] args)
            : this(applicationSettings, () => RunServer(applicationSettings, args), Server.Program.ServerStartedEvent, TimeSpan.FromSeconds(100), DefaultStopTimeout)
        {
        }

        /// <summary>
        /// Starts the hosted server and waits for it to start
        /// </summary>
        /// <param name="applicationSettings">The settings of the application, which are used to ask the server to stop</param>
        /// <param name="run">Runs the server until it is asked to stop</param>
        /// <param name="startedEvent">The event that is set when the server has started</param>
        /// <param name="startTimeout">The time the server is given to start</param>
        /// <param name="stopTimeout">The time the server is given to stop when this instance is disposed</param>
        internal HostedInstanceKeeper(IApplicationSettings applicationSettings, Action run, ManualResetEvent startedEvent, TimeSpan startTimeout, TimeSpan stopTimeout)
        {
            this.applicationSettings = applicationSettings;
            m_stopTimeout = stopTimeout;
            m_runner = new Thread(_ =>
            {
                try
                {
                    run();
                }
                catch (Exception ex)
                {
                    m_runnerException = ex;
                    startedEvent?.Set();
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

            })
            {
                // The server is asked to stop and given time to do so when the tray icon exits. If it is still
                // stuck after that, it must not keep the process running with no tray icon (#4533).
                IsBackground = true
            };

            m_runner.Start();

            if (!startedEvent.WaitOne(startTimeout, true))
            {
                if (m_runnerException != null)
                    throw new Library.Interface.UserInformationException("Server crashed on startup", "HostedStartupErrorCrash", m_runnerException);
                else
                    throw new Library.Interface.UserInformationException("Hosted server startup timed out", "HostedStartupError");
            }

            if (m_runnerException != null)
                throw new Library.Interface.UserInformationException("Server crashed on startup", "HostedStartupErrorCrash", m_runnerException);
        }

        /// <summary>
        /// Runs the hosted server until it is asked to stop
        /// </summary>
        /// <param name="applicationSettings">The settings of the application</param>
        /// <param name="args">The commandline arguments</param>
        private static void RunServer(IApplicationSettings applicationSettings, string[] args)
        {
            //When running the hosted instance we do not really care what port we are using,
            // so we just throw a few out there and try them
            if (args == null || !args.Any(x => x.Trim().StartsWith("--" + Server.WebServerLoader.OPTION_PORT + "=", StringComparison.OrdinalIgnoreCase)))
                args = (args ?? new string[0]).Union(new string[] { "--" + Server.WebServerLoader.OPTION_PORT + "=8200,8300,8400,8500,8600,8700,8800,8900,8989" }).ToArray();

            Server.Program.Main(applicationSettings, args);
        }

        public void Dispose()
        {
            try
            {
                applicationSettings.SignalApplicationExit();

                // The server is not interrupted: an interrupt breaks whichever wait of its shutdown it is in,
                // and the steps after it run on a half-stopped server
                if (!m_runner.Join(m_stopTimeout))
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "HostedServerStopTimeout", null, "The hosted server did not stop within {0} seconds, exiting without waiting for it", (int)m_stopTimeout.TotalSeconds);
            }
            catch
            {
            }
        }
    }
}
