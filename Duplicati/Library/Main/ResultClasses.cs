#region Disclaimer / License
// Copyright (C) 2019, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
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
using Duplicati.Library.Interface;
using System.Collections.Generic;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Logging;
using System.Linq;
using Newtonsoft.Json;

namespace Duplicati.Library.Main
{
    internal interface IBackendWriter : IParsedBackendStatistics
    {
        bool ReportedQuotaError { get; set; }
        bool ReportedQuotaWarning { get; set; }

        /// <summary>
        /// The backend sends this event when performing an action
        /// </summary>
        /// <param name="action">The action performed</param>
        /// <param name="type">The event type</param>
        /// <param name="path">Path to the resource</param>
        /// <param name="size">Size of the file or progress</param>
        /// <param name="updateProgress">Whether this event should update the backend progress count</param>
        void SendEvent(BackendActionType action, BackendEventType type, string path, long size, bool updateProgress = true);

        /// <summary>
        /// Gets the backend progress updater.
        /// </summary>
        /// <value>The backend progress updater.</value>
        IBackendProgressUpdater BackendProgressUpdater { get; }
    }

    internal interface ISetCommonOptions
    {
        DateTime EndTime { get; set; }
        DateTime BeginTime { get; set; }
        IMessageSink MessageSink { get; set; }
        OperationMode MainOperation { get; }

        void SetDatabase(LocalDatabase db);
    }

    internal class BackendWriter : BasicResults, IBackendWriter, IBackendStatstics, IParsedBackendStatistics
    {
        public BackendWriter(BasicResults p) : base(p) { }

        protected long m_remoteCalls = 0;
        protected long m_bytesUploaded = 0;
        protected long m_bytesDownloaded = 0;
        protected long m_filesUploaded = 0;
        protected long m_filesDownloaded = 0;
        protected long m_filesDeleted = 0;
        protected long m_foldersCreated = 0;
        protected long m_retryAttemptCount = 0;

        public long RemoteCalls { get { return m_remoteCalls; } }
        public long BytesUploaded { get { return m_bytesUploaded; } }
        public long BytesDownloaded { get { return m_bytesDownloaded; } }
        public long FilesUploaded { get { return m_filesUploaded; } }
        public long FilesDownloaded { get { return m_filesDownloaded; } }
        public long FilesDeleted { get { return m_filesDeleted; } }
        public long FoldersCreated { get { return m_foldersCreated; } }
        public long RetryAttempts { get { return m_retryAttemptCount; } }

        public long UnknownFileSize { get; set; }
        public long UnknownFileCount { get; set; }
        public long KnownFileCount { get; set; }
        public long KnownFileSize { get; set; }
        public DateTime LastBackupDate { get; set; }
        public long BackupListCount { get; set; }
        public long TotalQuotaSpace { get; set; }
        public long FreeQuotaSpace { get; set; }
        public long AssignedQuotaSpace { get; set; }

        public bool ReportedQuotaError { get; set; }
        public bool ReportedQuotaWarning { get; set; }

        public override OperationMode MainOperation { get { return m_parent.MainOperation; } }

        public void SendEvent(BackendActionType action, BackendEventType type, string path, long size, bool updateProgress = true)
        {
            if (type == BackendEventType.Started)
            {
                System.Threading.Interlocked.Increment(ref m_remoteCalls);
            }
            else if (type == BackendEventType.Retrying)
            {
                System.Threading.Interlocked.Increment(ref m_retryAttemptCount);
            }
            else if (type == BackendEventType.Completed)
            {
                switch (action)
                {
                    case BackendActionType.CreateFolder:
                        System.Threading.Interlocked.Increment(ref m_foldersCreated);
                        break;
                    case BackendActionType.List:
                        break;
                    case BackendActionType.Delete:
                        System.Threading.Interlocked.Increment(ref m_filesDeleted);
                        break;
                    case BackendActionType.Get:
                        System.Threading.Interlocked.Increment(ref m_filesDownloaded);
                        System.Threading.Interlocked.Add(ref m_bytesDownloaded, size);
                        break;
                    case BackendActionType.Put:
                        System.Threading.Interlocked.Increment(ref m_filesUploaded);
                        System.Threading.Interlocked.Add(ref m_bytesUploaded, size);
                        break;
                }
            }

            base.AddBackendEvent(action, type, path, size, updateProgress);
        }

