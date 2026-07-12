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
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.IPC.Dto;

namespace Duplicati.Library.Main.IPC;

/// <summary>
/// Base wrapper for basic results
/// </summary>
public abstract class BasicResultsWrapper : IBasicResults
{
    protected readonly BasicResultsDto _dto;

    protected BasicResultsWrapper(BasicResultsDto dto)
    {
        _dto = dto ?? throw new ArgumentNullException(nameof(dto));
    }

    public DateTime BeginTime => _dto.BeginTime;
    public DateTime EndTime => _dto.EndTime;
    public TimeSpan Duration => _dto.Duration;
    public IEnumerable<string> Errors => _dto.Errors;
    public IEnumerable<string> Warnings => _dto.Warnings;
    public IEnumerable<string> Messages => _dto.Messages;
    public ParsedResultType ParsedResult => _dto.ParsedResult;
    public bool Interrupted => _dto.Interrupted;
}

/// <summary>
/// Wrapper for backup results
/// </summary>
public class BackupResultsWrapper : BasicResultsWrapper, IBackupResults
{
    public BackupResultsWrapper(BackupResultsDto dto) : base(dto) { }

    protected new BackupResultsDto _dto => (BackupResultsDto)base._dto;

    public long DeletedFiles => _dto.DeletedFiles;
    public long DeletedFolders => _dto.DeletedFolders;
    public long ModifiedFiles => _dto.ModifiedFiles;
    public long ExaminedFiles => _dto.ExaminedFiles;
    public long OpenedFiles => _dto.OpenedFiles;
    public long AddedFiles => _dto.AddedFiles;
    public long SizeOfModifiedFiles => _dto.SizeOfModifiedFiles;
    public long SizeOfAddedFiles => _dto.SizeOfAddedFiles;
    public long SizeOfExaminedFiles => _dto.SizeOfExaminedFiles;
    public long SizeOfOpenedFiles => _dto.SizeOfOpenedFiles;
    public long NotProcessedFiles => _dto.NotProcessedFiles;
    public long AddedFolders => _dto.AddedFolders;
    public long TooLargeFiles => _dto.TooLargeFiles;
    public long FilesWithError => _dto.FilesWithError;
    public long ModifiedFolders => _dto.ModifiedFolders;
    public long ModifiedSymlinks => _dto.ModifiedSymlinks;
    public long AddedSymlinks => _dto.AddedSymlinks;
    public long DeletedSymlinks => _dto.DeletedSymlinks;
    public bool PartialBackup => _dto.PartialBackup;
    public bool Dryrun => _dto.Dryrun;

    public ICompactResults CompactResults => _dto.CompactResults == null ? null : new CompactResultsWrapper(_dto.CompactResults);
    public IDeleteResults DeleteResults => _dto.DeleteResults == null ? null : new DeleteResultsWrapper(_dto.DeleteResults);
    public IRepairResults RepairResults => _dto.RepairResults == null ? null : new RepairResultsWrapper(_dto.RepairResults);
    public ISetLockResults LockResults => _dto.LockResults == null ? null : new SetLockResultsWrapper(_dto.LockResults);
    public IVacuumResults VacuumResults
    {
        get => _dto.VacuumResults == null ? null : new VacuumResultsWrapper(_dto.VacuumResults);
        set => throw new InvalidOperationException("Cannot set property on wrapper");
    }
    public IBackendStatstics BackendStatistics => _dto.BackendStatistics == null ? null : new BackendStatisticsWrapper(_dto.BackendStatistics);
    public IRemoteSynchronizationResults[] RemoteSynchronizationResults =>
        _dto.RemoteSynchronizationResults?.Select(r => new RemoteSynchronizationResultsWrapper(r)).ToArray();
}

/// <summary>
/// Wrapper for backend statistics
/// </summary>
public class BackendStatisticsWrapper : IBackendStatstics, IParsedBackendStatistics
{
    private readonly BackendStatisticsDto _dto;

    public BackendStatisticsWrapper(BackendStatisticsDto dto)
    {
        _dto = dto;
    }

