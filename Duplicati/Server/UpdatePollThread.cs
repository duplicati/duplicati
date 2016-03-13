//  Copyright (C) 2015, The Duplicati Team

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
        private double m_downloadProgress;

        public UpdatePollerStates ThreadState { get; private set; }
        public double DownloadProgess
        {
            get { return m_downloadProgress ; }

            private set
            {
                var oldv = m_downloadProgress;
                m_downloadProgress = value;
                if ((int)(oldv * 100) != (int)(value * 100))
                    Program.StatusEventNotifyer.SignalNewEvent();
            }
        }
        
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
                m_forceCheck = true;
                m_download = true;
                m_waitSignal.Set();
            }
        }

        public void ActivateUpdate()
        {
            if (Duplicati.Library.AutoUpdater.UpdaterManager.SetRunUpdate())
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

                    Library.AutoUpdater.ReleaseType rt;
                    if (!Enum.TryParse<Library.AutoUpdater.ReleaseType>(Program.DataConnection.ApplicationSettings.UpdateChannel, true, out rt))
                        rt = Duplicati.Library.AutoUpdater.ReleaseType.Unknown;

                    // Choose the default channel in case we have unknown
                    rt = rt == Duplicati.Library.AutoUpdater.ReleaseType.Unknown ? Duplicati.Library.AutoUpdater.AutoUpdateSettings.DefaultUpdateChannel : rt;

                    try
                    {                        
                        var update = Duplicati.Library.AutoUpdater.UpdaterManager.CheckForUpdate(rt);
                        if (update != null)
                            Program.DataConnection.ApplicationSettings.UpdatedVersion = update;
                    }
                    catch
                    {
                    }

                    // It could be that we have registered an update from a more unstable channel, 
                    // but the user has switched to a more stable channel.
                    // In that case we discard the old update to avoid offering it.
                    if (Program.DataConnection.ApplicationSettings.UpdatedVersion != null)
                    {
                        Library.AutoUpdater.ReleaseType updatert;
                        var updatertstring = Program.DataConnection.ApplicationSettings.UpdatedVersion.ReleaseType;
                        if (string.Equals(updatertstring, "preview", StringComparison.OrdinalIgnoreCase))
                            updatertstring = Library.AutoUpdater.ReleaseType.Experimental.ToString();
                        
                        if (!Enum.TryParse<Library.AutoUpdater.ReleaseType>(updatertstring, true, out updatert))
                            updatert = Duplicati.Library.AutoUpdater.ReleaseType.Nightly;

                        if (updatert == Duplicati.Library.AutoUpdater.ReleaseType.Unknown)
                            updatert = Duplicati.Library.AutoUpdater.ReleaseType.Nightly;
                        
                        if (updatert > rt)
                            Program.DataConnection.ApplicationSettings.UpdatedVersion = null;
                    }

                    if (Program.DataConnection.ApplicationSettings.UpdatedVersion != null && Duplicati.Library.AutoUpdater.UpdaterManager.TryParseVersion(Program.DataConnection.ApplicationSettings.UpdatedVersion.Version) > System.Reflection.Assembly.GetExecutingAssembly().GetName().Version)
                    {
                        Program.DataConnection.RegisterNotification(
                                    NotificationType.Information,
                                    "Found update",
                                    Program.DataConnection.ApplicationSettings.UpdatedVersion.Displayname,
                                    null,
                                    null,
                                    "update:new",
                                    (self, all) => {
                                        return all.Where(x => x.Action == "update:new").FirstOrDefault() ?? self;
                                    }
                                );
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

                        if (Duplicati.Library.AutoUpdater.UpdaterManager.DownloadAndUnpackUpdate(v, (pg) => { DownloadProgess = pg; }))
                            Program.StatusEventNotifyer.SignalNewEvent();
                    }
                }

                DownloadProgess = 0;

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

