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
    internal class DatabaseCommon : SingleRunner, IBackendHandlerDatabase, IDisposable
    {
        protected LocalDatabase m_db;
        protected Options m_options;

        public DatabaseCommon(LocalDatabase db, Options options)
            : base()
        {
            m_db = db;
            m_options = options;
        }

        public Task<long> RegisterRemoteVolumeAsync(string name, RemoteVolumeType type, RemoteVolumeState state)
        {
            return RunOnMain(() => m_db.RegisterRemoteVolume(name, type, state));
        }

        /// <summary>
        /// Updates the remote volume information.
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="name">The name of the remote volume to update.</param>
        /// <param name="state">The new volume state.</param>
        /// <param name="size">The new volume size.</param>
        /// <param name="hash">The new volume hash.</param>
        /// <param name="suppressCleanup">If set to <c>true</c> suppress cleanup operation.</param>
        /// <param name="deleteGraceTime">The new delete grace time.</param>
        public Task UpdateRemoteVolumeAsync(string name, RemoteVolumeState state, long size, string hash, bool suppressCleanup = false, TimeSpan deleteGraceTime = default(TimeSpan))
        {
            return RunOnMain(() => m_db.UpdateRemoteVolume(name, state, size, hash, suppressCleanup, deleteGraceTime));
        }

        /// <summary>
        /// Writes the current changes to the database
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="message">The message to use for logging the time spent in this operation.</param>
        /// <param name="restart">If set to <c>true</c>, a transaction will be started again after this call.</param>
        public Task CommitTransactionAsync(string message, bool restart = true)
        {
            return RunOnMain(() => 
            {
                if (m_options.Dryrun)
                    m_db.RollbackTransaction(restart);
                else
                    m_db.CommitTransaction(message, restart);
            });
        }

        public Task RollbackTransactionAsync(bool restart = true)
        {
            return RunOnMain(() =>
            {
                m_db.RollbackTransaction(restart);
            });
        }

        /// <summary>
        /// Renames a remote file in the database
        /// </summary>
        /// <returns>The remote file to rename.</returns>
        /// <param name="oldname">The old filename.</param>
        /// <param name="newname">The new filename.</param>
        public Task RenameRemoteFileAsync(string oldname, string newname)
        {
            return RunOnMain(() => m_db.RenameRemoteFile(oldname, newname));
        }

        /// <summary>
        /// Writes remote operation log data to the database
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="operation">The operation performed.</param>
        /// <param name="path">The remote path used.</param>
        /// <param name="data">Any data reported by the operation.</param>
        public Task LogRemoteOperationAsync(string operation, string path, string data)
        {
            return RunOnMain(() => m_db.LogRemoteOperation(operation, path, data));
        }

        public Task<IEnumerable<LocalDatabase.IBlock>> GetBlocksAsync(long volumeid)
        {
            // TODO: Consider AsyncEnumerable
            return RunOnMain(() => m_db.GetBlocks(volumeid).ToArray().AsEnumerable());
        }

        public Task<RemoteVolumeEntry> GetVolumeInfoAsync(string remotename)
        {
            return RunOnMain(() => m_db.GetRemoteVolume(remotename));
        }

        public Task<IEnumerable<Tuple<string, byte[], int>>> GetBlocklistsAsync(long volumeid, int blocksize, int hashsize)
        {
            // TODO: Consider AsyncEnumerable
            return RunOnMain(() => m_db.GetBlocklists(volumeid, blocksize, hashsize).ToArray().AsEnumerable());
        }

        public Task<long> GetRemoteVolumeIDAsync(string remotename)
        {
            return RunOnMain(() => m_db.GetRemoteVolumeID(remotename));
        }

        public Task AddIndexBlockLinkAsync(long indexVolumeID, long blockVolumeID)
        {
            return RunOnMain(() => m_db.AddIndexBlockLink(indexVolumeID, blockVolumeID));
        }


        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
        }

    }
}

