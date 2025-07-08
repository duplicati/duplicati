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

#nullable enable

using System;
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Interface;
using Duplicati.Server.Serialization;
using Duplicati.Library.RestAPI;
using Duplicati.Library.Utility;
using System.Threading.Tasks;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using System.Threading;

namespace Duplicati.Server
{
    public static class Runner
    {
        public interface IRunnerData : Serialization.Interface.IQueuedTask
        {
            Serialization.Interface.IBackup? Backup { get; }
            IDictionary<string, string?>? ExtraOptions { get; }
            string[]? FilterStrings { get; }
            string[]? ExtraArguments { get; }
            int PageSize { get; }
            int PageOffset { get; }
            void SetController(Library.Main.Controller? controller);
        }

        private class RunnerData : IRunnerData
        {
            private static long RunnerTaskID = 1;

            public Func<Task>? OnStarting { get; set; }
            public Func<Exception?, Task>? OnFinished { get; set; }

            /// <summary>
            /// Callback to be executed when the task is finished.
            /// Only supported for the delete operation for now.
            /// </summary>
            internal Action<IRunnerData>? AfterTaskFinished { get; set; }

            public DuplicatiOperation Operation { get; internal set; }
            public Serialization.Interface.IBackup? Backup { get; internal set; }
            public IDictionary<string, string?>? ExtraOptions { get; internal set; }
            public string[]? FilterStrings { get; internal set; }

            public string? BackupID { get { return Backup?.ID; } }
            public long TaskID { get { return m_taskID; } }

            public string[]? ExtraArguments { get; internal set; }
            public int PageSize { get; internal set; } = 0;
            public int PageOffset { get; internal set; } = 0;

            public DateTime? TaskStarted { get; set; }
            public DateTime? TaskFinished { get; set; }

            internal Library.Main.Controller? Controller { get; set; }

            public void SetController(Library.Main.Controller? controller)
            {
                Controller = controller;
            }

            public void Stop()
            {
                Controller?.Stop();
            }

            public void Abort()
            {
                Controller?.Abort();
            }

            public void Pause(bool alsoTransfers)
            {
                Controller?.Pause(alsoTransfers);
            }

            public void Resume()
            {
                Controller?.Resume();
            }

            public long OriginalUploadSpeed { get; set; }
            public long OriginalDownloadSpeed { get; set; }

            public void UpdateThrottleSpeeds(string? uploadSpeed, string? downloadSpeed)
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
                    if (!string.IsNullOrWhiteSpace(uploadSpeed))
                        server_upload_throttle = Sizeparser.ParseSize(uploadSpeed, "kb");
                }
                catch { }

                try
                {
                    if (!string.IsNullOrWhiteSpace(downloadSpeed))
                        server_download_throttle = Sizeparser.ParseSize(downloadSpeed, "kb");
                }
                catch { }

                var upload_throttle = Math.Min(job_upload_throttle, server_upload_throttle);
                var download_throttle = Math.Min(job_download_throttle, server_download_throttle);

                if (upload_throttle <= 0 || upload_throttle == long.MaxValue)
                    upload_throttle = 0;

                if (download_throttle <= 0 || download_throttle == long.MaxValue)
                    download_throttle = 0;

