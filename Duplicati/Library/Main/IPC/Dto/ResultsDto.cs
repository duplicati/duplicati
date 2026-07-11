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

namespace Duplicati.Library.Main.IPC.Dto;

/// <summary>
/// DTO for basic results
/// </summary>
[Serializable]
public class BasicResultsDto
{
    public DateTime BeginTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Messages { get; set; } = new();
    public ParsedResultType ParsedResult { get; set; }
    public bool Interrupted { get; set; }
    public bool Fatal { get; set; }

    public static BasicResultsDto FromResults(IBasicResults results)
    {
        if (results == null) return null;
        return new BasicResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            Fatal = results is BasicResults br && br.Fatal
        };
    }
}

/// <summary>
/// DTO for backup results
/// </summary>
[Serializable]
public class BackupResultsDto : BasicResultsDto
{
    public long DeletedFiles { get; set; }
    public long DeletedFolders { get; set; }
    public long ModifiedFiles { get; set; }
    public long ExaminedFiles { get; set; }
    public long OpenedFiles { get; set; }
    public long AddedFiles { get; set; }
    public long SizeOfModifiedFiles { get; set; }
    public long SizeOfAddedFiles { get; set; }
    public long SizeOfExaminedFiles { get; set; }
    public long SizeOfOpenedFiles { get; set; }
    public long NotProcessedFiles { get; set; }
    public long AddedFolders { get; set; }
    public long TooLargeFiles { get; set; }
    public long FilesWithError { get; set; }
    public long ModifiedFolders { get; set; }
    public long ModifiedSymlinks { get; set; }
    public long AddedSymlinks { get; set; }
    public long DeletedSymlinks { get; set; }
    public bool PartialBackup { get; set; }
    public bool Dryrun { get; set; }

    public CompactResultsDto CompactResults { get; set; }
    public DeleteResultsDto DeleteResults { get; set; }
    public RepairResultsDto RepairResults { get; set; }
    public SetLockResultsDto LockResults { get; set; }
    public VacuumResultsDto VacuumResults { get; set; }
    public BackendStatisticsDto BackendStatistics { get; set; }
    public RemoteSynchronizationResultsDto[] RemoteSynchronizationResults { get; set; }

    public static BackupResultsDto FromResults(IBackupResults results)
    {
        if (results == null) return null;
        return new BackupResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            DeletedFiles = results.DeletedFiles,
            DeletedFolders = results.DeletedFolders,
            ModifiedFiles = results.ModifiedFiles,
            ExaminedFiles = results.ExaminedFiles,
            OpenedFiles = results.OpenedFiles,
            AddedFiles = results.AddedFiles,
            SizeOfModifiedFiles = results.SizeOfModifiedFiles,
            SizeOfAddedFiles = results.SizeOfAddedFiles,
            SizeOfExaminedFiles = results.SizeOfExaminedFiles,
            SizeOfOpenedFiles = results.SizeOfOpenedFiles,
            NotProcessedFiles = results.NotProcessedFiles,
            AddedFolders = results.AddedFolders,
            TooLargeFiles = results.TooLargeFiles,
            FilesWithError = results.FilesWithError,
            ModifiedFolders = results.ModifiedFolders,
            ModifiedSymlinks = results.ModifiedSymlinks,
            AddedSymlinks = results.AddedSymlinks,
            DeletedSymlinks = results.DeletedSymlinks,
            PartialBackup = results.PartialBackup,
            Dryrun = results.Dryrun,
            CompactResults = CompactResultsDto.FromResults(results.CompactResults),
            DeleteResults = DeleteResultsDto.FromResults(results.DeleteResults),
            RepairResults = RepairResultsDto.FromResults(results.RepairResults),
            LockResults = SetLockResultsDto.FromResults(results.LockResults),
            VacuumResults = VacuumResultsDto.FromResults(results.VacuumResults),
            BackendStatistics = BackendStatisticsDto.FromResults(results.BackendStatistics),
            RemoteSynchronizationResults = results.RemoteSynchronizationResults?.Select(RemoteSynchronizationResultsDto.FromResults).ToArray()
        };
    }
}

/// <summary>
/// DTO for backend statistics
/// </summary>
[Serializable]
public class BackendStatisticsDto
{
    public long RemoteCalls { get; set; }
    public long BytesUploaded { get; set; }
    public long BytesDownloaded { get; set; }
    public long FilesUploaded { get; set; }
    public long FilesDownloaded { get; set; }
    public long FilesDeleted { get; set; }
    public long FoldersCreated { get; set; }
    public long RetryAttempts { get; set; }
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

