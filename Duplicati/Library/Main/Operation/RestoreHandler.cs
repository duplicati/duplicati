using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;

namespace Duplicati.Library.Main.Operation
{
    internal class RestoreHandler
    {    
        private string m_backendurl;
        private Options m_options;
        private byte[] m_blockbuffer;
        private RestoreResults m_result;
        private static readonly Snapshots.ISystemIO m_systemIO = Duplicati.Library.Utility.Utility.IsClientLinux ? (Snapshots.ISystemIO)new Snapshots.SystemIOLinux() : (Snapshots.ISystemIO)new Snapshots.SystemIOWindows();
        private static readonly string DIRSEP = System.IO.Path.DirectorySeparatorChar.ToString();

        public RestoreHandler(string backendurl, Options options, RestoreResults result)
        {
            m_options = options;
            m_backendurl = backendurl;
            m_result = result;
        }

        /// <summary>
        /// Gets the compression module by parsing the filename
        /// </summary>
        /// <param name="filename">The filename to parse</param>
        /// <returns>The compression module</returns>
        public static string GetCompressionModule(string filename)
        {
            var tmp = VolumeBase.ParseFilename(filename);
            if (tmp == null)
                throw new Exception(string.Format("Unable to parse filename to valid entry: {0}", filename));

            return tmp.CompressionModule;
        }

        public static RecreateDatabaseHandler.NumberedFilterFilelistDelegate FilterNumberedFilelist(DateTime time, long[] versions, bool singleTimeMatch = false)
        {
            if (time.Kind == DateTimeKind.Unspecified)
                throw new Exception("Unspecified datetime instance, must be either local or UTC");

            // Make sure the resolution is the same (i.e. no milliseconds)
            if (time.Ticks > 0)
                time = Library.Utility.Utility.DeserializeDateTime(Library.Utility.Utility.SerializeDateTime(time)).ToUniversalTime();

            return 
                _lst =>
                {
                    // Unwrap, so we do not query the remote storage twice
                    var lst = (from n in _lst 
                                 where n.FileType == RemoteVolumeType.Files 
                                 orderby n.Time descending
                                 select n).ToArray();
                                                         
                    var numbers = lst.Zip(Enumerable.Range(0, lst.Length), (a, b) => new KeyValuePair<long, IParsedVolume>(b, a)).ToList();

                    if (time.Ticks > 0 && versions != null && versions.Length > 0)
                        return from n in numbers
                            where (singleTimeMatch ? n.Value.Time == time : n.Value.Time <= time) && versions.Contains(n.Key)
                            select n;
                    else if (time.Ticks > 0)
                        return from n in numbers
                            where (singleTimeMatch ? n.Value.Time == time : n.Value.Time <= time)
                            select n;
                    else if (versions != null && versions.Length > 0)
                        return from n in numbers
                            where versions.Contains(n.Key)
                            select n;
                    else
                        return numbers;
                };
        }

        public void Run(string[] paths, Library.Utility.IFilter filter = null)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_Begin);
                
            // If we have both target paths and a filter, combine into a single filter
            filter = Library.Utility.JoinedFilterExpression.Join(new Library.Utility.FilterExpression(paths), filter);
			
