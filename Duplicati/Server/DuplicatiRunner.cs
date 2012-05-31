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
using Duplicati.Datamodel;
using Duplicati.Server.Serialization;

namespace Duplicati.Server
{
    /// <summary>
    /// This class translates tasks into Duplicati calls, and executes them
    /// </summary>
    public class DuplicatiRunner : Duplicati.Library.Main.LiveControl.ILiveControl
    {
        public class ProgressEventData : IProgressEventData
        {
            public DuplicatiOperation Operation { get; set; }
            public DuplicatiOperationMode Mode { get; set; }
            public string Message { get; set; }
            public string SubMessage { get; set; }
            public int Progress { get; set; }
            public int SubProgress { get; set; }
        }

        public delegate void ResultEventDelegate(RunnerResult result, string parsedMessage, string message);
        public event ResultEventDelegate ResultEvent;

        public delegate void ProgressEventDelegate(DuplicatiOperation operation, RunnerState state, string message, string submessage, int progress, int subprogress);
        public event ProgressEventDelegate ProgressEvent;

        private const int PERCENT_PR_EXTRA_OPERATION = 2;
        private int m_extraOperations = 0;

        private ProgressEventData m_lastEvent = new ProgressEventData();

        private object m_lock = new object();
        private CloseReason m_stopReason = CloseReason.None;
        private Library.Main.LiveControl.ILiveControl m_currentBackupControlInterface;
        private bool m_isAborted = false;

        private RunnerState m_currentRunnerState = RunnerState.Suspended;

        public DuplicatiRunner()
        {
            ProgressEvent += new ProgressEventDelegate(DuplicatiRunner_ProgressEvent);
        }

        void DuplicatiRunner_ProgressEvent(DuplicatiOperation operation, RunnerState state, string message, string submessage, int progress, int subprogress)
        {
            m_currentRunnerState = state;
        }

