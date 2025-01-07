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

namespace Duplicati.Library.Interface
{
    public enum ParsedResultType
    {
        Unknown,
        Success,
        Warning,
        Error,
        Fatal
    }

    public interface IBasicResults
    {
        DateTime BeginTime { get; }
        DateTime EndTime { get; }
        TimeSpan Duration { get; }
        
        IEnumerable<string> Errors { get; }
        IEnumerable<string> Warnings { get; }
        IEnumerable<string> Messages { get; }
        ParsedResultType ParsedResult { get; }
        bool Interrupted { get; }
    }

    public interface IBackendStatstics
    {
        long RemoteCalls { get; }
        long BytesUploaded { get; }
        long BytesDownloaded { get; }
        long FilesUploaded { get; }
        long FilesDownloaded { get; }
        long FilesDeleted { get; }
        long FoldersCreated { get; }
        long RetryAttempts { get; }
    }

    public interface IParsedBackendStatistics : IBackendStatstics
    {
        long UnknownFileSize { get; set; }
        long UnknownFileCount { get; set; }
        long KnownFileCount { get; set; }
        long KnownFileSize { get; set; }
        DateTime LastBackupDate { get; set; }
        long BackupListCount { get; set; }
        long TotalQuotaSpace { get; set; }
        long FreeQuotaSpace { get; set; }
        long AssignedQuotaSpace { get; set; }
    }

    public interface IBackendStatsticsReporter
    {
        IBackendStatstics BackendStatistics { get; }
    }

    public interface IListResultFile
    {
        string Path { get; }
        IEnumerable<long> Sizes { get; }
    }

    public interface IListResultRemoteLog
    {
        DateTime Timestamp { get; }
        string Message { get; }
    }

    public interface IListResultRemoteVolume
    {
        string Name { get; }
    }

    public interface IListResultFileset
    {
        long Version { get; }
        int IsFullBackup { get; }
        DateTime Time { get; }
        long FileCount { get; }
        long FileSizes { get; }
    }

    public interface IListResults : IBasicResults
    {
        IEnumerable<IListResultFileset> Filesets { get; }
        IEnumerable<IListResultFile> Files { get; }
        bool EncryptedFiles { get; }
    }

    public interface IListAffectedResults : IBasicResults
    {
        IEnumerable<IListResultFileset> Filesets { get; }
        IEnumerable<IListResultFile> Files { get; }
        IEnumerable<IListResultRemoteLog> LogMessages { get; }
        IEnumerable<IListResultRemoteVolume> RemoteVolumes { get; }
    }
        
    public interface IDeleteResults : IBasicResults
    {
        IEnumerable<Tuple<long, DateTime>> DeletedSets { get; }
        ICompactResults CompactResults { get; }
        bool Dryrun { get; }
    }
        
    public interface IBackupResults : IBasicResults, IBackendStatsticsReporter
    {
        long DeletedFiles { get; }
        long DeletedFolders { get; }
        long ModifiedFiles { get; }
        long ExaminedFiles { get; }
        long OpenedFiles { get; }
        long AddedFiles { get; }
        long SizeOfModifiedFiles { get; }
        long SizeOfAddedFiles { get; }
        long SizeOfExaminedFiles { get; }
        long SizeOfOpenedFiles { get; }
        long NotProcessedFiles { get; }
        long AddedFolders { get; }
        long TooLargeFiles { get; }
        long FilesWithError { get; }
        long ModifiedFolders { get; }
        long ModifiedSymlinks { get; }
        long AddedSymlinks { get; }
        long DeletedSymlinks { get; } 
        bool PartialBackup { get; }
        bool Dryrun { get; }
        
        ICompactResults CompactResults { get; }
        IVacuumResults VacuumResults { get; }
        IDeleteResults DeleteResults { get; }
        IRepairResults RepairResults { get; }
    }
    
    public interface IRestoreResults : IBasicResults
    {
        long RestoredFiles { get; }
        long SizeOfRestoredFiles { get; }
        long RestoredFolders { get; }
        long RestoredSymlinks { get; }
        long PatchedFiles { get; }
        long DeletedFiles { get; }
        long DeletedFolders { get; }
        long DeletedSymlinks { get; }
        
