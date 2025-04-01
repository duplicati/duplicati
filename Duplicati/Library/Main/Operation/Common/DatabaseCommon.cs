// Copyright (C) 2025, The Duplicati Team
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
using Duplicati.Library.Main.Database;

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
        private System.Data.IDbTransaction m_transaction;
        protected readonly Options m_options;

        public DatabaseCommon(LocalDatabase db, Options options)
            : base()
        {
            m_db = db;
            m_options = options;
            m_transaction = db.BeginTransaction();
        }

        protected System.Data.IDbTransaction GetTransaction()
        {
            if (m_transaction == null)
                m_transaction = m_db.BeginTransaction();
            return m_transaction;
        }

        public Task<long> RegisterRemoteVolumeAsync(string name, RemoteVolumeType type, RemoteVolumeState state)
        {
            return RunOnMain(() => m_db.RegisterRemoteVolume(name, type, state, GetTransaction()));
        }

        public Task UpdateRemoteVolumeAsync(string name, RemoteVolumeState state, long size, string hash, bool suppressCleanup = false, TimeSpan deleteGraceTime = default(TimeSpan))
        {
            return RunOnMain(() => m_db.UpdateRemoteVolume(name, state, size, hash, suppressCleanup, deleteGraceTime, GetTransaction()));
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

                if (m_transaction != null)
                {
                    using (new Logging.Timer(LOGTAG, "CommitTransactionAsync", message))
                        m_transaction.Commit();
                    m_transaction = null;
                }
            });
        }

        public Task RollbackTransactionAsync()
        {
            return RunOnMain(() =>
            {
                if (m_transaction != null)
                {
                    m_transaction.Rollback();
                    m_transaction = null;
                }
            });
        }



        public Task RenameRemoteFileAsync(string oldname, string newname)
        {
            return RunOnMain(() => m_db.RenameRemoteFile(oldname, newname, GetTransaction()));
        }

        public Task LogRemoteOperationAsync(string operation, string path, string data)
        {
            return RunOnMain(() => m_db.LogRemoteOperation(operation, path, data, GetTransaction()));
        }

        public Task<LocalDatabase.IBlock[]> GetBlocksAsync(long volumeid)
        {
            // TODO: Figure out how to return the enumerable, while keeping the lock
            // and not creating the entire result in memory
            return RunOnMain(() => m_db.GetBlocks(volumeid, GetTransaction()).ToArray());
        }

        public Task<RemoteVolumeEntry> GetVolumeInfoAsync(string remotename)
        {
            return RunOnMain(() => m_db.GetRemoteVolume(remotename, GetTransaction()));
        }

        public Task<Tuple<string, byte[], int>[]> GetBlocklistsAsync(long volumeid, int blocksize, int hashsize)
        {
            // TODO: Figure out how to return the enumerable, while keeping the lock
            // and not creating the entire result in memory
            return RunOnMain(() => m_db.GetBlocklists(volumeid, blocksize, hashsize, GetTransaction()).ToArray());
        }

        public Task<long> GetRemoteVolumeIDAsync(string remotename)
        {
            return RunOnMain(() => m_db.GetRemoteVolumeID(remotename, GetTransaction()));
        }

        public Task AddIndexBlockLinkAsync(long indexVolumeID, long blockVolumeID)
        {
            return RunOnMain(() => m_db.AddIndexBlockLink(indexVolumeID, blockVolumeID, GetTransaction()));
        }


        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            var tr = System.Threading.Interlocked.Exchange(ref m_transaction, null);
            tr?.Commit();
            tr?.Dispose();
        }

    }
}

