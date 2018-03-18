#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Server
{
    /// <summary>
    /// This class keeps track of the users modifications regarding
    /// throttling and pause/resume
    /// </summary>
    public class LiveControls
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<LiveControls>();

        /// <summary>
        /// An event that is activated when the pause state changes
        /// </summary>
        public event EventHandler StateChanged;

        /// <summary>
        /// An event that is activated when the thread priority changes
        /// </summary>
        public event EventHandler ThreadPriorityChanged;

        /// <summary>
        /// An event that is activated when the throttle speed changes
        /// </summary>
        public event EventHandler ThrottleSpeedChanged;

        /// <summary>
        /// The possible states for the live control
        /// </summary>
        public enum LiveControlState
        {
            /// <summary>
            /// Indicates that the backups are running
            /// </summary>
            Running,
            /// <summary>
            /// Indicates that the backups are currently suspended
            /// </summary>
            Paused
        }

        /// <summary>
        /// The current control state
        /// </summary>
        private LiveControlState m_state;

        /// <summary>
        /// A value that indicates if the current pause state is caused by being suspended
        /// </summary>
        private bool m_pausedForSuspend = false;

        /// <summary>
        /// The time to pause for, used to ensure that a user set pause can override the suspend pause
        /// </summary>
        private DateTime m_suspendMinimumPause = new DateTime(0);

        /// <summary>
        /// Gets the current state for the control
        /// </summary>
        public LiveControlState State { get { return m_state; } }

        /// <summary>
        /// The internal variable that tracks the the priority
        /// </summary>
        private System.Threading.ThreadPriority? m_priority;

        /// <summary>
        /// The internal variable that tracks the upload limit
        /// </summary>
        private long? m_uploadLimit;

        /// <summary>
        /// The internal variable that tracks the download limit
        /// </summary>
        private long? m_downloadLimit;

        /// <summary>
        /// The object that ensures concurrent operations
        /// </summary>
        private readonly object m_lock = new object();

        /// <summary>
        /// Gets the current overridden thread priority
        /// </summary>
        public System.Threading.ThreadPriority? ThreadPriority 
        { 
            get { return m_priority; }
            set
            {
                if (m_priority != value)
                {
                    m_priority = value;
                    if (ThreadPriorityChanged != null)
                        ThreadPriorityChanged(this, null);
                }
            }
        }

        /// <summary>
        /// Gets the current upload limit in bps
        /// </summary>
        public long? UploadLimit 
        { 
            get { return m_uploadLimit; }
            set
            {
                if (m_uploadLimit != value)
                {
                    m_uploadLimit = value;
                    if (ThrottleSpeedChanged != null)
                        ThrottleSpeedChanged(this, null);
                }
            }
        }

        /// <summary>
        /// Gets the download limit in bps
        /// </summary>
        public long? DownloadLimit 
        { 
            get { return m_downloadLimit; }
            set
            {
                if (m_downloadLimit != value)
                {
                    m_downloadLimit = value;
                    if (ThrottleSpeedChanged != null)
                        ThrottleSpeedChanged(this, null);
                }
            }
        }

        /// <summary>
        /// The timer that is activated after a pause period.
        /// </summary>
        private System.Threading.Timer m_waitTimer;

        /// <summary>
        /// The time that the current pause is expected to expire
        /// </summary>
        private DateTime m_waitTimeExpiration = new DateTime(0);

        /// <summary>
        /// Constructs a new instance of the LiveControl
        /// </summary>
        public LiveControls(Database.ServerSettings settings)
        {
            m_state = LiveControlState.Running;
            m_waitTimer = new System.Threading.Timer(m_waitTimer_Tick, this, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

            if (!string.IsNullOrEmpty(settings.StartupDelayDuration) && settings.StartupDelayDuration != "0")
            {
                long milliseconds = 0;
                try { milliseconds = (long)Duplicati.Library.Utility.Timeparser.ParseTimeSpan(settings.StartupDelayDuration).TotalMilliseconds; }
                catch {}

                if (milliseconds > 0)
                {
                    m_waitTimeExpiration = DateTime.Now.AddMilliseconds(milliseconds);
                    m_waitTimer.Change(milliseconds, System.Threading.Timeout.Infinite);
                    m_state = LiveControlState.Paused;
                }
            }

            m_priority = settings.ThreadPriorityOverride;
            if (!string.IsNullOrEmpty(settings.DownloadSpeedLimit))
                try
                {
                    m_downloadLimit = Library.Utility.Sizeparser.ParseSize(settings.DownloadSpeedLimit, "kb");
                }
                catch (Exception ex)
                {
                    Library.Logging.Log.WriteErrorMessage(LOGTAG, "ParseDownloadLimitError", ex, "Failed to parse download limit: {0}", settings.DownloadSpeedLimit);
                }

            if (!string.IsNullOrEmpty(settings.UploadSpeedLimit))
                try
                {
                    m_uploadLimit = Library.Utility.Sizeparser.ParseSize(settings.UploadSpeedLimit, "kb");
                }
                catch (Exception ex)
                {
                    Library.Logging.Log.WriteErrorMessage(LOGTAG, "ParseUploadLimitError", ex, "Failed to parse upload limit: {0}", settings.UploadSpeedLimit);
                }

            try
            {
                if (!Library.Utility.Utility.IsClientLinux)
                    RegisterHibernateMonitor();
            }
            catch { }
        }

        /// <summary>
        /// Event that occurs when the timeout duration is exceeded
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        private void  m_waitTimer_Tick(object sender)
        {
            lock (m_lock)
                Resume();
        }

        /// <summary>
        /// Internal helper to reset the timeout timer
        /// </summary>
        /// <param name="timeout">The time to wait</param>
        private void ResetTimer(string timeout)
        {
            lock (m_lock)
                if (!string.IsNullOrEmpty(timeout))
                {
                    long milliseconds = (long)Duplicati.Library.Utility.Timeparser.ParseTimeSpan(timeout).TotalMilliseconds;
                    m_waitTimeExpiration = DateTime.Now.AddMilliseconds(milliseconds);
                    m_waitTimer.Change(milliseconds, System.Threading.Timeout.Infinite);
                }
                else
                {
                    m_waitTimeExpiration = new DateTime(0);
                    m_waitTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                }
        }

        /// <summary>
        /// Internal helper to set the pause mode
        /// </summary>
        private void SetPauseMode()
        {
            lock (m_lock)
            {
                if (m_state == LiveControlState.Running)
                {
                    m_state = LiveControlState.Paused;
                    if (StateChanged != null)
                        StateChanged(this, null);
                }
            }
        }

        /// <summary>
        /// Pauses the backups until resumed
        /// </summary>
        public void Pause()
        {
            lock(m_lock)
            {
                var fireEvent = m_waitTimeExpiration.Ticks != 0 && m_state == LiveControlState.Paused && StateChanged != null;

                ResetTimer(null);

                if (fireEvent)
                    StateChanged(this, null);
                else
                    SetPauseMode();
            }
        }

        /// <summary>
        /// Resumes a backups to the running state
        /// </summary>
        public void Resume()
        {
            lock (m_lock)
            {
                if (m_state == LiveControlState.Paused)
                {
                    //Make sure that the timer is cleared
                    ResetTimer(null);

                    m_state = LiveControlState.Running;
                    if (StateChanged != null)
                        StateChanged(this, null);
                }
            }
        }

        /// <summary>
        /// Suspends the backups for a given period
        /// </summary>
        /// <param name="timeout">The duration to wait</param>
        public void Pause(string timeout)
        {
            Pause(Duplicati.Library.Utility.Timeparser.ParseTimeSpan(timeout));
        }

        /// <summary>
        /// Suspends the backups for a given period
        /// </summary>
        /// <param name="timeout">The duration to wait</param>
        public void Pause(TimeSpan timeout)
        {
            lock (m_lock)
            {
                m_waitTimeExpiration = DateTime.Now.AddMilliseconds((long)timeout.TotalMilliseconds);
                m_waitTimer.Change((long)timeout.TotalMilliseconds, System.Threading.Timeout.Infinite);

                //We change the time, so we issue a new event
                if (m_state == LiveControlState.Paused && StateChanged != null)
                    StateChanged(this, null);
                else
                    SetPauseMode();
            }
        }

        /// <summary>
        /// Gets the time the current pause is expected to end
        /// </summary>
        public DateTime EstimatedPauseEnd { get { return m_waitTimeExpiration; } }

        /// <summary>
        /// Method for calling a Win32 API
        /// </summary>
        private void RegisterHibernateMonitor()
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged += new Microsoft.Win32.PowerModeChangedEventHandler(SystemEvents_PowerModeChanged);
        }

        /// <summary>
        /// Method for calling a Win32 API
        /// </summary>
        private void UnregisterHibernateMonitor()
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= new Microsoft.Win32.PowerModeChangedEventHandler(SystemEvents_PowerModeChanged);
        }

        /// <summary>
        /// A monitor for detecting when the system hibernates or resumes
        /// </summary>
        /// <param name="sender">Unused sender parameter</param>
        /// <param name="_e">The event information</param>
        private void SystemEvents_PowerModeChanged(object sender, object _e)
        {
            Microsoft.Win32.PowerModeChangedEventArgs e = _e as Microsoft.Win32.PowerModeChangedEventArgs;
            if (e == null)
                return;

            if (e.Mode == Microsoft.Win32.PowerModes.Suspend)
            {
                //If we are running, register as being paused due to suspending
                if (this.m_state == LiveControlState.Running)
                {
                    this.SetPauseMode();
                    m_pausedForSuspend = true;
                    m_suspendMinimumPause = new DateTime(0);
                }
                else
                {
                    if (m_waitTimeExpiration.Ticks != 0)
                    {
                        m_pausedForSuspend = true;
                        m_suspendMinimumPause = this.EstimatedPauseEnd;
                        ResetTimer(null);
                    }

                }
            }
            else if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            {
                //If we have been been paused due to suspending, we un-pause now
                if (m_pausedForSuspend)
                {
                    long delayTicks = (m_suspendMinimumPause - DateTime.Now).Ticks;
                    
                    var appset = Program.DataConnection.ApplicationSettings;
                    if (!string.IsNullOrEmpty(appset.StartupDelayDuration) && appset.StartupDelayDuration != "0")
                        try { delayTicks = Math.Max(delayTicks, Library.Utility.Timeparser.ParseTimeSpan(appset.StartupDelayDuration).Ticks); }
                        catch { }

                    if (delayTicks > 0)
                    {
                        this.Pause(TimeSpan.FromTicks(delayTicks));
                    }
                    else
                    {
                        this.Resume();
                    }
                }

                m_pausedForSuspend = false;
                m_suspendMinimumPause = new DateTime(0);
            }
        }

    }
}
