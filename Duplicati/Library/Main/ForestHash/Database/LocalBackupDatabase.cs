using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

        private readonly System.Data.IDbCommand m_selectfileCommand;
        private readonly System.Data.IDbCommand m_selectblocklistHashesCommand;

        private readonly System.Data.IDbCommand m_selectfileidsCommand;
        private readonly System.Data.IDbCommand m_findremotevolumestateCommand;
        private readonly System.Data.IDbCommand m_updateblockCommand;

        private readonly System.Data.IDbCommand m_createremotevolumeCommand;

        public LocalBackupDatabase(string path)
            : base(path, "Backup")
        {
            m_findblockCommand = m_connection.CreateCommand();
            m_insertblockCommand = m_connection.CreateCommand();
            m_insertfileCommand = m_connection.CreateCommand();
            m_insertblocksetCommand = m_connection.CreateCommand();
            m_selectfileCommand = m_connection.CreateCommand();
            m_insertmetadatasetCommand = m_connection.CreateCommand();
            m_findblocksetCommand = m_connection.CreateCommand();
            m_findmetadatasetCommand = m_connection.CreateCommand();
            m_findfilesetCommand = m_connection.CreateCommand();
            m_insertblocksetentryCommand = m_connection.CreateCommand();
            m_selectfileidsCommand = m_connection.CreateCommand();
            m_findremotevolumestateCommand = m_connection.CreateCommand();
            m_updateblockCommand = m_connection.CreateCommand();
            m_createremotevolumeCommand = m_connection.CreateCommand();
            m_insertblocklistHashesCommand = m_connection.CreateCommand();
            m_selectblocklistHashesCommand = m_connection.CreateCommand();

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

            m_insertfileCommand.CommandText = @"INSERT INTO ""Fileset"" (""OperationID"", ""Path"", ""Scantime"",""BlocksetID"", ""MetadataID"") VALUES (?, ?, ? ,?, ?)";
            m_insertfileCommand.AddParameter(m_operationid);
            m_insertfileCommand.AddParameters(4);

            m_insertblocksetCommand.CommandText = @"INSERT INTO ""Blockset"" (""Length"", ""FullHash"") VALUES (?, ?)";
            m_insertblocksetCommand.AddParameters(2);

            m_insertblocksetentryCommand.CommandText = @"INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") SELECT ? AS A, ? AS B, ""ID"" FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ?";
            m_insertblocksetentryCommand.AddParameters(4);

            m_insertblocklistHashesCommand.CommandText = @"INSERT INTO ""BlocklistHash"" (""BlocksetID"", ""Index"", ""Hash"") VALUES (?, ?, ?)";
            m_insertblocklistHashesCommand.AddParameters(3);

            m_insertmetadatasetCommand.CommandText = @"INSERT INTO ""Metadataset"" (""BlocksetID"") SELECT ""ID"" FROM ""Blockset"" WHERE ""FullHash"" = ? AND ""Length"" = ?";
            m_insertmetadatasetCommand.AddParameters(2);

            m_selectfileCommand.CommandText = @"SELECT ""A"".""ID"", ""A"".""Scantime"", ""B"".""Length"", ""B"".""FullHash"", ""E"".""FullHash"", ""E"".""Length"", ""B"".""ID"" FROM ""Fileset"" A, ""Blockset"" B, ""Metadataset"" C, ""Operation"" D, ""Blockset"" E WHERE ""A"".""OperationID"" = ""D"".""ID"" AND ""A"".""Path"" = ? AND ""A"".""BlocksetID"" = ""B"".""ID"" AND ""A"".""MetadataID"" = ""C"".""ID"" AND ""E"".""ID"" = ""C"".""BlocksetID"" ORDER BY ""D"".""Timestamp"" DESC ";
            m_selectfileCommand.AddParameters(1);

            m_selectblocklistHashesCommand.CommandText = @"SELECT ""Hash"" FROM ""BlocklistHash"" WHERE ""BlocksetID"" = ? ORDER BY ""Index"" ASC ";
            m_selectblocklistHashesCommand.AddParameters(1);

            m_selectfileidsCommand.CommandText = @"SELECT ""Path"", ""BlocksetID"", ""MetadataID"" FROM ""Fileset"" WHERE ""ID"" = ?";
            m_selectfileidsCommand.AddParameters(1);

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
        public bool AddBlock(string key, long size, string archivename)
        {
            //TODO: Should have some of this in local memory for fast lookup, the DB is a bit slow
            var r = m_findblockCommand.ExecuteScalar(null, key, size);
            if (r == null || r == DBNull.Value)
            {
                m_insertblockCommand.SetParameterValue(0, key);
                m_insertblockCommand.SetParameterValue(1, archivename);
                m_insertblockCommand.SetParameterValue(2, size);
                m_insertblockCommand.ExecuteNonQuery();
                return true;
            }
            else
            {
                r = m_findremotevolumestateCommand.ExecuteScalar(null, r);
                if (r != null && (r.ToString() == RemoteVolumeState.Temporary.ToString() || r.ToString() == RemoteVolumeState.Uploading.ToString() || r.ToString() == RemoteVolumeState.Uploaded.ToString() || r.ToString() == RemoteVolumeState.Verified.ToString()))
                {
                    return false;
                }
                else
                {
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
        public bool AddBlockset(string filehash, long size, int blocksize, IEnumerable<string> hashes, IEnumerable<string> blocklistHashes, out long blocksetid)
        {
            object r = m_findblocksetCommand.ExecuteScalar(null, filehash, size);
            if (r != null)
            {
                blocksetid = Convert.ToInt64(r);
                //TODO: Make a sweep to see if the blocks match the record?
                return false;
            }

            using (var tr = m_connection.BeginTransaction())
            {
                m_insertblocksetCommand.Transaction = tr;
                m_insertblocksetCommand.ExecuteNonQuery(null, size, filehash);

                blocksetid = m_connection.GetLastRowID(tr);

                long ix = 0;
                if (blocklistHashes != null)
                {
                    m_insertblocklistHashesCommand.SetParameterValue(0, blocksetid);
                    m_insertblocklistHashesCommand.Transaction = tr;
                    foreach (var bh in blocklistHashes)
                    {
                        m_insertblocklistHashesCommand.SetParameterValue(1, ix);
                        m_insertblocklistHashesCommand.SetParameterValue(2, bh);
                        m_insertblocklistHashesCommand.ExecuteNonQuery();
                    }
                }

                m_insertblocksetentryCommand.SetParameterValue(0, blocksetid);
                m_insertblocksetentryCommand.Transaction = tr;


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
        public bool AddMetadataset(string hash, long size, out long metadataid)
        {
            if (size > 0)
            {
                object r = m_findmetadatasetCommand.ExecuteScalar(null, hash, size);
                if (r != null)
                {
                    metadataid = Convert.ToInt64(r);
                    return false;
                }

                long blocksetid;
                AddBlockset(hash, size, (int)size, new string[] { hash }, null, out blocksetid);

                using (var tr = m_connection.BeginTransaction())
                {
                    m_insertmetadatasetCommand.Transaction = tr;
                    var c = m_insertmetadatasetCommand.ExecuteNonQuery(null, hash, size);
                    if (c != 1)
                        throw new Exception(string.Format("Unexpected result count: {0}, expected: {1}", c, 1));

                    metadataid = m_connection.GetLastRowID(tr);
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
        public void AddFile(string filename, DateTime scantime, long blocksetID, long metadataID)
        {
            m_insertfileCommand.SetParameterValue(1, filename);
            m_insertfileCommand.SetParameterValue(2, scantime);
            m_insertfileCommand.SetParameterValue(3, blocksetID);
            m_insertfileCommand.SetParameterValue(4, metadataID);
            m_insertfileCommand.ExecuteNonQuery();
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
        public bool GetFileEntry(string path, out long fileid, out long filesize, out DateTime lastScanned, out string hash, out string metahash, out long metasize, out IList<string> blockHashes)
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

        }

        public void AddUnmodifiedFile(long fileid, DateTime scantime)
        {
            long metadataID;
            long blocksetID;
            string path;

            m_selectfileidsCommand.SetParameterValue(0, fileid);
            using (var rd = m_selectfileidsCommand.ExecuteReader())
            {
                path = rd.GetValue(0).ToString();
                blocksetID = Convert.ToInt64(rd.GetValue(1));
                metadataID = Convert.ToInt64(rd.GetValue(2));
            }

            AddFile(path, scantime, blocksetID, metadataID);
        }

        public void AddDirectoryEntry(string path, long metadataID, DateTime scantime)
        {
            AddFile(path, scantime, FOLDER_BLOCKSET_ID, metadataID);
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

        public void AddSymlinkEntry(string path, long metadataID, DateTime scantime)
        {
            AddFile(path, scantime, SYMLINK_BLOCKSET_ID, metadataID);
        }
    }
}
