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
using Duplicati.Library.Interface;
using System.Collections.Generic;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main
{
    public interface ILogWriter
    {
        bool VerboseOutput { get; }
        void AddVerboseMessage(string message, params object[] args);
        void AddMessage(string message);
        void AddWarning(string message, Exception ex);
        void AddError(string message, Exception ex);
        void AddDryrunMessage(string message);
    }
    
    internal interface IBackendWriter : ILogWriter
    {
        void AddRetryAttempt(string message, Exception ex);
        
        long UnknownFileSize { set; }
        long UnknownFileCount { set; }
        long KnownFileCount { set; }
        long KnownFileSize { set; }
        DateTime LastBackupDate { set; }
        long BackupListCount { set; }
        long TotalQuotaSpace { set; }
        long FreeQuotaSpace { set; }
        long AssignedQuotaSpace { set; }

        /// <summary>
        /// The backend sends this event when performing an action
        /// </summary>
        /// <param name="action">The action performed</param>
        /// <param name="type">The event type</param>
        /// <param name="path">Path to the resource</param>
        /// <param name="size">Size of the file or progress</param>
        void SendEvent(BackendActionType action, BackendEventType type, string path, long size);
        
        /// <summary>
        /// Gets the backend progress updater.
        /// </summary>
        /// <value>The backend progress updater.</value>
        IBackendProgressUpdater BackendProgressUpdater { get; }
    }
    
    internal interface ISetCommonOptions : ILogWriter
    {
        bool VerboseOutput { set; }
        bool VerboseErrors { set; }
        
        DateTime EndTime { set; }
        DateTime BeginTime { set; }
        
        void SetDatabase(LocalDatabase db);
        
        OperationMode MainOperation { get; }
        IMessageSink MessageSink { set; }        
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
        
        public void AddNumberOfRemoteCalls(long count)
        {
            System.Threading.Interlocked.Add(ref m_remoteCalls, count);
        }
        
        public void AddBytesUploaded(long count)
        {
            System.Threading.Interlocked.Add(ref m_bytesUploaded, count);
        }
        
        public void AddBytesDownloaded(long count)
        {
            System.Threading.Interlocked.Add(ref m_bytesDownloaded, count);
        }
        
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
        
        public override OperationMode MainOperation { get { return m_parent.MainOperation; } }
                
        public void SendEvent(BackendActionType action, BackendEventType type, string path, long size)
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
            
            base.AddBackendEvent(action, type, path, size);
        }
        
        IBackendProgressUpdater IBackendWriter.BackendProgressUpdater { get { return base.BackendProgressUpdater; } }   
    }
    
    public interface ITaskControl
    {
        void Pause();
        void Resume();
        void Stop();
        void Abort();
    }
    
    internal enum TaskControlState
    {
        Run,
        Pause,
        Stop,
        Abort
    }
    
    internal abstract class BasicResults : IBasicResults, ILogWriter, ISetCommonOptions, ITaskControl
    {
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
        protected Queue<DbMessage> m_dbqueue;
                
        private TaskControlState m_controlState = TaskControlState.Run;
        private System.Threading.ManualResetEvent m_pauseEvent = new System.Threading.ManualResetEvent(true);
        
        private bool m_verboseOutput;
        private bool m_verboseErrors;
        
        public bool VerboseOutput
        { 
            get
            { 
                if (m_parent != null)
                    return m_parent.VerboseOutput;
                else
                    return m_verboseOutput;
            } 
            set
            {
                if (m_parent != null)
                    m_parent.VerboseOutput = value;
                else
                    m_verboseOutput = value;
            }
        }
        public bool VerboseErrors
        { 
            get
            { 
                if (m_parent != null)
                    return m_parent.VerboseErrors;
                else
                    return m_verboseErrors;
            } 
            set
            {
                if (m_parent != null)
                    m_parent.VerboseErrors = value;
                else
                    m_verboseErrors = value;
            }
        }
        
        public DateTime EndTime { get; set; }
        public DateTime BeginTime { get; set; }
        public TimeSpan Duration { get { return EndTime - BeginTime; } }
        
        public abstract OperationMode MainOperation { get; }
        
        protected Library.Utility.FileBackedStringList m_messages;
        protected Library.Utility.FileBackedStringList m_warnings;
        protected Library.Utility.FileBackedStringList m_errors;
        protected Library.Utility.FileBackedStringList m_retryAttempts;
        
        protected IMessageSink m_messageSink;
        public IMessageSink MessageSink
        {
            get { return m_messageSink; }
            set 
            {
                m_messageSink = value;
                if (value != null)
                {
                    m_messageSink.OperationProgress = this.OperationProgressUpdater;
                    m_messageSink.BackendProgress = this.BackendProgressUpdater;
                }
            }
        }
        
        protected internal IOperationProgressUpdaterAndReporter m_operationProgressUpdater;
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
        
        protected internal IBackendProgressUpdaterAndReporter m_backendProgressUpdater;
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
                lock(m_lock)
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
                lock(m_lock)
                {
                    while (m_dbqueue.Count > 0)
                    {
                        var el = m_dbqueue.Dequeue();
                        m_db.LogMessage(el.Type, el.Message, el.Exception, null);
                    }
                }
            }
        }
         
        private void LogDbMessage(string type, string message, Exception ex)
        {
            if (System.Threading.Thread.CurrentThread != m_callerThread)
            {
                m_dbqueue.Enqueue(new DbMessage(type, message, ex));
            }
            else
            {
                FlushLog();
                m_db.LogMessage("Message", message, ex, null);
            }
        }
        
        public void AddBackendEvent(BackendActionType action, BackendEventType type, string path, long size)
        {
            if (m_parent != null)
            {
                m_parent.AddBackendEvent(action, type, path, size);
            }
            else
            {
                if (type == BackendEventType.Started)
                    this.BackendProgressUpdater.StartAction(action, path, size);

                Logging.Log.WriteMessage(string.Format("Backend event: {0} - {1}: {2} ({3})", action, type, path, size <= 0 ? "" : Library.Utility.Utility.FormatSizeString(size)), Duplicati.Library.Logging.LogMessageType.Information);

                if (MessageSink != null)
                    MessageSink.BackendEvent(action, type, path, size);
            }
                
        }

        public void AddDryrunMessage(string message)
        {
            if (m_parent != null)
                m_parent.AddDryrunMessage(message);
            else
            {
                Logging.Log.WriteMessage(message, Duplicati.Library.Logging.LogMessageType.Information);
                if (MessageSink != null)
                    MessageSink.DryrunEvent(message);
            }
        }
               
        public void AddVerboseMessage(string message, params object[] args)
        {
            if (m_parent != null)
                m_parent.AddVerboseMessage(message, args);
            else
            {
                if (Logging.Log.LogLevel == Duplicati.Library.Logging.LogMessageType.Profiling || VerboseOutput)
                    Logging.Log.WriteMessage(string.Format(message, args), Duplicati.Library.Logging.LogMessageType.Information);
                    
                if (MessageSink != null)
                    MessageSink.VerboseEvent(message, args);
            }
        }
        
        public void AddMessage(string message)
        { 
            if (m_parent != null)
                m_parent.AddMessage(message);
            else
            {
                lock(m_lock)
                {
                    Logging.Log.WriteMessage(message, Duplicati.Library.Logging.LogMessageType.Information);
                    m_messages.Add(message);
            
                    if (MessageSink != null)
                        MessageSink.MessageEvent(message);
                            
                    if (m_db != null && !m_db.IsDisposed)
                        LogDbMessage("Message", message, null);
                }
            }
        }
        
        public void AddWarning(string message, Exception ex)
        {
            if (m_parent != null)
                m_parent.AddWarning(message, ex);
            else
            {
                Logging.Log.WriteMessage(message, Duplicati.Library.Logging.LogMessageType.Warning, ex);
                
                var s = ex == null ? message : string.Format("{0} => {1}", message, VerboseErrors ? ex.ToString() : ex.Message);
                m_warnings.Add(s);
                
                if (MessageSink != null)
                    MessageSink.WarningEvent(message, ex);
                
                if (m_db != null && !m_db.IsDisposed)
                    LogDbMessage("Warning", message, ex);
            }
        }

        public void AddRetryAttempt(string message, Exception ex)
        {
            if (m_parent != null)
                m_parent.AddRetryAttempt(message, ex);
            else
            {
                Logging.Log.WriteMessage(message, Duplicati.Library.Logging.LogMessageType.Warning, ex);
                
                var s = ex == null ? message : string.Format("{0} => {1}", message, VerboseErrors ? ex.ToString() : ex.Message);
                m_retryAttempts.Add(s);

                if (MessageSink != null)
                    MessageSink.RetryEvent(message, ex);
                
                if (m_db != null && !m_db.IsDisposed)
                    LogDbMessage("Retry", message, ex);
            }
        }
        
        public void AddError(string message, Exception ex)
        {
            if (m_parent != null)
                m_parent.AddError(message, ex);
            else
            {
                Logging.Log.WriteMessage(message, Duplicati.Library.Logging.LogMessageType.Error, ex);
                
                var s = ex == null ? message : string.Format("{0} => {1}", message, VerboseErrors ? ex.ToString() : ex.Message);
                m_errors.Add(s);
            
                if (MessageSink != null)
                    MessageSink.ErrorEvent(message, ex);

                if (m_db != null && !m_db.IsDisposed)
                    LogDbMessage("Error", message, ex);
            }
        }
        
        public IEnumerable<string> Messages { get { return m_messages; } }
        public IEnumerable<string> Warnings { get { return m_warnings; } }
        public IEnumerable<string> Errors { get { return m_errors; } }
        
        public BasicResults() 
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
        }

        public BasicResults(BasicResults p)
        { 
            this.BeginTime = DateTime.UtcNow; 
            this.m_parent = p;
        }
        
        protected IBackendStatstics m_backendStatistics;
        public IBackendStatstics BackendStatistics
        { 
            get
            {
                if (this.m_parent != null)
                    return this.m_parent.BackendStatistics;
                    
                return m_backendStatistics;
            }
        }
        
        public IBackendWriter BackendWriter { get { return (IBackendWriter)this.BackendStatistics; } }
        
        public event Action<TaskControlState> StateChangedEvent;
        
        /// <summary>
        /// Request that this task pauses.
        /// </summary>
        public void Pause()
        {
            lock(m_lock)
                if (m_controlState == TaskControlState.Run)
                {
                    m_pauseEvent.Reset();
                    m_controlState = TaskControlState.Pause;
                }
            
            if (StateChangedEvent != null)
                StateChangedEvent(m_controlState);
        }
        
        /// <summary>
        /// Request that this task resumes.
        /// </summary>
        public void Resume()
        {
            lock(m_lock)
                if (m_controlState == TaskControlState.Pause)
                {
                    m_pauseEvent.Set();
                    m_controlState = TaskControlState.Run;
                }
            
            if (StateChangedEvent != null)
                StateChangedEvent(m_controlState);
        }
        
        /// <summary>
        /// Request that this task stops.
        /// </summary>
        public void Stop() 
        {
            lock(m_lock)
                if (m_controlState != TaskControlState.Abort)
                {
                    m_controlState = TaskControlState.Stop;
                    m_pauseEvent.Set();
                }
            
            if (StateChangedEvent != null)
                StateChangedEvent(m_controlState);
        }
        
        /// <summary>
        /// Request that this task aborts.
        /// </summary>
        public void Abort()
        {
            lock(m_lock)
            {
                m_controlState = TaskControlState.Abort;
                m_pauseEvent.Set();
            }
            
            if (StateChangedEvent != null)
                StateChangedEvent(m_controlState);
        }
        
        /// <summary>
        /// Helper method that the current running task can call to obtain the current state
        /// </summary>
        public TaskControlState TaskControlRendevouz()
        {
            // If we are paused, go into pause mode
            m_pauseEvent.WaitOne();
            
            // If we are aborted, throw exception
            if (m_controlState == TaskControlState.Abort)
                System.Threading.Thread.CurrentThread.Abort();
            
            return m_controlState;
        }
        
        /// <summary>
        /// Returns a <see cref="System.String"/> that represents the current <see cref="Duplicati.Library.Main.BasicResults"/>.
        /// </summary>
        /// <returns>A <see cref="System.String"/> that represents the current <see cref="Duplicati.Library.Main.BasicResults"/>.</returns>
        public override string ToString()
        {
            return Library.Utility.Utility.PrintSerializeObject(
                this, 
                filter: x =>
                    !typeof(IBackendProgressUpdater).IsAssignableFrom(x.PropertyType) &&
                    !typeof(IMessageSink).IsAssignableFrom(x.PropertyType) &&
                    !typeof(ILogWriter).IsAssignableFrom(x.PropertyType),
                recurseobjects: true
            ).ToString();
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
        public IDeleteResults DeleteResults { get; internal set; }
        public IRepairResults RepairResults { get; internal set; }        
        public ITestResults TestResults { get; internal set; }        
    }
    
    internal class RestoreResults : BasicResults, Library.Interface.IRestoreResults
    {
        public long FilesRestored { get; internal set; }
        public long SizeOfRestoredFiles { get; internal set; }
        public long FoldersRestored { get; internal set; }
        public long SymlinksRestored { get; internal set; }
        public long FilesPatched { get; internal set; }
        public long FilesDeleted { get; internal set; }
        public long FoldersDeleted { get; internal set; }
        public long SymlinksDeleted { get; internal set; }
        
        public override OperationMode MainOperation { get { return OperationMode.Restore; } }
        
        public IRecreateDatabaseResults RecreateDatabaseResults { get; internal set; }
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
        public DateTime Time { get; private set; }
        public long FileCount { get; private set; }
        public long FileSizes { get; private set; }
        public ListResultFileset(long version, DateTime time, long fileCount, long fileSizes)
        {
            this.Version = version;
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
        public IEnumerable<Tuple<long, DateTime>> DeletedSets { get; private set; }
        public bool Dryrun { get; private set; }
        
        public void SetResults(IEnumerable<Tuple<long, DateTime>> deletedSets, bool dryrun)
        {
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
                if (m_parent != null && m_parent is BackupResults)
                    return ((BackupResults)m_parent).CompactResults;

                return m_compactResults;
            }
            internal set
            {
                if (m_parent != null && m_parent is BackupResults)
                    ((BackupResults)m_parent).CompactResults = value;
                    
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
        public IEnumerable<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>> Changes { get { return m_changes; } }
        private List<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>> m_changes = new List<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>>();
        
        public KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>> AddResult(string volume, IEnumerable<KeyValuePair<TestEntryStatus, string>> changes)
        {
            var res = new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(volume, changes);
            m_changes.Add(res);
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

}