    public static BackendStatisticsDto FromResults(IBackendStatstics stats)
    {
        if (stats == null) return null;
        var dto = new BackendStatisticsDto
        {
            RemoteCalls = stats.RemoteCalls,
            BytesUploaded = stats.BytesUploaded,
            BytesDownloaded = stats.BytesDownloaded,
            FilesUploaded = stats.FilesUploaded,
            FilesDownloaded = stats.FilesDownloaded,
            FilesDeleted = stats.FilesDeleted,
            FoldersCreated = stats.FoldersCreated,
            RetryAttempts = stats.RetryAttempts
        };

        if (stats is IParsedBackendStatistics parsed)
        {
            dto.UnknownFileSize = parsed.UnknownFileSize;
            dto.UnknownFileCount = parsed.UnknownFileCount;
            dto.KnownFileCount = parsed.KnownFileCount;
            dto.KnownFileSize = parsed.KnownFileSize;
            dto.KnownFilesets = parsed.KnownFilesets;
            dto.LastBackupDate = parsed.LastBackupDate;
            dto.BackupListCount = parsed.BackupListCount;
            dto.TotalQuotaSpace = parsed.TotalQuotaSpace;
            dto.FreeQuotaSpace = parsed.FreeQuotaSpace;
            dto.AssignedQuotaSpace = parsed.AssignedQuotaSpace;
        }

        return dto;
    }
}

/// <summary>
/// DTO for compact results
/// </summary>
[Serializable]
public class CompactResultsDto : BasicResultsDto
{
    public long DeletedFileCount { get; set; }
    public long DownloadedFileCount { get; set; }
    public long UploadedFileCount { get; set; }
    public long DeletedFileSize { get; set; }
    public long DownloadedFileSize { get; set; }
    public long UploadedFileSize { get; set; }
    public bool Dryrun { get; set; }
    public VacuumResultsDto VacuumResults { get; set; }
    public BackendStatisticsDto BackendStatistics { get; set; }

    public static CompactResultsDto FromResults(ICompactResults results)
    {
        if (results == null) return null;
        return new CompactResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            DeletedFileCount = results.DeletedFileCount,
            DownloadedFileCount = results.DownloadedFileCount,
            UploadedFileCount = results.UploadedFileCount,
            DeletedFileSize = results.DeletedFileSize,
            DownloadedFileSize = results.DownloadedFileSize,
            UploadedFileSize = results.UploadedFileSize,
            Dryrun = results.Dryrun,
            VacuumResults = VacuumResultsDto.FromResults(results.VacuumResults),
            BackendStatistics = BackendStatisticsDto.FromResults(results.BackendStatistics)
        };
    }
}

/// <summary>
/// DTO for vacuum results
/// </summary>
[Serializable]
public class VacuumResultsDto : BasicResultsDto
{
    public static VacuumResultsDto FromResults(IVacuumResults results)
    {
        if (results == null) return null;
        return new VacuumResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted
        };
    }
}

/// <summary>
/// DTO for sync results
/// </summary>
[Serializable]
public class SyncResultsDto : BasicResultsDto
{
    public long FoldersCreated { get; set; }
    public long FoldersDeleted { get; set; }
    public long FilesUploaded { get; set; }
    public long UnchangedFiles { get; set; }
    public long FilesDeleted { get; set; }
    public long SourceFiles { get; set; }
    public long SizeOfSourceFiles { get; set; }
    public long SizeOfUploadedFiles { get; set; }
    public long SizeOfDeletedFiles { get; set; }

    public static SyncResultsDto FromResults(ISyncResults results)
    {
        if (results == null) return null;
        return new SyncResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            FoldersCreated = results.FoldersCreated,
            FoldersDeleted = results.FoldersDeleted,
            FilesUploaded = results.FilesUploaded,
            UnchangedFiles = results.UnchangedFiles,
            FilesDeleted = results.FilesDeleted,
            SourceFiles = results.SourceFiles,
            SizeOfSourceFiles = results.SizeOfSourceFiles,
            SizeOfUploadedFiles = results.SizeOfUploadedFiles,
            SizeOfDeletedFiles = results.SizeOfDeletedFiles
        };
    }
}

