using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Main.ForestHash.Database;
using Duplicati.Library.Main.ForestHash.Volumes;

namespace Duplicati.Library.Main.ForestHash.Operation
{
    internal class CleanupHandler : IDisposable
    {
        private string m_backendurl;
        private FhOptions m_options;
        private CommunicationStatistics m_stat;

        public CleanupHandler(string backend, FhOptions options, CommunicationStatistics stat)
        {
            m_backendurl = backend;
            m_options = options;
            m_stat = stat;
        }

        public void Run()
        {
			if (!System.IO.File.Exists(m_options.Fhdbpath))
				throw new Exception(string.Format("Database file does not exist: {0}", m_options.Fhdbpath));

        	using(var db = new LocalCleanupDatabase(m_options.Fhdbpath))
			using(var backend = new FhBackend(m_backendurl, m_options, m_stat, db))
        	{
	        	ForestHash.VerifyParameters(db, m_options);

	            var tp = ForestHash.RemoteListAnalysis(backend, m_options, db);
				var buffer = new byte[m_options.Fhblocksize];
				var blockhasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.FhBlockHashAlgorithm);

				if (blockhasher == null)
					throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, m_options.FhBlockHashAlgorithm));
	            if (!blockhasher.CanReuseTransform)
	                throw new Exception(string.Format(Strings.Foresthash.InvalidCryptoSystem, m_options.FhBlockHashAlgorithm));
				
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
							if (m_options.Force && !m_options.FhDryrun)
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
								
								using(var b = new LocalBackupDatabase(db, m_options))
									b.WriteFileset(w, null, filesetId);
	
								w.Close();
								if (m_options.FhDryrun)
									m_stat.LogMessage("[Dryrun] would re-upload fileset {0}, with size {1}, previous size {2}", n.Name, Utility.Utility.FormatSizeString(new System.IO.FileInfo(w.LocalFilename).Length), Utility.Utility.FormatSizeString(n.Size));
								else
								{
									db.UpdateRemoteVolume(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null, null);
									backend.Put(w);
								}
							}
							else if (n.Type == RemoteVolumeType.Shadow)
							{
								var w = new ShadowVolumeWriter(m_options);
								w.SetRemoteFilename(n.Name);
								
								foreach(var blockvolume in db.GetBlockVolumesFromShadowName(n.Name))
								{								
									w.StartVolume(blockvolume.Name);
									var volumeid = db.GetRemoteVolumeID(blockvolume.Name);
									
									using(var ldb = new LocalBackupDatabase(db, m_options))
										foreach(var b in ldb.GetBlocks(volumeid))
											w.AddBlock(b.Hash, b.Size);
										
									w.FinishVolume(blockvolume.Hash, blockvolume.Size);
								}
								
								w.Close();
								
								if (m_options.FhDryrun)
									m_stat.LogMessage("[Dryrun] would re-upload shadow file {0}, with size {1}, previous size {2}", n.Name, Utility.Utility.FormatSizeString(new System.IO.FileInfo(w.LocalFilename).Length), Utility.Utility.FormatSizeString(n.Size));
								else
								{
									db.UpdateRemoteVolume(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null, null);
									backend.Put(w);
								}
							}
							else if (n.Type == RemoteVolumeType.Blocks)
							{
								var w = new BlockVolumeWriter(m_options);
								w.SetRemoteFilename(n.Name);
								var volumeid = db.GetRemoteVolumeID(n.Name);
	
								foreach(var block in db.GetSourceFilesWithBlocks(volumeid, m_options.Fhblocksize))
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
												if (size == Utility.Utility.ForceStreamRead(f, buffer, size))
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
								if (m_options.FhDryrun)
									m_stat.LogMessage("[Dryrun] would upload new block file {0}, size {1}, previous size {2}", n.Name, Utility.Utility.FormatSizeString(new System.IO.FileInfo(w.LocalFilename).Length), Utility.Utility.FormatSizeString(n.Size));
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
