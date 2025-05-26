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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Utility;

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
        protected readonly Options m_options;

        public DatabaseCommon(LocalDatabase db, Options options)
            : base()
        {
            m_db = db;
            m_options = options;
        }

        public Task<long> RegisterRemoteVolumeAsync(string name, RemoteVolumeType type, RemoteVolumeState state)
        {
            return RunOnMain(async () => await m_db.RegisterRemoteVolume(name, type, state));
        }

        public Task UpdateRemoteVolumeAsync(string name, RemoteVolumeState state, long size, string hash, bool suppressCleanup = false, TimeSpan deleteGraceTime = default(TimeSpan), bool? setArchived = null)
        {
            return RunOnMain(async () => await m_db.UpdateRemoteVolume(name, state, size, hash, suppressCleanup, deleteGraceTime, setArchived));
        }

        public async Task FlushBackendMessagesAndCommitAsync(IBackendManager backendManager)
        {
            await FlushPendingBackendMessagesAsync(backendManager).ConfigureAwait(false);
            await CommitTransactionAsync("FlushBackendMessagesAndCommitAsync").ConfigureAwait(false);
        }

        private Task FlushPendingBackendMessagesAsync(IBackendManager backendManager)
            => RunOnMain(async () => await backendManager.FlushPendingMessagesAsync(m_db, CancellationToken.None).ConfigureAwait(false));

        public Task CommitTransactionAsync(string message, bool restart = true)
        {
            return RunOnMain(async () =>
            {
                if (m_options.Dryrun)
                {
                    await m_db.Transaction.RollBackAsync();
                }
                else
                {
                    using (new Logging.Timer(LOGTAG, "CommitTransactionAsync", message))
                        await m_db.Transaction.CommitAsync(message, restart);
                }
            });
        }

        public Task RollbackTransactionAsync()
        {
            return RunOnMain(async () => await m_db.Transaction.RollBackAsync());
        }

        public Task RenameRemoteFileAsync(string oldname, string newname)
        {
            return RunOnMain(async () => await m_db.RenameRemoteFile(oldname, newname));
        }

        public Task LogRemoteOperationAsync(string operation, string path, string data)
        {
            return RunOnMain(async () => await m_db.LogRemoteOperation(operation, path, data));
        }

        public Task<LocalDatabase.IBlock[]> GetBlocksAsync(long volumeid)
        {
            // TODO: Figure out how to return the enumerable, while keeping the lock
            // and not creating the entire result in memory
            return RunOnMain(async () => await m_db.GetBlocks(volumeid).ToArrayAsync());
        }

        public Task<RemoteVolumeEntry> GetVolumeInfoAsync(string remotename)
        {
            return RunOnMain(async () => await m_db.GetRemoteVolume(remotename));
        }

        public Task<(string Hash, byte[] Buffer, int Size)[]> GetBlocklistsAsync(long volumeid, int blocksize, int hashsize)
        {
            // TODO: Figure out how to return the enumerable, while keeping the lock
            // and not creating the entire result in memory
            return RunOnMain(async () => await m_db.GetBlocklists(volumeid, blocksize, hashsize).ToArrayAsync());
        }

        public Task<long> GetRemoteVolumeIDAsync(string remotename)
        {
            return RunOnMain(async () => await m_db.GetRemoteVolumeID(remotename));
        }

        public Task AddIndexBlockLinkAsync(long indexVolumeID, long blockVolumeID)
        {
            return RunOnMain(async () => await m_db.AddIndexBlockLink(indexVolumeID, blockVolumeID));
        }


        protected override void Dispose(bool isDisposing)
        {
            if (m_workerSource.IsCancellationRequested)
                return;

            base.Dispose(isDisposing);
        }

    }
}

