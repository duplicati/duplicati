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
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Duplicati.Library.Modules.Builtin.ResultSerialization;
using Duplicati.Library.Utility;
using Duplicati.Library.Interface;
using Microsoft.Data.Sqlite;
using System.Threading;

// Expose internal classes to UnitTests, so that Database classes can be tested
[assembly: InternalsVisibleTo("Duplicati.UnitTest")]

namespace Duplicati.Library.Main.Database
{
    /// <summary>
    /// Represents a local database for Duplicati operations.
    /// This class provides methods to interact with the local SQLite database, including
    /// managing remote volumes, logging operations, and handling transactions.
    /// </summary>
    internal class LocalDatabase : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(LocalDatabase));

        /// <summary>
        /// The chunk size for batch operations
        /// </summary>
        /// <remarks>SQLite has a limit of 999 parameters in a single statement</remarks>
        public const int CHUNK_SIZE = 128;

        // All of the required fields have been set to null! to ignore the compiler warning, as they will be initialized in the Create* factory methods.
        /// <summary>
        /// The SQLite connection to the local database.
        /// </summary>
        protected SqliteConnection m_connection = null!;
        /// <summary>
        /// The operation ID for the current operation.
        /// </summary>
        protected long m_operationid = -1;
        /// <summary>
        /// The size of the SQLite page cache.
        /// </summary>
        protected long m_pagecachesize;
        /// <summary>
        /// Indicates whether the database has executed a vacuum operation.
        /// </summary>
        private bool m_hasExecutedVacuum;
        /// <summary>
        /// The reusable transaction for the current operation.
        /// </summary>
        protected ReusableTransaction m_rtr = null!;
        /// <summary>
        /// A read-only property that provides access to the current transaction.
        /// </summary>
        public ReusableTransaction Transaction { get { return m_rtr; } }

        /// <summary>
        /// The command used to update a remote volume in the database.
        /// </summary>
        private SqliteCommand m_updateremotevolumeCommand = null!;
        /// <summary>
        /// The command used to select remote volumes from the database.
        /// </summary>
        private SqliteCommand m_selectremotevolumesCommand = null!;
        /// <summary>
        /// The command used to select a specific remote volume by name.
        /// </summary>
        private SqliteCommand m_selectremotevolumeCommand = null!;
        /// <summary>
        /// The command used to remove a remote volume from the database.
        /// </summary>
        private SqliteCommand m_removeremotevolumeCommand = null!;
        /// <summary>
        /// The command used to remove deleted remote volumes from the database.
        /// </summary>
        private SqliteCommand m_removedeletedremotevolumeCommand = null!;
        /// <summary>
        /// The command used to select the ID of a remote volume by name.
        /// </summary>
        private SqliteCommand m_selectremotevolumeIdCommand = null!;
        /// <summary>
        /// The command used to create a new remote volume in the database.
        /// </summary>
        private SqliteCommand m_createremotevolumeCommand = null!;
        /// <summary>
        /// The command used to select duplicate remote volumes from the database.
        /// </summary>
        private SqliteCommand m_selectduplicateRemoteVolumesCommand = null!;

        /// <summary>
        /// The command used to insert log data into the database.
        /// </summary>
        private SqliteCommand m_insertlogCommand = null!;
        /// <summary>
        /// The command used to insert remote operation logs into the database.
        /// </summary>
        private SqliteCommand m_insertremotelogCommand = null!;
        /// <summary>
        /// The command used to insert index block links into the database.
        /// </summary>
        private SqliteCommand m_insertIndexBlockLink = null!;

        /// <summary>
        /// The command used to find a path prefix in the database.
        /// </summary>
        private SqliteCommand m_findpathprefixCommand = null!;
        /// <summary>
        /// The command used to insert a new path prefix into the database.
        /// </summary>
        private SqliteCommand m_insertpathprefixCommand = null!;

        /// <summary>
        /// A constant representing the blockset ID for a folder.
        /// </summary>
        public const long FOLDER_BLOCKSET_ID = -100;
        /// <summary>
        /// A constant representing the blockset ID for a symlink.
        /// </summary>
        public const long SYMLINK_BLOCKSET_ID = -200;

        /// <summary>
        /// The timestamp of the operation being performed.
        /// </summary>
        public DateTime OperationTimestamp { get; private set; }

        /// <summary>
        /// A read-only property that provides access to the internal SQLite connection.
        /// </summary>
        internal SqliteConnection Connection { get { return m_connection; } }

        /// <summary>
        /// Indicates whether the database has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Indicates whether the connection should be closed when the database is disposed.
        /// </summary>
        public bool ShouldCloseConnection { get; set; }

        // Constructor is private to force use of CreateLocalDatabaseAsync
        protected LocalDatabase() { }

        // Arguments are not used, but are required to match the constructor signatures
        [Obsolete("Calling the constructor will throw an exception. Use the CreateLocalDatabaseAsync or CreateLocalDatabase functions instead")]
        public LocalDatabase(object? _ignore1 = null, object? _ignore2 = null, object? _ignore3 = null, object? _ignore4 = null)
        {
            throw new NotImplementedException("Use the CreateLocalDatabaseAsync or CreateLocalDatabase functions instead");
        }

        /// <summary>
        /// Creates a new SQLite connection to the specified database path with the given page cache size.
        /// This method ensures that the directory for the database exists and upgrades the database schema if necessary.
        /// </summary>
        /// <param name="path">The path to the SQLite database file.</param>
        /// <returns>A task that, when awaited, returns a new <see cref="SqliteConnection"/> to the specified database.</returns>
        protected static async Task<SqliteConnection> CreateConnectionAsync(string path)
        {
            path = Path.GetFullPath(path);
            if (!Directory.Exists(Path.GetDirectoryName(path)))
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new DirectoryNotFoundException("Path was a root folder."));

            var c = await SQLiteHelper.SQLiteLoader.LoadConnectionAsync(path)
                .ConfigureAwait(false);

            try
            {
                SQLiteHelper.DatabaseUpgrader.UpgradeDatabase(c, path, typeof(DatabaseSchemaMarker));
            }
            catch
            {
                //Don't leak database connections when something goes wrong
                await c.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            return c;
        }

        /// <summary>
        /// Creates a new instance of <see cref="LocalDatabase"/> with the specified parameters.
        /// This method initializes the database connection, sets the operation timestamp, and prepares the necessary commands for database operations.
        /// </summary>
        /// <param name="path">The path to the SQLite database file.</param>
        /// <param name="operation">The description of the operation being performed.</param>
        /// <param name="shouldclose">Indicates whether the connection should be closed when the database is disposed.</param>
        /// <param name="db">An optional existing <see cref="LocalDatabase"/> instance to use. If not provided, a new instance will be created.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns a new instance of <see cref="LocalDatabase"/>.</returns>
        public static async Task<LocalDatabase> CreateLocalDatabaseAsync(string path, string operation, bool shouldclose, LocalDatabase? db, CancellationToken token)
        {
            db ??= new LocalDatabase();

            var connection = await CreateConnectionAsync(path)
                .ConfigureAwait(false);
            db = await CreateLocalDatabaseAsync(connection, operation, db, token)
                .ConfigureAwait(false);

            db.ShouldCloseConnection = shouldclose;

            return db;
        }

        /// <summary>
        /// Creates a new instance of <see cref="LocalDatabase"/> based on an existing database instance.
        /// This method copies the connection and transaction from the parent database and initializes the new database with the same operation timestamp and ID.
        /// </summary>
        /// <param name="dbparent">The parent <see cref="LocalDatabase"/> instance to copy from.</param>
        /// <param name="dbnew">An optional existing <see cref="LocalDatabase"/> instance to use. If not provided, a new instance will be created.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns a new instance of <see cref="LocalDatabase"/>.</returns>
        public static async Task<LocalDatabase> CreateLocalDatabaseAsync(LocalDatabase dbparent, LocalDatabase? dbnew, CancellationToken token)
        {
            dbnew ??= new LocalDatabase();

            dbnew.m_connection = dbparent.m_connection;
            dbnew.m_rtr = dbparent.m_rtr;
            dbnew = await CreateLocalDatabaseAsync(dbparent.m_connection, dbnew, token)
                .ConfigureAwait(false);

            dbnew.OperationTimestamp = dbparent.OperationTimestamp;
            dbnew.m_operationid = dbparent.m_operationid;
            dbnew.m_pagecachesize = dbparent.m_pagecachesize;

            return dbnew;
        }

        /// <summary>
        /// Creates a new instance of <see cref="LocalDatabase"/> with the specified SQLite connection and operation description.
        /// This method initializes the database connection, sets the operation timestamp, and prepares the necessary commands for database operations.
        /// </summary>
        /// <param name="connection">The SQLite connection to use for the database operations.</param>
        /// <param name="operation">The description of the operation being performed.</param>
        /// <param name="dbnew">An optional existing <see cref="LocalDatabase"/> instance to use. If not provided, a new instance will be created.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns a new instance of <see cref="LocalDatabase"/>.</returns>
        public static async Task<LocalDatabase> CreateLocalDatabaseAsync(SqliteConnection connection, string operation, LocalDatabase? dbnew, CancellationToken token)
        {
            dbnew ??= new LocalDatabase();
            dbnew = await CreateLocalDatabaseAsync(connection, dbnew, token)
                .ConfigureAwait(false);

            dbnew.OperationTimestamp = DateTime.UtcNow;

            if (dbnew.m_connection.State != ConnectionState.Open)
                await dbnew.m_connection.OpenAsync(token).ConfigureAwait(false);

            await using var cmd = dbnew.m_connection.CreateCommand()
                .SetTransaction(dbnew.m_rtr);
            if (operation != null)
            {
                dbnew.m_operationid = await cmd.SetCommandAndParameters(@"
                    INSERT INTO ""Operation"" (
                        ""Description"",
                        ""Timestamp""
                    )
                    VALUES (
                        @Description,
                        @Timestamp
                    );
                    SELECT last_insert_rowid();
                ")
                    .SetParameterValue("@Description", operation)
                    .SetParameterValue("@Timestamp", dbnew.OperationTimestamp)
                    .ExecuteScalarInt64Async(-1, token)
                    .ConfigureAwait(false);
            }
            else
            {
                // Get last operation
                await using var rd = await cmd.ExecuteReaderAsync(@"
                    SELECT
                        ""ID"",
                        ""Timestamp""
                    FROM ""Operation""
                    ORDER BY ""Timestamp""
                    DESC LIMIT 1
                ", token)
                    .ConfigureAwait(false);

                if (!await rd.ReadAsync(token).ConfigureAwait(false))
                    throw new Exception("LocalDatabase does not contain a previous operation.");

                dbnew.m_operationid = rd.ConvertValueToInt64(0);
                dbnew.OperationTimestamp = ParseFromEpochSeconds(rd.ConvertValueToInt64(1));
            }

            return dbnew;
        }

        /// <summary>
        /// Creates a new instance of <see cref="LocalDatabase"/> with the specified SQLite connection.
        /// This method initializes the database connection, sets up the reusable transaction, and prepares the necessary commands for database operations.
        /// </summary>
        /// <param name="connection">The SQLite connection to use for the database operations.</param>
        /// <param name="dbnew">An optional existing <see cref="LocalDatabase"/> instance to use. If not provided, a new instance will be created.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns a new instance of <see cref="LocalDatabase"/>.</returns>
        private static async Task<LocalDatabase> CreateLocalDatabaseAsync(SqliteConnection connection, LocalDatabase? dbnew, CancellationToken token)
        {
            dbnew ??= new LocalDatabase();
            dbnew.m_connection = connection;
            dbnew.m_rtr ??= new ReusableTransaction(connection);

            var selectremotevolumes_sql = @"
                SELECT
                    ""ID"",
                    ""Name"",
                    ""Type"",
                    ""Size"",
                    ""Hash"",
                    ""State"",
                    ""DeleteGraceTime"",
                    ""ArchiveTime"",
                    ""LockExpirationTime""
                FROM ""Remotevolume""
            ";

            dbnew.m_insertlogCommand = await connection.CreateCommandAsync(@"
                INSERT INTO ""LogData"" (
                    ""OperationID"",
                    ""Timestamp"",
                    ""Type"",
                    ""Message"",
                    ""Exception""
                )
                VALUES (
                    @OperationID,
                    @Timestamp,
                    @Type,
                    @Message,
                    @Exception
                )
            ", token)
                .ConfigureAwait(false);

            dbnew.m_insertremotelogCommand = await connection.CreateCommandAsync(@"
                INSERT INTO ""RemoteOperation"" (
                    ""OperationID"",
                    ""Timestamp"",
                    ""Operation"",
                    ""Path"",
                    ""Data""
                )
                VALUES (
                    @OperationID,
                    @Timestamp,
                    @Operation,
                    @Path,
                    @Data
                )
            ", token)
                .ConfigureAwait(false);

            dbnew.m_updateremotevolumeCommand = await connection.CreateCommandAsync(@"
                UPDATE ""Remotevolume""
                SET
                    ""OperationID"" = @OperationID,
                    ""State"" = @State,
                    ""Hash"" = @Hash,
                    ""Size"" = @Size
                WHERE ""Name"" = @Name
            ", token)
                .ConfigureAwait(false);

            dbnew.m_selectremotevolumesCommand =
                await connection.CreateCommandAsync(selectremotevolumes_sql, token)
                    .ConfigureAwait(false);

            dbnew.m_selectremotevolumeCommand = await connection.CreateCommandAsync(@$"
                {selectremotevolumes_sql}
                WHERE ""Name"" = @Name
            ", token)
                .ConfigureAwait(false);

            dbnew.m_selectduplicateRemoteVolumesCommand = await connection.CreateCommandAsync($@"
                SELECT DISTINCT
                    ""Name"",
                    ""State""
                FROM ""Remotevolume""
                WHERE
                    ""Name"" IN (
                        SELECT ""Name""
                        FROM ""Remotevolume""
                        WHERE ""State"" IN ('{Library.Utility.Utility.FormatInvariantValue(RemoteVolumeState.Deleted)}', '{Library.Utility.Utility.FormatInvariantValue(RemoteVolumeState.Deleting)}')
                    )
                    AND NOT ""State"" IN ('{Library.Utility.Utility.FormatInvariantValue(RemoteVolumeState.Deleted)}', '{Library.Utility.Utility.FormatInvariantValue(RemoteVolumeState.Deleting)}')
            ", token)
                .ConfigureAwait(false);

            dbnew.m_removeremotevolumeCommand = await connection.CreateCommandAsync(@"
                DELETE FROM ""Remotevolume""
                WHERE
                    ""Name"" = @Name
                    AND (
                        ""DeleteGraceTime"" < @Now
                        OR ""State"" != @State
                    )
            ", token)
                .ConfigureAwait(false);

            // >12 is to handle removal of old records that were in ticks
            dbnew.m_removedeletedremotevolumeCommand = await connection.CreateCommandAsync($@"
                DELETE FROM ""Remotevolume""
                WHERE
                    ""State"" == '{Library.Utility.Utility.FormatInvariantValue(RemoteVolumeState.Deleted)}'
                    AND (
                        ""DeleteGraceTime"" < @Now
                        OR LENGTH(""DeleteGraceTime"") > 12
                    )
            ", token)
                .ConfigureAwait(false);

            dbnew.m_selectremotevolumeIdCommand = await connection.CreateCommandAsync(@"
                SELECT ""ID""
                FROM ""Remotevolume""
                WHERE ""Name"" = @Name
            ", token)
                .ConfigureAwait(false);

            dbnew.m_createremotevolumeCommand = await connection.CreateCommandAsync(@"
                INSERT INTO ""Remotevolume"" (
                    ""OperationID"",
                    ""Name"",
                    ""Type"",
                    ""State"",
                    ""Size"",
                    ""VerificationCount"",
                    ""DeleteGraceTime"",
                    ""ArchiveTime""
                )
                VALUES (
                    @OperationID,
                    @Name,
                    @Type,
                    @State,
                    @Size,
                    @VerificationCount,
                    @DeleteGraceTime,
                    @ArchiveTime
                );
                SELECT last_insert_rowid();
            ", token)
                .ConfigureAwait(false);

            dbnew.m_insertIndexBlockLink = await connection.CreateCommandAsync(@"
                INSERT INTO ""IndexBlockLink"" (
                    ""IndexVolumeID"",
                    ""BlockVolumeID""
                )
                VALUES (
                    @IndexVolumeId,
                    @BlockVolumeId
                )
            ", token)
                .ConfigureAwait(false);

            dbnew.m_findpathprefixCommand = await connection.CreateCommandAsync(@"
                SELECT ""ID""
                FROM ""PathPrefix""
                WHERE ""Prefix"" = @Prefix
            ", token)
                .ConfigureAwait(false);

            dbnew.m_insertpathprefixCommand = await connection.CreateCommandAsync(@"
                INSERT INTO ""PathPrefix"" (""Prefix"")
                VALUES (@Prefix);
                SELECT last_insert_rowid();
            ", token)
                .ConfigureAwait(false);

            return dbnew;
        }

        /// <summary>
        /// Creates a DateTime instance by adding the specified number of seconds to the EPOCH value.
        /// </summary>
        /// <param name="seconds">The number of seconds since the EPOCH (January 1, 1970).</param>
        /// <returns>A DateTime instance representing the specified number of seconds since the EPOCH.</returns>
        public static DateTime ParseFromEpochSeconds(long seconds)
        {
            return Library.Utility.Utility.EPOCH.AddSeconds(seconds);
        }

        /// <summary>
        /// Returns remote volumes referenced by the provided filesets.
        /// This is a shared helper used by delete/lock operations to avoid duplicating the CTEs.
        /// </summary>
        /// <param name="filesetIds">Fileset IDs to resolve.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An async sequence of remote volume name, type, and lock expiration time (UTC) if present.</returns>
        public async IAsyncEnumerable<(string Name, DateTime? LockExpirationTime)> GetRemoteVolumesDependingOnFilesets(
            IEnumerable<long> filesetIds,
            [EnumeratorCancellation] CancellationToken token)
        {
            var ids = filesetIds?.ToArray();
            if (ids == null || ids.Length == 0)
                yield break;

            await using var cmd = m_connection.CreateCommand();
            cmd.SetTransaction(m_rtr);

            cmd.SetCommandAndParameters($@"
                WITH
                    fileset_blocks AS (
                        SELECT DISTINCT ""VolumeID"" AS ""VolumeID""
                        FROM ""Block""
                        WHERE ""ID"" IN (
                            SELECT ""BlockID""
                            FROM ""BlocksetEntry""
                            WHERE ""BlocksetID"" IN (
                                SELECT ""BlocksetID""
                                FROM ""FileLookup""
                                WHERE ""ID"" IN (
                                    SELECT ""FileID""
                                    FROM ""FilesetEntry""
                                    WHERE ""FilesetID"" IN (@FilesetIds)
                                )
                            )
                        )
                    ),
                    index_volumes AS (
                        SELECT DISTINCT ""IndexVolumeID"" AS ""VolumeID""
                        FROM ""IndexBlockLink""
                        WHERE ""BlockVolumeID"" IN (SELECT ""VolumeID"" FROM fileset_blocks)
                    )
                    ,
                    fileset_volumes AS (
                        SELECT DISTINCT ""VolumeID"" AS ""VolumeID""
                        FROM ""Fileset""
                        WHERE ""ID"" IN (@FilesetIds)
                    )
                    ,combined AS (
                        SELECT ""VolumeID"" FROM fileset_blocks
                        UNION
                        SELECT ""VolumeID"" FROM index_volumes
                        UNION
                        SELECT ""VolumeID"" FROM fileset_volumes
                    )
                SELECT DISTINCT ""Name"", ""LockExpirationTime""
                FROM ""RemoteVolume""
                WHERE ""ID"" IN (SELECT * FROM combined)
            ");

            cmd.ExpandInClauseParameterMssqlite("@FilesetIds", ids);

            await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await rd.ReadAsync(token).ConfigureAwait(false))
            {
                var name = rd.ConvertValueToString(0) ?? string.Empty;
                var lockUntil = rd.IsDBNull(1)
                    ? (DateTime?)null
                    : ParseFromEpochSeconds(rd.ConvertValueToInt64(2));

                yield return (name, lockUntil);
            }
        }

        /// <summary>
        /// Updates the state of a remote volume in the database.
        /// </summary>
        /// <param name="name">The name of the remote volume to update.</param>
        /// <param name="state">The new state of the remote volume.</param>
        /// <param name="size">The size of the remote volume in bytes.</param>
        /// <param name="hash">The hash of the remote volume, or null if not applicable.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the remote volume has been updated.</returns>
        public async Task UpdateRemoteVolume(string name, RemoteVolumeState state, long size, string? hash, CancellationToken token)
        {
            await UpdateRemoteVolume(name, state, size, hash, false, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Updates the state of a remote volume in the database.
        /// </summary>
        /// <param name="name">The name of the remote volume to update.</param>
        /// <param name="state">The new state of the remote volume.</param>
        /// <param name="size">The size of the remote volume in bytes.</param>
        /// <param name="hash">The hash of the remote volume, or null if not applicable.</param>
        /// <param name="suppressCleanup">If true, suppresses cleanup of the remote volume after updating.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the remote volume has been updated.</returns>
        public async Task UpdateRemoteVolume(string name, RemoteVolumeState state, long size, string? hash, bool suppressCleanup, CancellationToken token)
        {
            await UpdateRemoteVolume(name, state, size, hash, suppressCleanup, new TimeSpan(0), null, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Updates the state of a remote volume in the database.
        /// </summary>
        /// <param name="name">The name of the remote volume to update.</param>
        /// <param name="state">The new state of the remote volume.</param>
        /// <param name="size">The size of the remote volume in bytes.</param>
        /// <param name="hash">The hash of the remote volume, or null if not applicable.</param>
        /// <param name="suppressCleanup">If true, suppresses cleanup of the remote volume after updating.</param>
        /// <param name="deleteGraceTime">The time after which the remote volume can be deleted.</param>
        /// <param name="setArchived">If true, sets the remote volume as archived.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the remote volume has been updated.</returns>
        public async Task UpdateRemoteVolume(string name, RemoteVolumeState state, long size, string? hash, bool suppressCleanup, TimeSpan deleteGraceTime, bool? setArchived, CancellationToken token)
        {
            var c = await m_updateremotevolumeCommand.SetTransaction(m_rtr)
                .SetParameterValue("@OperationID", m_operationid)
                .SetParameterValue("@State", state.ToString())
                .SetParameterValue("@Hash", hash)
                .SetParameterValue("@Size", size)
                .SetParameterValue("@Name", name)
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);

            if (c != 1)
            {
                throw new Exception($"Unexpected number of remote volumes detected: {c}!");
            }

            if (deleteGraceTime.Ticks > 0)
            {
                await using var cmd = await m_connection.CreateCommandAsync(@"
                    UPDATE ""RemoteVolume""
                    SET ""DeleteGraceTime"" = @DeleteGraceTime
                    WHERE ""Name"" = @Name
                ", token)
                    .ConfigureAwait(false);

                c = await cmd.SetTransaction(m_rtr)
                    .SetParameterValue("@DeleteGraceTime", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow + deleteGraceTime))
                    .SetParameterValue("@Name", name)
                    .ExecuteNonQueryAsync(token)
                    .ConfigureAwait(false);

                if (c != 1)
                    throw new Exception($"Unexpected number of updates when recording remote volume updates: {c}!");
            }

            if (setArchived.HasValue)
            {
                await using var cmd = await m_connection.CreateCommandAsync(@"
                    UPDATE ""RemoteVolume""
                    SET ""ArchiveTime"" = @ArchiveTime
                    WHERE ""Name"" = @Name
                ", token)
                    .ConfigureAwait(false);

                c = await cmd.SetTransaction(m_rtr)
                    .SetParameterValue("@ArchiveTime", setArchived.Value ? Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow) : 0)
                    .SetParameterValue("@Name", name)
                    .ExecuteNonQueryAsync(token)
                    .ConfigureAwait(false);

                if (c != 1)
                    throw new Exception($"Unexpected number of updates when recording remote volume archive-time updates: {c}!");
            }

            if (!suppressCleanup && state == RemoteVolumeState.Deleted)
            {
                await RemoveRemoteVolume(name, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Gets the ID and timestamp of all filesets in the database, ordered by timestamp in descending order.
        /// </summary>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of key-value pairs, where each pair contains the fileset ID and its timestamp.</returns>
        public async IAsyncEnumerable<KeyValuePair<long, DateTime>> FilesetTimes([EnumeratorCancellation] CancellationToken token)
        {
            await using var cmd = await m_connection.CreateCommandAsync(@"
                SELECT
                    ""ID"",
                    ""Timestamp""
                FROM ""Fileset""
                ORDER BY ""Timestamp"" DESC
            ", token)
                .ConfigureAwait(false);

            await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await rd.ReadAsync(token).ConfigureAwait(false))
                yield return new KeyValuePair<long, DateTime>(
                    rd.ConvertValueToInt64(0),
                    ParseFromEpochSeconds(rd.ConvertValueToInt64(1)).ToLocalTime()
                );
        }

        /// <summary>
        /// Generates a SQL WHERE clause for filtering file lists based on a specified time and versions.
        /// </summary>
        /// <param name="time">The time to filter files by. If Ticks is 0, it will not be used in the query.</param>
        /// <param name="versions">An array of versions to filter files by. If null or empty, it will not be used in the query.</param>
        /// <param name="filesetslist">An optional list of filesets to use for filtering. If null, it will fetch all filesets from the database.</param>
        /// <param name="singleTimeMatch">If true, matches files with the exact timestamp; otherwise, matches files with timestamps less than or equal to the specified time.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns a tuple containing the SQL WHERE clause and a dictionary of parameter values.</returns>
        /// <exception cref="Exception">Thrown if the provided time is unspecified.</exception>
        public async Task<(string Query, Dictionary<string, object?> Values)> GetFilelistWhereClause(DateTime time, long[]? versions, IEnumerable<KeyValuePair<long, DateTime>>? filesetslist, bool singleTimeMatch, CancellationToken token)
        {
            KeyValuePair<long, DateTime>[] filesets;
            if (filesetslist != null)
                filesets = [.. filesetslist];
            else
                filesets = await FilesetTimes(token)
                    .ToArrayAsync(cancellationToken: token)
                    .ConfigureAwait(false);

            var query = new StringBuilder();
            var args = new Dictionary<string, object?>();
            if (time.Ticks > 0 || (versions != null && versions.Length > 0))
            {
                var hasTime = false;
                if (time.Ticks > 0)
                {
                    if (time.Kind == DateTimeKind.Unspecified)
                        throw new Exception("Invalid DateTime given, must be either local or UTC");

                    query.Append(singleTimeMatch ? @" ""Timestamp"" = @Timestamp" : @" ""Timestamp"" <= @Timestamp");
                    // Make sure the resolution is the same (i.e. no milliseconds)
                    args.Add("@Timestamp", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(time));
                    hasTime = true;
                }

                if (versions != null && versions.Length > 0)
                {
                    var qs = new StringBuilder();
                    foreach (var v in versions)
                    {
                        if (v >= 0 && v < filesets.Length)
                        {
                            var argName = $"@Fileset{Library.Utility.Utility.FormatInvariantValue(v)}";
                            args.Add(argName, filesets[v].Key);
                            qs.Append(argName);
                            qs.Append(',');
                        }
                        else
                            Logging.Log.WriteWarningMessage(LOGTAG, "SkipInvalidVersion", null, "Skipping invalid version: {0}", v);
                    }

                    if (qs.Length > 0)
                    {
                        if (hasTime)
                            query.Append(" OR ");

                        query.Append(@" ""ID"" IN (" + qs.ToString(0, qs.Length - 1) + ")");
                    }
                }

                if (query.Length > 0)
                {
                    query.Insert(0, " WHERE ");
                }
            }

            return (query.ToString(), args);
        }

        /// <summary>
        /// Gets the ID of a remote volume by its name.
        /// </summary>
        /// <param name="file">The name of the remote volume.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns the ID of the remote volume. If the volume does not exist, it returns -1.</returns>
        public async Task<long> GetRemoteVolumeID(string file, CancellationToken token)
        {
            return await m_selectremotevolumeIdCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@Name", file)
                .ExecuteScalarInt64Async(-1, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the IDs of remote volumes for a list of files.
        /// </summary>
        /// <param name="files">An enumerable collection of file names.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of key-value pairs, where each pair contains the file name and its corresponding remote volume ID.</returns>
        public async IAsyncEnumerable<KeyValuePair<string, long>> GetRemoteVolumeIDs(IEnumerable<string> files, [EnumeratorCancellation] CancellationToken token)
        {
            await using var cmd = await m_connection.CreateCommandAsync(@"
                SELECT
                    ""Name"",
                    ""ID""
                FROM ""RemoteVolume""
                WHERE ""Name"" IN (@Name)
            ", token)
                .ConfigureAwait(false);

            cmd.SetTransaction(m_rtr);
            await using var tmptable = await TemporaryDbValueList.CreateAsync(this, files, token)
                .ConfigureAwait(false);
            await cmd.ExpandInClauseParameterMssqliteAsync("@Name", tmptable, token)
                .ConfigureAwait(false);

            await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await rd.ReadAsync(token).ConfigureAwait(false))
                yield return new KeyValuePair<string, long>(rd.ConvertValueToString(0) ?? "", rd.ConvertValueToInt64(1));
        }

        /// <summary>
        /// Gets a remote volume entry by its file name.
        /// </summary>
        /// <param name="file">The name of the remote volume file.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns a <see cref="RemoteVolumeEntry"/> representing the remote volume. If the volume does not exist, it returns an empty entry.</returns>
        public async Task<RemoteVolumeEntry> GetRemoteVolume(string file, CancellationToken token)
        {
            m_selectremotevolumeCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@Name", file);

            await using (var rd = await m_selectremotevolumeCommand.ExecuteReaderAsync(token).ConfigureAwait(false))
                if (await rd.ReadAsync(token).ConfigureAwait(false))
                    return new RemoteVolumeEntry(
                        rd.ConvertValueToInt64(0),
                        rd.ConvertValueToString(1),
                        rd.ConvertValueToString(4),
                        rd.ConvertValueToInt64(3, -1),
                        (RemoteVolumeType)Enum.Parse(typeof(RemoteVolumeType), rd.ConvertValueToString(2) ?? ""),
                        (RemoteVolumeState)Enum.Parse(typeof(RemoteVolumeState), rd.ConvertValueToString(5) ?? ""),
                        ParseFromEpochSeconds(rd.ConvertValueToInt64(6, 0)),
                        ParseFromEpochSeconds(rd.ConvertValueToInt64(7, 0)),
                        ParseFromEpochSeconds(rd.ConvertValueToInt64(8, 0))
                    );

            return RemoteVolumeEntry.Empty;
        }

        /// <summary>
        /// Gets remote volumes that are duplicates, meaning they have the same name but different states.
        /// </summary>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of key-value pairs, where each pair contains the name of the remote volume and its state.</returns>
        public async IAsyncEnumerable<KeyValuePair<string, RemoteVolumeState>> DuplicateRemoteVolumes([EnumeratorCancellation] CancellationToken token)
        {
            m_selectduplicateRemoteVolumesCommand.SetTransaction(m_rtr);

            await foreach (var rd in m_selectduplicateRemoteVolumesCommand.ExecuteReaderEnumerableAsync(token).ConfigureAwait(false))
            {
                yield return new KeyValuePair<string, RemoteVolumeState>(
                    rd.ConvertValueToString(0) ?? throw new Exception("Name was null"),
                    (RemoteVolumeState)Enum.Parse(
                        typeof(RemoteVolumeState), rd.ConvertValueToString(1) ?? ""
                    )
                );
            }
        }

        /// <summary>
        /// Gets all remote volumes from the database.
        /// </summary>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of <see cref="RemoteVolumeEntry"/> representing all remote volumes.</returns>
        public async IAsyncEnumerable<RemoteVolumeEntry> GetRemoteVolumes([EnumeratorCancellation] CancellationToken token)
        {
            m_selectremotevolumesCommand.SetTransaction(m_rtr);
            await using var rd = await m_selectremotevolumesCommand
                .ExecuteReaderAsync(token)
                .ConfigureAwait(false);

            while (await rd.ReadAsync(token).ConfigureAwait(false))
            {
                yield return new RemoteVolumeEntry(
                    rd.ConvertValueToInt64(0),
                    rd.ConvertValueToString(1),
                    rd.ConvertValueToString(4),
                    rd.ConvertValueToInt64(3, -1),
                    (RemoteVolumeType)Enum.Parse(typeof(RemoteVolumeType), rd.ConvertValueToString(2) ?? ""),
                    (RemoteVolumeState)Enum.Parse(typeof(RemoteVolumeState), rd.ConvertValueToString(5) ?? ""),
                    ParseFromEpochSeconds(rd.ConvertValueToInt64(6, 0)),
                    ParseFromEpochSeconds(rd.ConvertValueToInt64(7, 0)),
                    ParseFromEpochSeconds(rd.ConvertValueToInt64(8, 0))
                );
            }
        }

        /// <summary>
        /// Log an operation performed on the remote backend.
        /// </summary>
        /// <param name="operation">The operation performed.</param>
        /// <param name="path">The path involved.</param>
        /// <param name="data">Any data relating to the operation.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the operation log has been recorded.</returns>
        public async Task LogRemoteOperation(string operation, string path, string? data, CancellationToken token)
        {
            await m_insertremotelogCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@OperationID", m_operationid)
                .SetParameterValue("@Timestamp", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow))
                .SetParameterValue("@Operation", operation)
                .SetParameterValue("@Path", path)
                .SetParameterValue("@Data", data)
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Log a debug message.
        /// </summary>
        /// <param name="type">The message type.</param>
        /// <param name="message">The message.</param>
        /// <param name="exception">An optional exception.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the log message has been recorded.</returns>
        public async Task LogMessage(string type, string message, Exception? exception, CancellationToken token)
        {
            await m_insertlogCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@OperationID", m_operationid)
                .SetParameterValue("@Timestamp", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow))
                .SetParameterValue("@Type", type)
                .SetParameterValue("@Message", message)
                .SetParameterValue("@Exception", exception?.ToString())
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Unlinks a remote volume from the database.
        /// This operation removes the remote volume entry from the database without deleting the actual volume.
        /// </summary>
        /// <param name="name">The name of the remote volume to unlink.</param>
        /// <param name="state">The state of the remote volume to unlink.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the remote volume has been unlinked.</returns>
        /// <exception cref="Exception">Thrown if the number of unlinked remote volumes is not equal to 1.</exception>
        public async Task UnlinkRemoteVolume(string name, RemoteVolumeState state, CancellationToken token)
        {
            await using var cmd = await m_connection.CreateCommandAsync(@"
                DELETE FROM ""RemoteVolume""
                WHERE
                    ""Name"" = @Name
                    AND ""State"" = @State
            ", token)
                .ConfigureAwait(false);

            var c = await cmd.SetTransaction(m_rtr)
                .SetParameterValue("@Name", name)
                .SetParameterValue("@State", state.ToString())
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);

            if (c != 1)
                throw new Exception($"Unexpected number of remote volumes deleted: {c}, expected {1}");
        }

        /// <summary>
        /// Removes a remote volume from the database.
        /// </summary>
        /// <param name="name">The name of the remote volume to remove.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the remote volume has been removed.</returns>
        public async Task RemoveRemoteVolume(string name, CancellationToken token)
        {
            await RemoveRemoteVolumes([name], token).ConfigureAwait(false);
        }

        /// <summary>
        /// Removes multiple remote volumes from the database.
        /// </summary>
        /// <param name="names">An enumerable collection of names of remote volumes to remove.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the remote volumes have been removed.</returns>
        public async Task RemoveRemoteVolumes(IEnumerable<string> names, CancellationToken token)
        {
            if (names == null || !names.Any()) return;

            await using var deletecmd = m_connection.CreateCommand(m_rtr);

            var nonAttachedFilesPre = await deletecmd.ExecuteScalarInt64Async(@"
                SELECT COUNT(*)
                FROM ""FilesetEntry""
                WHERE ""FileID"" NOT IN (
                    SELECT ""ID""
                    FROM ""FileLookup""
                )
            ", token)
                .ConfigureAwait(false);

            if (nonAttachedFilesPre > 0)
                throw new ConstraintException($"Refusing to remove remote volumes, detected {nonAttachedFilesPre} file(s) in FilesetEntry without corresponding FileLookup entry");


            string temptransguid = Library.Utility.Utility.GetHexGuid();
            var volidstable = $"DelVolSetIds-{temptransguid}";
            var blocksetidstable = $"DelBlockSetIds-{temptransguid}";
            var filesetidstable = $"DelFilesetIds-{temptransguid}";

            // Create and fill a temp table with the volids to delete. We avoid using too many parameters that way.
            await deletecmd.ExecuteNonQueryAsync($@"
                CREATE TEMP TABLE ""{volidstable}"" (
                    ""ID"" INTEGER PRIMARY KEY
                )
            ", token)
                .ConfigureAwait(false);

            await deletecmd.SetCommandAndParameters($@"
                INSERT OR IGNORE INTO ""{volidstable}""
                SELECT ""ID""
                FROM ""RemoteVolume""
                WHERE ""Name"" IN (@VolumeNames)
            ")
                .ExpandInClauseParameterMssqlite("@VolumeNames", [.. names])
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);

            var volIdsSubQuery = $@"
                SELECT ""ID""
                FROM ""{volidstable}""
            ";

            var bsIdsSubQuery = @$"
                SELECT DISTINCT ""BlocksetEntry"".""BlocksetID""
                FROM
                    ""BlocksetEntry"",
                    ""Block""
                WHERE
                    ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                    AND ""Block"".""VolumeID"" IN ({volIdsSubQuery})
                UNION ALL
                SELECT DISTINCT ""BlocksetID""
                FROM ""BlocklistHash""
                WHERE ""Hash"" IN (
                    SELECT ""Hash""
                    FROM ""Block""
                    WHERE ""VolumeID"" IN ({volIdsSubQuery})
                )
            ";

            // Create a temporary table to cache subquery result, as it might take long (SQLite does not cache at all).
            await deletecmd.ExecuteNonQueryAsync($@"
                CREATE TEMP TABLE ""{blocksetidstable}"" (
                    ""ID"" INTEGER PRIMARY KEY
                )
            ", token)
                .ConfigureAwait(false);

            await deletecmd.ExecuteNonQueryAsync($@"
                INSERT OR IGNORE INTO ""{blocksetidstable}"" (""ID"")
                {bsIdsSubQuery}
            ", token)
                .ConfigureAwait(false);

            bsIdsSubQuery = $@"
                SELECT DISTINCT ""ID""
                FROM ""{blocksetidstable}""
            ";

            // Create a temp table to associate metadata that is being deleted to a fileset
            var metadataFilesetQuery = $@"
                SELECT
                    ""Metadataset"".""ID"",
                    ""FilesetEntry"".""FilesetID""
                FROM ""Metadataset""
                INNER JOIN ""FileLookup""
                    ON ""FileLookup"".""MetadataID"" = ""Metadataset"".""ID""
                INNER JOIN ""FilesetEntry""
                    ON ""FilesetEntry"".""FileID"" = ""FileLookup"".""ID""
                WHERE ""Metadataset"".""BlocksetID"" IN ({bsIdsSubQuery})
                OR ""Metadataset"".""ID"" IN (
                    SELECT ""MetadataID""
                    FROM ""FileLookup""
                    WHERE ""BlocksetID"" IN ({bsIdsSubQuery})
                )
            ";

            var metadataFilesetTable = $"DelMetadataFilesetIds-{temptransguid}";
            await deletecmd.ExecuteNonQueryAsync($@"
                CREATE TEMP TABLE ""{metadataFilesetTable}"" (
                    ""MetadataID"" INTEGER PRIMARY KEY,
                    ""FilesetID"" INTEGER
                )
            ", token)
                .ConfigureAwait(false);

            await deletecmd.ExecuteNonQueryAsync($@"
                INSERT OR IGNORE INTO ""{metadataFilesetTable}"" (
                    ""MetadataID"",
                    ""FilesetID""
                )
                {metadataFilesetQuery}
            ", token)
                .ConfigureAwait(false);

            // Delete FilesetEntry rows that had their metadata deleted
            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""FilesetEntry""
                WHERE
                    ""FilesetEntry"".""FilesetID"" IN (
                        SELECT DISTINCT ""FilesetID""
                        FROM ""{metadataFilesetTable}""
                    )
                    AND ""FilesetEntry"".""FileID"" IN (
                        SELECT ""FilesetEntry"".""FileID""
                        FROM ""FilesetEntry""
                        INNER JOIN ""FileLookup""
                            ON ""FileLookup"".""ID"" = ""FilesetEntry"".""FileID""
                        WHERE ""FileLookup"".""MetadataID"" IN (
                            SELECT ""MetadataID""
                            FROM ""{metadataFilesetTable}""
                        )
                    )
            ", token)
                .ConfigureAwait(false);

            // Delete FilesetEntry rows that had their blocks deleted
            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""FilesetEntry""
                WHERE ""FilesetEntry"".""FileID"" IN (
                    SELECT ""ID""
                    FROM ""FileLookup""
                    WHERE ""FileLookup"".""BlocksetID"" IN ({bsIdsSubQuery})
                )
            ", token)
                .ConfigureAwait(false);

            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""FileLookup""
                WHERE ""FileLookup"".""MetadataID"" IN (
                    SELECT ""MetadataID""
                    FROM ""{metadataFilesetTable}""
                )
            ", token)
                .ConfigureAwait(false);

            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""Metadataset""
                WHERE ""BlocksetID"" IN ({bsIdsSubQuery})
            ", token)
                .ConfigureAwait(false);

            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""FileLookup""
                WHERE ""BlocksetID"" IN ({bsIdsSubQuery})
            ", token)
                .ConfigureAwait(false);

            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""Blockset""
                WHERE ""ID"" IN ({bsIdsSubQuery})
            ", token)
                .ConfigureAwait(false);

            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""BlocksetEntry""
                WHERE ""BlocksetID"" IN ({bsIdsSubQuery})
            ", token)
                .ConfigureAwait(false);

            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""BlocklistHash""
                WHERE ""BlocklistHash"".""BlocksetID"" IN ({bsIdsSubQuery})
            ", token)
                .ConfigureAwait(false);

            // If the volume is a block or index volume, this will update the crosslink table, otherwise nothing will happen
            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""IndexBlockLink""
                WHERE ""BlockVolumeID"" IN ({volIdsSubQuery})
                OR ""IndexVolumeID"" IN ({volIdsSubQuery})
            ", token)
                .ConfigureAwait(false);

            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""Block""
                WHERE ""VolumeID"" IN ({volIdsSubQuery})
            ", token)
                .ConfigureAwait(false);

            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""DeletedBlock""
                WHERE ""VolumeID"" IN ({volIdsSubQuery})
            ", token)
                .ConfigureAwait(false);

            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""ChangeJournalData""
                WHERE ""FilesetID"" IN (
                    SELECT ""ID""
                    FROM ""Fileset""
                    WHERE ""VolumeID"" IN ({volIdsSubQuery})
                )
            ", token)
                .ConfigureAwait(false);

            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""FilesetEntry""
                WHERE ""FilesetID"" IN (
                    SELECT ""ID""
                    FROM ""Fileset""
                    WHERE ""VolumeID"" IN ({volIdsSubQuery})
                )
            ", token)
                .ConfigureAwait(false);

            await deletecmd.ExecuteNonQueryAsync($@"
                CREATE TABLE ""{filesetidstable}"" (
                    ""ID"" INTEGER PRIMARY KEY
                )
            ", token)
                .ConfigureAwait(false);

            await deletecmd.ExecuteNonQueryAsync($@"
                INSERT OR IGNORE INTO ""{filesetidstable}""
                SELECT ""ID""
                FROM ""Fileset""
                WHERE ""VolumeID"" IN ({volIdsSubQuery})
            ", token)
                .ConfigureAwait(false);

            // Delete from Fileset if FilesetEntry rows were deleted by related metadata and there are no references in FilesetEntry anymore
            await deletecmd.ExecuteNonQueryAsync($@"
                INSERT OR IGNORE INTO ""{filesetidstable}""
                SELECT ""ID""
                FROM ""Fileset""
                WHERE
                    ""Fileset"".""ID"" IN (
                        SELECT DISTINCT ""FilesetID""
                        FROM ""{metadataFilesetTable}""
                    )
                    AND ""Fileset"".""ID"" NOT IN (
                        SELECT DISTINCT ""FilesetID""
                        FROM ""FilesetEntry""
                    )
            ", token)
                .ConfigureAwait(false);

            // Since we are deleting the fileset, we also need to mark the remote volume as deleting so it will be cleaned up later
            await deletecmd.SetCommandAndParameters($@"
                UPDATE ""RemoteVolume""
                SET ""State"" = @NewState
                WHERE
                    ""ID"" IN (
                        SELECT DISTINCT ""VolumeID""
                        FROM ""Fileset""
                        WHERE ""Fileset"".""ID"" IN (
                            SELECT ""ID""
                            FROM ""{filesetidstable}""
                        )
                    )
                    AND ""State"" IN (@AllowedStates)
            ")
                .SetParameterValue("@NewState", RemoteVolumeState.Deleting.ToString())
                .ExpandInClauseParameterMssqlite("@AllowedStates", [
                        RemoteVolumeState.Uploading.ToString(),
                        RemoteVolumeState.Uploaded.ToString(),
                        RemoteVolumeState.Verified.ToString(),
                        RemoteVolumeState.Temporary.ToString()
                    ])
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);

            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""Fileset""
                WHERE ""ID"" IN (
                    SELECT ""ID""
                    FROM ""{filesetidstable}""
                )
            ", token)
                .ConfigureAwait(false);

            // Clean up temp tables for subqueries. We truncate content and then try to delete.
            // Drop in try-block, as it fails in nested transactions (SQLite problem)
            // SQLite.SQLiteException (0x80004005): database table is locked
            await deletecmd.ExecuteNonQueryAsync($@"DELETE FROM ""{blocksetidstable}"" ", token)
                .ConfigureAwait(false);
            await deletecmd.ExecuteNonQueryAsync($@"DELETE FROM ""{volidstable}"" ", token)
                .ConfigureAwait(false);
            await deletecmd.ExecuteNonQueryAsync($@"DELETE FROM ""{metadataFilesetTable}"" ", token)
                .ConfigureAwait(false);
            await deletecmd.ExecuteNonQueryAsync($@"DELETE FROM ""{filesetidstable}"" ", token)
                .ConfigureAwait(false);
            try
            {
                deletecmd.CommandTimeout = 2;
                await deletecmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{blocksetidstable}"" ", token)
                    .ConfigureAwait(false);
                await deletecmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{volidstable}"" ", token)
                    .ConfigureAwait(false);
                await deletecmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{metadataFilesetTable}"" ", token)
                    .ConfigureAwait(false);
                await deletecmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{filesetidstable}"" ", token)
                    .ConfigureAwait(false);
            }
            catch { /* Ignore, will be deleted on close anyway. */ }

            m_removeremotevolumeCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@Now", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow))
                .SetParameterValue("@State", RemoteVolumeState.Deleted.ToString());
            foreach (var name in names)
            {
                await m_removeremotevolumeCommand
                    .SetTransaction(m_rtr)
                    .SetParameterValue("@Name", name)
                    .ExecuteNonQueryAsync(token)
                    .ConfigureAwait(false);
            }

            // Validate before commiting changes
            var nonAttachedFiles = await deletecmd.ExecuteScalarInt64Async(@"
                SELECT COUNT(*)
                FROM ""FilesetEntry""
                WHERE ""FileID"" NOT IN (
                    SELECT ""ID""
                    FROM ""FileLookup""
                )
            ", token)
                .ConfigureAwait(false);

            if (nonAttachedFiles > 0)
                throw new ConstraintException($"Detected {nonAttachedFiles} file(s) in FilesetEntry without corresponding FileLookup entry after removing remote volumes, rolling back changes");
        }

        /// <summary>
        /// Performs a VACUUM operation on the database to reclaim unused space.
        /// This operation can help optimize the database performance by defragmenting it.
        /// Note: This operation can take a significant amount of time depending on the size of the database.
        /// </summary>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the VACUUM operation has finished.</returns>
        public async Task Vacuum(CancellationToken token)
        {
            m_hasExecutedVacuum = true;
            await using var cmd = m_connection.CreateCommand();

            await cmd.ExecuteNonQueryAsync("VACUUM", token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Registers a new remote volume in the database.
        /// </summary>
        /// <param name="name">The name of the remote volume.</param>
        /// <param name="type">The type of the remote volume.</param>
        /// <param name="size">The size of the remote volume in bytes. Use -1 for unknown size.</param>
        /// <param name="state">The state of the remote volume.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns the ID of the newly registered remote volume.</returns>
        public async Task<long> RegisterRemoteVolume(string name, RemoteVolumeType type, long size, RemoteVolumeState state, CancellationToken token)
        {
            return await RegisterRemoteVolume(name, type, state, size, new TimeSpan(0), token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Registers a new remote volume in the database.
        /// </summary>
        /// <param name="name">The name of the remote volume.</param>
        /// <param name="type">The type of the remote volume.</param>
        /// <param name="state">The state of the remote volume.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns the ID of the newly registered remote volume.</returns>
        public async Task<long> RegisterRemoteVolume(string name, RemoteVolumeType type, RemoteVolumeState state, CancellationToken token)
        {
            return await RegisterRemoteVolume(name, type, state, new TimeSpan(0), token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Registers a new remote volume in the database.
        /// </summary>
        /// <param name="name">The name of the remote volume.</param>
        /// <param name="type">The type of the remote volume.</param>
        /// <param name="state">The state of the remote volume.</param>
        /// <param name="deleteGraceTime">The time after which the remote volume can be deleted.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns the ID of the newly registered remote volume.</returns>
        public async Task<long> RegisterRemoteVolume(string name, RemoteVolumeType type, RemoteVolumeState state, TimeSpan deleteGraceTime, CancellationToken token)
        {
            return await RegisterRemoteVolume(name, type, state, -1, deleteGraceTime, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Registers a new remote volume in the database.
        /// </summary>
        /// <param name="name">The name of the remote volume.</param>
        /// <param name="type">The type of the remote volume.</param>
        /// <param name="state">The state of the remote volume.</param>
        /// <param name="size">The size of the remote volume in bytes. Use -1 for unknown size.</param>
        /// <param name="deleteGraceTime">The time after which the remote volume can be deleted.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns the ID of the newly registered remote volume.</returns>
        public async Task<long> RegisterRemoteVolume(string name, RemoteVolumeType type, RemoteVolumeState state, long size, TimeSpan deleteGraceTime, CancellationToken token)
        {
            var r = await m_createremotevolumeCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@OperationID", m_operationid)
                .SetParameterValue("@Name", name)
                .SetParameterValue("@Type", type.ToString())
                .SetParameterValue("@State", state.ToString())
                .SetParameterValue("@Size", size)
                .SetParameterValue("@VerificationCount", 0)
                .SetParameterValue("@DeleteGraceTime", deleteGraceTime.Ticks <= 0 ? 0 : (DateTime.UtcNow + deleteGraceTime).Ticks)
                .SetParameterValue("@ArchiveTime", 0)
                .ExecuteScalarInt64Async(token)
                .ConfigureAwait(false);

            return r;
        }

        /// <summary>
        /// Retrieves the IDs of filesets that match a specific restore time and optional versions.
        /// If no filesets match the criteria, it returns the newest fileset ID.
        /// </summary>
        /// <param name="restoretime">The time to restore from.</param>
        /// <param name="versions">Optional array of versions to match against the filesets.</param>
        /// <param name="singleTimeMatch">If true, only match filesets that exactly match the restore time.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of fileset IDs that match the criteria.</returns>
        /// <exception cref="Exception">Thrown if the provided DateTime is unspecified.</exception>
        /// <exception cref="UserInformationException">Thrown if no backups are found at the specified date.</exception>
        public async IAsyncEnumerable<long> GetFilesetIDs(DateTime restoretime, long[]? versions, bool singleTimeMatch, [EnumeratorCancellation] CancellationToken token)
        {
            if (restoretime.Kind == DateTimeKind.Unspecified)
                throw new Exception("Invalid DateTime given, must be either local or UTC");

            (var wherequery, var values) =
                await GetFilelistWhereClause(restoretime, versions, null, singleTimeMatch, token)
                    .ConfigureAwait(false);
            var res = new List<long>();
            await using var cmd = m_connection.CreateCommand();
            cmd.SetCommandAndParameters($@"
                SELECT ""ID""
                FROM ""Fileset""
                {wherequery}
                ORDER BY ""Timestamp"" DESC
            ")
                .SetParameterValues(values);

            await using (var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                while (await rd.ReadAsync(token).ConfigureAwait(false))
                    res.Add(rd.ConvertValueToInt64(0));

            if (res.Count == 0)
            {
                cmd.SetCommandAndParameters(@"
                    SELECT ""ID""
                    FROM ""Fileset""
                    ORDER BY ""Timestamp"" DESC
                ");
                await using (var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    while (await rd.ReadAsync(token).ConfigureAwait(false))
                        res.Add(rd.ConvertValueToInt64(0));

                if (res.Count == 0)
                    throw new Duplicati.Library.Interface.UserInformationException("No backup at the specified date", "NoBackupAtDate");
                else
                    Logging.Log.WriteWarningMessage(LOGTAG, "RestoreTimeNoMatch", null, "Restore time or version did not match any existing backups, selecting newest backup");
            }

            foreach (var el in res)
                yield return el;
        }

        /// <summary>
        /// Finds filesets that match a specific restore time and optional versions.
        /// </summary>
        /// <param name="restoretime">The time to restore from.</param>
        /// <param name="versions">Optional array of versions to match against the filesets.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous task that returns a collection of fileset IDs that match the criteria.</returns>
        public async Task<IEnumerable<long>> FindMatchingFilesets(DateTime restoretime, long[]? versions, CancellationToken token)
        {
            if (restoretime.Kind == DateTimeKind.Unspecified)
                throw new Exception("Invalid DateTime given, must be either local or UTC");

            var (wherequery, args) =
                await GetFilelistWhereClause(restoretime, versions, null, true, token)
                    .ConfigureAwait(false);

            var res = new List<long>();
            await using var cmd = m_connection.CreateCommand();
            cmd.SetCommandAndParameters(@$"
                SELECT ""ID""
                FROM ""Fileset""
                {wherequery}
                ORDER BY ""Timestamp"" DESC
            ")
                .SetParameterValues(args);
            await using (var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                while (await rd.ReadAsync(token).ConfigureAwait(false))
                    res.Add(rd.ConvertValueToInt64(0));

            return res;
        }

        /// <summary>
        /// Checks if a fileset is a full backup.
        /// A fileset is considered a full backup if its "IsFullBackup" field is set to FULL_BACKUP.
        /// </summary>
        /// <param name="filesetTime">The timestamp of the fileset to check.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns true if the fileset is a full backup, otherwise false.</returns>
        public async Task<bool> IsFilesetFullBackup(DateTime filesetTime, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand();
            cmd.SetCommandAndParameters($@"
                SELECT ""IsFullBackup""
                FROM ""Fileset""
                WHERE ""Timestamp"" = @Timestamp
            ")
                .SetTransaction(m_rtr)
                .SetParameterValue("@Timestamp", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(filesetTime));

            await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await rd.ReadAsync(token).ConfigureAwait(false))
                return false;
            var isFullBackup = rd.GetInt32(0);
            return isFullBackup == BackupType.FULL_BACKUP;
        }

        /// <summary>
        /// Retrieves a list of database options from the "Configuration" table.
        /// </summary>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of key-value pairs representing the database options.</returns>
        private async IAsyncEnumerable<KeyValuePair<string, string>> GetDbOptionList([EnumeratorCancellation] CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(@"
                SELECT
                    ""Key"",
                    ""Value""
                FROM ""Configuration""
            ")
                .SetTransaction(m_rtr);

            await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await rd.ReadAsync(token).ConfigureAwait(false))
                yield return new KeyValuePair<string, string>(
                    rd.ConvertValueToString(0) ?? "",
                    rd.ConvertValueToString(1) ?? ""
                );
        }

        /// <summary>
        /// Retrieves all database options as a dictionary.
        /// </summary>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns a dictionary containing all database options.</returns>
        public async Task<IDictionary<string, string>> GetDbOptions(CancellationToken token)
        {
            var res = await GetDbOptionList(token)
                .ToDictionaryAsync(x => x.Key, x => x.Value, cancellationToken: token)
                .ConfigureAwait(false);

            return res;
        }

        /// <summary>
        /// Updates a database option.
        /// </summary>
        /// <param name="key">The key to update.</param>
        /// <param name="value">The value to set.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the option has been updated.</returns>
        private async Task UpdateDbOption(string key, bool value, CancellationToken token)
        {
            var opts = await GetDbOptions(token).ConfigureAwait(false);

            if (value)
                opts[key] = "true";
            else
                opts.Remove(key);

            await SetDbOptions(opts, token).ConfigureAwait(false);
            await m_rtr.CommitAsync(token: token).ConfigureAwait(false);
        }

        /// <summary>
        /// Flag indicating if a repair is in progress.
        /// </summary>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <param name="value">Optional value to set the flag to. If null, the current value in the database is returned.</param>
        /// <returns>A task that, when awaited, returns true if a repair is in progress, otherwise false.</returns>
        public async Task<bool> RepairInProgress(CancellationToken token, bool? value = null)
        {
            if (value is bool v)
            {
                await UpdateDbOption("repair-in-progress", v, token)
                    .ConfigureAwait(false);

                return v;
            }

            var opts = await GetDbOptions(token).ConfigureAwait(false);
            return opts.ContainsKey("repair-in-progress");
        }

        /// <summary>
        /// Flag indicating if the database has been partially recreated.
        /// </summary>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <param name="value">Optional value to set the flag to. If null, the current value in the database is returned.</param>
        /// <returns>A task that, when awaited, returns true if the database has been partially recreated, otherwise false.</returns>
        public async Task<bool> PartiallyRecreated(CancellationToken token, bool? value = null)
        {
            if (value is bool v)
            {
                await UpdateDbOption("partially-recreated", v, token)
                    .ConfigureAwait(false);

                return v;
            }

            var opts = await GetDbOptions(token).ConfigureAwait(false);
            return opts.ContainsKey("partially-recreated");
        }

        /// <summary>
        /// Flag indicating if the database has been terminated with active uploads.
        /// </summary>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <param name="value">Optional value to set the flag to. If null, the current value in the database is returned.</param>
        /// <returns>A task that, when awaited, returns true if the database has been terminated with active uploads, otherwise false.</returns>
        public async Task<bool> TerminatedWithActiveUploads(CancellationToken token, bool? value = null)
        {
            if (value is bool v)
            {
                await UpdateDbOption("terminated-with-active-uploads", v, token)
                    .ConfigureAwait(false);

                return v;
            }

            var opts = await GetDbOptions(token).ConfigureAwait(false);
            return opts.ContainsKey("terminated-with-active-uploads");
        }

        /// <summary>
        /// Sets the database options.
        /// </summary>
        /// <param name="options">The options to set.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the options have been set.</returns>
        public async Task SetDbOptions(IDictionary<string, string> options, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand();
            await cmd.ExecuteNonQueryAsync(@"
                DELETE FROM ""Configuration""
            ", token)
                .ConfigureAwait(false);

            foreach (var kp in options)
            {
                await cmd.SetCommandAndParameters(@"
                    INSERT INTO ""Configuration"" (
                        ""Key"",
                        ""Value""
                    )
                    VALUES (
                        @Key,
                        @Value
                    )
                ")
                    .SetParameterValue("@Key", kp.Key)
                    .SetParameterValue("@Value", kp.Value)
                    .ExecuteNonQueryAsync(token)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Counts the number of blocks larger than a specified size.
        /// </summary>
        /// <param name="fhblocksize">The size in bytes to compare against.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that, when awaited, returns the count of blocks larger than the specified size.</returns>
        public async Task<long> GetBlocksLargerThan(long fhblocksize, CancellationToken token)
        {
            await using var cmd = await m_connection.CreateCommandAsync(@"
                SELECT COUNT(*)
                FROM ""Block""
                WHERE ""Size"" > @Size
            ", token)
                .ConfigureAwait(false);

            return await cmd
                .SetParameterValue("@Size", fhblocksize)
                .ExecuteScalarInt64Async(-1, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies the consistency of the database.
        /// </summary>
        /// <param name="blocksize">The block size in bytes.</param>
        /// <param name="hashsize">The hash size in bytes.</param>
        /// <param name="verifyfilelists">Also verify filelists (can be slow).</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the consistency check is finished.</returns>
        public async Task VerifyConsistency(long blocksize, long hashsize, bool verifyfilelists, CancellationToken token)
            => await VerifyConsistencyInner(blocksize, hashsize, verifyfilelists, false, token)
                    .ConfigureAwait(false);

        /// <summary>
        /// Verifies the consistency of the database prior to repair.
        /// </summary>
        /// <param name="blocksize">The block size in bytes.</param>
        /// <param name="hashsize">The hash size in bytes.</param>
        /// <param name="verifyfilelists">Also verify filelists (can be slow).</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the consistency check is finished.</returns>
        public async Task VerifyConsistencyForRepair(long blocksize, long hashsize, bool verifyfilelists, CancellationToken token)
            => await VerifyConsistencyInner(blocksize, hashsize, verifyfilelists, true, token)
                    .ConfigureAwait(false);

        /// <summary>
        /// Verifies the consistency of the database.
        /// </summary>
        /// <param name="blocksize">The block size in bytes.</param>
        /// <param name="hashsize">The hash size in bytes.</param>
        /// <param name="verifyfilelists">Also verify filelists (can be slow).</param>
        /// <param name="laxVerifyForRepair">Disable verify for errors that will be fixed by repair.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the consistency check is finished.</returns>
        private async Task VerifyConsistencyInner(long blocksize, long hashsize, bool verifyfilelists, bool laxVerifyForRepair, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand()
                .SetTransaction(m_rtr);
            // Calculate the lengths for each blockset
            var combinedLengths = @"
                SELECT
                    ""A"".""ID"" AS ""BlocksetID"",
                    IFNULL(""B"".""CalcLen"", 0) AS ""CalcLen"",
                    ""A"".""Length""
                FROM ""Blockset"" ""A""
                LEFT OUTER JOIN (
                    SELECT
                        ""BlocksetEntry"".""BlocksetID"",
                        SUM(""Block"".""Size"") AS ""CalcLen""
                    FROM ""BlocksetEntry""
                    LEFT OUTER JOIN ""Block""
                    ON ""Block"".""ID"" = ""BlocksetEntry"".""BlockID""
                    GROUP BY ""BlocksetEntry"".""BlocksetID""
                ) ""B""
                    ON ""A"".""ID"" = ""B"".""BlocksetID""
            ";

            // For each blockset with wrong lengths, fetch the file path
            var reportDetails = @$"
                SELECT
                    ""CalcLen"",
                    ""Length"", ""A"".""BlocksetID"",
                    ""File"".""Path""
                FROM
                    ({combinedLengths}) ""A"",
                    ""File""
                WHERE
                    ""A"".""BlocksetID"" = ""File"".""BlocksetID""
                    AND ""A"".""CalcLen"" != ""A"".""Length""
            ";

            await using (var rd = await cmd.ExecuteReaderAsync(reportDetails, token).ConfigureAwait(false))
                if (await rd.ReadAsync(token).ConfigureAwait(false))
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Found inconsistency in the following files while validating database: ");
                    var c = 0;
                    do
                    {
                        if (c < 5)
                            sb.AppendFormat("{0}, actual size {1}, dbsize {2}, blocksetid: {3}{4}", rd.GetValue(3), rd.GetValue(1), rd.GetValue(0), rd.GetValue(2), Environment.NewLine);
                        c++;
                    } while (await rd.ReadAsync(token).ConfigureAwait(false));

                    c -= 5;
                    if (c > 0)
                        sb.AppendFormat("... and {0} more", c);

                    sb.Append(". Run repair to fix it.");
                    throw new DatabaseInconsistencyException(sb.ToString());
                }

            var real_count = await cmd.ExecuteScalarInt64Async(@"
                SELECT COUNT(*)
                FROM ""BlocklistHash""
            ", 0, token)
                .ConfigureAwait(false);

            var unique_count = await cmd.ExecuteScalarInt64Async(@"
                SELECT COUNT(*)
                FROM (
                    SELECT DISTINCT
                        ""BlocksetID"",
                        ""Index""
                    FROM ""BlocklistHash""
                )
            ", 0, token)
                .ConfigureAwait(false);

            if (real_count != unique_count)
                throw new DatabaseInconsistencyException($"Found {real_count} blocklist hashes, but there should be {unique_count}. Run repair to fix it.");

            var blocksize_per_hashsize = Library.Utility.Utility.FormatInvariantValue(blocksize / hashsize);
            var itemswithnoblocklisthash = await cmd.ExecuteScalarInt64Async($@"
                SELECT COUNT(*)
                FROM (
                    SELECT *
                    FROM (
                        SELECT
                            ""N"".""BlocksetID"",
                            ((""N"".""BlockCount"" + {blocksize_per_hashsize} - 1) / {blocksize_per_hashsize}) AS ""BlocklistHashCountExpected"",
                            CASE
                                WHEN ""G"".""BlocklistHashCount"" IS NULL
                                THEN 0
                                ELSE ""G"".""BlocklistHashCount""
                            END AS ""BlocklistHashCountActual""
                        FROM (
                            SELECT
                                ""BlocksetID"",
                                COUNT(*) AS ""BlockCount""
                            FROM ""BlocksetEntry""
                            GROUP BY ""BlocksetID""
                        ) ""N""
                        LEFT OUTER JOIN (
                            SELECT
                                ""BlocksetID"",
                                COUNT(*) AS ""BlocklistHashCount""
                            FROM ""BlocklistHash""
                            GROUP BY ""BlocksetID""
                        ) ""G""
                            ON ""N"".""BlocksetID"" = ""G"".""BlocksetID""
                        WHERE ""N"".""BlockCount"" > 1
                    )
                    WHERE ""BlocklistHashCountExpected"" != ""BlocklistHashCountActual""
                )
            ", 0, token)
                .ConfigureAwait(false);

            if (itemswithnoblocklisthash != 0)
                throw new DatabaseInconsistencyException($"Found {itemswithnoblocklisthash} file(s) with missing blocklist hashes");

            cmd.SetCommandAndParameters(@"
                SELECT COUNT(*)
                FROM ""Blockset""
                WHERE
                    ""Length"" > 0
                    AND ""ID"" NOT IN (
                        SELECT ""BlocksetId""
                        FROM ""BlocksetEntry""
                    )
            ");

            if (await cmd.ExecuteScalarInt64Async(token).ConfigureAwait(false) != 0)
                throw new DatabaseInconsistencyException("Detected non-empty blocksets with no associated blocks!");

            cmd.SetCommandAndParameters(@"
                SELECT COUNT(*)
                FROM ""FileLookup""
                WHERE
                    ""BlocksetID"" != @FolderBlocksetId
                    AND ""BlocksetID"" != @SymlinkBlocksetId
                    AND NOT ""BlocksetID"" IN (
                        SELECT ""ID""
                        FROM ""Blockset""
                    )
            ");
            cmd.SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID);
            cmd.SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID);
            if (await cmd.ExecuteScalarInt64Async(0, token).ConfigureAwait(false) != 0)
                throw new DatabaseInconsistencyException("Detected files associated with non-existing blocksets!");

            if (!laxVerifyForRepair)
            {
                cmd.SetCommandAndParameters(@"
                    SELECT COUNT(*)
                    FROM ""Fileset""
                    WHERE ""VolumeID"" NOT IN (
                        SELECT ""ID""
                        FROM ""RemoteVolume""
                        WHERE
                            ""Type"" = @Type
                            AND ""State"" != @State
                    )
                ");
                cmd.SetParameterValue("@Type", RemoteVolumeType.Files.ToString());
                cmd.SetParameterValue("@State", RemoteVolumeState.Deleted.ToString());
                var filesetsMissingVolumes =
                    await cmd.ExecuteScalarInt64Async(0, token)
                        .ConfigureAwait(false);

                if (filesetsMissingVolumes != 0)
                {
                    if (filesetsMissingVolumes == 1)
                    {
                        cmd.SetCommandAndParameters(@"
                            SELECT
                                ""ID"",
                                ""Timestamp"",
                                ""VolumeID""
                            FROM ""Fileset""
                            WHERE ""VolumeID"" NOT IN (
                                SELECT ""ID""
                                FROM ""RemoteVolume""
                                WHERE
                                    ""Type"" = @Type
                                    AND ""State"" != @State
                            )
                        ");
                        cmd.SetParameterValue("@Type", RemoteVolumeType.Files.ToString());
                        cmd.SetParameterValue("@State", RemoteVolumeState.Deleted.ToString());
                        await using var reader = await cmd.ExecuteReaderAsync(token)
                            .ConfigureAwait(false);
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            throw new DatabaseInconsistencyException($"Detected 1 fileset with missing volume: FilesetId = {reader.ConvertValueToInt64(0)}, Time = ({ParseFromEpochSeconds(reader.ConvertValueToInt64(1))}), unmatched VolumeID {reader.ConvertValueToInt64(2)}");
                    }

                    throw new DatabaseInconsistencyException($"Detected {filesetsMissingVolumes} filesets with missing volumes");
                }

                cmd.SetCommandAndParameters(@"
                    SELECT COUNT(*)
                    FROM ""RemoteVolume""
                    WHERE
                        ""Type"" = @Type
                        AND ""State"" != @State
                        AND ""ID"" NOT IN (
                            SELECT ""VolumeID""
                            FROM ""Fileset""
                        )
                ");
                cmd.SetParameterValue("@Type", RemoteVolumeType.Files.ToString());
                cmd.SetParameterValue("@State", RemoteVolumeState.Deleted.ToString());
                var volumesMissingFilests = await cmd.ExecuteScalarInt64Async(0, token)
                    .ConfigureAwait(false);

                if (volumesMissingFilests != 0)
                {
                    if (volumesMissingFilests == 1)
                    {
                        cmd.SetCommandAndParameters(@"
                            SELECT
                                ""ID"",
                                ""Name"",
                                ""State""
                            FROM ""RemoteVolume""
                            WHERE
                                ""Type"" = @Type
                                AND ""State"" != @State
                                AND ""ID"" NOT IN (
                                    SELECT ""VolumeID""
                                    FROM ""Fileset""
                                )
                        ");
                        cmd.SetParameterValue("@Type", RemoteVolumeType.Files.ToString());
                        cmd.SetParameterValue("@State", RemoteVolumeState.Deleted.ToString());
                        await using var reader = await cmd.ExecuteReaderAsync(token)
                            .ConfigureAwait(false);
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            throw new DatabaseInconsistencyException($"Detected 1 volume with missing filesets: VolumeId = {reader.ConvertValueToInt64(0)}, Name = {reader.ConvertValueToString(1)}, State = {reader.ConvertValueToString(2)}");
                    }

                    throw new DatabaseInconsistencyException($"Detected {volumesMissingFilests} volumes with missing filesets");
                }
            }

            var nonAttachedFiles = await cmd.ExecuteScalarInt64Async(@"
                SELECT COUNT(*)
                FROM ""FilesetEntry""
                WHERE ""FileID"" NOT IN (
                    SELECT ""ID""
                    FROM ""FileLookup""
                )
            ", token)
                .ConfigureAwait(false);

            if (nonAttachedFiles != 0)
            {
                // Attempt to create a better error message by finding the first 10 fileset ids with the issue
                await using var filesetIdReader = await cmd.ExecuteReaderAsync(@"
                    SELECT DISTINCT(FilesetID)
                    FROM ""FilesetEntry""
                    WHERE ""FileID"" NOT IN (
                        SELECT ""ID""
                        FROM ""FileLookup""
                    )
                    LIMIT 11
                ", token)
                    .ConfigureAwait(false);

                var filesetIds = new HashSet<long>();
                var overflow = false;
                while (await filesetIdReader.ReadAsync(token).ConfigureAwait(false))
                {
                    if (filesetIds.Count >= 10)
                    {
                        overflow = true;
                        break;
                    }
                    filesetIds.Add(filesetIdReader.ConvertValueToInt64(0));
                }

                var pairs = FilesetTimes(token)
                    .Select((x, i) => new { FilesetId = x.Key, Version = i, Time = x.Value })
                    .Where(x => filesetIds.Contains(x.FilesetId))
                    .Select(x => $"Fileset {x.Version}: {x.Time} (id = {x.FilesetId})");

                // Fall back to a generic error message if we can't find the fileset ids
                if (!await pairs.AnyAsync(cancellationToken: token).ConfigureAwait(false))
                    throw new DatabaseInconsistencyException($"Detected {nonAttachedFiles} file(s) in FilesetEntry without corresponding FileLookup entry");

                if (overflow)
                    pairs = pairs.Append("... and more");

                throw new DatabaseInconsistencyException($"Detected {nonAttachedFiles} file(s) in FilesetEntry without corresponding FileLookup entry in the following filesets:{Environment.NewLine}{string.Join(Environment.NewLine, pairs)}");
            }

            if (verifyfilelists)
            {
                var anyError = new List<string>();
                await using (var cmd2 = m_connection.CreateCommand())
                {
                    cmd2.SetTransaction(m_rtr);
                    cmd.SetCommandAndParameters(@"
                        SELECT ""ID""
                        FROM ""Fileset""
                    ");
                    await foreach (var filesetid in cmd.ExecuteReaderEnumerableAsync(token).Select(x => x.ConvertValueToInt64(0, -1)).ConfigureAwait(false))
                    {
                        var expandedlist = await cmd2.SetCommandAndParameters($@"
                            SELECT COUNT(*)
                            FROM (
                                SELECT DISTINCT ""Path""
                                FROM ({LIST_FILESETS})
                                UNION
                                SELECT DISTINCT ""Path""
                                FROM ({LIST_FOLDERS_AND_SYMLINKS})
                            )
                        ")
                            .SetParameterValue("@FilesetId", filesetid)
                            .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
                            .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
                            .ExecuteScalarInt64Async(0, token)
                            .ConfigureAwait(false);

                        //var storedfilelist = cmd2.ExecuteScalarInt64(FormatInvariant(@"SELECT COUNT(*) FROM ""FilesetEntry"", ""FileLookup"" WHERE ""FilesetEntry"".""FilesetID"" = @FilesetId AND ""FileLookup"".""ID"" = ""FilesetEntry"".""FileID"" AND ""FileLookup"".""BlocksetID"" != @FolderBlocksetId AND ""FileLookup"".""BlocksetID"" != @SymlinkBlocksetId"), 0, filesetid, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID);

                        var storedlist = await cmd2.SetCommandAndParameters(@"
                            SELECT COUNT(*)
                            FROM ""FilesetEntry""
                            WHERE ""FilesetEntry"".""FilesetID"" = @FilesetId
                        ")
                            .SetParameterValue("@FilesetId", filesetid)
                            .ExecuteScalarInt64Async(0, token)
                            .ConfigureAwait(false);

                        if (expandedlist != storedlist)
                        {
                            var filesetname = filesetid.ToString();
                            var fileset = await FilesetTimes(token)
                                .Zip(
                                    AsyncEnumerable.Range(0, await FilesetTimes(token)
                                        .CountAsync(cancellationToken: token)
                                        .ConfigureAwait(false)
                                    ), (a, b) => new Tuple<long, long, DateTime>(b, a.Key, a.Value)
                                )
                                .FirstOrDefaultAsync(x => x.Item2 == filesetid, cancellationToken: token)
                                .ConfigureAwait(false);

                            if (fileset != null)
                                filesetname = $"version {fileset.Item1}: {fileset.Item3} (database id: {fileset.Item2})";

                            anyError.Add($"Unexpected difference in fileset {filesetname}, found {expandedlist} entries, but expected {storedlist}");
                        }
                    }
                }

                if (anyError.Count != 0)
                {
                    throw new DatabaseInconsistencyException(string.Join("\n\r", anyError), "FilesetDifferences");
                }
            }
        }

        /// <summary>
        /// Represents a block in the database.
        /// </summary>
        public interface IBlock
        {
            /// <summary>
            /// Gets the hash of the block.
            /// </summary>
            string Hash { get; }
            /// <summary>
            /// Gets the size of the block in bytes.
            /// </summary>
            long Size { get; }
        }

        /// <summary>
        /// Represents a block in the database with its hash and size.
        /// </summary>
        /// <param name="hash">The hash of the block.</param>
        /// <param name="size">The size of the block in bytes.</param>
        internal class Block(string hash, long size) : IBlock
        {
            public string Hash { get; private set; } = hash;
            public long Size { get; private set; } = size;
        }

        /// <summary>
        /// Retrieves all blocks associated with a specific volume ID.
        /// </summary>
        /// <param name="volumeid">The ID of the volume to retrieve blocks for.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of blocks associated with the specified volume ID.</returns>
        public async IAsyncEnumerable<IBlock> GetBlocks(long volumeid, [EnumeratorCancellation] CancellationToken token)
        {
            await using var cmd = await m_connection.CreateCommandAsync(@"
                WITH AllBlocks AS (
                    SELECT ""Hash"", ""Size"" FROM ""Block"" WHERE ""VolumeID"" = @VolumeId
                    UNION ALL
                    SELECT ""Hash"", ""Size"" FROM ""DeletedBlock"" WHERE ""VolumeID"" = @VolumeId
                    UNION ALL
                    SELECT ""Hash"", ""Size"" FROM ""DuplicateBlock"" INNER JOIN ""Block"" ON ""DuplicateBlock"".""BlockID"" = ""Block"".""ID"" WHERE ""DuplicateBlock"".""VolumeID"" = @VolumeId
                )
                SELECT DISTINCT ""Hash"", ""Size"" FROM AllBlocks
            ", token)
                .ConfigureAwait(false);

            cmd.SetTransaction(m_rtr)
                .SetParameterValue("@VolumeId", volumeid);

            await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await rd.ReadAsync(token).ConfigureAwait(false))
                yield return new Block(
                    rd.ConvertValueToString(0) ?? throw new Exception("Hash is null"),
                    rd.ConvertValueToInt64(1)
                );
        }

        /// <summary>
        /// An asynchronous enumerable that retrieves blocklist hashes for files in a specific fileset.
        /// </summary>
        private class BlocklistHashEnumerable : IAsyncEnumerable<string>
        {
            /// <summary>
            /// An asynchronous enumerator for the blocklist hashes.
            /// </summary>
            private class BlocklistHashEnumerator : IDisposable, IAsyncEnumerator<string>
            {
                /// <summary>
                /// The data reader used to read blocklist hashes.
                /// </summary>
                private readonly SqliteDataReader m_reader;
                /// <summary>
                /// The parent enumerable that this enumerator belongs to.
                /// </summary>
                private readonly BlocklistHashEnumerable m_parent;
                /// <summary>
                /// The path of the current file being processed.
                /// </summary>
                private string? m_path = null;
                /// <summary>
                /// Indicates if this is the first entry being processed.
                /// </summary>
                private bool m_first = true;
                /// <summary>
                /// The current blocklist hash being processed.
                /// </summary>
                private string? m_current = null;
                /// <summary>
                /// The cancellation token for this enumerator.
                /// </summary>
                private readonly CancellationToken m_token;

                /// <summary>
                /// Initializes a new instance of the <see cref="BlocklistHashEnumerator"/> class.
                /// </summary>
                /// <param name="parent">The parent enumerable that this enumerator belongs to.</param>
                /// <param name="reader">The data reader used to read blocklist hashes.</param>
                /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
                public BlocklistHashEnumerator(BlocklistHashEnumerable parent, SqliteDataReader reader, CancellationToken token)
                {
                    m_reader = reader;
                    m_parent = parent;
                    m_token = token;
                }

                /// <summary>
                /// Gets the current blocklist hash.
                /// </summary>
                public string Current { get { return m_current!; } }

                public void Dispose() { }
                // The warning is suppressed because the interface requires the method, but there's nothing to dispose.
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
                public async ValueTask DisposeAsync() { }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

                public async ValueTask<bool> MoveNextAsync()
                {
                    m_first = false;

                    if (m_path == null)
                    {
                        m_path = m_reader.ConvertValueToString(0);
                        m_current = m_reader.ConvertValueToString(6);
                        return true;
                    }
                    else
                    {
                        if (m_current == null)
                            return false;

                        if (!await m_reader.ReadAsync(m_token).ConfigureAwait(false))
                        {
                            m_current = null;
                            m_parent.MoreData = false;
                            return false;
                        }

                        var np = m_reader.ConvertValueToString(0);
                        if (m_path != np)
                        {
                            m_current = null;
                            return false;
                        }

                        m_current = m_reader.ConvertValueToString(6);
                        return true;
                    }
                }

                /// <summary>
                /// Resets the enumerator to its initial state.
                /// </summary>
                public void Reset()
                {
                    if (!m_first)
                        throw new Exception("Iterator reset not supported");

                    m_first = false;
                }
            }

            /// <summary>
            /// The data reader used to read blocklist hashes.
            /// </summary>
            private readonly SqliteDataReader m_reader;

            /// <summary>
            /// Initializes a new instance of the <see cref="BlocklistHashEnumerable"/> class.
            /// </summary>
            public BlocklistHashEnumerable(SqliteDataReader reader)
            {
                m_reader = reader;
                MoreData = true;
            }

            /// <summary>
            /// Indicates if there is more data to read.
            /// </summary>
            public bool MoreData { get; protected set; }

            /// <summary>
            /// Gets an enumerator that iterates through the blocklist hashes.
            /// </summary>
            /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
            public IAsyncEnumerator<string> GetEnumerator(CancellationToken token)
            {
                return new BlocklistHashEnumerator(this, m_reader, token);
            }

            IAsyncEnumerator<string> IAsyncEnumerable<string>.GetAsyncEnumerator(CancellationToken token)
            {
                return GetEnumerator(token);
            }
        }

        /// <summary>
        /// SQL query for listing all filesets in the database, including their metadata and blocklist hashes.
        /// </summary>
        public const string LIST_FILESETS = @"
            SELECT
                ""L"".""Path"",
                ""L"".""Lastmodified"",
                ""L"".""Filelength"",
                ""L"".""Filehash"",
                ""L"".""Metahash"",
                ""L"".""Metalength"",
                ""L"".""BlocklistHash"",
                ""L"".""FirstBlockHash"",
                ""L"".""FirstBlockSize"",
                ""L"".""FirstMetaBlockHash"",
                ""L"".""FirstMetaBlockSize"",
                ""M"".""Hash"" AS ""MetaBlocklistHash""
            FROM (
                SELECT
                    ""J"".""Path"",
                    ""J"".""Lastmodified"",
                    ""J"".""Filelength"",
                    ""J"".""Filehash"",
                    ""J"".""Metahash"",
                    ""J"".""Metalength"",
                    ""K"".""Hash"" AS ""BlocklistHash"",
                    ""J"".""FirstBlockHash"",
                    ""J"".""FirstBlockSize"",
                    ""J"".""FirstMetaBlockHash"",
                    ""J"".""FirstMetaBlockSize"",
                    ""J"".""MetablocksetID""
                FROM (
                    SELECT
                        ""A"".""Path"" AS ""Path"",
                        ""D"".""Lastmodified"" AS ""Lastmodified"",
                        ""B"".""Length"" AS ""Filelength"",
                        ""B"".""FullHash"" AS ""Filehash"",
                        ""E"".""FullHash"" AS ""Metahash"",
                        ""E"".""Length"" AS ""Metalength"",
                        ""A"".""BlocksetID"" AS ""BlocksetID"",
                        ""F"".""Hash"" AS ""FirstBlockHash"",
                        ""F"".""Size"" AS ""FirstBlockSize"",
                        ""H"".""Hash"" AS ""FirstMetaBlockHash"",
                        ""H"".""Size"" AS ""FirstMetaBlockSize"",
                        ""C"".""BlocksetID"" AS ""MetablocksetID""
                    FROM ""File"" ""A""
                    LEFT JOIN ""Blockset"" ""B""
                        ON ""A"".""BlocksetID"" = ""B"".""ID""
                    LEFT JOIN ""Metadataset"" ""C""
                        ON ""A"".""MetadataID"" = ""C"".""ID""
                    LEFT JOIN ""FilesetEntry"" ""D""
                        ON ""A"".""ID"" = ""D"".""FileID""
                    LEFT JOIN ""Blockset"" ""E""
                        ON ""E"".""ID"" = ""C"".""BlocksetID""
                    LEFT JOIN ""BlocksetEntry"" ""G""
                        ON ""B"".""ID"" = ""G"".""BlocksetID""
                    LEFT JOIN ""Block"" ""F""
                        ON ""G"".""BlockID"" = ""F"".""ID""
                    LEFT JOIN ""BlocksetEntry"" ""I""
                        ON ""E"".""ID"" = ""I"".""BlocksetID""
                    LEFT JOIN ""Block"" ""H""
                        ON ""I"".""BlockID"" = ""H"".""ID""
                    WHERE
                        ""A"".""BlocksetId"" >= 0
                        AND ""D"".""FilesetID"" = @FilesetId
                        AND (
                            ""I"".""Index"" = 0
                            OR ""I"".""Index"" IS NULL
                        )
                        AND (
                            ""G"".""Index"" = 0
                            OR ""G"".""Index"" IS NULL
                        )
                ) ""J""
                LEFT OUTER JOIN ""BlocklistHash"" ""K""
                    ON ""K"".""BlocksetID"" = ""J"".""BlocksetID""
                ORDER BY
                    ""J"".""Path"",
                    ""K"".""Index""
            ) ""L""
            LEFT OUTER JOIN ""BlocklistHash"" ""M""
                ON ""M"".""BlocksetID"" = ""L"".""MetablocksetID""
        ";

        /// <summary>
        /// SQL query for listing folders and symlinks in a specific fileset, including their metadata and blocklist hashes.
        /// </summary>
        public const string LIST_FOLDERS_AND_SYMLINKS = @"
            SELECT
                ""G"".""BlocksetID"",
                ""G"".""ID"",
                ""G"".""Path"",
                ""G"".""Length"",
                ""G"".""FullHash"",
                ""G"".""Lastmodified"",
                ""G"".""FirstMetaBlockHash"",
                ""H"".""Hash"" AS ""MetablocklistHash""
            FROM (
                SELECT
                    ""B"".""BlocksetID"",
                    ""B"".""ID"",
                    ""B"".""Path"",
                    ""D"".""Length"",
                    ""D"".""FullHash"",
                    ""A"".""Lastmodified"",
                    ""F"".""Hash"" AS ""FirstMetaBlockHash"",
                    ""C"".""BlocksetID"" AS ""MetaBlocksetID""
                FROM
                    ""FilesetEntry"" ""A"",
                    ""File"" ""B"",
                    ""Metadataset"" ""C"",
                    ""Blockset"" ""D"",
                    ""BlocksetEntry"" ""E"",
                    ""Block"" ""F""
                WHERE
                    ""A"".""FileID"" = ""B"".""ID""
                    AND ""B"".""MetadataID"" = ""C"".""ID""
                    AND ""C"".""BlocksetID"" = ""D"".""ID""
                    AND ""E"".""BlocksetID"" = ""C"".""BlocksetID""
                    AND ""E"".""BlockID"" = ""F"".""ID""
                    AND ""E"".""Index"" = 0
                    AND (
                        ""B"".""BlocksetID"" = @FolderBlocksetId
                        OR ""B"".""BlocksetID"" = @SymlinkBlocksetId
                    )
                    AND ""A"".""FilesetID"" = @FilesetId
            ) ""G""
            LEFT OUTER JOIN ""BlocklistHash"" ""H""
                ON ""H"".""BlocksetID"" = ""G"".""MetaBlocksetID""
            ORDER BY
                ""G"".""Path"",
                ""H"".""Index""
            ";

        /// <summary>
        /// Writes the contents of a fileset to a specified volume writer.
        /// </summary>
        /// <param name="filesetvolume">The volume writer to which the fileset will be written.</param>
        /// <param name="filesetId">The ID of the fileset to write.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the fileset has been written.</returns>
        public async Task WriteFileset(Volumes.FilesetVolumeWriter filesetvolume, long filesetId, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand()
                .SetCommandAndParameters(LIST_FOLDERS_AND_SYMLINKS)
                .SetTransaction(m_rtr)
                .SetParameterValue("@FilesetId", filesetId)
                .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
                .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID);

            string? lastpath = null;
            await using (var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                while (await rd.ReadAsync(token).ConfigureAwait(false))
                {
                    var blocksetID = rd.ConvertValueToInt64(0, -1);
                    var path = rd.ConvertValueToString(2);
                    var metalength = rd.ConvertValueToInt64(3, -1);
                    var metahash = rd.ConvertValueToString(4);
                    var metablockhash = rd.ConvertValueToString(6);
                    var metablocklisthash = rd.ConvertValueToString(7);

                    if (path == lastpath)
                        Logging.Log.WriteWarningMessage(LOGTAG, "DuplicatePathFound", null, "Duplicate path detected: {0}", path);

                    lastpath = path;

                    if (blocksetID == FOLDER_BLOCKSET_ID)
                        filesetvolume.AddDirectory(path, metahash, metalength, metablockhash, string.IsNullOrWhiteSpace(metablocklisthash) ? null : new string[] { metablocklisthash });
                    else if (blocksetID == SYMLINK_BLOCKSET_ID)
                        filesetvolume.AddSymlink(path, metahash, metalength, metablockhash, string.IsNullOrWhiteSpace(metablocklisthash) ? null : new string[] { metablocklisthash });
                }

            // TODO: Perhaps run the above query after recreate and compare count(*) with count(*) from filesetentry where id = x

            cmd.SetCommandAndParameters(LIST_FILESETS);
            cmd.SetParameterValue("@FilesetId", filesetId);

            await using (var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                if (await rd.ReadAsync(token).ConfigureAwait(false))
                {
                    var more = false;
                    do
                    {
                        var path = rd.ConvertValueToString(0);
                        var filehash = rd.ConvertValueToString(3);
                        var size = rd.ConvertValueToInt64(2);
                        var lastmodified = new DateTime(rd.ConvertValueToInt64(1, 0), DateTimeKind.Utc);
                        var metahash = rd.ConvertValueToString(4);
                        var metasize = rd.ConvertValueToInt64(5, -1);
                        var p = rd.GetValue(6);
                        var blrd = (p == null || p == DBNull.Value) ? null : new BlocklistHashEnumerable(rd);
                        var blockhash = rd.ConvertValueToString(7);
                        var blocksize = rd.ConvertValueToInt64(8, -1);
                        var metablockhash = rd.ConvertValueToString(9);
                        //var metablocksize = rd.ConvertValueToInt64(10, -1);
                        var metablocklisthash = rd.ConvertValueToString(11);

                        if (blockhash == filehash)
                            blockhash = null;

                        if (metablockhash == metahash)
                            metablockhash = null;

                        await filesetvolume.AddFile(
                            path,
                            filehash,
                            size,
                            lastmodified,
                            metahash,
                            metasize,
                            metablockhash,
                            blockhash,
                            blocksize,
                            blrd,
                            string.IsNullOrWhiteSpace(metablocklisthash) ? null : new string[] { metablocklisthash }
                        )
                            .ConfigureAwait(false);

                        if (blrd == null)
                            more = await rd.ReadAsync(token).ConfigureAwait(false);
                        else
                            more = blrd.MoreData;

                    } while (more);
                }
        }

        /// <summary>
        /// Links a fileset to a specific volume by updating the VolumeID in the Fileset table.
        /// </summary>
        /// <param name="filesetid">The ID of the fileset to link.</param>
        /// <param name="volumeid">The ID of the volume to link the fileset to.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the link operation is finished.</returns>
        public async Task LinkFilesetToVolume(long filesetid, long volumeid, CancellationToken token)
        {
            await using var cmd = await m_connection.CreateCommandAsync(@"
                UPDATE ""Fileset""
                SET ""VolumeID"" = @VolumeId
                WHERE ""ID"" = @FilesetId
            ", token)
                .ConfigureAwait(false);

            var c = await cmd.SetTransaction(m_rtr)
                .SetParameterValue("@VolumeId", volumeid)
                .SetParameterValue("@FilesetId", filesetid)
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);

            if (c != 1)
                throw new Exception($"Failed to link filesetid {filesetid} to volumeid {volumeid}");
        }

        /// <summary>
        /// Pushes timestamp changes from the latest version of a fileset to the previous version.
        /// </summary>
        /// <param name="filesetId">The ID of the fileset whose timestamp changes will be pushed.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the timestamp changes have been pushed.</returns>
        public async Task PushTimestampChangesToPreviousVersion(long filesetId, CancellationToken token)
        {
            var query = @"
                UPDATE ""FilesetEntry"" AS ""oldVersion""
                SET ""Lastmodified"" = ""tempVersion"".""Lastmodified""
                FROM ""FilesetEntry"" AS ""tempVersion""
                WHERE
                    ""oldVersion"".""FileID"" = ""tempVersion"".""FileID""
                    AND ""tempVersion"".""FilesetID"" = @FilesetId
                    AND ""oldVersion"".""FilesetID"" = (
                        SELECT ""ID""
                        FROM ""Fileset""
                        WHERE ""ID"" != @FilesetId
                        ORDER BY ""Timestamp"" DESC
                        LIMIT 1
                    )
            ";

            await using var cmd = await m_connection
                .CreateCommandAsync(query, token)
                .ConfigureAwait(false);

            await cmd.SetTransaction(m_rtr)
                .SetParameterValue("@FilesetId", filesetId)
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Keeps a list of filenames in a temporary table with a single column Path.
        /// </summary>
        public class FilteredFilenameTable : IDisposable, IAsyncDisposable
        {
            /// <summary>
            /// The name of the temporary table that holds the filtered filenames.
            /// </summary>
            public string Tablename { get; private set; }
            /// <summary>
            /// The database used to create and manage the temporary table.
            /// </summary>
            private readonly LocalDatabase m_db;

            /// <summary>
            /// Initializes a new instance of the <see cref="FilteredFilenameTable"/> class.
            /// </summary>
            /// <param name="db">The database to use for creating the temporary table.</param>
            private FilteredFilenameTable(LocalDatabase db)
            {
                Tablename = $"Filenames-{Library.Utility.Utility.GetHexGuid()}";
                m_db = db;
            }

            [Obsolete("Calling this constructor will throw an exception. Use the Create method instead.")]
            public FilteredFilenameTable(SqliteConnection connection, IFilter filter)
            {
                throw new NotImplementedException("Use the Create method instead.");
            }

            /// <summary>
            /// Creates a new instance of the <see cref="FilteredFilenameTable"/> class asynchronously.
            /// </summary>
            /// <param name="db">The database to use for creating the temporary table.</param>
            /// <param name="filter">The filter to apply to the filenames.</param>
            /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
            /// <returns>A task that represents the asynchronous operation, with a <see cref="FilteredFilenameTable"/> as the result.</returns>
            public static async Task<FilteredFilenameTable> CreateFilteredFilenameTableAsync(LocalDatabase db, IFilter filter, CancellationToken token)
            {
                var ftt = new FilteredFilenameTable(db);

                var type = FilterType.Regexp;
                if (filter is FilterExpression expression)
                    type = expression.Type;

                // Bugfix: SQLite does not handle case-insensitive LIKE with non-ascii characters
                if (type != FilterType.Regexp && !Library.Utility.Utility.IsFSCaseSensitive && filter.ToString()!.Any(x => x > 127))
                    type = FilterType.Regexp;

                if (filter.Empty)
                {
                    await using var cmd = await db.Connection.CreateCommandAsync($@"
                        CREATE TEMPORARY TABLE ""{ftt.Tablename}"" AS
                        SELECT DISTINCT ""Path""
                        FROM ""File""
                    ", token)
                        .ConfigureAwait(false);

                    await cmd.SetTransaction(db.Transaction)
                        .ExecuteNonQueryAsync(token)
                        .ConfigureAwait(false);

                    return ftt;
                }

                if (type == FilterType.Regexp || type == FilterType.Group)
                {
                    // TODO: Optimize this to not rely on the "File" view, and not instantiate the paths in full
                    await using var cmd = await db.Connection.CreateCommandAsync($@"
                        CREATE TEMPORARY TABLE ""{ftt.Tablename}"" (
                            ""Path"" TEXT NOT NULL
                        )
                    ", token)
                        .ConfigureAwait(false);

                    await cmd.SetTransaction(db.Transaction)
                        .ExecuteNonQueryAsync(token)
                        .ConfigureAwait(false);

                    cmd.SetCommandAndParameters($@"
                        INSERT INTO ""{ftt.Tablename}"" (""Path"")
                        VALUES (@Path)
                    ");

                    await using var c2 = await db.Connection.CreateCommandAsync(@"
                        SELECT DISTINCT ""Path""
                        FROM ""File""
                    ", token)
                        .ConfigureAwait(false);

                    c2.SetTransaction(db.Transaction);
                    await using (var rd = await c2.ExecuteReaderAsync(token).ConfigureAwait(false))
                        while (await rd.ReadAsync(token).ConfigureAwait(false))
                        {
                            var p = rd.ConvertValueToString(0) ?? "";
                            if (FilterExpression.Matches(filter, p))
                            {
                                await cmd
                                    .SetParameterValue("@Path", p)
                                    .ExecuteNonQueryAsync(token)
                                    .ConfigureAwait(false);
                            }
                        }

                    await db.Transaction.CommitAsync(token: token).ConfigureAwait(false);
                }
                else
                {
                    var sb = new StringBuilder();
                    var args = new Dictionary<string, object?>();
                    foreach (var f in ((FilterExpression)filter).GetSimpleList())
                    {
                        if (sb.Length != 0)
                            sb.Append(" OR ");

                        var argName = $"@Arg{Library.Utility.Utility.FormatInvariantValue(args.Count)}";
                        if (type == FilterType.Wildcard)
                        {
                            sb.Append(@$"""Path"" LIKE {argName}");
                            args.Add(argName, f.Replace('*', '%').Replace('?', '_'));
                        }
                        else
                        {
                            sb.Append(@$"""Path"" = {argName}");
                            args.Add(argName, f);
                        }
                    }

                    await using var cmd = db.Connection.CreateCommand();
                    cmd.SetTransaction(db.Transaction);

                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE TEMPORARY TABLE ""{ftt.Tablename}"" (
                            ""Path"" TEXT NOT NULL
                        )
                    ", token)
                        .ConfigureAwait(false);

                    await cmd.ExecuteNonQueryAsync($@"
                        INSERT INTO ""{ftt.Tablename}""
                        SELECT DISTINCT ""Path""
                        FROM ""File""
                        WHERE {sb}
                    ", args, token)
                        .ConfigureAwait(false);

                    await db.Transaction.CommitAsync(token: token).ConfigureAwait(false);
                }

                return ftt;
            }

            public void Dispose()
            {
                DisposeAsync().AsTask().Await();
            }

            public async ValueTask DisposeAsync()
            {
                if (Tablename != null)
                    try
                    {
                        await using var cmd = m_db.Connection.CreateCommand();
                        await cmd.ExecuteNonQueryAsync(@$"
                            DROP TABLE IF EXISTS ""{Tablename}""
                        ", default)
                            .ConfigureAwait(false);
                    }
                    catch { }
                    finally { Tablename = null!; }
            }
        }

        /// <summary>
        /// Renames a remote file in the database, preserving its ID links.
        /// </summary>
        /// <param name="oldname">The current name of the remote file.</param>
        /// <param name="newname">The new name for the remote file.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the renaming operation is finished.</returns>
        /// <exception cref="Exception">Thrown if the renaming operation does not affect exactly one row.</exception>
        public async Task RenameRemoteFile(string oldname, string newname, CancellationToken token)
        {
            //Rename the old entry, to preserve ID links
            await using var cmd = await m_connection.CreateCommandAsync(@"
                UPDATE ""Remotevolume""
                SET ""Name"" = @Newname
                WHERE ""Name"" = @Oldname
            ", token)
                .ConfigureAwait(false);

            var c = await cmd.SetTransaction(m_rtr)
                .SetParameterValue("@Newname", newname)
                .SetParameterValue("@Oldname", oldname)
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);

            if (c != 1)
                throw new Exception($"Unexpected result from renaming \"{oldname}\" to \"{newname}\", expected {1} got {c}");

            // Grab the type of entry
            var type = (RemoteVolumeType)Enum.Parse(
                typeof(RemoteVolumeType),
                (await cmd.SetCommandAndParameters(@"
                        SELECT ""Type""
                        FROM ""Remotevolume""
                        WHERE ""Name"" = @Name
                    ")
                    .SetParameterValue("@Name", newname)
                    .ExecuteScalarAsync(token)
                    .ConfigureAwait(false)
                )?.ToString() ?? "",
                true
            );

            //Create a fake new entry with the old name and mark as deleting
            // as this ensures we will remove it, if it shows up in some later listing
            var newvolId =
                await RegisterRemoteVolume(oldname, type, RemoteVolumeState.Deleting, token)
                    .ConfigureAwait(false);

            // IF needed, also create an empty fileset, so the validation works
            if (type == RemoteVolumeType.Files)
            {
                await CreateFileset(newvolId, DateTime.UnixEpoch, token)
                    .ConfigureAwait(false);
                await m_rtr.CommitAsync(token: token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates a timestamped backup operation to correctly associate the fileset with the time it was created.
        /// </summary>
        /// <param name="volumeid">The ID of the fileset volume to update.</param>
        /// <param name="timestamp">The timestamp of the operation to create.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that when awaited contains the ID of the newly created fileset.</returns>
        public virtual async Task<long> CreateFileset(long volumeid, DateTime timestamp, CancellationToken token)
        {
            await using var cmd = await m_connection.CreateCommandAsync(@"
                INSERT INTO ""Fileset"" (
                    ""OperationID"",
                    ""Timestamp"",
                    ""VolumeID"",
                    ""IsFullBackup""
                )
                VALUES (
                    @OperationId,
                    @Timestamp,
                    @VolumeId,
                    @IsFullBackup
                );
                SELECT last_insert_rowid();
            ", token)
                .ConfigureAwait(false);

            var id = await cmd
                .SetTransaction(m_rtr)
                .SetParameterValue("@OperationId", m_operationid)
                .SetParameterValue("@Timestamp", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(timestamp))
                .SetParameterValue("@VolumeId", volumeid)
                .SetParameterValue("@IsFullBackup", BackupType.PARTIAL_BACKUP)
                .ExecuteScalarInt64Async(-1, token)
                .ConfigureAwait(false);

            return id;
        }

        /// <summary>
        /// Adds a link between an index volume and a block volume.
        /// </summary>
        /// <param name="indexVolumeID">The ID of the index volume.</param>
        /// <param name="blockVolumeID">The ID of the block volume.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the link has been added.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if either volume ID is less than or equal to 0.</exception>
        public async Task AddIndexBlockLink(long indexVolumeID, long blockVolumeID, CancellationToken token)
        {
            if (indexVolumeID <= 0)
                throw new ArgumentOutOfRangeException(nameof(indexVolumeID), "Index volume ID must be greater than 0.");
            if (blockVolumeID <= 0)
                throw new ArgumentOutOfRangeException(nameof(blockVolumeID), "Block volume ID must be greater than 0.");

            await m_insertIndexBlockLink
                .SetTransaction(m_rtr)
                .SetParameterValue("@IndexVolumeId", indexVolumeID)
                .SetParameterValue("@BlockVolumeId", blockVolumeID)
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Returns all unique blocklists for a given volume.
        /// </summary>
        /// <param name="volumeid">The volume ID to get blocklists for.</param>
        /// <param name="blocksize">The blocksize.</param>
        /// <param name="hashsize">The size of the hash.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>An enumerable of tuples containing the blocklist hash, the blocklist data and the length of the data</returns>
        public async IAsyncEnumerable<(string Hash, byte[] Buffer, int Size)> GetBlocklists(long volumeid, long blocksize, int hashsize, [EnumeratorCancellation] CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(m_rtr);
            // Group subquery by hash to ensure that each blocklist hash appears only once in the result
            // The AllBlocks CTE is used to map both active and duplicate blocks from the volume,
            // because the new volume is initially registered with only duplicate blocks.
            var sql = @"
                    WITH ""AllBlocksInVolume"" AS (
                        SELECT DISTINCT
                            ""ID"",
                            ""Hash"",
                            ""Size""
                        FROM (
                            SELECT
                                ""ID"",
                                ""Hash"",
                                ""Size""
                            FROM ""Block""
                            WHERE ""VolumeID"" = @VolumeId
                            UNION
                            SELECT
                                ""DuplicateBlock"".""BlockID"" AS ""ID"",
                                ""Block"".""Hash"" AS ""Hash"",
                                ""Block"".""Size"" AS ""Size""
                            FROM ""DuplicateBlock""
                            INNER JOIN ""Block""
                                ON ""DuplicateBlock"".""BlockID"" = ""Block"".""ID""
                            WHERE ""DuplicateBlock"".""VolumeID"" = @VolumeId
                        )
                    )

                    SELECT
                        ""A"".""Hash"" AS ""BlockHash"",
                        ""C"".""Hash"" AS ""ItemHash""
                    FROM
                        (
                            SELECT
                                ""BlocklistHash"".""BlocksetID"",
                                ""AllBlocksInVolume"".""Hash"",
                                ""BlocklistHash"".""Index""
                            FROM
                                ""BlocklistHash"",
                                ""AllBlocksInVolume""
                            WHERE ""BlocklistHash"".""Hash"" = ""AllBlocksInVolume"".""Hash""
                            GROUP BY
                                ""AllBlocksInVolume"".""Hash"",
                                ""AllBlocksInVolume"".""Size""
                        ) ""A"",
                        ""BlocksetEntry"" ""B"",
                        ""Block"" ""C""

                    WHERE
                        ""B"".""BlocksetID"" = ""A"".""BlocksetID""
                        AND ""B"".""Index"" >= (""A"".""Index"" * @HashesPerBlock)
                        AND ""B"".""Index"" < ((""A"".""Index"" + 1) * @HashesPerBlock)
                        AND ""C"".""ID"" = ""B"".""BlockID""
                        ORDER BY ""A"".""BlocksetID"", ""B"".""Index""
                    ";

            string? curHash = null;
            var count = 0;
            var buffer = new byte[blocksize];

            cmd.SetCommandAndParameters(sql)
                .SetParameterValue("@VolumeId", volumeid)
                .SetParameterValue("@HashesPerBlock", blocksize / hashsize);

            await using (var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                while (await rd.ReadAsync(token).ConfigureAwait(false))
                {
                    var blockhash = rd.ConvertValueToString(0);
                    if (curHash != null && (blockhash != curHash || count + hashsize > buffer.Length))
                    {
                        yield return (curHash, buffer, count);
                        buffer = new byte[blocksize];
                        count = 0;
                    }

                    var hash = Convert.FromBase64String(rd.ConvertValueToString(1) ?? throw new Exception("Hash is null"));
                    Array.Copy(hash, 0, buffer, count, hashsize);
                    curHash = blockhash;
                    count += hashsize;
                }

            if (curHash != null)
                yield return (curHash, buffer, count);
        }

        /// <summary>
        /// Update fileset with full backup state.
        /// </summary>
        /// <param name="fileSetId">Existing file set to update.</param>
        /// <param name="isFullBackup">Full backup state.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the update is finished.</returns>
        public async Task UpdateFullBackupStateInFileset(long fileSetId, bool isFullBackup, CancellationToken token)
        {
            await using var cmd = await m_connection.CreateCommandAsync(@"
                UPDATE ""Fileset""
                SET ""IsFullBackup"" = @IsFullBackup
                WHERE ""ID"" = @FilesetId
            ", token)
                .ConfigureAwait(false);

            await cmd.SetTransaction(m_rtr)
                .SetParameterValue("@FilesetId", fileSetId)
                .SetParameterValue("@IsFullBackup", isFullBackup ? BackupType.FULL_BACKUP : BackupType.PARTIAL_BACKUP)
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Removes all entries in the fileset entry table for a given fileset ID.
        /// </summary>
        /// <param name="filesetId">The fileset ID to clear.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the entries have been cleared.</returns>
        public async Task ClearFilesetEntries(long filesetId, CancellationToken token)
        {
            await using var cmd = await m_connection.CreateCommandAsync(@"
                DELETE FROM ""FilesetEntry""
                WHERE ""FilesetID"" = @FilesetId
            ", token)
                .ConfigureAwait(false);

            await cmd.SetTransaction(m_rtr)
                .SetParameterValue("@FilesetId", filesetId)
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the last previous fileset that was incomplete.
        /// </summary>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that when awaited returns the last incomplete fileset or default</returns>
        public async Task<RemoteVolumeEntry> GetLastIncompleteFilesetVolume(CancellationToken token)
        {
            var candidates = GetIncompleteFilesets(token)
                .OrderBy(x => x.Value);

            if (await candidates.AnyAsync(cancellationToken: token).ConfigureAwait(false))
                return await GetRemoteVolumeFromFilesetID(
                    (
                        await candidates.LastAsync(cancellationToken: token).ConfigureAwait(false)
                    ).Key
                , token)
                    .ConfigureAwait(false);

            return default;
        }

        /// <summary>
        /// Gets a list of incomplete filesets.
        /// </summary>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>An asynchronous enumerable of key-value pairs where the key is the fileset ID and the value is the timestamp of the fileset.</returns>
        public async IAsyncEnumerable<KeyValuePair<long, DateTime>> GetIncompleteFilesets([EnumeratorCancellation] CancellationToken token)
        {
            await using var cmd = await m_connection.CreateCommandAsync(@$"
                SELECT DISTINCT
                    ""Fileset"".""ID"",
                    ""Fileset"".""Timestamp""
                FROM
                    ""Fileset"",
                    ""RemoteVolume""
                WHERE
                    ""RemoteVolume"".""ID"" = ""Fileset"".""VolumeID""
                    AND ""Fileset"".""ID"" IN (
                        SELECT ""FilesetID""
                        FROM ""FilesetEntry""
                    )
                    AND (
                        ""RemoteVolume"".""State"" = '{Library.Utility.Utility.FormatInvariantValue(RemoteVolumeState.Uploading)}'
                        OR ""RemoteVolume"".""State"" = '{Library.Utility.Utility.FormatInvariantValue(RemoteVolumeState.Temporary)}'
                    )
            ", token)
                .ConfigureAwait(false);

            await using var rd = await cmd.SetTransaction(m_rtr)
                .ExecuteReaderAsync(token)
                .ConfigureAwait(false);

            while (await rd.ReadAsync(token).ConfigureAwait(false))
            {
                yield return new KeyValuePair<long, DateTime>(
                    rd.ConvertValueToInt64(0),
                    ParseFromEpochSeconds(rd.ConvertValueToInt64(1))
                        .ToLocalTime()
                );
            }
        }

        /// <summary>
        /// Gets the remote volume entry from the fileset ID.
        /// </summary>
        /// <param name="filesetID">The fileset ID.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that when awaited returns the remote volume entry associated with the fileset ID, or default if not found.</returns>
        public async Task<RemoteVolumeEntry> GetRemoteVolumeFromFilesetID(long filesetID, CancellationToken token)
        {
            await using var cmd = await m_connection.CreateCommandAsync(@"
                SELECT
                    ""RemoteVolume"".""ID"",
                    ""Name"",
                    ""Type"",
                    ""Size"",
                    ""Hash"",
                    ""State"",
                    ""DeleteGraceTime"",
                    ""ArchiveTime"",
                    ""LockExpirationTime""
                FROM
                    ""RemoteVolume"",
                    ""Fileset""
                WHERE
                    ""Fileset"".""VolumeID"" = ""RemoteVolume"".""ID""
                    AND ""Fileset"".""ID"" = @FilesetId
            ", token)
                .ConfigureAwait(false);

            await using var rd = await cmd.SetTransaction(m_rtr)
                .SetParameterValue("@FilesetId", filesetID)
                .ExecuteReaderAsync(token)
                .ConfigureAwait(false);

            if (await rd.ReadAsync(token).ConfigureAwait(false))
                return new RemoteVolumeEntry(
                    rd.ConvertValueToInt64(0, -1),
                    rd.ConvertValueToString(1),
                    rd.ConvertValueToString(4),
                    rd.ConvertValueToInt64(3, -1),
                    (RemoteVolumeType)Enum.Parse(typeof(RemoteVolumeType), rd.ConvertValueToString(2) ?? ""),
                    (RemoteVolumeState)Enum.Parse(typeof(RemoteVolumeState), rd.ConvertValueToString(5) ?? ""),
                    ParseFromEpochSeconds(rd.ConvertValueToInt64(6)).ToLocalTime(),
                    ParseFromEpochSeconds(rd.ConvertValueToInt64(7)).ToLocalTime(),
                    ParseFromEpochSeconds(rd.ConvertValueToInt64(8)).ToLocalTime()
                );
            else
                return default(RemoteVolumeEntry);
        }

        /// <summary>
        /// Purges log data and remote operations older than the specified threshold.
        /// </summary>
        /// <param name="threshold">The threshold date and time; all log data and remote operations older than this will be purged.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the purge operation is finished.</returns>
        public async Task PurgeLogData(DateTime threshold, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(m_rtr);
            var t = Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(threshold);

            await cmd.SetCommandAndParameters(@"
                DELETE FROM ""LogData""
                WHERE ""Timestamp"" < @Timestamp
            ")
                .SetParameterValue("@Timestamp", t)
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);

            await cmd.SetCommandAndParameters(@"
                DELETE FROM ""RemoteOperation""
                WHERE ""Timestamp"" < @Timestamp
            ")
                .SetParameterValue("@Timestamp", t)
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);

            await m_rtr.CommitAsync(token: token).ConfigureAwait(false);
        }

        /// <summary>
        /// Purges deleted remote volumes that have not been modified since the specified threshold.
        /// </summary>
        /// <param name="threshold">The threshold date and time; all deleted remote volumes older than this will be purged.</param>
        /// <returns>A task that completes when the purge operation is finished.</returns>
        public async Task PurgeDeletedVolumes(DateTime threshold, CancellationToken token)
        {
            await m_removedeletedremotevolumeCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@Now", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(threshold))
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);

            await m_rtr.CommitAsync(token: token).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns the ID of an empty metadata blockset. If no empty blockset is found, it returns the ID of the smallest blockset that is not in the given block volume IDs.
        /// If no such blockset is found, it returns -1.
        /// </summary>
        /// <param name="blockVolumeIds">The volume ids to ignore when searching for a suitable metadata block.</param>
        /// <param name="emptyHash">The hash of the empty blockset.</param>
        /// <param name="emptyHashSize">The size of the empty blockset.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the ID of the empty metadata blockset, or -1 if no suitable blockset is found</returns>
        public async Task<long> GetEmptyMetadataBlocksetId(IEnumerable<long> blockVolumeIds, string emptyHash, long emptyHashSize, CancellationToken token)
        {
            await using var cmd = Connection.CreateCommand(@"
                SELECT ""ID""
                FROM ""Blockset""
                WHERE
                    ""FullHash"" = @EmptyHash
                    AND ""Length"" == @EmptyHashSize
                    AND ""ID"" NOT IN (
                        SELECT ""BlocksetID""
                        FROM
                            ""BlocksetEntry"",
                            ""Block""
                        WHERE
                            ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                            AND ""Block"".""VolumeID"" NOT IN (@BlockVolumeIds)
                    )
            ")
                .SetTransaction(m_rtr)
                .ExpandInClauseParameterMssqlite("@BlockVolumeIds", blockVolumeIds)
                .SetParameterValue("@EmptyHash", emptyHash)
                .SetParameterValue("@EmptyHashSize", emptyHashSize);

            var res = await cmd.ExecuteScalarInt64Async(-1, token)
                .ConfigureAwait(false);

            // No empty block found, try to find a zero-length block instead
            if (res < 0 && emptyHashSize != 0)
                res = await cmd.SetCommandAndParameters(@"
                    SELECT ""ID""
                    FROM ""Blockset""
                    WHERE
                        ""Length"" == @EmptyHashSize
                        AND ""ID"" NOT IN (
                            SELECT ""BlocksetID""
                            FROM
                                ""BlocksetEntry"",
                                ""Block""
                            WHERE
                                ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                                AND ""Block"".""VolumeID"" NOT IN (@BlockVolumeIds)
                        )
                ")
                  .ExpandInClauseParameterMssqlite("@BlockVolumeIds", blockVolumeIds)
                  .SetParameterValue("@EmptyHashSize", 0)
                  .ExecuteScalarInt64Async(-1, token)
                  .ConfigureAwait(false);

            // No empty block found, pick the smallest one
            if (res < 0)
                res = await cmd.SetCommandAndParameters(@"
                    SELECT ""Blockset"".""ID""
                    FROM
                        ""BlocksetEntry"",
                        ""Blockset"",
                        ""Metadataset"",
                        ""Block""
                    WHERE
                        ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID""
                        AND ""BlocksetEntry"".""BlocksetID"" = ""Blockset"".""ID""
                        AND ""Block"".""ID"" = ""BlocksetEntry"".""BlockID""
                        AND ""Block"".""VolumeID"" NOT IN (@BlockVolumeIds)
                        AND ""Blockset"".""Length"" > 0
                    ORDER BY ""Blockset"".""Length"" ASC
                    LIMIT 1
                ")
                  .ExpandInClauseParameterMssqlite("@BlockVolumeIds", blockVolumeIds)
                  .ExecuteScalarInt64Async(-1, token)
                  .ConfigureAwait(false);

            return res;
        }

        public virtual void Dispose()
        {
            this.DisposeAsync().AsTask().Await();
        }

        public virtual async ValueTask DisposeAsync()
        {
            if (IsDisposed)
                return;

            DisposeAllFields<SqliteCommand>(this, false);

            if (ShouldCloseConnection && m_connection != null)
            {
                await m_rtr.DisposeAsync().ConfigureAwait(false);
                if (m_connection.State == ConnectionState.Open && !m_hasExecutedVacuum)
                {
                    await using (var command = m_connection.CreateCommand())
                    await using (var tr = m_connection.BeginTransaction())
                    {
                        // SQLite recommends that PRAGMA optimize is run just before closing each database connection.
                        await command
                            .SetTransaction(tr)
                            .ExecuteNonQueryAsync("PRAGMA optimize", default)
                            .ConfigureAwait(false);

                        try
                        {
                            await tr.CommitAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "FailedToCommitTransaction", ex, "Failed to commit transaction after pragma optimize, usually caused by the a no-op transaction");
                        }
                    }

                    m_connection.Close();
                }

                await m_connection.DisposeAsync().ConfigureAwait(false);
            }

            IsDisposed = true;
        }

        /// <summary>
        /// Disposes all fields of a certain type, in the instance and its bases.
        /// </summary>
        /// <typeparam name="T">The type of fields to find.</typeparam>
        /// <param name="item">The item to dispose.</param>
        /// <param name="throwExceptions"><c>True</c> if an aggregate exception should be thrown, or <c>false</c> if exceptions are silently captured.</param>
        public static void DisposeAllFields<T>(object item, bool throwExceptions)
            where T : IDisposable
        {
            var typechain = new List<Type>();
            var cur = item.GetType();
            var exceptions = new List<Exception>();

            while (cur != null && cur != typeof(object))
            {
                typechain.Add(cur);
                cur = cur.BaseType;
            }

            var fields =
                typechain.SelectMany(x =>
                    x.GetFields(
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.FlattenHierarchy
                    )
                ).Distinct()
                    .Where(x => x.FieldType.IsAssignableFrom(typeof(T)));

            foreach (var p in fields)
                try
                {
                    var val = p.GetValue(item);
                    if (val != null)
                        ((T)val).Dispose();
                }
                catch (Exception ex)
                {
                    if (throwExceptions)
                        exceptions.Add(ex);
                }

            if (exceptions.Count > 0)
                throw new AggregateException(exceptions);
        }

        /// <summary>
        /// Writes the results of a basic operation to the log.
        /// </summary>
        /// <param name="result">The results to write.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the results have been written.</returns>
        public async Task WriteResults(IBasicResults result, CancellationToken token)
        {
            if (IsDisposed)
                return;

            if (m_connection != null && result != null)
            {
                if (result is BasicResults basicResults)
                {
                    await basicResults.FlushLog(this, token).ConfigureAwait(false);
                    if (basicResults.EndTime.Ticks == 0)
                        basicResults.EndTime = DateTime.UtcNow;
                }

                var serializer = new JsonFormatSerializer();
                await LogMessage("Result",
                    serializer.SerializeResults(result),
                    null,
                    token
                )
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The current index into the path prefix buffer.
        /// </summary>
        private int m_pathPrefixIndex = 0;
        /// <summary>
        /// The path prefix lookup list.
        /// </summary>
        private readonly KeyValuePair<string, long>[] m_pathPrefixLookup = new KeyValuePair<string, long>[5];

        /// <summary>
        /// Gets the path prefix ID, optionally creating it in the process..
        /// </summary>
        /// <param name="prefix">The path to get the prefix for.</param>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that when awaited returns the path prefix ID.</returns>
        public async Task<long> GetOrCreatePathPrefix(string prefix, CancellationToken token)
        {
            // Ring-buffer style lookup
            for (var i = 0; i < m_pathPrefixLookup.Length; i++)
            {
                var ix = (i + m_pathPrefixIndex) % m_pathPrefixLookup.Length;
                if (string.Equals(m_pathPrefixLookup[ix].Key, prefix, StringComparison.Ordinal))
                    return m_pathPrefixLookup[ix].Value;
            }

            var id = await m_findpathprefixCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@Prefix", prefix)
                .ExecuteScalarInt64Async(token)
                .ConfigureAwait(false);

            if (id < 0)
            {
                id = await m_insertpathprefixCommand
                    .SetTransaction(m_rtr)
                    .SetParameterValue("@Prefix", prefix)
                    .ExecuteScalarInt64Async(token)
                    .ConfigureAwait(false);
            }

            m_pathPrefixIndex = (m_pathPrefixIndex + 1) % m_pathPrefixLookup.Length;
            m_pathPrefixLookup[m_pathPrefixIndex] = new KeyValuePair<string, long>(prefix, id);

            return id;
        }

        /// <summary>
        /// The path separators on this system.
        /// </summary>
        private static readonly char[] _pathseparators = [
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar,
        ];

        /// <summary>
        /// Helper method that splits a path on the last path separator.
        /// </summary>
        /// <param name="path">The path to split.</param>
        /// <returns>The prefix and name.</returns>
        public static KeyValuePair<string, string> SplitIntoPrefixAndName(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException($"Invalid path: {path}", nameof(path));

            int nLast = path.TrimEnd(_pathseparators).LastIndexOfAny(_pathseparators);
            if (nLast >= 0)
                return new KeyValuePair<string, string>(path.Substring(0, nLast + 1), path.Substring(nLast + 1));

            return new KeyValuePair<string, string>(string.Empty, path);
        }
    }

    /// <summary>
    /// Defines the backups types.
    /// </summary>
    public static class BackupType
    {
        /// <summary>
        /// Indicates a partial backup.
        /// </summary>
        public const int PARTIAL_BACKUP = 0;
        /// <summary>
        /// Indicates a full backup.
        /// </summary>
        public const int FULL_BACKUP = 1;
    }

}
