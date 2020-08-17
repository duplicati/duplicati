using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace Duplicati.Library.Main.Database
{

    internal class LocalBackupDatabase : LocalDatabase
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<LocalBackupDatabase>();

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

        private readonly System.Data.IDbCommand m_findfileCommand;
        private readonly System.Data.IDbCommand m_selectfilelastmodifiedCommand;
        private readonly System.Data.IDbCommand m_selectfilelastmodifiedWithSizeCommand;
        private readonly System.Data.IDbCommand m_selectfileHashCommand;
        private readonly System.Data.IDbCommand m_selectblocklistHashesCommand;

        private readonly System.Data.IDbCommand m_insertfileOperationCommand;
        private readonly System.Data.IDbCommand m_selectfilemetadatahashandsizeCommand;

        private Dictionary<string, long> m_blockCache;

        private long m_filesetId;

        private readonly bool m_logQueries;

        public LocalBackupDatabase(string path, Options options)
            : this(new LocalDatabase(path, "Backup", false), options)
        {
            this.ShouldCloseConnection = true;
        }

        public LocalBackupDatabase(LocalDatabase db, Options options)
            : base(db)
        {
            m_logQueries = options.ProfileAllDatabaseQueries;

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
            m_findfileCommand = m_connection.CreateCommand();
            m_selectfilelastmodifiedCommand = m_connection.CreateCommand();
            m_selectfilelastmodifiedWithSizeCommand = m_connection.CreateCommand();
            m_selectfileHashCommand = m_connection.CreateCommand();
            m_insertblocksetentryFastCommand = m_connection.CreateCommand();
            m_selectfilemetadatahashandsizeCommand = m_connection.CreateCommand();

            m_findblockCommand.CommandText = @"SELECT ""ID"" FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ?";
            m_findblockCommand.AddParameters(2);

            m_findblocksetCommand.CommandText = @"SELECT ""ID"" FROM ""Blockset"" WHERE ""Fullhash"" = ? AND ""Length"" = ?";
            m_findblocksetCommand.AddParameters(2);

            m_findmetadatasetCommand.CommandText = @"SELECT ""A"".""ID"" FROM ""Metadataset"" A, ""BlocksetEntry"" B, ""Block"" C WHERE ""A"".""BlocksetID"" = ""B"".""BlocksetID"" AND ""B"".""BlockID"" = ""C"".""ID"" AND ""C"".""Hash"" = ? AND ""C"".""Size"" = ?";
            m_findmetadatasetCommand.AddParameters(2);

            m_findfilesetCommand.CommandText = @"SELECT ""ID"" FROM ""FileLookup"" WHERE ""BlocksetID"" = ? AND ""MetadataID"" = ? AND ""Path"" = ? AND ""PrefixID"" = ?";
            m_findfilesetCommand.AddParameters(4);

            m_insertblockCommand.CommandText = @"INSERT INTO ""Block"" (""Hash"", ""VolumeID"", ""Size"") VALUES (?, ?, ?); SELECT last_insert_rowid();";
            m_insertblockCommand.AddParameters(3);

            m_insertfileOperationCommand.CommandText = @"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") VALUES (?, ?, ?)";
            m_insertfileOperationCommand.AddParameters(3);

            m_insertfileCommand.CommandText = @"INSERT INTO ""FileLookup"" (""PrefixID"", ""Path"",""BlocksetID"", ""MetadataID"") VALUES (?, ?, ? ,?); SELECT last_insert_rowid();";
            m_insertfileCommand.AddParameters(4);

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

            m_selectfilelastmodifiedCommand.CommandText = @"SELECT ""A"".""ID"", ""B"".""LastModified"" FROM (SELECT ""ID"" FROM ""FileLookup"" WHERE ""PrefixID"" = ? AND ""Path"" = ?) ""A"" CROSS JOIN ""FilesetEntry"" ""B"" WHERE ""A"".""ID"" = ""B"".""FileID"" AND ""B"".""FilesetID"" = ?";
            m_selectfilelastmodifiedCommand.AddParameters(3);

            m_selectfilelastmodifiedWithSizeCommand.CommandText = @"SELECT ""C"".""ID"", ""C"".""LastModified"", ""D"".""Length"" FROM (SELECT ""A"".""ID"", ""B"".""LastModified"", ""A"".""BlocksetID"" FROM (SELECT ""ID"", ""BlocksetID"" FROM ""FileLookup"" WHERE ""PrefixID"" = ? AND ""Path"" = ?) ""A"" CROSS JOIN ""FilesetEntry"" ""B"" WHERE ""A"".""ID"" = ""B"".""FileID"" AND ""B"".""FilesetID"" = ?) AS ""C"", ""Blockset"" AS ""D"" WHERE ""C"".""BlocksetID"" == ""D"".""ID"" ";
            m_selectfilelastmodifiedWithSizeCommand.AddParameters(3);

            m_selectfilemetadatahashandsizeCommand.CommandText = @"SELECT ""Blockset"".""Length"", ""Blockset"".""FullHash"" FROM ""Blockset"", ""Metadataset"", ""File"" WHERE ""File"".""ID"" = ? AND ""Blockset"".""ID"" = ""Metadataset"".""BlocksetID"" AND ""Metadataset"".""ID"" = ""File"".""MetadataID"" ";
            m_selectfilemetadatahashandsizeCommand.AddParameters(1);

            // Allow users to test on real-world data
            // to get feedback on potential performance
            int.TryParse(Environment.GetEnvironmentVariable("TEST_QUERY_VERSION"), out var testqueryversion);

            if (testqueryversion != 0)
                Logging.Log.WriteWarningMessage(LOGTAG, "TestFileQuery", null, "Using performance test query version {0} as the TEST_QUERY_VERSION environment variable is set", testqueryversion);

            // The original query (v==1) finds the most recent entry of the file in question, 
            // but it requires some large joins to extract the required information.
            // To speed it up, we use a slightly simpler approach that only looks at the
            // previous fileset, and uses information here.
            // If there is a case where a file is sometimes there and sometimes not
            // (i.e. filter file, remove filter) we will not find the file.
            // We currently use this faster version, 
            // but allow users to switch back via an environment variable
            // such that we can get performance feedback

            switch (testqueryversion)
            {
                // The query used in Duplicati until 2.0.3.9
                case 1:
                    m_findfileCommand.CommandText =
                        @" SELECT ""FileLookup"".""ID"" AS ""FileID"", ""FilesetEntry"".""Lastmodified"", ""FileBlockset"".""Length"", ""MetaBlockset"".""Fullhash"" AS ""Metahash"", ""MetaBlockset"".""Length"" AS ""Metasize"" " +
                        @"   FROM ""FileLookup"", ""FilesetEntry"", ""Fileset"", ""Blockset"" ""FileBlockset"", ""Metadataset"", ""Blockset"" ""MetaBlockset"" " +
                        @"  WHERE ""FileLookup"".""PrefixID"" = ? AND ""FileLookup"".""Path"" = ? " +
                        @"    AND ""FilesetEntry"".""FileID"" = ""FileLookup"".""ID"" AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID"" " +
                        @"    AND ""FileBlockset"".""ID"" = ""FileLookup"".""BlocksetID"" " +
                        @"    AND ""Metadataset"".""ID"" = ""FileLookup"".""MetadataID"" AND ""MetaBlockset"".""ID"" = ""Metadataset"".""BlocksetID"" " +
                        @"    AND ? IS NOT NULL" +
                        @"  ORDER BY ""Fileset"".""Timestamp"" DESC " +
                        @"  LIMIT 1 ";
                    break;

                // The fastest reported query in Duplicati 2.0.3.10, but with "LIMIT 1" added
                default:
                case 2:
                    var getLastFileEntryForPath =
                        @"SELECT ""A"".""ID"", ""B"".""LastModified"", ""A"".""BlocksetID"", ""A"".""MetadataID"" " +
                        @"  FROM (SELECT ""ID"", ""BlocksetID"", ""MetadataID"" FROM ""FileLookup"" WHERE ""PrefixID"" = ? AND ""Path"" = ?) ""A"" " +
                        @"  CROSS JOIN ""FilesetEntry"" ""B"" " +
                        @"  WHERE ""A"".""ID"" = ""B"".""FileID"" " +
                        @"    AND ""B"".""FilesetID"" = ? ";

                    m_findfileCommand.CommandText = string.Format(
                        @"SELECT ""C"".""ID"" AS ""FileID"", ""C"".""LastModified"", ""D"".""Length"", ""E"".""FullHash"" as ""Metahash"", ""E"".""Length"" AS ""Metasize"" " +
                        @"  FROM " +
                        @"  ({0}) AS ""C"", ""Blockset"" AS ""D"", ""Blockset"" AS ""E"", ""Metadataset"" ""F"" " +
                        @" WHERE ""C"".""BlocksetID"" == ""D"".""ID"" AND ""C"".""MetadataID"" == ""F"".""ID"" AND ""F"".""BlocksetID"" = ""E"".""ID"" " +
                        @" LIMIT 1",
                        getLastFileEntryForPath
                    );
                    break;

                // Potentially faster query: https://forum.duplicati.com/t/release-2-0-3-10-canary-2018-08-30/4497/25
                case 3:
                    m_findfileCommand.CommandText =
                        @"    SELECT FileLookup.ID as FileID, FilesetEntry.Lastmodified, FileBlockset.Length,  " +
                        @"           MetaBlockset.FullHash AS Metahash, MetaBlockset.Length as Metasize " +
                        @"      FROM FilesetEntry " +
                        @"INNER JOIN Fileset ON (FileSet.ID = FilesetEntry.FilesetID) " +
                        @"INNER JOIN FileLookup ON (FileLookup.ID = FilesetEntry.FileID) " +
                        @"INNER JOIN Metadataset ON (Metadataset.ID = FileLookup.MetadataID) " +
                        @"INNER JOIN Blockset AS MetaBlockset ON (MetaBlockset.ID = Metadataset.BlocksetID) " +
                        @" LEFT JOIN Blockset AS FileBlockset ON (FileBlockset.ID = FileLookup.BlocksetID) " +
                        @"     WHERE FileLookup.PrefixID = ? AND FileLookup.Path = ? AND FilesetID = ? " +
                        @"     LIMIT 1 ";
                    break;

                // The slow query used in Duplicati 2.0.3.10, but with "LIMIT 1" added
                case 4:
                    m_findfileCommand.CommandText =
                        @" SELECT ""FileLookup"".""ID"" AS ""FileID"", ""FilesetEntry"".""Lastmodified"", ""FileBlockset"".""Length"", ""MetaBlockset"".""Fullhash"" AS ""Metahash"", ""MetaBlockset"".""Length"" AS ""Metasize"" " +
                        @"   FROM ""FileLookup"", ""FilesetEntry"", ""Fileset"", ""Blockset"" ""FileBlockset"", ""Metadataset"", ""Blockset"" ""MetaBlockset"" " +
                        @"  WHERE ""FileLookup"".""PrefixID"" = ? AND ""FileLookup"".""Path"" = ? " +
                        @"    AND ""Fileset"".""ID"" = ? " +
                        @"    AND ""FilesetEntry"".""FileID"" = ""FileLookup"".""ID"" AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID"" " +
                        @"    AND ""FileBlockset"".""ID"" = ""FileLookup"".""BlocksetID"" " +
                        @"    AND ""Metadataset"".""ID"" = ""FileLookup"".""MetadataID"" AND ""MetaBlockset"".""ID"" = ""Metadataset"".""BlocksetID"" " +
                        @"  LIMIT 1 ";
                    break;

            }

            m_findfileCommand.AddParameters(3);

            m_selectfileHashCommand.CommandText = @"SELECT ""Blockset"".""Fullhash"" FROM ""Blockset"", ""FileLookup"" WHERE ""Blockset"".""ID"" = ""FileLookup"".""BlocksetID"" AND ""FileLookup"".""ID"" = ?  ";
            m_selectfileHashCommand.AddParameters(1);

            m_selectblocklistHashesCommand.CommandText = @"SELECT ""Hash"" FROM ""BlocklistHash"" WHERE ""BlocksetID"" = ? ORDER BY ""Index"" ASC ";
            m_selectblocklistHashesCommand.AddParameters(1);
        }

        /// <summary>
        /// Probes to see if a block already exists
        /// </summary>
        /// <param name="key">The block key</param>
        /// <param name="size">The size of the block</param>
        /// <returns>True if the block should be added to the current output</returns>
        public long FindBlockID(string key, long size, System.Data.IDbTransaction transaction = null)
        {
            m_findblockCommand.Transaction = transaction;
            m_findblockCommand.SetParameterValue(0, key);
            m_findblockCommand.SetParameterValue(1, size);
            return m_findblockCommand.ExecuteScalarInt64(m_logQueries, -1);
        }

        /// <summary>
        /// Adds a block to the local database, returning a value indicating if the value presents a new block
        /// </summary>
        /// <param name="key">The block key</param>
        /// <param name="size">The size of the block</param>
        /// <returns>True if the block should be added to the current output</returns>
        public bool AddBlock(string key, long size, long volumeid, System.Data.IDbTransaction transaction = null)
        {
            long exsize;

            if (m_blockCache != null && m_blockCache.TryGetValue(key, out exsize))
            {
                if (exsize == size)
                    return false;

                Logging.Log.WriteWarningMessage(LOGTAG, "HashCollisionsFound", null, "Found hash collision on {0}, sizes {1} vs {2}. Disabling cache from now on.", key, size, exsize);
                m_blockCache = null;
            }

            m_findblockCommand.Transaction = transaction;
            m_findblockCommand.SetParameterValue(0, key);
            m_findblockCommand.SetParameterValue(1, size);
            var r = m_findblockCommand.ExecuteScalarInt64(m_logQueries, -1);

            if (r == -1L)
            {
                m_insertblockCommand.Transaction = transaction;
                m_insertblockCommand.SetParameterValue(0, key);
                m_insertblockCommand.SetParameterValue(1, volumeid);
                m_insertblockCommand.SetParameterValue(2, size);
                m_insertblockCommand.ExecuteScalarInt64(m_logQueries);
                if (m_blockCache != null)
                    m_blockCache.Add(key, size);
                return true;
            }
            else
            {
                //Update lookup cache if required
                return false;
            }
        }


        /// <summary>
        /// Adds a blockset to the database, returns a value indicating if the blockset is new
        /// </summary>
        /// <param name="filehash">The hash of the blockset</param>
        /// <param name="size">The size of the blockset</param>
        /// <param name="hashes">The list of hashes</param>
        /// <param name="blocksetid">The id of the blockset, new or old</param>
        /// <returns>True if the blockset was created, false otherwise</returns>
        public bool AddBlockset(string filehash, long size, int blocksize, IEnumerable<string> hashes, IEnumerable<string> blocklistHashes, out long blocksetid, System.Data.IDbTransaction transaction = null)
        {
            m_findblocksetCommand.Transaction = transaction;
            blocksetid = m_findblocksetCommand.ExecuteScalarInt64(m_logQueries, null, -1, filehash, size);
            if (blocksetid != -1)
                return false; //Found it

            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                m_insertblocksetCommand.Transaction = tr.Parent;
                m_insertblocksetCommand.SetParameterValue(0, size);
                m_insertblocksetCommand.SetParameterValue(1, filehash);
                blocksetid = m_insertblocksetCommand.ExecuteScalarInt64(m_logQueries);

                long ix = 0;
                if (blocklistHashes != null)
                {
                    m_insertblocklistHashesCommand.SetParameterValue(0, blocksetid);
                    m_insertblocklistHashesCommand.Transaction = tr.Parent;
                    foreach (var bh in blocklistHashes)
                    {
                        m_insertblocklistHashesCommand.SetParameterValue(1, ix);
                        m_insertblocklistHashesCommand.SetParameterValue(2, bh);
                        m_insertblocklistHashesCommand.ExecuteNonQuery(m_logQueries);
                        ix++;
                    }
                }

                m_insertblocksetentryCommand.SetParameterValue(0, blocksetid);
                m_insertblocksetentryCommand.Transaction = tr.Parent;

                m_insertblocksetentryFastCommand.SetParameterValue(0, blocksetid);
                m_insertblocksetentryFastCommand.Transaction = tr.Parent;

                ix = 0;
                long remainsize = size;
                foreach (var h in hashes)
                {
                    var exsize = remainsize < blocksize ? remainsize : blocksize;
                    m_insertblocksetentryCommand.SetParameterValue(1, ix);
                    m_insertblocksetentryCommand.SetParameterValue(2, h);
                    m_insertblocksetentryCommand.SetParameterValue(3, exsize);
                    var c = m_insertblocksetentryCommand.ExecuteNonQuery(m_logQueries);
                    if (c != 1)
                    {
                        Logging.Log.WriteErrorMessage(LOGTAG, "CheckingErrorsForIssue1400", null, "Checking errors, related to #1400. Unexpected result count: {0}, expected {1}, hash: {2}, size: {3}, blocksetid: {4}, ix: {5}, fullhash: {6}, fullsize: {7}", c, 1, h, exsize, blocksetid, ix, filehash, size);
                        using (var cmd = m_connection.CreateCommand(tr.Parent))
                        {
                            var bid = cmd.ExecuteScalarInt64(@"SELECT ""ID"" FROM ""Block"" WHERE ""Hash"" = ?", -1, h);
                            if (bid == -1)
                                throw new Exception(string.Format("Could not find any blocks with the given hash: {0}", h));
                            foreach (var rd in cmd.ExecuteReaderEnumerable(@"SELECT ""Size"" FROM ""Block"" WHERE ""Hash"" = ?", h))
                                Logging.Log.WriteErrorMessage(LOGTAG, "FoundIssue1400Error", null, "Found block with ID {0} and hash {1} and size {2}", bid, h, rd.ConvertValueToInt64(0, -1));
                        }

                        throw new Exception(string.Format("Unexpected result count: {0}, expected {1}, check log for more messages", c, 1));
                    }

                    ix++;
                    remainsize -= blocksize;
                }

                tr.Commit();
            }

            return true;
        }

        /// <summary>
        /// Gets the metadataset ID from the filehash
        /// </summary>
        /// <returns><c>true</c>, if metadataset found, false if does not exist.</returns>
        /// <param name="filehash">The metadata hash.</param>
        /// <param name="size">The size of the metadata.</param>
        /// <param name="metadataid">The ID of the metadataset.</param>
        /// <param name="transaction">An optional transaction.</param>
        public bool GetMetadatasetID(string filehash, long size, out long metadataid, System.Data.IDbTransaction transaction = null)
        {
            if (size > 0)
            {
                m_findmetadatasetCommand.Transaction = transaction;
                metadataid = m_findmetadatasetCommand.ExecuteScalarInt64(m_logQueries, null, -1, filehash, size);
                return metadataid != -1;
            }

            metadataid = -2;
            return false;
        }

        /// <summary>
        /// Adds a metadata set to the database, and returns a value indicating if the record was new
        /// </summary>
        /// <param name="filehash">The metadata hash</param>
        /// <param name="size">The size of the metadata</param>
        /// <param name="transaction">The transaction to execute under</param>
        /// <param name="blocksetid">The id of the blockset to add</param>
        /// <param name="metadataid">The id of the metadata set</param>
        /// <returns>True if the set was added to the database, false otherwise</returns>
        public bool AddMetadataset(string filehash, long size, long blocksetid, out long metadataid, System.Data.IDbTransaction transaction = null)
        {
            if (GetMetadatasetID(filehash, size, out metadataid, transaction))
                return false;

            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                m_insertmetadatasetCommand.Transaction = tr.Parent;
                m_insertmetadatasetCommand.SetParameterValue(0, blocksetid);
                metadataid = m_insertmetadatasetCommand.ExecuteScalarInt64(m_logQueries);
                tr.Commit();
                return true;
            }
        }

        /// <summary>
        /// Adds a file record to the database
        /// </summary>
        /// <param name="pathprefixid">The path prefix ID</param>
        /// <param name="filename">The path to the file</param>
        /// <param name="lastmodified">The time the file was modified</param>
        /// <param name="blocksetID">The ID of the hashkey for the file</param>
        /// <param name="metadataID">The ID for the metadata</param>
        /// <param name="transaction">The transaction to use for insertion, or null for no transaction</param>
        public void AddFile(long pathprefixid, string filename, DateTime lastmodified, long blocksetID, long metadataID, System.Data.IDbTransaction transaction)
        {
            var fileidobj = -1L;
            m_findfilesetCommand.Transaction = transaction;
            m_findfilesetCommand.SetParameterValue(0, blocksetID);
            m_findfilesetCommand.SetParameterValue(1, metadataID);
            m_findfilesetCommand.SetParameterValue(2, filename);
            m_findfilesetCommand.SetParameterValue(3, pathprefixid);
            fileidobj = m_findfilesetCommand.ExecuteScalarInt64(m_logQueries);

            if (fileidobj == -1)
            {
                using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
                {
                    m_insertfileCommand.Transaction = tr.Parent;
                    m_insertfileCommand.SetParameterValue(0, pathprefixid);
                    m_insertfileCommand.SetParameterValue(1, filename);
                    m_insertfileCommand.SetParameterValue(2, blocksetID);
                    m_insertfileCommand.SetParameterValue(3, metadataID);
                    fileidobj = m_insertfileCommand.ExecuteScalarInt64(m_logQueries);
                    tr.Commit();
                }
            }

            m_insertfileOperationCommand.Transaction = transaction;
            m_insertfileOperationCommand.SetParameterValue(0, m_filesetId);
            m_insertfileOperationCommand.SetParameterValue(1, fileidobj);
            m_insertfileOperationCommand.SetParameterValue(2, lastmodified.ToUniversalTime().Ticks);
            m_insertfileOperationCommand.ExecuteNonQuery(m_logQueries);
        }

        /// <summary>
        /// Adds a file record to the database
        /// </summary>
        /// <param name="filename">The path to the file</param>
        /// <param name="lastmodified">The time the file was modified</param>
        /// <param name="blocksetID">The ID of the hashkey for the file</param>
        /// <param name="metadataID">The ID for the metadata</param>
        /// <param name="transaction">The transaction to use for insertion, or null for no transaction</param>
        public void AddFile(string filename, DateTime lastmodified, long blocksetID, long metadataID, System.Data.IDbTransaction transaction)
        {
            var split = SplitIntoPrefixAndName(filename);
            AddFile(GetOrCreatePathPrefix(split.Key, transaction), split.Value, lastmodified, blocksetID, metadataID, transaction);
        }

        public void AddUnmodifiedFile(long fileid, DateTime lastmodified, System.Data.IDbTransaction transaction = null)
        {
            m_insertfileOperationCommand.Transaction = transaction;
            m_insertfileOperationCommand.SetParameterValue(0, m_filesetId);
            m_insertfileOperationCommand.SetParameterValue(1, fileid);
            m_insertfileOperationCommand.SetParameterValue(2, lastmodified.ToUniversalTime().Ticks);
            m_insertfileOperationCommand.ExecuteNonQuery(m_logQueries);
        }

        public void AddDirectoryEntry(string path, long metadataID, DateTime lastmodified, System.Data.IDbTransaction transaction = null)
        {
            AddFile(path, lastmodified, FOLDER_BLOCKSET_ID, metadataID, transaction);
        }

        public void AddSymlinkEntry(string path, long metadataID, DateTime lastmodified, System.Data.IDbTransaction transaction = null)
        {
            AddFile(path, lastmodified, SYMLINK_BLOCKSET_ID, metadataID, transaction);
        }

        public long GetFileLastModified(long prefixid, string path, long filesetid, bool includeLength, out DateTime oldModified, out long length, System.Data.IDbTransaction transaction = null)
        {
            if (includeLength)
            {
                m_selectfilelastmodifiedWithSizeCommand.Transaction = transaction;
                m_selectfilelastmodifiedWithSizeCommand.SetParameterValue(0, prefixid);
                m_selectfilelastmodifiedWithSizeCommand.SetParameterValue(1, path);
                m_selectfilelastmodifiedWithSizeCommand.SetParameterValue(2, filesetid);
                using (var rd = m_selectfilelastmodifiedWithSizeCommand.ExecuteReader(m_logQueries, null))
                    if (rd.Read())
                    {
                        oldModified = new DateTime(rd.ConvertValueToInt64(1), DateTimeKind.Utc);
                        length = rd.ConvertValueToInt64(2);
                        return rd.ConvertValueToInt64(0);
                    }
            }
            else
            {
                m_selectfilelastmodifiedCommand.Transaction = transaction;
                m_selectfilelastmodifiedCommand.SetParameterValue(0, prefixid);
                m_selectfilelastmodifiedCommand.SetParameterValue(1, path);
                m_selectfilelastmodifiedCommand.SetParameterValue(2, filesetid);
                using (var rd = m_selectfilelastmodifiedCommand.ExecuteReader(m_logQueries, null))
                    if (rd.Read())
                    {
                        length = -1;
                        oldModified = new DateTime(rd.ConvertValueToInt64(1), DateTimeKind.Utc);
                        return rd.ConvertValueToInt64(0);
                    }

            }
            oldModified = new DateTime(0, DateTimeKind.Utc);
            length = -1;
            return -1;
        }


        public long GetFileEntry(long prefixid, string path, long filesetid, out DateTime oldModified, out long lastFileSize, out string oldMetahash, out long oldMetasize, System.Data.IDbTransaction transaction)
        {
            m_findfileCommand.SetParameterValue(0, prefixid);
            m_findfileCommand.SetParameterValue(1, path);
            m_findfileCommand.SetParameterValue(2, filesetid);
            m_findfileCommand.Transaction = transaction;

            using (var rd = m_findfileCommand.ExecuteReader())
                if (rd.Read())
                {
                    oldModified = new DateTime(rd.ConvertValueToInt64(1), DateTimeKind.Utc);
                    lastFileSize = rd.GetInt64(2);
                    oldMetahash = rd.GetString(3);
                    oldMetasize = rd.GetInt64(4);
                    return rd.ConvertValueToInt64(0);
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

        public Tuple<long, string> GetMetadataHashAndSizeForFile(long fileid, System.Data.IDbTransaction transaction)
        {
            m_selectfilemetadatahashandsizeCommand.SetParameterValue(0, fileid);
            m_selectfilemetadatahashandsizeCommand.Transaction = transaction;

            using (var rd = m_findfileCommand.ExecuteReader())
                if (rd.Read())
                    return new Tuple<long, string>(rd.ConvertValueToInt64(0), rd.ConvertValueToString(1));

            return null;
        }


        public string GetFileHash(long fileid, System.Data.IDbTransaction transaction)
        {
            m_selectfileHashCommand.SetParameterValue(0, fileid);
            m_selectfileHashCommand.Transaction = transaction;
            var r = m_selectfileHashCommand.ExecuteScalar(m_logQueries, null);
            if (r == null || r == DBNull.Value)
                return null;

            return r.ToString();
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        private long GetPreviousFilesetID(System.Data.IDbCommand cmd)
        {
            return GetPreviousFilesetID(cmd, OperationTimestamp, m_filesetId);
        }

        private long GetPreviousFilesetID(System.Data.IDbCommand cmd, DateTime timestamp, long filesetid)
        {
            var lastFilesetId = cmd.ExecuteScalarInt64(@"SELECT ""ID"" FROM ""Fileset"" WHERE ""Timestamp"" < ? AND ""ID"" != ? ORDER BY ""Timestamp"" DESC ", -1, Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(timestamp), filesetid);
            return lastFilesetId;
        }

        internal Tuple<long, long> GetLastBackupFileCountAndSize()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                var lastFilesetId = cmd.ExecuteScalarInt64(@"SELECT ""ID"" FROM ""Fileset"" ORDER BY ""Timestamp"" DESC LIMIT 1");
                var count = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM ""FileLookup"" INNER JOIN ""FilesetEntry"" ON ""FileLookup"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""FileLookup"".""BlocksetID"" NOT IN (?, ?)", -1, lastFilesetId, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID);
                var size = cmd.ExecuteScalarInt64(@"SELECT SUM(""Blockset"".""Length"") FROM ""FileLookup"", ""FilesetEntry"", ""Blockset"" WHERE ""FileLookup"".""ID"" = ""FilesetEntry"".""FileID"" AND ""FileLookup"".""BlocksetID"" = ""Blockset"".""ID"" AND ""FilesetEntry"".""FilesetID"" = ? AND ""FileLookup"".""BlocksetID"" NOT IN (?, ?)", -1, lastFilesetId, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID);

                return new Tuple<long, long>(count, size);
            }
        }

        internal void UpdateChangeStatistics(BackupResults results, System.Data.IDbTransaction transaction)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            {
                // TODO: Optimize these queries to not use the "File" view
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
                    var subqueryFiles = @"SELECT ""File"".""Path"" AS ""Path"", ""A"".""Fullhash"" AS ""Filehash"", ""B"".""Fullhash"" AS ""Metahash"" FROM ""File"", ""FilesetEntry"", ""Blockset"" A, ""Blockset"" B, ""Metadataset""  WHERE ""File"".""ID"" = ""FilesetEntry"".""FileID"" AND ""A"".""ID"" = ""File"".""BlocksetID"" AND ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""MetadataID"" = ""Metadataset"".""ID"" AND ""Metadataset"".""BlocksetID"" = ""B"".""ID"" ";

                    cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS " + subqueryFiles, tmpName1), lastFilesetId);
                    cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS " + subqueryFiles, tmpName2), m_filesetId);

                    results.AddedFiles = cmd.ExecuteScalarInt64(string.Format(@"SELECT COUNT(*) FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ? AND ""File"".""BlocksetID"" != ? AND ""File"".""BlocksetID"" != ? AND NOT ""File"".""Path"" IN (SELECT ""Path"" FROM ""{0}"")", tmpName1), 0, m_filesetId, FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID);
                    results.DeletedFiles = cmd.ExecuteScalarInt64(string.Format(@"SELECT COUNT(*) FROM ""{0}"" WHERE ""{0}"".""Path"" NOT IN (SELECT ""Path"" FROM ""File"" INNER JOIN ""FilesetEntry"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = ?)", tmpName1), 0, m_filesetId);
                    results.ModifiedFiles = cmd.ExecuteScalarInt64(string.Format(@"SELECT COUNT(*) FROM ""{0}"" A, ""{1}"" B WHERE ""A"".""Path"" = ""B"".""Path"" AND (""A"".""Filehash"" != ""B"".""Filehash"" OR ""A"".""Metahash"" != ""B"".""Metahash"")", tmpName1, tmpName2), 0);

                }
                finally
                {
                    try { cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"";", tmpName1)); }
                    catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "DisposeError", ex, "Dispose temp table error"); }
                    try { cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"";", tmpName2)); }
                    catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "DisposeError", ex, "Dispose temp table error"); }
                }
            }
        }

        /// <summary>
        /// Populates FilesetEntry table with files from previous fileset, which aren't 
        /// yet part of the new fileset, and which aren't on the (optional) list of <c>deleted</c> paths.
        /// </summary>
        /// <param name="transaction">Transaction</param>
        /// <param name="deleted">List of deleted paths, or null</param>
        public void AppendFilesFromPreviousSet(System.Data.IDbTransaction transaction, IEnumerable<string> deleted = null)
        {
            AppendFilesFromPreviousSet(transaction, deleted, m_filesetId, -1, OperationTimestamp);
        }

        /// <summary>
        /// Populates FilesetEntry table with files from previous fileset, which aren't 
        /// yet part of the new fileset, and which aren't on the (optional) list of <c>deleted</c> paths.
        /// </summary>
        /// <param name="transaction">Transaction</param>
        /// <param name="deleted">List of deleted paths, or null</param>
        /// <param name="filesetid">Current file-set ID</param>
        /// <param name="prevId">Source file-set ID</param>
        /// <param name="timestamp">If <c>filesetid</c> == -1, used to locate previous file-set</param>
        public void AppendFilesFromPreviousSet(System.Data.IDbTransaction transaction, IEnumerable<string> deleted, long filesetid, long prevId, DateTime timestamp)
        {
            using (var cmd = m_connection.CreateCommand())
            using (var cmdDelete = m_connection.CreateCommand())
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                long lastFilesetId = prevId < 0 ? GetPreviousFilesetID(cmd, timestamp, filesetid) : prevId;

                cmd.Transaction = tr.Parent;
                cmd.ExecuteNonQuery(@"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") SELECT ? AS ""FilesetID"", ""FileID"", ""Lastmodified"" FROM (SELECT DISTINCT ""FilesetID"", ""FileID"", ""Lastmodified"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = ? AND ""FileID"" NOT IN (SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = ?)) ", filesetid, lastFilesetId, filesetid);

                if (deleted != null)
                {
                    cmdDelete.Transaction = tr.Parent;
                    cmdDelete.CommandText = @"DELETE FROM ""FilesetEntry"" WHERE ""FilesetID"" = ? AND ""FileID"" IN (SELECT ""ID"" FROM ""File"" WHERE ""Path"" = ?) ";
                    cmdDelete.AddParameters(2);
                    cmdDelete.SetParameterValue(0, filesetid);

                    foreach (string s in deleted)
                    {
                        cmdDelete.SetParameterValue(1, s);
                        cmdDelete.ExecuteNonQuery();
                    }
                }

                tr.Commit();
            }
        }

        /// <summary>
        /// Populates FilesetEntry table with files from previous fileset, which aren't 
        /// yet part of the new fileset, and which aren't excluded by the (optional) exclusion 
        /// predicate.
        /// </summary>
        /// <param name="transaction">Transaction</param>
        /// <param name="exclusionPredicate">Optional exclusion predicate (true = exclude file)</param>
        public void AppendFilesFromPreviousSetWithPredicate(System.Data.IDbTransaction transaction, Func<string, long, bool> exclusionPredicate)
        {
            AppendFilesFromPreviousSetWithPredicate(transaction, exclusionPredicate, m_filesetId, -1, OperationTimestamp);
        }

        /// <summary>
        /// Populates FilesetEntry table with files from previous fileset, which aren't 
        /// yet part of the new fileset, and which aren't excluded by the (optional) exclusion 
        /// predicate.
        /// </summary>
        /// <param name="transaction">Transaction</param>
        /// <param name="exclusionPredicate">Optional exclusion predicate (true = exclude file)</param>
        /// <param name="fileSetId">Current fileset ID</param>
        /// <param name="prevFileSetId">Source fileset ID</param>
        /// <param name="timestamp">If <c>prevFileSetId</c> == -1, used to locate previous fileset</param>
        public void AppendFilesFromPreviousSetWithPredicate(System.Data.IDbTransaction transaction,
            Func<string, long, bool> exclusionPredicate, long fileSetId, long prevFileSetId, DateTime timestamp)
        {
            using (var cmd = m_connection.CreateCommand())
            using (var cmdDelete = m_connection.CreateCommand())
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                long lastFilesetId = prevFileSetId < 0 ? GetPreviousFilesetID(cmd, timestamp, fileSetId) : prevFileSetId;

                cmd.Transaction = tr.Parent;
                cmd.ExecuteNonQuery(@"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") SELECT ? AS ""FilesetID"", ""FileID"", ""Lastmodified"" FROM (SELECT DISTINCT ""FilesetID"", ""FileID"", ""Lastmodified"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = ? AND ""FileID"" NOT IN (SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = ?)) ",
                                    fileSetId,
                                    lastFilesetId,
                                    fileSetId);

                if (exclusionPredicate == null)
                    return;

                // prepare command for deleting new entries
                cmdDelete.Transaction = tr.Parent;
                cmdDelete.CommandText = @"DELETE FROM ""FilesetEntry"" WHERE ""FilesetID"" = ? AND ""FileID"" = ?";
                cmdDelete.AddParameters(2);
                cmdDelete.SetParameterValue(0, fileSetId);

                // enumerate files from previous set
                cmd.Transaction = tr.Parent;
                foreach (var row in cmd.ExecuteReaderEnumerable(
                    @"SELECT
	                      f.""Path"", fs.""FileID"", fs.""Lastmodified"", COALESCE(bs.""Length"", -1)
                      FROM (  SELECT DISTINCT ""FileID"", ""Lastmodified""
		                      FROM ""FilesetEntry""
		                      WHERE ""FilesetID"" = ?
		                      ) AS fs
                      LEFT JOIN ""File"" AS f ON fs.""FileID"" = f.""ID""
                      LEFT JOIN ""Blockset"" AS bs ON f.""BlocksetID"" = bs.""ID"";",
                    lastFilesetId))
                {
                    var path = row.GetString(0);
                    var size = row.GetInt64(3);

                    if (exclusionPredicate(path, size))
                    {
                        cmdDelete.SetParameterValue(1, row.GetInt64(1));
                        cmdDelete.ExecuteNonQuery();
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
            using (var cmd = m_connection.CreateCommand(transaction))
            {
                using (var rd = cmd.ExecuteReader(@"SELECT DISTINCT ""Fileset"".""ID"", ""Fileset"".""Timestamp"" FROM ""Fileset"", ""RemoteVolume"" WHERE ""RemoteVolume"".""ID"" = ""Fileset"".""VolumeID"" AND ""Fileset"".""ID"" IN (SELECT ""FilesetID"" FROM ""FilesetEntry"")  AND (""RemoteVolume"".""State"" = ""Uploading"" OR ""RemoteVolume"".""State"" = ""Temporary"")"))
                    while (rd.Read())
                    {
                        yield return new KeyValuePair<long, DateTime>(
                            rd.GetInt64(0),
                            ParseFromEpochSeconds(rd.GetInt64(1)).ToLocalTime()
                        );
                    }
            }
        }

        public RemoteVolumeEntry GetRemoteVolumeFromFilesetID(long filesetID, IDbTransaction transaction = null)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            using (var rd = cmd.ExecuteReader(@"SELECT ""RemoteVolume"".""ID"", ""Name"", ""Type"", ""Size"", ""Hash"", ""State"", ""DeleteGraceTime"" FROM ""RemoteVolume"", ""Fileset"" WHERE ""Fileset"".""VolumeID"" = ""RemoteVolume"".""ID"" AND ""Fileset"".""ID"" = ?", filesetID))
                if (rd.Read())
                    return new RemoteVolumeEntry(
                        rd.ConvertValueToInt64(0, -1),
                        rd.GetValue(1).ToString(),
                        (rd.GetValue(4) == null || rd.GetValue(4) == DBNull.Value) ? null : rd.GetValue(4).ToString(),
                        rd.ConvertValueToInt64(3, -1),
                        (RemoteVolumeType)Enum.Parse(typeof(RemoteVolumeType), rd.GetValue(2).ToString()),
                        (RemoteVolumeState)Enum.Parse(typeof(RemoteVolumeState), rd.GetValue(5).ToString()),
                        new DateTime(rd.ConvertValueToInt64(6, 0), DateTimeKind.Utc)
                    );
                else
                    return default(RemoteVolumeEntry);
        }

        public IEnumerable<string> GetTemporaryFilelistVolumeNames(bool latestOnly, IDbTransaction transaction = null)
        {
            var incompleteFilesetIDs = GetIncompleteFilesets(transaction).OrderBy(x => x.Value).Select(x => x.Key).ToArray();

            if (!incompleteFilesetIDs.Any())
                return Enumerable.Empty<string>();

            if (latestOnly)
                incompleteFilesetIDs = new long[] { incompleteFilesetIDs.Last() };

            var volumeNames = new List<string>();
            foreach (var filesetID in incompleteFilesetIDs)
                volumeNames.Add(GetRemoteVolumeFromFilesetID(filesetID).Name);

            return volumeNames;
        }

        public IEnumerable<string> GetMissingIndexFiles(System.Data.IDbTransaction transaction)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            using (var rd = cmd.ExecuteReader(@"SELECT ""Name"" FROM ""RemoteVolume"" WHERE ""Type"" = ? AND NOT ""ID"" IN (SELECT ""BlockVolumeID"" FROM ""IndexBlockLink"") AND ""State"" IN (?,?)", RemoteVolumeType.Blocks.ToString(), RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString()))
                while (rd.Read())
                    yield return rd.GetValue(0).ToString();
        }

        public void LinkFilesetToVolume(long filesetid, long volumeid, System.Data.IDbTransaction transaction)
        {
            using (var cmd = m_connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                var c = cmd.ExecuteNonQuery(@"UPDATE ""Fileset"" SET ""VolumeID"" = ? WHERE ""ID"" = ?", volumeid, filesetid);
                if (c != 1)
                    throw new Exception(string.Format("Failed to link filesetid {0} to volumeid {1}", filesetid, volumeid));
            }
        }

        public void MoveBlockToVolume(string blockkey, long size, long sourcevolumeid, long targetvolumeid, System.Data.IDbTransaction transaction)
        {
            using (var cmd = m_connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                var c = cmd.ExecuteNonQuery(@"UPDATE ""Block"" SET ""VolumeID"" = ? WHERE ""Hash"" = ? AND ""Size"" = ? AND ""VolumeID"" = ? ", targetvolumeid, blockkey, size, sourcevolumeid);
                if (c != 1)
                    throw new Exception(string.Format("Failed to move block {0}:{1} from volume {2}, count: {3}", blockkey, size, sourcevolumeid, c));
            }
        }

        public void SafeDeleteRemoteVolume(string name, System.Data.IDbTransaction transaction)
        {
            var volumeid = GetRemoteVolumeID(name, transaction);

            using (var cmd = m_connection.CreateCommand(transaction))
            {
                var c = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM ""Block"" WHERE ""VolumeID"" = ? ", -1, volumeid);
                if (c != 0)
                    throw new Exception(string.Format("Failed to safe-delete volume {0}, blocks: {1}", name, c));

                RemoveRemoteVolume(name, transaction);
            }
        }

        public string[] GetBlocklistHashes(string name, System.Data.IDbTransaction transaction)
        {
            var volumeid = GetRemoteVolumeID(name, transaction);
            using (var cmd = m_connection.CreateCommand(transaction))
            {
                // Grab the strings and return as array to avoid concurrent access to the IEnumerable
                return cmd.ExecuteReaderEnumerable(
                    @"SELECT DISTINCT ""Block"".""Hash"" FROM ""Block"" WHERE ""Block"".""VolumeID"" = ? AND ""Block"".""Hash"" IN (SELECT ""Hash"" FROM ""BlocklistHash"")", volumeid)
                    .Select(x => x.ConvertValueToString(0))
                    .ToArray();
            }
        }

        public string GetFirstPath()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                cmd.CommandText = @"SELECT ""Path"" FROM ""File"" ORDER BY LENGTH(""Path"") DESC LIMIT 1";
                var v0 = cmd.ExecuteScalar();
                if (v0 == null || v0 == DBNull.Value)
                    return null;

                return v0.ToString();
            }
        }

        /// <summary>
        /// Retrieves change journal data for file set
        /// </summary>
        /// <param name="fileSetId">Fileset-ID</param>
        public IEnumerable<Interface.USNJournalDataEntry> GetChangeJournalData(long fileSetId)
        {
            var data = new List<Interface.USNJournalDataEntry>();

            using (var cmd = m_connection.CreateCommand())
            using (var rd =
                cmd.ExecuteReader(
                    @"SELECT ""VolumeName"", ""JournalID"", ""NextUSN"", ""ConfigHash"" FROM ""ChangeJournalData"" WHERE ""FilesetID"" = ?",
                    fileSetId))
            {
                while (rd.Read())
                {
                    data.Add(new Interface.USNJournalDataEntry
                    {
                        Volume = rd.ConvertValueToString(0),
                        JournalId = rd.ConvertValueToInt64(1),
                        NextUsn = rd.ConvertValueToInt64(2),
                        ConfigHash = rd.ConvertValueToString(3)
                    });
                }

            }

            return data;
        }

        /// <summary>
        /// Adds NTFS change journal data for file set and volume
        /// </summary>
        /// <param name="data">Data to add</param>
        /// <param name="transaction">An optional external transaction</param>
        public void CreateChangeJournalData(IEnumerable<Interface.USNJournalDataEntry> data, System.Data.IDbTransaction transaction = null)
        {
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                foreach (var entry in data)
                {
                    using (var cmd = m_connection.CreateCommand())
                    {
                        cmd.Transaction = tr.Parent;
                        var c = cmd.ExecuteNonQuery(
                            @"INSERT INTO ""ChangeJournalData"" (""FilesetID"", ""VolumeName"", ""JournalID"", ""NextUSN"", ""ConfigHash"") VALUES (?, ?, ?, ?, ?);",
                            m_filesetId, entry.Volume, entry.JournalId, entry.NextUsn, entry.ConfigHash);

                        if (c != 1)
                            throw new Exception("Unable to add change journal entry");
                    }
                }

                tr.Commit();
            }
        }

        /// <summary>
        /// Adds NTFS change journal data for file set and volume
        /// </summary>
        /// <param name="data">Data to add</param>
        /// <param name="fileSetId">Existing file set to update</param>
        /// <param name="transaction">An optional external transaction</param>
        public void UpdateChangeJournalData(IEnumerable<Interface.USNJournalDataEntry> data, long fileSetId, System.Data.IDbTransaction transaction = null)
        {
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                foreach (var entry in data)
                {
                    using (var cmd = m_connection.CreateCommand())
                    {
                        cmd.Transaction = tr.Parent;
                        cmd.ExecuteNonQuery(
                            @"UPDATE ""ChangeJournalData"" SET ""NextUSN"" = ? WHERE ""FilesetID"" = ? AND ""VolumeName"" = ? AND ""JournalID"" = ?;",
                            entry.NextUsn, fileSetId, entry.Volume, entry.JournalId);
                    }
                }

                tr.Commit();
            }
        }
        
    }
}