/// <summary>
/// DTO for delete results
/// </summary>
[Serializable]
public class DeleteResultsDto : BasicResultsDto
{
    public List<Tuple<long, DateTime>> DeletedSets { get; set; } = new();
    public CompactResultsDto CompactResults { get; set; }
    public BackendStatisticsDto BackendStatistics { get; set; }
    public bool Dryrun { get; set; }

    public static DeleteResultsDto FromResults(IDeleteResults results)
    {
        if (results == null) return null;
        return new DeleteResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            DeletedSets = results.DeletedSets?.ToList() ?? new List<Tuple<long, DateTime>>(),
            CompactResults = CompactResultsDto.FromResults(results.CompactResults),
            BackendStatistics = BackendStatisticsDto.FromResults(results.BackendStatistics),
            Dryrun = results.Dryrun
        };
    }
}

/// <summary>
/// DTO for repair results
/// </summary>
[Serializable]
public class RepairResultsDto : BasicResultsDto
{
    public RecreateDatabaseResultsDto RecreateDatabaseResults { get; set; }

    public static RepairResultsDto FromResults(IRepairResults results)
    {
        if (results == null) return null;
        return new RepairResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            RecreateDatabaseResults = RecreateDatabaseResultsDto.FromResults(results.RecreateDatabaseResults)
        };
    }
}

/// <summary>
/// DTO for recreate database results
/// </summary>
[Serializable]
public class RecreateDatabaseResultsDto : BasicResultsDto
{
    public static RecreateDatabaseResultsDto FromResults(IRecreateDatabaseResults results)
    {
        if (results == null) return null;
        return new RecreateDatabaseResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted
        };
    }
}

/// <summary>
/// DTO for set lock results
/// </summary>
[Serializable]
public class SetLockResultsDto : BasicResultsDto
{
    public long VolumesRead { get; set; }
    public long VolumesUpdated { get; set; }

    public static SetLockResultsDto FromResults(ISetLockResults results)
    {
        if (results == null) return null;
        return new SetLockResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            VolumesRead = results.VolumesRead,
            VolumesUpdated = results.VolumesUpdated
        };
    }
}

/// <summary>
/// DTO for remote synchronization results
/// </summary>
[Serializable]
public class RemoteSynchronizationResultsDto : BasicResultsDto
{
    public string Destination { get; set; }
    public long DeletedFileCount { get; set; }
    public long RenamedFileCount { get; set; }
    public long CopiedFileCount { get; set; }
    public long VerifiedFileCount { get; set; }
    public long FailedVerificationCount { get; set; }
    public long CopiedFileSize { get; set; }

    public static RemoteSynchronizationResultsDto FromResults(IRemoteSynchronizationResults results)
    {
        if (results == null) return null;
        return new RemoteSynchronizationResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            Destination = results.Destination,
            DeletedFileCount = results.DeletedFileCount,
            RenamedFileCount = results.RenamedFileCount,
            CopiedFileCount = results.CopiedFileCount,
            VerifiedFileCount = results.VerifiedFileCount,
            FailedVerificationCount = results.FailedVerificationCount,
            CopiedFileSize = results.CopiedFileSize
        };
    }
}

/// <summary>
/// DTO for restore results
/// </summary>
[Serializable]
public class RestoreResultsDto : BasicResultsDto
{
    public long RestoredFiles { get; set; }
    public long SizeOfRestoredFiles { get; set; }
    public long SizeOfRestoredData { get; set; }
    public long RestoredFolders { get; set; }
    public long RestoredSymlinks { get; set; }
    public long PatchedFiles { get; set; }
    public long DeletedFiles { get; set; }
    public long DeletedFolders { get; set; }
    public long DeletedSymlinks { get; set; }
    public long UnmodifiedFiles { get; set; }
    public long SizeOfUnmodifiedFiles { get; set; }
    public string RestorePath { get; set; }
    public RecreateDatabaseResultsDto RecreateDatabaseResults { get; set; }

    public static RestoreResultsDto FromResults(IRestoreResults results)
    {
        if (results == null) return null;
        return new RestoreResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            RestoredFiles = results.RestoredFiles,
            SizeOfRestoredFiles = results.SizeOfRestoredFiles,
            RestoredFolders = results.RestoredFolders,
            RestoredSymlinks = results.RestoredSymlinks,
            PatchedFiles = results.PatchedFiles,
            DeletedFiles = results.DeletedFiles,
            DeletedFolders = results.DeletedFolders,
            DeletedSymlinks = results.DeletedSymlinks,
            RestorePath = results.RestorePath,
            RecreateDatabaseResults = RecreateDatabaseResultsDto.FromResults(results.RecreateDatabaseResults)
        };
    }
}

