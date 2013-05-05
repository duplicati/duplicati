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

namespace Duplicati.Library.Main.ForestHash.Database
{
	public class LocalRepairDatabase : LocalDatabase
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
		
		public interface IRemoteVolume
		{	
			string Name { get; }
			string Hash { get; }
			long Size { get; }
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
		
		public IEnumerable<IBlockWithSources> GetSourceFilesWithBlocks(long volumeid, long blocksize)
		{
			using(var cmd = m_connection.CreateCommand())
			using(var rd = cmd.ExecuteReader(string.Format(@"SELECT DISTINCT ""Block"".""Hash"", ""Block"".""Size"", ""File"".""Path"", ""BlocksetEntry"".""Index"" * {0} FROM  ""Block"", ""BlocksetEntry"", ""File"" WHERE ""File"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID"" AND ""Block"".""ID"" = ""BlocksetEntry"".""BlockID"" AND ""Block"".""VolumeID"" = ? ", blocksize), volumeid))
				if (rd.Read())
				{
					var bs = new BlockWithSources(rd);
					while (!bs.Done)
						yield return (IBlockWithSources)bs;
				}
		}

		public IEnumerable<IRemoteVolume> GetBlockFromRemote(string hash, long size)
		{
			using(var cmd = m_connection.CreateCommand())
			using(var rd = cmd.ExecuteReader(@"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"" FROM ""RemoteVolume"", ""Block"" WHERE ""Block"".""Hash"" = ? AND ""Block"".""Size"" = ? AND ""Block"".""VolumeID"" = ""RemoteVolume"".""ID"" ", hash, size))
				while (rd.Read())
					yield return new RemoteVolume(rd.GetValue(0).ToString(), rd.GetValue(1).ToString(), Convert.ToInt64(rd.GetValue(2)));
		}

		public IEnumerable<IRemoteVolume> GetFilesetsUsingBlock(string hash, int size)
		{
			var blocks = @"SELECT DISTINCT ""File"".""ID"" AS ID FROM ""Block"", ""Blockset"", ""BlocksetEntry"", ""File"" WHERE ""Block"".""Hash"" = ? AND ""Block"".""Size"" = ? AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" AND ""BlocksetEntry"".""BlocksetID"" = ""Blockset"".""ID"" AND ""File"".""BlocksetID"" = ""Blockset"".""ID"" ";
			var blocklists = @"SELECT DISTINCT ""File"".""ID"" AS ID FROM ""Block"", ""Blockset"", ""BlocklistHash"", ""File"" WHERE ""Block"".""Hash"" = ? AND ""Block"".""Size"" = ? AND ""BlocklistHash"".""Hash"" = ""Block"".""Hash"" AND ""BlocklistHash"".""BlocksetID"" = ""Blockset"".""ID"" AND ""File"".""BlocksetID"" = ""Blockset"".""ID"" ";
		
			var cmdtxt = @"SELECT DISTINCT ""RemoteVolume"".""Name"", ""RemoteVolume"".""Hash"", ""RemoteVolume"".""Size"" FROM ""RemoteVolume"", ""FilesetEntry"", ""Fileset"" WHERE ""RemoteVolume"".""ID"" = ""Fileset"".""VolumeID"" AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID"" AND ""RemoteVolume"".""Type"" = ? AND ""FilesetEntry"".""FileID"" IN  (SELECT DISTINCT ""ID"" FROM ( " + blocks + " UNION " + blocklists + " ))";
		
			using(var cmd = m_connection.CreateCommand())
			using(var rd = cmd.ExecuteReader(cmdtxt, RemoteVolumeType.Files.ToString(),  hash, size, hash, size))
				while (rd.Read())
					yield return new RemoteVolume(rd.GetValue(0).ToString(), rd.GetValue(1).ToString(), Convert.ToInt64(rd.GetValue(2)));
		}

		public IEnumerable<IRemoteVolume> GetBlockVolumesFromIndexName(string name)
		{
			using(var cmd = m_connection.CreateCommand())
			using(var rd = cmd.ExecuteReader(@"SELECT ""Name"", ""Hash"", ""Size"" FROM ""RemoteVolume"" WHERE ""ID"" IN (SELECT ""BlockVolumeID"" FROM ""IndexBlockLink"" WHERE ""IndexVolumeID"" IN (SELECT ""ID"" FROM ""RemoteVolume"" WHERE ""Name"" = ?))", name))
				while (rd.Read())
					yield return new RemoteVolume(rd.GetValue(0).ToString(), rd.GetValue(1).ToString(), Convert.ToInt64(rd.GetValue(2)));
		}
	}
}

