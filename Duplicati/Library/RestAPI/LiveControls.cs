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

using System;
using System.Runtime.Versioning;
using Duplicati.Library.IO;
using Duplicati.Server.Database;

namespace Duplicati.Server
{
    /// <summary>
    /// This class keeps track of the users modifications regarding
    /// throttling and pause/resume
    /// </summary>
    public class LiveControls : ILiveControls
    {
        /// <summary>
        /// Event that is activated the the live control state changes
        /// </summary>
        public sealed record LiveControlEvent
        {
            /// <summary>
            /// The new state of the live control
            /// </summary>
            public required LiveControlState State { get; init; }
            /// <summary>
            /// A value that indicates if the transfers are paused
            /// </summary>
            public required bool TransfersPaused { get; init; }
            /// <summary>
            /// The time when processing will resume, or zero if paused indefinitely
            /// </summary>
            public required DateTime WaitTimeExpiration { get; init; }
        }

        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<LiveControls>();

        /// <summary>
        /// An callback that is activated when the pause state changes
        /// </summary>
        public Action<LiveControlEvent> StateChanged;

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
        /// A value that indicates if the transfers are paused
        /// </summary>
        private bool m_transfersPaused = false;

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
        /// Gets a value that indicates if the backups are running
        /// </summary>
        public bool IsPaused => State == LiveControlState.Paused;

        /// <summary>
        /// Gets a value that indicates if the transfers are paused
        /// </summary>
        public bool TransfersPaused => m_transfersPaused;

        /// <summary>
        /// The object that ensures concurrent operations
        /// </summary>
        private readonly object m_lock = new object();

        /// <summary>
        /// The timer that is activated after a pause period.
        /// </summary>
        private System.Threading.Timer m_waitTimer;

        /// <summary>
        /// The time that the current pause is expected to expire
        /// </summary>
        private DateTime m_waitTimeExpiration = new DateTime(0);

        /// <summary>
        /// The connection to use
        /// </summary>
        private readonly Connection m_connection;

        /// <summary>
        /// Constructs a new instance of the LiveControl
        /// </summary>
        /// <param name="connection">The connection to use</param>
        public LiveControls(Connection connection)
        {
            m_connection = connection;
            Init();
        }

