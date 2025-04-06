// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;

namespace Duplicati.Library.Main.Database
{

    internal class LocalBackupDatabase : LocalDatabase
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<LocalBackupDatabase>();

        private readonly IDbCommand m_findblockCommand;
        private readonly IDbCommand m_findblocksetCommand;
        private readonly IDbCommand m_findfilesetCommand;
        private readonly IDbCommand m_findmetadatasetCommand;

        private readonly IDbCommand m_insertblockCommand;

        private readonly IDbCommand m_insertfileCommand;

        private readonly IDbCommand m_insertblocksetCommand;
        private readonly IDbCommand m_insertblocksetentryCommand;
        private readonly IDbCommand m_insertblocklistHashesCommand;

        private readonly IDbCommand m_insertmetadatasetCommand;

        private readonly IDbCommand m_findfileCommand;
        private readonly IDbCommand m_selectfilelastmodifiedCommand;
        private readonly IDbCommand m_selectfilelastmodifiedWithSizeCommand;
        private readonly IDbCommand m_selectfileHashCommand;

        private readonly IDbCommand m_insertfileOperationCommand;
        private readonly IDbCommand m_selectfilemetadatahashandsizeCommand;
        private readonly IDbCommand m_getfirstfilesetwithblockinblockset;

        private HashSet<string> m_blocklistHashes;

        /// <summary>
        /// The temporary table with deleted blocks that can be re-used; null if not table is used
        /// </summary>
        private string? m_tempDeletedBlockTable;
        /// <summary>
        /// The in-mmeory lookup for deleted blocks; null if in-memory lookup is not used
        /// </summary>
        private Dictionary<string, Dictionary<long, long>>? m_deletedBlockLookup;
        /// <summary>
        /// The command used to move deleted blocks to the main block table; null if not used
        /// </summary>
        private readonly IDbCommand? m_moveblockfromdeletedCommand;
        /// <summary>
        /// The command used to find blocks in the deleted blocks table; null if not used
        /// </summary>
        private readonly IDbCommand? m_findindeletedCommand;

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

            m_findblockCommand = m_connection.CreateCommand(@"SELECT ""ID"" FROM ""Block"" WHERE ""Hash"" = @hash AND ""Size"" = @Size");
            m_findblocksetCommand = m_connection.CreateCommand(@"SELECT ""ID"" FROM ""Blockset"" WHERE ""Fullhash"" = @Fullhash AND ""Length"" = @Length");
            m_findmetadatasetCommand = m_connection.CreateCommand(@"SELECT ""A"".""ID"" FROM ""Metadataset"" A, ""BlocksetEntry"" B, ""Block"" C WHERE ""A"".""BlocksetID"" = ""B"".""BlocksetID"" AND ""B"".""BlockID"" = ""C"".""ID"" AND ""C"".""Hash"" = @Hash AND ""C"".""Size"" = @Size");
            m_findfilesetCommand = m_connection.CreateCommand(@"SELECT ""ID"" FROM ""FileLookup"" WHERE ""BlocksetID"" = @BlocksetId AND ""MetadataID"" = @MetadataId AND ""Path"" = @Path AND ""PrefixID"" = @PrefixId");
            m_insertblockCommand = m_connection.CreateCommand(@"INSERT INTO ""Block"" (""Hash"", ""VolumeID"", ""Size"") VALUES (@Hash, @VolumeId, @Size); SELECT last_insert_rowid();");
            m_insertfileOperationCommand = m_connection.CreateCommand(@"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") VALUES (@FilesetId, @FileId, @LastModified)");
            m_insertfileCommand = m_connection.CreateCommand(@"INSERT INTO ""FileLookup"" (""PrefixID"", ""Path"",""BlocksetID"", ""MetadataID"") VALUES (@PrefixId, @Path, @BlocksetId, @MetadataId); SELECT last_insert_rowid();");
            m_insertblocksetCommand = m_connection.CreateCommand(@"INSERT INTO ""Blockset"" (""Length"", ""FullHash"") VALUES (@Length, @Fullhash); SELECT last_insert_rowid();");
            m_insertblocksetentryCommand = m_connection.CreateCommand(@"INSERT INTO ""BlocksetEntry"" (""BlocksetID"", ""Index"", ""BlockID"") SELECT @BlocksetId AS A, @Index AS B, ""ID"" FROM ""Block"" WHERE ""Hash"" = @Hash AND ""Size"" = @Size");
            m_insertblocklistHashesCommand = m_connection.CreateCommand(@"INSERT INTO ""BlocklistHash"" (""BlocksetID"", ""Index"", ""Hash"") VALUES (@BlocksetId, @Index, @Hash)");
            m_insertmetadatasetCommand = m_connection.CreateCommand(@"INSERT INTO ""Metadataset"" (""BlocksetID"") VALUES (@BlocksetId); SELECT last_insert_rowid();");
            m_selectfilelastmodifiedCommand = m_connection.CreateCommand(@"SELECT ""A"".""ID"", ""B"".""LastModified"" FROM (SELECT ""ID"" FROM ""FileLookup"" WHERE ""PrefixID"" = @PrefixId AND ""Path"" = @Path) ""A"" CROSS JOIN ""FilesetEntry"" ""B"" WHERE ""A"".""ID"" = ""B"".""FileID"" AND ""B"".""FilesetID"" = @FilesetId");
            m_selectfilelastmodifiedWithSizeCommand = m_connection.CreateCommand(@"SELECT ""C"".""ID"", ""C"".""LastModified"", ""D"".""Length"" FROM (SELECT ""A"".""ID"", ""B"".""LastModified"", ""A"".""BlocksetID"" FROM (SELECT ""ID"", ""BlocksetID"" FROM ""FileLookup"" WHERE ""PrefixID"" = @PrefixId AND ""Path"" = @Path) ""A"" CROSS JOIN ""FilesetEntry"" ""B"" WHERE ""A"".""ID"" = ""B"".""FileID"" AND ""B"".""FilesetID"" = @FilesetId) AS ""C"", ""Blockset"" AS ""D"" WHERE ""C"".""BlocksetID"" == ""D"".""ID"" ");
            m_selectfilemetadatahashandsizeCommand = m_connection.CreateCommand(@"SELECT ""Blockset"".""Length"", ""Blockset"".""FullHash"" FROM ""Blockset"", ""Metadataset"", ""File"" WHERE ""File"".""ID"" = @FileId AND ""Blockset"".""ID"" = ""Metadataset"".""BlocksetID"" AND ""Metadataset"".""ID"" = ""File"".""MetadataID"" ");

