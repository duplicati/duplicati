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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Operation.Common;
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
        bool Interrupted { get; set; }
        bool Fatal { get; set; }
        DateTime EndTime { get; set; }
        DateTime BeginTime { get; set; }
        IMessageSink MessageSink { get; set; }
        OperationMode MainOperation { get; }
    }

    internal interface ITaskControlProvider
    {
        ITaskControl TaskControl { get; }
    }

    internal interface IBackendWriterProvider
    {
        IBackendWriter BackendWriter { get; }
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
        public long KnownFilesets { get; set; }
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


    internal abstract class BasicResults : IBasicResults, ISetCommonOptions, Logging.ILogDestination, ITaskControlProvider, IBackendWriterProvider
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

        protected readonly BasicResults m_parent;
        protected System.Threading.Thread m_callerThread;
        protected readonly SemaphoreSlim m_lock = new(1, 1);
        protected readonly Queue<DbMessage> m_dbqueue;

        public virtual ParsedResultType ParsedResult
        {
            get
            {
                if (Fatal)
                    return ParsedResultType.Fatal;
                if (Errors != null && Errors.Any())
                    return ParsedResultType.Error;
                else if (Warnings != null && Warnings.Any())
                    return ParsedResultType.Warning;
                else
                    return ParsedResultType.Success;
            }
        }
        public bool Interrupted { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool Fatal { get; set; }

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

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
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

        /// <summary>
        /// Flushes the log messages to the database.
        /// </summary>
        /// <param name="db">The database to flush the log messages to.</param>
        /// <param name="token">The cancellation token to use.</param>
        /// <returns>A task that completes when the log messages have been flushed.</returns>
        public async Task FlushLog(LocalDatabase db, CancellationToken token)
        {
            if (m_parent != null)
                await m_parent.FlushLog(db, token).ConfigureAwait(false);
            else
            {
                await m_lock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    while (m_dbqueue.Count > 0)
                    {
                        var el = m_dbqueue.Dequeue();
                        await db
                            .LogMessage(el.Type, el.Message, el.Exception, token)
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_lock.Release();
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

                        // Because the backend manager keeps track of the current transfer, we don't need to do it here.
                        // if (type == BackendEventType.Started && updateProgress)
                        //     this.BackendProgressUpdater.StartAction(action, path, size);

                        Logging.Log.WriteInformationMessage(LOGTAG, "BackendEvent", "Backend event: {0} - {1}: {2} ({3})", action, type, path, size <= 0 ? "" : Library.Utility.Utility.FormatSizeString(size));

                        // This is a bit hard to use, but there might be multiple in-flight transfers
                        // The message sink(s) get all messages for all transfers
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

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public IEnumerable<string> Messages { get { return m_messages; } }

        // ReSharper disable once UnusedMember.Global
        // This is referenced in the logs.
        public int MessagesActualLength { get { return Messages == null ? 0 : Messages.Count(); } }

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public IEnumerable<string> Warnings { get { return m_warnings; } }

        // ReSharper disable once UnusedMember.Global
        // This is referenced in the logs.
        public int WarningsActualLength { get { return Warnings == null ? 0 : Warnings.Count(); } }

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public IEnumerable<string> Errors { get { return m_errors; } }

        // ReSharper disable once UnusedMember.Global
        // This is referenced in the logs.
        public int ErrorsActualLength { get { return Errors == null ? 0 : Errors.Count(); } }

        [JsonProperty(PropertyName = "Messages")]
        [JsonPropertyName("Messages")]
        public IEnumerable<string> LimitedMessages { get { return Messages?.Take(SERIALIZATION_LIMIT); } }
        [JsonProperty(PropertyName = "Warnings")]
        [JsonPropertyName("Warnings")]
        public IEnumerable<string> LimitedWarnings { get { return Warnings?.Take(SERIALIZATION_LIMIT); } }
        [JsonProperty(PropertyName = "Errors")]
        [JsonPropertyName("Errors")]
        public IEnumerable<string> LimitedErrors { get { return Errors?.Take(SERIALIZATION_LIMIT); } }

        protected readonly TaskControl m_taskController;
        public ITaskControl TaskControl => m_parent?.TaskControl ?? m_taskController;
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

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public IBackendWriter BackendWriter { get { return (IBackendWriter)this.BackendStatistics; } }

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

    internal class BackupResults : BasicResults, IBackupResults, IResultsWithVacuum
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
        public long TimestampChangedFiles { get; internal set; }
        public long ModifiedFolders { get; internal set; }
        public long ModifiedSymlinks { get; internal set; }
        public long AddedSymlinks { get; internal set; }
        public long DeletedSymlinks { get; internal set; }
        public bool PartialBackup { get; internal set; }
        public bool Dryrun { get; internal set; }

        public override OperationMode MainOperation { get { return OperationMode.Backup; } }

        public ICompactResults CompactResults { get; internal set; }
        public IVacuumResults VacuumResults { get; set; }
        public IDeleteResults DeleteResults { get; internal set; }
        public IRepairResults RepairResults { get; internal set; }
        public ITestResults TestResults { get; internal set; }
        public ISetLockResults LockResults { get; internal set; }

        public override ParsedResultType ParsedResult
        {
            get
            {
                if ((CompactResults != null && CompactResults.ParsedResult == ParsedResultType.Fatal) ||
                    (LockResults != null && LockResults.ParsedResult == ParsedResultType.Fatal) ||
                    (VacuumResults != null && VacuumResults.ParsedResult == ParsedResultType.Fatal) ||
                    (DeleteResults != null && DeleteResults.ParsedResult == ParsedResultType.Fatal) ||
                    (RepairResults != null && RepairResults.ParsedResult == ParsedResultType.Fatal) ||
                    (TestResults != null && TestResults.ParsedResult == ParsedResultType.Fatal) ||
                    Fatal)
                {
                    return ParsedResultType.Fatal;
                }
                else if ((CompactResults != null && CompactResults.ParsedResult == ParsedResultType.Error) ||
                    (LockResults != null && LockResults.ParsedResult == ParsedResultType.Error) ||
                    (VacuumResults != null && VacuumResults.ParsedResult == ParsedResultType.Error) ||
                    (DeleteResults != null && DeleteResults.ParsedResult == ParsedResultType.Error) ||
                    (RepairResults != null && RepairResults.ParsedResult == ParsedResultType.Error) ||
                    (TestResults != null && TestResults.ParsedResult == ParsedResultType.Error) ||
                    (Errors != null && Errors.Any()) || FilesWithError > 0)
                {
                    return ParsedResultType.Error;
                }
                else if ((CompactResults != null && CompactResults.ParsedResult == ParsedResultType.Warning) ||
                         (LockResults != null && LockResults.ParsedResult == ParsedResultType.Warning) ||
                         (VacuumResults != null && VacuumResults.ParsedResult == ParsedResultType.Warning) ||
                         (DeleteResults != null && DeleteResults.ParsedResult == ParsedResultType.Warning) ||
                         (RepairResults != null && RepairResults.ParsedResult == ParsedResultType.Warning) ||
                         (TestResults != null && TestResults.ParsedResult == ParsedResultType.Warning) ||
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

    internal class RestoreResults : BasicResults, IRestoreResults
    {
        /// <summary>
        /// The list of broken local files - i.e. locally stored files that raised an error during restore.
        /// </summary>
        public Library.Utility.FileBackedStringList BrokenLocalFiles { get; internal set; } = [];
        /// <summary>
        /// The list of broken remote files - i.e. remotely stored files that raised an error during restore.
        /// </summary>
        public Library.Utility.FileBackedStringList BrokenRemoteFiles { get; internal set; } = [];
        public long RestoredFiles { get; internal set; }
        public long SizeOfRestoredFiles { get; internal set; }
        public long RestoredFolders { get; internal set; }
        public long RestoredSymlinks { get; internal set; }
        public long PatchedFiles { get; internal set; }
        public long DeletedFiles { get; internal set; }
        public long DeletedFolders { get; internal set; }
        public long DeletedSymlinks { get; internal set; }
        public string RestorePath { get; internal set; }

        /// <summary>
        /// Number of LRU evictions triggered by low disk space in the temp directory
        /// (disk-pressure path, unlimited cache mode only).
        /// </summary>
        public long CachePressureEvictions { get; internal set; }
        public bool ShouldSerializeCachePressureEvictions() => CachePressureEvictions > 0;

        /// <summary>
        /// Number of volumes re-downloaded after being evicted due to disk pressure.
        /// </summary>
        public long CachePressureRedownloads { get; internal set; }
        public bool ShouldSerializeCachePressureRedownloads() => CachePressureRedownloads > 0;

        /// <summary>
        /// Total number of distinct dblock volumes accessed during the restore.
        /// </summary>
        public long TotalVolumesAccessed { get; internal set; }
        public bool ShouldSerializeTotalVolumesAccessed() => TotalVolumesAccessed > 0;

        public override OperationMode MainOperation { get { return OperationMode.Restore; } }

        public IRecreateDatabaseResults RecreateDatabaseResults { get; internal set; }

        public override ParsedResultType ParsedResult
        {
            get
            {
                if ((RecreateDatabaseResults != null && RecreateDatabaseResults.ParsedResult == ParsedResultType.Fatal) ||
                    Fatal)
                {
                    return ParsedResultType.Fatal;
                }
                else if ((RecreateDatabaseResults != null && RecreateDatabaseResults.ParsedResult == ParsedResultType.Error) ||
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

    internal class ListResultFile : IListResultFile
    {
        public string Path { get; private set; }
        public IEnumerable<long> Sizes { get; private set; }
        public ListResultFile(string path, IEnumerable<long> sizes)
        {
            this.Path = path;
            this.Sizes = sizes;
        }
    }

    internal class ListResultFileset : IListResultFileset
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

    internal sealed record ListFilesetResultFileset(long Version, DateTime Time, bool? IsFullBackup, long? FileCount, long? FileSizes) : IListFilesetResultFileset;


    internal class ListFilesetResults : BasicResults, IListFilesetResults
    {
        public override OperationMode MainOperation => OperationMode.ListFilesets;
        public IEnumerable<IListFilesetResultFileset> Filesets { get; set; }
        public bool? EncryptedFiles { get; set; }
    }

    internal sealed record PaginatedResults<T>(int Page, int PageSize, int TotalPages, long TotalCount, IEnumerable<T> Items) : IPaginatedResults<T>;

    internal class ListFolderResults : BasicResults, IListFolderResults
    {
        public override OperationMode MainOperation => OperationMode.ListFolder;
        public IPaginatedResults<IListFolderEntry> Entries { get; set; }
    }

    internal sealed record ListFileVersion(long Version, DateTime Time, string Path, long Size, bool IsDirectory, bool IsSymlink, DateTime LastModified) : IListFileVersion;

    internal class ListFileVersionsResults : BasicResults, IListFileVersionsResults
    {
        public override OperationMode MainOperation => OperationMode.ListFileVersions;
        public IPaginatedResults<IListFileVersion> FileVersions { get; set; }
    }

    internal sealed record SearchFileVersion(long FileId, long Version, DateTime Time, string Path, long Size, bool IsDirectory, bool IsSymlink, DateTime LastModified, Range MatchedPathRange, Dictionary<string, string> Metadata) : ISearchFileVersion;

    internal class SearchFilesResults : BasicResults, ISearchFilesResults
    {
        public override OperationMode MainOperation => OperationMode.SearchFiles;
        public IPaginatedResults<ISearchFileVersion> FileVersions { get; set; }
    }


    internal class ListResults : BasicResults, IListResults
    {
        private IEnumerable<IListResultFileset> m_filesets;
        private IEnumerable<IListResultFile> m_files;
        public bool EncryptedFiles { get; set; }

        public void SetResult(IEnumerable<IListResultFileset> filesets, IEnumerable<IListResultFile> files)
        {
            m_filesets = filesets;
            m_files = files;
        }

        public IEnumerable<IListResultFileset> Filesets { get { return m_filesets; } }
        public IEnumerable<IListResultFile> Files { get { return m_files; } }

        public override OperationMode MainOperation { get { return OperationMode.List; } }
    }

    internal class ListAffectedResults : BasicResults, IListAffectedResults
    {
        private IEnumerable<IListResultFileset> m_filesets;
        private IEnumerable<IListResultFile> m_files;
        private IEnumerable<IListResultRemoteLog> m_logs;
        private IEnumerable<IListResultRemoteVolume> m_volumes;

        public void SetResult(IEnumerable<IListResultFileset> filesets, IEnumerable<IListResultFile> files, IEnumerable<IListResultRemoteLog> logs, IEnumerable<IListResultRemoteVolume> volumes)
        {
            m_filesets = filesets;
            m_files = files;
            m_logs = logs;
            m_volumes = volumes;
        }

        public IEnumerable<IListResultFileset> Filesets { get { return m_filesets; } }
        public IEnumerable<IListResultFile> Files { get { return m_files; } }
        public IEnumerable<IListResultRemoteLog> LogMessages { get { return m_logs; } }
        public IEnumerable<IListResultRemoteVolume> RemoteVolumes { get { return m_volumes; } }

        public override OperationMode MainOperation { get { return OperationMode.ListAffected; } }
    }

    internal class DeleteResults : BasicResults, IDeleteResults
    {
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public IEnumerable<Tuple<long, DateTime>> DeletedSets { get; private set; }

        // ReSharper disable once UnusedMember.Global
        // This is referenced in the logs.
        public int DeletedSetsActualLength { get { return DeletedSets == null ? 0 : DeletedSets.Count(); } }

        [JsonProperty(PropertyName = "DeletedSets")]
        [JsonPropertyName("DeletedSets")]
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

    internal class RecreateDatabaseResults : BasicResults, IRecreateDatabaseResults
    {
        public override OperationMode MainOperation { get { return OperationMode.Repair; } }

        public RecreateDatabaseResults() : base() { }
        public RecreateDatabaseResults(BasicResults p) : base(p) { }
    }

    internal class CreateLogDatabaseResults : BasicResults, ICreateLogDatabaseResults
    {
        public override OperationMode MainOperation { get { return OperationMode.CreateLogDb; } }
        public string TargetPath { get; internal set; }
    }

    internal class RestoreControlFilesResults : BasicResults, IRestoreControlFilesResults
    {
        public IEnumerable<string> Files { get; private set; }

        public override OperationMode MainOperation { get { return OperationMode.RestoreControlfiles; } }
        public void SetResult(IEnumerable<string> files) { this.Files = files; }
    }

    internal class ListRemoteResults : BasicResults, IListRemoteResults
    {
        public IEnumerable<IFileEntry> Files { get; private set; }

        public override OperationMode MainOperation { get { return OperationMode.ListRemote; } }
        public void SetResult(IEnumerable<IFileEntry> files) { this.Files = files; }
    }

    internal class SetLockResults : BasicResults, ISetLockResults
    {
        public override OperationMode MainOperation => OperationMode.SetLock;

        public long VolumesRead { get; internal set; }
        public long VolumesUpdated { get; internal set; }

        public SetLockResults() : base() { }
        public SetLockResults(BasicResults p) : base(p) { }
    }

    internal class ReadLockInfoResults : BasicResults, IReadLockInfoResults
    {
        public override OperationMode MainOperation => OperationMode.ReadLockInfo;

        public long VolumesRead { get; internal set; }
        public long VolumesUpdated { get; internal set; }

        public ReadLockInfoResults() : base() { }
        public ReadLockInfoResults(BasicResults p) : base(p) { }
    }

    internal class RepairResults : BasicResults, IRepairResults
    {
        public override OperationMode MainOperation { get { return OperationMode.Repair; } }

        public RepairResults() : base() { }
        public RepairResults(BasicResults p) : base(p) { }
        public IRecreateDatabaseResults RecreateDatabaseResults { get; internal set; }

        public override ParsedResultType ParsedResult
        {
            get
            {
                if ((RecreateDatabaseResults != null && RecreateDatabaseResults.ParsedResult == ParsedResultType.Fatal) ||
                    Fatal)
                {
                    return ParsedResultType.Fatal;
                }
                else if ((RecreateDatabaseResults != null && RecreateDatabaseResults.ParsedResult == ParsedResultType.Error) ||
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

    internal class CompactResults : BasicResults, ICompactResults, IResultsWithVacuum
    {
        public long DeletedFileCount { get; internal set; }
        public long DownloadedFileCount { get; internal set; }
        public long UploadedFileCount { get; internal set; }
        public long DeletedFileSize { get; internal set; }
        public long DownloadedFileSize { get; internal set; }
        public long UploadedFileSize { get; internal set; }
        public bool Dryrun { get; internal set; }

        public IVacuumResults VacuumResults { get; set; }

        public override OperationMode MainOperation { get { return OperationMode.Compact; } }

        public CompactResults() : base() { }
        public CompactResults(BasicResults p) : base(p) { }
    }

    internal class ListChangesResults : BasicResults, IListChangesResults
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
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public IEnumerable<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>> Verifications { get { return m_verifications; } }

        // ReSharper disable once UnusedMember.Global
        // This is referenced in the logs.
        public int VerificationsActualLength { get { return Verifications == null ? 0 : Verifications.Count(); } }

        [JsonProperty(PropertyName = "Verifications")]
        [JsonPropertyName("Verifications")]
        public IEnumerable<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>> LimitedVerifications { get { return Verifications?.Take(SERIALIZATION_LIMIT); } }

        public KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>> AddResult(string volume, IEnumerable<KeyValuePair<TestEntryStatus, string>> changes)
        {
            var res = new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(volume, changes);
            m_verifications.Add(res);
            return res;
        }

        public void RemoveResult(string volume)
        {
            var item = m_verifications.FirstOrDefault(x => x.Key == volume);
            if (item.Key == volume)
                m_verifications.Remove(item);
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
        public long UpdatedFileCount { get; set; }

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
