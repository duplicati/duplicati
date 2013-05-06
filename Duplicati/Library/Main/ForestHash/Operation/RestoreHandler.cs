using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Main.ForestHash.Database;
using Duplicati.Library.Main.ForestHash.Volumes;

namespace Duplicati.Library.Main.ForestHash.Operation
{
    internal class RestoreHandler : IDisposable
    {
        private string m_backendurl;
        private RestoreStatistics m_stat;
        private Options m_options;
        private byte[] m_blockbuffer;
        private string m_destination;

        public RestoreHandler(string backendurl, Options options, RestoreStatistics stat, string destination)
        {
            m_options = options;
            m_stat = stat;
            m_backendurl = backendurl;

            m_destination = destination;
            m_blockbuffer = new byte[m_options.Fhblocksize];

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

        public static RecreateDatabaseHandler.FilterFilelistDelegate FilterFilelist(DateTime restoretime)
        {
            if (restoretime.Kind == DateTimeKind.Unspecified)
                throw new Exception("Unspecified datetime instance, must be either local or UTC");

            restoretime = restoretime.ToUniversalTime();

            return 
                _lst =>
                {
                    // Unwrap, so we do not query the remote storage twice
                    var lst = _lst;
                    if (!(lst is IList<IParsedVolume>) && !(lst is IParsedVolume[]))
                        lst = lst.ToArray();

                    //We filter the filelist so we only prepare restoring the files we actually need
                    var entry = (from n in lst 
                                 where n.FileType == RemoteVolumeType.Files && n.Time <= restoretime 
                                 orderby n.Time descending
                                 select n).FirstOrDefault();
                    if (entry == null)
                        entry = lst.FirstOrDefault();

                    if (entry == null)
                        throw new Exception("No remote filelist found");

                    return new IParsedVolume[] { entry };
                };
        }

        public void Run()
        {
#if DEBUG
            if (!m_options.NoLocalDb)
#endif
                if (System.IO.File.Exists(m_options.Fhdbpath))
                {
					using(var db = new LocalRestoreDatabase(m_options.Fhdbpath, m_options.Fhblocksize))
        	            DoRun(db);
                    return;
                }


            using (var tmpdb = new Utility.TempFile())
            {
                RecreateDatabaseHandler.FilterFilelistDelegate filelistfilter = FilterFilelist(m_options.RestoreTime);

                RecreateDatabaseHandler.FilenameFilterDelegate filenamefilter = null;
                if (m_options.HasFilter)
                    filenamefilter = lst =>
                    {
                        return
                            from x in lst
                            where m_options.Filter.ShouldInclude("", x.Path)
                            select x;
                    };


                // Simultaneously with downloading blocklists, we patch as much as we can from the blockvolumes
                // This prevents repeated downloads, except for cases where the blocklists refer blocks
                // that have been previously handled. A local blockvolume cache can reduce this issue
                using (var database = new LocalRestoreDatabase(tmpdb, m_options.Fhblocksize))
                {
                    var blockhasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.FhBlockHashAlgorithm);
                    if (!blockhasher.CanReuseTransform)
                        throw new Exception(string.Format(Strings.Foresthash.InvalidCryptoSystem, m_options.FhBlockHashAlgorithm));

                    bool first = true;
                    RecreateDatabaseHandler.BlockVolumePostProcessor localpatcher =
                        (key, rd) =>
                        {
                            if (first)
                            {
                                //Figure out what files are to be patched, and what blocks are needed
                                PrepareBlockAndFileList(database, m_options, m_destination, m_stat);

                                // Don't run this again
                                first = false;
                            }
                            else
                            {
                                // Patch the missing blocks list to include the newly discovered blocklists
                                //UpdateMissingBlocksTable(key);
                            }

                            CreateDirectoryStructure(database, m_options, m_stat);

                            //If we are patching an existing target folder, do not touch stuff that is already updated
                            ScanForExistingTargetBlocks(database, m_blockbuffer, blockhasher, m_stat);

#if DEBUG
                            if (!m_options.NoLocalBlocks)
#endif
                            // If other local files already have the blocks we want, we use them instead of downloading
                            ScanForExistingSourceBlocks(database, m_options, m_blockbuffer, blockhasher, m_stat);
                            
                            //Update files with data
                            PatchWithBlocklist(database, rd, m_options, m_stat, m_blockbuffer);
                        };

                    // TODO: When UpdateMisisngBlocksTable is implemented, the localpatcher can be activated
                    // and this will reduce the need for multiple downloads of the same volume
                    // TODO: This will need some work to preserve the missing block list for use with --fh-dryrun
                    using (var rdb = new RecreateDatabaseHandler(m_backendurl, m_options, m_stat))
                        rdb.DoRun(database, filelistfilter, filenamefilter, /*localpatcher*/ null);

	                DoRun(database);
                }

                return;
            }
        }
        
