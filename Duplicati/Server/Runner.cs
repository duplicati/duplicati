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
using System.Collections.Generic;
using Duplicati.Server.Serialization;

namespace Duplicati.Server
{
    public static class Runner
    {
        public interface IRunnerData : Duplicati.Server.Serialization.Interface.IQueuedTask
        {
            Duplicati.Server.Serialization.Interface.IBackup Backup { get; }
            IDictionary<string, string> ExtraOptions { get; }
            string[] FilterStrings { get; }
            void Stop();
            void Abort();
            void Pause();
            void Resume();
            void UpdateThrottleSpeed();
            void SetController(Duplicati.Library.Main.Controller controller);
        }
        
        private class RunnerData : IRunnerData
        {
            private static long RunnerTaskID = 1;
            
            public Duplicati.Server.Serialization.DuplicatiOperation Operation { get; internal set; }
            public Duplicati.Server.Serialization.Interface.IBackup Backup { get; internal set; }
            public IDictionary<string, string> ExtraOptions { get; internal set; }
            public string[] FilterStrings { get; internal set; }
            
            public string BackupID { get { return Backup.ID; } }
            public long TaskID { get { return m_taskID; } }
            
            internal Duplicati.Library.Main.Controller Controller { get; set; }

            public void SetController(Duplicati.Library.Main.Controller controller)
            {
                Controller = controller;
            }

            public void Stop()
            {
                var c = Controller;
                if (c != null)
                    c.Stop();
            }

            public void Abort()
            {
                var c = Controller;
                if (c != null)
                    c.Abort();
            }

            public void Pause()
            {
                var c = Controller;
                if (c != null)
                    c.Pause();
            }

            public void Resume()
            {
                var c = Controller;
                if (c != null)
                    c.Resume();
            }

            public long OriginalUploadSpeed { get; set; }
            public long OriginalDownloadSpeed { get; set; }

			public void UpdateThrottleSpeed()
			{
				var controller = this.Controller;
				if (controller == null)
					return;

				var job_upload_throttle = this.OriginalUploadSpeed <= 0 ? long.MaxValue : this.OriginalUploadSpeed;
				var job_download_throttle = this.OriginalDownloadSpeed <= 0 ? long.MaxValue : this.OriginalDownloadSpeed;

				var server_upload_throttle = long.MaxValue;
				var server_download_throttle = long.MaxValue;

				try
				{
					if (!string.IsNullOrWhiteSpace(Program.DataConnection.ApplicationSettings.UploadSpeedLimit))
						server_upload_throttle = Duplicati.Library.Utility.Sizeparser.ParseSize(Program.DataConnection.ApplicationSettings.UploadSpeedLimit, "kb");
				}
				catch { }

				try
				{
					if (!string.IsNullOrWhiteSpace(Program.DataConnection.ApplicationSettings.DownloadSpeedLimit))
						server_download_throttle = Duplicati.Library.Utility.Sizeparser.ParseSize(Program.DataConnection.ApplicationSettings.DownloadSpeedLimit, "kb");
				}
				catch { }

				var upload_throttle = Math.Min(job_upload_throttle, server_upload_throttle);
				var download_throttle = Math.Min(job_download_throttle, server_download_throttle);

				if (upload_throttle <= 0 || upload_throttle == long.MaxValue)
					upload_throttle = 0;

				if (download_throttle <= 0 || download_throttle == long.MaxValue)
					download_throttle = 0;

				controller.MaxUploadSpeed = upload_throttle;
				controller.MaxDownloadSpeed = download_throttle;
			}

            private readonly long m_taskID;
            
            public RunnerData()
            {
                m_taskID = System.Threading.Interlocked.Increment(ref RunnerTaskID);
            }
        }

        private class CustomRunnerTask : RunnerData
        {
            public readonly Action<Library.Main.IMessageSink> Run;

            public CustomRunnerTask(Action<Library.Main.IMessageSink> runner)
                : base()
            {
                if (runner == null)
                    throw new ArgumentNullException("runner");
                Run = runner;
                Operation = DuplicatiOperation.CustomRunner;
                Backup = new Database.Backup();
            }
        }