    public long RemoteCalls => _dto.RemoteCalls;
    public long BytesUploaded => _dto.BytesUploaded;
    public long BytesDownloaded => _dto.BytesDownloaded;
    public long FilesUploaded => _dto.FilesUploaded;
    public long FilesDownloaded => _dto.FilesDownloaded;
    public long FilesDeleted => _dto.FilesDeleted;
    public long FoldersCreated => _dto.FoldersCreated;
    public long RetryAttempts => _dto.RetryAttempts;
    public long UnknownFileSize { get => _dto.UnknownFileSize; set => throw new InvalidOperationException("Cannot set property on wrapper"); }
    public long UnknownFileCount { get => _dto.UnknownFileCount; set => throw new InvalidOperationException("Cannot set property on wrapper"); }
    public long KnownFileCount { get => _dto.KnownFileCount; set => throw new InvalidOperationException("Cannot set property on wrapper"); }
    public long KnownFileSize { get => _dto.KnownFileSize; set => throw new InvalidOperationException("Cannot set property on wrapper"); }
    public long KnownFilesets { get => _dto.KnownFilesets; set => throw new InvalidOperationException("Cannot set property on wrapper"); }
    public DateTime LastBackupDate { get => _dto.LastBackupDate; set => throw new InvalidOperationException("Cannot set property on wrapper"); }
    public long BackupListCount { get => _dto.BackupListCount; set => throw new InvalidOperationException("Cannot set property on wrapper"); }
    public long TotalQuotaSpace { get => _dto.TotalQuotaSpace; set => throw new InvalidOperationException("Cannot set property on wrapper"); }
    public long FreeQuotaSpace { get => _dto.FreeQuotaSpace; set => throw new InvalidOperationException("Cannot set property on wrapper"); }
    public long AssignedQuotaSpace { get => _dto.AssignedQuotaSpace; set => throw new InvalidOperationException("Cannot set property on wrapper"); }
}

/// <summary>
/// Wrapper for compact results
/// </summary>
public class CompactResultsWrapper : BasicResultsWrapper, ICompactResults
{
    public CompactResultsWrapper(CompactResultsDto dto) : base(dto) { }

    protected new CompactResultsDto _dto => (CompactResultsDto)base._dto;

    public long DeletedFileCount => _dto.DeletedFileCount;
    public long DownloadedFileCount => _dto.DownloadedFileCount;
    public long UploadedFileCount => _dto.UploadedFileCount;
    public long DeletedFileSize => _dto.DeletedFileSize;
    public long DownloadedFileSize => _dto.DownloadedFileSize;
    public long UploadedFileSize => _dto.UploadedFileSize;
    public bool Dryrun => _dto.Dryrun;
    public IVacuumResults VacuumResults
    {
        get => _dto.VacuumResults == null ? null : new VacuumResultsWrapper(_dto.VacuumResults);
        set => throw new InvalidOperationException("Cannot set property on wrapper");
    }

    public IBackendStatstics BackendStatistics => _dto.BackendStatistics == null ? null : new BackendStatisticsWrapper(_dto.BackendStatistics);
}

/// <summary>
/// Wrapper for vacuum results
/// </summary>
public class VacuumResultsWrapper : BasicResultsWrapper, IVacuumResults
{
    public VacuumResultsWrapper(VacuumResultsDto dto) : base(dto) { }
}

/// <summary>
/// Wrapper for sync results
/// </summary>
public class SyncResultsWrapper : BasicResultsWrapper, ISyncResults
{
    public SyncResultsWrapper(SyncResultsDto dto) : base(dto) { }

    protected new SyncResultsDto _dto => (SyncResultsDto)base._dto;

    public long FoldersCreated => _dto.FoldersCreated;
    public long FoldersDeleted => _dto.FoldersDeleted;
    public long FilesUploaded => _dto.FilesUploaded;
    public long UnchangedFiles => _dto.UnchangedFiles;
    public long FilesDeleted => _dto.FilesDeleted;
    public long SourceFiles => _dto.SourceFiles;
    public long SizeOfSourceFiles => _dto.SizeOfSourceFiles;
    public long SizeOfUploadedFiles => _dto.SizeOfUploadedFiles;
    public long SizeOfDeletedFiles => _dto.SizeOfDeletedFiles;
}

/// <summary>
/// Wrapper for delete results
/// </summary>
public class DeleteResultsWrapper : BasicResultsWrapper, IDeleteResults
{
    public DeleteResultsWrapper(DeleteResultsDto dto) : base(dto) { }

    protected new DeleteResultsDto _dto => (DeleteResultsDto)base._dto;

