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

#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main.Operation
{
    /// <summary>
    /// Handler for reading lock information from the backend and updating the database.
    /// </summary>
    internal class ReadLockInfoHandler
    {
        private static readonly string LOGTAG = Log.LogTagFromType<ReadLockInfoHandler>();

        private readonly Options m_options;
        private readonly ReadLockInfoResults m_result;

        /// <summary>
        /// Creates a new instance of the <see cref="ReadLockInfoHandler"/> class.
        /// </summary>
        /// <param name="options">The options for the operation.</param>
        /// <param name="backendWriter">The backend writer for logging.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ReadLockInfoHandler(Options options, ReadLockInfoResults result)
        {
            m_options = options;
            m_result = result;
        }

        /// <summary>
        /// Runs the read lock info operation, updating the database with lock expiration times from the backend.
        /// </summary>
        /// <param name="backendManager">The backend manager to use for reading lock info.</param>
        /// <returns>A task that completes when the operation is finished.</returns>
        public async Task RunAsync(IBackendManager backendManager)
        {
            await RunAsync(backendManager, null).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs the read lock info operation, updating the database with lock expiration times from the backend.
        /// </summary>
        /// <param name="backendManager">The backend manager to use for reading lock info.</param>
        /// <param name="databaseOverride">An optional database override to use instead of creating a new one.</param>
        /// <returns>A task that completes when the operation is finished.</returns>
        internal async Task RunAsync(IBackendManager backendManager, LocalLockDatabase? databaseOverride)
        {
            if (!backendManager.SupportsObjectLocking)
                throw new UserInformationException("Backend does not support object locking", "BackendDoesNotSupportLocking");

            if (!File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath), "DatabaseFileDoesNotExist");

            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.ReadLockInfo_Running);

            var ownsDatabase = databaseOverride is null;
            await using var db = ownsDatabase
                ? await LocalLockDatabase.CreateAsync(m_options.Dbpath, null, m_result.TaskControl.ProgressToken).ConfigureAwait(false)
                : null;
            var database = databaseOverride ?? db!;

            long readCount = 0;
            var updatedCount = 0;
            var errorCount = 0;

            await foreach (var (name, lockExpiration) in database.GetRemoteVolumesWithLockExpiration(!m_options.RefreshLockInfoComplete, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
            {
                readCount++;
                try
                {
                    var remoteLockExpiration = (await backendManager.GetObjectLockUntilAsync(name, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                        ?? DateTime.UnixEpoch;

                    // Only update if the value has changed
                    if (remoteLockExpiration != lockExpiration)
                    {
                        await database.UpdateRemoteVolumeLockExpiration(name, remoteLockExpiration, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                        updatedCount++;

                        Log.WriteVerboseMessage(LOGTAG, "UpdatedLockExpiration", "Updated lock expiration for {0} to {1}", name, remoteLockExpiration);
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    Log.WriteWarningMessage(LOGTAG, "GetObjectLockFailed", ex, "Failed to get object lock for {0}: {1}", name, ex.Message);
                }
            }

            await backendManager.WaitForEmptyAsync(m_result.TaskControl.ProgressToken).ConfigureAwait(false);

            m_result.VolumesRead = readCount;
            m_result.VolumesUpdated = updatedCount;

            if (updatedCount > 0 || errorCount > 0)
                Log.WriteInformationMessage(LOGTAG, "ReadLockInfoComplete", "Read lock info complete: {0} updated, {1} errors", updatedCount, errorCount);
        }
    }
}