        public static IRunnerData CreateCustomTask(Action<Library.Main.IMessageSink> runner)
        {
            return new CustomRunnerTask(runner);
        }
        
        public static IRunnerData CreateTask(Duplicati.Server.Serialization.DuplicatiOperation operation, Duplicati.Server.Serialization.Interface.IBackup backup, IDictionary<string, string> extraOptions = null, string[] filterStrings = null)
        {
            return new RunnerData() {
                Operation = operation,
                Backup = backup,
                ExtraOptions = extraOptions,
                FilterStrings = filterStrings
            };
        }
        
        public static IRunnerData CreateListTask(Duplicati.Server.Serialization.Interface.IBackup backup, string[] filters, bool onlyPrefix, bool allVersions, bool folderContents, DateTime time)
        {
            var dict = new Dictionary<string, string>();
            if (onlyPrefix)
                dict["list-prefix-only"] = "true";
            if (allVersions)
                dict["all-versions"] = "true";
            if (time.Ticks > 0)
                dict["time"] = Duplicati.Library.Utility.Utility.SerializeDateTime(time.ToUniversalTime());
            if (folderContents)
                dict["list-folder-contents"] = "true";
            
            return CreateTask(
                DuplicatiOperation.List,
                backup,
                dict,
                filters);
        }

        public static IRunnerData CreateRestoreTask(Duplicati.Server.Serialization.Interface.IBackup backup, string[] filters, DateTime time, string restoreTarget, bool overwrite, bool restore_permissions, bool skip_metadata)
        {
            var dict = new Dictionary<string, string>();
            dict["time"] = Duplicati.Library.Utility.Utility.SerializeDateTime(time.ToUniversalTime());
            if (!string.IsNullOrWhiteSpace(restoreTarget))
                dict["restore-path"] = SpecialFolders.ExpandEnvironmentVariables(restoreTarget);
            if (overwrite)
                dict["overwrite"] = "true";
            if (restore_permissions)
                dict["restore-permissions"] = "true";
            if (skip_metadata)
                dict["skip-metadata"] = "true";
            
            return CreateTask(
                DuplicatiOperation.Restore,
                backup,
                dict,
                filters);            
        }        
        private class MessageSink : Duplicati.Library.Main.IMessageSink
        {
            private class ProgressState : Server.Serialization.Interface.IProgressEventData
            {
                private readonly string m_backupID;
                private readonly long m_taskID;
                
                internal Duplicati.Library.Main.BackendActionType m_backendAction;
                internal string m_backendPath;
                internal long m_backendFileSize;
                internal long m_backendFileProgress;
                internal long m_backendSpeed;
                
                internal string m_currentFilename;
                internal long m_currentFilesize;
                internal long m_currentFileoffset;
                
                internal Duplicati.Library.Main.OperationPhase m_phase;
                internal float m_overallProgress;
                internal long m_processedFileCount;
                internal long m_processedFileSize;
                internal long m_totalFileCount;
                internal long m_totalFileSize;
                internal bool m_stillCounting;
                
                public ProgressState(long taskId, string backupId)
                {
                    m_backupID = backupId;
                    m_taskID = taskId;
                }
                
                internal ProgressState Clone()
                {
                    return (ProgressState)this.MemberwiseClone();
                }

                #region IProgressEventData implementation
                public string BackupID { get { return m_backupID; } }
                public long TaskID { get { return m_taskID; } }
                public string BackendAction { get { return m_backendAction.ToString(); } }
                public string BackendPath { get { return m_backendPath; } }
                public long BackendFileSize { get { return m_backendFileSize; } }
                public long BackendFileProgress { get { return m_backendFileProgress; } }
                public long BackendSpeed { get { return m_backendSpeed; } }
                public string CurrentFilename { get { return m_currentFilename; } }
                public long CurrentFilesize { get { return m_currentFilesize; } }
                public long CurrentFileoffset { get { return m_currentFileoffset; } }
                public string Phase { get { return  m_phase.ToString(); } }
                public float OverallProgress { get { return m_overallProgress; } }
                public long ProcessedFileCount { get { return m_processedFileCount; } }
                public long ProcessedFileSize { get { return m_processedFileSize; } }
                public long TotalFileCount { get { return m_totalFileCount; } }
                public long TotalFileSize { get { return m_totalFileSize; } }
                public bool StillCounting { get { return m_stillCounting; } }
                #endregion
            }
                        