/// <summary>
/// DTO for list results
/// </summary>
[Serializable]
public class ListResultsDto : BasicResultsDto
{
    public List<ListResultFilesetDto> Filesets { get; set; } = new();
    public List<ListResultFileDto> Files { get; set; } = new();
    public bool EncryptedFiles { get; set; }

    public static ListResultsDto FromResults(IListResults results)
    {
        if (results == null) return null;
        return new ListResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            Filesets = results.Filesets?.Select(ListResultFilesetDto.FromResult).ToList() ?? new List<ListResultFilesetDto>(),
            Files = results.Files?.Select(ListResultFileDto.FromResult).ToList() ?? new List<ListResultFileDto>(),
            EncryptedFiles = results.EncryptedFiles
        };
    }
}

/// <summary>
/// DTO for list result fileset
/// </summary>
[Serializable]
public class ListResultFilesetDto
{
    public long Version { get; set; }
    public int IsFullBackup { get; set; }
    public DateTime Time { get; set; }
    public long FileCount { get; set; }
    public long FileSizes { get; set; }

    public static ListResultFilesetDto FromResult(IListResultFileset result)
    {
        if (result == null) return null;
        return new ListResultFilesetDto
        {
            Version = result.Version,
            IsFullBackup = result.IsFullBackup,
            Time = result.Time,
            FileCount = result.FileCount,
            FileSizes = result.FileSizes
        };
    }
}

/// <summary>
/// DTO for list result file
/// </summary>
[Serializable]
public class ListResultFileDto
{
    public string Path { get; set; }
    public List<long> Sizes { get; set; } = new();

    public static ListResultFileDto FromResult(IListResultFile result)
    {
        if (result == null) return null;
        return new ListResultFileDto
        {
            Path = result.Path,
            Sizes = result.Sizes?.ToList() ?? new List<long>()
        };
    }
}

/// <summary>
/// DTO for list fileset results
/// </summary>
[Serializable]
public class ListFilesetResultsDto : BasicResultsDto
{
    public List<ListFilesetResultFilesetDto> Filesets { get; set; } = new();
    public bool? EncryptedFiles { get; set; }

    public static ListFilesetResultsDto FromResults(IListFilesetResults results)
    {
        if (results == null) return null;
        return new ListFilesetResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            Filesets = results.Filesets?.Select(ListFilesetResultFilesetDto.FromResult).ToList() ?? new List<ListFilesetResultFilesetDto>(),
            EncryptedFiles = results.EncryptedFiles
        };
    }
}

/// <summary>
/// DTO for list fileset result fileset
/// </summary>
[Serializable]
public class ListFilesetResultFilesetDto
{
    public long Version { get; set; }
    public bool? IsFullBackup { get; set; }
    public DateTime Time { get; set; }
    public long? FileCount { get; set; }
    public long? FileSizes { get; set; }

    public static ListFilesetResultFilesetDto FromResult(IListFilesetResultFileset result)
    {
        if (result == null) return null;
        return new ListFilesetResultFilesetDto
        {
            Version = result.Version,
            IsFullBackup = result.IsFullBackup,
            Time = result.Time,
            FileCount = result.FileCount,
            FileSizes = result.FileSizes
        };
    }
}

/// <summary>
/// DTO for list folder results
/// </summary>
[Serializable]
public class ListFolderResultsDto : BasicResultsDto
{
    public PaginatedResultsDto<ListFolderEntryDto> Entries { get; set; }

    public static ListFolderResultsDto FromResults(IListFolderResults results)
    {
        if (results == null) return null;

        PaginatedResultsDto<ListFolderEntryDto> entries = null;
        if (results.Entries != null)
        {
            entries = new PaginatedResultsDto<ListFolderEntryDto>
            {
                Page = results.Entries.Page,
                PageSize = results.Entries.PageSize,
                TotalPages = results.Entries.TotalPages,
                TotalCount = results.Entries.TotalCount,
                Items = results.Entries.Items?.Select(i => new ListFolderEntryDto
                {
                    Path = i.Path,
                    Size = i.Size,
                    IsDirectory = i.IsDirectory,
                    IsSymlink = i.IsSymlink,
                    LastModified = i.LastModified,
                    Metadata = i.Metadata
                }).ToList() ?? new List<ListFolderEntryDto>()
            };
        }

        return new ListFolderResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            Entries = entries
        };
    }
}

