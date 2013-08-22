using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;


namespace Duplicati.Library.Main.Database
{
    internal class LocalBackupDatabase : LocalDatabase
    {
        /// <summary>
        /// An approximate size of a hash-string in memory (44 chars * 2 for unicode + 8 bytes for pointer = 104)
        /// </summary>
        internal const uint HASH_GUESS_SIZE = 128;
        
        /// <summary>
        /// An approximate size of a path string in bytes.
        /// As all .Net strings are unicode, the average path length,
        /// must be half this size. Windows uses MAX_PATH=256, so 
        /// we guess that the average size is around 128 chars.
        /// On linux/OSX this may be wrong, but will only result
        /// in slightly more memory being used that what the user 
        /// specifies.
        /// </summary>
        internal const uint PATH_STRING_GUESS_SIZE = 256;
        
        /// <summary>
        /// The usage threshold for a lookup table that triggers a warning
        /// </summary>
        private const double FULL_HASH_USAGE_THRESHOLD = 0.05;
        /// <summary>
        /// The number of hash misses that triggers a warning
        /// </summary>
        private const long HASH_MISS_THRESHOLD = 200;

        private readonly System.Data.IDbCommand m_findblockCommand;
        private readonly System.Data.IDbCommand m_findblocksetCommand;
        private readonly System.Data.IDbCommand m_findfilesetCommand;
        private readonly System.Data.IDbCommand m_findmetadatasetCommand;
        private readonly System.Data.IDbCommand m_findmetadatasetProbeCommand;

        private readonly System.Data.IDbCommand m_insertblockCommand;

        private readonly System.Data.IDbCommand m_insertfileCommand;

        private readonly System.Data.IDbCommand m_insertblocksetCommand;
        private readonly System.Data.IDbCommand m_insertblocksetentryCommand;
        private readonly System.Data.IDbCommand m_insertblocklistHashesCommand;

        private readonly System.Data.IDbCommand m_insertmetadatasetCommand;

        private readonly System.Data.IDbCommand m_selectfileSimpleCommand;
        private readonly System.Data.IDbCommand m_selectfileHashCommand;
        private readonly System.Data.IDbCommand m_selectblocklistHashesCommand;

        private readonly System.Data.IDbCommand m_findremotevolumestateCommand;
        private readonly System.Data.IDbCommand m_updateblockCommand;

        private readonly System.Data.IDbCommand m_insertfileOperationCommand;
		
		private HashDatabaseProtector<string> m_blockHashLookup;
        private HashDatabaseProtector<string, long> m_fileHashLookup;
        private HashDatabaseProtector<string, long> m_metadataLookup;
        private HashDatabaseProtector<string, KeyValuePair<long, DateTime>> m_fileScantimeLookup;
        private HashDatabaseProtector<Tuple<string, long,long>, long> m_filesetLookup;
        
        private long m_missingBlockHashes;
        private string m_scantimelookupTablename;
        
        private long m_filesetId;

        public LocalBackupDatabase(string path, Options options)
            : this(new LocalDatabase(path, "Backup"), options)
        {
        }
       	
        public LocalBackupDatabase(LocalDatabase db, Options options)
        	: base(db)
        {
            m_findblockCommand = m_connection.CreateCommand();
            m_insertblockCommand = m_connection.CreateCommand();
            m_insertfileCommand = m_connection.CreateCommand();
            m_insertblocksetCommand = m_connection.CreateCommand();
            m_insertmetadatasetCommand = m_connection.CreateCommand();
            m_findblocksetCommand = m_connection.CreateCommand();
            m_findmetadatasetCommand = m_connection.CreateCommand();
            m_findfilesetCommand = m_connection.CreateCommand();
            m_insertblocksetentryCommand = m_connection.CreateCommand();
            m_findremotevolumestateCommand = m_connection.CreateCommand();
            m_updateblockCommand = m_connection.CreateCommand();
            m_insertblocklistHashesCommand = m_connection.CreateCommand();
            m_selectblocklistHashesCommand = m_connection.CreateCommand();
            m_insertfileOperationCommand = m_connection.CreateCommand();
            m_selectfileSimpleCommand = m_connection.CreateCommand();
            m_selectfileHashCommand = m_connection.CreateCommand();
            m_findmetadatasetProbeCommand = m_connection.CreateCommand();
				
			m_findblockCommand.CommandText = @"SELECT ""VolumeID"" FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ?";
            m_findblockCommand.AddParameters(2);

            m_findblocksetCommand.CommandText = @"SELECT ""ID"" FROM ""Blockset"" WHERE ""Fullhash"" = ? AND ""Length"" = ?";
            m_findblocksetCommand.AddParameters(2);

            m_findmetadatasetProbeCommand.CommandText = @"SELECT ""ID"" FROM ""Blockset"" WHERE ""Hash"" = ? AND ""Size"" = ? LIMIT 1";
            m_findmetadatasetProbeCommand.AddParameters(2);

            m_findmetadatasetCommand.CommandText = @"SELECT ""A"".""ID"" FROM ""Metadataset"" A, ""BlocksetEntry"" B, ""Block"" C WHERE ""A"".""BlocksetID"" = ""B"".""BlocksetID"" AND ""B"".""BlocksetID"" = ""C"".""ID"" AND ""C"".""Hash"" = ? AND ""C"".""Size"" = ? LIMIT 1";
            m_findmetadatasetCommand.AddParameters(2);

            m_findfilesetCommand.CommandText = @"SELECT ""ID"" FROM ""File"" WHERE ""BlocksetID"" = ? AND ""MetadataID"" = ? AND ""Path"" = ?";
            m_findfilesetCommand.AddParameters(3);

            m_insertblockCommand.CommandText = @"INSERT INTO ""Block"" (""Hash"", ""VolumeID"", ""Size"") VALUES (?, ?, ?)";
            m_insertblockCommand.AddParameters(3);

            m_insertfileOperationCommand.CommandText = @"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Scantime"") VALUES (?, ?, ?)";
            m_insertfileOperationCommand.AddParameters(3);

            m_insertfileCommand.CommandText = @"INSERT INTO ""File"" (""Path"",""BlocksetID"", ""MetadataID"") VALUES (?, ? ,?); SELECT last_insert_rowid();";
            m_insertfileCommand.AddParameters(3);

            m_insertblocksetCommand.CommandText = @"INSERT INTO ""Blockset"" (""Length"", ""FullHash"") VALUES (?, ?); SELECT last_insert_rowid();";
            m_insertblocksetCommand.AddParameters(2);

            m_insertblocksetentryCommand.CommandText = @"INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") SELECT ? AS A, ? AS B, ""ID"" FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ?";
            m_insertblocksetentryCommand.AddParameters(4);