        private static void PatchWithBlocklist (LocalRestoreDatabase database, BlockVolumeReader blocks, Options options, CommunicationStatistics stat, byte[] blockbuffer)
        {
            foreach (var restorelist in database.GetFilesWithMissingBlocks(blocks))
            {
                var targetpath = restorelist.Path;
                if (options.FhDryrun)
                {
                	stat.LogMessage("[Dryrun] Would patch file with remote data: {0}", targetpath);
                }
                else
                {
	                try 
	                {
	                	// TODO: Much faster if we iterate the volume and checks what blocks are used,
	                	// because the compressors usually like sequential reading
	                    using (var file = System.IO.File.Open(targetpath, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
	                        foreach (var targetblock in restorelist.Blocks)
		                    {
		                        file.Position = targetblock.Offset;
		                        var size = blocks.ReadBlock(targetblock.Key, blockbuffer);
		                        if (targetblock.Size == size)
		                            file.Write(blockbuffer, 0, size);
		                    }
	                } 
	                catch (Exception ex)
	                {
	                    stat.LogWarning(string.Format("Failed to patch file: \"{0}\", message: {1}, message: {1}", targetpath, ex.Message), ex);
	                    database.LogMessage("Warning", string.Format("Failed to patch file: \"{0}\", message: {1}", targetpath, ex.Message), ex, null);
	                }
	                
	                try
	                {
	                    ApplyMetadata(targetpath, database);
	                }
	                catch (Exception ex)
	                {
	                    stat.LogWarning(string.Format("Failed to apply metadata to file: \"{0}\", message: {1}", targetpath, ex.Message), ex);
	                    database.LogMessage("Warning", string.Format("Failed to apply metadata to file: \"{0}\", message: {1}", targetpath, ex.Message), ex, null);
	                }
                }
            }
        }

        private void DoRun(LocalDatabase dbparent)
        {
            //In this case, we check that the remote storage fits with the database.
            //We can then query the database and find the blocks that we need to do the restore
            using (var database = new LocalRestoreDatabase(dbparent, m_options.Fhblocksize))
            using (var backend = new FhBackend(m_backendurl, m_options, m_stat, database))
            {
	        	ForestHash.VerifyParameters(database, m_options);
	        	
                var blockhasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.FhBlockHashAlgorithm);
				if (blockhasher == null)
					throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, m_options.FhBlockHashAlgorithm));
                if (!blockhasher.CanReuseTransform)
                    throw new Exception(string.Format(Strings.Foresthash.InvalidCryptoSystem, m_options.FhBlockHashAlgorithm));

				if (!m_options.FhNoBackendverification)
                	ForestHash.VerifyRemoteList(backend, m_options, database, m_stat);

                //Figure out what files are to be patched, and what blocks are needed
                PrepareBlockAndFileList(database, m_options, m_destination, m_stat);

                //Make the entire output setup
                CreateDirectoryStructure(database, m_options, m_stat);

                //If we are patching an existing target folder, do not touch stuff that is already updated
                ScanForExistingTargetBlocks(database, m_blockbuffer, blockhasher, m_stat);

#if DEBUG
                if (!m_options.NoLocalBlocks)
#endif
				//Look for existing blocks in the original source files only
				ScanForExistingSourceBlocksFast(database, m_options, m_blockbuffer, blockhasher, m_stat);

                // If other local files already have the blocks we want, we use them instead of downloading
				if (m_options.FhPatchWithLocalBlocks)
                	ScanForExistingSourceBlocks(database, m_options, m_blockbuffer, blockhasher, m_stat);

                // Fill BLOCKS with remote sources
                var volumes = database.GetMissingVolumes();

                foreach (var blockvolume in new AsyncDownloader(volumes, backend))
                	using (var tmpfile = blockvolume.Value)
                    using (var blocks = new BlockVolumeReader(GetCompressionModule(blockvolume.Key.Name), tmpfile, m_options))
                        PatchWithBlocklist(database, blocks, m_options, m_stat, m_blockbuffer);

                // After all blocks in the files are restored, verify the file hash
                var filehasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.FhFileHashAlgorithm);
				if (filehasher == null)
					throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, m_options.FhFileHashAlgorithm));
					
                foreach (var file in database.GetFilesToRestore())
                {
                    try
                    {
                        string key;
                        using (var fs = System.IO.File.OpenRead(file.Path))
                            key = Convert.ToBase64String(filehasher.ComputeHash(fs));

                        if (key != file.Hash)
                            throw new Exception(string.Format("Failed to restore file: \"{0}\". File hash is {1}, expected hash is {2}", file.Path, key, file.Hash));
                    } 
                    catch (Exception ex)
                    {
                        m_stat.LogWarning(string.Format("Failed to restore file: \"{0}\", message: {1}", file.Path, ex.Message), ex);
                        database.LogMessage("Warning", string.Format("Failed to restore file: \"{0}\", message: {1}", file.Path, ex.Message), ex, null);
                    }
                }

                // Drop the temp tables
                database.DropRestoreTable();
                backend.WaitForComplete(database, null);
            }
        }

        private static void ApplyMetadata(string path, LocalRestoreDatabase database)
        {
            //TODO: Implement writing metadata
        }

        private static void ScanForExistingSourceBlocksFast(LocalRestoreDatabase database, Options options, byte[] blockbuffer, System.Security.Cryptography.HashAlgorithm hasher, CommunicationStatistics stat)
        {
            // Fill BLOCKS with data from known local source files
            using (var blockmarker = database.CreateBlockMarker())
            {
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
	                		using(var targetstream = System.IO.File.Open(targetpath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.Read))
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
						                            	if (options.FhDryrun)
						                            		patched = true;
					                            		else
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
	                                stat.LogWarning(string.Format("Failed to patch file: \"{0}\" with data from local file \"{1}\", message: {2}", targetpath, sourcepath, ex.Message), ex);
	                                database.LogMessage("Warning", string.Format("Failed to patch file: \"{0}\" with data from local file \"{1}\", message: {2}", targetpath, sourcepath, ex.Message), ex, null);
	                			}
	                		}	
	                	}
	 				}
                    catch (Exception ex)
                    {
                        stat.LogWarning(string.Format("Failed to patch file: \"{0}\" with local data, message: {1}", targetpath, ex.Message), ex);
                        database.LogMessage("Warning", string.Format("Failed to patch file: \"{0}\" with local data, message: {1}", targetpath, ex.Message), ex, null);
                    }
                    
                    if (patched && options.FhDryrun)
                    	stat.LogMessage("[Dryrun] Would patch file with local data: {0}", targetpath);
            	}
            	
            	blockmarker.Commit();
            }
        }


        private static void ScanForExistingSourceBlocks(LocalRestoreDatabase database, Options options, byte[] blockbuffer, System.Security.Cryptography.HashAlgorithm hasher, CommunicationStatistics stat)
        {
            // Fill BLOCKS with data from known local source files
            using (var blockmarker = database.CreateBlockMarker())
            {
                foreach (var restorelist in database.GetFilesAndSourceBlocks())
                {
                    var targetpath = restorelist.TargetPath;
                    var targetfileid = restorelist.TargetFileID;
                    var patched = false;
                    try
                    {
                        using (var file = options.FhDryrun ? null : System.IO.File.Open(targetpath, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                        using (var block = new Blockprocessor(file, blockbuffer))
                            foreach (var targetblock in restorelist.Blocks)
                            {
                            	if (!options.FhDryrun)
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
						                            	if (options.FhDryrun)
						                            		patched = true;
					                            		else
	                                                        file.Write(blockbuffer, 0, size);
	                                                        
                                                        blockmarker.SetBlockRestored(targetfileid, targetblock.Index, key, targetblock.Size);
                                                        break;
                                                    }
                                                }
                                            }
                                    }
                                    catch (Exception ex)
                                    {
                                        stat.LogWarning(string.Format("Failed to patch file: \"{0}\" with data from local file \"{1}\", message: {2}", targetpath, source.Path, ex.Message), ex);
                                        database.LogMessage("Warning", string.Format("Failed to patch file: \"{0}\" with data from local file \"{1}\", message: {2}", targetpath, source.Path, ex.Message), ex, null);
                                    }
                                }
                            }
                    }
                    catch (Exception ex)
                    {
                        stat.LogWarning(string.Format("Failed to patch file: \"{0}\" with local data, message: {1}", targetpath, ex.Message), ex);
                        database.LogMessage("Warning", string.Format("Failed to patch file: \"{0}\" with local data, message: {1}", targetpath, ex.Message), ex, null);
                    }
                    
                    if (patched && options.FhDryrun)
                    	stat.LogMessage("[Dryrun] Would patch file with local data: {0}", targetpath);
                }

                blockmarker.Commit();
            }
        }

        private static void PrepareBlockAndFileList(LocalRestoreDatabase database, Options options, string destination, CommunicationStatistics stat)
        {
            // Create a temporary table FILES by selecting the files from fileset that matches a specific operation id
            // Delete all entries from the temp table that are excluded by the filter(s)
            database.PrepareRestoreFilelist(options.RestoreTime, (options.FileToRestore ?? "").Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries), options.HasFilter ? options.Filter : null, stat);

            // Find the largest common prefix
            string largest_prefix = database.GetLargestPrefix();

            // Set the target paths, special care with C:\ and /
            database.SetTargetPaths(largest_prefix, destination);

            // Create a temporary table BLOCKS that lists all blocks that needs to be recovered
            database.FindMissingBlocks();
        }

        private static void CreateDirectoryStructure(LocalRestoreDatabase database, Options options, CommunicationStatistics stat)
        {
            foreach (var folder in database.GetTargetFolders())
            {
                try
                {
                    if (!System.IO.Directory.Exists(folder))
                    	if (options.FhDryrun)
                    		stat.LogMessage("[Dryrun] Would create folder: {0}", folder);
                    	else
                        	System.IO.Directory.CreateDirectory(folder);
                }
                catch (Exception ex)
                {
                    stat.LogWarning(string.Format("Failed to create folder: \"{0}\", message: {1}", folder, ex.Message), ex);
                    database.LogMessage("Warning", string.Format("Failed to create folder: \"{0}\", message: {1}", folder, ex.Message), ex, null);
                }

                try
                {
                	if (!options.FhDryrun)
	                    ApplyMetadata(folder, database);
                }
                catch (Exception ex)
                {
                    stat.LogWarning(string.Format("Failed to set folder metadata: \"{0}\", message: {1}", folder, ex.Message), ex);
                    database.LogMessage("Warning", string.Format("Failed to set folder metadata: \"{0}\", message: {1}", folder, ex.Message), ex, null);
                }
            }
        }

        private static void ScanForExistingTargetBlocks(LocalRestoreDatabase database, byte[] blockbuffer, System.Security.Cryptography.HashAlgorithm hasher, CommunicationStatistics stat)
        {
            // Scan existing files for existing BLOCKS
            using (var blockmarker = database.CreateBlockMarker())
            {
                foreach (var restorelist in database.GetExistingFilesWithBlocks())
                {
                    var targetpath = restorelist.TargetPath;
                    var targetfileid = restorelist.TargetFileID;
                    if (System.IO.File.Exists(targetpath))
                    {
                        try
                        {
                            using (var file = System.IO.File.Open(targetpath, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                            using (var block = new Blockprocessor(file, blockbuffer))
                                foreach (var targetblock in restorelist.Blocks)
                                {
                                    var size = block.Readblock();
                                    if (size <= 0)
                                        break;
    
                                    if (size == targetblock.Size)
                                    {
                                        var key = Convert.ToBase64String(hasher.ComputeHash(blockbuffer, 0, size));
                                        if (key == targetblock.Hash)
                                        {
                                            blockmarker.SetBlockRestored(targetfileid, targetblock.Index, key, size);
                                        }
                                    }
                                }
                        }
                        catch (Exception ex)
                        {
                            stat.LogWarning(string.Format("Failed to read target file: \"{0}\", message: {1}", targetpath, ex.Message), ex);
                            database.LogMessage("Warning", string.Format("Failed to read target file: \"{0}\", message: {1}", targetpath, ex.Message), ex, null);
                        }
                    }
                }

                blockmarker.Commit();
            }
        }

        public void Dispose()
        {
            m_stat.EndTime = DateTime.Now;
        }
    }
}
