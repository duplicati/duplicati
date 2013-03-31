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
        	using(var tr = db.BeginTransaction())
			using(var backend = new FhBackend(m_backendurl, m_options, db, m_stat))
        	{
	        	ForestHash.VerifyParameters(db, m_options);

	            var tp = ForestHash.RemoteListAnalysis(backend, m_options, db);
				var extra = tp.Item1.ToList();
				var missing = tp.Item2.ToList();
				var buffer = new byte[m_options.Fhblocksize];
				var blockhasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.FhBlockHashAlgorithm);

				if (blockhasher == null)
					throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, m_options.FhBlockHashAlgorithm));
	            if (!blockhasher.CanReuseTransform)
	                throw new Exception(string.Format(Strings.Foresthash.InvalidCryptoSystem, m_options.FhBlockHashAlgorithm));
				
				if (extra.Count > 0 || missing.Count > 0)
				{
					foreach(var n in extra)
						if (m_options.Force && !m_options.FhDryrun)
							backend.Delete(n.Name);
						else
							m_stat.LogMessage("[Dryrun] would delete file {0}", n.Name);
							
					foreach(var n in missing)
					{
						if (n.Type == RemoteVolumeType.Files)
						{
							var operationId = db.GetOperationIdFromRemotename(n.Name);
							var w = new FilesetVolumeWriter(m_options, DateTime.UtcNow);
							w.SetRemoteFilename(n.Name);
							
							using(var b = new LocalBackupDatabase(db.Connection, m_options))
								b.WriteFileset(w, operationId);

							w.Close();
							if (m_options.FhDryrun)
								m_stat.LogMessage("[Dryrun] would re-upload fileset {0}", n.Name);
							else
							{
								db.UpdateRemoteVolume(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null, tr);
								backend.Put(w);
							}
						}
						else if (n.Type == RemoteVolumeType.Shadow)
						{
							var blockvolumename = GetBlockVolumeName(n.Name);
							var w = new ShadowVolumeWriter(m_options);
							w.SetRemoteFilename(n.Name);
							w.StartVolume(blockvolumename);
							
							string blockvolumehash;
							long blockvolumesize;
							RemoteVolumeType blockvolumetype;
							RemoteVolumeState blockvolumestate;
							db.GetRemoteVolume(blockvolumename, out blockvolumehash, out blockvolumesize, out blockvolumetype, out blockvolumestate);
							
							var volumeid = db.GetRemoteVolumeID(blockvolumename);
							
							foreach(var b in db.GetBlocks(volumeid))
								w.AddBlock(b.Hash, b.Size);
								
							w.FinishVolume(blockvolumehash, blockvolumesize);
							w.Close();
							
							if (m_options.FhDryrun)
								m_stat.LogMessage("[Dryrun] would re-upload shadow file {0}", n.Name);
							else
							{
								db.UpdateRemoteVolume(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null, tr);
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
											using(var file = backend.Get(vol.Name, vol.Size, vol.Hash))
											using(var f = new BlockVolumeReader(p.CompressionModule, file, m_options))
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
									//TODO: What is logical?
									//Accept, but mark certain filelists broken?
									//Should at least report what filesets to delete for repair to succeed
									
									throw new Exception(string.Format("Block {0} is required for recreating the file \"{1}\". Repair not possible!!!", hash, n.Name));
								}
							}
							
							w.Close();
							if (m_options.FhDryrun)
								m_stat.LogMessage("[Dryrun] would upload new block file {0}", n.Name);
							else
							{
								db.UpdateRemoteVolume(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null, tr);
								backend.Put(w);
							}
						}
					}
				}
				
				if (!m_options.FhDryrun)
					tr.Commit();
				else
					tr.Rollback();
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
