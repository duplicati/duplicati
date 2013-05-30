//  Copyright (C) 2011, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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
    }
    
    public interface IBackendWriter : ILogWriter
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
    }
        
    
    internal interface ISetCommonOptions : ILogWriter
    {
        bool VerboseErrors { set; }
        bool QuietConsole { set; }
        bool VerboseOutput { get; set; }
        
        DateTime EndTime { set; }
        DateTime BeginTime { set; }
        
        void SetDatabase(LocalDatabase db);
        
        OperationMode MainOperation { get; }
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
                if (action == BackendActionType.Put)
                    base.AddMessage(string.Format("Uploading file ({0}) ...", Library.Utility.Utility.FormatSizeString(size)));
                else if (action == BackendActionType.Get)
                    base.AddMessage(string.Format("Downloading file ({0}) ...", size < 0 ? "unknown" : Library.Utility.Utility.FormatSizeString(size)));
                else if (action == BackendActionType.List)
                    base.AddMessage("Listing remote folder ...");
                else if (action == BackendActionType.CreateFolder)
                    base.AddMessage("Creating remote folder ...");
                else if (action == BackendActionType.Delete)
                    base.AddMessage(string.Format("Deleting file ({0}) ...", size < 0 ? "unknown" : Library.Utility.Utility.FormatSizeString(size)));
                
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
                        base.AddMessage("Created remote folder");
                        break;
                    case BackendActionType.List:
                        base.AddMessage(string.Format("Listed remote folder ({0} files)", size));
                        break;
                    case BackendActionType.Delete:
                        System.Threading.Interlocked.Increment(ref m_filesDeleted);
                        base.AddMessage(string.Format("Deleted file ({0})", size < 0 ? "unknown" : Library.Utility.Utility.FormatSizeString(size)));
                        break;
                    case BackendActionType.Get:
                        System.Threading.Interlocked.Increment(ref m_filesDownloaded);
                        System.Threading.Interlocked.Add(ref m_bytesDownloaded, size);
                        base.AddMessage(string.Format("Downloaded file ({0})", size < 0 ? "unknown" : Library.Utility.Utility.FormatSizeString(size)));
                        break;
                    case BackendActionType.Put:
                        System.Threading.Interlocked.Increment(ref m_filesUploaded);
                        System.Threading.Interlocked.Add(ref m_bytesUploaded, size);
                        base.AddMessage(string.Format("Uploaded file ({0})", Library.Utility.Utility.FormatSizeString(size)));
                        break;
                }
            }
        }
    }
    
    internal abstract class BasicResults : IBasicResults, ILogWriter, ISetCommonOptions
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
        
        public bool VerboseErrors { get; set; }
        public bool VerboseOutput { get; set; }
        public bool QuietConsole { get; set; }
        
        public DateTime EndTime { get; set; }
        public DateTime BeginTime { get; set; }
        public TimeSpan Duration { get { return EndTime - BeginTime; } }
        
        public abstract OperationMode MainOperation { get; }
        
        protected Library.Utility.FileBackedStringList m_messages;
        protected Library.Utility.FileBackedStringList m_warnings;
        protected Library.Utility.FileBackedStringList m_errors;
        protected Library.Utility.FileBackedStringList m_retryAttempts;
        
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

        public void AddDryrunMessage(string message)
        {
            if (m_parent != null)
                m_parent.AddDryrunMessage(message);
            else
                Console.WriteLine(string.Format("[Dryrun]: {0}", message));
        }
               
        public void AddVerboseMessage(string message, params object[] args)
        {
            if (m_parent != null)
                m_parent.AddVerboseMessage(message, args);
            else if (VerboseOutput)
                Console.WriteLine(message, args);
        }
        
        public void AddMessage(string message)
        { 
            if (m_parent != null)
                m_parent.AddMessage(message);
            else
            {
                lock(m_lock)
                {
                    m_messages.Add(message);
            
                    if (!QuietConsole)
                        Console.WriteLine(message);
                
                    Logging.Log.WriteMessage(message, Duplicati.Library.Logging.LogMessageType.Information);
            
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
                var s = ex == null ? message : string.Format("{0} => {1}", message, VerboseErrors ? ex.ToString() : ex.Message);
                m_warnings.Add(s);

                if (!QuietConsole)
                    Console.WriteLine(s);
                
                Logging.Log.WriteMessage(message, Duplicati.Library.Logging.LogMessageType.Warning, ex);

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
                var s = ex == null ? message : string.Format("{0} => {1}", message, VerboseErrors ? ex.ToString() : ex.Message);
                m_retryAttempts.Add(s);

                if (!QuietConsole)
                    Console.WriteLine(s);
                
                Logging.Log.WriteMessage(message, Duplicati.Library.Logging.LogMessageType.Warning, ex);

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
                var s = ex == null ? message : string.Format("{0} => {1}", message, VerboseErrors ? ex.ToString() : ex.Message);
                m_errors.Add(s);
            
                if (!QuietConsole)
                    Console.WriteLine(s);

                Logging.Log.WriteMessage(message, Duplicati.Library.Logging.LogMessageType.Error, ex);

                if (m_db != null && !m_db.IsDisposed)
                    LogDbMessage("Error", message, ex);
            }
        }
        
        public IEnumerable<string> Messages { get { return m_messages; } }
        public IEnumerable<string> Warnings { get { return m_warnings; } }
        public IEnumerable<string> Errors { get { return m_errors; } }
        
        public BasicResults() 
        { 
            this.BeginTime = DateTime.Now; 
            this.m_parent = null;
            m_messages = new Library.Utility.FileBackedStringList();
            m_warnings = new Library.Utility.FileBackedStringList();
            m_errors = new Library.Utility.FileBackedStringList();
            m_retryAttempts = new Library.Utility.FileBackedStringList();
            m_dbqueue = new Queue<DbMessage>();
            m_backendStatistics = new BackendWriter(this);
            m_callerThread = System.Threading.Thread.CurrentThread;
        }

        public BasicResults(BasicResults p)
        { 
            this.BeginTime = DateTime.Now; 
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
        
        
        public override string ToString()
        {
            return Library.Utility.Utility.PrintSerializeObject(this).ToString();
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
        
        public void SetResult(IEnumerable<Duplicati.Library.Interface.IListResultFileset> filesets, IEnumerable<Duplicati.Library.Interface.IListResultFile> files)
        {
            m_filesets = filesets;
            m_files = files;
        }
        
        public IEnumerable<Duplicati.Library.Interface.IListResultFileset> Filesets { get { return m_filesets; } }
        public IEnumerable<Duplicati.Library.Interface.IListResultFile> Files { get { return m_files; } }
        
        public override OperationMode MainOperation { get { return OperationMode.List; } }
    }

    internal class DeleteResults : BasicResults, Duplicati.Library.Interface.IDeleteResults
    {
        public IEnumerable<DateTime> DeletedSets { get; private set; }
        public bool Dryrun { get; private set; }
        
        public void SetResults(IEnumerable<DateTime> deletedSets, bool dryrun)
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
    }   
    
    internal class RestoreControlFilesResults : BasicResults, Library.Interface.IRestoreControlFilesResults
    {
        public override OperationMode MainOperation { get { return OperationMode.RestoreControlfiles; } }
    }   

    internal class RepairResults : BasicResults, Library.Interface.IRepairResults
    {
        public override OperationMode MainOperation { get { return OperationMode.Repair; } }
        
        public RepairResults() : base() { }
        public RepairResults(BasicResults p) : base(p) { }
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

}

