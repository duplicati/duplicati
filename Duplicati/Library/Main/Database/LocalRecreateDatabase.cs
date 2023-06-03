using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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
        
        private readonly System.Data.IDbCommand m_insertFileCommand;
        private readonly System.Data.IDbCommand m_insertFilesetEntryCommand;
        private readonly System.Data.IDbCommand m_insertMetadatasetCommand;
        private readonly System.Data.IDbCommand m_insertBlocksetCommand;
        private readonly System.Data.IDbCommand m_insertBlocklistHashCommand;
        private readonly System.Data.IDbCommand m_updateBlockVolumeCommand;
        private readonly System.Data.IDbCommand m_insertBlockset;
        private readonly System.Data.IDbCommand m_insertSmallBlockset;
        private readonly System.Data.IDbCommand m_findBlocksetCommand;
        private readonly System.Data.IDbCommand m_findMetadatasetCommand;
        private readonly System.Data.IDbCommand m_findFilesetCommand;
        private readonly System.Data.IDbCommand m_findblocklisthashCommand;
        private readonly System.Data.IDbCommand m_findHashBlockCommand;
        private readonly System.Data.IDbCommand m_insertBlockCommand;
        private readonly System.Data.IDbCommand m_insertDuplicateBlockCommand;

        private string m_tempblocklist;
        private string m_tempsmalllist;

        /// <summary>
        /// A lookup table that prevents multiple downloads of the same volume
        /// </summary>
        private Dictionary<long, long> m_proccessedVolumes;
        
        // SQL that finds index and block size for all blocklist hashes, based on the temporary hash list
        // with vars Used:
        // {0} --> Blocksize
        // {1} --> BlockHash-Size
        // {2} --> Temp-Table
        // {3} --> FullBlocklist-BlockCount [equals ({0} / {1}), if SQLite pays respect to ints]
        private const string SELECT_BLOCKLIST_ENTRIES =
            @" 
        SELECT
            ""E"".""BlocksetID"",
            ""F"".""Index"" + (""E"".""BlocklistIndex"" * {3}) AS ""FullIndex"",
            ""F"".""BlockHash"",
            MIN({0}, ""E"".""Length"" - ((""F"".""Index"" + (""E"".""BlocklistIndex"" * {3})) * {0})) AS ""BlockSize"",
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
                            MIN({3} * {1}, (((""B"".""Length"" + {0} - 1) / {0}) - (""A"".""Index"" * ({3}))) * {1}) AS ""BlocklistSize"",
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
            ""{2}"" F
        WHERE
           ""F"".""BlocklistHash"" = ""E"".""Hash""
        ORDER BY 
           ""E"".""BlocksetID"",
           ""FullIndex""
