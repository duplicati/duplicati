using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;

namespace Duplicati.Library.Main.Operation
{
    internal class RepairHandler : IDisposable
    {
        private string m_backendurl;
        private Options m_options;
        private CommunicationStatistics m_stat;

        public RepairHandler(string backend, Options options, CommunicationStatistics stat)
        {
            m_backendurl = backend;
            m_options = options;
            m_stat = stat;
        }

        public void Run()
        {
			if (!System.IO.File.Exists(m_options.Dbpath))
				throw new Exception(string.Format("Database file does not exist: {0}", m_options.Dbpath));

        	using(var db = new LocalRepairDatabase(m_options.Dbpath))
			using(var backend = new BackendManager(m_backendurl, m_options, m_stat, db))
        	{
	        	Utility.VerifyParameters(db, m_options);

	            var tp = FilelistProcessor.RemoteListAnalysis(backend, m_options, db);
				var buffer = new byte[m_options.Blocksize];
				var blockhasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.BlockHashAlgorithm);

				if (blockhasher == null)
					throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, m_options.BlockHashAlgorithm));
	            if (!blockhasher.CanReuseTransform)
	                throw new Exception(string.Format(Strings.Foresthash.InvalidCryptoSystem, m_options.BlockHashAlgorithm));
				
				if (tp.ExtraVolumes.Count() > 0 || tp.MissingVolumes.Count() > 0)
				{
					if (!m_options.Force)
					{
						if (tp.MissingVolumes.Count() == 0 && tp.ExtraVolumes.Count() > 0)
						{
							throw new Exception(string.Format("No files were missing, but {0} remote files were, found, did you mean to run recreate-database? (use --force to skip this check)", tp.ExtraVolumes.Count()));
						}
						else if (!tp.BackupPrefixes.Contains(m_options.BackupPrefix) && tp.ParsedVolumes.Count() > 0)
						{
							if (tp.BackupPrefixes.Length == 1)
								throw new Exception(string.Format("Found no backup files with prefix {0}, but files with prefix {1}, did you forget to set the backup-prefix? (use --force to skip this check)", m_options.BackupPrefix, tp.BackupPrefixes[0]));
							else
								throw new Exception(string.Format("Found no backup files with prefix {0}, but files with prefixes {1}, did you forget to set the backup-prefix? (use --force to skip this check)", m_options.BackupPrefix, string.Join(", ",  tp.BackupPrefixes)));
						}
					}
				
					foreach(var n in tp.ExtraVolumes)
						try
						{
							if (m_options.Force && !m_options.Dryrun)
							{
								db.UpdateRemoteVolume(n.File.Name, RemoteVolumeState.Deleting, -1, null, null);								
								backend.Delete(n.File.Name);
							}
							else
								m_stat.LogMessage("[Dryrun] would delete file {0}", n.File.Name);
						}
						catch (Exception ex)
						{
							m_stat.LogError(string.Format("Failed to perform cleanup for extra file: {0}, message: {1}", n.File.Name, ex.Message), ex);
							db.LogMessage("error", string.Format("Failed to perform cleanup for extra file: {0}, message: {1}", n.File.Name, ex.Message), ex, null);
						}
							
					foreach(var n in tp.MissingVolumes)
					{
						try
						{
							if (n.Type == RemoteVolumeType.Files)
							{
								var filesetId = db.GetFilesetIdFromRemotename(n.Name);
								var w = new FilesetVolumeWriter(m_options, DateTime.UtcNow);
								w.SetRemoteFilename(n.Name);
								
								db.WriteFileset(w, null, filesetId);
	
								w.Close();
								if (m_options.Dryrun)
									m_stat.LogMessage("[Dryrun] would re-upload fileset {0}, with size {1}, previous size {2}", n.Name, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(w.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(n.Size));
								else
								{
									db.UpdateRemoteVolume(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null, null);
									backend.Put(w);
								}
							}
							else if (n.Type == RemoteVolumeType.Index)
							{
								var w = new IndexVolumeWriter(m_options);
								w.SetRemoteFilename(n.Name);
								
								foreach(var blockvolume in db.GetBlockVolumesFromIndexName(n.Name))
								{								
									w.StartVolume(blockvolume.Name);
									var volumeid = db.GetRemoteVolumeID(blockvolume.Name);
									
									foreach(var b in db.GetBlocks(volumeid))
										w.AddBlock(b.Hash, b.Size);
										
									w.FinishVolume(blockvolume.Hash, blockvolume.Size);
								}
								
								w.Close();
								
								if (m_options.Dryrun)
									m_stat.LogMessage("[Dryrun] would re-upload index file {0}, with size {1}, previous size {2}", n.Name, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(w.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(n.Size));
								else
								{
									db.UpdateRemoteVolume(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null, null);
									backend.Put(w);
								}
							}
							else if (n.Type == RemoteVolumeType.Blocks)
							{
							
								//TODO: Need to rewrite this to actually work,
								// and use a patch-table so we know exactly what blocks
								// are missing.
								
								//TODO: Once the above is done, figure out how to
								// deal with incomplete block recreates
							
								var w = new BlockVolumeWriter(m_options);
								w.SetRemoteFilename(n.Name);
								var volumeid = db.GetRemoteVolumeID(n.Name);
	
								foreach(var block in db.GetSourceFilesWithBlocks(volumeid, m_options.Blocksize))
								{
									var hash = block.Hash;
									var size = (int)block.Size;
									var recovered = false;
									
									foreach(var source in block.Sources)
									{
										var file = source.File;
										var offset = source.Offset;
										
										try 
										{
											using(var f = System.IO.File.OpenRead(file))
											{
												f.Position = offset;
												if (size == Library.Utility.Utility.ForceStreamRead(f, buffer, size))
												{
													var newhash = Convert.ToBase64String(blockhasher.ComputeHash(buffer, 0, size));
													if (newhash == hash)
													{
														w.AddBlock(hash, buffer, size, Duplicati.Library.Interface.CompressionHint.Default);
														recovered = true;
														break;
													}
												}
											}
										}
										catch (Exception ex)
										{
											m_stat.LogError(string.Format("Failed to access file: {0}", file), ex);
										}
									}
									
									if (!recovered)
									{
										//TODO: Ineffecient to download the remote files repeatedly
										foreach(var vol in db.GetBlockFromRemote(hash, size))
										{
											try 
											{
												var p = VolumeBase.ParseFilename(vol.Name);
												using(var tmpfile = backend.Get(vol.Name, vol.Size, vol.Hash))
												using(var f = new BlockVolumeReader(p.CompressionModule, tmpfile, m_options))
													if (f.ReadBlock(hash, buffer) == size && Convert.ToBase64String(blockhasher.ComputeHash(buffer)) == hash)
													{
														w.AddBlock(hash, buffer, size, Duplicati.Library.Interface.CompressionHint.Default);
														recovered = true;
														break;
													}
											}
											catch (Exception ex)
											{
												m_stat.LogError(string.Format("Failed to access remote file: {0}", vol.Name), ex);
											}											
										}
									}
									
									if (!recovered)
									{
										m_stat.LogMessage("Repair cannot acquire block with hash {0} and size {1}, which is required by the following filesets: ", hash, size);
										foreach(var f in db.GetFilesetsUsingBlock(hash, size))
											m_stat.LogMessage(f.Name);
	
										m_stat.LogMessage("This may be fixed by deleting the filesets and running cleanup again");
										
										throw new Exception(string.Format("Block {0} is required for recreating the file \"{1}\". Repair not possible!!!", hash, n.Name));
									}
									else
									{
										//TODO: Upload
									}
								}
								
								w.Close();
								if (m_options.Dryrun)
									m_stat.LogMessage("[Dryrun] would upload new block file {0}, size {1}, previous size {2}", n.Name, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(w.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(n.Size));
								else
								{
									db.UpdateRemoteVolume(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null, null);
									backend.Put(w);
								}
							}
						}
						catch (Exception ex)
						{
							m_stat.LogError(string.Format("Failed to perform cleanup for missing file: {0}, message: {1}", n.Name, ex.Message), ex);
							db.LogMessage("error", string.Format("Failed to perform cleanup for missing file: {0}, message: {1}", n.Name, ex.Message), ex, null);
						}
					}
				}
				
				backend.WaitForComplete(db, null);
			}
        }

        public void Dispose()
        {
        }

		string GetBlockVolumeName(string name)
		{
			var e = VolumeBase.ParseFilename(name);
			
			return VolumeBase.GenerateFilename(RemoteVolumeType.Blocks, e.Prefix, e.Guid, e.Time, e.CompressionModule, e.EncryptionModule);
		}
    }
}
