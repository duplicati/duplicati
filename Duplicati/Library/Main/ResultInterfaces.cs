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

namespace Duplicati.Library.Main
{

	public interface IBasicResults
	{
        DateTime BeginTime { get; }
        DateTime EndTime { get; }
        TimeSpan Duration { get; }
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
        bool Dryrun { get; }
    }
    
    public interface IBackendStatstics : IBasicResults
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
    }
}

