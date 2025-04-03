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

using System;
using System.Data;
using System.Collections.Generic;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Database
{
    internal class LocalRepairDatabase : LocalDatabase
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(LocalRepairDatabase));

        public LocalRepairDatabase(string path)
            : base(path, "Repair", true)
        {

        }

        public long GetFilesetIdFromRemotename(string filelist)
        {
            using (var cmd = m_connection.CreateCommand())
            {
                var filesetid = cmd.SetCommandAndParameters(@"SELECT ""Fileset"".""ID"" FROM ""Fileset"",""RemoteVolume"" WHERE ""Fileset"".""VolumeID"" = ""RemoteVolume"".""ID"" AND ""RemoteVolume"".""Name"" = @Name")
                    .SetParameterValue("@Name", filelist)
                    .ExecuteScalarInt64(-1);

                if (filesetid == -1)
                    throw new Exception($"No such remote file: {filelist}");

                return filesetid;
            }
        }

        /// <summary>
        /// Gets a list of filesets that are missing files
        /// </summary>
        /// <param name="transaction">An optional transaction</param>
        /// <returns>A list of fileset IDs and timestamps</returns>
        public IEnumerable<KeyValuePair<long, DateTime>> GetFilesetsWithMissingFiles(IDbTransaction transaction)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            using (var rd = cmd.ExecuteReader(@"SELECT ID, Timestamp FROM Fileset WHERE ID IN (SELECT FilesetID FROM ""FilesetEntry"" WHERE ""FileID"" NOT IN (SELECT ""ID"" FROM ""FileLookup""))"))
                while (rd.Read())
                {
                    yield return new KeyValuePair<long, DateTime>(
                        rd.GetInt64(0),
                        ParseFromEpochSeconds(rd.GetInt64(1)).ToLocalTime()
                    );
                }
        }

        /// <summary>
        /// Deletes all fileset entries for a given fileset
        /// </summary>
        /// <param name="filesetid">The fileset ID</param>
        /// <param name="transaction">An optional transaction</param>
        /// <returns>The number of deleted entries</returns>
        public int DeleteFilesetEntries(long filesetid, IDbTransaction transaction = null)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
                return cmd.SetCommandAndParameters(@"DELETE FROM ""FilesetEntry"" WHERE ""FilesetID"" = @FilesetId")
                    .SetParameterValue("@FilesetId", filesetid)
                    .ExecuteNonQuery();
        }

        public interface IBlockSource
        {
            string File { get; }
            long Offset { get; }
        }

        public interface IBlockWithSources : IBlock
        {
            IEnumerable<IBlockSource> Sources { get; }
        }

        private class BlockWithSources : Block, IBlockWithSources
        {
            private class BlockSource : IBlockSource
            {
                public string File { get; private set; }
                public long Offset { get; private set; }

                public BlockSource(string file, long offset)
                {
                    this.File = file;
                    this.Offset = offset;

                }
            }

            private readonly IDataReader m_rd;
            public bool Done { get; private set; }

            public BlockWithSources(IDataReader rd)
                : base(rd.GetString(0), rd.GetInt64(1))
            {
                m_rd = rd;
                Done = !m_rd.Read();
            }

            public IEnumerable<IBlockSource> Sources
            {
                get
                {
                    if (Done)
                        yield break;

                    var cur = new BlockSource(m_rd.GetString(2), m_rd.GetInt64(3));
                    var file = cur.File;

                    while (!Done && cur.File == file)
                    {
                        yield return cur;
                        Done = m_rd.Read();
                        if (!Done)
                            cur = new BlockSource(m_rd.GetString(2), m_rd.GetInt64(3));
                    }
                }
            }
        }

        private class RemoteVolume : IRemoteVolume
        {
            public string Name { get; private set; }
            public string Hash { get; private set; }
            public long Size { get; private set; }

            public RemoteVolume(string name, string hash, long size)
            {
                this.Name = name;
                this.Hash = hash;
                this.Size = size;
            }
        }

        public IEnumerable<IRemoteVolume> GetBlockVolumesFromIndexName(string name)
        {
            using var cmd = m_connection.CreateCommand(@"SELECT ""Name"", ""Hash"", ""Size"" FROM ""RemoteVolume"" WHERE ""ID"" IN (SELECT ""BlockVolumeID"" FROM ""IndexBlockLink"" WHERE ""IndexVolumeID"" IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" = @Name)) ")
                .SetParameterValue("@Name", name);

            foreach (var rd in cmd.ExecuteReaderEnumerable())
                yield return new RemoteVolume(rd.GetString(0), rd.ConvertValueToString(1), rd.ConvertValueToInt64(2));
        }

        public interface IMissingBlockList : IDisposable
        {
            bool SetBlockRestored(string hash, long size);
            IEnumerable<IBlockWithSources> GetSourceFilesWithBlocks(long blocksize);
            IEnumerable<KeyValuePair<string, long>> GetMissingBlocks();
            IEnumerable<IRemoteVolume> GetFilesetsUsingMissingBlocks();
            IEnumerable<IRemoteVolume> GetMissingBlockSources();
        }

        private class MissingBlockList : IMissingBlockList
        {
            private readonly IDbConnection m_connection;
            private readonly TemporaryTransactionWrapper m_transaction;
            private IDbCommand m_insertCommand;
            private string m_tablename;
            private readonly string m_volumename;

            public MissingBlockList(string volumename, IDbConnection connection, IDbTransaction transaction)
            {
                m_connection = connection;
                m_transaction = new TemporaryTransactionWrapper(m_connection, transaction);
                m_volumename = volumename;
                var tablename = "MissingBlocks-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                using (var cmd = m_connection.CreateCommand(m_transaction.Parent))
                {
                    cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{tablename}"" (""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL, ""Restored"" INTEGER NOT NULL) "));
                    m_tablename = tablename;

                    var blockCount = cmd.SetCommandAndParameters(FormatInvariant($@"INSERT INTO ""{m_tablename}"" (""Hash"", ""Size"", ""Restored"") SELECT DISTINCT ""Block"".""Hash"", ""Block"".""Size"", 0 AS ""Restored"" FROM ""Block"",""Remotevolume"" WHERE ""Block"".""VolumeID"" = ""Remotevolume"".""ID"" AND ""Remotevolume"".""Name"" = @Name "))
                        .SetParameterValue("@Name", volumename)
                        .ExecuteNonQuery();

                    if (blockCount == 0)
                        throw new Exception($"Unexpected empty block volume: {0}");

                    cmd.ExecuteNonQuery(FormatInvariant($@"CREATE UNIQUE INDEX ""{tablename}-Ix"" ON ""{tablename}"" (""Hash"", ""Size"", ""Restored"")"));
                }

                m_insertCommand = m_connection.CreateCommand(m_transaction.Parent, FormatInvariant($@"UPDATE ""{tablename}"" SET ""Restored"" = @NewRestoredValue WHERE ""Hash"" = @Hash AND ""Size"" = @Size AND ""Restored"" = @PreviousRestoredValue "));
            }

            public bool SetBlockRestored(string hash, long size)
            {
                return m_insertCommand.SetParameterValue("@NewRestoredValue", 1)
                    .SetParameterValue("@Hash", hash)
                    .SetParameterValue("@Size", size)
                    .SetParameterValue(@"PreviousRestoredValue", 0)
                    .ExecuteNonQuery() == 1;
            }

            public IEnumerable<IBlockWithSources> GetSourceFilesWithBlocks(long blocksize)
            {
                using var cmd = m_connection.CreateCommand(m_transaction.Parent, FormatInvariant($@"SELECT DISTINCT ""{m_tablename}"".""Hash"", ""{m_tablename}"".""Size"", ""File"".""Path"", ""BlocksetEntry"".""Index"" * {blocksize} FROM  ""{m_tablename}"", ""Block"", ""BlocksetEntry"", ""File"" WHERE ""File"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID"" AND ""Block"".""ID"" = ""BlocksetEntry"".""BlockID"" AND ""{m_tablename}"".""Hash"" = ""Block"".""Hash"" AND ""{m_tablename}"".""Size"" = ""Block"".""Size"" AND ""{m_tablename}"".""Restored"" = @Restored "))
                    .SetParameterValue("@Restored", 0);
                using (var rd = cmd.ExecuteReader())
                    if (rd.Read())
                    {
                        var bs = new BlockWithSources(rd);
                        while (!bs.Done)
                            yield return bs;
                    }
            }

            public IEnumerable<KeyValuePair<string, long>> GetMissingBlocks()
            {
                using var cmd = m_connection.CreateCommand(m_transaction.Parent, FormatInvariant($@"SELECT ""{m_tablename}"".""Hash"", ""{m_tablename}"".""Size"" FROM ""{m_tablename}"" WHERE ""{m_tablename}"".""Restored"" = @Restored "))
                    .SetParameterValue("@Restored", 0);

                foreach (var rd in cmd.ExecuteReaderEnumerable())
                    yield return new KeyValuePair<string, long>(rd.ConvertValueToString(0), rd.ConvertValueToInt64(1));
            }

            public IEnumerable<IRemoteVolume> GetFilesetsUsingMissingBlocks()
            {
                var blocks = FormatInvariant($@"SELECT DISTINCT ""FileLookup"".""ID"" AS ID FROM ""{m_tablename}"", ""Block"", ""Blockset"", ""BlocksetEntry"", ""FileLookup"" WHERE ""Block"".""Hash"" = ""{m_tablename}"".""Hash"" AND ""Block"".""Size"" = ""{m_tablename}"".""Size"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" AND ""BlocksetEntry"".""BlocksetID"" = ""Blockset"".""ID"" AND ""FileLookup"".""BlocksetID"" = ""Blockset"".""ID"" ");
                var blocklists = FormatInvariant($@"SELECT DISTINCT ""FileLookup"".""ID"" AS ID FROM ""{m_tablename}"", ""Block"", ""Blockset"", ""BlocklistHash"", ""FileLookup"" WHERE ""Block"".""Hash"" = ""{m_tablename}"".""Hash"" AND ""Block"".""Size"" = ""{m_tablename}"".""Size"" AND ""BlocklistHash"".""Hash"" = ""Block"".""Hash"" AND ""BlocklistHash"".""BlocksetID"" = ""Blockset"".""ID"" AND ""FileLookup"".""BlocksetID"" = ""Blockset"".""ID"" ");
                var cmdtxt = FormatInvariant($@"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"" FROM ""RemoteVolume"", ""FilesetEntry"", ""Fileset"" WHERE ""RemoteVolume"".""ID"" = ""Fileset"".""VolumeID"" AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID"" AND ""RemoteVolume"".""Type"" = @Type AND ""FilesetEntry"".""FileID"" IN  (SELECT DISTINCT ""ID"" FROM ( {blocks} UNION {blocklists} ))");

                using var cmd = m_connection.CreateCommand(m_transaction.Parent, cmdtxt)
                    .SetParameterValue("@Type", RemoteVolumeType.Files.ToString());

                foreach (var rd in cmd.ExecuteReaderEnumerable())
                    yield return new RemoteVolume(rd.GetString(0), rd.ConvertValueToString(1), rd.ConvertValueToInt64(2));
            }

            public IEnumerable<IRemoteVolume> GetMissingBlockSources()
            {
                using var cmd = m_connection.CreateCommand(m_transaction.Parent, FormatInvariant($@"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"" FROM ""RemoteVolume"", ""Block"", ""{m_tablename}"" WHERE ""Block"".""Hash"" = ""{m_tablename}"".""Hash"" AND ""Block"".""Size"" = ""{m_tablename}"".""Size"" AND ""Block"".""VolumeID"" = ""RemoteVolume"".""ID"" AND ""Remotevolume"".""Name"" != @Name "))
                    .SetParameterValue("@Name", m_volumename);

                foreach (var rd in cmd.ExecuteReaderEnumerable())
                    yield return new RemoteVolume(rd.GetString(0), rd.ConvertValueToString(1), rd.ConvertValueToInt64(2));
            }

            public void Dispose()
            {
                if (m_tablename != null)
                {
                    try
                    {
                        using (var cmd = m_connection.CreateCommand(m_transaction.Parent))
                            cmd.SetTransaction(m_transaction.Parent).ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_tablename}"" "));
                    }
                    catch { }
                    finally { m_tablename = null; }
                }

                if (m_insertCommand != null)
                    try { m_insertCommand.Dispose(); }
                    catch { }
                    finally { m_insertCommand = null; }
            }
        }

        public IMissingBlockList CreateBlockList(string volumename, IDbTransaction transaction = null)
        {
            return new MissingBlockList(volumename, m_connection, transaction);
        }

        public void FixDuplicateMetahash()
        {
            using (var tr = m_connection.BeginTransaction())
            using (var cmd = m_connection.CreateCommand(tr))
            {
                var sql_count =
                    @"SELECT COUNT(*) FROM (" +
                    @" SELECT DISTINCT c1 FROM (" +
                    @"SELECT COUNT(*) AS ""C1"" FROM (SELECT DISTINCT ""BlocksetID"" FROM ""Metadataset"") UNION SELECT COUNT(*) AS ""C1"" FROM ""Metadataset"" " +
                    @")" +
                    @")";

                var x = cmd.ExecuteScalarInt64(sql_count, 0);
                if (x > 1)
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateMetadataHashes", "Found duplicate metadatahashes, repairing");

                    var tablename = "TmpFile-" + Guid.NewGuid().ToString("N");

                    cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{tablename}"" AS SELECT * FROM ""File"""));

                    var sql = @"SELECT ""A"".""ID"", ""B"".""BlocksetID"" FROM (SELECT MIN(""ID"") AS ""ID"", COUNT(""ID"") AS ""Duplicates"" FROM ""Metadataset"" GROUP BY ""BlocksetID"") ""A"", ""Metadataset"" ""B"" WHERE ""A"".""Duplicates"" > 1 AND ""A"".""ID"" = ""B"".""ID""";

                    using (var c2 = m_connection.CreateCommand(tr))
                    {
                        c2.SetCommandAndParameters(FormatInvariant($@"UPDATE ""{tablename}"" SET ""MetadataID"" = @MetadataId WHERE ""MetadataID"" IN (SELECT ""ID"" FROM ""Metadataset"" WHERE ""BlocksetID"" = @BlocksetId)")
                            + @"; DELETE FROM ""Metadataset"" WHERE ""BlocksetID"" = @BlocksetId AND ""ID"" != @MetadataId");
                        using (var rd = cmd.ExecuteReader(sql))
                            while (rd.Read())
                                c2.SetParameterValue("@MetadataId", rd.GetValue(0))
                                    .SetParameterValue("@BlocksetId", rd.GetValue(1))
                                    .ExecuteNonQuery();
                    }

                    sql = FormatInvariant($@"SELECT ""ID"", ""Path"", ""BlocksetID"", ""MetadataID"", ""Entries"" FROM (
                            SELECT MIN(""ID"") AS ""ID"", ""Path"", ""BlocksetID"", ""MetadataID"", COUNT(*) as ""Entries"" FROM ""{tablename}"" GROUP BY ""Path"", ""BlocksetID"", ""MetadataID"") 
                            WHERE ""Entries"" > 1 ORDER BY ""ID""");

                    using (var c2 = m_connection.CreateCommand(tr))
                    {
                        c2.SetCommandAndParameters(FormatInvariant($@"UPDATE ""FilesetEntry"" SET ""FileID"" = @FileId WHERE ""FileID"" IN (SELECT ""ID"" FROM ""{tablename}"" WHERE ""Path"" = @Path AND ""BlocksetID"" = @BlocksetId AND ""MetadataID"" = @MetadataId)")
                            + FormatInvariant($@"; DELETE FROM ""{tablename}"" WHERE ""Path"" = @Path AND ""BlocksetID"" = @BlocksetId AND ""MetadataID"" = @MetadataId AND ""ID"" != @FileId"));

                        foreach (var rd in cmd.ExecuteReaderEnumerable(sql))
                            c2.SetParameterValue("@FileId", rd.GetValue(0))
                                .SetParameterValue("@Path", rd.GetValue(1))
                                .SetParameterValue("@BlocksetId", rd.GetValue(2))
                                .SetParameterValue("@MetadataId", rd.GetValue(3))
                                .ExecuteNonQuery();
                    }

                    cmd.ExecuteNonQuery(FormatInvariant($@"DELETE FROM ""FileLookup"" WHERE ""ID"" NOT IN (SELECT ""ID"" FROM ""{tablename}"") "));
                    cmd.ExecuteNonQuery(FormatInvariant($@"CREATE INDEX ""{tablename}-Ix"" ON  ""{tablename}"" (""ID"", ""MetadataID"")"));
                    cmd.ExecuteNonQuery(FormatInvariant($@"UPDATE ""FileLookup"" SET ""MetadataID"" = (SELECT ""MetadataID"" FROM ""{tablename}"" A WHERE ""A"".""ID"" = ""FileLookup"".""ID"") "));
                    cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE ""{tablename}"" "));

                    cmd.CommandText = sql_count;
                    x = cmd.ExecuteScalarInt64(0);
                    if (x > 1)
                        throw new Interface.UserInformationException("Repair failed, there are still duplicate metadatahashes!", "DuplicateHashesRepairFailed");

                    Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateMetadataHashesFixed", "Duplicate metadatahashes repaired succesfully");
                    tr.Commit();
                }
            }
        }

        public void FixDuplicateFileentries()
        {
            using (var tr = m_connection.BeginTransaction())
            using (var cmd = m_connection.CreateCommand(tr))
            {
                var sql_count = @"SELECT COUNT(*) FROM (SELECT ""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"", COUNT(*) as ""Duplicates"" FROM ""FileLookup"" GROUP BY ""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"") WHERE ""Duplicates"" > 1";

                var x = cmd.ExecuteScalarInt64(sql_count, 0);
                if (x > 0)
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateFileEntries", "Found duplicate file entries, repairing");

                    var sql = @"SELECT ""ID"", ""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"", ""Entries"" FROM (
                            SELECT MIN(""ID"") AS ""ID"", ""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"", COUNT(*) as ""Entries"" FROM ""FileLookup"" GROUP BY ""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"") 
                            WHERE ""Entries"" > 1 ORDER BY ""ID""";

                    using (var c2 = m_connection.CreateCommand(tr))
                    {
                        c2.SetCommandAndParameters(@"UPDATE ""FilesetEntry"" SET ""FileID"" = @FileId WHERE ""FileID"" IN (SELECT ""ID"" FROM ""FileLookup"" WHERE ""PrefixID"" = @PrefixId AND ""Path"" = @Path AND ""BlocksetID"" = @BlocksetId AND ""MetadataID"" = @MetatadataId)"
                            + @"; DELETE FROM ""FileLookup"" WHERE ""PrefixID"" = @PrefixId AND ""Path"" = @Path AND ""BlocksetID"" = @BlocksetId AND ""MetadataID"" = @MetadataId AND ""ID"" != @FileId");

                        foreach (var rd in cmd.SetCommandAndParameters(sql).ExecuteReaderEnumerable())
                            c2.SetParameterValue("@FileId", rd.GetValue(0))
                                .SetParameterValue("@PrefixId", rd.GetValue(1))
                                .SetParameterValue("@Path", rd.GetValue(2))
                                .SetParameterValue("@BlocksetId", rd.GetValue(3))
                                .SetParameterValue("@MetadataId", rd.GetValue(4))
                                .ExecuteNonQuery();
                    }

                    cmd.CommandText = sql_count;
                    x = cmd.ExecuteScalarInt64(0);
                    if (x > 1)
                        throw new Interface.UserInformationException("Repair failed, there are still duplicate file entries!", "DuplicateFilesRepairFailed");

                    Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateFileEntriesFixed", "Duplicate file entries repaired succesfully");
                    tr.Commit();
                }
            }

        }

        public void FixMissingBlocklistHashes(string blockhashalgorithm, long blocksize)
        {
            var blocklistbuffer = new byte[blocksize];

            using (var tr = m_connection.BeginTransaction())
            using (var cmd = m_connection.CreateCommand(tr))
            using (var blockhasher = HashFactory.CreateHasher(blockhashalgorithm))
            {
                var hashsize = blockhasher.HashSize / 8;

                var sql = FormatInvariant($@"SELECT * FROM (SELECT ""N"".""BlocksetID"", ((""N"".""BlockCount"" + {blocksize / hashsize} - 1) / {blocksize / hashsize}) AS ""BlocklistHashCountExpected"", CASE WHEN ""G"".""BlocklistHashCount"" IS NULL THEN 0 ELSE ""G"".""BlocklistHashCount"" END AS ""BlocklistHashCountActual"" FROM (SELECT ""BlocksetID"", COUNT(*) AS ""BlockCount"" FROM ""BlocksetEntry"" GROUP BY ""BlocksetID"") ""N"" LEFT OUTER JOIN (SELECT ""BlocksetID"", COUNT(*) AS ""BlocklistHashCount"" FROM ""BlocklistHash"" GROUP BY ""BlocksetID"") ""G"" ON ""N"".""BlocksetID"" = ""G"".""BlocksetID"" WHERE ""N"".""BlockCount"" > 1) WHERE ""BlocklistHashCountExpected"" != ""BlocklistHashCountActual""");
                var countsql = @"SELECT COUNT(*) FROM (" + sql + @")";

                var itemswithnoblocklisthash = cmd.ExecuteScalarInt64(countsql, 0);
                if (itemswithnoblocklisthash != 0)
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "MissingBlocklistHashes", "Found {0} missing blocklisthash entries, repairing", itemswithnoblocklisthash);
                    using (var c2 = m_connection.CreateCommand(tr))
                    using (var c3 = m_connection.CreateCommand(tr))
                    using (var c4 = m_connection.CreateCommand(tr))
                    using (var c5 = m_connection.CreateCommand(tr))
                    using (var c6 = m_connection.CreateCommand(tr))
                    {
                        c3.SetCommandAndParameters(@"INSERT INTO ""BlocklistHash"" (""BlocksetID"", ""Index"", ""Hash"") VALUES (@BlocksetId, @Index, @Hash) ");
                        c4.SetCommandAndParameters(@"SELECT ""ID"" FROM ""Block"" WHERE ""Hash"" = @Hash AND ""Size"" = @Size LIMIT 1");
                        c5.SetCommandAndParameters(@"SELECT ""ID"" FROM ""DeletedBlock"" WHERE ""Hash"" = @Hash AND ""Size"" = @Size AND ""VolumeID"" IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Type"" = @Type AND (""State"" IN (@State1, @State2)))");
                        c6.SetCommandAndParameters(@"INSERT INTO ""Block"" (""Hash"", ""Size"", ""VolumeID"") SELECT ""Hash"", ""Size"", ""VolumeID"" FROM ""DeletedBlock"" WHERE ""ID"" = @DeletedBlockId LIMIT 1; DELETE FROM ""DeletedBlock"" WHERE ""ID"" = @DeletedBlockId;");

                        foreach (var e in cmd.ExecuteReaderEnumerable(sql))
                        {
                            var blocksetid = e.ConvertValueToInt64(0);
                            var ix = 0L;
                            int blocklistoffset = 0;

                            c2.SetCommandAndParameters(@"DELETE FROM ""BlocklistHash"" WHERE ""BlocksetID"" = @BlocksetId")
                                .SetParameterValue("@BlocksetId", blocksetid)
                                .ExecuteNonQuery();

                            c2.SetCommandAndParameters(@"SELECT ""A"".""Hash"" FROM ""Block"" ""A"", ""BlocksetEntry"" ""B"" WHERE ""A"".""ID"" = ""B"".""BlockID"" AND ""B"".""BlocksetID"" = @BlocksetId ORDER BY ""B"".""Index""")
                                .SetParameterValue("@BlocksetId", blocksetid);

                            foreach (var h in c2.ExecuteReaderEnumerable())
                            {
                                var tmp = Convert.FromBase64String(h.GetString(0));
                                if (blocklistbuffer.Length - blocklistoffset < tmp.Length)
                                {
                                    var blkey = Convert.ToBase64String(blockhasher.ComputeHash(blocklistbuffer, 0, blocklistoffset));

                                    // Check if the block exists in "blocks"
                                    var blockId = c4.SetParameterValue("@Hash", blkey)
                                        .SetParameterValue("@Size", blocklistoffset)
                                        .ExecuteScalarInt64(-1);

                                    if (blockId != 1)
                                    {
                                        var c = c5.SetParameterValue("@Hash", blkey)
                                            .SetParameterValue("@Size", blocklistoffset)
                                            .SetParameterValue("@Type", RemoteVolumeType.Blocks.ToString())
                                            .SetParameterValue("@State1", RemoteVolumeState.Uploaded.ToString())
                                            .SetParameterValue("@State2", RemoteVolumeState.Verified.ToString())
                                            .ExecuteScalarInt64(-1);

                                        if (c <= 0)
                                            throw new Exception($"Missing block for blocklisthash: {blkey}");
                                        else
                                        {
                                            var rc = c6.SetParameterValue("@DeletedBlockId", c)
                                                .ExecuteNonQuery();

                                            if (rc != 2)
                                                throw new Exception($"Unexpected update count: {rc}");
                                        }
                                    }

                                    // Add to table
                                    c3.SetParameterValue("@BlocksetId", blocksetid)
                                        .SetParameterValue("@Index", ix)
                                        .SetParameterValue("@Hash", blkey)
                                        .ExecuteNonQuery();

                                    ix++;
                                    blocklistoffset = 0;
                                }

                                Array.Copy(tmp, 0, blocklistbuffer, blocklistoffset, tmp.Length);
                                blocklistoffset += tmp.Length;

                            }

                            if (blocklistoffset != 0)
                            {
                                var blkeyfinal = Convert.ToBase64String(blockhasher.ComputeHash(blocklistbuffer, 0, blocklistoffset));

                                // Ensure that the block exists in "blocks"
                                if (c4.SetParameterValue("@Hash", blkeyfinal)
                                        .SetParameterValue("@Size", blocklistoffset)
                                        .ExecuteScalarInt64(-1) != 1)
                                {
                                    var c = c5.SetParameterValue("@Hash", blkeyfinal)
                                            .SetParameterValue("@Size", blocklistoffset)
                                            .SetParameterValue("@Type", RemoteVolumeType.Blocks.ToString())
                                            .SetParameterValue("@State1", RemoteVolumeState.Uploaded.ToString())
                                            .SetParameterValue("@State2", RemoteVolumeState.Verified.ToString())
                                            .ExecuteScalarInt64(-1);

                                    if (c == 0)
                                        throw new Exception($"Missing block for blocklisthash: {blkeyfinal}");
                                    else
                                    {
                                        var rc = c6.SetParameterValue("@DeletedBlockId", c)
                                                .ExecuteNonQuery(); ;
                                        if (rc != 2)
                                            throw new Exception($"Unexpected update count: {rc}");
                                    }
                                }

                                // Add to table
                                c3.SetParameterValue("@BlocksetId", blocksetid)
                                    .SetParameterValue("@Index", ix)
                                    .SetParameterValue("@Hash", blkeyfinal)
                                    .ExecuteNonQuery();
                            }
                        }
                    }


                    itemswithnoblocklisthash = cmd.ExecuteScalarInt64(countsql, 0);
                    if (itemswithnoblocklisthash != 0)
                        throw new Interface.UserInformationException($"Failed to repair, after repair {itemswithnoblocklisthash} blocklisthashes were missing", "MissingBlocklistHashesRepairFailed");

                    Logging.Log.WriteInformationMessage(LOGTAG, "MissingBlocklisthashesRepaired", "Missing blocklisthashes repaired succesfully");
                    tr.Commit();
                }
            }

        }

        public void FixDuplicateBlocklistHashes(long blocksize, long hashsize)
        {
            using (var tr = m_connection.BeginTransaction())
            using (var cmd = m_connection.CreateCommand(tr))
            {
                var dup_sql = @"SELECT * FROM (SELECT ""BlocksetID"", ""Index"", COUNT(*) AS ""EC"" FROM ""BlocklistHash"" GROUP BY ""BlocksetID"", ""Index"") WHERE ""EC"" > 1";

                var sql_count = @"SELECT COUNT(*) FROM (" + dup_sql + ")";

                var x = cmd.ExecuteScalarInt64(sql_count, 0);
                if (x > 0)
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateBlocklistHashes", "Found duplicate blocklisthash entries, repairing");

                    var unique_count = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM (SELECT DISTINCT ""BlocksetID"", ""Index"" FROM ""BlocklistHash"")", 0);

                    using (var c2 = m_connection.CreateCommand(tr))
                    {
                        c2.SetCommandAndParameters(@"DELETE FROM ""BlocklistHash"" WHERE rowid IN (SELECT rowid FROM ""BlocklistHash"" WHERE ""BlocksetID"" = @BlocksetId AND ""Index"" = @Index LIMIT @Limit)");
                        foreach (var rd in cmd.ExecuteReaderEnumerable(dup_sql))
                        {
                            var expected = rd.GetInt32(2) - 1;
                            var actual = c2.SetParameterValue("@BlocksetId", rd.GetValue(0))
                                .SetParameterValue("@Index", rd.GetValue(1))
                                .SetParameterValue("@Limit", expected)
                                .ExecuteNonQuery();

                            if (actual != expected)
                                throw new Exception($"Unexpected number of results after fix, got: {actual}, expected: {expected}");
                        }
                    }

                    cmd.CommandText = sql_count;
                    x = cmd.ExecuteScalarInt64();
                    if (x > 1)
                        throw new Exception("Repair failed, there are still duplicate file entries!");

                    var real_count = cmd.ExecuteScalarInt64(@"SELECT Count(*) FROM ""BlocklistHash""", 0);

                    if (real_count != unique_count)
                        throw new Interface.UserInformationException($"Failed to repair, result should have been {unique_count} blocklist hashes, but result was {real_count} blocklist hashes", "DuplicateBlocklistHashesRepairFailed");

                    try
                    {
                        VerifyConsistency(blocksize, hashsize, true, tr);
                    }
                    catch (Exception ex)
                    {
                        throw new Interface.UserInformationException("Repaired blocklisthashes, but the database was broken afterwards, rolled back changes", "DuplicateBlocklistHashesRepairFailed", ex);
                    }

                    Logging.Log.WriteInformationMessage(LOGTAG, "DuplicateBlocklistHashesRepaired", "Duplicate blocklisthashes repaired succesfully");
                    tr.Commit();
                }
            }
        }

        public void CheckAllBlocksAreInVolume(string filename, IEnumerable<KeyValuePair<string, long>> blocks)
        {
            using (var tr = m_connection.BeginTransaction())
            using (var cmd = m_connection.CreateCommand(tr))
            {
                var tablename = "ProbeBlocks-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

                cmd.ExecuteNonQuery(FormatInvariant($@"CREATE TEMPORARY TABLE ""{tablename}"" (""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL)"));
                cmd.CommandText = FormatInvariant($@"INSERT INTO ""{tablename}"" (""Hash"", ""Size"") VALUES (@Hash, @Size)");

                foreach (var kp in blocks)
                {
                    cmd.SetParameterValue("@Hash", kp.Key);
                    cmd.SetParameterValue("@Size", kp.Value);
                    cmd.ExecuteNonQuery();
                }

                var id = cmd.SetCommandAndParameters(@"SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" = @Name")
                    .SetParameterValue("@Name", filename)
                    .ExecuteScalarInt64(-1);

                var aliens = cmd.SetCommandAndParameters(FormatInvariant($@"SELECT COUNT(*) FROM (SELECT ""A"".""VolumeID"" FROM ""{tablename}"" B LEFT OUTER JOIN ""Block"" A ON ""A"".""Hash"" = ""B"".""Hash"" AND ""A"".""Size"" = ""B"".""Size"") WHERE ""VolumeID"" != @VolumeId "))
                    .SetParameterValue("@VolumeId", id)
                    .ExecuteScalarInt64(0);

                cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{tablename}"" "));

                if (aliens != 0)
                    throw new Exception($"Not all blocks were found in {filename}");
            }
        }

        public void CheckBlocklistCorrect(string hash, long length, IEnumerable<string> blocklist, long blocksize, long blockhashlength)
        {
            using (var cmd = m_connection.CreateCommand())
            {
                var query = FormatInvariant($@"
SELECT 
    ""C"".""Hash"", 
    ""C"".""Size""
FROM 
    ""BlocksetEntry"" A, 
    (
        SELECT 
            ""Y"".""BlocksetID"",
            ""Y"".""Hash"" AS ""BlocklistHash"",
            ""Y"".""Index"" AS ""BlocklistHashIndex"",
            ""Z"".""Size"" AS ""BlocklistSize"",
            ""Z"".""ID"" AS ""BlocklistHashBlockID"" 
        FROM 
            ""BlocklistHash"" Y,
            ""Block"" Z 
        WHERE 
            ""Y"".""Hash"" = ""Z"".""Hash"" AND ""Y"".""Hash"" = @Hash AND ""Z"".""Size"" = @Size
        LIMIT 1
    ) B,
    ""Block"" C
WHERE 
    ""A"".""BlocksetID"" = ""B"".""BlocksetID"" 
    AND 
    ""A"".""BlockID"" = ""C"".""ID""
    AND
    ""A"".""Index"" >= ""B"".""BlocklistHashIndex"" * ({blocksize} / {blockhashlength})
    AND
    ""A"".""Index"" < (""B"".""BlocklistHashIndex"" + 1) * ({blocksize} / {blockhashlength})
ORDER BY 
    ""A"".""Index""

");

                using (var en = blocklist.GetEnumerator())
                {
                    cmd.SetCommandAndParameters(query)
                        .SetParameterValue("@Hash", hash)
                        .SetParameterValue("@Size", length);
                    foreach (var r in cmd.ExecuteReaderEnumerable())
                    {
                        if (!en.MoveNext())
                            throw new Exception($"Too few entries in source blocklist with hash {hash}");
                        if (en.Current != r.GetString(0))
                            throw new Exception($"Mismatch in blocklist with hash {hash}");
                    }

                    if (en.MoveNext())
                        throw new Exception($"Too many source blocklist entries in {hash}");
                }
            }
        }

        public IEnumerable<string> MissingLocalFilesets()
        {
            using var cmd = m_connection.CreateCommand(@"SELECT ""Name"" FROM ""RemoteVolume"" WHERE ""Type"" = @Type AND ""State"" NOT IN (@States) AND ""ID"" NOT IN (SELECT ""VolumeID"" FROM ""Fileset"")")
                .SetParameterValue("@Type", RemoteVolumeType.Files.ToString())
                .ExpandInClauseParameter("@States", [RemoteVolumeState.Deleting.ToString(), RemoteVolumeState.Deleted.ToString()]);

            foreach (var rd in cmd.ExecuteReaderEnumerable())
                yield return rd.ConvertValueToString(0);
        }

        public IEnumerable<(long FilesetID, DateTime Timestamp, bool IsFull)> MissingRemoteFilesets()
        {
            using var cmd = m_connection.CreateCommand(@"SELECT ""ID"", ""Timestamp"", ""IsFullBackup"" FROM ""Fileset"" WHERE ""VolumeID"" NOT IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Type"" = @Type AND ""State"" NOT IN (@States))")
                .SetParameterValue("@Type", RemoteVolumeType.Files.ToString())
                .ExpandInClauseParameter("@States", [RemoteVolumeState.Deleting.ToString(), RemoteVolumeState.Deleted.ToString()]);

            foreach (var rd in cmd.ExecuteReaderEnumerable())
                yield return (rd.ConvertValueToInt64(0), ParseFromEpochSeconds(rd.ConvertValueToInt64(1)), rd.ConvertValueToInt64(2) == BackupType.FULL_BACKUP);
        }

        public IEnumerable<IRemoteVolume> EmptyIndexFiles()
        {
            using var cmd = m_connection.CreateCommand(@"SELECT ""Name"", ""Hash"", ""Size"" FROM ""RemoteVolume"" WHERE ""Type"" = @Type AND ""State"" IN (@States) AND ""ID"" NOT IN (SELECT ""IndexVolumeId"" FROM ""IndexBlockLink"")")
                .SetParameterValue("@Type", RemoteVolumeType.Index.ToString())
                .ExpandInClauseParameter("@States", [RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString()]);

            foreach (var rd in cmd.ExecuteReaderEnumerable())
                yield return new RemoteVolume(rd.ConvertValueToString(0), rd.ConvertValueToString(1), rd.ConvertValueToInt64(2));
        }
    }
}

