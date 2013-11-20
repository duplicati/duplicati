using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;


namespace Duplicati.Library.Main.Database
{
    internal class LocalBackupDatabase : LocalDatabase
    {
        private class PathEntryKeeper
        {
            public DateTime ScanTime;
            public long FileID;
            
            private SortedList<KeyValuePair<long, long>, long> m_versions;
            
            public PathEntryKeeper(long fileId, DateTime scanTime)
            {
                this.FileID = fileId;
                this.ScanTime = scanTime;
                this.m_versions = null;
            }
            
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
    
        private readonly System.Data.IDbCommand m_findblockCommand;
        private readonly System.Data.IDbCommand m_findblocksetCommand;
        private readonly System.Data.IDbCommand m_findfilesetCommand;
        private readonly System.Data.IDbCommand m_findmetadatasetCommand;
        private readonly System.Data.IDbCommand m_findmetadatasetProbeCommand;

        private readonly System.Data.IDbCommand m_insertblockCommand;

        private readonly System.Data.IDbCommand m_insertfileCommand;

        private readonly System.Data.IDbCommand m_insertblocksetCommand;
        private readonly System.Data.IDbCommand m_insertblocksetentryFastCommand;
        private readonly System.Data.IDbCommand m_insertblocksetentryCommand;
        private readonly System.Data.IDbCommand m_insertblocklistHashesCommand;

        private readonly System.Data.IDbCommand m_insertmetadatasetCommand;

        private readonly System.Data.IDbCommand m_selectfileSimpleCommand;
        private readonly System.Data.IDbCommand m_selectfileHashCommand;
        private readonly System.Data.IDbCommand m_selectblocklistHashesCommand;

        private readonly System.Data.IDbCommand m_findremotevolumestateCommand;
        private readonly System.Data.IDbCommand m_updateblockCommand;

        private readonly System.Data.IDbCommand m_insertfileOperationCommand;
		
		private HashLookupHelper<KeyValuePair<long, long>> m_blockHashLookup;
        private HashLookupHelper<long> m_fileHashLookup;
        private HashLookupHelper<long> m_metadataLookup;
        private PathLookupHelper<PathEntryKeeper> m_pathLookup;
        
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
            m_insertblocksetentryFastCommand = m_connection.CreateCommand();
				
			m_findblockCommand.CommandText = @"SELECT ""ID"" FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ?";
            m_findblockCommand.AddParameters(2);

            m_findblocksetCommand.CommandText = @"SELECT ""ID"" FROM ""Blockset"" WHERE ""Fullhash"" = ? AND ""Length"" = ?";
            m_findblocksetCommand.AddParameters(2);

            m_findmetadatasetProbeCommand.CommandText = @"SELECT ""ID"" FROM ""Blockset"" WHERE ""Hash"" = ? AND ""Size"" = ? LIMIT 1";
            m_findmetadatasetProbeCommand.AddParameters(2);

            m_findmetadatasetCommand.CommandText = @"SELECT ""A"".""ID"" FROM ""Metadataset"" A, ""BlocksetEntry"" B, ""Block"" C WHERE ""A"".""BlocksetID"" = ""B"".""BlocksetID"" AND ""B"".""BlocksetID"" = ""C"".""ID"" AND ""C"".""Hash"" = ? AND ""C"".""Size"" = ? LIMIT 1";
            m_findmetadatasetCommand.AddParameters(2);

            m_findfilesetCommand.CommandText = @"SELECT ""ID"" FROM ""File"" WHERE ""BlocksetID"" = ? AND ""MetadataID"" = ? AND ""Path"" = ?";
            m_findfilesetCommand.AddParameters(3);

            m_insertblockCommand.CommandText = @"INSERT INTO ""Block"" (""Hash"", ""VolumeID"", ""Size"") VALUES (?, ?, ?); SELECT last_insert_rowid();";
            m_insertblockCommand.AddParameters(3);

