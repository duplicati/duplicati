// Copyright (C) 2024, The Duplicati Team
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

        protected readonly LocalDatabase m_db;
        protected System.Data.IDbTransaction m_transaction;
        protected readonly Options m_options;

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

        public Task<LocalDatabase.IBlock[]> GetBlocksAsync(long volumeid)
        {
            // TODO: Figure out how to return the enumerable, while keeping the lock
            // and not creating the entire result in memory
            return RunOnMain(() => m_db.GetBlocks(volumeid, m_transaction).ToArray());
        }

        public Task<RemoteVolumeEntry> GetVolumeInfoAsync(string remotename)
        {
            return RunOnMain(() => m_db.GetRemoteVolume(remotename, m_transaction));
        }

        public Task<Tuple<string, byte[], int>[]> GetBlocklistsAsync(long volumeid, int blocksize, int hashsize)
        {
            // TODO: Figure out how to return the enumerable, while keeping the lock
            // and not creating the entire result in memory
            return RunOnMain(() => m_db.GetBlocklists(volumeid, blocksize, hashsize, m_transaction).ToArray());
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