    public IEnumerable<Tuple<long, DateTime>> DeletedSets => _dto.DeletedSets;
    public ICompactResults CompactResults => _dto.CompactResults == null ? null : new CompactResultsWrapper(_dto.CompactResults);
    public bool Dryrun => _dto.Dryrun;

    public IBackendStatstics BackendStatistics => _dto.BackendStatistics == null ? null : new BackendStatisticsWrapper(_dto.BackendStatistics);
}

/// <summary>
/// Wrapper for repair results
/// </summary>
public class RepairResultsWrapper : BasicResultsWrapper, IRepairResults
{
    public RepairResultsWrapper(RepairResultsDto dto) : base(dto) { }

    protected new RepairResultsDto _dto => (RepairResultsDto)base._dto;

    public IRecreateDatabaseResults RecreateDatabaseResults =>
        _dto.RecreateDatabaseResults == null ? null : new RecreateDatabaseResultsWrapper(_dto.RecreateDatabaseResults);
}

/// <summary>
/// Wrapper for recreate database results
/// </summary>
public class RecreateDatabaseResultsWrapper : BasicResultsWrapper, IRecreateDatabaseResults
{
    public RecreateDatabaseResultsWrapper(RecreateDatabaseResultsDto dto) : base(dto) { }
}

/// <summary>
/// Wrapper for set lock results
/// </summary>
public class SetLockResultsWrapper : BasicResultsWrapper, ISetLockResults
{
    public SetLockResultsWrapper(SetLockResultsDto dto) : base(dto) { }

    protected new SetLockResultsDto _dto => (SetLockResultsDto)base._dto;

    public long VolumesRead => _dto.VolumesRead;
    public long VolumesUpdated => _dto.VolumesUpdated;
}

/// <summary>
/// Wrapper for remote synchronization results
/// </summary>
public class RemoteSynchronizationResultsWrapper : BasicResultsWrapper, IRemoteSynchronizationResults
{
    public RemoteSynchronizationResultsWrapper(RemoteSynchronizationResultsDto dto) : base(dto) { }

    protected new RemoteSynchronizationResultsDto _dto => (RemoteSynchronizationResultsDto)base._dto;

    public string Destination => _dto.Destination;
    public long DeletedFileCount => _dto.DeletedFileCount;
    public long RenamedFileCount => _dto.RenamedFileCount;
    public long CopiedFileCount => _dto.CopiedFileCount;
    public long VerifiedFileCount => _dto.VerifiedFileCount;
    public long FailedVerificationCount => _dto.FailedVerificationCount;
    public long CopiedFileSize => _dto.CopiedFileSize;
}

/// <summary>
/// Wrapper for restore results
/// </summary>
public class RestoreResultsWrapper : BasicResultsWrapper, IRestoreResults
{
    public RestoreResultsWrapper(RestoreResultsDto dto) : base(dto) { }

    protected new RestoreResultsDto _dto => (RestoreResultsDto)base._dto;

    public long RestoredFiles => _dto.RestoredFiles;
    public long SizeOfRestoredFiles => _dto.SizeOfRestoredFiles;
    public long SizeOfRestoredData => _dto.SizeOfRestoredData;
    public long RestoredFolders => _dto.RestoredFolders;
    public long RestoredSymlinks => _dto.RestoredSymlinks;
    public long PatchedFiles => _dto.PatchedFiles;
    public long DeletedFiles => _dto.DeletedFiles;
    public long DeletedFolders => _dto.DeletedFolders;
    public long DeletedSymlinks => _dto.DeletedSymlinks;
    public long UnmodifiedFiles => _dto.UnmodifiedFiles;
    public long SizeOfUnmodifiedFiles => _dto.SizeOfUnmodifiedFiles;
    public string RestorePath => _dto.RestorePath;
    public IRecreateDatabaseResults RecreateDatabaseResults =>
        _dto.RecreateDatabaseResults == null ? null : new RecreateDatabaseResultsWrapper(_dto.RecreateDatabaseResults);
}

/// <summary>
/// Wrapper for list results
/// </summary>
public class ListResultsWrapper : BasicResultsWrapper, IListResults
{
    public ListResultsWrapper(ListResultsDto dto) : base(dto) { }

    protected new ListResultsDto _dto => (ListResultsDto)base._dto;

