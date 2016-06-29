//  Copyright (C) 2013, The Duplicati Team

//  http://www.duplicati.com, opensource@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Linq;
using System.Collections.Generic;

namespace Duplicati.Library.Main.Database
{
	internal class LocalRepairDatabase : LocalDatabase
	{
		public LocalRepairDatabase(string path)
			: base(path, "Repair", true)
		{
		
		}
		
		public long GetFilesetIdFromRemotename(string filelist)
		{
			using(var cmd = m_connection.CreateCommand())
			{
				var filesetid = cmd.ExecuteScalarInt64(@"SELECT ""Fileset"".""ID"" FROM ""Fileset"",""RemoteVolume"" WHERE ""Fileset"".""VolumeID"" = ""RemoteVolume"".""ID"" AND ""RemoteVolume"".""Name"" = ?", -1, filelist);
                if (filesetid == -1)
					throw new Exception(string.Format("No such remote file: {0}", filelist));
					
                return filesetid;
			}
		}

		public interface IBlockSource
		{
			string File { get; }
			long Offset { get; }
		}
		
		public interface IBlockWithSources :  LocalBackupDatabase.IBlock
		{
			IEnumerable<IBlockSource> Sources { get; }
		}
		
		private class BlockWithSources : LocalBackupDatabase.Block, IBlockWithSources
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
		
			private System.Data.IDataReader m_rd;
			public bool Done { get; private set; }
			
			public BlockWithSources(System.Data.IDataReader rd)
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
					
					while(!Done && cur.File == file)
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
			using(var cmd = m_connection.CreateCommand())
                foreach(var rd in cmd.ExecuteReaderEnumerable(@"SELECT ""Name"", ""Hash"", ""Size"" FROM ""RemoteVolume"" WHERE ""ID"" IN (SELECT ""BlockVolumeID"" FROM ""IndexBlockLink"" WHERE ""IndexVolumeID"" IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" = ?))", name))
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
            private System.Data.IDbConnection m_connection;
            private TemporaryTransactionWrapper m_transaction;
            private System.Data.IDbCommand m_insertCommand;
            private string m_tablename;
            private string m_volumename;
            
            public MissingBlockList(string volumename, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction)
            {
                m_connection = connection;
                m_transaction = new TemporaryTransactionWrapper(m_connection, transaction);
                m_volumename = volumename;
                var tablename = "MissingBlocks-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction.Parent;
                    cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL, ""Restored"" INTEGER NOT NULL) ", tablename));
                    m_tablename = tablename;
                    
                    var blockCount = cmd.ExecuteNonQuery(string.Format(@"INSERT INTO ""{0}"" (""Hash"", ""Size"", ""Restored"") SELECT DISTINCT ""Block"".""Hash"", ""Block"".""Size"", 0 AS ""Restored"" FROM ""Block"",""Remotevolume"" WHERE ""Block"".""VolumeID"" = ""Remotevolume"".""ID"" AND ""Remotevolume"".""Name"" = ? ", m_tablename), volumename);
                    if (blockCount == 0)
                        throw new Exception(string.Format("Unexpected empty block volume: {0}", volumename));

                    cmd.ExecuteNonQuery(string.Format(@"CREATE UNIQUE INDEX ""{0}-Ix"" ON ""{0}"" (""Hash"", ""Size"", ""Restored"")", tablename));
                }

