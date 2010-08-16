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
using Duplicati.Datamodel;
using Duplicati.Library.Main;

namespace Duplicati.GUI
{
    /// <summary>
    /// This class translates tasks into Duplicati calls, and executes them
    /// </summary>
    public class DuplicatiRunner : Duplicati.Library.Main.LiveControl.ILiveControl
    {
        public enum RunnerState
        {
            Started,
            Suspended,
            Running,
            Stopped,
        }

        public delegate void DuplicatiRunnerProgress(DuplicatiOperation operation, RunnerState state, string message, string submessage, int progress, int subprogress);
        public event DuplicatiRunnerProgress DuplicatiProgress;

        private DuplicatiOperation m_lastPGOperation;
        private int m_lastPGProgress;
        private int m_lastPGSubprogress;
        private string m_lastPGmessage;
        private string m_lastPGSubmessage;

        private object m_lock = new object();
        private Library.Main.LiveControl.ILiveControl m_currentBackupControlInterface;

        public void ExecuteTask(IDuplicityTask task)
        {
            Dictionary<string, string> options = new Dictionary<string,string>();
            string destination = task.GetConfiguration(options);

            string results = "";

            try
            {

                switch (task.TaskType)
                {
                    case DuplicityTaskType.FullBackup:
                    case DuplicityTaskType.IncrementalBackup:
                        {
                            //Activate auto-cleanup
                            options["auto-cleanup"] = "";
                            options["force"] = "";

                            Library.Core.TempFolder tf = null;
                            try
                            {
                                if (DuplicatiProgress != null)
                                    DuplicatiProgress(DuplicatiOperation.Backup, RunnerState.Started, task.Schedule.Name, "", 0, -1);

                                if (task.Task.IncludeSetup)
                                {
                                    //Make a copy of the current database
                                    tf = new Duplicati.Library.Core.TempFolder();
                                    string filename = System.IO.Path.Combine(tf, System.IO.Path.GetFileName(Program.DatabasePath));

                                    System.IO.File.Copy(Program.DatabasePath, filename, true);
                                    using (System.Data.IDbConnection con = (System.Data.IDbConnection)Activator.CreateInstance(SQLiteLoader.SQLiteConnectionType))
                                    {
                                        con.ConnectionString = "Data Source=" + filename;
                                        con.Open();

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
                                        }
                                    }

                                    options["signature-control-files"] = filename;
                                }

                                options["full-if-sourcefolder-changed"] = "";

                                List<KeyValuePair<bool, string>> filters = new List<KeyValuePair<bool, string>>();
                                string[] sourceFolders = DynamicSetupHelper.GetSourceFolders(task.Task, new ApplicationSettings(task.Task.DataParent), filters);

                                if (options.ContainsKey("filter"))
                                    filters.AddRange(Library.Core.FilenameFilter.DecodeFilter(options["filter"]));

                                options["filter"] = Library.Core.FilenameFilter.EncodeAsFilter(filters);

                                using (Interface i = new Interface(destination, options))
                                {
                                    try
                                    {
                                        lock (m_lock)
                                            m_currentBackupControlInterface = i;
                                        
                                        SetupControlInterface();

                                        i.OperationProgress += new OperationProgressEvent(Duplicati_OperationProgress);
                                        results = i.Backup(sourceFolders);
                                    }
                                    finally
                                    {
                                        lock (m_lock)
                                            m_currentBackupControlInterface = null;
                                    }
                                }
                            }
                            finally
                            {
                                if (tf != null)
                                    tf.Dispose();

                                if (DuplicatiProgress != null)
                                    DuplicatiProgress(DuplicatiOperation.Backup, RunnerState.Stopped, task.Schedule.Name, "", 100, -1);
                            }
                            break;
                        }
                    case DuplicityTaskType.ListBackups:

                        List<string> res = new List<string>();
                        foreach (ManifestEntry be in Interface.ParseFileList(destination, options))
                        {
                            res.Add(be.Time.ToString());
                            foreach (ManifestEntry bei in be.Incrementals)
                                res.Add(bei.Time.ToString());
                        }

                        (task as ListBackupsTask).Backups = res.ToArray();
                        break;
                    case DuplicityTaskType.ListBackupEntries:
                        (task as ListBackupEntriesTask).Backups = Interface.ParseFileList(destination, options);
                        break;
                    case DuplicityTaskType.ListFiles:
                        (task as ListFilesTask).Files = Interface.ListContent(destination, options);
                        break;
                    case DuplicityTaskType.ListSourceFolders:
                        {
                            string[] tmp = Interface.ListSourceFolders(destination, options);
                            (task as ListSourceFoldersTask).Files = new List<string>(tmp ?? new string[0]);
                        }
                        break;
                    case DuplicityTaskType.ListActualFiles:
                        (task as ListActualFilesTask).Files = Interface.ListActualSignatureFiles(destination, options);
                        break;
                    case DuplicityTaskType.RemoveAllButNFull:
                        results = Interface.DeleteAllButNFull(destination, options);
                        break;
                    case DuplicityTaskType.RemoveOlderThan:
                        results = Interface.DeleteOlderThan(destination, options);
                        break;
                    case DuplicityTaskType.Restore:
                        options["file-to-restore"] = ((RestoreTask)task).SourceFiles;
                        if (options.ContainsKey("filter"))
                            options.Remove("filter");

                        using (Interface i = new Interface(destination, options))
                        {
                            try
                            {
                                lock (m_lock)
                                    m_currentBackupControlInterface = i;

                                SetupControlInterface();

                                if (DuplicatiProgress != null)
                                    DuplicatiProgress(DuplicatiOperation.Restore, RunnerState.Started, task.Schedule.Name, "", 0, -1);
                                i.OperationProgress += new OperationProgressEvent(Duplicati_OperationProgress);
                                results = i.Restore(task.LocalPath.Split(System.IO.Path.PathSeparator));
                            }
                            finally
                            {
                                lock (m_lock)
                                    m_currentBackupControlInterface = null;

                                if (DuplicatiProgress != null)
                                    DuplicatiProgress(DuplicatiOperation.Restore, RunnerState.Stopped, task.Schedule.Name, "", 100, -1);
                            }
                        }
                        break;

                    case DuplicityTaskType.RestoreSetup:
                        Interface.RestoreControlFiles(destination, task.LocalPath, options);
                        break;
                    default:
                        return;
                }
            }
            catch (Exception ex)
            {
                //TODO: Extract ex.Message and save it in seperate field in the database
                if (ex is System.Threading.ThreadAbortException)
                    System.Threading.Thread.ResetAbort();

                while (ex is System.Reflection.TargetInvocationException && ex.InnerException != null)
                    ex = ex.InnerException;

                results = "Error: " + ex.ToString(); //Don't localize

                while (ex.InnerException != null)
                {
                    ex = ex.InnerException;
                    results += Environment.NewLine + "InnerError: " + ex.ToString(); //Don't localize
                }
            }

