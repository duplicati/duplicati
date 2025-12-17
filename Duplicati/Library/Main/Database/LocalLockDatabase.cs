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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Database
{
    /// <summary>
    /// A local database specialized for object lock operations.
    /// </summary>
    internal class LocalLockDatabase : LocalListDatabase
    {
        /// <summary>
        /// Creates a new instance of the <see cref="LocalLockDatabase"/> class.
        /// </summary>
        /// <param name="path">The path to the database file.</param>
        /// <param name="dbnew">An optional existing database instance to use. Used to mimic constructor chaining.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains a new instance of <see cref="LocalLockDatabase"/>.</returns>
        public static async Task<LocalLockDatabase> CreateAsync(string path, LocalLockDatabase? dbnew, CancellationToken token)
        {
            dbnew ??= new LocalLockDatabase();

            dbnew = (LocalLockDatabase)
                await CreateLocalDatabaseAsync(path, "Lock", false, dbnew, token)
                    .ConfigureAwait(false);
            dbnew.ShouldCloseConnection = true;

            return dbnew;
        }

        /// <summary>
        /// Updates the lock expiration time for a remote volume.
        /// </summary>
        /// <param name="name">The name of the remote volume.</param>
        /// <param name="lockExpirationTime">The lock expiration time, or null to clear the lock.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the update is done.</returns>
        public async Task UpdateRemoteVolumeLockExpiration(string name, DateTime lockExpirationTime, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand();
            cmd.SetTransaction(m_rtr);
            cmd.SetCommandAndParameters(@"
                UPDATE ""Remotevolume""
                SET ""LockExpirationTime"" = @LockExpirationTime
                WHERE ""Name"" = @Name
            ");
            cmd.SetParameterValue("@Name", name);
            cmd.SetParameterValue("@LockExpirationTime", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(lockExpirationTime));

            var c = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            if (c != 1)
                throw new Exception($"Unexpected number of remote volumes updated: {c}, expected 1");
        }

        /// <summary>
        /// Gets all remote volumes with their lock expiration times.
        /// </summary>
        /// <param name="onlyWithoutLockInformation">If true, only returns volumes without lock expiration</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An asynchronous sequence of tuples containing the volume name and lock expiration time.</returns>
        public async IAsyncEnumerable<(string Name, DateTime? LockExpirationTime)> GetRemoteVolumesWithLockExpiration(bool onlyWithoutLockInformation, [EnumeratorCancellation] CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand();
            cmd.SetTransaction(m_rtr);

            var whereClause = onlyWithoutLockInformation
                ? @"WHERE ""LockExpirationTime"" = 0"
                : "";

            cmd.SetCommandAndParameters($@"
                SELECT ""Name"", ""LockExpirationTime""
                FROM ""Remotevolume""
                {whereClause}
            ");

            await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await rd.ReadAsync(token).ConfigureAwait(false))
            {
                var name = rd.ConvertValueToString(0) ?? string.Empty;
                var lockExpiration = ParseFromEpochSeconds(rd.ConvertValueToInt64(1));
                yield return (name, lockExpiration);
            }
        }
    }
}
