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
using System.Collections.Generic;

namespace Duplicati.Library.Main.Database
{
	internal class LocalRepairDatabase : LocalDatabase
	{
		public LocalRepairDatabase(string path)
			: base(LocalDatabase.CreateConnection(path), "Repair")
		{
		
		}
		
		public long GetFilesetIdFromRemotename(string filelist)
		{
			using(var cmd = m_connection.CreateCommand())
			{
				var r = cmd.ExecuteScalar(@"SELECT ""Fileset"".""ID"" FROM ""Fileset"",""RemoteVolume"" WHERE ""Fileset"".""VolumeID"" = ""RemoteVolume"".""ID"" AND ""RemoteVolume"".""Name"" = ?", filelist);
				if (r == null || r == DBNull.Value)
					throw new Exception(string.Format("No such remote file: {0}", filelist));
					
				return Convert.ToInt64(r);
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
				: base(rd.GetValue(0).ToString(), Convert.ToInt64(rd.GetValue(1)))
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
					
					var cur = new BlockSource(m_rd.GetValue(2).ToString(), Convert.ToInt64(m_rd.GetValue(3)));
					var file = cur.File;
					
					while(!Done && cur.File == file)
					{
						yield return cur;
						Done = m_rd.Read();
						if (!Done)
							cur = new BlockSource(m_rd.GetValue(2).ToString(), Convert.ToInt64(m_rd.GetValue(3)));
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
			using(var rd = cmd.ExecuteReader(@"SELECT ""Name"", ""Hash"", ""Size"" FROM ""RemoteVolume"" WHERE ""ID"" IN (SELECT ""BlockVolumeID"" FROM ""IndexBlockLink"" WHERE ""IndexVolumeID"" IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" = ?))", name))
				while (rd.Read())
					yield return new RemoteVolume(rd.GetValue(0).ToString(), rd.GetValue(1).ToString(), Convert.ToInt64(rd.GetValue(2)));
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
                }
                
                m_insertCommand = m_connection.CreateCommand();
                m_insertCommand.Transaction = m_transaction.Parent;
                m_insertCommand.AddParameters(3);
            }
            
            public bool SetBlockRestored(string hash, long size)
            {
                m_insertCommand.SetParameterValue(0, hash);
                m_insertCommand.SetParameterValue(1, size);
                m_insertCommand.SetParameterValue(2, 1);
                return m_insertCommand.ExecuteNonQuery() == 1;
            }
            
            public IEnumerable<IBlockWithSources> GetSourceFilesWithBlocks(long blocksize)
            {
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction.Parent;
                    using(var rd = cmd.ExecuteReader(string.Format(@"SELECT DISTINCT ""{0}"".""Hash"", ""{0}"".""Size"", ""File"".""Path"", ""BlocksetEntry"".""Index"" * {1} FROM  ""{0}"", ""Block"", ""BlocksetEntry"", ""File"" WHERE ""File"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID"" AND ""Block"".""ID"" = ""BlocksetEntry"".""BlockID"" AND ""{0}"".""Hash"" = ""Block"".""Hash"" AND ""{0}"".""Size"" = ""Block"".""Size"" AND ""{0}"".""Restored"" = ? ", m_tablename, blocksize), 0))
                        if (rd.Read())
                        {
                            var bs = new BlockWithSources(rd);
                            while (!bs.Done)
                                yield return (IBlockWithSources)bs;
                        }
                }
            }
                    
            public IEnumerable<KeyValuePair<string, long>> GetMissingBlocks()
            {
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction.Parent;
                    using(var rd = cmd.ExecuteReader(string.Format(@"SELECT ""{0}"".""Hash"", ""{0}"".""Size"" FROM ""{0}"" WHERE ""{0}"".""Restored"" = ? ", m_tablename), 0))
                        while (rd.Read())
                            yield return new KeyValuePair<string, long>(rd.GetValue(0).ToString(), Convert.ToInt64(rd.GetValue(1)));
                }
            }
            
            public IEnumerable<IRemoteVolume> GetFilesetsUsingMissingBlocks()
            {
                var blocks = @"SELECT DISTINCT ""File"".""ID"" AS ID FROM ""{0}"", ""Block"", ""Blockset"", ""BlocksetEntry"", ""File"" WHERE ""Block"".""Hash"" = ""{0}"".""Hash"" AND ""Block"".""Size"" = ""{0}"".""Size"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" AND ""BlocksetEntry"".""BlocksetID"" = ""Blockset"".""ID"" AND ""File"".""BlocksetID"" = ""Blockset"".""ID"" ";
                var blocklists = @"SELECT DISTINCT ""File"".""ID"" AS ID FROM ""{0}"", ""Block"", ""Blockset"", ""BlocklistHash"", ""File"" WHERE ""Block"".""Hash"" = ""{0}"".""Hash"" AND ""Block"".""Size"" = ""{0}"".""Size"" AND ""BlocklistHash"".""Hash"" = ""Block"".""Hash"" AND ""BlocklistHash"".""BlocksetID"" = ""Blockset"".""ID"" AND ""File"".""BlocksetID"" = ""Blockset"".""ID"" ";
            
                var cmdtxt = @"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"" FROM ""RemoteVolume"", ""FilesetEntry"", ""Fileset"" WHERE ""RemoteVolume"".""ID"" = ""Fileset"".""VolumeID"" AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID"" AND ""RemoteVolume"".""Type"" = ? AND ""FilesetEntry"".""FileID"" IN  (SELECT DISTINCT ""ID"" FROM ( " + blocks + " UNION " + blocklists + " ))";
            
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction.Parent;
                    using(var rd = cmd.ExecuteReader(string.Format(cmdtxt, m_tablename), RemoteVolumeType.Files.ToString()))
                        while (rd.Read())
                            yield return new RemoteVolume(rd.GetValue(0).ToString(), rd.GetValue(1).ToString(), Convert.ToInt64(rd.GetValue(2)));
                }
            }
            
            public IEnumerable<IRemoteVolume> GetMissingBlockSources()
            {
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = m_transaction.Parent;
                    using(var rd = cmd.ExecuteReader(string.Format(@"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"" FROM ""RemoteVolume"", ""Block"", ""{0}"" WHERE ""Block"".""Hash"" = ""{0}"".""Hash"" AND ""Block"".""Size"" = ""{0}"".""Size"" AND ""Block"".""VolumeID"" = ""RemoteVolume"".""ID"" AND ""Remotevolume"".""Name"" != ? ", m_tablename), m_volumename))
                        while (rd.Read())
                            yield return new RemoteVolume(rd.GetValue(0).ToString(), rd.GetValue(1).ToString(), Convert.ToInt64(rd.GetValue(2)));
                }
            }
            
