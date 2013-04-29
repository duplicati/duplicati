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
			
			using(var db = new LocalDeleteDatabase(m_options.Fhdbpath, true))
			using(var tr = db.BeginTransaction())
			{
	        	ForestHash.VerifyParameters(db, m_options);
	        	
				var r = DoCompact(db, false, tr);
				if (m_options.Force && !m_options.FhDryrun)
					tr.Commit();
				else
					tr.Rollback();
					
				return r;
			}
		}
		
		internal string DoCompact(LocalDeleteDatabase db, bool hasVerifiedBackend, System.Data.IDbTransaction transaction)
		{
			var report = db.GetCompactReport(m_options.VolumeSize, m_options.FhMaxWasteSize, m_options.FhVolsizeTolerance, transaction);
			report.ReportCompactData(m_stat);
			string msg;
			
			if (report.ShouldReclaim || report.ShouldCompact)
			{
				using(var backend = new FhBackend(m_backendurl, m_options, m_stat, db))
				{
					if (!hasVerifiedBackend && !m_options.FhNoBackendverification)
						ForestHash.VerifyRemoteList(backend, m_options, db, m_stat);
		
					BlockVolumeWriter newvol = new BlockVolumeWriter(m_options);
					newvol.VolumeID = db.RegisterRemoteVolume(newvol.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, transaction);
	
					IndexVolumeWriter newvolindex = null;
					if (!m_options.FhNoIndexfiles)
					{
						newvolindex = new IndexVolumeWriter(m_options);
						db.RegisterRemoteVolume(newvolindex.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary, transaction);
						newvolindex.StartVolume(newvol.RemoteFilename);
					}
					
					long blocksInVolume = 0;
					long discardedBlocks = 0;
					long discardedSize = 0;
					byte[] buffer = new byte[m_options.Fhblocksize];
					var remoteList = db.GetRemoteVolumes();
					
					//These are for bookkeeping
					var uploadedVolumes = new List<KeyValuePair<string, long>>();
					var deletedVolumes = new List<KeyValuePair<string, long>>();
					var downloadedVolumes = new List<KeyValuePair<string, long>>();
					
					//We start by deleting unused volumes to save space before uploading new stuff
					var fullyDeleteable = (from v in remoteList
							where report.DeleteableVolumes.Contains(v.Name)
							select (IRemoteVolume)v).ToList();
					deletedVolumes.AddRange(DoDelete(db, backend, fullyDeleteable, transaction));

					// This list is used to pick up unused volumes,
					// so they can be deleted once the upload of the
					// required fragments is complete
					var deleteableVolumes = new List<IRemoteVolume>();

					if (report.ShouldCompact)
					{
						var volumesToDownload = (from v in remoteList
									   where report.CompactableVolumes.Contains(v.Name) 
									   select (IRemoteVolume)v).ToList();
						
						using(var q = db.CreateBlockQueryHelper(m_options, transaction))
						{
							foreach (var entry in new AsyncDownloader(volumesToDownload, backend))
							using (var tmpfile = entry.Value)
							{
								downloadedVolumes.Add(new KeyValuePair<string, long>(entry.Key.Name, entry.Key.Size));
								var inst = VolumeBase.ParseFilename(entry.Key.Name);
								using(var f = new BlockVolumeReader(inst.CompressionModule, tmpfile, m_options))
								{
									foreach(var e in f.Blocks)
									{
										if (q.UseBlock(e.Key, e.Value))
										{
											//TODO: How do we get the compression hint? Reverse query for filename in db?
											var s = f.ReadBlock(e.Key, buffer);
											if (s != e.Value)
												throw new Exception("Size mismatch problem, {0} vs {1}");
												
											newvol.AddBlock(e.Key, buffer, s, Duplicati.Library.Interface.CompressionHint.Compressible);
											if (newvolindex != null)
												newvolindex.AddBlock(e.Key, e.Value);
												
											db.MoveBlockToNewVolume(e.Key, e.Value, newvol.VolumeID, transaction);
											blocksInVolume++;
											
											if (newvol.Filesize > m_options.VolumeSize)
											{
												uploadedVolumes.Add(new KeyValuePair<string, long>(newvol.RemoteFilename, new System.IO.FileInfo(newvol.LocalFilename).Length));
												if (newvolindex != null)
													uploadedVolumes.Add(new KeyValuePair<string, long>(newvolindex.RemoteFilename, new System.IO.FileInfo(newvolindex.LocalFilename).Length));
	
												if (m_options.Force && !m_options.FhDryrun)
													backend.Put(newvol, newvolindex);
												else
													m_stat.LogMessage("[Dryrun] - Would upload generated blockset of size {0}", Utility.Utility.FormatSizeString(new System.IO.FileInfo(newvol.LocalFilename).Length));
												
												
												newvol = new BlockVolumeWriter(m_options);
												newvol.VolumeID = db.RegisterRemoteVolume(newvol.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, transaction);
				
												if (!m_options.FhNoIndexfiles)
												{
													newvolindex = new IndexVolumeWriter(m_options);
													db.RegisterRemoteVolume(newvolindex.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary, transaction);
													newvolindex.StartVolume(newvol.RemoteFilename);
												}
												
												blocksInVolume = 0;
												
												//After we upload this volume, we can delete all previous encountered volumes
												deletedVolumes.AddRange(DoDelete(db, backend, deleteableVolumes, transaction));
											}
										}
										else
										{
											discardedBlocks++;
											discardedSize += e.Value;
										}
									}
								}
	
								deleteableVolumes.Add(entry.Key);
							}
							
							if (blocksInVolume > 0)
							{
								uploadedVolumes.Add(new KeyValuePair<string, long>(newvol.RemoteFilename, new System.IO.FileInfo(newvol.LocalFilename).Length));
								if (newvolindex != null)
									uploadedVolumes.Add(new KeyValuePair<string, long>(newvolindex.RemoteFilename, new System.IO.FileInfo(newvolindex.LocalFilename).Length));
								if (m_options.Force && !m_options.FhDryrun)
									backend.Put(newvol, newvolindex);
								else
									m_stat.LogMessage("[Dryrun] - Would upload generated blockset of size {0}", Utility.Utility.FormatSizeString(new System.IO.FileInfo(newvol.LocalFilename).Length));
							}
							else
							{
				                db.RemoveRemoteVolume(newvol.RemoteFilename, transaction);
			                    if (newvolindex != null)
			                    {
				                    db.RemoveRemoteVolume(newvolindex.RemoteFilename, transaction);
				                    newvolindex.FinishVolume(null, 0);
			                    }
							}
						}
					}
					
					deletedVolumes.AddRange(DoDelete(db, backend, deleteableVolumes, transaction));
										
					var downloadSize = downloadedVolumes.Aggregate(0L, (a,x) => a + x.Value);
					var deletedSize = deletedVolumes.Aggregate(0L, (a,x) => a + x.Value);
					var uploadSize = uploadedVolumes.Aggregate(0L, (a,x) => a + x.Value);
					
					if (m_options.Force && !m_options.FhDryrun)
					{
						if (downloadedVolumes.Count == 0)
							msg = string.Format("Deleted {0} files, which reduced storage by {1}", deletedVolumes.Count, Utility.Utility.FormatSizeString(deletedSize));
						else
							msg = string.Format("Downloaded {0} file(s) with a total size of {1}, deleted {2} file(s) with a total size of {3}, and compacted to {4} file(s) with a size of {5}, which reduced storage by {6} file(s) and {7}", downloadedVolumes.Count, Utility.Utility.FormatSizeString(downloadSize), deletedVolumes.Count, Utility.Utility.FormatSizeString(deletedSize), uploadedVolumes.Count, Utility.Utility.FormatSizeString(uploadSize), deletedVolumes.Count - uploadedVolumes.Count, Utility.Utility.FormatSizeString(deletedSize - uploadSize));
					}
					else
					{
						if (downloadedVolumes.Count == 0)
							msg = string.Format("Would delete {0} files, which would reduce storage by {1}", deletedVolumes.Count, Utility.Utility.FormatSizeString(deletedSize));
						else
							msg = string.Format("Would download {0} file(s) with a total size of {1}, delete {2} file(s) with a total size of {3}, and compact to {4} file(s) with a size of {5}, which would reduce storage by {6} file(s) and {7}", downloadedVolumes.Count, Utility.Utility.FormatSizeString(downloadSize), deletedVolumes.Count, Utility.Utility.FormatSizeString(deletedSize), uploadedVolumes.Count, Utility.Utility.FormatSizeString(uploadSize), deletedVolumes.Count - uploadedVolumes.Count, Utility.Utility.FormatSizeString(deletedSize - uploadSize));
					}
					m_stat.LogMessage(msg);
							
					backend.WaitForComplete(db, transaction);
				}
			}
			else
			{
				msg = "Compacting not required";
			}
			
			
			return msg;
		}
		
		private IEnumerable<KeyValuePair<string, long>> DoDelete(LocalDeleteDatabase db, FhBackend backend, List<IRemoteVolume> deleteableVolumes, System.Data.IDbTransaction transaction)
		{
			foreach(var f in db.GetDeletableVolumes(deleteableVolumes, transaction))
			{
				if (m_options.Force && !m_options.FhDryrun)
					backend.Delete(f.Name);
				else
					m_stat.LogMessage("[Dryrun] - Would delete remote file: {0}, size: {1}", f.Name, Utility.Utility.FormatSizeString(f.Size));

				yield return new KeyValuePair<string, long>(f.Name, f.Size);
			}				
			
			deleteableVolumes.Clear();
		}

		#region IDisposable implementation

		public void Dispose()
		{
		}

		#endregion
	}
}

