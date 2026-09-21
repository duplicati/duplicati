// Copyright (C) 2026, The Duplicati Team
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
        /// <summary>
        /// The label assigned to the version, or null if no label is set
        /// </summary>
        string Label { get; }
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
        /// <summary>
        /// The label assigned to the version; not set if listing remote or no label is set
        /// </summary>
        string Label { get; }
    }

    /// <summary>
    /// The result of a set version label operation
    /// </summary>
    public interface ISetVersionLabelResults : IBasicResults
    {
        /// <summary>
        /// The backup version that was updated
        /// </summary>
        long BackupVersion { get; }
        /// <summary>
        /// The timestamp of the version that was updated
        /// </summary>
        DateTime Time { get; }
        /// <summary>
        /// The label that was assigned, or null if the label was cleared
        /// </summary>
        string Label { get; }
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

        /// <summary>
        /// The metadata of the entry, if any
        /// </summary>
        Dictionary<string, string> Metadata { get; }
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
        /// <summary>
        /// The metadata of the entry, if any
        /// </summary>
        Dictionary<string, string> Metadata { get; }
    }

    public interface IListAffectedResults : IBasicResults
    {
        IEnumerable<IListResultFileset> Filesets { get; }
        IEnumerable<IListResultFile> Files { get; }
        IEnumerable<IListResultRemoteLog> LogMessages { get; }
        IEnumerable<IListResultRemoteVolume> RemoteVolumes { get; }
    }

    public interface IDeleteResults : IBasicResults, IBackendStatsticsReporter
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
        ISetLockResults LockResults { get; }

        /// <summary>
        /// Results from the restore test run after the backup, or null if none was run.
        /// </summary>
        IRestoreTestResults RestoreTestResults { get; }

        /// <summary>
        /// Results from remote synchronization operations to multiple destinations.
        /// </summary>
        IRemoteSynchronizationResults[] RemoteSynchronizationResults { get; }
    }

    public interface IRestoreResults : IBasicResults
    {
        long RestoredFiles { get; }
        long SizeOfRestoredFiles { get; }
        long SizeOfRestoredData { get; }
        long RestoredFolders { get; }
        long RestoredSymlinks { get; }
        long PatchedFiles { get; }
        long DeletedFiles { get; }
        long DeletedFolders { get; }
        long DeletedSymlinks { get; }
        long UnmodifiedFiles { get; }
        long SizeOfUnmodifiedFiles { get; }
        string RestorePath { get; }

        IRecreateDatabaseResults RecreateDatabaseResults { get; }
    }

    public interface IRecreateDatabaseResults : IBasicResults
    {
    }

    public interface IListRemoteResults : IBasicResults, IBackendStatsticsReporter
    {
        IEnumerable<IFileEntry> Files { get; }
    }

    public interface ICompactResults : IBasicResults, IResultsWithVacuum, IBackendStatsticsReporter
    {
        long DeletedFileCount { get; }
        long DownloadedFileCount { get; }
        long UploadedFileCount { get; }
        long DeletedFileSize { get; }
        long DownloadedFileSize { get; }
        long UploadedFileSize { get; }
        bool Dryrun { get; }
    }

    /// <summary>
    /// Results from a remote synchronization operation to a single destination.
    /// </summary>
    public interface IRemoteSynchronizationResults : IBasicResults
    {
        /// <summary>
        /// The destination URL or identifier.
        /// </summary>
        string Destination { get; }

        /// <summary>
        /// Number of files deleted from the destination.
        /// </summary>
        long DeletedFileCount { get; }

        /// <summary>
        /// Number of files renamed at the destination (retention mode).
        /// </summary>
        long RenamedFileCount { get; }

        /// <summary>
        /// Number of files copied to the destination.
        /// </summary>
        long CopiedFileCount { get; }

        /// <summary>
        /// Number of files verified at the destination.
        /// </summary>
        long VerifiedFileCount { get; }

        /// <summary>
        /// Number of files that failed verification.
        /// </summary>
        long FailedVerificationCount { get; }

        /// <summary>
        /// Total size of files copied in bytes.
        /// </summary>
        long CopiedFileSize { get; }
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

    /// <summary>
    /// The strategies for choosing which files a restore test verifies
    /// </summary>
    public enum RestoreTestMode
    {
        /// <summary>
        /// Pick a fixed number of random files
        /// </summary>
        RandomFiles,
        /// <summary>
        /// Pick random files until a percentage of the backup size is reached
        /// </summary>
        RandomSize,
        /// <summary>
        /// Test every file in the version
        /// </summary>
        Full,
        /// <summary>
        /// Prefer files that have not been verified recently, so all files are covered over time
        /// </summary>
        Rolling
    }

    /// <summary>
    /// The reason a restored file failed verification
    /// </summary>
    public enum RestoreTestFailureReason
    {
        /// <summary>
        /// The restored file content hash did not match the hash recorded in the backup
        /// </summary>
        HashMismatch,
        /// <summary>
        /// The restored file size did not match the size recorded in the backup
        /// </summary>
        SizeMismatch,
        /// <summary>
        /// A remote volume required for the file could not be downloaded or was damaged
        /// </summary>
        MissingRemoteVolume,
        /// <summary>
        /// The restore engine failed to produce the file
        /// </summary>
        RestoreError,
        /// <summary>
        /// The restored metadata did not match the metadata recorded in the backup
        /// </summary>
        MetadataMismatch
    }

    /// <summary>
    /// The budget that stopped a restore test
    /// </summary>
    public enum RestoreTestBudgetReason
    {
        /// <summary>
        /// No budget was exceeded
        /// </summary>
        None,
        /// <summary>
        /// The remote download size budget was exceeded
        /// </summary>
        DownloadSize,
        /// <summary>
        /// The runtime budget was exceeded
        /// </summary>
        Runtime
    }

    /// <summary>
    /// A file that failed verification during a restore test
    /// </summary>
    public interface IRestoreTestFailure
    {
        /// <summary>
        /// The path of the file, as recorded in the backup
        /// </summary>
        string Path { get; }
        /// <summary>
        /// The reason the verification failed
        /// </summary>
        RestoreTestFailureReason Reason { get; }
        /// <summary>
        /// The expected value, such as the recorded hash, size or volume name
        /// </summary>
        string Expected { get; }
        /// <summary>
        /// The actual value observed on the restored file
        /// </summary>
        string Actual { get; }
    }

    /// <summary>
    /// A difference found between the backup and the live source file
    /// </summary>
    public interface IRestoreTestSourceDifference
    {
        /// <summary>
        /// The path of the source file
        /// </summary>
        string Path { get; }
        /// <summary>
        /// A description of the difference
        /// </summary>
        string Reason { get; }
        /// <summary>
        /// The value recorded in the backup
        /// </summary>
        string Expected { get; }
        /// <summary>
        /// The value found on the live source
        /// </summary>
        string Actual { get; }
    }

    /// <summary>
    /// The budget state of a restore test
    /// </summary>
    public interface IRestoreTestBudget
    {
        /// <summary>
        /// True if a budget was exceeded and the test stopped early
        /// </summary>
        bool Exceeded { get; }
        /// <summary>
        /// The budget that was exceeded
        /// </summary>
        RestoreTestBudgetReason Reason { get; }
    }

    /// <summary>
    /// The results of a restore test operation
    /// </summary>
    public interface IRestoreTestResults : IBasicResults
    {
        /// <summary>
        /// The sampling mode that was used
        /// </summary>
        RestoreTestMode Mode { get; }
        /// <summary>
        /// The backup version that was tested (0 is the newest)
        /// </summary>
        long Version { get; }
        /// <summary>
        /// The seed used for sample selection
        /// </summary>
        int Seed { get; }
        /// <summary>
        /// The number of files selected for testing
        /// </summary>
        long FilesTested { get; }
        /// <summary>
        /// The number of files that were restored and verified successfully
        /// </summary>
        long FilesPassed { get; }
        /// <summary>
        /// The number of files that failed verification
        /// </summary>
        long FilesFailed { get; }
        /// <summary>
        /// The number of selected files that were not verified, for instance because a budget was exceeded
        /// </summary>
        long FilesSkipped { get; }
        /// <summary>
        /// The number of bytes of verified file content
        /// </summary>
        long BytesRestored { get; }
        /// <summary>
        /// The number of bytes downloaded from the remote destination during the test
        /// </summary>
        long BytesDownloaded { get; }
        /// <summary>
        /// The number of remote volumes downloaded during the test
        /// </summary>
        long RemoteVolumesDownloaded { get; }
        /// <summary>
        /// True if the database was recreated from the remote destination
        /// </summary>
        bool DatabaseRecreated { get; }
        /// <summary>
        /// The results of the database recreation, if performed
        /// </summary>
        IRecreateDatabaseResults RecreateDatabaseResults { get; }
        /// <summary>
        /// The results of the restore of the sample
        /// </summary>
        IRestoreResults RestoreResults { get; }
        /// <summary>
        /// The files that failed verification
        /// </summary>
        IEnumerable<IRestoreTestFailure> Failures { get; }
        /// <summary>
        /// The differences found between the backup and the live source
        /// </summary>
        IEnumerable<IRestoreTestSourceDifference> SourceDifferences { get; }
        /// <summary>
        /// The budget state
        /// </summary>
        IRestoreTestBudget Budget { get; }
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

    /// <summary>
    /// Results of a sync operation.
    /// </summary>
    public interface ISyncResults : IBasicResults
    {
        /// <summary>
        /// The number of folders created on the remote destination.
        /// </summary>
        long FoldersCreated { get; }

        /// <summary>
        /// The number of folders deleted from the remote destination.
        /// </summary>
        long FoldersDeleted { get; }

        /// <summary>
        /// The number of files uploaded to the remote destination (new files plus
        /// updated files whose content changed).
        /// </summary>
        long FilesUploaded { get; }

        /// <summary>
        /// The number of files that were examined but left unchanged because the
        /// local and remote content already matched (size/mtime, or hash when
        /// <c>--sync-verify-hash</c> is set).
        /// </summary>
        long UnchangedFiles { get; }

        /// <summary>
        /// The number of files deleted from the remote destination (unknown remote
        /// files removed when <c>--sync-then-delete</c> is set).
        /// </summary>
        long FilesDeleted { get; }

        /// <summary>
        /// The total number of local source files encountered during enumeration
        /// across all folders, regardless of whether they were uploaded, unchanged,
        /// or skipped.
        /// </summary>
        long SourceFiles { get; }

        /// <summary>
        /// The total size in bytes of all local source files encountered during
        /// enumeration, regardless of whether they were uploaded or left unchanged.
        /// </summary>
        long SizeOfSourceFiles { get; }

        /// <summary>
        /// The total size in bytes of the files uploaded to the remote destination
        /// (new files plus updated files whose content changed).
        /// </summary>
        long SizeOfUploadedFiles { get; }

        /// <summary>
        /// The total size in bytes of the files deleted from the remote destination
        /// (unknown remote files removed when <c>--sync-then-delete</c> is set).
        /// </summary>
        long SizeOfDeletedFiles { get; }
    }

    public interface ISetLockResults : IBasicResults
    {
        /// <summary>
        /// Number of remote volumes that were considered for setting an object lock.
        /// </summary>
        long VolumesRead { get; }

        /// <summary>
        /// Number of remote volumes that had their object lock updated.
        /// </summary>
        long VolumesUpdated { get; }
    }

    public interface IReadLockInfoResults : IBasicResults
    {
        /// <summary>
        /// Number of remote volumes that were queried for object lock information.
        /// </summary>
        long VolumesRead { get; }

        /// <summary>
        /// Number of remote volumes whose lock information was updated in the local database.
        /// </summary>
        long VolumesUpdated { get; }
    }
}
