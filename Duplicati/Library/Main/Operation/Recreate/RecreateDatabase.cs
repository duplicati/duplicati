//  Copyright (C) 2017, The Duplicati Team
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
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main.Operation.Recreate
{
    internal class RecreateDatabase : Common.DatabaseCommon
    {
        private readonly LocalRecreateDatabase m_database;
        
        public RecreateDatabase(LocalRecreateDatabase database, Options options)
            : base(database, options)
        {
            m_database = database;
        }

        public Task<IEnumerable<long>> FindMatchingFilesetsAsync(DateTime restoretime, long[] versions)
        {
            return RunOnMain(() => m_db.FindMatchingFilesets(restoretime, versions));
        }

        public Task SetPartiallyRecreated(bool value)
        {
            return RunOnMain(() => m_db.PartiallyRecreated = value);
        }

        public Task SetRepairInProgressAsync(bool value)
        {
            return RunOnMain(() => m_db.RepairInProgress = value);
		}

        public Task<long> AddMetadatasetAsync(string hash, long size, IEnumerable<string> blocklisthashes, long expectedhashcount)
        {
            return RunOnMain(() => m_database.AddMetadataset(hash, size, blocklisthashes, expectedhashcount, m_transaction));
        }

        public Task AddDirectoryEntryAsync(long filesetid, string path, DateTime time, long metadataid)
        {
            return RunOnMain(() => m_database.AddDirectoryEntry(filesetid, path, time, metadataid, m_transaction));
        }

        public Task AddFileEntryAsync(long filesetid, string path, DateTime time, long blocksetid, long metadataid)
        {
            return RunOnMain(() => m_database.AddFileEntry(filesetid, path, time, blocksetid, metadataid, m_transaction));
        }

        public Task AddSymlinkEntryAsync(long filesetid, string path, DateTime time, long metadataid)
        {
            return RunOnMain(() => m_database.AddSymlinkEntry(filesetid, path, time, metadataid, m_transaction));
        }

        public Task<long> AddBlocksetAsync(string fullhash, long size, IEnumerable<string> blocklisthashes, long expectedblocklisthashes)
        {
            return RunOnMain(() => m_database.AddBlockset(fullhash, size, blocklisthashes, expectedblocklisthashes, m_transaction));
        }

        public Task AddSmallBlocksetLinkAsync(string filehash, string blockhash, long blocksize)
        {
            return RunOnMain(() => m_database.AddSmallBlocksetLink(filehash, blockhash, blocksize, m_transaction));
        }

        public Task<bool> UpdateBlockAsync(string hash, long size, long volumeID)
        {
            return RunOnMain(() => m_database.UpdateBlock(hash, size, volumeID, m_transaction));
        }

        public Task<bool> UpdateBlocksetAsync(string hash, IEnumerable<string> blocklisthashes)
        {
            return RunOnMain(() => m_database.UpdateBlockset(hash, blocklisthashes, m_transaction));
        }

        public Task FindMissingBlocklistHashesAsync(long hashsize, long blocksize)
        {
            return RunOnMain(() => m_database.FindMissingBlocklistHashes(hashsize, blocksize, m_transaction));
        }

        public Task<List<IRemoteVolume>> GetMissingBlockListVolumesAsync(int passNo, long blocksize, long hashsize)
        {
            return RunOnMain(() => m_database.GetMissingBlockListVolumes(passNo, blocksize, hashsize, m_transaction).ToList());
        }

        public Task<IEnumerable<string>> GetBlockListsAsync(long volumeid)
        {
            return RunOnMain(() => m_database.GetBlockLists(volumeid));
        }
    }
}
