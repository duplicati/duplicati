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
using System.Linq;
using System.Text;
using System.IO;

namespace Duplicati.Library.Main.ForestHash.Database
{
	public class LocalDeleteDatabase : LocalDatabase
	{
        /// <summary>
        /// An approximate size of a hash-string in memory (44 chars * 2 for unicode + 8 bytes for pointer = 104)
        /// </summary>
        private const uint HASH_GUESS_SIZE = 128;
        
        private System.Data.IDbCommand m_moveBlockToNewVolumeCommand;

		public LocalDeleteDatabase(string path, bool isCompact)
			: base(CreateConnection(path), isCompact ? "Compact" : "Delete")
		{
			InitializeCommands();
		}
		
		public LocalDeleteDatabase(LocalDatabase db)
			: base(db)
		{
			InitializeCommands();
		}
		
		private void InitializeCommands()
		{
			m_moveBlockToNewVolumeCommand = m_connection.CreateCommand();
			
			m_moveBlockToNewVolumeCommand.CommandText = @"UPDATE ""Block"" SET ""VolumeID"" = ? WHERE ""Hash"" = ? AND ""Size"" = ?";
			m_moveBlockToNewVolumeCommand.AddParameters(3);
		}

		/// <summary>
		/// Deletes all but n backups from the database and remote storage.
		/// </summary>
		/// <param name="keep">The number of backups to keep</param>
		/// <param name="allowRemovingLast">If set to <c>true</c> allow removing last backup, otherwise at least one backup is kept</param>
		public IEnumerable<string> DeleteAllButN(int keep, bool allowRemovingLast, CommunicationStatistics stat, Options options, System.Data.IDbTransaction transaction)
		{
			IEnumerable<string> result;

			keep = Math.Max(allowRemovingLast ? 0 : 1, keep);
						
			using(var cmd = m_connection.CreateCommand())
			{
				cmd.Transaction = transaction;
				
				//We create a table with the operationIDs that are about to be deleted
				var tmptablename = "DeletedOperations-" + Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
				cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS SELECT ""ID"" FROM ""Fileset"" WHERE ""ID"" NOT IN (SELECT ""ID"" FROM ""Fileset"" ORDER BY ""Timestamp"" DESC LIMIT {1})", tmptablename, keep));
				
				result = DropFromIDTable(cmd, tmptablename, stat);
				
				cmd.ExecuteNonQuery(string.Format(@"DROP TABLE ""{0}"" ", tmptablename));
			}
			
			return result;
		}
		
		private long GetLastFilesetID(System.Data.IDbCommand cmd)
		{
			long id = -1;
			var r = cmd.ExecuteScalar(@"SELECT ""ID"" FROM ""Fileset"" ORDER BY ""Timestamp"" DESC LIMIT 1");
			if (r != null && r != DBNull.Value)
				id = Convert.ToInt64(r);
				
			return id;
		}

		/// <summary>
		/// Deletes all but n backups from the database and remote storage.
		/// </summary>
		/// <param name="keep">The number of backups to keep</param>
		/// <param name="allowRemovingLast">If set to <c>true</c> allow removing last backup, otherwise at least one backup is kept</param>
		public IEnumerable<string> DeleteOlderThan(DateTime limit, bool allowRemovingLast, CommunicationStatistics stat, Options options, System.Data.IDbTransaction transaction)
		{
			if (limit.Kind == DateTimeKind.Unspecified)
				throw new Exception("Time must be either UTC or Local");
		
			IEnumerable<string> result;
			using(var cmd = m_connection.CreateCommand())
			{
				cmd.Transaction = transaction;
				long keepFilesetId = -1;
				if (!allowRemovingLast)
					keepFilesetId = GetLastFilesetID(cmd);
				
				//We create a table with the operationIDs that are about to be deleted
				var tmptablename = "DeletedOperations-" + Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
				cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" AS SELECT ""ID"" FROM ""Fileset"" WHERE ""ID"" NOT IN (SELECT ""ID"" FROM ""Fileset"" WHERE ""Timestamp"" > ? OR ""ID"" = ? ORDER BY ""Timestamp"")", tmptablename), limit.ToUniversalTime(), keepFilesetId);
				