/// <summary>
/// DTO for paginated results
/// </summary>
[Serializable]
public class PaginatedResultsDto<T>
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public long TotalCount { get; set; }
    public List<T> Items { get; set; } = new();

    public static PaginatedResultsDto<T> FromResult(IPaginatedResults<T> result)
    {
        if (result == null) return null;
        return new PaginatedResultsDto<T>
        {
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages,
            TotalCount = result.TotalCount,
            Items = result.Items?.ToList() ?? new List<T>()
        };
    }
}

/// <summary>
/// DTO for list folder entry
/// </summary>
[Serializable]
public class ListFolderEntryDto
{
    public string Path { get; set; }
    public long Size { get; set; }
    public bool IsDirectory { get; set; }
    public bool IsSymlink { get; set; }
    public DateTime LastModified { get; set; }
    public Dictionary<string, string> Metadata { get; set; }

    public static ListFolderEntryDto FromResult(IListFolderEntry result)
    {
        if (result == null) return null;
        return new ListFolderEntryDto
        {
            Path = result.Path,
            Size = result.Size,
            IsDirectory = result.IsDirectory,
            IsSymlink = result.IsSymlink,
            LastModified = result.LastModified,
            Metadata = result.Metadata
        };
    }
}

/// <summary>
/// DTO for list file versions results
/// </summary>
[Serializable]
public class ListFileVersionsResultsDto : BasicResultsDto
{
    public PaginatedResultsDto<ListFileVersionDto> FileVersions { get; set; }

    public static ListFileVersionsResultsDto FromResults(IListFileVersionsResults results)
    {
        if (results == null) return null;

        PaginatedResultsDto<ListFileVersionDto> fileVersions = null;
        if (results.FileVersions != null)
        {
            fileVersions = new PaginatedResultsDto<ListFileVersionDto>
            {
                Page = results.FileVersions.Page,
                PageSize = results.FileVersions.PageSize,
                TotalPages = results.FileVersions.TotalPages,
                TotalCount = results.FileVersions.TotalCount,
                Items = results.FileVersions.Items?.Select(i => new ListFileVersionDto
                {
                    Path = i.Path,
                    Version = i.Version,
                    Time = i.Time,
                    Size = i.Size,
                    IsDirectory = i.IsDirectory,
                    IsSymlink = i.IsSymlink,
                    LastModified = i.LastModified
                }).ToList() ?? new List<ListFileVersionDto>()
            };
        }

        return new ListFileVersionsResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            FileVersions = fileVersions
        };
    }
}

/// <summary>
/// DTO for list file version
/// </summary>
[Serializable]
public class ListFileVersionDto
{
    public string Path { get; set; }
    public long Version { get; set; }
    public DateTime Time { get; set; }
    public long Size { get; set; }
    public bool IsDirectory { get; set; }
    public bool IsSymlink { get; set; }
    public DateTime LastModified { get; set; }

    public static ListFileVersionDto FromResult(IListFileVersion result)
    {
        if (result == null) return null;
        return new ListFileVersionDto
        {
            Path = result.Path,
            Version = result.Version,
            Time = result.Time,
            Size = result.Size,
            IsDirectory = result.IsDirectory,
            IsSymlink = result.IsSymlink,
            LastModified = result.LastModified
        };
    }
}

/// <summary>
/// DTO for search files results
/// </summary>
[Serializable]
public class SearchFilesResultsDto : BasicResultsDto
{
    public PaginatedResultsDto<SearchFileVersionDto> FileVersions { get; set; }

    public static SearchFilesResultsDto FromResults(ISearchFilesResults results)
    {
        if (results == null) return null;
        return new SearchFilesResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            FileVersions = results.FileVersions == null ? null : new PaginatedResultsDto<SearchFileVersionDto>
            {
                Page = results.FileVersions.Page,
                PageSize = results.FileVersions.PageSize,
                TotalPages = results.FileVersions.TotalPages,
                TotalCount = results.FileVersions.TotalCount,
                Items = results.FileVersions.Items?.Select(SearchFileVersionDto.FromSearchResult).ToList() ?? new List<SearchFileVersionDto>()
            }
        };
    }
}

