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
using Duplicati.Library.AutoUpdater;
using Duplicati.Service;
using Microsoft.Win32;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

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

        /// <summary>
        /// Registry key holding the one-shot init password written by the
        /// MSI installer. The value is readable only by SYSTEM and
        /// Administrators (the MSI hardens the ACL via
        /// MsiLockPermissionsEx). On first start of the service we promote
        /// it to a process-scoped environment variable for the child
        /// Duplicati.Server.exe (which the option-loader maps to
        /// --webservice-password).
        /// After the server has stayed up for a few seconds we delete the
        /// value so the password no longer lives on disk.
        /// </summary>
        internal const string INIT_REGISTRY_KEY = @"SOFTWARE\DuplicatiTeam\Duplicati\Service";
        /// <summary>
        /// The name of the registry value holding the init password.
        /// </summary>
        internal const string INIT_REGISTRY_VALUE = "InitPassword";

        /// <summary>
        /// The name of the registry value holding the TLS certs install/uninstall instruction.
        /// </summary>
        internal const string TLS_CERTS_REGISTRY_VALUE = "TlsCertsOption";

        /// <summary>
        /// Separate registry key for the BootstrapApplied sentinel. This key
        /// retains its default inheritable ACL (Authenticated Users have
        /// Read), unlike INIT_REGISTRY_KEY which is locked down to admins
        /// because it briefly holds the password. The sentinel only stores
        /// "1" with no secret content, so read access for non-admin users
        /// is fine - and is required because the MSI's CheckBootstrapResult
        /// CA polls this value from the un-elevated UI process.
        /// </summary>
        private const string SERVICE_STATE_REGISTRY_KEY = @"SOFTWARE\DuplicatiTeam\Duplicati\ServiceState";

        /// <summary>
        /// Sentinel value the installer (CheckBootstrapResult) polls for to
        /// confirm that the service actually applied the password. We write
        /// "1" once we have successfully deleted InitPassword. The installer
        /// removes any pre-existing copy at the start of WriteServicePassword
        /// so this is always a fresh signal.
        /// </summary>
        private const string BOOTSTRAP_APPLIED_VALUE = "BootstrapApplied";

        /// <summary>
        /// Environment variable name consumed by Duplicati.Server.exe's
        /// option loader (ApplyEnvironmentVariables in Server/Program.cs).
        /// The prefix matches AutoUpdateSettings.AppName.ToUpperInvariant()
        /// and the suffix is the option name with '-' replaced by '_' and
        /// uppercased. Encoding "webservice-password" yields
        /// DUPLICATI__WEBSERVICE_PASSWORD.
        /// </summary>
        private const string ENV_WEBSERVICE_PASSWORD = "DUPLICATI__WEBSERVICE_PASSWORD";

        /// <summary>
        /// How long the server must stay alive after start before we are
        /// confident the bootstrap actually consumed the password and we
        /// can safely remove the copy from the registry.
        /// </summary>
        private static readonly TimeSpan INIT_PASSWORD_RETENTION = TimeSpan.FromSeconds(5);

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

            // Pick up the one-shot init password planted by the MSI installer
            // (only relevant for the Server service, not the Agent service).
            // This sets process-level environment variables that the Runner's
            // child Process.Start inherits via UseShellExecute=false.
            var initPasswordPickedUp = false;
            if (m_executable == PackageHelper.NamedExecutable.Server)
            {
                ReadAndExecuteTlsCertsCommandFromRegistry();
                initPasswordPickedUp = ReadInitPasswordFromRegistry();
            }

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

                            // If we just consumed an init password, give the
                            // server a few seconds to actually accept it,
                            // then remove the on-disk copy. We also clear
                            // the env vars so they don't accidentally apply
                            // to a future child process started in the same
                            // service host.
                            if (initPasswordPickedUp)
                                ScheduleInitPasswordCleanup();
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

        private void ReadAndExecuteTlsCertsCommandFromRegistry()
        {
            try
            {
                using (var view = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = view.OpenSubKey(INIT_REGISTRY_KEY, writable: true))
                {
                    if (key == null)
                        return;

                    var raw = key.GetValue(TLS_CERTS_REGISTRY_VALUE);
                    if (raw == null)
                        return;

                    var value = raw as string;
                    if (string.IsNullOrEmpty(value))
                        return;

                    var installDir = UpdaterManager.INSTALLATIONDIR;
                    var exeName = PackageHelper.GetExecutableName(PackageHelper.NamedExecutable.ConfigureTool);
                    string configureToolExe = System.IO.Path.Combine(installDir, exeName);
                    if (System.IO.File.Exists(configureToolExe))
                    {
                        var startInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = configureToolExe,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };

                        if (string.Equals(value, "install", StringComparison.OrdinalIgnoreCase))
                        {
                            startInfo.Arguments = "https generate --auto-create-database";
                            m_eventLog.WriteEntry("Running ConfigureTool to generate TLS certificates.", System.Diagnostics.EventLogEntryType.Information);
                        }
                        else if (string.Equals(value, "uninstall", StringComparison.OrdinalIgnoreCase))
                        {
                            startInfo.Arguments = "https remove";
                            m_eventLog.WriteEntry("Running ConfigureTool to remove TLS certificates.", System.Diagnostics.EventLogEntryType.Information);
                        }
                        else
                        {
                            return;
                        }

                        using (var process = new System.Diagnostics.Process { StartInfo = startInfo })
                        {
                            var outputBuilder = new System.Text.StringBuilder();
                            var errorBuilder = new System.Text.StringBuilder();

                            if (m_verbose_messages)
                            {
                                process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
                                process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };
                            }

                            if (process.Start())
                            {
                                if (m_verbose_messages)
                                {
                                    process.BeginOutputReadLine();
                                    process.BeginErrorReadLine();
                                }

                                if (process.WaitForExit(TimeSpan.FromSeconds(20)))
                                {
                                    process.WaitForExit(); // Ensure async streams are fully read
                                    if (process.ExitCode != 0)
                                        m_eventLog.WriteEntry($"ConfigureTool failed with exit code {process.ExitCode}", System.Diagnostics.EventLogEntryType.Warning);

                                    if (m_verbose_messages)
                                        m_eventLog.WriteEntry($"ConfigureTool output was:.\n\nSTDOUT:\n{outputBuilder}\n\nSTDERR:\n{errorBuilder}", System.Diagnostics.EventLogEntryType.Information);
                                }
                                else
                                {
                                    m_eventLog.WriteEntry("ConfigureTool timed out after 20 seconds.", System.Diagnostics.EventLogEntryType.Warning);
                                    try { process.Kill(); } catch { }
                                }
                            }
                        }
                    }
                    else
                    {
                        m_eventLog.WriteEntry("ConfigureTool not found, cannot configure TLS certificates.", System.Diagnostics.EventLogEntryType.Warning);
                    }

                    key.DeleteValue(TLS_CERTS_REGISTRY_VALUE, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                m_eventLog.WriteEntry("Failed to process TLS certs registry command: " + ex.Message, System.Diagnostics.EventLogEntryType.Warning);
            }
        }

        /// <summary>
        /// Reads the InitPassword value written by the MSI installer's
        /// WriteServicePassword custom action. If found, exposes it to 
        /// the child server process via environment variables.
        /// </summary>
        /// <returns>true if a value was found and the env vars were set; false otherwise.</returns>
        private bool ReadInitPasswordFromRegistry()
        {
            try
            {
                using (var view = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = view.OpenSubKey(INIT_REGISTRY_KEY, writable: false))
                {
                    if (key == null)
                    {
                        if (m_verbose_messages)
                            m_eventLog.WriteEntry(
                                "ConsumeInitPasswordFromRegistry: HKLM\\" + INIT_REGISTRY_KEY + " does not exist; "
                                + "no init password to apply.",
                                System.Diagnostics.EventLogEntryType.Information);
                        return false;
                    }

                    var raw = key.GetValue(INIT_REGISTRY_VALUE);
                    if (raw == null)
                    {
                        if (m_verbose_messages)
                            m_eventLog.WriteEntry(
                                "ConsumeInitPasswordFromRegistry: " + INIT_REGISTRY_VALUE + " value is missing under HKLM\\"
                                + INIT_REGISTRY_KEY + "; no init password to apply.",
                                System.Diagnostics.EventLogEntryType.Information);
                        return false;
                    }

                    var value = raw as string;
                    if (string.IsNullOrEmpty(value))
                    {
                        if (m_verbose_messages)
                            m_eventLog.WriteEntry(
                                "ConsumeInitPasswordFromRegistry: " + INIT_REGISTRY_VALUE + " is present but empty/non-string "
                                + "(type=" + raw.GetType().FullName + "); not applying.",
                                System.Diagnostics.EventLogEntryType.Warning);
                        return false;
                    }

                    Environment.SetEnvironmentVariable(ENV_WEBSERVICE_PASSWORD, value);

                    if (m_verbose_messages)
                        m_eventLog.WriteEntry(
                            "ConsumeInitPasswordFromRegistry: picked up init password"
                            + " and set " + ENV_WEBSERVICE_PASSWORD
                            + " for the child server process. Cleanup will fire " + INIT_PASSWORD_RETENTION.TotalSeconds
                            + "s after the runner reports started.",
                            System.Diagnostics.EventLogEntryType.Information);

                    return true;
                }
            }
            catch (Exception ex)
            {
                // Don't block service start on registry read failure.
                m_eventLog.WriteEntry(
                    "ConsumeInitPasswordFromRegistry: failed to read InitPassword: " + ex,
                    System.Diagnostics.EventLogEntryType.Warning);
                return false;
            }
        }

        /// <summary>
        /// Background timer that, after INIT_PASSWORD_RETENTION elapses,
        /// removes the InitPassword value from the registry and clears the
        /// process-scoped environment variables.
        ///
        /// We get here only after Runner.PingProcess succeeded, i.e. the
        /// child Duplicati.Server.exe responded on stdin/stdout, which in
        /// turn means it completed its option-loader pass (consuming the
        /// env vars) and entered its event loop. At that point the
        /// password is already persisted in the server database (via the
        /// --database-bootstrap-mode=Init flow) and the registry copy is
        /// no longer needed.
        ///
        /// The retention window covers the gap between "server has started
        /// listening" and "the bootstrap commit has flushed", and gives a
        /// buffer in case of an immediate crash; a few seconds of extra
        /// retention is acceptable since the registry value is already
        /// ACL-locked to SYSTEM and Administrators.
        /// </summary>
        private void ScheduleInitPasswordCleanup()
        {
            if (m_verbose_messages)
                m_eventLog.WriteEntry(
                    "ScheduleInitPasswordCleanup: scheduling cleanup timer to fire in "
                    + INIT_PASSWORD_RETENTION.TotalSeconds + "s.",
                    System.Diagnostics.EventLogEntryType.Information);

            // Use a one-shot Timer; fire-and-forget. The timer is captured
            // by the closure and self-disposed in the callback.
            Timer timer = null;
            timer = new Timer(_ =>
            {
                if (m_verbose_messages)
                    m_eventLog.WriteEntry(
                       "InitPasswordCleanup: timer fired; clearing InitPassword and writing BootstrapApplied sentinel.",
                       System.Diagnostics.EventLogEntryType.Information);

                try
                {
                    using (var view = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    {
                        // Remove the password first, then write the sentinel
                        // that confirms successful application. Doing it in
                        // this order guarantees that an observer either sees
                        // no signal (still racing) or sees BootstrapApplied
                        // with InitPassword already gone - never the inverse.
                        using (var initKey = view.OpenSubKey(INIT_REGISTRY_KEY, writable: true))
                        {
                            if (initKey == null)
                            {
                                if (m_verbose_messages)
                                    m_eventLog.WriteEntry(
                                       "InitPasswordCleanup: HKLM\\" + INIT_REGISTRY_KEY + " could not be opened for write; "
                                       + "skipping InitPassword deletion.",
                                       System.Diagnostics.EventLogEntryType.Warning);
                            }
                            else
                            {
                                initKey.DeleteValue(INIT_REGISTRY_VALUE, throwOnMissingValue: false);
                                if (m_verbose_messages)
                                    m_eventLog.WriteEntry(
                                       "InitPasswordCleanup: deleted " + INIT_REGISTRY_VALUE + " from HKLM\\" + INIT_REGISTRY_KEY + ".",
                                       System.Diagnostics.EventLogEntryType.Information);
                            }
                        }

                        // The installer creates SERVICE_STATE_REGISTRY_KEY
                        // with default ACLs so this CreateSubKey is idempotent
                        // (it opens the existing key); if for some reason it
                        // doesn't exist (manual cleanup, partial install),
                        // creating it here is still safe and keeps the same
                        // default-ACL property because we are running as
                        // LocalSystem and the parent SOFTWARE key's
                        // inheritable ACEs apply.
                        using (var stateKey = view.CreateSubKey(SERVICE_STATE_REGISTRY_KEY))
                        {
                            if (stateKey == null)
                            {
                                if (m_verbose_messages)
                                    m_eventLog.WriteEntry(
                                       "InitPasswordCleanup: failed to create/open HKLM\\" + SERVICE_STATE_REGISTRY_KEY
                                       + "; cannot write " + BOOTSTRAP_APPLIED_VALUE + " sentinel.",
                                       System.Diagnostics.EventLogEntryType.Warning);
                            }
                            else
                            {
                                stateKey.SetValue(BOOTSTRAP_APPLIED_VALUE, "1", RegistryValueKind.String);
                                if (m_verbose_messages)
                                    m_eventLog.WriteEntry(
                                       "InitPasswordCleanup: wrote " + BOOTSTRAP_APPLIED_VALUE + "=1 to HKLM\\"
                                       + SERVICE_STATE_REGISTRY_KEY + ".",
                                       System.Diagnostics.EventLogEntryType.Information);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    m_eventLog.WriteEntry(
                        "InitPasswordCleanup: failed to clear InitPassword / write BootstrapApplied: " + ex,
                        System.Diagnostics.EventLogEntryType.Warning);
                }
                finally
                {
                    // Clear the env vars from this process so they do not
                    // accidentally apply to a future restart of the server
                    // child (which would re-trigger database-bootstrap-mode
                    // if it ever ran a second time on a stale value).
                    Environment.SetEnvironmentVariable(ENV_WEBSERVICE_PASSWORD, null);
                    timer?.Dispose();
                }
            }, null, INIT_PASSWORD_RETENTION, Timeout.InfiniteTimeSpan);
        }
    }
}