            private ProgressState m_state;
            private Duplicati.Library.Main.IBackendProgress m_backendProgress;
            private Duplicati.Library.Main.IOperationProgress m_operationProgress;
            private object m_lock = new object();
            
            public MessageSink(long taskId, string backupId)
            {
                m_state = new ProgressState(taskId, backupId);
            }
            
            public Server.Serialization.Interface.IProgressEventData Copy()
            {
                lock(m_lock)
                {
                    if (m_backendProgress != null)
                        m_backendProgress.Update(out m_state.m_backendAction, out m_state.m_backendPath, out m_state.m_backendFileSize, out m_state.m_backendFileProgress, out m_state.m_backendSpeed);
                    if (m_operationProgress != null)
                    {
                        m_operationProgress.UpdateFile(out m_state.m_currentFilename, out m_state.m_currentFilesize, out m_state.m_currentFileoffset);
                        m_operationProgress.UpdateOverall(out m_state.m_phase, out m_state.m_overallProgress, out m_state.m_processedFileCount, out m_state.m_processedFileSize, out m_state.m_totalFileCount, out m_state.m_totalFileSize, out m_state.m_stillCounting);
                    }
                        
                    return m_state.Clone();
                }
            }
            
            #region IMessageSink implementation
            public void BackendEvent(Duplicati.Library.Main.BackendActionType action, Duplicati.Library.Main.BackendEventType type, string path, long size)
            {
                lock(m_lock)
                {
                    m_state.m_backendAction = action;
                    m_state.m_backendPath = path;
                    if (type == Duplicati.Library.Main.BackendEventType.Started)
                        m_state.m_backendFileSize = size;
                    else if (type == Duplicati.Library.Main.BackendEventType.Progress)
                        m_state.m_backendFileProgress = size;
                    else
                    {
                        m_state.m_backendFileSize = 0;
                        m_state.m_backendFileProgress = 0;
                        m_state.m_backendSpeed = 0;
                    }
                }
            }
            public void VerboseEvent(string message, object[] args)
            {
            }
            public void MessageEvent(string message)
            {
            }
            public void RetryEvent(string message, Exception ex)
            {
            }
            public void WarningEvent(string message, Exception ex)
            {
            }
            public void ErrorEvent(string message, Exception ex)
            {
            }
            public void DryrunEvent(string message)
            {
            }
            public Duplicati.Library.Main.IBackendProgress BackendProgress
            {
                set
                {
                    lock(m_lock)
                        m_backendProgress = value;
                }
            }
            public Duplicati.Library.Main.IOperationProgress OperationProgress
            {
                set
                {                    
                    lock(m_lock)
                        m_operationProgress = value;
                }
            }
            #endregion
        }

