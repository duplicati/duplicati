// Copyright (C) 2024, The Duplicati Team
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
using System.Linq;
using System.Threading;
using Duplicati.Library.RestAPI;
using Duplicati.Server.Serialization;

namespace Duplicati.Server
{
    /// <summary>
    /// The thread that checks on the update server if new versions are available
    /// </summary>
    public class UpdatePollThread
    {
        private readonly Thread m_thread;
        private volatile bool m_terminated = false;
        private volatile bool m_forceCheck = false;
        private readonly object m_lock = new object();
        private readonly AutoResetEvent m_waitSignal;
        private double m_downloadProgress;

        public bool IsUpdateRequested { get; private set; } = false;

        public UpdatePollerStates ThreadState { get; private set; }
        public double DownloadProgess
        {
            get { return m_downloadProgress; }

            private set
            {
                var oldv = m_downloadProgress;
                m_downloadProgress = value;
                if ((int)(oldv * 100) != (int)(value * 100))
                    FIXMEGlobal.StatusEventNotifyer.SignalNewEvent();
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
            lock (m_lock)
            {
                m_forceCheck = true;
                m_waitSignal.Set();
            }
        }

        public void Terminate()
        {
            lock (m_lock)
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
                var nextCheck = FIXMEGlobal.DataConnection.ApplicationSettings.NextUpdateCheck;

                var maxcheck = TimeSpan.FromDays(7);
                try
                {
                    maxcheck = Library.Utility.Timeparser.ParseTimeSpan(FIXMEGlobal.DataConnection.ApplicationSettings.UpdateCheckInterval);
                }
                catch
                {
                }

                // If we have some weirdness, just check now
                if (nextCheck - DateTime.UtcNow > maxcheck)
                    nextCheck = DateTime.UtcNow - TimeSpan.FromSeconds(1);

                if (nextCheck < DateTime.UtcNow || m_forceCheck)
                {
                    lock (m_lock)
                        m_forceCheck = false;

                    ThreadState = UpdatePollerStates.Checking;
                    FIXMEGlobal.StatusEventNotifyer.SignalNewEvent();

                    DateTime started = DateTime.UtcNow;
                    FIXMEGlobal.DataConnection.ApplicationSettings.LastUpdateCheck = started;
                    nextCheck = FIXMEGlobal.DataConnection.ApplicationSettings.NextUpdateCheck;

                    Library.AutoUpdater.ReleaseType rt;
                    if (!Enum.TryParse<Library.AutoUpdater.ReleaseType>(FIXMEGlobal.DataConnection.ApplicationSettings.UpdateChannel, true, out rt))
                        rt = Duplicati.Library.AutoUpdater.ReleaseType.Unknown;

                    // Choose the default channel in case we have unknown
                    rt = rt == Duplicati.Library.AutoUpdater.ReleaseType.Unknown ? Duplicati.Library.AutoUpdater.AutoUpdateSettings.DefaultUpdateChannel : rt;

                    try
                    {
                        var update = Duplicati.Library.AutoUpdater.UpdaterManager.CheckForUpdate(rt);
                        if (update != null)
                            FIXMEGlobal.DataConnection.ApplicationSettings.UpdatedVersion = update;
                    }
                    catch
                    {
                    }

                    // It could be that we have registered an update from a more unstable channel, 
                    // but the user has switched to a more stable channel.
                    // In that case we discard the old update to avoid offering it.
                    if (FIXMEGlobal.DataConnection.ApplicationSettings.UpdatedVersion != null)
                    {
                        Library.AutoUpdater.ReleaseType updatert;
                        var updatertstring = FIXMEGlobal.DataConnection.ApplicationSettings.UpdatedVersion.ReleaseType;
                        if (string.Equals(updatertstring, "preview", StringComparison.OrdinalIgnoreCase))
                            updatertstring = Library.AutoUpdater.ReleaseType.Experimental.ToString();

                        if (!Enum.TryParse<Library.AutoUpdater.ReleaseType>(updatertstring, true, out updatert))
                            updatert = Duplicati.Library.AutoUpdater.ReleaseType.Nightly;

                        if (updatert == Duplicati.Library.AutoUpdater.ReleaseType.Unknown)
                            updatert = Duplicati.Library.AutoUpdater.ReleaseType.Nightly;

                        if (updatert > rt)
                            FIXMEGlobal.DataConnection.ApplicationSettings.UpdatedVersion = null;
                    }

                    var updatedinfo = FIXMEGlobal.DataConnection.ApplicationSettings.UpdatedVersion;
                    if (updatedinfo != null && Duplicati.Library.AutoUpdater.UpdaterManager.TryParseVersion(updatedinfo.Version) > System.Reflection.Assembly.GetExecutingAssembly().GetName().Version)
                    {
                        var package = updatedinfo.FindPackage();

                        FIXMEGlobal.DataConnection.RegisterNotification(
                                    NotificationType.Information,
                                    "Found update",
                                    updatedinfo.Displayname,
                                    null,
                                    null,
                                    "update:new",
                                    null,
                                    "NewUpdateFound",
                                    null,
                                    (self, all) =>
                                    {
                                        return all.FirstOrDefault(x => x.Action == "update:new") ?? self;
                                    }
                                );
                    }
                }

                DownloadProgess = 0;

                if (ThreadState != UpdatePollerStates.Waiting)
                {
                    ThreadState = UpdatePollerStates.Waiting;
                    FIXMEGlobal.StatusEventNotifyer.SignalNewEvent();
                }

                var waitTime = nextCheck - DateTime.UtcNow;

                // Guard against spin-loop
                if (waitTime.TotalSeconds < 5)
                    waitTime = TimeSpan.FromSeconds(5);

                // Guard against year-long waits
                // A re-check does not cause an update check
                if (waitTime.TotalDays > 1)
                    waitTime = TimeSpan.FromDays(1);

                m_waitSignal.WaitOne(waitTime, true);
            }
        }
    }
}