";

        public LocalRecreateDatabase(LocalDatabase parentdb, Options options)
            : base(parentdb)
        {
            m_tempblocklist = "TempBlocklist_" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
            m_tempsmalllist = "TempSmalllist_" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

            using(var cmd = m_connection.CreateCommand())
            {
                cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""BlockListHash"" TEXT NOT NULL, ""BlockHash"" TEXT NOT NULL, ""Index"" INTEGER NOT NULL)", m_tempblocklist));
                cmd.ExecuteNonQuery(string.Format(@"CREATE INDEX ""Index_{0}"" ON ""{0}"" (""BlockListHash"");", m_tempblocklist));

                cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""FileHash"" TEXT NOT NULL, ""BlockHash"" TEXT NOT NULL, ""BlockSize"" INTEGER NOT NULL)", m_tempsmalllist));
                cmd.ExecuteNonQuery(string.Format(@"CREATE UNIQUE INDEX ""Index_File_{0}"" ON ""{0}"" (""FileHash"", ""BlockSize"");", m_tempsmalllist));
                cmd.ExecuteNonQuery(string.Format(@"CREATE UNIQUE INDEX ""Index_Block_{0}"" ON ""{0}"" (""BlockHash"", ""BlockSize"");", m_tempsmalllist));
            }

            m_insertFileCommand = m_connection.CreateCommand();
            m_insertFilesetEntryCommand = m_connection.CreateCommand();
            m_insertMetadatasetCommand = m_connection.CreateCommand();
            m_insertBlocksetCommand = m_connection.CreateCommand();
            m_insertBlocklistHashCommand = m_connection.CreateCommand();
            m_updateBlockVolumeCommand = m_connection.CreateCommand();
            m_insertBlockset = m_connection.CreateCommand();
            m_insertSmallBlockset = m_connection.CreateCommand();
            m_findBlocksetCommand = m_connection.CreateCommand();
            m_findMetadatasetCommand = m_connection.CreateCommand();
            m_findFilesetCommand = m_connection.CreateCommand();
            m_findblocklisthashCommand = m_connection.CreateCommand();
            m_findHashBlockCommand = m_connection.CreateCommand();
            m_insertBlockCommand = m_connection.CreateCommand();
            m_insertDuplicateBlockCommand = m_connection.CreateCommand();

            m_insertFileCommand.CommandText = @"INSERT INTO ""FileLookup"" (""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"") VALUES (?,?,?,?); SELECT last_insert_rowid();";
            m_insertFileCommand.AddParameters(4);
            
            m_insertFilesetEntryCommand.CommandText = @"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") VALUES (?,?,?)";
            m_insertFilesetEntryCommand.AddParameters(3);

            m_insertMetadatasetCommand.CommandText = @"INSERT INTO ""Metadataset"" (""BlocksetID"") VALUES (?); SELECT last_insert_rowid();";
            m_insertMetadatasetCommand.AddParameters(1);
            
            m_insertBlocksetCommand.CommandText = @"INSERT INTO ""Blockset"" (""Length"", ""FullHash"") VALUES (?,?); SELECT last_insert_rowid();";
            m_insertBlocksetCommand.AddParameters(2);
                            
            m_insertBlocklistHashCommand.CommandText = @"INSERT INTO ""BlocklistHash"" (""BlocksetID"", ""Index"", ""Hash"") VALUES (?,?,?)";
            m_insertBlocklistHashCommand.AddParameters(3);
            
            m_updateBlockVolumeCommand.CommandText = @"UPDATE ""Block"" SET ""VolumeID"" = ? WHERE ""Hash"" = ? AND ""Size"" = ?";
            m_updateBlockVolumeCommand.AddParameters(3);

            m_insertBlockset.CommandText = string.Format(@"INSERT INTO ""{0}"" (""BlocklistHash"", ""BlockHash"", ""Index"") VALUES (?,?,?) ", m_tempblocklist);
            m_insertBlockset.AddParameters(3);

            m_insertSmallBlockset.CommandText = string.Format(@"INSERT OR IGNORE INTO ""{0}"" (""FileHash"", ""BlockHash"", ""BlockSize"") VALUES (?,?,?) ", m_tempsmalllist);
            m_insertSmallBlockset.AddParameters(3);

            m_findBlocksetCommand.CommandText = @"SELECT ""ID"" FROM ""Blockset"" WHERE ""Length"" = ? AND ""FullHash"" = ? ";
            m_findBlocksetCommand.AddParameters(2);
            
            m_findMetadatasetCommand.CommandText = @"SELECT ""Metadataset"".""ID"" FROM ""Metadataset"",""Blockset"" WHERE ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID"" AND ""Blockset"".""FullHash"" = ? AND ""Blockset"".""Length"" = ? ";
            m_findMetadatasetCommand.AddParameters(2);
            
            m_findFilesetCommand.CommandText = @"SELECT ""ID"" FROM ""FileLookup"" WHERE ""PrefixID"" = ? AND ""Path"" = ? AND ""BlocksetID"" = ? AND ""MetadataID"" = ? ";
            m_findFilesetCommand.AddParameters(4);
            
            m_findblocklisthashCommand.CommandText = string.Format(@"SELECT DISTINCT ""BlockListHash"" FROM ""{0}"" WHERE ""BlockListHash"" = ? ", m_tempblocklist);
            m_findblocklisthashCommand.AddParameters(1);
            
            m_findHashBlockCommand.CommandText = @"SELECT ""VolumeID"" FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ? ";
            m_findHashBlockCommand.AddParameters(2);
                        
            m_insertBlockCommand.CommandText = @"INSERT INTO ""Block"" (""Hash"", ""Size"", ""VolumeID"") VALUES (?,?,?)";
            m_insertBlockCommand.AddParameters(3);
            
            m_insertDuplicateBlockCommand.CommandText = @"INSERT INTO ""DuplicateBlock"" (""BlockID"", ""VolumeID"") VALUES ((SELECT ""ID"" FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ?), ?)";
            m_insertDuplicateBlockCommand.AddParameters(3);
        }

        public void FindMissingBlocklistHashes(long hashsize, long blocksize, System.Data.IDbTransaction transaction)
        {
            using(var cmd = m_connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                
                //Update all small blocklists and matching blocks

                var selectSmallBlocks = string.Format(@"SELECT ""BlockHash"", ""BlockSize"" FROM ""{0}""", m_tempsmalllist);
            
                var selectBlockHashes = string.Format(
                    @"SELECT ""BlockHash"" AS ""FullHash"", ""BlockSize"" AS ""Length"" FROM ( " +
                    SELECT_BLOCKLIST_ENTRIES +
                    @" )",
                    blocksize,
                    hashsize,
                    m_tempblocklist,
                    blocksize / hashsize
                );
                                
                var selectAllBlocks = @"SELECT DISTINCT ""FullHash"", ""Length"" FROM (" + selectBlockHashes + " UNION " + selectSmallBlocks + " )";
                
                var selectNewBlocks = string.Format(
                    @"SELECT ""FullHash"" AS ""Hash"", ""Length"" AS ""Size"", -1 AS ""VolumeID"" " +
                    @" FROM (SELECT ""A"".""FullHash"", ""A"".""Length"", CASE WHEN ""B"".""Hash"" IS NULL THEN '' ELSE ""B"".""Hash"" END AS ""Hash"", CASE WHEN ""B"".""Size"" is NULL THEN -1 ELSE ""B"".""Size"" END AS ""Size"" FROM ({0}) A" + 
                    @" LEFT OUTER JOIN ""Block"" B ON ""B"".""Hash"" =  ""A"".""FullHash"" AND ""B"".""Size"" = ""A"".""Length"" )" + 
                    @" WHERE ""FullHash"" != ""Hash"" AND ""Length"" != ""Size"" ",
                    selectAllBlocks    
                );
                
                var insertBlocksCommand = 
                    @"INSERT INTO ""Block"" (""Hash"", ""Size"", ""VolumeID"") " + 
                    selectNewBlocks;
                    
                // Insert all known blocks into block table with volumeid = -1
                cmd.ExecuteNonQuery(insertBlocksCommand);
                    
                var selectBlocklistBlocksetEntries = string.Format(
                    @"SELECT ""E"".""BlocksetID"" AS ""BlocksetID"", ""D"".""FullIndex"" AS ""Index"", ""F"".""ID"" AS ""BlockID"" FROM ( " +
                    SELECT_BLOCKLIST_ENTRIES +
                    @") D, ""BlocklistHash"" E, ""Block"" F, ""Block"" G WHERE ""D"".""BlocksetID"" = ""E"".""BlocksetID"" AND ""D"".""BlocklistHash"" = ""E"".""Hash"" AND ""D"".""BlocklistSize"" = ""G"".""Size"" AND ""D"".""BlocklistHash"" = ""G"".""Hash"" AND ""D"".""Blockhash"" = ""F"".""Hash"" AND ""D"".""BlockSize"" = ""F"".""Size"" ",

                    // TODO: The BlocklistHash join seems to be unnecessary, but the join might be required to work around a from really old versions of Duplicati
                    // this could be used instead
                    //@") D, ""Block"" WHERE  ""BlockQuery"".""BlockHash"" = ""Block"".""Hash"" AND ""BlockQuery"".""BlockSize"" = ""Block"".""Size"" ";

                    blocksize,
                    hashsize,
                    m_tempblocklist,
                    blocksize / hashsize
                    );

                var selectBlocksetEntries = string.Format(
                    @"SELECT ""Blockset"".""ID"" AS ""BlocksetID"", 0 AS ""Index"", ""Block"".""ID"" AS ""BlockID"" FROM ""Blockset"", ""Block"", ""{1}"" S WHERE ""Blockset"".""Fullhash"" = ""S"".""FileHash"" AND ""S"".""BlockHash"" = ""Block"".""Hash"" AND ""S"".""BlockSize"" = ""Block"".""Size"" AND ""Blockset"".""Length"" = ""S"".""BlockSize"" AND ""Blockset"".""Length"" <= {0} ",
                    blocksize,
                    m_tempsmalllist
                    );
                    
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
                        fixcmd.ExecuteNonQuery(string.Format(@"CREATE TABLE ""{0}-Failure"" AS SELECT * FROM ""{0}"" ", m_tempblocklist));
                        fixcmd.ExecuteNonQuery(string.Format(@"CREATE TABLE ""{0}-Failure"" AS SELECT * FROM ""{0}"" ", m_tempsmalllist));
                    }

                    throw new Exception("The recreate failed, please create a bug-report from this database and send it to the developers for further analysis");
                }
            }
        }

	/// <summary>
        /// From the temporary tables 1) insert new blocks into Block (VolumeID to be set at a later stage)
        /// and 2) add missing BlocksetEntry lines
        /// </summary>
        /// Notes:
        ///
        /// temp block list structure: blocklist hash, block hash, index relative to the
        /// beginning of the blocklist hash (NOT the file)
        ///
        /// temp small list structure: filehash, blockhash, blocksize: as the small files are defined
        /// by the fact that they are contained in a single block, blockhash is the same as the filehash,
        /// and blocksize can vary from 0 to the configured block size for the backup
        public void AddToBlockAndBlockSetEntryFromTemp(long hashsize, long blocksize, System.Data.IDbTransaction transaction)
        {

            using(var cmd = m_connection.CreateCommand())
            {
                cmd.Transaction = transaction;

                var insertBlocksCommand = string.Format(
                    @"INSERT INTO BLOCK (Hash, Size, VolumeID) " +
                    @"SELECT DISTINCT BlockHash AS Hash, BlockSize AS Size, -1 AS VolumeID FROM " +
                    @"(" +
                    @"SELECT NB.BlockHash, " +
                    @"MIN({0}, BS.Length - ((NB.""Index"" + (BH.""Index"" * {1})) * {0})) AS BlockSize " +
                    @"FROM (" +
                    @"     SELECT TBL.BlockListHash, TBL.BlockHash, TBL.""Index"" FROM {2} TBL " +
                    @"        LEFT OUTER JOIN Block B ON (B.Hash = TBL.BlockHash) " +
                    @"        WHERE B.Hash IS NULL " +
                    @"     ) NB " +
                    @"JOIN BlocklistHash BH ON (BH.Hash = NB.BlocklistHash) " +
                    @"JOIN Blockset BS ON (BS.ID = BH.Blocksetid) " +
                    @"" +
                    @"UNION " +
                    @"" +
                    @"SELECT TS.BlockHash, TS.BlockSize FROM " +
                    @"{3} TS " +
                    @"WHERE NOT EXISTS (SELECT ""X"" FROM Block AS B WHERE " +
                    @"  B.Hash =  TS.BlockHash AND " +
                    @"  B.Size = TS.BlockSize) " +
                    @")",
                    blocksize,
                    blocksize / hashsize,
                    m_tempblocklist,
                    m_tempsmalllist
                );

                var insertBlocksetEntriesCommand = string.Format(
                    @"INSERT INTO BlocksetEntry (BlocksetID, ""Index"", BlockID) " +
                    @"SELECT DISTINCT BH.blocksetid, (BH.""Index"" * {1})+TBL.""Index"" as FullIndex, BK.ID AS BlockID " +
                    @"FROM {2} TBL " +
                    @"   JOIN blocklisthash BH ON (BH.hash = TBL.blocklisthash) " +
                    @"   JOIN block BK ON (BK.Hash = TBL.BlockHash) " +
                    @"   LEFT OUTER JOIN BlocksetEntry BE ON (BE.BlockSetID = BH.BlocksetID AND BE.""Index"" = (BH.""Index"" * {1})+TBL.""Index"") " +
                    @"WHERE " +
                    @"BE.BlockSetID IS NULL " +
                    @"UNION " +
                    @"SELECT BS.ID AS BlocksetID, 0 AS ""Index"", BL.ID AS BlockID " +
                    @"FROM {3} TS " +
                    @"   JOIN Blockset BS ON (BS.FullHash = TS.FileHash AND " +
                    @"                        BS.Length = TS.BlockSize AND " +
                    @"                        BS.Length <= {0}) " +
                    @"   JOIN Block BL ON (BL.Hash = TS.BlockHash AND " +
                    @"                     BL.Size = TS.BlockSize) " +
                    @"   LEFT OUTER JOIN BlocksetEntry BE ON (BE.BlocksetID = BS.ID AND BE.""Index"" = 0) " +
                    @"WHERE " +
                    @"BE.BlocksetID IS NULL ",
                    blocksize,
                    blocksize / hashsize,
                    m_tempblocklist,
                    m_tempsmalllist
                );


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
                        fixcmd.ExecuteNonQuery(string.Format(@"CREATE TABLE ""{0}_Failure"" AS SELECT * FROM ""{0}"" ", m_tempblocklist));
                        fixcmd.ExecuteNonQuery(string.Format(@"CREATE TABLE ""{0}_Failure"" AS SELECT * FROM ""{0}"" ", m_tempsmalllist));
                    }

                    throw new Exception("The recreate failed, please create a bug-report from this database and send it to the developers for further analysis");
                }
            }
        }

        public void AddDirectoryEntry(long filesetid, long pathprefixid, string path, DateTime time, long metadataid, System.Data.IDbTransaction transaction)
        {
            AddEntry(filesetid, pathprefixid, path, time, FOLDER_BLOCKSET_ID, metadataid, transaction);
        }

        public void AddSymlinkEntry(long filesetid, long pathprefixid, string path, DateTime time, long metadataid, System.Data.IDbTransaction transaction)
        {
            AddEntry(filesetid, pathprefixid, path, time, SYMLINK_BLOCKSET_ID, metadataid, transaction);
        }
        
        public void AddFileEntry(long filesetid, long pathprefixid, string path, DateTime time, long blocksetid, long metadataid, System.Data.IDbTransaction transaction)
        {
            AddEntry(filesetid, pathprefixid, path, time, blocksetid, metadataid, transaction);
        }

        private void AddEntry(long filesetid, long pathprefixid, string path, DateTime time, long blocksetid, long metadataid, System.Data.IDbTransaction transaction)
        {
            var fileid = -1L;
                        
            m_findFilesetCommand.Transaction = transaction;
            m_findFilesetCommand.SetParameterValue(0, pathprefixid);
            m_findFilesetCommand.SetParameterValue(1, path);
            m_findFilesetCommand.SetParameterValue(2, blocksetid);
            m_findFilesetCommand.SetParameterValue(3, metadataid);
            fileid = m_findFilesetCommand.ExecuteScalarInt64(-1);

            if (fileid < 0)
            {
                m_insertFileCommand.Transaction = transaction;
                m_insertFileCommand.SetParameterValue(0, pathprefixid);
                m_insertFileCommand.SetParameterValue(1, path);
                m_insertFileCommand.SetParameterValue(2, blocksetid);
                m_insertFileCommand.SetParameterValue(3, metadataid);
                fileid = m_insertFileCommand.ExecuteScalarInt64(-1);
            }
            
            m_insertFilesetEntryCommand.Transaction = transaction;
            m_insertFilesetEntryCommand.SetParameterValue(0, filesetid);
            m_insertFilesetEntryCommand.SetParameterValue(1, fileid);
            m_insertFilesetEntryCommand.SetParameterValue(2, time.ToUniversalTime().Ticks);
            m_insertFilesetEntryCommand.ExecuteNonQuery();
        }
        
        public long AddMetadataset(string metahash, long metahashsize, IEnumerable<string> metablocklisthashes, long expectedmetablocklisthashes, System.Data.IDbTransaction transaction)
        {
            var metadataid = -1L;
            if (metahash == null)
                return metadataid;
                                
            m_findMetadatasetCommand.Transaction = transaction;
            m_findMetadatasetCommand.SetParameterValue(0, metahash);
            m_findMetadatasetCommand.SetParameterValue(1, metahashsize);
            metadataid = m_findMetadatasetCommand.ExecuteScalarInt64(-1);
            if (metadataid != -1)
                return metadataid;

            var blocksetid = AddBlockset(metahash, metahashsize, metablocklisthashes, expectedmetablocklisthashes, transaction);
            
            m_insertMetadatasetCommand.Transaction = transaction;
            m_insertMetadatasetCommand.SetParameterValue(0, blocksetid);
            metadataid = m_insertMetadatasetCommand.ExecuteScalarInt64(-1);
                            
            return metadataid;
        }
        
        public long AddBlockset(string fullhash, long size, IEnumerable<string> blocklisthashes, long expectedblocklisthashes, System.Data.IDbTransaction transaction)
        {
            m_findBlocksetCommand.Transaction = transaction;
            m_findBlocksetCommand.SetParameterValue(0, size);
            m_findBlocksetCommand.SetParameterValue(1, fullhash);
            var blocksetid = m_findBlocksetCommand.ExecuteScalarInt64(-1);
            if (blocksetid != -1)
                return blocksetid;                        
            
            m_insertBlocksetCommand.Transaction = transaction;
            m_insertBlocksetCommand.SetParameterValue(0, size);
            m_insertBlocksetCommand.SetParameterValue(1, fullhash);
            blocksetid = m_insertBlocksetCommand.ExecuteScalarInt64(-1);

            long c = 0;
            if (blocklisthashes != null)
            {
                var index = 0L;
                m_insertBlocklistHashCommand.Transaction = transaction;
                m_insertBlocklistHashCommand.SetParameterValue(0, blocksetid);

                foreach(var hash in blocklisthashes)
                {
                    if (!string.IsNullOrEmpty(hash))
                    {
                        c++;
                        if (c <= expectedblocklisthashes)
                        {
                            m_insertBlocklistHashCommand.SetParameterValue(1, index++);
                            m_insertBlocklistHashCommand.SetParameterValue(2, hash);
                            m_insertBlocklistHashCommand.ExecuteNonQuery();
                        }
                    }
                }
            }
                            
            if (c != expectedblocklisthashes)
                Logging.Log.WriteWarningMessage(LOGTAG, "MismatchInBlocklistHashCount", null, "Mismatching number of blocklist hashes detected on blockset {2}. Expected {0} blocklist hashes, but found {1}", expectedblocklisthashes, c, blocksetid);
            
            return blocksetid;
        }

        public bool UpdateBlock(string hash, long size, long volumeID, System.Data.IDbTransaction transaction)
        {
            m_findHashBlockCommand.Transaction = transaction;
            m_findHashBlockCommand.SetParameterValue(0, hash);
            m_findHashBlockCommand.SetParameterValue(1, size);
            var currentVolumeId = m_findHashBlockCommand.ExecuteScalarInt64(-2);

            if (currentVolumeId == volumeID)
                return false;
                
            if (currentVolumeId == -2)
            {
                //Insert
                m_insertBlockCommand.Transaction = transaction;
                m_insertBlockCommand.SetParameterValue(0, hash);
                m_insertBlockCommand.SetParameterValue(1, size);
                m_insertBlockCommand.SetParameterValue(2, volumeID);
                m_insertBlockCommand.ExecuteNonQuery();
                
                return true;
            }
            else if (currentVolumeId == -1)
            {
                //Update
                m_updateBlockVolumeCommand.Transaction = transaction;
                m_updateBlockVolumeCommand.SetParameterValue(0, volumeID);
                m_updateBlockVolumeCommand.SetParameterValue(1, hash);
                m_updateBlockVolumeCommand.SetParameterValue(2, size);
                var c = m_updateBlockVolumeCommand.ExecuteNonQuery();
                if (c != 1)
                    throw new Exception(string.Format("Failed to update table, found {0} entries for key {1} with size {2}", c ,hash, size));
                    
                return true;
            }
            else
            {
                m_insertDuplicateBlockCommand.Transaction = transaction;
                m_insertDuplicateBlockCommand.SetParameterValue(0, hash);
                m_insertDuplicateBlockCommand.SetParameterValue(1, size);
                m_insertDuplicateBlockCommand.SetParameterValue(2, volumeID);
                m_insertDuplicateBlockCommand.ExecuteNonQuery();

                return false;
            }            
        }

        public void AddSmallBlocksetLink(string filehash, string blockhash, long blocksize, System.Data.IDbTransaction transaction)
        {
            m_insertSmallBlockset.Transaction = transaction;
            m_insertSmallBlockset.SetParameterValue(0, filehash);
            m_insertSmallBlockset.SetParameterValue(1, blockhash);
            m_insertSmallBlockset.SetParameterValue(2, blocksize);
            m_insertSmallBlockset.ExecuteNonQuery();
        }
        
        public bool UpdateBlockset(string hash, IEnumerable<string> blocklisthashes, System.Data.IDbTransaction transaction)
        {
            m_findblocklisthashCommand.Transaction = transaction;
            m_findblocklisthashCommand.SetParameterValue(0, hash);
            var r = m_findblocklisthashCommand.ExecuteScalar();
            if (r != null && r != DBNull.Value)
                return false;
        
            m_insertBlockset.Transaction = transaction;                
            m_insertBlockset.SetParameterValue(0, hash);
            
            var index = 0L;
            
            foreach(var s in blocklisthashes)
            {
                m_insertBlockset.SetParameterValue(1, s);
                m_insertBlockset.SetParameterValue(2, index++);
                m_insertBlockset.ExecuteNonQuery();
            }

            return true;
        }            


        public IEnumerable<string> GetBlockLists(long volumeid)
        {
            using(var cmd = m_connection.CreateCommand())
            {
                cmd.CommandText = @"SELECT DISTINCT ""BlocklistHash"".""Hash"" FROM ""BlocklistHash"", ""Block"" WHERE ""Block"".""Hash"" = ""BlocklistHash"".""Hash"" AND ""Block"".""VolumeID"" = ?";
                cmd.AddParameter(volumeid);
                
                using(var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        yield return rd.GetValue(0).ToString();
            }
        }

        public IEnumerable<IRemoteVolume> GetMissingBlockListVolumes(int passNo, long blocksize, long hashsize)
        {
            using(var cmd = m_connection.CreateCommand())
            {
                var selectCommand = @"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"", ""RemoteVolume"".""ID"" FROM ""RemoteVolume""";
            
                var missingBlocklistEntries = 
                    string.Format(
                        @"SELECT ""BlocklistHash"".""Hash"" FROM ""BlocklistHash"" LEFT OUTER JOIN ""BlocksetEntry"" ON ""BlocksetEntry"".""Index"" = (""BlocklistHash"".""Index"" * {0}) AND ""BlocksetEntry"".""BlocksetID"" = ""BlocklistHash"".""BlocksetID"" WHERE ""BlocksetEntry"".""BlocksetID"" IS NULL",
                        blocksize / hashsize
                    );

                var missingBlockInfo =
                    @"SELECT ""VolumeID"" FROM ""Block"" WHERE ""VolumeID"" < 0 AND SIZE > 0";
            
                var missingBlocklistVolumes = string.Format(
                    @"SELECT ""VolumeID"" FROM ""Block"", (" +
                    missingBlocklistEntries + 
                    @") A WHERE ""A"".""Hash"" = ""Block"".""Hash"" "
                );
                
                var countMissingInformation = string.Format(
                    @"SELECT COUNT(*) FROM (SELECT DISTINCT ""VolumeID"" FROM ({0} UNION {1}))",
                    missingBlockInfo,
                    missingBlocklistVolumes);

                if (passNo == 0)
                {
                    // On the first pass, we select all the volumes we know we need,
                    // which may be an empty list
                    cmd.CommandText = string.Format(selectCommand + @" WHERE ""ID"" IN ({0})", missingBlocklistVolumes);
                    
                    // Reset the list
                    m_proccessedVolumes = new Dictionary<long, long>();
                }
                else
                {
                    //On anything but the first pass, we check if we are done
                    var r = cmd.ExecuteScalarInt64(countMissingInformation, 0);
                    if (r == 0)
                        yield break;
                    
                    if (passNo == 1)
                    {
                        // On the second pass, we select all volumes that are not mentioned in the db
                        
                        var mentionedVolumes =
                            @"SELECT DISTINCT ""VolumeID"" FROM ""Block"" ";
                        
                        cmd.CommandText = string.Format(selectCommand + @" WHERE ""ID"" NOT IN ({0}) AND ""Type"" = ? ", mentionedVolumes);
                        cmd.AddParameter(RemoteVolumeType.Blocks.ToString());
                    }
                    else
                    {
                        // On the final pass, we select all volumes
                        // the filter will ensure that we do not download anything twice
                        cmd.CommandText = selectCommand + @" WHERE ""Type"" = ?";
                        cmd.AddParameter(RemoteVolumeType.Blocks.ToString());
                    }
                }
                
                using(var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {

                        var volumeID = rd.GetInt64(3);
                        
                        // Guard against multiple downloads of the same file
                        if (!m_proccessedVolumes.ContainsKey(volumeID))
                        {
                            m_proccessedVolumes.Add(volumeID, volumeID);
                            
                            yield return new RemoteVolume(
                                rd.GetString(0),
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

            // The first part of this query swaps out blocks for non-present remote files with
			// existing ones (as recorded in the DuplicateBlock table)
            // The second part removes references to the non-present remote files,
            // and marks the index files that pointed to them, such that they will be removed later on
			var sql = $@"
CREATE TEMPORARY TABLE ""{tablename}"" AS
SELECT ""A"".""ID"" AS ""BlockID"", ""A"".""VolumeID"" AS ""SourceVolumeID"", ""A"".""State"" AS ""SourceVolumeState"", ""B"".""VolumeID"" AS ""TargetVolumeID"", ""B"".""State"" AS ""TargetVolumeState"" FROM (SELECT ""Block"".""ID"", ""Block"".""VolumeID"", ""Remotevolume"".""State"" FROM ""Block"", ""Remotevolume"" WHERE ""Block"".""VolumeID"" = ""Remotevolume"".""ID"" and ""Remotevolume"".""State"" = ""{RemoteVolumeState.Temporary}"") A, (SELECT ""DuplicateBlock"".""BlockID"",  ""DuplicateBlock"".""VolumeID"", ""Remotevolume"".""State"" FROM ""DuplicateBlock"", ""Remotevolume"" WHERE ""DuplicateBlock"".""VolumeID"" = ""Remotevolume"".""ID"" and ""Remotevolume"".""State"" = ""{RemoteVolumeState.Verified}"") B WHERE ""A"".""ID"" = ""B"".""BlockID"";

UPDATE ""Block"" SET ""VolumeID"" = (SELECT ""TargetVolumeID"" FROM ""{tablename}"" WHERE ""Block"".""ID"" = ""{tablename}"".""BlockID"") WHERE ""Block"".""ID"" IN (SELECT ""BlockID"" FROM ""{tablename}"");

UPDATE ""DuplicateBlock"" SET ""VolumeID"" = (SELECT ""SourceVolumeID"" FROM ""{tablename}"" WHERE ""DuplicateBlock"".""BlockID"" = ""{tablename}"".""BlockID"") WHERE ""DuplicateBlock"".""BlockID"" IN (SELECT ""BlockID"" FROM ""{tablename}"");
DROP TABLE ""{tablename}"";

DELETE FROM ""IndexBlockLink"" WHERE ""BlockVolumeID"" IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Type"" = ""{RemoteVolumeType.Blocks}"" AND ""State"" = ""{RemoteVolumeState.Temporary}"" AND ""ID"" NOT IN (SELECT DISTINCT ""VolumeID"" FROM ""Block""));
DELETE FROM ""DuplicateBlock"" WHERE ""VolumeID"" IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Type"" = ""Blocks"" AND ""State"" = ""{RemoteVolumeState.Temporary}"" AND ""ID"" NOT IN (SELECT DISTINCT ""VolumeID"" FROM ""Block""));
DELETE FROM ""RemoteVolume"" WHERE ""Type"" = ""{RemoteVolumeType.Blocks}"" AND ""State"" = ""{RemoteVolumeState.Temporary}"" AND ""ID"" NOT IN (SELECT DISTINCT ""VolumeID"" FROM ""Block"");
";

// We could delete these, but we don't have to, so we keep them around until the next compact is done
// UPDATE ""RemoteVolume"" SET ""State"" = ""{3}"" WHERE ""Type"" = ""{5}"" AND ""ID"" NOT IN (SELECT ""IndexVolumeID"" FROM ""IndexBlockLink"");

			var countsql = @"SELECT COUNT(*) FROM ""RemoteVolume"" WHERE ""State"" = ""Temporary"" AND ""Type"" = ""Blocks"" ";

			using (var cmd = m_connection.CreateCommand())
			{
				var cnt = cmd.ExecuteScalarInt64(countsql);
				if (cnt > 0)
				{
					Logging.Log.WriteWarningMessage(LOGTAG, "MissingVolumesDetected", null, "Found {0} missing volumes; attempting to replace blocks from existing volumes", cnt);
					cmd.ExecuteNonQuery(sql);

					var cnt2 = cmd.ExecuteScalarInt64(countsql);
					Logging.Log.WriteVerboseMessage(LOGTAG, "ReplacedMissingVolumes", "Replaced blocks for {0} missing volumes; there are now {1} missing volumes", cnt, cnt2);
				}
			}
		}

		public override void Dispose()
        {                        
            using (var cmd = m_connection.CreateCommand())
            {                    
                if (m_tempblocklist != null)
                    try
                    {
                        cmd.CommandText = string.Format(@"DROP TABLE IF EXISTS ""{0}""", m_tempblocklist);
                        cmd.ExecuteNonQuery();
                    }
                    catch { }
                    finally { m_tempblocklist = null; }
                    
                if (m_tempsmalllist != null)
                    try
                    {
                        cmd.CommandText = string.Format(@"DROP TABLE IF EXISTS ""{0}""", m_tempsmalllist);
                        cmd.ExecuteNonQuery();
                    }
                    catch { }
                    finally { m_tempsmalllist = null; }

            }

            base.Dispose();
        }
    }
}