        IBackendProgressUpdater IBackendWriter.BackendProgressUpdater { get { return base.BackendProgressUpdater; } }
    }

    public interface ITaskControl
    {
        void Pause();
        void Resume();
        void Stop(bool allowCurrentFileToFinish);
        void Abort();
    }

    internal enum TaskControlState
    {
        Run,
        Pause,
        Stop,
        Abort
    }

    internal abstract class BasicResults : IBasicResults, ISetCommonOptions, ITaskControl, Logging.ILogDestination
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        protected static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(BasicResults));

        /// <summary>
        /// Max number of elements to be serialized to JSON
        /// </summary>
        protected static readonly int SERIALIZATION_LIMIT = 20;

        protected class DbMessage
        {
            public readonly string Type;
            public readonly string Message;
            public readonly Exception Exception;

            public DbMessage(string type, string message, Exception ex)
            {
                this.Type = type;
                this.Message = message;
                this.Exception = ex;
            }
        }

        protected LocalDatabase m_db;
        protected readonly BasicResults m_parent;
        protected System.Threading.Thread m_callerThread;
        protected readonly object m_lock = new object();
        protected readonly Queue<DbMessage> m_dbqueue;

        private TaskControlState m_controlState = TaskControlState.Run;
        private readonly System.Threading.ManualResetEvent m_pauseEvent = new System.Threading.ManualResetEvent(true);

        public virtual ParsedResultType ParsedResult
        {
            get
            {
                if (Errors != null && Errors.Any())
                    return ParsedResultType.Error;
                else if (Warnings != null && Warnings.Any())
                    return ParsedResultType.Warning;
                else
                    return ParsedResultType.Success;
            }
        }

        // ReSharper disable once UnusedMember.Global
        // This is referenced in the logs.
        public string Version { get { return string.Format("{0} ({1})", AutoUpdater.UpdaterManager.SelfVersion.Version, AutoUpdater.UpdaterManager.SelfVersion.Displayname); } }

        public DateTime EndTime { get; set; }
        public DateTime BeginTime { get; set; }
        public TimeSpan Duration { get { return EndTime.Ticks == 0 ? new TimeSpan(0) : EndTime - BeginTime; } }

        public abstract OperationMode MainOperation { get; }

        protected readonly Library.Utility.FileBackedStringList m_messages;
        protected readonly Library.Utility.FileBackedStringList m_warnings;
        protected readonly Library.Utility.FileBackedStringList m_errors;
        protected Library.Utility.FileBackedStringList m_retryAttempts;
        
        protected IMessageSink m_messageSink;

        [JsonIgnore]
        public IMessageSink MessageSink
        {
            get { return m_messageSink; }
            set
            {
                m_messageSink = value;
                if (value != null)
                {
                    m_messageSink.SetOperationProgress(this.OperationProgressUpdater);
                    m_messageSink.SetBackendProgress(this.BackendProgressUpdater);
                }
            }
        }

        protected internal readonly IOperationProgressUpdaterAndReporter m_operationProgressUpdater;
        internal IOperationProgressUpdaterAndReporter OperationProgressUpdater
        {
            get
            {
                if (m_parent != null)
                    return m_parent.OperationProgressUpdater;
                else
                    return m_operationProgressUpdater;
            }
        }

        protected internal readonly IBackendProgressUpdaterAndReporter m_backendProgressUpdater;
        internal IBackendProgressUpdaterAndReporter BackendProgressUpdater
        {
            get
            {
                if (m_parent != null)
                    return m_parent.BackendProgressUpdater;
                else
                    return m_backendProgressUpdater;
            }
        }

        public void SetDatabase(LocalDatabase db)
        {
            if (m_parent != null)
            {
                m_parent.SetDatabase(db);
            }
            else
            {
                lock (m_lock)
                {
                    m_db = db;
                    if (m_db != null)
                        db.SetResult(this);
                }
            }
        }

        public void FlushLog()
        {
            if (m_parent != null)
                m_parent.FlushLog();
            else
            {
                lock (m_lock)
                {
                    while (m_dbqueue.Count > 0)
                    {
                        var el = m_dbqueue.Dequeue();
                        m_db.LogMessage(el.Type, el.Message, el.Exception, null);
                    }
                }
            }
        }

        private static bool m_is_reporting = false;

        public void AddBackendEvent(BackendActionType action, BackendEventType type, string path, long size, bool updateProgress = true)
        {
            if (m_parent != null)
            {
                m_parent.AddBackendEvent(action, type, path, size, updateProgress);
            }
            else
            {
                lock (Logging.Log.Lock)
                {
                    if (m_is_reporting)
                        return;

                    try
                    {
                        m_is_reporting = true;
                        if (type == BackendEventType.Started && updateProgress)
                            this.BackendProgressUpdater.StartAction(action, path, size);

                        Logging.Log.WriteInformationMessage(LOGTAG, "BackendEvent", "Backend event: {0} - {1}: {2} ({3})", action, type, path, size <= 0 ? "" : Library.Utility.Utility.FormatSizeString(size));

                        if (MessageSink != null)
                            MessageSink.BackendEvent(action, type, path, size);
                    }
                    finally
                    {
                        m_is_reporting = false;
                    }
                }
            }

        }

        [JsonIgnore]
        public IEnumerable<string> Messages { get { return m_messages; } }

        // ReSharper disable once UnusedMember.Global
        // This is referenced in the logs.
        public int MessagesActualLength { get { return Messages == null ? 0 : Messages.Count();  } }

        [JsonIgnore]
        public IEnumerable<string> Warnings { get { return m_warnings; } }

        // ReSharper disable once UnusedMember.Global
        // This is referenced in the logs.
        public int WarningsActualLength { get { return Warnings == null ? 0 : Warnings.Count(); } }

        [JsonIgnore]
        public IEnumerable<string> Errors { get { return m_errors; } }

        // ReSharper disable once UnusedMember.Global
        // This is referenced in the logs.
        public int ErrorsActualLength { get { return Errors == null ? 0 : Errors.Count(); } }

        [JsonProperty(PropertyName = "Messages")]
        public IEnumerable<string> LimitedMessages { get { return Messages?.Take(SERIALIZATION_LIMIT); } }
        [JsonProperty(PropertyName = "Warnings")]
        public IEnumerable<string> LimitedWarnings { get { return Warnings?.Take(SERIALIZATION_LIMIT); } }
        [JsonProperty(PropertyName = "Errors")]
        public IEnumerable<string> LimitedErrors { get { return Errors?.Take(SERIALIZATION_LIMIT); } }

        protected readonly Operation.Common.TaskControl m_taskController;
        public Operation.Common.ITaskReader TaskReader { get { return m_taskController; } }

        protected BasicResults()
        {
            this.BeginTime = DateTime.UtcNow;
            this.m_parent = null;
            m_messages = new Library.Utility.FileBackedStringList();
            m_warnings = new Library.Utility.FileBackedStringList();
            m_errors = new Library.Utility.FileBackedStringList();
            m_retryAttempts = new Library.Utility.FileBackedStringList();
            m_dbqueue = new Queue<DbMessage>();
            m_backendStatistics = new BackendWriter(this);
            m_callerThread = System.Threading.Thread.CurrentThread;
            m_backendProgressUpdater = new BackendProgressUpdater();
            m_operationProgressUpdater = new OperationProgressUpdater();
            m_taskController = new Duplicati.Library.Main.Operation.Common.TaskControl();
        }

        protected BasicResults(BasicResults p)
        {
            this.BeginTime = DateTime.UtcNow;
            this.m_parent = p;
        }

        protected readonly IBackendStatstics m_backendStatistics;
        public IBackendStatstics BackendStatistics
        {
            get
            {
                if (this.m_parent != null)
                    return this.m_parent.BackendStatistics;

                return m_backendStatistics;
            }
        }

        [JsonIgnore]
        public IBackendWriter BackendWriter { get { return (IBackendWriter)this.BackendStatistics; } }

        public event Action<TaskControlState> StateChangedEvent;

        /// <summary>
        /// Request that this task pauses.
        /// </summary>
        public void Pause()
        {
            if (m_parent != null)
                m_parent.Pause();
            else
            {
                lock (m_lock)
                    if (m_controlState == TaskControlState.Run)
                    {
                        m_pauseEvent.Reset();
                        m_controlState = TaskControlState.Pause;
                    }

                if (StateChangedEvent != null)
                    StateChangedEvent(m_controlState);
            }
        }

        /// <summary>
        /// Request that this task resumes.
        /// </summary>
        public void Resume()
        {
            if (m_parent != null)
                m_parent.Resume();
            else
            {
                lock (m_lock)
                    if (m_controlState == TaskControlState.Pause)
                    {
                        m_pauseEvent.Set();
                        m_controlState = TaskControlState.Run;
                    }

                if (StateChangedEvent != null)
                    StateChangedEvent(m_controlState);
            }
        }

        /// <summary>
        /// Request that this task stops.
        /// </summary>
        public void Stop(bool allowCurrentFileToFinish)
        {
            if (m_parent != null)
                m_parent.Stop(allowCurrentFileToFinish);
            else
            {
                lock (m_lock)
                    if (m_controlState != TaskControlState.Abort)
                    {
                        m_controlState = TaskControlState.Stop;
                        m_pauseEvent.Set();
                        if (!allowCurrentFileToFinish)
                        {
                            m_taskController.Stop(true);
                        }
                    }

                if (StateChangedEvent != null)
                    StateChangedEvent(m_controlState);
            }
        }

        /// <summary>
        /// Request that this task aborts.
        /// </summary>
        public void Abort()
        {
            if (m_parent != null)
                m_parent.Abort();
            else
            {
                lock (m_lock)
                {
                    m_controlState = TaskControlState.Abort;
                    m_pauseEvent.Set();
                }

                if (StateChangedEvent != null)
                    StateChangedEvent(m_controlState);
            }
        }

        /// <summary>
        /// Helper method that the current running task can call to obtain the current state
        /// </summary>
        public TaskControlState TaskControlRendevouz()
        {
            if (m_parent != null)
                return m_parent.TaskControlRendevouz();
            else
            {
                // If we are paused, go into pause mode
                m_pauseEvent.WaitOne();

                // If we are aborted, throw exception
                if (m_controlState == TaskControlState.Abort)
                {
                    System.Threading.Thread.CurrentThread.Interrupt();

                    // For some reason, aborting the current thread does not always throw an exception
                    throw new CancelException();
                }

                return m_controlState;
            }
        }

        /// <summary>
        /// Helper method to check if abort is requested
        /// </summary>
        public bool IsAbortRequested()
        {
            if (m_parent != null)
                return m_parent.IsAbortRequested();
            else
                return m_controlState == TaskControlState.Abort;
        }

        /// <summary>
        /// Returns a <see cref="System.String"/> that represents the current <see cref="Duplicati.Library.Main.BasicResults"/>.
        /// </summary>
        /// <returns>A <see cref="System.String"/> that represents the current <see cref="Duplicati.Library.Main.BasicResults"/>.</returns>
        public override string ToString()
        {
            return Library.Utility.Utility.PrintSerializeObject(
                this,
                filter: (prop, item) =>
                    !typeof(IBackendProgressUpdater).IsAssignableFrom(prop.PropertyType) &&
                    !typeof(IMessageSink).IsAssignableFrom(prop.PropertyType) &&
                    !(prop.Name == "MainOperation" && item is BackendWriter) &&
                    !(prop.Name == "EndTime" && item is BackendWriter) &&
                    !(prop.Name == "Duration" && item is BackendWriter) &&
                    !(prop.Name == "BeginTime" && item is BackendWriter),
                recurseobjects: true
            ).ToString();
        }

        public void WriteMessage(LogEntry entry)
        {
            if (m_parent != null)
                m_parent.WriteMessage(entry);
            else
            {
                switch (entry.Level)
                {
                    case LogMessageType.Error:
                        m_errors.Add(entry.AsString(false));
                        break;
                    case LogMessageType.Warning:
                        m_warnings.Add(entry.AsString(false));
                        break;
                    case LogMessageType.Information:
                        m_messages.Add(entry.AsString(false));
                        break;
                }
            }
        }
    }

    internal class BackupResults : BasicResults, IBackupResults
    {
        public long DeletedFiles { get; internal set; }
        public long DeletedFolders { get; internal set; }
        public long ModifiedFiles { get; internal set; }
        public long ExaminedFiles { get; internal set; }
        public long OpenedFiles { get; internal set; }
        public long AddedFiles { get; internal set; }
        public long SizeOfModifiedFiles { get; internal set; }
        public long SizeOfAddedFiles { get; internal set; }
        public long SizeOfExaminedFiles { get; internal set; }
        public long SizeOfOpenedFiles { get; internal set; }
        public long NotProcessedFiles { get; internal set; }
        public long AddedFolders { get; internal set; }
        public long TooLargeFiles { get; internal set; }
        public long FilesWithError { get; internal set; }
        public long ModifiedFolders { get; internal set; }
        public long ModifiedSymlinks { get; internal set; }
        public long AddedSymlinks { get; internal set; }
        public long DeletedSymlinks { get; internal set; }
        public bool PartialBackup { get; internal set; }
        public bool Dryrun { get; internal set; }

        public override OperationMode MainOperation { get { return OperationMode.Backup; } }

        public ICompactResults CompactResults { get; internal set; }
        public IVacuumResults VacuumResults { get; internal set; }
        public IDeleteResults DeleteResults { get; internal set; }
        public IRepairResults RepairResults { get; internal set; }
        public ITestResults TestResults { get; internal set; }

        public override ParsedResultType ParsedResult
        {
            get
            {
                if ((CompactResults != null && CompactResults.ParsedResult == ParsedResultType.Error) ||
                    (VacuumResults  != null && VacuumResults.ParsedResult  == ParsedResultType.Error) ||
                    (DeleteResults  != null && DeleteResults.ParsedResult  == ParsedResultType.Error) ||
                    (RepairResults  != null && RepairResults.ParsedResult  == ParsedResultType.Error) || 
                    (TestResults    != null && TestResults.ParsedResult    == ParsedResultType.Error) ||
                    (Errors != null && Errors.Any()) || FilesWithError > 0)
                {
                    return ParsedResultType.Error;
                }
                else if ((CompactResults != null && CompactResults.ParsedResult == ParsedResultType.Warning) ||
                         (VacuumResults  != null && VacuumResults.ParsedResult  == ParsedResultType.Warning) ||
                         (DeleteResults  != null && DeleteResults.ParsedResult  == ParsedResultType.Warning) ||
                         (RepairResults  != null && RepairResults.ParsedResult  == ParsedResultType.Warning) ||
                         (TestResults    != null && TestResults.ParsedResult    == ParsedResultType.Warning) ||
                         (Warnings != null && Warnings.Any()) || PartialBackup)
                {
                    return ParsedResultType.Warning;
                }
                else
                {
                    return ParsedResultType.Success;
                }
            }
        }
    }

    internal class RestoreResults : BasicResults, Library.Interface.IRestoreResults
    {
        public long RestoredFiles { get; internal set; }
        public long SizeOfRestoredFiles { get; internal set; }
        public long RestoredFolders { get; internal set; }
        public long RestoredSymlinks { get; internal set; }
        public long PatchedFiles { get; internal set; }
        public long DeletedFiles { get; internal set; }
        public long DeletedFolders { get; internal set; }
        public long DeletedSymlinks { get; internal set; }

        public override OperationMode MainOperation { get { return OperationMode.Restore; } }

        public IRecreateDatabaseResults RecreateDatabaseResults { get; internal set; }

        public override ParsedResultType ParsedResult
        {
            get
            {
                if ((RecreateDatabaseResults != null && RecreateDatabaseResults.ParsedResult == ParsedResultType.Error) ||
                    (Errors != null && Errors.Any()))
                {
                    return ParsedResultType.Error;
                }
                else if ((RecreateDatabaseResults != null && RecreateDatabaseResults.ParsedResult == ParsedResultType.Warning) ||
                         (Warnings != null && Warnings.Any()))
                {
                    return ParsedResultType.Warning;
                }
                else
                {
                    return ParsedResultType.Success;
                }
            }
        }
    }

    internal class ListResultFile : Duplicati.Library.Interface.IListResultFile
    {
        public string Path { get; private set; }
        public IEnumerable<long> Sizes { get; private set; }
        public ListResultFile(string path, IEnumerable<long> sizes)
        {
            this.Path = path;
            this.Sizes = sizes;
        }
    }

    internal class ListResultFileset : Duplicati.Library.Interface.IListResultFileset
    {
        public long Version { get; private set; }
        public int IsFullBackup { get; private set; }
        public DateTime Time { get; private set; }
        public long FileCount { get; private set; }
        public long FileSizes { get; private set; }
        public ListResultFileset(long version, int isFullBackup, DateTime time, long fileCount, long fileSizes)
        {
            this.Version = version;
            this.IsFullBackup = isFullBackup;
            this.Time = time;
            this.FileCount = fileCount;
            this.FileSizes = fileSizes;
        }
    }

    internal class ListResults : BasicResults, Duplicati.Library.Interface.IListResults
    {
        private IEnumerable<Duplicati.Library.Interface.IListResultFileset> m_filesets;
        private IEnumerable<Duplicati.Library.Interface.IListResultFile> m_files;
        public bool EncryptedFiles { get; set; }

        public void SetResult(IEnumerable<Duplicati.Library.Interface.IListResultFileset> filesets, IEnumerable<Duplicati.Library.Interface.IListResultFile> files)
        {
            m_filesets = filesets;
            m_files = files;
        }

        public IEnumerable<Duplicati.Library.Interface.IListResultFileset> Filesets { get { return m_filesets; } }
        public IEnumerable<Duplicati.Library.Interface.IListResultFile> Files { get { return m_files; } }

        public override OperationMode MainOperation { get { return OperationMode.List; } }
    }

    internal class ListAffectedResults : BasicResults, Duplicati.Library.Interface.IListAffectedResults
    {
        private IEnumerable<Duplicati.Library.Interface.IListResultFileset> m_filesets;
        private IEnumerable<Duplicati.Library.Interface.IListResultFile> m_files;
        private IEnumerable<Duplicati.Library.Interface.IListResultRemoteLog> m_logs;
        private IEnumerable<Duplicati.Library.Interface.IListResultRemoteVolume> m_volumes;

        public void SetResult(IEnumerable<Duplicati.Library.Interface.IListResultFileset> filesets, IEnumerable<Duplicati.Library.Interface.IListResultFile> files, IEnumerable<Duplicati.Library.Interface.IListResultRemoteLog> logs, IEnumerable<Duplicati.Library.Interface.IListResultRemoteVolume> volumes)
        {
            m_filesets = filesets;
            m_files = files;
            m_logs = logs;
            m_volumes = volumes;
        }

        public IEnumerable<Duplicati.Library.Interface.IListResultFileset> Filesets { get { return m_filesets; } }
        public IEnumerable<Duplicati.Library.Interface.IListResultFile> Files { get { return m_files; } }
        public IEnumerable<Duplicati.Library.Interface.IListResultRemoteLog> LogMessages { get { return m_logs; } }
        public IEnumerable<Duplicati.Library.Interface.IListResultRemoteVolume> RemoteVolumes { get { return m_volumes; } }

        public override OperationMode MainOperation { get { return OperationMode.ListAffected; } }
    }

    internal class DeleteResults : BasicResults, Duplicati.Library.Interface.IDeleteResults
    {
        [JsonIgnore]
        public IEnumerable<Tuple<long, DateTime>> DeletedSets { get; private set; }

        // ReSharper disable once UnusedMember.Global
        // This is referenced in the logs.
        public int DeletedSetsActualLength { get { return DeletedSets == null ? 0 : DeletedSets.Count(); } }

        [JsonProperty(PropertyName = "DeletedSets")]
        public IEnumerable<Tuple<long, DateTime>> LimitedDeletedSets { get { return DeletedSets?.Take(SERIALIZATION_LIMIT); } }

        public bool Dryrun { get; private set; }

        public void SetResults(IEnumerable<Tuple<long, DateTime>> deletedSets, bool dryrun)
        {
            EndTime = DateTime.UtcNow;
            DeletedSets = deletedSets;
            Dryrun = dryrun;
        }

        public override OperationMode MainOperation { get { return OperationMode.Delete; } }

        public DeleteResults() : base() { }
        public DeleteResults(BasicResults p) : base(p) { }

        private ICompactResults m_compactResults;

        public ICompactResults CompactResults
        {
            get
            {
                if (m_parent != null && this.m_parent is BackupResults results)
                    return results.CompactResults;

                return m_compactResults;
            }
            internal set
            {
                if (m_parent != null && this.m_parent is BackupResults results)
                    results.CompactResults = value;

                m_compactResults = value;
            }
        }
    }

    internal class RecreateDatabaseResults : BasicResults, Library.Interface.IRecreateDatabaseResults
    {
        public override OperationMode MainOperation { get { return OperationMode.Repair; } }

        public RecreateDatabaseResults() : base() { }
        public RecreateDatabaseResults(BasicResults p) : base(p) { }
    }

    internal class CreateLogDatabaseResults : BasicResults, Library.Interface.ICreateLogDatabaseResults
    {
        public override OperationMode MainOperation { get { return OperationMode.CreateLogDb; } }
        public string TargetPath { get; internal set; }
    }

    internal class RestoreControlFilesResults : BasicResults, Library.Interface.IRestoreControlFilesResults
    {
        public IEnumerable<string> Files { get; private set; }

        public override OperationMode MainOperation { get { return OperationMode.RestoreControlfiles; } }
        public void SetResult(IEnumerable<string> files) { this.Files = files; }
    }

    internal class ListRemoteResults : BasicResults, Library.Interface.IListRemoteResults
    {
        public IEnumerable<IFileEntry> Files { get; private set; }

        public override OperationMode MainOperation { get { return OperationMode.ListRemote; } }
        public void SetResult(IEnumerable<IFileEntry> files) { this.Files = files; }
    }

    internal class RepairResults : BasicResults, Library.Interface.IRepairResults
    {
        public override OperationMode MainOperation { get { return OperationMode.Repair; } }

        public RepairResults() : base() { }
        public RepairResults(BasicResults p) : base(p) { }
        public Library.Interface.IRecreateDatabaseResults RecreateDatabaseResults { get; internal set; }

        public override ParsedResultType ParsedResult
        {
            get
            {
                if ((RecreateDatabaseResults != null && RecreateDatabaseResults.ParsedResult == ParsedResultType.Error) ||
                    (Errors != null && Errors.Any()))
                {
                    return ParsedResultType.Error;
                }
                else if ((RecreateDatabaseResults != null && RecreateDatabaseResults.ParsedResult == ParsedResultType.Warning) ||
                         (Warnings != null && Warnings.Any()))
                {
                    return ParsedResultType.Warning;
                }
                else
                {
                    return ParsedResultType.Success;
                }
            }
        }
    }

    internal class CompactResults : BasicResults, Library.Interface.ICompactResults
    {
        public long DeletedFileCount { get; internal set; }
        public long DownloadedFileCount { get; internal set; }
        public long UploadedFileCount { get; internal set; }
        public long DeletedFileSize { get; internal set; }
        public long DownloadedFileSize { get; internal set; }
        public long UploadedFileSize { get; internal set; }
        public bool Dryrun { get; internal set; }

        public IVacuumResults VacuumResults { get; internal set; }

        public override OperationMode MainOperation { get { return OperationMode.Compact; } }

        public CompactResults() : base() { }
        public CompactResults(BasicResults p) : base(p) { }
    }

    internal class ListChangesResults : BasicResults, Library.Interface.IListChangesResults
    {
        public override OperationMode MainOperation { get { return OperationMode.ListChanges; } }

        public DateTime BaseVersionTimestamp { get; internal set; }
        public DateTime CompareVersionTimestamp { get; internal set; }
        public long BaseVersionIndex { get; internal set; }
        public long CompareVersionIndex { get; internal set; }

        public IEnumerable<Tuple<ListChangesChangeType, ListChangesElementType, string>> ChangeDetails { get; internal set; }

        public long AddedFolders { get; internal set; }
        public long AddedSymlinks { get; internal set; }
        public long AddedFiles { get; internal set; }

        public long DeletedFolders { get; internal set; }
        public long DeletedSymlinks { get; internal set; }
        public long DeletedFiles { get; internal set; }

        public long ModifiedFolders { get; internal set; }
        public long ModifiedSymlinks { get; internal set; }
        public long ModifiedFiles { get; internal set; }

        public long PreviousSize { get; internal set; }
        public long CurrentSize { get; internal set; }

        public long AddedSize { get; internal set; }
        public long DeletedSize { get; internal set; }

        public void SetResult(
            DateTime baseVersionTime, long baseVersionIndex, DateTime compareVersionTime, long compareVersionIndex,
            long addedFolders, long addedSymlinks, long addedFiles,
            long deletedFolders, long deletedSymlinks, long deletedFiles,
            long modifiedFolders, long modifiedSymlinks, long modifiedFiles,
            long addedSize, long deletedSize, long previousSize, long currentSize,
            IEnumerable<Tuple<ListChangesChangeType, ListChangesElementType, string>> changeDetails
        )
        {
            this.BaseVersionTimestamp = baseVersionTime;
            this.BaseVersionIndex = baseVersionIndex;
            this.CompareVersionTimestamp = compareVersionTime;
            this.CompareVersionIndex = compareVersionIndex;

            this.AddedFolders = addedFolders;
            this.AddedSymlinks = addedSymlinks;
            this.AddedFiles = addedFiles;

            this.DeletedFolders = deletedFolders;
            this.DeletedSymlinks = deletedSymlinks;
            this.DeletedFiles = deletedFiles;

            this.ModifiedFolders = modifiedFolders;
            this.ModifiedSymlinks = modifiedSymlinks;
            this.ModifiedFiles = modifiedFiles;

            this.AddedSize = addedSize;
            this.DeletedSize = deletedSize;

            this.PreviousSize = previousSize;
            this.CurrentSize = currentSize;

            this.ChangeDetails = changeDetails;
        }
    }

    internal class TestResults : BasicResults, ITestResults
    {
        public TestResults() : base() { }
        public TestResults(BasicResults p) : base(p) { }

        public override OperationMode MainOperation { get { return OperationMode.Test; } }

        private readonly List<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>> m_verifications = new List<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>>();
        [JsonIgnore]
        public IEnumerable<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>> Verifications { get { return m_verifications; } }

        // ReSharper disable once UnusedMember.Global
        // This is referenced in the logs.
        public int VerificationsActualLength { get { return Verifications == null ? 0 : Verifications.Count(); } }

        [JsonProperty(PropertyName = "Verifications")]
        public IEnumerable<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>> LimitedVerifications { get { return Verifications?.Take(SERIALIZATION_LIMIT); } }

        public KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>> AddResult(string volume, IEnumerable<KeyValuePair<TestEntryStatus, string>> changes)
        {
            var res = new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(volume, changes);
            m_verifications.Add(res);
            return res;
        }
    }

    internal class TestFilterResults : BasicResults, ITestFilterResults
    {
        public long FileCount { get; set; }
        public long FileSize { get; set; }
        public override OperationMode MainOperation { get { return OperationMode.TestFilters; } }

    }

    internal class SystemInfoResults : BasicResults, ISystemInfoResults
    {
        public override OperationMode MainOperation { get { return OperationMode.SystemInfo; } }
        public IEnumerable<string> Lines { get; set; }
    }

    internal class PurgeFilesResults : BasicResults, IPurgeFilesResults
    {
        public PurgeFilesResults() : base() { }
        public PurgeFilesResults(BasicResults p) : base(p) { }

        public override OperationMode MainOperation { get { return OperationMode.PurgeFiles; } }
        public long RemovedFileCount { get; set; }
        public long RemovedFileSize { get; set; }
        public long RewrittenFileLists { get; set; }

        public ICompactResults CompactResults { get; set; }
    }

    internal class ListBrokenFilesResults : BasicResults, IListBrokenFilesResults
    {
        public override OperationMode MainOperation { get { return OperationMode.ListBrokenFiles; } }
        public IEnumerable<Tuple<long, DateTime, IEnumerable<Tuple<string, long>>>> BrokenFiles { get; set; }
    }

    internal class PurgeBrokenFilesResults : BasicResults, IPurgeBrokenFilesResults
    {
        public override OperationMode MainOperation { get { return OperationMode.PurgeBrokenFiles; } }
        public IPurgeFilesResults PurgeResults { get; set; }
        public IDeleteResults DeleteResults { get; set; }
    }

    internal class SendMailResults : BasicResults, ISendMailResults
    {
        public override OperationMode MainOperation { get { return OperationMode.SendMail; } }
        public IEnumerable<string> Lines { get; set; }
    }

    internal class VacuumResults : BasicResults, IVacuumResults
    {
        public VacuumResults() : base() { }
        public VacuumResults(BasicResults p) : base(p) { }

        public override OperationMode MainOperation { get { return OperationMode.Vacuum; } }
    }
}

