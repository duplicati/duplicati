// Copyright (C) 2026, The Duplicati Team
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

namespace Duplicati.Library.Main.Database.Local
{
    /// <summary>
    /// A database for listing broken files, i.e., files that reference blocksets or blocks that are not available.
    /// </summary>
    internal class LocalListBrokenFilesDatabase : LocalDatabase
    {
        /// <summary>
        /// The tag used for logging.
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(LocalListBrokenFilesDatabase));

        /// <summary>
        /// SQL query to get the IDs of all block volumes.
        /// </summary>
        private static readonly string BLOCK_VOLUME_IDS = $@"
            SELECT ""ID""
            FROM ""RemoteVolume""
            WHERE ""Type"" = '{Library.Utility.Utility.FormatInvariantValue(RemoteVolumeType.Blocks)}'
        ";

        /// <summary>
        /// SQL query returning the ids of the zero-length blocksets that are valid.
        /// </summary>
        /// <remarks>
        /// Because <c>Blockset</c> is unique on <c>("FullHash", "Length")</c> there is at most one
        /// zero-length blockset, and it is the blockset that every empty file in the backup uses as its
        /// content. It has no blocks, and needs none, so it is the one case where a blockset without blocks
        /// is not damage.
        /// </remarks>
        private static readonly string VALID_EMPTY_BLOCKSET_IDS = @"
            SELECT ""A"".""ID""
            FROM ""Blockset"" ""A""
            LEFT JOIN ""BlocksetEntry"" ""B""
                ON ""A"".""ID"" = ""B"".""BlocksetID""
            WHERE
                ""A"".""Length"" = 0
                AND ""B"".""BlocksetID"" IS NULL
        ";

