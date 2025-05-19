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

// Expose internal classes to UnitTests, so that Database classes can be tested
[assembly: InternalsVisibleTo("Duplicati.UnitTest")]

namespace Duplicati.Library.Main.Database
{
    internal class LocalDatabase : IDisposable
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

        // All of the required fields have been set to null! to ignore the compiler warning, as they will be initialized in the Create* factory functions.
        protected SqliteConnection m_connection = null!;
        protected long m_operationid = -1;
        protected long m_pagecachesize;
        private bool m_hasExecutedVacuum;
        // TODO use this rather than passing down transactions.
        // TODO dispose should check this, and report if it was not disposed prior to calling dispose. Maybe the rtr itself should check this during dispose? And just throw a warning during runtime.
        protected ReusableTransaction m_rtr;
        public ReusableTransaction Transaction { get { return m_rtr; } }

        private SqliteCommand m_updateremotevolumeCommand = null!;
        private SqliteCommand m_selectremotevolumesCommand = null!;
        private SqliteCommand m_selectremotevolumeCommand = null!;
        private SqliteCommand m_removeremotevolumeCommand = null!;
        private SqliteCommand m_removedeletedremotevolumeCommand = null!;
        private SqliteCommand m_selectremotevolumeIdCommand = null!;
        private SqliteCommand m_createremotevolumeCommand = null!;
        private SqliteCommand m_selectduplicateRemoteVolumesCommand = null!;

        private SqliteCommand m_insertlogCommand = null!;
        private SqliteCommand m_insertremotelogCommand = null!;
        private SqliteCommand m_insertIndexBlockLink = null!;

        private SqliteCommand m_findpathprefixCommand = null!;
        private SqliteCommand m_insertpathprefixCommand = null!;

        public const long FOLDER_BLOCKSET_ID = -100;
        public const long SYMLINK_BLOCKSET_ID = -200;

        public DateTime OperationTimestamp { get; private set; }

        internal SqliteConnection Connection { get { return m_connection; } }

        public bool IsDisposed { get; private set; }

        public bool ShouldCloseConnection { get; set; }

        // Constructor is private to force use of CreateLocalDatabaseAsync
        protected LocalDatabase() { }

        // Arguments are not used, but are required to match the constructor signatures
        [Obsolete("Calling the constructor will throw an exception. Use the CreateLocalDatabaseAsync or CreateLocalDatabase functions instead")]
        public LocalDatabase(object? _ignore1 = null, object? _ignore2 = null, object? _ignore3 = null, object? _ignore4 = null)
        {
            throw new NotImplementedException("Use the CreateLocalDatabaseAsync or CreateLocalDatabase functions instead");
        }

        protected static SqliteConnection CreateConnection(string path, long pagecachesize)
        {
            return CreateConnectionAsync(path, pagecachesize).Await();
        }

        protected static async Task<SqliteConnection> CreateConnectionAsync(string path, long pagecachesize)
        {
            path = Path.GetFullPath(path);
            if (!Directory.Exists(Path.GetDirectoryName(path)))
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new DirectoryNotFoundException("Path was a root folder."));

            var c = await SQLiteHelper.SQLiteLoader.LoadConnectionAsync(path, pagecachesize);

            try
            {
                SQLiteHelper.DatabaseUpgrader.UpgradeDatabase(c, path, typeof(DatabaseSchemaMarker));
            }
            catch
            {
                //Don't leak database connections when something goes wrong
                await c.DisposeAsync();
                throw;
            }

