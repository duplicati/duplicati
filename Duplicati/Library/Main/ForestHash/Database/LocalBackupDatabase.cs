using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;


namespace Duplicati.Library.Main.ForestHash.Database
{
    public class LocalBackupDatabase : Localdatabase
    {
        /// <summary>
        /// An approximate size of a hash-string in memory (44 chars * 2 for unicode + 8 bytes for pointer = 104)
        /// </summary>
        private const uint HASH_GUESS_SIZE = 128;
        
        /// <summary>
        ///An approximate size of a path string in memory.
        /// </summary>
        private const uint PATH_STRING_GUESS_SIZE = 1024;

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

        private readonly System.Data.IDbCommand m_createremotevolumeCommand;
        private readonly System.Data.IDbCommand m_insertfileOperationCommand;

        private HashDatabaseProtector<string> m_blockHashLookup;
        private HashDatabaseProtector<string, long> m_fileHashLookup;
        private HashDatabaseProtector<string, long> m_metadataLookup;
        private HashDatabaseProtector<string, KeyValuePair<long, DateTime>> m_fileScantimeLookup;
        private HashDatabaseProtector<Tuple<string, long,long>, long> m_filesetLookup;
        
        private long m_missingBlockHashes;
        private string m_scantimelookupTablename;

        public LocalBackupDatabase(string path, FhOptions options)
            : this(CreateConnection(path), options)
        {
        }

        public LocalBackupDatabase(LocalRestoredatabase connection, FhOptions options)
            : this(connection.Connection, options)
        {
        }

        public LocalBackupDatabase(System.Data.IDbConnection connection, FhOptions options)
            : base(connection, "Backup")
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
            m_createremotevolumeCommand = m_connection.CreateCommand();
            m_insertblocklistHashesCommand = m_connection.CreateCommand();
            m_selectblocklistHashesCommand = m_connection.CreateCommand();
            m_insertfileOperationCommand = m_connection.CreateCommand();
            m_selectfileSimpleCommand = m_connection.CreateCommand();
            m_selectfileHashCommand = m_connection.CreateCommand();
            m_findmetadatasetProbeCommand = m_connection.CreateCommand();

            m_findblockCommand.CommandText = @"SELECT ""File"" FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ?";
            m_findblockCommand.AddParameters(2);

            m_findblocksetCommand.CommandText = @"SELECT ""ID"" FROM ""Blockset"" WHERE ""Fullhash"" = ? AND ""Length"" = ?";
            m_findblocksetCommand.AddParameters(2);

            m_findmetadatasetProbeCommand.CommandText = @"SELECT ""ID"" FROM ""Blockset"" WHERE ""Hash"" = ? AND ""Size"" = ? LIMIT 1";
            m_findmetadatasetProbeCommand.AddParameters(2);

            m_findmetadatasetCommand.CommandText = @"SELECT ""A"".""ID"" FROM ""Metadataset"" A, ""BlocksetEntry"" B, ""Block"" C WHERE ""A"".""BlocksetID"" = ""B"".""BlocksetID"" AND ""B"".""BlocksetID"" = ""C"".""ID"" AND ""C"".""Hash"" = ? AND ""C"".""Size"" = ? LIMIT 1";
            m_findmetadatasetCommand.AddParameters(2);

            m_findfilesetCommand.CommandText = @"SELECT ""ID"" FROM ""Fileset"" WHERE ""BlocksetID"" = ? AND ""MetadatasetID"" = ? AND ""Path"" = ?";
            m_findfilesetCommand.AddParameters(3);

            m_insertblockCommand.CommandText = @"INSERT INTO ""Block"" (""Hash"", ""File"", ""Size"") VALUES (?, ?, ?)";
            m_insertblockCommand.AddParameters(3);

            m_insertfileOperationCommand.CommandText = @"INSERT INTO ""OperationFileset"" (""OperationID"", ""FilesetID"", ""Scantime"") VALUES (?, ?, ?)";
            m_insertfileOperationCommand.AddParameter(m_operationid);
            m_insertfileOperationCommand.AddParameters(2);