				result = DropFromIDTable(cmd, tmptablename, stat);
				
				cmd.ExecuteNonQuery(string.Format(@"DROP TABLE ""{0}"" ", tmptablename));
			}
				
			return result;
		}
		
		/// <summary>
		/// Deletes the specified filesets from the database and remote storage.
		/// </summary>
		/// <param name="dates">The filesets to remove</param>
		/// <param name="allowRemovingLast">If set to <c>true</c> allow removing last backup, otherwise at least one backup is kept</param>
		public IEnumerable<string> DeleteFilesets(string filesets, bool allowRemovingLast, CommunicationStatistics stat, Options options, System.Data.IDbTransaction transaction)
		{
			IEnumerable<string> result;
			using(var cmd = m_connection.CreateCommand())
			{
				cmd.Transaction = transaction;
				long keepFilesetId = -1;
				if (!allowRemovingLast)
					keepFilesetId = GetLastFilesetID(cmd);

				var tmptablename = "DeletedOperations-" + Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
				cmd.ExecuteNonQuery(string.Format(@"CREATE TEMPORARY TABLE ""{0}"" (""ID"" INTEGER NOT NULL)", tmptablename));
				
				foreach(var d in filesets.Split(new char[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
				{
					var c = cmd.ExecuteNonQuery(string.Format(@"INSERT INTO ""{0}"" (ID) VALUES (SELECT DISTINCT ""Fileset"".""ID"" FROM ""Fileset"", ""RemoteVolume"" WHERE ""RemoteVolume"".""Name"" = ? AND ""RemoteVolume"".""Type"" = ? AND ""RemoteVolume"".""ID"" = ""Fileset"".""VolumeID"")", tmptablename), d, RemoteVolumeType.Files.ToString());
					if (c != 1)
						throw new Exception(string.Format("Failed to mark fileset {0}, query gave {1} volume(s)", d, c));
				}
								
				if (keepFilesetId >= 0)
				{
					var c0 = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""{0}"" "));
					var c1 = Convert.ToInt64(cmd.ExecuteScalar(@"SELECT COUNT(*) FROM ""Fileset"" "));
					if (c1 - c0 <= 0)
						cmd.ExecuteNonQuery(string.Format(@"DELETE FROM ""{0}"" WHERE ""ID"" = ?", tmptablename), keepFilesetId);
				}
				
				result = DropFromIDTable(cmd, tmptablename, stat);
				
				cmd.ExecuteNonQuery(string.Format(@"DROP TABLE ""{0}"" ", tmptablename));
			}
				
			return result;
		}
		
		/// <summary>
		/// Drops all entries related to operations listed in the table.
		/// </summary>
		/// <param name="cmd">The command used to execute the queries</param>
		/// <param name="tmptablename">The name of a table with the operation IDs</param>
		/// <returns>A list of filesets to delete</returns>
		private static IEnumerable<string> DropFromIDTable(System.Data.IDbCommand cmd, string tmptablename, CommunicationStatistics stat)
		{
			//First we remove unwanted entries
			var deleted = cmd.ExecuteNonQuery(string.Format(@"DELETE FROM ""Fileset"" WHERE ""ID"" IN (SELECT ""ID"" FROM ""{0}"") ", tmptablename));
			cmd.ExecuteNonQuery(string.Format(@"DELETE FROM ""FilesetEntry"" WHERE ""FilesetID"" IN (SELECT ""ID"" FROM ""{0}"") ", tmptablename));
			

			//Then we delete anything that is no longer being referenced
			cmd.ExecuteNonQuery(@"DELETE FROM ""File"" WHERE ""ID"" NOT IN (SELECT DISTINCT ""FileID"" FROM ""FilesetEntry"") ");
			cmd.ExecuteNonQuery(@"DELETE FROM ""Metadataset"" WHERE ""ID"" NOT IN (SELECT DISTINCT ""MetadataID"" FROM ""File"") ");
			cmd.ExecuteNonQuery(@"DELETE FROM ""Blockset"" WHERE ""ID"" NOT IN (SELECT DISTINCT ""BlocksetID"" FROM ""File"" UNION SELECT DISTINCT ""BlocksetID"" FROM ""Metadataset"") ");
			cmd.ExecuteNonQuery(@"DELETE FROM ""BlocksetEntry"" WHERE ""BlocksetID"" NOT IN (SELECT DISTINCT ""ID"" FROM ""Blockset"") ");
			cmd.ExecuteNonQuery(@"DELETE FROM ""BlocklistHash"" WHERE ""BlocksetID"" NOT IN (SELECT DISTINCT ""ID"" FROM ""Blockset"") ");
			
			//We save the block info for the remote files, before we delete it
			cmd.ExecuteNonQuery(@"INSERT INTO ""DeletedBlock"" (""Hash"", ""Size"", ""VolumeID"") SELECT ""Hash"", ""Size"", ""VolumeID"" FROM ""Block"" WHERE ""ID"" NOT IN (SELECT DISTINCT ""BlockID"" AS ""BlockID"" FROM ""BlocksetEntry"" UNION SELECT DISTINCT ""ID"" FROM ""Block"", ""BlocklistHash"" WHERE ""Block"".""Hash"" = ""BlocklistHash"".""Hash"") ");
			cmd.ExecuteNonQuery(@"DELETE FROM ""Block"" WHERE ""ID"" NOT IN (SELECT DISTINCT ""BlockID"" FROM ""BlocksetEntry"" UNION SELECT DISTINCT ""ID"" FROM ""Block"", ""BlocklistHash"" WHERE ""Block"".""Hash"" = ""BlocklistHash"".""Hash"") ");		

			//Find all remote filesets that are no longer required, and mark them as delete
			var updated = cmd.ExecuteNonQuery(@"UPDATE ""RemoteVolume"" SET ""State"" = ? WHERE ""Type"" = ? AND ""State"" IN (?, ?) AND ""ID"" NOT IN (SELECT ""VolumeID"" FROM ""Fileset"") ", RemoteVolumeState.Deleting.ToString(), RemoteVolumeType.Files.ToString(), RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString());

			if (deleted != updated)
				throw new Exception(string.Format("Unexpected number of remote volumes marked as deleted. Found {0} filesets, but {1} volumes", deleted, updated));

			var res = new List<string>();
			using (var rd = cmd.ExecuteReader(@"SELECT ""Name"" FROM ""RemoteVolume"" WHERE ""Type"" = ? AND ""State"" = ? ", RemoteVolumeType.Files.ToString(), RemoteVolumeState.Deleting.ToString()))
			while (rd.Read())
				res.Add(rd.GetValue(0).ToString());
			
			return res;
		}

		private struct VolumeUsage
		{
			public string Name;
			public long VolumeSize;
			public long WastedSize;
			
			public VolumeUsage(string name, long volumesize, long wastedsize)
			{
				this.Name = name;
				this.VolumeSize = volumesize;
				this.WastedSize = wastedsize;
			}
		}

		/// <summary>
		/// Returns the number of bytes stored in each volume,
		/// and the number of bytes no longer needed in each volume.
		/// The sizes are the uncompressed values.
		/// </summary>
		/// <returns>A list of tuples with name, datasize, wastedbytes.</returns>
		private IEnumerable<VolumeUsage> GetWastedSpaceReport(System.Data.IDbTransaction transaction)
		{
			var tmptablename = "UsageReport-" + Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
			var active = @"SELECT SUM(""Block"".""Size"") AS ""ActiveSize"", 0 AS ""InactiveSize"", ""Block"".""VolumeID"" AS ""VolumeID"", MIN(""FilesetEntry"".""Scantime"") AS ""SortScantime"" FROM ""FilesetEntry"", ""File"", ""BlocksetEntry"", ""Block"" WHERE ""FilesetEntry"".""FileID"" = ""File"".""ID"" AND ""File"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" GROUP BY ""Block"".""VolumeID"" ";
			var inactive = @"SELECT 0 AS ""ActiveSize"", SUM(""Size"") AS ""InactiveSize"", ""VolumeID"" AS ""VolumeID"", 0 AS ""SortScantime"" FROM ""DeletedBlock"" GROUP BY ""VolumeID"" ";
			
			var combined = active + " UNION " + inactive;
			var collected = @"SELECT ""VolumeID"" AS ""VolumeID"", SUM(""ActiveSize"") AS ""ActiveSize"", SUM(""InactiveSize"") AS ""InactiveSize"", MAX(""SortScantime"") AS ""SortScantime"" FROM (" + combined + @") GROUP BY ""VolumeID"" ";
			var createtable = @"CREATE TEMPORARY TABLE """ + tmptablename + @""" AS " + collected;
						
			using (var cmd = m_connection.CreateCommand())
			{
				cmd.Transaction = transaction;
				try
				{
					cmd.ExecuteNonQuery(createtable);
					var res = new List<VolumeUsage>();
					using (var rd = cmd.ExecuteReader(string.Format(@"SELECT ""A"".""Name"", ""B"".""ActiveSize"", ""B"".""InactiveSize"" FROM ""Remotevolume"" A, ""{0}"" B WHERE ""A"".""ID"" = ""B"".""VolumeID"" ORDER BY ""B"".""SortScantime"" ASC ", tmptablename)))
						while (rd.Read())
							res.Add(new VolumeUsage(rd.GetValue(0).ToString(), Convert.ToInt64(rd.GetValue(1)) + Convert.ToInt64(rd.GetValue(2)), Convert.ToInt64(rd.GetValue(2))));
							
					return res;
				}
				finally 
				{
					try { cmd.ExecuteNonQuery(string.Format(@"DROP TABLE ""{0}"" ", tmptablename)); }
					catch { }
				}
			}
		}
		
		public interface ICompactReport
		{
			IEnumerable<string> DeleteableVolumes { get; }
			IEnumerable<string> CompactableVolumes { get; }
			bool ShouldReclaim { get; }
			bool ShouldCompact { get; }
			void ReportCompactData(CommunicationStatistics stat); 
		}
		
		private class CompactReport : ICompactReport
		{
			private IEnumerable<VolumeUsage> m_report;
			private IEnumerable<VolumeUsage> m_cleandelete;
			private IEnumerable<VolumeUsage> m_wastevolumes;
			private IEnumerable<VolumeUsage> m_smallvolumes;
			
			private long m_deletablevolumes;
			private long m_wastedspace;
			private long m_smallspace;
			
			private long m_maxwastesize;
			private long m_volsize;
			
			public CompactReport(long volsize, long maxwastesize, long volsizetolerance, IEnumerable<VolumeUsage> report)
			{
				m_report = report;
				
				m_cleandelete = from n in m_report where n.VolumeSize <= n.WastedSize select n;
				m_wastevolumes = from n in m_report where n.WastedSize > volsizetolerance && !m_cleandelete.Contains(n) select n;
				m_smallvolumes = from n in m_report where n.VolumeSize - volsizetolerance < volsize && !m_cleandelete.Contains(n) select n;

				m_maxwastesize = maxwastesize;
				m_volsize = volsize;

				m_deletablevolumes = m_cleandelete.Count();
				m_wastedspace = m_wastevolumes.Aggregate(0L, (a,x) => a + x.WastedSize);
				m_smallspace = m_smallvolumes.Aggregate(0L, (a,x) => a + x.VolumeSize);
			}
			
			public void ReportCompactData(CommunicationStatistics stat)
			{
				stat.LogMessage("Found {0} fully deletable volume(s)", m_deletablevolumes);
				stat.LogMessage("Found {0} small volumes(s) with a total size of {1}", m_smallvolumes.Count(), Utility.Utility.FormatSizeString(m_smallspace));
				stat.LogMessage("Found {0} volume(s) with a total of {1} wasted space", m_wastevolumes.Count(), Utility.Utility.FormatSizeString(m_wastedspace));
				
				if (m_deletablevolumes > 0)
					stat.LogMessage("Compacting because there are {0} fully deletable volume(s)", m_deletablevolumes);
				else if (m_wastedspace > m_maxwastesize && m_wastevolumes.Count() >= 2)
					stat.LogMessage("Compacting because there are {0} wasted space and the limit is {1}", Utility.Utility.FormatSizeString(m_wastedspace), Utility.Utility.FormatSizeString(m_maxwastesize));
				else if (m_smallspace > m_volsize)
					stat.LogMessage("Compacting because there are {0} in small volumes and the volume size is {1}", Utility.Utility.FormatSizeString(m_smallspace), Utility.Utility.FormatSizeString(m_volsize));
				else
					stat.LogMessage("Not compacting");
			}
			
			public bool ShouldReclaim
			{
				get 
				{
					return m_deletablevolumes > 0;
				}
			}
			
			public bool ShouldCompact
			{
				get 
				{
					return m_wastedspace > m_maxwastesize || m_smallspace > m_volsize;
				}
			}

			public IEnumerable<string> DeleteableVolumes 
			{ 
				get { return from n in m_cleandelete select n.Name; } 
			}
			
			public IEnumerable<string> CompactableVolumes 
			{ 
				get 
				{ 
					//The order matters, we compact old volumes together first,
					// as we anticipate old data will stay around, where never data
					// is more likely to be discarded again
					return m_wastevolumes.Union(m_smallvolumes).Select(x => x.Name).Distinct();
				} 
			}
		}
		
		public ICompactReport GetCompactReport(long volsize, long maxwastesize, long volsizetolerance, System.Data.IDbTransaction transaction)
		{
			return new CompactReport(volsize, maxwastesize, volsizetolerance, GetWastedSpaceReport(transaction).ToList());
		}
		
				
		public interface IBlockQuery : IDisposable
		{
			bool UseBlock(string hash, long size);
		}
		
		private class BlockQuery : IBlockQuery
		{
			private System.Data.IDbCommand m_command;
			private HashDatabaseProtector<string, long> m_lookup;
			
			public BlockQuery(System.Data.IDbConnection con, Options options, System.Data.IDbTransaction transaction)
			{
				m_command = con.CreateCommand();
				m_command.Transaction = transaction;
				
				if (options.BlockHashLookupMemory > 0)
				{
					m_lookup = new HashDatabaseProtector<string, long>(HASH_GUESS_SIZE, (ulong)options.BlockHashLookupMemory);
					using(var reader = m_command.ExecuteReader(@"SELECT ""Hash"", ""Size"" FROM ""Block"" "))
					while (reader.Read())
					{
						var hash = reader.GetValue(0).ToString();
						var size = Convert.ToInt64(reader.GetValue(1));
						m_lookup.Add(HashPrefixLookup.DecodeBase64Hash(hash), hash, size);
					}
				}
				
				m_command.Parameters.Clear();
				m_command.CommandText = @"SELECT ""VolumeID"" FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ? ";
				m_command.AddParameters(2);
			}
			
			public bool UseBlock(string hash, long size)
			{
				if (m_lookup != null)
				{
					long nsize;
					switch(m_lookup.HasValue(HashPrefixLookup.DecodeBase64Hash(hash), hash, out nsize))
					{
						case HashLookupResult.Found:
							if (nsize == size)
								return true;
							break;
						case HashLookupResult.NotFound:
							return false;
					}
				}
				
				m_command.SetParameterValue(0, hash);	
				m_command.SetParameterValue(1, size);
				var r = m_command.ExecuteScalar();
				return r != null && r != DBNull.Value;
			}
			
			public void Dispose()
			{
				if (m_lookup != null)
					try { m_lookup.Dispose(); } 
					finally { m_lookup = null; }
					
				if (m_command != null)
					try { m_command.Dispose(); }
					finally { m_command = null; }
			}
		}
		
		/// <summary>
		/// Builds a lookup table to enable faster response to block queries
		/// </summary>
		/// <param name="volumename">The name of the volume to prepare for</param>
		public IBlockQuery CreateBlockQueryHelper(Options options, System.Data.IDbTransaction transaction)
		{
			return new BlockQuery(m_connection, options, transaction);
		}

		public void MoveBlockToNewVolume(string hash, long size, long volumeID, System.Data.IDbTransaction tr)
		{
			m_moveBlockToNewVolumeCommand.SetParameterValue(0, volumeID);
			m_moveBlockToNewVolumeCommand.SetParameterValue(1, hash);
			m_moveBlockToNewVolumeCommand.SetParameterValue(2, size);
			m_moveBlockToNewVolumeCommand.Transaction = tr;
			var c = m_moveBlockToNewVolumeCommand.ExecuteNonQuery();
			if (c != 1)
				throw new Exception("Unexpected update result");
		}
		
		/// <summary>
		/// Calculates the sequence in which files should be deleted based on their releations.
		/// </summary>
		/// <returns>The deletable volumes.</returns>
		/// <param name="deleteableVolumes">Block volumes slated for deletion.</param>
		public IEnumerable<IRemoteVolume> GetDeletableVolumes(IEnumerable<IRemoteVolume> deleteableVolumes, System.Data.IDbTransaction transaction)
		{
			using(var cmd = m_connection.CreateCommand())
			{
				// Although the generated index volumes are always in pairs,
				// this code handles many-to-many relations between
				// index files and block volumes, should this be added later
				var lookupBlock = new Dictionary<string, List<IRemoteVolume>>();
				var lookupIndexfiles = new Dictionary<string, List<string>>();
				
				cmd.Transaction = transaction;
					using(var rd = cmd.ExecuteReader(@"SELECT ""C"".""Name"", ""B"".""Name"", ""B"".""Hash"", ""B"".""Size"" FROM ""IndexBlockLink"" A, ""RemoteVolume"" B, ""RemoteVolume"" C WHERE ""A"".""IndexVolumeID"" = ""B"".""ID"" AND ""A"".""BlockVolumeID"" = ""C"".""ID"" "))
						while(rd.Read())
						{
							var name = rd.GetValue(0).ToString();
							List<IRemoteVolume> indexfileList;
							if (!lookupBlock.TryGetValue(name, out indexfileList))
							{	
								indexfileList = new List<IRemoteVolume>();
								lookupBlock.Add(name, indexfileList);
							}
							
							var v = new RemoteVolume(rd.GetValue(1).ToString(), rd.GetValue(2).ToString(), Convert.ToInt64(rd.GetValue(3)));
							indexfileList.Add(v);

							List<string> blockList;
							if (!lookupIndexfiles.TryGetValue(v.Name, out blockList))
							{	
								blockList = new List<string>();
								lookupIndexfiles.Add(v.Name, blockList);
							}
							blockList.Add(name);
						}

				foreach(var r in deleteableVolumes.Distinct())
				{
					// Return the input
					yield return r;
					List<IRemoteVolume> indexfileList;
					if (lookupBlock.TryGetValue(r.Name, out indexfileList))
						foreach(var sh in indexfileList)
						{
							List<string> backref;
							if (lookupIndexfiles.TryGetValue(sh.Name, out backref))
							{
								//If this is the last reference, 
								// remove the index file as well
								if (backref.Remove(r.Name) && backref.Count == 0)
									yield return sh;
							}
						}
				}
			}
		}

	}
}