            if (!m_options.NoLocalDb && m_systemIO.FileExists(m_options.Dbpath))
            {
                using(var db = new LocalRestoreDatabase(m_options.Dbpath))
                {
                    db.SetResult(m_result);
                    DoRun(db, filter, m_result);
                    db.WriteResults();
                }
                    
                return;
            }
            
            
            m_result.AddMessage("No local database, building a temporary database");
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_RecreateDatabase);

            using(var tmpdb = new Library.Utility.TempFile())
            {
                RecreateDatabaseHandler.NumberedFilterFilelistDelegate filelistfilter = FilterNumberedFilelist(m_options.Time, m_options.Version);

                // Simultaneously with downloading blocklists, we patch as much as we can from the blockvolumes
                // This prevents repeated downloads, except for cases where the blocklists refer blocks
                // that have been previously handled. A local blockvolume cache can reduce this issue
                using(var database = new LocalRestoreDatabase(tmpdb))
                {
                    using(var metadatastorage = new RestoreHandlerMetadataStorage())
                    {
                        System.Security.Cryptography.HashAlgorithm blockhasher = null;
                        System.Security.Cryptography.HashAlgorithm filehasher = null;

                        bool first = true;
                        RecreateDatabaseHandler.BlockVolumePostProcessor localpatcher =
                            (key, rd) =>
                            {
                                if (first)
                                {
                                    Utility.UpdateOptionsFromDb(database, m_options);
                                    m_blockbuffer = new byte[m_options.Blocksize];

                                    //Figure out what files are to be patched, and what blocks are needed
                                    PrepareBlockAndFileList(database, m_options, filter, m_result);

                                    blockhasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.BlockHashAlgorithm);
                                    filehasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.FileHashAlgorithm);
                                    if (blockhasher == null)
                                        throw new Exception(Strings.Foresthash.InvalidHashAlgorithm(m_options.BlockHashAlgorithm));
                                    if (!blockhasher.CanReuseTransform)
                                        throw new Exception(Strings.Foresthash.InvalidCryptoSystem(m_options.BlockHashAlgorithm));

                                    if (filehasher == null)
                                        throw new Exception(Strings.Foresthash.InvalidHashAlgorithm(m_options.FileHashAlgorithm));
                                    if (!filehasher.CanReuseTransform)
                                        throw new Exception(Strings.Foresthash.InvalidCryptoSystem(m_options.FileHashAlgorithm));

                                    // Don't run this again
                                    first = false;
                                }
                                else
                                {
                                    // Patch the missing blocks list to include the newly discovered blocklists
                                    //UpdateMissingBlocksTable(key);
                                }

                                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                                    return;

                                CreateDirectoryStructure(database, m_options, m_result);

                                //If we are patching an existing target folder, do not touch stuff that is already updated
                                ScanForExistingTargetBlocks(database, m_blockbuffer, blockhasher, filehasher, m_options, m_result);

                                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                                    return;

                                // If other local files already have the blocks we want, we use them instead of downloading
                                if (!m_options.NoLocalBlocks)
                                    ScanForExistingSourceBlocks(database, m_options, m_blockbuffer, blockhasher, m_result, metadatastorage);
                                
                                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                                    return;
                            
                                //Update files with data
                                PatchWithBlocklist(database, rd, m_options, m_result, m_blockbuffer, metadatastorage);
                            };

                        // TODO: When UpdateMissingBlocksTable is implemented, the localpatcher can be activated
                        // and this will reduce the need for multiple downloads of the same volume
                        // TODO: This will need some work to preserve the missing block list for use with --fh-dryrun
                        m_result.RecreateDatabaseResults = new RecreateDatabaseResults(m_result);
                        using(new Logging.Timer("Recreate temporary database for restore"))
                            new RecreateDatabaseHandler(m_backendurl, m_options, (RecreateDatabaseResults)m_result.RecreateDatabaseResults)
                                .DoRun(database, false, filter, filelistfilter, /*localpatcher*/null);

                        if (!m_options.SkipMetadata)
                            ApplyStoredMetadata(database, m_options, m_result, metadatastorage);
                    }

                    //If we have --version set, we need to adjust, as the db has only the required versions
                    //TODO: Bit of a hack to set options that way
                    if (m_options.Version != null && m_options.Version.Length > 0)
                        m_options.RawOptions["version"] = string.Join(",", Enumerable.Range(0, m_options.Version.Length).Select(x => x.ToString()));

	                DoRun(database, filter, m_result);
                }
            }
        }
        
        private static void PatchWithBlocklist(LocalRestoreDatabase database, BlockVolumeReader blocks, Options options, RestoreResults result, byte[] blockbuffer, RestoreHandlerMetadataStorage metadatastorage)
        {
            var blocksize = options.Blocksize;
            var updateCounter = 0L;
            var fullblockverification = options.FullBlockVerification;
            var blockhasher = fullblockverification ? System.Security.Cryptography.HashAlgorithm.Create(options.BlockHashAlgorithm) : null;

            using (var blockmarker = database.CreateBlockMarker())
            using(var volumekeeper = database.GetMissingBlockData(blocks, options.Blocksize))
            {
                foreach(var restorelist in volumekeeper.FilesWithMissingBlocks)
                {
                    var targetpath = restorelist.Path;
                    result.AddVerboseMessage("Patching file with remote data: {0}", targetpath);
                    
                    if (options.Dryrun)
                    {
                        result.AddDryrunMessage(string.Format("Would patch file with remote data: {0}", targetpath));
                    }
                    else
                    {
                        try
                        {   
                            var folderpath = m_systemIO.PathGetDirectoryName(targetpath);
                            if (!options.Dryrun && !m_systemIO.DirectoryExists(folderpath))
                            {
                                result.AddWarning(string.Format("Creating missing folder {0} for  file {1}", folderpath, targetpath), null);
                                m_systemIO.DirectoryCreate(folderpath);
                            }
                            
                            // TODO: Much faster if we iterate the volume and checks what blocks are used,
                            // because the compressors usually like sequential reading
                            using(var file = m_systemIO.FileOpenReadWrite(targetpath))
                                foreach(var targetblock in restorelist.Blocks)
                                {
                                    file.Position = targetblock.Offset;
                                    var size = blocks.ReadBlock(targetblock.Key, blockbuffer);
                                    if (targetblock.Size == size)
                                    {
                                        var valid = !fullblockverification;
                                        if (!valid)
                                        {
                                            blockhasher.Initialize();
                                            var key = Convert.ToBase64String(blockhasher.ComputeHash(blockbuffer, 0, size));
                                            if (targetblock.Key == key)
                                                valid = true;
                                            else
                                                result.AddWarning(string.Format("Invalid block detected for {0}, expected hash: {1}, actual hash: {2}", targetpath, targetblock.Key, key), null);
                                        }

                                        if (valid)
                                        {
                                            file.Write(blockbuffer, 0, size);
                                            blockmarker.SetBlockRestored(restorelist.FileID, targetblock.Offset / blocksize, targetblock.Key, size, false);
                                        }
                                    } 
                                    else
                                    {
                                        result.AddWarning(string.Format("Block with hash {0} should have size {1} but has size {2}", targetblock.Key, targetblock.Size, size), null);
                                    }
                                }
                            
                            if ((++updateCounter) % 20 == 0)
                                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
                        }
                        catch (Exception ex)
                        {
                            result.AddWarning(string.Format("Failed to patch file: \"{0}\", message: {1}, message: {1}", targetpath, ex.Message), ex);
                        }
                    }
                }

                if (!options.SkipMetadata)
                {
                    foreach(var restoremetadata in volumekeeper.MetadataWithMissingBlocks)
                    {
                        var targetpath = restoremetadata.Path;
                        result.AddVerboseMessage("Recording metadata from remote data: {0}", targetpath);
                    
                        try
                        {
                            // TODO: When we support multi-block metadata this needs to deal with it
                            using(var ms = new System.IO.MemoryStream())
                            {
                                foreach(var targetblock in restoremetadata.Blocks)
                                {
                                    ms.Position = targetblock.Offset;
                                    var size = blocks.ReadBlock(targetblock.Key, blockbuffer);
                                    if (targetblock.Size == size)
                                    {
                                        ms.Write(blockbuffer, 0, size);
                                        blockmarker.SetBlockRestored(restoremetadata.FileID, targetblock.Offset / blocksize, targetblock.Key, size, true);
                                    }   
                                }

                                ms.Position = 0; 
                                metadatastorage.Add(targetpath, ms);
                                //blockmarker.RecordMetadata(restoremetadata.FileID, ms);
                            }
                        }
                        catch (Exception ex)
                        {
                            result.AddWarning(string.Format("Failed to record metadata for file: \"{0}\", message: {1}", targetpath, ex.Message), ex);
                        }
                    }
                }
                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
                blockmarker.Commit(result);
            }
        }

        private static void ApplyStoredMetadata(LocalRestoreDatabase database, Options options, RestoreResults result, RestoreHandlerMetadataStorage metadatastorage)
        {
            foreach(var metainfo in metadatastorage.Records)
            {
                var targetpath = metainfo.Key;
                result.AddVerboseMessage("Patching metadata with remote data: {0}", targetpath);

                if (options.Dryrun)
                {
                    result.AddDryrunMessage(string.Format("Would patch metadata with remote data: {0}", targetpath));
                }
                else
                {               
                    try
                    {
                        var folderpath = m_systemIO.PathGetDirectoryName(targetpath);
                        if (!options.Dryrun && !m_systemIO.DirectoryExists(folderpath))
                        {
                            result.AddWarning(string.Format("Creating missing folder {0} for target {1}", folderpath, targetpath), null);
                            m_systemIO.DirectoryCreate(folderpath);
                        }

                        ApplyMetadata(targetpath, metainfo.Value, options.RestorePermissions);
                    }
                    catch (Exception ex)
                    {
                        result.AddWarning(string.Format("Failed to apply metadata to file: \"{0}\", message: {1}", targetpath, ex.Message), ex);
                    }
                }
            }
        }

        private void DoRun(LocalDatabase dbparent, Library.Utility.IFilter filter, RestoreResults result)
        {
            //In this case, we check that the remote storage fits with the database.
            //We can then query the database and find the blocks that we need to do the restore
            using(var database = new LocalRestoreDatabase(dbparent))
            using(var backend = new BackendManager(m_backendurl, m_options, result.BackendWriter, database))
            using(var metadatastorage = new RestoreHandlerMetadataStorage())
            {
                database.SetResult(m_result);
                Utility.UpdateOptionsFromDb(database, m_options);
                Utility.VerifyParameters(database, m_options);
                m_blockbuffer = new byte[m_options.Blocksize];
	        	
                var blockhasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.BlockHashAlgorithm);
                var filehasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.FileHashAlgorithm);
                if (blockhasher == null)
                    throw new Exception(Strings.Foresthash.InvalidHashAlgorithm(m_options.BlockHashAlgorithm));
                if (!blockhasher.CanReuseTransform)
                    throw new Exception(Strings.Foresthash.InvalidCryptoSystem(m_options.BlockHashAlgorithm));

                if (filehasher == null)
                    throw new Exception(Strings.Foresthash.InvalidHashAlgorithm(m_options.FileHashAlgorithm));
                if (!filehasher.CanReuseTransform)
                    throw new Exception(Strings.Foresthash.InvalidCryptoSystem(m_options.FileHashAlgorithm));

                if (!m_options.NoBackendverification)
                {
                    m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_PreRestoreVerify);                
                    FilelistProcessor.VerifyRemoteList(backend, m_options, database, result.BackendWriter);
                }

                //Figure out what files are to be patched, and what blocks are needed
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_CreateFileList);
                using(new Logging.Timer("PrepareBlockList"))
                    PrepareBlockAndFileList(database, m_options, filter, result);

                //Make the entire output setup
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_CreateTargetFolders);
                using(new Logging.Timer("CreateDirectory"))
                    CreateDirectoryStructure(database, m_options, result);
                
                //If we are patching an existing target folder, do not touch stuff that is already updated
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_ScanForExistingFiles);
                using(new Logging.Timer("ScanForexistingTargetBlocks"))
                    ScanForExistingTargetBlocks(database, m_blockbuffer, blockhasher, filehasher, m_options, result);

                //Look for existing blocks in the original source files only
                using(new Logging.Timer("ScanForExistingSourceBlocksFast"))
                    if (!m_options.NoLocalBlocks && !string.IsNullOrEmpty(m_options.Restorepath))
                    {
                        m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_ScanForLocalBlocks);
                        ScanForExistingSourceBlocksFast(database, m_options, m_blockbuffer, blockhasher, result);
                    }

                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                {
                    backend.WaitForComplete(database, null);
                    return;
                }

                // If other local files already have the blocks we want, we use them instead of downloading
                if (m_options.PatchWithLocalBlocks)
                {
                    m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_PatchWithLocalBlocks);
                    using(new Logging.Timer("PatchWithLocalBlocks"))
                        ScanForExistingSourceBlocks(database, m_options, m_blockbuffer, blockhasher, result, metadatastorage);
                }

                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                {
                    backend.WaitForComplete(database, null);
                    return;
                }
                
                // Fill BLOCKS with remote sources
                List<IRemoteVolume> volumes;
                using (new Logging.Timer("GetMissingVolumes"))
                    volumes = database.GetMissingVolumes().ToList();

                if (volumes.Count > 0)
                {
                    m_result.AddMessage(string.Format("{0} remote files are required to restore", volumes.Count));
                    m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_DownloadingRemoteFiles);
                }

                var brokenFiles = new List<string>();
				foreach(var blockvolume in new AsyncDownloader(volumes, backend))
					try
					{
                        if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                        {
                            backend.WaitForComplete(database, null);
                            return;
                        }
                    
						using(var tmpfile = blockvolume.TempFile)
						using(var blocks = new BlockVolumeReader(GetCompressionModule(blockvolume.Name), tmpfile, m_options))
                            PatchWithBlocklist(database, blocks, m_options, result, m_blockbuffer, metadatastorage);
					}
					catch (Exception ex)
					{
                        brokenFiles.Add(blockvolume.Name);
                        result.AddError(string.Format("Failed to patch with remote file: \"{0}\", message: {1}", blockvolume.Name, ex.Message), ex);
                        if (ex is System.Threading.ThreadAbortException)
                            throw;
					}

                // Enforce the length of restored files
                // This might be integrated in block restore process when verification is triggered per file on complete
                using (new Logging.Timer("Adjusting final file lengths"))
                    foreach (var file in database.GetFilesToRestore())
                    {
                        try
                        {
                            if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                            {
                                backend.WaitForComplete(database, null);
                                return;
                            }

                            // Fix the length
                            using (var fs = m_systemIO.FileOpenWrite(file.Path))
                                fs.SetLength(file.Length);
                        }
                        catch (Exception ex)
                        {
                            result.AddWarning(ex.Message, ex);
                            if (ex is System.Threading.ThreadAbortException)
                                throw;
                        }
                    }

                // Apply metadata
                if (!m_options.SkipMetadata)
                    ApplyStoredMetadata(database, m_options, m_result, metadatastorage);
                
                // Reset the filehasher if it was used to verify existing files
                filehasher.Initialize();
					
                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                    return;
                
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_PostRestoreVerify);
                
                var fileErrors = 0L;

                if (m_options.PerformRestoredFileVerification)
                {
                    // After all blocks in the files are restored, verify the file hash
                    using(new Logging.Timer("RestoreVerification"))
                        foreach(var file in database.GetFilesToRestore())
                        {
                            try
                            {
                                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                                {
                                    backend.WaitForComplete(database, null);
                                    return;
                                }
                            
                                result.AddVerboseMessage("Testing restored file integrity: {0}", file.Path);
                            
                                string key;
                                long size;
                                using(var fs = m_systemIO.FileOpenRead(file.Path))
                                {
                                    size = fs.Length;
                                    key = Convert.ToBase64String(filehasher.ComputeHash(fs));
                                }
    
                                if (key != file.Hash)
                                    throw new Exception(string.Format("Failed to restore file: \"{0}\". File hash is {1}, expected hash is {2}", file.Path, key, file.Hash));
                                result.FilesRestored++;
                                result.SizeOfRestoredFiles += size;
                            }
                            catch (Exception ex)
                            {
                                fileErrors++;
                                result.AddWarning(ex.Message, ex);
                                if (ex is System.Threading.ThreadAbortException)
                                    throw;
                            }
                        }
                }
                    
                if (fileErrors > 0 && brokenFiles.Count > 0)
                    m_result.AddMessage(string.Format("Failed to restore {0} files, additionally the following files failed to download, which may be the cause:{1}", fileErrors, Environment.NewLine, string.Join(Environment.NewLine, brokenFiles)));

                // Drop the temp tables
                database.DropRestoreTable();
                backend.WaitForComplete(database, null);
            }
            
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_Complete);
            result.EndTime = DateTime.UtcNow;
        }

        private static void ApplyMetadata(string path, System.IO.Stream stream, bool restorePermissions)
        {
            using(var tr = new System.IO.StreamReader(stream))
            using(var jr = new Newtonsoft.Json.JsonTextReader(tr))
            {
                var metadata = new Newtonsoft.Json.JsonSerializer().Deserialize<Dictionary<string, string>>(jr);
                string k;
                long t;
                System.IO.FileAttributes fa;

                var isDirTarget = path.EndsWith(DIRSEP);
                var targetpath = isDirTarget ? path.Substring(0, path.Length - 1) : path;

                // Make the symlink first, otherwise we cannot apply metadata to it
                if (metadata.TryGetValue("CoreSymlinkTarget", out k))
                    m_systemIO.CreateSymlink(targetpath, k, isDirTarget);

                if (metadata.TryGetValue("CoreLastWritetime", out k) && long.TryParse(k, out t))
                {
                    if (isDirTarget)
                        m_systemIO.DirectorySetLastWriteTimeUtc(targetpath, new DateTime(t, DateTimeKind.Utc));
                    else
                        m_systemIO.FileSetLastWriteTimeUtc(targetpath, new DateTime(t, DateTimeKind.Utc));
                }

                if (metadata.TryGetValue("CoreCreatetime", out k) && long.TryParse(k, out t))
                {
                    if (isDirTarget)
                        m_systemIO.DirectorySetCreationTimeUtc(targetpath, new DateTime(t, DateTimeKind.Utc));
                    else
                        m_systemIO.FileSetCreationTimeUtc(targetpath, new DateTime(t, DateTimeKind.Utc));
                }

                if (metadata.TryGetValue("CoreAttributes", out k) && Enum.TryParse(k, true, out fa))
                    m_systemIO.SetFileAttributes(targetpath, fa);

                m_systemIO.SetMetadata(path, metadata, restorePermissions);
            }
        }

        private static void ScanForExistingSourceBlocksFast(LocalRestoreDatabase database, Options options, byte[] blockbuffer, System.Security.Cryptography.HashAlgorithm hasher, RestoreResults result)
        {
            // Fill BLOCKS with data from known local source files
            using (var blockmarker = database.CreateBlockMarker())
            {
                var updateCount = 0L;
                foreach(var entry in database.GetFilesAndSourceBlocksFast(options.Blocksize))
            	{
                    var targetpath = entry.TargetPath;
                    var targetfileid = entry.TargetFileID;
                    var sourcepath = entry.SourcePath;
                    var patched = false;
                    
                	try
                	{
        				if (m_systemIO.FileExists(sourcepath))
        				{
	    					var folderpath = m_systemIO.PathGetDirectoryName(targetpath);
	    					if (!options.Dryrun && !m_systemIO.DirectoryExists(folderpath))
	    					{
	                            result.AddWarning(string.Format("Creating missing folder {0} for  file {1}", folderpath, targetpath), null);
	    						m_systemIO.DirectoryCreate(folderpath);
	    					}
        				
	                		using(var targetstream = options.Dryrun ? null : m_systemIO.FileCreate(targetpath))
	                		{
	                			try
	                			{
		                    		using(var sourcestream = m_systemIO.FileOpenRead(sourcepath))
		                    		{
		    			                foreach(var block in entry.Blocks)
		    			                {
                                            if (result.TaskControlRendevouz() == TaskControlState.Stop)
                                                return;

                                            //TODO: Handle metadata

			    			                if (sourcestream.Length > block.Offset)
				                    		{
				                    			sourcestream.Position = block.Offset;
				                    			
		                                        var size = sourcestream.Read(blockbuffer, 0, blockbuffer.Length);
		                                        if (size == block.Size)
		                                        {
		                                            var key = Convert.ToBase64String(hasher.ComputeHash(blockbuffer, 0, size));
		                                            if (key == block.Hash)
		                                            {
                                                        patched = true;
						                            	if (!options.Dryrun)
					                            		{
					                            			targetstream.Position = block.Offset;
		                                                    targetstream.Write(blockbuffer, 0, size);
		                                                }
		                                                    
		                                                blockmarker.SetBlockRestored(targetfileid, block.Index, key, block.Size, false);
		                                            }
		                                        }	                    		
											}
										}
									}
	                			}
	                			catch (Exception ex)
	                			{
	                                result.AddWarning(string.Format("Failed to patch file: \"{0}\" with data from local file \"{1}\", message: {2}", targetpath, sourcepath, ex.Message), ex);
                                    if (ex is System.Threading.ThreadAbortException)
                                        throw;
	                			}
	                		}

                            if ((++updateCount) % 20 == 0)
                            {
                                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
                                if (result.TaskControlRendevouz() == TaskControlState.Stop)
                                    return;
                            }
                            
	                	}
                        else
                        {
                            result.AddVerboseMessage("Local source file not found: {0}", sourcepath);
                        }
	 				}
                    catch (Exception ex)
                    {
                        result.AddWarning(string.Format("Failed to patch file: \"{0}\" with local data, message: {1}", targetpath, ex.Message), ex);
                        if (ex is System.Threading.ThreadAbortException)
                            throw;
                    }
                    
                    if (patched)
                        result.AddVerboseMessage("Target file is patched with some local data: {0}", targetpath);
                    else
                        result.AddVerboseMessage("Target file is not patched any local data: {0}", targetpath);
                    
                    if (patched && options.Dryrun)
                    	result.AddDryrunMessage(string.Format("Would patch file with local data: {0}", targetpath));
            	}
            	
                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
            	blockmarker.Commit(result);
            }
        }

        private static void ScanForExistingSourceBlocks(LocalRestoreDatabase database, Options options, byte[] blockbuffer, System.Security.Cryptography.HashAlgorithm hasher, RestoreResults result, RestoreHandlerMetadataStorage metadatastorage)
        {
            // Fill BLOCKS with data from known local source files
            using (var blockmarker = database.CreateBlockMarker())
            {
                var updateCount = 0L;
                foreach (var restorelist in database.GetFilesAndSourceBlocks(options.SkipMetadata, options.Blocksize))
                {
                    var targetpath = restorelist.TargetPath;
                    var targetfileid = restorelist.TargetFileID;
                    var patched = false;
                    try
                    {
                        if (result.TaskControlRendevouz() == TaskControlState.Stop)
                            return;
                        
    					var folderpath = m_systemIO.PathGetDirectoryName(targetpath);
    					if (!options.Dryrun && !m_systemIO.DirectoryExists(folderpath))
    					{
                            result.AddWarning(string.Format("Creating missing folder {0} for  file {1}", folderpath, targetpath), null);
    						m_systemIO.DirectoryCreate(folderpath);
    					}
                    
                        using (var file = options.Dryrun ? null : m_systemIO.FileOpenReadWrite(targetpath))
                        using (var block = new Blockprocessor(file, blockbuffer))
                            foreach (var targetblock in restorelist.Blocks)
                            {
                                if (!options.Dryrun && !targetblock.IsMetadata)
                                	file.Position = targetblock.Offset;
                                	
                                foreach (var source in targetblock.Blocksources)
                                {
                                    try
                                    {
                                        if (result.TaskControlRendevouz() == TaskControlState.Stop)
                                            return;

                                        if (m_systemIO.FileExists(source.Path))
                                        {
                                            if (source.IsMetadata)
                                            {
                                                // TODO: Handle this by reconstructing 
                                                // metadata from file and checking the hash

                                                continue;
                                            }
                                            else
                                            {
                                                using (var sourcefile = m_systemIO.FileOpenRead(source.Path))
                                                {
                                                    sourcefile.Position = source.Offset;
                                                    var size = sourcefile.Read(blockbuffer, 0, blockbuffer.Length);
                                                    if (size == targetblock.Size)
                                                    {
                                                        var key = Convert.ToBase64String(hasher.ComputeHash(blockbuffer, 0, size));
                                                        if (key == targetblock.Hash)
                                                        {
    						                            	if (!options.Dryrun)
                                                            {
                                                                if (targetblock.IsMetadata)
                                                                    metadatastorage.Add(targetpath, new System.IO.MemoryStream(blockbuffer, 0, size));
                                                                else
    	                                                            file.Write(blockbuffer, 0, size);
                                                            }
    	                                                        
                                                            blockmarker.SetBlockRestored(targetfileid, targetblock.Index, key, targetblock.Size, false);
                                                            patched = true;
                                                            break;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        result.AddWarning(string.Format("Failed to patch file: \"{0}\" with data from local file \"{1}\", message: {2}", targetpath, source.Path, ex.Message), ex);
                                        if (ex is System.Threading.ThreadAbortException)
                                            throw;
                                    }
                                }
                            }
                            
                            if ((++updateCount) % 20 == 0)
                                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
                    }
                    catch (Exception ex)
                    {
                        result.AddWarning(string.Format("Failed to patch file: \"{0}\" with local data, message: {1}", targetpath, ex.Message), ex);
                    }
                    
                    if (patched)
                        result.AddVerboseMessage("Target file is patched with some local data: {0}", targetpath);
                    else
                        result.AddVerboseMessage("Target file is not patched any local data: {0}", targetpath);
                        
                    if (patched && options.Dryrun)
                    	result.AddDryrunMessage(string.Format("Would patch file with local data: {0}", targetpath));
                }

                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
                blockmarker.Commit(result);
            }
        }

        private static void PrepareBlockAndFileList(LocalRestoreDatabase database, Options options, Library.Utility.IFilter filter, RestoreResults result)
        {
            // Create a temporary table FILES by selecting the files from fileset that matches a specific operation id
            // Delete all entries from the temp table that are excluded by the filter(s)
            using(new Logging.Timer("PrepareRestoreFileList"))
            {
                var c = database.PrepareRestoreFilelist(options.Time, options.Version, filter, result);
                result.OperationProgressUpdater.UpdatefileCount(c.Item1, c.Item2, true);
            }

			using(new Logging.Timer("SetTargetPaths"))
    			if (!string.IsNullOrEmpty(options.Restorepath))
    			{
    				// Find the largest common prefix
    				string largest_prefix = database.GetLargestPrefix();
                    result.AddVerboseMessage("Mapping restore path prefix to \"{0}\" to \"{1}\"", largest_prefix, Library.Utility.Utility.AppendDirSeparator(options.Restorepath));
    
    				// Set the target paths, special care with C:\ and /
    				database.SetTargetPaths(largest_prefix, Library.Utility.Utility.AppendDirSeparator(options.Restorepath));
    			}
    			else
    			{
    				database.SetTargetPaths("", "");
    			}

            // Create a temporary table BLOCKS that lists all blocks that needs to be recovered
			using(new Logging.Timer("FindMissingBlocks"))
                database.FindMissingBlocks(result, options.SkipMetadata);

            // Create temporary tables and triggers that automatically track progress
            using (new Logging.Timer("CreateProgressTracker"))
                database.CreateProgressTracker(false);

        }

        private static void CreateDirectoryStructure(LocalRestoreDatabase database, Options options, RestoreResults result)
		{
			// This part is not protected by try/catch as we need the target folder to exist
			if (!string.IsNullOrEmpty(options.Restorepath))
                if (!m_systemIO.DirectoryExists(options.Restorepath))
                {
                    if (options.Verbose)
                        result.AddVerboseMessage("Creating folder: {0}", options.Restorepath);
                        
                	if (options.Dryrun)
                		result.AddDryrunMessage(string.Format("Would create folder: {0}", options.Restorepath));
                	else
                    	m_systemIO.DirectoryCreate(options.Restorepath);
                }
        
            foreach (var folder in database.GetTargetFolders())
            {
                try
                {
                    if (result.TaskControlRendevouz() == TaskControlState.Stop)
                        return;
                    
                    if (!m_systemIO.DirectoryExists(folder))
                    {
                    	result.FoldersRestored++;
                    	
                        if (options.Verbose)
                            result.AddVerboseMessage("Creating folder: {0}", folder);
                            
                    	if (options.Dryrun)
                    		result.AddDryrunMessage(string.Format("Would create folder: {0}", folder));
                    	else
                        	m_systemIO.DirectoryCreate(folder);
                    }
                }
                catch (Exception ex)
                {
                    result.AddWarning(string.Format("Failed to create folder: \"{0}\", message: {1}", folder, ex.Message), ex);
                }
            }
        }

        private static void ScanForExistingTargetBlocks(LocalRestoreDatabase database, byte[] blockbuffer, System.Security.Cryptography.HashAlgorithm blockhasher, System.Security.Cryptography.HashAlgorithm filehasher, Options options, RestoreResults result)
        {
            // Scan existing files for existing BLOCKS
            using(var blockmarker = database.CreateBlockMarker())
            {
                var updateCount = 0L;
                foreach(var restorelist in database.GetExistingFilesWithBlocks())
                {
                    var rename = !options.Overwrite;
                    var targetpath = restorelist.TargetPath;
                    var targetfileid = restorelist.TargetFileID;
                    var targetfilehash = restorelist.TargetHash;
                    if (m_systemIO.FileExists(targetpath))
                    {
                        try
                        {
                            if (result.TaskControlRendevouz() == TaskControlState.Stop)
                                return;
                            
                            if (rename)
                                filehasher.Initialize();

                            using(var file = m_systemIO.FileOpenReadWrite(targetpath))
                            using(var block = new Blockprocessor(file, blockbuffer))
                                foreach(var targetblock in restorelist.Blocks)
                                {
                                    var size = block.Readblock();
                                    if (size <= 0)
                                        break;
    
                                    //TODO: Handle Metadata

                                    if (size == targetblock.Size)
                                    {
                                        var key = Convert.ToBase64String(blockhasher.ComputeHash(blockbuffer, 0, size));
                                        if (key == targetblock.Hash)
                                        {
                                            blockmarker.SetBlockRestored(targetfileid, targetblock.Index, key, size, false);
                                        }
                                    }
                                    
                                    if (rename)
                                        filehasher.TransformBlock(blockbuffer, 0, size, blockbuffer, 0);
                                }
                                
                            if (rename)
                            {
                                filehasher.TransformFinalBlock(blockbuffer, 0, 0);
                                var filekey = Convert.ToBase64String(filehasher.Hash);
                                if (filekey == targetfilehash)
                                {
                                    //TODO: Check metadata to trigger rename? If metadata changed, it will still be restored for the file in-place.
                                    result.AddVerboseMessage("Target file exists and is correct version: {0}", targetpath);
                                    rename = false;
                                }
                                else
                                {
                                    // The new file will have none of the correct blocks,
                                    // even if the scanned file had some
                                    blockmarker.SetAllBlocksMissing(targetfileid);
                                }
                            }
                            
                            if ((++updateCount) % 20 == 0)
                            {
                                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
                                if (result.TaskControlRendevouz() == TaskControlState.Stop)
                                    return;
                            }
                        }
                        catch (Exception ex)
                        {
                            result.AddWarning(string.Format("Failed to read target file: \"{0}\", message: {1}", targetpath, ex.Message), ex);
                            if (ex is System.Threading.ThreadAbortException)
                                throw;
                        }                        
                    }
                    else
                    {
                        result.AddVerboseMessage("Target file does not exist: {0}", targetpath);
                        rename = false;
                    }
                    
                    if (rename)
                    {
                        //Select a new filename
                        var ext = m_systemIO.PathGetExtension(targetpath) ?? "";
                        if (!string.IsNullOrEmpty(ext) && !ext.StartsWith("."))
                            ext = "." + ext;
                        
                        // First we try with a simple date append, assuming that there are not many conflicts there
                        var newname = m_systemIO.PathChangeExtension(targetpath, null) + "." + database.RestoreTime.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                        var tr = newname + ext;
                        var c = 0;
                        while (m_systemIO.FileExists(tr) && c < 1000)
                        {
                            try
                            {
                                // If we have a file with the correct name, 
                                // it is most likely the file we want
                                filehasher.Initialize();
                                
                                string key;
                                using(var file = m_systemIO.FileOpenReadWrite(tr))
                                    key = Convert.ToBase64String(filehasher.ComputeHash(file));
                                    
                                if (key == targetfilehash)
                                {
                                    //TODO: Also needs metadata check to make correct decision.
                                    //      We stick to the policy to restore metadata in place, if data ok. So, metadata block may be restored.
                                    blockmarker.SetAllBlocksRestored(targetfileid, false);
                                    break;
                                }
                            }
                            catch(Exception ex)
                            {
                                result.AddWarning(string.Format("Failed to read candidate restore target {0}", tr), ex);
                            }
                            tr = newname + " (" + (c++).ToString() + ")" + ext;
                        }
                        
                        newname = tr;
                        
                        result.AddVerboseMessage("Target file exists and will be restored to: {0}", newname);
                        database.UpdateTargetPath(targetfileid, newname); 
                    }                        
                    
                }

                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
                blockmarker.Commit(result);
            }
        }
    }
}
