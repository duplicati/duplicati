#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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

namespace Duplicati.Server
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
        private object m_lock = new object();

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
        /// Constructs a new instance of the LiveControl
        /// </summary>
        /// <param name="initialTimeout">The duration that the backups should be initially suspended</param>
        public LiveControls(Datamodel.ApplicationSettings settings)
        {
            m_state = LiveControlState.Running;
            m_waitTimer = new System.Threading.Timer(m_waitTimer_Tick, this, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            if (!string.IsNullOrEmpty(settings.StartupDelayDuration) && settings.StartupDelayDuration != "0")
            {
                m_waitTimer.Change((long)Duplicati.Library.Utility.Timeparser.ParseTimeSpan(settings.StartupDelayDuration).TotalMilliseconds, System.Threading.Timeout.Infinite);
                m_state = LiveControlState.Paused;
            }

            m_priority = settings.ThreadPriorityOverride;
            if (!string.IsNullOrEmpty(settings.DownloadSpeedLimit))
                m_downloadLimit = Library.Utility.Sizeparser.ParseSize(settings.DownloadSpeedLimit, "kb");
            if (!string.IsNullOrEmpty(settings.UploadSpeedLimit))
                m_uploadLimit = Library.Utility.Sizeparser.ParseSize(settings.UploadSpeedLimit, "kb");
        }

        /// <summary>
        /// Event that occurs when the timeout duration is exceeded
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">An unused event argument</param>
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
                    m_waitTimer.Change((long)Duplicati.Library.Utility.Timeparser.ParseTimeSpan(timeout).TotalMilliseconds, System.Threading.Timeout.Infinite);
                else
                    m_waitTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        }

        /// <summary>
        /// Pauses the backups until resumed
        /// </summary>
        public void Pause()
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
        /// Resumes a backups to the running state
        /// </summary>
        public void Resume()
        {
            lock (m_lock)
            {
                if (m_state == LiveControlState.Paused)
                {
                    //Make sure that the timer is cleared
                    m_waitTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

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
            lock (m_lock)
            {
                if (m_state == LiveControlState.Running)
                {
                    Pause();
                    m_waitTimer.Change((long)Duplicati.Library.Utility.Timeparser.ParseTimeSpan(timeout).TotalMilliseconds, System.Threading.Timeout.Infinite);
                }
            }
        }
    }
}
