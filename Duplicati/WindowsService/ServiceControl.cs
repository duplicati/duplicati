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
using Duplicati.Library.AutoUpdater;
using Duplicati.Service;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Duplicati.WindowsService
{
    [SupportedOSPlatform("windows")]
    public class ServiceControl : System.ServiceProcess.ServiceBase
    {
        private const string LOG_SOURCE = "Duplicati Service";
        private const string LOG_NAME = "Duplicati 2";
        private const string SERVER_LOG_NAME = "Duplicati 2:Duplicati Server";
        private const string AGENT_LOG_NAME = "Duplicati 2:Duplicati Agent";
        public const string SERVICE_NAME = "Duplicati";
        public const string DISPLAY_NAME = "Duplicati service";
        public const string SERVICE_DESCRIPTION = "Duplicati running as a Windows Service";
        public const string SERVICE_NAME_AGENT = "Duplicati.Agent";
        public const string DISPLAY_NAME_AGENT = "Duplicati Agent service";
        public const string SERVICE_DESCRIPTION_AGENT = "The Duplicati Agent service";

        private readonly System.Diagnostics.EventLog m_eventLog;

        private readonly object m_lock = new object();
        private Runner m_runner = null;
        private readonly string[] m_cmdargs;
        private readonly bool m_verbose_messages;
        private readonly PackageHelper.NamedExecutable m_executable;

        public ServiceControl(string[] args, PackageHelper.NamedExecutable executable)
        {
            m_executable = executable;
            this.ServiceName = executable == PackageHelper.NamedExecutable.Agent
                ? SERVICE_NAME_AGENT
                : SERVICE_NAME;

            if (!System.Diagnostics.EventLog.SourceExists(LOG_SOURCE))
                System.Diagnostics.EventLog.CreateEventSource(LOG_SOURCE, LOG_NAME);
            m_eventLog = new System.Diagnostics.EventLog
            {
                Source = LOG_SOURCE,
                Log = LOG_NAME
            };
            m_verbose_messages = args != null && args.Any(x => string.Equals("--debug-service", x, StringComparison.OrdinalIgnoreCase));
            m_cmdargs = (args ?? new string[0]).Where(x => !string.Equals("--debug-service", x, StringComparison.OrdinalIgnoreCase)).ToArray();

        }

        protected override void OnStart(string[] args)
        {
            DoStart(args);
        }

        protected override void OnStop()
        {
            DoStop();
        }

        protected override void OnShutdown()
        {
            DoStop();
        }

        private void DoStart(string[] args)
        {
            var startargs = (args ?? [])
                .Union(m_cmdargs ?? [])
                .ToArray();

            if (!startargs.Any(x => x.StartsWith("--windows-eventlog=", StringComparison.OrdinalIgnoreCase)))
                startargs = startargs.Union(new string[] { "--windows-eventlog=" + (
                    m_executable == PackageHelper.NamedExecutable.Agent
                    ? AGENT_LOG_NAME
                    : SERVER_LOG_NAME)
                }).ToArray();

            if (m_verbose_messages)
                m_eventLog.WriteEntry("Starting...");
            lock (m_lock)
                if (m_runner == null)
                {
                    if (m_verbose_messages)
                        m_eventLog.WriteEntry("Set start time to 30 seconds...");
                    var sv = new ServiceStatus()
                    {
                        dwCurrentState = ServiceState.SERVICE_START_PENDING,
                        dwWaitHint = (uint)TimeSpan.FromSeconds(30).TotalMilliseconds
                    };
                    SetServiceStatus(this.ServiceHandle, ref sv);

                    if (m_verbose_messages)
                        m_eventLog.WriteEntry("Starting runner...");

                    m_runner = new Runner(
                        m_executable,
                        startargs,
                        () =>
                        {
                            if (m_verbose_messages)
                                m_eventLog.WriteEntry("Started!");

                            var sv2 = new ServiceStatus()
                            {
                                dwCurrentState = ServiceState.SERVICE_RUNNING
                            };
                            SetServiceStatus(this.ServiceHandle, ref sv2);
                        },
                        () =>
                        {
                            if (m_verbose_messages)
                                m_eventLog.WriteEntry("Stopped!");
                            var sv2 = new ServiceStatus()
                            {
                                dwCurrentState = ServiceState.SERVICE_STOPPED
                            };
                            SetServiceStatus(this.ServiceHandle, ref sv2);

                            base.Stop();
                        },
                        (msg, important) =>
                        {
                            if (important || m_verbose_messages)
                                m_eventLog.WriteEntry(msg);
                        }
                    );
                }
        }

        private void DoStop()
        {
            if (m_verbose_messages)
                m_eventLog.WriteEntry("Stopping...");
            lock (m_lock)
                if (m_runner != null)
                {
                    if (m_verbose_messages)
                        m_eventLog.WriteEntry("Calling stop...");
                    var sv = new ServiceStatus()
                    {
                        dwCurrentState = ServiceState.SERVICE_STOP_PENDING,
                        dwWaitHint = (uint)TimeSpan.FromSeconds(5).TotalMilliseconds
                    };
                    SetServiceStatus(this.ServiceHandle, ref sv);

                    if (m_verbose_messages)
                        m_eventLog.WriteEntry("Soft stop invoked...");
                    m_runner.Stop(false);
                }

        }

        private enum ServiceState : uint
        {
            SERVICE_STOPPED = 0x00000001,
            SERVICE_START_PENDING = 0x00000002,
            SERVICE_STOP_PENDING = 0x00000003,
            SERVICE_RUNNING = 0x00000004,
            SERVICE_CONTINUE_PENDING = 0x00000005,
            SERVICE_PAUSE_PENDING = 0x00000006,
            SERVICE_PAUSED = 0x00000007,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatus
        {
            public readonly uint dwServiceType;
            public ServiceState dwCurrentState;
            public readonly uint dwControlsAccepted;
            public readonly uint dwWin32ExitCode;
            public readonly uint dwServiceSpecificExitCode;
            public readonly uint dwCheckPoint;
            public uint dwWaitHint;
        };

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceStatus(IntPtr handle, ref ServiceStatus serviceStatus);
    }
}
