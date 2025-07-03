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

        public Task<long> RegisterRemoteVolumeAsync(string name, RemoteVolumeType type, RemoteVolumeState state, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_db
                    .RegisterRemoteVolume(name, type, state, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task UpdateRemoteVolumeAsync(string name, RemoteVolumeState state, long size, string hash, bool suppressCleanup, TimeSpan deleteGraceTime, bool? setArchived, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_db
                    .UpdateRemoteVolume(name, state, size, hash, suppressCleanup, deleteGraceTime, setArchived, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public async Task FlushBackendMessagesAndCommitAsync(IBackendManager backendManager, CancellationToken cancellationToken)
        {
            await FlushPendingBackendMessagesAsync(backendManager, cancellationToken).ConfigureAwait(false);
            await CommitTransactionAsync("FlushBackendMessagesAndCommitAsync", true, cancellationToken).ConfigureAwait(false);
        }

        private Task FlushPendingBackendMessagesAsync(IBackendManager backendManager, CancellationToken cancellationToken)
            => RunOnMain(async () => await backendManager.FlushPendingMessagesAsync(m_db, cancellationToken).ConfigureAwait(false));

        public Task CommitTransactionAsync(string message, bool restart, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
            {
                if (m_options.Dryrun)
                {
                    await m_db.Transaction
                        .RollBackAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    using (new Logging.Timer(LOGTAG, "CommitTransactionAsync", message))
                        await m_db.Transaction
                            .CommitAsync(message, restart, cancellationToken)
                            .ConfigureAwait(false);
                }
            });
        }

        public Task RollbackTransactionAsync(CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_db.Transaction
                    .RollBackAsync(cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task RenameRemoteFileAsync(string oldname, string newname, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_db
                    .RenameRemoteFile(oldname, newname, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task LogRemoteOperationAsync(string operation, string path, string data, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_db
                    .LogRemoteOperation(operation, path, data, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<LocalDatabase.IBlock[]> GetBlocksAsync(long volumeid, CancellationToken cancellationToken)
        {
            // TODO: Figure out how to return the enumerable, while keeping the lock
            // and not creating the entire result in memory
            return RunOnMain(async () =>
                await m_db
                    .GetBlocks(volumeid, cancellationToken)
                    .ToArrayAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<RemoteVolumeEntry> GetVolumeInfoAsync(string remotename, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_db
                    .GetRemoteVolume(remotename, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<(string Hash, byte[] Buffer, int Size)[]> GetBlocklistsAsync(long volumeid, int blocksize, int hashsize, CancellationToken cancellationToken)
        {
            // TODO: Figure out how to return the enumerable, while keeping the lock
            // and not creating the entire result in memory
            return RunOnMain(async () =>
                await m_db
                    .GetBlocklists(volumeid, blocksize, hashsize, cancellationToken)
                    .ToArrayAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task<long> GetRemoteVolumeIDAsync(string remotename, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_db
                    .GetRemoteVolumeID(remotename, cancellationToken)
                    .ConfigureAwait(false)
            );
        }

        public Task AddIndexBlockLinkAsync(long indexVolumeID, long blockVolumeID, CancellationToken cancellationToken)
        {
            return RunOnMain(async () =>
                await m_db
                    .AddIndexBlockLink(indexVolumeID, blockVolumeID, cancellationToken)
                    .ConfigureAwait(false)
            );
        }


        protected override void Dispose(bool isDisposing)
        {
            if (m_workerSource.IsCancellationRequested)
                return;

            base.Dispose(isDisposing);
        }

    }
}

