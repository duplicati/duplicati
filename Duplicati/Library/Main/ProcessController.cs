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
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Utility;
using Tmds.DBus.Protocol;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// This class provides various process control tasks,
    /// such as preventing sleep and setting the IO priority of
    /// the running process
    /// </summary>
    public class ProcessController : IDisposable
    {
        /// <summary>
        /// The log tag to use
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<ProcessController>();

        /// <summary>
        /// A flag used to control the stop invocation
        /// </summary>
        private volatile bool m_disposed = true;

        /// <summary>
        /// A flag indicating if the sleep prevention has been started
        /// </summary>
        private bool m_runningSleepPrevention;

        /// <summary>
        /// A flag indicating if the background IO priority has been started
        /// </summary>
        private bool m_hasEnabledBackgroundIOPriority;

        /// <summary>
        /// The caffeinate process runner
        /// </summary>
        private System.Diagnostics.Process m_caffeinate;

        /// <summary>
        /// The login1 inhibitor file descriptor, must be kept open to hold the lock
        /// </summary>
        private SafeHandle m_login1InhibitorHandle;

        /// <summary>
        /// The session bus connection holding the XDG desktop portal inhibition,
        /// disposing the connection ends the inhibition
        /// </summary>
        private DBusConnection m_portalInhibitConnection;

        /// <summary>
        /// Lock that guards the Linux sleep prevention state,
        /// which is assigned on a background thread
        /// </summary>
        private readonly object m_sleepPreventionLock = new object();

        /// <summary>
        /// The maximum time to wait for a D-Bus response when setting up sleep prevention
        /// </summary>
        private static readonly TimeSpan DBusTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// The nice level to restore the process to
        /// </summary>
        private int m_originalNiceLevel;

        /// <summary>
        /// The nice class to restore the process to
        /// </summary>
        private int m_originalNiceClass;

        /// <summary>
        /// The priority class to restore the process to
        /// </summary>
        private Win32.IO_PRIORITY_HINT m_originalWinPriorityClass;

        /// <summary>
        /// A flag indicating if the Windows background mode is started
        /// </summary>
        private bool m_hasStartedBackgroundMode = false;

        /// <summary>
        /// A timer used to prevent sleep
        /// </summary>
        private CancellationTokenSource m_timerCancellation;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Main.ProcessController"/> class.
        /// </summary>
        /// <param name="options">The options to use.</param>
        public ProcessController(Options options)
        {
            if (options == null)
                return;

            try
            {
                Start(options);
                m_disposed = false;
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "ProcessControllerStartError", ex, "Failed to start the process controller: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Starts the sleep prevention
        /// </summary>
        private void StartSleepPrevention()
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    m_timerCancellation?.Cancel();
                    m_timerCancellation = new CancellationTokenSource();

                    Task.Run(async () =>
                    {
                        if (!OperatingSystem.IsWindows())
                            return;

                        try
                        {
                            while (true)
                            {
                                // Capture the cancellation token, so we don't risk it being set to null
                                var ct = m_timerCancellation;
                                if (ct == null || ct.Token.IsCancellationRequested)
                                    break;

                                Win32.SetThreadExecutionState(Win32.EXECUTION_STATE.ES_CONTINUOUS | Win32.EXECUTION_STATE.ES_SYSTEM_REQUIRED);
                                await Task.Delay(TimeSpan.FromSeconds(10), ct.Token);
                            }
                        }
                        catch (TaskCanceledException)
                        {
                            // Ignore
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "SleepPreventionError", ex, "Failed to set sleep prevention");
                        }
                    }).FireAndForget();

                    m_runningSleepPrevention = true;
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "SleepPreventionError", ex, "Failed to set sleep prevention");
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                try
                {
                    // -s prevents sleep on AC, -i prevents sleep generally
                    var psi = new System.Diagnostics.ProcessStartInfo("caffeinate", "-s")
                    {
                        RedirectStandardInput = true,
                        RedirectStandardError = false,
                        RedirectStandardOutput = false,
                        UseShellExecute = false
                    };
                    m_caffeinate = System.Diagnostics.Process.Start(psi);
                    m_runningSleepPrevention = true;
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "SleepPreventionError", ex, "Failed to set sleep prevention");
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                // The D-Bus calls are performed on a background thread so startup is not blocked
                Task.Run(async () =>
                {
                    try
                    {
                        // Prefer the systemd logind inhibitor, which works both for
                        // desktop sessions and for system services without a display
                        var login1Handle = await TryStartLogin1InhibitorAsync().ConfigureAwait(false);
                        if (login1Handle != null)
                        {
                            if (TryAdoptSleepPrevention(login1Handle, null))
                                Logging.Log.WriteVerboseMessage(LOGTAG, "SleepPreventionStarted", "Sleep prevention activated via systemd-logind");
                            else
                                login1Handle.Dispose();
                            return;
                        }

                        // Fall back to the XDG desktop portal, which requires a desktop session
                        var portalConnection = await TryStartPortalInhibitorAsync().ConfigureAwait(false);
                        if (portalConnection != null)
                        {
                            if (TryAdoptSleepPrevention(null, portalConnection))
                                Logging.Log.WriteVerboseMessage(LOGTAG, "SleepPreventionStarted", "Sleep prevention activated via the XDG desktop portal");
                            else
                                portalConnection.Dispose();
                            return;
                        }

                        Logging.Log.WriteVerboseMessage(LOGTAG, "SleepPreventionNotAvailable", "Sleep prevention is not available; neither systemd-logind nor the XDG desktop portal responded");
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "SleepPreventionError", ex, "Failed to set sleep prevention");
                    }
                }).FireAndForget();
            }
        }

        /// <summary>
        /// Attempts to adopt the sleep prevention resources obtained on the background thread.
        /// If the controller has already been disposed, the resources are not adopted and
        /// the caller is responsible for disposing them.
        /// </summary>
        /// <param name="login1Handle">The login1 inhibitor handle, if any</param>
        /// <param name="portalConnection">The portal inhibit connection, if any</param>
        /// <returns>True if the resources were adopted; false if the controller is disposed</returns>
        private bool TryAdoptSleepPrevention(SafeHandle login1Handle, DBusConnection portalConnection)
        {
            lock (m_sleepPreventionLock)
            {
                if (m_disposed)
                    return false;

                m_login1InhibitorHandle = login1Handle;
                m_portalInhibitConnection = portalConnection;
                m_runningSleepPrevention = true;
                return true;
            }
        }

        /// <summary>
        /// Requests a sleep inhibitor lock from systemd-logind via the system bus.
        /// The lock is held by keeping the returned file descriptor open.
        /// </summary>
        /// <returns>The inhibitor handle, or null if the inhibitor could not be taken</returns>
        private async Task<SafeHandle> TryStartLogin1InhibitorAsync()
        {
            try
            {
                var connection = DBusConnection.System;
                var callTask = connection.CallMethodAsync(
                    BuildLogin1InhibitMessage(),
                    (Message m, object _) => m.GetBodyReader().ReadHandle<Microsoft.Win32.SafeHandles.SafeFileHandle>(),
                    null);

                // Do not hang forever if the system bus does not respond
                if (await Task.WhenAny(callTask, Task.Delay(DBusTimeout)).ConfigureAwait(false) != callTask)
                {
                    // Observe the result of the abandoned call so a late reply or
                    // failure does not surface as an unobserved task exception;
                    // the inhibitor handle is closed again if the call completes
                    callTask.ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            Logging.Log.WriteVerboseMessage(LOGTAG, "Login1InhibitFailed", t.Exception, "Failed to inhibit sleep via systemd-logind: {0}", t.Exception.GetBaseException().Message);
                        else if (!t.IsCanceled)
                            t.Result.Dispose();
                    }, TaskContinuationOptions.ExecuteSynchronously).FireAndForget();

                    Logging.Log.WriteVerboseMessage(LOGTAG, "Login1InhibitTimeout", "Timed out waiting for systemd-logind to respond");
                    return null;
                }

                return await callTask.ConfigureAwait(false);

                MessageBuffer BuildLogin1InhibitMessage()
                {
                    var writer = connection.GetMessageWriter();
                    writer.WriteMethodCallHeader(
                        destination: "org.freedesktop.login1",
                        path: "/org/freedesktop/login1",
                        @interface: "org.freedesktop.login1.Manager",
                        signature: "ssss",
                        member: "Inhibit");
                    writer.WriteString("sleep");
                    writer.WriteString("Duplicati");
                    writer.WriteString("Backup in progress");
                    writer.WriteString("block");
                    return writer.CreateMessage();
                }
            }
            catch (Exception ex)
            {
                Logging.Log.WriteVerboseMessage(LOGTAG, "Login1InhibitFailed", ex, "Failed to inhibit sleep via systemd-logind: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Requests session suspension inhibition via the XDG desktop portal on the session bus.
        /// The inhibition is held as long as the connection is kept alive.
        /// </summary>
        /// <returns>The connection holding the inhibition, or null if the inhibitor could not be taken</returns>
        private async Task<DBusConnection> TryStartPortalInhibitorAsync()
        {
            DBusConnection connection = null;
            try
            {
                // A dedicated connection is required because AddMatchAsync and UniqueName
                // are not supported on the shared auto-connect connection
                var address = DBusAddress.Session;
                if (string.IsNullOrWhiteSpace(address))
                    return null;

                connection = new DBusConnection(address);
                await connection.ConnectAsync().ConfigureAwait(false);

                // Create a request token path that is unique to this connection
                var token = $"duplicati_{Guid.NewGuid():N}";
                var senderPath = connection.UniqueName.TrimStart(':').Replace('.', '_');
                var requestPath = $"/org/freedesktop/portal/desktop/request/{senderPath}/{token}";

                MessageBuffer BuildPortalInhibitMessage()
                {
                    var writer = connection.GetMessageWriter();
                    writer.WriteMethodCallHeader(
                        destination: "org.freedesktop.portal.Desktop",
                        path: "/org/freedesktop/portal/desktop",
                        @interface: "org.freedesktop.portal.Inhibit",
                        signature: "sua{sv}",
                        member: "Inhibit");
                    writer.WriteString(""); // No parent window
                    // Inhibit both suspend (4) and idle (8)
                    writer.WriteUInt32(12);
                    var flags = new Dictionary<string, VariantValue>
                    {
                        ["reason"] = VariantValue.String("Backup in progress"),
                        ["handle_token"] = VariantValue.String(token)
                    };
                    writer.WriteDictionary(flags);
                    return writer.CreateMessage();
                }

                // Listen for the Response signal (older/interactive portal backends emit it).
                var subscription = await connection.AddMatchAsync(
                    new MatchRule
                    {
                        Type = MessageType.Signal,
                        Interface = "org.freedesktop.portal.Request",
                        Member = "Response",
                        Path = requestPath
                    },
                    (Message m, object _) => m.GetBodyReader().ReadUInt32(),
                    _ => { },
                    false, ObserverFlags.None, null).ConfigureAwait(false);
                using (subscription)
                    await connection.CallMethodAsync(BuildPortalInhibitMessage(), (Message m, object _) => m.GetBodyReader().ReadObjectPathAsString(), null).ConfigureAwait(false);

                // Keep the connection alive; disposing it ends the inhibition
                var keepAlive = connection;
                connection = null;
                return keepAlive;
            }
            catch (Exception ex)
            {
                Logging.Log.WriteVerboseMessage(LOGTAG, "PortalInhibitFailed", ex, "Failed to inhibit sleep via the XDG desktop portal: {0}", ex.Message);
                return null;
            }
            finally
            {
                connection?.Dispose();
            }
        }

        /// <summary>
        /// Activates the process background IO priority
        /// </summary>
        private void ActivateBackgroundIOPriority()
        {
            var pid = System.Diagnostics.Process.GetCurrentProcess().Id;

            if (OperatingSystem.IsWindows())
            {
                var handle = System.Diagnostics.Process.GetCurrentProcess().Handle;

                try
                {
                    var mode = Win32.IO_PRIORITY_HINT.IoPriorityLow;
                    var res = Win32.NtQueryInformationProcess(handle, Win32.PROCESS_INFORMATION_CLASS.ProcessIoPriority, ref mode, sizeof(Win32.IO_PRIORITY_HINT), IntPtr.Zero);
                    if (res != 0)
                        throw new Library.Interface.UserInformationException($"Failed to read process priority {res:x}", "BackgroundPriorityEnableError", new System.ComponentModel.Win32Exception());

                    m_originalWinPriorityClass = mode;
                    mode = Win32.IO_PRIORITY_HINT.IoPriorityVeryLow;
                    res = Win32.NtSetInformationProcess(handle, Win32.PROCESS_INFORMATION_CLASS.ProcessIoPriority, ref mode, sizeof(Win32.IO_PRIORITY_HINT));
                    if (res != 0)
                        throw new Library.Interface.UserInformationException($"Failed to set process priority {res:x}", "BackgroundPriorityEnableError", new System.ComponentModel.Win32Exception());

                    m_hasEnabledBackgroundIOPriority = true;
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "BackgroundPriorityError", ex, "Failed to set background IO priority");
                }

                try
                {
                    if (!Win32.SetPriorityClass(handle, Win32.PROCESS_PRIORITY_CLASS.PROCESS_MODE_BACKGROUND_BEGIN))
                        throw new Library.Interface.UserInformationException($"Failed to start process background mode", "BackgroundPriorityEnableError", new System.ComponentModel.Win32Exception());
                    m_hasStartedBackgroundMode = true;
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "BackgroundPriorityError", ex, "Failed to set start background processing mode");
                }
            }
            else
            {
                if (OperatingSystem.IsMacOS())
                {
                    var data = RunProcessAndGetResult("ps", $"-onice -p {pid}");
                    if (data.Item1 != 0)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "BackgroundPriorityError", null, "Failed to get background IO priority, exitcode: {0}, stderr: {1}", data.Item1, data.Item3);
                    }
                    else
                    {
                        m_originalNiceLevel = int.Parse(data.Item2.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Last());

                        data = RunProcessAndGetResult("renice", $"20 -p {pid}");
                        if (data.Item1 != 0)
                            Logging.Log.WriteWarningMessage(LOGTAG, "BackgroundPriorityError", null, "Failed to get background IO priority, exitcode: {0}, stderr: {1}", data.Item1, data.Item3);
                        else
                            m_hasEnabledBackgroundIOPriority = true;
                    }
                }
                else
                {
                    var data = RunProcessAndGetResult("ionice", $"-p {pid}");
                    var results = data.Item2.Split(new char[] { ':', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var ioclass = results[0];
                    if (string.Equals(ioclass, "idle", StringComparison.OrdinalIgnoreCase))
                    {
                        m_originalNiceClass = 3;
                        // Only allowed for "best-effort" and "realtime"
                        m_originalNiceLevel = -1;
                    }
                    else if (string.Equals(ioclass, "none", StringComparison.OrdinalIgnoreCase))
                    {
                        m_originalNiceClass = 0;
                        // Only allowed for "best-effort" and "realtime"
                        m_originalNiceLevel = -1;
                    }
                    else if (string.Equals(ioclass, "best-effort", StringComparison.OrdinalIgnoreCase))
                    {
                        m_originalNiceClass = 2;
                        m_originalNiceLevel = int.Parse(results.Last());
                    }
                    else if (string.Equals(ioclass, "realtime", StringComparison.OrdinalIgnoreCase))
                    {
                        m_originalNiceClass = 1;
                        m_originalNiceLevel = int.Parse(results.Last());
                    }
                    else
                        throw new Library.Interface.UserInformationException($"Unable to parse priority class {ioclass}", "UnableToParseIONicePriorityClass");

                    data = RunProcessAndGetResult("ionice", $"-c 3 -p {pid}");
                    m_hasEnabledBackgroundIOPriority = true;
                }
            }
        }

        /// <summary>
        /// Expose all filesystem attributes
        /// </summary>
        private static void ExposeAllFilesystemAttributes()
        {
            // Starting with Windows 10 1803, the operating system may mask the process's view of some
            // file attributes such as reparse, offline, and sparse.
            //
            // This function will turn off such masking.
            //
            // See https://docs.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-rtlqueryprocessplaceholdercompatibilitymode

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    Win32.RtlSetProcessPlaceholderCompatibilityMode(Win32.PHCM_VALUES.PHCM_EXPOSE_PLACEHOLDERS);
                }
                catch
                {
                    // Ignore exceptions - not applicable on this version of Windows
                }
            }
        }

        /// <summary>
        /// Starts the process controller
        /// </summary>
        /// <param name="options">The options to use</param>
        private void Start(Options options)
        {
            if (!options.AllowSleep)
                StartSleepPrevention();

            if (options.UseBackgroundIOPriority)
                ActivateBackgroundIOPriority();

            ExposeAllFilesystemAttributes();
        }

        /// <summary>
        /// Stops the sleep prevention, if it was enabled
        /// </summary>
        private void StopSleepPrevention()
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    if (m_runningSleepPrevention)
                    {
                        m_runningSleepPrevention = false;
                        m_timerCancellation?.Dispose();
                        m_timerCancellation = null;

                        Win32.SetThreadExecutionState(Win32.EXECUTION_STATE.ES_CONTINUOUS);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "SleepPreventionError", ex, "Failed to set sleep prevention");
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                try
                {
                    m_runningSleepPrevention = false;
                    if (m_caffeinate != null && !m_caffeinate.HasExited)
                    {
                        // Send CTRL+C
                        m_caffeinate.StandardInput.Write("\x3");
                        m_caffeinate.StandardInput.Flush();
                        m_caffeinate.WaitForExit(500);

                        if (!m_caffeinate.HasExited)
                        {
                            m_caffeinate.Kill();
                            m_caffeinate.WaitForExit(500);
                            if (!m_caffeinate.HasExited)
                                throw new Exception("Failed to kill the caffeinate process");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "SleepPreventionDisableError", ex, "Failed to unset sleep prevention");
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                SafeHandle login1Handle;
                DBusConnection portalConnection;

                // Swap out the resources under the lock so the background
                // D-Bus setup cannot store new resources after this point
                lock (m_sleepPreventionLock)
                {
                    m_runningSleepPrevention = false;
                    login1Handle = m_login1InhibitorHandle;
                    m_login1InhibitorHandle = null;
                    portalConnection = m_portalInhibitConnection;
                    m_portalInhibitConnection = null;
                }

                try
                {
                    // Closing the file descriptor releases the login1 inhibitor lock
                    login1Handle?.Dispose();
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "SleepPreventionDisableError", ex, "Failed to unset sleep prevention");
                }

                try
                {
                    // Disposing the connection ends the portal inhibition
                    portalConnection?.Dispose();
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "SleepPreventionDisableError", ex, "Failed to unset sleep prevention");
                }
            }
        }

        /// <summary>
        /// Deactivates the background IO Priority, if set.
        /// </summary>
        private void DeactivateBackgroundIOPriority()
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    if (m_hasStartedBackgroundMode)
                    {
                        m_hasStartedBackgroundMode = false;
                        var handle = System.Diagnostics.Process.GetCurrentProcess().Handle;
                        if (!Win32.SetPriorityClass(handle, Win32.PROCESS_PRIORITY_CLASS.PROCESS_MODE_BACKGROUND_END))
                            throw new Library.Interface.UserInformationException($"Failed to stop process background mode", "BackgroundPriorityEnableError", new System.ComponentModel.Win32Exception());
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "BackgroundPriorityError", ex, "Failed to stop start background processing mode");
                }

                try
                {
                    if (m_hasEnabledBackgroundIOPriority)
                    {
                        m_hasEnabledBackgroundIOPriority = false;

                        var handle = System.Diagnostics.Process.GetCurrentProcess().Handle;
                        var mode = m_originalWinPriorityClass;
                        var res = Win32.NtSetInformationProcess(handle, Win32.PROCESS_INFORMATION_CLASS.ProcessIoPriority, ref mode, sizeof(Win32.IO_PRIORITY_HINT));
                        if (res != 0)
                            Logging.Log.WriteWarningMessage(LOGTAG, "BackgroundPriorityDisableError", new System.ComponentModel.Win32Exception(), "Failed to reset background IO priority, status code {0}", res);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "BackgroundPriorityError", ex, "Failed to reset background IO priority");
                }
            }
            else
            {
                if (m_hasEnabledBackgroundIOPriority)
                {
                    m_hasEnabledBackgroundIOPriority = false;
                    var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                    Tuple<int, string, string> data;

                    if (OperatingSystem.IsMacOS())
                    {
                        // TODO: We can only give lower priority, thus not reset it ...
                        data = RunProcessAndGetResult($"renice", $"{m_originalNiceLevel} -p {pid}");
                        if (data.Item1 != 0)
                            Logging.Log.WriteWarningMessage(LOGTAG, "BackgroundPriorityError", null, "Failed to reset background IO priority, exitcode: {0}, stderr: {1}", data.Item1, data.Item3);
                    }
                    else
                    {
                        if (m_originalNiceLevel < 0)
                            data = RunProcessAndGetResult($"ionice", $"-c {m_originalNiceClass} -p {pid}");
                        else
                            data = RunProcessAndGetResult($"ionice", $"-c {m_originalNiceClass} -n {m_originalNiceLevel} -p {pid}");

                        if (!string.IsNullOrWhiteSpace(data.Item3))
                            Logging.Log.WriteWarningMessage(LOGTAG, "BackgroundPriorityError", null, "Failed to reset background IO priority, exitcode: {0}, stderr: {1}", data.Item1, data.Item3);

                    }


                }
            }
        }

        /// <summary>
        /// Stops the process controller
        /// </summary>
        private void Stop()
        {

            StopSleepPrevention();
            DeactivateBackgroundIOPriority();
        }

        /// <summary>
        /// Runs a process and returns the stdout data
        /// </summary>
        /// <returns>The stdout data.</returns>
        /// <param name="filename">The executable to invoke.</param>
        /// <param name="arguments">The commandline arguments.</param>
        private static Tuple<int, string, string> RunProcessAndGetResult(string filename, string arguments)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(filename, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                UseShellExecute = false
            };

            Logging.Log.WriteExplicitMessage(LOGTAG, "RunningCommand", null, "Running: {0} {1}", filename, arguments);

            var pi = System.Diagnostics.Process.Start(psi);
            pi.WaitForExit(5000);
            if (pi.HasExited)
            {
                return
                    new Tuple<int, string, string>(
                        pi.ExitCode,
                        pi.StandardOutput.ReadToEnd().Trim(),
                        pi.StandardError.ReadToEnd().Trim()
                    );
            }
            pi.Kill();

            throw new Library.Interface.UserInformationException($"The process {filename} with arguments {arguments} failed to stop", "LaunchProcessFailed");
        }

        /// <summary>
        /// Releases all resource used by the <see cref="T:Duplicati.Library.Main.ProcessController"/> object.
        /// </summary>
        /// <remarks>Call <see cref="Dispose"/> when you are finished using the
        /// <see cref="T:Duplicati.Library.Main.ProcessController"/>. The <see cref="Dispose"/> method leaves the
        /// <see cref="T:Duplicati.Library.Main.ProcessController"/> in an unusable state. After calling
        /// <see cref="Dispose"/>, you must release all references to the
        /// <see cref="T:Duplicati.Library.Main.ProcessController"/> so the garbage collector can reclaim the memory
        /// that the <see cref="T:Duplicati.Library.Main.ProcessController"/> was occupying.</remarks>
		public void Dispose()
        {
            if (!m_disposed)
            {
                m_disposed = true;
                try
                {
                    Stop();
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "ProcessControllerStopError", ex, "Failed to stop the process controller: {0}", ex.Message);
                }
            }
        }
    }
}
