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

namespace Duplicati.Library.Main.Database
{
	internal class LocalDeleteDatabase : LocalDatabase
	{
        private System.Data.IDbCommand m_moveBlockToNewVolumeCommand;

		public LocalDeleteDatabase(string path, bool isCompact)
			: base(path, isCompact ? "Compact" : "Delete", true)
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
		
		private long GetLastFilesetID(System.Data.IDbCommand cmd)
		{
			return cmd.ExecuteScalarInt64(@"SELECT ""ID"" FROM ""Fileset"" ORDER BY ""Timestamp"" DESC LIMIT 1", -1);
		}
		
		/// <summary>
		/// Drops all entries related to operations listed in the table.
		/// </summary>
		/// <param name="toDelete">The fileset entries to delete</param>
		/// <param name="transaction">The transaction to execute the commands in</param>
		/// <returns>A list of filesets to delete</returns>
		public IEnumerable<KeyValuePair<string, long>> DropFilesetsFromTable(DateTime[] toDelete, System.Data.IDbTransaction transaction)
		{
			using(var cmd = m_connection.CreateCommand())
			{
				cmd.Transaction = transaction;

				var q = "";
				foreach(var n in toDelete)
					if (q.Length == 0)
						q = "?";
					else
						q += ",?";
						
				//First we remove unwanted entries
                var deleted = cmd.ExecuteNonQuery(@"DELETE FROM ""Fileset"" WHERE ""Timestamp"" IN (" + q + @") ", toDelete.Select(x => NormalizeDateTimeToEpochSeconds(x)).Cast<object>().ToArray());
	
				if (deleted != toDelete.Length)
					throw new Exception(string.Format("Unexpected number of deleted filesets {0} vs {1}", deleted, toDelete.Length));
	
				//Then we delete anything that is no longer being referenced
                cmd.ExecuteNonQuery(@"DELETE FROM ""FilesetEntry"" WHERE ""FilesetID"" NOT IN (SELECT DISTINCT ""ID"" FROM ""Fileset"")");
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
	
				using (var rd = cmd.ExecuteReader(@"SELECT ""Name"", ""Size"" FROM ""RemoteVolume"" WHERE ""Type"" = ? AND ""State"" = ? ", RemoteVolumeType.Files.ToString(), RemoteVolumeState.Deleting.ToString()))
				while (rd.Read())
                    yield return new KeyValuePair<string, long>(rd.GetString(0), rd.ConvertValueToInt64(1));
			}
		}

		private struct VolumeUsage
		{
			public string Name;
			public long DataSize;
			public long WastedSize;
			public long CompressedSize;
			
