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
        public async Task<(long FilesetId, DateTime Time, bool IsFullBackup)> GetFilesetFromRemotename(string filelist, SqliteTransaction? transaction)
        {
            using var cmd = m_connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.SetCommandAndParameters(@"SELECT ""Fileset"".""ID"", ""Fileset"".""Timestamp"", ""Fileset"".""IsFullBackup"" FROM ""Fileset"",""RemoteVolume"" WHERE ""Fileset"".""VolumeID"" = ""RemoteVolume"".""ID"" AND ""RemoteVolume"".""Name"" = @Name");
            cmd.SetParameterValue("@Name", filelist);
            var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                throw new Exception($"No such remote file: {filelist}");

            return (rd.ConvertValueToInt64(0, -1), ParseFromEpochSeconds(rd.ConvertValueToInt64(1)).ToLocalTime(), rd.GetInt32(2) == BackupType.FULL_BACKUP);
        }

        /// <summary>
        /// Moves entries in the FilesetEntry table from previous fileset to current fileset
        /// </summary>
        /// <param name="filesetid">Current fileset ID</param>
        /// <param name="prevFilesetId">Source fileset ID</param>
        /// <param name="transaction">The transaction</param>
        public async Task MoveFilesFromFileset(long filesetid, long prevFilesetId, SqliteTransaction transaction)
        {
            if (filesetid <= 0)
                throw new ArgumentException("filesetid must be > 0");

            if (prevFilesetId <= 0)
                throw new ArgumentException("prevId must be > 0");

            using var cmd = m_connection.CreateCommand();
            cmd.SetCommandAndParameters(@"UPDATE ""FilesetEntry"" SET ""FilesetID"" = @CurrentFilesetId WHERE ""FilesetID"" = @PreviousFilesetId ");
            cmd.Transaction = transaction;
            cmd.SetParameterValue("@CurrentFilesetId", filesetid);
            cmd.SetParameterValue("@PreviousFilesetId", prevFilesetId);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Gets the list of index files that reference a given block file
        /// </summary>
        /// <param name="blockfileid">The block file ID</param>
        /// <returns>>A list of index file IDs</returns>
        public async IAsyncEnumerable<string> GetIndexFilesReferencingBlockFile(long blockfileid, SqliteTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand(@"SELECT ""RemoteVolume"".""Name"" FROM ""RemoteVolume"", ""IndexBlockLink"" WHERE ""IndexBlockLink"".""BlockVolumeID"" = @BlockFileId AND ""RemoteVolume"".""ID"" = ""IndexBlockLink"".""IndexVolumeID"" AND ""RemoteVolume"".""Type"" = @Type");
            cmd.Transaction = transaction;
            cmd.SetParameterValue("@BlockFileId", blockfileid);
            cmd.SetParameterValue("@Type", RemoteVolumeType.Index.ToString());

            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                yield return rd.ConvertValueToString(0) ?? throw new Exception("RemoteVolume name was null");
        }

        /// <summary>
        /// Gets a list of filesets that are missing files
        /// </summary>
        /// <param name="transaction">An optional transaction</param>
        /// <returns>A list of fileset IDs and timestamps</returns>
        public async IAsyncEnumerable<KeyValuePair<long, DateTime>> GetFilesetsWithMissingFiles(SqliteTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand();
            cmd.Transaction = transaction;
            using var rd = await cmd.ExecuteReaderAsync(@"SELECT ID, Timestamp FROM Fileset WHERE ID IN (SELECT FilesetID FROM ""FilesetEntry"" WHERE ""FileID"" NOT IN (SELECT ""ID"" FROM ""FileLookup""))");
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
        public async Task<int> DeleteFilesetEntries(long filesetid, SqliteTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.SetCommandAndParameters(@"DELETE FROM ""FilesetEntry"" WHERE ""FilesetID"" = @FilesetId");
            cmd.SetParameterValue("@FilesetId", filesetid);
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

        public async IAsyncEnumerable<IRemoteVolume> GetBlockVolumesFromIndexName(string indexName, SqliteTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand(@"SELECT ""Name"", ""Hash"", ""Size"" FROM ""RemoteVolume"" WHERE ""ID"" IN (SELECT ""BlockVolumeID"" FROM ""IndexBlockLink"" WHERE ""IndexVolumeID"" IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" = @Name)) ");
            cmd.Transaction = transaction;
            cmd.SetParameterValue("@Name", indexName);

            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                yield return new RemoteVolume(rd.ConvertValueToString(0) ?? "", rd.ConvertValueToString(1) ?? "", rd.ConvertValueToInt64(2));
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
                using (var cmd = connection.CreateCommand())
                {
                    cmd.SetTransaction(transaction.Transaction);
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
                        WHERE ""Block"".""VolumeID"" = ""Remotevolume"".""ID""
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
                    WHERE ""Hash"" = @Hash
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
                    WHERE ""Block"".""Hash"" = @Hash
                    AND ""Block"".""Size"" = @Size
                ");

                blocklist.m_assignBlocksToNewVolume = await connection.CreateCommandAsync(@"
                    UPDATE ""Block""
                    SET ""VolumeID"" = @TargetVolumeId
                    WHERE ""Hash"" = @Hash
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
                //m_insertCommand.SetTransaction(m_transaction.Transaction);
                m_insertCommand.Transaction = m_transaction.Transaction;
                m_insertCommand.SetParameterValue("@NewRestoredValue", 1);
                m_insertCommand.SetParameterValue("@Hash", hash);
                m_insertCommand.SetParameterValue("@Size", size);
                m_insertCommand.SetParameterValue("@PreviousRestoredValue", 0);
                var restored = await m_insertCommand.ExecuteNonQueryAsync() == 1;

                if (restored)
                {
                    m_copyIntoDuplicatedBlocks.Transaction = m_transaction.Transaction;
                    m_copyIntoDuplicatedBlocks.SetParameterValue("@Hash", hash);
                    m_copyIntoDuplicatedBlocks.SetParameterValue("@Size", size);
                    await m_copyIntoDuplicatedBlocks.ExecuteNonQueryAsync();

                    m_assignBlocksToNewVolume.Transaction = m_transaction.Transaction;
                    m_assignBlocksToNewVolume.SetParameterValue("@TargetVolumeId", targetVolumeId);
                    m_assignBlocksToNewVolume.SetParameterValue("@Hash", hash);
                    m_assignBlocksToNewVolume.SetParameterValue("@Size", size);
                    var c = await m_assignBlocksToNewVolume.ExecuteNonQueryAsync();

                    if (c != 1)
                        throw new Exception($"Unexpected number of updated blocks: {c} != 1");
                }

                return restored;
            }

            /// <inheritdoc/>
            public async IAsyncEnumerable<BlockWithSourceData> GetSourceFilesWithBlocks(long blocksize)
            {
                using var cmd = m_connection.CreateCommand(FormatInvariant($@"SELECT DISTINCT ""{m_tablename}"".""Hash"", ""{m_tablename}"".""Size"", ""File"".""Path"", ""BlocksetEntry"".""Index"" * {blocksize} FROM  ""{m_tablename}"", ""Block"", ""BlocksetEntry"", ""File"" WHERE ""File"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID"" AND ""Block"".""ID"" = ""BlocksetEntry"".""BlockID"" AND ""{m_tablename}"".""Hash"" = ""Block"".""Hash"" AND ""{m_tablename}"".""Size"" = ""Block"".""Size"" AND ""{m_tablename}"".""Restored"" = @Restored ORDER BY ""{m_tablename}"".""Hash"", ""{m_tablename}"".""Size"", ""File"".""Path"", ""BlocksetEntry"".""Index"" * {blocksize}"));
                cmd.Transaction = m_transaction.Transaction;
                cmd.SetParameterValue("@Restored", 0);

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
                using var cmd = m_connection.CreateCommand(FormatInvariant($@"SELECT DISTINCT ""{m_tablename}"".""Hash"", ""{m_tablename}"".""Size"", ""File"".""Path"" FROM  ""{m_tablename}"", ""Block"", ""BlocksetEntry"", ""Metadataset"", ""File"" WHERE ""File"".""MetadataID"" == ""Metadataset"".""ID"" AND ""Metadataset"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID"" AND ""Block"".""ID"" = ""BlocksetEntry"".""BlockID"" AND ""{m_tablename}"".""Hash"" = ""Block"".""Hash"" AND ""{m_tablename}"".""Size"" = ""Block"".""Size"" AND ""{m_tablename}"".""Restored"" = @Restored ORDER BY ""{m_tablename}"".""Hash"", ""{m_tablename}"".""Size"", ""File"".""Path"""));
                cmd.Transaction = m_transaction.Transaction;
                cmd.SetParameterValue("@Restored", 0);

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
                using var cmd = m_connection.CreateCommand();
                cmd.Transaction = m_transaction.Transaction;
                var blocklistTableName = $"BlocklistHashList-{Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray())}";
                try
                {
                    // We need to create a snapshot as we will be updating the m_tablename table during enumeration
                    cmd.SetCommandAndParameters(FormatInvariant($@"
                        CREATE TEMPORARY TABLE ""{blocklistTableName}"" AS
                        SELECT
                            b.""Hash"" AS ""BlockHash"",
                            bs.""Id"" AS ""BlocksetId"",
                            bs.""Length"" AS ""BlocksetLength"",
                            bse.""Index"" / @HashesPerBlock AS ""BlocklistHashIndex"",
                            (
                                SELECT blh.""Hash""
                                FROM ""BlocklistHash"" blh
                                WHERE blh.""BlocksetID"" = bs.""ID""
                                AND blh.""Index"" == bse.""Index"" / @HashesPerBlock
                                LIMIT 1
                            ) AS ""BlocklistHashHash"",
                            bse.""Index"" AS ""BlocksetEntryIndex""
                        FROM
                            ""BlocksetEntry"" bse
                        JOIN ""Block"" b ON b.""ID"" = bse.""BlockID""
                        JOIN ""Blockset"" bs ON bs.""ID"" = bse.""BlocksetID""
                        WHERE
                            EXISTS (
                                SELECT 1
                                FROM ""BlocklistHash"" blh
                                JOIN ""{m_tablename}"" mt ON mt.""Hash"" = blh.""Hash""
                                WHERE blh.""BlocksetID"" = bs.""ID""
                                AND mt.""Restored"" = @Restored
                            )
                "));
                    cmd.SetParameterValue("@HashesPerBlock", hashesPerBlock);
                    cmd.SetParameterValue("@Restored", 0);
                    await cmd.ExecuteNonQueryAsync();

                    cmd.SetCommandAndParameters(FormatInvariant($@"
                        SELECT
                            ""BlockHash"",
                            ""BlocksetId"",
                            ""BlocksetLength"",
                            ""BlocklistHashIndex"",
                            ""BlocklistHashHash"",
                            ""BlocksetEntryIndex""
                        FROM ""{blocklistTableName}""
                        ORDER BY ""BlocksetId"", ""BlocklistHashIndex"", ""BlocksetEntryIndex"""));

                    await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                    {
                        var hash = rd.ConvertValueToString(0) ?? throw new Exception("Block.Hash is null");
                        var blocksetId = rd.ConvertValueToInt64(1);
                        var length = rd.ConvertValueToInt64(2);
                        var blocklistHashIndex = rd.ConvertValueToInt64(3);
                        var blocklistHash = rd.ConvertValueToString(4) ?? throw new Exception("BlocklistHash is null");
                        var index = rd.ConvertValueToInt64(5);

                        yield return new BlocklistHashesEntry(blocksetId, blocklistHash, length, blocklistHashIndex, (int)index, hash);
                    }
                }
                finally
                {
                    try { await cmd.ExecuteNonQueryAsync(FormatInvariant($@"DROP TABLE IF EXISTS ""{blocklistTableName}"" ")); }
                    catch { }
                }
            }

            /// <inheritdoc/>
            public async Task<long> GetMissingBlockCount()
            {
                using var cmd = m_connection.CreateCommand(FormatInvariant($@"SELECT COUNT(*) FROM ({m_missingBlocksQuery})"));
                cmd.Transaction = m_transaction.Transaction;
                cmd.SetParameterValue("@Restored", 0);

                return await cmd.ExecuteScalarInt64Async(0);
            }

            /// <summary>
            /// Gets the list of missing blocks
            /// </summary>
            /// <returns>>A list of missing blocks</returns>
            public async IAsyncEnumerable<(string Hash, long Size)> GetMissingBlocks()
            {
                using var cmd = m_connection.CreateCommand(m_missingBlocksQuery);
                cmd.Transaction = m_transaction.Transaction;
                cmd.SetParameterValue("@Restored", 0);

                await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                    yield return (rd.ConvertValueToString(0) ?? "", rd.ConvertValueToInt64(1));
            }

            /// <inheritdoc/>
            public async Task<long> MoveBlocksToNewVolume(long targetVolumeId, long sourceVolumeId)
            {
                if (targetVolumeId <= 0)
                    throw new ArgumentOutOfRangeException(nameof(targetVolumeId), "Target volume ID must be greater than 0");

                // Move the source blocks into the DuplicateBlock table
                using var cmd = m_connection.CreateCommand(FormatInvariant($@"
                    INSERT OR IGNORE INTO ""DuplicateBlock"" (""BlockID"", ""VolumeID"")
                    SELECT b.""ID"", b.""VolumeID""
                    FROM ""Block"" b
                    WHERE b.""VolumeID"" = @SourceVolumeId
                    AND (b.""Hash"", b.""Size"") IN (
                        SELECT ""Hash"", ""Size""
                        FROM ""{m_tablename}""
                        WHERE ""Restored"" = @Restored
                    )
                "));
                cmd.SetParameterValue("@SourceVolumeId", sourceVolumeId);
                cmd.SetParameterValue("@Restored", 1);

                var moved = await cmd.ExecuteNonQueryAsync();

                // Then update the blocks table to point to the new volume
                cmd.SetCommandAndParameters(FormatInvariant($@"
                    UPDATE ""Block""
                    SET ""VolumeID"" = @TargetVolumeId
                    WHERE ""VolumeID"" = @SourceVolumeId
                    AND (""Hash"", ""Size"") IN (
                        SELECT ""Hash"", ""Size""
                        FROM ""{m_tablename}""
                        WHERE ""Restored"" = @Restored
                    )
                "));
                cmd.SetParameterValue("@TargetVolumeId", targetVolumeId);
                cmd.SetParameterValue("@SourceVolumeId", sourceVolumeId);
                cmd.SetParameterValue("@Restored", 1);

                var updated = await cmd.ExecuteNonQueryAsync();
                if (updated != moved)
                    throw new Exception($"Unexpected number of updated blocks: {updated} != {moved}");

                return updated;
            }

            public async IAsyncEnumerable<IRemoteVolume> GetFilesetsUsingMissingBlocks()
            {
                var blocks = FormatInvariant($@"SELECT DISTINCT ""FileLookup"".""ID"" AS ID FROM ""{m_tablename}"", ""Block"", ""Blockset"", ""BlocksetEntry"", ""FileLookup"" WHERE ""Block"".""Hash"" = ""{m_tablename}"".""Hash"" AND ""Block"".""Size"" = ""{m_tablename}"".""Size"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" AND ""BlocksetEntry"".""BlocksetID"" = ""Blockset"".""ID"" AND ""FileLookup"".""BlocksetID"" = ""Blockset"".""ID"" ");
                var blocklists = FormatInvariant($@"SELECT DISTINCT ""FileLookup"".""ID"" AS ID FROM ""{m_tablename}"", ""Block"", ""Blockset"", ""BlocklistHash"", ""FileLookup"" WHERE ""Block"".""Hash"" = ""{m_tablename}"".""Hash"" AND ""Block"".""Size"" = ""{m_tablename}"".""Size"" AND ""BlocklistHash"".""Hash"" = ""Block"".""Hash"" AND ""BlocklistHash"".""BlocksetID"" = ""Blockset"".""ID"" AND ""FileLookup"".""BlocksetID"" = ""Blockset"".""ID"" ");
                var cmdtxt = FormatInvariant($@"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"" FROM ""RemoteVolume"", ""FilesetEntry"", ""Fileset"" WHERE ""RemoteVolume"".""ID"" = ""Fileset"".""VolumeID"" AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID"" AND ""RemoteVolume"".""Type"" = @Type AND ""FilesetEntry"".""FileID"" IN  (SELECT DISTINCT ""ID"" FROM ( {blocks} UNION {blocklists} ))");

                using var cmd = m_connection.CreateCommand(cmdtxt);
                cmd.Transaction = m_transaction.Transaction;
                cmd.SetParameterValue("@Type", RemoteVolumeType.Files.ToString());

                await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                    yield return new RemoteVolume(rd.ConvertValueToString(0) ?? "", rd.ConvertValueToString(1) ?? "", rd.ConvertValueToInt64(2));
            }

            public async IAsyncEnumerable<IRemoteVolume> GetMissingBlockSources()
            {
                using var cmd = m_connection.CreateCommand(FormatInvariant($@"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"" FROM ""RemoteVolume"", ""Block"", ""{m_tablename}"" WHERE ""{m_tablename}"".""Restored"" = @Restored AND ""Block"".""Hash"" = ""{m_tablename}"".""Hash"" AND ""Block"".""Size"" = ""{m_tablename}"".""Size"" AND ""Block"".""VolumeID"" = ""RemoteVolume"".""ID"" AND ""Remotevolume"".""Name"" != @Name "));
                cmd.Transaction = m_transaction.Transaction;
                cmd.SetParameterValue("@Restored", 0);
                cmd.SetParameterValue("@Name", m_volumename);

                await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                    yield return new RemoteVolume(rd.ConvertValueToString(0) ?? "", rd.ConvertValueToString(1) ?? "", rd.ConvertValueToInt64(2));
            }

            public void Dispose()
            {
                if (m_isDisposed)
                    return;

                m_isDisposed = true;
                try
                {
                    if (m_tablename != null)
                    {

                        using var cmd = m_connection.CreateCommand();
                        cmd.Transaction = m_transaction.Transaction;
                        cmd.ExecuteNonQueryAsync(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_tablename}"" ")).Await();
                    }
                }
                catch { }

                try { m_insertCommand?.Dispose(); }
                catch { }
            }
        }

        public IMissingBlockList CreateBlockList(string volumename, ReusableTransaction transaction)
        {
            return new MissingBlockList(volumename, m_connection, transaction);
        }

        public async Task FixDuplicateMetahash()
        {
            using var tr = m_connection.BeginTransaction();
            using var cmd = m_connection.CreateCommand();

            cmd.Transaction = tr;
            var sql_count =
                @"SELECT COUNT(*) FROM (" +
                @" SELECT DISTINCT c1 FROM (" +
                @"SELECT COUNT(*) AS ""C1"" FROM (SELECT DISTINCT ""BlocksetID"" FROM ""Metadataset"") UNION SELECT COUNT(*) AS ""C1"" FROM ""Metadataset"" " +
                @")" +
                @")";

            var x = await cmd.ExecuteScalarInt64Async(sql_count, 0);
            if (x > 1)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateMetadataHashes", "Found duplicate metadatahashes, repairing");

                var tablename = "TmpFile-" + Guid.NewGuid().ToString("N");

                await cmd.ExecuteNonQueryAsync(FormatInvariant($@"CREATE TEMPORARY TABLE ""{tablename}"" AS SELECT * FROM ""File"""));

                var sql = @"SELECT ""A"".""ID"", ""B"".""BlocksetID"" FROM (SELECT MIN(""ID"") AS ""ID"", COUNT(""ID"") AS ""Duplicates"" FROM ""Metadataset"" GROUP BY ""BlocksetID"") ""A"", ""Metadataset"" ""B"" WHERE ""A"".""Duplicates"" > 1 AND ""A"".""ID"" = ""B"".""ID""";

                using (var c2 = m_connection.CreateCommand())
                {
                    c2.Transaction = tr;
                    c2.SetCommandAndParameters(FormatInvariant($@"UPDATE ""{tablename}"" SET ""MetadataID"" = @MetadataId WHERE ""MetadataID"" IN (SELECT ""ID"" FROM ""Metadataset"" WHERE ""BlocksetID"" = @BlocksetId)")
                        + @"; DELETE FROM ""Metadataset"" WHERE ""BlocksetID"" = @BlocksetId AND ""ID"" != @MetadataId");
                    using (var rd = await cmd.ExecuteReaderAsync(sql))
                        while (await rd.ReadAsync())
                        {
                            c2.SetParameterValue("@MetadataId", rd.GetValue(0));
                            c2.SetParameterValue("@BlocksetId", rd.GetValue(1));
                            await c2.ExecuteNonQueryAsync();
                        }
                }

                sql = FormatInvariant($@"SELECT ""ID"", ""Path"", ""BlocksetID"", ""MetadataID"", ""Entries"" FROM (
                            SELECT MIN(""ID"") AS ""ID"", ""Path"", ""BlocksetID"", ""MetadataID"", COUNT(*) as ""Entries"" FROM ""{tablename}"" GROUP BY ""Path"", ""BlocksetID"", ""MetadataID"")
                            WHERE ""Entries"" > 1 ORDER BY ""ID""");

                using (var c2 = m_connection.CreateCommand())
                {
                    c2.Transaction = tr;
                    c2.SetCommandAndParameters(FormatInvariant($@"UPDATE ""FilesetEntry"" SET ""FileID"" = @FileId WHERE ""FileID"" IN (SELECT ""ID"" FROM ""{tablename}"" WHERE ""Path"" = @Path AND ""BlocksetID"" = @BlocksetId AND ""MetadataID"" = @MetadataId)")
                        + FormatInvariant($@"; DELETE FROM ""{tablename}"" WHERE ""Path"" = @Path AND ""BlocksetID"" = @BlocksetId AND ""MetadataID"" = @MetadataId AND ""ID"" != @FileId"));

                    await foreach (var rd in cmd.ExecuteReaderEnumerableAsync(sql))
                    {
                        c2.SetParameterValue("@FileId", rd.GetValue(0));
                        c2.SetParameterValue("@Path", rd.GetValue(1));
                        c2.SetParameterValue("@BlocksetId", rd.GetValue(2));
                        c2.SetParameterValue("@MetadataId", rd.GetValue(3));
                        await c2.ExecuteNonQueryAsync();
                    }
                }

                await cmd.ExecuteNonQueryAsync(FormatInvariant($@"DELETE FROM ""FileLookup"" WHERE ""ID"" NOT IN (SELECT ""ID"" FROM ""{tablename}"") "));
                await cmd.ExecuteNonQueryAsync(FormatInvariant($@"CREATE INDEX ""{tablename}-Ix"" ON  ""{tablename}"" (""ID"", ""MetadataID"")"));
                await cmd.ExecuteNonQueryAsync(FormatInvariant($@"UPDATE ""FileLookup"" SET ""MetadataID"" = (SELECT ""MetadataID"" FROM ""{tablename}"" A WHERE ""A"".""ID"" = ""FileLookup"".""ID"") "));
                await cmd.ExecuteNonQueryAsync(FormatInvariant($@"DROP TABLE ""{tablename}"" "));

                x = await cmd.ExecuteScalarInt64Async(sql_count, 0);
                if (x > 1)
                    throw new Interface.UserInformationException("Repair failed, there are still duplicate metadatahashes!", "DuplicateHashesRepairFailed");

                Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateMetadataHashesFixed", "Duplicate metadatahashes repaired succesfully");
                await tr.CommitAsync();
            }
        }

        public async Task FixDuplicateFileentries()
        {
            using var tr = m_connection.BeginTransaction();
            using var cmd = m_connection.CreateCommand();

            cmd.Transaction = tr;
            var sql_count = @"SELECT COUNT(*) FROM (SELECT ""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"", COUNT(*) as ""Duplicates"" FROM ""FileLookup"" GROUP BY ""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"") WHERE ""Duplicates"" > 1";

            var x = await cmd.ExecuteScalarInt64Async(sql_count, 0);
            if (x > 0)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateFileEntries", "Found duplicate file entries, repairing");

                var sql = @"SELECT ""ID"", ""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"", ""Entries"" FROM (
                            SELECT MIN(""ID"") AS ""ID"", ""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"", COUNT(*) as ""Entries"" FROM ""FileLookup"" GROUP BY ""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"")
                            WHERE ""Entries"" > 1 ORDER BY ""ID""";

                using (var c2 = m_connection.CreateCommand())
                {
                    c2.Transaction = tr;
                    c2.SetCommandAndParameters(@"UPDATE ""FilesetEntry"" SET ""FileID"" = @FileId WHERE ""FileID"" IN (SELECT ""ID"" FROM ""FileLookup"" WHERE ""PrefixID"" = @PrefixId AND ""Path"" = @Path AND ""BlocksetID"" = @BlocksetId AND ""MetadataID"" = @MetatadataId)"
                        + @"; DELETE FROM ""FileLookup"" WHERE ""PrefixID"" = @PrefixId AND ""Path"" = @Path AND ""BlocksetID"" = @BlocksetId AND ""MetadataID"" = @MetadataId AND ""ID"" != @FileId");
                    cmd.SetCommandAndParameters(sql);

                    await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                    {
                        c2.SetParameterValue("@FileId", rd.GetValue(0));
                        c2.SetParameterValue("@PrefixId", rd.GetValue(1));
                        c2.SetParameterValue("@Path", rd.GetValue(2));
                        c2.SetParameterValue("@BlocksetId", rd.GetValue(3));
                        c2.SetParameterValue("@MetadataId", rd.GetValue(4));
                        await c2.ExecuteNonQueryAsync();
                    }
                }

                x = await cmd.ExecuteScalarInt64Async(sql_count, 0);
                if (x > 1)
                    throw new Interface.UserInformationException("Repair failed, there are still duplicate file entries!", "DuplicateFilesRepairFailed");

                Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateFileEntriesFixed", "Duplicate file entries repaired succesfully");
                await tr.CommitAsync();
            }

        }

        public async Task FixMissingBlocklistHashes(string blockhashalgorithm, long blocksize)
        {
            var blocklistbuffer = new byte[blocksize];

            using var tr = m_connection.BeginTransaction();
            using var cmd = m_connection.CreateCommand();
            using var blockhasher = HashFactory.CreateHasher(blockhashalgorithm);

            cmd.Transaction = tr;
            var hashsize = blockhasher.HashSize / 8;

            var sql = FormatInvariant($@"SELECT * FROM (SELECT ""N"".""BlocksetID"", ((""N"".""BlockCount"" + {blocksize / hashsize} - 1) / {blocksize / hashsize}) AS ""BlocklistHashCountExpected"", CASE WHEN ""G"".""BlocklistHashCount"" IS NULL THEN 0 ELSE ""G"".""BlocklistHashCount"" END AS ""BlocklistHashCountActual"" FROM (SELECT ""BlocksetID"", COUNT(*) AS ""BlockCount"" FROM ""BlocksetEntry"" GROUP BY ""BlocksetID"") ""N"" LEFT OUTER JOIN (SELECT ""BlocksetID"", COUNT(*) AS ""BlocklistHashCount"" FROM ""BlocklistHash"" GROUP BY ""BlocksetID"") ""G"" ON ""N"".""BlocksetID"" = ""G"".""BlocksetID"" WHERE ""N"".""BlockCount"" > 1) WHERE ""BlocklistHashCountExpected"" != ""BlocklistHashCountActual""");
            var countsql = @"SELECT COUNT(*) FROM (" + sql + @")";

            var itemswithnoblocklisthash = await cmd.ExecuteScalarInt64Async(countsql, 0);
            if (itemswithnoblocklisthash != 0)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "MissingBlocklistHashes", "Found {0} missing blocklisthash entries, repairing", itemswithnoblocklisthash);
                using (var c2 = m_connection.CreateCommand())
                using (var c3 = m_connection.CreateCommand())
                using (var c4 = m_connection.CreateCommand())
                using (var c5 = m_connection.CreateCommand())
                using (var c6 = m_connection.CreateCommand())
                {
                    c2.Transaction = tr;
                    c3.Transaction = tr;
                    c4.Transaction = tr;
                    c5.Transaction = tr;
                    c6.Transaction = tr;
                    c3.SetCommandAndParameters(@"INSERT INTO ""BlocklistHash"" (""BlocksetID"", ""Index"", ""Hash"") VALUES (@BlocksetId, @Index, @Hash) ");
                    c4.SetCommandAndParameters(@"SELECT ""ID"" FROM ""Block"" WHERE ""Hash"" = @Hash AND ""Size"" = @Size");
                    c5.SetCommandAndParameters(@"SELECT ""ID"" FROM ""DeletedBlock"" WHERE ""Hash"" = @Hash AND ""Size"" = @Size AND ""VolumeID"" IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Type"" = @Type AND (""State"" IN (@State1, @State2)))");
                    c6.SetCommandAndParameters(@"INSERT INTO ""Block"" (""Hash"", ""Size"", ""VolumeID"") SELECT ""Hash"", ""Size"", ""VolumeID"" FROM ""DeletedBlock"" WHERE ""ID"" = @DeletedBlockId LIMIT 1; DELETE FROM ""DeletedBlock"" WHERE ""ID"" = @DeletedBlockId;");

                    await foreach (var e in cmd.ExecuteReaderEnumerableAsync(sql))
                    {
                        var blocksetid = e.ConvertValueToInt64(0);
                        var ix = 0L;
                        int blocklistoffset = 0;

                        c2.SetCommandAndParameters(@"DELETE FROM ""BlocklistHash"" WHERE ""BlocksetID"" = @BlocksetId");
                        c2.SetParameterValue("@BlocksetId", blocksetid);
                        await c2.ExecuteNonQueryAsync();

                        c2.SetCommandAndParameters(@"SELECT ""A"".""Hash"" FROM ""Block"" ""A"", ""BlocksetEntry"" ""B"" WHERE ""A"".""ID"" = ""B"".""BlockID"" AND ""B"".""BlocksetID"" = @BlocksetId ORDER BY ""B"".""Index""");
                        c2.SetParameterValue("@BlocksetId", blocksetid);

                        await foreach (var h in c2.ExecuteReaderEnumerableAsync())
                        {
                            var tmp = Convert.FromBase64String(h.ConvertValueToString(0) ?? throw new Exception("Hash value was null"));
                            if (blocklistbuffer.Length - blocklistoffset < tmp.Length)
                            {
                                var blkey = Convert.ToBase64String(blockhasher.ComputeHash(blocklistbuffer, 0, blocklistoffset));

                                // Check if the block exists in "blocks"
                                c4.SetParameterValue("@Hash", blkey);
                                c4.SetParameterValue("@Size", blocklistoffset);
                                var existingBlockId = await c4.ExecuteScalarInt64Async(-1);

                                if (existingBlockId <= 0)
                                {
                                    c5.SetParameterValue("@Hash", blkey);
                                    c5.SetParameterValue("@Size", blocklistoffset);
                                    c5.SetParameterValue("@Type", RemoteVolumeType.Blocks.ToString());
                                    c5.SetParameterValue("@State1", RemoteVolumeState.Uploaded.ToString());
                                    c5.SetParameterValue("@State2", RemoteVolumeState.Verified.ToString());
                                    var deletedBlockId = await c5.ExecuteScalarInt64Async(-1);

                                    if (deletedBlockId <= 0)
                                        throw new Exception($"Missing block for blocklisthash: {blkey}");
                                    else
                                    {
                                        c6.SetParameterValue("@DeletedBlockId", deletedBlockId);
                                        var rc = await c6.ExecuteNonQueryAsync();

                                        if (rc != 2)
                                            throw new Exception($"Unexpected update count: {rc}");
                                    }
                                }

                                // Add to table
                                c3.SetParameterValue("@BlocksetId", blocksetid);
                                c3.SetParameterValue("@Index", ix);
                                c3.SetParameterValue("@Hash", blkey);
                                await c3.ExecuteNonQueryAsync();

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
                            c4.SetParameterValue("@Hash", blkeyfinal);
                            c4.SetParameterValue("@Size", blocklistoffset);
                            var existingBlockId = await c4.ExecuteScalarInt64Async(-1);

                            if (existingBlockId <= 0)
                            {
                                c5.SetParameterValue("@Hash", blkeyfinal);
                                c5.SetParameterValue("@Size", blocklistoffset);
                                c5.SetParameterValue("@Type", RemoteVolumeType.Blocks.ToString());
                                c5.SetParameterValue("@State1", RemoteVolumeState.Uploaded.ToString());
                                c5.SetParameterValue("@State2", RemoteVolumeState.Verified.ToString());
                                var deletedBlockId = await c5.ExecuteScalarInt64Async(-1);

                                if (deletedBlockId == 0)
                                    throw new Exception($"Missing block for blocklisthash: {blkeyfinal}");
                                else
                                {
                                    c6.SetParameterValue("@DeletedBlockId", deletedBlockId);
                                    var rc = await c6.ExecuteNonQueryAsync();
                                    if (rc != 2)
                                        throw new Exception($"Unexpected update count: {rc}");
                                }
                            }

                            // Add to table
                            c3.SetParameterValue("@BlocksetId", blocksetid);
                            c3.SetParameterValue("@Index", ix);
                            c3.SetParameterValue("@Hash", blkeyfinal);
                            await c3.ExecuteNonQueryAsync();
                        }
                    }
                }

                itemswithnoblocklisthash = cmd.ExecuteScalarInt64(countsql, 0);
                if (itemswithnoblocklisthash != 0)
                    throw new Interface.UserInformationException($"Failed to repair, after repair {itemswithnoblocklisthash} blocklisthashes were missing", "MissingBlocklistHashesRepairFailed");

                Logging.Log.WriteInformationMessage(LOGTAG, "MissingBlocklisthashesRepaired", "Missing blocklisthashes repaired succesfully");
                tr.Commit();
            }

        }

        public async Task FixDuplicateBlocklistHashes(long blocksize, long hashsize)
        {
            using var tr = m_connection.BeginTransaction();
            using var cmd = m_connection.CreateCommand();

            cmd.Transaction = tr;
            var dup_sql = @"SELECT * FROM (SELECT ""BlocksetID"", ""Index"", COUNT(*) AS ""EC"" FROM ""BlocklistHash"" GROUP BY ""BlocksetID"", ""Index"") WHERE ""EC"" > 1";

            var sql_count = @"SELECT COUNT(*) FROM (" + dup_sql + ")";

            var x = await cmd.ExecuteScalarInt64Async(sql_count, 0);
            if (x > 0)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateBlocklistHashes", "Found duplicate blocklisthash entries, repairing");

                var unique_count = await cmd.ExecuteScalarInt64Async(@"SELECT COUNT(*) FROM (SELECT DISTINCT ""BlocksetID"", ""Index"" FROM ""BlocklistHash"")", 0);

                using (var c2 = m_connection.CreateCommand())
                {
                    c2.Transaction = tr;
                    c2.SetCommandAndParameters(@"DELETE FROM ""BlocklistHash"" WHERE rowid IN (SELECT rowid FROM ""BlocklistHash"" WHERE ""BlocksetID"" = @BlocksetId AND ""Index"" = @Index LIMIT @Limit)");
                    await foreach (var rd in cmd.ExecuteReaderEnumerableAsync(dup_sql))
                    {
                        var expected = rd.GetInt32(2) - 1;
                        c2.SetParameterValue("@BlocksetId", rd.GetValue(0));
                        c2.SetParameterValue("@Index", rd.GetValue(1));
                        c2.SetParameterValue("@Limit", expected);
                        var actual = await c2.ExecuteNonQueryAsync();

                        if (actual != expected)
                            throw new Exception($"Unexpected number of results after fix, got: {actual}, expected: {expected}");
                    }
                }

                x = await cmd.ExecuteScalarInt64Async(sql_count);
                if (x > 1)
                    throw new Exception("Repair failed, there are still duplicate file entries!");

                var real_count = await cmd.ExecuteScalarInt64Async(@"SELECT Count(*) FROM ""BlocklistHash""", 0);

                if (real_count != unique_count)
                    throw new Interface.UserInformationException($"Failed to repair, result should have been {unique_count} blocklist hashes, but result was {real_count} blocklist hashes", "DuplicateBlocklistHashesRepairFailed");

                try
                {
                    await VerifyConsistency(blocksize, hashsize, true, tr);
                }
                catch (Exception ex)
                {
                    throw new Interface.UserInformationException("Repaired blocklisthashes, but the database was broken afterwards, rolled back changes", "DuplicateBlocklistHashesRepairFailed", ex);
                }

                Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateBlocklistHashesRepaired", "Duplicate blocklisthashes repaired succesfully");
                await tr.CommitAsync();
            }
        }

        public async Task CheckAllBlocksAreInVolume(string filename, IEnumerable<KeyValuePair<string, long>> blocks, SqliteTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand();
            cmd.Transaction = transaction;
            var tablename = "ProbeBlocks-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
            try
            {
                await cmd.ExecuteNonQueryAsync(FormatInvariant($@"CREATE TEMPORARY TABLE ""{tablename}"" (""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL)"));
                cmd.SetCommandAndParameters(FormatInvariant($@"INSERT INTO ""{tablename}"" (""Hash"", ""Size"") VALUES (@Hash, @Size)"));

                foreach (var kp in blocks)
                {
                    cmd.SetParameterValue("@Hash", kp.Key);
                    cmd.SetParameterValue("@Size", kp.Value);
                    await cmd.ExecuteNonQueryAsync();
                }

                cmd.SetCommandAndParameters(@"SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" = @Name");
                cmd.SetParameterValue("@Name", filename);
                var id = await cmd.ExecuteScalarInt64Async(-1);

                cmd.SetCommandAndParameters(FormatInvariant($@"SELECT COUNT(*) FROM (SELECT ""A"".""VolumeID"" FROM ""{tablename}"" B LEFT OUTER JOIN ""Block"" A ON ""A"".""Hash"" = ""B"".""Hash"" AND ""A"".""Size"" = ""B"".""Size"") WHERE ""VolumeID"" != @VolumeId "));
                cmd.SetParameterValue("@VolumeId", id);
                var aliens = await cmd.ExecuteScalarInt64Async(0);

                if (aliens != 0)
                    throw new Exception($"Not all blocks were found in {filename}");
            }
            finally
            {
                await cmd.ExecuteNonQueryAsync(FormatInvariant($@"DROP TABLE IF EXISTS ""{tablename}"" "));
            }
        }

        public async Task CheckBlocklistCorrect(string hash, long length, IEnumerable<string> blocklist, long blocksize, long blockhashlength, SqliteTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand();

            cmd.Transaction = transaction;
            var query = FormatInvariant($@"
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
            ""Y"".""Hash"" = ""Z"".""Hash"" AND ""Y"".""Hash"" = @Hash AND ""Z"".""Size"" = @Size
        LIMIT 1
    ) B,
    ""Block"" C
WHERE
    ""A"".""BlocksetID"" = ""B"".""BlocksetID""
    AND
    ""A"".""BlockID"" = ""C"".""ID""
    AND
    ""A"".""Index"" >= ""B"".""BlocklistHashIndex"" * ({blocksize} / {blockhashlength})
    AND
    ""A"".""Index"" < (""B"".""BlocklistHashIndex"" + 1) * ({blocksize} / {blockhashlength})
ORDER BY
    ""A"".""Index""

");

            using var en = blocklist.GetEnumerator();

            cmd.SetCommandAndParameters(query);
            cmd.SetParameterValue("@Hash", hash);
            cmd.SetParameterValue("@Size", length);
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

        public async IAsyncEnumerable<string> MissingLocalFilesets(SqliteTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand(@"SELECT ""Name"" FROM ""RemoteVolume"" WHERE ""Type"" = @Type AND ""State"" NOT IN (@States) AND ""ID"" NOT IN (SELECT ""VolumeID"" FROM ""Fileset"")");
            cmd.Transaction = transaction;
            cmd.SetParameterValue("@Type", RemoteVolumeType.Files.ToString());
            cmd.ExpandInClauseParameter("@States", [RemoteVolumeState.Deleting.ToString(), RemoteVolumeState.Deleted.ToString()]);

            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                yield return rd.ConvertValueToString(0) ?? "";
        }

        public async IAsyncEnumerable<(long FilesetID, DateTime Timestamp, bool IsFull)> MissingRemoteFilesets(SqliteTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand(@"SELECT ""ID"", ""Timestamp"", ""IsFullBackup"" FROM ""Fileset"" WHERE ""VolumeID"" NOT IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Type"" = @Type AND ""State"" NOT IN (@States))");
            cmd.Transaction = transaction;
            cmd.SetParameterValue("@Type", RemoteVolumeType.Files.ToString());
            cmd.ExpandInClauseParameter("@States", [RemoteVolumeState.Deleting.ToString(), RemoteVolumeState.Deleted.ToString()]);

            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                yield return (rd.ConvertValueToInt64(0), ParseFromEpochSeconds(rd.ConvertValueToInt64(1)), rd.ConvertValueToInt64(2) == BackupType.FULL_BACKUP);
        }

        public async IAsyncEnumerable<IRemoteVolume> EmptyIndexFiles(SqliteTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand(@"SELECT ""Name"", ""Hash"", ""Size"" FROM ""RemoteVolume"" WHERE ""Type"" = @Type AND ""State"" IN (@States) AND ""ID"" NOT IN (SELECT ""IndexVolumeId"" FROM ""IndexBlockLink"")");
            cmd.Transaction = transaction;
            cmd.SetParameterValue("@Type", RemoteVolumeType.Index.ToString());
            cmd.ExpandInClauseParameter("@States", [RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString()]);

            await foreach (var rd in cmd.ExecuteReaderEnumerableAsync())
                yield return new RemoteVolume(rd.ConvertValueToString(0) ?? "", rd.ConvertValueToString(1) ?? "", rd.ConvertValueToInt64(2));
        }
    }
}

