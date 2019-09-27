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
using Duplicati.Library.AutoUpdater;
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
        private volatile bool m_download = false;
        private volatile bool m_forceCheck = false;
        private readonly object m_lock = new object();
        private readonly AutoResetEvent m_waitSignal;
        private double m_downloadProgress;

        public bool IsUpdateRequested { get; private set; } = false;

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

        public bool ActivateUpdate()
        {
            if (Duplicati.Library.AutoUpdater.UpdaterManager.SetRunUpdate())
            {
                UpdateLogger.Log("Start activating updates.");
                // If we are on Windows
                if (Library.Utility.Utility.IsClientWindows)
                {
                    UpdateLogger.Log("Execute updates script.");
                    var lastUpdatesFolderLocation = AppDomain.CurrentDomain.GetData("AUTOUPDATER_LOAD_UPDATE");
                    var runUpdateScriptBat = "run-update-script.bat";

                    // On Windows, execute script file from the Last updates folder location
                    var ex = Library.Utility.Utility.ExecuteCommand(lastUpdatesFolderLocation.ToString(), runUpdateScriptBat);
                    if (null != ex)
                    {
                        UpdateLogger.Log($"Exception occurred on ExecuteCommand: {ex.Message}");
                    }

                    // Wait a few seconds for script to finish running
                    UpdateLogger.Log("Executing updates script. Wait a few seconds for script to finish running");
                    Thread.Sleep(5000);
                }
                // If we are on OSX
                else if (Library.Utility.Utility.IsClientOSX)
                {
                    UpdateLogger.Log("Execute OSX updates script.");
                    var lastUpdatesFolderLocation = AppDomain.CurrentDomain.GetData("AUTOUPDATER_LOAD_UPDATE");
                    UpdateLogger.Log($"lastUpdatesFolderLocation: {lastUpdatesFolderLocation}");
                    var runUpdateScript = "run-update-script_osx.sh";


                    // Execute script file from the Last updates folder location
                    var ex = Library.Utility.Utility.ExecuteCommand(lastUpdatesFolderLocation.ToString(), runUpdateScript, true);
                    if (null != ex)
                    {
                        UpdateLogger.Log($"Exception occurred on ExecuteCommand: {ex.Message}");
                    }

                    // Wait a few seconds for script to finish running
                    UpdateLogger.Log("Executing OSX updates script. Wait a few seconds for script to finish running");
                    Thread.Sleep(5000);
                }
                // If we are on Linux
                else if (Library.Utility.Utility.IsClientLinux)
                {
                    UpdateLogger.Log("Execute linux updates script.");
                    var lastUpdatesFolderLocation = AppDomain.CurrentDomain.GetData("AUTOUPDATER_LOAD_UPDATE");
                    UpdateLogger.Log($"lastUpdatesFolderLocation: {lastUpdatesFolderLocation}");
                    var runUpdateScript = "run-update-script_linux.sh";

                    // Execute script file from the Last updates folder location
                    var ex = Library.Utility.Utility.ExecuteCommand(lastUpdatesFolderLocation.ToString(), runUpdateScript, true);
                    if (null != ex)
                    {
                        UpdateLogger.Log($"Exception occurred on ExecuteCommand: {ex.Message}");
                    }

                    // Wait a few seconds for script to finish running
                    UpdateLogger.Log("Executing linux updates script. Wait a few seconds for script to finish running");
                    Thread.Sleep(5000);
                }

                UpdateLogger.Log("Application Exit Event.");
                IsUpdateRequested = true;
                Program.ApplicationExitEvent.Set();
                return true;
            }
            else
            {
                UpdateLogger.Log("Stop activating updates. Update has not been installed");
                return false;
            }
        }

        private bool DownloadUpdate()
        {
            bool downloadAndUnpackFinished = false;
            lock (m_lock)
                m_download = false;

            var v = Program.DataConnection.ApplicationSettings.UpdatedVersion;
            if (v != null)
            {
                ThreadState = UpdatePollerStates.Downloading;
                Program.StatusEventNotifyer.SignalNewEvent();

                downloadAndUnpackFinished = UpdaterManager.DownloadAndUnpackUpdate(v, (pg) => { DownloadProgess = pg; });
                if (downloadAndUnpackFinished)
                    Program.StatusEventNotifyer.SignalNewEvent();
            }

            return downloadAndUnpackFinished;
        }

        // If tasks are running or scheduled, retry the operation for about 180 seconds until can be safely executed
        private static bool TryExecuteOperation(Func<bool> operationMethod)
        {
            bool done = false;
            int retry = 0;
            int retries = 60;
            bool operationResult = false;

            // Try to execute Operation, wait until resources are available
            while (!done)
            {
                // Cannot execute Operation while task is running or scheduled
                if (Program.WorkThread.CurrentTask == null && Program.WorkThread.CurrentTasks.Count == 0)
                {
                    UpdateLogger.Log($"Executing Operation '{operationMethod.Method.Name}'.");
                    operationResult = operationMethod();
                    done = true;
                }
                else
                {
                    if (retry++ < retries)
                        Thread.Sleep(3000);
                    else
                    {
                        // Give up
                        UpdateLogger.Log($"Cannot execute Operation '{operationMethod.Method.Name}' while task is running or scheduled.");
                        done = true;
                    }
                }
            }

            return operationResult;
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
                UpdateLogger.Log($"Running update pool (at every {Program.DataConnection.ApplicationSettings.UpdateCheckInterval})");
                var nextCheck = Program.DataConnection.ApplicationSettings.NextUpdateCheck;

                var maxcheck = TimeSpan.FromDays(7);
                try
                {
                    maxcheck = Library.Utility.Timeparser.ParseTimeSpan(Program.DataConnection.ApplicationSettings.UpdateCheckInterval);
                }
                catch
                {
                }

                // If we have some weirdness, just check now
                if (nextCheck - DateTime.UtcNow > maxcheck)
                    nextCheck = DateTime.UtcNow - TimeSpan.FromSeconds(1);

                bool autoUpdateCheck = nextCheck < DateTime.UtcNow;
                bool updatePrepareForDownload = false;
                if (autoUpdateCheck || m_forceCheck)
                {
                    UpdateLogger.Log($"Checking updates ({(autoUpdateCheck ? "auto" : "force")}).");
                    lock (m_lock)
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
                        updatePrepareForDownload = true;
                        Program.DataConnection.RegisterNotification(
                                    NotificationType.Information,
                                    "Found update",
                                    Program.DataConnection.ApplicationSettings.UpdatedVersion.Displayname,
                                    null,
                                    null,
                                    "update:new",
                                    null,
                                    "NewUpdateFound",
                                    null,
                                    (self, all) => {
                                        return all.FirstOrDefault(x => x.Action == "update:new") ?? self;
                                    }
                                );
                        UpdateLogger.Log($"Updates {Program.DataConnection.ApplicationSettings.UpdatedVersion.Displayname} found.");
                    }
                }

                bool autoDownloadUpdate = autoUpdateCheck && Program.DataConnection.ApplicationSettings.AutoInstallUpdate && updatePrepareForDownload;
                bool downloadAndUnpackUpdateFinished = false;

                if (autoDownloadUpdate)
                    UpdateLogger.Log($"Auto install update {Program.DataConnection.ApplicationSettings.UpdatedVersion.Displayname}.");


                if (m_download)
                    UpdateLogger.Log($"Manual install update {Program.DataConnection.ApplicationSettings.UpdatedVersion.Displayname}.");

                if (autoDownloadUpdate || m_download)
                {
                    // Do not download another update if an update has been installed
                    if (!UpdaterManager.HasUpdateInstalled)
                    {
                        downloadAndUnpackUpdateFinished = TryExecuteOperation(DownloadUpdate);
                    }
                    else
                    {
                        UpdateLogger.Log("An update has been installed. Cannot download another update.");
                    }
                }

                DownloadProgess = 0;

                if (ThreadState != UpdatePollerStates.Waiting)
                {
                    ThreadState = UpdatePollerStates.Waiting;
                    Program.StatusEventNotifyer.SignalNewEvent();
                }

                if (autoDownloadUpdate && (downloadAndUnpackUpdateFinished || UpdaterManager.HasUpdateInstalled))
                {
                    if (downloadAndUnpackUpdateFinished)
                        UpdateLogger.Log($"Auto activate update {Program.DataConnection.ApplicationSettings.UpdatedVersion.Displayname}.");
                    else if (UpdaterManager.HasUpdateInstalled)
                        UpdateLogger.Log($"Auto activate previous installed update {Program.DataConnection.ApplicationSettings.UpdatedVersion.Displayname}.");

                    TryExecuteOperation(ActivateUpdate);
                }


                var waitTime = nextCheck - DateTime.UtcNow;

                // Guard against spin-loop
                if (waitTime.TotalSeconds < 5)
                    waitTime = TimeSpan.FromSeconds(5);
                
                // Guard against year-long waits
                // A re-check does not cause an update check
                if (waitTime.TotalDays > 1)
                    waitTime = TimeSpan.FromDays(1);

                UpdateLogger.Log($"Update pool next running time: {waitTime}.");

                m_waitSignal.WaitOne(waitTime, true);
            }   
        }
    }
}