            // Experimental toggling of the deleted block cache
            // If the value is less than zero, the lookup is disabled
            // meaning that deleted blocks are never reused (same as 2.1.0.5 and earlier)
            // A value of zero disables the in-memory cache, always using a temporary table
            // Any other value is the size of the in-memory cache
            // If the number of deleted blocks exceed the cache size, a temporary table is used
            var deletedBlockCacheSize = Environment.GetEnvironmentVariable("DUPLICATI_DELETEDBLOCKCACHESIZE");
            if (!long.TryParse(deletedBlockCacheSize, out var deletedBlockCacheSizeLong))
                deletedBlockCacheSizeLong = 10000;

            if (deletedBlockCacheSizeLong >= 0)
            {
                using (var cmd = m_connection.CreateCommand())
                {
                    m_tempDeletedBlockTable = "DeletedBlock-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                    cmd.SetCommandAndParameters(FormatInvariant($@"CREATE TEMPORARY TABLE ""{m_tempDeletedBlockTable}"" AS ")
                        + @$"SELECT MAX(""ID"") AS ""ID"", ""Hash"", ""Size"" FROM ""DeletedBlock"" WHERE ""VolumeID"" IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""State"" NOT IN (@States)) GROUP BY ""Hash"", ""Size""")
                        .ExpandInClauseParameter("@States", [RemoteVolumeState.Deleted, RemoteVolumeState.Deleting])
                        .ExecuteNonQuery();

                    var deletedBlocks = cmd.ExecuteScalarInt64(FormatInvariant(@$"SELECT COUNT(*) FROM ""{m_tempDeletedBlockTable}"""), 0);

                    // There are no deleted blocks, so we can drop the table
                    if (deletedBlocks == 0)
                    {
                        cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE ""{m_tempDeletedBlockTable}"""));
                        m_tempDeletedBlockTable = null;

                    }
                    // The deleted blocks are small enough to fit in memory
                    else if (deletedBlocks <= deletedBlockCacheSizeLong)
                    {
                        m_deletedBlockLookup = new Dictionary<string, Dictionary<long, long>>();
                        using (var reader = cmd.ExecuteReader(FormatInvariant(@$"SELECT ""ID"", ""Hash"", ""Size"" FROM ""{m_tempDeletedBlockTable}""")))
                            while (reader.Read())
                            {
                                var id = reader.ConvertValueToInt64(0);
                                var hash = reader.ConvertValueToString(1) ?? throw new Exception("Hash is null");
                                var size = reader.ConvertValueToInt64(2);

                                if (!m_deletedBlockLookup.TryGetValue(hash, out var sizes))
                                    m_deletedBlockLookup[hash] = sizes = new Dictionary<long, long>();
                                sizes[size] = id;
                            }

                        cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE ""{m_tempDeletedBlockTable}"""));
                        m_tempDeletedBlockTable = null;
                    }
                    // The deleted blocks are too large to fit in memory, so we use a temporary table
                    else
                    {
                        cmd.ExecuteNonQuery(FormatInvariant($@"CREATE UNIQUE INDEX ""unique_{m_tempDeletedBlockTable}"" ON ""{m_tempDeletedBlockTable}"" (""Hash"", ""Size"")"));
                        m_findindeletedCommand = m_connection.CreateCommand(FormatInvariant($@"SELECT ""ID"" FROM ""{m_tempDeletedBlockTable}"" WHERE ""Hash"" = @Hash AND ""Size"" = @Size"));
                        m_moveblockfromdeletedCommand = m_connection.CreateCommand(string.Join(";",
                            @"INSERT INTO ""Block"" (""Hash"", ""Size"", ""VolumeID"") SELECT ""Hash"", ""Size"", ""VolumeID"" FROM ""DeletedBlock"" WHERE ""ID"" = @DeletedBlockId LIMIT 1",
                            @"DELETE FROM ""DeletedBlock"" WHERE ""ID"" = @DeletedBlockId",
                            FormatInvariant(@$"DELETE FROM {m_tempDeletedBlockTable} WHERE ""ID"" = @DeletedBlockId"),
                            "SELECT last_insert_rowid()"));

                    }

                    if (deletedBlocks > 0)
                    {
                        m_moveblockfromdeletedCommand = m_connection.CreateCommand(string.Join(";",
                            @"INSERT INTO ""Block"" (""Hash"", ""Size"", ""VolumeID"") SELECT ""Hash"", ""Size"", ""VolumeID"" FROM ""DeletedBlock"" WHERE ""ID"" = @DeletedBlockId LIMIT 1",
                            @"DELETE FROM ""DeletedBlock"" WHERE ""ID"" = @DeletedBlockId",
                            "SELECT last_insert_rowid()"));
                    }

                }
            }

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

            string findQuery;
            switch (testqueryversion)
            {
                // The query used in Duplicati until 2.0.3.9
                case 1:
                    findQuery =
                        @" SELECT ""FileLookup"".""ID"" AS ""FileID"", ""FilesetEntry"".""Lastmodified"", ""FileBlockset"".""Length"", ""MetaBlockset"".""Fullhash"" AS ""Metahash"", ""MetaBlockset"".""Length"" AS ""Metasize"" " +
                        @"   FROM ""FileLookup"", ""FilesetEntry"", ""Fileset"", ""Blockset"" ""FileBlockset"", ""Metadataset"", ""Blockset"" ""MetaBlockset"" " +
                        @"  WHERE ""FileLookup"".""PrefixID"" = @PrefixId AND ""FileLookup"".""Path"" = @Path " +
                        @"    AND ""FilesetEntry"".""FileID"" = ""FileLookup"".""ID"" AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID"" " +
                        @"    AND ""FileBlockset"".""ID"" = ""FileLookup"".""BlocksetID"" " +
                        @"    AND ""Metadataset"".""ID"" = ""FileLookup"".""MetadataID"" AND ""MetaBlockset"".""ID"" = ""Metadataset"".""BlocksetID"" " +
                        @"    AND @FilesetId IS NOT NULL" +
                        @"  ORDER BY ""Fileset"".""Timestamp"" DESC " +
                        @"  LIMIT 1 ";
                    break;

                // The fastest reported query in Duplicati 2.0.3.10, but with "LIMIT 1" added
                default:
                case 2:
                    var getLastFileEntryForPath =
                        @"SELECT ""A"".""ID"", ""B"".""LastModified"", ""A"".""BlocksetID"", ""A"".""MetadataID"" " +
                        @"  FROM (SELECT ""ID"", ""BlocksetID"", ""MetadataID"" FROM ""FileLookup"" WHERE ""PrefixID"" = @PrefixId AND ""Path"" = @Path) ""A"" " +
                        @"  CROSS JOIN ""FilesetEntry"" ""B"" " +
                        @"  WHERE ""A"".""ID"" = ""B"".""FileID"" " +
                        @"    AND ""B"".""FilesetID"" = @FilesetId ";

                    findQuery = FormatInvariant($@"SELECT ""C"".""ID"" AS ""FileID"", ""C"".""LastModified"", ""D"".""Length"", ""E"".""FullHash"" as ""Metahash"", ""E"".""Length"" AS ""Metasize""
FROM ({getLastFileEntryForPath}) AS ""C"", ""Blockset"" AS ""D"", ""Blockset"" AS ""E"", ""Metadataset"" ""F"" 
WHERE ""C"".""BlocksetID"" == ""D"".""ID"" AND ""C"".""MetadataID"" == ""F"".""ID"" AND ""F"".""BlocksetID"" = ""E"".""ID"" 
LIMIT 1");
                    break;

                // Potentially faster query: https://forum.duplicati.com/t/release-2-0-3-10-canary-2018-08-30/4497/25
                case 3:
                    findQuery =
                        @"    SELECT FileLookup.ID as FileID, FilesetEntry.Lastmodified, FileBlockset.Length,  " +
                        @"           MetaBlockset.FullHash AS Metahash, MetaBlockset.Length as Metasize " +
                        @"      FROM FilesetEntry " +
                        @"INNER JOIN Fileset ON (FileSet.ID = FilesetEntry.FilesetID) " +
                        @"INNER JOIN FileLookup ON (FileLookup.ID = FilesetEntry.FileID) " +
                        @"INNER JOIN Metadataset ON (Metadataset.ID = FileLookup.MetadataID) " +
                        @"INNER JOIN Blockset AS MetaBlockset ON (MetaBlockset.ID = Metadataset.BlocksetID) " +
                        @" LEFT JOIN Blockset AS FileBlockset ON (FileBlockset.ID = FileLookup.BlocksetID) " +
                        @"     WHERE FileLookup.PrefixID = @PrefixId AND FileLookup.Path = @Path AND FilesetID = @FilesetId " +
                        @"     LIMIT 1 ";
                    break;

                // The slow query used in Duplicati 2.0.3.10, but with "LIMIT 1" added
                case 4:
                    findQuery =
                        @" SELECT ""FileLookup"".""ID"" AS ""FileID"", ""FilesetEntry"".""Lastmodified"", ""FileBlockset"".""Length"", ""MetaBlockset"".""Fullhash"" AS ""Metahash"", ""MetaBlockset"".""Length"" AS ""Metasize"" " +
                        @"   FROM ""FileLookup"", ""FilesetEntry"", ""Fileset"", ""Blockset"" ""FileBlockset"", ""Metadataset"", ""Blockset"" ""MetaBlockset"" " +
                        @"  WHERE ""FileLookup"".""PrefixID"" = @PrefixId AND ""FileLookup"".""Path"" = @Path " +
                        @"    AND ""Fileset"".""ID"" = @FilesetId " +
                        @"    AND ""FilesetEntry"".""FileID"" = ""FileLookup"".""ID"" AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID"" " +
                        @"    AND ""FileBlockset"".""ID"" = ""FileLookup"".""BlocksetID"" " +
                        @"    AND ""Metadataset"".""ID"" = ""FileLookup"".""MetadataID"" AND ""MetaBlockset"".""ID"" = ""Metadataset"".""BlocksetID"" " +
                        @"  LIMIT 1 ";
                    break;

            }

            m_findfileCommand = m_connection.CreateCommand(findQuery);

            m_selectfileHashCommand = m_connection.CreateCommand(@"SELECT ""Blockset"".""Fullhash"" FROM ""Blockset"", ""FileLookup"" WHERE ""Blockset"".""ID"" = ""FileLookup"".""BlocksetID"" AND ""FileLookup"".""ID"" = @FileId  ");
            m_getfirstfilesetwithblockinblockset = m_connection.CreateCommand(@"SELECT MIN(""FilesetEntry"".""FilesetID"") FROM ""FilesetEntry"" WHERE  ""FilesetEntry"".""FileID"" IN (
SELECT ""File"".""ID"" FROM ""File"" WHERE ""File"".""BlocksetID"" IN(
SELECT ""BlocklistHash"".""BlocksetID"" FROM ""BlocklistHash"" WHERE ""BlocklistHash"".""Hash"" = @Hash))");

            m_blocklistHashes = new HashSet<string>();
        }

        /// <summary>
        /// Probes to see if a block already exists
        /// </summary>
        /// <param name="key">The block key</param>
        /// <param name="size">The size of the block</param>
        /// <returns>True if the block should be added to the current output</returns>
        public long FindBlockID(string key, long size, IDbTransaction? transaction = null)
        {
            return m_findblockCommand.SetTransaction(transaction)
                .SetParameterValue("@Hash", key)
                .SetParameterValue("@Size", size)
                .ExecuteScalarInt64(m_logQueries, -1);
        }

        /// <summary>
        /// Adds a block to the local database, returning a value indicating if the value presents a new block
        /// </summary>
        /// <param name="key">The block key</param>
        /// <param name="size">The size of the block</param>
        /// <returns>True if the block should be added to the current output</returns>
        public bool AddBlock(string key, long size, long volumeid, IDbTransaction? transaction = null)
        {
            var r = FindBlockID(key, size, transaction);
            if (r == -1L)
            {
                if (m_moveblockfromdeletedCommand != null)
                {
                    if (m_deletedBlockLookup != null)
                    {
                        if (m_deletedBlockLookup.TryGetValue(key, out var sizes))
                            if (sizes.TryGetValue(size, out var id))
                            {
                                m_moveblockfromdeletedCommand.SetTransaction(transaction)
                                    .SetParameterValue("@DeletedBlockId", id)
                                    .ExecuteNonQuery(m_logQueries);

                                sizes.Remove(size);
                                if (sizes.Count == 0)
                                    m_deletedBlockLookup.Remove(key);
                                return false;
                            }
                    }
                    else if (m_findindeletedCommand != null)
                    {
                        // No transaction on the temporary table
                        var id = m_findindeletedCommand
                            .SetParameterValue("@Hash", key)
                            .SetParameterValue("@Size", size)
                            .ExecuteScalarInt64(m_logQueries, -1);

                        if (id != -1)
                        {
                            var c = m_moveblockfromdeletedCommand.SetTransaction(transaction)
                                .SetParameterValue("@DeletedBlockId", id)
                                .ExecuteNonQuery(m_logQueries);

                            if (c != 2)
                                throw new Exception($"Failed to move block {key} with size {size}, result count: {c}");

                            // We do not clean up the temporary table, as the regular block lookup should now find it
                            return false;
                        }
                    }
                }

                var ins = m_insertblockCommand.SetTransaction(transaction)
                    .SetParameterValue("@Hash", key)
                    .SetParameterValue("@VolumeId", volumeid)
                    .SetParameterValue("@Size", size)
                    .ExecuteNonQuery(m_logQueries);
                if (ins != 1)
                    throw new Exception($"Failed to insert block {key} with size {size}, result count: {ins}");

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
        public bool AddBlockset(string filehash, long size, int blocksize, IEnumerable<string> hashes, IEnumerable<string> blocklistHashes, out long blocksetid, IDbTransaction? transaction = null)
        {
            blocksetid = m_findblocksetCommand.SetTransaction(transaction)
                .SetParameterValue("@Fullhash", filehash)
                .SetParameterValue("@Length", size)
                .ExecuteScalarInt64(m_logQueries, null, -1);
            if (blocksetid != -1)
                return false; //Found it

            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                blocksetid = m_insertblocksetCommand.SetTransaction(tr.Parent)
                    .SetParameterValue("@Length", size)
                    .SetParameterValue("@Fullhash", filehash)
                    .ExecuteScalarInt64(m_logQueries);

                long ix = 0;
                if (blocklistHashes != null)
                {
                    m_insertblocklistHashesCommand.SetTransaction(tr.Parent)
                        .SetParameterValue("@BlocksetId", blocksetid);

                    foreach (var bh in blocklistHashes)
                    {
                        var c = m_insertblocklistHashesCommand.SetParameterValue("@Index", ix)
                            .SetParameterValue("@Hash", bh)
                            .ExecuteNonQuery(m_logQueries);
                        if (c != 1)
                            throw new Exception($"Failed to insert blocklist hash {bh} for blockset {blocksetid}, result count: {c}");
                        ix++;
                    }
                }

                m_insertblocksetentryCommand.SetTransaction(tr.Parent)
                    .SetParameterValue("@BlocksetId", blocksetid);

                ix = 0;
                long remainsize = size;
                foreach (var h in hashes)
                {
                    var exsize = remainsize < blocksize ? remainsize : blocksize;
                    var c = m_insertblocksetentryCommand.SetParameterValue("@Index", ix)
                        .SetParameterValue("@Hash", h)
                        .SetParameterValue("@Size", exsize)
                        .ExecuteNonQuery(m_logQueries);
                    if (c != 1)
                    {
                        Logging.Log.WriteErrorMessage(LOGTAG, "CheckingErrorsForIssue1400", null, "Checking errors, related to #1400. Unexpected result count: {0}, expected {1}, hash: {2}, size: {3}, blocksetid: {4}, ix: {5}, fullhash: {6}, fullsize: {7}", c, 1, h, exsize, blocksetid, ix, filehash, size);
                        using (var cmd = m_connection.CreateCommand(tr.Parent))
                        {
                            var bid = cmd.SetCommandAndParameters(@"SELECT ""ID"" FROM ""Block"" WHERE ""Hash"" = @Hash")
                                .SetParameterValue("@Hash", h)
                                .ExecuteScalarInt64(-1);
                            if (bid == -1)
                                throw new Exception($"Could not find any blocks with the given hash: {h}");
                            cmd.SetCommandAndParameters(@"SELECT ""Size"" FROM ""Block"" WHERE ""Hash"" = @Hash")
                                .SetParameterValue("@Hash", h);
                            foreach (var rd in cmd.ExecuteReaderEnumerable())
                                Logging.Log.WriteErrorMessage(LOGTAG, "FoundIssue1400Error", null, "Found block with ID {0} and hash {1} and size {2}", bid, h, rd.ConvertValueToInt64(0, -1));
                        }

                        throw new Exception($"Unexpected result count: {c}, expected {1}, check log for more messages");
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
        public bool GetMetadatasetID(string filehash, long size, out long metadataid, IDbTransaction? transaction = null)
        {
            if (size > 0)
            {
                metadataid = m_findmetadatasetCommand.SetTransaction(transaction)
                    .SetParameterValue("@Hash", filehash)
                    .SetParameterValue("@Size", size)
                    .ExecuteScalarInt64(m_logQueries, null, -1);
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
        public bool AddMetadataset(string filehash, long size, long blocksetid, out long metadataid, IDbTransaction? transaction = null)
        {
            if (GetMetadatasetID(filehash, size, out metadataid, transaction))
                return false;

            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                metadataid = m_insertmetadatasetCommand.SetTransaction(tr.Parent)
                    .SetParameterValue("@BlocksetId", blocksetid)
                    .ExecuteScalarInt64(m_logQueries);
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
        public void AddFile(long pathprefixid, string filename, DateTime lastmodified, long blocksetID, long metadataID, IDbTransaction? transaction)
        {
            var fileidobj = m_findfilesetCommand.SetTransaction(transaction)
                .SetParameterValue("@BlocksetId", blocksetID)
                .SetParameterValue("@MetadataId", metadataID)
                .SetParameterValue("@Path", filename)
                .SetParameterValue("@PrefixId", pathprefixid)
                .ExecuteScalarInt64(m_logQueries);

            if (fileidobj == -1)
            {
                using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
                {
                    fileidobj = m_insertfileCommand.SetTransaction(tr.Parent)
                        .SetParameterValue("@PrefixId", pathprefixid)
                        .SetParameterValue("@Path", filename)
                        .SetParameterValue("@BlocksetId", blocksetID)
                        .SetParameterValue("@MetadataId", metadataID)
                        .ExecuteScalarInt64(m_logQueries);
                    tr.Commit();
                }
            }

            AddKnownFile(fileidobj, lastmodified, transaction);
        }

        /// <summary>
        /// Adds a file record to the database
        /// </summary>
        /// <param name="filename">The path to the file</param>
        /// <param name="lastmodified">The time the file was modified</param>
        /// <param name="blocksetID">The ID of the hashkey for the file</param>
        /// <param name="metadataID">The ID for the metadata</param>
        /// <param name="transaction">The transaction to use for insertion, or null for no transaction</param>
        public void AddFile(string filename, DateTime lastmodified, long blocksetID, long metadataID, IDbTransaction? transaction)
        {
            var split = SplitIntoPrefixAndName(filename);
            AddFile(GetOrCreatePathPrefix(split.Key, transaction), split.Value, lastmodified, blocksetID, metadataID, transaction);
        }

        /// <summary>
        /// Adds a known file to the fileset
        /// </summary>
        /// <param name="fileid">Id of the file</param>
        /// <param name="lastmodified">The time the file was modified</param>
        /// <param name="transaction">The transaction to use for insertion, or null for no transaction</param>
        public void AddKnownFile(long fileid, DateTime lastmodified, IDbTransaction? transaction = null)
        {
            m_insertfileOperationCommand.SetTransaction(transaction)
                .SetParameterValue("@FilesetId", m_filesetId)
                .SetParameterValue("@FileId", fileid)
                .SetParameterValue("@LastModified", lastmodified.ToUniversalTime().Ticks)
                .ExecuteNonQuery(m_logQueries);
        }

        public void AddDirectoryEntry(string path, long metadataID, DateTime lastmodified, IDbTransaction? transaction = null)
        {
            AddFile(path, lastmodified, FOLDER_BLOCKSET_ID, metadataID, transaction);
        }

        public void AddSymlinkEntry(string path, long metadataID, DateTime lastmodified, IDbTransaction? transaction = null)
        {
            AddFile(path, lastmodified, SYMLINK_BLOCKSET_ID, metadataID, transaction);
        }

        public long GetFileLastModified(long prefixid, string path, long filesetid, bool includeLength, out DateTime oldModified, out long length, IDbTransaction? transaction = null)
        {
            if (includeLength)
            {
                m_selectfilelastmodifiedWithSizeCommand.SetTransaction(transaction)
                    .SetParameterValue("@PrefixId", prefixid)
                    .SetParameterValue("@Path", path)
                    .SetParameterValue("@FilesetId", filesetid);
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
                m_selectfilelastmodifiedCommand.SetTransaction(transaction)
                    .SetParameterValue("@PrefixId", prefixid)
                    .SetParameterValue("@Path", path)
                    .SetParameterValue("@FilesetId", filesetid);
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


        public long GetFileEntry(long prefixid, string path, long filesetid, out DateTime oldModified, out long lastFileSize, out string? oldMetahash, out long oldMetasize, IDbTransaction transaction)
        {
            m_findfileCommand.SetTransaction(transaction)
                .SetParameterValue("@PrefixId", prefixid)
                .SetParameterValue("@Path", path)
                .SetParameterValue("@FilesetId", filesetid);

            using (var rd = m_findfileCommand.ExecuteReader())
                if (rd.Read())
                {
                    oldModified = new DateTime(rd.ConvertValueToInt64(1), DateTimeKind.Utc);
                    lastFileSize = rd.ConvertValueToInt64(2);
                    oldMetahash = rd.ConvertValueToString(3);
                    oldMetasize = rd.ConvertValueToInt64(4);
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

        public (long Size, string MetadataHash)? GetMetadataHashAndSizeForFile(long fileid, IDbTransaction transaction)
        {
            m_selectfilemetadatahashandsizeCommand.SetTransaction(transaction)
                .SetParameterValue("@FileId", fileid);

            using (var rd = m_selectfilemetadatahashandsizeCommand.ExecuteReader())
                if (rd.Read())
                    return (rd.ConvertValueToInt64(0), rd.ConvertValueToString(1) ?? throw new InvalidOperationException("Metadata hash is null"));

            return null;
        }


        public string? GetFileHash(long fileid, IDbTransaction transaction)
        {
            var r = m_selectfileHashCommand.SetTransaction(transaction)
                .SetParameterValue("@FileId", fileid)
                .ExecuteScalar(m_logQueries, null);
            if (r == null || r == DBNull.Value)
                return null;

            return r.ToString();
        }

        public override void Dispose()
        {
            if (!string.IsNullOrWhiteSpace(m_tempDeletedBlockTable))
                try
                {
                    using (var cmd = m_connection.CreateCommand())
                        cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE ""{m_tempDeletedBlockTable}"""));
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "DropTempTableFailed", ex, "Failed to drop temporary table {0}: {1}", m_tempDeletedBlockTable, ex.Message);
                }

            base.Dispose();
        }

        private long GetPreviousFilesetID(IDbCommand cmd)
        {
            return GetPreviousFilesetID(cmd, OperationTimestamp, m_filesetId, cmd.Transaction);
        }

        private long GetPreviousFilesetID(IDbCommand cmd, DateTime timestamp, long filesetid, IDbTransaction? transaction)
        {
            return cmd.SetTransaction(transaction)
                .SetCommandAndParameters(@"SELECT ""ID"" FROM ""Fileset"" WHERE ""Timestamp"" < @Timestamp AND ""ID"" != @FilesetId ORDER BY ""Timestamp"" DESC ")
                .SetParameterValue("@Timestamp", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(timestamp))
                .SetParameterValue("@FilesetId", filesetid)
                .ExecuteScalarInt64(-1);
        }

        internal Tuple<long, long> GetLastBackupFileCountAndSize()
        {
            using (var cmd = m_connection.CreateCommand())
            {
                var lastFilesetId = cmd.ExecuteScalarInt64(@"SELECT ""ID"" FROM ""Fileset"" ORDER BY ""Timestamp"" DESC LIMIT 1");
                var count = cmd.SetCommandAndParameters(@"SELECT COUNT(*) FROM ""FileLookup"" INNER JOIN ""FilesetEntry"" ON ""FileLookup"".""ID"" = ""FilesetEntry"".""FileID"" WHERE ""FilesetEntry"".""FilesetID"" = @FilesetId AND ""FileLookup"".""BlocksetID"" NOT IN (@FolderBlocksetId, @SymlinkBlocksetId)")
                    .SetParameterValue("@FilesetId", lastFilesetId)
                    .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
                    .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
                    .ExecuteScalarInt64(-1);

                var size = cmd.SetCommandAndParameters(@"SELECT SUM(""Blockset"".""Length"") FROM ""FileLookup"", ""FilesetEntry"", ""Blockset"" WHERE ""FileLookup"".""ID"" = ""FilesetEntry"".""FileID"" AND ""FileLookup"".""BlocksetID"" = ""Blockset"".""ID"" AND ""FilesetEntry"".""FilesetID"" = @FilesetId AND ""FileLookup"".""BlocksetID"" NOT IN (@FolderBlocksetId, @SymlinkBlocksetId)")
                    .SetParameterValue("@FilesetId", lastFilesetId)
                    .SetParameterValue("@FolderBlocksetId", FOLDER_BLOCKSET_ID)
                    .SetParameterValue("@SymlinkBlocksetId", SYMLINK_BLOCKSET_ID)
                    .ExecuteScalarInt64(-1);

                return new Tuple<long, long>(count, size);
            }
        }

        internal void UpdateChangeStatistics(BackupResults results, IDbTransaction transaction)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            {
                var prevFileSetId = GetPreviousFilesetID(cmd);
                ChangeStatistics.UpdateChangeStatistics(cmd, results, m_filesetId, prevFileSetId);
            }
        }

        /// <summary>
        /// Populates FilesetEntry table with files from previous fileset, which aren't 
        /// yet part of the new fileset, and which aren't on the (optional) list of <c>deleted</c> paths.
        /// </summary>
        /// <param name="transaction">Transaction</param>
        /// <param name="deleted">List of deleted paths, or null</param>
        public void AppendFilesFromPreviousSet(IDbTransaction transaction, IEnumerable<string>? deleted = null)
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
        public void AppendFilesFromPreviousSet(IDbTransaction transaction, IEnumerable<string>? deleted, long filesetid, long prevId, DateTime timestamp)
        {
            using (var cmd = m_connection.CreateCommand())
            using (var cmdDelete = m_connection.CreateCommand())
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                long lastFilesetId = prevId < 0 ? GetPreviousFilesetID(cmd, timestamp, filesetid, tr.Parent) : prevId;

                cmd.SetTransaction(tr.Parent)
                    .SetCommandAndParameters(@"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") SELECT @CurrentFilesetId AS ""FilesetID"", ""FileID"", ""Lastmodified"" FROM (SELECT DISTINCT ""FilesetID"", ""FileID"", ""Lastmodified"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = @PreviousFilesetId AND ""FileID"" NOT IN (SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = @CurrentFilesetId)) ")
                    .SetParameterValue("@CurrentFilesetId", filesetid)
                    .SetParameterValue("@PreviousFilesetId", lastFilesetId)
                    .ExecuteNonQuery(m_logQueries);

                if (deleted != null)
                {
                    using var tmplist = new TemporaryDbValueList(m_connection, tr.Parent, deleted);
                    cmdDelete.SetTransaction(tr.Parent)
                        .SetCommandAndParameters(@"DELETE FROM ""FilesetEntry"" WHERE ""FilesetID"" = @FilesetId AND ""FileID"" IN (SELECT ""ID"" FROM ""File"" WHERE ""Path"" IN (@Paths)) ")
                        .SetParameterValue("@FilesetId", filesetid)
                        .ExpandInClauseParameter("@Paths", tmplist)
                        .ExecuteNonQuery(m_logQueries);
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
        public void AppendFilesFromPreviousSetWithPredicate(IDbTransaction transaction, Func<string, long, bool> exclusionPredicate)
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
        public void AppendFilesFromPreviousSetWithPredicate(IDbTransaction transaction,
            Func<string, long, bool> exclusionPredicate, long fileSetId, long prevFileSetId, DateTime timestamp)
        {
            if (exclusionPredicate == null)
            {
                AppendFilesFromPreviousSet(transaction, null, fileSetId, prevFileSetId, timestamp);
                return;
            }

            using (var cmd = m_connection.CreateCommand())
            using (var cmdDelete = m_connection.CreateCommand())
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                long lastFilesetId = prevFileSetId < 0 ? GetPreviousFilesetID(cmd, timestamp, fileSetId, tr.Parent) : prevFileSetId;

                // copy entries from previous file set into a temporary table, except those file IDs already added by the current backup
                var tempFileSetTable = "FilesetEntry-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                cmd.Transaction = tr.Parent;
                cmd.SetCommandAndParameters(FormatInvariant($@"CREATE TEMPORARY TABLE ""{tempFileSetTable}"" AS SELECT ""FileID"", ""Lastmodified"" FROM (SELECT DISTINCT ""FilesetID"", ""FileID"", ""Lastmodified"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = @PreviousFilesetId AND ""FileID"" NOT IN (SELECT ""FileID"" FROM ""FilesetEntry"" WHERE ""FilesetID"" = @CurrentFilesetId))"))
                    .SetParameterValue("@PreviousFilesetId", lastFilesetId)
                    .SetParameterValue("@CurrentFilesetId", fileSetId)
                    .ExecuteNonQuery();

                // now we need to remove, from the above, any entries that were enumerated by the 
                // UNC-driven backup
                cmdDelete.SetTransaction(tr.Parent)
                    .SetCommandAndParameters(FormatInvariant($@"DELETE FROM ""{tempFileSetTable}"" WHERE ""FileID"" = @FileId"));

                // enumerate files from new temporary file set, and remove any entries handled by UNC
                cmd.Transaction = tr.Parent;
                foreach (var row in cmd.ExecuteReaderEnumerable(FormatInvariant(
                    $@"SELECT f.""Path"", fs.""FileID"", fs.""Lastmodified"", COALESCE(bs.""Length"", -1)
                      FROM (SELECT DISTINCT ""FileID"", ""Lastmodified"" FROM ""{tempFileSetTable}"") AS fs
                      LEFT JOIN ""File"" AS f ON fs.""FileID"" = f.""ID""
                      LEFT JOIN ""Blockset"" AS bs ON f.""BlocksetID"" = bs.""ID"";")))
                {
                    var path = row.ConvertValueToString(0) ?? throw new Exception("Unexpected null value for path");
                    var size = row.ConvertValueToInt64(3);

                    if (exclusionPredicate(path, size))
                        cmdDelete.SetParameterValue("@FileId", row.ConvertValueToInt64(1))
                            .ExecuteNonQuery();
                }

                // now copy the temporary table into the FileSetEntry table
                cmd.SetCommandAndParameters(FormatInvariant($@"INSERT INTO ""FilesetEntry"" (""FilesetID"", ""FileID"", ""Lastmodified"") 
                                      SELECT @FilesetId, ""FileID"", ""Lastmodified"" FROM ""{tempFileSetTable}"""))
                    .SetParameterValue("@FilesetId", fileSetId)
                    .ExecuteNonQuery();

                tr.Commit();
            }
        }

        /// <summary>
        /// Creates a timestamped backup operation to correctly associate the fileset with the time it was created.
        /// </summary>
        /// <param name="volumeid">The ID of the fileset volume to update</param>
        /// <param name="timestamp">The timestamp of the operation to create</param>
        /// <param name="transaction">An optional external transaction</param>
        public override long CreateFileset(long volumeid, DateTime timestamp, IDbTransaction? transaction = null)
        {
            return m_filesetId = base.CreateFileset(volumeid, timestamp, transaction);
        }

        public IEnumerable<string> GetTemporaryFilelistVolumeNames(bool latestOnly, IDbTransaction? transaction = null)
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

        public IEnumerable<string> GetMissingIndexFiles(IDbTransaction transaction)
        {
            using var cmd = m_connection.CreateCommand(transaction)
                .SetCommandAndParameters(@"SELECT ""Name"" FROM ""RemoteVolume"" WHERE ""Type"" = @Type AND NOT ""ID"" IN (SELECT ""BlockVolumeID"" FROM ""IndexBlockLink"") AND ""State"" IN (@States)")
                .SetParameterValue("@Type", RemoteVolumeType.Blocks.ToString())
                .ExpandInClauseParameter("@States", [RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString()]);

            using (var rd = cmd.ExecuteReader())
                while (rd.Read())
                    yield return rd.ConvertValueToString(0) ?? throw new Exception("Unexpected null value for volume name");
        }

        public void MoveBlockToVolume(string blockkey, long size, long sourcevolumeid, long targetvolumeid, IDbTransaction transaction)
        {
            using (var cmd = m_connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                var c = cmd.SetCommandAndParameters(@"UPDATE ""Block"" SET ""VolumeID"" = @NewVolumeId WHERE ""Hash"" = @Hash AND ""Size"" = @Size AND ""VolumeID"" = @PreviousVolumeId ")
                    .SetParameterValue("@NewVolumeId", targetvolumeid)
                    .SetParameterValue("@Hash", blockkey)
                    .SetParameterValue("@Size", size)
                    .SetParameterValue("@PreviousVolumeId", sourcevolumeid)
                    .ExecuteNonQuery();
                if (c != 1)
                    throw new Exception($"Failed to move block {blockkey}:{size} from volume {sourcevolumeid}, count: {c}");
            }
        }

        public void SafeDeleteRemoteVolume(string name, IDbTransaction transaction)
        {
            var volumeid = GetRemoteVolumeID(name, transaction);

            using (var cmd = m_connection.CreateCommand(transaction))
            {
                var c = cmd.SetCommandAndParameters(@"SELECT COUNT(*) FROM ""Block"" WHERE ""VolumeID"" = @VolumeId ")
                    .SetParameterValue("@VolumeId", volumeid)
                    .ExecuteScalarInt64(-1);
                if (c != 0)
                    throw new Exception($"Failed to safe-delete volume {name}, blocks: {c}");

                RemoveRemoteVolume(name, transaction);
            }
        }

        public string[] GetBlocklistHashes(string name, IDbTransaction transaction)
        {
            var volumeid = GetRemoteVolumeID(name, transaction);
            using (var cmd = m_connection.CreateCommand(transaction))
            {
                // Grab the strings and return as array to avoid concurrent access to the IEnumerable
                cmd.SetCommandAndParameters(@"SELECT DISTINCT ""Block"".""Hash"" FROM ""Block"" WHERE ""Block"".""VolumeID"" = @VolumeId AND ""Block"".""Hash"" IN (SELECT ""Hash"" FROM ""BlocklistHash"")")
                    .SetParameterValue("@VolumeId", volumeid);
                return cmd.ExecuteReaderEnumerable()
                    .Select(x => x.ConvertValueToString(0) ?? throw new Exception("Unexpected null value for blocklist hash"))
                    .ToArray();
            }
        }

        public string? GetFirstPath()
        {
            using (var cmd = m_connection.CreateCommand(@"SELECT ""Path"" FROM ""File"" ORDER BY LENGTH(""Path"") DESC LIMIT 1"))
            {
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

            using var cmd = m_connection.CreateCommand(@"SELECT ""VolumeName"", ""JournalID"", ""NextUSN"", ""ConfigHash"" FROM ""ChangeJournalData"" WHERE ""FilesetID"" = @FilesetId")
                .SetParameterValue("@FilesetId", fileSetId);
            using (var rd = cmd.ExecuteReader())
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
        public void CreateChangeJournalData(IEnumerable<Interface.USNJournalDataEntry> data, IDbTransaction? transaction = null)
        {
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                foreach (var entry in data)
                {
                    using (var cmd = m_connection.CreateCommand())
                    {
                        cmd.Transaction = tr.Parent;
                        var c = cmd.SetCommandAndParameters(
                            @"INSERT INTO ""ChangeJournalData"" (""FilesetID"", ""VolumeName"", ""JournalID"", ""NextUSN"", ""ConfigHash"") VALUES (@FilesetId, @VolumeName, @JournalId, @NextUsn, @ConfigHash);")
                                .SetParameterValue("@FilesetId", m_filesetId)
                                .SetParameterValue("@VolumeName", entry.Volume)
                                .SetParameterValue("@JournalId", entry.JournalId)
                                .SetParameterValue("@NextUsn", entry.NextUsn)
                                .SetParameterValue("@ConfigHash", entry.ConfigHash)
                                .ExecuteNonQuery();

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
        public void UpdateChangeJournalData(IEnumerable<Interface.USNJournalDataEntry> data, long fileSetId, IDbTransaction? transaction = null)
        {
            using (var tr = new TemporaryTransactionWrapper(m_connection, transaction))
            {
                foreach (var entry in data)
                {
                    using (var cmd = m_connection.CreateCommand())
                    {
                        cmd.SetTransaction(tr.Parent)
                            .SetCommandAndParameters(
                                @"UPDATE ""ChangeJournalData"" SET ""NextUSN"" = @NextUsn WHERE ""FilesetID"" = @FilesetId AND ""VolumeName"" = @VolumeName AND ""JournalID"" = @JournalId;")
                            .SetParameterValue("@NextUsn", entry.NextUsn)
                            .SetParameterValue("@FilesetId", fileSetId)
                            .SetParameterValue("@VolumeName", entry.Volume)
                            .SetParameterValue("@JournalId", entry.JournalId)
                            .ExecuteNonQuery();
                    }
                }

                tr.Commit();
            }
        }

        /// <summary>
        /// Checks if a blocklist hash is known
        /// </summary>
        /// <param name="hash">The hash to check</param>
        /// <param name="transaction">An optional external transaction</param>
        /// <returns>True if the hash is known, false otherwise</returns>
        public bool IsBlocklistHashKnown(string hash, IDbTransaction transaction)
        {
            var res = m_getfirstfilesetwithblockinblockset.SetTransaction(transaction)
                .SetParameterValue("@Hash", hash)
                .ExecuteScalarInt64();
            if (res != -1 && res != m_filesetId)
                return true;
            else
                return !m_blocklistHashes.Add(hash);
        }
    }
}
