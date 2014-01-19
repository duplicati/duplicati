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

        public RestoreHandler(string backendurl, Options options, RestoreResults result)
        {
            m_options = options;
            m_backendurl = backendurl;
            m_result = result;

            m_blockbuffer = new byte[m_options.Blocksize];

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

        public static RecreateDatabaseHandler.NumberedFilterFilelistDelegate FilterNumberedFilelist(DateTime time, long[] versions)
        {
            if (time.Kind == DateTimeKind.Unspecified)
                throw new Exception("Unspecified datetime instance, must be either local or UTC");

            // Make sure the resolution is the same (i.e. no milliseconds)
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
                            where n.Value.Time <= time && versions.Contains(n.Key)
                            select n;
                    else if (time.Ticks > 0)
                        return from n in numbers
                            where n.Value.Time <= time
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
			
            if (!m_options.NoLocalDb && System.IO.File.Exists(m_options.Dbpath))
            {
                using(var db = new LocalRestoreDatabase(m_options.Dbpath, m_options.Blocksize))
                {
                    db.SetResult(m_result);
                    DoRun(db, filter, m_result);
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
                using(var database = new LocalRestoreDatabase(tmpdb, m_options.Blocksize))
                {
                    var blockhasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.BlockHashAlgorithm);
                    var filehasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.FileHashAlgorithm);
                    if (blockhasher == null)
                        throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, m_options.BlockHashAlgorithm));
                    if (!blockhasher.CanReuseTransform)
                        throw new Exception(string.Format(Strings.Foresthash.InvalidCryptoSystem, m_options.BlockHashAlgorithm));
    
                    if (filehasher == null)
                        throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, m_options.FileHashAlgorithm));
                    if (!filehasher.CanReuseTransform)
                        throw new Exception(string.Format(Strings.Foresthash.InvalidCryptoSystem, m_options.FileHashAlgorithm));
    
                    bool first = true;
                    RecreateDatabaseHandler.BlockVolumePostProcessor localpatcher =
                        (key, rd) =>
                        {
                            if (first)
                            {
                                //Figure out what files are to be patched, and what blocks are needed
                                PrepareBlockAndFileList(database, m_options, filter, m_result);

                                // Don't run this again
                                first = false;
                            }
                            else
                            {
                                // Patch the missing blocks list to include the newly discovered blocklists
                                //UpdateMissingBlocksTable(key);
                            }

                            CreateDirectoryStructure(database, m_options, m_result);

                            //If we are patching an existing target folder, do not touch stuff that is already updated
                            ScanForExistingTargetBlocks(database, m_blockbuffer, blockhasher, filehasher, m_options, m_result);

#if DEBUG
                            if (!m_options.NoLocalBlocks)
#endif
                            // If other local files already have the blocks we want, we use them instead of downloading
                            ScanForExistingSourceBlocks(database, m_options, m_blockbuffer, blockhasher, m_result);
                            
                            //Update files with data
                            PatchWithBlocklist(database, rd, m_options, m_result, m_blockbuffer);
                        };

                    // TODO: When UpdateMissingBlocksTable is implemented, the localpatcher can be activated
                    // and this will reduce the need for multiple downloads of the same volume
                    // TODO: This will need some work to preserve the missing block list for use with --fh-dryrun
                    m_result.RecreateDatabaseResults = new RecreateDatabaseResults(m_result);
                    using(new Logging.Timer("Recreate temporary database for restore"))
                        new RecreateDatabaseHandler(m_backendurl, m_options, (RecreateDatabaseResults)m_result.RecreateDatabaseResults)
                            .DoRun(database, filter, filelistfilter, /*localpatcher*/null);

                    //If we have --version set, we need to adjust, as the db has only the required versions
                    //TODO: Bit of a hack to set options that way
                    if (m_options.Version != null && m_options.Version.Length > 0)
                        m_options.RawOptions["version"] = string.Join(",", Enumerable.Range(0, m_options.Version.Length).Select(x => x.ToString()));

	                DoRun(database, filter, m_result);
                }
            }
        }
        
        private static void PatchWithBlocklist(LocalRestoreDatabase database, BlockVolumeReader blocks, Options options, RestoreResults result, byte[] blockbuffer)
        {
            var blocksize = options.Blocksize;
            var updateCounter = 0L;
            using(var blockmarker = database.CreateBlockMarker())
            {
                foreach(var restorelist in database.GetFilesWithMissingBlocks(blocks))
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
                            var folderpath = System.IO.Path.GetDirectoryName(targetpath);
                            if (!options.Dryrun && !System.IO.Directory.Exists(folderpath))
                            {
                                result.AddWarning(string.Format("Creating missing folder {0} for  file {1}", folderpath, targetpath), null);
                                System.IO.Directory.CreateDirectory(folderpath);
                            }
                            
                            // TODO: Much faster if we iterate the volume and checks what blocks are used,
                            // because the compressors usually like sequential reading
                            using(var file = System.IO.File.Open(targetpath, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                                foreach(var targetblock in restorelist.Blocks)
                                {
                                    file.Position = targetblock.Offset;
                                    var size = blocks.ReadBlock(targetblock.Key, blockbuffer);
                                    if (targetblock.Size == size)
                                    {
                                        file.Write(blockbuffer, 0, size);
                                        blockmarker.SetBlockRestored(restorelist.FileID, targetblock.Offset / blocksize, targetblock.Key, size);
                                    }   
                                    
                                }
                            
                            if (updateCounter++ % 20 == 0)
                                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
                        }
                        catch (Exception ex)
                        {
                            result.AddWarning(string.Format("Failed to patch file: \"{0}\", message: {1}, message: {1}", targetpath, ex.Message), ex);
                        }
    	                
                        try
                        {
                            ApplyMetadata(targetpath, database);
                        }
                        catch (Exception ex)
                        {
                            result.AddWarning(string.Format("Failed to apply metadata to file: \"{0}\", message: {1}", targetpath, ex.Message), ex);
                        }
                    }
                }
                
                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
                blockmarker.Commit(result);
            }
        }

        private void DoRun(LocalDatabase dbparent, Library.Utility.IFilter filter, RestoreResults result)
        {
            //In this case, we check that the remote storage fits with the database.
            //We can then query the database and find the blocks that we need to do the restore
            using(var database = new LocalRestoreDatabase(dbparent, m_options.Blocksize))
            using(var backend = new BackendManager(m_backendurl, m_options, result.BackendWriter, database))
            {
                database.SetResult(m_result);
                Utility.VerifyParameters(database, m_options);
	        	
                var blockhasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.BlockHashAlgorithm);
                var filehasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.FileHashAlgorithm);
                if (blockhasher == null)
                    throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, m_options.BlockHashAlgorithm));
                if (!blockhasher.CanReuseTransform)
                    throw new Exception(string.Format(Strings.Foresthash.InvalidCryptoSystem, m_options.BlockHashAlgorithm));

                if (filehasher == null)
                    throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, m_options.FileHashAlgorithm));
                if (!filehasher.CanReuseTransform)
                    throw new Exception(string.Format(Strings.Foresthash.InvalidCryptoSystem, m_options.FileHashAlgorithm));

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
#if DEBUG
                    if (!m_options.NoLocalBlocks && !string.IsNullOrEmpty(m_options.Restorepath))
