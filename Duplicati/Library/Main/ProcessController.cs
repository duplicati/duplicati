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
        /// The options used
        /// </summary>
        private readonly Options m_options;
        /// <summary>
        /// A flag used to control the stop invocation
        /// </summary>
        private bool m_disposed = true;

        /// <summary>
        /// A flag indicating if the sleep prevention has been started
        /// </summary>
        private bool m_runningSleepPrevention;

        /// <summary>
        /// The caffeinate process runner
        /// </summary>
        private System.Diagnostics.Process m_caffeinate;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Main.ProcessController"/> class.
        /// </summary>
        /// <param name="options">The options to use.</param>
        public ProcessController(Options options)
        {
            m_options = options;
            if (m_options == null)
                return;
            
            try
            {
                Start();
                m_disposed = false;
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "ProcessControllerStartError", ex, "Failed to start the process controller: {0}", ex.Message);
            }

        }

        /// <summary>
        /// Starts the process controller
        /// </summary>
        private void Start()
        {
            if (!m_options.AllowSleep)
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
                        var psi = new System.Diagnostics.ProcessStartInfo("caffeinate", "-s");
                        psi.RedirectStandardInput = true;
                        psi.UseShellExecute = false;
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
        }

        /// <summary>
        /// Stops the process controller
        /// </summary>
        private void Stop()
        {
            if (Duplicati.Library.Utility.Utility.IsClientWindows)
            {
                try
                {
                    if (m_runningSleepPrevention)
                        Win32.SetThreadExecutionState(Win32.EXECUTION_STATE.ES_CONTINUOUS);
                    m_runningSleepPrevention = false;
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