            m_insertblocklistHashesCommand.CommandText = @"INSERT INTO ""BlocklistHash"" (""BlocksetID"", ""Index"", ""Hash"") VALUES (?, ?, ?)";
            m_insertblocklistHashesCommand.AddParameters(3);

            m_insertmetadatasetCommand.CommandText = @"INSERT INTO ""Metadataset"" (""BlocksetID"") VALUES (?); SELECT last_insert_rowid();";
            m_insertmetadatasetCommand.AddParameter();

            //Need a temporary table with path/scantime lookups
            m_scantimelookupTablename = "ScanTime-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
            using (var cmd = m_connection.CreateCommand())
                cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS SELECT ""FilesetEntry"".""FileID"" AS ""FileID"", MAX(""FilesetEntry"".""Scantime"") AS ""Scantime"", ""File"".""Path"" AS ""Path"" FROM ""FilesetEntry"" INNER JOIN ""File"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" GROUP BY ""FilesetEntry"".""FileID"", ""File"".""Path"" ", m_scantimelookupTablename));

            m_selectfileSimpleCommand.CommandText = string.Format(@"SELECT ""FileID"", ""Scantime"" FROM ""{0}"" WHERE ""Path"" = ?", m_scantimelookupTablename);
            m_selectfileSimpleCommand.AddParameters(1);

            m_selectfileHashCommand.CommandText = @"SELECT ""Blockset"".""Fullhash"" FROM ""Blockset"", ""File"" WHERE ""Blockset"".""ID"" = ""File"".""BlocksetID"" AND ""File"".""ID"" = ?  ";
            m_selectfileHashCommand.AddParameters(1);

            m_selectblocklistHashesCommand.CommandText = @"SELECT ""Hash"" FROM ""BlocklistHash"" WHERE ""BlocksetID"" = ? ORDER BY ""Index"" ASC ";
            m_selectblocklistHashesCommand.AddParameters(1);

            m_findremotevolumestateCommand.CommandText = @"SELECT ""State"" FROM ""Remotevolume"" WHERE ""Name"" = ?";
            m_findremotevolumestateCommand.AddParameters(1);

            m_updateblockCommand.CommandText = @"UPDATE ""Block"" SET ""VolumeID"" = ? WHERE ""Hash"" = ? AND ""Size"" = ? ";
            m_updateblockCommand.AddParameters(3);
                        
			if (options.BlockHashLookupMemory > 0)
                m_blockHashLookup = new HashDatabaseProtector<string>(HASH_GUESS_SIZE, (ulong)options.BlockHashLookupMemory);            
            if (options.FileHashLookupMemory > 0)
                m_fileHashLookup = new HashDatabaseProtector<string, long>(HASH_GUESS_SIZE, (ulong)options.FileHashLookupMemory);
            if (options.MetadataHashMemory > 0)
                m_metadataLookup = new HashDatabaseProtector<string, long>(HASH_GUESS_SIZE, (ulong)options.MetadataHashMemory);

            if (options.FilePathMemory > 0)
            {
                m_fileScantimeLookup = new HashDatabaseProtector<string, KeyValuePair<long, DateTime>>(PATH_STRING_GUESS_SIZE, (ulong)options.FilePathMemory / 2);
                m_filesetLookup = new HashDatabaseProtector<Tuple<string, long, long>, long>(PATH_STRING_GUESS_SIZE, (ulong)options.FilePathMemory / 2);
            }

