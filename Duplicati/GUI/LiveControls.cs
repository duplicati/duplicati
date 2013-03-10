#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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

namespace Duplicati.GUI
{
    /// <summary>
    /// This class keeps track of the users modifications regarding
    /// throttling and pause/resume
    /// </summary>
    public class LiveControls
    {
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
        /// We use a Windows.Forms timer to ensure that the event is raised
        /// in the correct thread (the UI thread).
        /// </summary>
        private System.Windows.Forms.Timer m_waitTimer;

        /// <summary>
        /// The time that the current pause is expected to expire
        /// </summary>
        private DateTime m_waitTimeExpiration = new DateTime(0);

        /// <summary>
        /// Constructs a new instance of the LiveControl
        /// </summary>
        /// <param name="initialTimeout">The duration that the backups should be initially suspended</param>
        public LiveControls(Datamodel.ApplicationSettings settings)
        {
            m_state = LiveControlState.Running;
            m_waitTimer = new System.Windows.Forms.Timer();
            m_waitTimer.Tick += new EventHandler(m_waitTimer_Tick);
            if (!string.IsNullOrEmpty(settings.StartupDelayDuration) && settings.StartupDelayDuration != "0")
            {
                long milliseconds = 0;
                try { milliseconds = (long)Duplicati.Library.Utility.Timeparser.ParseTimeSpan(settings.StartupDelayDuration).TotalMilliseconds; }
                catch { }

                if (milliseconds > 0)
                {
                    m_waitTimer.Interval = (int)milliseconds;
                    m_waitTimer.Enabled = true;
                    m_waitTimeExpiration = DateTime.Now.AddMilliseconds(milliseconds);
                    m_state = LiveControlState.Paused;
                }
            }

            m_priority = settings.ThreadPriorityOverride;
            if (!string.IsNullOrEmpty(settings.DownloadSpeedLimit))
                m_downloadLimit = Library.Utility.Sizeparser.ParseSize(settings.DownloadSpeedLimit, "kb");
            if (!string.IsNullOrEmpty(settings.UploadSpeedLimit))
                m_uploadLimit = Library.Utility.Sizeparser.ParseSize(settings.UploadSpeedLimit, "kb");

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
        /// <param name="e">An unused event argument</param>
        private void  m_waitTimer_Tick(object sender, EventArgs e)
        {
            m_waitTimeExpiration = new DateTime(0);
            Resume();
        }

        /// <summary>
        /// Pauses the backups until resumed
        /// </summary>
        public void Pause()
        {
            if (m_state == LiveControlState.Running)
            {
                m_state = LiveControlState.Paused;
                if (StateChanged != null)
                    StateChanged(this, null);
            }
        }

        /// <summary>
        /// Resumes a backups to the running state
        /// </summary>
        public void Resume()
        {
            if (m_state == LiveControlState.Paused)
            {
                //Make sure that the timer is cleared
                m_waitTimer.Enabled = false;

                m_state = LiveControlState.Running;
                if (StateChanged != null)
                    StateChanged(this, null);
            }
        }

        /// <summary>
        /// Suspends the backups for a given period
        /// </summary>
        /// <param name="timeout">The duration to wait</param>
        public void Pause(TimeSpan timeout)
        {
            Pause();
            m_waitTimer.Enabled = false;
            m_waitTimer.Interval = ((int)timeout.TotalMilliseconds);
            m_waitTimeExpiration = DateTime.Now + TimeSpan.FromMilliseconds(m_waitTimer.Interval);
            m_waitTimer.Enabled = true;
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
                    this.Pause();
                    m_pausedForSuspend = true;
                    m_suspendMinimumPause = new DateTime(0);
                }
                else
                {
                    if (m_waitTimeExpiration.Ticks != 0)
                    {
                        m_pausedForSuspend = true;
                        m_suspendMinimumPause = m_waitTimeExpiration;
                        m_waitTimeExpiration = new DateTime(0);
                        m_waitTimer.Enabled = false;
                    }

                }
            }
            else if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            {
                //If we have been been paused due to suspending, we un-pause now
                if (m_pausedForSuspend)
                {
                    long delayTicks = (m_suspendMinimumPause - DateTime.Now).Ticks;
                    
                    Datamodel.ApplicationSettings appset = new Datamodel.ApplicationSettings(Program.DataConnection);
                    if (!string.IsNullOrEmpty(appset.StartupDelayDuration) && appset.StartupDelayDuration != "0")
                        delayTicks = Math.Max(delayTicks, Library.Utility.Timeparser.ParseTimeSpan(appset.StartupDelayDuration).Ticks);

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