    public IEnumerable<IListResultFileset> Filesets => _dto.Filesets.Select(f => new ListResultFilesetWrapper(f));
    public IEnumerable<IListResultFile> Files => _dto.Files.Select(f => new ListResultFileWrapper(f));
    public bool EncryptedFiles => _dto.EncryptedFiles;
}

/// <summary>
/// Wrapper for list result fileset
/// </summary>
public class ListResultFilesetWrapper : IListResultFileset
{
    private readonly ListResultFilesetDto _dto;

    public ListResultFilesetWrapper(ListResultFilesetDto dto)
    {
        _dto = dto;
    }

    public long Version => _dto.Version;
    public int IsFullBackup => _dto.IsFullBackup;
    public DateTime Time => _dto.Time;
    public long FileCount => _dto.FileCount;
    public long FileSizes => _dto.FileSizes;
}

/// <summary>
/// Wrapper for list result file
/// </summary>
public class ListResultFileWrapper : IListResultFile
{
    private readonly ListResultFileDto _dto;

    public ListResultFileWrapper(ListResultFileDto dto)
    {
        _dto = dto;
    }

    public string Path => _dto.Path;
    public IEnumerable<long> Sizes => _dto.Sizes;
}

/// <summary>
/// Wrapper for list fileset results
/// </summary>
public class ListFilesetResultsWrapper : BasicResultsWrapper, IListFilesetResults
{
    public ListFilesetResultsWrapper(ListFilesetResultsDto dto) : base(dto) { }

    protected new ListFilesetResultsDto _dto => (ListFilesetResultsDto)base._dto;

    public IEnumerable<IListFilesetResultFileset> Filesets => _dto.Filesets.Select(f => new ListFilesetResultFilesetWrapper(f));
    public bool? EncryptedFiles => _dto.EncryptedFiles;
}

/// <summary>
/// Wrapper for list fileset result fileset
/// </summary>
public class ListFilesetResultFilesetWrapper : IListFilesetResultFileset
{
    private readonly ListFilesetResultFilesetDto _dto;

    public ListFilesetResultFilesetWrapper(ListFilesetResultFilesetDto dto)
    {
        _dto = dto;
    }

    public long Version => _dto.Version;
    public bool? IsFullBackup => _dto.IsFullBackup;
    public DateTime Time => _dto.Time;
    public long? FileCount => _dto.FileCount;
    public long? FileSizes => _dto.FileSizes;
}

/// <summary>
/// Wrapper for list folder results
/// </summary>
public class ListFolderResultsWrapper : BasicResultsWrapper, IListFolderResults
{
    public ListFolderResultsWrapper(ListFolderResultsDto dto) : base(dto) { }

    protected new ListFolderResultsDto _dto => (ListFolderResultsDto)base._dto;

    public IPaginatedResults<IListFolderEntry> Entries =>
        _dto.Entries == null ? null : new PaginatedResultsWrapper<IListFolderEntry, ListFolderEntryDto>(_dto.Entries, e => new ListFolderEntryWrapper(e));
}

/// <summary>
/// Generic paginated results wrapper
/// </summary>
public class PaginatedResultsWrapper<TInterface, TDto> : IPaginatedResults<TInterface>
{
    private readonly PaginatedResultsDto<TDto> _dto;
    private readonly Func<TDto, TInterface> _converter;

    public PaginatedResultsWrapper(PaginatedResultsDto<TDto> dto, Func<TDto, TInterface> converter)
    {
        _dto = dto;
        _converter = converter;
    }

    public int Page => _dto.Page;
    public int PageSize => _dto.PageSize;
    public int TotalPages => _dto.TotalPages;
    public long TotalCount => _dto.TotalCount;
    public IEnumerable<TInterface> Items => _dto.Items.Select(_converter);
}

/// <summary>
/// Wrapper for list folder entry
/// </summary>
public class ListFolderEntryWrapper : IListFolderEntry
{
    private readonly ListFolderEntryDto _dto;

    public ListFolderEntryWrapper(ListFolderEntryDto dto)
    {
        _dto = dto;
    }

    public string Path => _dto.Path;
    public long Size => _dto.Size;
    public bool IsDirectory => _dto.IsDirectory;
    public bool IsSymlink => _dto.IsSymlink;
    public DateTime LastModified => _dto.LastModified;
    public Dictionary<string, string> Metadata => _dto.Metadata;
}