                m_insertCommand = m_connection.CreateCommand();
                m_insertCommand.Transaction = m_transaction.Parent;
                m_insertCommand.CommandText = string.Format(@"UPDATE ""{0}"" SET ""Restored"" = ? WHERE ""Hash"" = ? AND ""Size"" = ? AND ""Restored"" = ? ", tablename);
                m_insertCommand.AddParameters(4);
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
                using(var cmd = m_connection.CreateCommand(m_transaction.Parent))
                    using(var rd = cmd.ExecuteReader(string.Format(@"SELECT DISTINCT ""{0}"".""Hash"", ""{0}"".""Size"", ""File"".""Path"", ""BlocksetEntry"".""Index"" * {1} FROM  ""{0}"", ""Block"", ""BlocksetEntry"", ""File"" WHERE ""File"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID"" AND ""Block"".""ID"" = ""BlocksetEntry"".""BlockID"" AND ""{0}"".""Hash"" = ""Block"".""Hash"" AND ""{0}"".""Size"" = ""Block"".""Size"" AND ""{0}"".""Restored"" = ? ", m_tablename, blocksize), 0))
                        if (rd.Read())
                        {
                            var bs = new BlockWithSources(rd);
                            while (!bs.Done)
                                yield return (IBlockWithSources)bs;
                        }
            }
                    
            public IEnumerable<KeyValuePair<string, long>> GetMissingBlocks()
            {
                using(var cmd = m_connection.CreateCommand(m_transaction.Parent))
                    foreach(var rd in cmd.ExecuteReaderEnumerable(string.Format(@"SELECT ""{0}"".""Hash"", ""{0}"".""Size"" FROM ""{0}"" WHERE ""{0}"".""Restored"" = ? ", m_tablename), 0))
                        yield return new KeyValuePair<string, long>(rd.ConvertValueToString(0), rd.ConvertValueToInt64(1));
            }
            
            public IEnumerable<IRemoteVolume> GetFilesetsUsingMissingBlocks()
            {
                var blocks = @"SELECT DISTINCT ""File"".""ID"" AS ID FROM ""{0}"", ""Block"", ""Blockset"", ""BlocksetEntry"", ""File"" WHERE ""Block"".""Hash"" = ""{0}"".""Hash"" AND ""Block"".""Size"" = ""{0}"".""Size"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" AND ""BlocksetEntry"".""BlocksetID"" = ""Blockset"".""ID"" AND ""File"".""BlocksetID"" = ""Blockset"".""ID"" ";
                var blocklists = @"SELECT DISTINCT ""File"".""ID"" AS ID FROM ""{0}"", ""Block"", ""Blockset"", ""BlocklistHash"", ""File"" WHERE ""Block"".""Hash"" = ""{0}"".""Hash"" AND ""Block"".""Size"" = ""{0}"".""Size"" AND ""BlocklistHash"".""Hash"" = ""Block"".""Hash"" AND ""BlocklistHash"".""BlocksetID"" = ""Blockset"".""ID"" AND ""File"".""BlocksetID"" = ""Blockset"".""ID"" ";
            
                var cmdtxt = @"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"" FROM ""RemoteVolume"", ""FilesetEntry"", ""Fileset"" WHERE ""RemoteVolume"".""ID"" = ""Fileset"".""VolumeID"" AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID"" AND ""RemoteVolume"".""Type"" = ? AND ""FilesetEntry"".""FileID"" IN  (SELECT DISTINCT ""ID"" FROM ( " + blocks + " UNION " + blocklists + " ))";
            
                using(var cmd = m_connection.CreateCommand(m_transaction.Parent))
                    foreach(var rd in cmd.ExecuteReaderEnumerable(string.Format(cmdtxt, m_tablename), RemoteVolumeType.Files.ToString()))
                        yield return new RemoteVolume(rd.GetString(0), rd.ConvertValueToString(1), rd.ConvertValueToInt64(2));
            }
            
            public IEnumerable<IRemoteVolume> GetMissingBlockSources()
            {
                using(var cmd = m_connection.CreateCommand(m_transaction.Parent))
                    foreach(var rd in cmd.ExecuteReaderEnumerable(string.Format(@"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"" FROM ""RemoteVolume"", ""Block"", ""{0}"" WHERE ""Block"".""Hash"" = ""{0}"".""Hash"" AND ""Block"".""Size"" = ""{0}"".""Size"" AND ""Block"".""VolumeID"" = ""RemoteVolume"".""ID"" AND ""Remotevolume"".""Name"" != ? ", m_tablename), m_volumename))
                        yield return new RemoteVolume(rd.GetString(0), rd.ConvertValueToString(1), rd.ConvertValueToInt64(2));
            }
            
            public void Dispose()
            {
                if (m_tablename != null)
                {
                    try
                    {
                        using(var cmd = m_connection.CreateCommand(m_transaction.Parent))
                            cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", m_transaction));
                    }
                    catch { }
                    finally { m_tablename = null; }
                }
                
                if (m_insertCommand != null)
                    try { m_insertCommand.Dispose(); }
                    catch {}
                    finally { m_insertCommand = null; }
            }
        }
        
        public IMissingBlockList CreateBlockList(string volumename, System.Data.IDbTransaction transaction = null)
        {
            return new MissingBlockList(volumename, m_connection, transaction);
        }

        public void FixDuplicateMetahash()
        {
            using(var tr = m_connection.BeginTransaction())
            using(var cmd = m_connection.CreateCommand(tr))
            {
                cmd.Transaction = tr;

                var sql_count = 
                    @"SELECT COUNT(*) FROM (" +
                    @" SELECT DISTINCT c1 FROM (" +
                    @"SELECT COUNT(*) AS ""C1"" FROM (SELECT DISTINCT ""BlocksetID"" FROM ""Metadataset"") UNION SELECT COUNT(*) AS ""C1"" FROM ""Metadataset"" " +
                    @")" +
                    @")";

                var x = cmd.ExecuteScalarInt64(sql_count, 0);
                if (x > 1)
                {
                    m_result.AddMessage("Found duplicate metadatahashes, repairing");

                    var tablename = "TmpFile-" + Guid.NewGuid().ToString("N");

                    cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS SELECT * FROM ""File""", tablename));

                    var sql = @"SELECT ""A"".""ID"", ""B"".""BlocksetID"" FROM (SELECT MIN(""ID"") AS ""ID"", COUNT(""ID"") AS ""Duplicates"" FROM ""Metadataset"" GROUP BY ""BlocksetID"") ""A"", ""Metadataset"" ""B"" WHERE ""A"".""Duplicates"" > 1 AND ""A"".""ID"" = ""B"".""ID""";

                    using(var c2 = m_connection.CreateCommand(tr))
                    {
                        c2.CommandText = string.Format(@"UPDATE ""{0}"" SET ""MetadataID"" = ? WHERE ""MetadataID"" IN (SELECT ""ID"" FROM ""Metadataset"" WHERE ""BlocksetID"" = ?)", tablename);
                        c2.CommandText += @"; DELETE FROM ""Metadataset"" WHERE ""BlocksetID"" = ? AND ""ID"" != ?";
                        using(var rd = cmd.ExecuteReader(sql))
                            while (rd.Read())
                                c2.ExecuteNonQuery(null, rd.GetValue(0), rd.GetValue(1), rd.GetValue(1), rd.GetValue(0));
                    }

                    sql = string.Format(@"SELECT ""ID"", ""Path"", ""BlocksetID"", ""MetadataID"", ""Entries"" FROM (
                            SELECT MIN(""ID"") AS ""ID"", ""Path"", ""BlocksetID"", ""MetadataID"", COUNT(*) as ""Entries"" FROM ""{0}"" GROUP BY ""Path"", ""BlocksetID"", ""MetadataID"") 
                            WHERE ""Entries"" > 1 ORDER BY ""ID""", tablename);

                    using(var c2 = m_connection.CreateCommand())
                    {
                        c2.Transaction = tr;
                        c2.CommandText = string.Format(@"UPDATE ""FilesetEntry"" SET ""FileID"" = ? WHERE ""FileID"" IN (SELECT ""ID"" FROM ""{0}"" WHERE ""Path"" = ? AND ""BlocksetID"" = ? AND ""MetadataID"" = ?)", tablename);
                        c2.CommandText += string.Format(@"; DELETE FROM ""{0}"" WHERE ""Path"" = ? AND ""BlocksetID"" = ? AND ""MetadataID"" = ? AND ""ID"" != ?", tablename);
                        foreach(var rd in cmd.ExecuteReaderEnumerable(sql))
                            c2.ExecuteNonQuery(null, rd.GetValue(0), rd.GetValue(1), rd.GetValue(2), rd.GetValue(3), rd.GetValue(1), rd.GetValue(2), rd.GetValue(3), rd.GetValue(0));
                    }

                    cmd.ExecuteNonQuery(string.Format(@"DELETE FROM ""File"" WHERE ""ID"" NOT IN (SELECT ""ID"" FROM ""{0}"") ", tablename));
                    cmd.ExecuteNonQuery(string.Format(@"CREATE INDEX ""{0}-Ix"" ON  ""{0}"" (""ID"", ""MetadataID"")", tablename));
                    cmd.ExecuteNonQuery(string.Format(@"UPDATE ""File"" SET ""MetadataID"" = (SELECT ""MetadataID"" FROM ""{0}"" A WHERE ""A"".""ID"" = ""File"".""ID"") ", tablename));
                    cmd.ExecuteNonQuery(string.Format(@"DROP TABLE ""{0}"" ", tablename));

                    cmd.CommandText = sql_count;
                    x = cmd.ExecuteScalarInt64(0);
                    if (x > 1)
                        throw new Exception("Repair failed, there are still duplicate metadatahashes!");

                    m_result.AddMessage("Duplicate metadatahashes repaired succesfully");
                    tr.Commit();
                }
            }
        }

        public void FixDuplicateFileentries()
        {
            using(var tr = m_connection.BeginTransaction())
            using(var cmd = m_connection.CreateCommand(tr))
            {
                var sql_count = @"SELECT COUNT(*) FROM (SELECT ""Path"", ""BlocksetID"", ""MetadataID"", COUNT(*) as ""Duplicates"" FROM ""File"" GROUP BY ""Path"", ""BlocksetID"", ""MetadataID"") WHERE ""Duplicates"" > 1";

                var x = cmd.ExecuteScalarInt64(sql_count, 0);
                if (x > 0)
                {
                    m_result.AddMessage("Found duplicate file entries, repairing");

                    var sql = @"SELECT ""ID"", ""Path"", ""BlocksetID"", ""MetadataID"", ""Entries"" FROM (
                            SELECT MIN(""ID"") AS ""ID"", ""Path"", ""BlocksetID"", ""MetadataID"", COUNT(*) as ""Entries"" FROM ""File"" GROUP BY ""Path"", ""BlocksetID"", ""MetadataID"") 
                            WHERE ""Entries"" > 1 ORDER BY ""ID""";

                    using(var c2 = m_connection.CreateCommand(tr))
                    {
                        c2.CommandText = @"UPDATE ""FilesetEntry"" SET ""FileID"" = ? WHERE ""FileID"" IN (SELECT ""ID"" FROM ""File"" WHERE ""Path"" = ? AND ""BlocksetID"" = ? AND ""MetadataID"" = ?)";
                        c2.CommandText += @"; DELETE FROM ""File"" WHERE ""Path"" = ? AND ""BlocksetID"" = ? AND ""MetadataID"" = ? AND ""ID"" != ?";
                        foreach(var rd in cmd.ExecuteReaderEnumerable(sql))
                            c2.ExecuteNonQuery(null, rd.GetValue(0), rd.GetValue(1), rd.GetValue(2), rd.GetValue(3), rd.GetValue(1), rd.GetValue(2), rd.GetValue(3), rd.GetValue(0));
                    }

                    cmd.CommandText = sql_count;
                    x = cmd.ExecuteScalarInt64(0);
                    if (x > 1)
                        throw new Exception("Repair failed, there are still duplicate file entries!");

                    m_result.AddMessage("Duplicate file entries repaired succesfully");
                    tr.Commit();
                }
            }

        }

        public void FixMissingBlocklistHashes(string blockhashalgorithm, long blocksize)
        {
            var blockhasher = System.Security.Cryptography.HashAlgorithm.Create(blockhashalgorithm);
            var hashsize = blockhasher.HashSize / 8;
            var blocklistbuffer = new byte[blocksize];
            int blocklistoffset = 0;

            blockhasher.Initialize();

            var sql = string.Format(@"SELECT * FROM (SELECT ""N"".""BlocksetID"", ((""N"".""BlockCount"" + {0} - 1) / {0}) AS ""BlocklistHashCountExpected"", CASE WHEN ""G"".""BlocklistHashCount"" IS NULL THEN 0 ELSE ""G"".""BlocklistHashCount"" END AS ""BlocklistHashCountActual"" FROM (SELECT ""BlocksetID"", COUNT(*) AS ""BlockCount"" FROM ""BlocksetEntry"" GROUP BY ""BlocksetID"") ""N"" LEFT OUTER JOIN (SELECT ""BlocksetID"", COUNT(*) AS ""BlocklistHashCount"" FROM ""BlocklistHash"" GROUP BY ""BlocksetID"") ""G"" ON ""N"".""BlocksetID"" = ""G"".""BlocksetID"" WHERE ""N"".""BlockCount"" > 1) WHERE ""BlocklistHashCountExpected"" != ""BlocklistHashCountActual""", blocksize / hashsize);
            var countsql = @"SELECT COUNT(*) FROM (" + sql + @")";

            using(var tr = m_connection.BeginTransaction())
            using(var cmd = m_connection.CreateCommand(tr))
            {
                var itemswithnoblocklisthash = cmd.ExecuteScalarInt64(countsql, 0);
                if (itemswithnoblocklisthash != 0)
                {
                    m_result.AddMessage(string.Format("Found {0} missing blocklisthash entries, repairing", itemswithnoblocklisthash));
                    using(var c2 = m_connection.CreateCommand(tr))
                    using(var c3 = m_connection.CreateCommand(tr))
                    using(var c4 = m_connection.CreateCommand(tr))
                    using(var c5 = m_connection.CreateCommand(tr))
                    using(var c6 = m_connection.CreateCommand(tr))
                    {
                        c3.CommandText = @"INSERT INTO ""BlocklistHash"" (""BlocksetID"", ""Index"", ""Hash"") VALUES (?, ?, ?) ";
                        c4.CommandText = @"SELECT COUNT(*) FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ?";
                        c5.CommandText = @"SELECT ""ID"" FROM ""DeletedBlock"" WHERE ""Hash"" = ? AND ""Size"" = ? AND ""VolumeID"" IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Type"" = ? AND (""State"" = ? OR ""State"" = ?))";
                        c6.CommandText = @"INSERT INTO ""Block"" (""Hash"", ""Size"", ""VolumeID"") SELECT ""Hash"", ""Size"", ""VolumeID"" FROM ""DeletedBlock"" WHERE ""ID"" = ? LIMIT 1; DELETE FROM ""DeletedBlock"" WHERE ""ID"" = ?;";

                        foreach(var e in cmd.ExecuteReaderEnumerable(sql))
                        {
                            var blocksetid = e.ConvertValueToInt64(0);
                            var ix = 0L;

                            c2.ExecuteNonQuery(@"DELETE FROM ""BlocklistHash"" WHERE ""BlocksetID"" = ?", blocksetid);

                            foreach(var h in c2.ExecuteReaderEnumerable(@"SELECT ""A"".""Hash"" FROM ""Block"" ""A"", ""BlocksetEntry"" ""B"" WHERE ""A"".""ID"" = ""B"".""BlockID"" AND ""B"".""BlocksetID"" = ? ORDER BY ""B"".""Index""", blocksetid))
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
                                            throw new Exception(string.Format("Missing block for blocklisthash: {0}", blkey));
                                        else
                                        {
                                            var rc = c6.ExecuteNonQuery(null, c, c);
                                            if (rc != 2)
                                                throw new Exception(string.Format("Unexpected update count: {0}", rc));
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
                                        throw new Exception(string.Format("Missing block for blocklisthash: {0}", blkeyfinal));
                                    else
                                    {
                                        var rc = c6.ExecuteNonQuery(null, c, c);
                                        if (rc != 2)
                                            throw new Exception(string.Format("Unexpected update count: {0}", rc));
                                    }
                                }

                                // Add to table
                                c3.ExecuteNonQuery(null, blocksetid, ix, blkeyfinal);
                                ix++;
                            }
                        }
                    }


                    itemswithnoblocklisthash = cmd.ExecuteScalarInt64(countsql, 0);
                    if (itemswithnoblocklisthash != 0)
                        throw new Exception(string.Format("Failed to repair, after repair {0} blocklisthashes were missing", itemswithnoblocklisthash));

                    m_result.AddMessage("Missing blocklisthashes repaired succesfully");
                    tr.Commit();
                }
            }

        }