            public void Dispose()
            {
                if (m_tablename != null)
                {
                    try
                    {
                        using(var cmd = m_connection.CreateCommand())
                        {
                            cmd.Transaction = m_transaction.Parent;
                            cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", m_transaction));
                        }
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

        public IEnumerable<Tuple<string, byte[], int>> GetBlocklists(long volumeid, long blocksize, int hashsize)
        {
            using(var cmd = m_connection.CreateCommand())
            {
                var sql = string.Format(@"SELECT ""A"".""Hash"", ""C"".""Hash"" FROM " + 
                    @"(SELECT ""BlocklistHash"".""BlocksetID"", ""Block"".""Hash"", * FROM  ""BlocklistHash"",""Block"" WHERE  ""BlocklistHash"".""Hash"" = ""Block"".""Hash"" AND ""Block"".""VolumeID"" = 5) A, " + 
                    @" ""BlocksetEntry"" B, ""Block"" C WHERE ""B"".""BlocksetID"" = ""A"".""BlocksetID"" AND " + 
                    @" ""B"".""Index"" >= (""A"".""Index"" * ({0}/{1})) AND ""B"".""Index"" < ((""A"".""Index"" + 1) * ({0}/{1})) AND ""C"".""ID"" = ""B"".""BlockID"" " + 
                    @" ORDER BY ""A"".""BlocksetID"", ""B"".""Index""",
                    blocksize,
                    hashsize
                );
                    
                string curHash = null;
                int index = 0;
                byte[] buffer = new byte[blocksize];
                
                using(var rd = cmd.ExecuteReader(sql))
                    while (rd.Read())
                    {
                        var blockhash = rd.GetValue(0).ToString();
                        if (blockhash != curHash && curHash != null)
                        {
                            yield return new Tuple<string, byte[], int>(curHash, buffer, index);
                            curHash = null;
                            index = 0;
                        }
                        
                        var hash = Convert.FromBase64String(rd.GetValue(1).ToString());
                        Array.Copy(hash, 0, buffer, index, hashsize);
                        curHash = blockhash;
                        index += hashsize;
                    }
                    
                if (curHash != null)                    
                    yield return new Tuple<string, byte[], int>(curHash, buffer, index);
                    
                
            }
        }

	}
}

