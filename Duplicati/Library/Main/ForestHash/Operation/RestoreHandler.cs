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
        private FhOptions m_options;
        private byte[] m_blockbuffer;
        private string m_destination;

        public RestoreHandler(string backendurl, FhOptions options, RestoreStatistics stat, string destination)
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
                    Run(null);
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
                using (var database = new LocalRestoredatabase(tmpdb ?? m_options.Fhdbpath, m_options.Fhblocksize))
                {
                    var hasher = System.Security.Cryptography.SHA256.Create();
                    if (!hasher.CanReuseTransform)
                        throw new Exception(Strings.Foresthash.InvalidCryptoSystem);

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

                            CreateDirectoryStructure(database, m_stat);

                            //If we are patching an existing target folder, do not touch stuff that is already updated
                            ScanForExistingTargetBlocks(database, m_blockbuffer, hasher, m_stat);

#if DEBUG
                            if (!m_options.NoLocalBlocks)
#endif
                            // If other local files already have the blocks we want, we use them instead of downloading
                            ScanForExistingSourceBlocks(database, m_blockbuffer, hasher, m_stat);
                            
                            //Update files with data
                            PatchWithBlocklist(database, rd, m_stat, m_blockbuffer);
                        };

                    // TODO: When UpdateMisisngBlocksTable is implemented, the localpatcher can be activated
                    // and this will reduce the need for multiple downloads of the same volume
                    using (var rdb = new RecreateDatabaseHandler(m_backendurl, m_options, m_destination, m_stat))
                        rdb.Run(tmpdb, filelistfilter, filenamefilter, /*localpatcher*/ null);
                }

                Run(tmpdb);
                return;
            }
        }
        
        private static void PatchWithBlocklist (LocalRestoredatabase database, BlockVolumeReader blocks, CommunicationStatistics stat, byte[] blockbuffer)
        {
            foreach (var restorelist in database.GetFilesWithMissingBlocks(blocks))
            {
                var targetpath = restorelist.Path;
                try 
                {
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
                    database.LogMessage("Warning", string.Format("Failed to patch file: \"{0}\", message: {1}", targetpath, ex.Message), ex);
                }
                
                try
                {
                    ApplyMetadata(targetpath, database);
                }
                catch (Exception ex)
                {
                    stat.LogWarning(string.Format("Failed to apply metadata to file: \"{0}\", message: {1}", targetpath, ex.Message), ex);
                    database.LogMessage("Warning", string.Format("Failed to apply metadata to file: \"{0}\", message: {1}", targetpath, ex.Message), ex);
                }
            }
        }


        public void Run(string dbpath)
        {
            //In this case, we check that the remote storage fits with the database.
            //We can then query the database and find the blocks that we need to do the restore
            using (var database = new LocalRestoredatabase(dbpath ?? m_options.Fhdbpath, m_options.Fhblocksize))
            using (var backend = new FhBackend(m_backendurl, m_options, database, m_stat))
            {
                var hasher = System.Security.Cryptography.SHA256.Create();
                if (!hasher.CanReuseTransform)
                    throw new Exception(Strings.Foresthash.InvalidCryptoSystem);

                ForestHash.VerifyRemoteList(backend, m_options, database);

                //Figure out what files are to be patched, and what blocks are needed
                PrepareBlockAndFileList(database, m_options, m_destination, m_stat);

                //Make the entire output setup
                CreateDirectoryStructure(database, m_stat);

                //If we are patching an existing target folder, do not touch stuff that is already updated
                ScanForExistingTargetBlocks(database, m_blockbuffer, hasher, m_stat);


                //TODO: It is possible to combine the existing block scanning with the local block scanning
#if DEBUG
                if (!m_options.NoLocalBlocks)
#endif
                // If other local files already have the blocks we want, we use them instead of downloading
                ScanForExistingSourceBlocks(database, m_blockbuffer, hasher, m_stat);

                // Fill BLOCKS with remote sources
                var volumes = database.GetMissingVolumes();

                foreach (var blockvolume in new AsyncDownloader(volumes, backend))
                    using (var blocks = new BlockVolumeReader(GetCompressionModule(blockvolume.Key.Name), blockvolume.Value, m_options))
                        PatchWithBlocklist(database, blocks, m_stat, m_blockbuffer);

                // After all blocks in the files are restored, verify the file hash
                foreach (var file in database.GetFilesToRestore())
                {
                    try
                    {
                        string key;
                        using (var fs = System.IO.File.OpenRead(file.Path))
                            key = Convert.ToBase64String(hasher.ComputeHash(fs));

                        if (key != file.Hash)
                            throw new Exception(string.Format("Failed to restore file: \"{0}\". File hash is {1}, expected hash is {2}", file.Path, key, file.Hash));
                    } 
                    catch (Exception ex)
                    {
                        m_stat.LogWarning(string.Format("Failed to restore file: \"{0}\", message: {1}", file.Path, ex.Message), ex);
                        database.LogMessage("Warning", string.Format("Failed to restore file: \"{0}\", message: {1}", file.Path, ex.Message), ex);
                    }
                }

                // Drop the temp tables
                database.DropRestoreTable();
            }
        }

        private static void ApplyMetadata(string path, LocalRestoredatabase database)
        {
            //TODO: Implement writing metadata
        }

        private static void ScanForExistingSourceBlocks(LocalRestoredatabase database, byte[] blockbuffer, System.Security.Cryptography.HashAlgorithm hasher, CommunicationStatistics stat)
        {
            // Fill BLOCKS with data from known local source files
            using (var blockmarker = database.CreateBlockMarker())
            {
                foreach (var restorelist in database.GetFilesAndSourceBlocks())
                {
                    var targetpath = restorelist.TargetPath;
                    
                    try
                    {
                        using (var file = System.IO.File.Open(targetpath, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                        using (var block = new Blockprocessor(file, blockbuffer))
                            foreach (var targetblock in restorelist.Blocks)
                            {
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
                                                        file.Write(blockbuffer, 0, size);
                                                        blockmarker.SetBlockRestored(targetpath, targetblock.Index, key, targetblock.Size);
                                                        break;
                                                    }
                                                }
                                            }
                                    }
                                    catch (Exception ex)
                                    {
                                        stat.LogWarning(string.Format("Failed to patch file: \"{0}\" with data from local file \"{1}\", message: {2}", targetpath, source.Path, ex.Message), ex);
                                        database.LogMessage("Warning", string.Format("Failed to patch file: \"{0}\" with data from local file \"{1}\", message: {2}", targetpath, source.Path, ex.Message), ex);
                                    }
                                }
                            }
                    }
                    catch (Exception ex)
                    {
                        stat.LogWarning(string.Format("Failed to patch file: \"{0}\" with local data, message: {1}", targetpath, ex.Message), ex);
                        database.LogMessage("Warning", string.Format("Failed to patch file: \"{0}\" with local data, message: {1}", targetpath, ex.Message), ex);
                    }
                }

                blockmarker.Commit();
            }
        }

        private static void PrepareBlockAndFileList(LocalRestoredatabase database, FhOptions options, string destination, CommunicationStatistics stat)
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

        private static void CreateDirectoryStructure(LocalRestoredatabase database, CommunicationStatistics stat)
        {
            foreach (var folder in database.GetTargetFolders())
            {
                try
                {
                    if (!System.IO.Directory.Exists(folder))
                        System.IO.Directory.CreateDirectory(folder);
                }
                catch (Exception ex)
                {
                    stat.LogWarning(string.Format("Failed to create folder: \"{0}\", message: {1}", folder, ex.Message), ex);
                    database.LogMessage("Warning", string.Format("Failed to create folder: \"{0}\", message: {1}", folder, ex.Message), ex);
                }

                try
                {
                    ApplyMetadata(folder, database);
                }
                catch (Exception ex)
                {
                    stat.LogWarning(string.Format("Failed to set folder metadata: \"{0}\", message: {1}", folder, ex.Message), ex);
                    database.LogMessage("Warning", string.Format("Failed to set folder metadata: \"{0}\", message: {1}", folder, ex.Message), ex);
                }
            }
        }

        private static void ScanForExistingTargetBlocks(LocalRestoredatabase database, byte[] blockbuffer, System.Security.Cryptography.HashAlgorithm hasher, CommunicationStatistics stat)
        {
            // Scan existing files for existing BLOCKS
            using (var blockmarker = database.CreateBlockMarker())
            {
                foreach (var restorelist in database.GetExistingFilesWithBlocks())
                {
                    var targetpath = restorelist.TargetPath;
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
                                            blockmarker.SetBlockRestored(targetpath, targetblock.Index, key, size);
                                        }
                                    }
                                }
                        }
                        catch (Exception ex)
                        {
                            stat.LogWarning(string.Format("Failed to read target file: \"{0}\", message: {1}", targetpath, ex.Message), ex);
                            database.LogMessage("Warning", string.Format("Failed to read target file: \"{0}\", message: {1}", targetpath, ex.Message), ex);
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
