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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using Microsoft.Data.Sqlite;

namespace Duplicati.Library.Main.Database
{
    /// <summary>
    /// This class is used to recreate a local database from a backup.
    /// </summary>
    internal class LocalRecreateDatabase : LocalRestoreDatabase
    {
        /// <summary>
        /// The tag used for logging.
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(LocalRecreateDatabase));

        /// <summary>
        /// Keeps track of the path entries in a sorted manner.
        /// This is used to ensure that the entries are unique and sorted by their keys.
        /// </summary>
        private class PathEntryKeeper
        {
            /// <summary>
            /// Comparer for KeyValuePair<long, long> to sort by key and value.
            /// </summary>
            private struct KeyValueComparer : IComparer<KeyValuePair<long, long>>
            {
                public readonly int Compare(KeyValuePair<long, long> x, KeyValuePair<long, long> y)
                {
                    return x.Key == y.Key ?
                            (x.Value == y.Value ?
                                0
                                : (x.Value < y.Value ? -1 : 1))
                            : (x.Key < y.Key ? -1 : 1);
                }
            }
        }

        /// <summary>
        /// Inserts a new file entry into the FileLookup table and returns the new row ID.
        /// </summary>
        private SqliteCommand m_insertFileCommand = null!;
        /// <summary>
        /// Inserts a new entry into the FilesetEntry table linking a fileset to a file with a last-modified timestamp.
        /// </summary>
        private SqliteCommand m_insertFilesetEntryCommand = null!;
        /// <summary>
        /// Inserts a new row into the Metadataset table for a given blockset and returns the new row ID.
        /// </summary>
        private SqliteCommand m_insertMetadatasetCommand = null!;
        /// <summary>
        /// Inserts a new blockset with its length and full hash into the Blockset table and returns the new row ID.
        /// </summary>
        private SqliteCommand m_insertBlocksetCommand = null!;
        /// <summary>
        /// Inserts a new blocklist hash entry into the BlocklistHash table for a given blockset and index.
        /// </summary>
        private SqliteCommand m_insertBlocklistHashCommand = null!;
        /// <summary>
        /// Updates the VolumeID of a block in the Block table matching a specific hash and size.
        /// </summary>
        private SqliteCommand m_updateBlockVolumeCommand = null!;
        /// <summary>
        /// Inserts a blocklist hash, block hash, and index into the temporary blocklist table.
        /// </summary>
        private SqliteCommand m_insertTempBlockListHash = null!;
        /// <summary>
        /// Inserts or ignores a file hash, block hash, and block size into the temporary small blockset table.
        /// </summary>
        private SqliteCommand m_insertSmallBlockset = null!;
        /// <summary>
        /// Selects the ID of a blockset from the Blockset table matching a given length and full hash.
        /// </summary>
        private SqliteCommand m_findBlocksetCommand = null!;
        /// <summary>
        /// Selects the ID of a Metadataset whose blockset matches a given full hash and length.
        /// </summary>
        private SqliteCommand m_findMetadatasetCommand = null!;
        /// <summary>
        /// Selects the ID of a file from the FileLookup table matching a given prefix, path, blockset, and metadata.
        /// </summary>
        private SqliteCommand m_findFilesetCommand = null!;
        /// <summary>
        /// Selects distinct blocklist hashes from the temporary blocklist table matching a given hash.
        /// </summary>
        private SqliteCommand m_findTempBlockListHashCommand = null!;
        /// <summary>
        /// Selects the VolumeID of a block in the Block table matching a specific hash and size.
        /// </summary>
        private SqliteCommand m_findHashBlockCommand = null!;
        /// <summary>
        /// Inserts a new block with hash, size, and volume ID into the Block table.
        /// </summary>
        private SqliteCommand m_insertBlockCommand = null!;
        /// <summary>
        /// Inserts or ignores a duplicate block entry linking a block and a volume into the DuplicateBlock table.
        /// </summary>
        private SqliteCommand m_insertDuplicateBlockCommand = null!;

        /// <summary>
        /// Temporary table name for blocklist hashes and their indices.
        /// </summary>
        private string m_tempblocklist = null!;
        /// <summary>
        /// Temporary table name for small blocksets, containing file hashes, block hashes, and block sizes.
        /// </summary>
        private string m_tempsmalllist = null!;

        /// <summary>
        /// A lookup table that prevents multiple downloads of the same volume.
        /// </summary>
        private readonly Dictionary<long, long> m_proccessedVolumes = new Dictionary<long, long>();

        /// <summary>
        /// SQL that finds index and block size for all blocklist hashes, based on the temporary hash list.
        /// </summary>
        /// <param name="blocksize">The size of the blocks in the blocklist.</param>
        /// <param name="blockhashsize">The size of the block hashes.</param>
        /// <param name="temptable">The name of the temporary table containing blocklist hashes and indices.</param>
        /// <param name="fullBlockListBlockCount">The number of blocks in the full block list, used to calculate the full index.</param>
        /// <returns>A SQL query string that selects blocklist entries with their full index and block size.</returns>
        private static string SELECT_BLOCKLIST_ENTRIES(long blocksize, long blockhashsize, string temptable, long fullBlockListBlockCount)
        {
            string str_blocksize = Library.Utility.Utility.FormatInvariant("{0}", blocksize);
            string str_blockhashsize = Library.Utility.Utility.FormatInvariant("{0}", blockhashsize);
            string str_fullBlockListBlockCount = Library.Utility.Utility.FormatInvariant("{0}", fullBlockListBlockCount);
            return $@"
                SELECT
                    ""E"".""BlocksetID"",
                    ""F"".""Index"" + (""E"".""BlocklistIndex"" * {str_fullBlockListBlockCount}) AS ""FullIndex"",
                    ""F"".""BlockHash"",
                    MIN({str_blocksize}, ""E"".""Length"" - ((""F"".""Index"" + (""E"".""BlocklistIndex"" * {str_fullBlockListBlockCount})) * {str_blocksize})) AS ""BlockSize"",
                    ""E"".""Hash"",
                    ""E"".""BlocklistSize"",
                    ""E"".""BlocklistHash""
                FROM
                    (
                        SELECT *
                        FROM
                            (
                                SELECT
                                    ""A"".""BlocksetID"",
                                    ""A"".""Index"" AS ""BlocklistIndex"",
                                    MIN({str_fullBlockListBlockCount} * {str_blockhashsize}, (((""B"".""Length"" + {str_blocksize} - 1) / {str_blocksize}) - (""A"".""Index"" * ({str_fullBlockListBlockCount}))) * {str_blockhashsize}) AS ""BlocklistSize"",
                                    ""A"".""Hash"" AS ""BlocklistHash"",
                                    ""B"".""Length""
                                FROM
                                    ""BlocklistHash"" ""A"",
                                    ""Blockset"" ""B""
                                WHERE
                                    ""B"".""ID"" = ""A"".""BlocksetID""
                            ) ""C"",
                            ""Block"" ""D""
                        WHERE
                            ""C"".""BlocklistHash"" = ""D"".""Hash""
                            AND ""C"".""BlocklistSize"" = ""D"".""Size""
                    ) ""E"",
                    ""{temptable}"" ""F""
                WHERE
                    ""F"".""BlocklistHash"" = ""E"".""Hash""
                ORDER BY
                    ""E"".""BlocksetID"",
                    ""FullIndex""
            ";
        }

