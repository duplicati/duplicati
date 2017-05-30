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
        long UnknownFileSize { get; }
        long UnknownFileCount { get; }
        long KnownFileCount { get; }
        long KnownFileSize { get; }
        DateTime LastBackupDate { get; }
        long BackupListCount { get; }
        long TotalQuotaSpace { get; }
        long FreeQuotaSpace { get; }
        long AssignedQuotaSpace { get; }
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
        IDeleteResults DeleteResults { get; }
        IRepairResults RepairResults { get; }
    }
    
    public interface IRestoreResults : IBasicResults
    {
        long FilesRestored { get; }
        long SizeOfRestoredFiles { get; }
        long FoldersRestored { get; }
        long SymlinksRestored { get; }
        long FilesPatched { get; }
        long FilesDeleted { get; }
        long FoldersDeleted { get; }
        long SymlinksDeleted { get; }
        
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

}

