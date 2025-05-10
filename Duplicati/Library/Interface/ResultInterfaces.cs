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

    public interface IResultsWithVacuum
    {
        IVacuumResults VacuumResults { get; set; }
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
        long KnownFilesets { get; set; }
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

    /// <summary>
    /// The result of a list fileset operation
    /// </summary>
    public interface IListFilesetResultFileset
    {
        /// <summary>
        /// The version of the fileset
        /// </summary>
        long Version { get; }
        /// <summary>
        /// Flag indicating if this is a full backup; not set if listing remote
        /// </summary>
        bool? IsFullBackup { get; }
        /// <summary>
        /// The timestamp of the fileset
        /// </summary>
        DateTime Time { get; }
        /// <summary>
        /// The number of files in the fileset; not set if listing remote
        /// </summary>
        long? FileCount { get; }
        /// <summary>
        /// The size of the files in the fileset; not set if listing remote
        /// </summary>
        long? FileSizes { get; }
    }

    /// <summary>
    /// The result of a list fileset operation
    /// </summary>
    public interface IListFilesetResults : IBasicResults
    {
        /// <summary>
        /// The filesets in the result
        /// </summary>
        IEnumerable<IListFilesetResultFileset> Filesets { get; }

        /// <summary>
        /// A flag indicating if the backup contains encrypted files
        /// /// </summary>
        bool? EncryptedFiles { get; }
    }

    /// <summary>
    /// Wrapper for the paginated results
    /// </summary>
    /// <typeparam name="T">The type of the items in the result</typeparam>
    public interface IPaginatedResults<T>
    {
        /// <summary>
        /// The page number of the result
        /// </summary>
        int Page { get; }
        /// <summary>
        /// The page size of the result
        /// </summary>
        int PageSize { get; }
        /// <summary>
        /// The total number of pages in the result
        /// </summary>
        int TotalPages { get; }
        /// <summary>
        /// The total number of items in the result
        /// </summary>
        long TotalCount { get; }
        /// <summary>
        /// The items in the result
        /// </summary>
        IEnumerable<T> Items { get; }
    }

    /// <summary>
    /// Results of a list folder operation
    /// </summary>
    public interface IListFolderResults : IBasicResults
    {
        /// <summary>
        /// The files in the folder
        /// </summary>
        IPaginatedResults<IListFolderEntry> Entries { get; }
    }

    /// <summary>
    /// The interface for an entry in a list folder operation
    /// </summary>
    public interface IListFolderEntry
    {
        /// <summary>
        /// The path of the entry
        /// </summary>
        string Path { get; }
        /// <summary>
        /// The size of the entry
        /// </summary>
        long Size { get; }
        /// <summary>
        /// True if the entry is a directory, false otherwise
        /// </summary>
        bool IsDirectory { get; }
        /// <summary>
        /// True if the entry is a symlink, false otherwise
        /// </summary>
        bool IsSymlink { get; }
        /// <summary>
        /// The last modified time of the entry
        /// </summary>
        DateTime LastModified { get; }
    }

    /// <summary>
    /// Results of a list file versions operation
    /// </summary>
    public interface IListFileVersionsResults : IBasicResults
    {
        /// <summary>
        /// The file versions in the result
        /// </summary>
        IPaginatedResults<IListFileVersion> FileVersions { get; }
    }

    /// <summary>
    /// The interface for a file version in a list file versions operation
    /// </summary>
    public interface IListFileVersion
    {
        /// <summary>
        /// The path of the file version
        /// </summary>
        string Path { get; }
        /// <summary>
        /// The version of the backup
        /// </summary>
        long Version { get; }
        /// <summary>
        /// The time of the backup
        /// </summary>
        DateTime Time { get; }
        /// <summary>
        /// The size of the file version
        /// </summary>
        long Size { get; }
        /// <summary>
        /// Flag indicating if the file version is a directory
        /// </summary>
        bool IsDirectory { get; }
        /// <summary>
        /// Flag indicating if the file version is a symlink
        /// </summary>
        bool IsSymlink { get; }
        /// <summary>
        /// The last modified time of the file version
        /// </summary>
        DateTime LastModified { get; }
    }

    /// <summary>
    /// Results of a search files operation
    /// </summary>
    public interface ISearchFilesResults : IBasicResults
    {
        /// <summary>
        /// The file versions in the result
        /// </summary>
        IPaginatedResults<ISearchFileVersion> FileVersions { get; }
    }

    /// <summary>
    /// The interface for a file version in a search files operation
    /// </summary>
    public interface ISearchFileVersion : IListFileVersion
    {
        /// <summary>
        /// The matched path of the file version
        /// </summary>
        Range MatchedPathRange { get; }
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

    public interface IBackupResults : IBasicResults, IBackendStatsticsReporter, IResultsWithVacuum
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

    public interface ICompactResults : IBasicResults, IResultsWithVacuum
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
        long UpdatedFileCount { get; }
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

