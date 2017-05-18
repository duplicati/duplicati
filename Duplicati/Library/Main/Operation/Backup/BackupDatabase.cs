﻿//  Copyright (C) 2015, The Duplicati Team
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
using System.Threading.Tasks;
using Duplicati.Library.Main.Database;
using CoCoL;
using Duplicati.Library.Main.Operation.Common;
using System.Collections.Generic;
using Duplicati.Library.Main.Volumes;
using System.Linq;


namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// Asynchronous interface that ensures all requests
    /// to the database are performed in a sequential manner
    /// </summary>
    internal class BackupDatabase : DatabaseCommon
    {
        private LocalBackupDatabase m_database;

        public class FileEntryData
        {
            public long id;
            public DateTime modified;
            public long filesize;
            public string metahash;
            public long metasize;
        }

        public BackupDatabase(LocalBackupDatabase database, Options options)
            : base(database, options)
        {
            m_database = database;
        }

        public Task<long> FindBlockIDAsync(string hash, long size)
        {
            return RunOnMain(() => m_database.FindBlockID(hash, size, m_transaction));
        }

        public Task<bool> AddBlockAsync(string hash, long size, long volumeid)
        {
            return RunOnMain(() => m_database.AddBlock(hash, size, volumeid, m_transaction));
        }

        public Task<string> GetFileHashAsync(long fileid)
        {
            return RunOnMain(() => m_database.GetFileHash(fileid));
        }

        public Task<Tuple<bool, long>> AddMetadatasetAsync(string hash, long size, long blocksetid)
        {
            return RunOnMain(() =>
            {
                long metadataid;
                var n = m_database.AddMetadataset(hash, size, blocksetid, out metadataid, m_transaction);
                return new Tuple<bool, long>(n, metadataid);
            });
        }

        public Task<Tuple<bool, long>> GetMetadataIDAsync(string hash, long size)
        {
            return RunOnMain(() =>
            {
                long metadataid;
                var r = m_database.GetMetadatasetID(hash, size, out metadataid, m_transaction);
                return new Tuple<bool, long>(r, metadataid);
            });
        }

        public Task AddDirectoryEntryAsync(string filename, long metadataid, DateTime lastModified)
        {
            return RunOnMain(() => m_database.AddDirectoryEntry(filename, metadataid, lastModified, m_transaction));
        }

        public Task AddSymlinkEntryAsync(string filename, long metadataid, DateTime lastModified)
        {
            return RunOnMain(() => m_database.AddSymlinkEntry(filename, metadataid, lastModified, m_transaction));
        }

        public Task<FileEntryData> GetFileEntryAsync(string path)
        {
            return RunOnMain(() => { 
                DateTime oldModified;
                long lastFileSize;
                string oldMetahash;
                long oldMetasize;
                
                var id = m_database.GetFileEntry(path, out oldModified, out lastFileSize, out oldMetahash, out oldMetasize);
                return
                    id < 0 ?
                    null :
                    new FileEntryData() { id = id, modified = oldModified, filesize = lastFileSize, metahash = oldMetahash, metasize = oldMetasize };
            });
        }

        public Task<long> AddBlocksetAsync(string filehash, long size, int blocksize, IEnumerable<string> hashlist, IEnumerable<string> blocklisthashes)
        {
            return RunOnMain(() => {
                long blocksetid;

                m_database.AddBlockset(filehash, size, blocksize, hashlist, blocklisthashes, out blocksetid, m_transaction);
                return blocksetid;
            });
        }

        public Task AddFileAsync(string filename, DateTime lastmodified, long blocksetid, long metadataid)
        {
            return RunOnMain(() => m_database.AddFile(filename, lastmodified, blocksetid, metadataid, m_transaction));
        }

        public Task AddUnmodifiedAsync(long fileid, DateTime lastModified)
        {
            return RunOnMain(() => m_database.AddUnmodifiedFile(fileid, lastModified, m_transaction));
        }
            

        public Task MoveBlockToVolumeAsync(string blockkey, long size, long sourcevolumeid, long targetvolumeid)
        {
            return RunOnMain(() => m_database.MoveBlockToVolume(blockkey, size, sourcevolumeid, targetvolumeid, m_transaction));
        }

        public Task SafeDeleteRemoteVolumeAsync(string remotename)
        {
            return RunOnMain(() => m_database.SafeDeleteRemoteVolume(remotename, m_transaction));
        }

        public Task<IEnumerable<string>> GetBlocklistHashesAsync(string remotename)
        {
            return RunOnMain(() => Task.FromResult(m_database.GetBlocklistHashes(remotename, m_transaction)));
        }

        public Task UpdateIndexVolumeAsync(IndexVolumeWriter indexvolume, BlockVolumeWriter blockvolume)
        {
            if (indexvolume != null)
                return Task.FromResult<bool>(true);

            return RunOnMain(() =>
            {
                m_database.AddIndexBlockLink(indexvolume.VolumeID, blockvolume.VolumeID, m_transaction);
                indexvolume.StartVolume(blockvolume.RemoteFilename);

                foreach(var b in m_database.GetBlocks(blockvolume.VolumeID, m_transaction))
                    indexvolume.AddBlock(b.Hash, b.Size);

                m_database.UpdateRemoteVolume(indexvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);
            });
        }

        public Task AppendFilesFromPreviousSetAsync(string[] deletedFilelist)
        {
            return RunOnMain(() => m_database.AppendFilesFromPreviousSet(m_transaction, deletedFilelist));
        }

        public Task AppendFilesFromPreviousSetAsync(string[] deletedFilelist, long filesetid, long prevId, DateTime timestamp)
        {
            return RunOnMain(() => m_database.AppendFilesFromPreviousSet(m_transaction, deletedFilelist, filesetid, prevId, timestamp));
        }

        public Task<KeyValuePair<long, DateTime>[]> GetIncompleteFilesetsAsync()
        {
            return RunOnMain(() => m_database.GetIncompleteFilesets(m_transaction).OrderBy(x => x.Value).ToArray());
        }

        public Task<IEnumerable<KeyValuePair<long, DateTime>>> GetFilesetTimesAsync()
        {
            return RunOnMain(() => m_database.FilesetTimes);
        }

        public Task<long> CreateFilesetAsync(long volumeID, DateTime fileTime)
        {
            return RunOnMain(() => m_database.CreateFileset(volumeID, fileTime, m_transaction));
        }

        public Task LinkFilesetToVolumeAsync(long filesetid, long volumeid)
        {
            return RunOnMain(() => m_database.LinkFilesetToVolume(filesetid, volumeid, m_transaction));
        }

        public Task WriteFilesetAsync(FilesetVolumeWriter fsw, long filesetid)
        {
            return RunOnMain(() => m_database.WriteFileset(fsw, filesetid, m_transaction));
        }

        public Task<IEnumerable<string>> GetMissingIndexFilesAsync()
        {
            return RunOnMain(() => m_database.GetMissingIndexFiles(m_transaction));
        }

        public Task UpdateChangeStatisticsAsync(BackupResults result)
        {
            return RunOnMain(() => m_database.UpdateChangeStatistics(result, m_transaction));
        }

        public Task VerifyConsistencyAsync(int blocksize, int blockhashSize, bool verifyfilelists)
        {
            return RunOnMain(() => m_database.VerifyConsistency(m_transaction, blocksize, blockhashSize, verifyfilelists));
        }

        public Task RemoveRemoteVolumeAsync(string remoteFilename)
        {
            return RunOnMain(() => m_database.RemoveRemoteVolume(remoteFilename, m_transaction));
        }
    }
}

