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
            public DateTime Lastmodified;
            public long FileID;
            public long Filesize;
            public string Metahash;
            public long Metasize;
            
            private SortedList<KeyValuePair<long, long>, long> m_versions;
            
            public PathEntryKeeper(long fileId, DateTime lastmodified, long filesize, string metahash, long metasize)
            {
                this.FileID = fileId;
                this.Lastmodified = lastmodified;
                this.Filesize = filesize;
                this.Metahash = metahash;
                this.Metasize = metasize;
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

        private readonly System.Data.IDbCommand m_insertfileOperationCommand;
		
		private HashLookupHelper<KeyValuePair<long, long>> m_blockHashLookup;
        private HashLookupHelper<long> m_fileHashLookup;
        private HashLookupHelper<long> m_metadataLookup;
        private PathLookupHelper<PathEntryKeeper> m_pathLookup;
        
        private long m_missingBlockHashes;
        private string m_lastmodifiedLookupTablename;
        
        private long m_filesetId;

        public LocalBackupDatabase(string path, Options options)
            : this(new LocalDatabase(path, "Backup", false), options)
        {
            this.ShouldCloseConnection = true;
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
            m_insertblocklistHashesCommand = m_connection.CreateCommand();
            m_selectblocklistHashesCommand = m_connection.CreateCommand();
            m_insertfileOperationCommand = m_connection.CreateCommand();
            m_selectfileSimpleCommand = m_connection.CreateCommand();
            m_selectfileHashCommand = m_connection.CreateCommand();
            m_insertblocksetentryFastCommand = m_connection.CreateCommand();
				
			m_findblockCommand.CommandText = @"SELECT ""ID"" FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ?";
            m_findblockCommand.AddParameters(2);

            m_findblocksetCommand.CommandText = @"SELECT ""ID"" FROM ""Blockset"" WHERE ""Fullhash"" = ? AND ""Length"" = ?";
            m_findblocksetCommand.AddParameters(2);

            m_findmetadatasetCommand.CommandText = @"SELECT ""A"".""ID"" FROM ""Metadataset"" A, ""BlocksetEntry"" B, ""Block"" C WHERE ""A"".""BlocksetID"" = ""B"".""BlocksetID"" AND ""B"".""BlockID"" = ""C"".""ID"" AND ""C"".""Hash"" = ? AND ""C"".""Size"" = ?";
            m_findmetadatasetCommand.AddParameters(2);

            m_findfilesetCommand.CommandText = @"SELECT ""ID"" FROM ""File"" WHERE ""BlocksetID"" = ? AND ""MetadataID"" = ? AND ""Path"" = ?";
            m_findfilesetCommand.AddParameters(3);

            m_insertblockCommand.CommandText = @"INSERT INTO ""Block"" (""Hash"", ""VolumeID"", ""Size"") VALUES (?, ?, ?); SELECT last_insert_rowid();";
            m_insertblockCommand.AddParameters(3);

            m_insertfileOperationCommand.CommandText = @"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") VALUES (?, ?, ?)";
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

            //Need a temporary table with path/lastmodified lookups
            m_lastmodifiedLookupTablename = "LastModified-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
            m_selectfileSimpleCommand.CommandText = string.Format(@"SELECT ""FileID"", ""Lastmodified"", ""Length"", ""Metahash"", ""Metasize"" FROM ""{0}"" WHERE ""BlocksetID"" >= 0 AND ""Path"" = ?", m_lastmodifiedLookupTablename);
            m_selectfileSimpleCommand.AddParameters(1);

            m_selectfileHashCommand.CommandText = @"SELECT ""Blockset"".""Fullhash"" FROM ""Blockset"", ""File"" WHERE ""Blockset"".""ID"" = ""File"".""BlocksetID"" AND ""File"".""ID"" = ?  ";
            m_selectfileHashCommand.AddParameters(1);

            m_selectblocklistHashesCommand.CommandText = @"SELECT ""Hash"" FROM ""BlocklistHash"" WHERE ""BlocksetID"" = ? ORDER BY ""Index"" ASC ";
            m_selectblocklistHashesCommand.AddParameters(1);
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
                //Need a temporary table with path/lastmodified lookups
                var scantableDefinition = @"SELECT ""B"".""ID"" AS ""FileID"", ""D"".""Lastmodified"" AS ""Lastmodified"", ""B"".""Path"" AS ""Path"", ""C"".""Length"" AS ""Length"", ""F"".""Fullhash"" AS ""Metahash"", ""F"".""Length"" AS ""Metasize""  FROM (SELECT ""FilesetEntry"".""FileID"" AS ""FileID"", ""FilesetEntry"".""FilesetID"" AS ""FilesetID"", MAX(""Fileset"".""Timestamp"") AS ""MostRecent"" FROM ""FilesetEntry"", ""Fileset"", ""File"" WHERE ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID"" AND ""File"".""ID"" = ""FilesetEntry"".""FileID"" GROUP BY ""File"".""Path"") A, ""File"" B, ""Blockset"" C, ""FilesetEntry"" D, ""Metadataset"" E, ""Blockset"" F WHERE ""B"".""ID"" = ""A"".""FileID"" AND ""C"".""ID"" = ""B"".""BlocksetID"" AND ""D"".""FileID"" = ""B"".""ID"" AND ""D"".""FilesetID"" = ""A"".""FilesetID"" AND ""B"".""MetadataID"" = ""E"".""ID"" AND ""F"".""ID"" = ""E"".""BlocksetID""";
                cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS " + scantableDefinition, m_lastmodifiedLookupTablename));
                cmd.ExecuteNonQuery(string.Format(@"CREATE INDEX ""{0}Index"" ON ""{0}"" (""Path"", ""Lastmodified"", ""Length"", ""Metahash"", ""Metasize"", ""FileID"") ",  m_lastmodifiedLookupTablename));

                if (m_blockHashLookup != null)
                    try
                    {
                        using(new Logging.Timer("Build blockhash lookup table"))
                        using (var rd = cmd.ExecuteReader(@"SELECT DISTINCT ""Block"".""Hash"", ""Block"".""ID"", ""Block"".""Size"" FROM ""Block"" "))
                            while (rd.Read())
                            {
                                var str = rd.GetValue(0).ToString();
                                var id = rd.GetInt64(1);
                                var size = rd.GetInt64(2);
                                m_blockHashLookup.Add(str, size, new KeyValuePair<long, long>(id, size));
                            }
                    }
                    catch (Exception ex) 
                    {
                        throw new InvalidDataException("Duplicate blockhashes detected, either repair the database or rebuild it", ex);
                    }
                
                if (m_fileHashLookup != null)
                    try
                    {
                        using(new Logging.Timer("Build filehash lookup table"))
                        using (var rd = cmd.ExecuteReader(@"SELECT DISTINCT ""FullHash"", ""Length"", ""ID"" FROM ""BlockSet"""))
                            while (rd.Read())
                            {
                                var str = rd.GetValue(0).ToString();
                                var size = rd.GetInt64(1);
                                var id = rd.GetInt64(2);
                                m_fileHashLookup.Add(str, size, id);
                            }
                    }
                    catch (Exception ex) 
                    {
                        throw new InvalidDataException("Duplicate filehashes detected, either repair the database or rebuild it", ex);
                    }


                if (m_metadataLookup != null)
                    try 
                    { 
                        using(new Logging.Timer("Build metahash lookup table"))
                        using (var rd = cmd.ExecuteReader(@"SELECT ""Metadataset"".""ID"", ""Blockset"".""FullHash"", ""Blockset"".""Length"" FROM ""Metadataset"", ""Blockset"" WHERE ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID"" "))
                            while (rd.Read())
                            {
                                var metadataid = rd.GetInt64(0);
                                var hash = rd.GetValue(1).ToString();
                                var size = rd.GetInt64(2);;
                                    m_metadataLookup.Add(hash, size, metadataid); 
                            }
                    }
                    catch (Exception ex) 
                    {
                        throw new InvalidDataException("Duplicate metadatahash detected, run repair to fix it", ex);
                    }

                if (m_pathLookup != null)
                    using(new Logging.Timer("Build path lastmodified lookup table"))
                    using (var rd = cmd.ExecuteReader(string.Format(@" SELECT ""FileID"", ""Lastmodified"", ""Length"", ""Path"", ""Metahash"", ""Metasize"" FROM ""{0}"" WHERE ""BlocksetID"" >= 0 ", m_lastmodifiedLookupTablename)))
                        while (rd.Read())
                        {
                            var id = rd.GetInt64(0);
                            var lastmodified = new DateTime(rd.GetInt64(1), DateTimeKind.Utc);
                            var filesize = rd.GetInt64(2);
                            var path = rd.GetString(3);
                            var metahash = rd.GetString(4);
                            var metasize = rd.GetInt64(5);
                            m_pathLookup.Insert(path, new PathEntryKeeper(id, lastmodified, filesize, metahash, metasize));
                        }

                if (m_pathLookup != null)
                    try
                    {
                        using(new Logging.Timer("Build path lookup table"))
                        using (var rd = cmd.ExecuteReader(string.Format(@" SELECT ""Path"", ""BlocksetID"", ""MetadataID"", ""ID"" FROM ""File"" ")))
                            while (rd.Read())
                            {
                                var path = rd.GetValue(0).ToString();
                                var blocksetid = rd.GetInt64(1);
                                var metadataid = rd.GetInt64(2);
                                var filesetid = rd.GetInt64(3);

                                PathEntryKeeper r;
                                if (!m_pathLookup.TryFind(path, out r))
                                {
                                    r = new PathEntryKeeper(-1, new DateTime(0, DateTimeKind.Utc), -1, null, -1);
                                    r.AddFilesetID(blocksetid, metadataid, filesetid);
                                    m_pathLookup.Insert(path, r);
                                }
                                else
                                    r.AddFilesetID(blocksetid, metadataid, filesetid);
                            }
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("Duplicate file entries detected, run repair to fix it", ex);
                    }
                                                        
                m_missingBlockHashes = cmd.ExecuteScalarInt64(@"SELECT COUNT (*) FROM (SELECT DISTINCT ""Block"".""Hash"", ""Block"".""Size"" FROM ""Block"", ""RemoteVolume"" WHERE ""RemoteVolume"".""ID"" = ""Block"".""VolumeID"" AND ""RemoteVolume"".""State"" NOT IN (?,?,?,?))", 0, RemoteVolumeState.Temporary.ToString(), RemoteVolumeState.Uploading.ToString(), RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString());

                var tc = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM ""Remotevolume"" WHERE ""ID"" IN (SELECT DISTINCT ""VolumeID"" FROM ""Block"") AND ""State"" NOT IN (?, ?, ?, ?);", 0, RemoteVolumeState.Temporary.ToString(), RemoteVolumeState.Uploading.ToString(), RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString());
                if (tc > 0)
                    throw new InvalidDataException("Detected blocks that are not reachable in the block table");

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
            var r = -1L;
            if (m_blockHashLookup != null) 
            {
                KeyValuePair<long, long> blockid;
                if (m_blockHashLookup.TryGet(key, size, out blockid))
                    return false;
            }
            else
            {
                m_findblockCommand.Transaction = transaction;
                m_findblockCommand.SetParameterValue(0, key);
                m_findblockCommand.SetParameterValue(1, size);
                r = m_findblockCommand.ExecuteScalarInt64(-1);
            }

            if (r == -1L)
            {
                m_insertblockCommand.Transaction = transaction;
                m_insertblockCommand.SetParameterValue(0, key);
                m_insertblockCommand.SetParameterValue(1, volumeid);
                m_insertblockCommand.SetParameterValue(2, size);
                r = m_insertblockCommand.ExecuteScalarInt64();
                if (m_blockHashLookup != null)
                    m_blockHashLookup.Add(key, size, new KeyValuePair<long, long>(r, size));
                return true;
            }
            else
            {
                //Update lookup cache if required
                if (m_blockHashLookup != null)
                    m_blockHashLookup.Add(key, size, new KeyValuePair<long, long>(r, size));

                return false;
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
            if (m_fileHashLookup != null)
            {
                if (m_fileHashLookup.TryGet(filehash, size, out blocksetid))
                    return false;
            }
            else
            {
                m_findblocksetCommand.Transaction = transaction;
                blocksetid = m_findblocksetCommand.ExecuteScalarInt64(null, -1, filehash, size);
                if (blocksetid != -1)
                    return false; //Found it
            }
                
            using(var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                m_insertblocksetCommand.Transaction = tr.Parent;
                m_insertblocksetCommand.SetParameterValue(0, size);
                m_insertblocksetCommand.SetParameterValue(1, filehash);
                blocksetid = m_insertblocksetCommand.ExecuteScalarInt64();
                if (m_fileHashLookup != null)
                    m_fileHashLookup.Add(filehash, size, blocksetid);

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
                        if (m_blockHashLookup.TryGet(h, exsize, out id) && id.Value == exsize)
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
                    if(m_metadataLookup.TryGet(hash, size, out metadataid))
                        return false;
                }
                else
                {
                    m_findmetadatasetCommand.Transaction = transaction;
                    metadataid = m_findmetadatasetCommand.ExecuteScalarInt64(null, -1, hash, size);
                    if (metadataid != -1)
                        return false;
                }
            

                long blocksetid;
                AddBlockset(hash, size, (int)size, new string[] { hash }, null, out blocksetid, transaction);

                using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
                {
                    m_insertmetadatasetCommand.Transaction = tr.Parent;
                    m_insertmetadatasetCommand.SetParameterValue(0, blocksetid);
                    metadataid = m_insertmetadatasetCommand.ExecuteScalarInt64();
                    tr.Commit();
                    if (m_metadataLookup != null)
                        m_metadataLookup.Add(hash, size, metadataid);
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
        /// <param name="lastmodified">The time the file was modified</param>
        /// <param name="blocksetID">The ID of the hashkey for the file</param>
        /// <param name="metadataID">The ID for the metadata</param>
        /// <param name="transaction">The transaction to use for insertion, or null for no transaction</param>
        /// <param name="operationId">The operationId to use, or -1 to use the current operation</param>
        public void AddFile(string filename, DateTime lastmodified, long blocksetID, long metadataID, System.Data.IDbTransaction transaction)
        {            
            var fileidobj = -1L;
            PathEntryKeeper entry = null;
            var entryFound = false;
            
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
                fileidobj = m_findfilesetCommand.ExecuteScalarInt64();
            }
            
            if (fileidobj == -1)
            {
                using(var tr = new TemporaryTransactionWrapper(m_connection, transaction))
                {
                    m_insertfileCommand.Transaction = tr.Parent;
                    m_insertfileCommand.SetParameterValue(0, filename);
                    m_insertfileCommand.SetParameterValue(1, blocksetID);
                    m_insertfileCommand.SetParameterValue(2, metadataID);
                    fileidobj = m_insertfileCommand.ExecuteScalarInt64();
                    tr.Commit();                    

                    // We do not need to update this, because we will not ask for the same file twice
                    if (m_pathLookup != null)
                    {
                        if (!entryFound)
                        {
                            entry = new PathEntryKeeper(-1, new DateTime(0, DateTimeKind.Utc), -1, null, -1);
                            entry.AddFilesetID(blocksetID, metadataID, fileidobj);
                            m_pathLookup.Insert(filename, entry);
                        }
                        else
                            entry.AddFilesetID(blocksetID, metadataID, fileidobj);
                    }
                }
            }
            
            m_insertfileOperationCommand.Transaction = transaction;
            m_insertfileOperationCommand.SetParameterValue(0, m_filesetId);
            m_insertfileOperationCommand.SetParameterValue(1, fileidobj);
            m_insertfileOperationCommand.SetParameterValue(2, lastmodified.ToUniversalTime().Ticks);
            m_insertfileOperationCommand.ExecuteNonQuery();

        }

        public void AddUnmodifiedFile(long fileid, DateTime lastmodified, System.Data.IDbTransaction transaction = null)
        {
            m_insertfileOperationCommand.Transaction = transaction;
            m_insertfileOperationCommand.SetParameterValue(0, m_filesetId);
            m_insertfileOperationCommand.SetParameterValue(1, fileid);
            m_insertfileOperationCommand.SetParameterValue(2, lastmodified.ToUniversalTime().Ticks);
            m_insertfileOperationCommand.ExecuteNonQuery();
        }

        public void AddDirectoryEntry(string path, long metadataID, DateTime lastmodified, System.Data.IDbTransaction transaction = null)
        {
            AddFile(path, lastmodified, FOLDER_BLOCKSET_ID, metadataID, transaction);
        }
        
        public void AddSymlinkEntry(string path, long metadataID, DateTime lastmodified, System.Data.IDbTransaction transaction = null)
        {
            AddFile(path, lastmodified, SYMLINK_BLOCKSET_ID, metadataID, transaction);
        }

        public long GetFileEntry(string path, out DateTime oldModified, out long lastFileSize, out string oldMetahash, out long oldMetasize)
        {
            if (m_pathLookup != null)
            {            
                PathEntryKeeper tmp;
                if (m_pathLookup.TryFind(path, out tmp) && tmp != null && tmp.FileID >= 0)
                {
                    oldModified = tmp.Lastmodified;
                    lastFileSize = tmp.Filesize;
                    oldMetahash = tmp.Metahash;
                    oldMetasize = tmp.Metasize;
                    return tmp.FileID;
                }
                else
                {
                    oldModified = new DateTime(0, DateTimeKind.Utc);
                    lastFileSize = -1;
                    oldMetahash = null;
                    oldMetasize = -1;
                    return -1;
                }
            }
            else
            {
                m_selectfileSimpleCommand.SetParameterValue(0, path);
                using(var rd = m_selectfileSimpleCommand.ExecuteReader())
                    if (rd.Read())
                    {
                        oldModified = new DateTime(rd.GetInt64(1), DateTimeKind.Utc);
                        lastFileSize = rd.GetInt64(2);
                        oldMetahash = rd.GetString(3);
                        oldMetasize = rd.GetInt64(4);
                        return rd.GetInt64(0);
                    }
                    else
                    {
                        oldModified = new DateTime(0, DateTimeKind.Utc);
                        lastFileSize = -1;
                        oldMetahash = null;
                        oldMetasize = -1;
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
            if (m_lastmodifiedLookupTablename != null)
                try 
                { 
                    using(var cmd = m_connection.CreateCommand())
                        cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", m_lastmodifiedLookupTablename));
                }
                catch { }
                finally
                {
                    m_lastmodifiedLookupTablename = null;
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
            var lastFilesetId = cmd.ExecuteScalarInt64(@"SELECT ""ID"" FROM ""Fileset"" WHERE ""Timestamp"" < ? AND ""ID"" != ? ORDER BY ""Timestamp"" DESC ", -1, NormalizeDateTimeToEpochSeconds(timestamp), filesetid);                
            return lastFilesetId;
        }

        internal void UpdateChangeStatistics(BackupResults results)
        {
            using(var cmd = m_connection.CreateCommand())
            {
                var lastFilesetId = GetPreviousFilesetID(cmd);
                results.AddedFolders = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" = ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", 0, m_filesetId, FOLDER_BLOCKSET_ID, lastFilesetId);
                results.AddedSymlinks = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" = ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", 0, m_filesetId, SYMLINK_BLOCKSET_ID, lastFilesetId);

                results.DeletedFolders = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" = ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", 0, lastFilesetId, FOLDER_BLOCKSET_ID, m_filesetId);
                results.DeletedSymlinks = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" = ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", 0, lastFilesetId, SYMLINK_BLOCKSET_ID, m_filesetId);

                var subqueryNonFiles = @"SELECT ""File"".""Path"", ""Blockset"".""Fullhash"" FROM ""File"", ""FilesetEntry"", ""Metadataset"", ""Blockset"" WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""Metadataset"".""ID"" = ""File"".""MetadataID"" AND ""File"".""BlocksetID"" = ? AND ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID"" AND ""FilesetEntry"".""FilesetID"" = ? ";
                results.ModifiedFolders = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM (" + subqueryNonFiles + @") A, (" + subqueryNonFiles + @") B WHERE ""A"".""Path"" = ""B"".""Path"" AND ""A"".""Fullhash"" != ""B"".""Fullhash"" ", 0, lastFilesetId, FOLDER_BLOCKSET_ID, m_filesetId, FOLDER_BLOCKSET_ID);
                results.ModifiedSymlinks = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM (" + subqueryNonFiles + @") A, (" + subqueryNonFiles + @") B WHERE ""A"".""Path"" = ""B"".""Path"" AND ""A"".""Fullhash"" != ""B"".""Fullhash"" ", 0, lastFilesetId, SYMLINK_BLOCKSET_ID, m_filesetId, SYMLINK_BLOCKSET_ID);
                
                var tmpName1 = "TmpFileList-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                var tmpName2 = "TmpFileList-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                try
                {
                    var subqueryFiles = @"SELECT ""File"".""Path"", ""A"".""Fullhash"" AS ""Filehash"", ""B"".""Fullhash"" AS ""Metahash"" FROM ""File"", ""FilesetEntry"", ""Blockset"" A, ""Blockset"" B, ""Metadataset""  WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""A"".""ID"" = ""File"".""BlocksetID"" AND ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""MetadataID"" = ""Metadataset"".""ID"" AND ""Metadataset"".""BlocksetID"" = ""B"".""ID"" ";
                
                    cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS " + subqueryFiles, tmpName1), lastFilesetId);
                    cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS " + subqueryFiles, tmpName2), m_filesetId);
                
                    results.AddedFiles = cmd.ExecuteScalarInt64(string.Format(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" != ? AND ""File"".""BlocksetID"" != ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""{0}"")", tmpName1), 0, m_filesetId, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID);
                    results.DeletedFiles = cmd.ExecuteScalarInt64(string.Format(@"SELECT COUNT(*) FROM ""{0}"" WHERE ""{0}"".""Path"" NOT IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", tmpName1), 0, m_filesetId);
                    results.ModifiedFiles = cmd.ExecuteScalarInt64(string.Format(@"SELECT COUNT(*) FROM ""{0}"" A, ""{1}"" B WHERE ""A"".""Path"" = ""B"".""Path"" AND (""A"".""Filehash"" != ""B"".""Filehash"" OR ""A"".""Metahash"" != ""B"".""Metahash"")", tmpName1, tmpName2), 0);
                    
                }
                finally
                {
                    try { cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"";", tmpName1)); }
                    catch (Exception ex) { m_result.AddWarning("Dispose temp table error", ex); }
                    try { cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"";", tmpName2)); }
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
                cmd.ExecuteNonQuery( @"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") SELECT ? AS ""FilesetID"", ""FileID"", ""Lastmodified"" FROM (SELECT DISTINCT ""FilesetID"", ""FileID"", ""Lastmodified"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = ? AND ""FileID"" NOT IN (SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = ?)) ", filesetid, lastFilesetId, filesetid);

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
                            rd.GetInt64(0),
                            ParseFromEpochSeconds(rd.GetInt64(1)).ToLocalTime()
                        );
                    }
            }
        }

        public IRemoteVolume GetRemoteVolumeFromName(string name)
        {
            using(var cmd = m_connection.CreateCommand())
            using(var rd = cmd.ExecuteReader(@"SELECT ""Name"", ""Hash"", ""Size"" FROM ""RemoteVolume"" WHERE ""Name"" = ?", name))
                if (rd.Read())
                    return new RemoteVolume(rd.GetValue(0).ToString(), rd.GetValue(1).ToString(), rd.ConvertValueToInt64(2));
                else
                    return null;
        }

        public IEnumerable<string> GetMissingIndexFiles()
        {
            using(var cmd = m_connection.CreateCommand())
            using(var rd = cmd.ExecuteReader(@"SELECT ""Name"" FROM ""RemoteVolume"" WHERE ""Type"" = ? AND NOT ""ID"" IN (SELECT ""BlockVolumeID"" FROM ""IndexBlockLink"") AND ""State"" IN (?,?)", RemoteVolumeType.Blocks.ToString(), RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString()))
                while (rd.Read())
                    yield return rd.GetValue(0).ToString();
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
