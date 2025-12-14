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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main.Operation
{
    internal class SetLocksHandler
    {
        private static readonly string LOGTAG = Log.LogTagFromType<SetLocksHandler>();

        private readonly Options m_options;
        private readonly SetLockResults m_result;
        private readonly IEnumerable<DateTime>? m_versionTimestamps;

        public SetLocksHandler(Options options, SetLockResults result, IEnumerable<DateTime>? versionTimestamps = null)
        {
            m_options = options;
            m_result = result;
            m_versionTimestamps = versionTimestamps;
        }

        public Task RunAsync(IBackendManager backendManager, IEnumerable<DateTime>? versionTimestamps = null)
            => RunAsync(backendManager, null, versionTimestamps);

        internal async Task RunAsync(IBackendManager backendManager, Database.LocalLockDatabase? databaseOverride, IEnumerable<DateTime>? versionTimestamps = null)
        {
            if (m_options.FileLockDuration is null)
                throw new UserInformationException("No lock duration specified", "MissingLockDuration");

            if (m_options.FileLockDuration.Value.TotalSeconds < 5)
                throw new UserInformationException("Lock duration must be at least 5 seconds", "LockDurationTooShort");

            if (!backendManager.SupportsObjectLocking)
                throw new UserInformationException("Backend does not support object locking", "BackendDoesNotSupportLocking");

            if (!File.Exists(m_options.Dbpath))
                throw new Exception(string.Format("Database file does not exist: {0}", m_options.Dbpath));

            var effectiveVersionTimestamps = versionTimestamps ?? m_versionTimestamps;

            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Lock);

            var ownsDatabase = databaseOverride is null;
            await using var db = ownsDatabase
                ? await LocalLockDatabase.CreateAsync(m_options.Dbpath, null, m_result.TaskControl.ProgressToken).ConfigureAwait(false)
                : null;
            var database = databaseOverride ?? db!;

            var filesetIds = await ResolveFilesetIdsAsync(database, effectiveVersionTimestamps).ConfigureAwait(false);

            if (filesetIds.Count == 0)
                throw new UserInformationException("No version specified", "NoVersionForLockOperation");

            var lockUntilUtc = DateTime.UtcNow + m_options.FileLockDuration.Value;

            long readCount = 0;
            long updatedCount = 0;

            foreach (var filesetId in filesetIds)
            {
                await foreach ((var volumeName, var lockUntil) in database.GetRemoteVolumesDependingOnFilesets([filesetId], m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                {
                    readCount++;
                    try
                    {
                        if (lockUntil.HasValue && lockUntil.Value >= lockUntilUtc)
                        {
                            Log.WriteInformationMessage(LOGTAG, "SkipSetObjectLock", "Skipping setting object lock for {0} in fileset {1} as existing lock until {2:u} is later than requested lock until {3:u}", volumeName, filesetId, lockUntil.Value, lockUntilUtc);
                            continue;
                        }

                        await backendManager.SetObjectLockUntilAsync(volumeName, lockUntilUtc, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                        // Update the lock expiration time in the database
                        await database.UpdateRemoteVolumeLockExpiration(volumeName, lockUntilUtc, m_result.TaskControl.ProgressToken).ConfigureAwait(false);

                        updatedCount++;
                    }
                    catch (Exception ex)
                    {
                        Log.WriteWarningMessage(LOGTAG, "SetObjectLockFailed", ex, "Failed to apply object lock for {0} in fileset {1}: {2}", volumeName, filesetId, ex.Message);
                    }
                }
            }

            await backendManager.WaitForEmptyAsync(m_result.TaskControl.ProgressToken).ConfigureAwait(false);

            m_result.VolumesRead = readCount;
            m_result.VolumesUpdated = updatedCount;
        }

        private async Task<List<long>> ResolveFilesetIdsAsync(Database.LocalListDatabase db, IEnumerable<DateTime>? suppliedVersions)
        {
            var filesetIds = new List<long>();
            var token = m_result.TaskControl.ProgressToken;

            var versions = suppliedVersions?.ToArray();
            var hasSuppliedVersions = versions != null && versions.Length > 0;

            if (!hasSuppliedVersions && !m_options.AllVersions && (m_options.Version == null || m_options.Version.Length == 0) && m_options.Time.Ticks == 0)
                throw new UserInformationException("No version specified", "NoVersionForLockOperation");

            if (hasSuppliedVersions)
            {
                foreach (var versionTime in versions!)
                {
                    var matched = await db
                        .GetFilesetIDs(versionTime, null, true, token)
                        .ToArrayAsync(cancellationToken: token)
                        .ConfigureAwait(false);

                    if (matched.Length > 0)
                        filesetIds.AddRange(matched);
                }
            }
            else if (m_options.AllVersions)
            {
                filesetIds.AddRange(await db
                    .FilesetTimes(token)
                    .Select(x => x.Key)
                    .ToArrayAsync(token)
                    .ConfigureAwait(false));
            }
            else
            {
                var matched = await db
                    .GetFilesetIDs(m_options.Time, m_options.Version, false, token)
                    .ToArrayAsync(cancellationToken: token)
                    .ConfigureAwait(false);

                filesetIds.AddRange(matched);
            }

            return filesetIds;
        }
    }
}