        public static string GetCommandLine(IRunnerData data)
        {
            var backup = data.Backup;

            var options = ApplyOptions(backup, data.Operation, GetCommonOptions(backup, data.Operation));
            if (data.ExtraOptions != null)
                foreach(var k in data.ExtraOptions)
                    options[k.Key] = k.Value;

            var cf = Program.DataConnection.Filters;
            var bf = backup.Filters;

            var sources = 
                (from n in backup.Sources
                    let p = SpecialFolders.ExpandEnvironmentVariables(n)
                    where !string.IsNullOrWhiteSpace(p)
                    select p).ToArray();
            
            var cmd = new System.Text.StringBuilder();

            var exe = 
                System.IO.Path.Combine(
                    Library.AutoUpdater.UpdaterManager.InstalledBaseDir,
                        System.IO.Path.GetFileName(
                            typeof(Duplicati.CommandLine.Commands).Assembly.Location
                        )
                );

            Func<string, string> commandLineEscapeValue = x =>
            {
                if (string.IsNullOrWhiteSpace(x))
                    return x;

                if (x.EndsWith("\\", StringComparison.Ordinal))
                    x += "\\";

                x = x.Replace("\"", Library.Utility.Utility.IsClientWindows ? "\"\"" : "\\\"");

                return "\"" + x + "\"";
            };

            exe = commandLineEscapeValue(exe);

            if (Library.Utility.Utility.IsMono)
                exe = "mono " + exe;
            

            cmd.Append(exe);
            cmd.Append(" backup");
            cmd.Append(" ");
            cmd.Append(commandLineEscapeValue(backup.TargetURL));
            cmd.Append(" ");
            cmd.Append(string.Join(" ", sources.Select(x => commandLineEscapeValue(x))));

            foreach(var opt in options)
                cmd.AppendFormat(" --{0}={1}", opt.Key, commandLineEscapeValue(opt.Value));
            
            if (cf != null)
                foreach(var f in cf)
                    cmd.AppendFormat(" --{0}={1}", f.Include ? "include" : "exclude", commandLineEscapeValue(f.Expression));

            if (bf != null)
                foreach(var f in bf)
                    cmd.AppendFormat(" --{0}={1}", f.Include ? "include" : "exclude", commandLineEscapeValue(f.Expression));

            return cmd.ToString();
        }

        public static string[] GetCommandLineParts(IRunnerData data)
        {
            var backup = data.Backup;

            var options = ApplyOptions(backup, data.Operation, GetCommonOptions(backup, data.Operation));
            if (data.ExtraOptions != null)
                foreach (var k in data.ExtraOptions)
                    options[k.Key] = k.Value;

            var cf = Program.DataConnection.Filters;
            var bf = backup.Filters;

            var sources =
                (from n in backup.Sources
                 let p = SpecialFolders.ExpandEnvironmentVariables(n)
                 where !string.IsNullOrWhiteSpace(p)
                 select p).ToArray();

            var parts = new List<string>();

            parts.Add(backup.TargetURL);
            parts.AddRange(sources);

            foreach (var opt in options)
                parts.Add(string.Format("--{0}={1}", opt.Key, opt.Value));

            if (cf != null)
                foreach (var f in cf)
                    parts.Add(string.Format("--{0}={1}", f.Include ? "include" : "exclude", f.Expression));

            if (bf != null)
                foreach (var f in bf)
                    parts.Add(string.Format("--{0}={1}", f.Include ? "include" : "exclude", f.Expression));

            return parts.ToArray();
        }
        