/// <summary>
/// DTO for search file version
/// </summary>
[Serializable]
public class SearchFileVersionDto : ListFileVersionDto
{
    public RangeDto MatchedPathRange { get; set; }
    public Dictionary<string, string> Metadata { get; set; }

    public static SearchFileVersionDto FromSearchResult(ISearchFileVersion result)
    {
        if (result == null) return null;
        return new SearchFileVersionDto
        {
            Path = result.Path,
            Version = result.Version,
            Time = result.Time,
            Size = result.Size,
            IsDirectory = result.IsDirectory,
            IsSymlink = result.IsSymlink,
            LastModified = result.LastModified,
            MatchedPathRange = RangeDto.FromRange(result.MatchedPathRange),
            Metadata = result.Metadata
        };
    }
}

/// <summary>
/// DTO for range
/// </summary>
[Serializable]
public class RangeDto
{
    public int Start { get; set; }
    public int End { get; set; }

    public static RangeDto FromRange(Range range)
    {
        return new RangeDto
        {
            Start = range.Start.Value,
            End = range.End.Value
        };
    }

    public Range ToRange()
    {
        return new Range(Start, End);
    }
}

/// <summary>
/// DTO for list remote results
/// </summary>
[Serializable]
public class ListRemoteResultsDto : BasicResultsDto
{
    public List<FileEntryDto> Files { get; set; } = new();
    public BackendStatisticsDto BackendStatistics { get; set; }

    public static ListRemoteResultsDto FromResults(IListRemoteResults results)
    {
        if (results == null) return null;
        return new ListRemoteResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            Files = results.Files?.Select(FileEntryDto.FromResult).ToList() ?? new List<FileEntryDto>(),
            BackendStatistics = BackendStatisticsDto.FromResults(results.BackendStatistics)
        };
    }
}

/// <summary>
/// DTO for file entry
/// </summary>
[Serializable]
public class FileEntryDto
{
    public string Name { get; set; }
    public long Size { get; set; }
    public DateTime LastAccess { get; set; }
    public DateTime LastModification { get; set; }
    public DateTime Created { get; set; }
    public bool IsFolder { get; set; }
    public bool IsArchived { get; set; }

    public static FileEntryDto FromResult(IFileEntry result)
    {
        if (result == null) return null;
        return new FileEntryDto
        {
            Name = result.Name,
            Size = result.Size,
            LastAccess = result.LastAccess,
            LastModification = result.LastModification,
            Created = result.Created,
            IsFolder = result.IsFolder,
            IsArchived = result.IsArchived
        };
    }
}

/// <summary>
/// DTO for test results
/// </summary>
[Serializable]
public class TestResultsDto : BasicResultsDto
{
    public List<KeyValuePair<string, List<KeyValuePair<TestEntryStatus, string>>>> Verifications { get; set; } = new();

    public static TestResultsDto FromResults(ITestResults results)
    {
        if (results == null) return null;
        return new TestResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            Verifications = results.Verifications?
                .Select(v => new KeyValuePair<string, List<KeyValuePair<TestEntryStatus, string>>>(
                    v.Key,
                    v.Value?.Select(x => new KeyValuePair<TestEntryStatus, string>(x.Key, x.Value)).ToList() ?? new List<KeyValuePair<TestEntryStatus, string>>()
                )).ToList() ?? new List<KeyValuePair<string, List<KeyValuePair<TestEntryStatus, string>>>>()
        };
    }
}

/// <summary>
/// DTO for test filter results
/// </summary>
[Serializable]
public class TestFilterResultsDto : BasicResultsDto
{
    public long FileSize { get; set; }
    public long FileCount { get; set; }

    public static TestFilterResultsDto FromResults(ITestFilterResults results)
    {
        if (results == null) return null;
        return new TestFilterResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            FileSize = results.FileSize,
            FileCount = results.FileCount
        };
    }
}

/// <summary>
/// DTO for system info results
/// </summary>
[Serializable]
public class SystemInfoResultsDto : BasicResultsDto
{
    public List<string> Lines { get; set; } = new();

    public static SystemInfoResultsDto FromResults(ISystemInfoResults results)
    {
        if (results == null) return null;
        return new SystemInfoResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            Lines = results.Lines?.ToList() ?? new List<string>()
        };
    }
}

