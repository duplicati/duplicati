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
using Duplicati.Library.Main.ForestHash.Database;
using Duplicati.Library.Main.ForestHash.Volumes;
using System.Text;

namespace Duplicati.Library.Main.ForestHash.Operation
{
	internal class CompactHandler : IDisposable
	{
        protected string m_backendurl;
        protected FhOptions m_options;
        protected CommunicationStatistics m_stat;
        
		public CompactHandler(string backend, FhOptions options, CommunicationStatistics stat)
		{
            m_backendurl = backend;
            m_options = options;
            m_stat = stat;
		}
		
		public virtual string Run()
		{
			if (!System.IO.File.Exists(m_options.Fhdbpath))
				throw new Exception(string.Format("Database file does not exist: {0}", m_options.Fhdbpath));
			
			using(var db = new LocalDeleteDatabase(m_options.Fhdbpath))
			using(var tr = db.BeginTransaction())
			{
				var r = DoCompact(db, false, tr);
				if (m_options.Force && !m_options.FhDryrun)
					tr.Commit();
				else
					tr.Rollback();
					
				return r;
			}
		}
		
		protected string DoCompact(LocalDeleteDatabase db, bool hasVerifiedBackend, System.Data.IDbTransaction transaction)
		{
			var report = db.GetCompactReport(m_options.VolumeSize, m_options.FhMaxWasteSize, m_options.FhVolsizeTolerance, transaction);
			report.ReportCompactData(m_stat);
			string msg;
			
			if (report.ShouldCompact)
			{
				using(var backend = new FhBackend(m_backendurl, m_options, db, m_stat))
				{
					if (!hasVerifiedBackend && !m_options.FhNoBackendverification)
						ForestHash.VerifyRemoteList(backend, m_options, db);
		
					BlockVolumeWriter newvol = new BlockVolumeWriter(m_options);
					newvol.VolumeID = db.RegisterRemoteVolume(newvol.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, transaction);
	
					ShadowVolumeWriter newvolshadow = null;
					if (!m_options.FhNoShadowfiles)
					{
						newvolshadow = new ShadowVolumeWriter(m_options);
						db.RegisterRemoteVolume(newvolshadow.RemoteFilename, RemoteVolumeType.Shadow, RemoteVolumeState.Temporary, transaction);
						newvolshadow.StartVolume(newvol.RemoteFilename);
					}
					
					FhBackend.IDownloadWaitHandle handle = null;
					
					long blocksInVolume = 0;
					long discardedBlocks = 0;
					long discardedSize = 0;
					byte[] buffer = new byte[m_options.Fhblocksize];
					var remoteList = db.GetRemoteVolumes();
					
					var shadowLookup = remoteList.Where(x => x.Type == RemoteVolumeType.Shadow).ToDictionary(x => VolumeBase.ParseFilename(x.Name).Guid);
					
					var fullydeleted = (from v in remoteList
												where report.DeleteableVolumes.Contains(v.Name)
												select v).ToArray();

					var deleteableVolumes = fullydeleted.ToList();
					deleteableVolumes.AddRange(from v in fullydeleted 
												let key = VolumeBase.ParseFilename(v.Name).Guid
												where shadowLookup.ContainsKey(key) 
												select shadowLookup[key]);

					var downloadedVolumes = (from v in remoteList
								   where report.CompactableVolumes.Contains(v.Name) 
								   select v).ToArray();

					//These are for bookkeeping
					var uploadedVolumes = new List<KeyValuePair<string, long>>();
					var deletedVolumes = new List<KeyValuePair<string, long>>();
					
					using(var q = db.CreateBlockQueryHelper(m_options, transaction))
					{
						for(int i = 0; i < downloadedVolumes.Length; i++)
						{
							var inst = VolumeBase.ParseFilename(downloadedVolumes[i].Name);
							using(var f = new BlockVolumeReader(inst.CompressionModule, (handle ?? backend.GetAsync(downloadedVolumes[i].Name, downloadedVolumes[i].Size, downloadedVolumes[i].Hash)).Wait(), m_options))
							{
								//Prefetch next volume
								handle = i == downloadedVolumes.Length - 1 ? null : backend.GetAsync(downloadedVolumes[i + 1].Name, downloadedVolumes[i + 1].Size, downloadedVolumes[i + 1].Hash);
								
								foreach(var e in f.Blocks)
								{
									if (q.UseBlock(e.Key, e.Value))
									{
										//TODO: How do we get the compression hint? Reverse query for filename in db?
										var s = f.ReadBlock(e.Key, buffer);
										if (s != e.Value)
											throw new Exception("Size mismatch problem, {0} vs {1}");
											
										newvol.AddBlock(e.Key, buffer, s, Duplicati.Library.Interface.CompressionHint.Compressible);
										if (newvolshadow != null)
											newvolshadow.AddBlock(e.Key, e.Value);
											
										db.MoveBlockToNewVolume(e.Key, e.Value, newvol.VolumeID, transaction);
										blocksInVolume++;
										
										if (newvol.Filesize > m_options.VolumeSize)
										{
											uploadedVolumes.Add(new KeyValuePair<string, long>(newvol.RemoteFilename, new System.IO.FileInfo(newvol.LocalFilename).Length));
											if (newvolshadow != null)
												uploadedVolumes.Add(new KeyValuePair<string, long>(newvolshadow.RemoteFilename, new System.IO.FileInfo(newvolshadow.LocalFilename).Length));

											if (m_options.Force && !m_options.FhDryrun)
												backend.Put(newvol, newvolshadow);
											else
												m_stat.LogMessage("[Dryrun] - Would upload generated blockset of size {0}", Utility.Utility.FormatSizeString(new System.IO.FileInfo(newvol.LocalFilename).Length));
											
											
											newvol = new BlockVolumeWriter(m_options);
											newvol.VolumeID = db.RegisterRemoteVolume(newvol.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, transaction);
			
											if (!m_options.FhNoShadowfiles)
											{
												newvolshadow = new ShadowVolumeWriter(m_options);
												db.RegisterRemoteVolume(newvolshadow.RemoteFilename, RemoteVolumeType.Shadow, RemoteVolumeState.Temporary, transaction);
												newvolshadow.StartVolume(newvol.RemoteFilename);
											}
											
											blocksInVolume = 0;
											
											//After we upload this volume, we can delete all previous encountered volumes
											foreach(var d in deleteableVolumes)
											{
												deletedVolumes.Add(new KeyValuePair<string, long>(d.Name, d.Size));
												if (m_options.Force && !m_options.FhDryrun)
													backend.Delete(d.Name);
												else
													m_stat.LogMessage("[Dryrun] - Would delete remote file: {0}, size: {1}", d.Name, Utility.Utility.FormatSizeString(d.Size));
											}
											deleteableVolumes.Clear();
										}
									}
									else
									{
										discardedBlocks++;
										discardedSize += e.Value;
									}
								}
							}

							deleteableVolumes.Add(downloadedVolumes[i]);
							RemoteVolumeEntry sh;
							if (shadowLookup.TryGetValue(VolumeBase.ParseFilename(downloadedVolumes[i].Name).Guid, out sh))
								deleteableVolumes.Add(sh);			
						}
						
						if (blocksInVolume > 0)
						{
							uploadedVolumes.Add(new KeyValuePair<string, long>(newvol.RemoteFilename, new System.IO.FileInfo(newvol.LocalFilename).Length));
							if (newvolshadow != null)
								uploadedVolumes.Add(new KeyValuePair<string, long>(newvolshadow.RemoteFilename, new System.IO.FileInfo(newvolshadow.LocalFilename).Length));
							if (m_options.Force && !m_options.FhDryrun)
								backend.Put(newvol, newvolshadow);
							else
								m_stat.LogMessage("[Dryrun] - Would upload generated blockset of size {0}", Utility.Utility.FormatSizeString(new System.IO.FileInfo(newvol.LocalFilename).Length));
						}
						else
						{
			                db.RemoveRemoteVolume(newvol.RemoteFilename, transaction);
		                    if (newvolshadow != null)
		                    {
			                    db.RemoveRemoteVolume(newvolshadow.RemoteFilename, transaction);
			                    newvolshadow.FinishVolume(null, 0);
		                    }
						}
						
						foreach(var f in deleteableVolumes)
						{
							deletedVolumes.Add(new KeyValuePair<string, long>(f.Name, f.Size));
							if (m_options.Force && !m_options.FhDryrun)
								backend.Delete(f.Name);
							else
								m_stat.LogMessage("[Dryrun] - Would delete remote file: {0}, size: {1}", f.Name, Utility.Utility.FormatSizeString(f.Size));
						}
						
						var downloadSize = downloadedVolumes.Aggregate(0L, (a,x) => a + x.Size);
						var deletedSize = downloadedVolumes.Aggregate(0L, (a,x) => a + x.Size);
						var uploadSize = uploadedVolumes.Aggregate(0L, (a,x) => a + x.Value);
						
						if (m_options.Force && !m_options.FhDryrun)
							msg = string.Format("Downloaded {0} file(s) with a total size of {1}, deleted {2} file(s) with a total size of {3}, and compacted to {4} file(s) with a size of {5}, which reduced storage by {6} file(s) and {7}", downloadedVolumes.Length, Utility.Utility.FormatSizeString(downloadSize), deletedVolumes.Count, Utility.Utility.FormatSizeString(deletedSize), uploadedVolumes.Count, Utility.Utility.FormatSizeString(uploadSize), deletedVolumes.Count - uploadedVolumes.Count, Utility.Utility.FormatSizeString(deletedSize - uploadSize));
						else
							msg = string.Format("Would download {0} file(s) with a total size of {1}, delete {2} file(s) with a total size of {3}, and compact to {4} file(s) with a size of {5}, which will reduce storage by {6} file(s) and {7}", downloadedVolumes.Length, Utility.Utility.FormatSizeString(downloadSize), deletedVolumes.Count, Utility.Utility.FormatSizeString(deletedSize), uploadedVolumes.Count, Utility.Utility.FormatSizeString(uploadSize), deletedVolumes.Count - uploadedVolumes.Count, Utility.Utility.FormatSizeString(deletedSize - uploadSize));
						m_stat.LogMessage(msg);
						
					}
		
					backend.WaitForComplete();
				}
			}
			else
			{
				msg = "Compacting not required";
			}
			
			
			return msg;
		}

		#region IDisposable implementation

		public void Dispose()
		{
		}

		#endregion
	}
}

