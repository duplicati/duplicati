using Duplicati.Service;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.WindowsService
{
    public class ServiceControl : System.ServiceProcess.ServiceBase
    {
        private const string LOG_SOURCE = "ENATrustBackup";
        private const string LOG_NAME = "Application";
        public const string SERVICE_NAME = "Duplicati";
        public const string DISPLAY_NAME = "ENA TrustBackup service";
        public const string DESCRIPTION = "Runs ENA TrustBackup as a service";

        private readonly System.Diagnostics.EventLog m_eventLog;

        private readonly object m_lock = new object();
        private Runner m_runner = null;
        private readonly string[] m_cmdargs;
        private readonly bool m_verbose_messages;

        public ServiceControl(string[] args)
        {
            this.ServiceName = SERVICE_NAME;

            m_eventLog = new System.Diagnostics.EventLog();
            if (!System.Diagnostics.EventLog.SourceExists(LOG_SOURCE))
                System.Diagnostics.EventLog.CreateEventSource(LOG_SOURCE, LOG_NAME);

            m_eventLog.Source = LOG_SOURCE;
            m_eventLog.Log = LOG_NAME;
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
            var startargs = (args ?? new string[0]).Union(m_cmdargs ?? new string[0]).ToArray();

            if (m_verbose_messages)
                m_eventLog.WriteEntry("Starting...");
            lock(m_lock)
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
