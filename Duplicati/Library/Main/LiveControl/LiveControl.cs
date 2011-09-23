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

namespace Duplicati.Library.Main.LiveControl
{
    /// <summary>
    /// A delegate that is used to signal that the backup state has changed
    /// </summary>
    public delegate void BackupStateChangedDelegate(BackupState newState);

    /// <summary>
    /// An enumeration that changes describes the states a backup can be in
    /// </summary>
    public enum BackupState
    {
        Running,
        Paused,
        Stopped,
        Terminated,
        Completed
    }

    /// <summary>
    /// This class is an interface to interacting with a running backup
    /// </summary>
    internal class LiveControl : ILiveControl
    {
        /// <summary>
        /// The event that the thread waits for when paused
        /// </summary>
        private System.Threading.ManualResetEvent m_pauseEvent = new System.Threading.ManualResetEvent(true);

        /// <summary>
        /// The flag used to signal a stop has been requested
        /// </summary>
        private volatile bool m_stopRequested = false;

        /// <summary>
        /// The flag used to signal a pause has been requested
        /// </summary>
        private volatile bool m_pauseRequested = false;

        /// <summary>
        /// An event that is invoked when the backup state changes
        /// </summary>
        public event BackupStateChangedDelegate BackupStateChanged;

        /// <summary>
        /// The interface thread that owns this instance
        /// </summary>
        private System.Threading.Thread m_owner;

        /// <summary>
        /// A copy of the current options
        /// </summary>
        private Options m_options;

        /// <summary>
        /// The backup specified thread priority
        /// </summary>
        private System.Threading.ThreadPriority m_defaultPriority;

        /// <summary>
        /// The download limit specified in the backup
        /// </summary>
        private long m_downloadLimit;

        /// <summary>
        /// The upload limit specified in the backup
        /// </summary>
        private long m_uploadLimit;

        /// <summary>
        /// Default constructor
        /// </summary>
        internal LiveControl(System.Threading.Thread owner, Options options)
        {
            m_owner = owner;
            m_options = options;
            m_defaultPriority = m_owner.Priority;
            m_downloadLimit = m_options.MaxDownloadPrSecond;
            m_uploadLimit = m_options.MaxUploadPrSecond;
        }

        /// <summary>
        /// Helper method to invoke the event
        /// </summary>
        /// <param name="state">The state the backup is in</param>
        internal void InvokeStateChangeEvent(BackupState state)
        {
            if (BackupStateChanged != null)
                BackupStateChanged(state);
        }

        /// <summary>
        /// Gets a value indicating if stop has been requested
        /// </summary>
        /// <returns></returns>
        public bool IsStopRequested
        {
            get { return m_stopRequested; }
        }

        /// <summary>
        /// Gets a value indicating if pause has been requested
        /// </summary>
        /// <returns></returns>
        public bool IsPauseRequested
        {
            get { return m_pauseRequested; }
        }

        /// <summary>
        /// Helper method to pause the current backup
        /// </summary>
        internal void PauseIfRequested()
        {
            while (!IsStopRequested && !m_pauseEvent.WaitOne(1000, false))
            {
                //Just repeat
            }
        }

        /// <summary>
        /// Activates an asyncronous request for pausing the backup
        /// </summary>
        public void Pause()
        {
            lock (m_pauseEvent)
                if (!m_stopRequested)                
                {
                    m_pauseRequested = true;
                    m_pauseEvent.Reset();
                }
        }

        /// <summary>
        /// Activates an asyncronous request for resuming a paused backup
        /// </summary>
        public void Resume()
        {
            lock (m_pauseEvent)
            {
                m_pauseRequested = false;
                m_pauseEvent.Set();
            }
        }

        /// <summary>
        /// Activates an asyncronous request for stopping the backup.
        /// Stopping a backup does not interrupt ongoing uploads.
        /// </summary>
        public void Stop()
        {
            lock (m_pauseEvent)
            {
                m_stopRequested = true;
                if (m_pauseRequested) Resume();
            }
        }

        /// <summary>
        /// Activates an asyncronous request for terminating the backup.
        /// Terminating a backup breaks any ongoing uploads, and may leave partial files on the backend.
        /// </summary>
        public void Terminate()
        {
            if (m_owner.IsAlive)
                m_owner.Abort();
        }

        public void SetUploadLimit(string limit)
        {
            long newLimit = Utility.Sizeparser.ParseSize(limit);

            if (newLimit <= 0)
                m_options.MaxUploadPrSecond = m_uploadLimit;
            else if (m_uploadLimit > 0)
                m_options.MaxUploadPrSecond = Math.Min(m_uploadLimit, newLimit);
            else
                m_options.MaxUploadPrSecond = newLimit;
        }

        public void SetDownloadLimit(string limit)
        {
            long newLimit = Utility.Sizeparser.ParseSize(limit);

            if (newLimit <= 0)
                m_options.MaxDownloadPrSecond = m_downloadLimit;
            else if (m_downloadLimit > 0)
                m_options.MaxDownloadPrSecond = Math.Min(m_downloadLimit, newLimit);
            else
                m_options.MaxDownloadPrSecond = newLimit;
        }

        public void SetThreadPriority(System.Threading.ThreadPriority priority)
        {
            m_owner.Priority = priority;
        }

        public void UnsetThreadPriority()
        {
            m_owner.Priority = m_defaultPriority;
        }
    }

}
