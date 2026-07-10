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
using Duplicati.Library.Common.IO;
using Duplicati.Service;
using Microsoft.Win32;
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
        /// The name of the registry value holding the reset password.
        /// </summary>
        internal const string RESET_REGISTRY_VALUE = "ResetPassword";

        /// <summary>
        /// The name of the registry value holding the TLS certs install/uninstall instruction.
        /// </summary>
        internal const string TLS_CERTS_REGISTRY_VALUE = "TlsCertsOption";

        /// <summary>
        /// The name of the registry value holding the secure-datafolder instruction.
        /// </summary>
        internal const string SECURE_DATAFOLDER_REGISTRY_VALUE = "SecureDataFolder";

        /// <summary>
        /// Separate registry key for the BootstrapApplied sentinel. This key
        /// retains its default inheritable ACL (Authenticated Users have
        /// Read), unlike INIT_REGISTRY_KEY which is locked down to admins
        /// because it briefly holds the password. The sentinel only stores
        /// "1" with no secret content, so read access for non-admin users
        /// is fine - and is required because the MSI's CheckBootstrapResult
        /// CA polls this value from the un-elevated UI process.
        /// </summary>
        private const string SERVICE_STATE_REGISTRY_KEY = @"SOFTWARE\DuplicatiTeam\Duplicati\InstallState";

        /// <summary>
        /// Sentinel value the installer (CheckBootstrapResult) polls for to
        /// confirm that the service actually applied the password. We write
        /// "1" once we have successfully deleted InitPassword. The installer
        /// removes any pre-existing copy at the start of WriteServicePassword
        /// so this is always a fresh signal.
        /// </summary>
        private const string BOOTSTRAP_APPLIED_VALUE = "BootstrapApplied";

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

            // Pick up the one-shot init password planted by the MSI installer,
            // the certificate control command,
            // or the reset password planted by the reset-password command,
            // (only relevant for the Server service, not the Agent service).
            string resetPassword = null;
            if (m_executable == PackageHelper.NamedExecutable.Server)
            {
                ReadAndExecuteTlsCertsCommandFromRegistry();
                ReadAndExecuteSecureDataFolderCommandFromRegistry();
                ReadAndExecuteInitPasswordFromRegistry();
                resetPassword = ConsumeResetPasswordFromRegistry();
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
                        true,
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
                        },
                        (startInfo) =>
                        {
                            // If we have a reset password, pass it to the server once
                            if (!string.IsNullOrEmpty(resetPassword) && startInfo != null)
                            {
                                startInfo.EnvironmentVariables["DUPLICATI__WEBSERVICE_PASSWORD"] = resetPassword;
                                resetPassword = null;
                            }
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
                    m_runner.Stop();
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
                using (var key = ServiceRegistryKey.OpenIfTrusted(writable: true, out var reason))
                {
                    if (key == null)
                    {
                        if (reason != null && reason != "key-missing")
                            m_eventLog.WriteEntry(
                                $"Refusing to process TLS certs registry command: {reason}",
                                System.Diagnostics.EventLogEntryType.Warning);
                        return;
                    }

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
                            m_eventLog.WriteEntry($"Unknown TLS certs command: {value}", System.Diagnostics.EventLogEntryType.Warning);
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

                                var timeout = TimeSpan.FromSeconds(60);
                                if (process.WaitForExit(timeout))
                                {
                                    process.WaitForExit(); // Ensure async streams are fully read
                                    if (process.ExitCode != 0)
                                        m_eventLog.WriteEntry($"ConfigureTool failed with exit code {process.ExitCode}", System.Diagnostics.EventLogEntryType.Warning);

                                    if (m_verbose_messages)
                                        m_eventLog.WriteEntry($"ConfigureTool output was:.\n\nSTDOUT:\n{outputBuilder}\n\nSTDERR:\n{errorBuilder}", System.Diagnostics.EventLogEntryType.Information);
                                }
                                else
                                {
                                    m_eventLog.WriteEntry($"ConfigureTool timed out after {timeout.TotalSeconds} seconds.", System.Diagnostics.EventLogEntryType.Warning);
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
        /// Reads the secure-datafolder instruction from the registry if present, and directly
        /// restricts the permissions on the data folder to the current user (SYSTEM),
        /// Administrators only. The registry value is always deleted after being read,
        /// regardless of success.
        /// </summary>
        private void ReadAndExecuteSecureDataFolderCommandFromRegistry()
        {
            try
            {
                using (var key = ServiceRegistryKey.OpenIfTrusted(writable: true, out var reason))
                {
                    if (key == null)
                    {
                        if (reason != null && reason != "key-missing")
                            m_eventLog.WriteEntry(
                                $"Refusing to process secure-datafolder registry command: {reason}",
                                System.Diagnostics.EventLogEntryType.Warning);
                        return;
                    }

                    var raw = key.GetValue(SECURE_DATAFOLDER_REGISTRY_VALUE);
                    if (raw == null)
                        return;

                    var value = raw as string;
                    if (string.IsNullOrEmpty(value))
                        return;

                    // Always delete the registry value after reading it
                    key.DeleteValue(SECURE_DATAFOLDER_REGISTRY_VALUE, throwOnMissingValue: false);

                    // Only the "apply" instruction is supported; any other value is ignored
                    if (!string.Equals(value, "apply", StringComparison.OrdinalIgnoreCase))
                    {
                        m_eventLog.WriteEntry($"Unknown secure-datafolder command: {value}", System.Diagnostics.EventLogEntryType.Warning);
                        return;
                    }

                    // Resolve the data folder in probe mode so we don't create it or trigger the
                    // security check — we just need the path to lock down the permissions.
                    var dataFolder = DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ProbeOnly);
                    if (!System.IO.Directory.Exists(dataFolder))
                    {
                        m_eventLog.WriteEntry($"Data folder '{dataFolder}' does not exist, cannot secure it.", System.Diagnostics.EventLogEntryType.Warning);
                        return;
                    }

                    m_eventLog.WriteEntry($"Securing data folder: {dataFolder}", System.Diagnostics.EventLogEntryType.Information);

                    try
                    {
                        SystemIO.IO_OS.DirectorySetPermissionUserRWOnly(dataFolder);

                        // Verify the permissions were actually set correctly
                        if (SystemIO.IO_OS.DirectoryHasPermissionUserRWOnly(dataFolder, out var detail))
                            m_eventLog.WriteEntry("Data folder permissions restricted to SYSTEM and Administrators only.", System.Diagnostics.EventLogEntryType.Information);
                        else
                            m_eventLog.WriteEntry($"Data folder permissions could not be verified after applying: {detail}", System.Diagnostics.EventLogEntryType.Warning);
                    }
                    catch (Exception ex)
                    {
                        m_eventLog.WriteEntry($"Failed to set permissions on data folder '{dataFolder}': {ex.Message}", System.Diagnostics.EventLogEntryType.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                m_eventLog.WriteEntry("Failed to process secure-datafolder registry command: " + ex.Message, System.Diagnostics.EventLogEntryType.Warning);
            }
        }

        /// <summary>
        /// Reads the initial password from the registry if present, and then starts the server with the password.
        /// If the password is present, it is removed from the registry after the server has started.
        /// Starting the server with the password will cause the server to reset the password to the new value,
        /// and then immediately exit with exit code 0 if the password was reset successfully.
        /// </summary>
        private void ReadAndExecuteInitPasswordFromRegistry()
        {
            try
            {
                if (m_verbose_messages)
                    m_eventLog.WriteEntry("Checking for init password in registry...", System.Diagnostics.EventLogEntryType.Information);

                using (var key = ServiceRegistryKey.OpenIfTrusted(writable: true, out var reason))
                {
                    if (key == null)
                    {
                        if (reason != null && reason != "key-missing")
                            m_eventLog.WriteEntry(
                                $"Refusing to consume init password: {reason}",
                                System.Diagnostics.EventLogEntryType.Warning);

                        return;
                    }

                    var rawPwd = key.GetValue(INIT_REGISTRY_VALUE);
                    if (rawPwd != null)
                    {
                        var password = rawPwd as string;
                        var success = false;

                        if (!string.IsNullOrWhiteSpace(password))
                        {
                            m_eventLog.WriteEntry("Found init password in registry, applying...", System.Diagnostics.EventLogEntryType.Information);

                            if (m_verbose_messages)
                                m_eventLog.WriteEntry($"Starting server in-process to apply init password...", System.Diagnostics.EventLogEntryType.Information);

                            try
                            {
                                var res = Server.Program.Main(["--webservice-password-init=" + password]);
                                if (res == Server.Program.EXITCODE_INITPASSWORD_SUCCESS)
                                    success = true;
                            }
                            catch (Exception ex)
                            {
                                m_eventLog.WriteEntry($"Exception from applying init password: {ex.Message}", System.Diagnostics.EventLogEntryType.Warning);
                            }

                            if (m_verbose_messages)
                                m_eventLog.WriteEntry($"Result from applying init password: {success}", System.Diagnostics.EventLogEntryType.Information);
                        }

                        // Always delete the InitPassword key if found
                        key.DeleteValue(INIT_REGISTRY_VALUE, throwOnMissingValue: false);

                        // Always write sentinel if the key was present.
                        // This is not locked down because the MSI must be able to read it.
                        using (var stateView = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                        using (var stateKey = stateView.CreateSubKey(SERVICE_STATE_REGISTRY_KEY, writable: true))
                            stateKey.SetValue(BOOTSTRAP_APPLIED_VALUE, success ? "1" : "0", RegistryValueKind.String);
                    }
                }

            }
            catch (Exception ex)
            {
                m_eventLog.WriteEntry("Failed to apply init password: " + ex.Message, System.Diagnostics.EventLogEntryType.Warning);
            }
        }

        /// <summary>
        /// Reads the reset password from the registry if present, and then returns it.
        /// If the password is present, it is removed from the registry after being read.
        /// </summary>
        private string ConsumeResetPasswordFromRegistry()
        {
            try
            {
                if (m_verbose_messages)
                    m_eventLog.WriteEntry("Checking for reset password in registry...", System.Diagnostics.EventLogEntryType.Information);

                using (var key = ServiceRegistryKey.OpenIfTrusted(writable: true, out var reason))
                {
                    if (key == null)
                    {
                        if (reason != null && reason != "key-missing")
                            m_eventLog.WriteEntry(
                                $"Refusing to consume reset password: {reason}",
                                System.Diagnostics.EventLogEntryType.Warning);
                        return null;
                    }

                    var rawPwd = key.GetValue(RESET_REGISTRY_VALUE);
                    key.DeleteValue(RESET_REGISTRY_VALUE, throwOnMissingValue: false);

                    if (m_verbose_messages)
                        m_eventLog.WriteEntry($"Found reset password in registry: {rawPwd as string != null}", System.Diagnostics.EventLogEntryType.Information);

                    if (rawPwd != null)
                    {
                        var password = rawPwd as string;
                        if (m_verbose_messages && !string.IsNullOrWhiteSpace(password))
                            m_eventLog.WriteEntry($"Found reset password in registry, applying", System.Diagnostics.EventLogEntryType.Information);
                        return string.IsNullOrWhiteSpace(password) ? null : password;
                    }
                }
            }
            catch (Exception ex)
            {
                m_eventLog.WriteEntry("Failed to read reset password: " + ex.Message, System.Diagnostics.EventLogEntryType.Warning);
            }
            return null;
        }
    }
}
