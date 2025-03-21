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
                var filesetid = cmd.ExecuteScalarInt64(@"SELECT ""Fileset"".""ID"" FROM ""Fileset"",""RemoteVolume"" WHERE ""Fileset"".""VolumeID"" = ""RemoteVolume"".""ID"" AND ""RemoteVolume"".""Name"" = ?", -1, filelist);
                if (filesetid == -1)
                    throw new Exception($"No such remote file: {filelist}");

                return filesetid;
            }
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
            using (var cmd = m_connection.CreateCommand())
                foreach (var rd in cmd.ExecuteReaderEnumerable(@"SELECT ""Name"", ""Hash"", ""Size"" FROM ""RemoteVolume"" WHERE ""ID"" IN (SELECT ""BlockVolumeID"" FROM ""IndexBlockLink"" WHERE ""IndexVolumeID"" IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" = ?))", name))
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

                    var blockCount = cmd.ExecuteNonQuery(FormatInvariant($@"INSERT INTO ""{m_tablename}"" (""Hash"", ""Size"", ""Restored"") SELECT DISTINCT ""Block"".""Hash"", ""Block"".""Size"", 0 AS ""Restored"" FROM ""Block"",""Remotevolume"" WHERE ""Block"".""VolumeID"" = ""Remotevolume"".""ID"" AND ""Remotevolume"".""Name"" = ? "), volumename);
                    if (blockCount == 0)
                        throw new Exception($"Unexpected empty block volume: {0}");

                    cmd.ExecuteNonQuery(FormatInvariant($@"CREATE UNIQUE INDEX ""{tablename}-Ix"" ON ""{tablename}"" (""Hash"", ""Size"", ""Restored"")"));
                }

                m_insertCommand = m_connection.CreateCommand(m_transaction.Parent, FormatInvariant($@"UPDATE ""{tablename}"" SET ""Restored"" = ? WHERE ""Hash"" = ? AND ""Size"" = ? AND ""Restored"" = ? "));
            }

            public bool SetBlockRestored(string hash, long size)
            {
                m_insertCommand.SetParameterValue(0, 1);
                m_insertCommand.SetParameterValue(1, hash);
                m_insertCommand.SetParameterValue(2, size);
                m_insertCommand.SetParameterValue(3, 0);
                return m_insertCommand.ExecuteNonQuery() == 1;
            }

            public IEnumerable<IBlockWithSources> GetSourceFilesWithBlocks(long blocksize)
            {
                using (var cmd = m_connection.CreateCommand(m_transaction.Parent))
                using (var rd = cmd.ExecuteReader(FormatInvariant($@"SELECT DISTINCT ""{m_tablename}"".""Hash"", ""{m_tablename}"".""Size"", ""File"".""Path"", ""BlocksetEntry"".""Index"" * {blocksize} FROM  ""{m_tablename}"", ""Block"", ""BlocksetEntry"", ""File"" WHERE ""File"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID"" AND ""Block"".""ID"" = ""BlocksetEntry"".""BlockID"" AND ""{m_tablename}"".""Hash"" = ""Block"".""Hash"" AND ""{m_tablename}"".""Size"" = ""Block"".""Size"" AND ""{m_tablename}"".""Restored"" = ? "), 0))
                    if (rd.Read())
                    {
                        var bs = new BlockWithSources(rd);
                        while (!bs.Done)
                            yield return bs;
                    }
            }

            public IEnumerable<KeyValuePair<string, long>> GetMissingBlocks()
            {
                using (var cmd = m_connection.CreateCommand(m_transaction.Parent))
                    foreach (var rd in cmd.ExecuteReaderEnumerable(FormatInvariant($@"SELECT ""{m_tablename}"".""Hash"", ""{m_tablename}"".""Size"" FROM ""{m_tablename}"" WHERE ""{m_tablename}"".""Restored"" = ? "), 0))
                        yield return new KeyValuePair<string, long>(rd.ConvertValueToString(0), rd.ConvertValueToInt64(1));
            }

            public IEnumerable<IRemoteVolume> GetFilesetsUsingMissingBlocks()
            {
                var blocks = FormatInvariant($@"SELECT DISTINCT ""FileLookup"".""ID"" AS ID FROM ""{m_tablename}"", ""Block"", ""Blockset"", ""BlocksetEntry"", ""FileLookup"" WHERE ""Block"".""Hash"" = ""{m_tablename}"".""Hash"" AND ""Block"".""Size"" = ""{m_tablename}"".""Size"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" AND ""BlocksetEntry"".""BlocksetID"" = ""Blockset"".""ID"" AND ""FileLookup"".""BlocksetID"" = ""Blockset"".""ID"" ");
                var blocklists = FormatInvariant($@"SELECT DISTINCT ""FileLookup"".""ID"" AS ID FROM ""{m_tablename}"", ""Block"", ""Blockset"", ""BlocklistHash"", ""FileLookup"" WHERE ""Block"".""Hash"" = ""{m_tablename}"".""Hash"" AND ""Block"".""Size"" = ""{m_tablename}"".""Size"" AND ""BlocklistHash"".""Hash"" = ""Block"".""Hash"" AND ""BlocklistHash"".""BlocksetID"" = ""Blockset"".""ID"" AND ""FileLookup"".""BlocksetID"" = ""Blockset"".""ID"" ");
                var cmdtxt = FormatInvariant($@"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"" FROM ""RemoteVolume"", ""FilesetEntry"", ""Fileset"" WHERE ""RemoteVolume"".""ID"" = ""Fileset"".""VolumeID"" AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID"" AND ""RemoteVolume"".""Type"" = ? AND ""FilesetEntry"".""FileID"" IN  (SELECT DISTINCT ""ID"" FROM ( {blocks} UNION {blocklists} ))");

                using (var cmd = m_connection.CreateCommand(m_transaction.Parent))
                    foreach (var rd in cmd.ExecuteReaderEnumerable(cmdtxt, RemoteVolumeType.Files.ToString()))
                        yield return new RemoteVolume(rd.GetString(0), rd.ConvertValueToString(1), rd.ConvertValueToInt64(2));
            }

            public IEnumerable<IRemoteVolume> GetMissingBlockSources()
            {
                using (var cmd = m_connection.CreateCommand(m_transaction.Parent))
                    foreach (var rd in cmd.ExecuteReaderEnumerable(FormatInvariant($@"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"" FROM ""RemoteVolume"", ""Block"", ""{m_tablename}"" WHERE ""Block"".""Hash"" = ""{m_tablename}"".""Hash"" AND ""Block"".""Size"" = ""{m_tablename}"".""Size"" AND ""Block"".""VolumeID"" = ""RemoteVolume"".""ID"" AND ""Remotevolume"".""Name"" != ? "), m_volumename))
                        yield return new RemoteVolume(rd.GetString(0), rd.ConvertValueToString(1), rd.ConvertValueToInt64(2));
            }

            public void Dispose()
            {
                if (m_tablename != null)
                {
                    try
                    {
                        using (var cmd = m_connection.CreateCommand(m_transaction.Parent))
                            cmd.ExecuteNonQuery(FormatInvariant($@"DROP TABLE IF EXISTS ""{m_tablename}"" "), m_transaction);
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
                        c2.CommandText = FormatInvariant($@"UPDATE ""{tablename}"" SET ""MetadataID"" = ? WHERE ""MetadataID"" IN (SELECT ""ID"" FROM ""Metadataset"" WHERE ""BlocksetID"" = ?)");
                        c2.CommandText += @"; DELETE FROM ""Metadataset"" WHERE ""BlocksetID"" = ? AND ""ID"" != ?";
                        using (var rd = cmd.ExecuteReader(sql))
                            while (rd.Read())
                                c2.ExecuteNonQuery(null, rd.GetValue(0), rd.GetValue(1), rd.GetValue(1), rd.GetValue(0));
                    }

                    sql = FormatInvariant($@"SELECT ""ID"", ""Path"", ""BlocksetID"", ""MetadataID"", ""Entries"" FROM (
                            SELECT MIN(""ID"") AS ""ID"", ""Path"", ""BlocksetID"", ""MetadataID"", COUNT(*) as ""Entries"" FROM ""{tablename}"" GROUP BY ""Path"", ""BlocksetID"", ""MetadataID"") 
                            WHERE ""Entries"" > 1 ORDER BY ""ID""");

                    using (var c2 = m_connection.CreateCommand(tr))
                    {
                        c2.CommandText = FormatInvariant($@"UPDATE ""FilesetEntry"" SET ""FileID"" = ? WHERE ""FileID"" IN (SELECT ""ID"" FROM ""{tablename}"" WHERE ""Path"" = ? AND ""BlocksetID"" = ? AND ""MetadataID"" = ?)");
                        c2.CommandText += FormatInvariant($@"; DELETE FROM ""{tablename}"" WHERE ""Path"" = ? AND ""BlocksetID"" = ? AND ""MetadataID"" = ? AND ""ID"" != ?");
                        foreach (var rd in cmd.ExecuteReaderEnumerable(sql))
                            c2.ExecuteNonQuery(null, rd.GetValue(0), rd.GetValue(1), rd.GetValue(2), rd.GetValue(3), rd.GetValue(1), rd.GetValue(2), rd.GetValue(3), rd.GetValue(0));
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
                        c2.CommandText = @"UPDATE ""FilesetEntry"" SET ""FileID"" = ? WHERE ""FileID"" IN (SELECT ""ID"" FROM ""FileLookup"" WHERE ""PrefixID"" = ? AND ""Path"" = ? AND ""BlocksetID"" = ? AND ""MetadataID"" = ?)";
                        c2.CommandText += @"; DELETE FROM ""FileLookup"" WHERE ""PrefixID"" = ? AND ""Path"" = ? AND ""BlocksetID"" = ? AND ""MetadataID"" = ? AND ""ID"" != ?";
                        foreach (var rd in cmd.ExecuteReaderEnumerable(sql))
                            c2.ExecuteNonQuery(null, rd.GetValue(0), rd.GetValue(1), rd.GetValue(2), rd.GetValue(3), rd.GetValue(4), rd.GetValue(1), rd.GetValue(2), rd.GetValue(3), rd.GetValue(4), rd.GetValue(0));
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
                        c3.SetCommandAndParameters(@"INSERT INTO ""BlocklistHash"" (""BlocksetID"", ""Index"", ""Hash"") VALUES (?, ?, ?) ");
                        c4.SetCommandAndParameters(@"SELECT COUNT(*) FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ?");
                        c5.SetCommandAndParameters(@"SELECT ""ID"" FROM ""DeletedBlock"" WHERE ""Hash"" = ? AND ""Size"" = ? AND ""VolumeID"" IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Type"" = ? AND (""State"" = ? OR ""State"" = ?))");
                        c6.SetCommandAndParameters(@"INSERT INTO ""Block"" (""Hash"", ""Size"", ""VolumeID"") SELECT ""Hash"", ""Size"", ""VolumeID"" FROM ""DeletedBlock"" WHERE ""ID"" = ? LIMIT 1; DELETE FROM ""DeletedBlock"" WHERE ""ID"" = ?;");

                        foreach (var e in cmd.ExecuteReaderEnumerable(sql))
                        {
                            var blocksetid = e.ConvertValueToInt64(0);
                            var ix = 0L;
                            int blocklistoffset = 0;

                            c2.ExecuteNonQuery(@"DELETE FROM ""BlocklistHash"" WHERE ""BlocksetID"" = ?", blocksetid);

                            foreach (var h in c2.ExecuteReaderEnumerable(@"SELECT ""A"".""Hash"" FROM ""Block"" ""A"", ""BlocksetEntry"" ""B"" WHERE ""A"".""ID"" = ""B"".""BlockID"" AND ""B"".""BlocksetID"" = ? ORDER BY ""B"".""Index""", blocksetid))
                            {
                                var tmp = Convert.FromBase64String(h.GetString(0));
                                if (blocklistbuffer.Length - blocklistoffset < tmp.Length)
                                {
                                    var blkey = Convert.ToBase64String(blockhasher.ComputeHash(blocklistbuffer, 0, blocklistoffset));

                                    // Ensure that the block exists in "blocks"
                                    if (c4.ExecuteScalarInt64(null, -1, blkey, blocklistoffset) != 1)
                                    {
                                        var c = c5.ExecuteScalarInt64(null, -1, blkey, blocklistoffset, RemoteVolumeType.Blocks.ToString(), RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString());
                                        if (c <= 0)
                                            throw new Exception($"Missing block for blocklisthash: {blkey}");
                                        else
                                        {
                                            var rc = c6.ExecuteNonQuery(null, c, c);
                                            if (rc != 2)
                                                throw new Exception($"Unexpected update count: {rc}");
                                        }
                                    }

                                    // Add to table
                                    c3.ExecuteNonQuery(null, blocksetid, ix, blkey);
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
                                if (c4.ExecuteScalarInt64(null, -1, blkeyfinal, blocklistoffset) != 1)
                                {
                                    var c = c5.ExecuteScalarInt64(null, -1, blkeyfinal, blocklistoffset, RemoteVolumeType.Blocks.ToString(), RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString());
                                    if (c == 0)
                                        throw new Exception($"Missing block for blocklisthash: {blkeyfinal}");
                                    else
                                    {
                                        var rc = c6.ExecuteNonQuery(null, c, c);
                                        if (rc != 2)
                                            throw new Exception($"Unexpected update count: {rc}");
                                    }
                                }

                                // Add to table
                                c3.ExecuteNonQuery(null, blocksetid, ix, blkeyfinal);
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
                        c2.CommandText = @"DELETE FROM ""BlocklistHash"" WHERE rowid IN (SELECT rowid FROM ""BlocklistHash"" WHERE ""BlocksetID"" = ? AND ""Index"" = ? LIMIT ?)";
                        foreach (var rd in cmd.ExecuteReaderEnumerable(dup_sql))
                        {
                            var expected = rd.GetInt32(2) - 1;
                            var actual = c2.ExecuteNonQuery(null, rd.GetValue(0), rd.GetValue(1), expected);
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
                cmd.CommandText = FormatInvariant($@"INSERT INTO ""{tablename}"" (""Hash"", ""Size"") VALUES (?, ?)");
                cmd.AddParameters(2);

                foreach (var kp in blocks)
                {
                    cmd.SetParameterValue(0, kp.Key);
                    cmd.SetParameterValue(1, kp.Value);
                    cmd.ExecuteNonQuery();
                }

                var id = cmd.ExecuteScalarInt64(@"SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" = ?", -1, filename);
                var aliens = cmd.ExecuteScalarInt64(FormatInvariant($@"SELECT COUNT(*) FROM (SELECT ""A"".""VolumeID"" FROM ""{tablename}"" B LEFT OUTER JOIN ""Block"" A ON ""A"".""Hash"" = ""B"".""Hash"" AND ""A"".""Size"" = ""B"".""Size"") WHERE ""VolumeID"" != ? "), 0, id);

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
            ""Y"".""Hash"" = ""Z"".""Hash"" AND ""Y"".""Hash"" = ? AND ""Z"".""Size"" = ?
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
                    foreach (var r in cmd.ExecuteReaderEnumerable(query, hash, length))
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
            using (var cmd = m_connection.CreateCommand())
                foreach (var rd in cmd.ExecuteReaderEnumerable(@"SELECT ""Name"" FROM ""RemoteVolume"" WHERE ""Type"" = ? AND ""State"" NOT IN (?, ?) AND ""ID"" NOT IN (SELECT ""VolumeID"" FROM ""Fileset"")", RemoteVolumeType.Files.ToString(), RemoteVolumeState.Deleting.ToString(), RemoteVolumeState.Deleted.ToString()))
                    yield return rd.ConvertValueToString(0);
        }

        public IEnumerable<(long FilesetID, DateTime Timestamp, bool IsFull)> MissingRemoteFilesets()
        {
            using (var cmd = m_connection.CreateCommand())
                foreach (var rd in cmd.ExecuteReaderEnumerable(@"SELECT ""ID"", ""Timestamp"", ""IsFullBackup"" FROM ""Fileset"" WHERE ""VolumeID"" NOT IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Type"" = ? AND ""State"" NOT IN (?, ?))", RemoteVolumeType.Files.ToString(), RemoteVolumeState.Deleting.ToString(), RemoteVolumeState.Deleted.ToString()))
                    yield return (rd.ConvertValueToInt64(0), ParseFromEpochSeconds(rd.ConvertValueToInt64(1)), rd.ConvertValueToInt64(2) == BackupType.FULL_BACKUP);
        }

        public IEnumerable<IRemoteVolume> EmptyIndexFiles()
        {
            using (var cmd = m_connection.CreateCommand())
                foreach (var rd in cmd.ExecuteReaderEnumerable(@"SELECT ""Name"", ""Hash"", ""Size"" FROM ""RemoteVolume"" WHERE ""Type"" = ? AND ""State"" IN (?, ?, ?) AND ""ID"" NOT IN (SELECT ""IndexVolumeId"" FROM ""IndexBlockLink"")", RemoteVolumeType.Index.ToString(), RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString()))
                    yield return new RemoteVolume(rd.ConvertValueToString(0), rd.ConvertValueToString(1), rd.ConvertValueToInt64(2));
        }
    }
}