        IRecreateDatabaseResults RecreateDatabaseResults { get; }
    }
    
    public interface IRecreateDatabaseResults : IBasicResults
    {
    }

    public interface IListRemoteResults : IBasicResults, IBackendStatsticsReporter
    {
        IEnumerable<IFileEntry> Files { get; }
    }

    public interface ICompactResults : IBasicResults
    {
        long DeletedFileCount { get; }
        long DownloadedFileCount { get; }
        long UploadedFileCount { get; }
        long DeletedFileSize { get; }
        long DownloadedFileSize { get; }
        long UploadedFileSize { get; }
        bool Dryrun { get; }

        IVacuumResults VacuumResults { get; }
    }
    
    public interface ICreateLogDatabaseResults : IBasicResults
    {
        string TargetPath { get; }
    }
    
    public interface IRestoreControlFilesResults : IBasicResults
    {
        IEnumerable<string> Files { get; }
    }
    
    public interface IRepairResults : IBasicResults
    {
        IRecreateDatabaseResults RecreateDatabaseResults { get; }
    }
    
    
    /// <summary>
    /// The possible change types for an entry
    /// </summary>
    public enum ListChangesChangeType
    {
        /// <summary>
        /// The element was added
        /// </summary>
        Added,
        /// <summary>
        /// The element was deleted
        /// </summary>
        Deleted,
        /// <summary>
        /// The element was modified
        /// </summary>
        Modified
    }  
    
    /// <summary>
    /// The possible entry types
    /// </summary>
    public enum ListChangesElementType
    {
        /// <summary>
        /// The entry is a folder
        /// </summary>
        Folder,
        /// <summary>
        /// The entry is a symlink
        /// </summary>
        Symlink,
        /// <summary>
        /// The entry is a file
        /// </summary>
        File
    }
          
    public interface IListChangesResults : IBasicResults
    {
        DateTime BaseVersionTimestamp { get; }
        DateTime CompareVersionTimestamp { get; }
        long BaseVersionIndex { get; }
        long CompareVersionIndex { get; }
    
        IEnumerable<Tuple<ListChangesChangeType, ListChangesElementType, string>> ChangeDetails { get; } 
        
        long AddedFolders { get; }
        long AddedSymlinks { get; }
        long AddedFiles { get; }

        long DeletedFolders { get; }
        long DeletedSymlinks { get; }
        long DeletedFiles { get; }

        long ModifiedFolders { get; }
        long ModifiedSymlinks { get; }
        long ModifiedFiles { get; }
        
        long PreviousSize { get; }
        long CurrentSize { get; }

        long AddedSize { get; }
        long DeletedSize { get; }
    }
    
    /// <summary>
    /// The status of a change in a test entry 
    /// </summary>
    public enum TestEntryStatus
    {
        /// <summary>
        /// The element is missing
        /// </summary>
        Missing,
        /// <summary>
        /// The element was not expected
        /// </summary>
        Extra,
        /// <summary>
        /// The element was not the same as expected
        /// </summary>
        Modified,
        /// <summary>
        /// An error was encountered
        /// </summary>
        Error
    }

    public interface ITestResults : IBasicResults
    {
        IEnumerable<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>> Verifications { get; }
    }
    
    public interface ITestFilterResults : IBasicResults
    {
        long FileSize { get; set; }
        long FileCount { get; set; }
    }

    public interface ISystemInfoResults : IBasicResults
    {
        IEnumerable<string> Lines { get; }
    }

    public interface IPurgeFilesResults : IBasicResults
    {
        long RemovedFileCount { get; }
        long RemovedFileSize { get; }
        long RewrittenFileLists { get; }
        ICompactResults CompactResults { get; }
    }

    public interface IListBrokenFilesResults : IBasicResults
    {
        IEnumerable<Tuple<long, DateTime, IEnumerable<Tuple<string, long>>>> BrokenFiles { get; }
    }

    public interface IPurgeBrokenFilesResults : IBasicResults
    {
        IPurgeFilesResults PurgeResults { get; }
        IDeleteResults DeleteResults { get; }
    }

    public interface ISendMailResults : IBasicResults
    {
        IEnumerable<string> Lines { get; }
    }

    public interface IVacuumResults : IBasicResults
    {
    }
}

