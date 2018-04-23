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
using System.Linq;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main.Database;
using System.Collections.Generic;

namespace Duplicati.Library.Main.Operation.Common
{
    /// <summary>
    /// Asynchronous interface that ensures all requests
    /// to the database are performed in a sequential manner
    /// </summary>
    internal class DatabaseCommon : SingleRunner, IDisposable
    {
        /// <summary>
        /// The tag used for log messages
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<DatabaseCommon>();

        protected LocalDatabase m_db;
        protected System.Data.IDbTransaction m_transaction;
        protected Options m_options;

        public DatabaseCommon(LocalDatabase db, Options options)
            : base()
        {
            m_db = db;
            m_options = options;
            m_transaction = db.BeginTransaction();
        }

        public Task<long> RegisterRemoteVolumeAsync(string name, RemoteVolumeType type, RemoteVolumeState state)
        {
            return RunOnMain(() => m_db.RegisterRemoteVolume(name, type, state, m_transaction));
        }

        public Task UpdateRemoteVolumeAsync(string name, RemoteVolumeState state, long size, string hash, bool suppressCleanup = false, TimeSpan deleteGraceTime = default(TimeSpan))
        {
            return RunOnMain(() => m_db.UpdateRemoteVolume(name, state, size, hash, suppressCleanup, deleteGraceTime, m_transaction));
        }

        public Task CommitTransactionAsync(string message, bool restart = true)
        {
            return RunOnMain(() => 
            {
                if (m_options.Dryrun)
                {
                    if (!restart)
                    {
                        if (m_transaction != null)
                            m_transaction.Rollback();
                        m_transaction = null;
                    }
                    return;                    
                }

                using(new Logging.Timer(LOGTAG, "CommitTransactionAsync", message))
                    m_transaction.Commit();
                if (restart)
                    m_transaction = m_db.BeginTransaction();
                else
                    m_transaction = null;
            });
        }

        public Task RollbackTransactionAsync(bool restart = true)
        {
            return RunOnMain(() =>
            {
                m_transaction.Rollback();
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

        public Task<IEnumerable<LocalDatabase.IBlock>> GetBlocksAsync(long volumeid)
        {
            // TODO: How does the IEnumerable work with RunOnMain ?
            return RunOnMain(() => m_db.GetBlocks(volumeid, m_transaction).ToArray().AsEnumerable());
        }

        public Task<RemoteVolumeEntry> GetVolumeInfoAsync(string remotename)
        {
            return RunOnMain(() => m_db.GetRemoteVolume(remotename, m_transaction));
        }

        public Task<IEnumerable<Tuple<string, byte[], int>>> GetBlocklistsAsync(long volumeid, int blocksize, int hashsize)
        {
            // TODO: How does the IEnumerable work with RunOnMain ?
            return RunOnMain(() => m_db.GetBlocklists(volumeid, blocksize, hashsize, m_transaction));
        }

        public Task<long> GetRemoteVolumeIDAsync(string remotename)
        {
            return RunOnMain(() => m_db.GetRemoteVolumeID(remotename, m_transaction));
        }

        public Task AddIndexBlockLinkAsync(long indexVolumeID, long blockVolumeID)
        {
            return RunOnMain(() => m_db.AddIndexBlockLink(indexVolumeID, blockVolumeID, m_transaction));
        }


        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            if (m_transaction != null)
            {
                m_transaction.Commit();
                m_transaction = null;
            }
        }

    }
}

