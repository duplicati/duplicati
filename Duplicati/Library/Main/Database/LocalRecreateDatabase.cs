using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.Database
{
    internal partial class LocalRecreateDatabase : LocalRestoreDatabase
    {
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
        
        private HashDatabaseProtector<string> m_blockListHashLookup;
        private HashDatabaseProtector<string, long> m_blockHashLookup;
        private HashDatabaseProtector<string, long> m_fileHashLookup;
        private HashDatabaseProtector<string, long> m_metadataLookup;
        private HashDatabaseProtector<Tuple<string, long,long>, long> m_filesetLookup;
        
        private string m_tempblocklist;
        
        /// <summary>
        /// A lookup table that prevents multiple downloads of the same volume
        /// </summary>
        private Dictionary<long, long> m_proccessedVolumes;
        
        // SQL that finds index and block size for all blocklist hashes, based on the temporary hash list
        private const string SELECT_BLOCKLIST_ENTRIES = 
            @"SELECT ""B"".""Length"", ""A"".""BlocklistHash"", ""A"".""BlockHash"", ""C"".""Index"", ""A"".""Index"" AS ""BlocIndex"", ""B"".""Length""/{0} AS ""LastBlock"", ""B"".""Length"" - (""B"".""Length""/{0})*{0} AS ""LastBlockSize"", " +
            @" CASE WHEN ""A"".""Index"" + (""C"".""Index"" * ({0}/{1})) = ""B"".""Length""/{0} THEN ""B"".""Length"" - (""B"".""Length""/{0})*{0} ELSE {0} END AS ""BlockSize"", " +
            @" ""A"".""Index"" +  (""C"".""Index"" * ({0}/{1})) AS ""FullIndex"" " +
            @" FROM ""Blockset"" B, ""BlocklistHash"" C, ""{2}"" A " +
            @" WHERE ""B"".""ID"" = ""C"".""BlocksetID"" AND ""A"".""BlockListHash"" = ""C"".""Hash"" " +
            @" ORDER BY ""A"".""BlockListHash"", ""A"".""Index"" ";

        public LocalRecreateDatabase(LocalDatabase parentdb, Options options)
            : base(parentdb, options.Blocksize)
        {
            m_tempblocklist = "TempBlocklist-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                        
            using (var cmd = m_connection.CreateCommand())
                cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""BlockListHash"" TEXT NOT NULL, ""BlockHash"" TEXT NOT NULL, ""Index"" INTEGER NOT NULL)", m_tempblocklist));

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
            
            m_insertFilesetEntryCommand.CommandText = @"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Scantime"") VALUES (?,?,?)";
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
            
            m_findBlocksetCommand.CommandText = @"SELECT ""ID"" FROM ""Blockset"" WHERE ""Size"" = ? AND ""FullHash"" = ? ";
            m_findBlocksetCommand.AddParameters(2);
            
            m_findMetadatasetCommand.CommandText = @"SELECT ""Metadataset"".""ID"" FROM ""Metadataset"",""BlocksetEntry"",""Block"" WHERE ""Metadataset"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID"" AND ""Block"".""ID"" = ""BlocksetEntry"".""BlockID"" AND ""Block"".""Hash"" = ? AND ""Block"".""Size"" = ? ";
            m_findMetadatasetCommand.AddParameters(2);
            
            m_findFilesetCommand.CommandText = @"SELECT ""ID"" FROM ""File"" WHERE ""Path"" = ? AND ""BlocksetID"" = ? AND ""MetadataID"" = ? ";
            m_findFilesetCommand.AddParameters(3);
            
            m_findblocklisthashCommand.CommandText = string.Format(@"SELECT DISTINCT ""BlockListHash"" FROM ""{0}"" WHERE ""BlockListHash"" = ? ", m_tempblocklist);
            m_findblocklisthashCommand.AddParameters(1);
            
            m_findHashBlockCommand.CommandText = @"SELECT ""VolumeID"" FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ? ";
            m_findHashBlockCommand.AddParameters(2);
                        
            m_insertBlockCommand.CommandText = @"INSERT INTO ""Block"" (""Hash"", ""Size"", ""VolumeID"") VALUES (?,?,?)";
            m_insertBlockCommand.AddParameters(3);
            
            m_insertDuplicateBlockCommand.CommandText = @"INSERT INTO ""DuplicateBlock"" (""Hash"", ""Size"", ""VolumeID"") VALUE (?,?,?)";
            m_insertDuplicateBlockCommand.AddParameters(3);

            if (options.BlockHashLookupMemory > 0)
            {
                m_blockHashLookup = new HashDatabaseProtector<string, long>(LocalBackupDatabase.HASH_GUESS_SIZE, (ulong)options.BlockHashLookupMemory/2);
                m_blockListHashLookup = new HashDatabaseProtector<string>(LocalBackupDatabase.HASH_GUESS_SIZE, (ulong)options.BlockHashLookupMemory/2);
            }
            if (options.FileHashLookupMemory > 0)
                m_fileHashLookup = new HashDatabaseProtector<string, long>(LocalBackupDatabase.HASH_GUESS_SIZE, (ulong)options.FileHashLookupMemory);
            if (options.MetadataHashMemory > 0)
                m_metadataLookup = new HashDatabaseProtector<string, long>(LocalBackupDatabase.HASH_GUESS_SIZE, (ulong)options.MetadataHashMemory);
            if (options.FilePathMemory > 0)
                m_filesetLookup = new HashDatabaseProtector<Tuple<string, long, long>, long>(LocalBackupDatabase.PATH_STRING_GUESS_SIZE, (ulong)options.FilePathMemory);
        }

        public void FindMissingBlocklistHashes(long hashsize, System.Data.IDbTransaction transaction)
        {
            using(var cmd = m_connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                
                //Update all small blocklists and matching blocks
                var selectSmallBlocks = string.Format(@"SELECT ""Blockset"".""Fullhash"", ""Blockset"".""Length"" FROM ""Blockset"" WHERE ""Blockset"".""Length"" <= {0}", m_blocksize);
            
                var selectBlockHashes = string.Format(
                    @"SELECT ""BlockHash"" AS ""FullHash"", ""BlockSize"" AS ""Length"" FROM ( " +
                    SELECT_BLOCKLIST_ENTRIES +
                    @" )",
                    m_blocksize,
                    hashsize,
                    m_tempblocklist
                );
                                
                var selectAllBlocks = @"SELECT DISTINCT ""FullHash"", ""Length"" FROM (" + selectBlockHashes + " UNION " + selectSmallBlocks + " )";
                
                var selectNewBlocks = string.Format(
                    @"SELECT ""FullHash"" AS ""Hash"", ""Length"" AS ""Size"", -1 AS ""VolumeID"" " +
                    @" FROM (SELECT ""A"".""FullHash"", ""A"".""Length"", CASE WHEN ""B"".""Hash"" IS NULL THEN '' ELSE ""B"".""Hash"" END AS ""Hash"", CASE WHEN ""B"".""Size"" is NULL THEN -1 ELSE ""B"".""Size"" END AS ""Size"" FROM ({0}) A" + 
                    @" LEFT OUTER JOIN ""Block"" B ON (""B"".""Hash"" || ':' || ""B"".""Size"") = (""A"".""FullHash"" || ':' || ""A"".""Length"") )" + 
                    @" WHERE ""FullHash"" != ""Hash"" AND ""Length"" != ""Size"" ",
                    selectAllBlocks    
                );
                
                var insertBlocksCommand = 
                    @"INSERT INTO ""Block"" (""Hash"", ""Size"", ""VolumeID"") " + 
                    selectNewBlocks;
                    
                // Insert all known blocks into block table with volumeid = -1
                cmd.ExecuteNonQuery(insertBlocksCommand);
                    
                // Update the cache with new wblocks
                if (m_blockHashLookup != null)
                {
                    using(var rd = cmd.ExecuteReader(@"SELECT DISTINCT ""Hash"" FROM ""Block"" WHERE ""VolumeID"" = -1 "))
                        while(rd.Read())
                        {
                            var hash = rd.GetValue(0).ToString();
                            m_blockHashLookup.Add(HashPrefixLookup.DecodeBase64Hash(hash), hash, -1);
                        }
                }                
                                                
                
                var selectBlocklistBlocksetEntries = string.Format(
                    @"SELECT ""E"".""BlocksetID"" AS ""BlocksetID"", ""D"".""FullIndex"" AS ""Index"", ""F"".""ID"" AS ""BlockID"" FROM ( " +
                    SELECT_BLOCKLIST_ENTRIES +
                    @") D, ""BlocklistHash"" E, ""Block"" F WHERE ""D"".""BlocklistHash"" = ""E"".""Hash"" AND ""D"".""Blockhash"" = ""F"".""Hash"" AND ""D"".""BlockSize"" = ""F"".""Size"" ",
                    m_blocksize,
                    hashsize,
                    m_tempblocklist
                    );
                    
                var selectBlocksetEntries = string.Format(
                    @"SELECT ""Blockset"".""ID"" AS ""BlocksetID"", 0 AS ""Index"", ""Block"".""ID"" AS ""BlockID"" FROM ""Blockset"", ""Block"" WHERE ""Blockset"".""Fullhash"" = ""Block"".""Hash"" AND ""Blockset"".""Length"" <= {0} ",
                    m_blocksize
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
            
            var hashdata = (ulong)blocksetid ^ (ulong)metadataid ^ (ulong)path.GetHashCode();
            var tp = new Tuple<string, long, long>(path, blocksetid, metadataid);
            
            if (m_filesetLookup != null)
            {
                switch (m_filesetLookup.HasValue(hashdata, tp, out fileid))
                {
                    case HashLookupResult.Found:
                        break;
                    case HashLookupResult.NotFound:
                        fileid = -1;
                        break;
                    case HashLookupResult.Uncertain:
                        m_findFilesetCommand.Transaction = transaction;
                        m_findFilesetCommand.SetParameterValue(0, path);
                        m_findFilesetCommand.SetParameterValue(1, blocksetid);
                        m_findFilesetCommand.SetParameterValue(2, metadataid);
                        var r = m_findFilesetCommand.ExecuteScalar();
                        if (r == null || r == DBNull.Value)
                        {
                            m_filesetLookup.PositiveMisses++;
                            fileid = -1;
                        }
                        else
                        {
                            fileid = Convert.ToInt64(r);
                            m_filesetLookup.NegativeMisses++;
                            m_filesetLookup.Add(hashdata, tp, fileid);
                        }
                        break;
                }
            }
            else
            {
                m_findFilesetCommand.Transaction = transaction;
                m_findFilesetCommand.SetParameterValue(0, path);
                m_findFilesetCommand.SetParameterValue(1, blocksetid);
                m_findFilesetCommand.SetParameterValue(2, metadataid);
                var r = m_findFilesetCommand.ExecuteScalar();
                if (r != null && r != DBNull.Value)
                    fileid = Convert.ToInt64(r);
            }
            
            if (fileid < 0)
            {
                m_insertFileCommand.Transaction = transaction;
                m_insertFileCommand.SetParameterValue(0, path);
                m_insertFileCommand.SetParameterValue(1, blocksetid);
                m_insertFileCommand.SetParameterValue(2, metadataid);
                fileid = Convert.ToInt64(m_insertFileCommand.ExecuteScalar());
                if (m_filesetLookup != null)
                    m_filesetLookup.Add(hashdata, tp, fileid);
            }
            
            m_insertFilesetEntryCommand.Transaction = transaction;
            m_insertFilesetEntryCommand.SetParameterValue(0, filesetid);
            m_insertFilesetEntryCommand.SetParameterValue(1, fileid);
            m_insertFilesetEntryCommand.SetParameterValue(2,  NormalizeDateTimeToEpochSeconds(time));
            m_insertFilesetEntryCommand.ExecuteNonQuery();
        }
        
        private long AddMetadataset(string metahash, long metahashsize, System.Data.IDbTransaction transaction)
        {
            var metadataid = -1L;
            if (metahash == null)
                return metadataid;
                                
            var hashdata = HashPrefixLookup.DecodeBase64Hash(metahash);
            if (m_metadataLookup != null)
            {
                switch (m_metadataLookup.HasValue(hashdata, metahash, out metadataid))
                {
                    case HashLookupResult.Found:
                        return metadataid;
                    case HashLookupResult.NotFound:
                        metadataid = -1;
                        break;
                    case HashLookupResult.Uncertain:
                        m_findMetadatasetCommand.Transaction = transaction;
                        m_findMetadatasetCommand.SetParameterValue(0, metahash);
                        m_findMetadatasetCommand.SetParameterValue(1, metahashsize);
                        var r = m_findMetadatasetCommand.ExecuteScalar();
                        if (r == null || r == DBNull.Value)
                        {
                            metadataid = -1;
                            m_metadataLookup.PositiveMisses++;
                        }
                        else
                        {
                            metadataid = Convert.ToInt64(r);
                            m_metadataLookup.NegativeMisses++;
                            m_metadataLookup.Add(hashdata, metahash, metadataid);
                            return metadataid;
                        }

                        break;
                }
            }
            else
            {
                m_findMetadatasetCommand.Transaction = transaction;
                m_findMetadatasetCommand.SetParameterValue(0, metahash);
                m_findMetadatasetCommand.SetParameterValue(1, metahashsize);
                var r = m_findMetadatasetCommand.ExecuteScalar();
                if (r != null && r != DBNull.Value)
                    return Convert.ToInt64(r);                        
            }
            
            var blocksetid = AddBlockset(metahash, metahashsize, null, transaction);
            
            m_insertMetadatasetCommand.Transaction = transaction;
            m_insertMetadatasetCommand.SetParameterValue(0, blocksetid);
            metadataid = Convert.ToInt64(m_insertMetadatasetCommand.ExecuteScalar());
            
            if (m_metadataLookup != null)
                m_metadataLookup.Add(hashdata, metahash, metadataid);
                
            return metadataid;
        }
        
        public long AddBlockset(string fullhash, long size, IEnumerable<string> blocklisthashes, System.Data.IDbTransaction transaction)
        {
            var blocksetid = -1L;
            var hashdata = HashPrefixLookup.DecodeBase64Hash(fullhash);
            if (m_fileHashLookup != null)
            {
                switch (m_fileHashLookup.HasValue(hashdata, fullhash, out blocksetid))
                {
                    case HashLookupResult.Found:
                        return blocksetid;
                    case HashLookupResult.NotFound:
                        blocksetid = -1;
                        break;
                    case HashLookupResult.Uncertain:
                        m_findBlocksetCommand.Transaction = transaction;
                        m_findBlocksetCommand.SetParameterValue(0, size);
                        m_findBlocksetCommand.SetParameterValue(1, fullhash);
                        var r = m_findBlocksetCommand.ExecuteScalar();
                        if (r == null || r == DBNull.Value)
                        {
                            m_fileHashLookup.PositiveMisses++;
                            blocksetid = -1;
                        }
                        else
                        {
                            blocksetid = Convert.ToInt64(r);
                            m_fileHashLookup.NegativeMisses++;
                            m_fileHashLookup.Add(hashdata, fullhash, blocksetid);
                            return blocksetid;
                        }
                        break;
                }
            }
            else
            {
                m_findBlocksetCommand.Transaction = transaction;
                m_findBlocksetCommand.SetParameterValue(0, size);
                m_findBlocksetCommand.SetParameterValue(1, fullhash);
                var r = m_findBlocksetCommand.ExecuteScalar();
                if (r != null && r != DBNull.Value)
                    return Convert.ToInt64(r);                        
            }
            
            m_insertBlocksetCommand.Transaction = transaction;
            m_insertBlocksetCommand.SetParameterValue(0, size);
            m_insertBlocksetCommand.SetParameterValue(1, fullhash);
            blocksetid = Convert.ToInt64(m_insertBlocksetCommand.ExecuteScalar());

            if (m_fileHashLookup != null)
                m_fileHashLookup.Add(hashdata, fullhash, blocksetid);
        
            if (blocklisthashes != null)
            {
                var index = 0L;
                m_insertBlocklistHashCommand.Transaction = transaction;
                m_insertBlocklistHashCommand.SetParameterValue(0, blocksetid);
                foreach(var hash in blocklisthashes)
                {
                    if (!string.IsNullOrEmpty(hash))
                    {
                        m_insertBlocklistHashCommand.SetParameterValue(1, index++);
                        m_insertBlocklistHashCommand.SetParameterValue(2, hash);
                        m_insertBlocklistHashCommand.ExecuteNonQuery();
                    }
                }
            }
                            
            return blocksetid;
        }

        public void UpdateBlock(string hash, long size, long volumeID, System.Data.IDbTransaction transaction)
        {
            var hashdata = HashPrefixLookup.DecodeBase64Hash(hash);
            var currentVolumeId = -2L;
            if (m_blockHashLookup != null)
            {
                switch (m_blockHashLookup.HasValue(hashdata, hash, out currentVolumeId))
                {
                    case HashLookupResult.Found:
                        break;
                    case HashLookupResult.NotFound:
                        currentVolumeId = -2;
                        break;
                    case HashLookupResult.Uncertain:
                        m_findHashBlockCommand.Transaction = transaction;
                        m_findHashBlockCommand.SetParameterValue(0, hash);
                        m_findHashBlockCommand.SetParameterValue(1, size);
                        var r = m_findHashBlockCommand.ExecuteScalar();
                        if (r == null || r == DBNull.Value)
                        {
                            m_blockHashLookup.PositiveMisses++;
                            currentVolumeId = -2;
                        }
                        else
                        {
                            m_blockHashLookup.NegativeMisses++;
                            currentVolumeId = Convert.ToInt64(r);
                        }
                        break;
                }
            }
            else
            {
                m_findHashBlockCommand.Transaction = transaction;
                m_findHashBlockCommand.SetParameterValue(0, hash);
                m_findHashBlockCommand.SetParameterValue(1, size);
                var r = m_findHashBlockCommand.ExecuteScalar();
                if (r != null && r != DBNull.Value)
                    currentVolumeId = Convert.ToInt64(r);
            }
            
            if (currentVolumeId == volumeID)
                return;
                
            if (currentVolumeId == -2)
            {
                //Insert
                m_insertBlockCommand.Transaction = transaction;
                m_insertBlockCommand.SetParameterValue(0, hash);
                m_insertBlockCommand.SetParameterValue(1, size);
                m_insertBlockCommand.SetParameterValue(2, volumeID);
                m_insertBlockCommand.ExecuteNonQuery();
                
                if (m_blockHashLookup != null)
                    m_blockHashLookup.Add(hashdata, hash, volumeID);
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
                    m_blockHashLookup.Add(hashdata, hash, volumeID);
            }
            else
            {
                m_insertDuplicateBlockCommand.Transaction = transaction;
                m_insertDuplicateBlockCommand.SetParameterValue(0, hash);
                m_insertDuplicateBlockCommand.SetParameterValue(1, size);
                m_insertDuplicateBlockCommand.SetParameterValue(2, volumeID);
                m_insertDuplicateBlockCommand.ExecuteNonQuery();
            }
            
        }
        
        public void UpdateBlockset(string hash, IEnumerable<string> blocklisthashes, long hashsize, System.Data.IDbTransaction transaction)
        {
            var hashdata = HashPrefixLookup.DecodeBase64Hash(hash);
            if (m_blockListHashLookup != null)
            {
                switch (m_blockListHashLookup.HasValue(hashdata, hash))
                {
                    case HashLookupResult.Found:
                        return;
                    case HashLookupResult.NotFound:
                        break;
                    case HashLookupResult.Uncertain:
                        m_findblocklisthashCommand.Transaction = transaction;
                        m_findblocklisthashCommand.SetParameterValue(0, hash);
                        var r = m_findblocklisthashCommand.ExecuteScalar();
                        if (r != null && r != DBNull.Value)
                        {
                            m_blockListHashLookup.Add(hashdata, hash);
                            return;
                        }
                        break;
                }
            }
            else
            {
                m_findblocklisthashCommand.Transaction = transaction;
                m_findblocklisthashCommand.SetParameterValue(0, hash);
                var r = m_findblocklisthashCommand.ExecuteScalar();
                if (r != null && r != DBNull.Value)
                    return;
            }
            
            if (m_blockListHashLookup != null)
                m_blockListHashLookup.Add(hashdata, hash);
        
            m_insertBlockset.Transaction = transaction;                
            m_insertBlockset.SetParameterValue(0, hash);
            
            var index = 0L;
            
            foreach(var s in blocklisthashes)
            {
                m_insertBlockset.SetParameterValue(1, s);
                m_insertBlockset.SetParameterValue(2, index++);
                m_insertBlockset.ExecuteNonQuery();
            }
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
                    var r = cmd.ExecuteScalar(countMissingInformation);
                    if (r == null || r == DBNull.Value || Convert.ToInt64(r) == 0)
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
                    object[] r = new object[4];
                    while (rd.Read())
                    {
                        rd.GetValues(r);
                        
                        var volumeID = Convert.ToInt64(r[3]);
                        
                        // Guard against multiple downloads of the same file
                        if (!m_proccessedVolumes.ContainsKey(volumeID))
                        {
                            m_proccessedVolumes.Add(volumeID, volumeID);
                            
                            yield return new RemoteVolume(
                                (r[0] == null || r[0] == DBNull.Value) ? null : r[0].ToString(),
                                (r[1] == null || r[1] == DBNull.Value) ? null : r[1].ToString(),
                                (r[2] == null || r[2] == DBNull.Value) ? -1 : Convert.ToInt64(r[2])
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
                        cmd.CommandText = string.Format(@"DROP TABLE ""{0}""", m_tempblocklist);
                        cmd.ExecuteNonQuery();
                    }
                    finally { m_tempblocklist = null; }
                    
            }
            
            foreach(var cmd in new IDisposable [] {
                m_insertFileCommand,
                m_insertFilesetEntryCommand,
                m_insertMetadatasetCommand,
                m_insertBlocksetCommand,
                m_insertBlocklistHashCommand,
                m_updateBlockVolumeCommand,
                m_insertBlockset,
                m_findBlocksetCommand,
                m_findMetadatasetCommand,
                m_findFilesetCommand,
                m_blockHashLookup,
                m_fileHashLookup,
                m_metadataLookup,
                m_filesetLookup,
                m_findblocklisthashCommand,
                m_findHashBlockCommand,
                m_insertBlockCommand,
                m_insertDuplicateBlockCommand
                })
                    try
                    {
                        if (cmd != null)
                            cmd.Dispose();
                    }
                    catch
                    {
                    }
                    
            base.Dispose();
        }
    }
}