            return c;
        }

        // TODO Create these once the signature is stable.
        // public static LocalDatabase CreateLocalDatabase(string path, string operation, bool shouldclose, long pagecachesize)
        // {
        //     return CreateLocalDatabaseAsync(path, operation, shouldclose, pagecachesize).Await();
        // }

        // public static LocalDatabase CreateLocalDatabase(LocalDatabase db)
        // {
        //     return CreateLocalDatabaseAsync(db).Await();
        // }

        // public static LocalDatabase CreateLocalDatabase(SqliteConnection connection, string operation)
        // {
        //     return CreateLocalDatabaseAsync(connection, operation).Await();
        // }

        // private static LocalDatabase CreateLocalDatabase(SqliteConnection connection)
        // {
        //     return CreateLocalDatabaseAsync(connection).Await();
        // }

        public static async Task<LocalDatabase> CreateLocalDatabaseAsync(string path, string operation, bool shouldclose, long pagecachesize)
        {
            var db = new LocalDatabase();

            return await CreateLocalDatabaseAsync(db, path, operation, shouldclose, pagecachesize);
        }

        public static async Task<LocalDatabase> CreateLocalDatabaseAsync(LocalDatabase db, string path, string operation, bool shouldclose, long pagecachesize)
        {
            var connection = await CreateConnectionAsync(path, pagecachesize);
            db = await CreateLocalDatabaseAsync(db, connection, operation);

            db.ShouldCloseConnection = shouldclose;
            db.m_pagecachesize = pagecachesize;

            return db;
        }

        public static async Task<LocalDatabase> CreateLocalDatabaseAsync(LocalDatabase dbparent, LocalDatabase dbnew)
        {
            dbnew = await CreateLocalDatabaseAsync(dbnew, dbparent.m_connection);

            dbnew.OperationTimestamp = dbparent.OperationTimestamp;
            dbnew.m_connection = dbparent.m_connection;
            dbnew.m_operationid = dbparent.m_operationid;
            dbnew.m_pagecachesize = dbparent.m_pagecachesize;

            return dbnew;
        }

        public static async Task<LocalDatabase> CreateLocalDatabaseAsync(LocalDatabase db, SqliteConnection connection, string operation)
        {
            db = await CreateLocalDatabaseAsync(db, connection);

            db.OperationTimestamp = DateTime.UtcNow;

            if (db.m_connection.State != ConnectionState.Open)
                await db.m_connection.OpenAsync();

            using var cmd = db.m_connection.CreateCommand();
            using var transaction = db.m_connection.BeginTransaction();
            cmd.Transaction = transaction;
            if (operation != null)
            {
                db.m_operationid = await cmd.SetCommandAndParameters(@"
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
                    .SetParameterValue("@Timestamp", db.OperationTimestamp)
                    .ExecuteScalarInt64Async(-1);
            }
            else
            {
                // Get last operation
                using var rd = await cmd.ExecuteReaderAsync(@"
                    SELECT
                        ""ID"",
                        ""Timestamp""
                    FROM ""Operation""
                    ORDER BY ""Timestamp""
                    DESC LIMIT 1
                ");

                if (!await rd.ReadAsync())
                    throw new Exception("LocalDatabase does not contain a previous operation.");

                db.m_operationid = rd.ConvertValueToInt64(0);
                db.OperationTimestamp = ParseFromEpochSeconds(rd.ConvertValueToInt64(1));
            }

            return db;
        }

        private static async Task<LocalDatabase> CreateLocalDatabaseAsync(LocalDatabase db, SqliteConnection connection)
        {
            db.m_connection = connection;

            var selectremotevolumes_sql = @"
                SELECT
                    ""ID"",
                    ""Name"",
                    ""Type"",
                    ""Size"",
                    ""Hash"",
                    ""State"",
                    ""DeleteGraceTime"",
                    ""ArchiveTime""
                FROM ""Remotevolume""
            ";

            db.m_insertlogCommand = await connection.CreateCommandAsync(@"
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
            ");

            db.m_insertremotelogCommand = await connection.CreateCommandAsync(@"
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
            ");

            db.m_updateremotevolumeCommand = await connection.CreateCommandAsync(@"
                UPDATE ""Remotevolume""
                SET
                    ""OperationID"" = @OperationID,
                    ""State"" = @State,
                    ""Hash"" = @Hash,
                    ""Size"" = @Size
                WHERE ""Name"" = @Name
            ");

            db.m_selectremotevolumesCommand = await connection.CreateCommandAsync(selectremotevolumes_sql);

            db.m_selectremotevolumeCommand = await connection.CreateCommandAsync(@$"
                {selectremotevolumes_sql}
                WHERE ""Name"" = @Name
            ");

            db.m_selectduplicateRemoteVolumesCommand = await connection.CreateCommandAsync($@"
                SELECT DISTINCT ""Name"", ""State""
                FROM ""Remotevolume""
                WHERE
                    ""Name"" IN (
                        SELECT ""Name""
                        FROM ""Remotevolume""
                        WHERE ""State"" IN ('{RemoteVolumeState.Deleted}', '{RemoteVolumeState.Deleting}')
                    )
                    AND NOT ""State"" IN ('{RemoteVolumeState.Deleted}', '{RemoteVolumeState.Deleting}')
            ");

            db.m_removeremotevolumeCommand = await connection.CreateCommandAsync(@"
                DELETE FROM ""Remotevolume""
                WHERE
                    ""Name"" = @Name
                    AND (
                        ""DeleteGraceTime"" < @Now
                        OR ""State"" != @State
                    )
            ");

            // >12 is to handle removal of old records that were in ticks
            db.m_removedeletedremotevolumeCommand = await connection.CreateCommandAsync($@"
                DELETE FROM ""Remotevolume""
                WHERE
                    ""State"" == '{RemoteVolumeState.Deleted}'
                    AND (
                        ""DeleteGraceTime"" < @Now
                        OR LENGTH(""DeleteGraceTime"") > 12
                    )
            ");

            db.m_selectremotevolumeIdCommand = await connection.CreateCommandAsync(@"
                SELECT ""ID""
                FROM ""Remotevolume""
                WHERE ""Name"" = @Name
            ");

            db.m_createremotevolumeCommand = await connection.CreateCommandAsync(@"
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
            ");

            db.m_insertIndexBlockLink = await connection.CreateCommandAsync(@"
                INSERT INTO ""IndexBlockLink"" (
                    ""IndexVolumeID"",
                    ""BlockVolumeID""
                )
                VALUES (
                    @IndexVolumeId,
                    @BlockVolumeId
                )
            ");

            db.m_findpathprefixCommand = await connection.CreateCommandAsync(@"
                SELECT ""ID""
                FROM ""PathPrefix""
                WHERE ""Prefix"" = @Prefix
            ");

            db.m_insertpathprefixCommand = await connection.CreateCommandAsync(@"
                INSERT INTO ""PathPrefix"" (""Prefix"")
                VALUES (@Prefix);
                SELECT last_insert_rowid();
            ");

            return db;
        }

        /// <summary>
        /// TODO Remove from here and call the utility function directly - or maybe rename this so that the utility function makes more sense?
        /// Creates a DateTime instance by adding the specified number of seconds to the EPOCH value
        /// </summary>
        public static DateTime ParseFromEpochSeconds(long seconds)
        {
            return Library.Utility.Utility.EPOCH.AddSeconds(seconds);
        }

        public async Task UpdateRemoteVolume(string name, RemoteVolumeState state, long size, string? hash)
        {
            await UpdateRemoteVolume(name, state, size, hash, false);
        }

        public async Task UpdateRemoteVolume(string name, RemoteVolumeState state, long size, string? hash, bool suppressCleanup)
        {
            await UpdateRemoteVolume(name, state, size, hash, suppressCleanup, new TimeSpan(0), null);
        }

        public async Task UpdateRemoteVolume(string name, RemoteVolumeState state, long size, string? hash, bool suppressCleanup, TimeSpan deleteGraceTime, bool? setArchived)
        {
            var c = await m_updateremotevolumeCommand.SetTransaction(m_rtr)
                .SetParameterValue("@OperationID", m_operationid)
                .SetParameterValue("@State", state.ToString())
                .SetParameterValue("@Hash", hash)
                .SetParameterValue("@Size", size)
                .SetParameterValue("@Name", name)
                .ExecuteNonQueryAsync();

            if (c != 1)
            {
                throw new Exception($"Unexpected number of remote volumes detected: {c}!");
            }

            if (deleteGraceTime.Ticks > 0)
            {
                using var cmd = await m_connection.CreateCommandAsync(@"
                    UPDATE ""RemoteVolume""
                    SET ""DeleteGraceTime"" = @DeleteGraceTime
                    WHERE ""Name"" = @Name
                ");
                c = await cmd.SetTransaction(m_rtr)
                    .SetParameterValue("@DeleteGraceTime", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow + deleteGraceTime))
                    .SetParameterValue("@Name", name)
                    .ExecuteNonQueryAsync();

                if (c != 1)
                    throw new Exception($"Unexpected number of updates when recording remote volume updates: {c}!");
            }

            if (setArchived.HasValue)
            {
                using var cmd = await m_connection.CreateCommandAsync(@"
                    UPDATE ""RemoteVolume""
                    SET ""ArchiveTime"" = @ArchiveTime
                    WHERE ""Name"" = @Name
                ");
                c = await cmd.SetTransaction(m_rtr)
                    .SetParameterValue("@ArchiveTime", setArchived.Value ? Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow) : 0)
                    .SetParameterValue("@Name", name)
                    .ExecuteNonQueryAsync();

                if (c != 1)
                    throw new Exception($"Unexpected number of updates when recording remote volume archive-time updates: {c}!");
            }

            if (!suppressCleanup && state == RemoteVolumeState.Deleted)
            {
                await RemoveRemoteVolume(name);
            }
        }

        public async IAsyncEnumerable<KeyValuePair<long, DateTime>> FilesetTimes()
        {
            using var cmd = await m_connection.CreateCommandAsync(@"
                SELECT ""ID"", ""Timestamp""
                FROM ""Fileset""
                ORDER BY ""Timestamp"" DESC
            ");
            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                yield return new KeyValuePair<long, DateTime>(rd.ConvertValueToInt64(0), ParseFromEpochSeconds(rd.ConvertValueToInt64(1)).ToLocalTime());
        }

        public async Task<(string Query, Dictionary<string, object?> Values)> GetFilelistWhereClause(DateTime time, long[] versions, IEnumerable<KeyValuePair<long, DateTime>>? filesetslist = null, bool singleTimeMatch = false)
        {
            KeyValuePair<long, DateTime>[] filesets;
            if (filesetslist != null)
                filesets = [.. filesetslist];
            else
                filesets = await FilesetTimes().ToArrayAsync();
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
                            var argName = "@Fileset" + v;
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

        public async Task<long> GetRemoteVolumeID(string file)
        {
            return await m_selectremotevolumeIdCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@Name", file)
                .ExecuteScalarInt64Async(-1);
        }

        public async IAsyncEnumerable<KeyValuePair<string, long>> GetRemoteVolumeIDs(IEnumerable<string> files, SqliteTransaction transaction)
        {
            using var cmd = await m_connection.CreateCommandAsync(@"
                SELECT
                    ""Name"",
                    ""ID""
                FROM ""RemoteVolume""
                WHERE ""Name"" IN (@Name)
            ");
            cmd.SetTransaction(transaction);
            using var tmptable = await TemporaryDbValueList.CreateAsync(this, files);
            await cmd.ExpandInClauseParameterAsync("@Name", tmptable);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                yield return new KeyValuePair<string, long>(rd.ConvertValueToString(0) ?? "", rd.ConvertValueToInt64(1));
        }

        public async Task<RemoteVolumeEntry> GetRemoteVolume(string file)
        {
            m_selectremotevolumeCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@Name", file);

            using (var rd = await m_selectremotevolumeCommand.ExecuteReaderAsync())
                if (await rd.ReadAsync())
                    return new RemoteVolumeEntry(
                        rd.ConvertValueToInt64(0),
                        rd.ConvertValueToString(1),
                        rd.ConvertValueToString(4),
                        rd.ConvertValueToInt64(3, -1),
                        (RemoteVolumeType)Enum.Parse(typeof(RemoteVolumeType), rd.ConvertValueToString(2) ?? ""),
                        (RemoteVolumeState)Enum.Parse(typeof(RemoteVolumeState), rd.ConvertValueToString(5) ?? ""),
                        ParseFromEpochSeconds(rd.ConvertValueToInt64(6, 0)),
                        ParseFromEpochSeconds(rd.ConvertValueToInt64(7, 0))
                    );

            return RemoteVolumeEntry.Empty;
        }

        public async IAsyncEnumerable<KeyValuePair<string, RemoteVolumeState>> DuplicateRemoteVolumes(SqliteTransaction transaction)
        {
            m_selectduplicateRemoteVolumesCommand.SetTransaction(transaction);

            await foreach (var rd in m_selectduplicateRemoteVolumesCommand.ExecuteReaderEnumerableAsync())
            {
                yield return new KeyValuePair<string, RemoteVolumeState>(
                    rd.ConvertValueToString(0) ?? throw new Exception("Name was null"),
                    (RemoteVolumeState)Enum.Parse(typeof(RemoteVolumeState), rd.ConvertValueToString(1) ?? "")
                );
            }
        }

        public async IAsyncEnumerable<RemoteVolumeEntry> GetRemoteVolumes(SqliteTransaction transaction)
        {
            m_selectremotevolumesCommand.SetTransaction(transaction);
            using var rd = await m_selectremotevolumesCommand.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                yield return new RemoteVolumeEntry(
                    rd.ConvertValueToInt64(0),
                    rd.ConvertValueToString(1),
                    rd.ConvertValueToString(4),
                    rd.ConvertValueToInt64(3, -1),
                    (RemoteVolumeType)Enum.Parse(typeof(RemoteVolumeType), rd.ConvertValueToString(2) ?? ""),
                    (RemoteVolumeState)Enum.Parse(typeof(RemoteVolumeState), rd.ConvertValueToString(5) ?? ""),
                    ParseFromEpochSeconds(rd.ConvertValueToInt64(6, 0)),
                    ParseFromEpochSeconds(rd.ConvertValueToInt64(7, 0))
                );
            }
        }

        /// <summary>
        /// Log an operation performed on the remote backend
        /// </summary>
        /// <param name="operation">The operation performed</param>
        /// <param name="path">The path involved</param>
        /// <param name="data">Any data relating to the operation</param>
        public async Task LogRemoteOperation(string operation, string path, string? data, SqliteTransaction transaction)
        {
            await m_insertremotelogCommand
                .SetTransaction(transaction)
                .SetParameterValue("@OperationID", m_operationid)
                .SetParameterValue("@Timestamp", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow))
                .SetParameterValue("@Operation", operation)
                .SetParameterValue("@Path", path)
                .SetParameterValue("@Data", data)
                .ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Log a debug message
        /// </summary>
        /// <param name="type">The message type</param>
        /// <param name="message">The message</param>
        /// <param name="exception">An optional exception</param>
        public async Task LogMessage(string type, string message, Exception? exception)
        {
            await m_insertlogCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@OperationID", m_operationid)
                .SetParameterValue("@Timestamp", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow))
                .SetParameterValue("@Type", type)
                .SetParameterValue("@Message", message)
                .SetParameterValue("@Exception", exception?.ToString())
                .ExecuteNonQueryAsync();
        }

        public async Task UnlinkRemoteVolume(string name, RemoteVolumeState state, SqliteTransaction transaction)
        {
            using var cmd = await m_connection.CreateCommandAsync(@"
                DELETE FROM ""RemoteVolume""
                WHERE
                    ""Name"" = @Name
                    AND ""State"" = @State
            ");
            var c = await cmd.SetTransaction(transaction)
                .SetParameterValue("@Name", name)
                .SetParameterValue("@State", state.ToString())
                .ExecuteNonQueryAsync();

            if (c != 1)
                throw new Exception($"Unexpected number of remote volumes deleted: {c}, expected {1}");

            await transaction.CommitAsync();
        }

        public async Task RemoveRemoteVolume(string name)
        {
            await RemoveRemoteVolumes([name]);
        }

        public async Task RemoveRemoteVolumes(IEnumerable<string> names)
        {
            if (names == null || !names.Any()) return;

            using var deletecmd = m_connection.CreateCommand(m_rtr);
            string temptransguid = Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
            var volidstable = "DelVolSetIds-" + temptransguid;
            var blocksetidstable = "DelBlockSetIds-" + temptransguid;
            var filesetidstable = "DelFilesetIds-" + temptransguid;

            // Create and fill a temp table with the volids to delete. We avoid using too many parameters that way.
            await deletecmd.ExecuteNonQueryAsync($@"
                CREATE TEMP TABLE ""{volidstable}"" (
                    ""ID"" INTEGER PRIMARY KEY
                )
            ");
            await deletecmd.SetCommandAndParameters($@"
                INSERT OR IGNORE INTO ""{volidstable}""
                SELECT ""ID""
                FROM ""RemoteVolume""
                WHERE ""Name"" IN (@VolumeNames)
            ")
                .ExpandInClauseParameter("@VolumeNames", [.. names])
                .ExecuteNonQueryAsync();

            var volIdsSubQuery = $@"SELECT ""ID"" FROM ""{volidstable}"" ";
            deletecmd.Parameters.Clear();

            var bsIdsSubQuery = @$"
                SELECT DISTINCT ""BlocksetEntry"".""BlocksetID""
                FROM ""BlocksetEntry"", ""Block""
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
            ");
            await deletecmd.ExecuteNonQueryAsync($@"
                INSERT OR IGNORE INTO ""{blocksetidstable}"" (""ID"")
                {bsIdsSubQuery}
            ");
            bsIdsSubQuery = $@"
                SELECT DISTINCT ""ID""
                FROM ""{blocksetidstable}""
            ";
            deletecmd.Parameters.Clear();

            // Create a temp table to associate metadata that is being deleted to a fileset
            var metadataFilesetQuery = $@"
                SELECT Metadataset.ID, FilesetEntry.FilesetD
                FROM Metadataset
                INNER JOIN FileLookup
                    ON FileLookup.MetadataID = Metadataset.ID
                INNER JOIN FilesetEntry
                    ON FilesetEntry.FileID = FileLookup.ID
                WHERE Metadataset.BlocksetID IN ({bsIdsSubQuery})
                OR Metadataset.ID IN (
                    SELECT MetadataID
                    FROM FileLookup
                    WHERE BlocksetID IN ({bsIdsSubQuery})
                )
            ";

            var metadataFilesetTable = $"DelMetadataFilesetIds-{temptransguid}";
            await deletecmd.ExecuteNonQueryAsync($@"
                CREATE TEMP TABLE ""{metadataFilesetTable}"" (
                    MetadataID INTEGER PRIMARY KEY,
                    FilesetID INTEGER
                )
            ");
            await deletecmd.ExecuteNonQueryAsync($@"
                INSERT OR IGNORE INTO ""{metadataFilesetTable}"" (
                    MetadataID,
                    FilesetID
                )
                {metadataFilesetQuery}
            ");

            // Delete FilesetEntry rows that had their metadata deleted
            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM FilesetEntry
                WHERE
                    FilesetEntry.FilesetID IN (
                        SELECT DISTINCT FilesetID FROM ""{metadataFilesetTable}""
                    )
                    AND FilesetEntry.FileID IN (
                        SELECT FilesetEntry.FileID
                        FROM FilesetEntry
                        INNER JOIN FileLookup
                            ON FileLookup.ID = FilesetEntry.FileID
                        WHERE FileLookup.MetadataID IN (
                            SELECT MetadataID FROM ""{metadataFilesetTable}""
                        )
                    )
            ");

            // Delete FilesetEntry rows that had their blocks deleted
            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM FilesetEntry
                WHERE FilesetEntry.FileID IN (
                    SELECT ID
                    FROM FileLookup
                    WHERE FileLookup.BlocksetID IN ({bsIdsSubQuery})
                )
            ");
            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM FileLookup
                WHERE FileLookup.MetadataID IN (
                    SELECT MetadataID
                    FROM ""{metadataFilesetTable}""
                )
            ");

            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""Metadataset""
                WHERE ""BlocksetID"" IN ({bsIdsSubQuery})
            ");
            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""FileLookup""
                WHERE ""BlocksetID"" IN ({bsIdsSubQuery})
            ");
            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""Blockset""
                WHERE ""ID"" IN ({bsIdsSubQuery})
            ");
            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""BlocksetEntry""
                WHERE ""BlocksetID"" IN ({bsIdsSubQuery})
            ");
            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""BlocklistHash""
                WHERE ""BlocklistHash"".""BlocksetID"" IN ({bsIdsSubQuery})
            ");

            // If the volume is a block or index volume, this will update the crosslink table, otherwise nothing will happen
            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""IndexBlockLink""
                WHERE ""BlockVolumeID"" IN ({volIdsSubQuery})
                OR ""IndexVolumeID"" IN ({volIdsSubQuery})
            ");
            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""Block""
                WHERE ""VolumeID"" IN ({volIdsSubQuery})
            ");
            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""DeletedBlock""
                WHERE ""VolumeID"" IN ({volIdsSubQuery})
            ");
            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""ChangeJournalData""
                WHERE ""FilesetID"" IN (
                    SELECT ""ID""
                    FROM ""Fileset""
                    WHERE ""VolumeID"" IN ({volIdsSubQuery})
                )
            ");
            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM FilesetEntry
                WHERE FilesetID IN (
                    SELECT ID
                    FROM Fileset
                    WHERE VolumeID IN ({volIdsSubQuery})
                )
            ");

            await deletecmd.ExecuteNonQueryAsync($@"
                CREATE TABLE ""{filesetidstable}"" (
                    ""ID"" INTEGER PRIMARY KEY
                )
            ");
            await deletecmd.ExecuteNonQueryAsync($@"
                INSERT OR IGNORE INTO ""{filesetidstable}""
                SELECT ""ID""
                FROM ""Fileset""
                WHERE ""VolumeID"" IN ({volIdsSubQuery})
            ");
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
                        FROM FilesetEntry
                    )
            ");

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
                .ExpandInClauseParameter("@AllowedStates", [
                        RemoteVolumeState.Uploading.ToString(),
                        RemoteVolumeState.Uploaded.ToString(),
                        RemoteVolumeState.Verified.ToString(),
                        RemoteVolumeState.Temporary.ToString()
                    ])
                .ExecuteNonQueryAsync();

            await deletecmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""Fileset""
                WHERE ""ID"" IN (
                    SELECT ""ID""
                    FROM ""{filesetidstable}""
                )
            ");

            // Clean up temp tables for subqueries. We truncate content and then try to delete.
            // Drop in try-block, as it fails in nested transactions (SQLite problem)
            // SQLite.SQLiteException (0x80004005): database table is locked
            await deletecmd.ExecuteNonQueryAsync($@"DELETE FROM ""{blocksetidstable}"" ");
            await deletecmd.ExecuteNonQueryAsync($@"DELETE FROM ""{volidstable}"" ");
            await deletecmd.ExecuteNonQueryAsync($@"DELETE FROM ""{metadataFilesetTable}"" ");
            await deletecmd.ExecuteNonQueryAsync($@"DELETE FROM ""{filesetidstable}"" ");
            try
            {
                deletecmd.CommandTimeout = 2;
                await deletecmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{blocksetidstable}"" ");
                await deletecmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{volidstable}"" ");
                await deletecmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{metadataFilesetTable}"" ");
                await deletecmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{filesetidstable}"" ");
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
                    .ExecuteNonQueryAsync();
            }

            // Validate before commiting changes
            var nonAttachedFiles = await deletecmd.ExecuteScalarInt64Async(@"
                SELECT COUNT(*)
                FROM ""FilesetEntry""
                WHERE ""FileID"" NOT IN (
                    SELECT ""ID"" FROM ""FileLookup""
                )
            ");
            if (nonAttachedFiles > 0)
                throw new ConstraintException($"Detected {nonAttachedFiles} file(s) in FilesetEntry without corresponding FileLookup entry");

            await m_rtr.CommitAsync();
        }

        public async Task Vacuum()
        {
            m_hasExecutedVacuum = true;
            using var cmd = m_connection.CreateCommand();

            await cmd.ExecuteNonQueryAsync("VACUUM");
        }

        public async Task<long> RegisterRemoteVolume(string name, RemoteVolumeType type, long size, RemoteVolumeState state)
        {
            var transaction = m_connection.BeginTransaction();
            return await RegisterRemoteVolume(name, type, state, size, new TimeSpan(0), transaction);
        }

        public async Task<long> RegisterRemoteVolume(string name, RemoteVolumeType type, RemoteVolumeState state, SqliteTransaction transaction)
        {
            return await RegisterRemoteVolume(name, type, state, new TimeSpan(0), transaction);
        }

        public async Task<long> RegisterRemoteVolume(string name, RemoteVolumeType type, RemoteVolumeState state, TimeSpan deleteGraceTime, SqliteTransaction transaction)
        {
            return await RegisterRemoteVolume(name, type, state, -1, deleteGraceTime, transaction);
        }

        public async Task<long> RegisterRemoteVolume(string name, RemoteVolumeType type, RemoteVolumeState state, long size, TimeSpan deleteGraceTime, SqliteTransaction transaction)
        {
            var r = await m_createremotevolumeCommand
                .SetTransaction(transaction)
                .SetParameterValue("@OperationId", m_operationid)
                .SetParameterValue("@Name", name)
                .SetParameterValue("@Type", type.ToString())
                .SetParameterValue("@State", state.ToString())
                .SetParameterValue("@Size", size)
                .SetParameterValue("@VerificationCount", 0)
                .SetParameterValue("@DeleteGraceTime", deleteGraceTime.Ticks <= 0 ? 0 : (DateTime.UtcNow + deleteGraceTime).Ticks)
                .SetParameterValue("@ArchiveTime", 0)
                .ExecuteScalarInt64Async();

            await transaction.CommitAsync();

            return r;
        }

        public async Task<IEnumerable<long>> GetFilesetIDs(DateTime restoretime, long[] versions, bool singleTimeMatch = false)
        {
            if (restoretime.Kind == DateTimeKind.Unspecified)
                throw new Exception("Invalid DateTime given, must be either local or UTC");

            (var wherequery, var values) = await GetFilelistWhereClause(restoretime, versions, singleTimeMatch: singleTimeMatch);
            var res = new List<long>();
            using var cmd = m_connection.CreateCommand();
            cmd.SetCommandAndParameters($@"
                SELECT ""ID""
                FROM ""Fileset""
                {wherequery}
                ORDER BY ""Timestamp"" DESC
            ")
                .SetParameterValues(values);
            using (var rd = await cmd.ExecuteReaderAsync())
                while (await rd.ReadAsync())
                    res.Add(rd.ConvertValueToInt64(0));

            if (res.Count == 0)
            {
                cmd.SetCommandAndParameters(@"
                    SELECT ""ID""
                    FROM ""Fileset""
                    ORDER BY ""Timestamp"" DESC
                ");
                using (var rd = await cmd.ExecuteReaderAsync())
                    while (await rd.ReadAsync())
                        res.Add(rd.ConvertValueToInt64(0));

                if (res.Count == 0)
                    throw new Duplicati.Library.Interface.UserInformationException("No backup at the specified date", "NoBackupAtDate");
                else
                    Logging.Log.WriteWarningMessage(LOGTAG, "RestoreTimeNoMatch", null, "Restore time or version did not match any existing backups, selecting newest backup");
            }

            return res;
        }

        public async Task<IEnumerable<long>> FindMatchingFilesets(DateTime restoretime, long[] versions)
        {
            if (restoretime.Kind == DateTimeKind.Unspecified)
                throw new Exception("Invalid DateTime given, must be either local or UTC");

            var (wherequery, args) = await GetFilelistWhereClause(restoretime, versions, singleTimeMatch: true);

            var res = new List<long>();
            using var cmd = m_connection.CreateCommand();
            cmd.SetCommandAndParameters(@$"
                SELECT ""ID""
                FROM ""Fileset""
                {wherequery}
                ORDER BY ""Timestamp"" DESC
            ")
                .SetParameterValues(args);
            using (var rd = await cmd.ExecuteReaderAsync())
                while (await rd.ReadAsync())
                    res.Add(rd.ConvertValueToInt64(0));

            return res;
        }

        public async Task<bool> IsFilesetFullBackup(DateTime filesetTime, SqliteTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand();
            cmd.SetCommandAndParameters($@"
                SELECT ""IsFullBackup""
                FROM ""Fileset""
                WHERE ""Timestamp"" = @Timestamp
            ")
                .SetTransaction(transaction)
                .SetParameterValue("@Timestamp", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(filesetTime));

            using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return false;
            var isFullBackup = rd.GetInt32(0);
            return isFullBackup == BackupType.FULL_BACKUP;
        }

        private async IAsyncEnumerable<KeyValuePair<string, string>> GetDbOptionList(SqliteTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand(@"
                SELECT
                    ""Key"",
                    ""Value""
                FROM ""Configuration""
            ")
                .SetTransaction(transaction);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                yield return new KeyValuePair<string, string>(rd.ConvertValueToString(0) ?? "", rd.ConvertValueToString(1) ?? "");
        }

        public async Task<IDictionary<string, string>> GetDbOptions(SqliteTransaction transaction)
        {
            var res = await GetDbOptionList(transaction)
                .ToDictionaryAsync(x => x.Key, x => x.Value);

            await transaction.CommitAsync();
            return res;
        }

        /// <summary>
        /// Updates a database option
        /// </summary>
        /// <param name="key">The key to update</param>
        /// <param name="value">The value to set</param>
        private async Task UpdateDbOption(string key, bool value)
        {
            var transaction = m_connection.BeginTransaction();
            var opts = await GetDbOptions(transaction);

            if (value)
                opts[key] = "true";
            else
                opts.Remove(key);

            await SetDbOptions(opts, transaction);
            await transaction.CommitAsync();
        }

        /// <summary>
        /// Flag indicating if a repair is in progress
        /// </summary>
        public async Task<bool> RepairInProgress(SqliteTransaction transaction, bool? value = null)
        {
            if (value is bool v)
            {
                await UpdateDbOption("repair-in-progress", v);
                return v;
            }

            var opts = await GetDbOptions(transaction);
            return opts.ContainsKey("repair-in-progress");
        }

        /// <summary>
        /// Flag indicating if a repair is in progress
        /// </summary>
        public async Task<bool> PartiallyRecreated(SqliteTransaction transaction, bool? value = null)
        {
            if (value is bool v)
            {
                await UpdateDbOption("partially-recreated", v);
                return v;
            }

            var opts = await GetDbOptions(transaction);
            return opts.ContainsKey("partially-recreated");
        }

        /// <summary>
        /// Flag indicating if the database can contain partial uploads
        /// </summary>
        public async Task<bool> TerminatedWithActiveUploads(SqliteTransaction transaction, bool? value = null)
        {
            if (value is bool v)
            {
                await UpdateDbOption("terminated-with-active-uploads", v);
                return v;
            }

            var opts = await GetDbOptions(transaction);
            return opts.ContainsKey("terminated-with-active-uploads");
        }

        /// <summary>
        /// Sets the database options
        /// </summary>
        /// <param name="options">The options to set</param>
        /// <param name="transaction">An optional transaction</param>
        public async Task SetDbOptions(IDictionary<string, string> options, SqliteTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand();
            await cmd.ExecuteNonQueryAsync(@"
                DELETE FROM ""Configuration""
            ");

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
                    .ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }

        public async Task<long> GetBlocksLargerThan(long fhblocksize)
        {
            using var cmd = await m_connection.CreateCommandAsync(@"
                SELECT COUNT(*)
                FROM ""Block""
                WHERE ""Size"" > @Size
            ");

            return await cmd.SetParameterValue("@Size", fhblocksize)
                .ExecuteScalarInt64Async(-1);
        }

        /// <summary>
        /// Verifies the consistency of the database
        /// </summary>
        /// <param name="blocksize">The block size in bytes</param>
        /// <param name="hashsize">The hash size in byts</param>
        /// <param name="verifyfilelists">Also verify filelists (can be slow)</param>
        /// <param name="transaction">The transaction to run in</param>
        public async Task VerifyConsistency(long blocksize, long hashsize, bool verifyfilelists, SqliteTransaction transaction)
            => await VerifyConsistencyInner(blocksize, hashsize, verifyfilelists, false, transaction);

        /// <summary>
        /// Verifies the consistency of the database prior to repair
        /// </summary>
        /// <param name="blocksize">The block size in bytes</param>
        /// <param name="hashsize">The hash size in byts</param>
        /// <param name="verifyfilelists">Also verify filelists (can be slow)</param>
        /// <param name="transaction">The transaction to run in</param>
        public async Task VerifyConsistencyForRepair(long blocksize, long hashsize, bool verifyfilelists, SqliteTransaction transaction)
            => await VerifyConsistencyInner(blocksize, hashsize, verifyfilelists, true, transaction);

        /// <summary>
        /// Verifies the consistency of the database
        /// </summary>
        /// <param name="blocksize">The block size in bytes</param>
        /// <param name="hashsize">The hash size in byts</param>
        /// <param name="verifyfilelists">Also verify filelists (can be slow)</param>
        /// <param name="laxVerifyForRepair">Disable verify for errors that will be fixed by repair</param>
        /// <param name="transaction">The transaction to run in</param>
        private async Task VerifyConsistencyInner(long blocksize, long hashsize, bool verifyfilelists, bool laxVerifyForRepair, SqliteTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand();
            cmd.Transaction = transaction;
            // Calculate the lengths for each blockset
            var combinedLengths = @"
                SELECT
                    ""A"".""ID"" AS ""BlocksetID"",
                    IFNULL(""B"".""CalcLen"", 0) AS ""CalcLen"",
                    ""A"".""Length""
                FROM ""Blockset"" A
                LEFT OUTER JOIN (
                    SELECT
                        ""BlocksetEntry"".""BlocksetID"",
                        SUM(""Block"".""Size"") AS ""CalcLen""
                    FROM ""BlocksetEntry""
                    LEFT OUTER JOIN ""Block""
                    ON ""Block"".""ID"" = ""BlocksetEntry"".""BlockID""
                    GROUP BY ""BlocksetEntry"".""BlocksetID""
                ) B
                    ON ""A"".""ID"" = ""B"".""BlocksetID""
            ";

            // For each blockset with wrong lengths, fetch the file path
            var reportDetails = @$"
                SELECT
                    ""CalcLen"",
                    ""Length"", ""A"".""BlocksetID"",
                    ""File"".""Path""
                FROM ({combinedLengths}) A, ""File""
                WHERE
                    ""A"".""BlocksetID"" = ""File"".""BlocksetID""
                    AND ""A"".""CalcLen"" != ""A"".""Length""
            ";

            using (var rd = await cmd.ExecuteReaderAsync(reportDetails))
                if (await rd.ReadAsync())
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Found inconsistency in the following files while validating database: ");
                    var c = 0;
                    do
                    {
                        if (c < 5)
                            sb.AppendFormat("{0}, actual size {1}, dbsize {2}, blocksetid: {3}{4}", rd.GetValue(3), rd.GetValue(1), rd.GetValue(0), rd.GetValue(2), Environment.NewLine);
                        c++;
                    } while (await rd.ReadAsync());

                    c -= 5;
                    if (c > 0)
                        sb.AppendFormat("... and {0} more", c);

                    sb.Append(". Run repair to fix it.");
                    throw new DatabaseInconsistencyException(sb.ToString());
                }

            var real_count = await cmd.ExecuteScalarInt64Async(@"
                SELECT Count(*)
                FROM ""BlocklistHash""
            ", 0);
            var unique_count = await cmd.ExecuteScalarInt64Async(@"
                SELECT Count(*)
                FROM (
                    SELECT DISTINCT
                        ""BlocksetID"",
                        ""Index""
                    FROM ""BlocklistHash""
                )", 0);

            if (real_count != unique_count)
                throw new DatabaseInconsistencyException($"Found {real_count} blocklist hashes, but there should be {unique_count}. Run repair to fix it.");

            var itemswithnoblocklisthash = await cmd.ExecuteScalarInt64Async($@"
                SELECT COUNT(*)
                FROM (
                    SELECT *
                    FROM (
                        SELECT
                            ""N"".""BlocksetID"",
                            ((""N"".""BlockCount"" + {blocksize / hashsize} - 1) / {blocksize / hashsize}) AS ""BlocklistHashCountExpected"",
                            CASE WHEN ""G"".""BlocklistHashCount"" IS NULL THEN 0 ELSE ""G"".""BlocklistHashCount"" END AS ""BlocklistHashCountActual""
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
            ", 0);
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
            if (await cmd.ExecuteScalarInt64Async() != 0)
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
            if (await cmd.ExecuteScalarInt64Async(0) != 0)
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
                var filesetsMissingVolumes = await cmd.ExecuteScalarInt64Async(0);

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
                        using var reader = await cmd.ExecuteReaderAsync();
                        if (await reader.ReadAsync())
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
                var volumesMissingFilests = await cmd.ExecuteScalarInt64Async(0);
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
                        using var reader = await cmd.ExecuteReaderAsync();
                        if (await reader.ReadAsync())
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
            ");
            if (nonAttachedFiles != 0)
            {
                // Attempt to create a better error message by finding the first 10 fileset ids with the issue
                using var filesetIdReader = await cmd.ExecuteReaderAsync(@"
                    SELECT DISTINCT(FilesetID)
                    FROM ""FilesetEntry""
                    WHERE ""FileID"" NOT IN (
                        SELECT ""ID""
                        FROM ""FileLookup""
                    )
                    LIMIT 11
                ");
                var filesetIds = new HashSet<long>();
                var overflow = false;
                while (await filesetIdReader.ReadAsync())
                {
                    if (filesetIds.Count >= 10)
                    {
                        overflow = true;
                        break;
                    }
                    filesetIds.Add(filesetIdReader.ConvertValueToInt64(0));
                }

                var pairs = FilesetTimes()
                    .Select((x, i) => new { FilesetId = x.Key, Version = i, Time = x.Value })
                    .Where(x => filesetIds.Contains(x.FilesetId))
                    .Select(x => $"Fileset {x.Version}: {x.Time} (id = {x.FilesetId})");

                // Fall back to a generic error message if we can't find the fileset ids
                if (!await pairs.AnyAsync())
                    throw new DatabaseInconsistencyException($"Detected {nonAttachedFiles} file(s) in FilesetEntry without corresponding FileLookup entry");

                if (overflow)
                    pairs = pairs.Append("... and more");

                throw new DatabaseInconsistencyException($"Detected {nonAttachedFiles} file(s) in FilesetEntry without corresponding FileLookup entry in the following filesets:{Environment.NewLine}{string.Join(Environment.NewLine, pairs)}");
            }

            if (verifyfilelists)
            {
                var anyError = new List<string>();
                using (var cmd2 = m_connection.CreateCommand())
                {
                    cmd2.SetTransaction(transaction);
                    cmd.SetCommandAndParameters(@"
                        SELECT ""ID""
                        FROM ""Fileset""
                    ");
                    await foreach (var filesetid in cmd.ExecuteReaderEnumerableAsync().Select(x => x.ConvertValueToInt64(0, -1)))
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
                            .ExecuteScalarInt64Async(0);

                        //var storedfilelist = cmd2.ExecuteScalarInt64(FormatInvariant(@"SELECT COUNT(*) FROM ""FilesetEntry"", ""FileLookup"" WHERE ""FilesetEntry"".""FilesetID"" = @FilesetId AND ""FileLookup"".""ID"" = ""FilesetEntry"".""FileID"" AND ""FileLookup"".""BlocksetID"" != @FolderBlocksetId AND ""FileLookup"".""BlocksetID"" != @SymlinkBlocksetId"), 0, filesetid, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID);

                        var storedlist = await cmd2.SetCommandAndParameters(@"
                            SELECT COUNT(*)
                            FROM ""FilesetEntry""
                            WHERE ""FilesetEntry"".""FilesetID"" = @FilesetId
                        ")
                            .SetParameterValue("@FilesetId", filesetid)
                            .ExecuteScalarInt64Async(0);

                        if (expandedlist != storedlist)
                        {
                            var filesetname = filesetid.ToString();
                            var fileset = await FilesetTimes().Zip(AsyncEnumerable.Range(0, await FilesetTimes().CountAsync()), (a, b) => new Tuple<long, long, DateTime>(b, a.Key, a.Value)).FirstOrDefaultAsync(x => x.Item2 == filesetid);
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

        public interface IBlock
        {
            string Hash { get; }
            long Size { get; }
        }

        internal class Block(string hash, long size) : IBlock
        {
            public string Hash { get; private set; } = hash;
            public long Size { get; private set; } = size;
        }

        public async IAsyncEnumerable<IBlock> GetBlocks(long volumeid, SqliteTransaction transaction)
        {
            using var cmd = await m_connection.CreateCommandAsync(@"
                SELECT DISTINCT
                    ""Hash"",
                    ""Size""
                FROM ""Block""
                WHERE ""VolumeID"" = @VolumeId
            ");
            cmd.SetTransaction(transaction)
                .SetParameterValue("@VolumeId", volumeid);
            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                yield return new Block(rd.ConvertValueToString(0) ?? throw new Exception("Hash is null"), rd.ConvertValueToInt64(1));
        }

        // TODO: Replace this with an enumerable method
        private class BlocklistHashEnumerable : IEnumerable<string>
        {
            private class BlocklistHashEnumerator : IEnumerator<string>
            {
                private readonly SqliteDataReader m_reader;
                private readonly BlocklistHashEnumerable m_parent;
                private string? m_path = null;
                private bool m_first = true;
                private string? m_current = null;

                public BlocklistHashEnumerator(BlocklistHashEnumerable parent, SqliteDataReader reader)
                {
                    m_reader = reader;
                    m_parent = parent;
                }

                public string Current { get { return m_current!; } }

                public void Dispose()
                {
                }

                object System.Collections.IEnumerator.Current { get { return Current; } }

                public bool MoveNext()
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

                        if (!m_reader.ReadAsync().Await())
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

                public void Reset()
                {
                    if (!m_first)
                        throw new Exception("Iterator reset not supported");

                    m_first = false;
                }
            }

            private readonly SqliteDataReader m_reader;

            public BlocklistHashEnumerable(SqliteDataReader reader)
            {
                m_reader = reader;
                MoreData = true;
            }

            public bool MoreData { get; protected set; }

            public IEnumerator<string> GetEnumerator()
            {
                return new BlocklistHashEnumerator(this, m_reader);
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

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
                    FROM ""File"" A
                    LEFT JOIN ""Blockset"" B
                        ON ""A"".""BlocksetID"" = ""B"".""ID""
                    LEFT JOIN ""Metadataset"" C
                        ON ""A"".""MetadataID"" = ""C"".""ID""
                    LEFT JOIN ""FilesetEntry"" D
                        ON ""A"".""ID"" = ""D"".""FileID""
                    LEFT JOIN ""Blockset"" E
                        ON ""E"".""ID"" = ""C"".""BlocksetID""
                    LEFT JOIN ""BlocksetEntry"" G
                        ON ""B"".""ID"" = ""G"".""BlocksetID""
                    LEFT JOIN ""Block"" F
                        ON ""G"".""BlockID"" = ""F"".""ID""
                    LEFT JOIN ""BlocksetEntry"" I
                        ON ""E"".""ID"" = ""I"".""BlocksetID""
                    LEFT JOIN ""Block"" H
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
                ) J
                LEFT OUTER JOIN ""BlocklistHash"" K
                    ON ""K"".""BlocksetID"" = ""J"".""BlocksetID""
                ORDER BY
                    ""J"".""Path"",
                    ""K"".""Index""
            ) L
            LEFT OUTER JOIN ""BlocklistHash"" M
                ON ""M"".""BlocksetID"" = ""L"".""MetablocksetID""
";

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
                    ""FilesetEntry"" A,
                    ""File"" B,
                    ""Metadataset"" C,
                    ""Blockset"" D,
                    ""BlocksetEntry"" E,
                    ""Block"" F
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
            ) G
            LEFT OUTER JOIN ""BlocklistHash"" H
                ON ""H"".""BlocksetID"" = ""G"".""MetaBlocksetID""
            ORDER BY
                ""G"".""Path"",
                ""H"".""Index""
            ";

        public async Task WriteFileset(Volumes.FilesetVolumeWriter filesetvolume, long filesetId, SqliteTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand()
                .SetCommandAndParameters(LIST_FOLDERS_AND_SYMLINKS)
                .SetTransaction(transaction)
                .SetParameterValue("@FilesetId", filesetId)
                .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
                .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID);

            string? lastpath = null;
            using (var rd = await cmd.ExecuteReaderAsync())
                while (await rd.ReadAsync())
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

            using (var rd = await cmd.ExecuteReaderAsync())
                if (await rd.ReadAsync())
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

                        filesetvolume.AddFile(path, filehash, size, lastmodified, metahash, metasize, metablockhash, blockhash, blocksize, blrd, string.IsNullOrWhiteSpace(metablocklisthash) ? null : new string[] { metablocklisthash });
                        if (blrd == null)
                            more = await rd.ReadAsync();
                        else
                            more = blrd.MoreData;

                    } while (more);
                }
        }

        public async Task LinkFilesetToVolume(long filesetid, long volumeid, SqliteTransaction transaction)
        {
            using var cmd = await m_connection.CreateCommandAsync(@"
                UPDATE ""Fileset""
                SET ""VolumeID"" = @VolumeId
                WHERE ""ID"" = @FilesetId
            ");
            var c = await cmd.SetTransaction(transaction)
                .SetParameterValue("@VolumeId", volumeid)
                .SetParameterValue("@FilesetId", filesetid)
                .ExecuteNonQueryAsync();

            if (c != 1)
                throw new Exception($"Failed to link filesetid {filesetid} to volumeid {volumeid}");
        }

        public async Task PushTimestampChangesToPreviousVersion(long filesetId, SqliteTransaction transaction)
        {
            var query = @"
                UPDATE FilesetEntry AS oldVersion
                SET Lastmodified = tempVersion.Lastmodified
                FROM FilesetEntry AS tempVersion
                WHERE
                    oldVersion.FileID = tempVersion.FileID
                    AND tempVersion.FilesetID = @FilesetId
                    AND oldVersion.FilesetID = (
                        SELECT ID
                        FROM Fileset
                        WHERE ID != @FilesetId
                        ORDER BY Timestamp DESC
                        LIMIT 1
                    )
            ";

            using var cmd = await m_connection.CreateCommandAsync(query);
            await cmd.SetTransaction(transaction)
                .SetParameterValue("@FilesetId", filesetId)
                .ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Keeps a list of filenames in a temporary table with a single column Path
        /// </summary>
        public class FilteredFilenameTable : IDisposable
        {
            public string Tablename { get; private set; }
            private readonly SqliteConnection m_connection;

            private FilteredFilenameTable(SqliteConnection connection)
            {
                Tablename = "Filenames-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                m_connection = connection;
            }

            [Obsolete("Calling this constructor will throw an exception. Use the Create method instead.")]
            public FilteredFilenameTable(SqliteConnection connection, IFilter filter, SqliteTransaction transaction)
            {
                throw new NotImplementedException("Use the Create method instead.");
            }

            public async Task<FilteredFilenameTable> CreateFilteredFilenameTableAsync(SqliteConnection connection, IFilter filter, SqliteTransaction transaction)
            {
                var ftt = new FilteredFilenameTable(connection);

                // TODO factory
                var type = FilterType.Regexp;
                if (filter is FilterExpression expression)
                    type = expression.Type;

                // Bugfix: SQLite does not handle case-insensitive LIKE with non-ascii characters
                if (type != FilterType.Regexp && !Library.Utility.Utility.IsFSCaseSensitive && filter.ToString()!.Any(x => x > 127))
                    type = FilterType.Regexp;

                if (filter.Empty)
                {
                    using var cmd = await m_connection.CreateCommandAsync($@"
                        CREATE TEMPORARY TABLE ""{Tablename}"" AS
                        SELECT DISTINCT ""Path""
                        FROM ""File""
                    ");
                    await cmd.SetTransaction(transaction)
                        .ExecuteNonQueryAsync();
                    return ftt;
                }

                if (type == FilterType.Regexp || type == FilterType.Group)
                {
                    // TODO: Optimize this to not rely on the "File" view, and not instantiate the paths in full
                    using var cmd = await m_connection.CreateCommandAsync($@"
                        CREATE TEMPORARY TABLE ""{Tablename}"" (
                            ""Path"" TEXT NOT NULL
                        )
                    ");
                    await cmd.SetTransaction(transaction)
                        .ExecuteNonQueryAsync();

                    cmd.SetCommandAndParameters($@"
                        INSERT INTO ""{Tablename}"" (""Path"")
                        VALUES (@Path)
                    ");
                    using var c2 = await m_connection.CreateCommandAsync(@"
                        SELECT DISTINCT ""Path""
                        FROM ""File""
                    ");
                    c2.SetTransaction(transaction);
                    using (var rd = await c2.ExecuteReaderAsync())
                        while (await rd.ReadAsync())
                        {
                            var p = rd.ConvertValueToString(0);
                            if (FilterExpression.Matches(filter, p))
                            {
                                await cmd.SetParameterValue("@Path", p)
                                    .ExecuteNonQueryAsync();
                            }
                        }

                    await transaction.CommitAsync();
                }
                else
                {
                    var sb = new StringBuilder();
                    var args = new Dictionary<string, object?>();
                    foreach (var f in ((FilterExpression)filter).GetSimpleList())
                    {
                        if (sb.Length != 0)
                            sb.Append(" OR ");

                        var argName = $"@Arg{args.Count}";
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

                    using var cmd = m_connection.CreateCommand();
                    cmd.SetTransaction(transaction);
                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE TEMPORARY TABLE ""{Tablename}"" (
                            ""Path"" TEXT NOT NULL
                        )
                    ");
                    await cmd.ExecuteNonQueryAsync($@"
                        INSERT INTO ""{Tablename}""
                        SELECT DISTINCT ""Path""
                        FROM ""File""
                        WHERE {sb}
                    ", args);
                    await transaction.CommitAsync();
                }

                return ftt;
            }

            public void Dispose()
            {
                DisposeAsync().Await();
            }

            public async Task DisposeAsync()
            {
                if (Tablename != null)
                    try
                    {
                        using var cmd = m_connection.CreateCommand();
                        await cmd.ExecuteNonQueryAsync(@$"
                            DROP TABLE IF EXISTS ""{Tablename}""
                        ");
                    }
                    catch { }
                    finally { Tablename = null!; }
            }
        }

        public async Task RenameRemoteFile(string oldname, string newname, SqliteTransaction transaction)
        {
            //Rename the old entry, to preserve ID links
            using var cmd = await m_connection.CreateCommandAsync(@"
                UPDATE ""Remotevolume""
                SET ""Name"" = @Newname
                WHERE ""Name"" = @Oldname
            ");
            var c = await cmd.SetTransaction(transaction)
                .SetParameterValue("@Newname", newname)
                .SetParameterValue("@Oldname", oldname)
                .ExecuteNonQueryAsync();

            if (c != 1)
                throw new Exception($"Unexpected result from renaming \"{oldname}\" to \"{newname}\", expected {1} got {c}");

            // Grab the type of entry
            cmd.SetCommandAndParameters(@"
                SELECT ""Type""
                FROM ""Remotevolume""
                WHERE ""Name"" = @Name
            ")
                .SetParameterValue("@Name", newname);
            var type = (RemoteVolumeType)Enum.Parse(
                typeof(RemoteVolumeType), (await cmd.ExecuteScalarAsync())?.ToString() ?? "",
                true);

            //Create a fake new entry with the old name and mark as deleting
            // as this ensures we will remove it, if it shows up in some later listing
            await RegisterRemoteVolume(oldname, type, RemoteVolumeState.Deleting, transaction);

            await transaction.CommitAsync();
        }

        /// <summary>
        /// Creates a timestamped backup operation to correctly associate the fileset with the time it was created.
        /// </summary>
        /// <param name="volumeid">The ID of the fileset volume to update</param>
        /// <param name="timestamp">The timestamp of the operation to create</param>
        /// <param name="transaction">An optional external transaction</param>
        public virtual async Task<long> CreateFileset(long volumeid, DateTime timestamp)
        {
            using var cmd = await m_connection.CreateCommandAsync(@"
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
            ");
            var id = await cmd.SetTransaction(m_rtr)
                .SetParameterValue("@OperationId", m_operationid)
                .SetParameterValue("@Timestamp", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(timestamp))
                .SetParameterValue("@VolumeId", volumeid)
                .SetParameterValue("@IsFullBackup", BackupType.PARTIAL_BACKUP)
                .ExecuteScalarInt64Async(-1);

            await m_rtr.CommitAsync();

            return id;
        }

        /// <summary>
        /// Adds a link between an index volume and a block volume.
        /// </summary>
        /// <param name="indexVolumeID">The ID of the index volume.</param>
        /// <param name="blockVolumeID">The ID of the block volume.</param>
        /// <param name="transaction">An optional transaction.</param>
        public async Task AddIndexBlockLink(long indexVolumeID, long blockVolumeID, SqliteTransaction transaction)
        {
            if (indexVolumeID <= 0)
                throw new ArgumentOutOfRangeException(nameof(indexVolumeID), "Index volume ID must be greater than 0.");
            if (blockVolumeID <= 0)
                throw new ArgumentOutOfRangeException(nameof(blockVolumeID), "Block volume ID must be greater than 0.");

            await m_insertIndexBlockLink.SetTransaction(transaction)
                .SetParameterValue("@IndexVolumeId", indexVolumeID)
                .SetParameterValue("@BlockVolumeId", blockVolumeID)
                .ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Returns all unique blocklists for a given volume
        /// </summary>
        /// <param name="volumeid">The volume ID to get blocklists for</param>
        /// <param name="blocksize">The blocksize</param>
        /// <param name="hashsize">The size of the hash</param>
        /// <param name="transaction">An optional external transaction</param>
        /// <returns>An enumerable of tuples containing the blocklist hash, the blocklist data and the length of the data</returns>
        public async IAsyncEnumerable<Tuple<string, byte[], int>> GetBlocklists(long volumeid, long blocksize, int hashsize, SqliteTransaction transaction)
        {
            // Group subquery by hash to ensure that each blocklist hash appears only once in the result
            using var cmd = await m_connection.CreateCommandAsync($@"
                SELECT
                    ""A"".""Hash"",
                    ""C"".""Hash""
                FROM (
                    SELECT
                        ""BlocklistHash"".""BlocksetID"",
                        ""Block"".""Hash"",
                        ""BlocklistHash"".""Index""
                    FROM
                        ""BlocklistHash"",
                        ""Block""
                    WHERE
                        ""BlocklistHash"".""Hash"" = ""Block"".""Hash""
                        AND ""Block"".""VolumeID"" = @VolumeId
                    GROUP BY
                        ""Block"".""Hash"",
                        ""Block"".""Size""
                ) A,
                    ""BlocksetEntry"" B,
                    ""Block"" C
                WHERE
                    ""B"".""BlocksetID"" = ""A"".""BlocksetID""
                    AND ""B"".""Index"" >= (""A"".""Index"" * {blocksize / hashsize})
                    AND ""B"".""Index"" < ((""A"".""Index"" + 1) * {blocksize / hashsize})
                    AND ""C"".""ID"" = ""B"".""BlockID""
                ORDER BY
                    ""A"".""BlocksetID"",
                    ""B"".""Index""
            ");

            string? curHash = null;
            var count = 0;
            var buffer = new byte[blocksize];

            cmd.SetTransaction(transaction)
                .SetParameterValue("@VolumeId", volumeid);
            using (var rd = await cmd.ExecuteReaderAsync())
                while (await rd.ReadAsync())
                {
                    var blockhash = rd.ConvertValueToString(0);
                    if ((blockhash != curHash && curHash != null) || count + hashsize > buffer.Length)
                    {
                        yield return new Tuple<string, byte[], int>(curHash!, buffer, count);
                        buffer = new byte[blocksize];
                        count = 0;
                    }

                    var hash = Convert.FromBase64String(rd.ConvertValueToString(1) ?? throw new Exception("Hash is null"));
                    Array.Copy(hash, 0, buffer, count, hashsize);
                    curHash = blockhash;
                    count += hashsize;
                }

            if (curHash != null)
                yield return new Tuple<string, byte[], int>(curHash, buffer, count);
        }

        /// <summary>
        /// Update fileset with full backup state
        /// </summary>
        /// <param name="fileSetId">Existing file set to update</param>
        /// <param name="isFullBackup">Full backup state</param>
        /// <param name="transaction">An optional external transaction</param>
        public async Task UpdateFullBackupStateInFileset(long fileSetId, bool isFullBackup, SqliteTransaction transaction)
        {
            using var cmd = await m_connection.CreateCommandAsync(@"
                UPDATE ""Fileset""
                SET ""IsFullBackup"" = @IsFullBackup
                WHERE ""ID"" = @FilesetId
            ");
            await cmd.SetTransaction(transaction)
                .SetParameterValue("@FilesetId", fileSetId)
                .SetParameterValue("@IsFullBackup", isFullBackup ? BackupType.FULL_BACKUP : BackupType.PARTIAL_BACKUP)
                .ExecuteNonQueryAsync();

            await transaction.CommitAsync();
        }

        /// <summary>
        /// Removes all entries in the fileset entry table for a given fileset ID
        /// </summary>
        /// <param name="filesetId">The fileset ID to clear</param>
        /// <param name="transaction">The transaction to use</param>
        public async Task ClearFilesetEntries(long filesetId, SqliteTransaction transaction)
        {
            using var cmd = await m_connection.CreateCommandAsync(@"
                DELETE FROM ""FilesetEntry""
                WHERE ""FilesetID"" = @FilesetId
            ");
            await cmd.SetTransaction(transaction)
                .SetParameterValue("@FilesetId", filesetId)
                .ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Gets the last previous fileset that was incomplete
        /// </summary>
        /// <param name="transaction">The transaction to use</param>
        /// <returns>The last incomplete fileset or default</returns>
        public async Task<RemoteVolumeEntry> GetLastIncompleteFilesetVolume()
        {
            var candidates = GetIncompleteFilesets()
                .OrderBy(x => x.Value);

            if (await candidates.AnyAsync())
                return await GetRemoteVolumeFromFilesetID((await candidates.LastAsync()).Key);

            return default;
        }

        /// <summary>
        /// Gets a list of incomplete filesets
        /// </summary>
        /// <param name="transaction">An optional transaction</param>
        /// <returns>A list of fileset IDs and timestamps</returns>
        public async IAsyncEnumerable<KeyValuePair<long, DateTime>> GetIncompleteFilesets()
        {
            using var cmd = await m_connection.CreateCommandAsync(@$"
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
                        ""RemoteVolume"".""State"" = '{RemoteVolumeState.Uploading}'
                        OR ""RemoteVolume"".""State"" = '{RemoteVolumeState.Temporary}'
                    )
            ");

            using var rd = await cmd.SetTransaction(m_rtr)
                .ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                yield return new KeyValuePair<long, DateTime>(
                    rd.ConvertValueToInt64(0),
                    ParseFromEpochSeconds(rd.ConvertValueToInt64(1))
                        .ToLocalTime()
                );
            }
        }

        /// <summary>
        /// Gets the remote volume entry from the fileset ID
        /// </summary>
        /// <param name="filesetID">The fileset ID</param>
        /// <param name="transaction">An optional transaction</param>
        /// <returns>The remote volume entry or default</returns>
        public async Task<RemoteVolumeEntry> GetRemoteVolumeFromFilesetID(long filesetID)
        {
            using var cmd = await m_connection.CreateCommandAsync(@"
                SELECT
                    ""RemoteVolume"".""ID"",
                    ""Name"",
                    ""Type"",
                    ""Size"",
                    ""Hash"",
                    ""State"",
                    ""DeleteGraceTime"",
                    ""ArchiveTime""
                FROM
                    ""RemoteVolume"",
                    ""Fileset""
                WHERE
                    ""Fileset"".""VolumeID"" = ""RemoteVolume"".""ID""
                    AND ""Fileset"".""ID"" = @FilesetId
            ");

            using var rd = await cmd.SetTransaction(m_rtr)
                .SetParameterValue("@FilesetId", filesetID)
                .ExecuteReaderAsync();
            if (await rd.ReadAsync())
                return new RemoteVolumeEntry(
                    rd.ConvertValueToInt64(0, -1),
                    rd.ConvertValueToString(1),
                    rd.ConvertValueToString(4),
                    rd.ConvertValueToInt64(3, -1),
                    (RemoteVolumeType)Enum.Parse(typeof(RemoteVolumeType), rd.ConvertValueToString(2) ?? ""),
                    (RemoteVolumeState)Enum.Parse(typeof(RemoteVolumeState), rd.ConvertValueToString(5) ?? ""),
                    ParseFromEpochSeconds(rd.ConvertValueToInt64(6)).ToLocalTime(),
                    ParseFromEpochSeconds(rd.ConvertValueToInt64(7)).ToLocalTime()
                );
            else
                return default(RemoteVolumeEntry);
        }

        public async Task PurgeLogData(DateTime threshold)
        {
            using var transaction = m_connection.BeginTransaction();
            using var cmd = m_connection.CreateCommand();
            cmd.Transaction = transaction;
            var t = Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(threshold);

            await cmd.SetCommandAndParameters(@"
                DELETE FROM ""LogData""
                WHERE ""Timestamp"" < @Timestamp
            ")
                .SetParameterValue("@Timestamp", t)
                .ExecuteNonQueryAsync();

            await cmd.SetCommandAndParameters(@"
                DELETE FROM ""RemoteOperation""
                WHERE ""Timestamp"" < @Timestamp
            ")
                .SetParameterValue("@Timestamp", t)
                .ExecuteNonQueryAsync();

            await transaction.CommitAsync();
        }

        public async Task PurgeDeletedVolumes(DateTime threshold)
        {
            using var transaction = m_connection.BeginTransaction();

            await m_removedeletedremotevolumeCommand
                .SetTransaction(transaction)
                .SetParameterValue("@Now", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(threshold))
                .ExecuteNonQueryAsync();

            await transaction.CommitAsync();
        }

        public virtual void Dispose()
        {
            DisposeAsync().Await();
        }

        public virtual async Task DisposeAsync()
        {
            if (IsDisposed)
                return;

            DisposeAllFields<SqliteCommand>(this, false);

            if (ShouldCloseConnection && m_connection != null)
            {
                if (m_connection.State == ConnectionState.Open && !m_hasExecutedVacuum)
                {
                    using (var transaction = m_connection.BeginTransaction())
                    using (var command = m_connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        // SQLite recommends that PRAGMA optimize is run just before closing each database connection.
                        await command.ExecuteNonQueryAsync("PRAGMA optimize");

                        try
                        {
                            await transaction.CommitAsync();
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "FailedToCommitTransaction", ex, "Failed to commit transaction after pragma optimize, usually caused by the a no-op transaction");
                        }
                    }

                    await m_connection.CloseAsync();
                }

                await m_connection.DisposeAsync();
            }

            IsDisposed = true;
        }

        /// <summary>
        /// Disposes all fields of a certain type, in the instance and its bases
        /// </summary>
        /// <typeparam name="T">The type of fields to find</typeparam>
        /// <param name="item">The item to dispose</param>
        /// <param name="throwExceptions"><c>True</c> if an aggregate exception should be thrown, or <c>false</c> if exceptions are silently captured</param>
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

        public async Task WriteResults(IBasicResults result)
        {
            if (IsDisposed)
                return;

            if (m_connection != null && result != null)
            {
                if (result is BasicResults basicResults)
                {
                    basicResults.FlushLog(this);
                    if (basicResults.EndTime.Ticks == 0)
                        basicResults.EndTime = DateTime.UtcNow;
                }

                var serializer = new JsonFormatSerializer();
                await LogMessage("Result",
                    serializer.SerializeResults(result),
                    null
                );
            }
        }

        /// <summary>
        /// The current index into the path prefix buffer
        /// </summary>
        private int m_pathPrefixIndex = 0;
        /// <summary>
        /// The path prefix lookup list
        /// </summary>
        private readonly KeyValuePair<string, long>[] m_pathPrefixLookup = new KeyValuePair<string, long>[5];

        /// <summary>
        /// Gets the path prefix ID, optionally creating it in the process.
        /// </summary>
        /// <returns>The path prefix ID.</returns>
        /// <param name="prefix">The path to get the prefix for.</param>
        /// <param name="transaction">The transaction to use for insertion, or null for no transaction</param>
        public async Task<long> GetOrCreatePathPrefix(string prefix)
        {
            // Ring-buffer style lookup
            for (var i = 0; i < m_pathPrefixLookup.Length; i++)
            {
                var ix = (i + m_pathPrefixIndex) % m_pathPrefixLookup.Length;
                if (string.Equals(m_pathPrefixLookup[ix].Key, prefix, StringComparison.Ordinal))
                    return m_pathPrefixLookup[ix].Value;
            }

            var id = await m_findpathprefixCommand.SetTransaction(m_rtr)
                .SetParameterValue("@Prefix", prefix)
                .ExecuteScalarInt64Async();

            if (id < 0)
            {
                id = await m_insertpathprefixCommand.SetTransaction(m_rtr)
                    .SetParameterValue("@Prefix", prefix)
                    .ExecuteScalarInt64Async();
            }

            m_pathPrefixIndex = (m_pathPrefixIndex + 1) % m_pathPrefixLookup.Length;
            m_pathPrefixLookup[m_pathPrefixIndex] = new KeyValuePair<string, long>(prefix, id);

            return id;
        }

        /// <summary>
        /// The path separators on this system
        /// </summary>
        private static readonly char[] _pathseparators = [
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar,
        ];

        /// <summary>
        /// Helper method that splits a path on the last path separator
        /// </summary>
        /// <returns>The prefix and name.</returns>
        /// <param name="path">The path to split.</param>
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
    /// Defines the backups types
    /// </summary>
    public static class BackupType
    {
        public const int PARTIAL_BACKUP = 0;
        public const int FULL_BACKUP = 1;
    }
}