        public void FixDuplicateBlocklistHashes(long blocksize, long hashsize)
        {
            using(var tr = m_connection.BeginTransaction())
            using(var cmd = m_connection.CreateCommand(tr))
            {
                var dup_sql = @"SELECT * FROM (SELECT ""BlocksetID"", ""Index"", COUNT(*) AS ""EC"" FROM ""BlocklistHash"" GROUP BY ""BlocksetID"", ""Index"") WHERE ""EC"" > 1";

                var sql_count = @"SELECT COUNT(*) FROM (" + dup_sql + ")";

                var x = cmd.ExecuteScalarInt64(sql_count, 0);
                if (x > 0)
                {
                    m_result.AddMessage("Found duplicate blocklisthash entries, repairing");

                    var unique_count = cmd.ExecuteScalarInt64(@"SELECT COUNT(*) FROM (SELECT DISTINCT ""BlocksetID"", ""Index"" FROM ""BlocklistHash"")", 0);

                    using(var c2 = m_connection.CreateCommand(tr))
                    {
                        c2.CommandText = @"DELETE FROM ""BlocklistHash"" WHERE rowid IN (SELECT rowid FROM ""BlocklistHash"" WHERE ""BlocksetID"" = ? AND ""Index"" = ? LIMIT ?)";
                        foreach(var rd in cmd.ExecuteReaderEnumerable(dup_sql))
                        {
                            var expected = rd.GetInt32(2) - 1;
                            var actual = c2.ExecuteNonQuery(null, rd.GetValue(0), rd.GetValue(1), expected);
                            if (actual != expected)
                                throw new Exception(string.Format("Unexpected number of results after fix, got: {0}, expected: {1}", actual, expected));
                        }
                    }

                    cmd.CommandText = sql_count;
                    x = cmd.ExecuteScalarInt64();
                    if (x > 1)
                        throw new Exception("Repair failed, there are still duplicate file entries!");

                    var real_count = cmd.ExecuteScalarInt64(@"SELECT Count(*) FROM ""BlocklistHash""", 0);

                    if (real_count != unique_count)
                        throw new Exception(string.Format("Failed to repair, result should have been {0} blocklist hashes, but result was {1} blocklist hashes", unique_count, real_count));

                    try
                    {
                        VerifyConsistency(tr, blocksize, hashsize);
                    }
                    catch(Exception ex)
                    {
                        throw new Exception("Repaired blocklisthashes, but the database was broken afterwards, rolled back changes", ex);
                    }

                    m_result.AddMessage("Duplicate blocklisthashes repaired succesfully");
                    tr.Commit();
                }
            }
        }