            m_insertfileOperationCommand.CommandText = @"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Scantime"") VALUES (?, ?, ?)";
            m_insertfileOperationCommand.AddParameters(3);

            m_insertfileCommand.CommandText = @"INSERT INTO ""File"" (""Path"",""BlocksetID"", ""MetadataID"") VALUES (?, ? ,?); SELECT last_insert_rowid();";
            m_insertfileCommand.AddParameters(3);

            m_insertblocksetCommand.CommandText = @"INSERT INTO ""Blockset"" (""Length"", ""FullHash"") VALUES (?, ?); SELECT last_insert_rowid();";
            m_insertblocksetCommand.AddParameters(2);

            m_insertblocksetentryFastCommand.CommandText = @"INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") VALUES (?,?,?)";
            m_insertblocksetentryFastCommand.AddParameters(3);

            m_insertblocksetentryCommand.CommandText = @"INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") SELECT ? AS A, ? AS B, ""ID"" FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ?";
            m_insertblocksetentryCommand.AddParameters(4);

            m_insertblocklistHashesCommand.CommandText = @"INSERT INTO ""BlocklistHash"" (""BlocksetID"", ""Index"", ""Hash"") VALUES (?, ?, ?)";
            m_insertblocklistHashesCommand.AddParameters(3);

            m_insertmetadatasetCommand.CommandText = @"INSERT INTO ""Metadataset"" (""BlocksetID"") VALUES (?); SELECT last_insert_rowid();";
            m_insertmetadatasetCommand.AddParameter();

            //Need a temporary table with path/scantime lookups
            m_scantimelookupTablename = "ScanTime-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
            var scantableDefinition = @"SELECT ""A"".""FileID"" AS ""FileID"", ""A"".""Scantime"" AS ""Scantime"", ""File"".""Path"" AS ""Path"" FROM (SELECT ""FilesetEntry"".""FileID"" AS ""FileID"", MAX(""FilesetEntry"".""Scantime"") AS ""Scantime"" FROM ""FilesetEntry"" GROUP BY ""FilesetEntry"".""FileID"") A, ""File"" WHERE ""File"".""ID"" = ""A"".""FileID""";
            using (var cmd = m_connection.CreateCommand())
                cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS " + scantableDefinition, m_scantimelookupTablename));

            m_selectfileSimpleCommand.CommandText = string.Format(@"SELECT ""FileID"", ""Scantime"" FROM ""{0}"" WHERE ""BlocksetID"" >= 0 AND ""Path"" = ?", m_scantimelookupTablename);
            m_selectfileSimpleCommand.AddParameters(1);

            m_selectfileHashCommand.CommandText = @"SELECT ""Blockset"".""Fullhash"" FROM ""Blockset"", ""File"" WHERE ""Blockset"".""ID"" = ""File"".""BlocksetID"" AND ""File"".""ID"" = ?  ";
            m_selectfileHashCommand.AddParameters(1);

            m_selectblocklistHashesCommand.CommandText = @"SELECT ""Hash"" FROM ""BlocklistHash"" WHERE ""BlocksetID"" = ? ORDER BY ""Index"" ASC ";
            m_selectblocklistHashesCommand.AddParameters(1);

            m_findremotevolumestateCommand.CommandText = @"SELECT ""State"" FROM ""Remotevolume"" WHERE ""Name"" = ?";
            m_findremotevolumestateCommand.AddParameters(1);

