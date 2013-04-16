using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Duplicati.Library.Main.ForestHash.Database;
using Duplicati.Library.Main.ForestHash.Volumes;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.ForestHash.Operation
{
    internal class BackupHandler : IDisposable
    {
        private readonly FhOptions m_options;
        private readonly BackupStatistics m_stat;
        private FhBackend m_backend;
        private string m_backendurl;

        private readonly byte[] m_blockbuffer;
        private readonly byte[] m_blocklistbuffer;
        private readonly System.Security.Cryptography.HashAlgorithm m_blockhasher;
        private readonly System.Security.Cryptography.HashAlgorithm m_filehasher;

        private LocalBackupDatabase m_database;
        private System.Data.IDbTransaction m_transaction;
        private BlockVolumeWriter m_blockvolume;
        private ShadowVolumeWriter m_shadowvolume;
        private FilesetVolumeWriter m_filesetvolume;

        private Snapshots.ISnapshotService m_snapshot;
        private long m_otherchanges;

        private readonly ForestHash.IMetahash EMPTY_METADATA;

        private string[] m_sources;
        
        //To better record what happens with the backend, we flush the log messages regularly
        // We cannot flush immediately, because that would mess up the transaction, as the uploader
        // is on another thread
        private readonly TimeSpan FLUSH_TIMESPAN = TimeSpan.FromSeconds(10);
        private DateTime m_backendLogFlushTimer;

        public BackupHandler(string backendurl, FhOptions options, BackupStatistics stat, string[] sources)
        {
        	EMPTY_METADATA = ForestHash.WrapMetadata(new Dictionary<string, string>(), options);
        	
            m_options = options;
            m_stat = stat;
            m_database = new LocalBackupDatabase(m_options.Fhdbpath, m_options);
            m_backendurl = backendurl;

            m_sources = sources;
            m_blockbuffer = new byte[m_options.Fhblocksize];
            m_blocklistbuffer = new byte[m_options.Fhblocksize];

            m_blockhasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.FhBlockHashAlgorithm);
            m_filehasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.FhFileHashAlgorithm);

			if (m_blockhasher == null)
				throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, m_options.FhBlockHashAlgorithm));
			if (m_filehasher == null)
				throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, m_options.FhFileHashAlgorithm));

            if (!m_blockhasher.CanReuseTransform)
                throw new Exception(string.Format(Strings.Foresthash.InvalidCryptoSystem, m_options.FhBlockHashAlgorithm));
            if (!m_filehasher.CanReuseTransform)
                throw new Exception(string.Format(Strings.Foresthash.InvalidCryptoSystem, m_options.FhFileHashAlgorithm));
        }

        private static Snapshots.ISnapshotService GetSnapshot(string[] sourcefolders, Options options, CommunicationStatistics stat)
        {
            try
            {
                if (options.SnapShotStrategy != Options.OptimizationStrategy.Off)
                    return Duplicati.Library.Snapshots.SnapshotUtility.CreateSnapshot(sourcefolders, options.RawOptions);
            }
            catch (Exception ex)
            {
                if (options.SnapShotStrategy == Options.OptimizationStrategy.Required)
                    throw;
                else if (options.SnapShotStrategy == Options.OptimizationStrategy.On)
                {
                    stat.LogWarning(string.Format(Strings.RSyncDir.SnapshotFailedError, ex.ToString()), ex);
                }
            }

            return Utility.Utility.IsClientLinux ?
                (Library.Snapshots.ISnapshotService)new Duplicati.Library.Snapshots.NoSnapshotLinux(sourcefolders, options.RawOptions)
                    :
                (Library.Snapshots.ISnapshotService)new Duplicati.Library.Snapshots.NoSnapshotWindows(sourcefolders, options.RawOptions);
        }


        public void Run()
        {
        	ForestHash.VerifyParameters(m_database, m_options);
        	    
			var lastVolumeSize = -1L;
			m_backendLogFlushTimer = DateTime.Now.Add(FLUSH_TIMESPAN);

            try
            {
            	m_transaction = m_database.BeginTransaction();
	            using (m_backend = new FhBackend(m_backendurl, m_options, m_stat, m_database))
	            using (m_filesetvolume = new FilesetVolumeWriter(m_options, m_database.OperationTimestamp))
	            {
		        	if (!m_options.FhNoBackendverification)
		        	{
		            	try 
		            	{
		            		ForestHash.VerifyRemoteList(m_backend, m_options, m_database, m_stat);
		            	} 
		            	catch (Exception ex)
		            	{
		            		if (m_options.AutoCleanup)
		            		{
		            			m_stat.LogWarning("Backend verification failed, attempting automatic cleanup", ex);
		            			using(var ch = new CleanupHandler(m_backend.BackendUrl, m_options, m_stat))
		            				ch.Run();
		            			
		            			m_stat.LogMessage("Backend cleanup finished, retrying verification");
		            			ForestHash.VerifyRemoteList(m_backend, m_options, m_database, m_stat);
		            		}
		            		else
		            			throw;
		            	}
		            }
		            
	                var filesetvolumeid = m_database.RegisterRemoteVolume(m_filesetvolume.RemoteFilename, RemoteVolumeType.Files, RemoteVolumeState.Temporary, m_transaction);
	            	m_database.CreateFileset(filesetvolumeid, VolumeBase.ParseFilename(m_filesetvolume.RemoteFilename).Time, m_transaction);
	
		            m_blockvolume = new BlockVolumeWriter(m_options);
		            m_blockvolume.VolumeID = m_database.RegisterRemoteVolume(m_blockvolume.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, m_transaction);
		            
		            if (!m_options.FhNoShadowfiles)
		            {
			            m_shadowvolume = new ShadowVolumeWriter(m_options);
			            m_shadowvolume.VolumeID = m_database.RegisterRemoteVolume(m_shadowvolume.RemoteFilename, RemoteVolumeType.Shadow, RemoteVolumeState.Temporary, m_transaction);
			            m_database.AddShadowBlockLink(m_shadowvolume.VolumeID, m_blockvolume.VolumeID, m_transaction);
			            m_shadowvolume.StartVolume(m_blockvolume.RemoteFilename);
		            }
		                        	
	                if (m_options.FhChangedFilelist != null && m_options.FhChangedFilelist.Length >= 1)
	                {
	                    foreach (var p in m_options.FhChangedFilelist)
	                    {
	                        FileAttributes fa = new FileAttributes();
	                        try { fa = m_snapshot.GetAttributes(p); }
	                        catch (Exception ex) { m_stat.LogWarning(string.Format("Failed to read attributes: {0}, message: {1}", p, ex.Message), ex); }
	
	                        try { this.HandleFilesystemEntry(null, p, fa); }
	                        catch (Exception ex) { m_stat.LogWarning(string.Format("Failed to process element: {0}, message: {1}", p, ex.Message), ex); }
	                    }
	
	                    m_database.AppendFilesFromPreviousSet(m_transaction, m_options.FhDeletedFilelist);
	                }
	                else
	                {
	                    using (m_snapshot = GetSnapshot(m_sources, m_options, m_stat))
	                        m_snapshot.EnumerateFilesAndFolders(this.HandleFilesystemEntry);
	                }
		
		            m_database.VerifyConsistency();
		            m_database.UpdateChangeStatistics(m_stat);
							
		            if (m_stat.AddedFiles > 0 || m_stat.ModifiedFiles > 0 || m_otherchanges > 0)
		            {
	 	                if (m_blockvolume.SourceSize > 0)
		                {
		 					lastVolumeSize = m_blockvolume.SourceSize;
		 					
		                	if (m_options.FhDryrun)
		                	{
		                		m_stat.LogMessage("[Dryrun] Would upload block volume: {0}, size: {1}", m_blockvolume.RemoteFilename, Utility.Utility.FormatSizeString(new FileInfo(m_blockvolume.LocalFilename).Length));
		                		if (m_shadowvolume != null)
		                			m_stat.LogMessage("[Dryrun] Would upload shadow volume: {0}, size: {1}", m_shadowvolume.RemoteFilename, Utility.Utility.FormatSizeString(new FileInfo(m_shadowvolume.LocalFilename).Length));
		                	}
		                	else
		                	{
		                		m_database.UpdateRemoteVolume(m_blockvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);
		                		m_database.UpdateRemoteVolume(m_filesetvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);
		                		if (m_shadowvolume != null)
		                			m_database.UpdateRemoteVolume(m_shadowvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);
		                		
		                		m_transaction.Commit();
		                		m_transaction = m_database.BeginTransaction();
		                		
		                    	m_backend.Put(m_blockvolume, m_shadowvolume);
		                    }
		                }
		                else
		                {
		                    m_database.RemoveRemoteVolume(m_blockvolume.RemoteFilename, m_transaction);
		                    if (m_shadowvolume != null)
		                    {
			                    m_database.RemoveRemoteVolume(m_shadowvolume.RemoteFilename, m_transaction);
			                    m_shadowvolume.FinishVolume(null, 0);
		                    }
		                }
	
						if (lastVolumeSize > 0)
						{
		                    if (!string.IsNullOrEmpty(m_options.SignatureControlFiles))
		                        foreach (var p in m_options.SignatureControlFiles.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
		                            m_filesetvolume.AddControlFile(p, m_options.GetCompressionHintFromFilename(p));
		
		                    m_database.WriteFileset(m_filesetvolume, m_transaction);
		                	if (m_options.FhDryrun)
		                		m_stat.LogMessage("[Dryrun] Would upload fileset volume: {0}, size: {1}", m_filesetvolume.RemoteFilename, Utility.Utility.FormatSizeString(new FileInfo(m_filesetvolume.LocalFilename).Length));
		                	else
		                	{
		                		m_database.UpdateRemoteVolume(m_filesetvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);
		                    	m_backend.Put(m_filesetvolume);
		                    }
	                	}
	                	else
	                	{
	                		m_database.RemoveRemoteVolume(m_filesetvolume.RemoteFilename, m_transaction);
	                	}
		            }
		            else
		            {
		                m_database.LogMessage("info", "removing temp files, as no data needs to be uploaded", null, m_transaction);
		                m_database.RemoveRemoteVolume(m_blockvolume.RemoteFilename, m_transaction);
		                if (m_shadowvolume != null)
		                {
			                m_database.RemoveRemoteVolume(m_shadowvolume.RemoteFilename, m_transaction);
			                m_shadowvolume.FinishVolume(null, -1);
		                }
		            }
									
		            m_backend.WaitForComplete(m_database, m_transaction);
		            
		            if (lastVolumeSize < m_options.VolumeSize - m_options.FhVolsizeTolerance && !m_options.FhNoAutoCompact && (m_options.Force || m_options.FhDryrun))
		            	using(var ch = new CompactHandler(m_backend.BackendUrl, m_options, m_stat))
	            		using(var db = new LocalDeleteDatabase(m_database))
		            		ch.DoCompact(db, true, m_transaction);
		            
					if (m_options.FhDryrun)
					{
						m_transaction.Rollback();
						m_transaction = null;
					}
					else
					{
	                	m_transaction.Commit();
	                	m_transaction = null;
	                	using(var backend = new FhBackend(m_backendurl, m_options, m_stat, m_database))
	                	{
							ForestHash.VerifyRemoteList(backend, m_options, m_database, m_stat);
							backend.WaitForComplete(m_database, null);
						}
	                }
		    	}
	    	}
	    	finally
	    	{
	    		if (m_transaction != null)
	    			m_transaction.Rollback();
	    	} 
        }

        private bool HandleFilesystemEntry(string rootpath, string path, System.IO.FileAttributes attributes)
        {
        	if (m_backendLogFlushTimer < DateTime.Now)
        	{
				m_backendLogFlushTimer = DateTime.Now.Add(FLUSH_TIMESPAN);
        		m_backend.FlushDbMessages(m_database, null);
        	}
        
            if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                if (m_options.SymlinkPolicy == Options.SymlinkStrategy.Ignore)
                    return false;

                if (m_options.SymlinkPolicy == Options.SymlinkStrategy.Store)
                {
                    Dictionary<string, string> metadata;

                    if (!m_options.FhNoMetadata)
                    {
                        metadata = null; //snapshot.GetMetadata(path);
                        if (metadata == null)
                            metadata = new Dictionary<string, string>();

                        if (!metadata.ContainsKey("CoreAttributes"))
                            metadata["CoreAttributes"] = attributes.ToString();
                        if (!metadata.ContainsKey("CoreLastWritetime"))
                            metadata["CoreLastWritetime"] = Utility.Utility.SerializeDateTime(m_snapshot.GetLastWriteTime(path));
                    }
                    else
                    {
                        metadata = new Dictionary<string, string>();
                    }

                    if (!metadata.ContainsKey("CoreSymlinkTarget"))
                        metadata["CoreSymlinkTarget"] = m_snapshot.GetSymlinkTarget(path);

                    var metahash = ForestHash.WrapMetadata(metadata, m_options);
                    if (AddSymlinkToOutput(path, DateTime.UtcNow, metahash))
                        m_otherchanges++;
                    
                    //Do not recurse symlinks
                    return false;
                }
            }

            if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
            {
                ForestHash.IMetahash metahash;

                if (m_options.FhNoMetadata)
                {
                    metahash = EMPTY_METADATA;
                }
                else
                {
                    Dictionary<string, string> metadata = null; //snapshot.GetMetadata(path);
                    if (metadata == null)
                        metadata = new Dictionary<string, string>();

                    if (!metadata.ContainsKey("CoreAttributes"))
                        metadata["CoreAttributes"] = attributes.ToString();
                    if (!metadata.ContainsKey("CoreLastWritetime"))
                        metadata["CoreLastWritetime"] = Utility.Utility.SerializeDateTime(m_snapshot.GetLastWriteTime(path));
                    metahash = ForestHash.WrapMetadata(metadata, m_options);
                }

                //m_filesetvolume.AddDirectory(path, metahash.Hash, metahash.Size);
                if (AddFolderToOutput(path, DateTime.UtcNow, metahash))
                    m_otherchanges++;
                return true;
            }

            DateTime oldScanned;
            var oldId = m_database.GetFileEntry(path, out oldScanned);
            m_stat.ExaminedFiles++;

            bool changed = false;

            //Skip symlinks if required
            if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint && m_options.SymlinkPolicy == Options.SymlinkStrategy.Ignore)
                return false;

            try
            {
                DateTime lastModified = m_snapshot.GetLastWriteTime(path);
                if (oldId < 0 || m_options.DisableFiletimeCheck || lastModified > oldScanned)
                {
                    m_stat.OpenedFiles++;

                    long filesize = 0;
                    DateTime scantime = DateTime.UtcNow;

                    ForestHash.IMetahash metahashandsize;
                    if (m_options.FhNoMetadata)
                    {
                        metahashandsize = EMPTY_METADATA;
                    }
                    else
                    {
                        Dictionary<string, string> metadata = null; //snapshot.GetMetadata(file);
                        if (metadata == null)
                            metadata = new Dictionary<string, string>();

                        if (!metadata.ContainsKey("CoreAttributes"))
                            metadata["CoreAttributes"] = attributes.ToString();
                        if (!metadata.ContainsKey("CoreLastWritetime"))
                            metadata["CoreLastWritetime"] = Utility.Utility.SerializeDateTime(lastModified);

                        metahashandsize = ForestHash.WrapMetadata(metadata, m_options);
                    }

                    var blocklisthashes = new List<string>();
                    var hint = m_options.GetCompressionHintFromFilename(path);
                    var oldHash = oldId < 0 ? null : m_database.GetFileHash(oldId);

                    using (var hashcollector = new HashlistCollector())
                    {
                        using (var fs = new Blockprocessor(m_snapshot.OpenRead(path), m_blockbuffer))
                        {
                            int size;
                            int blocklistoffset = 0;

                            m_filehasher.Initialize();

                            do
                            {
                                size = fs.Readblock();

                                m_filehasher.TransformBlock(m_blockbuffer, 0, size, m_blockbuffer, 0);
                                var blockkey = m_blockhasher.ComputeHash(m_blockbuffer, 0, size);
                                if (m_blocklistbuffer.Length - blocklistoffset < blockkey.Length)
                                {
                                    var blkey = Convert.ToBase64String(m_blockhasher.ComputeHash(m_blocklistbuffer, 0, blocklistoffset));
                                    blocklisthashes.Add(blkey);
                                    AddBlockToOutput(blkey, m_blocklistbuffer, blocklistoffset, hint);
                                    blocklistoffset = 0;
                                }

                                Array.Copy(blockkey, 0, m_blocklistbuffer, blocklistoffset, blockkey.Length);
                                blocklistoffset += blockkey.Length;

                                var key = Convert.ToBase64String(blockkey);
                                AddBlockToOutput(key, m_blockbuffer, size, hint);
                                hashcollector.Add(key);
                                filesize += size;


                            } while (size == m_blockbuffer.Length);

                            //If all fits in a single block, don't bother with blocklists
                            if (hashcollector.Count > 1)
                            {
                                var blkeyfinal = Convert.ToBase64String(m_blockhasher.ComputeHash(m_blocklistbuffer, 0, blocklistoffset));
                                blocklisthashes.Add(blkeyfinal);
                                AddBlockToOutput(blkeyfinal, m_blocklistbuffer, blocklistoffset, CompressionHint.Noncompressible);
                            }
                        }

                        m_stat.SizeOfExaminedFiles += filesize;
                        m_filehasher.TransformFinalBlock(m_blockbuffer, 0, 0);

                        var filekey = Convert.ToBase64String(m_filehasher.Hash);
                        if (oldHash != filekey)
                        {
                            if (oldId < 0)
                            {
                                m_stat.AddedFiles++;
                                m_stat.SizeOfAddedFiles += filesize;
					            
					            if (m_options.FhDryrun)
					            	m_stat.LogMessage("[Dryrun] Would add new file {0}, size {1}", path, Utility.Utility.FormatSizeString(filesize));
                            }
                            else
                            {
                                m_stat.ModifiedFiles++;
                                m_stat.SizeOfModifiedFiles += filesize;
					            
					            if (m_options.FhDryrun)
					            	m_stat.LogMessage("[Dryrun] Would add changed file {0}, size {1}", path, Utility.Utility.FormatSizeString(filesize));
                            }

                            AddFileToOutput(path, filesize, scantime, metahashandsize, hashcollector, filekey, blocklisthashes);
                            changed = true;
                        }
                    }
                }

                if (!changed)
                    AddUnmodifiedFile(oldId, oldScanned);

            }
            catch (Exception ex)
            {
                m_stat.LogWarning(string.Format("Failed to process path: {0}", path), ex);
                m_stat.FilesWithError++;
            }

            if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                if (m_options.SymlinkPolicy == Options.SymlinkStrategy.Follow)
                    return true;
                else
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Adds the found file data to the output unless the block already exists
        /// </summary>
        /// <param name="key">The block hash</param>
        /// <param name="data">The data matching the hash</param>
        private bool AddBlockToOutput(string key, byte[] data, int len, CompressionHint hint)
        {
            if (m_database.AddBlock(key, len, m_blockvolume.VolumeID, m_transaction))
            {
                m_blockvolume.AddBlock(key, data, len, hint);
                if (m_shadowvolume != null)
                	m_shadowvolume.AddBlock(key, len);
                	
                if (m_blockvolume.Filesize > m_options.VolumeSize - m_options.Fhblocksize)
                {
                	if (m_options.FhDryrun)
                	{
                		m_stat.LogMessage("[Dryrun] Would upload block volume: {0}, size: {1}", m_blockvolume.RemoteFilename, Utility.Utility.FormatSizeString(new FileInfo(m_blockvolume.LocalFilename).Length));
                		m_blockvolume.Dispose();
                		m_blockvolume = null;
                		
                		if (m_shadowvolume != null)
                		{
                			m_shadowvolume.FinishVolume(Utility.Utility.CalculateHash(m_shadowvolume.LocalFilename), new FileInfo(m_shadowvolume.LocalFilename).Length);
                			m_stat.LogMessage("[Dryrun] Would upload shadow volume: {0}, size: {1}", m_shadowvolume.RemoteFilename, Utility.Utility.FormatSizeString(new FileInfo(m_shadowvolume.LocalFilename).Length));
                			m_shadowvolume.Dispose();
                			m_shadowvolume = null;
                		}
                	}
                	else
                	{
	                	//When uploading a new volume, we register the volumes and then flush the transaction
	                	// this ensures that the local database and remote storage are as closely related as possible
                		m_database.UpdateRemoteVolume(m_blockvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);
                		m_database.UpdateRemoteVolume(m_filesetvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);
                		if (m_shadowvolume != null)
	                		m_database.UpdateRemoteVolume(m_shadowvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);
	                	
	                	m_backend.FlushDbMessages(m_database, m_transaction);
        				m_backendLogFlushTimer = DateTime.Now.Add(FLUSH_TIMESPAN);

	                	m_transaction.Commit();
	                	m_transaction = m_database.BeginTransaction();
	                	
	                    m_backend.Put(m_blockvolume, m_shadowvolume);
	                    m_blockvolume = null;
	                    m_shadowvolume = null;
	                }
                    
                    m_blockvolume = new BlockVolumeWriter(m_options);
					m_blockvolume.VolumeID = m_database.RegisterRemoteVolume(m_blockvolume.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, m_transaction);
					
					if (!m_options.FhNoShadowfiles)
					{
	                    m_shadowvolume = new ShadowVolumeWriter(m_options);
						m_shadowvolume.VolumeID = m_database.RegisterRemoteVolume(m_shadowvolume.RemoteFilename, RemoteVolumeType.Shadow, RemoteVolumeState.Temporary, m_transaction);
						m_database.AddShadowBlockLink(m_shadowvolume.VolumeID, m_blockvolume.VolumeID, m_transaction);
						m_shadowvolume.StartVolume(m_blockvolume.RemoteFilename);
					}
                }

                return true;
            }

            return false;
        }

        private void AddUnmodifiedFile(long oldId, DateTime scantime)
        {
            m_database.AddUnmodifiedFile(oldId, scantime, m_transaction);
        }


        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        /// <param name="hashlist">The list of hashes that make up the file</param>
        /// <param name="size">The size of the file</param>
        /// <param name="fragmentoffset">The offset into a fragment block where the last few bytes are stored</param>
        /// <param name="metadata">A lookup table with various metadata values describing the file</param>
        private bool AddFolderToOutput(string filename, DateTime scantime, ForestHash.IMetahash meta)
        {
            long metadataid;
            bool r = false;

            //TODO: If meta.Size > blocksize...
            r |= AddBlockToOutput(meta.Hash, meta.Blob, (int)meta.Size, CompressionHint.Default);
            r |= m_database.AddMetadataset(meta.Hash, meta.Size, out metadataid, m_transaction);

            m_database.AddDirectoryEntry(filename, metadataid, scantime, m_transaction);
            return r;
        }

        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        /// <param name="hashlist">The list of hashes that make up the file</param>
        /// <param name="size">The size of the file</param>
        /// <param name="fragmentoffset">The offset into a fragment block where the last few bytes are stored</param>
        /// <param name="metadata">A lookup table with various metadata values describing the file</param>
        private bool AddSymlinkToOutput(string filename, DateTime scantime, ForestHash.IMetahash meta)
        {
            long metadataid;
            bool r = false;

            //TODO: If meta.Size > blocksize...
            r |= AddBlockToOutput(meta.Hash, meta.Blob, (int)meta.Size, CompressionHint.Default);
            r |= m_database.AddMetadataset(meta.Hash, meta.Size, out metadataid, m_transaction);

            m_database.AddSymlinkEntry(filename, metadataid, scantime, m_transaction);
            return r;
        }

        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        /// <param name="hashlist">The list of hashes that make up the file</param>
        /// <param name="size">The size of the file</param>
        /// <param name="fragmentoffset">The offset into a fragment block where the last few bytes are stored</param>
        /// <param name="metadata">A lookup table with various metadata values describing the file</param>
        private void AddFileToOutput(string filename, long size, DateTime scantime, ForestHash.IMetahash metadata, HashlistCollector hashlist, string filehash, IList<string> blocklisthashes)
        {
            long metadataid;
            long blocksetid;
            
            //TODO: If metadata.Size > blocksize...
            AddBlockToOutput(metadata.Hash, metadata.Blob, (int)metadata.Size, CompressionHint.Default);
            m_database.AddMetadataset(metadata.Hash, metadata.Size, out metadataid, m_transaction);

            m_database.AddBlockset(filehash, size, m_blockbuffer.Length, hashlist.Hashes, blocklisthashes, out blocksetid, m_transaction);

            //m_filesetvolume.AddFile(filename, filehash, size, scantime, metadata.Hash, metadata.Size, blocklisthashes);
            m_database.AddFile(filename, scantime, blocksetid, metadataid, m_transaction);
        }

        public void Dispose()
        {
            if (m_backend != null)
            {
                try { m_backend.Dispose(); }
                catch (Exception ex) { m_stat.LogError("Failed disposing backend", ex); }
                finally { m_backend = null; }
            }

            if (m_blockvolume != null)
            {
                try { m_blockvolume.Dispose(); }
                catch (Exception ex) { m_stat.LogError("Failed disposing block volume", ex); }
                finally { m_blockvolume = null; }
            }

            if (m_shadowvolume != null)
            {
                try { m_shadowvolume.Dispose(); }
                catch (Exception ex) { m_stat.LogError("Failed disposing shadow volume", ex); }
                finally { m_shadowvolume = null; }
            }

            m_stat.EndTime = DateTime.Now;
        }
    }
}
