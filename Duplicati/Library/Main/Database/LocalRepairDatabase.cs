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
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using Microsoft.Data.Sqlite;

namespace Duplicati.Library.Main.Database
{
    internal class LocalRepairDatabase : LocalDatabase
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(LocalRepairDatabase));

        /// <summary>
        /// Creates a new local repair database
        /// </summary>
        /// <param name="path">The path to the database</param>
        /// <param name="pagecachesize">The page cache size</param>
        public static async Task<LocalRepairDatabase> CreateRepairDatabase(string path, long pagecachesize)
        {
            var db = new LocalRepairDatabase();

            db = (LocalRepairDatabase)await CreateLocalDatabaseAsync(db, path, "Repair", false, pagecachesize);

            return db;
        }

        /// <summary>
        /// Gets the fileset ID from the remote name
        /// </summary>
        /// <param name="filelist">The remote name of the fileset</param>
        /// <param name="transaction">An optional transaction</param>
        /// <returns>>The fileset ID</returns>
        public async Task<(long FilesetId, DateTime Time, bool IsFullBackup)> GetFilesetFromRemotename(string filelist)
        {
            using var cmd = m_connection.CreateCommand(@"
                SELECT
                    ""Fileset"".""ID"",
                    ""Fileset"".""Timestamp"",
                    ""Fileset"".""IsFullBackup""
                FROM
                    ""Fileset"",
                    ""RemoteVolume""
                WHERE
                    ""Fileset"".""VolumeID"" = ""RemoteVolume"".""ID""
                    AND ""RemoteVolume"".""Name"" = @Name
            ")
                .SetTransaction(m_rtr.Transaction)
                .SetParameterValue("@Name", filelist);
            var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                throw new Exception($"No such remote file: {filelist}");

            return (
                rd.ConvertValueToInt64(0, -1),
                ParseFromEpochSeconds(rd.ConvertValueToInt64(1)).ToLocalTime(),
                rd.GetInt32(2) == BackupType.FULL_BACKUP
            );
        }

        /// <summary>
        /// Moves entries in the FilesetEntry table from previous fileset to current fileset
        /// </summary>
        /// <param name="filesetid">Current fileset ID</param>
        /// <param name="prevFilesetId">Source fileset ID</param>
        /// <param name="transaction">The transaction</param>
        public async Task MoveFilesFromFileset(long filesetid, long prevFilesetId)
        {
            if (filesetid <= 0)
                throw new ArgumentException("filesetid must be > 0");

            if (prevFilesetId <= 0)
                throw new ArgumentException("prevId must be > 0");

            using var cmd = m_connection.CreateCommand(@"
                UPDATE ""FilesetEntry""
                SET ""FilesetID"" = @CurrentFilesetId
                WHERE ""FilesetID"" = @PreviousFilesetId
            ")
                .SetTransaction(m_rtr.Transaction)
                .SetParameterValue("@CurrentFilesetId", filesetid)
                .SetParameterValue("@PreviousFilesetId", prevFilesetId);

            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Gets the list of index files that reference a given block file
        /// </summary>
        /// <param name="blockfileid">The block file ID</param>
        /// <returns>>A list of index file IDs</returns>
        public async IAsyncEnumerable<string> GetIndexFilesReferencingBlockFile(long blockfileid)
        {
            using var cmd = m_connection.CreateCommand(@"
                SELECT ""RemoteVolume"".""Name""
                FROM
                    ""RemoteVolume"",
                    ""IndexBlockLink""
                WHERE
                    ""IndexBlockLink"".""BlockVolumeID"" = @BlockFileId
                    AND ""RemoteVolume"".""ID"" = ""IndexBlockLink"".""IndexVolumeID""
                    AND ""RemoteVolume"".""Type"" = @Type
            ")
                .SetTransaction(m_rtr.Transaction)
                .SetParameterValue("@BlockFileId", blockfileid)
                .SetParameterValue("@Type", RemoteVolumeType.Index.ToString());

            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                yield return rd.ConvertValueToString(0) ?? throw new Exception("RemoteVolume name was null");
        }

        /// <summary>
        /// Gets a list of filesets that are missing files
        /// </summary>
        /// <param name="transaction">An optional transaction</param>
        /// <returns>A list of fileset IDs and timestamps</returns>
        public async IAsyncEnumerable<KeyValuePair<long, DateTime>> GetFilesetsWithMissingFiles()
        {
            using var cmd = m_connection.CreateCommand(@"
                SELECT
                    ID,
                    Timestamp
                FROM Fileset
                WHERE ID IN (
                    SELECT FilesetID
                    FROM ""FilesetEntry""
                    WHERE ""FileID"" NOT IN (
                        SELECT ""ID""
                        FROM ""FileLookup""
                    )
                )
            ")
                .SetTransaction(m_rtr.Transaction);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                yield return new KeyValuePair<long, DateTime>(
                    rd.ConvertValueToInt64(0),
                    ParseFromEpochSeconds(rd.ConvertValueToInt64(1)).ToLocalTime()
                );
            }
        }

        /// <summary>
        /// Deletes all fileset entries for a given fileset
        /// </summary>
        /// <param name="filesetid">The fileset ID</param>
        /// <param name="transaction">An optional transaction</param>
        /// <returns>The number of deleted entries</returns>
        public async Task<int> DeleteFilesetEntries(long filesetid)
        {
            using var cmd = m_connection.CreateCommand(@"
                DELETE FROM ""FilesetEntry""
                WHERE ""FilesetID"" = @FilesetId
            ")
                .SetTransaction(m_rtr.Transaction)
                .SetParameterValue("@FilesetId", filesetid);

            return await cmd.ExecuteNonQueryAsync();
        }

        private class RemoteVolume : IRemoteVolume
        {
            public string Name { get; private set; }
            public string Hash { get; private set; }
            public long Size { get; private set; }

            public RemoteVolume(string name, string hash, long size)
            {
                this.Name = name;
                this.Hash = hash;
                this.Size = size;
            }
        }

        public async IAsyncEnumerable<IRemoteVolume> GetBlockVolumesFromIndexName(string indexName)
        {
            using var cmd = m_connection.CreateCommand(@"
                SELECT
                    ""Name"",
                    ""Hash"",
                    ""Size""
                FROM ""RemoteVolume""
                WHERE ""ID"" IN (
                    SELECT ""BlockVolumeID""
                    FROM ""IndexBlockLink""
                    WHERE ""IndexVolumeID"" IN (
                        SELECT ""ID""
                        FROM ""RemoteVolume""
                        WHERE ""Name"" = @Name
                    )
                )
            ")
                .SetTransaction(m_rtr.Transaction)
                .SetParameterValue("@Name", indexName);

            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                yield return new RemoteVolume(
                    rd.ConvertValueToString(0) ?? "",
                    rd.ConvertValueToString(1) ?? "",
                    rd.ConvertValueToInt64(2)
                );
        }

        /// <summary>
        /// A single block with source data for repair
        /// </summary>
        /// <param name="Hash">The block hash</param>
        /// <param name="Size">The block size</param>
        /// <param name="File">The file that contains the block</param>
        /// <param name="Offset">The offset of the block in the file</param>
        public sealed record BlockWithSourceData(string Hash, long Size, string File, long Offset);

        /// <summary>
        /// A single block with metadata source data for repair
        /// </summary>
        /// <param name="Hash">The block hash</param>
        /// <param name="Size">The block size</param>
        /// <param name="Path">The path of the file or directory that contains the block</param>
        public sealed record BlockWithMetadataSourceData(string Hash, long Size, string Path);

        /// <summary>
        /// A single blocklist hash entry
        /// </summary>
        /// <param name="BlocksetId">The blockset id</param>
        /// <param name="BlocklistHash">The hash of the blocklist entry (when done)</param>
        /// <param name="BlocklistHashLength">The total length of the blockset</param>
        /// <param name="BlocklistHashIndex">The index of the blocklist hash</param>
        /// <param name="Index">The index of the block in the blockset</param>
        /// <param name="Hash">The hash of the entry</param>
        public sealed record BlocklistHashesEntry(
            long BlocksetId,
            string BlocklistHash,
            long BlocklistHashLength,
            long BlocklistHashIndex,
            int Index,
            string Hash);

        /// <summary>
        /// Helper interface for the missing block list
        /// </summary>
        public interface IMissingBlockList : IDisposable
        {
            /// <summary>
            /// Registers a block as restored
            /// </summary>
            /// <param name="hash">The block hash</param>
            /// <param name="size">The block size</param>
            /// <param name="volumeId">The volume ID of the new target volume</param>
            /// <returns>The restored blocks</returns>
            Task<bool> SetBlockRestored(string hash, long size, long volumeId);
            /// <summary>
            /// Gets the list of files that contains missing blocks
            /// </summary>
            /// <param name="blocksize">The blocksize setting</param>
            /// <returns>A list of files with missing blocks</returns>
            IAsyncEnumerable<BlockWithSourceData> GetSourceFilesWithBlocks(long blocksize);
            /// <summary>
            /// Gets a list for filesystem entries that contain missing blocks in metadata
            /// </summary>
            /// <returns>A list of filesystem entries with missing blocks</returns>
            IAsyncEnumerable<BlockWithMetadataSourceData> GetSourceItemsWithMetadataBlocks();
            /// <summary>
            /// Gets missing blocklist hashes
            /// </summary>
            /// <param name="hashesPerBlock">The number of hashes for each block</param>
            /// <returns>>A list of blocklist hashes</returns>
            IAsyncEnumerable<BlocklistHashesEntry> GetBlocklistHashes(long hashesPerBlock);
            /// <summary>
            /// Gets the number of missing blocks
            /// </summary>
            /// <returns>>The number of missing blocks</returns>
            Task<long> GetMissingBlockCount();
            /// <summary>
            /// Gets all the filesets that are affected by missing blocks
            /// </summary>
            /// <returns>>A list of filesets</returns>
            IAsyncEnumerable<IRemoteVolume> GetFilesetsUsingMissingBlocks();
            /// <summary>
            /// Gets a list of remote files that may contain missing blocks
            /// </summary>
            /// <returns>>A list of remote files</returns>
            IAsyncEnumerable<IRemoteVolume> GetMissingBlockSources();
        }

        /// <summary>
        /// Implementation of the missing block list
        /// </summary>
        private class MissingBlockList : IMissingBlockList
        {
            /// <summary>
            /// The connection to the database
            /// </summary>
            private readonly SqliteConnection m_connection;
            /// <summary>
            /// The transaction to use
            /// </summary>
            private readonly ReusableTransaction m_transaction;
            /// <summary>
            /// The insert command to use for restoring blocks
            /// </summary>
            private SqliteCommand m_insertCommand = null!;
            /// <summary>
            /// The command to copy blocks into the duplicate block table
            /// </summary>
            private SqliteCommand m_copyIntoDuplicatedBlocks = null!;
            /// <summary>
            /// The command to assign blocks to a new volume
            /// </summary>
            private SqliteCommand m_assignBlocksToNewVolume = null!;
            private SqliteCommand m_missingBlocksCommand = null!;
            private SqliteCommand m_missingBlocksCountCommand = null!;
            /// <summary>
            /// The name of the temporary table
            /// </summary>
            private readonly string m_tablename;
            /// <summary>
            /// The name of the volume where blocks are missing
            /// </summary>
            private readonly string m_volumename;
            // TODO remove
            /// <summary>
            /// The query to use for getting missing blocks
            /// </summary>
            //private readonly string m_missingBlocksQuery;

            /// <summary>
            /// Whether the object has been disposed
            /// </summary>
            private bool m_isDisposed = false;

            /// <summary>
            /// Creates a new missing block list
            /// </summary>
            /// <param name="volumename">The name of the volume with missing blocks</param>
            /// <param name="connection">The connection to the database</param>
            /// <param name="transaction">The transaction to use</param>
            private MissingBlockList(string volumename, SqliteConnection connection, ReusableTransaction transaction)
            {
                m_connection = connection;
                m_transaction = transaction;
                m_volumename = volumename;
                var tablename = "MissingBlocks-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                m_tablename = tablename;
            }

            public static async Task<IMissingBlockList> CreateMissingBlockList(string volumename, SqliteConnection connection, ReusableTransaction transaction)
            {
                var blocklist = new MissingBlockList(volumename, connection, transaction);
                using (var cmd = connection.CreateCommand(transaction.Transaction))
                {
                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE TEMPORARY TABLE ""{blocklist.m_tablename}"" (
                            ""Hash"" TEXT NOT NULL,
                            ""Size"" INTEGER NOT NULL,
                            ""Restored"" INTEGER NOT NULL
                        )
                    ");

                    cmd.SetCommandAndParameters($@"
                        INSERT INTO ""{blocklist.m_tablename}"" (
                            ""Hash"",
                            ""Size"",
                            ""Restored""
                        )
                        SELECT DISTINCT
                            ""Block"".""Hash"",
                            ""Block"".""Size"",
                            0 AS ""Restored"" FROM ""Block"",
                            ""Remotevolume""
                        WHERE
                            ""Block"".""VolumeID"" = ""Remotevolume"".""ID""
                            AND ""Remotevolume"".""Name"" = @Name
                    ")
                        .SetParameterValue("@Name", volumename);
                    var blockCount = await cmd.ExecuteNonQueryAsync();

                    if (blockCount == 0)
                        throw new Exception($"Unexpected empty block volume: {0}");

                    await cmd.ExecuteNonQueryAsync($@"
                        CREATE UNIQUE INDEX ""{blocklist.m_tablename}-Ix""
                        ON ""{blocklist.m_tablename}"" (
                            ""Hash"",
                            ""Size"",
                            ""Restored""
                        )
                    ");
                }

                blocklist.m_insertCommand = await connection.CreateCommandAsync($@"
                    UPDATE ""{blocklist.m_tablename}""
                    SET ""Restored"" = @NewRestoredValue
                    WHERE
                        ""Hash"" = @Hash
                        AND ""Size"" = @Size
                        AND ""Restored"" = @PreviousRestoredValue
                ");

                blocklist.m_copyIntoDuplicatedBlocks = await connection.CreateCommandAsync(@"
                    INSERT OR IGNORE INTO ""DuplicateBlock"" (
                        ""BlockID"",
                        ""VolumeID""
                    )
                    SELECT
                        ""Block"".""ID"",
                        ""Block"".""VolumeID""
                    FROM ""Block""
                    WHERE
                        ""Block"".""Hash"" = @Hash
                        AND ""Block"".""Size"" = @Size
                ");

                blocklist.m_assignBlocksToNewVolume = await connection.CreateCommandAsync(@"
                    UPDATE ""Block""
                    SET ""VolumeID"" = @TargetVolumeId
                    WHERE
                        ""Hash"" = @Hash
                        AND ""Size"" = @Size
                ");

                var m_missingBlocksQuery = $@"
                    SELECT
                        ""{blocklist.m_tablename}"".""Hash"",
                        ""{blocklist.m_tablename}"".""Size""
                    FROM ""{blocklist.m_tablename}""
                    WHERE ""{blocklist.m_tablename}"".""Restored"" = @Restored ";
                blocklist.m_missingBlocksCommand = await connection.CreateCommandAsync(m_missingBlocksQuery);

                blocklist.m_missingBlocksCountCommand = await connection.CreateCommandAsync($@"
                    SELECT COUNT(*)
                    FROM ({m_missingBlocksQuery})
                ");

                return blocklist;
            }

            /// <inheritdoc/>
            public async Task<bool> SetBlockRestored(string hash, long size, long targetVolumeId)
            {
                var restored = await m_insertCommand.SetTransaction(m_transaction.Transaction)
                    .SetParameterValue("@NewRestoredValue", 1)
                    .SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Size", size)
                    .SetParameterValue("@PreviousRestoredValue", 0)
                    .ExecuteNonQueryAsync() == 1;

                if (restored)
                {
                    await m_copyIntoDuplicatedBlocks.SetTransaction(m_transaction.Transaction)
                        .SetParameterValue("@Hash", hash)
                        .SetParameterValue("@Size", size)
                        .ExecuteNonQueryAsync();

                    var c = await m_assignBlocksToNewVolume.SetTransaction(m_transaction.Transaction)
                        .SetParameterValue("@TargetVolumeId", targetVolumeId)
                        .SetParameterValue("@Hash", hash)
                        .SetParameterValue("@Size", size)
                        .ExecuteNonQueryAsync();

                    if (c != 1)
                        throw new Exception($"Unexpected number of updated blocks: {c} != 1");
                }

                return restored;
            }

            /// <inheritdoc/>
            public async IAsyncEnumerable<BlockWithSourceData> GetSourceFilesWithBlocks(long blocksize)
            {
                using var cmd = m_connection.CreateCommand($@"
                    SELECT DISTINCT
                        ""{m_tablename}"".""Hash"",
                        ""{m_tablename}"".""Size"",
                        ""File"".""Path"",
                        ""BlocksetEntry"".""Index"" * {blocksize}
                    FROM
                        ""{m_tablename}"",
                        ""Block"",
                        ""BlocksetEntry"",
                        ""File""
                    WHERE
                        ""File"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID""
                        AND ""Block"".""ID"" = ""BlocksetEntry"".""BlockID""
                        AND ""{m_tablename}"".""Hash"" = ""Block"".""Hash""
                        AND ""{m_tablename}"".""Size"" = ""Block"".""Size""
                        AND ""{m_tablename}"".""Restored"" = @Restored
                    ORDER BY
                        ""{m_tablename}"".""Hash"",
                        ""{m_tablename}"".""Size"",
                        ""File"".""Path"",
                        ""BlocksetEntry"".""Index"" * {blocksize}
                ")
                    .SetTransaction(m_transaction.Transaction)
                    .SetParameterValue("@Restored", 0);

                await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                {
                    var hash = rd.ConvertValueToString(0) ?? throw new Exception("Hash value was null");
                    var size = rd.ConvertValueToInt64(1);
                    var file = rd.ConvertValueToString(2) ?? throw new Exception("File value was null");
                    var offset = rd.ConvertValueToInt64(3);

                    yield return new BlockWithSourceData(hash, size, file, offset);
                }
            }

            /// <inheritdoc/>
            public async IAsyncEnumerable<BlockWithMetadataSourceData> GetSourceItemsWithMetadataBlocks()
            {
                using var cmd = m_connection.CreateCommand($@"
                    SELECT DISTINCT
                        ""{m_tablename}"".""Hash"",
                        ""{m_tablename}"".""Size"",
                        ""File"".""Path""
                    FROM
                        ""{m_tablename}"",
                        ""Block"",
                        ""BlocksetEntry"",
                        ""Metadataset"",
                        ""File""
                    WHERE
                        ""File"".""MetadataID"" == ""Metadataset"".""ID""
                        AND ""Metadataset"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID""
                        AND ""Block"".""ID"" = ""BlocksetEntry"".""BlockID""
                        AND ""{m_tablename}"".""Hash"" = ""Block"".""Hash""
                        AND ""{m_tablename}"".""Size"" = ""Block"".""Size""
                        AND ""{m_tablename}"".""Restored"" = @Restored
                    ORDER BY
                        ""{m_tablename}"".""Hash"",
                        ""{m_tablename}"".""Size"",
                        ""File"".""Path""
                ")
                    .SetTransaction(m_transaction.Transaction)
                    .SetParameterValue("@Restored", 0);

                await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                {
                    var hash = rd.ConvertValueToString(0) ?? throw new Exception("Hash value was null");
                    var size = rd.ConvertValueToInt64(1);
                    var path = rd.ConvertValueToString(2) ?? throw new Exception("File value was null");

                    yield return new BlockWithMetadataSourceData(hash, size, path);
                }
            }

            /// <inheritdoc/>
            public async IAsyncEnumerable<BlocklistHashesEntry> GetBlocklistHashes(long hashesPerBlock)
            {
                using var cmd = m_connection.CreateCommand(m_transaction.Transaction);

                var blocklistTableName = $"BlocklistHashList-{Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray())}";

                try
                {
                    // We need to create a snapshot as we will be updating the m_tablename table during enumeration
                    await cmd.SetCommandAndParameters($@"
                        CREATE TEMPORARY TABLE ""{blocklistTableName}"" AS
                        SELECT
                            b.""Hash"" AS ""BlockHash"",
                            bs.""Id"" AS ""BlocksetId"",
                            bs.""Length"" AS ""BlocksetLength"",
                            bse.""Index"" / @HashesPerBlock AS ""BlocklistHashIndex"",
                            (
                                SELECT blh.""Hash""
                                FROM ""BlocklistHash"" blh
                                WHERE
                                    blh.""BlocksetID"" = bs.""ID""
                                    AND blh.""Index"" == bse.""Index"" / @HashesPerBlock
                                LIMIT 1
                            ) AS ""BlocklistHashHash"",
                            bse.""Index"" AS ""BlocksetEntryIndex""
                        FROM ""BlocksetEntry"" bse
                        JOIN ""Block"" b
                            ON b.""ID"" = bse.""BlockID""
                        JOIN ""Blockset"" bs
                            ON bs.""ID"" = bse.""BlocksetID""
                        WHERE EXISTS (
                            SELECT 1
                            FROM ""BlocklistHash"" blh
                            JOIN ""{m_tablename}"" mt
                                ON mt.""Hash"" = blh.""Hash""
                            WHERE
                                blh.""BlocksetID"" = bs.""ID""
                                AND mt.""Restored"" = @Restored
                        )
                ")
                    .SetParameterValue("@HashesPerBlock", hashesPerBlock)
                    .SetParameterValue("@Restored", 0)
                    .ExecuteNonQueryAsync();

                    cmd.SetCommandAndParameters($@"
                        SELECT
                            ""BlockHash"",
                            ""BlocksetId"",
                            ""BlocksetLength"",
                            ""BlocklistHashIndex"",
                            ""BlocklistHashHash"",
                            ""BlocksetEntryIndex""
                        FROM ""{blocklistTableName}""
                        ORDER BY
                            ""BlocksetId"",
                            ""BlocklistHashIndex"",
                            ""BlocksetEntryIndex""
                    ");

                    await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                    {
                        var hash = rd.ConvertValueToString(0) ?? throw new Exception("Block.Hash is null");
                        var blocksetId = rd.ConvertValueToInt64(1);
                        var length = rd.ConvertValueToInt64(2);
                        var blocklistHashIndex = rd.ConvertValueToInt64(3);
                        var blocklistHash = rd.ConvertValueToString(4) ?? throw new Exception("BlocklistHash is null");
                        var index = rd.ConvertValueToInt64(5);

                        yield return new BlocklistHashesEntry(
                            blocksetId,
                            blocklistHash,
                            length,
                            blocklistHashIndex,
                            (int)index,
                            hash
                        );
                    }
                }
                finally
                {
                    try
                    {
                        await cmd.ExecuteNonQueryAsync($@"
                            DROP TABLE IF EXISTS ""{blocklistTableName}""
                        ");
                    }
                    catch { }
                }
            }

            /// <inheritdoc/>
            public async Task<long> GetMissingBlockCount()
            {
                return await m_missingBlocksCountCommand
                    .SetTransaction(m_transaction.Transaction)
                    .SetParameterValue("@Restored", 0)
                    .ExecuteScalarInt64Async(0);
            }

            /// <summary>
            /// Gets the list of missing blocks
            /// </summary>
            /// <returns>>A list of missing blocks</returns>
            public async IAsyncEnumerable<(string Hash, long Size)> GetMissingBlocks()
            {
                m_missingBlocksCommand
                    .SetTransaction(m_transaction.Transaction)
                    .SetParameterValue("@Restored", 0);

                await foreach (var rd in m_missingBlocksCommand.ExecuteReaderEnumerableAsync())
                    yield return (rd.ConvertValueToString(0) ?? "", rd.ConvertValueToInt64(1));
            }

            /// <inheritdoc/>
            public async Task<long> MoveBlocksToNewVolume(long targetVolumeId, long sourceVolumeId)
            {
                if (targetVolumeId <= 0)
                    throw new ArgumentOutOfRangeException(nameof(targetVolumeId), "Target volume ID must be greater than 0");

                // Move the source blocks into the DuplicateBlock table
                using var cmd = m_connection.CreateCommand($@"
                    INSERT OR IGNORE INTO ""DuplicateBlock"" (
                        ""BlockID"",
                        ""VolumeID""
                    )
                    SELECT
                        b.""ID"",
                        b.""VolumeID""
                    FROM ""Block"" b
                    WHERE
                        b.""VolumeID"" = @SourceVolumeId
                        AND (b.""Hash"", b.""Size"") IN (
                            SELECT
                                ""Hash"",
                                ""Size""
                            FROM ""{m_tablename}""
                            WHERE ""Restored"" = @Restored
                        )
                ")
                    .SetParameterValue("@SourceVolumeId", sourceVolumeId)
                    .SetParameterValue("@Restored", 1);

                var moved = await cmd.ExecuteNonQueryAsync();

                // Then update the blocks table to point to the new volume
                var updated = await cmd.SetCommandAndParameters($@"
                    UPDATE ""Block""
                    SET ""VolumeID"" = @TargetVolumeId
                    WHERE
                        ""VolumeID"" = @SourceVolumeId
                        AND (""Hash"", ""Size"") IN (
                            SELECT
                                ""Hash"",
                                ""Size""
                            FROM ""{m_tablename}""
                            WHERE ""Restored"" = @Restored
                        )
                ")
                    .SetParameterValue("@TargetVolumeId", targetVolumeId)
                    .SetParameterValue("@SourceVolumeId", sourceVolumeId)
                    .SetParameterValue("@Restored", 1)
                    .ExecuteNonQueryAsync();

                if (updated != moved)
                    throw new Exception($"Unexpected number of updated blocks: {updated} != {moved}");

                return updated;
            }

            public async IAsyncEnumerable<IRemoteVolume> GetFilesetsUsingMissingBlocks()
            {
                var blocks = $@"
                    SELECT DISTINCT ""FileLookup"".""ID"" AS ID
                    FROM
                        ""{m_tablename}"",
                        ""Block"",
                        ""Blockset"",
                        ""BlocksetEntry"",
                        ""FileLookup""
                    WHERE
                        ""Block"".""Hash"" = ""{m_tablename}"".""Hash""
                        AND ""Block"".""Size"" = ""{m_tablename}"".""Size""
                        AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID""
                        AND ""BlocksetEntry"".""BlocksetID"" = ""Blockset"".""ID""
                        AND ""FileLookup"".""BlocksetID"" = ""Blockset"".""ID""
                ";
                var blocklists = $@"
                    SELECT DISTINCT ""FileLookup"".""ID"" AS ID
                    FROM
                        ""{m_tablename}"",
                        ""Block"",
                        ""Blockset"",
                        ""BlocklistHash"",
                        ""FileLookup""
                    WHERE
                        ""Block"".""Hash"" = ""{m_tablename}"".""Hash""
                        AND ""Block"".""Size"" = ""{m_tablename}"".""Size""
                        AND ""BlocklistHash"".""Hash"" = ""Block"".""Hash""
                        AND ""BlocklistHash"".""BlocksetID"" = ""Blockset"".""ID""
                        AND ""FileLookup"".""BlocksetID"" = ""Blockset"".""ID""
                ";
                var cmdtxt = $@"
                    SELECT DISTINCT
                        ""RemoteVolume"".""Name"",
                        ""RemoteVolume"".""Hash"",
                        ""RemoteVolume"".""Size""
                    FROM
                        ""RemoteVolume"",
                        ""FilesetEntry"",
                        ""Fileset""
                    WHERE
                        ""RemoteVolume"".""ID"" = ""Fileset"".""VolumeID""
                        AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID""
                        AND ""RemoteVolume"".""Type"" = @Type
                        AND ""FilesetEntry"".""FileID"" IN (
                            SELECT DISTINCT ""ID""
                            FROM (
                                {blocks} UNION {blocklists}
                            )
                        )
                ";

                using var cmd = m_connection.CreateCommand(cmdtxt)
                    .SetTransaction(m_transaction.Transaction)
                    .SetParameterValue("@Type", RemoteVolumeType.Files.ToString());

                await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                    yield return new RemoteVolume(
                        rd.ConvertValueToString(0) ?? "",
                        rd.ConvertValueToString(1) ?? "",
                        rd.ConvertValueToInt64(2)
                    );
            }

            public async IAsyncEnumerable<IRemoteVolume> GetMissingBlockSources()
            {
                using var cmd = m_connection.CreateCommand($@"
                    SELECT DISTINCT
                        ""RemoteVolume"".""Name"",
                        ""RemoteVolume"".""Hash"",
                        ""RemoteVolume"".""Size""
                    FROM
                        ""RemoteVolume"",
                        ""Block"",
                        ""{m_tablename}""
                    WHERE
                        ""{m_tablename}"".""Restored"" = @Restored
                        AND ""Block"".""Hash"" = ""{m_tablename}"".""Hash""
                        AND ""Block"".""Size"" = ""{m_tablename}"".""Size""
                        AND ""Block"".""VolumeID"" = ""RemoteVolume"".""ID""
                        AND ""Remotevolume"".""Name"" != @Name
                ")
                    .SetTransaction(m_transaction.Transaction)
                    .SetParameterValue("@Restored", 0)
                    .SetParameterValue("@Name", m_volumename);

                await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                    yield return new RemoteVolume(
                        rd.ConvertValueToString(0) ?? "",
                        rd.ConvertValueToString(1) ?? "",
                        rd.ConvertValueToInt64(2)
                    );
            }

            public void Dispose()
            {
                if (m_isDisposed)
                    return;

                DisposeAsync().Await();
            }

            public async Task DisposeAsync()
            {
                if (m_isDisposed)
                    return;

                m_isDisposed = true;
                try
                {
                    if (m_tablename != null)
                    {

                        using var cmd = await m_connection.CreateCommandAsync($@"DROP TABLE IF EXISTS ""{m_tablename}""");
                        await cmd.SetTransaction(m_transaction.Transaction)
                            .ExecuteNonQueryAsync();
                    }
                }
                catch { }

                try { m_insertCommand?.Dispose(); }
                catch { }
            }
        }

        public async Task<IMissingBlockList> CreateBlockList(string volumename)
        {
            return await MissingBlockList.CreateMissingBlockList(volumename, m_connection, m_rtr);
        }

        public async Task FixDuplicateMetahash()
        {
            using var cmd = m_connection.CreateCommand(m_rtr.Transaction);

            var sql_count = @"
                SELECT COUNT(*)
                FROM (
                    SELECT DISTINCT c1
                    FROM (
                        SELECT COUNT(*) AS ""C1""
                        FROM (
                            SELECT DISTINCT ""BlocksetID""
                            FROM ""Metadataset""
                        )
                        UNION SELECT COUNT(*) AS ""C1""
                        FROM ""Metadataset""
                    )
                )
            ";

            var x = await cmd.ExecuteScalarInt64Async(sql_count, 0);

            if (x > 1)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateMetadataHashes", "Found duplicate metadatahashes, repairing");

                var tablename = "TmpFile-" + Guid.NewGuid().ToString("N");

                await cmd.ExecuteNonQueryAsync($@"
                    CREATE TEMPORARY TABLE ""{tablename}""
                    AS SELECT * FROM ""File""
                ");

                var sql = @"
                    SELECT
                        ""A"".""ID"",
                        ""B"".""BlocksetID""
                    FROM (
                        SELECT
                            MIN(""ID"") AS ""ID"",
                            COUNT(""ID"") AS ""Duplicates""
                        FROM ""Metadataset""
                        GROUP BY ""BlocksetID""
                    ) ""A"",
                        ""Metadataset"" ""B""
                    WHERE
                        ""A"".""Duplicates"" > 1
                        AND ""A"".""ID"" = ""B"".""ID""
                ";

                using (var c2 = m_connection.CreateCommand(m_rtr.Transaction))
                {
                    c2.SetCommandAndParameters($@"
                        UPDATE ""{tablename}""
                        SET ""MetadataID"" = @MetadataId
                        WHERE ""MetadataID"" IN (
                            SELECT ""ID""
                            FROM ""Metadataset""
                            WHERE ""BlocksetID"" = @BlocksetId
                        );
                        DELETE FROM ""Metadataset""
                        WHERE
                            ""BlocksetID"" = @BlocksetId
                            AND ""ID"" != @MetadataId
                    ");

                    using var rd = await cmd.ExecuteReaderAsync(sql);

                    while (await rd.ReadAsync())
                    {
                        await c2.SetParameterValue("@MetadataId", rd.GetValue(0))
                            .SetParameterValue("@BlocksetId", rd.GetValue(1))
                            .ExecuteNonQueryAsync();
                    }
                }

                sql = $@"
                    SELECT
                        ""ID"",
                        ""Path"",
                        ""BlocksetID"",
                        ""MetadataID"",
                        ""Entries""
                    FROM (
                        SELECT
                            MIN(""ID"") AS ""ID"",
                            ""Path"",
                            ""BlocksetID"",
                            ""MetadataID"",
                            COUNT(*) as ""Entries""
                        FROM ""{tablename}""
                        GROUP BY
                            ""Path"",
                            ""BlocksetID"",
                            ""MetadataID""
                    )
                    WHERE ""Entries"" > 1
                    ORDER BY ""ID""
                ";

                using (var c2 = m_connection.CreateCommand(m_rtr.Transaction))
                {
                    c2.SetCommandAndParameters($@"
                        UPDATE ""FilesetEntry""
                        SET ""FileID"" = @FileId
                        WHERE ""FileID"" IN (
                            SELECT ""ID""
                            FROM ""{tablename}""
                            WHERE
                                ""Path"" = @Path
                                AND ""BlocksetID"" = @BlocksetId
                                AND ""MetadataID"" = @MetadataId
                        );
                        DELETE FROM ""{tablename}""
                        WHERE
                            ""Path"" = @Path
                            AND ""BlocksetID"" = @BlocksetId
                            AND ""MetadataID"" = @MetadataId
                            AND ""ID"" != @FileId
                    ");

                    await foreach (var rd in cmd.ExecuteReaderEnumerableAsync(sql))
                    {
                        await c2.SetParameterValue("@FileId", rd.GetValue(0))
                            .SetParameterValue("@Path", rd.GetValue(1))
                            .SetParameterValue("@BlocksetId", rd.GetValue(2))
                            .SetParameterValue("@MetadataId", rd.GetValue(3))
                            .ExecuteNonQueryAsync();
                    }
                }

                await cmd.ExecuteNonQueryAsync($@"
                    DELETE FROM ""FileLookup""
                    WHERE ""ID"" NOT IN (
                        SELECT ""ID""
                        FROM ""{tablename}""
                    )
                ");

                await cmd.ExecuteNonQueryAsync($@"
                    CREATE INDEX ""{tablename}-Ix""
                    ON  ""{tablename}"" (
                        ""ID"",
                        ""MetadataID""
                    )
                ");

                await cmd.ExecuteNonQueryAsync($@"
                    UPDATE ""FileLookup""
                    SET ""MetadataID"" = (
                        SELECT ""MetadataID""
                        FROM ""{tablename}"" A
                        WHERE ""A"".""ID"" = ""FileLookup"".""ID""
                    )
                ");

                await cmd.ExecuteNonQueryAsync($@"DROP TABLE ""{tablename}"" ");

                x = await cmd.ExecuteScalarInt64Async(sql_count, 0);
                if (x > 1)
                    throw new Interface.UserInformationException("Repair failed, there are still duplicate metadatahashes!", "DuplicateHashesRepairFailed");

                Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateMetadataHashesFixed", "Duplicate metadatahashes repaired succesfully");

                await m_rtr.CommitAsync();
            }
        }

        public async Task FixDuplicateFileentries()
        {
            using var cmd = m_connection.CreateCommand(m_rtr.Transaction);

            var sql_count = @"
                SELECT COUNT(*)
                FROM (
                    SELECT
                        ""PrefixID"",
                        ""Path"",
                        ""BlocksetID"",
                        ""MetadataID"",
                        COUNT(*) as ""Duplicates""
                    FROM ""FileLookup""
                    GROUP BY
                        ""PrefixID"",
                        ""Path"",
                        ""BlocksetID"",
                        ""MetadataID""
                )
                WHERE ""Duplicates"" > 1
            ";

            var x = await cmd.ExecuteScalarInt64Async(sql_count, 0);

            if (x > 0)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateFileEntries", "Found duplicate file entries, repairing");

                var sql = @"
                    SELECT
                        ""ID"",
                        ""PrefixID"",
                        ""Path"",
                        ""BlocksetID"",
                        ""MetadataID"",
                        ""Entries""
                    FROM (
                        SELECT
                            MIN(""ID"") AS ""ID"",
                            ""PrefixID"",
                            ""Path"",
                            ""BlocksetID"",
                            ""MetadataID"",
                            COUNT(*) as ""Entries""
                        FROM ""FileLookup""
                        GROUP BY
                            ""PrefixID"",
                            ""Path"",
                            ""BlocksetID"",
                            ""MetadataID""
                    )
                    WHERE ""Entries"" > 1
                    ORDER BY ""ID""
                ";

                using (var c2 = m_connection.CreateCommand(m_rtr.Transaction))
                {
                    c2.SetCommandAndParameters(@"
                        UPDATE ""FilesetEntry""
                        SET ""FileID"" = @FileId
                        WHERE ""FileID"" IN (
                            SELECT ""ID""
                            FROM ""FileLookup""
                            WHERE
                                ""PrefixID"" = @PrefixId
                                AND ""Path"" = @Path
                                AND ""BlocksetID"" = @BlocksetId
                                AND ""MetadataID"" = @MetatadataId
                        );
                        DELETE FROM ""FileLookup""
                        WHERE
                            ""PrefixID"" = @PrefixId
                            AND ""Path"" = @Path
                            AND ""BlocksetID"" = @BlocksetId
                            AND ""MetadataID"" = @MetadataId
                            AND ""ID"" != @FileId
                    ");

                    cmd.SetCommandAndParameters(sql);

                    await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                    {
                        await c2.SetParameterValue("@FileId", rd.GetValue(0))
                            .SetParameterValue("@PrefixId", rd.GetValue(1))
                            .SetParameterValue("@Path", rd.GetValue(2))
                            .SetParameterValue("@BlocksetId", rd.GetValue(3))
                            .SetParameterValue("@MetadataId", rd.GetValue(4))
                            .ExecuteNonQueryAsync();
                    }
                }

                x = await cmd.ExecuteScalarInt64Async(sql_count, 0);

                if (x > 1)
                    throw new Interface.UserInformationException("Repair failed, there are still duplicate file entries!", "DuplicateFilesRepairFailed");

                Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateFileEntriesFixed", "Duplicate file entries repaired succesfully");

                await m_rtr.CommitAsync();
            }

        }

        public async Task FixMissingBlocklistHashes(string blockhashalgorithm, long blocksize)
        {
            var blocklistbuffer = new byte[blocksize];

            using var cmd = m_connection.CreateCommand(m_rtr.Transaction);
            using var blockhasher = HashFactory.CreateHasher(blockhashalgorithm);

            var hashsize = blockhasher.HashSize / 8;

            var sql = $@"
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
            ";

            var countsql = @$"
                SELECT COUNT(*)
                FROM ({sql})
            ";

            var itemswithnoblocklisthash = await cmd.ExecuteScalarInt64Async(countsql, 0);

            if (itemswithnoblocklisthash != 0)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "MissingBlocklistHashes", "Found {0} missing blocklisthash entries, repairing", itemswithnoblocklisthash);
                using (var c2 = m_connection.CreateCommand(m_rtr.Transaction))
                using (var c3 = m_connection.CreateCommand(m_rtr.Transaction))
                using (var c4 = m_connection.CreateCommand(m_rtr.Transaction))
                using (var c5 = m_connection.CreateCommand(m_rtr.Transaction))
                using (var c6 = m_connection.CreateCommand(m_rtr.Transaction))
                {
                    c3.SetCommandAndParameters(@"
                        INSERT INTO ""BlocklistHash"" (
                            ""BlocksetID"",
                            ""Index"",
                            ""Hash""
                        )
                        VALUES (
                            @BlocksetId,
                            @Index,
                            @Hash
                        )
                    ");

                    c4.SetCommandAndParameters(@"
                        SELECT ""ID""
                        FROM ""Block""
                        WHERE
                            ""Hash"" = @Hash
                            AND ""Size"" = @Size
                    ");

                    c5.SetCommandAndParameters(@"
                        SELECT ""ID""
                        FROM ""DeletedBlock""
                        WHERE
                            ""Hash"" = @Hash
                            AND ""Size"" = @Size
                            AND ""VolumeID"" IN (
                                SELECT ""ID""
                                FROM ""RemoteVolume""
                                WHERE
                                    ""Type"" = @Type
                                    AND (
                                        ""State"" IN (
                                            @State1,
                                            @State2
                                        )
                                    )
                            )
                    ");

                    c6.SetCommandAndParameters(@"
                        INSERT INTO ""Block"" (
                            ""Hash"",
                            ""Size"",
                            ""VolumeID""
                        )
                        SELECT
                            ""Hash"",
                            ""Size"",
                            ""VolumeID""
                        FROM ""DeletedBlock""
                        WHERE ""ID"" = @DeletedBlockId
                        LIMIT 1;
                        DELETE FROM ""DeletedBlock""
                        WHERE ""ID"" = @DeletedBlockId;
                    ");

                    await foreach (var e in cmd.ExecuteReaderEnumerableAsync(sql))
                    {
                        var blocksetid = e.ConvertValueToInt64(0);
                        var ix = 0L;
                        int blocklistoffset = 0;

                        await c2.SetCommandAndParameters(@"
                            DELETE FROM ""BlocklistHash""
                            WHERE ""BlocksetID"" = @BlocksetId
                        ")
                            .SetParameterValue("@BlocksetId", blocksetid)
                            .ExecuteNonQueryAsync();

                        c2.SetCommandAndParameters(@"
                            SELECT ""A"".""Hash""
                            FROM
                                ""Block"" ""A"",
                                ""BlocksetEntry"" ""B""
                            WHERE
                                ""A"".""ID"" = ""B"".""BlockID""
                                AND ""B"".""BlocksetID"" = @BlocksetId
                            ORDER BY ""B"".""Index""
                        ")
                            .SetParameterValue("@BlocksetId", blocksetid);

                        await foreach (var h in c2.ExecuteReaderEnumerableAsync())
                        {
                            var tmp = Convert.FromBase64String(h.ConvertValueToString(0) ?? throw new Exception("Hash value was null"));
                            if (blocklistbuffer.Length - blocklistoffset < tmp.Length)
                            {
                                var blkey = Convert.ToBase64String(blockhasher.ComputeHash(blocklistbuffer, 0, blocklistoffset));

                                // Check if the block exists in "blocks"
                                var existingBlockId = await c4
                                    .SetParameterValue("@Hash", blkey)
                                    .SetParameterValue("@Size", blocklistoffset)
                                    .ExecuteScalarInt64Async(-1);

                                if (existingBlockId <= 0)
                                {
                                    var deletedBlockId = await c5
                                        .SetParameterValue("@Hash", blkey)
                                        .SetParameterValue("@Size", blocklistoffset)
                                        .SetParameterValue("@Type", RemoteVolumeType.Blocks.ToString())
                                        .SetParameterValue("@State1", RemoteVolumeState.Uploaded.ToString())
                                        .SetParameterValue("@State2", RemoteVolumeState.Verified.ToString())
                                        .ExecuteScalarInt64Async(-1);

                                    if (deletedBlockId <= 0)
                                        throw new Exception($"Missing block for blocklisthash: {blkey}");
                                    else
                                    {
                                        var rc = await c6
                                            .SetParameterValue("@DeletedBlockId", deletedBlockId)
                                            .ExecuteNonQueryAsync();

                                        if (rc != 2)
                                            throw new Exception($"Unexpected update count: {rc}");
                                    }
                                }

                                // Add to table
                                await c3.SetParameterValue("@BlocksetId", blocksetid)
                                    .SetParameterValue("@Index", ix)
                                    .SetParameterValue("@Hash", blkey)
                                    .ExecuteNonQueryAsync();

                                ix++;
                                blocklistoffset = 0;
                            }

                            Array.Copy(tmp, 0, blocklistbuffer, blocklistoffset, tmp.Length);
                            blocklistoffset += tmp.Length;

                        }

                        if (blocklistoffset != 0)
                        {
                            var blkeyfinal = Convert.ToBase64String(blockhasher.ComputeHash(blocklistbuffer, 0, blocklistoffset));

                            // Ensure that the block exists in "blocks"
                            var existingBlockId = await c4
                                .SetParameterValue("@Hash", blkeyfinal)
                                .SetParameterValue("@Size", blocklistoffset)
                                .ExecuteScalarInt64Async(-1);

                            if (existingBlockId <= 0)
                            {
                                var deletedBlockId = await c5
                                    .SetParameterValue("@Hash", blkeyfinal)
                                    .SetParameterValue("@Size", blocklistoffset)
                                    .SetParameterValue("@Type", RemoteVolumeType.Blocks.ToString())
                                    .SetParameterValue("@State1", RemoteVolumeState.Uploaded.ToString())
                                    .SetParameterValue("@State2", RemoteVolumeState.Verified.ToString())
                                    .ExecuteScalarInt64Async(-1);

                                if (deletedBlockId == 0)
                                    throw new Exception($"Missing block for blocklisthash: {blkeyfinal}");
                                else
                                {
                                    var rc = await c6
                                        .SetParameterValue("@DeletedBlockId", deletedBlockId)
                                        .ExecuteNonQueryAsync();
                                    if (rc != 2)
                                        throw new Exception($"Unexpected update count: {rc}");
                                }
                            }

                            // Add to table
                            await c3
                                .SetParameterValue("@BlocksetId", blocksetid)
                                .SetParameterValue("@Index", ix)
                                .SetParameterValue("@Hash", blkeyfinal)
                                .ExecuteNonQueryAsync();
                        }
                    }
                }

                itemswithnoblocklisthash = await cmd.ExecuteScalarInt64Async(countsql, 0);

                if (itemswithnoblocklisthash != 0)
                    throw new Interface.UserInformationException($"Failed to repair, after repair {itemswithnoblocklisthash} blocklisthashes were missing", "MissingBlocklistHashesRepairFailed");

                Logging.Log.WriteInformationMessage(LOGTAG, "MissingBlocklisthashesRepaired", "Missing blocklisthashes repaired succesfully");

                await m_rtr.CommitAsync();
            }

        }

        public async Task FixDuplicateBlocklistHashes(long blocksize, long hashsize)
        {
            using var cmd = m_connection.CreateCommand(m_rtr.Transaction);

            var dup_sql = @"
                SELECT *
                FROM (
                    SELECT
                        ""BlocksetID"",
                        ""Index"",
                        COUNT(*) AS ""EC""
                    FROM ""BlocklistHash""
                    GROUP BY
                        ""BlocksetID"",
                        ""Index""
                )
                WHERE ""EC"" > 1
            ";

            var sql_count = @$"
                SELECT COUNT(*)
                FROM ({dup_sql})
            ";

            var x = await cmd.ExecuteScalarInt64Async(sql_count, 0);

            if (x > 0)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateBlocklistHashes", "Found duplicate blocklisthash entries, repairing");

                var unique_count = await cmd.ExecuteScalarInt64Async(@"
                    SELECT COUNT(*)
                    FROM (
                        SELECT DISTINCT
                            ""BlocksetID"",
                            ""Index""
                        FROM ""BlocklistHash""
                    )
                ", 0);

                using (var c2 = m_connection.CreateCommand(m_rtr.Transaction))
                {
                    c2.SetCommandAndParameters(@"
                        DELETE FROM ""BlocklistHash""
                        WHERE rowid IN (
                            SELECT rowid
                            FROM ""BlocklistHash""
                            WHERE
                                ""BlocksetID"" = @BlocksetId
                                AND ""Index"" = @Index
                            LIMIT @Limit
                        )
                    ");

                    await foreach (var rd in cmd.ExecuteReaderEnumerableAsync(dup_sql))
                    {
                        var expected = rd.GetInt32(2) - 1;
                        var actual = await c2
                            .SetParameterValue("@BlocksetId", rd.GetValue(0))
                            .SetParameterValue("@Index", rd.GetValue(1))
                            .SetParameterValue("@Limit", expected)
                            .ExecuteNonQueryAsync();

                        if (actual != expected)
                            throw new Exception($"Unexpected number of results after fix, got: {actual}, expected: {expected}");
                    }
                }

                x = await cmd.ExecuteScalarInt64Async(sql_count);

                if (x > 1)
                    throw new Exception("Repair failed, there are still duplicate file entries!");

                var real_count = await cmd.ExecuteScalarInt64Async(@"
                    SELECT Count(*)
                    FROM ""BlocklistHash""
                ", 0);

                if (real_count != unique_count)
                    throw new Interface.UserInformationException($"Failed to repair, result should have been {unique_count} blocklist hashes, but result was {real_count} blocklist hashes", "DuplicateBlocklistHashesRepairFailed");

                try
                {
                    await VerifyConsistency(blocksize, hashsize, true, m_rtr.Transaction);
                }
                catch (Exception ex)
                {
                    throw new Interface.UserInformationException("Repaired blocklisthashes, but the database was broken afterwards, rolled back changes", "DuplicateBlocklistHashesRepairFailed", ex);
                }

                Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateBlocklistHashesRepaired", "Duplicate blocklisthashes repaired succesfully");

                await m_rtr.CommitAsync();
            }
        }

        public async Task CheckAllBlocksAreInVolume(string filename, IEnumerable<KeyValuePair<string, long>> blocks)
        {
            using var cmd = m_connection.CreateCommand(m_rtr.Transaction);
            var tablename = "ProbeBlocks-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

            try
            {
                await cmd.ExecuteNonQueryAsync($@"
                    CREATE TEMPORARY TABLE ""{tablename}"" (
                        ""Hash"" TEXT NOT NULL,
                        ""Size"" INTEGER NOT NULL
                    )
                ");

                cmd.SetCommandAndParameters($@"
                    INSERT INTO ""{tablename}"" (
                        ""Hash"",
                        ""Size""
                    )
                    VALUES (
                        @Hash,
                        @Size
                    )
                ");

                foreach (var kp in blocks)
                {
                    await cmd
                        .SetParameterValue("@Hash", kp.Key)
                        .SetParameterValue("@Size", kp.Value)
                        .ExecuteNonQueryAsync();
                }

                var id = await cmd.SetCommandAndParameters(@"
                    SELECT ""ID""
                    FROM ""RemoteVolume""
                    WHERE ""Name"" = @Name
                ")
                    .SetParameterValue("@Name", filename)
                    .ExecuteScalarInt64Async(-1);

                var aliens = await cmd.SetCommandAndParameters($@"
                    SELECT COUNT(*)
                    FROM (
                        SELECT ""A"".""VolumeID""
                        FROM ""{tablename}"" B
                        LEFT OUTER JOIN ""Block"" A
                            ON ""A"".""Hash"" = ""B"".""Hash""
                            AND ""A"".""Size"" = ""B"".""Size""
                    )
                    WHERE ""VolumeID"" != @VolumeId
                ")
                    .SetParameterValue("@VolumeId", id)
                    .ExecuteScalarInt64Async(0);

                if (aliens != 0)
                    throw new Exception($"Not all blocks were found in {filename}");
            }
            finally
            {
                await cmd.ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{tablename}"" ");
            }
        }

        public async Task CheckBlocklistCorrect(string hash, long length, IEnumerable<string> blocklist, long blocksize, long blockhashlength)
        {
            using var cmd = m_connection.CreateCommand(m_rtr.Transaction);

            var query = $@"
                SELECT
                    ""C"".""Hash"",
                    ""C"".""Size""
                FROM
                    ""BlocksetEntry"" A,
                    (
                        SELECT
                            ""Y"".""BlocksetID"",
                            ""Y"".""Hash"" AS ""BlocklistHash"",
                            ""Y"".""Index"" AS ""BlocklistHashIndex"",
                            ""Z"".""Size"" AS ""BlocklistSize"",
                            ""Z"".""ID"" AS ""BlocklistHashBlockID""
                        FROM
                            ""BlocklistHash"" Y,
                            ""Block"" Z
                        WHERE
                            ""Y"".""Hash"" = ""Z"".""Hash""
                            AND ""Y"".""Hash"" = @Hash
                            AND ""Z"".""Size"" = @Size
                        LIMIT 1
                    ) B,
                    ""Block"" C
                WHERE
                    ""A"".""BlocksetID"" = ""B"".""BlocksetID""
                    AND ""A"".""BlockID"" = ""C"".""ID""
                    AND ""A"".""Index"" >= ""B"".""BlocklistHashIndex"" * ({blocksize} / {blockhashlength})
                    AND ""A"".""Index"" < (""B"".""BlocklistHashIndex"" + 1) * ({blocksize} / {blockhashlength})
                ORDER BY ""A"".""Index""
                ";

            using var en = blocklist.GetEnumerator();

            cmd.SetCommandAndParameters(query)
                .SetParameterValue("@Hash", hash)
                .SetParameterValue("@Size", length);

            await foreach (var r in cmd.ExecuteReaderEnumerableAsync())
            {
                if (!en.MoveNext())
                    throw new Exception($"Too few entries in source blocklist with hash {hash}");
                if (en.Current != r.ConvertValueToString(0))
                    throw new Exception($"Mismatch in blocklist with hash {hash}");
            }

            if (en.MoveNext())
                throw new Exception($"Too many source blocklist entries in {hash}");
        }

        public async IAsyncEnumerable<string> MissingLocalFilesets()
        {
            using var cmd = m_connection.CreateCommand(@"
                SELECT ""Name""
                FROM ""RemoteVolume""
                WHERE
                    ""Type"" = @Type
                    AND ""State"" NOT IN (@States)
                    AND ""ID"" NOT IN (
                        SELECT ""VolumeID""
                        FROM ""Fileset""
                    )
            ")
                .SetTransaction(m_rtr.Transaction)
                .SetParameterValue("@Type", RemoteVolumeType.Files.ToString())
                .ExpandInClauseParameter("@States", [
                    RemoteVolumeState.Deleting.ToString(),
                    RemoteVolumeState.Deleted.ToString()
                ]);

            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                yield return rd.ConvertValueToString(0) ?? "";
        }

        public async IAsyncEnumerable<(long FilesetID, DateTime Timestamp, bool IsFull)> MissingRemoteFilesets()
        {
            using var cmd = m_connection.CreateCommand(@"
                SELECT
                    ""ID"",
                    ""Timestamp"",
                    ""IsFullBackup""
                FROM ""Fileset""
                WHERE ""VolumeID"" NOT IN (
                    SELECT ""ID""
                    FROM ""RemoteVolume""
                    WHERE
                        ""Type"" = @Type
                        AND ""State"" NOT IN (@States)
                )
            ")
                .SetTransaction(m_rtr.Transaction)
                .SetParameterValue("@Type", RemoteVolumeType.Files.ToString())
                .ExpandInClauseParameter("@States", [
                    RemoteVolumeState.Deleting.ToString(),
                    RemoteVolumeState.Deleted.ToString()
                ]);

            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                yield return (
                    rd.ConvertValueToInt64(0),
                    ParseFromEpochSeconds(rd.ConvertValueToInt64(1)),
                    rd.ConvertValueToInt64(2) == BackupType.FULL_BACKUP
                );
        }

        public async IAsyncEnumerable<IRemoteVolume> EmptyIndexFiles()
        {
            using var cmd = m_connection.CreateCommand(@"
                SELECT
                    ""Name"",
                    ""Hash"",
                    ""Size""
                FROM ""RemoteVolume""
                WHERE
                    ""Type"" = @Type
                    AND ""State"" IN (@States)
                    AND ""ID"" NOT IN (
                        SELECT ""IndexVolumeId""
                        FROM ""IndexBlockLink""
                    )
            ")
                .SetTransaction(m_rtr.Transaction)
                .SetParameterValue("@Type", RemoteVolumeType.Index.ToString())
                .ExpandInClauseParameter("@States", [
                    RemoteVolumeState.Uploaded.ToString(),
                    RemoteVolumeState.Verified.ToString()
                ]);

            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                yield return new RemoteVolume(
                    rd.ConvertValueToString(0) ?? "",
                    rd.ConvertValueToString(1) ?? "",
                    rd.ConvertValueToInt64(2)
                );
        }
    }

}