/// <summary>
/// Wrapper for list file versions results
/// </summary>
public class ListFileVersionsResultsWrapper : BasicResultsWrapper, IListFileVersionsResults
{
    public ListFileVersionsResultsWrapper(ListFileVersionsResultsDto dto) : base(dto) { }

    protected new ListFileVersionsResultsDto _dto => (ListFileVersionsResultsDto)base._dto;

    public IPaginatedResults<IListFileVersion> FileVersions =>
        _dto.FileVersions == null ? null : new PaginatedResultsWrapper<IListFileVersion, ListFileVersionDto>(_dto.FileVersions, v => new ListFileVersionWrapper(v));
}

/// <summary>
/// Wrapper for list file version
/// </summary>
public class ListFileVersionWrapper : IListFileVersion
{
    private readonly ListFileVersionDto _dto;

    public ListFileVersionWrapper(ListFileVersionDto dto)
    {
        _dto = dto;
    }

    public string Path => _dto.Path;
    public long Version => _dto.Version;
    public DateTime Time => _dto.Time;
    public long Size => _dto.Size;
    public bool IsDirectory => _dto.IsDirectory;
    public bool IsSymlink => _dto.IsSymlink;
    public DateTime LastModified => _dto.LastModified;
}

/// <summary>
/// Wrapper for search files results
/// </summary>
public class SearchFilesResultsWrapper : BasicResultsWrapper, ISearchFilesResults
{
    public SearchFilesResultsWrapper(SearchFilesResultsDto dto) : base(dto) { }

    protected new SearchFilesResultsDto _dto => (SearchFilesResultsDto)base._dto;

    public IPaginatedResults<ISearchFileVersion> FileVersions =>
        _dto.FileVersions == null ? null : new PaginatedResultsWrapper<ISearchFileVersion, SearchFileVersionDto>(_dto.FileVersions, v => new SearchFileVersionWrapper(v));
}

/// <summary>
/// Wrapper for search file version
/// </summary>
public class SearchFileVersionWrapper : ISearchFileVersion
{
    private readonly SearchFileVersionDto _dto;

    public SearchFileVersionWrapper(SearchFileVersionDto dto)
    {
        _dto = dto;
    }

    public string Path => _dto.Path;
    public long Version => _dto.Version;
    public DateTime Time => _dto.Time;
    public long Size => _dto.Size;
    public bool IsDirectory => _dto.IsDirectory;
    public bool IsSymlink => _dto.IsSymlink;
    public DateTime LastModified => _dto.LastModified;
    public Range MatchedPathRange => _dto.MatchedPathRange?.ToRange() ?? default;
    public Dictionary<string, string> Metadata => _dto.Metadata;
}

/// <summary>
/// Wrapper for list remote results
/// </summary>
public class ListRemoteResultsWrapper : BasicResultsWrapper, IListRemoteResults
{
    public ListRemoteResultsWrapper(ListRemoteResultsDto dto) : base(dto) { }

    protected new ListRemoteResultsDto _dto => (ListRemoteResultsDto)base._dto;

    public IEnumerable<IFileEntry> Files => _dto.Files.Select(f => new FileEntryWrapper(f));
    public IBackendStatstics BackendStatistics => _dto.BackendStatistics == null ? null : new BackendStatisticsWrapper(_dto.BackendStatistics);
}

/// <summary>
/// Wrapper for file entry
/// </summary>
public class FileEntryWrapper : IFileEntry
{
    private readonly FileEntryDto _dto;

    public FileEntryWrapper(FileEntryDto dto)
    {
        _dto = dto;
    }

    public string Name => _dto.Name;
    public long Size => _dto.Size;
    public DateTime LastAccess => _dto.LastAccess;
    public DateTime LastModification => _dto.LastModification;
    public DateTime Created => _dto.LastModification; // Fallback to LastModification if not provided
    public bool IsFolder => _dto.IsFolder;
    public bool IsArchived => false; // Default to false as this is not serialized
}

/// <summary>
/// Wrapper for test results
/// </summary>
public class TestResultsWrapper : BasicResultsWrapper, ITestResults
{
    public TestResultsWrapper(TestResultsDto dto) : base(dto) { }

    protected new TestResultsDto _dto => (TestResultsDto)base._dto;