            m_insertfileCommand.CommandText = @"INSERT INTO ""Fileset"" (""Path"",""BlocksetID"", ""MetadataID"") VALUES (?, ? ,?); SELECT last_insert_rowid();";
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
            m_scantimelookupTablename = "ScanTime-" + Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
            using (var cmd = m_connection.CreateCommand())
                cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS SELECT ""OperationFileset"".""FilesetID"" AS ""FilesetID"", MAX(""OperationFileset"".""Scantime"") AS ""Scantime"", ""Fileset"".""Path"" AS ""Path"" FROM ""OperationFileset"" INNER JOIN ""Fileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" GROUP BY ""FilesetID"", ""Path"" ", m_scantimelookupTablename));

            m_selectfileSimpleCommand.CommandText = string.Format(@"SELECT ""FilesetID"", ""Scantime"" FROM ""{0}"" WHERE ""Path"" = ?", m_scantimelookupTablename);
            m_selectfileSimpleCommand.AddParameters(1);

            m_selectfileHashCommand.CommandText = @"SELECT ""Blockset"".""Fullhash"" FROM ""Blockset"" INNER JOIN ""Fileset"" ON ""Blockset"".""ID"" = ""Fileset"".""BlocksetID"" WHERE ""Fileset"".""ID"" = ?  ";
            m_selectfileHashCommand.AddParameters(1);

            m_selectblocklistHashesCommand.CommandText = @"SELECT ""Hash"" FROM ""BlocklistHash"" WHERE ""BlocksetID"" = ? ORDER BY ""Index"" ASC ";
            m_selectblocklistHashesCommand.AddParameters(1);

            m_findremotevolumestateCommand.CommandText = @"SELECT ""State"" FROM ""Remotevolume"" WHERE ""Name"" = ?";
            m_findremotevolumestateCommand.AddParameters(1);

            m_updateblockCommand.CommandText = @"UPDATE ""Block"" SET ""File"" = ? WHERE ""Hash"" = ? AND ""Size"" = ? ";
            m_updateblockCommand.AddParameters(3);

            m_createremotevolumeCommand.CommandText = @"INSERT INTO ""Remotevolume"" (""OperationID"", ""Name"", ""Type"", ""State"") VALUES (?, ?, ?, ?)";
            m_createremotevolumeCommand.AddParameter(m_operationid);
            m_createremotevolumeCommand.AddParameters(3);

            if (options.FhBlockHashSize > 0)
                m_blockHashLookup = new HashDatabaseProtector<string>(HASH_GUESS_SIZE, (ulong)options.FhBlockHashSize);            
            if (options.FhFileHashSize > 0)
                m_fileHashLookup = new HashDatabaseProtector<string, long>(HASH_GUESS_SIZE, (ulong)options.FhFileHashSize);
            if (options.FhMetadataHashSize > 0)
                m_metadataLookup = new HashDatabaseProtector<string, long>(HASH_GUESS_SIZE, (ulong)options.FhMetadataHashSize);

            if (options.FhFilePathSize > 0)
            {
                m_fileScantimeLookup = new HashDatabaseProtector<string, KeyValuePair<long, DateTime>>(PATH_STRING_GUESS_SIZE, (ulong)options.FhFilePathSize / 2);
                m_filesetLookup = new HashDatabaseProtector<Tuple<string, long, long>, long>(PATH_STRING_GUESS_SIZE, (ulong)options.FhFilePathSize / 2);
            }

            //Populate the lookup tables
            using (var cmd = m_connection.CreateCommand())
            {
                if (m_blockHashLookup != null)
                    using (var rd = cmd.ExecuteReader(@"SELECT DISTINCT ""Block"".""Hash"" FROM ""Block"", ""RemoteVolume"" WHERE ""RemoteVolume"".""Name"" = ""Block"".""File"" AND ""RemoteVolume"".""State"" IN (?,?,?,?) ", RemoteVolumeState.Temporary.ToString(), RemoteVolumeState.Uploading.ToString(), RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString()))
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
                    using (var rd = cmd.ExecuteReader(@"SELECT ""A"".""ID"", ""C"".""Hash"" FROM ""Metadataset"" A, ""BlocksetEntry"" B, ""Block"" C WHERE ""A"".""BlocksetID"" = ""B"".""BlocksetID"" AND ""B"".""BlocksetID"" = ""C"".""ID"" "))
                        while (rd.Read())
                        {
                            var metadataid = Convert.ToInt64(rd.GetValue(0));
                            var hash = rd.GetValue(1).ToString();
                            var hashdata = HashPrefixLookup.DecodeBase64Hash(hash);
                            m_metadataLookup.Add(hashdata, hash, metadataid);
                        }

                if (m_fileScantimeLookup != null)
                    using (var rd = cmd.ExecuteReader(string.Format(@" SELECT ""FilesetID"", ""Scantime"", ""Path"" FROM ""{0}"" ", m_scantimelookupTablename)))
                        while (rd.Read())
                        {
                            var id = Convert.ToInt64(rd.GetValue(0));
                            var scantime = Convert.ToDateTime(rd.GetValue(1));
                            var path = rd.GetValue(2).ToString();
                            m_fileScantimeLookup.Add((ulong)path.GetHashCode(), path, new KeyValuePair<long, DateTime>(id, scantime));
                        }

                if (m_filesetLookup != null)
                    using (var rd = cmd.ExecuteReader(string.Format(@" SELECT ""Path"", ""BlocksetID"", ""MetadataID"", ""ID"" FROM ""Fileset"" ")))
                        while (rd.Read())
                        {
                            var path = rd.GetValue(0).ToString();
                            var blocksetid = Convert.ToInt64(rd.GetValue(1));
                            var metadataid = Convert.ToInt64(rd.GetValue(2));
                            var filesetid = Convert.ToInt64(rd.GetValue(3));
                            m_filesetLookup.Add((ulong)blocksetid ^ (ulong)metadataid ^ (ulong)path.GetHashCode(), new Tuple<string, long, long>(path, blocksetid, metadataid), filesetid);
                        }
                                                        
                m_missingBlockHashes = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT (*) FROM (SELECT DISTINCT ""Block"".""Hash"", ""Block"".""Size"" FROM ""Block"", ""RemoteVolume"" WHERE ""RemoteVolume"".""Name"" = ""Block"".""File"" AND ""RemoteVolume"".""State"" NOT IN (?,?,?,?))", RemoteVolumeState.Temporary.ToString(), RemoteVolumeState.Uploading.ToString(), RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString()));
            }

        }

        /// <summary>
        /// Adds a block to the local database, returning a value indicating if the value presents a new block
        /// </summary>
        /// <param name="key">The block key</param>
        /// <param name="archivename">The name of the archive that holds the data</param>
        /// <returns>True if the block should be added to the current output</returns>
        public bool AddBlock (string key, long size, string archivename, System.Data.IDbTransaction transaction = null)
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
                        r = m_findblockCommand.ExecuteScalar(null, key, size);
                        if (r == null || r == DBNull.Value) {
                            m_blockHashLookup.FalsePositives++;
                        } else {
                            m_blockHashLookup.FalseNegatives++;
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
                r = m_findblockCommand.ExecuteScalar(null, key, size);
            }

            if (r == null || r == DBNull.Value)
            {
                m_insertblockCommand.Transaction = transaction;
                m_insertblockCommand.SetParameterValue(0, key);
                m_insertblockCommand.SetParameterValue(1, archivename);
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
                    m_updateblockCommand.ExecuteNonQuery(null, archivename, key, size);
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
                            m_fileHashLookup.FalsePositives++;
                        else
                        {
                            m_fileHashLookup.FalseNegatives++;
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
            }
                
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                m_insertblocksetCommand.Transaction = tr.Parent;
                blocksetid = Convert.ToInt64(m_insertblocksetCommand.ExecuteScalar(null, size, filehash));
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
                                m_findmetadatasetCommand.ExecuteScalar(null, hash, size);
                                if (r != null && r != DBNull.Value)
                                {
                                    m_metadataLookup.FalseNegatives++;
                                    //Update the lookup table, MRU style
                                    metadataid = Convert.ToInt64(r);
                                    m_metadataLookup.Add(hashdata, hash, metadataid);
                                    return false;
                                }
                            }
                            
                            if (r == null || r == DBNull.Value)
                                m_metadataLookup.FalsePositives++;
                            else
                                m_metadataLookup.FalseNegatives++;
                                
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
                    metadataid = Convert.ToInt64(m_insertmetadatasetCommand.ExecuteScalar(null, blocksetid));
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
        public void AddFile(string filename, DateTime scantime, long blocksetID, long metadataID, System.Data.IDbTransaction transaction = null)
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
                        fileidobj = m_findfilesetCommand.ExecuteScalar(null, blocksetID, metadataID, filename);
                        if (fileidobj == null || fileidobj == DBNull.Value)
                            m_filesetLookup.FalsePositives++;
                        else
                            m_filesetLookup.FalseNegatives++;
                        break;
                }
            }
            else
            {
                m_findfilesetCommand.Transaction = transaction;
                fileidobj = m_findfilesetCommand.ExecuteScalar(null, blocksetID, metadataID, filename);
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
            m_insertfileOperationCommand.SetParameterValue(1, fileidobj);
            m_insertfileOperationCommand.SetParameterValue(2, scantime);
            m_insertfileOperationCommand.ExecuteNonQuery();

        }

        public void AddUnmodifiedFile(long fileid, DateTime scantime, System.Data.IDbTransaction transaction = null)
        {
            m_insertfileOperationCommand.Transaction = transaction;
            m_insertfileOperationCommand.SetParameterValue(1, fileid);
            m_insertfileOperationCommand.SetParameterValue(2, scantime);
            m_insertfileOperationCommand.ExecuteNonQuery();
        }

        public void AddDirectoryEntry(string path, long metadataID, DateTime scantime, System.Data.IDbTransaction transaction = null)
        {
            AddFile(path, scantime, FOLDER_BLOCKSET_ID, metadataID, transaction);
        }

        public void RegisterRemoteVolume(string name, RemoteVolumeType type, RemoteVolumeState state)
        {
            lock (m_lock)
            {
                m_createremotevolumeCommand.SetParameterValue(1, name);
                m_createremotevolumeCommand.SetParameterValue(2, type.ToString());
                m_createremotevolumeCommand.SetParameterValue(3, state.ToString());
                m_createremotevolumeCommand.ExecuteNonQuery();
            }
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
            
            using(var rd = m_selectfileSimpleCommand.ExecuteReader(null, path))
                if (rd.Read())
                {
                    if (m_fileScantimeLookup != null)
                        m_fileScantimeLookup.FalseNegatives++;                    

                    oldScanned =  Convert.ToDateTime(rd.GetValue(1));
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
                        m_fileScantimeLookup.FalsePositives++;

                    oldScanned = DateTime.UtcNow;
                    return -1;
                }

        }

        public string GetFileHash(long fileid)
        {
            m_selectfileHashCommand.SetParameterValue(0, fileid);
            return m_selectfileHashCommand.ExecuteScalar().ToString();
            
        }

        private class BlocklistHashEnumerable : IEnumerable<string>
        {
            private class BlocklistHashEnumerator : IEnumerator<string>
            {
                private System.Data.IDataReader m_reader;
                private BlocklistHashEnumerable m_parent;
                private string m_path = null;
                private bool m_first = true;
                private string m_current = null;

                public BlocklistHashEnumerator(BlocklistHashEnumerable parent, System.Data.IDataReader reader)
                {
                    m_reader = reader;
                    m_parent = parent;
                }

                public string Current { get{ return m_current; } }

                public void Dispose()
                {
                }

                object System.Collections.IEnumerator.Current { get { return this.Current; } }

                public bool MoveNext()
                {
                    m_first = false;

                    if (m_path == null)
                    {
                        m_path = m_reader.GetValue(0).ToString();
                        m_current = m_reader.GetValue(6).ToString();
                        return true;
                    }
                    else
                    {
                        if (m_current == null)
                            return false;

                        if (!m_reader.Read())
                        {
                            m_current = null;
                            m_parent.MoreData = false;
                            return false;
                        }

                        var np = m_reader.GetValue(0).ToString();
                        if (m_path != np)
                        {
                            m_current = null;
                            return false;
                        }

                        m_current = m_reader.GetValue(6).ToString();
                        return true;
                    }
                }

                public void Reset()
                {
                    if (!m_first)
                        throw new Exception("Iterator reset not supported");

                    m_first = false;
                }
            }

            private System.Data.IDataReader m_reader;

            public BlocklistHashEnumerable(System.Data.IDataReader reader)
            {
                m_reader = reader;
                this.MoreData = true;
            }

            public bool MoreData { get; protected set; }

            public IEnumerator<string> GetEnumerator()
            {
                return new BlocklistHashEnumerator(this, m_reader);
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return this.GetEnumerator();
            }
        }

        public void WriteFileset(Volumes.FilesetVolumeWriter filesetvolume)
        {
            using (var cmd = m_connection.CreateCommand())
            {
                //cmd.CommandText = @"SELECT ""A"".""ID"", ""D"".""Scantime"", ""B"".""Length"", ""B"".""FullHash"", ""E"".""FullHash"", ""E"".""Length"", ""B"".""ID"" FROM ""Fileset"" A, ""Blockset"" B, ""Metadataset"" C, ""OperationFileset"" D, ""Blockset"" E, ""Operation"" F WHERE ""A"".""ID"" = ""D"".""FilesetID"" AND ""F"".""ID"" = ? AND ""A"".""BlocksetID"" = ""B"".""ID"" AND ""A"".""MetadataID"" = ""C"".""ID"" AND ""E"".""ID"" = ""C"".""BlocksetID"" AND ""F"".""ID"" = ""D"".""OperationID"" ORDER BY ""F"".""Timestamp"" DESC ";
                cmd.CommandText = @"SELECT ""B"".""BlocksetID"", ""B"".""ID"", ""B"".""Path"", ""D"".""Length"", ""D"".""FullHash"", ""A"".""Scantime"" FROM ""OperationFileset"" A, ""Fileset"" B, ""Metadataset"" C, ""Blockset"" D WHERE ""A"".""FilesetID"" = ""B"".""ID"" AND ""B"".""MetadataID"" = ""C"".""ID"" AND ""C"".""BlocksetID"" = ""D"".""ID"" AND (""B"".""BlocksetID"" = ? OR ""B"".""BlocksetID"" = ?) AND ""A"".""OperationID"" = ? ";
                cmd.AddParameter(FOLDER_BLOCKSET_ID);
                cmd.AddParameter(SYMLINK_BLOCKSET_ID);
                cmd.AddParameter(m_operationid);

                using (var rd = cmd.ExecuteReader())
                while(rd.Read())
                {
                    var blocksetID = Convert.ToInt64(rd.GetValue(0));
                    var path = rd.GetValue(2).ToString();
                    var metalength = Convert.ToInt64(rd.GetValue(3));
                    var metahash = rd.GetValue(4).ToString();

                    if (blocksetID == FOLDER_BLOCKSET_ID)
                        filesetvolume.AddDirectory(path, metahash, metalength);
                    else if (blocksetID == SYMLINK_BLOCKSET_ID)
                        filesetvolume.AddSymlink(path, metahash, metalength);
                }

                cmd.CommandText = @"SELECT ""F"".""Path"", ""F"".""Scantime"", ""F"".""Filelength"", ""F"".""Filehash"", ""F"".""Metahash"", ""F"".""Metalength"", ""G"".""Hash"" FROM (SELECT ""A"".""Path"" AS ""Path"", ""D"".""Scantime"" AS ""Scantime"", ""B"".""Length"" AS ""Filelength"", ""B"".""FullHash"" AS ""Filehash"", ""E"".""FullHash"" AS ""Metahash"", ""E"".""Length"" AS ""Metalength"", ""A"".""BlocksetID"" AS ""BlocksetID"" FROM ""Fileset"" A, ""Blockset"" B, ""Metadataset"" C, ""OperationFileset"" D, ""Blockset"" E WHERE ""A"".""ID"" = ""D"".""FilesetID"" AND ""D"".""OperationID"" = ? AND ""A"".""BlocksetID"" = ""B"".""ID"" AND ""A"".""MetadataID"" = ""C"".""ID"" AND ""E"".""ID"" = ""C"".""BlocksetID"") F LEFT OUTER JOIN ""BlocklistHash"" G ON ""G"".""BlocksetID"" = ""F"".""BlocksetID"" ORDER BY ""F"".""Path"", ""G"".""Index"" ";
                cmd.Parameters.Clear();
                cmd.AddParameter(m_operationid);

                using (var rd = cmd.ExecuteReader())
                if (rd.Read())
                {
                    var more = false;
                    do
                    {
                        var path = rd.GetValue(0).ToString();
                        var filehash = rd.GetValue(3).ToString();
                        var size = Convert.ToInt64(rd.GetValue(2));
                        var scantime = Convert.ToDateTime(rd.GetValue(1));
                        var metahash = rd.GetValue(4).ToString();
                        var metasize = Convert.ToInt64(rd.GetValue(5));
                        var blrd = new BlocklistHashEnumerable(rd);

                        filesetvolume.AddFile(path, filehash, size, scantime, metahash, metasize, blrd);
                        more = blrd.MoreData;

                    } while (more);
                }
            }
        }

        public void VerifyConsistency()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                var c = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM (SELECT SUM(""Block"".""Size"") AS ""CalcLen"", ""Blockset"".""Length"" AS ""Length"", ""BlocksetEntry"".""BlocksetID"" FROM ""Block"", ""BlocksetEntry"", ""Blockset"" WHERE ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" AND ""Blockset"".""ID"" = ""BlocksetEntry"".""BlocksetID"" GROUP BY ""BlocksetEntry"".""BlocksetID"") WHERE ""CalcLen"" != ""Length"""));
                if (c != 0)
                    throw new InvalidDataException("Inconsistency detected, not all blocklists were restored correctly");
            }
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

        internal void UpdateChangeStatistics (BackupStatistics m_stat)
        {
            using (var cmd = m_connection.CreateCommand()) 
            {
                long lastOperationId = -1;

                var lastIdObj = cmd.ExecuteScalar(@"SELECT ""ID"" FROM ""Operation"" WHERE ""Timestamp"" < ? AND ""Description"" = ? ORDER BY ""Timestamp"" DESC ", OperationTimestamp, "Backup");
                if (lastIdObj != null && lastIdObj != DBNull.Value)
                    lastOperationId = Convert.ToInt64(lastIdObj);

                m_stat.AddedFolders = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ? AND ""Fileset"".""BlocksetID"" = ? AND NOT ""Fileset"".""Path"" IN (SELECT ""Path"" FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ?)", m_operationid, FOLDER_BLOCKSET_ID, lastOperationId));
                m_stat.AddedSymlinks = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ? AND ""Fileset"".""BlocksetID"" = ? AND NOT ""Fileset"".""Path"" IN (SELECT ""Path"" FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ?)", m_operationid, SYMLINK_BLOCKSET_ID, lastOperationId));
                m_stat.AddedFiles = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ? AND ""Fileset"".""BlocksetID"" != ? AND ""Fileset"".""BlocksetID"" != ? AND NOT ""Fileset"".""Path"" IN (SELECT ""Path"" FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ?)", m_operationid, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID, lastOperationId));

                m_stat.DeletedFolders = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ? AND ""Fileset"".""BlocksetID"" = ? AND NOT ""Fileset"".""Path"" IN (SELECT ""Path"" FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ?)", lastOperationId, FOLDER_BLOCKSET_ID, m_operationid));
                m_stat.DeletedSymlinks = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ? AND ""Fileset"".""BlocksetID"" = ? AND NOT ""Fileset"".""Path"" IN (SELECT ""Path"" FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ?)", lastOperationId, SYMLINK_BLOCKSET_ID, m_operationid));
                m_stat.DeletedFiles = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ? AND ""Fileset"".""BlocksetID"" != ? AND ""Fileset"".""BlocksetID"" != ? AND NOT ""Fileset"".""Path"" IN (SELECT ""Path"" FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ?)", lastOperationId, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID, m_operationid));

                m_stat.ModifiedFolders = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ? AND ""Fileset"".""BlocksetID"" = ? AND ""Fileset"".""Path"" IN (SELECT ""Path"" FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ?)", lastOperationId, FOLDER_BLOCKSET_ID, m_operationid));
                m_stat.ModifiedSymlinks = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ? AND ""Fileset"".""BlocksetID"" = ? AND ""Fileset"".""Path"" IN (SELECT ""Path"" FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ?)", lastOperationId, SYMLINK_BLOCKSET_ID, m_operationid));
                m_stat.ModifiedFiles = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ? AND ""Fileset"".""BlocksetID"" != ? AND ""Fileset"".""BlocksetID"" != ? AND ""Fileset"".""Path"" IN (SELECT ""Path"" FROM ""Fileset"" INNER JOIN ""OperationFileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""OperationFileset"".""OperationID"" = ?)", lastOperationId, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID, m_operationid));
            }

            if (m_blockHashLookup != null && m_fileHashLookup != null && (m_blockHashLookup.FalsePositives > 200 || m_fileHashLookup.FalsePositives > 20))
                    m_stat.LogWarning(string.Format("Lookup tables gave false positives, this may indicate too small tables. Block: {0}, File: {0}", m_blockHashLookup.FalsePositives, m_fileHashLookup.FalsePositives), null);

            if (m_blockHashLookup != null && m_blockHashLookup.FalsePositives > 200 && m_blockHashLookup.PrefixUsageRatio > 0.5)
                m_stat.LogWarning(string.Format("Block hash lookup table is too small, usage is: {0}%", m_blockHashLookup.PrefixUsageRatio * 100), null);

            if (m_fileHashLookup != null && m_fileHashLookup.FalsePositives > 20 && m_fileHashLookup.PrefixUsageRatio > 0.5)
                m_stat.LogWarning(string.Format("File hash lookup table is too small, usage is: {0}%", m_fileHashLookup.PrefixUsageRatio * 100), null);

            if (m_metadataLookup != null && m_metadataLookup.FalsePositives > 200 && m_metadataLookup.PrefixUsageRatio > 0.5)
                m_stat.LogWarning(string.Format("Metadata hash lookup table is too small, usage is: {0}%", m_metadataLookup.PrefixUsageRatio * 100), null);

            if (m_fileScantimeLookup != null && m_fileScantimeLookup.FalsePositives > 20 && m_fileScantimeLookup.PrefixUsageRatio > 0.5)
                m_stat.LogWarning(string.Format("File scantime lookup table is too small, usage is: {0}%", m_fileScantimeLookup.PrefixUsageRatio * 100), null);

            if (m_filesetLookup != null && m_filesetLookup.FalsePositives > 20 && m_filesetLookup.PrefixUsageRatio > 0.5)
                m_stat.LogWarning(string.Format("File scantime lookup table is too small, usage is: {0}%", m_filesetLookup.PrefixUsageRatio * 100), null);
            
            // Good for profiling, but takes some time to calculate these stats
            if (Logging.Log.LogLevel == Logging.LogMessageType.Profiling)
            {
                if (m_blockHashLookup != null)
                {
                    Logging.Log.WriteMessage(string.Format("Prefix BlockHash Lookup entries {0}, usage: {1}, falsePositives: {2}", m_blockHashLookup.PrefixBits, m_blockHashLookup.PrefixUsageRatio, m_blockHashLookup.FalsePositives), Logging.LogMessageType.Profiling);
                    Logging.Log.WriteMessage(string.Format("Full BlockHash Lookup entries {0}, usage: {1}, falseNegatives: {2}", m_blockHashLookup.FullEntries, m_blockHashLookup.FullUsageRatio, m_blockHashLookup.FalseNegatives), Logging.LogMessageType.Profiling);
                }

                if (m_fileHashLookup != null)
                {
                    Logging.Log.WriteMessage(string.Format("Prefix FileHash Lookup entries {0}, usage: {1}, falsePositives: {2}", m_fileHashLookup.PrefixBits, m_fileHashLookup.PrefixUsageRatio, m_fileHashLookup.FalsePositives), Logging.LogMessageType.Profiling);
                    Logging.Log.WriteMessage(string.Format("Full FileHash Lookup entries {0}, usage: {1}, falseNegatives: {2}", m_fileHashLookup.FullEntries, m_fileHashLookup.FullUsageRatio, m_fileHashLookup.FalseNegatives), Logging.LogMessageType.Profiling);
                }

                if (m_metadataLookup != null)
                {
                    Logging.Log.WriteMessage(string.Format("Prefix Metadata Lookup entries {0}, usage: {1}, falsePositives: {2}", m_metadataLookup.FullEntries, m_metadataLookup.FullUsageRatio, m_metadataLookup.FalsePositives), Logging.LogMessageType.Profiling);
                    Logging.Log.WriteMessage(string.Format("Full Metadata Lookup entries {0}, usage: {1}, falseNegatives: {2}", m_metadataLookup.FullEntries, m_metadataLookup.FullUsageRatio, m_metadataLookup.FalseNegatives), Logging.LogMessageType.Profiling);
                }

                if (m_fileScantimeLookup != null)
                {
                    Logging.Log.WriteMessage(string.Format("Prefix Scantime Lookup entries {0}, usage: {1}, falsePositives: {2}", m_fileScantimeLookup.FullEntries, m_fileScantimeLookup.FullUsageRatio, m_fileScantimeLookup.FalsePositives), Logging.LogMessageType.Profiling);
                    Logging.Log.WriteMessage(string.Format("Full Scantime Lookup entries {0}, usage: {1}, falseNegatives: {2}", m_fileScantimeLookup.FullEntries, m_fileScantimeLookup.FullUsageRatio, m_fileScantimeLookup.FalseNegatives), Logging.LogMessageType.Profiling);
                }

                if (m_filesetLookup != null)
                {
                    Logging.Log.WriteMessage(string.Format("Prefix Fileset Lookup entries {0}, usage: {1}, falsePositives: {2}", m_filesetLookup.FullEntries, m_filesetLookup.FullUsageRatio, m_filesetLookup.FalsePositives), Logging.LogMessageType.Profiling);
                    Logging.Log.WriteMessage(string.Format("Full Fileset Lookup entries {0}, usage: {1}, falseNegatives: {2}", m_filesetLookup.FullEntries, m_filesetLookup.FullUsageRatio, m_filesetLookup.FalseNegatives), Logging.LogMessageType.Profiling);
                }
            }
        }


        public void AppendFilesFromPreviousSet(System.Data.IDbTransaction transaction, IEnumerable<string> deleted)
        {
            using(var cmd = m_connection.CreateCommand())
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                long lastOperationId = -1;

                var lastIdObj = cmd.ExecuteScalar(@"SELECT ""ID"" FROM ""Operation"" WHERE ""Timestamp"" < ? AND ""Description"" = ? ORDER BY ""Timestamp"" DESC ", OperationTimestamp, "Backup");
                if (lastIdObj != null && lastIdObj != DBNull.Value)
                    lastOperationId = Convert.ToInt64(lastIdObj);

                cmd.Transaction = tr.Parent;
                cmd.ExecuteNonQuery(@"INSERT INTO ""OperationFileset"" (""OperationID"", ""FilesetID"", ""Scantime"") SELECT ? AS ""OperationID"", ""FilesetID"", ""Scantime"" FROM ""OperationFile"" WHERE ""OperationID"" = ? AND ""FilesetID"" NOT IN (SELECT ""FilesetID"" FROM ""OperationFileset"" WHERE ""OperationID"" = ?) ", m_operationid, lastOperationId, m_operationid);

                if (deleted != null)
                {
                    cmd.CommandText = @"DELETE FROM ""OperationFileset"" WHERE ""OperationID"" = ? AND ""FilesetID"" IN (SELECT ""ID"" FROM ""Fileset"" WHERE ""Path"" = ?) ";
                    cmd.AddParameter(m_operationid);
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
    }
}
