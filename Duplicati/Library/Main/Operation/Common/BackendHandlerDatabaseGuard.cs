//  Copyright (C) 2018, The Duplicati Team
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

namespace Duplicati.Library.Main.Operation.Common
{
    /// <summary>
    /// This class provides a thread-safe wrapper around a non-thread safe
    /// database instance, such that the database can be safely used within
    /// the <see cref="BackendHandler"/> without needing to rewrite all the
    /// code that does not handle concurrent access.
    /// </summary>
    internal class BackendHandlerDatabaseGuard : IDisposable, IBackendHandlerDatabase, IIndexVolumeCreatorDatabase
    {
        /// <summary>
        /// The database we are wrapping
        /// </summary>
        private readonly Database.LocalDatabase m_db;
        /// <summary>
        /// A flag indicating if this is a dry-run
        /// </summary>
        private readonly bool m_dryrun;
        /// <summary>
        /// The list of pending work
        /// </summary>
        private List<Tuple<Action, TaskCompletionSource<bool>>> m_workQueue = new List<Tuple<Action, TaskCompletionSource<bool>>>();
        /// <summary>
        /// The lock object protecting the queue.
        /// </summary>
        private readonly object m_lock = new object();
        /// <summary>
        /// The main thread, used to guard against execution from another thread
        /// </summary>
        private readonly System.Threading.Thread m_mainthread;

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="T:Duplicati.Library.Main.Operation.Common.BackendHandlerDatabaseGuard"/> class.
        /// </summary>
        /// <param name="db">The database to wrap.</param>
        /// <param name="dryrun">A flag indicating if this is a dry-run</param>
        public BackendHandlerDatabaseGuard(Database.LocalDatabase db, bool dryrun)
        {
            m_db = db ?? throw new ArgumentNullException(nameof(db));
            m_mainthread = System.Threading.Thread.CurrentThread;
            m_dryrun = dryrun;
        }

        /// <summary>
        /// Adds a pending work item to the queue
        /// </summary>
        /// <returns>The task wait handle.</returns>
        /// <param name="action">The method to run when ready.</param>
        private Task AddToQueue(Action action)
        {
            if (m_workQueue == null)
                throw new ObjectDisposedException("This database guard instance has been shut down");


            var tcs = new TaskCompletionSource<bool>();
            lock (m_lock)
                m_workQueue.Add(new Tuple<Action, TaskCompletionSource<bool>>(action, tcs));
            return tcs.Task;
        }

        /// <summary>
        /// Writes the current changes to the database
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="message">The message to use for logging the time spent in this operation.</param>
        /// <param name="restart">If set to <c>true</c>, a transaction will be started again after this call.</param>
        public Task CommitTransactionAsync(string message, bool restart = true)
        {
            return AddToQueue(() =>
            {
                if (m_dryrun)
                {
                    if (!restart)
                        m_db.RollbackTransaction();
                    return;
                }
                m_db.CommitTransaction(message, restart);
            });
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
            return AddToQueue(() => { m_db.LogRemoteOperation(operation, path, data); });
        }

        /// <summary>
        /// Renames a remote file in the database
        /// </summary>
        /// <returns>The remote file to rename.</returns>
        /// <param name="oldname">The old filename.</param>
        /// <param name="newname">The new filename.</param>
        public Task RenameRemoteFileAsync(string oldname, string newname)
        {
            return AddToQueue(() => { m_db.RenameRemoteFile(oldname, newname); });
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
            return AddToQueue(() => { m_db.UpdateRemoteVolume(name, state, size, hash, suppressCleanup, deleteGraceTime); });
        }

        /// <summary>
        /// Gets the volume information for a remote file, given the name.
        /// </summary>
        /// <returns>The remote volume information.</returns>
        /// <param name="remotename">The name of the remote file to query.</param>
        public async Task<Database.RemoteVolumeEntry> GetVolumeInfoAsync(string remotename)
        {
            var res = default(Database.RemoteVolumeEntry);
            await AddToQueue(() => res = m_db.GetRemoteVolume(remotename));
            return res;
        }


        /// <summary>
        /// Creates and registers a remote volume
        /// </summary>
        /// <returns>The newly created volume ID.</returns>
        /// <param name="name">The name of the remote file.</param>
        /// <param name="type">The type of the remote file.</param>
        /// <param name="state">The state of the remote file.</param>
        public async Task<long> RegisterRemoteVolumeAsync(string name, RemoteVolumeType type, RemoteVolumeState state)
        {
            var res = default(long);
            await AddToQueue(() => res = m_db.RegisterRemoteVolume(name, type, state));
            return res;
        }