/// <summary>
/// DTO for purge files results
/// </summary>
[Serializable]
public class PurgeFilesResultsDto : BasicResultsDto
{
    public long RemovedFileCount { get; set; }
    public long RemovedFileSize { get; set; }
    public long UpdatedFileCount { get; set; }
    public long RewrittenFileLists { get; set; }
    public CompactResultsDto CompactResults { get; set; }

    public static PurgeFilesResultsDto FromResults(IPurgeFilesResults results)
    {
        if (results == null) return null;
        return new PurgeFilesResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            RemovedFileCount = results.RemovedFileCount,
            RemovedFileSize = results.RemovedFileSize,
            UpdatedFileCount = results.UpdatedFileCount,
            RewrittenFileLists = results.RewrittenFileLists,
            CompactResults = CompactResultsDto.FromResults(results.CompactResults)
        };
    }
}

/// <summary>
/// DTO for list broken files results
/// </summary>
[Serializable]
public class ListBrokenFilesResultsDto : BasicResultsDto
{
    public List<Tuple<long, DateTime, List<Tuple<string, long>>>> BrokenFiles { get; set; } = new();

    public static ListBrokenFilesResultsDto FromResults(IListBrokenFilesResults results)
    {
        if (results == null) return null;
        return new ListBrokenFilesResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            BrokenFiles = results.BrokenFiles?
                .Select(b => Tuple.Create(
                    b.Item1,
                    b.Item2,
                    b.Item3?.Select(f => Tuple.Create(f.Item1, f.Item2)).ToList() ?? new List<Tuple<string, long>>()
                )).ToList() ?? new List<Tuple<long, DateTime, List<Tuple<string, long>>>>()
        };
    }
}

/// <summary>
/// DTO for purge broken files results
/// </summary>
[Serializable]
public class PurgeBrokenFilesResultsDto : BasicResultsDto
{
    public PurgeFilesResultsDto PurgeResults { get; set; }
    public DeleteResultsDto DeleteResults { get; set; }

    public static PurgeBrokenFilesResultsDto FromResults(IPurgeBrokenFilesResults results)
    {
        if (results == null) return null;
        return new PurgeBrokenFilesResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            PurgeResults = PurgeFilesResultsDto.FromResults(results.PurgeResults),
            DeleteResults = DeleteResultsDto.FromResults(results.DeleteResults)
        };
    }
}

/// <summary>
/// DTO for send mail results
/// </summary>
[Serializable]
public class SendMailResultsDto : BasicResultsDto
{
    public List<string> Lines { get; set; } = new();

    public static SendMailResultsDto FromResults(ISendMailResults results)
    {
        if (results == null) return null;
        return new SendMailResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            Lines = results.Lines?.ToList() ?? new List<string>()
        };
    }
}

/// <summary>
/// DTO for read lock info results
/// </summary>
[Serializable]
public class ReadLockInfoResultsDto : BasicResultsDto
{
    public long VolumesRead { get; set; }
    public long VolumesUpdated { get; set; }

    public static ReadLockInfoResultsDto FromResults(IReadLockInfoResults results)
    {
        if (results == null) return null;
        return new ReadLockInfoResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            VolumesRead = results.VolumesRead,
            VolumesUpdated = results.VolumesUpdated
        };
    }
}

/// <summary>
/// DTO for list changes results
/// </summary>
[Serializable]
public class ListChangesResultsDto : BasicResultsDto
{
    public DateTime BaseVersionTimestamp { get; set; }
    public DateTime CompareVersionTimestamp { get; set; }
    public long BaseVersionIndex { get; set; }
    public long CompareVersionIndex { get; set; }
    public List<Tuple<ListChangesChangeType, ListChangesElementType, string>> ChangeDetails { get; set; } = new();
    public long AddedFolders { get; set; }
    public long AddedSymlinks { get; set; }
    public long AddedFiles { get; set; }
    public long DeletedFolders { get; set; }
    public long DeletedSymlinks { get; set; }
    public long DeletedFiles { get; set; }
    public long ModifiedFolders { get; set; }
    public long ModifiedSymlinks { get; set; }
    public long ModifiedFiles { get; set; }
    public long PreviousSize { get; set; }
    public long CurrentSize { get; set; }
    public long AddedSize { get; set; }
    public long DeletedSize { get; set; }