        public static Duplicati.Library.Interface.IBasicResults Run(IRunnerData data, bool fromQueue)
        {
            if (data is CustomRunnerTask)
            {
                try
                {
                    var sink = new MessageSink(data.TaskID, null);
                    Program.GenerateProgressState = () => sink.Copy();
                    Program.StatusEventNotifyer.SignalNewEvent();

                    ((CustomRunnerTask)data).Run(sink);
                }
                catch(Exception ex)
                {
                    Program.DataConnection.LogError(string.Empty, "Failed while executing custom task", ex);
                }

                return null;
            }


            var backup = data.Backup;
            Duplicati.Library.Utility.TempFolder tempfolder = null;

            if (backup.Metadata == null)
                backup.Metadata = new Dictionary<string, string>();
            
            try
            {                
                var sink = new MessageSink(data.TaskID, backup.ID);
                if (fromQueue)
                {
                    Program.GenerateProgressState = () => sink.Copy();
                    Program.StatusEventNotifyer.SignalNewEvent();            
                }

                var options = ApplyOptions(backup, data.Operation, GetCommonOptions(backup, data.Operation));                
                if (data.ExtraOptions != null)
                    foreach(var k in data.ExtraOptions)
                        options[k.Key] = k.Value;                

                // Pack in the system or task config for easy restore
                if (data.Operation == DuplicatiOperation.Backup && options.ContainsKey("store-task-config"))
                {
                    var all_tasks = string.Equals(options["store-task-config"], "all", StringComparison.InvariantCultureIgnoreCase) || string.Equals(options["store-task-config"], "*", StringComparison.InvariantCultureIgnoreCase);
                    var this_task = Duplicati.Library.Utility.Utility.ParseBool(options["store-task-config"], false);

                    options.Remove("store-task-config");

                    if (all_tasks || this_task)
                    {
                        if (tempfolder == null)
                            tempfolder = new Duplicati.Library.Utility.TempFolder();

                        var temppath = System.IO.Path.Combine(tempfolder, "task-setup.json");
                        using(var tempfile = Duplicati.Library.Utility.TempFile.WrapExistingFile(temppath))
                        {
                            object taskdata = null;
                            if (all_tasks)
                                taskdata = Program.DataConnection.Backups.Where(x => !x.IsTemporary).Select(x => Program.DataConnection.PrepareBackupForExport(Program.DataConnection.GetBackup(x.ID)));
                            else
                                taskdata = new [] { Program.DataConnection.PrepareBackupForExport(data.Backup) };

                            using(var fs = System.IO.File.OpenWrite(tempfile))
                            using(var sw = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8))
                                Serializer.SerializeJson(sw, taskdata, true);

                            tempfile.Protected = true;

                            string controlfiles = null;
                            options.TryGetValue("control-files", out controlfiles);

                            if (string.IsNullOrWhiteSpace(controlfiles))
                                controlfiles = tempfile;
                            else
                                controlfiles += System.IO.Path.PathSeparator + tempfile;

                            options["control-files"] = controlfiles;
                        }
                    }
                }

                using(tempfolder)
                using(var controller = new Duplicati.Library.Main.Controller(backup.TargetURL, options, sink))
                {
                    try 
                    {
                        if (options.ContainsKey("throttle-upload"))
                            ((RunnerData)data).OriginalUploadSpeed = Duplicati.Library.Utility.Sizeparser.ParseSize(options["throttle-upload"], "kb");
                    }
                    catch { }

					try
					{
						if (options.ContainsKey("throttle-download"))
                            ((RunnerData)data).OriginalDownloadSpeed = Duplicati.Library.Utility.Sizeparser.ParseSize(options["throttle-download"], "kb");
					}
					catch { }

					((RunnerData)data).Controller = controller;
                    data.UpdateThrottleSpeed();

					switch (data.Operation)
                    {
                        case DuplicatiOperation.Backup:
                            {
                                var filter = ApplyFilter(backup, data.Operation, GetCommonFilter(backup, data.Operation));
                                var sources = 
                                    (from n in backup.Sources
                                        let p = SpecialFolders.ExpandEnvironmentVariables(n)
                                        where !string.IsNullOrWhiteSpace(p)
                                        select p).ToArray();

                                var r = controller.Backup(sources, filter);
                                UpdateMetadata(backup, r);
                                return r;
                            }                          
                        case DuplicatiOperation.List:
                            {
                                var r = controller.List(data.FilterStrings);
                                UpdateMetadata(backup, r);
                                return r;
                            }
                        case DuplicatiOperation.Repair:
                            {
                                var r = controller.Repair(data.FilterStrings == null ? null : new Library.Utility.FilterExpression(data.FilterStrings));
                                UpdateMetadata(backup, r);
                                return r;
                            }
                        case DuplicatiOperation.RepairUpdate:
                            {
                                var r = controller.UpdateDatabaseWithVersions();
                                UpdateMetadata(backup, r);
                                return r;
                            }
                        case DuplicatiOperation.Remove:
                            {
                                var r = controller.Delete();
                                UpdateMetadata(backup, r);
                                return r;
                            }
                        case DuplicatiOperation.Restore:
                            {
                                var r = controller.Restore(data.FilterStrings);
                                UpdateMetadata(backup, r);
                                return r;
                            }
                        case DuplicatiOperation.Verify:
                            {
                                var r = controller.Test();
                                UpdateMetadata(backup, r);
                                return r;
                            }

                        case DuplicatiOperation.Compact:
                            {
                            var r = controller.Compact();
                                UpdateMetadata(backup, r);
                                return r;
                            }

                        case DuplicatiOperation.CreateReport:
                            {
                                using(var tf = new Duplicati.Library.Utility.TempFile())
                                {
                                    var r = controller.CreateLogDatabase(tf);
                                    var tempid = Program.DataConnection.RegisterTempFile("create-bug-report", r.TargetPath, DateTime.Now.AddDays(3));

                                    if (string.Equals(tf, r.TargetPath, Library.Utility.Utility.ClientFilenameStringComparision))
                                        tf.Protected = true;

                                    Program.DataConnection.RegisterNotification(
                                        NotificationType.Information,
                                        "Bugreport ready",
                                        "Bugreport is ready for download",
                                         null,
                                         null,
                                         "bug-report:created:" + tempid,
                                         (n, a) => n
                                     );

                                    return r;
                                }
                            }

                        case DuplicatiOperation.ListRemote:
                            {
                                var r = controller.ListRemote();
                                UpdateMetadata(backup, r);
                                return r;
                            }

                        case DuplicatiOperation.Delete:
                            {
                                if (Library.Utility.Utility.ParseBoolOption(data.ExtraOptions, "delete-remote-files"))
                                    controller.DeleteAllRemoteFiles();

                                if (Library.Utility.Utility.ParseBoolOption(data.ExtraOptions, "delete-local-db"))
                                {
                                    string dbpath;
                                    options.TryGetValue("db-path", out dbpath);

                                    if (!string.IsNullOrWhiteSpace(dbpath) && System.IO.File.Exists(dbpath))
                                        System.IO.File.Delete(dbpath);
                                }
                                Program.DataConnection.DeleteBackup(backup);
                                Program.Scheduler.Reschedule();
                                return null;
                            }

                        default:
                            //TODO: Log this
                            return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Program.DataConnection.LogError(data.Backup.ID, string.Format("Failed while executing \"{0}\" with id: {1}", data.Operation, data.Backup.ID), ex);
                UpdateMetadataError(data.Backup, ex);
                Library.UsageReporter.Reporter.Report(ex);
                
                if (!fromQueue)
                    throw;
                
                return null;
            }
            finally
            {
                ((RunnerData)data).Controller = null;
            }
        }
        
        private static void UpdateMetadataError(Duplicati.Server.Serialization.Interface.IBackup backup, Exception ex)
        {
            backup.Metadata["LastErrorDate"] = Library.Utility.Utility.SerializeDateTime(DateTime.UtcNow);
            backup.Metadata["LastErrorMessage"] = ex.Message;

            if (!backup.IsTemporary)
                Program.DataConnection.SetMetadata(backup.Metadata, long.Parse(backup.ID), null);

            System.Threading.Interlocked.Increment(ref Program.LastDataUpdateID);
            Program.DataConnection.RegisterNotification(
                NotificationType.Error, 
                backup.IsTemporary ? 
                    "Error" : string.Format("Error while running {0}", backup.Name),
                ex.Message,
                ex,
                backup.ID,
                "backup:show-log",
                (n, a) => {
                    return a.Where(x => x.BackupID == backup.ID).FirstOrDefault() ?? n;
                }
            );
        }
        
        private static void UpdateMetadata(Duplicati.Server.Serialization.Interface.IBackup backup, Duplicati.Library.Interface.IParsedBackendStatistics r)
        {
            if (r != null)
            {
                backup.Metadata["LastBackupDate"] = Library.Utility.Utility.SerializeDateTime(r.LastBackupDate.ToUniversalTime());
                backup.Metadata["BackupListCount"] = r.BackupListCount.ToString();
                backup.Metadata["TotalQuotaSpace"] = r.TotalQuotaSpace.ToString();
                backup.Metadata["FreeQuotaSpace"] = r.FreeQuotaSpace.ToString();
                backup.Metadata["AssignedQuotaSpace"] = r.AssignedQuotaSpace.ToString();
                
                backup.Metadata["TargetFilesSize"] = r.KnownFileSize.ToString();
                backup.Metadata["TargetFilesCount"] = r.KnownFileCount.ToString();
                backup.Metadata["TargetSizeString"] = Duplicati.Library.Utility.Utility.FormatSizeString(r.KnownFileSize);
            }
        }        
        
        private static void UpdateMetadata(Duplicati.Server.Serialization.Interface.IBackup backup, object o)
        {
            if (o is Duplicati.Library.Interface.IBasicResults)
            {
                var r = (Duplicati.Library.Interface.IBasicResults)o;
                backup.Metadata["LastDuration"] = r.Duration.ToString();
                backup.Metadata["LastStarted"] = Library.Utility.Utility.SerializeDateTime(((Duplicati.Library.Interface.IBasicResults)o).BeginTime.ToUniversalTime());
                backup.Metadata["LastFinished"] = Library.Utility.Utility.SerializeDateTime(((Duplicati.Library.Interface.IBasicResults)o).EndTime.ToUniversalTime());
            }
            
            if (o is Duplicati.Library.Interface.IParsedBackendStatistics)
            {
                var r = (Duplicati.Library.Interface.IParsedBackendStatistics)o;
                UpdateMetadata(backup, r);
            }

            if (o is Duplicati.Library.Interface.IBackendStatsticsReporter)
            {
                var r = (Duplicati.Library.Interface.IBackendStatsticsReporter)o;
                if (r.BackendStatistics is Duplicati.Library.Interface.IParsedBackendStatistics)
                    UpdateMetadata(backup, (Duplicati.Library.Interface.IParsedBackendStatistics)r.BackendStatistics);
            }

            if (o is Duplicati.Library.Interface.IBackupResults)
            {
                var r = (Duplicati.Library.Interface.IBackupResults)o;
                backup.Metadata["SourceFilesSize"] = r.SizeOfExaminedFiles.ToString();
                backup.Metadata["SourceFilesCount"] = r.ExaminedFiles.ToString();
                backup.Metadata["SourceSizeString"] = Duplicati.Library.Utility.Utility.FormatSizeString(r.SizeOfExaminedFiles);
                backup.Metadata["LastBackupStarted"] = Library.Utility.Utility.SerializeDateTime(((Duplicati.Library.Interface.IBasicResults)o).BeginTime.ToUniversalTime());
                backup.Metadata["LastBackupFinished"] = Library.Utility.Utility.SerializeDateTime(((Duplicati.Library.Interface.IBasicResults)o).EndTime.ToUniversalTime());

                if (r.FilesWithError > 0 || r.Warnings.Any() || r.Errors.Any())
                {
                    Program.DataConnection.RegisterNotification(
                        NotificationType.Error,
                        backup.IsTemporary ?
                            "Warning" : string.Format("Warning while running {0}", backup.Name),
                            r.FilesWithError > 0 ?
                                string.Format("Errors affected {0} file(s) ", r.FilesWithError) :
                                string.Format("Got {0} warning(s) ", r.Warnings.Count())
                            ,
                        null,
                        backup.ID,
                        "backup:show-log",
                        (n, a) =>
                        {
                            var existing = (a.Where(x => x.BackupID == backup.ID)).FirstOrDefault();
                            if (existing == null)
                                return n;

                            if (existing.Type == NotificationType.Error)
                                return existing;

                            return n;
                        }
                    );
                }
            }
            else if (o is Duplicati.Library.Interface.IBasicResults)
            {
                var r = (Duplicati.Library.Interface.IBasicResults)o;
                if (r.ParsedResult != Library.Interface.ParsedResultType.Success)
                {
                    var type = r.ParsedResult == Library.Interface.ParsedResultType.Warning
                                ? NotificationType.Warning
                                : NotificationType.Error;

                    var title = r.ParsedResult == Library.Interface.ParsedResultType.Warning
                                 ? (backup.IsTemporary ?
                                    "Warning" : string.Format("Warning while running {0}", backup.Name))
                                : (backup.IsTemporary ?
                                   "Error" : string.Format("Error while running {0}", backup.Name));

                    var message = r.ParsedResult == Library.Interface.ParsedResultType.Warning
                                   ? string.Format("Got {0} warning(s) ", r.Warnings.Count())
                                   : string.Format("Got {0} error(s) ", r.Errors.Count());

                    Program.DataConnection.RegisterNotification(
                        type,
                        title,
                        message,
                        null,
                        backup.ID,
                        "backup:show-log",
                        (n, a) => n
                    );
                }                
            }
            
            if (!backup.IsTemporary)
                Program.DataConnection.SetMetadata(backup.Metadata, long.Parse(backup.ID), null);
            
            System.Threading.Interlocked.Increment(ref Program.LastDataUpdateID);
            Program.StatusEventNotifyer.SignalNewEvent();
        }
        
        private static bool TestIfOptionApplies(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode, string filter)
        {
            //TODO: Implement to avoid warnings
            return true;
        }
        
        private static void DisableModule(string module, Dictionary<string, string> options)
        {
            string disabledModules;
            string enabledModules;
            
            if (options.TryGetValue("enable-module", out enabledModules))
            {
                var emods = (enabledModules ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                options["enable-module"] = string.Join(",", emods.Where(x => module.Equals(x, StringComparison.InvariantCultureIgnoreCase)));
            }
            
            options.TryGetValue("disable-module", out disabledModules);
            var mods = (disabledModules ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            options["disable-module"] = string.Join(",", mods.Union(new string[] { module }).Distinct(StringComparer.InvariantCultureIgnoreCase));
        }
        
        private static Dictionary<string, string> ApplyOptions(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode, Dictionary<string, string> options)
        {
            options["backup-name"] = backup.Name;
            options["dbpath"] = backup.DBPath;
            
            // Apply normal options
            foreach(var o in backup.Settings)
                if (!o.Name.StartsWith("--") && TestIfOptionApplies(backup, mode, o.Filter))
                    options[o.Name] = o.Value;

            // Apply override options
            foreach(var o in backup.Settings)
                if (o.Name.StartsWith("--") && TestIfOptionApplies(backup, mode, o.Filter))
                    options[o.Name.Substring(2)] = o.Value;
            
            
            // The server hangs if the module is enabled as there is no console attached
            DisableModule("console-password-input", options);
            
            return options;
        }

        private static Duplicati.Library.Utility.IFilter ApplyFilter(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode, Duplicati.Library.Utility.IFilter filter)
        {
            var f2 = backup.Filters;
            if (f2 != null && f2.Length > 0)
            {
                var nf =
                    (from n in f2
                    let exp = 
                        n.Expression.StartsWith("[") && n.Expression.EndsWith("]")
                        ? SpecialFolders.ExpandEnvironmentVariablesRegexp(n.Expression)
                        : SpecialFolders.ExpandEnvironmentVariables(n.Expression)
                    orderby n.Order
                    select (Duplicati.Library.Utility.IFilter)(new Duplicati.Library.Utility.FilterExpression(exp, n.Include)))
                    .Aggregate((a, b) => Duplicati.Library.Utility.FilterExpression.Combine(a, b));

                return Duplicati.Library.Utility.FilterExpression.Combine(filter, nf);
            }
            else
                return filter;
        }
        
        private static Dictionary<string, string> GetCommonOptions(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode)
        {
            return 
                (from n in Program.DataConnection.Settings
                 where TestIfOptionApplies(backup, mode, n.Filter)
                 select n).ToDictionary(k => k.Name.StartsWith("--", StringComparison.Ordinal) ? k.Name.Substring(2) : k.Name, k => k.Value);
        }
        
        private static Duplicati.Library.Utility.IFilter GetCommonFilter(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode)
        {
            var filters = Program.DataConnection.Filters;
            if (filters == null || filters.Length == 0)
                return null;
            
           return   
                (from n in filters
                orderby n.Order
                let exp = Library.Utility.Utility.ExpandEnvironmentVariables(n.Expression)
                select (Duplicati.Library.Utility.IFilter)(new Duplicati.Library.Utility.FilterExpression(exp, n.Include)))
                .Aggregate((a, b) => Duplicati.Library.Utility.FilterExpression.Combine(a, b));
        }


    }
}