        /// <summary>
        /// Constructs a new instance of the LiveControl
        /// </summary>
        private void Init()
        {
            var settings = m_connection.ApplicationSettings;
            m_state = LiveControlState.Running;
            m_waitTimer = new System.Threading.Timer(m_waitTimer_Tick, this, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

            if (!string.IsNullOrEmpty(settings.StartupDelayDuration) && settings.StartupDelayDuration != "0")
            {
                long milliseconds = 0;
                try { milliseconds = (long)Duplicati.Library.Utility.Timeparser.ParseTimeSpan(settings.StartupDelayDuration).TotalMilliseconds; }
                catch { }

                if (milliseconds > 0)
                {
                    m_waitTimeExpiration = DateTime.Now.AddMilliseconds(milliseconds);
                    m_waitTimer.Change(milliseconds, System.Threading.Timeout.Infinite);
                    m_state = LiveControlState.Paused;
                }
            }

            var pausedUntil = settings.PausedUntil;
            if (pausedUntil != null)
            {
                if (pausedUntil.Value.Ticks == 0)
                {
                    m_waitTimeExpiration = new DateTime(0);
                    m_waitTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                    m_state = LiveControlState.Paused;
                }
                else if (pausedUntil.Value > DateTime.UtcNow && pausedUntil.Value > m_waitTimeExpiration)
                {
                    var ms = (long)(pausedUntil.Value - DateTime.UtcNow).TotalMilliseconds;
                    if (ms > 100)
                    {
                        m_waitTimeExpiration = pausedUntil.Value;
                        m_waitTimer.Change(ms, System.Threading.Timeout.Infinite);
                        m_state = LiveControlState.Paused;
                    }
                }
            }

            try
            {
                if (OperatingSystem.IsWindows())
                    RegisterHibernateMonitor();
            }
            catch { }
        }

        /// <summary>
        /// Event that occurs when the timeout duration is exceeded
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        private void m_waitTimer_Tick(object sender)
        {
            lock (m_lock)
                Resume();
        }

        /// <summary>
        /// Creates a new event object
        /// </summary>
        /// <returns>A new event object</returns>
        private LiveControlEvent CreateEvent()
        {
            lock (m_lock)
                return new LiveControlEvent()
                {
                    State = m_state,
                    TransfersPaused = m_transfersPaused,
                    WaitTimeExpiration = m_waitTimeExpiration
                };
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
            LiveControlEvent ev = null;
            lock (m_lock)
            {
                if (m_state == LiveControlState.Running)
                {
                    m_state = LiveControlState.Paused;
                    if (StateChanged != null)
                        ev = CreateEvent();
                }
            }

            if (ev != null)
                StateChanged(ev);
        }

        /// <summary>
        /// Pauses the backups until resumed
        /// </summary>
        public void Pause(bool alsoTransfers)
        {
            LiveControlEvent ev = null;
            lock (m_lock)
            {
                m_transfersPaused = alsoTransfers;
                m_waitTimeExpiration = new DateTime(0);

                ResetTimer(null);

                if (m_state == LiveControlState.Paused)
                    ev = StateChanged == null ? null : CreateEvent();
                else
                    SetPauseMode();
            }

            if (ev != null)
                StateChanged(ev);
        }

        /// <summary>
        /// Resumes a backups to the running state
        /// </summary>
        public void Resume()
        {
            LiveControlEvent ev = null;
            lock (m_lock)
            {
                if (m_state == LiveControlState.Paused)
                {
                    //Make sure that the timer is cleared
                    ResetTimer(null);

                    m_transfersPaused = false;
                    m_waitTimeExpiration = new DateTime(0);
                    m_state = LiveControlState.Running;
                    if (StateChanged != null)
                        ev = CreateEvent();
                }
            }

            if (ev != null)
                StateChanged(ev);
        }

        /// <summary>
        /// Suspends the backups for a given period
        /// </summary>
        /// <param name="timeout">The duration to wait</param>
        /// <param name="alsoTransfers">If true, also pause the transfers</param>
        public void Pause(string timeout, bool alsoTransfers)
        {
            Pause(Duplicati.Library.Utility.Timeparser.ParseTimeSpan(timeout), alsoTransfers);
        }

        /// <summary>
        /// Suspends the backups for a given period
        /// </summary>
        /// <param name="timeout">The duration to wait</param>
        /// <param name="alsoTransfers">If true, also pause the transfers</param>
        public void Pause(TimeSpan timeout, bool alsoTransfers)
        {
            LiveControlEvent ev = null;
            lock (m_lock)
            {
                m_waitTimeExpiration = DateTime.Now.AddMilliseconds((long)timeout.TotalMilliseconds);
                m_waitTimer.Change((long)timeout.TotalMilliseconds, System.Threading.Timeout.Infinite);
                m_transfersPaused = alsoTransfers;


                //We change the time, so we issue a new event
                if (m_state == LiveControlState.Paused)
                    ev = StateChanged == null ? null : CreateEvent();
                else
                    SetPauseMode();
            }

            if (ev != null)
                StateChanged(ev);
        }

        /// <summary>
        /// Gets the time the current pause is expected to end
        /// </summary>
        public DateTime EstimatedPauseEnd { get { return m_waitTimeExpiration; } }

        /// <summary>
        /// Method for calling a Win32 API
        /// </summary>
        [SupportedOSPlatform("windows")]
        private void RegisterHibernateMonitor()
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged += new Microsoft.Win32.PowerModeChangedEventHandler(SystemEvents_PowerModeChanged);
        }

        /// <summary>
        /// A monitor for detecting when the system hibernates or resumes
        /// </summary>
        /// <param name="sender">Unused sender parameter</param>
        /// <param name="_e">The event information</param>
        [SupportedOSPlatform("windows")]
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

                    var appset = m_connection.ApplicationSettings;
                    if (!string.IsNullOrEmpty(appset.StartupDelayDuration) && appset.StartupDelayDuration != "0")
                        try { delayTicks = Math.Max(delayTicks, Library.Utility.Timeparser.ParseTimeSpan(appset.StartupDelayDuration).Ticks); }
                        catch { }

                    if (delayTicks > 0)
                    {
                        this.Pause(TimeSpan.FromTicks(delayTicks), true);
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
