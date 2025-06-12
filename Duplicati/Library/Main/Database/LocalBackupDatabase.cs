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
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using Microsoft.Data.Sqlite;

namespace Duplicati.Library.Main.Database
{

    internal class LocalBackupDatabase : LocalDatabase
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<LocalBackupDatabase>();

        private SqliteCommand m_findblockCommand = null!;
        private SqliteCommand m_findblocksetCommand = null!;
        private SqliteCommand m_findfilesetCommand = null!;
        private SqliteCommand m_findmetadatasetCommand = null!;

        private SqliteCommand m_insertblockCommand = null!;

        private SqliteCommand m_insertfileCommand = null!;

        private SqliteCommand m_insertblocksetCommand = null!;
        private SqliteCommand m_insertblocksetentryCommand = null!;
        private SqliteCommand m_insertblocklistHashesCommand = null!;

        private SqliteCommand m_insertmetadatasetCommand = null!;

        private SqliteCommand m_findfileCommand = null!;
        private SqliteCommand m_selectfilelastmodifiedCommand = null!;
        private SqliteCommand m_selectfilelastmodifiedWithSizeCommand = null!;
        private SqliteCommand m_selectfileHashCommand = null!;

        private SqliteCommand m_insertfileOperationCommand = null!;
        private SqliteCommand m_selectfilemetadatahashandsizeCommand = null!;
        private SqliteCommand m_getfirstfilesetwithblockinblockset = null!;

        private HashSet<string> m_blocklistHashes = [];

        /// <summary>
        /// The temporary table with deleted blocks that can be re-used; null if not table is used
        /// </summary>
        private string? m_tempDeletedBlockTable;
        /// <summary>
        /// The in-mmeory lookup for deleted blocks; null if in-memory lookup is not used
        /// </summary>
        private Dictionary<string, Dictionary<long, long>>? m_deletedBlockLookup;
        /// <summary>
        /// The command used to move deleted blocks to the main block table; null if not used
        /// </summary>
        private SqliteCommand? m_moveblockfromdeletedCommand;
        /// <summary>
        /// The command used to find blocks in the deleted blocks table; null if not used
        /// </summary>
        private SqliteCommand? m_findindeletedCommand;

        private long m_filesetId;

        private bool m_logQueries;

        public static async Task<LocalBackupDatabase> CreateAsync(string path, Options options, LocalBackupDatabase? dbnew = null)
        {
            dbnew ??= new LocalBackupDatabase();

            dbnew = (LocalBackupDatabase)
                await CreateLocalDatabaseAsync(path, "Backup", false, options.SqlitePageCache, dbnew)
                .ConfigureAwait(false);
            dbnew = await CreateAsync(dbnew, options).ConfigureAwait(false);
            dbnew.ShouldCloseConnection = true;

            return dbnew;
        }

