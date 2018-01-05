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
using System.Threading.Tasks;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using static Duplicati.Library.Main.Database.LocalRepairDatabase;

namespace Duplicati.Library.Main.Operation.Repair
{
    internal class RepairDatabase : Common.DatabaseCommon
    {
        private readonly LocalRepairDatabase m_database;
        
        public RepairDatabase(LocalRepairDatabase database, Options options)
            : base(database, options)
        {
            m_database = database;
        }

        public Task<bool> GetRepairInProgressAsync()
        {
            return RunOnMain(() => m_db.RepairInProgress);
        }

        public Task<bool> GetPartiallyRecreatedAsync()
        {
            return RunOnMain(() => m_db.PartiallyRecreated);
        }

        public Task FixDuplicateMetahashAsync()
        {
            return RunOnMain(() => m_database.FixDuplicateMetahash());
        }

        public Task FixDuplicateFileentriesAsync()
        {
            return RunOnMain(() => m_database.FixDuplicateFileentries());
        }

        public Task FixDuplicateBlocklistHashesAsync(int blocksize, int blockhashsize)
        {
            return RunOnMain(async () =>
            {
                if (m_database.FixDuplicateBlocklistHashes(blocksize, blockhashsize, m_transaction))
                    await this.CommitTransactionAsync("Fixed duplicate blocklisthashes");
            });
        }

        public Task FixMissingBlocklistHashesAsync(string blockhashalgorithm, int blockhashsize)
        {
            return RunOnMain(async () =>
            {
                if (m_database.FixMissingBlocklistHashes(blockhashalgorithm, blockhashsize, m_transaction))
                    await this.CommitTransactionAsync("Repaired missing blocklisthashes");
            });
        }

        public Task<RemoteVolumeEntry> GetRemoteVolumeAsync(string filename)
        {
            return RunOnMain(() => m_db.GetRemoteVolume(filename, m_transaction));
        }

        public Task CheckAllBlocksAreInVolumeAsync(string filename, IEnumerable<KeyValuePair<string, long>> blocks)
        {
            return RunOnMain(() => m_database.CheckAllBlocksAreInVolume(filename, blocks, m_transaction));
        }

        public Task CheckBlocklistCorrectAsync(string hash, long length, IEnumerable<string> blocks, int blocksize, int hashsize)
        {
            return RunOnMain(() => m_database.CheckBlocklistCorrect(hash, length, blocks, blocksize, hashsize, m_transaction));
        }

        public Task<IEnumerable<IRemoteVolume>> GetBlockVolumesFromIndexNameAsync(string name)
        {
            return RunOnMain(() => m_database.GetBlockVolumesFromIndexName(name, m_transaction));
        }

        public Task<IMissingBlockList> CreateBlockListAsync(string name)
        {
            return RunOnMain(() => m_database.CreateBlockList(name, m_transaction));
        }

        public Task<long> GetFilesetIdFromRemotenameAsync(string name)
        {
            return RunOnMain(() => m_database.GetFilesetIdFromRemotename(name, m_transaction));
        }

        public Task WriteFilesetAsync(FilesetVolumeWriter writer, long filesetid)
        {
            return RunOnMain(() => m_db.WriteFileset(writer, filesetid, m_transaction));
        }

        public Test.TestDatabase GetTestDatabase()
        {
            return new Test.TestDatabase(m_db, m_options);
        }
    }
}