        /// <summary>
        /// SQL query returning the ids of the blocksets that cannot be restored.
        /// </summary>
        /// <remarks>
        /// A blockset is invalid when it:
        /// <list type="bullet">
        /// <item>has <c>BlocksetEntry</c> rows referencing unknown or invalid blocks, meaning the data to
        /// rebuild the blockset is not available. Invalid blocks are those that appear to be in non-Blocks
        /// volumes (e.g. are listed as being in an Index or Files volume) or in an unknown volume (-1);</item>
        /// <item>has <c>BlocklistHash</c> rows referencing unknown or invalid blocks, meaning the data that
        /// defines the list of hashes making up the blockset is not available;</item>
        /// <item>is defined in <c>Blockset</c> but has no <c>BlocksetEntry</c> rows, which can happen during
        /// a recreate if Files volumes reference blocksets that are not found in any Index file;</item>
        /// <item>records a length that does not match the summed size of the blocks it references, so there
        /// is no way to tell whether the length or the block mapping is the damaged part;</item>
        /// <item>records a length of 0 while having blocks attached, which is the same damage seen from the
        /// other side, and is called out separately because it is the state that caused this check to be
        /// written.</item>
        /// </list>
        /// The single legitimately zero-length blockset is exempted, see <see cref="VALID_EMPTY_BLOCKSET_IDS"/>.
        /// </remarks>
        private static readonly string INVALID_BLOCKSET_IDS = $@"
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
                UNION
                    SELECT ""A"".""ID"" AS ""BlocksetID""
                    FROM ""Blockset"" ""A""
                    LEFT JOIN (
                        SELECT
                            ""BlocksetEntry"".""BlocksetID"",
                            SUM(""Block"".""Size"") AS ""CalcLen""
                        FROM ""BlocksetEntry""
                        LEFT JOIN ""Block""
                            ON ""Block"".""ID"" = ""BlocksetEntry"".""BlockID""
                        GROUP BY ""BlocksetEntry"".""BlocksetID""
                    ) ""C""
                        ON ""A"".""ID"" = ""C"".""BlocksetID""
                    WHERE ""A"".""Length"" != IFNULL(""C"".""CalcLen"", 0)
                UNION
                    SELECT ""BlocksetEntry"".""BlocksetID""
                    FROM ""BlocksetEntry""
                    JOIN ""Blockset""
                        ON ""Blockset"".""ID"" = ""BlocksetEntry"".""BlocksetID""
                    WHERE ""Blockset"".""Length"" = 0
            )
            WHERE ""BlocksetID"" NOT IN ({VALID_EMPTY_BLOCKSET_IDS})
        ";

        /// <summary>
        /// Returns the SQL query for the ids of the blocksets that cannot be restored.
        /// </summary>
        /// <remarks>
        /// This currently just hands out a constant, and is asynchronous only so that it can be changed into
        /// a lookup that materializes the classification into a temporary table when it is requested more
        /// than once. The classification aggregates over every row of <c>BlocksetEntry</c> joined to
        /// <c>Block</c>, and the broken-file query is evaluated once per fileset, so evaluating it inline
        /// every time is a full scan per fileset.
        /// <para>
        /// Callers must resolve it once per query they build, and pass the string down, or a single query
        /// build will look like several requests to the caching version.
        /// </para>
        /// </remarks>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the SQL query.</returns>
        private Task<string> InvalidBlocksetIdsQueryAsync(CancellationToken token)
            => Task.FromResult(INVALID_BLOCKSET_IDS);

        /// <summary>
        /// Builds the SQL query for the blocksets that can hold restorable metadata.
        /// </summary>
        /// <remarks>
        /// Metadata always has contents - the smallest possible blob is a serialized empty dictionary - so a
        /// metadata entry with a length of 0 can never be restored, no matter how healthy the blockset looks
        /// otherwise. Everything about replaceable metadata is derived from this one predicate, so the
        /// detection cannot drift from what the replacement does.
        /// </remarks>
        /// <param name="invalidBlocksetIdsQuery">The query returned by <see cref="InvalidBlocksetIdsQueryAsync"/>.</param>
        /// <returns>A SQL query returning blockset ids.</returns>
        private static string UsableMetadataBlocksetIdsQuery(string invalidBlocksetIdsQuery) => $@"
            SELECT ""ID""
            FROM ""Blockset""
            WHERE
                ""Length"" > 0
                AND ""ID"" NOT IN ({invalidBlocksetIdsQuery})
        ";

        /// <summary>
        /// Builds the SQL query for the IDs of broken files.
        /// </summary>
        /// <remarks>
        /// A file is broken when its content blockset is invalid, or when its metadata cannot be restored.
        /// The metadata condition is expressed as <c>NOT IN (usable)</c> rather than <c>IN (unusable)</c>:
        /// a <c>Metadataset</c> row can point at a blockset id that no longer exists at all, and an
        /// <c>IN (unusable)</c> formulation misses that, since the unusable set is derived from
        /// <c>Blockset</c> rows that are gone.
        /// </remarks>
        /// <param name="ignoreReplaceableMetadata">If <c>true</c>, files that are only broken because their
        /// metadata needs replacing are not reported. This projects the state after a replacement, and is
        /// exact by construction, since the condition is dropped rather than inverted.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the SQL query.</returns>
        private async Task<string> BrokenFileIdsQueryAsync(bool ignoreReplaceableMetadata, CancellationToken token)
        {
            // Resolve once per query build, so a single build cannot count as two requests
            var invalidBlocksetIdsQuery = await InvalidBlocksetIdsQueryAsync(token).ConfigureAwait(false);

            // NULL is not covered by NOT IN, so a missing Metadataset row needs the explicit null check
            // below. No replacement can fix that anyway, so it is reported unconditionally.
            var metadataCondition = ignoreReplaceableMetadata
                ? string.Empty
                : $@"
                OR (
                    ""IsMetadata"" = 1
                    AND ""BlocksetID"" NOT IN ({UsableMetadataBlocksetIdsQuery(invalidBlocksetIdsQuery)})
                )";

            return $@"
            SELECT DISTINCT ""ID""
            FROM (
                SELECT
                    ""ID"" AS ""ID"",
                    ""BlocksetID"" AS ""BlocksetID"",
                    0 AS ""IsMetadata""
                FROM ""FileLookup""
                WHERE
                    ""BlocksetID"" != {Library.Utility.Utility.FormatInvariantValue(FOLDER_BLOCKSET_ID)}
                    AND ""BlocksetID"" != {Library.Utility.Utility.FormatInvariantValue(SYMLINK_BLOCKSET_ID)}
                UNION
                    SELECT
                        ""A"".""ID"" AS ""ID"",
                        ""B"".""BlocksetID"" AS ""BlocksetID"",
                        1 AS ""IsMetadata""
                    FROM ""FileLookup"" ""A""
                    LEFT JOIN ""Metadataset"" ""B""
                        ON ""A"".""MetadataID"" = ""B"".""ID""
            )
            WHERE
                ""BlocksetID"" IS NULL
                OR (
                    ""IsMetadata"" = 0
                    AND ""BlocksetID"" IN ({invalidBlocksetIdsQuery})
                ){metadataCondition}
            ";
        }

        /// <summary>
        /// Builds the SQL query for the broken filesets, i.e., filesets that contain broken files.
        /// </summary>
        /// <param name="ignoreReplaceableMetadata">If <c>true</c>, files that are only broken because their metadata needs replacing are not counted.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the SQL query.</returns>
        private async Task<string> BrokenFileSetsQueryAsync(bool ignoreReplaceableMetadata, CancellationToken token) => $@"
            SELECT DISTINCT
                ""B"".""Timestamp"",
                ""A"".""FilesetID"",
                COUNT(""A"".""FileID"") AS ""FileCount""
            FROM
                ""FilesetEntry"" ""A"",
                ""Fileset"" ""B""
            WHERE
                ""A"".""FilesetID"" = ""B"".""ID""
                AND ""A"".""FileID"" IN ({await BrokenFileIdsQueryAsync(ignoreReplaceableMetadata, token).ConfigureAwait(false)})
        ";

        /// <summary>
        /// Builds the SQL query for the names and lengths of broken files.
        /// </summary>
        /// <param name="ignoreReplaceableMetadata">If <c>true</c>, files that are only broken because their metadata needs replacing are not reported.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the SQL query.</returns>
        private async Task<string> BrokenFileNamesQueryAsync(bool ignoreReplaceableMetadata, CancellationToken token) => $@"
            SELECT
                ""A"".""Path"",
                ""B"".""Length""
            FROM ""File"" ""A""
            LEFT JOIN ""Blockset"" ""B""
                ON (""A"".""BlocksetID"" = ""B"".""ID"")
            WHERE
                ""A"".""ID"" IN ({await BrokenFileIdsQueryAsync(ignoreReplaceableMetadata, token).ConfigureAwait(false)})
                AND ""A"".""ID"" IN (
                    SELECT ""FileID""
                    FROM ""FilesetEntry""
                    WHERE ""FilesetID"" = @FilesetId
                )
        ";

        /// <summary>
        /// Builds the SQL statement that inserts broken file IDs into a specified table. This only builds the
        /// statement, it does not execute it, see <see cref="InsertBrokenFileIDsIntoTableAsync"/>.
        /// The table must have a single column with the same name as the ID field name.
        /// </summary>
        /// <param name="tablename">The name of the table to insert into.</param>
        /// <param name="IDfieldname">The name of the ID field in the table.</param>
        /// <param name="ignoreReplaceableMetadata">If <c>true</c>, files that are only broken because their metadata needs replacing are not inserted.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the SQL statement.</returns>
        private async Task<string> InsertBrokenIdsStatementAsync(string tablename, string IDfieldname, bool ignoreReplaceableMetadata, CancellationToken token) => $@"
            INSERT INTO ""{tablename}"" (
                ""{IDfieldname}""
            )
            SELECT ""ID""
            FROM ({await BrokenFileIdsQueryAsync(ignoreReplaceableMetadata, token).ConfigureAwait(false)})
            WHERE ""ID"" IN (
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
        /// <param name="ignoreReplaceableMetadata">If <c>true</c>, files that are only broken because their metadata needs replacing are not counted, which projects the state after a replacement.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>An asynchronous enumerable of broken file IDs.</returns>
        public async IAsyncEnumerable<(DateTime FilesetTime, long FilesetID, long RemoveFileCount)> GetBrokenFilesetsAsync(DateTime time, long[]? versions, bool ignoreReplaceableMetadata, [EnumeratorCancellation] CancellationToken token)
        {
            var query = await BrokenFileSetsQueryAsync(ignoreReplaceableMetadata, token)
                .ConfigureAwait(false);
            var clause = await GetFilelistWhereClauseAsync(time, versions, null, false, token)
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
                if (!await rd.IsDBNullAsync(0))
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
        /// <param name="ignoreReplaceableMetadata">If <c>true</c>, files that are only broken because their metadata needs replacing are not reported.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>An asynchronous enumerable of broken file IDs.</returns>
        public async IAsyncEnumerable<Tuple<string, long>> GetBrokenFilenamesAsync(long filesetid, bool ignoreReplaceableMetadata, [EnumeratorCancellation] CancellationToken token)
        {
            var query = await BrokenFileNamesQueryAsync(ignoreReplaceableMetadata, token)
                .ConfigureAwait(false);

            await using var cmd = Connection.CreateCommand(m_rtr)
                .SetCommandAndParameters(query)
                .SetParameterValue("@FilesetId", filesetid);

            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync(token).ConfigureAwait(false))
                if (!await rd.IsDBNullAsync(0))
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
        public async IAsyncEnumerable<RemoteVolume> GetOrphanedIndexFilesAsync([EnumeratorCancellation] CancellationToken token)
        {
            await using var cmd = Connection.CreateCommand($@"
                SELECT
                    ""Name"",
                    ""Hash"",
                    ""Size""
                FROM ""RemoteVolume""
                WHERE
                    ""Type"" = '{Library.Utility.Utility.FormatInvariantValue(RemoteVolumeType.Index)}'
                    AND ""ID"" NOT IN (
                        SELECT ""IndexVolumeID""
                        FROM ""IndexBlockLink""
                    )
            ")
                .SetTransaction(m_rtr);

            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync(token).ConfigureAwait(false))
                yield return new RemoteVolume(
                    rd.ConvertValueToString(0) ?? throw new Exception("Filename was null"),
                    rd.ConvertValueToString(1) ?? "",
                    rd.ConvertValueToInt64(2, -1)
                );
        }

        /// <summary>
        /// Inserts the broken file IDs into the given table. The table must have a single column with the same name as the ID field name.
        /// </summary>
        /// <param name="filesetid">The filset id for the current operation.</param>
        /// <param name="tablename">The name of the table to insert into.</param>
        /// <param name="IDfieldname">The name of the ID field in the table.</param>
        /// <param name="ignoreReplaceableMetadata">If <c>true</c>, files that are only broken because their metadata needs replacing are not inserted.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that completes when the insertion is finished.</returns>
        public async Task InsertBrokenFileIDsIntoTableAsync(long filesetid, string tablename, string IDfieldname, bool ignoreReplaceableMetadata, CancellationToken token)
        {
            var query = await InsertBrokenIdsStatementAsync(tablename, IDfieldname, ignoreReplaceableMetadata, token)
                .ConfigureAwait(false);

            await using var cmd = Connection.CreateCommand(m_rtr)
                .SetCommandAndParameters(query)
                .SetParameterValue("@FilesetId", filesetid);

            await cmd.ExecuteNonQueryAsync(true, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Returns the ids of the filesets that reference metadata which needs replacing.
        /// </summary>
        /// <remarks>
        /// Only <c>Metadataset</c> rows that actually exist are considered, since a missing row is not
        /// something a replacement can fix.
        /// </remarks>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the fileset ids.</returns>
        public async Task<HashSet<long>> GetFilesetsWithReplaceableMetadataAsync(CancellationToken token)
        {
            var invalidBlocksetIdsQuery = await InvalidBlocksetIdsQueryAsync(token).ConfigureAwait(false);

            await using var cmd = Connection.CreateCommand($@"
                SELECT DISTINCT ""FilesetEntry"".""FilesetID""
                FROM ""FilesetEntry""
                JOIN ""FileLookup""
                    ON ""FileLookup"".""ID"" = ""FilesetEntry"".""FileID""
                JOIN ""Metadataset""
                    ON ""Metadataset"".""ID"" = ""FileLookup"".""MetadataID""
                WHERE ""Metadataset"".""BlocksetID"" NOT IN ({UsableMetadataBlocksetIdsQuery(invalidBlocksetIdsQuery)})
            ")
                .SetTransaction(m_rtr);

            var result = new HashSet<long>();
            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync(token).ConfigureAwait(false))
                result.Add(rd.ConvertValueToInt64(0, -1));

            return result;
        }

        /// <summary>
        /// Repoints the metadata of the given fileset at the replacement blockset, for every metadata entry
        /// that cannot be restored.
        /// </summary>
        /// <remarks>
        /// <c>Metadataset.BlocksetID</c> is repointed rather than <c>FileLookup.MetadataID</c>, because
        /// <c>FileLookup</c> is unique on <c>("PrefixID", "Path", "BlocksetID", "MetadataID")</c> and
        /// repointing it can therefore collide, while <c>Metadataset.BlocksetID</c> has a non-unique index.
        /// <para>
        /// Metadata rows are shared between filesets, so the returned row count is not a per-fileset
        /// recovery count: the first fileset repairs rows that later filesets also use, and those would then
        /// report zero.
        /// </para>
        /// </remarks>
        /// <param name="filesetId">The filesetId to target.</param>
        /// <param name="replacementBlocksetId">The blockset ID to point the damaged metadata at.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the number of rows affected</returns>
        public async Task<int> ReplaceMetadataAsync(long filesetId, long replacementBlocksetId, CancellationToken token)
        {
            var invalidBlocksetIdsQuery = await InvalidBlocksetIdsQueryAsync(token).ConfigureAwait(false);
            var usableMetadataBlocksetIdsQuery = UsableMetadataBlocksetIdsQuery(invalidBlocksetIdsQuery);

            await using var cmd = m_connection.CreateCommand()
                .SetTransaction(m_rtr);

            // Refuse to hand out something that is not restorable itself, as that would silently turn one
            // kind of unrestorable metadata into another
            var replacementIsUsable = await cmd.SetCommandAndParameters($@"
                SELECT COUNT(*)
                FROM ({usableMetadataBlocksetIdsQuery})
                WHERE ""ID"" = @ReplacementBlocksetID
            ")
                .SetParameterValue("@ReplacementBlocksetID", replacementBlocksetId)
                .ExecuteScalarInt64Async(0, token)
                .ConfigureAwait(false);

            if (replacementIsUsable == 0)
                throw new Interface.UserInformationException($"The blockset {replacementBlocksetId} cannot be used as replacement metadata, as it is not restorable itself.", "ReplacementMetadataNotUsable");

            return await cmd.SetCommandAndParameters($@"
                UPDATE ""Metadataset""
                SET ""BlocksetID"" = @ReplacementBlocksetID
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
                    AND ""BlocksetID"" NOT IN ({usableMetadataBlocksetIdsQuery})
            ")
                .SetParameterValue("@ReplacementBlocksetID", replacementBlocksetId)
                .SetParameterValue("@FilesetID", filesetId)
                .ExecuteNonQueryAsync(true, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Removes all blocks, blocksets, and index links that are missing from the specified volumes.
        /// </summary>
        /// <param name="names">The names of the volumes to check for missing blocks.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that completes when the removal is finished.</returns>
        public async Task RemoveMissingBlocksAsync(IEnumerable<string> names, CancellationToken token)
        {
            if (names == null || !names.Any()) return;

            await using var deletecmd = m_connection.CreateCommand(m_rtr);
            var temptransguid = Library.Utility.Utility.GetHexGuid();
            var volidstable = $"DelVolSetIds-{temptransguid}";

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
                  .ExecuteNonQueryAsync(true, token)
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
        /// Finds the id of an available block with the given hash and size.
        /// </summary>
        /// <remarks>
        /// A block can be stored without any blockset pointing at it, so this is not the same question as
        /// <see cref="FindExactMetadataBlocksetIdAsync"/>: the block may be present at the destination while
        /// the blockset that used to reference it has been damaged or removed.
        /// </remarks>
        /// <param name="hash">The block hash to look for.</param>
        /// <param name="size">The block size to look for.</param>
        /// <param name="blockVolumeIds">The ids of the volumes to treat as unavailable.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the id of the block, or -1 if it is not available.</returns>
        public async Task<long> FindAvailableBlockIdAsync(string hash, long size, IEnumerable<long> blockVolumeIds, CancellationToken token)
        {
            await using var tmptable = await TemporaryDbValueList.CreateAsync(this, blockVolumeIds, token)
                .ConfigureAwait(false);

            await using var cmd = Connection.CreateCommand(@$"
                SELECT ""Block"".""ID""
                FROM ""Block""
                WHERE
                    ""Block"".""Hash"" = @Hash
                    AND ""Block"".""Size"" = @Size
                    AND ""Block"".""VolumeID"" NOT IN (@BlockVolumeIds)
                    AND ""Block"".""VolumeID"" IN ({LIVE_BLOCK_VOLUME_IDS})
                LIMIT 1
            ")
                .SetTransaction(m_rtr)
                .SetParameterValue("@Hash", hash)
                .SetParameterValue("@Size", size);

            await cmd.ExpandInClauseParameterMssqliteAsync("@BlockVolumeIds", tmptable, token)
                .ConfigureAwait(false);

            return await cmd.ExecuteScalarInt64Async(-1, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Describes a <c>Block</c> row that was pointed at a volume by <see cref="RegisterBlockAsync"/>,
        /// with what is needed to undo it again.
        /// </summary>
        /// <param name="BlockId">The id of the block row.</param>
        /// <param name="Hash">The hash of the block.</param>
        /// <param name="Size">The size of the block.</param>
        /// <param name="VolumeId">The volume the block was pointed at.</param>
        /// <param name="PreviousVolumeId">The volume the block was pointed at before, if that could be read.</param>
        /// <param name="Created">Whether the row was created, as opposed to being repointed.</param>
        public sealed record BlockRegistration(long BlockId, string Hash, long Size, long VolumeId, long? PreviousVolumeId, bool Created);

        /// <summary>
        /// Records that the block with the given hash and size is stored in the given volume, creating the
        /// <c>Block</c> row if it does not exist yet.
        /// </summary>
        /// <remarks>
        /// The registration has to be committed before the upload of the volume starts, so it can be visible
        /// before the volume actually exists at the destination. Keep the returned value and hand it to
        /// <see cref="RollbackBlockRegistrationAsync"/> if the volume never arrives, or a block that is
        /// perfectly restorable from its old volume is left looking unrestorable.
        /// </remarks>
        /// <param name="hash">The block hash.</param>
        /// <param name="size">The block size.</param>
        /// <param name="volumeId">The id of the volume the block is stored in.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the registration.</returns>
        public async Task<BlockRegistration> RegisterBlockAsync(string hash, long size, long volumeId, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand()
                .SetTransaction(m_rtr);

            cmd.SetCommandAndParameters(@"
                SELECT
                    ""ID"",
                    ""VolumeID""
                FROM ""Block""
                WHERE
                    ""Hash"" = @Hash
                    AND ""Size"" = @Size
            ")
                .SetParameterValue("@Hash", hash)
                .SetParameterValue("@Size", size);

            var existingId = -1L;
            long? previousVolumeId = null;
            await using (var rd = await cmd.ExecuteReaderAsync(writeLog: false, token).ConfigureAwait(false))
                if (await rd.ReadAsync(token).ConfigureAwait(false))
                {
                    existingId = rd.ConvertValueToInt64(0, -1);
                    // Only record a previous volume if one could actually be read. Guessing one and writing
                    // it back on rollback would point the block at a volume that never held it.
                    if (!await rd.IsDBNullAsync(1, token).ConfigureAwait(false))
                        previousVolumeId = rd.ConvertValueToInt64(1);
                }

            if (existingId >= 0)
            {
                await cmd.SetCommandAndParameters(@"
                    UPDATE ""Block""
                    SET ""VolumeID"" = @VolumeId
                    WHERE ""ID"" = @Id
                ")
                    .SetParameterValue("@VolumeId", volumeId)
                    .SetParameterValue("@Id", existingId)
                    .ExecuteNonQueryAsync(true, token)
                    .ConfigureAwait(false);

                return new BlockRegistration(existingId, hash, size, volumeId, previousVolumeId, false);
            }

            var newId = await cmd.SetCommandAndParameters(@"
                INSERT INTO ""Block"" (
                    ""Hash"",
                    ""Size"",
                    ""VolumeID""
                )
                VALUES (
                    @Hash,
                    @Size,
                    @VolumeId
                );
                SELECT last_insert_rowid();
            ")
                .SetParameterValue("@Hash", hash)
                .SetParameterValue("@Size", size)
                .SetParameterValue("@VolumeId", volumeId)
                .ExecuteScalarInt64Async(-1, token)
                .ConfigureAwait(false);

            if (newId < 0)
                throw new InvalidOperationException($"Failed to register the block {hash} in volume {volumeId}.");

            return new BlockRegistration(newId, hash, size, volumeId, null, true);
        }

        /// <summary>
        /// Undoes a registration made by <see cref="RegisterBlockAsync"/>.
        /// </summary>
        /// <remarks>
        /// The row is only touched while it still points at the volume from the registration, so a
        /// registration made by something else in the meantime is left alone. If the row was repointed and
        /// the previous volume is not known, the row is left as it is and a warning is written: writing a
        /// guessed volume id would be worse than leaving the block where the failed upload put it.
        /// </remarks>
        /// <param name="registration">The registration to undo.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that completes when the rollback has been attempted.</returns>
        public async Task RollbackBlockRegistrationAsync(BlockRegistration registration, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand()
                .SetTransaction(m_rtr);

            if (registration.Created)
            {
                await cmd.SetCommandAndParameters(@"
                    DELETE FROM ""Block""
                    WHERE
                        ""ID"" = @Id
                        AND ""VolumeID"" = @VolumeId
                ")
                    .SetParameterValue("@Id", registration.BlockId)
                    .SetParameterValue("@VolumeId", registration.VolumeId)
                    .ExecuteNonQueryAsync(true, token)
                    .ConfigureAwait(false);

                return;
            }

            if (!registration.PreviousVolumeId.HasValue)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "BlockRegistrationNotRolledBack", null, "The block {0} was pointed at volume {1}, but the volume it was in before is not known, so it is left as it is. Run repair to restore the block mapping.", registration.Hash, registration.VolumeId);
                return;
            }

            await cmd.SetCommandAndParameters(@"
                UPDATE ""Block""
                SET ""VolumeID"" = @PreviousVolumeId
                WHERE
                    ""ID"" = @Id
                    AND ""VolumeID"" = @VolumeId
            ")
                .SetParameterValue("@PreviousVolumeId", registration.PreviousVolumeId.Value)
                .SetParameterValue("@Id", registration.BlockId)
                .SetParameterValue("@VolumeId", registration.VolumeId)
                .ExecuteNonQueryAsync(true, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Creates, or repairs, the blockset with the given hash and length, made up of the given blocks.
        /// </summary>
        /// <remarks>
        /// An existing <c>Blockset</c> row for the hash and length is reused - it is unique on those two
        /// columns - but its <c>BlocksetEntry</c> and <c>BlocklistHash</c> rows are rebuilt, so a stale row
        /// cannot keep pointing at the wrong blocks.
        /// </remarks>
        /// <param name="fullHash">The file hash of the metadata blob.</param>
        /// <param name="size">The length of the metadata blob.</param>
        /// <param name="blockIds">The ids of the blocks making up the blockset, in order.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the id of the blockset.</returns>
        public async Task<long> CreateMetadataBlocksetAsync(string fullHash, long size, IEnumerable<long> blockIds, CancellationToken token)
        {
            var ids = blockIds?.ToList() ?? [];

            // A zero-length metadata blockset can never be restored, and is shared with every empty file in
            // the backup, see FindExactMetadataBlocksetIdAsync
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size), "A metadata blockset must have contents.");
            if (ids.Count == 0)
                throw new ArgumentException("A metadata blockset must have at least one block.", nameof(blockIds));
            // More than one block needs blocklist hashes, and those are blocks in their own right, which
            // would have to be stored at the destination as well
            if (ids.Count > 1)
                throw new ArgumentException("A metadata blockset of more than one block is not supported here.", nameof(blockIds));

            await using var cmd = m_connection.CreateCommand()
                .SetTransaction(m_rtr);

            var blocksetId = await cmd.SetCommandAndParameters(@"
                SELECT ""ID""
                FROM ""Blockset""
                WHERE
                    ""FullHash"" = @FullHash
                    AND ""Length"" = @Length
            ")
                .SetParameterValue("@FullHash", fullHash)
                .SetParameterValue("@Length", size)
                .ExecuteScalarInt64Async(-1, token)
                .ConfigureAwait(false);

            if (blocksetId < 0)
            {
                blocksetId = await cmd.SetCommandAndParameters(@"
                    INSERT INTO ""Blockset"" (
                        ""Length"",
                        ""FullHash""
                    )
                    VALUES (
                        @Length,
                        @FullHash
                    );
                    SELECT last_insert_rowid();
                ")
                    .SetParameterValue("@Length", size)
                    .SetParameterValue("@FullHash", fullHash)
                    .ExecuteScalarInt64Async(-1, token)
                    .ConfigureAwait(false);

                if (blocksetId < 0)
                    throw new InvalidOperationException($"Failed to create a metadata blockset for {fullHash}.");
            }
            else
            {
                await cmd.SetCommandAndParameters(@"
                    DELETE FROM ""BlocksetEntry""
                    WHERE ""BlocksetID"" = @BlocksetId
                ")
                    .SetParameterValue("@BlocksetId", blocksetId)
                    .ExecuteNonQueryAsync(true, token)
                    .ConfigureAwait(false);

                await cmd.SetCommandAndParameters(@"
                    DELETE FROM ""BlocklistHash""
                    WHERE ""BlocksetID"" = @BlocksetId
                ")
                    .SetParameterValue("@BlocksetId", blocksetId)
                    .ExecuteNonQueryAsync(true, token)
                    .ConfigureAwait(false);
            }

            cmd.SetCommandAndParameters(@"
                INSERT INTO ""BlocksetEntry"" (
                    ""BlocksetID"",
                    ""Index"",
                    ""BlockID""
                )
                VALUES (
                    @BlocksetId,
                    @Index,
                    @BlockId
                )
            ");

            for (var i = 0; i < ids.Count; i++)
                await cmd
                    .SetParameterValue("@BlocksetId", blocksetId)
                    .SetParameterValue("@Index", i)
                    .SetParameterValue("@BlockId", ids[i])
                    .ExecuteNonQueryAsync(true, token)
                    .ConfigureAwait(false);

            // Refuse to hand out a blockset that does not add up, as that is exactly the damage this is
            // meant to repair
            var calculatedLength = await cmd.SetCommandAndParameters(@"
                SELECT IFNULL(SUM(""Block"".""Size""), 0)
                FROM ""BlocksetEntry""
                JOIN ""Block""
                    ON ""Block"".""ID"" = ""BlocksetEntry"".""BlockID""
                WHERE ""BlocksetEntry"".""BlocksetID"" = @BlocksetId
            ")
                .SetParameterValue("@BlocksetId", blocksetId)
                .ExecuteScalarInt64Async(-1, token)
                .ConfigureAwait(false);

            if (calculatedLength != size)
                throw new InvalidOperationException($"The metadata blockset {blocksetId} has a recorded length of {size}, but its blocks add up to {calculatedLength}.");

            return blocksetId;
        }

        /// <summary>
        /// Returns the id of a <c>Metadataset</c> row for the given blockset, creating one if there is none.
        /// </summary>
        /// <remarks>
        /// A freshly created metadata blockset is referenced by nothing until the damaged metadata is
        /// repointed at it, and an unreferenced blockset is removed by the next cleanup. The row also makes
        /// the blockset visible to <see cref="FindSmallestUsableMetadataBlocksetIdAsync"/>, which only
        /// considers blocksets that are already used as metadata.
        /// </remarks>
        /// <param name="blocksetId">The blockset to point the metadata entry at.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the id of the metadata entry.</returns>
        public async Task<long> GetOrCreateMetadatasetIdAsync(long blocksetId, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand()
                .SetTransaction(m_rtr);

            var metadataId = await cmd.SetCommandAndParameters(@"
                SELECT ""ID""
                FROM ""Metadataset""
                WHERE ""BlocksetID"" = @BlocksetId
                LIMIT 1
            ")
                .SetParameterValue("@BlocksetId", blocksetId)
                .ExecuteScalarInt64Async(-1, token)
                .ConfigureAwait(false);

            if (metadataId >= 0)
                return metadataId;

            metadataId = await cmd.SetCommandAndParameters(@"
                INSERT INTO ""Metadataset"" (
                    ""BlocksetID""
                )
                VALUES (
                    @BlocksetId
                );
                SELECT last_insert_rowid();
            ")
                .SetParameterValue("@BlocksetId", blocksetId)
                .ExecuteScalarInt64Async(-1, token)
                .ConfigureAwait(false);

            if (metadataId < 0)
                throw new InvalidOperationException($"Failed to create a metadata entry for blockset {blocksetId}.");

            return metadataId;
        }

        /// <summary>
        /// Gets the count of files in a specific fileset.
        /// </summary>
        /// <param name="filesetid">The ID of the fileset to count files in.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the count of files in the specified fileset.</returns>
        public async Task<long> GetFilesetFileCountAsync(long filesetid, CancellationToken token)
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