            //Populate the lookup tables
            using (var cmd = m_connection.CreateCommand())
            {
                if (m_blockHashLookup != null)
                    using (var rd = cmd.ExecuteReader(@"SELECT DISTINCT ""Block"".""Hash"" FROM ""Block"", ""RemoteVolume"" WHERE ""RemoteVolume"".""ID"" = ""Block"".""VolumeID"" AND ""RemoteVolume"".""State"" IN (?,?,?,?) ", RemoteVolumeState.Temporary.ToString(), RemoteVolumeState.Uploading.ToString(), RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString()))
                        while (rd.Read())
                        {
                            var str = rd.GetValue(0).ToString();
                            var key = HashPrefixLookup.DecodeBase64Hash(str);
                            m_blockHashLookup.Add(key, str);
                        }
                
                if (m_fileHashLookup != null)
                    using (var rd = cmd.ExecuteReader(@"SELECT DISTINCT ""FullHash"", ""ID"" FROM ""BlockSet"""))
                        while (rd.Read())
                        {
                            var str = rd.GetValue(0).ToString();
                            var id = Convert.ToInt64(rd.GetValue(1));
                            var key = HashPrefixLookup.DecodeBase64Hash(str);
                            m_fileHashLookup.Add(key, str, id);
                        }

                if (m_metadataLookup != null)
                    using (var rd = cmd.ExecuteReader(@"SELECT ""Metadataset"".""ID"", ""Blockset"".""FullHash"" FROM ""Metadataset"", ""Blockset"" WHERE ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID"" "))
                        while (rd.Read())
                        {
                            var metadataid = Convert.ToInt64(rd.GetValue(0));
                            var hash = rd.GetValue(1).ToString();
                            var hashdata = HashPrefixLookup.DecodeBase64Hash(hash);
                            m_metadataLookup.Add(hashdata, hash, metadataid);
                        }

                if (m_fileScantimeLookup != null)
                    using (var rd = cmd.ExecuteReader(string.Format(@" SELECT ""FileID"", ""Scantime"", ""Path"", ""Scantime"" FROM ""{0}"" ", m_scantimelookupTablename)))
                        while (rd.Read())
                        {
                            var id = Convert.ToInt64(rd.GetValue(0));
                            var scantime = ParseFromEpochSeconds(Convert.ToInt64(rd.GetValue(1)));
                            var path = rd.GetValue(2).ToString();
                            m_fileScantimeLookup.Add((ulong)path.GetHashCode(), path, new KeyValuePair<long, DateTime>(id, scantime));
                        }

                if (m_filesetLookup != null)
                    using (var rd = cmd.ExecuteReader(string.Format(@" SELECT ""Path"", ""BlocksetID"", ""MetadataID"", ""ID"" FROM ""File"" ")))
                        while (rd.Read())
                        {
                            var path = rd.GetValue(0).ToString();
                            var blocksetid = Convert.ToInt64(rd.GetValue(1));
                            var metadataid = Convert.ToInt64(rd.GetValue(2));
                            var filesetid = Convert.ToInt64(rd.GetValue(3));
                            m_filesetLookup.Add((ulong)blocksetid ^ (ulong)metadataid ^ (ulong)path.GetHashCode(), new Tuple<string, long, long>(path, blocksetid, metadataid), filesetid);
                        }
                                                        
                m_missingBlockHashes = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT (*) FROM (SELECT DISTINCT ""Block"".""Hash"", ""Block"".""Size"" FROM ""Block"", ""RemoteVolume"" WHERE ""RemoteVolume"".""ID"" = ""Block"".""VolumeID"" AND ""RemoteVolume"".""State"" NOT IN (?,?,?,?))", RemoteVolumeState.Temporary.ToString(), RemoteVolumeState.Uploading.ToString(), RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString()));
            }

        }

        /// <summary>
        /// Adds a block to the local database, returning a value indicating if the value presents a new block
        /// </summary>
        /// <param name="key">The block key</param>
        /// <param name="archivename">The name of the archive that holds the data</param>
        /// <returns>True if the block should be added to the current output</returns>
        public bool AddBlock (string key, long size, long volumeid, System.Data.IDbTransaction transaction = null)
        {
            object r = null;
            ulong hashdata = 0;
            if (m_blockHashLookup != null) 
            {
                hashdata = HashPrefixLookup.DecodeBase64Hash(key);
                switch (m_blockHashLookup.HasValue(hashdata, key)) 
                {
                    case HashLookupResult.NotFound:
                        //We insert it
                        break;
                        
                    case HashLookupResult.Found:
                        return false;
                        
                    default:
                        m_findblockCommand.Transaction = transaction;
                        m_findblockCommand.SetParameterValue(0, key);
                        m_findblockCommand.SetParameterValue(1, size);
                        r = m_findblockCommand.ExecuteScalar();
                        if (r == null || r == DBNull.Value) {
                            m_blockHashLookup.PositiveMisses++;
                        } else {
                            m_blockHashLookup.NegativeMisses++;
                            if (m_missingBlockHashes == 0) {
                                //Update the lookup table, MRU style
                                m_blockHashLookup.Add(hashdata, key);
                                return false;
                            }
                        }
                        break;
                }
            }
            else
            {
                m_findblockCommand.Transaction = transaction;
                m_findblockCommand.SetParameterValue(0, key);
                m_findblockCommand.SetParameterValue(1, size);
                r = m_findblockCommand.ExecuteScalar();
            }

            if (r == null || r == DBNull.Value)
            {
                m_insertblockCommand.Transaction = transaction;
                m_insertblockCommand.SetParameterValue(0, key);
                m_insertblockCommand.SetParameterValue(1, volumeid);
                m_insertblockCommand.SetParameterValue(2, size);
                m_insertblockCommand.ExecuteNonQuery();
                if (m_blockHashLookup != null)
                    m_blockHashLookup.Add(hashdata, key);
                return true;
            }
            else
            {
                //We add/update it now
                if (m_blockHashLookup != null)
                    m_blockHashLookup.Add(hashdata, key);

                //If the block is found and the volume is broken somehow.
                m_findremotevolumestateCommand.Transaction = transaction;
                r = m_findremotevolumestateCommand.ExecuteScalar(null, r);
                if (r != null && (r.ToString() == RemoteVolumeState.Temporary.ToString() || r.ToString() == RemoteVolumeState.Uploading.ToString() || r.ToString() == RemoteVolumeState.Uploaded.ToString() || r.ToString() == RemoteVolumeState.Verified.ToString()))
                {
                    return false;
                }
                else
                {
                    m_updateblockCommand.Transaction = transaction;
                    m_updateblockCommand.ExecuteNonQuery(null, volumeid, key, size);
                    return true;
                }

            }
        }