        public void ExecuteTask(IDuplicityTask task)
        {
            Dictionary<string, string> options = new Dictionary<string,string>();

            //Set the log level to be that of the GUI
            options["log-level"] = Duplicati.Library.Logging.Log.LogLevel.ToString();

            string destination = task.GetConfiguration(options);

            string results = "";
            string parsedMessage = "";
            m_isAborted = false;

            try
            {
                //TODO: Its a bit dirty to set the options after creating the instance
                using (Duplicati.Library.Main.Interface i = new Duplicati.Library.Main.Interface(destination, options))
                {
                    lock (m_lock)
                    {
                        m_stopReason = CloseReason.None;
                        m_currentBackupControlInterface = i;
                    }

                    SetupControlInterface();

                    i.OperationProgress += new Duplicati.Library.Main.OperationProgressEvent(Duplicati_OperationProgress);
                    i.MetadataReport += new Library.Main.MetadataReportDelegate(new MetadataReportCapture(task).Duplicati_MetadataReport);

                    switch (task.TaskType)
                    {
                        case DuplicityTaskType.FullBackup:
                        case DuplicityTaskType.IncrementalBackup:
                            {
                                //Activate auto-cleanup
                                options["auto-cleanup"] = "";
                                options["force"] = "";
                                if (task.Schedule.Task.KeepFull > 0)
                                    m_extraOperations++;
                                if (!string.IsNullOrEmpty(task.Schedule.Task.KeepTime))
                                    m_extraOperations++;

                                Library.Utility.TempFolder tf = null;
                                try
                                {
                                    if (ProgressEvent != null)
                                        ProgressEvent(DuplicatiOperation.Backup, RunnerState.Started, task.Schedule.Name, "", 0, -1);

                                    if (task.Task.IncludeSetup)
                                    {
                                        //Make a copy of the current database
                                        tf = new Duplicati.Library.Utility.TempFolder();
                                        string filename = System.IO.Path.Combine(tf, System.IO.Path.GetFileName(Program.DatabasePath));

                                        System.IO.File.Copy(Program.DatabasePath, filename, true);
                                        using (System.Data.IDbConnection con = (System.Data.IDbConnection)Activator.CreateInstance(SQLiteLoader.SQLiteConnectionType))
                                        {
                                            con.ConnectionString = "Data Source=" + filename;

                                            //Open the database, handle any encryption issues automatically
                                            Program.OpenDatabase(con);

                                            using (System.Data.IDbCommand cmd = con.CreateCommand())
                                            {
                                                //Remove all log data to minimize the size of the database
                                                cmd.CommandText = "DELETE FROM CommandQueue;";
                                                cmd.ExecuteNonQuery();
                                                cmd.CommandText = "DELETE FROM Log;";
                                                cmd.ExecuteNonQuery();
                                                cmd.CommandText = "DELETE FROM LogBlob;";
                                                cmd.ExecuteNonQuery();

                                                //Free up unused space
                                                cmd.CommandText = "VACUUM;";
                                                cmd.ExecuteNonQuery();
                                            }
                                        }

                                        options["signature-control-files"] = filename;
                                    }

                                    options["full-if-sourcefolder-changed"] = "";

                                    List<KeyValuePair<bool, string>> filters = new List<KeyValuePair<bool, string>>();
                                    string[] sourceFolders = DynamicSetupHelper.GetSourceFolders(task.Task, new ApplicationSettings(task.Task.DataParent), filters);

                                    if (options.ContainsKey("filter"))
                                        filters.AddRange(Library.Utility.FilenameFilter.DecodeFilter(options["filter"]));

                                    options["filter"] = Library.Utility.FilenameFilter.EncodeAsFilter(filters);

                                    //At this point we register the backup as being in progress
                                    ((FullOrIncrementalTask)task).WriteBackupInProgress(Strings.DuplicatiRunner.ShutdownWhileBackupInprogress);

                                    results = i.Backup(sourceFolders);
                                }
                                finally
                                {
                                    if (tf != null)
                                        tf.Dispose();

                                    if (ProgressEvent != null)
                                        ProgressEvent(DuplicatiOperation.Backup, RunnerState.Stopped, task.Schedule.Name, "", 100, -1);
                                }
                                break;
                            }
                        case DuplicityTaskType.ListBackups:

                            List<string> res = new List<string>();
                            foreach (Duplicati.Library.Main.ManifestEntry be in i.GetBackupSets())
                            {
                                res.Add(be.Time.ToString());
                                foreach (Duplicati.Library.Main.ManifestEntry bei in be.Incrementals)
                                    res.Add(bei.Time.ToString());
                            }

                            (task as ListBackupsTask).Backups = res.ToArray();
                            break;
                        case DuplicityTaskType.ListBackupEntries:
                            (task as ListBackupEntriesTask).Backups = i.GetBackupSets();
                            break;
                        case DuplicityTaskType.ListFiles:
                            (task as ListFilesTask).Files = i.ListCurrentFiles();
                            break;
                        case DuplicityTaskType.ListSourceFolders:
                            (task as ListSourceFoldersTask).Files = new List<string>(i.ListSourceFolders() ?? new string[0]);
                            break;
                        case DuplicityTaskType.ListActualFiles:
                            (task as ListActualFilesTask).Files = i.ListActualSignatureFiles();
                            break;
                        case DuplicityTaskType.RemoveAllButNFull:
                            results = i.DeleteAllButNFull();
                            break;
                        case DuplicityTaskType.RemoveOlderThan:
                            results = i.DeleteOlderThan();
                            break;
                        case DuplicityTaskType.Restore:
                            options["file-to-restore"] = ((RestoreTask)task).SourceFiles;
                            if (options.ContainsKey("filter"))
                                options.Remove("filter");

                            try
                            {
                                if (ProgressEvent != null)
                                    ProgressEvent(DuplicatiOperation.Restore, RunnerState.Started, task.Schedule.Name, "", 0, -1);
                                results = i.Restore(task.LocalPath.Split(System.IO.Path.PathSeparator));
                            }
                            finally
                            {
                                if (ProgressEvent != null)
                                    ProgressEvent(DuplicatiOperation.Restore, RunnerState.Stopped, task.Schedule.Name, "", 100, -1);
                            }
                            break;

                        case DuplicityTaskType.RestoreSetup:
                            i.RestoreControlFiles(task.LocalPath);
                            break;
                        default:
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                while (ex is System.Reflection.TargetInvocationException && ex.InnerException != null)
                    ex = ex.InnerException;

                if (ex is System.Threading.ThreadAbortException)
                {
                    m_isAborted = true;
                    System.Threading.Thread.ResetAbort();
                }
                else if (ex is Library.Main.LiveControl.ExecutionStoppedException)
                    m_isAborted = true;

                if (m_isAborted && m_stopReason != CloseReason.None)
                {
                    //If the user has stopped the backup for some reason, write a nicer message
                    switch (m_stopReason)
                    {
                        case CloseReason.ApplicationExitCall:
                            parsedMessage = Strings.DuplicatiRunner.ApplicationExitLogMesssage;
                            break;
                        case CloseReason.TaskManagerClosing:
                            parsedMessage = Strings.DuplicatiRunner.TaskManagerCloseMessage;
                            break;
                        case CloseReason.UserClosing:
                            parsedMessage = Strings.DuplicatiRunner.UserClosingMessage;
                            break;
                        case CloseReason.WindowsShutDown:
                            parsedMessage = Strings.DuplicatiRunner.WindowsShutdownMessage;
                            break;
                        default:
                            parsedMessage = string.Format(Strings.DuplicatiRunner.OtherAbortMessage, m_stopReason);
                            break;
                    }

                    if (task.Schedule != null)
                    {
                        //If the application is going down, the backup should resume on next launch
                        switch (m_stopReason)
                        {
                            case CloseReason.ApplicationExitCall:
                            case CloseReason.TaskManagerClosing:
                            case CloseReason.WindowsShutDown:
                                task.Schedule.ScheduledRunFailed();
                                break;
                        }
                    }
                }
                else
                    parsedMessage = string.Format(Strings.DuplicatiRunner.ErrorMessage, ex.Message);

                results = "Error: " + ex.ToString(); //Don't localize

                while (ex.InnerException != null)
                {
                    ex = ex.InnerException;
                    results += Environment.NewLine + "InnerError: " + ex.ToString(); //Don't localize
                }
                
            }
            finally
            {
                lock (m_lock)
                    m_currentBackupControlInterface = null;
            }

            try
            {
                if (!m_isAborted && (task.TaskType == DuplicityTaskType.FullBackup || task.TaskType == DuplicityTaskType.IncrementalBackup))
                {
                    if (task.Schedule.Task.KeepFull > 0)
                    {
                        m_lastEvent.Progress = 100;
                        m_lastEvent.Message = Strings.DuplicatiRunner.CleaningUpMessage;
                        m_lastEvent.SubMessage = "";
                        m_lastEvent.SubProgress = -1;

                        ReinvokeLastProgressEvent();
                        m_extraOperations--;

                        RemoveAllButNFullTask tmpTask = new RemoveAllButNFullTask(task.Schedule, (int)task.Schedule.Task.KeepFull);
                        tmpTask.Metadata = task.Metadata;
                        ExecuteTask(tmpTask);   
                        results += Environment.NewLine + Strings.DuplicatiRunner.CleanupLogdataHeader + Environment.NewLine + tmpTask.Result;
                    }

                    if (!string.IsNullOrEmpty(task.Schedule.Task.KeepTime))
                    {
                        m_lastEvent.Progress = 100;
                        m_lastEvent.Message = Strings.DuplicatiRunner.CleaningUpMessage;
                        m_lastEvent.SubMessage = "";
                        m_lastEvent.SubProgress = -1;

                        ReinvokeLastProgressEvent();
                        m_extraOperations--;

                        RemoveOlderThanTask tmpTask = new RemoveOlderThanTask(task.Schedule, task.Schedule.Task.KeepTime);
                        tmpTask.Metadata = task.Metadata;
                        ExecuteTask(tmpTask);
                        results += Environment.NewLine + Strings.DuplicatiRunner.CleanupLogdataHeader + Environment.NewLine + tmpTask.Result;
                    }

                    if (task.Schedule.Task.KeepFull > 0 || !string.IsNullOrEmpty(task.Schedule.Task.KeepTime))
                        ReinvokeLastProgressEvent();

                    if (ProgressEvent != null)
                        ProgressEvent(DuplicatiOperation.Backup, RunnerState.Stopped, task.Schedule.Name, "", 100, -1);
                }
            }
            catch (Exception ex)
            {
                results += Environment.NewLine + string.Format(Strings.DuplicatiRunner.CleanupError, ex.Message);
            }

            task.IsAborted = m_isAborted;
            task.Result = results;
            task.RaiseTaskCompleted(results, parsedMessage);

            if (ResultEvent != null && task is FullBackupTask || task is IncrementalBackupTask)
            {
                Log[] logs = Program.DataConnection.GetObjects<Log>("TaskID = ? AND SubAction LIKE ? ORDER BY EndTime DESC", task.Task.ID, "Primary");
                if (logs != null && logs.Length > 0)
                {
                    Datamodel.Log l = logs[0];
                    RunnerResult r = RunnerResult.Error;
                    if (l.ParsedStatus == DuplicatiOutputParser.ErrorStatus)
                        r = RunnerResult.Error;
                    else if (l.ParsedStatus == DuplicatiOutputParser.OKStatus || l.ParsedStatus == DuplicatiOutputParser.NoChangedFiles)
                        r = RunnerResult.OK;
                    else if (l.ParsedStatus == DuplicatiOutputParser.PartialStatus)
                        r = RunnerResult.Partial;
                    else if (l.ParsedStatus == DuplicatiOutputParser.WarningStatus)
                        r = RunnerResult.Warning;

                    if (ResultEvent != null)
                        ResultEvent(r, parsedMessage, results);
                }
            }

            if (task.Schedule != null && !m_isAborted)
            {
                //Write metadata to schedule prior to commit
                if (task.Metadata != null)
                {
                    //Backups will replace all entries in the metadata table
                    if (task is FullOrIncrementalTask)
                    {
                        //Make a copy so we are sure we return the dictionary too
                        Dictionary<string, string> tmp = new Dictionary<string, string>(task.Metadata);

                        //Update existing and remove unused
                        foreach (string k in new List<string>(task.Schedule.MetadataLookup.Values))
                            if (tmp.ContainsKey(k))
                            {
                                task.Schedule.MetadataLookup[k] = tmp[k];
                                tmp.Remove(k);
                            }
                            else
                                task.Schedule.MetadataLookup.Remove(k);

                        //Add new elements
                        foreach (KeyValuePair<string, string> kv in tmp)
                            task.Schedule.MetadataLookup[kv.Key] = kv.Value;

                        //Add a value stating that we have run a backup now
                        task.Schedule.MetadataLookup["last-backup-completed-time"] = DateTime.Now.ToUniversalTime().ToString("u");
                    }
                    else
                    {
                        //Any other task will just update the table values
                        foreach (KeyValuePair<string, string> kv in task.Metadata)
                            task.Schedule.MetadataLookup[kv.Key] = kv.Value;
                    }
                }

                task.Schedule.ScheduledRunCompleted(); //Register as completed if not aborted
            }

            Program.EventNotifyer.SignalNewEvent();
        }

        private class MetadataReportCapture
        {
            private IDuplicityTask Task;
            public MetadataReportCapture(IDuplicityTask task)
            {
                this.Task = task;
            }

            public void Duplicati_MetadataReport(IDictionary<string, string> metadata)
            {
                if (Task.Metadata == null)
                    Task.Metadata = new Dictionary<string, string>();

                foreach (KeyValuePair<string, string> kvp in metadata)
                    Task.Metadata[kvp.Key] = kvp.Value;
            }

        }


        void Duplicati_OperationProgress(Duplicati.Library.Main.Interface caller, Duplicati.Library.Main.DuplicatiOperation operation, Duplicati.Library.Main.DuplicatiOperationMode specificmode, int progress, int subprogress, string message, string submessage)
        {
            Duplicati_OperationProgress(caller, EnumConverter.Convert<DuplicatiOperation>(operation), EnumConverter.Convert<DuplicatiOperationMode>(specificmode), progress, subprogress, message, submessage);
        }

        void Duplicati_OperationProgress(Duplicati.Library.Main.Interface caller, DuplicatiOperation operation, DuplicatiOperationMode specificmode, int progress, int subprogress, string message, string submessage)
        {
            m_lastEvent.Operation = operation;
            m_lastEvent.Mode = specificmode;
            m_lastEvent.Progress = progress;
            m_lastEvent.SubProgress = subprogress;
            m_lastEvent.Message = message;
            m_lastEvent.SubMessage = submessage;

            //If there are extra operations, reserve some space for it by reducing the displayed progress
            if (m_extraOperations > 0 && progress > 0)
                progress = (int)((m_lastEvent.Progress / 100.0) * (100 - (m_extraOperations * PERCENT_PR_EXTRA_OPERATION)));

            if (ProgressEvent != null)
                try { ProgressEvent(operation, RunnerState.Running, message, submessage, progress, subprogress); }
                catch { }
        }

        public void ReinvokeLastProgressEvent()
        {
            Duplicati_OperationProgress(null, m_lastEvent.Operation, m_lastEvent.Mode, m_lastEvent.Progress, m_lastEvent.SubProgress, m_lastEvent.Message, m_lastEvent.SubMessage );
        }

        private void PerformBackup(Schedule schedule, bool forceFull, string fullAfter)
        {
            if (forceFull)
                ExecuteTask(new FullBackupTask(schedule));
            else
                ExecuteTask(new IncrementalBackupTask(schedule, fullAfter));
        }

        public void Restore(Schedule schedule, DateTime when, string where)
        {
            ExecuteTask(new RestoreTask(schedule, where, when));
        }

        public string[] ListBackups(Schedule schedule)
        {
            ListBackupsTask task = new ListBackupsTask(schedule);
            ExecuteTask(task);

            if (task.IsAborted)
                return null;
            
            return task.Backups;
        }

        public List<Duplicati.Library.Main.ManifestEntry> ListBackupEntries(Schedule schedule)
        {
            ListBackupEntriesTask task = new ListBackupEntriesTask(schedule);
            ExecuteTask(task);

            if (task.IsAborted)
                return null;

            if (task.Result.StartsWith("Error:"))
                throw new Exception(task.Result);

            return task.Backups;
        }

        public List<KeyValuePair<Library.Main.RSync.RSyncDir.PatchFileType, string>> ListActualFiles(Schedule schedule, DateTime when)
        {
            ListActualFilesTask task = new ListActualFilesTask(schedule, when);
            ExecuteTask(task);
            
            if (task.IsAborted)
                return null;
            
            if (task.Result.StartsWith("Error:"))
                throw new Exception(task.Result);
            
            return task.Files;
        }

        public IList<string> ListFiles(Schedule schedule, DateTime when)
        {
            ListFilesTask task = new ListFilesTask(schedule, when);
            ExecuteTask(task);

            if (task.IsAborted)
                return null;

            if (task.Result.StartsWith("Error:"))
                throw new Exception(task.Result);
            return task.Files;
        }

        public IList<string> ListSourceFolders(Schedule schedule, DateTime when)
        {
            ListSourceFoldersTask task = new ListSourceFoldersTask(schedule, when);
            ExecuteTask(task);
            
            if (task.IsAborted)
                return null;

            if (task.Result.StartsWith("Error:"))
                throw new Exception(task.Result);
            
            return task.Files;
        }

        public void IncrementalBackup(Schedule schedule)
        {
            PerformBackup(schedule, false, null);
        }

        public void FullBackup(Schedule schedule)
        {
            PerformBackup(schedule, true, null);
        }

        /// <summary>
        /// Gets a value indicating if the last run was aborted
        /// </summary>
        public bool IsAborted { get { return m_isAborted; } }

        /// <summary>
        /// Function used to apply settings to a new interface
        /// </summary>
        private void SetupControlInterface()
        {
            //Copy the values to avoid thread race problems
            System.Threading.ThreadPriority? priority = Program.LiveControl.ThreadPriority;
            long? uploadLimit = Program.LiveControl.UploadLimit;
            long? downloadLimit = Program.LiveControl.DownloadLimit;

            if (priority != null)
                m_currentBackupControlInterface.SetThreadPriority(priority.Value);
            if (uploadLimit != null)
                m_currentBackupControlInterface.SetUploadLimit(uploadLimit.Value.ToString() + "b");
            if (downloadLimit != null)
                m_currentBackupControlInterface.SetDownloadLimit(downloadLimit.Value.ToString() + "b");
        }

        public void Pause()
        {
            lock (m_lock)
                if (m_currentBackupControlInterface != null)
                    m_currentBackupControlInterface.Pause();
        }

        public void Resume()
        {
            lock (m_lock)
                if (m_currentBackupControlInterface != null)
                    m_currentBackupControlInterface.Resume();
        }

        public RunnerState CurrentState { get { return m_currentRunnerState; } }
        public ProgressEventData LastEvent { get { return m_lastEvent; } }

        public void Stop()
        {
            Stop(CloseReason.None);
        }

        public void Stop(CloseReason reason)
        {
            lock (m_lock)
                if (m_currentBackupControlInterface != null)
                {
                    m_stopReason = reason;
                    if (m_currentBackupControlInterface.IsStopRequested)
                        m_currentBackupControlInterface.Terminate();
                    else
                        m_currentBackupControlInterface.Stop();
                }
        }

        public void Terminate(CloseReason reason)
        {
            lock (m_lock)
                if (m_currentBackupControlInterface != null)
                {
                    m_stopReason = reason;
                    m_currentBackupControlInterface.Terminate();
                }
        }

        public void Terminate()
        {
            Terminate(CloseReason.None);
        }

        public bool IsStopRequested
        {
            get 
            { 
                lock (m_lock)
                    if (m_currentBackupControlInterface != null)
                        return m_currentBackupControlInterface.IsStopRequested;
                    else
                        return false;
            }
        }

        public void SetUploadLimit(string limit)
        {
            lock (m_lock)
                if (m_currentBackupControlInterface != null)
                    m_currentBackupControlInterface.SetUploadLimit(limit);
        }

        public void SetDownloadLimit(string limit)
        {
            lock (m_lock)
                if (m_currentBackupControlInterface != null)
                    m_currentBackupControlInterface.SetDownloadLimit(limit);
        }

        public void SetThreadPriority(System.Threading.ThreadPriority priority)
        {
            lock (m_lock)
                if (m_currentBackupControlInterface != null)
                    m_currentBackupControlInterface.SetThreadPriority(priority);
        }

        public void UnsetThreadPriority()
        {
            lock (m_lock)
                if (m_currentBackupControlInterface != null)
                    m_currentBackupControlInterface.UnsetThreadPriority();
        }
    }
}