    public static ListChangesResultsDto FromResults(IListChangesResults results)
    {
        if (results == null) return null;
        return new ListChangesResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            BaseVersionTimestamp = results.BaseVersionTimestamp,
            CompareVersionTimestamp = results.CompareVersionTimestamp,
            BaseVersionIndex = results.BaseVersionIndex,
            CompareVersionIndex = results.CompareVersionIndex,
            ChangeDetails = results.ChangeDetails?.ToList() ?? new List<Tuple<ListChangesChangeType, ListChangesElementType, string>>(),
            AddedFolders = results.AddedFolders,
            AddedSymlinks = results.AddedSymlinks,
            AddedFiles = results.AddedFiles,
            DeletedFolders = results.DeletedFolders,
            DeletedSymlinks = results.DeletedSymlinks,
            DeletedFiles = results.DeletedFiles,
            ModifiedFolders = results.ModifiedFolders,
            ModifiedSymlinks = results.ModifiedSymlinks,
            ModifiedFiles = results.ModifiedFiles,
            PreviousSize = results.PreviousSize,
            CurrentSize = results.CurrentSize,
            AddedSize = results.AddedSize,
            DeletedSize = results.DeletedSize
        };
    }
}

/// <summary>
/// DTO for list affected results
/// </summary>
[Serializable]
public class ListAffectedResultsDto : BasicResultsDto
{
    public List<ListResultFilesetDto> Filesets { get; set; } = new();
    public List<ListResultFileDto> Files { get; set; } = new();
    public List<ListResultRemoteLogDto> LogMessages { get; set; } = new();
    public List<ListResultRemoteVolumeDto> RemoteVolumes { get; set; } = new();

    public static ListAffectedResultsDto FromResults(IListAffectedResults results)
    {
        if (results == null) return null;
        return new ListAffectedResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            Filesets = results.Filesets?.Select(ListResultFilesetDto.FromResult).ToList() ?? new List<ListResultFilesetDto>(),
            Files = results.Files?.Select(ListResultFileDto.FromResult).ToList() ?? new List<ListResultFileDto>(),
            LogMessages = results.LogMessages?.Select(ListResultRemoteLogDto.FromResult).ToList() ?? new List<ListResultRemoteLogDto>(),
            RemoteVolumes = results.RemoteVolumes?.Select(ListResultRemoteVolumeDto.FromResult).ToList() ?? new List<ListResultRemoteVolumeDto>()
        };
    }
}

/// <summary>
/// DTO for list result remote log
/// </summary>
[Serializable]
public class ListResultRemoteLogDto
{
    public DateTime Timestamp { get; set; }
    public string Message { get; set; }

    public static ListResultRemoteLogDto FromResult(IListResultRemoteLog result)
    {
        if (result == null) return null;
        return new ListResultRemoteLogDto
        {
            Timestamp = result.Timestamp,
            Message = result.Message
        };
    }
}

/// <summary>
/// DTO for list result remote volume
/// </summary>
[Serializable]
public class ListResultRemoteVolumeDto
{
    public string Name { get; set; }

    public static ListResultRemoteVolumeDto FromResult(IListResultRemoteVolume result)
    {
        if (result == null) return null;
        return new ListResultRemoteVolumeDto
        {
            Name = result.Name
        };
    }
}

/// <summary>
/// DTO for create log database results
/// </summary>
[Serializable]
public class CreateLogDatabaseResultsDto : BasicResultsDto
{
    public string TargetPath { get; set; }

    public static CreateLogDatabaseResultsDto FromResults(ICreateLogDatabaseResults results)
    {
        if (results == null) return null;
        return new CreateLogDatabaseResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            TargetPath = results.TargetPath
        };
    }
}

/// <summary>
/// DTO for restore control files results
/// </summary>
[Serializable]
public class RestoreControlFilesResultsDto : BasicResultsDto
{
    public List<string> Files { get; set; } = new();

    public static RestoreControlFilesResultsDto FromResults(IRestoreControlFilesResults results)
    {
        if (results == null) return null;
        return new RestoreControlFilesResultsDto
        {
            BeginTime = results.BeginTime,
            EndTime = results.EndTime,
            Duration = results.Duration,
            Errors = results.Errors?.ToList() ?? new List<string>(),
            Warnings = results.Warnings?.ToList() ?? new List<string>(),
            Messages = results.Messages?.ToList() ?? new List<string>(),
            ParsedResult = results.ParsedResult,
            Interrupted = results.Interrupted,
            Files = results.Files?.ToList() ?? new List<string>()
        };
    }
}