        public void CheckAllBlocksAreInVolume(string filename, IEnumerable<KeyValuePair<string, long>> blocks)
        {
            using(var tr = m_connection.BeginTransaction())
            using(var cmd = m_connection.CreateCommand(tr))
            {
                var tablename = "ProbeBlocks-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());

                cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""Hash"" TEXT NOT NULL, ""Size"" INTEGER NOT NULL)", tablename));
                cmd.CommandText = string.Format(@"INSERT INTO ""{0}"" (""Hash"", ""Size"") VALUES (?, ?)", tablename);
                cmd.AddParameters(2);

                foreach(var kp in blocks)
                {
                    cmd.SetParameterValue(0, kp.Key);
                    cmd.SetParameterValue(1, kp.Value);
                    cmd.ExecuteNonQuery();
                }

                var id = cmd.ExecuteScalarInt64(@"SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" = ?", -1, filename);
                var aliens = cmd.ExecuteScalarInt64(string.Format(@"SELECT COUNT(*) FROM (SELECT ""A"".""VolumeID"" FROM ""{0}"" B LEFT OUTER JOIN ""Block"" A ON ""A"".""Hash"" = ""B"".""Hash"" AND ""A"".""Size"" = ""B"".""Size"") WHERE ""VolumeID"" != ? ", tablename), 0, id);

                cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", tablename));

                if (aliens != 0)
                    throw new Exception(string.Format("Not all blocks were found in {0}", filename));
            }
        }

        public void CheckBlocklistCorrect(string hash, long length, IEnumerable<string> blocklist, long blocksize, long blockhashlength)
        {
            using(var cmd = m_connection.CreateCommand())
            {
                var query = string.Format(@"
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
    ""A"".""Index"" >= ""B"".""BlocklistHashIndex"" * ({0} / {1})
    AND
    ""A"".""Index"" < (""B"".""BlocklistHashIndex"" + 1) * ({0} / {1})
ORDER BY 
    ""A"".""Index""

"
                ,blocksize, blockhashlength);

                var en = blocklist.GetEnumerator();
                foreach(var r in cmd.ExecuteReaderEnumerable(query, hash, length))
                {
                    if (!en.MoveNext())
                        throw new Exception(string.Format("Too few entries in source blocklist with hash {0}", hash));
                    if (en.Current != r.GetString(0))
                        throw new Exception(string.Format("Mismatch in blocklist with hash {0}", hash));
                }

                if (en.MoveNext())
                    throw new Exception(string.Format("Too many source blocklist entries in {0}", hash));
            }
        }
	}
}

