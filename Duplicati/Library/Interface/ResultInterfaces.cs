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
using System.Collections.Generic;

namespace Duplicati.Library.Interface
{
	public interface IBasicResults
	{
        DateTime BeginTime { get; }
        DateTime EndTime { get; }
        TimeSpan Duration { get; }
        
        IEnumerable<string> Errors { get; }
        IEnumerable<string> Warnings { get; }
        IEnumerable<string> Messages { get; }
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

    public interface IListResults : IBasicResults
    {
        IEnumerable<KeyValuePair<long, DateTime>> Filesets { get; }
        IEnumerable<IListResultFile> Files { get; }
    }
    
    public interface IListResultFile
    {
        string Path { get; }
        IEnumerable<long> Sizes { get; }
    }
    
    public interface IDeleteResults : IBasicResults
    {
        IEnumerable<DateTime> DeletedSets { get; }
        ICompactResults CompactResults { get; }
        bool Dryrun { get; }
    }
        
    public interface IBackupResults : IBasicResults
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
        IBackendStatstics BackendStatistics { get; }
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
    }
    
    public interface IRestoreControlFilesResults : IBasicResults
    {
    }
    
    public interface IRepairResults : IBasicResults
    {
    }
}