    public IEnumerable<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>> Verifications =>
        _dto.Verifications.Select(v => new KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>(
            v.Key,
            v.Value.Select(x => new KeyValuePair<TestEntryStatus, string>(x.Key, x.Value))
        ));
}

/// <summary>
/// Wrapper for test filter results
/// </summary>
public class TestFilterResultsWrapper : BasicResultsWrapper, ITestFilterResults
{
    public TestFilterResultsWrapper(TestFilterResultsDto dto) : base(dto) { }

    protected new TestFilterResultsDto _dto => (TestFilterResultsDto)base._dto;

    public long FileSize { get => _dto.FileSize; set => throw new InvalidOperationException("Cannot set property on wrapper"); }
    public long FileCount { get => _dto.FileCount; set => throw new InvalidOperationException("Cannot set property on wrapper"); }
}

/// <summary>
/// Wrapper for system info results
/// </summary>
public class SystemInfoResultsWrapper : BasicResultsWrapper, ISystemInfoResults
{
    public SystemInfoResultsWrapper(SystemInfoResultsDto dto) : base(dto) { }

    protected new SystemInfoResultsDto _dto => (SystemInfoResultsDto)base._dto;

    public IEnumerable<string> Lines => _dto.Lines;
}

/// <summary>
/// Wrapper for purge files results
/// </summary>
public class PurgeFilesResultsWrapper : BasicResultsWrapper, IPurgeFilesResults
{
    public PurgeFilesResultsWrapper(PurgeFilesResultsDto dto) : base(dto) { }

    protected new PurgeFilesResultsDto _dto => (PurgeFilesResultsDto)base._dto;

    public long RemovedFileCount => _dto.RemovedFileCount;
    public long RemovedFileSize => _dto.RemovedFileSize;
    public long UpdatedFileCount => _dto.UpdatedFileCount;
    public long RewrittenFileLists => _dto.RewrittenFileLists;
    public ICompactResults CompactResults => _dto.CompactResults == null ? null : new CompactResultsWrapper(_dto.CompactResults);
}

/// <summary>
/// Wrapper for list broken files results
/// </summary>
public class ListBrokenFilesResultsWrapper : BasicResultsWrapper, IListBrokenFilesResults
{
    public ListBrokenFilesResultsWrapper(ListBrokenFilesResultsDto dto) : base(dto) { }

    protected new ListBrokenFilesResultsDto _dto => (ListBrokenFilesResultsDto)base._dto;

    public IEnumerable<Tuple<long, DateTime, IEnumerable<Tuple<string, long>>>> BrokenFiles =>
        _dto.BrokenFiles.Select(b => Tuple.Create(
            b.Item1,
            b.Item2,
            b.Item3.Select(f => Tuple.Create(f.Item1, f.Item2)).Cast<Tuple<string, long>>()
        ));
}

/// <summary>
/// Wrapper for purge broken files results
/// </summary>
public class PurgeBrokenFilesResultsWrapper : BasicResultsWrapper, IPurgeBrokenFilesResults
{
    public PurgeBrokenFilesResultsWrapper(PurgeBrokenFilesResultsDto dto) : base(dto) { }

    protected new PurgeBrokenFilesResultsDto _dto => (PurgeBrokenFilesResultsDto)base._dto;

    public IPurgeFilesResults PurgeResults => _dto.PurgeResults == null ? null : new PurgeFilesResultsWrapper(_dto.PurgeResults);
    public IDeleteResults DeleteResults => _dto.DeleteResults == null ? null : new DeleteResultsWrapper(_dto.DeleteResults);
}

/// <summary>
/// Wrapper for send mail results
/// </summary>
public class SendMailResultsWrapper : BasicResultsWrapper, ISendMailResults
{
    public SendMailResultsWrapper(SendMailResultsDto dto) : base(dto) { }

    protected new SendMailResultsDto _dto => (SendMailResultsDto)base._dto;

    public IEnumerable<string> Lines => _dto.Lines;
}

/// <summary>
/// Wrapper for read lock info results
/// </summary>
public class ReadLockInfoResultsWrapper : BasicResultsWrapper, IReadLockInfoResults
{
    public ReadLockInfoResultsWrapper(ReadLockInfoResultsDto dto) : base(dto) { }

    protected new ReadLockInfoResultsDto _dto => (ReadLockInfoResultsDto)base._dto;

    public long VolumesRead => _dto.VolumesRead;
    public long VolumesUpdated => _dto.VolumesUpdated;
}

