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
using System.Data;
using System.Collections.Generic;

namespace Duplicati.Library.Main.Database
{
    internal class LocalRecreateDatabase : LocalRestoreDatabase
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(LocalRecreateDatabase));

        private class PathEntryKeeper
        {
            private struct KeyValueComparer : IComparer<KeyValuePair<long, long>>
            {
                public int Compare(KeyValuePair<long, long> x, KeyValuePair<long, long> y)
                {
                    return x.Key == y.Key ?
                            (x.Value == y.Value ?
                                0
                                : (x.Value < y.Value ? -1 : 1))
                            : (x.Key < y.Key ? -1 : 1);
                }
            }
        }

        private readonly IDbCommand m_insertFileCommand;
        private readonly IDbCommand m_insertFilesetEntryCommand;
        private readonly IDbCommand m_insertMetadatasetCommand;
        private readonly IDbCommand m_insertBlocksetCommand;
        private readonly IDbCommand m_insertBlocklistHashCommand;
        private readonly IDbCommand m_updateBlockVolumeCommand;
        private readonly IDbCommand m_insertTempBlockListHash;
        private readonly IDbCommand m_insertSmallBlockset;
        private readonly IDbCommand m_findBlocksetCommand;
        private readonly IDbCommand m_findMetadatasetCommand;
        private readonly IDbCommand m_findFilesetCommand;
        private readonly IDbCommand m_findTempBlockListHashCommand;
        private readonly IDbCommand m_findHashBlockCommand;
        private readonly IDbCommand m_insertBlockCommand;
        private readonly IDbCommand m_insertDuplicateBlockCommand;

        private string m_tempblocklist;
        private string m_tempsmalllist;

        /// <summary>
        /// A lookup table that prevents multiple downloads of the same volume
        /// </summary>
        private readonly Dictionary<long, long> m_proccessedVolumes = new Dictionary<long, long>();

        // SQL that finds index and block size for all blocklist hashes, based on the temporary hash list
        // with vars Used:
        // {0} --> Blocksize
        // {1} --> BlockHash-Size
        // {2} --> Temp-Table
        // {3} --> FullBlocklist-BlockCount [equals ({0} / {1}), if SQLite pays respect to ints]
        private static string SELECT_BLOCKLIST_ENTRIES(long blocksize, long blockhashsize, string temptable, long fullBlockListBlockCount) =>
            FormatInvariant($@" 
        SELECT
            ""E"".""BlocksetID"",
            ""F"".""Index"" + (""E"".""BlocklistIndex"" * {fullBlockListBlockCount}) AS ""FullIndex"",
            ""F"".""BlockHash"",
            MIN({blocksize}, ""E"".""Length"" - ((""F"".""Index"" + (""E"".""BlocklistIndex"" * {fullBlockListBlockCount})) * {blocksize})) AS ""BlockSize"",
            ""E"".""Hash"",
            ""E"".""BlocklistSize"",
            ""E"".""BlocklistHash""
        FROM
            (
                    SELECT * FROM
                    (
                        SELECT 
                            ""A"".""BlocksetID"",
                            ""A"".""Index"" AS ""BlocklistIndex"",
                            MIN({fullBlockListBlockCount} * {blockhashsize}, (((""B"".""Length"" + {blocksize} - 1) / {blocksize}) - (""A"".""Index"" * ({fullBlockListBlockCount}))) * {blockhashsize}) AS ""BlocklistSize"",
                            ""A"".""Hash"" AS ""BlocklistHash"",
                            ""B"".""Length""
                        FROM 
                            ""BlocklistHash"" A,
                            ""Blockset"" B
                        WHERE 
                            ""B"".""ID"" = ""A"".""BlocksetID""
                    ) C,
                    ""Block"" D
                WHERE
                   ""C"".""BlocklistHash"" = ""D"".""Hash""
                   AND
                   ""C"".""BlocklistSize"" = ""D"".""Size""
            ) E,
            ""{temptable}"" F
        WHERE
           ""F"".""BlocklistHash"" = ""E"".""Hash""
        ORDER BY 
           ""E"".""BlocksetID"",
           ""FullIndex""
");

        public LocalRecreateDatabase(LocalDatabase parentdb, Options options)
            : base(parentdb)
        {
            m_tempblocklist = "TempBlocklist_" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
            m_tempsmalllist = "TempSmalllist_" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

            using (var cmd = m_connection.CreateCommand())
            {
                cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{m_tempblocklist}"" (""BlockListHash"" TEXT NOT NULL, ""BlockHash"" TEXT NOT NULL, ""Index"" INTEGER NOT NULL)"));
                cmd.ExecuteNonQuery(FormatInvariant($@"CREATE INDEX ""Index_{m_tempblocklist}"" ON ""{m_tempblocklist}"" (""BlockListHash"");"));

                cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{m_tempsmalllist}"" (""FileHash"" TEXT NOT NULL, ""BlockHash"" TEXT NOT NULL, ""BlockSize"" INTEGER NOT NULL)"));
                cmd.ExecuteNonQuery(FormatInvariant($@"CREATE UNIQUE INDEX ""Index_File_{m_tempsmalllist}"" ON ""{m_tempsmalllist}"" (""FileHash"", ""BlockSize"");"));
                cmd.ExecuteNonQuery(FormatInvariant($@"CREATE UNIQUE INDEX ""Index_Block_{m_tempsmalllist}"" ON ""{m_tempsmalllist}"" (""BlockHash"", ""BlockSize"");"));
            }

            m_insertFileCommand = m_connection.CreateCommand(@"INSERT INTO ""FileLookup"" (""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"") VALUES (@PrefixId,@Path,@BlocksetId,@MetadataId); SELECT last_insert_rowid();");
            m_insertFilesetEntryCommand = m_connection.CreateCommand(@"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") VALUES (@filesetId,@FileId,@LastModified)");
            m_insertMetadatasetCommand = m_connection.CreateCommand(@"INSERT INTO ""Metadataset"" (""BlocksetID"") VALUES (@BlocksetId); SELECT last_insert_rowid();");
            m_insertBlocksetCommand = m_connection.CreateCommand(@"INSERT INTO ""Blockset"" (""Length"", ""FullHash"") VALUES (@Length,@FullHash); SELECT last_insert_rowid();");
            m_insertBlocklistHashCommand = m_connection.CreateCommand(@"INSERT INTO ""BlocklistHash"" (""BlocksetID"", ""Index"", ""Hash"") VALUES (@BlocksetId,@Index,@Hash)");
            m_updateBlockVolumeCommand = m_connection.CreateCommand(@"UPDATE ""Block"" SET ""VolumeID"" = @VolumeId WHERE ""Hash"" = @Hash AND ""Size"" = @Size");
            m_insertTempBlockListHash = m_connection.CreateCommand(FormatInvariant($@"INSERT INTO ""{m_tempblocklist}"" (""BlocklistHash"", ""BlockHash"", ""Index"") VALUES (@BlocklistHash,@BlockHash,@Index) "));
            m_insertSmallBlockset = m_connection.CreateCommand(FormatInvariant($@"INSERT OR IGNORE INTO ""{m_tempsmalllist}"" (""FileHash"", ""BlockHash"", ""BlockSize"") VALUES (@FileHash,@BlockHash,@BlockSize) "));
            m_findBlocksetCommand = m_connection.CreateCommand(@"SELECT ""ID"" FROM ""Blockset"" WHERE ""Length"" = @Length AND ""FullHash"" = @FullHash ");
            m_findMetadatasetCommand = m_connection.CreateCommand(@"SELECT ""Metadataset"".""ID"" FROM ""Metadataset"",""Blockset"" WHERE ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID"" AND ""Blockset"".""FullHash"" = @FullHash AND ""Blockset"".""Length"" = @Length ");
            m_findFilesetCommand = m_connection.CreateCommand(@"SELECT ""ID"" FROM ""FileLookup"" WHERE ""PrefixID"" = @PrefixId AND ""Path"" = @Path AND ""BlocksetID"" = @BlocksetId AND ""MetadataID"" = @MetadataId ");
            m_findTempBlockListHashCommand = m_connection.CreateCommand(FormatInvariant($@"SELECT DISTINCT ""BlockListHash"" FROM ""{m_tempblocklist}"" WHERE ""BlockListHash"" = @BlocklistHash "));
            m_findHashBlockCommand = m_connection.CreateCommand(@"SELECT ""VolumeID"" FROM ""Block"" WHERE ""Hash"" = @Hash AND ""Size"" = @Size ");
            m_insertBlockCommand = m_connection.CreateCommand(@"INSERT INTO ""Block"" (""Hash"", ""Size"", ""VolumeID"") VALUES (@Hash,@Size,@VolumeId)");
            m_insertDuplicateBlockCommand = m_connection.CreateCommand(@"INSERT OR IGNORE INTO ""DuplicateBlock"" (""BlockID"", ""VolumeID"") VALUES ((SELECT ""ID"" FROM ""Block"" WHERE ""Hash"" = @Hash AND ""Size"" = @Size), @VolumeId)");
        }

        public void FindMissingBlocklistHashes(long hashsize, long blocksize, IDbTransaction transaction)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            {
                //Update all small blocklists and matching blocks

                var selectSmallBlocks = FormatInvariant($@"SELECT ""BlockHash"", ""BlockSize"" FROM ""{m_tempsmalllist}""");

                var selectBlockHashes = FormatInvariant($@"SELECT ""BlockHash"" AS ""FullHash"", ""BlockSize"" AS ""Length"" FROM ( {SELECT_BLOCKLIST_ENTRIES(blocksize, hashsize, m_tempblocklist, blocksize / hashsize)} )");

                var selectAllBlocks = @"SELECT DISTINCT ""FullHash"", ""Length"" FROM (" + selectBlockHashes + " UNION " + selectSmallBlocks + " )";

                var selectNewBlocks = FormatInvariant($@"SELECT ""FullHash"" AS ""Hash"", ""Length"" AS ""Size"", -1 AS ""VolumeID""
FROM (SELECT ""A"".""FullHash"", ""A"".""Length"", CASE WHEN ""B"".""Hash"" IS NULL THEN '' ELSE ""B"".""Hash"" END AS ""Hash"", CASE WHEN ""B"".""Size"" is NULL THEN -1 ELSE ""B"".""Size"" END AS ""Size"" FROM ({selectAllBlocks}) A
LEFT OUTER JOIN ""Block"" B ON ""B"".""Hash"" =  ""A"".""FullHash"" AND ""B"".""Size"" = ""A"".""Length"" )
WHERE ""FullHash"" != ""Hash"" AND ""Length"" != ""Size"" ");

                var insertBlocksCommand =
                    @"INSERT INTO ""Block"" (""Hash"", ""Size"", ""VolumeID"") " +
                    selectNewBlocks;

                // Insert all known blocks into block table with volumeid = -1
                cmd.ExecuteNonQuery(insertBlocksCommand);

                var selectBlocklistBlocksetEntries = FormatInvariant(
                    // TODO: The BlocklistHash join seems to be unnecessary, but the join might be required to work around a from really old versions of Duplicati
                    // this could be used instead
                    //@") D, ""Block"" WHERE  ""BlockQuery"".""BlockHash"" = ""Block"".""Hash"" AND ""BlockQuery"".""BlockSize"" = ""Block"".""Size"" ";
                    $@"SELECT ""E"".""BlocksetID"" AS ""BlocksetID"", ""D"".""FullIndex"" AS ""Index"", ""F"".""ID"" AS ""BlockID"" FROM ( {SELECT_BLOCKLIST_ENTRIES(blocksize, hashsize, m_tempblocklist, blocksize / hashsize)}) D, ""BlocklistHash"" E, ""Block"" F, ""Block"" G WHERE ""D"".""BlocksetID"" = ""E"".""BlocksetID"" AND ""D"".""BlocklistHash"" = ""E"".""Hash"" AND ""D"".""BlocklistSize"" = ""G"".""Size"" AND ""D"".""BlocklistHash"" = ""G"".""Hash"" AND ""D"".""Blockhash"" = ""F"".""Hash"" AND ""D"".""BlockSize"" = ""F"".""Size"" "
                );

                var selectBlocksetEntries = FormatInvariant($@"SELECT ""Blockset"".""ID"" AS ""BlocksetID"", 0 AS ""Index"", ""Block"".""ID"" AS ""BlockID"" FROM ""Blockset"", ""Block"", ""{m_tempsmalllist}"" S WHERE ""Blockset"".""Fullhash"" = ""S"".""FileHash"" AND ""S"".""BlockHash"" = ""Block"".""Hash"" AND ""S"".""BlockSize"" = ""Block"".""Size"" AND ""Blockset"".""Length"" = ""S"".""BlockSize"" AND ""Blockset"".""Length"" <= {blocksize} ");

                var selectAllBlocksetEntries =
                    selectBlocklistBlocksetEntries +
                    @" UNION " +
                    selectBlocksetEntries;

                var selectFiltered =
                    @"SELECT DISTINCT ""H"".""BlocksetID"", ""H"".""Index"", ""H"".""BlockID"" FROM (" +
                    selectAllBlocksetEntries +
                    @") H WHERE (""H"".""BlocksetID"" || ':' || ""H"".""Index"") NOT IN (SELECT (""ExistingBlocksetEntries"".""BlocksetID"" || ':' || ""ExistingBlocksetEntries"".""Index"") FROM ""BlocksetEntry"" ""ExistingBlocksetEntries"" )";

                var insertBlocksetEntriesCommand =
                    @"INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") " + selectFiltered;

                try
                {
                    cmd.ExecuteNonQuery(insertBlocksetEntriesCommand);
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "BlocksetInsertFailed", ex, "Blockset insert failed, committing temporary tables for trace purposes");

                    using (var fixcmd = m_connection.CreateCommand())
                    {
                        fixcmd.ExecuteNonQuery(FormatInvariant($@"CREATE TABLE ""{m_tempblocklist}-Failure"" AS SELECT * FROM ""{m_tempblocklist}"" "));
                        fixcmd.ExecuteNonQuery(FormatInvariant($@"CREATE TABLE ""{m_tempsmalllist}-Failure"" AS SELECT * FROM ""{m_tempsmalllist}"" "));
                    }

                    throw new Exception("The recreate failed, please create a bug-report from this database and send it to the developers for further analysis");
                }
            }
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
        public void AddBlockAndBlockSetEntryFromTemp(long hashsize, long blocksize, IDbTransaction transaction, bool hashOnly = false)
        {

            using (var cmd = m_connection.CreateCommand(transaction))
            {
                var insertBlocksCommand = FormatInvariant($@"INSERT INTO BLOCK (Hash, Size, VolumeID)
SELECT DISTINCT BlockHash AS Hash, BlockSize AS Size, -1 AS VolumeID FROM 
(
SELECT NB.BlockHash, 
MIN({blocksize}, BS.Length - ((NB.""Index"" + (BH.""Index"" * {blocksize / hashsize})) * {blocksize})) AS BlockSize 
FROM (
     SELECT TBL.BlockListHash, TBL.BlockHash, TBL.""Index"" FROM {m_tempblocklist} TBL 
        LEFT OUTER JOIN Block B ON (B.Hash = TBL.BlockHash) 
        WHERE B.Hash IS NULL 
     ) NB 
JOIN BlocklistHash BH ON (BH.Hash = NB.BlocklistHash) 
JOIN Blockset BS ON (BS.ID = BH.Blocksetid) ");
                if (!hashOnly)
                {
                    insertBlocksCommand += FormatInvariant($@" UNION
SELECT TS.BlockHash, TS.BlockSize FROM 
{m_tempsmalllist} TS 
WHERE NOT EXISTS (SELECT ""X"" FROM Block AS B WHERE 
  B.Hash =  TS.BlockHash AND 
  B.Size = TS.BlockSize) 
)");
                }

                var insertBlocksetEntriesCommand = FormatInvariant($@"INSERT INTO BlocksetEntry (BlocksetID, ""Index"", BlockID)
SELECT DISTINCT BH.blocksetid, (BH.""Index"" * {blocksize / hashsize})+TBL.""Index"" as FullIndex, BK.ID AS BlockID 
FROM {m_tempblocklist} TBL 
   JOIN blocklisthash BH ON (BH.hash = TBL.blocklisthash) 
   JOIN block BK ON (BK.Hash = TBL.BlockHash) 
   LEFT OUTER JOIN BlocksetEntry BE ON (BE.BlockSetID = BH.BlocksetID AND BE.""Index"" = (BH.""Index"" * {blocksize / hashsize})+TBL.""Index"") 
WHERE 
BE.BlockSetID IS NULL ");
                if (!hashOnly)
                {
                    insertBlocksetEntriesCommand += FormatInvariant($@" UNION 
SELECT BS.ID AS BlocksetID, 0 AS ""Index"", BL.ID AS BlockID 
FROM {m_tempsmalllist} TS 
   JOIN Blockset BS ON (BS.FullHash = TS.FileHash AND 
                        BS.Length = TS.BlockSize AND 
                        BS.Length <= {blocksize}) 
   JOIN Block BL ON (BL.Hash = TS.BlockHash AND 
                     BL.Size = TS.BlockSize) 
   LEFT OUTER JOIN BlocksetEntry BE ON (BE.BlocksetID = BS.ID AND BE.""Index"" = 0) 
WHERE 
BE.BlocksetID IS NULL ");
                }

                try
                {
                    // Insert discovered new blocks into block table with volumeid = -1
                    cmd.ExecuteNonQuery(insertBlocksCommand);
                    // Insert corresponding entries into blockset
                    cmd.ExecuteNonQuery(insertBlocksetEntriesCommand);
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "BlockOrBlocksetInsertFailed", ex, "Block or Blockset insert failed, committing temporary tables for trace purposes");

                    using (var fixcmd = m_connection.CreateCommand())
                    {
                        fixcmd.ExecuteNonQuery(FormatInvariant($@"CREATE TABLE ""{m_tempblocklist}_Failure"" AS SELECT * FROM ""{m_tempblocklist}"" "));
                        fixcmd.ExecuteNonQuery(FormatInvariant($@"CREATE TABLE ""{m_tempsmalllist}_Failure"" AS SELECT * FROM ""{m_tempsmalllist}"" "));
                    }

                    throw new Exception("The recreate failed, please create a bug-report from this database and send it to the developers for further analysis");
                }
            }
        }

        public void AddDirectoryEntry(long filesetid, long pathprefixid, string path, DateTime time, long metadataid, IDbTransaction transaction)
        {
            AddEntry(filesetid, pathprefixid, path, time, FOLDER_BLOCKSET_ID, metadataid, transaction);
        }

        public void AddSymlinkEntry(long filesetid, long pathprefixid, string path, DateTime time, long metadataid, IDbTransaction transaction)
        {
            AddEntry(filesetid, pathprefixid, path, time, SYMLINK_BLOCKSET_ID, metadataid, transaction);
        }

        public void AddFileEntry(long filesetid, long pathprefixid, string path, DateTime time, long blocksetid, long metadataid, IDbTransaction transaction)
        {
            AddEntry(filesetid, pathprefixid, path, time, blocksetid, metadataid, transaction);
        }

        private void AddEntry(long filesetid, long pathprefixid, string path, DateTime time, long blocksetid, long metadataid, IDbTransaction transaction)
        {
            m_findFilesetCommand.Transaction = transaction;
            m_findFilesetCommand.SetParameterValue("@PrefixId", pathprefixid);
            m_findFilesetCommand.SetParameterValue("@Path", path);
            m_findFilesetCommand.SetParameterValue("@BlocksetId", blocksetid);
            m_findFilesetCommand.SetParameterValue("@MetadataId", metadataid);
            var fileid = m_findFilesetCommand.ExecuteScalarInt64(-1);

            if (fileid < 0)
            {
                m_insertFileCommand.Transaction = transaction;
                m_insertFileCommand.SetParameterValue("@PrefixId", pathprefixid);
                m_insertFileCommand.SetParameterValue("@Path", path);
                m_insertFileCommand.SetParameterValue("@BlocksetId", blocksetid);
                m_insertFileCommand.SetParameterValue("@MetadataId", metadataid);
                fileid = m_insertFileCommand.ExecuteScalarInt64(-1);
            }

            m_insertFilesetEntryCommand.Transaction = transaction;
            m_insertFilesetEntryCommand.SetParameterValue("@FilesetId", filesetid);
            m_insertFilesetEntryCommand.SetParameterValue("@FileId", fileid);
            m_insertFilesetEntryCommand.SetParameterValue("@LastModified", time.ToUniversalTime().Ticks);
            m_insertFilesetEntryCommand.ExecuteNonQuery();
        }

        public long AddMetadataset(string metahash, long metahashsize, IEnumerable<string> metablocklisthashes, long expectedmetablocklisthashes, IDbTransaction transaction)
        {
            var metadataid = -1L;
            if (metahash == null)
                return metadataid;

            m_findMetadatasetCommand.Transaction = transaction;
            m_findMetadatasetCommand.SetParameterValue("@FullHash", metahash);
            m_findMetadatasetCommand.SetParameterValue("@Length", metahashsize);
            metadataid = m_findMetadatasetCommand.ExecuteScalarInt64(-1);
            if (metadataid != -1)
                return metadataid;

            var blocksetid = AddBlockset(metahash, metahashsize, metablocklisthashes, expectedmetablocklisthashes, transaction);

            m_insertMetadatasetCommand.Transaction = transaction;
            m_insertMetadatasetCommand.SetParameterValue("@BlocksetId", blocksetid);
            metadataid = m_insertMetadatasetCommand.ExecuteScalarInt64(-1);

            return metadataid;
        }

        public long AddBlockset(string fullhash, long size, IEnumerable<string> blocklisthashes, long expectedblocklisthashes, IDbTransaction transaction)
        {
            m_findBlocksetCommand.Transaction = transaction;
            m_findBlocksetCommand.SetParameterValue("@Length", size);
            m_findBlocksetCommand.SetParameterValue("@FullHash", fullhash);
            var blocksetid = m_findBlocksetCommand.ExecuteScalarInt64(-1);
            if (blocksetid != -1)
                return blocksetid;

            m_insertBlocksetCommand.Transaction = transaction;
            m_insertBlocksetCommand.SetParameterValue("@Length", size);
            m_insertBlocksetCommand.SetParameterValue("@FullHash", fullhash);
            blocksetid = m_insertBlocksetCommand.ExecuteScalarInt64(-1);

            long c = 0;
            if (blocklisthashes != null)
            {
                var index = 0L;
                m_insertBlocklistHashCommand.Transaction = transaction;
                m_insertBlocklistHashCommand.SetParameterValue("@BlocksetId", blocksetid);

                foreach (var hash in blocklisthashes)
                {
                    if (!string.IsNullOrEmpty(hash))
                    {
                        c++;
                        if (c <= expectedblocklisthashes)
                        {
                            m_insertBlocklistHashCommand.SetParameterValue("@Index", index++);
                            m_insertBlocklistHashCommand.SetParameterValue("@Hash", hash);
                            m_insertBlocklistHashCommand.ExecuteNonQuery();
                        }
                    }
                }
            }

            if (c != expectedblocklisthashes)
                Logging.Log.WriteWarningMessage(LOGTAG, "MismatchInBlocklistHashCount", null, "Mismatching number of blocklist hashes detected on blockset {2}. Expected {0} blocklist hashes, but found {1}", expectedblocklisthashes, c, blocksetid);

            return blocksetid;
        }

        public bool UpdateBlock(string hash, long size, long volumeID, IDbTransaction transaction, ref bool anyChange)
        {
            m_findHashBlockCommand.Transaction = transaction;
            m_findHashBlockCommand.SetParameterValue("@Hash", hash);
            m_findHashBlockCommand.SetParameterValue("@Size", size);
            var currentVolumeId = m_findHashBlockCommand.ExecuteScalarInt64(-2);

            if (currentVolumeId == volumeID)
                return false;

            anyChange = true;

            if (currentVolumeId == -2)
            {
                //Insert
                m_insertBlockCommand.Transaction = transaction;
                m_insertBlockCommand.SetParameterValue("@Hash", hash);
                m_insertBlockCommand.SetParameterValue("@Size", size);
                m_insertBlockCommand.SetParameterValue("@VolumeId", volumeID);
                m_insertBlockCommand.ExecuteNonQuery();

                return true;
            }
            else if (currentVolumeId == -1)
            {
                //Update
                m_updateBlockVolumeCommand.Transaction = transaction;
                m_updateBlockVolumeCommand.SetParameterValue("@VolumeId", volumeID);
                m_updateBlockVolumeCommand.SetParameterValue("@Hash", hash);
                m_updateBlockVolumeCommand.SetParameterValue("@Size", size);
                var c = m_updateBlockVolumeCommand.ExecuteNonQuery();
                if (c != 1)
                    throw new Exception($"Failed to update table, found {c} entries for key {hash} with size {size}");

                return true;
            }
            else
            {
                m_insertDuplicateBlockCommand.SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Size", size)
                    .SetParameterValue("@VolumeId", volumeID)
                    .ExecuteNonQuery(transaction);

                return false;
            }
        }

        public void AddSmallBlocksetLink(string filehash, string blockhash, long blocksize, IDbTransaction transaction)
        {
            m_insertSmallBlockset.SetParameterValue("@FileHash", filehash)
                .SetParameterValue("@BlockHash", blockhash)
                .SetParameterValue("@BlockSize", blocksize)
                .ExecuteNonQuery(transaction);
        }

        public bool AddTempBlockListHash(string hash, IEnumerable<string> blocklisthashes, IDbTransaction transaction)
        {
            m_findTempBlockListHashCommand.Transaction = transaction;
            m_findTempBlockListHashCommand.SetParameterValue("@BlocklistHash", hash);
            var r = m_findTempBlockListHashCommand.ExecuteScalar();
            if (r != null && r != DBNull.Value)
                return false;

            m_insertTempBlockListHash.Transaction = transaction;
            m_insertTempBlockListHash.SetParameterValue("@BlocklistHash", hash);

            var index = 0L;

            foreach (var s in blocklisthashes)
            {
                m_insertTempBlockListHash.SetParameterValue("@BlockHash", s);
                m_insertTempBlockListHash.SetParameterValue("@Index", index++);
                m_insertTempBlockListHash.ExecuteNonQuery();
            }

            return true;
        }


        public IEnumerable<string> GetBlockLists(long volumeid)
        {
            using (var cmd = m_connection.CreateCommand(@"SELECT DISTINCT ""BlocklistHash"".""Hash"" FROM ""BlocklistHash"", ""Block"" WHERE ""Block"".""Hash"" = ""BlocklistHash"".""Hash"" AND ""Block"".""VolumeID"" = @VolumeId"))
            {
                cmd.SetParameterValue("@VolumeId", volumeid);
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        yield return rd.ConvertValueToString(0) ?? "";
            }
        }

        public IEnumerable<IRemoteVolume> GetMissingBlockListVolumes(int passNo, long blocksize, long hashsize, bool forceBlockUse)
        {
            using (var cmd = m_connection.CreateCommand())
            {
                var selectCommand = @"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"", ""RemoteVolume"".""ID"" FROM ""RemoteVolume""";

                var missingBlocklistEntries =
                    FormatInvariant($@"SELECT ""BlocklistHash"".""Hash"" FROM ""BlocklistHash"" LEFT OUTER JOIN ""BlocksetEntry"" ON ""BlocksetEntry"".""Index"" = (""BlocklistHash"".""Index"" * {blocksize / hashsize}) AND ""BlocksetEntry"".""BlocksetID"" = ""BlocklistHash"".""BlocksetID"" WHERE ""BlocksetEntry"".""BlocksetID"" IS NULL");

                var missingBlockInfo =
                    @"SELECT ""VolumeID"" FROM ""Block"" WHERE ""VolumeID"" < 0 AND SIZE > 0";

                var missingBlocklistVolumes = FormatInvariant(
                    $@"SELECT ""VolumeID"" FROM ""Block"", ({missingBlocklistEntries}) A WHERE ""A"".""Hash"" = ""Block"".""Hash"" "
                );

                var countMissingInformation = FormatInvariant($@"SELECT COUNT(*) FROM (SELECT DISTINCT ""VolumeID"" FROM ({missingBlockInfo} UNION {missingBlocklistVolumes}))");

                if (passNo == 0)
                {
                    // On the first pass, we select all the volumes we know we need,
                    // which may be an empty list
                    cmd.SetCommandAndParameters(selectCommand + FormatInvariant($@" WHERE ""ID"" IN ({missingBlocklistVolumes})"));

                    // Reset the list
                    m_proccessedVolumes.Clear();
                }
                else
                {
                    //On anything but the first pass, we check if we are done
                    var r = cmd.ExecuteScalarInt64(countMissingInformation, 0);
                    if (r == 0 && !forceBlockUse)
                        yield break;

                    if (passNo == 1)
                    {
                        // On the second pass, we select all volumes that are not mentioned in the db

                        var mentionedVolumes =
                            @"SELECT DISTINCT ""VolumeID"" FROM ""Block"" ";

                        cmd.SetCommandAndParameters(selectCommand + FormatInvariant($@" WHERE ""ID"" NOT IN ({mentionedVolumes}) AND ""Type"" = @Type "))
                            .SetParameterValue("@Type", RemoteVolumeType.Blocks.ToString());

                    }
                    else
                    {
                        // On the final pass, we select all volumes
                        // the filter will ensure that we do not download anything twice
                        cmd.SetCommandAndParameters(selectCommand + @" WHERE ""Type"" = @Type ")
                            .SetParameterValue("@Type", RemoteVolumeType.Blocks.ToString());
                    }
                }

                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        var volumeID = rd.ConvertValueToInt64(3);

                        // Guard against multiple downloads of the same file
                        if (!m_proccessedVolumes.ContainsKey(volumeID))
                        {
                            m_proccessedVolumes.Add(volumeID, volumeID);

                            yield return new RemoteVolume(
                                rd.ConvertValueToString(0),
                                rd.ConvertValueToString(1),
                                rd.ConvertValueToInt64(2, -1)
                            );
                        }
                    }
                }
            }
        }

        public void CleanupMissingVolumes()
        {
            var tablename = "SwapBlocks-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

            // TODO: either hardcode all string constants or none

            // The first part of this query swaps out blocks for non-present remote files with
            // existing ones (as recorded in the DuplicateBlock table)
            // The second part removes references to the non-present remote files,
            // and marks the index files that pointed to them, such that they will be removed later on
            var sql = FormatInvariant($@"
CREATE TEMPORARY TABLE ""{tablename}"" AS
SELECT 
  ""A"".""ID"" AS ""BlockID"", ""A"".""VolumeID"" AS ""SourceVolumeID"", ""A"".""State"" AS ""SourceVolumeState"", ""B"".""VolumeID"" AS ""TargetVolumeID"", ""B"".""State"" AS ""TargetVolumeState"" 
FROM 
  (SELECT 
    ""Block"".""ID"", ""Block"".""VolumeID"", ""Remotevolume"".""State"" 
    FROM ""Block"", ""Remotevolume"" 
    WHERE ""Block"".""VolumeID"" = ""Remotevolume"".""ID"" AND ""Remotevolume"".""State"" = '{RemoteVolumeState.Temporary}'
  ) A, 
  (SELECT 
    ""DuplicateBlock"".""BlockID"",  MIN(""DuplicateBlock"".""VolumeID"") AS ""VolumeID"", ""Remotevolume"".""State"" 
    FROM ""DuplicateBlock"", ""Remotevolume"" 
    WHERE ""DuplicateBlock"".""VolumeID"" = ""Remotevolume"".""ID"" AND ""Remotevolume"".""State"" = '{RemoteVolumeState.Verified}' 
    GROUP BY ""DuplicateBlock"".""BlockID"", ""Remotevolume"".""State"" 
  ) B 
WHERE ""A"".""ID"" = ""B"".""BlockID"";

UPDATE ""Block"" 
  SET ""VolumeID"" = (SELECT 
    ""TargetVolumeID"" FROM ""{tablename}"" 
    WHERE ""Block"".""ID"" = ""{tablename}"".""BlockID""
  ) 
  WHERE ""Block"".""ID"" IN (SELECT ""BlockID"" FROM ""{tablename}"");

UPDATE ""DuplicateBlock"" 
  SET ""VolumeID"" = (SELECT 
    ""SourceVolumeID"" FROM ""{tablename}"" 
    WHERE ""DuplicateBlock"".""BlockID"" = ""{tablename}"".""BlockID""
  ) 
WHERE 
  (""DuplicateBlock"".""BlockID"", ""DuplicateBlock"".""VolumeID"") 
  IN (SELECT ""BlockID"", ""TargetVolumeID"" FROM ""{tablename}"");

DROP TABLE ""{tablename}"";

DELETE FROM ""IndexBlockLink"" WHERE ""BlockVolumeID"" IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Type"" = '{RemoteVolumeType.Blocks}' AND ""State"" = '{RemoteVolumeState.Temporary}' AND ""ID"" NOT IN (SELECT DISTINCT ""VolumeID"" FROM ""Block""));
DELETE FROM ""DuplicateBlock"" WHERE ""VolumeID"" IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Type"" = '{RemoteVolumeType.Blocks}' AND ""State"" = '{RemoteVolumeState.Temporary}' AND ""ID"" NOT IN (SELECT DISTINCT ""VolumeID"" FROM ""Block""));
DELETE FROM ""RemoteVolume"" WHERE ""Type"" = '{RemoteVolumeType.Blocks}' AND ""State"" = '{RemoteVolumeState.Temporary}' AND ""ID"" NOT IN (SELECT DISTINCT ""VolumeID"" FROM ""Block"");
");

            // We could delete these, but we don't have to, so we keep them around until the next compact is done
            // UPDATE ""RemoteVolume"" SET ""State"" = ""{3}"" WHERE ""Type"" = ""{5}"" AND ""ID"" NOT IN (SELECT ""IndexVolumeID"" FROM ""IndexBlockLink"");

            var countsql = FormatInvariant($@"SELECT COUNT(*) FROM ""RemoteVolume"" WHERE ""State"" = '{RemoteVolumeState.Temporary}' AND ""Type"" = '{RemoteVolumeType.Blocks}' ");

            using (var cmd = m_connection.CreateCommand())
            {
                var cnt = cmd.ExecuteScalarInt64(countsql);
                if (cnt > 0)
                {
                    try
                    {
                        cmd.ExecuteNonQuery(sql);
                        var cnt2 = cmd.ExecuteScalarInt64(countsql);
                        Logging.Log.WriteWarningMessage(LOGTAG, "MissingVolumesDetected", null, "Replaced blocks for {0} missing volumes; there are now {1} missing volumes", cnt, cnt2);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "MissingVolumesDetected", ex, "Found {0} missing volumes; failed while attempting to replace blocks from existing volumes", cnt);
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Move blocks that are not referenced by any files to DeletedBlock table.
        /// </summary>
        /// Needs to be called after the last FindMissingBlocklistHashes, otherwise the tables are not up to date.
        public void CleanupDeletedBlocks(IDbTransaction transaction)
        {
            // Find out which blocks are deleted and move them into DeletedBlock, so that compact notices these blocks are empty
            // Deleted blocks do not appear in the BlocksetEntry and not in the BlocklistHash table

            var tmptablename = "DeletedBlocks-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            using (var cmd = m_connection.CreateCommand(tr.Parent))
            {
                // 1. Select blocks not used by any file and not as a blocklist into temporary table
                cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{tmptablename}""
                    AS SELECT ""Block"".""ID"", ""Block"".""Hash"", ""Block"".""Size"", ""Block"".""VolumeID"" FROM ""Block""
                        WHERE ""Block"".""ID"" NOT IN (SELECT ""BlocksetEntry"".""BlockID"" FROM ""BlocksetEntry"")
                        AND ""Block"".""Hash"" NOT IN (SELECT ""BlocklistHash"".""Hash"" FROM ""BlocklistHash"")"));
                // 2. Insert blocks into DeletedBlock table
                cmd.ExecuteNonQuery(FormatInvariant($@"INSERT INTO ""DeletedBlock"" (""Hash"", ""Size"", ""VolumeID"") SELECT ""Hash"", ""Size"", ""VolumeID"" FROM ""{tmptablename}"""));
                // 3. Remove blocks from Block table
                cmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""Block"" WHERE ""ID"" IN (SELECT ""ID"" FROM ""{tmptablename}"")"));
                cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{tmptablename}"""));
                tr.Commit();
            }


        }

        public override void Dispose()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                if (m_tempblocklist != null)
                    try
                    {
                        cmd.ExecuteNonQuery(FormatInvariant(@$"DROP TABLE IF EXISTS ""{m_tempblocklist}"""));
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
                        cmd.ExecuteNonQuery(FormatInvariant(@$"DROP TABLE IF EXISTS ""{m_tempsmalllist}"""));
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

            base.Dispose();
        }
    }
}
