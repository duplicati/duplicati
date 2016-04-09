using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.Database
{
    internal partial class LocalRecreateDatabase : LocalRestoreDatabase
    {
        private class PathEntryKeeper
        {
            private SortedList<KeyValuePair<long, long>, long> m_versions;
                        
            public long GetFilesetID(long blocksetId, long metadataId)
            {
                if (m_versions == null)
                    return -1;

                long r;
                if (!m_versions.TryGetValue(new KeyValuePair<long, long>(blocksetId, metadataId), out r))
                    return -1;
                else
                    return r;
            }
            
            public void AddFilesetID(long blocksetId, long metadataId, long filesetId)
            {
                if (m_versions == null)
                    m_versions = new SortedList<KeyValuePair<long, long>, long>(1, new KeyValueComparer());
                m_versions.Add(new KeyValuePair<long, long>(blocksetId, metadataId), filesetId);
            }
            
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
        
        private System.Data.IDbCommand m_insertFileCommand;
        private System.Data.IDbCommand m_insertFilesetEntryCommand;
        private System.Data.IDbCommand m_insertMetadatasetCommand;
        private System.Data.IDbCommand m_insertBlocksetCommand;
        private System.Data.IDbCommand m_insertBlocklistHashCommand;
        private System.Data.IDbCommand m_updateBlockVolumeCommand;
        private System.Data.IDbCommand m_insertBlockset;
        private System.Data.IDbCommand m_findBlocksetCommand;
        private System.Data.IDbCommand m_findMetadatasetCommand;
        private System.Data.IDbCommand m_findFilesetCommand;
        private System.Data.IDbCommand m_findblocklisthashCommand;
        private System.Data.IDbCommand m_findHashBlockCommand;
        private System.Data.IDbCommand m_insertBlockCommand;
        private System.Data.IDbCommand m_insertDuplicateBlockCommand;
        
        private HashLookupHelper<bool> m_blockListHashLookup;
        private HashLookupHelper<long> m_blockHashLookup;
        private HashLookupHelper<long> m_fileHashLookup;
        private HashLookupHelper<long> m_metadataLookup;
        private PathLookupHelper<PathEntryKeeper> m_filesetLookup;
        
        private string m_tempblocklist;
        
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
        SELECT DISTINCT
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
            m_tempblocklist = "TempBlocklist-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                        
            using(var cmd = m_connection.CreateCommand())
            {
                cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""BlockListHash"" TEXT NOT NULL, ""BlockHash"" TEXT NOT NULL, ""Index"" INTEGER NOT NULL)", m_tempblocklist));
                cmd.ExecuteNonQuery(string.Format(@"CREATE INDEX ""Index_{0}"" ON ""{0}"" (""BlockListHash"");", m_tempblocklist));
            }

            m_insertFileCommand = m_connection.CreateCommand();
            m_insertFilesetEntryCommand = m_connection.CreateCommand();
            m_insertMetadatasetCommand = m_connection.CreateCommand();
            m_insertBlocksetCommand = m_connection.CreateCommand();
            m_insertBlocklistHashCommand = m_connection.CreateCommand();
            m_updateBlockVolumeCommand = m_connection.CreateCommand();
            m_insertBlockset = m_connection.CreateCommand();
            m_findBlocksetCommand = m_connection.CreateCommand();
            m_findMetadatasetCommand = m_connection.CreateCommand();
            m_findFilesetCommand = m_connection.CreateCommand();
            m_findblocklisthashCommand = m_connection.CreateCommand();
            m_findHashBlockCommand = m_connection.CreateCommand();
            m_insertBlockCommand = m_connection.CreateCommand();
            m_insertDuplicateBlockCommand = m_connection.CreateCommand();
                            
            m_insertFileCommand.CommandText = @"INSERT INTO ""File"" (""Path"", ""BlocksetID"", ""MetadataID"") VALUES (?,?,?); SELECT last_insert_rowid();";
            m_insertFileCommand.AddParameters(3);
            
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
            
            m_findBlocksetCommand.CommandText = @"SELECT ""ID"" FROM ""Blockset"" WHERE ""Length"" = ? AND ""FullHash"" = ? ";
            m_findBlocksetCommand.AddParameters(2);
            
            m_findMetadatasetCommand.CommandText = @"SELECT ""Metadataset"".""ID"" FROM ""Metadataset"",""Blockset"" WHERE ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID"" AND ""Blockset"".""FullHash"" = ? AND ""Blockset"".""Length"" = ? ";
            m_findMetadatasetCommand.AddParameters(2);
            
            m_findFilesetCommand.CommandText = @"SELECT ""ID"" FROM ""File"" WHERE ""Path"" = ? AND ""BlocksetID"" = ? AND ""MetadataID"" = ? ";
            m_findFilesetCommand.AddParameters(3);
            
            m_findblocklisthashCommand.CommandText = string.Format(@"SELECT DISTINCT ""BlockListHash"" FROM ""{0}"" WHERE ""BlockListHash"" = ? ", m_tempblocklist);
            m_findblocklisthashCommand.AddParameters(1);
            
            m_findHashBlockCommand.CommandText = @"SELECT ""VolumeID"" FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ? ";
            m_findHashBlockCommand.AddParameters(2);
                        
            m_insertBlockCommand.CommandText = @"INSERT INTO ""Block"" (""Hash"", ""Size"", ""VolumeID"") VALUES (?,?,?)";
            m_insertBlockCommand.AddParameters(3);
            
            m_insertDuplicateBlockCommand.CommandText = @"INSERT INTO ""DuplicateBlock"" (""BlockID"", ""VolumeID"") VALUES ((SELECT ""ID"" FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ?), ?)";
            m_insertDuplicateBlockCommand.AddParameters(3);

            if (options.BlockHashLookupMemory > 0)
            {
                m_blockHashLookup = new HashLookupHelper<long>((ulong)options.BlockHashLookupMemory/2);
                m_blockListHashLookup = new HashLookupHelper<bool>((ulong)options.BlockHashLookupMemory/2);
            }
            if (options.FileHashLookupMemory > 0)
                m_fileHashLookup = new HashLookupHelper<long>((ulong)options.FileHashLookupMemory);
            if (options.MetadataHashMemory > 0)
                m_metadataLookup = new HashLookupHelper<long>((ulong)options.MetadataHashMemory);
            if (options.UseFilepathCache)
                m_filesetLookup = new PathLookupHelper<PathEntryKeeper>();
        }

        public void FindMissingBlocklistHashes(long hashsize, long blocksize, System.Data.IDbTransaction transaction)
        {
            using(var cmd = m_connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                
                //Update all small blocklists and matching blocks
                var selectSmallBlocks = string.Format(@"SELECT ""Blockset"".""Fullhash"", ""Blockset"".""Length"" FROM ""Blockset"" WHERE ""Blockset"".""Length"" <= {0}", blocksize);
            
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
                    
                // Update the cache with new blocks
                if (m_blockHashLookup != null)
                {
                    using(var rd = cmd.ExecuteReader(@"SELECT DISTINCT ""Hash"", ""Size"" FROM ""Block"" WHERE ""VolumeID"" = -1 "))
                        while(rd.Read())
                        {
                            var hash = rd.GetString(0);
                            var size = rd.GetInt64(1);
                            m_blockHashLookup.TryAdd(hash, size, -1);
                        }
                }                
                                                
                var selectBlocklistBlocksetEntries = string.Format(
                    @"SELECT ""E"".""BlocksetID"" AS ""BlocksetID"", ""D"".""FullIndex"" AS ""Index"", ""F"".""ID"" AS ""BlockID"" FROM ( " +
                    SELECT_BLOCKLIST_ENTRIES +
                    @") D, ""BlocklistHash"" E, ""Block"" F, ""Block"" G WHERE ""D"".""BlocklistHash"" = ""E"".""Hash"" AND ""D"".""BlocklistSize"" = ""G"".""Size"" AND ""D"".""BlocklistHash"" = ""G"".""Hash"" AND ""D"".""Blockhash"" = ""F"".""Hash"" AND ""D"".""BlockSize"" = ""F"".""Size"" ",
                    blocksize,
                    hashsize,
                    m_tempblocklist,
                    blocksize / hashsize
                    );
                    
                var selectBlocksetEntries = string.Format(
                    @"SELECT ""Blockset"".""ID"" AS ""BlocksetID"", 0 AS ""Index"", ""Block"".""ID"" AS ""BlockID"" FROM ""Blockset"", ""Block"" WHERE ""Blockset"".""Fullhash"" = ""Block"".""Hash"" AND ""Blockset"".""Length"" <= {0} ",
                    blocksize
                    );
                    
                var selectAllBlocksetEntries =
                    selectBlocklistBlocksetEntries +
                    @" UNION " +
                    selectBlocksetEntries;
                    
                var selectFiltered =
                    @"SELECT DISTINCT ""BlocksetID"", ""Index"", ""BlockID"" FROM (" +
                    selectAllBlocksetEntries +
                    @") A WHERE (""A"".""BlocksetID"" || ':' || ""A"".""Index"") NOT IN (SELECT (""BlocksetID"" || ':' || ""Index"") FROM ""BlocksetEntry"" )";
                
                var insertBlocksetEntriesCommand =
                    @"INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") " + selectFiltered;    
                
                cmd.ExecuteNonQuery(insertBlocksetEntriesCommand);                
            }
        }
        
        public void AddDirectoryEntry(long filesetid, string path, DateTime time, string metahash, long metahashsize, System.Data.IDbTransaction transaction)
        {
            AddEntry(FilelistEntryType.Folder, filesetid, path, time, FOLDER_BLOCKSET_ID, metahash, metahashsize, transaction);
        }

        public void AddSymlinkEntry(long filesetid, string path, DateTime time, string metahash, long metahashsize, System.Data.IDbTransaction transaction)
        {
            AddEntry(FilelistEntryType.Symlink, filesetid, path, time, SYMLINK_BLOCKSET_ID, metahash, metahashsize, transaction);
        }
        
        public void AddFileEntry(long filesetid, string path, DateTime time, long blocksetid, string metahash, long metahashsize, System.Data.IDbTransaction transaction)
        {
            AddEntry(FilelistEntryType.File , filesetid, path, time, blocksetid, metahash, metahashsize, transaction);
        }
        
        private void AddEntry(FilelistEntryType type, long filesetid, string path, DateTime time, long blocksetid, string metahash, long metahashsize, System.Data.IDbTransaction transaction)
        {
            var fileid = -1L;
            var metadataid = AddMetadataset(metahash, metahashsize, transaction);
                        
            if (m_filesetLookup != null)
            {
                PathEntryKeeper e;
                if (m_filesetLookup.TryFind(path, out e))
                    fileid = e.GetFilesetID(blocksetid, metadataid);
            }
            else
            {
                m_findFilesetCommand.Transaction = transaction;
                m_findFilesetCommand.SetParameterValue(0, path);
                m_findFilesetCommand.SetParameterValue(1, blocksetid);
                m_findFilesetCommand.SetParameterValue(2, metadataid);
                fileid = m_findFilesetCommand.ExecuteScalarInt64(-1);
            }
            
            if (fileid < 0)
            {
                m_insertFileCommand.Transaction = transaction;
                m_insertFileCommand.SetParameterValue(0, path);
                m_insertFileCommand.SetParameterValue(1, blocksetid);
                m_insertFileCommand.SetParameterValue(2, metadataid);
                fileid = m_insertFileCommand.ExecuteScalarInt64(-1);
                if (m_filesetLookup != null)
                {
                    PathEntryKeeper e;
                    if (m_filesetLookup.TryFind(path, out e))
                        e.AddFilesetID(blocksetid, metadataid, fileid);
                    else
                    {
                        e = new PathEntryKeeper();
                        e.AddFilesetID(blocksetid, metadataid, fileid);
                        m_filesetLookup.Insert(path, e);
                    }
                }
            }
            
            m_insertFilesetEntryCommand.Transaction = transaction;
            m_insertFilesetEntryCommand.SetParameterValue(0, filesetid);
            m_insertFilesetEntryCommand.SetParameterValue(1, fileid);
            m_insertFilesetEntryCommand.SetParameterValue(2, time.ToUniversalTime().Ticks);
            m_insertFilesetEntryCommand.ExecuteNonQuery();
        }
        
        private long AddMetadataset(string metahash, long metahashsize, System.Data.IDbTransaction transaction)
        {
            var metadataid = -1L;
            if (metahash == null)
                return metadataid;
                                
            if (m_metadataLookup != null)
            {
                if (m_metadataLookup.TryGet(metahash, metahashsize, out metadataid))
                    return metadataid;
                else
                    metadataid = -1;
            }
            else
            {
                m_findMetadatasetCommand.Transaction = transaction;
                m_findMetadatasetCommand.SetParameterValue(0, metahash);
                m_findMetadatasetCommand.SetParameterValue(1, metahashsize);
                metadataid = m_findMetadatasetCommand.ExecuteScalarInt64(-1);
                if (metadataid != -1)
                    return metadataid;
            }
            
            var blocksetid = AddBlockset(metahash, metahashsize, null, 0, transaction);
            
            m_insertMetadatasetCommand.Transaction = transaction;
            m_insertMetadatasetCommand.SetParameterValue(0, blocksetid);
            metadataid = m_insertMetadatasetCommand.ExecuteScalarInt64(-1);
            
            if (m_metadataLookup != null)
                m_metadataLookup.Add(metahash, metahashsize, metadataid);
                
            return metadataid;
        }
        
        public long AddBlockset(string fullhash, long size, IEnumerable<string> blocklisthashes, long expectedblocklisthashes, System.Data.IDbTransaction transaction)
        {
            var blocksetid = -1L;
            if (m_fileHashLookup != null)
            {
                if (m_fileHashLookup.TryGet(fullhash, size, out blocksetid))
                    return blocksetid;
                else
                    blocksetid = -1;
            }
            else
            {
                m_findBlocksetCommand.Transaction = transaction;
                m_findBlocksetCommand.SetParameterValue(0, size);
                m_findBlocksetCommand.SetParameterValue(1, fullhash);
                blocksetid = m_findBlocksetCommand.ExecuteScalarInt64(-1);
                if (blocksetid != -1)
                    return blocksetid;                        
            }
            
            m_insertBlocksetCommand.Transaction = transaction;
            m_insertBlocksetCommand.SetParameterValue(0, size);
            m_insertBlocksetCommand.SetParameterValue(1, fullhash);
            blocksetid = m_insertBlocksetCommand.ExecuteScalarInt64(-1);

            if (m_fileHashLookup != null)
                m_fileHashLookup.Add(fullhash, size, blocksetid);
        
            if (blocklisthashes != null)
            {
                var index = 0L;
                m_insertBlocklistHashCommand.Transaction = transaction;
                m_insertBlocklistHashCommand.SetParameterValue(0, blocksetid);

                long c = 0;
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

                if (c != expectedblocklisthashes) // or is there a legacy with single element blocklist hashes? --> && !(c == 1 && hash == computeBlockHash(fullhash))
                    m_result.AddWarning(string.Format("Mismatching number of blocklist hashes detected on blockset {2}. Expected {0} blocklist hashes, but found {1}", expectedblocklisthashes, c, blocksetid), null);

            }
                            
            return blocksetid;
        }

        public bool UpdateBlock(string hash, long size, long volumeID, System.Data.IDbTransaction transaction)
        {
            var currentVolumeId = -2L;
            if (m_blockHashLookup != null)
            {
                if (!m_blockHashLookup.TryGet(hash, size, out currentVolumeId))
                    currentVolumeId = -2;
            }
            else
            {
                m_findHashBlockCommand.Transaction = transaction;
                m_findHashBlockCommand.SetParameterValue(0, hash);
                m_findHashBlockCommand.SetParameterValue(1, size);
                currentVolumeId = m_findHashBlockCommand.ExecuteScalarInt64(-2);
            }
            
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
                
                if (m_blockHashLookup != null)
                    m_blockHashLookup.Add(hash, size, volumeID);

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
                    
                if (m_blockHashLookup != null)
                    m_blockHashLookup.Add(hash, size, volumeID);

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
        
        public bool UpdateBlockset(string hash, IEnumerable<string> blocklisthashes, System.Data.IDbTransaction transaction)
        {
            if (m_blockListHashLookup != null)
            {
                bool b;
                if (m_blockListHashLookup.TryGet(hash, -1, out b))
                    return false;
            }
            else
            {
                m_findblocklisthashCommand.Transaction = transaction;
                m_findblocklisthashCommand.SetParameterValue(0, hash);
                var r = m_findblocklisthashCommand.ExecuteScalar();
                if (r != null && r != DBNull.Value)
                    return false;
            }
            
            if (m_blockListHashLookup != null)
                m_blockListHashLookup.Add(hash, -1, false);
        
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
                cmd.CommandText = string.Format(@"SELECT ""BlocklistHash"".""Hash"" FROM ""BlocklistHash"", ""Block"" WHERE ""Block"".""Hash"" = ""BlocklistHash"".""Hash"" AND ""Block"".""VolumeID"" = ?");
                cmd.AddParameter(volumeid);
                
                using(var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        yield return rd.GetValue(0).ToString();
            }
        }

        public IEnumerable<IRemoteVolume> GetMissingBlockListVolumes(int passNo)
        {
            using(var cmd = m_connection.CreateCommand())
            {
                var selectCommand = @"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"", ""RemoteVolume"".""ID"" FROM ""RemoteVolume""";
            
                var missingBlocklistEntries = 
                    @"SELECT ""BlocklistHash"".""Hash"" FROM ""BlocklistHash"" LEFT OUTER JOIN ""BlocksetEntry"" ON ""BlocksetEntry"".""Index"" = ""BlocklistHash"".""Index"" AND ""BlocksetEntry"".""BlocksetID"" = ""BlocklistHash"".""BlocksetID"" WHERE ""BlocksetEntry"".""BlocksetID"" IS NULL";
                
                var missingBlockInfo = 
                    @"SELECT ""VolumeID"" FROM ""Block"" WHERE ""VolumeID"" < 0 ";
            
                var missingBlocklistVolumes = string.Format(
                    @"SELECT ""VolumeID"" FROM ""Block"", (" +
                    missingBlocklistEntries + 
                    @") A WHERE ""A"".""Hash"" = ""Block"".""Hash"" "
                );
                
                var countMissingInformation = string.Format(
                    @"SELECT COUNT(*) FROM (SELECT DISTINCT ""VolumeID"" FROM ({0} UNION {1}))",
                    missingBlockInfo,
                    missingBlocklistEntries);
                        
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
                    
            }
                    
            base.Dispose();
        }
    }
}
