//  Copyright (C) 2014, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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
using System.Threading;
using Duplicati.Server.Serialization;

namespace Duplicati.Server
{
    /// <summary>
    /// The thread that checks on the update server if new versions are available
    /// </summary>
    public class UpdatePollThread
    {
        private Thread m_thread;
        private volatile bool m_terminated = false;
        private volatile bool m_download = false;
        private volatile bool m_forceCheck = false;
        private object m_lock = new object();
        private AutoResetEvent m_waitSignal;

        public UpdatePollerStates ThreadState { get; private set; }
        
        public UpdatePollThread()
        {
            m_waitSignal = new AutoResetEvent(false);
            ThreadState = UpdatePollerStates.Waiting;
            m_thread = new Thread(Run);
            m_thread.IsBackground = true;
            m_thread.Name = "UpdatePollThread";
            m_thread.Start();
        }

        public void CheckNow()
        {
            lock(m_lock)
            {
                m_forceCheck = true;
                m_waitSignal.Set();
            }
        }

        public void InstallUpdate()
        {
            lock(m_lock)
            {
                m_download = true;
                m_waitSignal.Set();
            }
        }

        public void ActivateUpdate()
        {
            if (Program.UpdateManager.SetRunUpdate())
                Program.ApplicationExitEvent.Set();
        }

        public void Terminate()
        {
            lock(m_lock)
            {
                m_terminated = true;
                m_waitSignal.Set();
            }
        }

        public void Reschedule()
        {
            m_waitSignal.Set();
        }

        private void Run()
        {
            // Wait on startup
            m_waitSignal.WaitOne(TimeSpan.FromMinutes(1), true);

            while (!m_terminated)
            {
                var nextCheck = Program.DataConnection.ApplicationSettings.NextUpdateCheck;
                if (nextCheck < DateTime.UtcNow || m_forceCheck)
                {
                    lock(m_lock)
                        m_forceCheck = false;

                    ThreadState = UpdatePollerStates.Checking;
                    Program.StatusEventNotifyer.SignalNewEvent();
                     
                    DateTime started = DateTime.UtcNow;
                    Program.DataConnection.ApplicationSettings.LastUpdateCheck = started;
                    nextCheck = Program.DataConnection.ApplicationSettings.NextUpdateCheck;

                    try
                    {
                        var update = Program.UpdateManager.CheckForUpdate();
                        if (update != null && (Program.DataConnection.ApplicationSettings.UpdatedVersion == null || Program.DataConnection.ApplicationSettings.UpdatedVersion.ReleaseTime != update.ReleaseTime))
                        {
                            Program.DataConnection.ApplicationSettings.UpdatedVersion = update;
                            Program.StatusEventNotifyer.SignalNewEvent();
                        }
                    }
                    catch
                    {
                    }
                }

                if (m_download)
                {
                    lock(m_lock)
                        m_download = false;

                    var v = Program.DataConnection.ApplicationSettings.UpdatedVersion;
                    if (v != null)
                    {
                        ThreadState = UpdatePollerStates.Downloading;
                        Program.StatusEventNotifyer.SignalNewEvent();

                        if (Program.UpdateManager.DownloadAndUnpackUpdate(v))
                            Program.StatusEventNotifyer.SignalNewEvent();
                    }
                }

                if (ThreadState != UpdatePollerStates.Waiting)
                {
                    ThreadState = UpdatePollerStates.Waiting;
                    Program.StatusEventNotifyer.SignalNewEvent();
                }

                var waitTime = nextCheck - DateTime.UtcNow;
                if (waitTime.TotalSeconds < 5)
                    waitTime = TimeSpan.FromSeconds(5);
                m_waitSignal.WaitOne(waitTime, true);
            }   
        }
    }
}