/// <summary>
/// Wrapper for list changes results
/// </summary>
public class ListChangesResultsWrapper : BasicResultsWrapper, IListChangesResults
{
    public ListChangesResultsWrapper(ListChangesResultsDto dto) : base(dto) { }

    protected new ListChangesResultsDto _dto => (ListChangesResultsDto)base._dto;

    public DateTime BaseVersionTimestamp => _dto.BaseVersionTimestamp;
    public DateTime CompareVersionTimestamp => _dto.CompareVersionTimestamp;
    public long BaseVersionIndex => _dto.BaseVersionIndex;
    public long CompareVersionIndex => _dto.CompareVersionIndex;
    public IEnumerable<Tuple<ListChangesChangeType, ListChangesElementType, string>> ChangeDetails => _dto.ChangeDetails;
    public long AddedFolders => _dto.AddedFolders;
    public long AddedSymlinks => _dto.AddedSymlinks;
    public long AddedFiles => _dto.AddedFiles;
    public long DeletedFolders => _dto.DeletedFolders;
    public long DeletedSymlinks => _dto.DeletedSymlinks;
    public long DeletedFiles => _dto.DeletedFiles;
    public long ModifiedFolders => _dto.ModifiedFolders;
    public long ModifiedSymlinks => _dto.ModifiedSymlinks;
    public long ModifiedFiles => _dto.ModifiedFiles;
    public long PreviousSize => _dto.PreviousSize;
    public long CurrentSize => _dto.CurrentSize;
    public long AddedSize => _dto.AddedSize;
    public long DeletedSize => _dto.DeletedSize;
}

/// <summary>
/// Wrapper for list affected results
/// </summary>
public class ListAffectedResultsWrapper : BasicResultsWrapper, IListAffectedResults
{
    public ListAffectedResultsWrapper(ListAffectedResultsDto dto) : base(dto) { }

    protected new ListAffectedResultsDto _dto => (ListAffectedResultsDto)base._dto;

    public IEnumerable<IListResultFileset> Filesets => _dto.Filesets.Select(f => new ListResultFilesetWrapper(f));
    public IEnumerable<IListResultFile> Files => _dto.Files.Select(f => new ListResultFileWrapper(f));
    public IEnumerable<IListResultRemoteLog> LogMessages => _dto.LogMessages.Select(l => new ListResultRemoteLogWrapper(l));
    public IEnumerable<IListResultRemoteVolume> RemoteVolumes => _dto.RemoteVolumes.Select(r => new ListResultRemoteVolumeWrapper(r));
}

/// <summary>
/// Wrapper for list result remote log
/// </summary>
public class ListResultRemoteLogWrapper : IListResultRemoteLog
{
    private readonly ListResultRemoteLogDto _dto;

    public ListResultRemoteLogWrapper(ListResultRemoteLogDto dto)
    {
        _dto = dto;
    }

    public DateTime Timestamp => _dto.Timestamp;
    public string Message => _dto.Message;
}

/// <summary>
/// Wrapper for list result remote volume
/// </summary>
public class ListResultRemoteVolumeWrapper : IListResultRemoteVolume
{
    private readonly ListResultRemoteVolumeDto _dto;

    public ListResultRemoteVolumeWrapper(ListResultRemoteVolumeDto dto)
    {
        _dto = dto;
    }

    public string Name => _dto.Name;
}

/// <summary>
/// Wrapper for create log database results
/// </summary>
public class CreateLogDatabaseResultsWrapper : BasicResultsWrapper, ICreateLogDatabaseResults
{
    public CreateLogDatabaseResultsWrapper(CreateLogDatabaseResultsDto dto) : base(dto) { }

    protected new CreateLogDatabaseResultsDto _dto => (CreateLogDatabaseResultsDto)base._dto;

    public string TargetPath => _dto.TargetPath;
}

/// <summary>
/// Wrapper for restore control files results
/// </summary>
public class RestoreControlFilesResultsWrapper : BasicResultsWrapper, IRestoreControlFilesResults
{
    public RestoreControlFilesResultsWrapper(RestoreControlFilesResultsDto dto) : base(dto) { }

    protected new RestoreControlFilesResultsDto _dto => (RestoreControlFilesResultsDto)base._dto;

    public IEnumerable<string> Files => _dto.Files;
}