        /// <summary>
        /// Adds a blockset to the database, returns a value indicating if the blockset is new
        /// </summary>
        /// <param name="filehash">The hash of the blockset</param>
        /// <param name="size">The size of the blockset</param>
        /// <param name="fragmentoffset">The fragmentoffset for the last block</param>
        /// <param name="fragmenthash">The hash of the fragment</param>
        /// <param name="hashes">The list of hashes</param>
        /// <param name="blocksetid">The id of the blockset, new or old</param>
        /// <returns>True if the blockset was created, false otherwise</returns>
        public bool AddBlockset (string filehash, long size, int blocksize, IEnumerable<string> hashes, IEnumerable<string> blocklistHashes, out long blocksetid, System.Data.IDbTransaction transaction = null)
        {
            object r = null;
            ulong hashdata = 0;
            
            if (m_fileHashLookup != null)
            {
                hashdata = HashPrefixLookup.DecodeBase64Hash(filehash);
                
                switch (m_fileHashLookup.HasValue(hashdata, filehash, out blocksetid)) 
                {
                    case HashLookupResult.NotFound:
                        //We insert it, but avoid looking for it
                        break;
                        
                    case HashLookupResult.Found:
                        //Got it, just return it
                        return false;
                        
                    default:
                        m_findblocksetCommand.Transaction = transaction;
                        r = m_findblocksetCommand.ExecuteScalar(null, filehash, size);
                        if (r == null || r == DBNull.Value)
                            m_fileHashLookup.PositiveMisses++;
                        else
                        {
                            m_fileHashLookup.NegativeMisses++;
                            blocksetid = Convert.ToInt64(r);
                            
                            //Update, MRU style
                            m_fileHashLookup.Add(hashdata, filehash, blocksetid);
                            return false;
                        }
                        break;
                }
            } 
            else 
            {
                m_findblocksetCommand.Transaction = transaction;
                r = m_findblocksetCommand.ExecuteScalar(null, filehash, size);
                if (r != null && r != DBNull.Value)
                {
                    blocksetid = Convert.ToInt64(r);
                	return false; //Found it
                }
            }
                
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                m_insertblocksetCommand.Transaction = tr.Parent;
                m_insertblocksetCommand.SetParameterValue(0, size);
                m_insertblocksetCommand.SetParameterValue(1, filehash);
                blocksetid = Convert.ToInt64(m_insertblocksetCommand.ExecuteScalar());
                if (m_fileHashLookup != null)
                    m_fileHashLookup.Add(hashdata, filehash, blocksetid);

                long ix = 0;
                if (blocklistHashes != null)
                {
                    m_insertblocklistHashesCommand.SetParameterValue(0, blocksetid);
                    m_insertblocklistHashesCommand.Transaction = tr.Parent;
                    foreach (var bh in blocklistHashes)
                    {
                        m_insertblocklistHashesCommand.SetParameterValue(1, ix);
                        m_insertblocklistHashesCommand.SetParameterValue(2, bh);
                        m_insertblocklistHashesCommand.ExecuteNonQuery();
                        ix++;
                    }
                }

                m_insertblocksetentryCommand.SetParameterValue(0, blocksetid);
                m_insertblocksetentryCommand.Transaction = tr.Parent;


                ix = 0;
                long remainsize = size;
                foreach (var h in hashes)
                {
                    m_insertblocksetentryCommand.SetParameterValue(1, ix);
                    m_insertblocksetentryCommand.SetParameterValue(2, h);
                    m_insertblocksetentryCommand.SetParameterValue(3, remainsize < blocksize ? remainsize : blocksize);
                    var c = m_insertblocksetentryCommand.ExecuteNonQuery();
                    if (c != 1)
                        throw new Exception(string.Format("Unexpected result count: {0}, expected {1}", c, 1));
                    ix++;
                    remainsize -= blocksize;
                }

                tr.Commit();
            }

