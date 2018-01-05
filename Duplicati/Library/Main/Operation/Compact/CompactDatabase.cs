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
using Duplicati.Library.Main.Operation.Common;
using static Duplicati.Library.Main.Database.LocalDeleteDatabase;

namespace Duplicati.Library.Main.Operation.Compact
{
    internal class CompactDatabase : Delete.DeleteDatabase
    {
        public CompactDatabase(LocalDeleteDatabase database, Options options)
            : base(database, options)
        {
            m_database = database;
        }

        public Task<ICompactReport> GetCompactReportAsync(long volsize, long wastethreshold, long smallfilesize, long maxsmallfilecount)
        {
            return RunOnMain(() => m_database.GetCompactReport(volsize, wastethreshold, smallfilesize, maxsmallfilecount, m_transaction));
        }

        public Task<IEnumerable<IRemoteVolume>> GetDeletableVolumesAsync(IEnumerable<IRemoteVolume> deleteableVolumes)
        {
            return RunOnMain(() => m_database.GetDeletableVolumes(deleteableVolumes, m_transaction));
        }

        public Task RemoveRemoteVolumeAsync(string name)
        {
            return RunOnMain(() => m_db.RemoveRemoteVolume(name, m_transaction));
        }

        public Task MoveBlockToNewVolumeAsync(string hash, long size, long volumeID)
        {
            return RunOnMain(() => m_database.MoveBlockToNewVolume(hash, size, volumeID, m_transaction));
        }

        public Task<IBlockQuery> CreateBlockQueryHelperAsync(Options options)
        {
            return RunOnMain(() => m_database.CreateBlockQueryHelper(options, m_transaction));
        }
    }
}
