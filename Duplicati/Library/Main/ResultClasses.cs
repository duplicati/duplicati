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
using Duplicati.Library.Logging;
using System.Linq;

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
        void WriteLogMessageDirect(string message, LogMessageType type, Exception ex);
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
        
        DateTime EndTime { get; set; }
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
    
    internal abstract class BasicResults : IBasicResults, ILogWriter, ISetCommonOptions, ITaskControl, Logging.ILog
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

        public DateTime EndTime { get; set; }
        public DateTime BeginTime { get; set; }
        public TimeSpan Duration { get { return EndTime.Ticks == 0 ? new TimeSpan(0) : EndTime - BeginTime; } }
        
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

        private static bool m_is_reporting = false;

        public void AddBackendEvent(BackendActionType action, BackendEventType type, string path, long size)
        {
            if (m_parent != null)
            {
                m_parent.AddBackendEvent(action, type, path, size);
            }
            else
            {
                lock(Logging.Log.Lock)
                {
                    if (m_is_reporting)
                        return;

                    try
                    {
                        m_is_reporting = true;
                        if (type == BackendEventType.Started)
                            this.BackendProgressUpdater.StartAction(action, path, size);

                        Logging.Log.WriteMessage(string.Format("Backend event: {0} - {1}: {2} ({3})", action, type, path, size <= 0 ? "" : Library.Utility.Utility.FormatSizeString(size)), Duplicati.Library.Logging.LogMessageType.Information, null);

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

        public void AddDryrunMessage(string message)
        {
            if (m_parent != null)
                m_parent.AddDryrunMessage(message);
            else
            {
                lock(Logging.Log.Lock)
                {
                    if (m_is_reporting)
                        return;

                    try
                    {
                        m_is_reporting = true;
                        Logging.Log.WriteMessage(message, Duplicati.Library.Logging.LogMessageType.Information, null);
                        if (MessageSink != null)
                            MessageSink.DryrunEvent(message);
                    }
                    finally
                    {
                        m_is_reporting = false;
                    }
                }
            }
        }

        public void AddVerboseMessage(string message, params object[] args)
        {
            if (m_parent != null)
                m_parent.AddVerboseMessage(message, args);
            else
            {
                lock(Logging.Log.Lock)
                {
                    if (m_is_reporting)
                        return;

                    try
                    {
                        m_is_reporting = true;
                        Logging.Log.WriteMessage(string.Format(message, args), Duplicati.Library.Logging.LogMessageType.Profiling, null);

                        if (MessageSink != null)
                            MessageSink.VerboseEvent(message, args);
                    }
                    finally
                    {
                        m_is_reporting = false;
                    }
                }
            }
        }

        public void AddMessage(string message)
        { 
            if (m_parent != null)
                m_parent.AddMessage(message);
            else
            {
                lock(Logging.Log.Lock)
                {
                    if (m_is_reporting)
                        return;

                    try
                    {
                        m_is_reporting = true;
                        Logging.Log.WriteMessage(message, Duplicati.Library.Logging.LogMessageType.Information, null);

                        m_messages.Add(message);

                        if (MessageSink != null)
                            MessageSink.MessageEvent(message);

                        lock(m_lock)
                            if (m_db != null && !m_db.IsDisposed)
                                LogDbMessage("Message", message, null);
                    }
                    finally
                    {
                        m_is_reporting = false;
                    }
                }
            }
        }

        public void AddWarning(string message, Exception ex)
        {
            if (m_parent != null)
                m_parent.AddWarning(message, ex);
            else
            {
                lock(Logging.Log.Lock)
                {
                    if (m_is_reporting)
                        return;

                    try
                    {
                        m_is_reporting = true;
                        Logging.Log.WriteMessage(message, Duplicati.Library.Logging.LogMessageType.Warning, ex);

                        var s = ex == null ? message : string.Format("{0} => {1}", message, VerboseErrors ? ex.ToString() : ex.Message);
                        m_warnings.Add(s);

                        if (MessageSink != null)
                            MessageSink.WarningEvent(message, ex);

                        lock(m_lock)
                            if (m_db != null && !m_db.IsDisposed)
                                LogDbMessage("Warning", message, ex);
                    }
                    finally
                    {
                        m_is_reporting = false;
                    }
                }
            }
        }

        public void AddRetryAttempt(string message, Exception ex)
        {
            if (m_parent != null)
                m_parent.AddRetryAttempt(message, ex);
            else
            {
                lock(Logging.Log.Lock)
                {
                    if (m_is_reporting)
                        return;

                    try
                    {
                        m_is_reporting = true;
                        Logging.Log.WriteMessage(message, Duplicati.Library.Logging.LogMessageType.Warning, ex);

                        var s = ex == null ? message : string.Format("{0} => {1}", message, VerboseErrors ? ex.ToString() : ex.Message);
                        m_retryAttempts.Add(s);

                        if (MessageSink != null)
                            MessageSink.RetryEvent(message, ex);

                        lock(m_lock)
                            if (m_db != null && !m_db.IsDisposed)
                                LogDbMessage("Retry", message, ex);
                    }
                    finally
                    {
                        m_is_reporting = false;
                    }
                }
            }
        }

        public void AddError(string message, Exception ex)
        {
            if (m_parent != null)
                m_parent.AddError(message, ex);
            else
            {
                lock(Logging.Log.Lock)
                {
                    if (m_is_reporting)
                        return;

                    try
                    {
                        m_is_reporting = true;
                        Logging.Log.WriteMessage(message, Duplicati.Library.Logging.LogMessageType.Error, ex);

                        var s = ex == null ? message : string.Format("{0} => {1}", message, VerboseErrors ? ex.ToString() : ex.Message);
                        m_errors.Add(s);

                        if (MessageSink != null)
                            MessageSink.ErrorEvent(message, ex);

                        lock(m_lock)
                            if (m_db != null && !m_db.IsDisposed)
                                LogDbMessage("Error", message, ex);
                    }
                    finally
                    {
                        m_is_reporting = false;
                    }
                }
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
        public void Stop() 
        {
            if (m_parent != null)
                m_parent.Stop();
            else
            {
                lock (m_lock)
                    if (m_controlState != TaskControlState.Abort)
                    {
                        m_controlState = TaskControlState.Stop;
                        m_pauseEvent.Set();
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
                    System.Threading.Thread.CurrentThread.Abort();

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
                    !typeof(ILogWriter).IsAssignableFrom(prop.PropertyType) &&
                    prop.Name != "VerboseOutput" &&
                    prop.Name != "VerboseErrors" &&
                    !(prop.Name == "MainOperation" && item is BackendWriter) &&
                    !(prop.Name == "EndTime" && item is BackendWriter) &&
                    !(prop.Name == "Duration" && item is BackendWriter) &&
                    !(prop.Name == "BeginTime" && item is BackendWriter),
                recurseobjects: true
            ).ToString();
        }

        public void WriteMessage(string message, LogMessageType type, Exception exception)
        {
            switch (type)
            {
                case LogMessageType.Error:
                    AddError(message, exception);
                    break;
                case LogMessageType.Warning:
                    AddWarning(message, exception);
                    break;
                case LogMessageType.Profiling:
                    if (Log.LogLevel == LogMessageType.Profiling && VerboseOutput)
                        AddVerboseMessage(message, new object[0]);
                    break;
                default:
                    AddMessage(message);
                    break;
            }
        }

        /// <summary>
        /// Writes a message to the log, bypassing injection as normal messages
        /// </summary>
        /// <param name="message">The message to write to the log.</param>
        /// <param name="type">Type.</param>
        /// <param name="exception">Exception.</param>
        public void WriteLogMessageDirect(string message, LogMessageType type, Exception exception)
        {
            if (m_parent != null)
                m_parent.WriteLogMessageDirect(message, type, exception);
            else
            {
                lock (Logging.Log.Lock)
                {
                    try
                    {
                        m_is_reporting = true;
                        WriteMessage(message, type, exception);
                    }
                    finally
                    {
                        m_is_reporting = false;
                    }
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
        public IDeleteResults DeleteResults { get; internal set; }
        public IRepairResults RepairResults { get; internal set; }
        public ITestResults TestResults { get; internal set; }

        public override ParsedResultType ParsedResult
        {
            get
            {
                if ((Errors != null && Errors.Any()) || FilesWithError > 0)
                    return ParsedResultType.Error;
                else if ((Warnings != null && Warnings.Any()) || PartialBackup)
                    return ParsedResultType.Warning;
                else
                    return ParsedResultType.Success;                    
            }
        }
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

        public override ParsedResultType ParsedResult
        {
            get
            {
                if (Errors != null && Errors.Any())
                    return ParsedResultType.Error;
                else if ((Warnings != null && Warnings.Any()) || FilesRestored == 0)
                    return ParsedResultType.Warning;
                else
                    return ParsedResultType.Success;
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
        public IEnumerable<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>> Verifications { get { return m_verifications; } }
        private List<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>> m_verifications = new List<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>>();
        
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
}

