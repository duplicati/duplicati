//  Copyright (C) 2015, The Duplicati Team
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
using CoCoL;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main.Operation.Common
{
    internal class DatabaseCommon : SingleRunner, IDisposable
    {
        protected LocalDatabase m_db;
        protected System.Data.IDbTransaction m_transaction;

        public DatabaseCommon(LocalDatabase db)
            : base()
        {
            m_db = db;
            m_transaction = db.BeginTransaction();
        }

        public Task<long> RegisterRemoteVolumeAsync(string name, RemoteVolumeType type, RemoteVolumeState state)
        {
            return RunOnMain(() => m_db.RegisterRemoteVolume(name, type, state, m_transaction));
        }

        public Task UpdateRemoteVolume(string name, RemoteVolumeState state, long size, string hash)
        {
            return RunOnMain(() => m_db.UpdateRemoteVolume(name, state, size, hash, m_transaction));
        }

        public Task CommitTransactionAsync(string message, bool restart = true)
        {
            return RunOnMain(() => 
            {
                m_transaction.Commit();
                if (restart)
                    m_transaction = m_db.BeginTransaction();
                else
                    m_transaction = null;
            });
        }


        public Task RenameRemoteFileAsync(string oldname, string newname)
        {
            return RunOnMain(() => m_db.RenameRemoteFile(oldname, newname, m_transaction));
        }

        public Task LogRemoteOperationAsync(string operation, string path, string data)
        {
            return RunOnMain(() => m_db.LogRemoteOperation(operation, path, data, m_transaction));
        }

    }
}

