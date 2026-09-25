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

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

#nullable enable

namespace Duplicati.Library.Main.Database.Local
{
    /// <summary>
    /// Database access used by the restore test operation.
    /// The candidate files and the selected sample are kept in temporary tables on this
    /// connection, so no list of paths is ever held in memory and nothing is left behind
    /// in the database file. Only IDs, counts and single rows cross into the handler.
    /// The connection must stay open, with its transaction committed, while the restore
    /// engine works on the same database file through its own connection.
    /// </summary>
    internal class LocalRestoreTestDatabase : LocalDatabase
    {
        /// <summary>
        /// The alias under which the real database is attached when the history lives elsewhere
        /// </summary>
        private const string HISTORY_ALIAS = "RestoreTestHistoryDb";

        /// <summary>
        /// A file selected for the test.
        /// </summary>
        /// <param name="FileId">The ID of the file entry.</param>
        /// <param name="Path">The path of the file as recorded in the backup.</param>
        /// <param name="Size">The size of the file content.</param>
        /// <param name="Hash">The hash of the file content.</param>
        /// <param name="LastModified">The last modification time recorded in the backup (UTC).</param>
        public sealed record SampleFile(long FileId, string Path, long Size, string Hash, DateTime LastModified);

        /// <summary>
        /// The name of the scratch table holding the candidate entries, or null if not created
        /// </summary>
        private string? m_candidateTable;
        /// <summary>
        /// The name of the scratch table holding the sampled files, or null if not created
        /// </summary>
        private string? m_sampleTable;
        /// <summary>
        /// The reference to the history table used in queries, or null if no history is available
        /// </summary>
        private string? m_historyTable;

        /// <summary>
        /// Opens the database.
        /// </summary>
        /// <param name="path">The path to the database file.</param>
        /// <param name="createOperation">True to record a restore test operation, false to attach to the last operation.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that when awaited returns the opened database.</returns>
        public static async Task<LocalRestoreTestDatabase> CreateAsync(string path, bool createOperation, CancellationToken token)
        {
            var db = (LocalRestoreTestDatabase)
                await CreateLocalDatabaseAsync(path, createOperation ? OperationMode.RestoreTest.ToString() : null, true, new LocalRestoreTestDatabase(), token)
                    .ConfigureAwait(false);

            db.ShouldCloseConnection = true;
            return db;
        }

        /// <summary>
        /// Makes the verification history available to the sampling queries.
        /// </summary>
        /// <param name="historyDatabasePath">The path of the database holding the history, or null if this database holds it.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that completes when the history is available.</returns>
        public async Task UseHistoryAsync(string? historyDatabasePath, CancellationToken token)
        {
            if (historyDatabasePath == null)
            {
                m_historyTable = @"""RestoreTestHistory""";
                return;
            }

            await using var cmd = m_connection.CreateCommand(m_rtr);
            await cmd.SetCommandAndParameters($@"ATTACH DATABASE @Path AS ""{HISTORY_ALIAS}""")
                .SetParameterValue("@Path", historyDatabasePath)
                .ExecuteNonQueryAsync(writeLog: false, token)
                .ConfigureAwait(false);

            m_historyTable = $@"""{HISTORY_ALIAS}"".""RestoreTestHistory""";
        }

