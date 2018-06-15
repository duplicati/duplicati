//  Copyright (C) 2018, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Linq;
using Duplicati.Library.Utility;

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
        private bool m_disposed = true;

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
            if (Duplicati.Library.Utility.Utility.IsClientWindows)
            {
                try
                {
                    Win32.SetThreadExecutionState(Win32.EXECUTION_STATE.ES_CONTINUOUS | Win32.EXECUTION_STATE.ES_SYSTEM_REQUIRED);
                    m_runningSleepPrevention = true;
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "SleepPrevetionError", ex, "Failed to set sleep prevention");
                }
            }
            else if (Duplicati.Library.Utility.Utility.IsClientOSX)
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
            else
            {


            }
        }

        /// <summary>
        /// Activates the process bacground IO priority
        /// </summary>
        private void ActivateBackgroundIOPriority()
        {
            var pid = System.Diagnostics.Process.GetCurrentProcess().Id;

            if (Duplicati.Library.Utility.Utility.IsClientWindows)
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
                if (Duplicati.Library.Utility.Utility.IsClientOSX)
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
                        m_originalNiceLevel = -1;
                    }
                    else if (string.Equals(ioclass, "none", StringComparison.OrdinalIgnoreCase))
                    {
                        m_originalNiceClass = 0;
                        m_originalNiceLevel = int.Parse(results.Last());
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
        /// Starts the process controller
        /// </summary>
        /// <param name="options">The options to use</param>
        private void Start(Options options)
        {
            if (!options.AllowSleep)
                StartSleepPrevention();

            if (options.UseBackgroundIOPriority)
                ActivateBackgroundIOPriority();
        }

        /// <summary>
        /// Stops the sleep prevention, if it was enabled
        /// </summary>
        private void StopSleepPrevention()
        {
            if (Duplicati.Library.Utility.Utility.IsClientWindows)
            {
                try
                {
                    if (m_runningSleepPrevention)
                    {
                        m_runningSleepPrevention = false;
                        Win32.SetThreadExecutionState(Win32.EXECUTION_STATE.ES_CONTINUOUS);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "SleepPrevetionError", ex, "Failed to set sleep prevention");
                }
            }
            else if (Duplicati.Library.Utility.Utility.IsClientOSX)
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
        }

        /// <summary>
        /// Deactivates the background IO Priority, if set.
        /// </summary>
        private void DeactivateBackgroundIOPriority()
        {
            if (Duplicati.Library.Utility.Utility.IsClientWindows)
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

                    if (Duplicati.Library.Utility.Utility.IsClientOSX)
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
                catch(Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "ProcessControllerStopError", ex, "Failed to stop the process controller: {0}", ex.Message);
                }
            }
        }
    }
}