#else
				    if (!string.IsNullOrEmpty(m_options.Restorepath))
#endif
                    {
                        m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_ScanForLocalBlocks);
                        ScanForExistingSourceBlocksFast(database, m_options, m_blockbuffer, blockhasher, result);
                    }

                // If other local files already have the blocks we want, we use them instead of downloading
                if (m_options.PatchWithLocalBlocks)
                {
                    m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_PatchWithLocalBlocks);
                    using(new Logging.Timer("PatchWithLocalBlocks"))
                        ScanForExistingSourceBlocks(database, m_options, m_blockbuffer, blockhasher, result);
                }

                // Fill BLOCKS with remote sources
                var volumes = database.GetMissingVolumes().ToList();

                if (volumes.Count > 0)
                {
                    m_result.AddMessage(string.Format("{0} remote files are required to restore", volumes.Count));
                    m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_DownloadingRemoteFiles);
                }

                var brokenFiles = new List<string>();
				foreach(var blockvolume in new AsyncDownloader(volumes, backend))
					try
					{
						using(var tmpfile = blockvolume.TempFile)
						using(var blocks = new BlockVolumeReader(GetCompressionModule(blockvolume.Name), tmpfile, m_options))
							PatchWithBlocklist(database, blocks, m_options, result, m_blockbuffer);
					}
					catch (Exception ex)
					{
                        brokenFiles.Add(blockvolume.Name);
                        result.AddError(string.Format("Failed to patch with remote file: \"{0}\", message: {1}", blockvolume.Name, ex.Message), ex);
					}
                
                // Reset the filehasher if it was used to verify existing files
                filehasher.Initialize();
					
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_PostRestoreVerify);
                
                var fileErrors = 0L;
                // After all blocks in the files are restored, verify the file hash
                using(new Logging.Timer("RestoreVerification"))
                    foreach (var file in database.GetFilesToRestore())
                    {
                        try
                        {
                            result.AddVerboseMessage("Testing restored file integrity: {0}", file.Path);
                            
                            string key;
                            long size;
                            using (var fs = System.IO.File.OpenRead(file.Path))
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
                        }
                    }
                    
                if (fileErrors > 0 && brokenFiles.Count > 0)
                    m_result.AddMessage(string.Format("Failed to restore {0} files, additionally the following files failed to download, which may be the cause:{1}", fileErrors, Environment.NewLine, string.Join(Environment.NewLine, brokenFiles)));

                // Drop the temp tables
                database.DropRestoreTable();
                backend.WaitForComplete(database, null);
            }
            
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_Complete);
            result.EndTime = DateTime.Now;
        }

        private static void ApplyMetadata(string path, LocalRestoreDatabase database)
        {
            //TODO: Implement writing metadata
        }

        private static void ScanForExistingSourceBlocksFast(LocalRestoreDatabase database, Options options, byte[] blockbuffer, System.Security.Cryptography.HashAlgorithm hasher, RestoreResults result)
        {
            // Fill BLOCKS with data from known local source files
            using (var blockmarker = database.CreateBlockMarker())
            {
                var updateCount = 0L;
            	foreach(var entry in database.GetFilesAndSourceBlocksFast())
            	{
                    var targetpath = entry.TargetPath;
                    var targetfileid = entry.TargetFileID;
                    var sourcepath = entry.SourcePath;
                    var patched = false;
                    
                	try
                	{
        				if (System.IO.File.Exists(sourcepath))
        				{
	    					var folderpath = System.IO.Path.GetDirectoryName(targetpath);
	    					if (!options.Dryrun && !System.IO.Directory.Exists(folderpath))
	    					{
	                            result.AddWarning(string.Format("Creating missing folder {0} for  file {1}", folderpath, targetpath), null);
	    						System.IO.Directory.CreateDirectory(folderpath);
	    					}
        				
	                		using(var targetstream = options.Dryrun ? null : System.IO.File.Open(targetpath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.Read))
	                		{
	                			try
	                			{
		                    		using(var sourcestream = System.IO.File.Open(sourcepath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
		                    		{
		    			                foreach(var block in entry.Blocks)
		    			                {
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
		                                                    
		                                                blockmarker.SetBlockRestored(targetfileid, block.Index, key, block.Size);
		                                            }
		                                        }	                    		
											}
										}
									}
	                			}
	                			catch (Exception ex)
	                			{
	                                result.AddWarning(string.Format("Failed to patch file: \"{0}\" with data from local file \"{1}\", message: {2}", targetpath, sourcepath, ex.Message), ex);
	                			}
	                		}
                            
                            if (updateCount++ % 20 == 0)
                                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
                            
	                	}
                        else
                        {
                            result.AddVerboseMessage("Local source file not found: {0}", sourcepath);
                        }
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


        private static void ScanForExistingSourceBlocks(LocalRestoreDatabase database, Options options, byte[] blockbuffer, System.Security.Cryptography.HashAlgorithm hasher, RestoreResults result)
        {
            // Fill BLOCKS with data from known local source files
            using (var blockmarker = database.CreateBlockMarker())
            {
                var updateCount = 0L;
                foreach (var restorelist in database.GetFilesAndSourceBlocks())
                {
                    var targetpath = restorelist.TargetPath;
                    var targetfileid = restorelist.TargetFileID;
                    var patched = false;
                    try
                    {
    					var folderpath = System.IO.Path.GetDirectoryName(targetpath);
    					if (!options.Dryrun && !System.IO.Directory.Exists(folderpath))
    					{
                            result.AddWarning(string.Format("Creating missing folder {0} for  file {1}", folderpath, targetpath), null);
    						System.IO.Directory.CreateDirectory(folderpath);
    					}
                    
                        using (var file = options.Dryrun ? null : System.IO.File.Open(targetpath, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                        using (var block = new Blockprocessor(file, blockbuffer))
                            foreach (var targetblock in restorelist.Blocks)
                            {
                            	if (!options.Dryrun)
                                	file.Position = targetblock.Offset;
                                	
                                foreach (var source in targetblock.Blocksources)
                                {
                                    try
                                    {
                                        if (System.IO.File.Exists(source.Path))
                                            using (var sourcefile = System.IO.File.Open(source.Path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                                            {
                                                sourcefile.Position = source.Offset;
                                                var size = sourcefile.Read(blockbuffer, 0, blockbuffer.Length);
                                                if (size == targetblock.Size)
                                                {
                                                    var key = Convert.ToBase64String(hasher.ComputeHash(blockbuffer, 0, size));
                                                    if (key == targetblock.Hash)
                                                    {
                                                        patched = true;
						                            	if (!options.Dryrun)
	                                                        file.Write(blockbuffer, 0, size);
	                                                        
                                                        blockmarker.SetBlockRestored(targetfileid, targetblock.Index, key, targetblock.Size);
                                                        break;
                                                    }
                                                }
                                            }
                                    }
                                    catch (Exception ex)
                                    {
                                        result.AddWarning(string.Format("Failed to patch file: \"{0}\" with data from local file \"{1}\", message: {2}", targetpath, source.Path, ex.Message), ex);
                                    }
                                }
                            }
                            
                            if (updateCount++ % 20 == 0)
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
                database.FindMissingBlocks(result);
        }

        private static void CreateDirectoryStructure(LocalRestoreDatabase database, Options options, RestoreResults result)
		{
			// This part is not protected by try/catch as we need the target folder to exist
			if (!string.IsNullOrEmpty(options.Restorepath))
                if (!System.IO.Directory.Exists(options.Restorepath))
                {
                    if (options.Verbose)
                        result.AddVerboseMessage("Creating folder: {0}", options.Restorepath);
                        
                	if (options.Dryrun)
                		result.AddDryrunMessage(string.Format("Would create folder: {0}", options.Restorepath));
                	else
                    	System.IO.Directory.CreateDirectory(options.Restorepath);
                }
        
            foreach (var folder in database.GetTargetFolders())
            {
                try
                {
                    if (!System.IO.Directory.Exists(folder))
                    {
                    	result.FoldersRestored++;
                    	
                        if (options.Verbose)
                            result.AddVerboseMessage("Creating folder: {0}", folder);
                            
                    	if (options.Dryrun)
                    		result.AddDryrunMessage(string.Format("Would create folder: {0}", folder));
                    	else
                        	System.IO.Directory.CreateDirectory(folder);
                    }
                }
                catch (Exception ex)
                {
                    result.AddWarning(string.Format("Failed to create folder: \"{0}\", message: {1}", folder, ex.Message), ex);
                }

                try
                {
                	if (!options.Dryrun)
	                    ApplyMetadata(folder, database);
                }
                catch (Exception ex)
                {
                    result.AddWarning(string.Format("Failed to set folder metadata: \"{0}\", message: {1}", folder, ex.Message), ex);
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
                    if (System.IO.File.Exists(targetpath))
                    {
                        try
                        {
                            if (rename)
                                filehasher.Initialize();

                            using(var file = System.IO.File.Open(targetpath, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                            using(var block = new Blockprocessor(file, blockbuffer))
                                foreach(var targetblock in restorelist.Blocks)
                                {
                                    var size = block.Readblock();
                                    if (size <= 0)
                                        break;
    
                                    if (size == targetblock.Size)
                                    {
                                        var key = Convert.ToBase64String(blockhasher.ComputeHash(blockbuffer, 0, size));
                                        if (key == targetblock.Hash)
                                        {
                                            blockmarker.SetBlockRestored(targetfileid, targetblock.Index, key, size);
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
                            
                            if (updateCount++ % 20 == 0)
                                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
                        }
                        catch (Exception ex)
                        {
                            result.AddWarning(string.Format("Failed to read target file: \"{0}\", message: {1}", targetpath, ex.Message), ex);
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
                        var ext = System.IO.Path.GetExtension(targetpath) ?? "";
                        if (!string.IsNullOrEmpty(ext) && !ext.StartsWith("."))
                            ext = "." + ext;
                        
                        // First we try with a simple date append, assuming that there are not many conflicts there
                        var newname = System.IO.Path.ChangeExtension(targetpath, null) + "." + database.RestoreTime.ToLocalTime().ToString("yyyy-MM-dd");
                        var tr = newname + ext;
                        var c = 0;
                        while (System.IO.File.Exists(tr) && c < 1000)
                        {
                            try
                            {
                                // If we have a file with the correct name, 
                                // it is most likely the file we want
                                filehasher.Initialize();
                                
                                string key;
                                using(var file = System.IO.File.Open(tr, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                                    key = Convert.ToBase64String(filehasher.ComputeHash(file));
                                    
                                if (key == targetfilehash)
                                {
                                    blockmarker.SetAllBlocksRestored(targetfileid);
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