        /// <summary>
        /// Gets the ID of the fileset with the given version index, where 0 is the newest.
        /// </summary>
        /// <param name="version">The version index.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that when awaited returns the fileset ID, or -1 if no such version exists.</returns>
        public async Task<long> GetFilesetIdForVersionAsync(long version, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(m_rtr);
            return await cmd.SetCommandAndParameters(@"
                SELECT ""ID""
                FROM ""Fileset""
                ORDER BY ""Timestamp"" DESC
                LIMIT 1 OFFSET @Offset
            ")
                .SetParameterValue("@Offset", version)
                .ExecuteScalarInt64Async(-1, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the number of filesets in the database.
        /// </summary>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that when awaited returns the number of filesets.</returns>
        public async Task<long> GetFilesetCountAsync(CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(m_rtr);
            return await cmd.ExecuteScalarInt64Async(@"SELECT COUNT(*) FROM ""Fileset""", 0, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates the candidate table with the entries of the fileset that pass the filter.
        /// Folders and symlinks are kept so the restore path prefix can be computed the way the
        /// restore engine does; they are never selected for the sample.
        /// </summary>
        /// <param name="filesetId">The ID of the fileset.</param>
        /// <param name="filter">The filter limiting the entries, or null for all entries.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that when awaited returns the number of regular files among the candidates.</returns>
        public async Task<long> CreateCandidateTableAsync(long filesetId, IFilter? filter, CancellationToken token)
        {
            m_candidateTable = $"RestoreTestCandidates-{Library.Utility.Utility.GetHexGuid()}";

            await using var cmd = m_connection.CreateCommand(m_rtr);
            await cmd.ExecuteNonQueryAsync($@"
                CREATE TEMPORARY TABLE ""{m_candidateTable}"" (
                    ""FileID"" INTEGER PRIMARY KEY,
                    ""BlocksetID"" INTEGER NOT NULL
                )
            ", token).ConfigureAwait(false);

            if (filter == null || filter.Empty)
            {
                await cmd.SetCommandAndParameters($@"
                    INSERT INTO ""{m_candidateTable}"" (""FileID"", ""BlocksetID"")
                    SELECT ""File"".""ID"", ""File"".""BlocksetID""
                    FROM ""File"", ""FilesetEntry""
                    WHERE
                        ""File"".""ID"" = ""FilesetEntry"".""FileID""
                        AND ""FilesetEntry"".""FilesetID"" = @FilesetId
                ")
                    .SetParameterValue("@FilesetId", filesetId)
                    .ExecuteNonQueryAsync(writeLog: false, token)
                    .ConfigureAwait(false);
            }
            else
            {
                // The filter is evaluated one path at a time, so no path list is built
                await using var insert = m_connection.CreateCommand(m_rtr);
                insert.SetCommandAndParameters($@"
                    INSERT INTO ""{m_candidateTable}"" (""FileID"", ""BlocksetID"")
                    VALUES (@FileId, @BlocksetId)
                ");

                cmd.SetCommandAndParameters(@"
                    SELECT ""File"".""ID"", ""File"".""Path"", ""File"".""BlocksetID""
                    FROM ""File"", ""FilesetEntry""
                    WHERE
                        ""File"".""ID"" = ""FilesetEntry"".""FileID""
                        AND ""FilesetEntry"".""FilesetID"" = @FilesetId
                ")
                    .SetParameterValue("@FilesetId", filesetId);

                await using var rd = await cmd.ExecuteReaderAsync(writeLog: false, token).ConfigureAwait(false);
                while (await rd.ReadAsync(token).ConfigureAwait(false))
                {
                    var path = rd.ConvertValueToString(1);
                    if (path == null || !FilterExpression.Matches(filter, path))
                        continue;

                    await insert
                        .SetParameterValue("@FileId", rd.ConvertValueToInt64(0))
                        .SetParameterValue("@BlocksetId", rd.ConvertValueToInt64(2))
                        .ExecuteNonQueryAsync(writeLog: false, token)
                        .ConfigureAwait(false);
                }
            }

            return await cmd.SetCommandAndParameters($@"
                SELECT COUNT(*)
                FROM ""{m_candidateTable}""
                WHERE {RegularFilesClause}
            ")
                .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
                .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
                .ExecuteScalarInt64Async(0, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// The SQL clause selecting regular files from the candidate table
        /// </summary>
        private const string RegularFilesClause = @"""BlocksetID"" != @FolderBlocksetId AND ""BlocksetID"" != @SymlinkBlocksetId";

        /// <summary>
        /// Builds a deterministic pseudo-random ordering key for a file ID from the seed.
        /// SQLite cannot seed its random function, so two mixing rounds of a linear
        /// congruential generator are applied to the ID, which keeps a run reproducible.
        /// </summary>
        /// <param name="column">The column holding the file ID.</param>
        /// <returns>The SQL expression for the ordering key.</returns>
        private static string SeededOrder(string column)
            => $@"(((({column} * 1103515245) + @Seed) % 2147483647) * 1103515245 + 12345) % 2147483647";

        /// <summary>
        /// Creates the sample table with the files selected according to the mode.
        /// </summary>
        /// <param name="mode">The sampling mode.</param>
        /// <param name="sampleCount">The number of files for the count based modes.</param>
        /// <param name="samplePercent">The percentage of the candidate size for the size based mode.</param>
        /// <param name="seed">The seed for the random order.</param>
        /// <param name="rollingThreshold">Files verified before this time count as never verified in rolling mode.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that completes when the sample is selected.</returns>
        public async Task CreateSampleTableAsync(RestoreTestMode mode, int sampleCount, double samplePercent, int seed, DateTime rollingThreshold, CancellationToken token)
        {
            if (m_candidateTable == null)
                throw new InvalidOperationException("The candidate table has not been created");

            m_sampleTable = $"RestoreTestSample-{Library.Utility.Utility.GetHexGuid()}";

            await using var cmd = m_connection.CreateCommand(m_rtr);
            await cmd.ExecuteNonQueryAsync($@"
                CREATE TEMPORARY TABLE ""{m_sampleTable}"" (
                    ""FileID"" INTEGER PRIMARY KEY
                )
            ", token).ConfigureAwait(false);

            switch (mode)
            {
                case RestoreTestMode.Full:
                    await cmd.SetCommandAndParameters($@"
                        INSERT INTO ""{m_sampleTable}"" (""FileID"")
                        SELECT ""FileID""
                        FROM ""{m_candidateTable}""
                        WHERE {RegularFilesClause}
                    ")
                        .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
                        .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
                        .ExecuteNonQueryAsync(writeLog: false, token)
                        .ConfigureAwait(false);
                    break;

                case RestoreTestMode.RandomFiles:
                    await cmd.SetCommandAndParameters($@"
                        INSERT INTO ""{m_sampleTable}"" (""FileID"")
                        SELECT ""FileID""
                        FROM ""{m_candidateTable}""
                        WHERE {RegularFilesClause}
                        ORDER BY {SeededOrder(@"""FileID""")}
                        LIMIT @Count
                    ")
                        .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
                        .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
                        .SetParameterValue("@Seed", Math.Abs((long)seed))
                        .SetParameterValue("@Count", sampleCount)
                        .ExecuteNonQueryAsync(writeLog: false, token)
                        .ConfigureAwait(false);
                    break;

                case RestoreTestMode.Rolling:
                    {
                        // Files verified before the threshold sort with the never verified files,
                        // so the least recently verified files are picked first
                        var historyJoin = m_historyTable == null
                            ? string.Empty
                            : $@"LEFT JOIN {m_historyTable} ""H"" ON ""H"".""Path"" = ""File"".""Path""";
                        var historyOrder = m_historyTable == null
                            ? string.Empty
                            : @"CASE WHEN ""H"".""LastVerified"" IS NULL OR ""H"".""LastVerified"" < @Threshold THEN 0 ELSE ""H"".""LastVerified"" END,";

                        await cmd.SetCommandAndParameters($@"
                            INSERT INTO ""{m_sampleTable}"" (""FileID"")
                            SELECT ""C"".""FileID""
                            FROM ""{m_candidateTable}"" ""C""
                            JOIN ""File"" ON ""File"".""ID"" = ""C"".""FileID""
                            {historyJoin}
                            WHERE ""C"".""BlocksetID"" != @FolderBlocksetId AND ""C"".""BlocksetID"" != @SymlinkBlocksetId
                            ORDER BY {historyOrder} {SeededOrder(@"""C"".""FileID""")}
                            LIMIT @Count
                        ")
                            .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
                            .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
                            .SetParameterValue("@Seed", Math.Abs((long)seed))
                            .SetParameterValue("@Threshold", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(rollingThreshold))
                            .SetParameterValue("@Count", sampleCount)
                            .ExecuteNonQueryAsync(writeLog: false, token)
                            .ConfigureAwait(false);
                        break;
                    }

                case RestoreTestMode.RandomSize:
                    {
                        var totalBytes = await cmd.SetCommandAndParameters($@"
                            SELECT IFNULL(SUM(""Blockset"".""Length""), 0)
                            FROM ""{m_candidateTable}"" ""C""
                            JOIN ""Blockset"" ON ""Blockset"".""ID"" = ""C"".""BlocksetID""
                            WHERE ""C"".""BlocksetID"" != @FolderBlocksetId AND ""C"".""BlocksetID"" != @SymlinkBlocksetId
                        ")
                            .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
                            .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
                            .ExecuteScalarInt64Async(0, token)
                            .ConfigureAwait(false);

                        var targetBytes = (long)Math.Ceiling(totalBytes * (samplePercent / 100.0));

                        await using var insert = m_connection.CreateCommand(m_rtr);
                        insert.SetCommandAndParameters($@"INSERT INTO ""{m_sampleTable}"" (""FileID"") VALUES (@FileId)");

                        cmd.SetCommandAndParameters($@"
                            SELECT ""C"".""FileID"", ""Blockset"".""Length""
                            FROM ""{m_candidateTable}"" ""C""
                            JOIN ""Blockset"" ON ""Blockset"".""ID"" = ""C"".""BlocksetID""
                            WHERE ""C"".""BlocksetID"" != @FolderBlocksetId AND ""C"".""BlocksetID"" != @SymlinkBlocksetId
                            ORDER BY {SeededOrder(@"""C"".""FileID""")}
                        ")
                            .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
                            .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
                            .SetParameterValue("@Seed", Math.Abs((long)seed));

                        // At least one file is always tested, and files are added until the target is reached
                        var selectedBytes = 0L;
                        var selectedCount = 0L;
                        await using var rd = await cmd.ExecuteReaderAsync(writeLog: false, token).ConfigureAwait(false);
                        while (await rd.ReadAsync(token).ConfigureAwait(false))
                        {
                            if (selectedCount > 0 && selectedBytes >= targetBytes)
                                break;

                            await insert
                                .SetParameterValue("@FileId", rd.ConvertValueToInt64(0))
                                .ExecuteNonQueryAsync(writeLog: false, token)
                                .ConfigureAwait(false);
                            selectedCount++;
                            selectedBytes += rd.ConvertValueToInt64(1, 0);
                        }
                        break;
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported restore test mode");
            }
        }

        /// <summary>
        /// Gets the number of files and the total size of the sample.
        /// </summary>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that when awaited returns the file count and total size.</returns>
        public async Task<(long Count, long Bytes)> GetSampleStatisticsAsync(CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(m_rtr);
            cmd.SetCommandAndParameters($@"
                SELECT COUNT(*), IFNULL(SUM(""Blockset"".""Length""), 0)
                FROM ""{m_sampleTable}"" ""S""
                JOIN ""FileLookup"" ON ""FileLookup"".""ID"" = ""S"".""FileID""
                JOIN ""Blockset"" ON ""Blockset"".""ID"" = ""FileLookup"".""BlocksetID""
            ");

            await using var rd = await cmd.ExecuteReaderAsync(writeLog: false, token).ConfigureAwait(false);
            if (!await rd.ReadAsync(token).ConfigureAwait(false))
                return (0, 0);
            return (rd.ConvertValueToInt64(0, 0), rd.ConvertValueToInt64(1, 0));
        }

        /// <summary>
        /// Estimates the remote data needed to restore the sample as the total size of the
        /// distinct block volumes holding data or metadata blocks of the sampled files.
        /// </summary>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that when awaited returns the estimated number of bytes.</returns>
        public async Task<long> EstimateSampleDownloadAsync(CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(m_rtr);
            return await cmd.ExecuteScalarInt64Async($@"
                SELECT IFNULL(SUM(""Size""), 0)
                FROM ""Remotevolume""
                WHERE ""Size"" > 0 AND ""ID"" IN (
                    SELECT ""B"".""VolumeID""
                    FROM ""{m_sampleTable}"" ""S""
                    JOIN ""FileLookup"" ""FL"" ON ""FL"".""ID"" = ""S"".""FileID""
                    JOIN ""BlocksetEntry"" ""BE"" ON ""BE"".""BlocksetID"" = ""FL"".""BlocksetID""
                    JOIN ""Block"" ""B"" ON ""B"".""ID"" = ""BE"".""BlockID""

                    UNION

                    SELECT ""B"".""VolumeID""
                    FROM ""{m_sampleTable}"" ""S""
                    JOIN ""FileLookup"" ""FL"" ON ""FL"".""ID"" = ""S"".""FileID""
                    JOIN ""Metadataset"" ""MS"" ON ""MS"".""ID"" = ""FL"".""MetadataID""
                    JOIN ""BlocksetEntry"" ""BE"" ON ""BE"".""BlocksetID"" = ""MS"".""BlocksetID""
                    JOIN ""Block"" ""B"" ON ""B"".""ID"" = ""BE"".""BlockID""
                )
            ", 0, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Streams the sampled files with the values recorded in the backup.
        /// </summary>
        /// <param name="filesetId">The ID of the tested fileset.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>An asynchronous enumerable of the sampled files, ordered by path.</returns>
        public async IAsyncEnumerable<SampleFile> GetSampleFilesAsync(long filesetId, [EnumeratorCancellation] CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(m_rtr);
            cmd.SetCommandAndParameters($@"
                SELECT
                    ""S"".""FileID"",
                    ""File"".""Path"",
                    ""Blockset"".""Length"",
                    ""Blockset"".""FullHash"",
                    ""FilesetEntry"".""Lastmodified""
                FROM ""{m_sampleTable}"" ""S""
                JOIN ""File"" ON ""File"".""ID"" = ""S"".""FileID""
                JOIN ""Blockset"" ON ""Blockset"".""ID"" = ""File"".""BlocksetID""
                JOIN ""FilesetEntry"" ON ""FilesetEntry"".""FileID"" = ""S"".""FileID"" AND ""FilesetEntry"".""FilesetID"" = @FilesetId
                ORDER BY ""File"".""Path""
            ")
                .SetParameterValue("@FilesetId", filesetId);

            await using var rd = await cmd.ExecuteReaderAsync(writeLog: false, token).ConfigureAwait(false);
            while (await rd.ReadAsync(token).ConfigureAwait(false))
                yield return new SampleFile(
                    rd.ConvertValueToInt64(0),
                    rd.ConvertValueToString(1) ?? string.Empty,
                    rd.ConvertValueToInt64(2, 0),
                    rd.ConvertValueToString(3) ?? string.Empty,
                    new DateTime(rd.ConvertValueToInt64(4, 0), DateTimeKind.Utc)
                );
        }

        /// <summary>
        /// Lists the remote block volumes holding data or metadata blocks of a file.
        /// </summary>
        /// <param name="fileId">The ID of the file entry.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that when awaited returns the volume names.</returns>
        public async Task<List<string>> GetFileVolumesAsync(long fileId, CancellationToken token)
        {
            var result = new List<string>();
            await using var cmd = m_connection.CreateCommand(m_rtr);
            cmd.SetCommandAndParameters(@"
                SELECT DISTINCT ""RV"".""Name""
                FROM ""Remotevolume"" ""RV""
                WHERE ""RV"".""ID"" IN (
                    SELECT ""B"".""VolumeID""
                    FROM ""FileLookup"" ""FL""
                    JOIN ""BlocksetEntry"" ""BE"" ON ""BE"".""BlocksetID"" = ""FL"".""BlocksetID""
                    JOIN ""Block"" ""B"" ON ""B"".""ID"" = ""BE"".""BlockID""
                    WHERE ""FL"".""ID"" = @FileId

                    UNION

                    SELECT ""B"".""VolumeID""
                    FROM ""FileLookup"" ""FL""
                    JOIN ""Metadataset"" ""MS"" ON ""MS"".""ID"" = ""FL"".""MetadataID""
                    JOIN ""BlocksetEntry"" ""BE"" ON ""BE"".""BlocksetID"" = ""MS"".""BlocksetID""
                    JOIN ""Block"" ""B"" ON ""B"".""ID"" = ""BE"".""BlockID""
                    WHERE ""FL"".""ID"" = @FileId
                )
            ")
                .SetParameterValue("@FileId", fileId);

            await using var rd = await cmd.ExecuteReaderAsync(writeLog: false, token).ConfigureAwait(false);
            while (await rd.ReadAsync(token).ConfigureAwait(false))
            {
                var name = rd.ConvertValueToString(0);
                if (!string.IsNullOrEmpty(name))
                    result.Add(name);
            }

            return result;
        }

        /// <summary>
        /// Computes the largest common folder prefix of the entries in a scratch table, using
        /// the same algorithm as the restore engine so the restored paths can be predicted.
        /// </summary>
        /// <param name="useCandidates">True to use all candidate entries (as an unfiltered restore does), false to use the sampled files.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that when awaited returns the prefix including a trailing separator, or an empty string.</returns>
        public async Task<string> GetLargestPrefixAsync(bool useCandidates, CancellationToken token)
        {
            var table = useCandidates ? m_candidateTable : m_sampleTable;
            if (table == null)
                throw new InvalidOperationException("The scratch table has not been created");

            await using var cmd = m_connection.CreateCommand(m_rtr);
            var maxpath = await cmd.ExecuteScalarAsync($@"
                SELECT ""File"".""Path""
                FROM ""{table}"" ""T""
                JOIN ""File"" ON ""File"".""ID"" = ""T"".""FileID""
                ORDER BY LENGTH(""File"".""Path"") DESC
                LIMIT 1
            ", token).ConfigureAwait(false) as string ?? string.Empty;

            if (maxpath.Length == 0)
                return string.Empty;

            var dirsep = Util.GuessDirSeparator(maxpath);
            var filecount = await cmd.ExecuteScalarInt64Async($@"SELECT COUNT(*) FROM ""{table}""", -1, token).ConfigureAwait(false);
            var foundfiles = -1L;

            cmd.SetCommandAndParameters($@"
                SELECT COUNT(*)
                FROM ""{table}"" ""T""
                JOIN ""File"" ON ""File"".""ID"" = ""T"".""FileID""
                WHERE SUBSTR(""File"".""Path"", 1, @PrefixLength) = @Prefix
            ");

            while (filecount != foundfiles && maxpath.Length > 0)
            {
                var mp = Util.AppendDirSeparator(maxpath, dirsep);
                foundfiles = await cmd
                    .SetParameterValue("@PrefixLength", mp.Length)
                    .SetParameterValue("@Prefix", mp)
                    .ExecuteScalarInt64Async(-1, token)
                    .ConfigureAwait(false);

                if (filecount != foundfiles)
                {
                    var oldlen = maxpath.Length;
                    var lix = maxpath.Length < 2 ? -1 : maxpath.LastIndexOf(dirsep, maxpath.Length - 2, StringComparison.Ordinal);
                    maxpath = maxpath.Substring(0, lix + 1);
                    if (string.IsNullOrWhiteSpace(maxpath) || maxpath.Length == oldlen)
                        maxpath = string.Empty;
                }
            }

            return maxpath.Length == 0 ? string.Empty : Util.AppendDirSeparator(maxpath, dirsep);
        }

        /// <summary>
        /// Records the verification of a file, replacing any previous record for the same path.
        /// </summary>
        /// <param name="path">The verified path.</param>
        /// <param name="verified">The verification time (UTC).</param>
        /// <param name="result">The outcome.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that completes when the record is written.</returns>
        public async Task RecordVerificationAsync(string path, DateTime verified, string result, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(m_rtr);
            await cmd.SetCommandAndParameters(@"
                INSERT OR REPLACE INTO ""RestoreTestHistory"" (
                    ""Path"",
                    ""LastVerified"",
                    ""LastResult""
                )
                VALUES (
                    @Path,
                    @LastVerified,
                    @LastResult
                )
            ")
                .SetParameterValue("@Path", path)
                .SetParameterValue("@LastVerified", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(verified))
                .SetParameterValue("@LastResult", result)
                .ExecuteNonQueryAsync(writeLog: false, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Drops the temporary tables. They also vanish when the connection closes.
        /// </summary>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that completes when the tables are dropped.</returns>
        public async Task DropScratchTablesAsync(CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(m_rtr);
            foreach (var table in new[] { m_sampleTable, m_candidateTable })
                if (table != null)
                    await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{table}""", token).ConfigureAwait(false);

            m_sampleTable = null;
            m_candidateTable = null;
        }
    }
}