                controller.SetThrottleSpeeds(upload_throttle, download_throttle);
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
                    throw new ArgumentNullException(nameof(runner));
                Run = runner;
                Operation = DuplicatiOperation.CustomRunner;
                Backup = new Database.Backup();
            }
        }

        public static IRunnerData CreateCustomTask(Action<Library.Main.IMessageSink> runner)
        {
            return new CustomRunnerTask(runner);
        }

        public static IRunnerData CreateTask(DuplicatiOperation operation, IBackup backup, IDictionary<string, string?>? extraOptions = null, string[]? filterStrings = null, string[]? extraArguments = null, int pageSize = 0, int pageOffset = 0)
        {
            return new RunnerData()
            {
                Operation = operation,
                Backup = backup,
                ExtraOptions = extraOptions ?? new Dictionary<string, string?>(),
                FilterStrings = filterStrings,
                ExtraArguments = extraArguments,
                PageSize = pageSize,
                PageOffset = pageOffset
            };
        }

        public static IRunnerData CreateDeleteTask(IBackup backup, IDictionary<string, string?> extraOptions, Action<IRunnerData>? afterTaskFinished = null)
        {
            return new RunnerData()
            {
                Operation = DuplicatiOperation.Delete,
                Backup = backup,
                ExtraOptions = extraOptions,
                AfterTaskFinished = afterTaskFinished
            };
        }

        public static IRunnerData CreateListTask(IBackup backup, string[]? filters, bool onlyPrefix, bool allVersions, bool folderContents, DateTime time)
        {
            var dict = new Dictionary<string, string?>();
            if (onlyPrefix)
                dict["list-prefix-only"] = "true";
            if (allVersions)
                dict["all-versions"] = "true";
            if (time.Ticks > 0)
                dict["time"] = Utility.SerializeDateTime(time.ToUniversalTime());
            if (folderContents)
                dict["list-folder-contents"] = "true";

            return CreateTask(
                DuplicatiOperation.List,
                backup,
                dict,
                filters);
        }

        public static IRunnerData CreateListFilesetsTask(IBackup backup, Dictionary<string, string?>? extraOptions = null)
        {
            return CreateTask(
                DuplicatiOperation.ListFilesets,
                backup,
                extraOptions ?? new Dictionary<string, string?>());
        }

        public static IRunnerData CreateListFolderContents(IBackup backup, string[]? folders, DateTime time, int pageSize, int pageOffset)
        {
            var dict = new Dictionary<string, string?>();
            if (time.Ticks > 0)
                dict["time"] = Utility.SerializeDateTime(time.ToUniversalTime());

            return CreateTask(
                DuplicatiOperation.ListFolderContents,
                backup,
                dict,
                extraArguments: folders,
                pageSize: pageSize,
                pageOffset: pageOffset);
        }

        public static IRunnerData ListFileVersionsTask(IBackup backup, string[]? filepaths, int pageSize, int pageOffset)
        {
            var dict = new Dictionary<string, string?>();
            return CreateTask(
                DuplicatiOperation.ListFileVersions,
                backup,
                dict,
                extraArguments: filepaths,
                pageSize: pageSize,
                pageOffset: pageOffset);
        }

        public static IRunnerData CreateSearchEntriesTask(IBackup backup, string[]? filters, string[]? folders, DateTime time, int pageSize, int pageOffset)
        {
            var dict = new Dictionary<string, string?>();
            if (time.Ticks > 0)
                dict["time"] = Utility.SerializeDateTime(time.ToUniversalTime());

            return CreateTask(
                DuplicatiOperation.SearchEntries,
                backup,
                dict,
                filters,
                extraArguments: folders,
                pageSize: pageSize,
                pageOffset: pageOffset);
        }


        public static IRunnerData CreateRestoreTask(IBackup backup, string[]? filters,
                                                    DateTime time, string? restoreTarget, bool overwrite, bool restore_permissions,
                                                    bool skip_metadata, string? passphrase)
        {
            var dict = new Dictionary<string, string?>
            {
                ["time"] = Utility.SerializeDateTime(time.ToUniversalTime()),
                ["overwrite"] = overwrite ? bool.TrueString : bool.FalseString,
                ["restore-permissions"] = restore_permissions ? bool.TrueString : bool.FalseString,
                ["skip-metadata"] = skip_metadata ? bool.TrueString : bool.FalseString,
                ["allow-passphrase-change"] = bool.TrueString
            };
            if (!string.IsNullOrWhiteSpace(restoreTarget))
                dict["restore-path"] = SpecialFolders.ExpandEnvironmentVariables(restoreTarget);
            if (!(passphrase is null))
                dict["passphrase"] = passphrase;

            return CreateTask(
                DuplicatiOperation.Restore,
                backup,
                dict,
                filters);
        }
        private class MessageSink : Library.Main.IMessageSink
        {
            private class ProgressState : IProgressEventData
            {
                private readonly string? m_backupID;
                private readonly long m_taskID;

                internal Library.Main.BackendActionType m_backendAction;
                internal string? m_backendPath;
                internal long m_backendFileSize;
                internal long m_backendFileProgress;
                internal long m_backendSpeed;
                internal bool m_backendIsBlocking;

                internal ActiveTransfer[] m_activeTransfers = [];

                internal string? m_currentFilename;
                internal long m_currentFilesize;
                internal long m_currentFileoffset;
                internal bool m_currentFilecomplete;

                internal Library.Main.OperationPhase m_phase;
                internal float m_overallProgress;
                internal long m_processedFileCount;
                internal long m_processedFileSize;
                internal long m_totalFileCount;
                internal long m_totalFileSize;
                internal bool m_stillCounting;

                public ProgressState(long taskId, string? backupId)
                {
                    m_backupID = backupId;
                    m_taskID = taskId;
                }

                internal ProgressState Clone()
                {
                    var res = (ProgressState)this.MemberwiseClone();
                    res.m_activeTransfers = [.. m_activeTransfers];
                    return res;
                }

                #region IProgressEventData implementation
                public string? BackupID { get { return m_backupID; } }
                public long TaskID { get { return m_taskID; } }
                public string BackendAction { get { return m_backendAction.ToString(); } }
                public string? BackendPath { get { return m_backendPath; } }
                public long BackendFileSize { get { return m_backendFileSize; } }
                public long BackendFileProgress { get { return m_backendFileProgress; } }
                public long BackendSpeed { get { return m_backendSpeed; } }
                public bool BackendIsBlocking { get { return m_backendIsBlocking; } }
                public string? CurrentFilename { get { return m_currentFilename; } }
                public long CurrentFilesize { get { return m_currentFilesize; } }
                public long CurrentFileoffset { get { return m_currentFileoffset; } }
                public bool CurrentFilecomplete { get { return m_currentFilecomplete; } }
                public string Phase { get { return m_phase.ToString(); } }
                public float OverallProgress { get { return m_overallProgress; } }
                public long ProcessedFileCount { get { return m_processedFileCount; } }
                public long ProcessedFileSize { get { return m_processedFileSize; } }
                public long TotalFileCount { get { return m_totalFileCount; } }
                public long TotalFileSize { get { return m_totalFileSize; } }
                public bool StillCounting { get { return m_stillCounting; } }
                public ActiveTransfer[] ActiveTransfers => m_activeTransfers;

                #endregion
            }

            private readonly ProgressState m_state;
            private Library.Main.IBackendProgress? m_backendProgress;
            private Library.Main.IOperationProgress? m_operationProgress;
            private readonly object m_lock = new object();

            public MessageSink(long taskId, string? backupId)
            {
                m_state = new ProgressState(taskId, backupId);
            }

            public Serialization.Interface.IProgressEventData Copy()
            {
                lock (m_lock)
                {
                    if (m_backendProgress != null)
                    {
                        var transfers = m_backendProgress.GetActiveTransfers();
                        m_state.m_activeTransfers = transfers
                            .Select(st => new ActiveTransfer(
                                st.Action.ToString(),
                                st.Path,
                                st.Size,
                                st.Progress,
                                st.BytesPerSecond,
                                st.IsBlocking
                            )).ToArray();

                        if (transfers.Any())
                        {
                            var st = transfers.First();
                            m_state.m_backendAction = st.Action;
                            m_state.m_backendPath = st.Path;
                            m_state.m_backendFileSize = st.Size;
                            m_state.m_backendFileProgress = st.Progress;
                            m_state.m_backendSpeed = st.BytesPerSecond;
                            m_state.m_backendIsBlocking = st.IsBlocking;
                        }
                        else
                        {
                            m_state.m_backendAction = Library.Main.BackendActionType.Get;
                            m_state.m_backendPath = null;
                            m_state.m_backendFileSize = 0;
                            m_state.m_backendFileProgress = 0;
                            m_state.m_backendSpeed = -1;
                            m_state.m_backendIsBlocking = false;
                        }
                    }
                    if (m_operationProgress != null)
                    {
                        m_operationProgress.UpdateFile(out m_state.m_currentFilename, out m_state.m_currentFilesize, out m_state.m_currentFileoffset, out m_state.m_currentFilecomplete);
                        m_operationProgress.UpdateOverall(out m_state.m_phase, out m_state.m_overallProgress, out m_state.m_processedFileCount, out m_state.m_processedFileSize, out m_state.m_totalFileCount, out m_state.m_totalFileSize, out m_state.m_stillCounting);
                    }

                    return m_state.Clone();
                }
            }

            #region IMessageSink implementation
            public void BackendEvent(Duplicati.Library.Main.BackendActionType action, Duplicati.Library.Main.BackendEventType type, string path, long size)
            {
                lock (m_lock)
                {
                    if (path == m_state.m_currentFilename && type != Duplicati.Library.Main.BackendEventType.Started && type != Duplicati.Library.Main.BackendEventType.Progress)
                    {
                        m_state.m_backendFileSize = 0;
                        m_state.m_backendFileProgress = 0;
                        m_state.m_backendSpeed = 0;
                    }
                }
            }

            public void SetBackendProgress(Library.Main.IBackendProgress progress)
            {
                lock (m_lock)
                    m_backendProgress = progress;
            }

            public void SetOperationProgress(Library.Main.IOperationProgress progress)
            {
                lock (m_lock)
                    m_operationProgress = progress;
            }

            public void WriteMessage(Library.Logging.LogEntry entry)
            {
                // Do nothing. Implementation needed for ILogDestination interface.
            }
            #endregion
        }
        public static string GetCommandLine(Connection databaseConnection, IRunnerData data)
        {
            var backup = data.Backup;
            if (backup == null)
                throw new ArgumentNullException(nameof(backup));

            var options = ApplyOptions(databaseConnection, backup, GetCommonOptions(databaseConnection));
            if (data.ExtraOptions != null)
                foreach (var k in data.ExtraOptions)
                    options[k.Key] = k.Value;

            var cf = databaseConnection.Filters;
            var bf = backup.Filters;

            var sources =
                (from n in backup.Sources
                 let p = SpecialFolders.ExpandEnvironmentVariables(n)
                 where !string.IsNullOrWhiteSpace(p)
                 select p).ToArray();

            var exe = System.IO.Path.Combine(
                Library.AutoUpdater.UpdaterManager.INSTALLATIONDIR,
                Library.AutoUpdater.PackageHelper.GetExecutableName(Library.AutoUpdater.PackageHelper.NamedExecutable.CommandLine)
            );

            var cmd = new System.Text.StringBuilder();
            cmd.Append(Utility.WrapAsCommandLine([exe, "backup", backup.TargetURL], false));

            cmd.Append(" ");
            cmd.Append(Utility.WrapAsCommandLine(sources, true));

            // TODO: We should check each option to see if it is a path, and allow expansion on that
            foreach (var opt in options)
                cmd.AppendFormat(" --{0}={1}", opt.Key, Utility.WrapCommandLineElement(opt.Value, false));

            if (cf != null)
                foreach (var f in cf)
                    cmd.AppendFormat(" --{0}={1}", f.Include ? "include" : "exclude", Utility.WrapCommandLineElement(f.Expression, true));

            if (bf != null)
                foreach (var f in bf)
                    cmd.AppendFormat(" --{0}={1}", f.Include ? "include" : "exclude", Utility.WrapCommandLineElement(f.Expression, true));

            return cmd.ToString();
        }

        public static string[] GetCommandLineParts(Connection databaseConnection, IRunnerData data)
        {
            var backup = data.Backup;
            if (backup == null)
                throw new ArgumentNullException(nameof(backup));

            var options = ApplyOptions(databaseConnection, backup, GetCommonOptions(databaseConnection));
            if (data.ExtraOptions != null)
                foreach (var k in data.ExtraOptions)
                    options[k.Key] = k.Value;

            var cf = databaseConnection.Filters;
            var bf = backup.Filters;

            var sources =
                (from n in backup.Sources
                 let p = SpecialFolders.ExpandEnvironmentVariables(n)
                 where !string.IsNullOrWhiteSpace(p)
                 select p).ToArray();

            var parts = new List<string>
            {
                backup.TargetURL
            };
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

        public static IBasicResults? Run(Connection databaseConnection, EventPollNotify eventPollNotify, INotificationUpdateService notificationUpdateService, IProgressStateProviderService progressStateProviderService, IApplicationSettings applicationSettings, IQueuedTask data, bool fromQueue)
        {
            if (data is IRunnerData runnerData)
                return RunInternal(databaseConnection, eventPollNotify, notificationUpdateService, progressStateProviderService, applicationSettings, runnerData, fromQueue);

            throw new ArgumentException("Invalid task type", nameof(data));
        }

        private static IBasicResults? RunInternal(Connection databaseConnection, EventPollNotify eventPollNotify, INotificationUpdateService notificationUpdateService, IProgressStateProviderService progressStateProviderService, IApplicationSettings applicationSettings, IRunnerData data, bool fromQueue)
        {
            data.TaskStarted = DateTime.Now;
            if (data is CustomRunnerTask task)
            {
                try
                {
                    var sink = new MessageSink(task.TaskID, null);
                    progressStateProviderService.GenerateProgressState = sink.Copy;
                    eventPollNotify.SignalNewEvent();
                    eventPollNotify.SignalProgressUpdate(sink.Copy);

                    // Attach a log scope that tags all messages to relay the TaskID and BackupID
                    using var _ = Library.Logging.Log.StartScope(log =>
                    {
                        log[ILogWriteHandler.LiveLogEntry.LOG_EXTRA_TASKID] = data.TaskID.ToString();
                    });

                    // Keep emitting progress updates during the operation
                    using var cts = new CancellationTokenSource();
                    Task.Run(async () =>
                    {
                        while (!cts.IsCancellationRequested)
                        {
                            await Task.Delay(1000, cts.Token);
                            if (!cts.IsCancellationRequested)
                                eventPollNotify.SignalProgressUpdate(sink.Copy);
                        }
                    });

                    task.Run(sink);
                    eventPollNotify.SignalProgressUpdate(sink.Copy);
                }
                catch (Exception ex)
                {
                    databaseConnection.LogError(string.Empty, "Failed while executing custom task", ex);
                }
                finally
                {
                    eventPollNotify.SignalProgressUpdate(null);
                    data.TaskFinished = DateTime.Now;
                }

                return null;
            }

            var backup = data.Backup;
            if (backup == null)
                throw new ArgumentNullException(nameof(backup));

            backup.Metadata ??= new Dictionary<string, string>();
            TempFolder? tempfolder = null;

            try
            {
                var sink = new MessageSink(data.TaskID, backup.ID);
                using var cts = new CancellationTokenSource();
                // Non-queue are "wrong" tasks that are running directly and often cause
                // timeouts. They should be removed, but for now we keep them,
                // but do not report progress updates.
                if (fromQueue)
                {
                    progressStateProviderService.GenerateProgressState = () => sink.Copy();
                    eventPollNotify.SignalNewEvent();
                    eventPollNotify.SignalProgressUpdate(sink.Copy);

                    // Keep emitting progress updates during the operation
                    Task.Run(async () =>
                    {
                        while (!cts.IsCancellationRequested)
                        {
                            await Task.Delay(1000, cts.Token);
                            if (!cts.IsCancellationRequested)
                                eventPollNotify.SignalProgressUpdate(sink.Copy);
                        }
                    });
                }

                var options = ApplyOptions(databaseConnection, backup, GetCommonOptions(databaseConnection));
                if (data.ExtraOptions != null)
                    foreach (var k in data.ExtraOptions)
                        options[k.Key] = k.Value;

                // Pack in the system or task config for easy restore
                if (data.Operation == DuplicatiOperation.Backup && options.ContainsKey("store-task-config"))
                    tempfolder = StoreTaskConfigAndGetTempFolder(databaseConnection, data, options);

                // Attach a log scope that tags all messages to relay the TaskID and BackupID
                using (Library.Logging.Log.StartScope(log =>
                {
                    log[ILogWriteHandler.LiveLogEntry.LOG_EXTRA_TASKID] = data.TaskID.ToString();
                    log[ILogWriteHandler.LiveLogEntry.LOG_EXTRA_BACKUPID] = data.BackupID;
                }))

                using (tempfolder)
                using (var controller = new Duplicati.Library.Main.Controller(backup.TargetURL, options, sink))
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
                    var appSettings = databaseConnection.ApplicationSettings;
                    data.UpdateThrottleSpeeds(appSettings.UploadSpeedLimit, appSettings.DownloadSpeedLimit);

                    // Pass on the provider, will be replaced if configured in the backup
                    controller.SetSecretProvider(applicationSettings.SecretProvider);

                    if (backup.Metadata.ContainsKey("LastCompactFinished"))
                        controller.LastCompact = Utility.DeserializeDateTime(backup.Metadata["LastCompactFinished"]);

                    if (backup.Metadata.ContainsKey("LastVacuumFinished"))
                        controller.LastVacuum = Utility.DeserializeDateTime(backup.Metadata["LastVacuumFinished"]);

                    switch (data.Operation)
                    {
                        case DuplicatiOperation.Backup:
                            {
                                var filter = ApplyFilter(backup, GetCommonFilter(databaseConnection));
                                var sources = backup.Sources
                                    .Select(n => SpecialFolders.ExpandEnvironmentVariables(n))
                                    .WhereNotNullOrWhiteSpace()
                                    .ToArray();

                                var r = controller.Backup(sources, filter);
                                UpdateMetadataBase(databaseConnection, eventPollNotify, notificationUpdateService, backup, r);
                                return r;
                            }
                        case DuplicatiOperation.List:
                            {
                                var r = controller.List(data.FilterStrings, null);
                                UpdateMetadataBase(databaseConnection, eventPollNotify, notificationUpdateService, backup, r);
                                return r;
                            }
                        case DuplicatiOperation.Repair:
                            {
                                var r = controller.Repair(data.FilterStrings == null ? null : new Library.Utility.FilterExpression(data.FilterStrings));
                                UpdateMetadataBase(databaseConnection, eventPollNotify, notificationUpdateService, backup, r);
                                return r;
                            }
                        case DuplicatiOperation.RepairUpdate:
                            {
                                var r = controller.UpdateDatabaseWithVersions();
                                UpdateMetadataBase(databaseConnection, eventPollNotify, notificationUpdateService, backup, r);
                                return r;
                            }
                        case DuplicatiOperation.Remove:
                            {
                                var r = controller.Delete();
                                UpdateMetadataBase(databaseConnection, eventPollNotify, notificationUpdateService, backup, r);
                                return r;
                            }
                        case DuplicatiOperation.Restore:
                            {
                                var r = controller.Restore(data.FilterStrings);
                                UpdateMetadataBase(databaseConnection, eventPollNotify, notificationUpdateService, backup, r);
                                return r;
                            }
                        case DuplicatiOperation.Verify:
                            {
                                var r = controller.Test();
                                UpdateMetadataBase(databaseConnection, eventPollNotify, notificationUpdateService, backup, r);
                                return r;
                            }
                        case DuplicatiOperation.Compact:
                            {
                                var r = controller.Compact();
                                UpdateMetadataBase(databaseConnection, eventPollNotify, notificationUpdateService, backup, r);
                                return r;
                            }
                        case DuplicatiOperation.CreateReport:
                            {
                                using (var tf = new Library.Utility.TempFile())
                                {
                                    var r = controller.CreateLogDatabase(tf);
                                    var tempid = databaseConnection.RegisterTempFile("create-bug-report", r.TargetPath, DateTime.Now.AddDays(3));

                                    if (string.Equals(tf, r.TargetPath, Utility.ClientFilenameStringComparison))
                                        tf.Protected = true;

                                    databaseConnection.RegisterNotification(
                                        NotificationType.Information,
                                        "Bugreport ready",
                                        "Bugreport is ready for download",
                                         null,
                                         null,
                                         "bug-report:created:" + tempid,
                                         null,
                                         "BugreportCreatedReady",
                                         "",
                                         (n, a) => n
                                     );

                                    return r;
                                }
                            }

                        case DuplicatiOperation.ListRemote:
                            {
                                var r = controller.ListRemote();
                                UpdateMetadataBase(databaseConnection, eventPollNotify, notificationUpdateService, backup, r);
                                return r;
                            }

                        case DuplicatiOperation.Delete:
                            {
                                if (data.ExtraOptions != null)
                                {
                                    if (Utility.ParseBoolOption(data.ExtraOptions.AsReadOnly(), "delete-remote-files"))
                                        controller.DeleteAllRemoteFiles();

                                    if (Utility.ParseBoolOption(data.ExtraOptions.AsReadOnly(), "delete-local-db"))
                                    {
                                        options.TryGetValue("dbpath", out var dbpath);

                                        if (!string.IsNullOrWhiteSpace(dbpath) && System.IO.File.Exists(dbpath))
                                            System.IO.File.Delete(dbpath);
                                    }
                                }
                                databaseConnection.DeleteBackup(backup);
                                (data as RunnerData)?.AfterTaskFinished?.Invoke(data);
                                return null;
                            }
                        case DuplicatiOperation.Vacuum:
                            {
                                var r = controller.Vacuum();
                                UpdateMetadataBase(databaseConnection, eventPollNotify, notificationUpdateService, backup, r);
                                return r;
                            }

                        case DuplicatiOperation.ListFilesets:
                            {
                                var r = controller.ListFilesets();
                                UpdateMetadataBase(databaseConnection, eventPollNotify, notificationUpdateService, backup, r);
                                return r;
                            }
                        case DuplicatiOperation.ListFolderContents:
                            {
                                return controller.ListFolder(data.ExtraArguments, data.PageOffset * data.PageSize, data.PageSize);
                            }

                        case DuplicatiOperation.ListFileVersions:
                            {
                                return controller.ListFileVersions(data.ExtraArguments, data.PageOffset * data.PageSize, data.PageSize);
                            }
                        case DuplicatiOperation.SearchEntries:
                            {
                                var parsedfilter = new FilterExpression(data.FilterStrings);
                                return controller.SearchEntries(data.ExtraArguments, parsedfilter, data.PageOffset * data.PageSize, data.PageSize);
                            }
                        default:
                            //TODO: Log this
                            return null;
                    }
                }
            }
            catch (Exception ex)
            {
                databaseConnection.LogError(data.Backup?.ID, string.Format("Failed while executing {0} \"{1}\" (id: {2})", data.Operation, data.Backup?.Name, data.Backup?.ID), ex);
                if (data.Backup != null)
                    UpdateMetadataError(databaseConnection, notificationUpdateService, data.Backup, ex);
                Library.UsageReporter.Reporter.Report(ex);

                throw;
            }
            finally
            {
                data.SetController(null);
                data.TaskFinished = DateTime.Now;
                eventPollNotify.SignalProgressUpdate(progressStateProviderService?.GenerateProgressState);
            }
        }

        private static TempFolder? StoreTaskConfigAndGetTempFolder(Connection databaseConnection, IRunnerData data, Dictionary<string, string?> options)
        {
            if (data.Backup == null)
                throw new ArgumentNullException(nameof(data.Backup));

            var all_tasks = string.Equals(options["store-task-config"], "all", StringComparison.OrdinalIgnoreCase) || string.Equals(options["store-task-config"], "*", StringComparison.OrdinalIgnoreCase);
            var this_task = Utility.ParseBool(options["store-task-config"], false);

            options.Remove("store-task-config");

            TempFolder? tempfolder = null;
            if (all_tasks || this_task)
            {
                tempfolder = new TempFolder();
                var temppath = System.IO.Path.Combine(tempfolder, "task-setup.json");
                using (var tempfile = Library.Utility.TempFile.WrapExistingFile(temppath))
                {
                    object? taskdata = null;
                    if (all_tasks)
                        taskdata = databaseConnection.Backups.Where(x => !x.IsTemporary).Select(x => databaseConnection.PrepareBackupForExport(databaseConnection.GetBackup(x.ID)!));
                    else
                        taskdata = new[] { databaseConnection.PrepareBackupForExport(data.Backup) };

                    using (var fs = System.IO.File.OpenWrite(tempfile))
                    using (var sw = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8))
                        Serializer.SerializeJson(sw, taskdata, true);

                    tempfile.Protected = true;

                    options.TryGetValue("control-files", out var controlfiles);

                    if (string.IsNullOrWhiteSpace(controlfiles))
                        controlfiles = tempfile;
                    else
                        controlfiles += System.IO.Path.PathSeparator + tempfile;

                    options["control-files"] = controlfiles;
                }
            }
            return tempfolder;
        }

        private static void UpdateMetadataError(Connection databaseConnection, INotificationUpdateService notificationUpdateService, IBackup backup, Exception ex)
        {
            backup.Metadata["LastErrorDate"] = Utility.SerializeDateTime(DateTime.UtcNow);
            backup.Metadata["LastErrorMessage"] = ex.Message;

            if (!backup.IsTemporary)
                databaseConnection.SetMetadata(backup.Metadata, long.Parse(backup.ID), null);

            string? messageid = null;
            if (ex is UserInformationException exception)
                messageid = exception.HelpID;

            notificationUpdateService.IncrementLastDataUpdateId();
            databaseConnection.RegisterNotification(
                NotificationType.Error,
                backup.IsTemporary ?
                    "Error" : string.Format("Error while running {0}", backup.Name),
                ex.Message,
                ex,
                backup.ID,
                "backup:show-log",
                null,
                messageid,
                null,
                (n, a) =>
                {
                    return a.FirstOrDefault(x => x.BackupID == backup.ID) ?? n;
                }
            );
        }

        private static void UpdateMetadataLastCompact(IBackup backup, ICompactResults r)
        {
            if (r != null)
            {
                backup.Metadata["LastCompactDuration"] = r.Duration.ToString();
                backup.Metadata["LastCompactStarted"] = Utility.SerializeDateTime(r.BeginTime.ToUniversalTime());
                backup.Metadata["LastCompactFinished"] = Utility.SerializeDateTime(r.EndTime.ToUniversalTime());
            }
        }

        private static void UpdateMetadataLastVacuum(IBackup backup, IVacuumResults r)
        {
            if (r != null)
            {
                backup.Metadata["LastVacuumDuration"] = r.Duration.ToString();
                backup.Metadata["LastVacuumStarted"] = Utility.SerializeDateTime(r.BeginTime.ToUniversalTime());
                backup.Metadata["LastVacuumFinished"] = Utility.SerializeDateTime(r.EndTime.ToUniversalTime());
            }
        }

        private static void UpdateMetadataStatistics(IBackup backup, IParsedBackendStatistics r)
        {
            if (r != null)
            {
                backup.Metadata["LastBackupDate"] = Utility.SerializeDateTime(r.LastBackupDate.ToUniversalTime());
                backup.Metadata["BackupListCount"] = r.BackupListCount.ToString();
                backup.Metadata["TotalQuotaSpace"] = r.TotalQuotaSpace.ToString();
                backup.Metadata["FreeQuotaSpace"] = r.FreeQuotaSpace.ToString();
                backup.Metadata["AssignedQuotaSpace"] = r.AssignedQuotaSpace.ToString();

                backup.Metadata["TargetFilesSize"] = r.KnownFileSize.ToString();
                backup.Metadata["TargetFilesCount"] = r.KnownFileCount.ToString();
                backup.Metadata["TargetFilesetsCount"] = r.KnownFilesets.ToString();
                backup.Metadata["TargetSizeString"] = Utility.FormatSizeString(r.KnownFileSize);
            }
        }

        private static void UpdateMetadataBase(Connection databaseConnection, EventPollNotify eventPollNotify, INotificationUpdateService notificationUpdateService, IBackup backup, IBasicResults result)
        {
            if (result is IRestoreResults r1)
            {
                backup.Metadata["LastRestoreDuration"] = r1.Duration.ToString();
                backup.Metadata["LastRestoreStarted"] = Utility.SerializeDateTime(result.BeginTime.ToUniversalTime());
                backup.Metadata["LastRestoreFinished"] = Utility.SerializeDateTime(result.EndTime.ToUniversalTime());
            }

            if (result is IParsedBackendStatistics r2 && !result.Interrupted)
            {
                UpdateMetadataStatistics(backup, r2);
            }

            if (result is IBackendStatsticsReporter r3 && !result.Interrupted)
            {
                if (r3.BackendStatistics is IParsedBackendStatistics statistics)
                    UpdateMetadataStatistics(backup, statistics);
            }

            if (result is ICompactResults r4 && !result.Interrupted)
            {
                UpdateMetadataLastCompact(backup, r4);

                if (r4.VacuumResults != null)
                    UpdateMetadataLastVacuum(backup, r4.VacuumResults);
            }

            if (result is IVacuumResults r5 && !result.Interrupted)
            {
                UpdateMetadataLastVacuum(backup, r5);
            }

            if (result is IBackupResults r)
            {
                if (!result.Interrupted)
                {
                    backup.Metadata["SourceFilesSize"] = r.SizeOfExaminedFiles.ToString();
                    backup.Metadata["SourceFilesCount"] = r.ExaminedFiles.ToString();
                    backup.Metadata["SourceSizeString"] = Duplicati.Library.Utility.Utility.FormatSizeString(r.SizeOfExaminedFiles);
                    backup.Metadata["LastBackupStarted"] = Utility.SerializeDateTime(r.BeginTime.ToUniversalTime());
                    backup.Metadata["LastBackupFinished"] = Utility.SerializeDateTime(r.EndTime.ToUniversalTime());
                    backup.Metadata["LastBackupDuration"] = r.Duration.ToString();

                    if (r.CompactResults != null)
                        UpdateMetadataLastCompact(backup, r.CompactResults);

                    if (r.VacuumResults != null)
                        UpdateMetadataLastVacuum(backup, r.VacuumResults);
                }

                if (r.FilesWithError > 0 || r.Warnings.Any() || r.Errors.Any())
                {
                    string message;
                    string titleType;
                    if (r.FilesWithError > 0)
                    {
                        message = $"Errors affected {r.FilesWithError} file(s).";
                        titleType = "Error";
                    }
                    else if (r.Errors.Any())
                    {
                        message = r.Errors.Count() == 1 ? r.Errors.Single() : $"Encountered {r.Errors.Count()} errors.";
                        titleType = "Error";
                    }
                    else
                    {
                        message = r.Warnings.Count() == 1 ? r.Warnings.Single() : $"Encountered {r.Warnings.Count()} warnings.";
                        titleType = "Warning";
                    }

                    databaseConnection.RegisterNotification(
                        r.FilesWithError == 0 && !r.Errors.Any() ? NotificationType.Warning : NotificationType.Error,
                        backup.IsTemporary ? "Warning" : $"{titleType} while running {backup.Name}",
                        message,
                        null,
                        backup.ID,
                        "backup:show-log",
                        null,
                        null,
                        null,
                        (n, a) =>
                        {
                            var existing = a.FirstOrDefault(x => x.BackupID == backup.ID);
                            if (existing == null)
                                return n;

                            if (existing.Type == NotificationType.Error)
                                return existing;

                            return n;
                        }
                    );
                }
            }
            else if (result.ParsedResult != ParsedResultType.Success)
            {
                var type = result.ParsedResult == ParsedResultType.Warning
                            ? NotificationType.Warning
                            : NotificationType.Error;

                var title = result.ParsedResult == ParsedResultType.Warning
                                ? (backup.IsTemporary ?
                                "Warning" : string.Format("Warning while running {0}", backup.Name))
                            : (backup.IsTemporary ?
                                "Error" : string.Format("Error while running {0}", backup.Name));

                var message = result.ParsedResult == ParsedResultType.Warning
                                    ? string.Format("Got {0} warning(s)", result.Warnings.Count())
                                    : string.Format("Got {0} error(s)", result.Errors.Count());

                // If there is only one error or warning, show the message
                if (result.ParsedResult == ParsedResultType.Warning && result.Warnings.Count() == 1)
                    message = $"Warning: {result.Warnings.Single()}";
                else if (result.ParsedResult == ParsedResultType.Error && result.Errors.Count() == 1)
                    message = $"Error: {result.Errors.Single()}";

                databaseConnection.RegisterNotification(
                    type,
                    title,
                    message,
                    null,
                    backup.ID,
                    "backup:show-log",
                    null,
                    null,
                    "backup:show-log",
                    (n, a) => n
                );
            }

            if (!backup.IsTemporary)
                databaseConnection.SetMetadata(backup.Metadata, long.Parse(backup.ID), null);

            notificationUpdateService.IncrementLastDataUpdateId();
            eventPollNotify.SignalNewEvent();
            if (!backup.IsTemporary)
                eventPollNotify.SignalBackupListUpdate();
        }

        private static bool TestIfOptionApplies()
        {
            //TODO: Implement to avoid warnings
            return true;
        }

        private static void DisableModule(string module, Dictionary<string, string?> options)
        {
            if (options.TryGetValue("enable-module", out var enabledModules))
            {
                var emods = (enabledModules ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                options["enable-module"] = string.Join(",", emods.Where(x => module.Equals(x, StringComparison.OrdinalIgnoreCase)));
            }

            options.TryGetValue("disable-module", out var disabledModules);
            var mods = (disabledModules ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            options["disable-module"] = string.Join(",", mods.Union(new string[] { module }).Distinct(StringComparer.OrdinalIgnoreCase));
        }

        internal static Dictionary<string, string?> ApplyOptions(Connection databaseConnection, Serialization.Interface.IBackup backup, Dictionary<string, string?> options)
        {
            options["backup-name"] = backup.Name;
            options["dbpath"] = backup.DBPath;
            options["backup-id"] = $"DB-{backup.ID}";

            // Apply normal options
            foreach (var o in backup.Settings)
                if (!o.Name.StartsWith("--", StringComparison.Ordinal) && TestIfOptionApplies())
                    options[o.Name] = o.Value;

            // Apply override options
            foreach (var o in backup.Settings)
                if (o.Name.StartsWith("--", StringComparison.Ordinal) && TestIfOptionApplies())
                    options[o.Name.Substring(2)] = o.Value;

            // The server hangs if the module is enabled as there is no console attached
            DisableModule("console-password-input", options);

            // Patch in additional report urls
            var additionalReportUrl = databaseConnection.ApplicationSettings.AdditionalReportUrl;
            if (!string.IsNullOrWhiteSpace(additionalReportUrl))
            {
                options["send-http-json-urls"] = string.Join(";",
                    new[] {
                        options.GetValueOrDefault("send-http-json-urls"),
                        additionalReportUrl
                    }.Where(x => !string.IsNullOrWhiteSpace(x))
                );
            }

            return options;
        }

        private static Library.Utility.IFilter? ApplyFilter(IBackup backup, Library.Utility.IFilter? filter)
        {
            var f2 = backup.Filters;
            if (f2 != null && f2.Length > 0)
            {
                var nf =
                    (from n in f2
                     let exp =
                         n.Expression.StartsWith("[", StringComparison.Ordinal) && n.Expression.EndsWith("]", StringComparison.Ordinal)
                         ? SpecialFolders.ExpandEnvironmentVariablesRegexp(n.Expression)
                         : SpecialFolders.ExpandEnvironmentVariables(n.Expression)
                     orderby n.Order
                     select (Library.Utility.IFilter)new FilterExpression(exp, n.Include))
                    .Aggregate((a, b) => FilterExpression.Combine(a, b));

                filter = FilterExpression.Combine(filter, nf);
            }

            return filter;
        }

        public static Dictionary<string, string?> GetCommonOptions(Connection databaseConnection)
        {
            return
                (from n in databaseConnection.Settings
                 where TestIfOptionApplies()
                 select n).ToDictionary(k => k.Name.StartsWith("--", StringComparison.Ordinal) ? k.Name.Substring(2) : k.Name, k => (string?)k.Value);
        }

        private static Library.Utility.IFilter? GetCommonFilter(Connection databaseConnection)
        {
            var filters = databaseConnection.Filters;
            if (filters == null || filters.Length == 0)
                return null;

            return
                (from n in filters
                 orderby n.Order
                 let exp = Environment.ExpandEnvironmentVariables(n.Expression)
                 select (Library.Utility.IFilter)new FilterExpression(exp, n.Include))
                .Aggregate((a, b) => FilterExpression.Combine(a, b));
        }
    }
}