        /// <summary>
        /// Asynchronously creates a new instance of the <see cref="LocalRecreateDatabase"/> class.
        /// </summary>
        /// <param name="parentdb">The parent database from which to restore.</param>
        /// <param name="options">The options to use for the database creation.</param>
        /// <param name="dbnew">An optional existing instance of <see cref="LocalRecreateDatabase"/> to use; if null, a new instance will be created.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that represents the asynchronous operation, containing the newly created <see cref="LocalRecreateDatabase"/> instance.</returns>
        public static async Task<LocalRecreateDatabase> CreateAsync(LocalDatabase parentdb, Options options, LocalRecreateDatabase? dbnew, CancellationToken token)
        {
            dbnew ??= new LocalRecreateDatabase();

            dbnew = (LocalRecreateDatabase)
                await LocalRestoreDatabase.CreateAsync(parentdb, dbnew, token)
                    .ConfigureAwait(false);

            dbnew.m_tempblocklist = $"TempBlocklist_{Library.Utility.Utility.GetHexGuid()}";
            dbnew.m_tempsmalllist = $"TempSmalllist_{Library.Utility.Utility.GetHexGuid()}";

            if (dbnew.m_connection == null)
                throw new Exception("Connection is null");
            await using var cmd = dbnew.m_connection.CreateCommand();

            await cmd.ExecuteNonQueryAsync($@"
                CREATE TEMPORARY TABLE ""{dbnew.m_tempblocklist}"" (
                    ""BlockListHash"" TEXT NOT NULL,
                    ""BlockHash"" TEXT NOT NULL,
                    ""Index"" INTEGER NOT NULL
                )
            ", token)
                .ConfigureAwait(false);

            await cmd.ExecuteNonQueryAsync($@"
                CREATE INDEX ""Index_{dbnew.m_tempblocklist}""
                ON ""{dbnew.m_tempblocklist}"" (""BlockListHash"");
            ", token)
                .ConfigureAwait(false);

            await cmd.ExecuteNonQueryAsync($@"
                CREATE TEMPORARY TABLE ""{dbnew.m_tempsmalllist}"" (
                    ""FileHash"" TEXT NOT NULL,
                    ""BlockHash"" TEXT NOT NULL,
                    ""BlockSize"" INTEGER NOT NULL
                )
            ", token)
                .ConfigureAwait(false);

            await cmd.ExecuteNonQueryAsync($@"
                CREATE UNIQUE INDEX ""Index_File_{dbnew.m_tempsmalllist}""
                ON ""{dbnew.m_tempsmalllist}"" (
                    ""FileHash"",
                    ""BlockSize""
                );
            ", token)
                .ConfigureAwait(false);

            await cmd.ExecuteNonQueryAsync($@"
                CREATE UNIQUE INDEX ""Index_Block_{dbnew.m_tempsmalllist}""
                ON ""{dbnew.m_tempsmalllist}"" (
                    ""BlockHash"",
                    ""BlockSize""
                );
            ", token)
                .ConfigureAwait(false);

            dbnew.m_insertFileCommand = await dbnew.m_connection.CreateCommandAsync(@"
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
            ", token)
                .ConfigureAwait(false);

            dbnew.m_insertFilesetEntryCommand = await dbnew.m_connection.CreateCommandAsync(@"
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
            ", token)
                .ConfigureAwait(false);

            dbnew.m_insertMetadatasetCommand = await dbnew.m_connection.CreateCommandAsync(@"
                INSERT INTO ""Metadataset"" (""BlocksetID"")
                VALUES (@BlocksetId);
                SELECT last_insert_rowid();
            ", token)
                .ConfigureAwait(false);

            dbnew.m_insertBlocksetCommand = await dbnew.m_connection.CreateCommandAsync(@"
                INSERT INTO ""Blockset"" (
                    ""Length"",
                    ""FullHash""
                )
                VALUES (
                    @Length,
                    @FullHash
                );
                SELECT last_insert_rowid();
            ", token)
                .ConfigureAwait(false);

            dbnew.m_insertBlocklistHashCommand = await dbnew.m_connection.CreateCommandAsync(@"
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
            ", token)
                .ConfigureAwait(false);

            dbnew.m_updateBlockVolumeCommand = await dbnew.m_connection.CreateCommandAsync(@"
                UPDATE ""Block""
                SET ""VolumeID"" = @VolumeId
                WHERE
                    ""Hash"" = @Hash
                    AND ""Size"" = @Size
            ", token)
                .ConfigureAwait(false);

            dbnew.m_insertTempBlockListHash = await dbnew.m_connection.CreateCommandAsync($@"
                INSERT INTO ""{dbnew.m_tempblocklist}"" (
                    ""BlocklistHash"",
                    ""BlockHash"",
                    ""Index""
                )
                VALUES (
                    @BlocklistHash,
                    @BlockHash,
                    @Index
                )
            ", token)
                .ConfigureAwait(false);

            dbnew.m_insertSmallBlockset = await dbnew.m_connection.CreateCommandAsync($@"
                INSERT OR IGNORE INTO ""{dbnew.m_tempsmalllist}"" (
                    ""FileHash"",
                    ""BlockHash"",
                    ""BlockSize""
                )
                VALUES (
                    @FileHash,
                    @BlockHash,
                    @BlockSize
                )
            ", token)
                .ConfigureAwait(false);

            dbnew.m_findBlocksetCommand = await dbnew.m_connection.CreateCommandAsync(@"
                SELECT ""ID""
                FROM ""Blockset""
                WHERE
                    ""Length"" = @Length
                    AND ""FullHash"" = @FullHash
            ", token)
                .ConfigureAwait(false);

            dbnew.m_findMetadatasetCommand = await dbnew.m_connection.CreateCommandAsync(@"
                SELECT ""Metadataset"".""ID""
                FROM
                    ""Metadataset"",
                    ""Blockset""
                WHERE
                    ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID""
                    AND ""Blockset"".""FullHash"" = @FullHash
                    AND ""Blockset"".""Length"" = @Length
            ", token)
                .ConfigureAwait(false);

            dbnew.m_findFilesetCommand = await dbnew.m_connection.CreateCommandAsync(@"
                SELECT ""ID""
                FROM ""FileLookup""
                WHERE
                    ""PrefixID"" = @PrefixId
                    AND ""Path"" = @Path
                    AND ""BlocksetID"" = @BlocksetId
                    AND ""MetadataID"" = @MetadataId
            ", token)
                .ConfigureAwait(false);

            dbnew.m_findTempBlockListHashCommand = await dbnew.m_connection.CreateCommandAsync($@"
                SELECT DISTINCT ""BlockListHash""
                FROM ""{dbnew.m_tempblocklist}""
                WHERE ""BlockListHash"" = @BlocklistHash
            ", token)
                .ConfigureAwait(false);

            dbnew.m_findHashBlockCommand = await dbnew.m_connection.CreateCommandAsync(@"
                SELECT ""VolumeID""
                FROM ""Block""
                WHERE
                    ""Hash"" = @Hash
                    AND ""Size"" = @Size
            ", token)
                .ConfigureAwait(false);

            dbnew.m_insertBlockCommand = await dbnew.m_connection.CreateCommandAsync(@"
                INSERT INTO ""Block"" (
                    ""Hash"",
                    ""Size"",
                    ""VolumeID""
                )
                VALUES (
                    @Hash,
                    @Size,
                    @VolumeId
                )
            ", token)
                .ConfigureAwait(false);

            dbnew.m_insertDuplicateBlockCommand = await dbnew.m_connection.CreateCommandAsync(@"
                INSERT OR IGNORE INTO ""DuplicateBlock"" (
                    ""BlockID"",
                    ""VolumeID""
                )
                VALUES (
                    (
                        SELECT ""ID""
                        FROM ""Block""
                        WHERE
                            ""Hash"" = @Hash
                            AND ""Size"" = @Size
                    ),
                    @VolumeId
                )
            ", token)
                .ConfigureAwait(false);

            return dbnew;
        }

        /// <summary>
        /// Identifies and inserts any missing blocks and blockset entries into the database by comparing temporary tables of blocklist and small blockset data with the main tables, ensuring that all required block and blockset relationships are present for database consistency.
        /// </summary>
        /// <param name="hashsize">The size of the hashes in the blocklist.</param>
        /// <param name="blocksize">The size of the blocks in the blocklist.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that completes when the operation is finished.</returns>
        public async Task FindMissingBlocklistHashes(long hashsize, long blocksize, CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(m_rtr);
            //Update all small blocklists and matching blocks

            var selectSmallBlocks = $@"
                SELECT
                    ""BlockHash"",
                    ""BlockSize""
                FROM ""{m_tempsmalllist}""
            ";

            var selectBlockHashes = $@"
                SELECT
                    ""BlockHash"" AS ""FullHash"",
                    ""BlockSize"" AS ""Length""
                FROM (
                    {SELECT_BLOCKLIST_ENTRIES(blocksize, hashsize, m_tempblocklist, blocksize / hashsize)}
                )
            ";

            var selectAllBlocks = @$"
                SELECT DISTINCT
                    ""FullHash"",
                    ""Length""
                FROM (
                    {selectBlockHashes}
                    UNION {selectSmallBlocks}
                )
            ";

            var selectNewBlocks = $@"
                SELECT
                    ""FullHash"" AS ""Hash"",
                    ""Length"" AS ""Size"",
                    -1 AS ""VolumeID""
                FROM (
                    SELECT
                        ""A"".""FullHash"",
                        ""A"".""Length"",
                        CASE
                            WHEN ""B"".""Hash"" IS NULL
                            THEN ''
                            ELSE ""B"".""Hash""
                        END AS ""Hash"",
                        CASE
                            WHEN ""B"".""Size"" is NULL
                            THEN -1
                            ELSE ""B"".""Size""
                        END AS ""Size""
                    FROM ({selectAllBlocks}) ""A""
                    LEFT OUTER JOIN ""Block"" ""B""
                        ON ""B"".""Hash"" =  ""A"".""FullHash""
                        AND ""B"".""Size"" = ""A"".""Length""
                )
                WHERE
                    ""FullHash"" != ""Hash""
                    AND ""Length"" != ""Size""
            ";

            var insertBlocksCommand = @$"
                INSERT INTO ""Block"" (
                    ""Hash"",
                    ""Size"",
                    ""VolumeID""
                )
                {selectNewBlocks}
            ";

            // Insert all known blocks into block table with volumeid = -1
            await cmd.ExecuteNonQueryAsync(insertBlocksCommand, token)
                .ConfigureAwait(false);

            // TODO: The BlocklistHash join seems to be unnecessary, but the join might be required to work around a from really old versions of Duplicati
            // this could be used instead
            //@") D, ""Block"" WHERE  ""BlockQuery"".""BlockHash"" = ""Block"".""Hash"" AND ""BlockQuery"".""BlockSize"" = ""Block"".""Size"" ";
            var selectBlocklistBlocksetEntries = $@"
                SELECT
                    ""E"".""BlocksetID"" AS ""BlocksetID"",
                    ""D"".""FullIndex"" AS ""Index"",
                    ""F"".""ID"" AS ""BlockID""
                FROM
                    ({SELECT_BLOCKLIST_ENTRIES(blocksize, hashsize, m_tempblocklist, blocksize / hashsize)}) ""D"",
                    ""BlocklistHash"" ""E"",
                    ""Block"" ""F"",
                    ""Block"" ""G""
                WHERE
                    ""D"".""BlocksetID"" = ""E"".""BlocksetID""
                    AND ""D"".""BlocklistHash"" = ""E"".""Hash""
                    AND ""D"".""BlocklistSize"" = ""G"".""Size""
                    AND ""D"".""BlocklistHash"" = ""G"".""Hash""
                    AND ""D"".""Blockhash"" = ""F"".""Hash""
                    AND ""D"".""BlockSize"" = ""F"".""Size""
            ";

            var selectBlocksetEntries = $@"
                SELECT
                    ""Blockset"".""ID"" AS ""BlocksetID"",
                    0 AS ""Index"",
                    ""Block"".""ID"" AS ""BlockID""
                FROM
                    ""Blockset"",
                    ""Block"",
                    ""{m_tempsmalllist}"" ""S""
                WHERE
                    ""Blockset"".""Fullhash"" = ""S"".""FileHash""
                    AND ""S"".""BlockHash"" = ""Block"".""Hash""
                    AND ""S"".""BlockSize"" = ""Block"".""Size""
                    AND ""Blockset"".""Length"" = ""S"".""BlockSize""
                    AND ""Blockset"".""Length"" <= {Library.Utility.Utility.FormatInvariant(blocksize)}
            ";

            var selectAllBlocksetEntries = @$"
                {selectBlocklistBlocksetEntries}
                UNION
                {selectBlocksetEntries}
            ";

            var selectFiltered = @$"
                SELECT DISTINCT
                    ""H"".""BlocksetID"",
                    ""H"".""Index"",
                    ""H"".""BlockID""
                FROM ({selectAllBlocksetEntries}) H
                WHERE (
                        ""H"".""BlocksetID""
                        || ':'
                        || ""H"".""Index""
                    ) NOT IN (
                    SELECT (
                        ""ExistingBlocksetEntries"".""BlocksetID""
                        || ':'
                        || ""ExistingBlocksetEntries"".""Index""
                    )
                    FROM ""BlocksetEntry"" ""ExistingBlocksetEntries""
                )
            ";

            var insertBlocksetEntriesCommand = @$"
                INSERT INTO ""BlocksetEntry"" (
                    ""BlocksetID"",
                    ""Index"",
                    ""BlockID""
                )
                {selectFiltered}
            ";

            try
            {
                await cmd.ExecuteNonQueryAsync(insertBlocksetEntriesCommand, token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "BlocksetInsertFailed", ex, "Blockset insert failed, committing temporary tables for trace purposes");

                await using (var fixcmd = m_connection.CreateCommand(m_rtr))
                {
                    await fixcmd.ExecuteNonQueryAsync($@"
                        CREATE TABLE ""{m_tempblocklist}-Failure"" AS
                        SELECT *
                        FROM ""{m_tempblocklist}""
                    ", token)
                        .ConfigureAwait(false);

                    await fixcmd.ExecuteNonQueryAsync($@"
                        CREATE TABLE ""{m_tempsmalllist}-Failure"" AS
                        SELECT *
                        FROM ""{m_tempsmalllist}""
                    ", token)
                        .ConfigureAwait(false);
                }

                throw new Exception("The recreate failed, please create a bug-report from this database and send it to the developers for further analysis");
            }

            await m_rtr.CommitAsync(token: token).ConfigureAwait(false);
        }

        /// <summary>
        /// From the temporary tables 1) insert new blocks into Block (VolumeID to be set at a later stage)
        /// and 2) add missing BlocksetEntry lines
        ///
        /// hashsize and blocksize: global database parameters
        /// hashOnly: do not take in account small blocks - these have been added at the
        /// end of the index handling and are not changed in the dblock handling so we can ignore them
        /// </summary>
        /// Notes:
        ///
        /// temp block list structure: blocklist hash, block hash, index relative to the
        /// beginning of the blocklist hash (NOT the file)
        ///
        /// temp small list structure: filehash, blockhash, blocksize: as the small files are defined
        /// by the fact that they are contained in a single block, blockhash is the same as the filehash,
        /// and blocksize can vary from 0 to the configured block size for the backup
        /// <param name="hashsize">The size of the hashes in the blocklist.</param>
        /// <param name="blocksize"> The size of the blocks in the blocklist.</param>
        /// <param name="hashOnly">If true, only hash entries are processed, ignoring small blocks.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that completes when the operation is finished.</returns>
        public async Task AddBlockAndBlockSetEntryFromTemp(long hashsize, long blocksize, bool hashOnly, CancellationToken token)
        {
            // TODO should values be parameters, rather than hardcoded into the SQL queries?
            await using var cmd = m_connection.CreateCommand(m_rtr);
            var extra = hashOnly ? "" : $@"
                    UNION
                    SELECT
                        ""TS"".""BlockHash"",
                        ""TS"".""BlockSize""
                    FROM ""{m_tempsmalllist}"" ""TS""
                    WHERE NOT EXISTS (
                        SELECT ""X""
                        FROM Block AS ""B""
                        WHERE
                            ""B"".""Hash"" =  ""TS"".""BlockHash""
                            AND ""B"".""Size"" = ""TS"".""BlockSize""
                    )
                ";
            var str_blocksize = Library.Utility.Utility.FormatInvariant(blocksize);
            var str_blocksize_per_hashsize = Library.Utility.Utility.FormatInvariant(blocksize / hashsize);
            var insertBlocksCommand = $@"
                INSERT INTO ""Block"" (
                    ""Hash"",
                    ""Size"",
                    ""VolumeID""
                )
                SELECT DISTINCT
                    ""BlockHash"" AS ""Hash"",
                    ""BlockSize"" AS ""Size"",
                    -1 AS ""VolumeID""
                FROM (
                    SELECT
                        ""NB"".""BlockHash"",
                        MIN({str_blocksize}, ""BS"".""Length"" - ((""NB"".""Index"" + (BH.""Index"" * {str_blocksize_per_hashsize})) * {str_blocksize})) AS ""BlockSize""
                    FROM (
                        SELECT
                            ""TBL"".""BlockListHash"",
                            ""TBL"".""BlockHash"",
                            ""TBL"".""Index"" FROM ""{m_tempblocklist}"" ""TBL""
                        LEFT OUTER JOIN ""Block"" ""B""
                            ON (""B"".""Hash"" = ""TBL"".""BlockHash"")
                        WHERE ""B"".""Hash"" IS NULL
                    ) ""NB""
                    JOIN ""BlocklistHash"" ""BH""
                        ON (""BH"".""Hash"" = ""NB"".""BlocklistHash"")
                    JOIN ""Blockset"" ""BS""
                        ON (""BS"".""ID"" = ""BH"".""Blocksetid"")
                    {extra}
                )
            ";

            extra = hashOnly ? "" : $@"
                UNION
                    SELECT
                        ""BS"".""ID"" AS ""BlocksetID"",
                        0 AS ""Index"",
                        ""BL"".""ID"" AS ""BlockID""
                    FROM ""{m_tempsmalllist}"" ""TS""
                    JOIN ""Blockset"" ""BS""
                        ON (
                            ""BS"".""FullHash"" = ""TS"".""FileHash""
                            AND ""BS"".""Length"" = ""TS"".""BlockSize""
                            AND ""BS"".""Length"" <= {str_blocksize}
                        )
                    JOIN ""Block"" ""BL""
                        ON (
                            ""BL"".""Hash"" = ""TS"".""BlockHash""
                            AND ""BL"".""Size"" = ""TS"".""BlockSize""
                        )
                    LEFT OUTER JOIN ""BlocksetEntry"" ""BE""
                        ON (
                            ""BE"".""BlocksetID"" = ""BS"".""ID""
                            AND ""BE"".""Index"" = 0
                        )
                    WHERE ""BE"".""BlocksetID"" IS NULL
            ";
            var insertBlocksetEntriesCommand = $@"
                INSERT INTO ""BlocksetEntry"" (
                    ""BlocksetID"",
                    ""Index"",
                    ""BlockID""
                )
                SELECT DISTINCT
                    ""BH"".""blocksetid"",
                    (""BH"".""Index"" * {str_blocksize_per_hashsize}) + ""TBL"".""Index"" as ""FullIndex"",
                    ""BK"".""ID"" AS ""BlockID""
                FROM ""{m_tempblocklist}"" ""TBL""
                    JOIN ""blocklisthash"" ""BH""
                        ON (""BH"".""Hash"" = ""TBL"".""blocklisthash"")
                    JOIN ""Block"" ""BK""
                        ON (""BK"".""Hash"" = ""TBL"".""BlockHash"")
                    LEFT OUTER JOIN ""BlocksetEntry"" ""BE""
                        ON (
                            BE.""BlockSetID"" = ""BH"".""BlocksetID""
                            AND BE.""Index"" = (""BH"".""Index"" * {str_blocksize_per_hashsize})+""TBL"".""Index""
                        )
                WHERE ""BE"".""BlockSetID"" IS NULL
                {extra}
            ";

            try
            {
                // Insert discovered new blocks into block table with volumeid = -1
                await cmd.ExecuteNonQueryAsync(insertBlocksCommand, token)
                    .ConfigureAwait(false);

                // Insert corresponding entries into blockset
                await cmd.ExecuteNonQueryAsync(insertBlocksetEntriesCommand, token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "BlockOrBlocksetInsertFailed", ex, "Block or Blockset insert failed, committing temporary tables for trace purposes");

                await using (var fixcmd = m_connection.CreateCommand(m_rtr))
                {
                    await fixcmd.ExecuteNonQueryAsync($@"
                        CREATE TABLE ""{m_tempblocklist}_Failure"" AS
                        SELECT *
                        FROM ""{m_tempblocklist}""
                    ", token)
                        .ConfigureAwait(false);

                    await fixcmd.ExecuteNonQueryAsync($@"
                        CREATE TABLE ""{m_tempsmalllist}_Failure""
                        AS SELECT *
                        FROM ""{m_tempsmalllist}""
                    ", token)
                        .ConfigureAwait(false);
                }

                throw new Exception("The recreate failed, please create a bug-report from this database and send it to the developers for further analysis");
            }
        }

        /// <summary>
        /// Adds a directory entry to the FilesetEntry table, linking it to a fileset and a path prefix.
        /// </summary>
        /// <param name="filesetid">The ID of the fileset to which the entry belongs.</param>
        /// <param name="pathprefixid">The ID of the path prefix for the entry.</param>
        /// <param name="path">The path of the directory entry.</param>
        /// <param name="time">The last modified time of the directory entry.</param>
        /// <param name="metadataid">The ID of the metadata associated with the directory entry.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that when completed, indicates that the directory entry has been added.</returns>
        public async Task AddDirectoryEntry(long filesetid, long pathprefixid, string path, DateTime time, long metadataid, CancellationToken token)
        {
            await AddEntry(filesetid, pathprefixid, path, time, FOLDER_BLOCKSET_ID, metadataid, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a symlink entry to the FilesetEntry table, linking it to a fileset and a path prefix.
        /// </summary>
        /// <param name="filesetid">The ID of the fileset to which the entry belongs.</param>
        /// <param name="pathprefixid">The ID of the path prefix for the entry.</param>
        /// <param name="path">The path of the symlink entry.</param>
        /// <param name="time">The last modified time of the symlink entry.</param>
        /// <param name="metadataid">The ID of the metadata associated with the symlink entry.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that when completed, indicates that the symlink entry has been added.</returns>
        public async Task AddSymlinkEntry(long filesetid, long pathprefixid, string path, DateTime time, long metadataid, CancellationToken token)
        {
            await AddEntry(filesetid, pathprefixid, path, time, SYMLINK_BLOCKSET_ID, metadataid, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a file entry to the FilesetEntry table, linking it to a fileset and a path prefix.
        /// </summary>
        /// <param name="filesetid">The ID of the fileset to which the entry belongs.</param>
        /// <param name="pathprefixid">The ID of the path prefix for the entry.</param>
        /// <param name="path">The path of the file entry.</param>
        /// <param name="time">The last modified time of the file entry.</param>
        /// <param name="blocksetid">The ID of the blockset associated with the file entry.</param>
        /// <param name="metadataid">The ID of the metadata associated with the file entry.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that when completed, indicates that the file entry has been added.</returns>
        public async Task AddFileEntry(long filesetid, long pathprefixid, string path, DateTime time, long blocksetid, long metadataid, CancellationToken token)
        {
            await AddEntry(filesetid, pathprefixid, path, time, blocksetid, metadataid, token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a file and fileset entry to the FilesetEntry table, linking it to a fileset and a path prefix.
        /// If the file already exists, it retrieves its ID; otherwise, it inserts a new file entry.
        /// Then, it inserts the fileset entry linking the file to the fileset.
        /// </summary>
        /// <param name="filesetid">The ID of the fileset to which the entry belongs.</param>
        /// <param name="pathprefixid">The ID of the path prefix for the entry.</param>
        /// <param name="path">The path of the file entry.</param>
        /// <param name="time">The last modified time of the file entry.</param>
        /// <param name="blocksetid">The ID of the blockset associated with the file entry.</param>
        /// <param name="metadataid">The ID of the metadata associated with the file entry.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that when completed, indicates that the file and fileset entry has been added.</returns>
        private async Task AddEntry(long filesetid, long pathprefixid, string path, DateTime time, long blocksetid, long metadataid, CancellationToken token)
        {
            var fileid = await m_findFilesetCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@PrefixId", pathprefixid)
                .SetParameterValue("@Path", path)
                .SetParameterValue("@BlocksetId", blocksetid)
                .SetParameterValue("@MetadataId", metadataid)
                .ExecuteScalarInt64Async(-1, token)
                .ConfigureAwait(false);

            if (fileid < 0)
            {
                fileid = await m_insertFileCommand
                    .SetTransaction(m_rtr)
                    .SetParameterValue("@PrefixId", pathprefixid)
                    .SetParameterValue("@Path", path)
                    .SetParameterValue("@BlocksetId", blocksetid)
                    .SetParameterValue("@MetadataId", metadataid)
                    .ExecuteScalarInt64Async(-1, token)
                    .ConfigureAwait(false);
            }

            await m_insertFilesetEntryCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@FilesetId", filesetid)
                .SetParameterValue("@FileId", fileid)
                .SetParameterValue("@LastModified", time.ToUniversalTime().Ticks)
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a new metadataset to the database, creating a blockset if it does not already exist.
        /// If the metadataset already exists, it returns its ID.
        /// </summary>
        /// <param name="metahash">The full hash of the metadataset.</param>
        /// <param name="metahashsize">The size of the metadataset in bytes.</param>
        /// <param name="metablocklisthashes">A collection of blocklist hashes associated with the metadataset.</param>
        /// <param name="expectedmetablocklisthashes">The expected number of blocklist hashes for the metadataset.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that returns the ID of the added or existing metadataset.</returns>
        public async Task<long> AddMetadataset(string metahash, long metahashsize, IEnumerable<string> metablocklisthashes, long expectedmetablocklisthashes, CancellationToken token)
        {
            var metadataid = -1L;
            if (metahash == null)
                return metadataid;

            metadataid = await m_findMetadatasetCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@FullHash", metahash)
                .SetParameterValue("@Length", metahashsize)
                .ExecuteScalarInt64Async(-1, token)
                .ConfigureAwait(false);

            if (metadataid != -1)
                return metadataid;

            var blocksetid =
                await AddBlockset(metahash, metahashsize, metablocklisthashes, expectedmetablocklisthashes, token)
                    .ConfigureAwait(false);

            metadataid = await m_insertMetadatasetCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@BlocksetId", blocksetid)
                .ExecuteScalarInt64Async(-1, token)
                .ConfigureAwait(false);

            return metadataid;
        }

        /// <summary>
        /// Adds a new blockset to the database, creating it if it does not already exist.
        /// If the blockset already exists, it returns its ID.
        /// </summary>
        /// <param name="fullhash">The full hash of the blockset.</param>
        /// <param name="size">The size of the blockset in bytes.</param>
        /// <param name="blocklisthashes">A collection of blocklist hashes associated with the blockset.</param>
        /// <param name="expectedblocklisthashes">The expected number of blocklist hashes for the blockset.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that returns the ID of the added or existing blockset.</returns>
        public async Task<long> AddBlockset(string fullhash, long size, IEnumerable<string> blocklisthashes, long expectedblocklisthashes, CancellationToken token)
        {
            var blocksetid = await m_findBlocksetCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@Length", size)
                .SetParameterValue("@FullHash", fullhash)
                .ExecuteScalarInt64Async(-1, token)
                .ConfigureAwait(false);

            if (blocksetid != -1)
                return blocksetid;

            blocksetid = await m_insertBlocksetCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@Length", size)
                .SetParameterValue("@FullHash", fullhash)
                .ExecuteScalarInt64Async(-1, token)
                .ConfigureAwait(false);

            long c = 0;
            if (blocklisthashes != null)
            {
                var index = 0L;
                m_insertBlocklistHashCommand
                    .SetTransaction(m_rtr)
                    .SetParameterValue("@BlocksetId", blocksetid);

                foreach (var hash in blocklisthashes)
                {
                    if (!string.IsNullOrEmpty(hash))
                    {
                        c++;
                        if (c <= expectedblocklisthashes)
                        {
                            await m_insertBlocklistHashCommand
                                .SetParameterValue("@Index", index++)
                                .SetParameterValue("@Hash", hash)
                                .ExecuteNonQueryAsync(token)
                                .ConfigureAwait(false);
                        }
                    }
                }
            }

            if (c != expectedblocklisthashes)
                Logging.Log.WriteWarningMessage(LOGTAG, "MismatchInBlocklistHashCount", null, "Mismatching number of blocklist hashes detected on blockset {2}. Expected {0} blocklist hashes, but found {1}", expectedblocklisthashes, c, blocksetid);

            return blocksetid;
        }

        /// <summary>
        /// Updates the block information in the database, inserting a new block if it does not exist,
        /// updating the volume ID if it does, or inserting a duplicate block entry if the block already exists with a different volume ID.
        /// </summary>
        /// <param name="hash">The hash of the block to update.</param>
        /// <param name="size">The size of the block in bytes.</param>
        /// <param name="volumeID">The ID of the volume to which the block belongs.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that returns a tuple indicating whether any changes were made and whether the block was newly inserted.</returns>
        public async Task<(bool, bool)> UpdateBlock(string hash, long size, long volumeID, CancellationToken token)
        {
            var anyChange = false;
            var currentVolumeId = await m_findHashBlockCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@Hash", hash)
                .SetParameterValue("@Size", size)
                .ExecuteScalarInt64Async(-2, token)
                .ConfigureAwait(false);

            if (currentVolumeId == volumeID)
                return (anyChange, false);

            anyChange = true;

            if (currentVolumeId == -2)
            {
                //Insert
                await m_insertBlockCommand
                    .SetTransaction(m_rtr)
                    .SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Size", size)
                    .SetParameterValue("@VolumeId", volumeID)
                    .ExecuteNonQueryAsync(token)
                    .ConfigureAwait(false);

                return (anyChange, true);
            }
            else if (currentVolumeId == -1)
            {
                //Update
                var c = await m_updateBlockVolumeCommand
                    .SetTransaction(m_rtr)
                    .SetParameterValue("@VolumeId", volumeID)
                    .SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Size", size)
                    .ExecuteNonQueryAsync(token)
                    .ConfigureAwait(false);

                if (c != 1)
                    throw new Exception($"Failed to update table, found {c} entries for key {hash} with size {size}");

                return (anyChange, true);
            }
            else
            {
                await m_insertDuplicateBlockCommand
                    .SetTransaction(m_rtr)
                    .SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Size", size)
                    .SetParameterValue("@VolumeId", volumeID)
                    .ExecuteNonQueryAsync(token)
                    .ConfigureAwait(false);

                return (anyChange, false);
            }
        }

        /// <summary>
        /// Adds a link between a small blockset and a block in the database.
        /// This method is used to track small blocks that are part of a larger blockset, allowing for efficient storage and retrieval of small files.
        /// </summary>
        /// <param name="filehash">The hash of the file that contains the small block.</param>
        /// <param name="blockhash">The hash of the small block.</param>
        /// <param name="blocksize">The size of the small block in bytes.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that completes when the small blockset link has been added.</returns>
        public async Task AddSmallBlocksetLink(string filehash, string blockhash, long blocksize, CancellationToken token)
        {
            await m_insertSmallBlockset
                .SetTransaction(m_rtr)
                .SetParameterValue("@FileHash", filehash)
                .SetParameterValue("@BlockHash", blockhash)
                .SetParameterValue("@BlockSize", blocksize)
                .ExecuteNonQueryAsync(token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a temporary blocklist hash to the database, along with its associated block hashes.
        /// This method is used to track temporary blocklists that are being processed, allowing for efficient management of blocklist data.
        /// </summary>
        /// <param name="hash">The hash of the temporary blocklist.</param>
        /// <param name="blocklisthashes">A collection of block hashes associated with the temporary blocklist.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that returns a boolean indicating whether the temporary blocklist hash was successfully added.</returns>
        public async Task<bool> AddTempBlockListHash(string hash, IEnumerable<string> blocklisthashes, CancellationToken token)
        {
            var r = await m_findTempBlockListHashCommand
                .SetTransaction(m_rtr)
                .SetParameterValue("@BlocklistHash", hash)
                .ExecuteScalarAsync(token)
                .ConfigureAwait(false);

            if (r != null && r != DBNull.Value)
                return false;

            m_insertTempBlockListHash
                .SetTransaction(m_rtr)
                .SetParameterValue("@BlocklistHash", hash);

            var index = 0L;

            foreach (var s in blocklisthashes)
            {
                await m_insertTempBlockListHash
                    .SetParameterValue("@BlockHash", s)
                    .SetParameterValue("@Index", index++)
                    .ExecuteNonQueryAsync(token)
                    .ConfigureAwait(false);
            }

            return true;
        }

        /// <summary>
        /// Retrieves a list of block hashes associated with a specific volume ID from the database.
        /// This method is used to obtain the block hashes for a given volume, allowing for efficient retrieval of block data.
        /// </summary>
        /// <param name="volumeid">The ID of the volume for which to retrieve block hashes.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>An asynchronous enumerable collection of block hashes associated with the specified volume ID.</returns>
        public async IAsyncEnumerable<string> GetBlockLists(long volumeid, [EnumeratorCancellation] CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(@"
                SELECT DISTINCT ""BlocklistHash"".""Hash""
                FROM
                    ""BlocklistHash"",
                    ""Block""
                WHERE
                    ""Block"".""Hash"" = ""BlocklistHash"".""Hash""
                    AND ""Block"".""VolumeID"" = @VolumeId
            ")
                .SetTransaction(m_rtr)
                .SetParameterValue("@VolumeId", volumeid);

            await using var rd = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await rd.ReadAsync(token).ConfigureAwait(false))
                yield return rd.ConvertValueToString(0) ?? "";
        }

        /// <summary>
        /// Retrieves a list of remote volumes that are missing blocks based on the specified parameters.
        /// This method is used to identify volumes that need to be processed for missing blocks, allowing for efficient management of remote storage.
        /// </summary>
        /// <param name="passNo">The current pass number, used to determine the state of processing.</param>
        /// <param name="blocksize">The size of the blocks in the blocklist.</param>
        /// <param name="hashsize">The size of the hashes in the blocklist.</param>
        /// <param name="forceBlockUse">A boolean indicating whether to force the use of blocks, even if they are not missing.</param>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>An asynchronous enumerable collection of remote volumes that are missing blocks.</returns>
        public async IAsyncEnumerable<IRemoteVolume> GetMissingBlockListVolumes(int passNo, long blocksize, long hashsize, bool forceBlockUse, [EnumeratorCancellation] CancellationToken token)
        {
            await using var cmd = m_connection.CreateCommand(m_rtr);
            var selectCommand = @"
                    SELECT DISTINCT
                        ""RemoteVolume"".""Name"",
                        ""RemoteVolume"".""Hash"",
                        ""RemoteVolume"".""Size"",
                        ""RemoteVolume"".""ID""
                    FROM ""RemoteVolume""
                ";

            var missingBlocklistEntries = $@"
                    SELECT ""BlocklistHash"".""Hash""
                    FROM ""BlocklistHash""
                    LEFT OUTER JOIN ""BlocksetEntry""
                        ON ""BlocksetEntry"".""Index"" = (""BlocklistHash"".""Index"" * {Library.Utility.Utility.FormatInvariant(blocksize / hashsize)})
                        AND ""BlocksetEntry"".""BlocksetID"" = ""BlocklistHash"".""BlocksetID""
                    WHERE ""BlocksetEntry"".""BlocksetID"" IS NULL
                ";

            var missingBlockInfo = @"
                    SELECT ""VolumeID""
                    FROM ""Block""
                    WHERE
                        ""VolumeID"" < 0
                        AND ""Size"" > 0
                ";

            var missingBlocklistVolumes = $@"
                    SELECT ""VolumeID""
                    FROM
                        ""Block"",
                        ({missingBlocklistEntries}) ""A""
                    WHERE ""A"".""Hash"" = ""Block"".""Hash""
                ";

            var countMissingInformation = $@"
                    SELECT COUNT(*)
                    FROM (
                        SELECT DISTINCT ""VolumeID""
                        FROM (
                            {missingBlockInfo}
                            UNION {missingBlocklistVolumes}
                        )
                    )
                ";

            if (passNo == 0)
            {
                // On the first pass, we select all the volumes we know we need,
                // which may be an empty list
                cmd.SetCommandAndParameters($@"
                        {selectCommand}
                        WHERE ""ID"" IN ({missingBlocklistVolumes})
                    ");

                // Reset the list
                m_proccessedVolumes.Clear();
            }
            else
            {
                //On anything but the first pass, we check if we are done
                var r = await cmd
                    .SetCommandAndParameters(countMissingInformation)
                    .ExecuteScalarInt64Async(0, token)
                    .ConfigureAwait(false);

                if (r == 0 && !forceBlockUse)
                    yield break;

                if (passNo == 1)
                {
                    // On the second pass, we select all volumes that are not mentioned in the db

                    var mentionedVolumes = @"
                            SELECT DISTINCT ""VolumeID""
                            FROM ""Block""
                        ";

                    cmd
                        .SetCommandAndParameters($@"
                                {selectCommand}
                                WHERE
                                    ""ID"" NOT IN ({mentionedVolumes})
                                    AND ""Type"" = @Type
                            ")
                        .SetParameterValue("@Type", RemoteVolumeType.Blocks.ToString());


                }
                else
                {
                    // On the final pass, we select all volumes
                    // the filter will ensure that we do not download anything twice
                    cmd
                        .SetCommandAndParameters($@"
                                {selectCommand}
                                WHERE ""Type"" = @Type
                            ")
                        .SetParameterValue("@Type", RemoteVolumeType.Blocks.ToString());
                }
            }

            await using var rd = await cmd
                .ExecuteReaderAsync(token)
                .ConfigureAwait(false);

            while (await rd.ReadAsync(token).ConfigureAwait(false))
            {
                var volumeID = rd.ConvertValueToInt64(3);

                // Guard against multiple downloads of the same file
                if (m_proccessedVolumes.TryAdd(volumeID, volumeID))
                {
                    yield return new RemoteVolume(
                        rd.ConvertValueToString(0),
                        rd.ConvertValueToString(1),
                        rd.ConvertValueToInt64(2, -1)
                    );
                }
            }
        }

        /// <summary>
        /// Cleans up missing volumes by replacing blocks for non-present remote files with existing ones,
        /// removing references to non-present remote files, and marking index files for later removal.
        /// </summary>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that completes when the cleanup operation is finished.</returns>
        public async Task CleanupMissingVolumes(CancellationToken token)
        {
            var tablename = "SwapBlocks-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

            // TODO: either hardcode all string constants or none

            // The first part of this query swaps out blocks for non-present remote files with
            // existing ones (as recorded in the DuplicateBlock table)
            // The second part removes references to the non-present remote files,
            // and marks the index files that pointed to them, such that they will be removed later on
            var sql = $@"
                CREATE TEMPORARY TABLE ""{tablename}"" AS
                SELECT
                    ""A"".""ID"" AS ""BlockID"",
                    ""A"".""VolumeID"" AS ""SourceVolumeID"",
                    ""A"".""State"" AS ""SourceVolumeState"",
                    ""B"".""VolumeID"" AS ""TargetVolumeID"",
                    ""B"".""State"" AS ""TargetVolumeState""
                FROM
                    (
                        SELECT
                            ""Block"".""ID"",
                            ""Block"".""VolumeID"",
                            ""Remotevolume"".""State""
                        FROM
                            ""Block"",
                            ""Remotevolume""
                        WHERE
                            ""Block"".""VolumeID"" = ""Remotevolume"".""ID""
                            AND ""Remotevolume"".""State"" = '{Library.Utility.Utility.FormatInvariant(RemoteVolumeState.Temporary)}'
                    ) A,
                    (
                        SELECT
                            ""DuplicateBlock"".""BlockID"",
                            MIN(""DuplicateBlock"".""VolumeID"") AS ""VolumeID"",
                            ""Remotevolume"".""State""
                        FROM
                            ""DuplicateBlock"",
                            ""Remotevolume""
                        WHERE
                            ""DuplicateBlock"".""VolumeID"" = ""Remotevolume"".""ID""
                            AND ""Remotevolume"".""State"" = '{Library.Utility.Utility.FormatInvariant(RemoteVolumeState.Verified)}'
                        GROUP BY
                            ""DuplicateBlock"".""BlockID"",
                            ""Remotevolume"".""State""
                    ) B
                WHERE ""A"".""ID"" = ""B"".""BlockID"";

                UPDATE ""Block""
                SET ""VolumeID"" = (
                    SELECT ""TargetVolumeID""
                    FROM ""{tablename}""
                    WHERE ""Block"".""ID"" = ""{tablename}"".""BlockID""
                )
                WHERE ""Block"".""ID"" IN (
                    SELECT ""BlockID""
                    FROM ""{tablename}""
                );

                UPDATE ""DuplicateBlock""
                SET ""VolumeID"" = (
                    SELECT ""SourceVolumeID""
                    FROM ""{tablename}""
                    WHERE ""DuplicateBlock"".""BlockID"" = ""{tablename}"".""BlockID""
                )
                WHERE (
                    ""DuplicateBlock"".""BlockID"",
                    ""DuplicateBlock"".""VolumeID""
                ) IN (
                    SELECT
                        ""BlockID"",
                        ""TargetVolumeID""
                    FROM ""{tablename}""
                );

                DROP TABLE ""{tablename}"";

                DELETE FROM ""IndexBlockLink""
                WHERE ""BlockVolumeID"" IN (
                    SELECT ""ID""
                    FROM ""RemoteVolume""
                    WHERE
                        ""Type"" = '{Library.Utility.Utility.FormatInvariant(RemoteVolumeType.Blocks)}'
                        AND ""State"" = '{Library.Utility.Utility.FormatInvariant(RemoteVolumeState.Temporary)}'
                        AND ""ID"" NOT IN (
                            SELECT DISTINCT ""VolumeID""
                            FROM ""Block""
                        )
                );

                DELETE FROM ""DuplicateBlock""
                WHERE ""VolumeID"" IN (
                    SELECT ""ID""
                    FROM ""RemoteVolume""
                    WHERE
                        ""Type"" = '{Library.Utility.Utility.FormatInvariant(RemoteVolumeType.Blocks)}'
                        AND ""State"" = '{Library.Utility.Utility.FormatInvariant(RemoteVolumeState.Temporary)}'
                        AND ""ID"" NOT IN (
                            SELECT DISTINCT ""VolumeID""
                            FROM ""Block""
                        )
                );

                DELETE FROM ""RemoteVolume""
                WHERE
                    ""Type"" = '{Library.Utility.Utility.FormatInvariant(RemoteVolumeType.Blocks)}'
                    AND ""State"" = '{Library.Utility.Utility.FormatInvariant(RemoteVolumeState.Temporary)}'
                    AND ""ID"" NOT IN (
                        SELECT DISTINCT ""VolumeID""
                        FROM ""Block""
                    );
            ";

            // We could delete these, but we don't have to, so we keep them around until the next compact is done
            // UPDATE ""RemoteVolume"" SET ""State"" = ""{3}"" WHERE ""Type"" = ""{5}"" AND ""ID"" NOT IN (SELECT ""IndexVolumeID"" FROM ""IndexBlockLink"");

            var countsql = $@"
                SELECT COUNT(*)
                FROM ""RemoteVolume""
                WHERE
                    ""State"" = '{Library.Utility.Utility.FormatInvariant(RemoteVolumeState.Temporary)}'
                    AND ""Type"" = '{Library.Utility.Utility.FormatInvariant(RemoteVolumeType.Blocks)}'
            ";

            await using var cmd = m_connection.CreateCommand(m_rtr);
            var cnt = await cmd.ExecuteScalarInt64Async(countsql, token)
                .ConfigureAwait(false);

            if (cnt > 0)
            {
                try
                {
                    await cmd.ExecuteNonQueryAsync(sql, token)
                        .ConfigureAwait(false);

                    var cnt2 = await cmd.ExecuteScalarInt64Async(countsql, token)
                        .ConfigureAwait(false);

                    Logging.Log.WriteWarningMessage(LOGTAG, "MissingVolumesDetected", null, "Replaced blocks for {0} missing volumes; there are now {1} missing volumes", cnt, cnt2);
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "MissingVolumesDetected", ex, "Found {0} missing volumes; failed while attempting to replace blocks from existing volumes", cnt);
                    throw;
                }
            }
        }

        /// <summary>
        /// Move blocks that are not referenced by any files to DeletedBlock table.
        /// Needs to be called after the last FindMissingBlocklistHashes, otherwise the tables are not up to date.
        /// </summary>
        /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>A task that completes when the cleanup operation is finished.</returns>
        public async Task CleanupDeletedBlocks(CancellationToken token)
        {
            // Find out which blocks are deleted and move them into DeletedBlock, so that compact notices these blocks are empty
            // Deleted blocks do not appear in the BlocksetEntry and not in the BlocklistHash table

            var tmptablename = "DeletedBlocks-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

            await using var cmd = m_connection.CreateCommand(m_rtr);
            // 1. Select blocks not used by any file and not as a blocklist into temporary table
            await cmd.ExecuteNonQueryAsync($@"
                CREATE TEMPORARY TABLE ""{tmptablename}"" AS
                SELECT
                    ""Block"".""ID"",
                    ""Block"".""Hash"",
                    ""Block"".""Size"",
                    ""Block"".""VolumeID""
                FROM ""Block""
                WHERE
                    ""Block"".""ID"" NOT IN (
                        SELECT ""BlocksetEntry"".""BlockID""
                        FROM ""BlocksetEntry""
                    )
                    AND ""Block"".""Hash"" NOT IN (
                        SELECT ""BlocklistHash"".""Hash""
                        FROM ""BlocklistHash""
                    )
            ", token)
                .ConfigureAwait(false);

            // 2. Insert blocks into DeletedBlock table
            await cmd.ExecuteNonQueryAsync($@"
                INSERT INTO ""DeletedBlock"" (
                    ""Hash"",
                    ""Size"",
                    ""VolumeID""
                )
                SELECT
                    ""Hash"",
                    ""Size"",
                    ""VolumeID""
                FROM ""{tmptablename}""
            ", token)
                .ConfigureAwait(false);

            // 3. Remove blocks from Block table
            await cmd.ExecuteNonQueryAsync($@"
                DELETE FROM ""Block""
                WHERE ""ID"" IN (
                    SELECT ""ID""
                    FROM ""{tmptablename}""
                )
            ", token)
                .ConfigureAwait(false);

            await cmd
                .ExecuteNonQueryAsync($@"DROP TABLE IF EXISTS ""{tmptablename}""", token)
                .ConfigureAwait(false);

            await m_rtr.CommitAsync(token: token).ConfigureAwait(false);
        }

        public override void Dispose()
        {
            DisposeAsync().AsTask().Await();

        }

        public async override ValueTask DisposeAsync()
        {
            await using (var cmd = m_connection.CreateCommand(m_rtr))
            {
                if (m_tempblocklist != null)
                    try
                    {
                        await cmd.ExecuteNonQueryAsync(@$"DROP TABLE IF EXISTS ""{m_tempblocklist}""", default)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "FailedToDropTempBlocklist", ex, "Failed to drop temporary blocklist table {0}", m_tempblocklist);
                    }
                    finally
                    {
                        m_tempblocklist = null!;
                    }

                if (m_tempsmalllist != null)
                    try
                    {
                        await cmd.ExecuteNonQueryAsync(@$"DROP TABLE IF EXISTS ""{m_tempsmalllist}""", default)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "FailedToDropTempSmalllist", ex, "Failed to drop temporary smalllist table {0}", m_tempsmalllist);
                    }
                    finally
                    {
                        m_tempsmalllist = null!;
                    }

            }

            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