        /// <summary>
        /// Gets a list of all blocks associated with a given volume
        /// </summary>
        /// <returns>The blocks found in the volume.</returns>
        /// <param name="volumeid">The ID of the volume to examine.</param>
        public async Task<IEnumerable<Database.LocalDatabase.IBlock>> GetBlocksAsync(long volumeid)
        {
            var res = default(IEnumerable<Database.LocalDatabase.IBlock>);
            await AddToQueue(() => res = m_db.GetBlocks(volumeid));
            return res;
        }

        /// <summary>
        /// Gets the blocklists contained in a remote volume
        /// </summary>
        /// <returns>The blocklists.</returns>
        /// <param name="volumeid">The ID of the volume to get the blocklists for.</param>
        /// <param name="blocksize">The blocksize setting.</param>
        /// <param name="hashsize">The size of the hash in bytes.</param>
        public async Task<IEnumerable<Tuple<string, byte[], int>>> GetBlocklistsAsync(long volumeid, int blocksize, int hashsize)
        {
            var res = default(IEnumerable<Tuple<string, byte[], int>>);
            await AddToQueue(() => res = m_db.GetBlocklists(volumeid, blocksize, hashsize));
            return res;
        }

        /// <summary>
        /// Adds a link between a block volume and an index volume
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="indexVolumeID">The index volume ID.</param>
        /// <param name="blockVolumeID">The block volume ID.</param>
        public Task AddIndexBlockLinkAsync(long indexVolumeID, long blockVolumeID)
        {
            return AddToQueue(() => m_db.AddIndexBlockLink(indexVolumeID, blockVolumeID));
        }

        /// <summary>
        /// Processes all pending operations into the database.
        /// </summary>
        /// <param name="transaction">The transaction instance</param>
        public void ProcessAllPendingOperations()
        {
            //if (m_mainthread != System.Threading.Thread.CurrentThread)
            //    throw new InvalidOperationException("Attempted to flush database work queue from another thread");

            // If we are shut down, just exit
            if (m_workQueue == null)
                return;

            var prevqueue = m_workQueue;
            lock (m_lock)
            {
                // No need to fiddle if nothing has happened
                if (prevqueue.Count == 0)
                    return;

                m_workQueue = new List<Tuple<Action, TaskCompletionSource<bool>>>();
            }

            // We repeat here to allow quicker progress in case the backend is blocked
            // on a log message, and emits a new one immediately after having one handled
            while (prevqueue.Count != 0)
            {
                foreach (var op in prevqueue)
                {
                    try
                    {
                        op.Item1();
                        op.Item2.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        op.Item2.TrySetException(ex);
                    }
                }

                // Inject a sleep here to allow the BackendHandler to progress
                // Not pretty, but this entire class should be deleted once the code is 
                // rewritten to use thread-safe database access
                System.Threading.Thread.Sleep(100);

                prevqueue = m_workQueue;
                lock (m_lock)
                    m_workQueue = new List<Tuple<Action, TaskCompletionSource<bool>>>();
            }
        }

        /// <summary>
        /// Releases all resource used by the
        /// <see cref="T:Duplicati.Library.Main.Operation.Common.BackendHandlerDatabaseGuard"/> object.
        /// </summary>
        /// <remarks>Call <see cref="Dispose"/> when you are finished using the
        /// <see cref="T:Duplicati.Library.Main.Operation.Common.BackendHandlerDatabaseGuard"/>. The
        /// <see cref="Dispose"/> method leaves the
        /// <see cref="T:Duplicati.Library.Main.Operation.Common.BackendHandlerDatabaseGuard"/> in an unusable state.
        /// After calling <see cref="Dispose"/>, you must release all references to the
        /// <see cref="T:Duplicati.Library.Main.Operation.Common.BackendHandlerDatabaseGuard"/> so the garbage collector
        /// can reclaim the memory that the
        /// <see cref="T:Duplicati.Library.Main.Operation.Common.BackendHandlerDatabaseGuard"/> was occupying.</remarks>
        public void Dispose()
        {
            m_db.Dispose();
            lock (m_lock)
            {
                if (m_workQueue != null)
                    foreach (var n in m_workQueue)
                        n.Item2.TrySetCanceled();
                m_workQueue = null;
            }
        }


    }
}
