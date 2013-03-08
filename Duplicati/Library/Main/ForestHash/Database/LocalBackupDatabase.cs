using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Duplicati.Library.Main.ForestHash.Database
{
    public class LocalBackupDatabase : Localdatabase
    {
        private readonly System.Data.IDbCommand m_findblockCommand;
        private readonly System.Data.IDbCommand m_findblocksetCommand;
        private readonly System.Data.IDbCommand m_findfilesetCommand;
        private readonly System.Data.IDbCommand m_findmetadatasetCommand;

        private readonly System.Data.IDbCommand m_insertblockCommand;

        private readonly System.Data.IDbCommand m_insertfileCommand;

        private readonly System.Data.IDbCommand m_insertblocksetCommand;
        private readonly System.Data.IDbCommand m_insertblocksetentryCommand;
        private readonly System.Data.IDbCommand m_insertblocklistHashesCommand;

        private readonly System.Data.IDbCommand m_insertmetadatasetCommand;

        //private readonly System.Data.IDbCommand m_selectfileCommand;
        private readonly System.Data.IDbCommand m_selectfileSimpleCommand;
        private readonly System.Data.IDbCommand m_selectfileHashCommand;
        private readonly System.Data.IDbCommand m_selectblocklistHashesCommand;

        private readonly System.Data.IDbCommand m_findremotevolumestateCommand;
        private readonly System.Data.IDbCommand m_updateblockCommand;

        private readonly System.Data.IDbCommand m_createremotevolumeCommand;
        private readonly System.Data.IDbCommand m_insertfileOperationCommand;

        public LocalBackupDatabase(string path)
            : this(CreateConnection(path))
        {
        }

        public LocalBackupDatabase(LocalRestoredatabase connection)
            : this(connection.Connection)
        {
        }

        public LocalBackupDatabase(System.Data.IDbConnection connection)
            : base(connection, "Backup")
        {
            m_findblockCommand = m_connection.CreateCommand();
            m_insertblockCommand = m_connection.CreateCommand();
            m_insertfileCommand = m_connection.CreateCommand();
            m_insertblocksetCommand = m_connection.CreateCommand();
            //m_selectfileCommand = m_connection.CreateCommand();
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

            m_findblockCommand.CommandText = @"SELECT ""File"" FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ?";
            m_findblockCommand.AddParameters(2);

            m_findblocksetCommand.CommandText = @"SELECT ""ID"" FROM ""Blockset"" WHERE ""Fullhash"" = ? AND ""Length"" = ?";
            m_findblocksetCommand.AddParameters(2);

            m_findmetadatasetCommand.CommandText = @"SELECT ""ID"" FROM ""Metadataset"" WHERE ""BlocksetID"" = (SELECT DISTINCT ""BlocksetEntry"".""BlocksetID"" FROM ""BlocksetEntry"", ""Block"" WHERE ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" AND ""Block"".""Hash"" = ? AND ""Block"".""Size"" = ?)";
            m_findmetadatasetCommand.AddParameters(2);

            m_findfilesetCommand.CommandText = @"SELECT ""ID"" FROM ""Fileset"" WHERE ""BlocksetID"" = ? AND ""MetadatasetID"" = ?";
            m_findfilesetCommand.AddParameters(2);

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

            m_selectfileSimpleCommand.CommandText = @"SELECT ""OperationFileset"".""FilesetID"", ""OperationFileset"".""Scantime"" FROM ""OperationFileset"" INNER JOIN ""Fileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""FilesetID"" WHERE ""Fileset"".""Path"" = ? ORDER BY ""OperationFileset"".""Scantime"" DESC";
            m_selectfileSimpleCommand.AddParameters(1);

            m_selectfileHashCommand.CommandText = @"SELECT ""Blockset"".""Fullhash"" FROM ""Blockset"" INNER JOIN ""Fileset"" ON ""Blockset"".""ID"" = ""Fileset"".""BlocksetID"" WHERE ""Fileset"".""ID"" = ?  ";
            m_selectfileHashCommand.AddParameters(1);

            //m_selectfileCommand.CommandText = @"SELECT ""A"".""ID"", ""D"".""Scantime"", ""B"".""Length"", ""B"".""FullHash"", ""E"".""FullHash"", ""E"".""Length"", ""B"".""ID"" FROM ""Fileset"" A, ""Blockset"" B, ""Metadataset"" C, ""OperationFileset"" D, ""Blockset"" E, ""Operation"" F WHERE ""A"".""ID"" = ""D"".""FilesetID"" AND ""A"".""Path"" = ? AND ""A"".""BlocksetID"" = ""B"".""ID"" AND ""A"".""MetadataID"" = ""C"".""ID"" AND ""E"".""ID"" = ""C"".""BlocksetID"" AND ""F"".""ID"" = ""D"".""OperationID"" ORDER BY ""F"".""Timestamp"" DESC ";
            //m_selectfileCommand.AddParameters(1);

            m_selectblocklistHashesCommand.CommandText = @"SELECT ""Hash"" FROM ""BlocklistHash"" WHERE ""BlocksetID"" = ? ORDER BY ""Index"" ASC ";
            m_selectblocklistHashesCommand.AddParameters(1);

            m_findremotevolumestateCommand.CommandText = @"SELECT ""State"" FROM ""Remotevolume"" WHERE ""Name"" = ?";
            m_findremotevolumestateCommand.AddParameters(1);

            m_updateblockCommand.CommandText = @"UPDATE ""Block"" SET ""File"" = ? WHERE ""Hash"" = ? AND ""Size"" = ? ";
            m_updateblockCommand.AddParameters(3);

            m_createremotevolumeCommand.CommandText = @"INSERT INTO ""Remotevolume"" (""OperationID"", ""Name"", ""Type"", ""State"") VALUES (?, ?, ?, ?)";
            m_createremotevolumeCommand.AddParameter(m_operationid);
            m_createremotevolumeCommand.AddParameters(3);
        }

        /// <summary>
        /// Adds a block to the local database, returning a value indicating if the value presents a new block
        /// </summary>
        /// <param name="key">The block key</param>
        /// <param name="archivename">The name of the archive that holds the data</param>
        /// <returns>True if the block should be added to the current output</returns>
        public bool AddBlock(string key, long size, string archivename, System.Data.IDbTransaction transaction = null)
        {
            //TODO: Should have some of this in local memory for fast lookup, the DB is a bit slow
            m_findblockCommand.Transaction = transaction;
            var r = m_findblockCommand.ExecuteScalar(null, key, size);
            if (r == null || r == DBNull.Value)
            {
                m_insertblockCommand.Transaction = transaction;
                m_insertblockCommand.SetParameterValue(0, key);
                m_insertblockCommand.SetParameterValue(1, archivename);
                m_insertblockCommand.SetParameterValue(2, size);
                m_insertblockCommand.ExecuteNonQuery();
                return true;
            }
            else
            {
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
        public bool AddBlockset(string filehash, long size, int blocksize, IEnumerable<string> hashes, IEnumerable<string> blocklistHashes, out long blocksetid, System.Data.IDbTransaction transaction = null)
        {
            m_findblocksetCommand.Transaction = transaction;
            object r = m_findblocksetCommand.ExecuteScalar(null, filehash, size);
            if (r != null)
            {
                blocksetid = Convert.ToInt64(r);
                //TODO: Make a sweep to see if the blocks match the record?
                return false;
            }

            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                m_insertblocksetCommand.Transaction = tr.Parent;
                blocksetid = Convert.ToInt64(m_insertblocksetCommand.ExecuteScalar(null, size, filehash));

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
            if (size > 0)
            {
                m_findmetadatasetCommand.Transaction = transaction;
                object r = m_findmetadatasetCommand.ExecuteScalar(null, hash, size);
                if (r != null && r != DBNull.Value)
                {
                    metadataid = Convert.ToInt64(r);
                    return false;
                }

                long blocksetid;
                AddBlockset(hash, size, (int)size, new string[] { hash }, null, out blocksetid, transaction);

                using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
                {
                    m_insertmetadatasetCommand.Transaction = tr.Parent;
                    metadataid = Convert.ToInt64(m_insertmetadatasetCommand.ExecuteScalar(null, blocksetid));
                    tr.Commit();
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
            m_findfilesetCommand.Transaction = transaction;
            m_findfilesetCommand.SetParameterValue(0, blocksetID);
            m_findfilesetCommand.SetParameterValue(1, metadataID);
            var fileid = m_findfilesetCommand.ExecuteScalar();
            if (fileid == null || fileid == DBNull.Value)
            {
                using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
                {
                    m_insertfileCommand.Transaction = tr.Parent;
                    m_insertfileCommand.SetParameterValue(0, filename);
                    m_insertfileCommand.SetParameterValue(1, blocksetID);
                    m_insertfileCommand.SetParameterValue(2, metadataID);
                    fileid = Convert.ToInt64(m_insertfileCommand.ExecuteScalar());
                    tr.Commit();
                }
            }

            m_insertfileOperationCommand.Transaction = transaction;
            m_insertfileOperationCommand.SetParameterValue(1, fileid);
            m_insertfileOperationCommand.SetParameterValue(2, scantime);
            m_insertfileOperationCommand.ExecuteNonQuery();

        }

        /// <summary>
        /// Gets a file entry from the database
        /// </summary>
        /// <param name="path">The path to the file</param>
        /// <param name="fileid">The id of the file, or -1</param>
        /// <param name="filesize">The size of the file, or -1</param>
        /// <param name="lastScanned">The time the file was last scanned, or the current time</param>
        /// <param name="hash">The hash of the file</param>
        /// <param name="metahash">The hash of the metadata</param>
        /// <returns>True if the entry was found, false otherwise</returns>
        /*public bool GetFileEntry(string path, out long fileid, out long filesize, out DateTime lastScanned, out string hash, out string metahash, out long metasize, out IList<string> blockHashes)
        {
            ((System.Data.IDataParameter)m_selectfileCommand.Parameters[0]).Value = path;
            using (var rd = m_selectfileCommand.ExecuteReader())
                if (rd.Read())
                {
                    fileid = Convert.ToInt64(rd.GetValue(0));
                    lastScanned = Convert.ToDateTime(rd.GetValue(1));
                    filesize = Convert.ToInt64(rd.GetValue(2));
                    object h = rd.GetValue(3);
                    hash = (h == null || h == DBNull.Value) ? null : h.ToString();
                    h = rd.GetValue(4);
                    metahash = (h == null || h == DBNull.Value) ? null : h.ToString();
                    metasize = Convert.ToInt64(rd.GetValue(5));

                    m_selectblocklistHashesCommand.SetParameterValue(0, rd.GetValue(6));
                    using (var bhrd = m_selectblocklistHashesCommand.ExecuteReader())
                    {
                        blockHashes = new List<string>();
                        while (bhrd.Read())
                        {
                            var blh = bhrd.GetValue(0);
                            if (blh != null && blh != DBNull.Value)
                                blockHashes.Add(blh.ToString());
                        }

                        if (blockHashes.Count == 0)
                            blockHashes = null;
                    }

                    return true;
                }
                else
                {
                    fileid = -1;
                    lastScanned = DateTime.UtcNow;
                    filesize = -1;
                    hash = null;
                    metahash = null;
                    metasize = -1;
                    blockHashes = null;
                    return false;
                }

        }*/

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
            m_selectfileSimpleCommand.SetParameterValue(0, path);
            using(var rd = m_selectfileSimpleCommand.ExecuteReader())
                if (rd.Read())
                {
                    oldScanned =  Convert.ToDateTime(rd.GetValue(1));
                    return Convert.ToInt64(rd.GetValue(0));
                }
                else
                {
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

                        if (m_path != m_reader.GetValue(0).ToString())
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

                /*m_stat.ModifiedFolders = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT * FROM ( (SELECT ""Fileset"".""Path"", ""Fileset"".""BlocksetID"", ""Fileset"".""MetadataID"" FROM ""OperationFileset"" INNER JOIN ""Fileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""Fileset"".""ID"" WHERE ""OperationID"" = ?) AS A, (SELECT ""Fileset"".""Path"", ""Fileset"".""BlocksetID"", ""Fileset"".""MetadataID"" FROM ""OperationFileset"" INNER JOIN ""Fileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""Fileset"".""ID"" WHERE ""OperationID"" = ?) AS B WHERE ""A"".""Path"" = ""B"".""Path"" AND ""A"".""BlocsetID"" != ? AND ""A"".""MetadataID"" != ""B"".""MetadataID"" )", m_operationid, lastOperationId, FOLDER_BLOCKSET_ID));
                m_stat.ModifiedSymlinks = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT * FROM ( (SELECT ""Fileset"".""Path"", ""Fileset"".""BlocksetID"", ""Fileset"".""MetadataID"" FROM ""OperationFileset"" INNER JOIN ""Fileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""Fileset"".""ID"" WHERE ""OperationID"" = ?) AS A, (SELECT ""Fileset"".""Path"", ""Fileset"".""BlocksetID"", ""Fileset"".""MetadataID"" FROM ""OperationFileset"" INNER JOIN ""Fileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""Fileset"".""ID"" WHERE ""OperationID"" = ?) AS B WHERE ""A"".""Path"" = ""B"".""Path"" AND ""A"".""BlocsetID"" != ? AND ""A"".""MetadataID"" != ""B"".""MetadataID"" )", m_operationid, lastOperationId, SYMLINK_BLOCKSET_ID));
                m_stat.ModifiedFiles = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT * FROM ( (SELECT ""Fileset"".""Path"", ""Fileset"".""BlocksetID"", ""Fileset"".""MetadataID"" FROM ""OperationFileset"" INNER JOIN ""Fileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""Fileset"".""ID"" WHERE ""OperationID"" = ?) AS A, (SELECT ""Fileset"".""Path"", ""Fileset"".""BlocksetID"", ""Fileset"".""MetadataID"" FROM ""OperationFileset"" INNER JOIN ""Fileset"" ON ""Fileset"".""ID"" = ""OperationFileset"".""Fileset"".""ID"" WHERE ""OperationID"" = ?) AS B WHERE ""A"".""Path"" = ""B"".""Path"" AND (""A"".""BlocsetID"" != ""B"".""BlocksetID"" OR ""A"".""MetadataID"" != ""B"".""MetadataID"") )", m_operationid, lastOperationId));
                 * */
            }
        }

    }
}
