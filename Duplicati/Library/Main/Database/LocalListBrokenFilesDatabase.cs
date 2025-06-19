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
    /// A database for listing broken files, i.e., files that reference blocksets or blocks that are not available.
    /// </summary>
    internal class LocalListBrokenFilesDatabase : LocalDatabase
    {
        /// <summary>
        /// SQL query to get the IDs of all block volumes.
        /// </summary>
        private static readonly string BLOCK_VOLUME_IDS = $@"
            SELECT ""ID""
            FROM ""RemoteVolume""
            WHERE ""Type"" = '{RemoteVolumeType.Blocks}'
        ";

        // Invalid blocksets include those that:
        // - Have BlocksetEntries with unknown/invalid blocks (meaning the data to rebuild the blockset isn't available)
        //   - Invalid blocks include those that appear to be in non-Blocks volumes (e.g., are listed as being in an Index or Files volume) or that appear in an unknown volume (-1)
        // - Have BlocklistHash entries with unknown/invalid blocks (meaning the data which defines the list of hashes that makes up the blockset isn't available)
        // - Are defined in the Blockset table but have no entries in the BlocksetEntries table (this can happen during recreate if Files volumes reference blocksets that are not found in any Index files)
        // However, blocksets with a length of 0 are excluded from this check, as the corresponding blocks for these are not needed.

        /// <summary>
        /// SQL query to get the IDs of broken files.
        /// A broken file is one that has a BlocksetID that is not in the list of valid blocksets, or that has a MetadataID that is not in the list of valid metadata blocksets.
        /// </summary>
        private static readonly string BROKEN_FILE_IDS = $@"
            SELECT DISTINCT ""ID""
            FROM (
                SELECT
                    ""ID"" AS ""ID"",
                    ""BlocksetID"" AS ""BlocksetID""
                FROM ""FileLookup""
                WHERE
                    ""BlocksetID"" != {FOLDER_BLOCKSET_ID}
                    AND ""BlocksetID"" != {SYMLINK_BLOCKSET_ID}
                UNION
                    SELECT
                        ""A"".""ID"" AS ""ID"",
                        ""B"".""BlocksetID"" AS ""BlocksetID""
                    FROM ""FileLookup"" ""A""
                    LEFT JOIN ""Metadataset"" ""B""
                        ON ""A"".""MetadataID"" = ""B"".""ID""
            )
            WHERE
                ""BlocksetID"" IS NULL
                OR ""BlocksetID"" IN (
                    SELECT DISTINCT ""BlocksetID""
                    FROM (
                        SELECT ""BlocksetID""
                        FROM ""BlocksetEntry""
                        WHERE ""BlockID"" NOT IN (
                            SELECT ""ID""
                            FROM ""Block""
                            WHERE ""VolumeID"" IN ({BLOCK_VOLUME_IDS})
                        )
                        UNION
                            SELECT ""BlocksetID""
                            FROM ""BlocklistHash""
                            WHERE ""Hash"" NOT IN (
                                SELECT ""Hash""
                                FROM ""Block""
                                WHERE ""VolumeID"" IN ({BLOCK_VOLUME_IDS})
                            )
                        UNION
                            SELECT ""A"".""ID"" AS ""BlocksetID""
                            FROM ""Blockset"" ""A""
                            LEFT JOIN ""BlocksetEntry"" ""B""
                                ON ""A"".""ID"" = ""B"".""BlocksetID""
                            WHERE
                                ""A"".""Length"" > 0
                                AND ""B"".""BlocksetID"" IS NULL
                    )
                    WHERE ""BlocksetID"" NOT IN (
                        SELECT ""ID""
                        FROM ""Blockset""
                        WHERE ""Length"" == 0
                    )
                )
        ";

        /// <summary>
        /// SQL query to get the broken filesets, i.e., filesets that contain broken files.
        /// </summary>
        private static readonly string BROKEN_FILE_SETS = $@"
            SELECT DISTINCT
                ""B"".""Timestamp"",
                ""A"".""FilesetID"",
                COUNT(""A"".""FileID"") AS ""FileCount""
            FROM
                ""FilesetEntry"" ""A"",
                ""Fileset"" ""B""
            WHERE
                ""A"".""FilesetID"" = ""B"".""ID""
                AND ""A"".""FileID"" IN ({BROKEN_FILE_IDS})
        ";

        /// <summary>
        /// SQL query to get the names and lengths of broken files.
        /// </summary>
        private static readonly string BROKEN_FILE_NAMES = $@"
            SELECT
                ""A"".""Path"",
                ""B"".""Length""
            FROM ""File"" ""A""
            LEFT JOIN ""Blockset"" ""B""
                ON (""A"".""BlocksetID"" = ""B"".""ID"")
            WHERE
                ""A"".""ID"" IN ({BROKEN_FILE_IDS})
                AND ""A"".""ID"" IN (
                    SELECT ""FileID""
                    FROM ""FilesetEntry""
                    WHERE ""FilesetID"" = @FilesetId
                )
        ";

        /// <summary>
        /// SQL query to insert broken file IDs into a specified table.
        /// The table must have a single column with the same name as the ID field name.
        /// </summary>
        private static string INSERT_BROKEN_IDS(string tablename, string IDfieldname) => $@"
            INSERT INTO ""{tablename}"" (
                ""{IDfieldname}""
            )
            {BROKEN_FILE_IDS}
            AND ""ID"" IN (
                SELECT ""FileID""
                FROM ""FilesetEntry""
                WHERE ""FilesetID"" = @FilesetId
            )
        ";

        /// <summary>
        /// Creates a new instance of the <see cref="LocalListBrokenFilesDatabase"/> class.
        /// </summary>
        /// <param name="path">The path to the database file.</param>
        /// <param name="dbnew">An optional existing database instance to use. Used to mimic constructor chaining.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains a new instance of <see cref="LocalListBrokenFilesDatabase"/>.</returns>
        public static async Task<LocalListBrokenFilesDatabase> CreateAsync(string path, LocalListBrokenFilesDatabase? dbnew, CancellationToken token)
        {
            dbnew ??= new LocalListBrokenFilesDatabase();

            dbnew = (LocalListBrokenFilesDatabase)
                await CreateLocalDatabaseAsync(path, "ListBrokenFiles", false, dbnew, token)
                    .ConfigureAwait(false);
            dbnew.ShouldCloseConnection = true;

            return dbnew;
        }

        /// <summary>
        /// Creates a new instance of the <see cref="LocalListBrokenFilesDatabase"/> class.
        /// </summary>
        /// <param name="dbparent">The parent database to use for the new database.</param>
        /// <param name="dbnew">An optional existing database instance to use. Used to mimic constructor chaining.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains a new instance of <see cref="LocalListBrokenFilesDatabase"/>.</returns>
        public static async Task<LocalListBrokenFilesDatabase> CreateAsync(LocalDatabase dbparent, LocalListBrokenFilesDatabase? dbnew, CancellationToken token)
        {
            dbnew ??= new LocalListBrokenFilesDatabase();

            dbnew = (LocalListBrokenFilesDatabase)
                await CreateLocalDatabaseAsync(dbparent, dbnew, token)
                    .ConfigureAwait(false);
            dbnew.ShouldCloseConnection = false;

            return dbnew;
        }

        /// <summary>
        /// Returns all broken file IDs, i.e., files that reference blocksets or blocks that are not available.
        /// </summary>
        /// <param name="time">The time to filter filesets by.</param>
        /// <param name="versions">Optional array of versions to filter filesets by. If null, all versions are considered.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>An asynchronous enumerable of broken file IDs.</returns>
        public async IAsyncEnumerable<(DateTime FilesetTime, long FilesetID, long RemoveFileCount)> GetBrokenFilesets(DateTime time, long[]? versions, [EnumeratorCancellation] CancellationToken token)
        {
            var query = BROKEN_FILE_SETS;
            var clause = await GetFilelistWhereClause(time, versions, null, false, token)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(clause.Query))
                query += $@"
                    AND ""A"".""FilesetID"" IN (
                        SELECT ""ID""
                        FROM ""Fileset""
                        {clause.Query}
                    )
                ";

            query += @" GROUP BY ""A"".""FilesetID""";

            await using var cmd = Connection.CreateCommand(m_rtr)
                .SetCommandAndParameters(query)
                .SetParameterValues(clause.Values);

            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync(token).ConfigureAwait(false))
                if (!rd.IsDBNull(0))
                    yield return (
                        ParseFromEpochSeconds(rd.ConvertValueToInt64(0, 0)),
                        rd.ConvertValueToInt64(1, -1),
                        rd.ConvertValueToInt64(2, 0)
                    );
        }

        /// <summary>
        /// Returns all broken file IDs, i.e., files that reference blocksets or blocks that are not available.
        /// </summary>
        /// <param name="filesetid">The fileset ID to filter by.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>An asynchronous enumerable of broken file IDs.</returns>
        public async IAsyncEnumerable<Tuple<string, long>> GetBrokenFilenames(long filesetid, [EnumeratorCancellation] CancellationToken token)
        {
            await using var cmd = Connection.CreateCommand(m_rtr)
                .SetCommandAndParameters(BROKEN_FILE_NAMES)
                .SetParameterValue("@FilesetId", filesetid);

            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync(token).ConfigureAwait(false))
                if (!rd.IsDBNull(0))
                    yield return new Tuple<string, long>(
                        rd.ConvertValueToString(0) ?? throw new Exception("Filename was null"),
                        rd.ConvertValueToInt64(1)
                    );
        }

        /// <summary>
        /// Returns all index files that are orphaned, i.e., not referenced by any block files.
        /// </summary>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>An asynchronous enumerable of <see cref="RemoteVolume"/> representing the orphaned index files.</returns>
        public async IAsyncEnumerable<RemoteVolume> GetOrphanedIndexFiles([EnumeratorCancellation] CancellationToken token)
        {
            await using var cmd = Connection.CreateCommand($@"
                SELECT
                    ""Name"",
                    ""Hash"",
                    ""Size""
                FROM ""RemoteVolume""
                WHERE
                    ""Type"" = '{RemoteVolumeType.Index}'
                    AND ""ID"" NOT IN (
                        SELECT ""IndexVolumeID""
                        FROM ""IndexBlockLink""
                    )
            ")
                .SetTransaction(m_rtr);

            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync(token).ConfigureAwait(false))
                yield return new RemoteVolume(
                    rd.ConvertValueToString(0) ?? throw new Exception("Filename was null"),
                    rd.ConvertValueToString(1) ?? throw new Exception("Hash was null"),
                    rd.ConvertValueToInt64(2, -1)
                );
        }

        /// <summary>
        /// Inserts the broken file IDs into the given table. The table must have a single column with the same name as the ID field name.
        /// </summary>
        /// <param name="filesetid">The filset id for the current operation.</param>
        /// <param name="tablename">The name of the table to insert into.</param>
        /// <param name="IDfieldname">The name of the ID field in the table.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that completes when the insertion is finished.</returns>
        public async Task InsertBrokenFileIDsIntoTable(long filesetid, string tablename, string IDfieldname, CancellationToken token)
        {
            await using var cmd = Connection.CreateCommand(m_rtr)
                .SetCommandAndParameters(INSERT_BROKEN_IDS(tablename, IDfieldname))
                .SetParameterValue("@FilesetId", filesetid);

            await cmd.ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);
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
                    ORDER BY ""Blockset"".""Length"" ASC
                    LIMIT 1
                ")
                  .ExpandInClauseParameterMssqlite("@BlockVolumeIds", blockVolumeIds)
                  .ExecuteScalarInt64Async(-1, token)
                  .ConfigureAwait(false);

            return res;
        }

        /// <summary>
        /// Replaces the metadata blockset ID in the Metadataset table with the empty blockset ID for all fileset entries that are not in any block volume.
        /// This is used to clean up the metadata blocksets that are now missing.
        /// </summary>
        /// <param name="filesetId">The filesetId to target.</param>
        /// <param name="emptyBlocksetId">The empty blockset ID to replace with.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the number of rows affected</returns>
        public async Task<int> ReplaceMetadata(long filesetId, long emptyBlocksetId, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(@"
                UPDATE ""Metadataset""
                SET ""BlocksetID"" = @EmptyBlocksetID
                WHERE
                    ""ID"" IN (
                        SELECT ""FileLookup"".""MetadataID""
                        FROM
                            ""FileLookup"",
                            ""FilesetEntry""
                        WHERE
                            ""FilesetEntry"".""FilesetId"" = @FilesetID
                            AND ""FileLookup"".""ID"" = ""FilesetEntry"".""FileID""
                    )
                    AND ""BlocksetID"" NOT IN (
                        SELECT ""BlocksetID""
                        FROM
                            ""BlocksetEntry"",
                            ""Block""
                        WHERE ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                    )
            ")
                .SetTransaction(m_rtr)
                .SetParameterValue("@EmptyBlocksetID", emptyBlocksetId)
                .SetParameterValue("@FilesetID", filesetId);

            return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Removes all blocks, blocksets, and index links that are missing from the specified volumes.
        /// </summary>
        /// <param name="names">The names of the volumes to check for missing blocks.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that completes when the removal is finished.</returns>
        public async Task RemoveMissingBlocks(IEnumerable<string> names, CancellationToken token)
        {
            if (names == null || !names.Any()) return;

            await using var deletecmd = m_connection.CreateCommand(m_rtr);
            var temptransguid = Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
            var volidstable = "DelVolSetIds-" + temptransguid;

            // Create and fill a temp table with the volids to delete. We avoid using too many parameters that way.
            await deletecmd.ExecuteNonQueryAsync($@"
                    CREATE TEMP TABLE ""{volidstable}"" (
                        ""ID"" INTEGER PRIMARY KEY
                    )
                ", token)
                .ConfigureAwait(false);

            await using (var tmptable = await TemporaryDbValueList.CreateAsync(this, names, token).ConfigureAwait(false))
                await (
                    await deletecmd.SetCommandAndParameters($@"
                            INSERT OR IGNORE INTO ""{volidstable}"" (""ID"")
                            SELECT ""ID""
                            FROM ""RemoteVolume""
                            WHERE ""Name"" IN (@Names)
                        ")
                    .ExpandInClauseParameterMssqliteAsync("@Names", tmptable, token)
                    .ConfigureAwait(false)
                )
                  .ExecuteNonQueryAsync(token)
                  .ConfigureAwait(false);

            var volIdsSubQuery = $@"
                    SELECT ""ID""
                    FROM ""{volidstable}""
                ";

            await deletecmd.ExecuteNonQueryAsync($@"
                    DELETE FROM ""IndexBlockLink""
                    WHERE
                        ""BlockVolumeID"" IN ({volIdsSubQuery})
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
                    DELETE FROM ""DuplicateBlock""
                    WHERE ""VolumeID"" IN ({volIdsSubQuery})
                ", token)
                .ConfigureAwait(false);

            // Clean up temp tables for subqueries. We truncate content and then try to delete.
            // Drop in try-block, as it fails in nested transactions (SQLite problem)
            // SQLite.SQLiteException (0x80004005): database table is locked
            await deletecmd
                .ExecuteNonQueryAsync($@"DELETE FROM ""{volidstable}"" ", token)
                .ConfigureAwait(false);

            try
            {
                deletecmd.CommandTimeout = 2;
                await deletecmd
                    .ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{volidstable}"" ", token)
                    .ConfigureAwait(false);
            }
            catch { /* Ignore, will be deleted on close anyway. */ }
        }

        /// <summary>
        /// Gets the count of files in a specific fileset.
        /// </summary>
        /// <param name="filesetid">The ID of the fileset to count files in.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the count of files in the specified fileset.</returns>
        public async Task<long> GetFilesetFileCount(long filesetid, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(@"
                SELECT COUNT(*)
                FROM ""FilesetEntry""
                WHERE ""FilesetID"" = @FilesetId
            ")
                .SetTransaction(m_rtr)
                .SetParameterValue("@FilesetId", filesetid);

            return await cmd
                .ExecuteScalarInt64Async(0, token)
                .ConfigureAwait(false);
        }
    }
}