            m_updateblockCommand.CommandText = @"UPDATE ""Block"" SET ""VolumeID"" = ? WHERE ""Hash"" = ? AND ""Size"" = ? ";
            m_updateblockCommand.AddParameters(3);
        }
        
        /// <summary>
        /// Builds the lookup tables. Call this method after deleting items, and before processing items
        /// </summary>
        /// <param name="options">The option settings</param>
        public void BuildLookupTable(Options options)
        {
            if (options.BlockHashLookupMemory > 0)
                m_blockHashLookup = new HashLookupHelper<KeyValuePair<long, long>>((ulong)options.BlockHashLookupMemory);            
            if (options.FileHashLookupMemory > 0)
                m_fileHashLookup = new HashLookupHelper<long>((ulong)options.FileHashLookupMemory);
            if (options.MetadataHashMemory > 0)
                m_metadataLookup = new HashLookupHelper<long>((ulong)options.MetadataHashMemory);
            if (options.UseFilepathCache)
                m_pathLookup = new PathLookupHelper<PathEntryKeeper>(true);


            //Populate the lookup tables
            using (var cmd = m_connection.CreateCommand())
            {
                if (m_blockHashLookup != null)
                    using(new Logging.Timer("Build blockhash lookup table"))
                    using (var rd = cmd.ExecuteReader(@"SELECT DISTINCT ""Block"".""Hash"", ""Block"".""ID"", ""Block"".""Size"" FROM ""Block"" "))
                        while (rd.Read())
                        {
                            var str = rd.GetValue(0).ToString();
                            var id = Convert.ToInt64(rd.GetValue(1));
                            var size = Convert.ToInt64(rd.GetValue(2));
                            m_blockHashLookup.Add(str, new KeyValuePair<long, long>(id, size));
                        }
                
                if (m_fileHashLookup != null)
                    using(new Logging.Timer("Build filehash lookup table"))
                    using (var rd = cmd.ExecuteReader(@"SELECT DISTINCT ""FullHash"", ""ID"" FROM ""BlockSet"""))
                        while (rd.Read())
                        {
                            var str = rd.GetValue(0).ToString();
                            var id = Convert.ToInt64(rd.GetValue(1));
                            m_fileHashLookup.Add(str, id);
                        }

                if (m_metadataLookup != null)
                    using(new Logging.Timer("Build metahash lookup table"))
                    using (var rd = cmd.ExecuteReader(@"SELECT ""Metadataset"".""ID"", ""Blockset"".""FullHash"" FROM ""Metadataset"", ""Blockset"" WHERE ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID"" "))
                        while (rd.Read())
                        {
                            var metadataid = Convert.ToInt64(rd.GetValue(0));
                            var hash = rd.GetValue(1).ToString();
                            m_metadataLookup.Add(hash, metadataid);
                        }

                if (m_pathLookup != null)
                    using(new Logging.Timer("Build path scantime lookup table"))
                    using (var rd = cmd.ExecuteReader(string.Format(@" SELECT ""FileID"", ""Scantime"", ""Path"", ""Scantime"" FROM ""{0}"" WHERE ""BlocksetID"" >= 0 ", m_scantimelookupTablename)))
                        while (rd.Read())
                        {
                            var id = Convert.ToInt64(rd.GetValue(0));
                            var scantime = ParseFromEpochSeconds(Convert.ToInt64(rd.GetValue(1)));
                            var path = rd.GetValue(2).ToString();
                            m_pathLookup.Insert(path, new PathEntryKeeper(id, scantime));
                        }

                if (m_pathLookup != null)
                    using(new Logging.Timer("Build path lookup table"))
                    using (var rd = cmd.ExecuteReader(string.Format(@" SELECT ""Path"", ""BlocksetID"", ""MetadataID"", ""ID"" FROM ""File"" ")))
                        while (rd.Read())
                        {
                            var path = rd.GetValue(0).ToString();
                            var blocksetid = Convert.ToInt64(rd.GetValue(1));
                            var metadataid = Convert.ToInt64(rd.GetValue(2));
                            var filesetid = Convert.ToInt64(rd.GetValue(3));
                            PathEntryKeeper r;
                            if (!m_pathLookup.TryFind(path, out r))
                            {
                                r = new PathEntryKeeper(-1, DateTime.UtcNow);
                                r.AddFilesetID(blocksetid, metadataid, filesetid);
                                m_pathLookup.Insert(path, r);
                            }
                            else
                                r.AddFilesetID(blocksetid, metadataid, filesetid);
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
            if (m_blockHashLookup != null) 
            {
                KeyValuePair<long, long> blockid;
                if (m_blockHashLookup.TryGet(key, out blockid))
                    return false;
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
                r = m_insertblockCommand.ExecuteScalar();
                if (m_blockHashLookup != null)
                    m_blockHashLookup.Add(key, new KeyValuePair<long, long>(Convert.ToInt64(r), size));
                return true;
            }
            else
            {
                //We add/update it now
                if (m_blockHashLookup != null)
                    m_blockHashLookup.Add(key, new KeyValuePair<long, long>(Convert.ToInt64(r), size));

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
        public bool AddBlockset(string filehash, long size, int blocksize, IEnumerable<string> hashes, IEnumerable<string> blocklistHashes, out long blocksetid, System.Data.IDbTransaction transaction = null)
        {
            object r = null;
            if (m_fileHashLookup != null)
            {
                if (m_fileHashLookup.TryGet(filehash, out blocksetid))
                    return false;
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
                
            using(var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                m_insertblocksetCommand.Transaction = tr.Parent;
                m_insertblocksetCommand.SetParameterValue(0, size);
                m_insertblocksetCommand.SetParameterValue(1, filehash);
                blocksetid = Convert.ToInt64(m_insertblocksetCommand.ExecuteScalar());
                if (m_fileHashLookup != null)
                    m_fileHashLookup.Add(filehash, blocksetid);

                long ix = 0;
                if (blocklistHashes != null)
                {
                    m_insertblocklistHashesCommand.SetParameterValue(0, blocksetid);
                    m_insertblocklistHashesCommand.Transaction = tr.Parent;
                    foreach(var bh in blocklistHashes)
                    {
                        m_insertblocklistHashesCommand.SetParameterValue(1, ix);
                        m_insertblocklistHashesCommand.SetParameterValue(2, bh);
                        m_insertblocklistHashesCommand.ExecuteNonQuery();
                        ix++;
                    }
                }

                m_insertblocksetentryCommand.SetParameterValue(0, blocksetid);
                m_insertblocksetentryCommand.Transaction = tr.Parent;

                m_insertblocksetentryFastCommand.SetParameterValue(0, blocksetid);
                m_insertblocksetentryFastCommand.Transaction = tr.Parent;

                ix = 0;
                long remainsize = size;
                foreach(var h in hashes)
                {
                    var exsize = remainsize < blocksize ? remainsize : blocksize;
                    var found = false;
                    if (m_blockHashLookup != null)
                    {
                        KeyValuePair<long, long> id;
                        if (m_blockHashLookup.TryGet(h, out id) && id.Value == exsize)
                        {
                            m_insertblocksetentryFastCommand.SetParameterValue(1, ix);
                            m_insertblocksetentryFastCommand.SetParameterValue(2, id.Key);
                            var cx = m_insertblocksetentryFastCommand.ExecuteNonQuery();
                            if (cx != 1)
                                throw new Exception(string.Format("Unexpected result count: {0}, expected {1}", cx, 1));
                            found = true;
                        }
                    }
                
                    if (!found)
                    {
                        m_insertblocksetentryCommand.SetParameterValue(1, ix);
                        m_insertblocksetentryCommand.SetParameterValue(2, h);
                        m_insertblocksetentryCommand.SetParameterValue(3, exsize);
                        var c = m_insertblocksetentryCommand.ExecuteNonQuery();
                        if (c != 1)
                            throw new Exception(string.Format("Unexpected result count: {0}, expected {1}", c, 1));
                    }
                    
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
            if (size > 0)
            {
                if (m_metadataLookup != null)
                {
                    if(m_metadataLookup.TryGet(hash, out metadataid))
                        return false;
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
                        m_metadataLookup.Add(hash, metadataid);
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
            object fileidobj = null;
            PathEntryKeeper entry = null;
            bool entryFound = false;
            
            if (m_pathLookup != null)
            {
                if (entryFound = (m_pathLookup.TryFind(filename, out entry) && entry != null))
                {
                    var fid = entry.GetFilesetID(blocksetID, metadataID);
                    if (fid >= 0)
                        fileidobj = fid;
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
                using(var tr = new TemporaryTransactionWrapper(m_connection, transaction))
                {
                    m_insertfileCommand.Transaction = tr.Parent;
                    m_insertfileCommand.SetParameterValue(0, filename);
                    m_insertfileCommand.SetParameterValue(1, blocksetID);
                    m_insertfileCommand.SetParameterValue(2, metadataID);
                    fileidobj = Convert.ToInt64(m_insertfileCommand.ExecuteScalar());
                    tr.Commit();                    

                    // We do not need to update this, because we will not ask for the same file twice
                    if (m_pathLookup != null)
                    {
                        if (!entryFound)
                        {
                            entry = new PathEntryKeeper(-1, DateTime.UtcNow);
                            entry.AddFilesetID(blocksetID, metadataID, Convert.ToInt64(fileidobj));
                            m_pathLookup.Insert(filename, entry);
                        }
                        else
                            entry.AddFilesetID(blocksetID, metadataID, Convert.ToInt64(fileidobj));
                    }
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
            if (m_pathLookup != null)
            {            
                PathEntryKeeper tmp;
                if (m_pathLookup.TryFind(path, out tmp) && tmp != null && tmp.FileID >= 0)
                {
                    oldScanned = tmp.ScanTime;
                    return tmp.FileID;
                }
                else
                {
                    oldScanned = DateTime.UtcNow;
                    return -1;
                }
            }
            else
            {
                m_selectfileSimpleCommand.SetParameterValue(0, path);
                using(var rd = m_selectfileSimpleCommand.ExecuteReader())
                    if (rd.Read())
                    {
                        oldScanned = ParseFromEpochSeconds(Convert.ToInt64(rd.GetValue(1)));
                        return Convert.ToInt64(rd.GetValue(0));
                    }
                    else
                    {
                        oldScanned = DateTime.UtcNow;
                        return -1;
                    }
            }
        }

        public string GetFileHash(long fileid)
        {
            m_selectfileHashCommand.SetParameterValue(0, fileid);
            var r = m_selectfileHashCommand.ExecuteScalar();
            if (r == null || r == DBNull.Value)
                return null;
                
            return r.ToString();
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
                        cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", m_scantimelookupTablename));
                }
                catch { }
                finally
                {
                    m_scantimelookupTablename = null;
                }


            m_fileHashLookup = null;
            m_metadataLookup = null;
            m_blockHashLookup = null;
            m_pathLookup = null;

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
            using(var cmd = m_connection.CreateCommand())
            {
                var lastFilesetId = GetPreviousFilesetID(cmd);
                results.AddedFolders = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" = ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", m_filesetId, FOLDER_BLOCKSET_ID, lastFilesetId));
                results.AddedSymlinks = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" = ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", m_filesetId, SYMLINK_BLOCKSET_ID, lastFilesetId));

                results.DeletedFolders = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" = ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", lastFilesetId, FOLDER_BLOCKSET_ID, m_filesetId));
                results.DeletedSymlinks = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" = ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", lastFilesetId, SYMLINK_BLOCKSET_ID, m_filesetId));

                var subqueryNonFiles = @"SELECT ""File"".""Path"", ""Blockset"".""Fullhash"" FROM ""File"", ""FilesetEntry"", ""Metadataset"", ""Blockset"" WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""Metadataset"".""ID"" = ""File"".""MetadataID"" AND ""File"".""BlocksetID"" = ? AND ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID"" AND ""FilesetEntry"".""FilesetID"" = ? ";
                results.ModifiedFolders = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM (" + subqueryNonFiles + @") A, (" + subqueryNonFiles + @") B WHERE ""A"".""Path"" = ""B"".""Path"" AND ""A"".""Fullhash"" != ""B"".""Fullhash"" ", lastFilesetId, FOLDER_BLOCKSET_ID, m_filesetId, FOLDER_BLOCKSET_ID));
                results.ModifiedSymlinks = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM (" + subqueryNonFiles + @") A, (" + subqueryNonFiles + @") B WHERE ""A"".""Path"" = ""B"".""Path"" AND ""A"".""Fullhash"" != ""B"".""Fullhash"" ", lastFilesetId, SYMLINK_BLOCKSET_ID, m_filesetId, SYMLINK_BLOCKSET_ID));
                
                var tmpName = "TmpFileList-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                try
                {
                    var subqueryFiles = @"SELECT ""File"".""Path"", ""A"".""Fullhash"" AS ""Filehash"", ""B"".""Fullhash"" AS ""Metahash"" FROM ""File"", ""FilesetEntry"", ""Blockset"" A, ""Blockset"" B, ""Metadataset""  WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""A"".""ID"" = ""File"".""BlocksetID"" AND ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""MetadataID"" = ""Metadataset"".""ID"" AND ""Metadataset"".""BlocksetID"" = ""B"".""ID"" ";
                
                    cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS " + subqueryFiles, tmpName), lastFilesetId);
                
                    results.AddedFiles = Convert.ToInt64(cmd.ExecuteScalar(string.Format(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" != ? AND ""File"".""BlocksetID"" != ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""{0}"")", tmpName), m_filesetId, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID));
                    results.DeletedFiles = Convert.ToInt64(cmd.ExecuteScalar(string.Format(@"SELECT COUNT(*) FROM ""{0}"" WHERE ""{0}"".""Path"" NOT IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", tmpName), m_filesetId));                
                    results.ModifiedFiles = Convert.ToInt64(cmd.ExecuteScalar(string.Format(@"SELECT COUNT(*) FROM ""{0}"" A, (" + subqueryFiles + @") B WHERE ""A"".""Path"" = ""B"".""Path"" AND (""A"".""Filehash"" != ""B"".""Filehash"" OR ""A"".""Metahash"" != ""B"".""Metahash"")", tmpName), m_filesetId));
                    
                }
                finally
                {
                    try { cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"";", tmpName)); }
                    catch (Exception ex) { m_result.AddWarning("Dispose temp table error", ex); }
                }
            }
        }

        public void AppendFilesFromPreviousSet(System.Data.IDbTransaction transaction, IEnumerable<string> deleted)
        {
            AppendFilesFromPreviousSet(transaction, deleted, m_filesetId, -1, OperationTimestamp);
        }

        public void AppendFilesFromPreviousSet(System.Data.IDbTransaction transaction, IEnumerable<string> deleted, long filesetid, long prevId, DateTime timestamp)
        {
            using(var cmd = m_connection.CreateCommand())
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                long lastFilesetId = prevId < 0 ? GetPreviousFilesetID(cmd, timestamp, filesetid) : prevId;

                cmd.Transaction = tr.Parent;
                cmd.ExecuteNonQuery( @"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Scantime"") SELECT ? AS ""FilesetID"", ""FileID"", ""Scantime"" FROM (SELECT DISTINCT ""FilesetID"", ""FileID"", ""Scantime"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = ? AND ""FileID"" NOT IN (SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = ?)) ", filesetid, lastFilesetId, filesetid);

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
                using(var rd = cmd.ExecuteReader(@"SELECT DISTINCT ""Fileset"".""ID"", ""Fileset"".""Timestamp"" FROM ""Fileset"", ""RemoteVolume"" WHERE ""RemoteVolume"".""ID"" = ""Fileset"".""VolumeID"" AND ""Fileset"".""ID"" IN (SELECT ""FilesetID"" FROM ""FilesetEntry"")  AND (""RemoteVolume"".""State"" = ""Uploading"" OR ""RemoteVolume"".""State"" = ""Temporary"")"))
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