			public VolumeUsage(string name, long datasize, long wastedsize, long compressedsize)
			{
				this.Name = name;
				this.DataSize = datasize;
				this.WastedSize = wastedsize;
				this.CompressedSize = compressedsize;
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
			var tmptablename = "UsageReport-" + Library.Utility.Utility.ByteArrayAsHexString(Guid.NewGuid().ToByteArray());
            
            var usedBlocks = @"SELECT SUM(""Block"".""Size"") AS ""ActiveSize"", ""Block"".""VolumeID"" AS ""VolumeID"" FROM ""Block"", ""Remotevolume"" WHERE ""Block"".""VolumeID"" = ""Remotevolume"".""ID"" AND ""Block"".""ID"" NOT IN (SELECT ""Block"".""ID"" FROM ""Block"",""DeletedBlock"" WHERE ""Block"".""Hash"" = ""DeletedBlock"".""Hash"" AND ""Block"".""Size"" = ""DeletedBlock"".""Size"") GROUP BY ""Block"".""VolumeID"" ";
            var lastmodifiedFile = @"SELECT ""Block"".""VolumeID"" AS ""VolumeID"", ""Fileset"".""Timestamp"" AS ""Sorttime"" FROM ""Fileset"", ""FilesetEntry"", ""File"", ""BlocksetEntry"", ""Block"" WHERE ""FilesetEntry"".""FileID"" = ""File"".""ID"" AND ""File"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID"" ";
            var lastmodifiedMetadata = @"SELECT ""Block"".""VolumeID"" AS ""VolumeID"", ""Fileset"".""Timestamp"" AS ""Sorttime"" FROM ""Fileset"", ""FilesetEntry"", ""File"", ""BlocksetEntry"", ""Block"", ""Metadataset"" WHERE ""FilesetEntry"".""FileID"" = ""File"".""ID"" AND ""File"".""MetadataID"" = ""Metadataset"".""ID"" AND ""Metadataset"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID"" AND ""BlocksetEntry"".""BlockID"" = ""Block"".""ID"" AND ""Fileset"".""ID"" = ""FilesetEntry"".""FilesetID"" ";
            var scantime = @"SELECT ""VolumeID"" AS ""VolumeID"", MIN(""Sorttime"") AS ""Sorttime"" FROM (" + lastmodifiedFile + @" UNION " + lastmodifiedMetadata + @") GROUP BY ""VolumeID"" ";
            var active = @"SELECT ""A"".""ActiveSize"" AS ""ActiveSize"",  0 AS ""InactiveSize"", ""A"".""VolumeID"" AS ""VolumeID"", CASE WHEN ""B"".""Sorttime"" IS NULL THEN 0 ELSE ""B"".""Sorttime"" END AS ""Sorttime"" FROM (" + usedBlocks + @") A LEFT OUTER JOIN (" + scantime + @") B ON ""B"".""VolumeID"" = ""A"".""VolumeID"" ";
            
			var inactive = @"SELECT 0 AS ""ActiveSize"", SUM(""Size"") AS ""InactiveSize"", ""VolumeID"" AS ""VolumeID"", 0 AS ""SortScantime"" FROM ""DeletedBlock"" GROUP BY ""VolumeID"" ";
            var empty = @"SELECT 0 AS ""ActiveSize"", 0 AS ""InactiveSize"", ""Remotevolume"".""ID"" AS ""VolumeID"", 0 AS ""SortScantime"" FROM ""Remotevolume"" WHERE ""Remotevolume"".""Type"" = ? AND ""Remotevolume"".""State"" IN (?, ?) AND ""Remotevolume"".""ID"" NOT IN (SELECT ""VolumeID"" FROM ""Block"") ";
			
			var combined = active + " UNION " + inactive + " UNION " + empty;
			var collected = @"SELECT ""VolumeID"" AS ""VolumeID"", SUM(""ActiveSize"") AS ""ActiveSize"", SUM(""InactiveSize"") AS ""InactiveSize"", MAX(""Sortime"") AS ""Sorttime"" FROM (" + combined + @") GROUP BY ""VolumeID"" ";
			var createtable = @"CREATE TEMPORARY TABLE """ + tmptablename + @""" AS " + collected;
						
			using (var cmd = m_connection.CreateCommand())
			{
				cmd.Transaction = transaction;
				try
				{
                    cmd.ExecuteNonQuery(createtable, RemoteVolumeType.Blocks.ToString(), RemoteVolumeState.Uploaded.ToString(), RemoteVolumeState.Verified.ToString());
					using (var rd = cmd.ExecuteReader(string.Format(@"SELECT ""A"".""Name"", ""B"".""ActiveSize"", ""B"".""InactiveSize"", ""A"".""Size"" FROM ""Remotevolume"" A, ""{0}"" B WHERE ""A"".""ID"" = ""B"".""VolumeID"" ORDER BY ""B"".""Sorttime"" ASC ", tmptablename)))
						while (rd.Read())
                            yield return new VolumeUsage(rd.GetValue(0).ToString(), rd.ConvertValueToInt64(1, 0) + rd.ConvertValueToInt64(2, 0), rd.ConvertValueToInt64(2, 0), rd.ConvertValueToInt64(3, 0));
				}
				finally 
				{
					try { cmd.ExecuteNonQuery(string.Format(@"DROP TABLE IF EXISTS ""{0}"" ", tmptablename)); }
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
			void ReportCompactData(ILogWriter log); 
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
			private long m_fullsize;
            private long m_smallvolumecount;
			
			private long m_wastethreshold;
			private long m_volsize;
            private long m_maxsmallfilecount;
			
			public CompactReport(long volsize, long wastethreshold, long smallfilesize, long maxsmallfilecount, IEnumerable<VolumeUsage> report)
			{
				m_report = report;
				
                m_cleandelete = (from n in m_report where n.DataSize <= n.WastedSize select n).ToArray();
				m_wastevolumes = from n in m_report where ((((n.WastedSize / (float)n.DataSize) * 100) >= wastethreshold) || (((n.WastedSize / (float)volsize) * 100) >= wastethreshold)) && !m_cleandelete.Contains(n) select n;
				m_smallvolumes = from n in m_report where n.CompressedSize <= smallfilesize && !m_cleandelete.Contains(n) select n;

				m_wastethreshold = wastethreshold;
				m_volsize = volsize;
                m_maxsmallfilecount = maxsmallfilecount;

				m_deletablevolumes = m_cleandelete.Count();
				m_fullsize = report.Select(x => x.DataSize).Sum();
				
				m_wastedspace = m_wastevolumes.Select(x => x.WastedSize).Sum();
				m_smallspace = m_smallvolumes.Select(x => x.CompressedSize).Sum();
                m_smallvolumecount = m_smallvolumes.Count();
			}
			
			public void ReportCompactData(ILogWriter log)
            {
                var wastepercentage = ((m_wastedspace / (float)m_fullsize) * 100);
                if (log.VerboseOutput)
                {
                    log.AddVerboseMessage(string.Format("Found {0} fully deletable volume(s)", m_deletablevolumes));
                    log.AddVerboseMessage(string.Format("Found {0} small volumes(s) with a total size of {1}", m_smallvolumes.Count(), Library.Utility.Utility.FormatSizeString(m_smallspace)));
                    log.AddVerboseMessage(string.Format("Found {0} volume(s) with a total of {1:F2}% wasted space ({2} of {3})", m_wastevolumes.Count(), wastepercentage, Library.Utility.Utility.FormatSizeString(m_wastedspace), Library.Utility.Utility.FormatSizeString(m_fullsize)));
                }
				
				if (m_deletablevolumes > 0)
					log.AddMessage(string.Format("Compacting because there are {0} fully deletable volume(s)", m_deletablevolumes));
				else if (wastepercentage >= m_wastethreshold && m_wastevolumes.Count() >= 2)
					log.AddMessage(string.Format("Compacting because there is {0:F2}% wasted space and the limit is {1}%", wastepercentage, m_wastethreshold));
				else if (m_smallspace > m_volsize)
					log.AddMessage(string.Format("Compacting because there are {0} in small volumes and the volume size is {1}", Library.Utility.Utility.FormatSizeString(m_smallspace), Library.Utility.Utility.FormatSizeString(m_volsize)));
                else if (m_smallvolumecount > m_maxsmallfilecount)
                    log.AddMessage(string.Format("Compacting because there are {0} small volumes and the maximum is {1}", m_smallvolumecount, m_maxsmallfilecount));
				else
					log.AddMessage("Compacting not required");
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
					return (((m_wastedspace / (float)m_fullsize) * 100) >= m_wastethreshold && m_wastevolumes.Count() >= 2) || m_smallspace > m_volsize || m_smallvolumecount > m_maxsmallfilecount;
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
		
		public ICompactReport GetCompactReport(long volsize, long wastethreshold, long smallfilesize, long maxsmallfilecount, System.Data.IDbTransaction transaction)
		{
			return new CompactReport(volsize, wastethreshold, smallfilesize, maxsmallfilecount, GetWastedSpaceReport(transaction).ToList());
		}
		
				
		public interface IBlockQuery : IDisposable
		{
            bool UseBlock(string hash, long size, System.Data.IDbTransaction transaction);
		}
		
		private class BlockQuery : IBlockQuery
		{
			private System.Data.IDbCommand m_command;
			private HashLookupHelper<long> m_lookup;
			
			public BlockQuery(System.Data.IDbConnection con, Options options, System.Data.IDbTransaction transaction)
			{
				m_command = con.CreateCommand();
				m_command.Transaction = transaction;
				
				if (options.BlockHashLookupMemory > 0)
				{
					m_lookup = new HashLookupHelper<long>((ulong)options.BlockHashLookupMemory);
					using(var reader = m_command.ExecuteReader(@"SELECT ""Hash"", ""Size"" FROM ""Block"" "))
					while (reader.Read())
					{
                        var hash = reader.GetString(0);
                        var size = reader.GetInt64(1);
						m_lookup.Add(hash, size, size);
					}
				}
				
				m_command.Parameters.Clear();
				m_command.CommandText = @"SELECT ""VolumeID"" FROM ""Block"" WHERE ""Hash"" = ? AND ""Size"" = ? ";
				m_command.AddParameters(2);
			}
			
            public bool UseBlock(string hash, long size, System.Data.IDbTransaction transaction)
			{
				if (m_lookup != null)
				{
					long nsize;
					if(m_lookup.TryGet(hash, size, out nsize) && nsize == size)
                        return true;
                    else
                        return false;
				}
				
                m_command.Transaction = transaction;
				m_command.SetParameterValue(0, hash);	
				m_command.SetParameterValue(1, size);
				var r = m_command.ExecuteScalar();
				return r != null && r != DBNull.Value;
			}
			
			public void Dispose()
			{
        		m_lookup = null;
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
                
				using(var rd = cmd.ExecuteReader(@"SELECT ""C"".""Name"", ""B"".""Name"", ""B"".""Hash"", ""B"".""Size"" FROM ""IndexBlockLink"" A, ""RemoteVolume"" B, ""RemoteVolume"" C WHERE ""A"".""IndexVolumeID"" = ""B"".""ID"" AND ""A"".""BlockVolumeID"" = ""C"".""ID"" AND ""B"".""Hash"" IS NOT NULL AND ""B"".""Size"" IS NOT NULL "))
					while(rd.Read())
					{
						var name = rd.GetValue(0).ToString();
						List<IRemoteVolume> indexfileList;
						if (!lookupBlock.TryGetValue(name, out indexfileList))
						{	
							indexfileList = new List<IRemoteVolume>();
							lookupBlock.Add(name, indexfileList);
						}
						
                        var v = new RemoteVolume(rd.GetString(1), rd.GetString(2), rd.GetInt64(3));
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