            return true;

        }

        /// <summary>
        /// Adds a metadata set to the database, and returns a value indicating if the record was new
        /// </summary>
        /// <param name="hash">The metadata hash</param>
        /// <param name="metadataid">The id of the metadata set</param>
        /// <returns>True if the set was added to the database, false otherwise</returns>
        public bool AddMetadataset(string hash, long size, out long metadataid, System.Data.IDbTransaction transaction = null)
        {
            ulong hashdata = 0;
            if (size > 0)
            {
                if (m_metadataLookup != null)
                {
                    hashdata = HashPrefixLookup.DecodeBase64Hash(hash);
                    
                    switch(m_metadataLookup.HasValue(hashdata, hash, out metadataid))
                    {
                        case HashLookupResult.NotFound:
                            //We insert it, but avoid looking for it
                            break;
                            
                        case HashLookupResult.Found:
                            //Got it, just return the result
                            return false;
                        
                        default:
                            //Highly unlikely that we find matching metadata, due to the timestamp
                            m_findmetadatasetProbeCommand.Transaction = transaction;
                            var r = m_findmetadatasetProbeCommand.ExecuteScalar(null, hash, size);
                            if (r != null && r != DBNull.Value)
                            {
                                m_findmetadatasetCommand.Transaction = transaction;
                                r = m_findmetadatasetCommand.ExecuteScalar(null, hash, size);
                                if (r != null && r != DBNull.Value)
                                {
                                    m_metadataLookup.NegativeMisses++;
                                    //Update the lookup table, MRU style
                                    metadataid = Convert.ToInt64(r);
                                    m_metadataLookup.Add(hashdata, hash, metadataid);
                                    return false;
                                }
                            }
                            
                            if (r == null || r == DBNull.Value)
                                m_metadataLookup.PositiveMisses++;
                            else
                                m_metadataLookup.NegativeMisses++;
                                
                            break;
                    }
                }
                else
                {
                    m_findmetadatasetProbeCommand.Transaction = transaction;
                    var r = m_findmetadatasetProbeCommand.ExecuteScalar(null, hash, size);
                    if (r != null && r != DBNull.Value)
                    {
                        m_findmetadatasetCommand.Transaction = transaction;
                        m_findmetadatasetCommand.ExecuteScalar(null, hash, size);
                    }
                }
            

                long blocksetid;
                AddBlockset(hash, size, (int)size, new string[] { hash }, null, out blocksetid, transaction);

                using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
                {
                    m_insertmetadatasetCommand.Transaction = tr.Parent;
                    m_insertmetadatasetCommand.SetParameterValue(0, blocksetid);
                    metadataid = Convert.ToInt64(m_insertmetadatasetCommand.ExecuteScalar());
                    tr.Commit();
                    if (m_metadataLookup != null)
                        m_metadataLookup.Add(hashdata, hash, metadataid);
                }

                return true;
            }

            metadataid = -2;
            return false;

        }

        /// <summary>
        /// Adds a file record to the database
        /// </summary>
        /// <param name="filename">The path to the file</param>
        /// <param name="scantime">The time the file was scanned</param>
        /// <param name="blocksetID">The ID of the hashkey for the file</param>
        /// <param name="metadataID">The ID for the metadata</param>
        /// <param name="transaction">The transaction to use for insertion, or null for no transaction</param>
        /// <param name="operationId">The operationId to use, or -1 to use the current operation</param>
        public void AddFile(string filename, DateTime scantime, long blocksetID, long metadataID, System.Data.IDbTransaction transaction)
        {
            long fileid;
            object fileidobj = null;
            ulong hashdata = 0;
            if (m_filesetLookup != null)
            {
                hashdata = (ulong)blocksetID ^ (ulong)metadataID ^ (ulong)filename.GetHashCode();
                var tp = new Tuple<string, long, long>(filename, blocksetID, metadataID);

                switch (m_filesetLookup.HasValue(hashdata, tp, out fileid))
                {
                    case HashLookupResult.NotFound:
                        // We insert it, but avoid looking for it
                        fileidobj = null;
                        break;

                    case HashLookupResult.Found:
                        // Have the id, avoid looking for it,
                        // but update operation filelist
                        fileidobj = fileid;
                        break;

                    default:
                        m_findfilesetCommand.Transaction = transaction;
                        m_findfilesetCommand.SetParameterValue(0, blocksetID);
                        m_findfilesetCommand.SetParameterValue(1, metadataID);
                        m_findfilesetCommand.SetParameterValue(2, filename);
                        fileidobj = m_findfilesetCommand.ExecuteScalar();
                        if (fileidobj == null || fileidobj == DBNull.Value)
                            m_filesetLookup.PositiveMisses++;
                        else
                            m_filesetLookup.NegativeMisses++;
                        break;
                }
            }
            else
            {
                m_findfilesetCommand.Transaction = transaction;
                m_findfilesetCommand.SetParameterValue(0, blocksetID);
                m_findfilesetCommand.SetParameterValue(1, metadataID);
                m_findfilesetCommand.SetParameterValue(2, filename);
                fileidobj = m_findfilesetCommand.ExecuteScalar();
            }
            
            if (fileidobj == null || fileidobj == DBNull.Value)
            {
                using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
                {
                    m_insertfileCommand.Transaction = tr.Parent;
                    m_insertfileCommand.SetParameterValue(0, filename);
                    m_insertfileCommand.SetParameterValue(1, blocksetID);
                    m_insertfileCommand.SetParameterValue(2, metadataID);
                    fileidobj = Convert.ToInt64(m_insertfileCommand.ExecuteScalar());
                    tr.Commit();                    

                    // We do not need to update this, because we will not ask for the same file twice
                    if (m_filesetLookup != null)                    
                        m_filesetLookup.Add(hashdata, new Tuple<string, long, long>(filename, blocksetID, metadataID), Convert.ToInt64(fileidobj));
                }
            }
            
            m_insertfileOperationCommand.Transaction = transaction;
            m_insertfileOperationCommand.SetParameterValue(0, m_filesetId);
            m_insertfileOperationCommand.SetParameterValue(1, fileidobj);
            m_insertfileOperationCommand.SetParameterValue(2, NormalizeDateTimeToEpochSeconds(scantime));
            m_insertfileOperationCommand.ExecuteNonQuery();

        }

        public void AddUnmodifiedFile(long fileid, DateTime scantime, System.Data.IDbTransaction transaction = null)
        {
            m_insertfileOperationCommand.Transaction = transaction;
            m_insertfileOperationCommand.SetParameterValue(0, m_filesetId);
            m_insertfileOperationCommand.SetParameterValue(1, fileid);
            m_insertfileOperationCommand.SetParameterValue(2, NormalizeDateTimeToEpochSeconds(scantime));
            m_insertfileOperationCommand.ExecuteNonQuery();
        }

        public void AddDirectoryEntry(string path, long metadataID, DateTime scantime, System.Data.IDbTransaction transaction = null)
        {
            AddFile(path, scantime, FOLDER_BLOCKSET_ID, metadataID, transaction);
        }
        
        public void AddSymlinkEntry(string path, long metadataID, DateTime scantime, System.Data.IDbTransaction transaction = null)
        {
            AddFile(path, scantime, SYMLINK_BLOCKSET_ID, metadataID, transaction);
        }

        public long GetFileEntry(string path, out DateTime oldScanned)
        {
            if (m_fileScantimeLookup != null)
            {
                KeyValuePair<long, DateTime> tmp;
                var hashdata = (ulong)path.GetHashCode();
                switch (m_fileScantimeLookup.HasValue(hashdata, path, out tmp))
                {
                    case HashLookupResult.NotFound:
                        oldScanned = DateTime.UtcNow;
                        return -1;
                    case HashLookupResult.Found:
                        oldScanned = tmp.Value;
                        return tmp.Key;
                }
            }
            
            m_selectfileSimpleCommand.SetParameterValue(0, path);
            using(var rd = m_selectfileSimpleCommand.ExecuteReader())
                if (rd.Read())
                {
                    if (m_fileScantimeLookup != null)
                        m_fileScantimeLookup.NegativeMisses++;                    
                    
                    oldScanned = ParseFromEpochSeconds(Convert.ToInt64(rd.GetValue(1)));
                    var id = Convert.ToInt64(rd.GetValue(0));
                    
                    // We do not add here, because we will never query the same path twice,
                    // so it is more likely that the value currently occuping the table
                    // will be used later
                    
                    //m_fileScantimeLookup.Add(hashdata, path, new KeyValuePair<long, DateTime>(id, oldScanned));
                    return id;
                }
                else
                {
                    if (m_fileScantimeLookup != null)
                        m_fileScantimeLookup.PositiveMisses++;

                    oldScanned = DateTime.UtcNow;
                    return -1;
                }

        }

        public string GetFileHash(long fileid)
        {
            m_selectfileHashCommand.SetParameterValue(0, fileid);
            return m_selectfileHashCommand.ExecuteScalar().ToString();
            
        }
        
        public void WriteFileset(Volumes.FilesetVolumeWriter filesetvolume, System.Data.IDbTransaction transaction)
		{
			WriteFileset(filesetvolume, transaction, m_filesetId);
		}        

        public override void Dispose ()
        {
            if (m_scantimelookupTablename != null)
                try 
                { 
                    using(var cmd = m_connection.CreateCommand())
                        cmd.ExecuteNonQuery(string.Format(@"DROP TABLE ""{0}"" ", m_scantimelookupTablename));
                }
                finally
                {
                    m_scantimelookupTablename = null;
                }

            if (m_blockHashLookup != null)
                try { m_blockHashLookup.Dispose(); }
                finally { m_blockHashLookup = null; }

            if (m_fileHashLookup != null)
                try { m_fileHashLookup.Dispose(); }
                finally { m_fileHashLookup = null; }

            if (m_metadataLookup != null)
                try { m_metadataLookup.Dispose(); }
                finally { m_metadataLookup = null; }

            if (m_fileScantimeLookup != null)
                try { m_fileScantimeLookup.Dispose(); }
                finally { m_fileScantimeLookup = null; }

            if (m_filesetLookup != null)
                try { m_filesetLookup.Dispose(); }
                finally { m_filesetLookup = null; }

            base.Dispose();
        }

        private long GetPreviousFilesetID(System.Data.IDbCommand cmd)
        {
            return GetPreviousFilesetID(cmd, OperationTimestamp, m_filesetId);
        }
        
        private long GetPreviousFilesetID(System.Data.IDbCommand cmd, DateTime timestamp, long filesetid)
        {
            long lastFilesetId = -1;

            var lastIdObj = cmd.ExecuteScalar(@"SELECT ""ID"" FROM ""Fileset"" WHERE ""Timestamp"" < ? AND ""ID"" != ? ORDER BY ""Timestamp"" DESC ", NormalizeDateTimeToEpochSeconds(timestamp), filesetid);
            if (lastIdObj != null && lastIdObj != DBNull.Value)
                lastFilesetId = Convert.ToInt64(lastIdObj);
                
            return lastFilesetId;
        }

        internal void UpdateChangeStatistics(BackupResults results)
        {
            using (var cmd = m_connection.CreateCommand()) 
            {
				var lastFilesetId = GetPreviousFilesetID(cmd);
                results.AddedFolders = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" = ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", m_filesetId, FOLDER_BLOCKSET_ID, lastFilesetId));
                results.AddedSymlinks = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" = ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", m_filesetId, SYMLINK_BLOCKSET_ID, lastFilesetId));
                results.AddedFiles = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" != ? AND ""File"".""BlocksetID"" != ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", m_filesetId, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID, lastFilesetId));

                results.DeletedFolders = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" = ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", lastFilesetId, FOLDER_BLOCKSET_ID, m_filesetId));
                results.DeletedSymlinks = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" = ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", lastFilesetId, SYMLINK_BLOCKSET_ID, m_filesetId));
                results.DeletedFiles = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" != ? AND ""File"".""BlocksetID"" != ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", lastFilesetId, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID, m_filesetId));

				var subqueryNonFiles = @"SELECT ""File"".""Path"", ""Blockset"".""Fullhash"" FROM ""File"", ""FilesetEntry"", ""Metadataset"", ""Blockset"" WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""Metadataset"".""ID"" = ""File"".""MetadataID"" AND ""File"".""BlocksetID"" = ? AND ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID"" AND ""FilesetEntry"".""FilesetID"" = ? ";
				var subqueryFiles = @"SELECT ""File"".""Path"", ""A"".""Fullhash"" AS ""Filehash"", ""B"".""Fullhash"" AS ""Metahash"" FROM ""File"", ""FilesetEntry"", ""Blockset"" A, ""Blockset"" B, ""Metadataset""  WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""A"".""ID"" = ""File"".""BlocksetID"" AND ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""MetadataID"" = ""Metadataset"".""ID"" AND ""Metadataset"".""BlocksetID"" = ""B"".""ID"" ";

                results.ModifiedFolders = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM (" + subqueryNonFiles + @") A, (" + subqueryNonFiles + @") B WHERE ""A"".""Path"" = ""B"".""Path"" AND ""A"".""Fullhash"" != ""B"".""Fullhash"" ", lastFilesetId, FOLDER_BLOCKSET_ID, m_filesetId, FOLDER_BLOCKSET_ID));
                results.ModifiedSymlinks = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM (" + subqueryNonFiles + @") A, (" + subqueryNonFiles + @") B WHERE ""A"".""Path"" = ""B"".""Path"" AND ""A"".""Fullhash"" != ""B"".""Fullhash"" ", lastFilesetId, SYMLINK_BLOCKSET_ID, m_filesetId, SYMLINK_BLOCKSET_ID));
                results.ModifiedFiles = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM (" + subqueryFiles + @") A, (" + subqueryFiles + @") B WHERE ""A"".""Path"" = ""B"".""Path"" AND (""A"".""Filehash"" != ""B"".""Filehash"" OR ""A"".""Metahash"" != ""B"".""Metahash"")", lastFilesetId, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID, m_filesetId));
            }

            if (m_blockHashLookup != null && m_fileHashLookup != null && ((m_blockHashLookup.PositiveMisses + m_blockHashLookup.NegativeMisses) > HASH_MISS_THRESHOLD || (m_fileHashLookup.PositiveMisses + m_fileHashLookup.NegativeMisses) > HASH_MISS_THRESHOLD))
                results.AddWarning(string.Format("Lookup tables gave false positives, this may indicate too small tables. Block: {0}, File: {0}", m_blockHashLookup.PositiveMisses + m_blockHashLookup.NegativeMisses, m_fileHashLookup.PositiveMisses + m_fileHashLookup.NegativeMisses), null);

            if (m_blockHashLookup != null && (m_blockHashLookup.PositiveMisses + m_blockHashLookup.NegativeMisses) > HASH_MISS_THRESHOLD && m_blockHashLookup.FullUsageRatio > FULL_HASH_USAGE_THRESHOLD)
                results.AddWarning(string.Format("Block hash lookup table is too small, usage is: {0:0.00}%. Adjust with --{1}", m_blockHashLookup.FullUsageRatio * 100, "blockhash-lookup-memory"), null);

            if (m_fileHashLookup != null && (m_fileHashLookup.PositiveMisses + m_fileHashLookup.NegativeMisses) > HASH_MISS_THRESHOLD && m_fileHashLookup.FullUsageRatio > FULL_HASH_USAGE_THRESHOLD)
                results.AddWarning(string.Format("File hash lookup table is too small, usage is: {0:0.00}%. Adjust with --{1}", m_fileHashLookup.FullUsageRatio * 100, "filehash-lookup-memory"), null);

            if (m_metadataLookup != null && (m_metadataLookup.PositiveMisses + m_metadataLookup.NegativeMisses) > HASH_MISS_THRESHOLD && m_metadataLookup.FullUsageRatio > FULL_HASH_USAGE_THRESHOLD)
                results.AddWarning(string.Format("Metadata hash lookup table is too small, usage is: {0:0.00}%. Adjust with --{1}", m_metadataLookup.FullUsageRatio * 100, "metadatahash-lookup-memory"), null);

            if (m_fileScantimeLookup != null && (m_fileScantimeLookup.PositiveMisses + m_fileScantimeLookup.NegativeMisses) > HASH_MISS_THRESHOLD && m_fileScantimeLookup.FullUsageRatio > FULL_HASH_USAGE_THRESHOLD)
                results.AddWarning(string.Format("File scantime lookup table is too small, usage is: {0:0.00}%. Adjust with --{1}", m_fileScantimeLookup.FullUsageRatio * 100, "filepath-lookup-memory"), null);

            if (m_filesetLookup != null && (m_filesetLookup.PositiveMisses + m_filesetLookup.NegativeMisses) > HASH_MISS_THRESHOLD && m_filesetLookup.FullUsageRatio > FULL_HASH_USAGE_THRESHOLD)
                results.AddWarning(string.Format("File id lookup table is too small, usage is: {0:0.00}%. Adjust with --{1}", m_filesetLookup.FullUsageRatio * 100, "filepath-lookup-memory"), null);
            
            // Good for profiling, but takes some time to calculate these stats
            if (Logging.Log.LogLevel == Logging.LogMessageType.Profiling)
            {
                if (m_blockHashLookup != null)
                {
                    Logging.Log.WriteMessage(string.Format("Prefix BlockHash Lookup entries {0}, usage: {1}, positive misses: {2}", m_blockHashLookup.PrefixBits, m_blockHashLookup.PrefixUsageRatio, m_blockHashLookup.PositiveMisses), Logging.LogMessageType.Profiling);
                    Logging.Log.WriteMessage(string.Format("Full BlockHash Lookup entries {0}, usage: {1}, negative misses: {2}", m_blockHashLookup.FullEntries, m_blockHashLookup.FullUsageRatio, m_blockHashLookup.NegativeMisses), Logging.LogMessageType.Profiling);
                }

                if (m_fileHashLookup != null)
                {
                    Logging.Log.WriteMessage(string.Format("Prefix FileHash Lookup entries {0}, usage: {1}, positive misses: {2}", m_fileHashLookup.PrefixBits, m_fileHashLookup.PrefixUsageRatio, m_fileHashLookup.PositiveMisses), Logging.LogMessageType.Profiling);
                    Logging.Log.WriteMessage(string.Format("Full FileHash Lookup entries {0}, usage: {1}, negative misses: {2}", m_fileHashLookup.FullEntries, m_fileHashLookup.FullUsageRatio, m_fileHashLookup.NegativeMisses), Logging.LogMessageType.Profiling);
                }

                if (m_metadataLookup != null)
                {
                    Logging.Log.WriteMessage(string.Format("Prefix Metadata Lookup entries {0}, usage: {1}, positive misses: {2}", m_metadataLookup.PrefixBits, m_metadataLookup.PrefixUsageRatio, m_metadataLookup.PositiveMisses), Logging.LogMessageType.Profiling);
                    Logging.Log.WriteMessage(string.Format("Full Metadata Lookup entries {0}, usage: {1}, negative misses: {2}", m_metadataLookup.FullEntries, m_metadataLookup.FullUsageRatio, m_metadataLookup.NegativeMisses), Logging.LogMessageType.Profiling);
                }

                if (m_fileScantimeLookup != null)
                {
                    Logging.Log.WriteMessage(string.Format("Prefix Scantime Lookup entries {0}, usage: {1}, positive misses: {2}", m_fileScantimeLookup.PrefixBits, m_fileScantimeLookup.PrefixUsageRatio, m_fileScantimeLookup.PositiveMisses), Logging.LogMessageType.Profiling);
                    Logging.Log.WriteMessage(string.Format("Full Scantime Lookup entries {0}, usage: {1}, negative misses: {2}", m_fileScantimeLookup.FullEntries, m_fileScantimeLookup.FullUsageRatio, m_fileScantimeLookup.NegativeMisses), Logging.LogMessageType.Profiling);
                }

                if (m_filesetLookup != null)
                {
                    Logging.Log.WriteMessage(string.Format("Prefix Fileset Lookup entries {0}, usage: {1}, positive misses: {2}", m_filesetLookup.PrefixBits, m_filesetLookup.PrefixUsageRatio, m_filesetLookup.PositiveMisses), Logging.LogMessageType.Profiling);
                    Logging.Log.WriteMessage(string.Format("Full Fileset Lookup entries {0}, usage: {1}, negative misses: {2}", m_filesetLookup.FullEntries, m_filesetLookup.FullUsageRatio, m_filesetLookup.NegativeMisses), Logging.LogMessageType.Profiling);
                }
            }
        }

        public void AppendFilesFromPreviousSet(System.Data.IDbTransaction transaction, IEnumerable<string> deleted)
        {
            AppendFilesFromPreviousSet(transaction, deleted, m_filesetId, OperationTimestamp);
        }

        public void AppendFilesFromPreviousSet(System.Data.IDbTransaction transaction, IEnumerable<string> deleted, long filesetid, DateTime timestamp)
        {
            using(var cmd = m_connection.CreateCommand())
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                long lastFilesetId = GetPreviousFilesetID(cmd, timestamp, filesetid);

                cmd.Transaction = tr.Parent;
                cmd.ExecuteNonQuery(@"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Scantime"") SELECT ? AS ""FilesetID"", ""FileID"", ""Scantime"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = ? AND ""FileID"" NOT IN (SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = ?) ", filesetid, lastFilesetId, filesetid);

                if (deleted != null)
                {
                    cmd.CommandText = @"DELETE FROM ""FilesetEntry"" WHERE ""FilesetID"" = ? AND ""FileID"" IN (SELECT ""ID"" FROM ""File"" WHERE ""Path"" = ?) ";
                    cmd.AddParameter(filesetid);
                    cmd.AddParameter();

                    foreach (string s in deleted)
                    {
                        cmd.SetParameterValue(1, s);
                        cmd.ExecuteNonQuery();
                    }
                }

                tr.Commit();
            }
        }
    
        /// <summary>
        /// Creates a timestamped backup operation to correctly associate the fileset with the time it was created.
        /// </summary>
        /// <param name="volumeid">The ID of the fileset volume to update</param>
        /// <param name="timestamp">The timestamp of the operation to create</param>
        /// <param name="transaction">An optional external transaction</param>
        public override long CreateFileset(long volumeid, DateTime timestamp, System.Data.IDbTransaction transaction = null)
        {
            return m_filesetId = base.CreateFileset(volumeid, timestamp, transaction);
        }
								
        public IEnumerable<KeyValuePair<long, DateTime>> GetIncompleteFilesets(System.Data.IDbTransaction transaction)
        {
            using(var cmd = m_connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                using(var rd = cmd.ExecuteReader(@"SELECT ""ID"", ""Timestamp"" FROM ""Fileset"" WHERE ""ID"" IN (SELECT ""FilesetID"" FROM ""FilesetEntry"") AND ""VolumeID"" NOT IN (SELECT ""ID"" FROM ""RemoteVolume"")"))
                    while(rd.Read())
                    {
                        yield return new KeyValuePair<long, DateTime>(
                            Convert.ToInt64(rd.GetValue(0)),
                            ParseFromEpochSeconds(Convert.ToInt64(rd.GetValue(1))).ToLocalTime()
                        );
                    }
            }
        }
        
        public void LinkFilesetToVolume(long filesetid, long volumeid, System.Data.IDbTransaction transaction)
        {
            using(var cmd = m_connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                var c = cmd.ExecuteNonQuery(@"UPDATE ""Fileset"" SET ""VolumeID"" = ? WHERE ""ID"" = ?", volumeid, filesetid);
                if (c != 1)
                    throw new Exception(string.Format("Failed to link filesetid {0} to volumeid {1}", filesetid, volumeid));
            }            
        }
    }
}