            try
            {
                if (task.TaskType == DuplicityTaskType.FullBackup || task.TaskType == DuplicityTaskType.IncrementalBackup)
                {
                    if (task.Schedule.Task.KeepFull > 0)
                    {
                        RemoveAllButNFullTask tmpTask = new RemoveAllButNFullTask(task.Schedule, (int)task.Schedule.Task.KeepFull);
                        ExecuteTask(tmpTask);
                        results += Environment.NewLine + Strings.DuplicatiRunner.CleanupLogdataHeader + Environment.NewLine + tmpTask.Result;
                    }

                    if (!string.IsNullOrEmpty(task.Schedule.Task.KeepTime))
                    {
                        RemoveOlderThanTask tmpTask = new RemoveOlderThanTask(task.Schedule, task.Schedule.Task.KeepTime);
                        ExecuteTask(tmpTask);
                        results += Environment.NewLine + Strings.DuplicatiRunner.CleanupLogdataHeader + Environment.NewLine + tmpTask.Result;
                    }
                }
            }
            catch (Exception ex)
            {
                results += Environment.NewLine + string.Format(Strings.DuplicatiRunner.CleanupError, ex.Message);
            }

            task.Result = results;
            task.RaiseTaskCompleted(results);

            if (task.Schedule != null)
                task.Schedule.ScheduledRunCompleted(); //Register as completed
        }

        void Duplicati_OperationProgress(Interface caller, DuplicatiOperation operation, int progress, int subprogress, string message, string submessage)
        {
            m_lastPGOperation = operation;
            m_lastPGProgress = progress;
            m_lastPGSubprogress = subprogress;
            m_lastPGmessage = message;
            m_lastPGSubmessage = submessage;

            if (DuplicatiProgress != null)
                try { DuplicatiProgress(operation, RunnerState.Running, message, submessage, progress, subprogress); }
                catch { }
        }

        public void ReinvokeLastProgressEvent()
        {
            Duplicati_OperationProgress(null, m_lastPGOperation, m_lastPGProgress, m_lastPGSubprogress, m_lastPGmessage, m_lastPGSubmessage );
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
            return task.Backups;
        }

        public List<ManifestEntry> ListBackupEntries(Schedule schedule)
        {
            ListBackupEntriesTask task = new ListBackupEntriesTask(schedule);
            ExecuteTask(task);
            if (task.Result.StartsWith("Error:"))
                throw new Exception(task.Result);
            return task.Backups;
        }

        public List<KeyValuePair<Library.Main.RSync.RSyncDir.PatchFileType, string>> ListActualFiles(Schedule schedule, DateTime when)
        {
            ListActualFilesTask task = new ListActualFilesTask(schedule, when);
            ExecuteTask(task);
            if (task.Result.StartsWith("Error:"))
                throw new Exception(task.Result);
            return task.Files;
        }

        public IList<string> ListFiles(Schedule schedule, DateTime when)
        {
            ListFilesTask task = new ListFilesTask(schedule, when);
            ExecuteTask(task);
            if (task.Result.StartsWith("Error:"))
                throw new Exception(task.Result);
            return task.Files;
        }

        public IList<string> ListSourceFolders(Schedule schedule, DateTime when)
        {
            ListSourceFoldersTask task = new ListSourceFoldersTask(schedule, when);
            ExecuteTask(task);
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

        public void Stop()
        {
            lock (m_lock)
                if (m_currentBackupControlInterface != null)
                    if (m_currentBackupControlInterface.IsStopRequested)
                        m_currentBackupControlInterface.Terminate();
                    else
                        m_currentBackupControlInterface.Stop();
        }

        public void Terminate()
        {
            lock (m_lock)
                if (m_currentBackupControlInterface != null)
                    m_currentBackupControlInterface.Terminate();
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