        public static async Task<LocalBackupDatabase> CreateAsync(LocalDatabase dbparent, Options options, LocalBackupDatabase? dbnew = null)
        {
            dbnew ??= new LocalBackupDatabase();

            dbnew = (LocalBackupDatabase)await CreateLocalDatabaseAsync(dbparent, dbnew)
                .ConfigureAwait(false);

            dbnew.m_logQueries = options.ProfileAllDatabaseQueries;

            dbnew.m_findblockCommand = await dbnew.Connection.CreateCommandAsync(@"
                SELECT ""ID""
                FROM ""Block""
                WHERE
                    ""Hash"" = @Hash
                    AND ""Size"" = @Size
            ")
                .ConfigureAwait(false);

            dbnew.m_findblocksetCommand = await dbnew.Connection.CreateCommandAsync(@"
                SELECT ""ID""
                FROM ""Blockset""
                WHERE
                    ""Fullhash"" = @Fullhash
                    AND ""Length"" = @Length
            ")
                .ConfigureAwait(false);

            dbnew.m_findmetadatasetCommand = await dbnew.Connection.CreateCommandAsync(@"
                SELECT ""A"".""ID""
                FROM
                    ""Metadataset"" A,
                    ""BlocksetEntry"" B,
                    ""Block"" C
                WHERE
                    ""A"".""BlocksetID"" = ""B"".""BlocksetID""
                    AND ""B"".""BlockID"" = ""C"".""ID""
                    AND ""C"".""Hash"" = @Hash
                    AND ""C"".""Size"" = @Size
            ")
                .ConfigureAwait(false);

            dbnew.m_findfilesetCommand = await dbnew.Connection.CreateCommandAsync(@"
                SELECT ""ID""
                FROM ""FileLookup""
                WHERE
                    ""BlocksetID"" = @BlocksetId
                    AND ""MetadataID"" = @MetadataId
                    AND ""Path"" = @Path
                    AND ""PrefixID"" = @PrefixId
            ")
                .ConfigureAwait(false);

            dbnew.m_insertblockCommand = await dbnew.Connection.CreateCommandAsync(@"
                INSERT INTO ""Block"" (
                    ""Hash"",
                    ""VolumeID"",
                    ""Size""
                )
                VALUES (
                    @Hash,
                    @VolumeId,
                    @Size
                );
                SELECT last_insert_rowid();
            ")
                .ConfigureAwait(false);

            dbnew.m_insertfileOperationCommand = await dbnew.Connection.CreateCommandAsync(@"
                INSERT INTO ""FilesetEntry"" (
                    ""FilesetID"",
                    ""FileID"",
                    ""Lastmodified""
                )
                VALUES (
                    @FilesetId,
                    @FileId,
                    @LastModified
                )
            ")
                .ConfigureAwait(false);

            dbnew.m_insertfileCommand = await dbnew.Connection.CreateCommandAsync(@"
                INSERT INTO ""FileLookup"" (
                    ""PrefixID"",
                    ""Path"",
                    ""BlocksetID"",
                    ""MetadataID""
                )
                VALUES (
                    @PrefixId,
                    @Path,
                    @BlocksetId,
                    @MetadataId
                );
                SELECT last_insert_rowid();
            ")
                .ConfigureAwait(false);

            dbnew.m_insertblocksetCommand = await dbnew.Connection.CreateCommandAsync(@"
                INSERT INTO ""Blockset"" (
                    ""Length"",
                    ""FullHash""
                )
                VALUES (
                    @Length,
                    @Fullhash
                );
                SELECT last_insert_rowid();")
                .ConfigureAwait(false);

            dbnew.m_insertblocksetentryCommand = await dbnew.Connection.CreateCommandAsync(@"
                INSERT INTO ""BlocksetEntry"" (
                    ""BlocksetID"",
                    ""Index"",
                    ""BlockID""
                )
                SELECT
                    @BlocksetId AS A,
                    @Index AS B,
                    ""ID""
                FROM ""Block""
                WHERE ""Hash"" = @Hash
                AND ""Size"" = @Size
            ")
                .ConfigureAwait(false);

            dbnew.m_insertblocklistHashesCommand = await dbnew.Connection.CreateCommandAsync(@"
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
            ")
                .ConfigureAwait(false);

            dbnew.m_insertmetadatasetCommand = await dbnew.Connection.CreateCommandAsync(@"
                INSERT INTO ""Metadataset"" (""BlocksetID"")
                VALUES (@BlocksetId);
                SELECT last_insert_rowid();
            ")
                .ConfigureAwait(false);

            dbnew.m_selectfilelastmodifiedCommand = await dbnew.Connection.CreateCommandAsync(@"
                SELECT
                    ""A"".""ID"",
                    ""B"".""LastModified""
                FROM (
                    SELECT ""ID""
                    FROM ""FileLookup""
                    WHERE ""PrefixID"" = @PrefixId
                    AND ""Path"" = @Path
                ) ""A""
                CROSS JOIN ""FilesetEntry"" ""B""
                WHERE
                    ""A"".""ID"" = ""B"".""FileID""
                    AND ""B"".""FilesetID"" = @FilesetId
            ")
                .ConfigureAwait(false);

            dbnew.m_selectfilelastmodifiedWithSizeCommand = await dbnew.Connection.CreateCommandAsync(@"
                SELECT
                    ""C"".""ID"",
                    ""C"".""LastModified"",
                    ""D"".""Length""
                FROM
                    (
                        SELECT
                            ""A"".""ID"",
                            ""B"".""LastModified"",
                            ""A"".""BlocksetID""
                        FROM (
                            SELECT
                                ""ID"",
                                ""BlocksetID""
                            FROM ""FileLookup""
                            WHERE
                                ""PrefixID"" = @PrefixId
                                AND ""Path"" = @Path
                        ) ""A""
                        CROSS JOIN ""FilesetEntry"" ""B""
                        WHERE
                            ""A"".""ID"" = ""B"".""FileID""
                            AND ""B"".""FilesetID"" = @FilesetId
                    ) AS ""C"",
                    ""Blockset"" AS ""D""
                WHERE ""C"".""BlocksetID"" == ""D"".""ID""
            ")
                .ConfigureAwait(false);

            dbnew.m_selectfilemetadatahashandsizeCommand = await dbnew.Connection.CreateCommandAsync(@"
                SELECT
                    ""Blockset"".""Length"",
                    ""Blockset"".""FullHash""
                FROM
                    ""Blockset"",
                    ""Metadataset"",
                    ""File""
                WHERE
                    ""File"".""ID"" = @FileId
                    AND ""Blockset"".""ID"" = ""Metadataset"".""BlocksetID""
                    AND ""Metadataset"".""ID"" = ""File"".""MetadataID""
            ")
                .ConfigureAwait(false);

            // Experimental toggling of the deleted block cache
            // If the value is less than zero, the lookup is disabled
            // meaning that deleted blocks are never reused (same as 2.1.0.5 and earlier)
            // A value of zero disables the in-memory cache, always using a temporary table
            // Any other value is the size of the in-memory cache
            // If the number of deleted blocks exceed the cache size, a temporary table is used
            var deletedBlockCacheSize = Environment.GetEnvironmentVariable("DUPLICATI_DELETEDBLOCKCACHESIZE");
            if (!long.TryParse(deletedBlockCacheSize, out var deletedBlockCacheSizeLong))
                deletedBlockCacheSizeLong = 10000;

            if (deletedBlockCacheSizeLong >= 0)
            {
                using (var cmd = dbnew.Connection.CreateCommand())
                {
                    dbnew.m_tempDeletedBlockTable = "DeletedBlock-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                    await cmd.SetCommandAndParameters($@"
                        CREATE TEMPORARY TABLE ""{dbnew.m_tempDeletedBlockTable}"" AS
                        SELECT
                            MAX(""ID"") AS ""ID"",
                            ""Hash"",
                            ""Size""
                        FROM ""DeletedBlock""
                        WHERE ""VolumeID"" IN (
                            SELECT ""ID""
                            FROM ""RemoteVolume""
                            WHERE ""State"" NOT IN (@States)
                        )
                        GROUP BY
                            ""Hash"",
                            ""Size""
                    ")
                        .ExpandInClauseParameterMssqlite("@States", [
                            RemoteVolumeState.Deleted,
                            RemoteVolumeState.Deleting
                        ])
                        .ExecuteNonQueryAsync()
                        .ConfigureAwait(false);

                    var deletedBlocks = await cmd.ExecuteScalarInt64Async(@$"
                        SELECT COUNT(*)
                        FROM ""{dbnew.m_tempDeletedBlockTable}""
                    ", 0)
                        .ConfigureAwait(false);

                    // There are no deleted blocks, so we can drop the table
                    if (deletedBlocks == 0)
                    {
                        await cmd.ExecuteNonQueryAsync($@"DROP TABLE ""{dbnew.m_tempDeletedBlockTable}""")
                            .ConfigureAwait(false);
                        dbnew.m_tempDeletedBlockTable = null;

                    }
                    // The deleted blocks are small enough to fit in memory
                    else if (deletedBlocks <= deletedBlockCacheSizeLong)
                    {
                        dbnew.m_deletedBlockLookup = new Dictionary<string, Dictionary<long, long>>();
                        cmd.SetCommandAndParameters(@$"
                            SELECT
                                ""ID"",
                                ""Hash"",
                                ""Size""
                            FROM ""{dbnew.m_tempDeletedBlockTable}""
                        ")
                            .ConfigureAwait(false);

                        using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                            while (await reader.ReadAsync().ConfigureAwait(false))
                            {
                                var id = reader.ConvertValueToInt64(0);
                                var hash = reader.ConvertValueToString(1) ?? throw new Exception("Hash is null");
                                var size = reader.ConvertValueToInt64(2);

                                if (!dbnew.m_deletedBlockLookup.TryGetValue(hash, out var sizes))
                                    dbnew.m_deletedBlockLookup[hash] = sizes = new Dictionary<long, long>();
                                sizes[size] = id;
                            }

                        await cmd.ExecuteNonQueryAsync($@"DROP TABLE ""{dbnew.m_tempDeletedBlockTable}""")
                            .ConfigureAwait(false);
                        dbnew.m_tempDeletedBlockTable = null;
                    }
                    // The deleted blocks are too large to fit in memory, so we use a temporary table
                    else
                    {
                        await cmd.ExecuteNonQueryAsync($@"
                            CREATE UNIQUE INDEX ""unique_{dbnew.m_tempDeletedBlockTable}""
                            ON ""{dbnew.m_tempDeletedBlockTable}"" (
                                ""Hash"",
                                ""Size""
                            )
                        ")
                            .ConfigureAwait(false);

                        dbnew.m_findindeletedCommand = await dbnew.Connection.CreateCommandAsync($@"
                            SELECT ""ID""
                            FROM ""{dbnew.m_tempDeletedBlockTable}""
                            WHERE
                                ""Hash"" = @Hash
                                AND ""Size"" = @Size
                        ")
                            .ConfigureAwait(false);

                        dbnew.m_moveblockfromdeletedCommand = await dbnew.Connection.CreateCommandAsync(@$"
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
                            WHERE ""ID"" = @DeletedBlockId LIMIT 1;

                            DELETE FROM ""DeletedBlock""
                            WHERE ""ID"" = @DeletedBlockId;

                            DELETE FROM ""{dbnew.m_tempDeletedBlockTable}""
                            WHERE ""ID"" = @DeletedBlockId;

                            SELECT last_insert_rowid()
                        ")
                            .ConfigureAwait(false);

                    }

                    if (deletedBlocks > 0)
                    {
                        dbnew.m_moveblockfromdeletedCommand = await dbnew.m_connection.CreateCommandAsync(@$"
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
                            WHERE ""ID"" = @DeletedBlockId LIMIT 1;

                            DELETE FROM ""DeletedBlock""
                            WHERE ""ID"" = @DeletedBlockId;

                            SELECT last_insert_rowid()
                        ")
                            .ConfigureAwait(false);
                    }

                }
            }

            // Allow users to test on real-world data
            // to get feedback on potential performance
            int.TryParse(Environment.GetEnvironmentVariable("TEST_QUERY_VERSION"), out var testqueryversion);

            if (testqueryversion != 0)
                Logging.Log.WriteWarningMessage(LOGTAG, "TestFileQuery", null, "Using performance test query version {0} as the TEST_QUERY_VERSION environment variable is set", testqueryversion);

            // The original query (v==1) finds the most recent entry of the file in question,
            // but it requires some large joins to extract the required information.
            // To speed it up, we use a slightly simpler approach that only looks at the
            // previous fileset, and uses information here.
            // If there is a case where a file is sometimes there and sometimes not
            // (i.e. filter file, remove filter) we will not find the file.
            // We currently use this faster version,
            // but allow users to switch back via an environment variable
            // such that we can get performance feedback

            string findQuery;
            switch (testqueryversion)
            {
                // The query used in Duplicati until 2.0.3.9
                case 1:
                    findQuery = @"
                        SELECT
                            ""FileLookup"".""ID"" AS ""FileID"",
                            ""FilesetEntry"".""Lastmodified"",
                            ""FileBlockset"".""Length"",
                            ""MetaBlockset"".""Fullhash"" AS ""Metahash"",
                            ""MetaBlockset"".""Length"" AS ""Metasize""
                        FROM
                            ""FileLookup"",
                            ""FilesetEntry"",
                            ""Fileset"",
                            ""Blockset"" ""FileBlockset"",
                            ""Metadataset"",
                            ""Blockset"" ""MetaBlockset""
                        WHERE
                            ""FileLookup"".""PrefixID"" = @PrefixId
                            AND ""FileLookup"".""Path"" = @Path
                            AND ""FilesetEntry"".""FileID"" = ""FileLookup"".""ID""
                            AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID""
                            AND ""FileBlockset"".""ID"" = ""FileLookup"".""BlocksetID""
                            AND ""Metadataset"".""ID"" = ""FileLookup"".""MetadataID""
                            AND ""MetaBlockset"".""ID"" = ""Metadataset"".""BlocksetID""
                            AND @FilesetId IS NOT NULL
                        ORDER BY ""Fileset"".""Timestamp"" DESC
                        LIMIT 1
                    ";
                    break;

                // The fastest reported query in Duplicati 2.0.3.10, but with "LIMIT 1" added
                default:
                case 2:
                    var getLastFileEntryForPath = @"
                        SELECT
                            ""A"".""ID"",
                            ""B"".""LastModified"",
                            ""A"".""BlocksetID"",
                            ""A"".""MetadataID""
                        FROM (
                            SELECT
                                ""ID"",
                                ""BlocksetID"",
                                ""MetadataID""
                            FROM ""FileLookup""
                            WHERE
                                ""PrefixID"" = @PrefixId
                                AND ""Path"" = @Path
                        ) ""A""
                        CROSS JOIN ""FilesetEntry"" ""B""
                        WHERE
                            ""A"".""ID"" = ""B"".""FileID""
                            AND ""B"".""FilesetID"" = @FilesetId
                    ";

                    findQuery = $@"
                        SELECT
                            ""C"".""ID"" AS ""FileID"",
                            ""C"".""LastModified"",
                            ""D"".""Length"",
                            ""E"".""FullHash"" as ""Metahash"",
                            ""E"".""Length"" AS ""Metasize""
                        FROM
                            ({getLastFileEntryForPath}) AS ""C"",
                            ""Blockset"" AS ""D"",
                            ""Blockset"" AS ""E"",
                            ""Metadataset"" ""F""
                        WHERE
                            ""C"".""BlocksetID"" == ""D"".""ID""
                            AND ""C"".""MetadataID"" == ""F"".""ID""
                            AND ""F"".""BlocksetID"" = ""E"".""ID""
                        LIMIT 1
                    ";
                    break;

                // Potentially faster query: https://forum.duplicati.com/t/release-2-0-3-10-canary-2018-08-30/4497/25
                case 3:
                    findQuery = @"
                        SELECT
                            FileLookup.ID as FileID,
                            FilesetEntry.Lastmodified,
                            FileBlockset.Length,
                            MetaBlockset.FullHash AS Metahash,
                            MetaBlockset.Length as Metasize
                        FROM FilesetEntry
                        INNER JOIN Fileset
                            ON (FileSet.ID = FilesetEntry.FilesetID)
                        INNER JOIN FileLookup
                            ON (FileLookup.ID = FilesetEntry.FileID)
                        INNER JOIN Metadataset
                            ON (Metadataset.ID = FileLookup.MetadataID)
                        INNER JOIN Blockset AS MetaBlockset
                            ON (MetaBlockset.ID = Metadataset.BlocksetID)
                        LEFT JOIN Blockset AS FileBlockset
                            ON (FileBlockset.ID = FileLookup.BlocksetID)
                        WHERE
                            FileLookup.PrefixID = @PrefixId
                            AND FileLookup.Path = @Path
                            AND FilesetID = @FilesetId
                        LIMIT 1
                    ";
                    break;

                // The slow query used in Duplicati 2.0.3.10, but with "LIMIT 1" added
                case 4:
                    findQuery = @"
                        SELECT
                            ""FileLookup"".""ID"" AS ""FileID"",
                            ""FilesetEntry"".""Lastmodified"",
                            ""FileBlockset"".""Length"",
                            ""MetaBlockset"".""Fullhash"" AS ""Metahash"",
                            ""MetaBlockset"".""Length"" AS ""Metasize""
                        FROM
                            ""FileLookup"",
                            ""FilesetEntry"",
                            ""Fileset"",
                            ""Blockset"" ""FileBlockset"",
                            ""Metadataset"",
                            ""Blockset"" ""MetaBlockset""
                        WHERE
                            ""FileLookup"".""PrefixID"" = @PrefixId
                            AND ""FileLookup"".""Path"" = @Path
                            AND ""Fileset"".""ID"" = @FilesetId
                            AND ""FilesetEntry"".""FileID"" = ""FileLookup"".""ID""
                            AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID""
                            AND ""FileBlockset"".""ID"" = ""FileLookup"".""BlocksetID""
                            AND ""Metadataset"".""ID"" = ""FileLookup"".""MetadataID""
                            AND ""MetaBlockset"".""ID"" = ""Metadataset"".""BlocksetID""
                        LIMIT 1
                    ";
                    break;

            }

            dbnew.m_findfileCommand = dbnew.m_connection.CreateCommand(findQuery);

            dbnew.m_selectfileHashCommand = dbnew.m_connection.CreateCommand(@"
                SELECT ""Blockset"".""Fullhash""
                FROM
                    ""Blockset"",
                    ""FileLookup""
                WHERE
                    ""Blockset"".""ID"" = ""FileLookup"".""BlocksetID""
                    AND ""FileLookup"".""ID"" = @FileId
            ");

            dbnew.m_getfirstfilesetwithblockinblockset = dbnew.m_connection.CreateCommand(@"
                SELECT MIN(""FilesetEntry"".""FilesetID"")
                FROM ""FilesetEntry""
                WHERE  ""FilesetEntry"".""FileID"" IN (
                    SELECT ""File"".""ID""
                    FROM ""File""
                    WHERE ""File"".""BlocksetID"" IN(
                        SELECT ""BlocklistHash"".""BlocksetID""
                        FROM ""BlocklistHash""
                        WHERE ""BlocklistHash"".""Hash"" = @Hash
                    )
                )
            ");

            dbnew.m_blocklistHashes = new HashSet<string>();

            return dbnew;
        }

        /// <summary>
        /// Probes to see if a block already exists
        /// </summary>
        /// <param name="key">The block key</param>
        /// <param name="size">The size of the block</param>
        /// <returns>True if the block should be added to the current output</returns>
        public async Task<long> FindBlockID(string key, long size)
        {
            return await m_findblockCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@Hash", key)
                .SetParameterValue("@Size", size)
                .ExecuteScalarInt64Async(m_logQueries, -1)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a block to the local database, returning a value indicating if the value presents a new block
        /// </summary>
        /// <param name="key">The block key</param>
        /// <param name="size">The size of the block</param>
        /// <returns>True if the block should be added to the current output</returns>
        public async Task<bool> AddBlock(string key, long size, long volumeid)
        {
            var r = await FindBlockID(key, size).ConfigureAwait(false);
            if (r == -1L)
            {
                if (m_moveblockfromdeletedCommand != null)
                {
                    if (m_deletedBlockLookup != null)
                    {
                        if (m_deletedBlockLookup.TryGetValue(key, out var sizes))
                            if (sizes.TryGetValue(size, out var id))
                            {
                                await m_moveblockfromdeletedCommand
                                    .SetTransaction(m_rtr)
                                    .SetParameterValue("@DeletedBlockId", id)
                                    .ExecuteNonQueryAsync(m_logQueries)
                                    .ConfigureAwait(false);

                                sizes.Remove(size);
                                if (sizes.Count == 0)
                                    m_deletedBlockLookup.Remove(key);
                                return false;
                            }
                    }
                    else if (m_findindeletedCommand != null)
                    {
                        // No transaction on the temporary table
                        var id = await m_findindeletedCommand
                            .SetTransaction(m_rtr)
                            .SetParameterValue("@Hash", key)
                            .SetParameterValue("@Size", size)
                            .ExecuteScalarInt64Async(m_logQueries, -1)
                            .ConfigureAwait(false);

                        if (id != -1)
                        {
                            var c = await m_moveblockfromdeletedCommand
                                .SetTransaction(m_rtr)
                                .SetParameterValue("@DeletedBlockId", id)
                                .ExecuteNonQueryAsync(m_logQueries)
                                .ConfigureAwait(false);

                            if (c != 2)
                                throw new Exception($"Failed to move block {key} with size {size}, result count: {c}");

                            // We do not clean up the temporary table, as the regular block lookup should now find it
                            return false;
                        }
                    }
                }

                var ins = await m_insertblockCommand
                    .SetTransaction(m_rtr)
                    .SetParameterValue("@Hash", key)
                    .SetParameterValue("@VolumeId", volumeid)
                    .SetParameterValue("@Size", size)
                    .ExecuteNonQueryAsync(m_logQueries)
                    .ConfigureAwait(false);

                if (ins != 1)
                    throw new Exception($"Failed to insert block {key} with size {size}, result count: {ins}");

                return true;
            }
            else
            {
                //Update lookup cache if required
                return false;
            }
        }


        /// <summary>
        /// Adds a blockset to the database, returns a value indicating if the blockset is new
        /// </summary>
        /// <param name="filehash">The hash of the blockset</param>
        /// <param name="size">The size of the blockset</param>
        /// <param name="hashes">The list of hashes</param>
        /// <param name="blocksetid">The id of the blockset, new or old</param>
        /// <returns>True if the blockset was created, false otherwise</returns>
        public async Task<(bool, long)> AddBlockset(string filehash, long size, int blocksize, IEnumerable<string> hashes, IEnumerable<string> blocklistHashes)
        {
            long blocksetid = await m_findblocksetCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@Fullhash", filehash)
                .SetParameterValue("@Length", size)
                .ExecuteScalarInt64Async(m_logQueries, -1)
                .ConfigureAwait(false);

            if (blocksetid != -1)
                return (false, blocksetid); //Found it

            blocksetid = await m_insertblocksetCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@Length", size)
                .SetParameterValue("@Fullhash", filehash)
                .ExecuteScalarInt64Async(m_logQueries)
                .ConfigureAwait(false);

            long ix = 0;
            if (blocklistHashes != null)
            {
                m_insertblocklistHashesCommand
                    .SetTransaction(m_rtr)
                    .SetParameterValue("@BlocksetId", blocksetid);

                foreach (var bh in blocklistHashes)
                {
                    var c = await m_insertblocklistHashesCommand
                        .SetParameterValue("@Index", ix)
                        .SetParameterValue("@Hash", bh)
                        .ExecuteNonQueryAsync(m_logQueries)
                        .ConfigureAwait(false);

                    if (c != 1)
                        throw new Exception($"Failed to insert blocklist hash {bh} for blockset {blocksetid}, result count: {c}");

                    ix++;
                }
            }

            m_insertblocksetentryCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@BlocksetId", blocksetid);

            ix = 0;
            long remainsize = size;
            foreach (var h in hashes)
            {
                var exsize = remainsize < blocksize ? remainsize : blocksize;
                var c = await m_insertblocksetentryCommand
                    .SetParameterValue("@Index", ix)
                    .SetParameterValue("@Hash", h)
                    .SetParameterValue("@Size", exsize)
                    .ExecuteNonQueryAsync(m_logQueries)
                    .ConfigureAwait(false);

                if (c != 1)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "CheckingErrorsForIssue1400", null, "Checking errors, related to #1400. Unexpected result count: {0}, expected {1}, hash: {2}, size: {3}, blocksetid: {4}, ix: {5}, fullhash: {6}, fullsize: {7}", c, 1, h, exsize, blocksetid, ix, filehash, size);
                    using (var cmd = m_connection.CreateCommand(m_rtr))
                    {
                        var bid = await cmd.SetCommandAndParameters(@"
                            SELECT ""ID""
                            FROM ""Block""
                            WHERE ""Hash"" = @Hash
                        ")
                            .SetParameterValue("@Hash", h)
                            .ExecuteScalarInt64Async(-1)
                            .ConfigureAwait(false);

                        if (bid == -1)
                            throw new Exception($"Could not find any blocks with the given hash: {h}");

                        cmd.SetCommandAndParameters(@"
                            SELECT ""Size""
                            FROM ""Block""
                            WHERE ""Hash"" = @Hash
                        ")
                            .SetParameterValue("@Hash", h);

                        await foreach (var rd in cmd.ExecuteReaderEnumerableAsync().ConfigureAwait(false))
                            Logging.Log.WriteErrorMessage(LOGTAG, "FoundIssue1400Error", null, "Found block with ID {0} and hash {1} and size {2}", bid, h, rd.ConvertValueToInt64(0, -1));
                    }

                    throw new Exception($"Unexpected result count: {c}, expected {1}, check log for more messages");
                }

                ix++;
                remainsize -= blocksize;
            }

            await m_rtr.CommitAsync().ConfigureAwait(false);

            return (true, blocksetid);
        }

        /// <summary>
        /// Gets the metadataset ID from the filehash
        /// </summary>
        /// <returns><c>true</c>, if metadataset found, false if does not exist.</returns>
        /// <param name="filehash">The metadata hash.</param>
        /// <param name="size">The size of the metadata.</param>
        /// <param name="metadataid">The ID of the metadataset.</param>
        /// <param name="transaction">An optional transaction.</param>
        public async Task<(bool, long)> GetMetadatasetID(string filehash, long size)
        {
            long metadataid;

            if (size > 0)
            {
                metadataid = await m_findmetadatasetCommand
                    .SetTransaction(m_rtr)
                    .SetParameterValue("@Hash", filehash)
                    .SetParameterValue("@Size", size)
                    .ExecuteScalarInt64Async(m_logQueries, -1)
                    .ConfigureAwait(false);

                return (metadataid != -1, metadataid);
            }

            metadataid = -2;
            return (false, metadataid);
        }

        /// <summary>
        /// Adds a metadata set to the database, and returns a value indicating if the record was new
        /// </summary>
        /// <param name="filehash">The metadata hash</param>
        /// <param name="size">The size of the metadata</param>
        /// <param name="transaction">The transaction to execute under</param>
        /// <param name="blocksetid">The id of the blockset to add</param>
        /// <param name="metadataid">The id of the metadata set</param>
        /// <returns>True if the set was added to the database, false otherwise</returns>
        public async Task<(bool, long)> AddMetadataset(string filehash, long size, long blocksetid)
        {
            var (metadatafound, metadataid) = await GetMetadatasetID(filehash, size)
                .ConfigureAwait(false);
            if (metadatafound)
                return (false, metadataid);

            metadataid = await m_insertmetadatasetCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@BlocksetId", blocksetid)
                .ExecuteScalarInt64Async(m_logQueries)
                .ConfigureAwait(false);

            await m_rtr.CommitAsync().ConfigureAwait(false);

            return (true, metadataid);
        }

        /// <summary>
        /// Adds a file record to the database
        /// </summary>
        /// <param name="pathprefixid">The path prefix ID</param>
        /// <param name="filename">The path to the file</param>
        /// <param name="lastmodified">The time the file was modified</param>
        /// <param name="blocksetID">The ID of the hashkey for the file</param>
        /// <param name="metadataID">The ID for the metadata</param>
        /// <param name="transaction">The transaction to use for insertion, or null for no transaction</param>
        public async Task AddFile(long pathprefixid, string filename, DateTime lastmodified, long blocksetID, long metadataID)
        {
            var fileidobj = await m_findfilesetCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@BlocksetId", blocksetID)
                .SetParameterValue("@MetadataId", metadataID)
                .SetParameterValue("@Path", filename)
                .SetParameterValue("@PrefixId", pathprefixid)
                .ExecuteScalarInt64Async(m_logQueries)
                .ConfigureAwait(false);

            if (fileidobj == -1)
            {
                fileidobj = await m_insertfileCommand.SetTransaction(m_rtr)
                    .SetParameterValue("@PrefixId", pathprefixid)
                    .SetParameterValue("@Path", filename)
                    .SetParameterValue("@BlocksetId", blocksetID)
                    .SetParameterValue("@MetadataId", metadataID)
                    .ExecuteScalarInt64Async(m_logQueries)
                    .ConfigureAwait(false);

                await m_rtr.CommitAsync().ConfigureAwait(false);
            }

            await AddKnownFile(fileidobj, lastmodified).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a file record to the database
        /// </summary>
        /// <param name="filename">The path to the file</param>
        /// <param name="lastmodified">The time the file was modified</param>
        /// <param name="blocksetID">The ID of the hashkey for the file</param>
        /// <param name="metadataID">The ID for the metadata</param>
        /// <param name="transaction">The transaction to use for insertion, or null for no transaction</param>
        public async Task AddFile(string filename, DateTime lastmodified, long blocksetID, long metadataID)
        {
            var split = SplitIntoPrefixAndName(filename);

            await AddFile(
                await GetOrCreatePathPrefix(split.Key).ConfigureAwait(false),
                split.Value,
                lastmodified,
                blocksetID,
                metadataID
            )
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a known file to the fileset
        /// </summary>
        /// <param name="fileid">Id of the file</param>
        /// <param name="lastmodified">The time the file was modified</param>
        /// <param name="transaction">The transaction to use for insertion, or null for no transaction</param>
        public async Task AddKnownFile(long fileid, DateTime lastmodified)
        {
            await m_insertfileOperationCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@FilesetId", m_filesetId)
                .SetParameterValue("@FileId", fileid)
                .SetParameterValue("@LastModified", lastmodified.ToUniversalTime().Ticks)
                .ExecuteNonQueryAsync(m_logQueries)
                .ConfigureAwait(false);
        }

        public async Task AddDirectoryEntry(string path, long metadataID, DateTime lastmodified)
        {
            await AddFile(path, lastmodified, FOLDER_BLOCKSET_ID, metadataID)
                .ConfigureAwait(false);
        }

        public async Task AddSymlinkEntry(string path, long metadataID, DateTime lastmodified)
        {
            await AddFile(path, lastmodified, SYMLINK_BLOCKSET_ID, metadataID)
                .ConfigureAwait(false);
        }

        public async Task<(long, DateTime, long)> GetFileLastModified(long prefixid, string path, long filesetid, bool includeLength)
        {
            DateTime oldModified;
            long length;
            if (includeLength)
            {
                m_selectfilelastmodifiedWithSizeCommand
                    .SetTransaction(m_rtr)
                    .SetParameterValue("@PrefixId", prefixid)
                    .SetParameterValue("@Path", path)
                    .SetParameterValue("@FilesetId", filesetid);

                using (var rd = await m_selectfilelastmodifiedWithSizeCommand.ExecuteReaderAsync(m_logQueries, null).ConfigureAwait(false))
                    if (await rd.ReadAsync().ConfigureAwait(false))
                    {
                        oldModified = new DateTime(rd.ConvertValueToInt64(1), DateTimeKind.Utc);
                        length = rd.ConvertValueToInt64(2);
                        return (rd.ConvertValueToInt64(0), oldModified, length);
                    }
            }
            else
            {
                m_selectfilelastmodifiedCommand.SetTransaction(m_rtr)
                    .SetParameterValue("@PrefixId", prefixid)
                    .SetParameterValue("@Path", path)
                    .SetParameterValue("@FilesetId", filesetid);

                using (var rd = await m_selectfilelastmodifiedCommand.ExecuteReaderAsync(m_logQueries).ConfigureAwait(false))
                    if (await rd.ReadAsync().ConfigureAwait(false))
                    {
                        length = -1;
                        oldModified = new DateTime(rd.ConvertValueToInt64(1), DateTimeKind.Utc);
                        return (rd.ConvertValueToInt64(0), oldModified, length);
                    }
            }

            oldModified = new DateTime(0, DateTimeKind.Utc);
            length = -1;
            return (-1, oldModified, length);
        }


        public async Task<(long, DateTime, long, string?, long)> GetFileEntry(long prefixid, string path, long filesetid)
        {
            DateTime oldModified;
            long lastFileSize;
            string? oldMetahash;
            long oldMetasize;

            m_findfileCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@PrefixId", prefixid)
                .SetParameterValue("@Path", path)
                .SetParameterValue("@FilesetId", filesetid);

            using (var rd = await m_findfileCommand.ExecuteReaderAsync().ConfigureAwait(false))
                if (await rd.ReadAsync().ConfigureAwait(false))
                {
                    oldModified = new DateTime(rd.ConvertValueToInt64(1), DateTimeKind.Utc);
                    lastFileSize = rd.ConvertValueToInt64(2);
                    oldMetahash = rd.ConvertValueToString(3);
                    oldMetasize = rd.ConvertValueToInt64(4);

                    return (
                        rd.ConvertValueToInt64(0),
                        oldModified,
                        lastFileSize,
                        oldMetahash,
                        oldMetasize
                    );
                }
                else
                {
                    oldModified = new DateTime(0, DateTimeKind.Utc);
                    lastFileSize = -1;
                    oldMetahash = null;
                    oldMetasize = -1;

                    return (
                        -1,
                        oldModified,
                        lastFileSize,
                        oldMetahash,
                        oldMetasize
                    );
                }
        }

        public async Task<(long Size, string MetadataHash)?> GetMetadataHashAndSizeForFile(long fileid)
        {
            m_selectfilemetadatahashandsizeCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@FileId", fileid);

            using (var rd = await m_selectfilemetadatahashandsizeCommand.ExecuteReaderAsync().ConfigureAwait(false))
                if (await rd.ReadAsync().ConfigureAwait(false))
                    return (
                        rd.ConvertValueToInt64(0),
                        rd.ConvertValueToString(1) ?? throw new InvalidOperationException("Metadata hash is null")
                    );

            return null;
        }


        public async Task<string?> GetFileHash(long fileid)
        {
            var r = await m_selectfileHashCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@FileId", fileid)
                .ExecuteScalarAsync(m_logQueries, null)
                .ConfigureAwait(false);

            if (r == null || r == DBNull.Value)
                return null;

            return r.ToString();
        }

        public override void Dispose()
        {
            this.DisposeAsync().Await();
        }

        public override async Task DisposeAsync()
        {
            if (!string.IsNullOrWhiteSpace(m_tempDeletedBlockTable))
                try
                {
                    using (var cmd = m_connection.CreateCommand(m_rtr))
                        await cmd.ExecuteNonQueryAsync($@"DROP TABLE ""{m_tempDeletedBlockTable}""")
                            .ConfigureAwait(false);
                    await m_rtr.CommitAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "DropTempTableFailed", ex, "Failed to drop temporary table {0}: {1}", m_tempDeletedBlockTable, ex.Message);
                }

            await base.DisposeAsync().ConfigureAwait(false);
        }

        public async Task<long> GetLastWrittenDBlockVolumeSize()
        {
            using (var cmd = m_connection.CreateCommand(m_rtr))
                return await cmd.SetCommandAndParameters(@"
                    SELECT ""Size""
                    FROM ""RemoteVolume""
                    WHERE
                        ""State"" = @State
                        AND ""Type"" = @Type
                    ORDER BY ""ID"" DESC
                    LIMIT 1
                ")
                    .SetParameterValue("@State", RemoteVolumeState.Uploaded.ToString())
                    .SetParameterValue("@Type", RemoteVolumeType.Blocks.ToString())
                    .ExecuteScalarInt64Async(-1)
                    .ConfigureAwait(false);
        }

        private async Task<long> GetPreviousFilesetID(SqliteCommand cmd)
        {
            return await GetPreviousFilesetID(cmd, OperationTimestamp, m_filesetId)
                .ConfigureAwait(false);
        }

        private async Task<long> GetPreviousFilesetID(SqliteCommand cmd, DateTime timestamp, long filesetid)
        {
            return await cmd
                .SetTransaction(m_rtr)
                .SetCommandAndParameters(@"
                    SELECT ""ID""
                    FROM ""Fileset""
                    WHERE
                        ""Timestamp"" < @Timestamp
                        AND ""ID"" != @FilesetId
                    ORDER BY ""Timestamp"" DESC
                ")
                .SetParameterValue("@Timestamp", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(timestamp))
                .SetParameterValue("@FilesetId", filesetid)
                .ExecuteScalarInt64Async(-1)
                .ConfigureAwait(false);
        }

        internal async Task<Tuple<long, long>> GetLastBackupFileCountAndSize()
        {
            using (var cmd = m_connection.CreateCommand(m_rtr))
            {
                var lastFilesetId = await cmd.ExecuteScalarInt64Async(@"
                    SELECT ""ID""
                    FROM ""Fileset""
                    ORDER BY ""Timestamp"" DESC
                    LIMIT 1
                ")
                    .ConfigureAwait(false);

                var count = await cmd.SetCommandAndParameters(@"
                    SELECT COUNT(*)
                    FROM ""FileLookup""
                    INNER JOIN ""FilesetEntry""
                        ON ""FileLookup"".""ID"" = ""FilesetEntry"".""FileID""
                    WHERE
                        ""FilesetEntry"".""FilesetID"" = @FilesetId
                        AND ""FileLookup"".""BlocksetID"" NOT IN (
                            @FolderBlocksetId,
                            @SymlinkBlocksetId
                        )
                    ")
                    .SetParameterValue("@FilesetId", lastFilesetId)
                    .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
                    .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
                    .ExecuteScalarInt64Async(-1)
                    .ConfigureAwait(false);

                var size = await cmd.SetCommandAndParameters(@"
                    SELECT SUM(""Blockset"".""Length"")
                    FROM
                        ""FileLookup"",
                        ""FilesetEntry"",
                        ""Blockset""
                    WHERE
                        ""FileLookup"".""ID"" = ""FilesetEntry"".""FileID"" AND
                        ""FileLookup"".""BlocksetID"" = ""Blockset"".""ID"" AND
                        ""FilesetEntry"".""FilesetID"" = @FilesetId AND
                        ""FileLookup"".""BlocksetID"" NOT IN (
                            @FolderBlocksetId,
                            @SymlinkBlocksetId
                        )
                    ")
                    .SetParameterValue("@FilesetId", lastFilesetId)
                    .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
                    .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
                    .ExecuteScalarInt64Async(-1)
                    .ConfigureAwait(false);

                return new Tuple<long, long>(count, size);
            }
        }

        internal async Task UpdateChangeStatistics(BackupResults results)
        {
            using (var cmd = m_connection.CreateCommand(m_rtr))
            {
                var prevFileSetId = await GetPreviousFilesetID(cmd)
                    .ConfigureAwait(false);
                await ChangeStatistics.UpdateChangeStatistics(cmd, results, m_filesetId, prevFileSetId)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Populates FilesetEntry table with files from previous fileset, which aren't
        /// yet part of the new fileset, and which aren't on the (optional) list of <c>deleted</c> paths.
        /// </summary>
        /// <param name="transaction">Transaction</param>
        /// <param name="deleted">List of deleted paths, or null</param>
        public async Task AppendFilesFromPreviousSet(IEnumerable<string>? deleted = null)
        {
            await AppendFilesFromPreviousSet(deleted, m_filesetId, -1, OperationTimestamp)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Populates FilesetEntry table with files from previous fileset, which aren't
        /// yet part of the new fileset, and which aren't on the (optional) list of <c>deleted</c> paths.
        /// </summary>
        /// <param name="transaction">Transaction</param>
        /// <param name="deleted">List of deleted paths, or null</param>
        /// <param name="filesetid">Current file-set ID</param>
        /// <param name="prevId">Source file-set ID</param>
        /// <param name="timestamp">If <c>filesetid</c> == -1, used to locate previous file-set</param>
        public async Task AppendFilesFromPreviousSet(IEnumerable<string>? deleted, long filesetid, long prevId, DateTime timestamp)
        {
            using (var cmd = m_connection.CreateCommand())
            using (var cmdDelete = m_connection.CreateCommand())
            {
                long lastFilesetId = prevId < 0 ?
                    await GetPreviousFilesetID(cmd, timestamp, filesetid)
                        .ConfigureAwait(false)
                    :
                    prevId;

                await cmd.SetTransaction(m_rtr)
                    .SetCommandAndParameters(@"
                        INSERT INTO ""FilesetEntry"" (
                            ""FilesetID"",
                            ""FileID"",
                            ""Lastmodified""
                        )
                        SELECT
                            @CurrentFilesetId AS ""FilesetID"",
                            ""FileID"",
                            ""Lastmodified""
                        FROM (
                            SELECT DISTINCT
                                ""FilesetID"",
                                ""FileID"",
                                ""Lastmodified""
                            FROM ""FilesetEntry""
                            WHERE
                                ""FilesetID"" = @PreviousFilesetId
                                AND ""FileID"" NOT IN (
                                    SELECT ""FileID""
                                    FROM ""FilesetEntry""
                                    WHERE ""FilesetID"" = @CurrentFilesetId
                                )
                        )
                    ")
                    .SetParameterValue("@CurrentFilesetId", filesetid)
                    .SetParameterValue("@PreviousFilesetId", lastFilesetId)
                    .ExecuteNonQueryAsync(m_logQueries)
                    .ConfigureAwait(false);

                if (deleted != null)
                {
                    using var tmplist = await TemporaryDbValueList
                        .CreateAsync(this, deleted)
                        .ConfigureAwait(false);

                    await (
                        await cmdDelete.SetTransaction(m_rtr)
                            .SetCommandAndParameters(@"
                                DELETE FROM ""FilesetEntry""
                                WHERE
                                    ""FilesetID"" = @FilesetId
                                    AND ""FileID"" IN (
                                        SELECT ""ID""
                                        FROM ""File""
                                        WHERE ""Path"" IN (@Paths)
                                    )
                            ")
                            .SetParameterValue("@FilesetId", filesetid)
                            .ExpandInClauseParameterMssqliteAsync("@Paths", tmplist)
                            .ConfigureAwait(false)
                    )
                        .ExecuteNonQueryAsync(m_logQueries)
                        .ConfigureAwait(false);
                }

                await m_rtr.CommitAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Populates FilesetEntry table with files from previous fileset, which aren't
        /// yet part of the new fileset, and which aren't excluded by the (optional) exclusion
        /// predicate.
        /// </summary>
        /// <param name="transaction">Transaction</param>
        /// <param name="exclusionPredicate">Optional exclusion predicate (true = exclude file)</param>
        public async Task AppendFilesFromPreviousSetWithPredicate(Func<string, long, bool> exclusionPredicate)
        {
            await AppendFilesFromPreviousSetWithPredicate(exclusionPredicate, m_filesetId, -1, OperationTimestamp)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Populates FilesetEntry table with files from previous fileset, which aren't
        /// yet part of the new fileset, and which aren't excluded by the (optional) exclusion
        /// predicate.
        /// </summary>
        /// <param name="transaction">Transaction</param>
        /// <param name="exclusionPredicate">Optional exclusion predicate (true = exclude file)</param>
        /// <param name="fileSetId">Current fileset ID</param>
        /// <param name="prevFileSetId">Source fileset ID</param>
        /// <param name="timestamp">If <c>prevFileSetId</c> == -1, used to locate previous fileset</param>
        public async Task AppendFilesFromPreviousSetWithPredicate(Func<string, long, bool> exclusionPredicate, long fileSetId, long prevFileSetId, DateTime timestamp)
        {
            if (exclusionPredicate == null)
            {
                await AppendFilesFromPreviousSet(null, fileSetId, prevFileSetId, timestamp)
                    .ConfigureAwait(false);
                return;
            }

            using (var cmd = m_connection.CreateCommand())
            using (var cmdDelete = m_connection.CreateCommand())
            {
                long lastFilesetId = prevFileSetId < 0 ?
                    await GetPreviousFilesetID(cmd, timestamp, fileSetId)
                        .ConfigureAwait(false)
                    :
                    prevFileSetId;

                // copy entries from previous file set into a temporary table, except those file IDs already added by the current backup
                var tempFileSetTable = "FilesetEntry-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                await cmd
                    .SetTransaction(m_rtr)
                    .SetCommandAndParameters($@"
                        CREATE TEMPORARY TABLE ""{tempFileSetTable}"" AS
                        SELECT
                            ""FileID"",
                            ""Lastmodified""
                        FROM (
                            SELECT DISTINCT
                                ""FilesetID"",
                                ""FileID"",
                                ""Lastmodified""
                            FROM ""FilesetEntry""
                            WHERE
                                ""FilesetID"" = @PreviousFilesetId
                                AND ""FileID"" NOT IN (
                                    SELECT ""FileID""
                                    FROM ""FilesetEntry""
                                    WHERE ""FilesetID"" = @CurrentFilesetId
                                )
                        )
                    ")
                    .SetParameterValue("@PreviousFilesetId", lastFilesetId)
                    .SetParameterValue("@CurrentFilesetId", fileSetId)
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);

                // now we need to remove, from the above, any entries that were enumerated by the
                // UNC-driven backup
                cmdDelete.SetTransaction(m_rtr)
                    .SetCommandAndParameters($@"
                        DELETE FROM ""{tempFileSetTable}""
                        WHERE ""FileID"" = @FileId
                    ");

                // enumerate files from new temporary file set, and remove any entries handled by UNC
                cmd.SetCommandAndParameters($@"
                    SELECT
                        f.""Path"",
                        fs.""FileID"",
                        fs.""Lastmodified"",
                        COALESCE(bs.""Length"", -1)
                    FROM (
                        SELECT DISTINCT
                            ""FileID"",
                            ""Lastmodified""
                        FROM ""{tempFileSetTable}""
                    ) AS fs
                    LEFT JOIN ""File"" AS f
                        ON fs.""FileID"" = f.""ID""
                    LEFT JOIN ""Blockset"" AS bs
                        ON f.""BlocksetID"" = bs.""ID"";
                ");

                await foreach (var row in cmd.ExecuteReaderEnumerableAsync().ConfigureAwait(false))
                {
                    var path = row.ConvertValueToString(0) ?? throw new Exception("Unexpected null value for path");
                    var size = row.ConvertValueToInt64(3);

                    if (exclusionPredicate(path, size))
                        await cmdDelete.SetParameterValue("@FileId", row.ConvertValueToInt64(1))
                            .ExecuteNonQueryAsync()
                            .ConfigureAwait(false);
                }

                // now copy the temporary table into the FileSetEntry table
                await cmd.SetCommandAndParameters($@"
                    INSERT INTO ""FilesetEntry"" (
                        ""FilesetID"",
                        ""FileID"",
                        ""Lastmodified""
                    )
                    SELECT
                        @FilesetId,
                        ""FileID"",
                        ""Lastmodified""
                    FROM ""{tempFileSetTable}""
                ")
                    .SetParameterValue("@FilesetId", fileSetId)
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);

                await m_rtr.CommitAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates a timestamped backup operation to correctly associate the fileset with the time it was created.
        /// </summary>
        /// <param name="volumeid">The ID of the fileset volume to update</param>
        /// <param name="timestamp">The timestamp of the operation to create</param>
        /// <param name="transaction">An optional external transaction</param>
        public override async Task<long> CreateFileset(long volumeid, DateTime timestamp)
        {
            return m_filesetId = await base.CreateFileset(volumeid, timestamp)
                .ConfigureAwait(false);
        }

        public async Task<IEnumerable<string>> GetTemporaryFilelistVolumeNames(bool latestOnly)
        {
            var incompleteFilesetIDs = GetIncompleteFilesets().OrderBy(x => x.Value).Select(x => x.Key);

            if (!await incompleteFilesetIDs.AnyAsync().ConfigureAwait(false))
                return [];

            if (latestOnly)
                incompleteFilesetIDs = new long[] {
                    await incompleteFilesetIDs.LastAsync().ConfigureAwait(false)
                }
                    .ToAsyncEnumerable();

            var volumeNames = new List<string>();
            await foreach (var filesetID in incompleteFilesetIDs.ConfigureAwait(false))
                volumeNames.Add((
                    await GetRemoteVolumeFromFilesetID(filesetID)
                        .ConfigureAwait(false)
                ).Name);

            return volumeNames;
        }

        public async IAsyncEnumerable<string> GetMissingIndexFiles()
        {
            using var cmd = m_connection.CreateCommand(m_rtr)
                .SetCommandAndParameters(@"
                    SELECT ""Name""
                    FROM ""RemoteVolume""
                    WHERE
                        ""Type"" = @Type
                        AND NOT ""ID"" IN (
                            SELECT ""BlockVolumeID""
                            FROM ""IndexBlockLink""
                        )
                        AND ""State"" IN (@States)
                ")
                .SetParameterValue("@Type", RemoteVolumeType.Blocks.ToString())
                .ExpandInClauseParameterMssqlite("@States", [
                    RemoteVolumeState.Uploaded.ToString(),
                    RemoteVolumeState.Verified.ToString()
                ]);

            using (var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                while (await rd.ReadAsync().ConfigureAwait(false))
                    yield return rd.ConvertValueToString(0) ?? throw new Exception("Unexpected null value for volume name");
        }

        public async Task MoveBlockToVolume(string blockkey, long size, long sourcevolumeid, long targetvolumeid)
        {
            using (var cmd = m_connection.CreateCommand(m_rtr))
            {
                var c = await cmd.SetCommandAndParameters(@"
                    UPDATE ""Block""
                    SET ""VolumeID"" = @NewVolumeId
                    WHERE
                        ""Hash"" = @Hash
                        AND ""Size"" = @Size
                        AND ""VolumeID"" = @PreviousVolumeId
                    ")
                    .SetParameterValue("@NewVolumeId", targetvolumeid)
                    .SetParameterValue("@Hash", blockkey)
                    .SetParameterValue("@Size", size)
                    .SetParameterValue("@PreviousVolumeId", sourcevolumeid)
                    .ExecuteNonQueryAsync()
                    .ConfigureAwait(false);

                if (c != 1)
                    throw new Exception($"Failed to move block {blockkey}:{size} from volume {sourcevolumeid}, count: {c}");
            }
        }

        public async Task SafeDeleteRemoteVolume(string name)
        {
            var volumeid = await GetRemoteVolumeID(name).ConfigureAwait(false);

            using (var cmd = m_connection.CreateCommand(m_rtr))
            {
                var c = await cmd.SetCommandAndParameters(@"
                    SELECT COUNT(*)
                    FROM ""Block""
                    WHERE ""VolumeID"" = @VolumeId
                ")
                    .SetParameterValue("@VolumeId", volumeid)
                    .ExecuteScalarInt64Async(-1)
                    .ConfigureAwait(false);

                if (c != 0)
                    throw new Exception($"Failed to safe-delete volume {name}, blocks: {c}");

                await RemoveRemoteVolume(name).ConfigureAwait(false);
            }
        }

        public async Task<string[]> GetBlocklistHashes(string name)
        {
            var volumeid = GetRemoteVolumeID(name);
            using (var cmd = m_connection.CreateCommand(m_rtr))
            {
                // Grab the strings and return as array to avoid concurrent access to the IEnumerable
                cmd.SetCommandAndParameters(@"
                    SELECT DISTINCT ""Block"".""Hash""
                    FROM ""Block""
                    WHERE
                        ""Block"".""VolumeID"" = @VolumeId
                        AND ""Block"".""Hash"" IN (
                            SELECT ""Hash""
                            FROM ""BlocklistHash""
                        )
                ")
                    .SetParameterValue("@VolumeId", volumeid);

                return await cmd.ExecuteReaderEnumerableAsync()
                    .Select(x => x.ConvertValueToString(0) ?? throw new Exception("Unexpected null value for blocklist hash"))
                    .ToArrayAsync()
                    .ConfigureAwait(false);
            }
        }

        public async Task<string?> GetFirstPath()
        {
            using (var cmd = m_connection.CreateCommand(m_rtr))
            {
                var v0 = await cmd.ExecuteScalarAsync(@"
                    SELECT ""Path""
                    FROM ""File""
                    ORDER BY LENGTH(""Path"") DESC
                    LIMIT 1
                ")
                    .ConfigureAwait(false);

                if (v0 == null || v0 == DBNull.Value)
                    return null;

                return v0.ToString();
            }
        }

        /// <summary>
        /// Retrieves change journal data for file set
        /// </summary>
        /// <param name="fileSetId">Fileset-ID</param>
        public async IAsyncEnumerable<Interface.USNJournalDataEntry> GetChangeJournalData(long fileSetId)
        {
            var data = new List<Interface.USNJournalDataEntry>();

            using var cmd = m_connection.CreateCommand(@"
                SELECT
                    ""VolumeName"",
                    ""JournalID"",
                    ""NextUSN"",
                    ""ConfigHash""
                FROM ""ChangeJournalData""
                WHERE ""FilesetID"" = @FilesetId
            ")
                .SetTransaction(m_rtr)
                .SetParameterValue("@FilesetId", fileSetId);

            using (var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
            {
                while (await rd.ReadAsync().ConfigureAwait(false))
                {
                    yield return new Interface.USNJournalDataEntry
                    {
                        Volume = rd.ConvertValueToString(0),
                        JournalId = rd.ConvertValueToInt64(1),
                        NextUsn = rd.ConvertValueToInt64(2),
                        ConfigHash = rd.ConvertValueToString(3)
                    };
                }
            }
        }

        /// <summary>
        /// Adds NTFS change journal data for file set and volume
        /// </summary>
        /// <param name="data">Data to add</param>
        /// <param name="transaction">An optional external transaction</param>
        public async Task CreateChangeJournalData(IEnumerable<Interface.USNJournalDataEntry> data)
        {
            foreach (var entry in data)
            {
                using (var cmd = m_connection.CreateCommand(m_rtr))
                {
                    var c = await cmd.SetCommandAndParameters(@"
                        INSERT INTO ""ChangeJournalData"" (
                            ""FilesetID"",
                            ""VolumeName"",
                            ""JournalID"",
                            ""NextUSN"",
                            ""ConfigHash""
                        )
                        VALUES (
                            @FilesetId,
                            @VolumeName,
                            @JournalId,
                            @NextUsn,
                            @ConfigHash
                        );
                    ")
                        .SetParameterValue("@FilesetId", m_filesetId)
                        .SetParameterValue("@VolumeName", entry.Volume)
                        .SetParameterValue("@JournalId", entry.JournalId)
                        .SetParameterValue("@NextUsn", entry.NextUsn)
                        .SetParameterValue("@ConfigHash", entry.ConfigHash)
                        .ExecuteNonQueryAsync()
                        .ConfigureAwait(false);

                    if (c != 1)
                        throw new Exception("Unable to add change journal entry");
                }
            }

            await m_rtr.CommitAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Adds NTFS change journal data for file set and volume
        /// </summary>
        /// <param name="data">Data to add</param>
        /// <param name="fileSetId">Existing file set to update</param>
        /// <param name="transaction">An optional external transaction</param>
        public async Task UpdateChangeJournalData(IEnumerable<Interface.USNJournalDataEntry> data, long fileSetId)
        {
            foreach (var entry in data)
            {
                using (var cmd = m_connection.CreateCommand())
                {
                    await cmd.SetCommandAndParameters(@"
                        UPDATE ""ChangeJournalData""
                        SET ""NextUSN"" = @NextUsn
                        WHERE
                            ""FilesetID"" = @FilesetId
                            AND ""VolumeName"" = @VolumeName
                            AND ""JournalID"" = @JournalId;
                    ")
                        .SetTransaction(m_rtr)
                        .SetParameterValue("@NextUsn", entry.NextUsn)
                        .SetParameterValue("@FilesetId", fileSetId)
                        .SetParameterValue("@VolumeName", entry.Volume)
                        .SetParameterValue("@JournalId", entry.JournalId)
                        .ExecuteNonQueryAsync()
                        .ConfigureAwait(false);
                }
            }

            await m_rtr.CommitAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Checks if a blocklist hash is known
        /// </summary>
        /// <param name="hash">The hash to check</param>
        /// <param name="transaction">An optional external transaction</param>
        /// <returns>True if the hash is known, false otherwise</returns>
        public async Task<bool> IsBlocklistHashKnown(string hash)
        {
            var res = await m_getfirstfilesetwithblockinblockset
                .SetTransaction(m_rtr)
                .SetParameterValue("@Hash", hash)
                .ExecuteScalarInt64Async()
                .ConfigureAwait(false);

            if (res != -1 && res != m_filesetId)
                return true;
            else
                return !m_blocklistHashes.Add(hash);
        }
    }
}
