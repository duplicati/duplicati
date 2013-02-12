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
                using (var backend = new FhBackend(m_backendurl, m_options, database))
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
                                PrepareBlockAndFileList(database, m_options, m_destination);

                                // Don't run this again
                                first = false;
                            }
                            else
                            {
                                // Patch the missing blocks list to include the newly discovered blocklists
                                //UpdateMissingBlocksTable(key);
                            }

                            CreateDirectoryStructure(database);

                            //If we are patching an existing target folder, do not touch stuff that is already updated
                            ScanForExistingTargetBlocks(database, m_blockbuffer, hasher);

#if DEBUG
                            if (!m_options.NoLocalBlocks)
#endif
                            // If other local files already have the blocks we want, we use them instead of downloading
                            ScanForExistingSourceBlocks(database, m_blockbuffer, hasher);


                            // Patch with blocks as required
                            foreach (var restorelist in database.GetFilesWithMissingBlocks(rd))
                            {
                                var targetpath = restorelist.Path;
                                using (var file = System.IO.File.Open(targetpath, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                                    foreach (var targetblock in restorelist.Blocks)
                                    {
                                        file.Position = targetblock.Offset;
                                        var size = rd.ReadBlock(targetblock.Key, m_blockbuffer);
                                        file.Write(m_blockbuffer, 0, size);

                                    }

                                ApplyMetadata(targetpath, database);
                            }
                        };

                    // TODO: When UpdateMisisngBlocksTable is implemented, the localpatcher can be activated
                    // and this will reduce the need for multiple downloads of the same volume
                    using (var rdb = new RecreateDatabaseHandler(m_backendurl, m_options, m_destination))
                        rdb.Run(tmpdb, filelistfilter, filenamefilter, /*localpatcher*/ null);
                }

                Run(tmpdb);
                return;
            }
        }


        public void Run(string dbpath = null)
        {
            //In this case, we check that the remote storage fits with the database.
            //We can then query the database and find the blocks that we need to do the restore
            using (var database = new LocalRestoredatabase(dbpath ?? m_options.Fhdbpath, m_options.Fhblocksize))
            using (var backend = new FhBackend(m_backendurl, m_options, database))
            {
                var hasher = System.Security.Cryptography.SHA256.Create();
                if (!hasher.CanReuseTransform)
                    throw new Exception(Strings.Foresthash.InvalidCryptoSystem);

                ForestHash.VerifyRemoteList(backend, m_options, database);

                //Figure out what files are to be patched, and what blocks are needed
                PrepareBlockAndFileList(database, m_options, m_destination);

                //Make the entire output setup
                CreateDirectoryStructure(database);

                //If we are patching an existing target folder, do not touch stuff that is already updated
                ScanForExistingTargetBlocks(database, m_blockbuffer, hasher);

#if DEBUG
                if (!m_options.NoLocalBlocks)
#endif
                // If other local files already have the blocks we want, we use them instead of downloading
                ScanForExistingSourceBlocks(database, m_blockbuffer, hasher);

                // Fill BLOCKS with remote sources
                var volumes = database.GetMissingVolumes();

                foreach (var blockvolume in new AsyncDownloader(volumes, backend))
                    using (var blocks = new BlockVolumeReader(GetCompressionModule(blockvolume.Key.Name), blockvolume.Value, m_options))
                    {
                        foreach (var restorelist in database.GetFilesWithMissingBlocks(blocks))
                        {
                            var targetpath = restorelist.Path;
                            using (var file = System.IO.File.Open(targetpath, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                                foreach (var targetblock in restorelist.Blocks)
                                {
                                    file.Position = targetblock.Offset;
                                    var size = blocks.ReadBlock(targetblock.Key, m_blockbuffer);
                                    if (targetblock.Size == size)
                                        file.Write(m_blockbuffer, 0, size);
                                }

                            ApplyMetadata(targetpath, database);
                        }
                    }

                // After all blocks in the files are restored, verify the file hash
                foreach (var file in database.GetFilesToRestore())
                {
                    string key;
                    //TODO: Try/Catch
                    using (var fs = System.IO.File.OpenRead(file.Path))
                        key = Convert.ToBase64String(hasher.ComputeHash(fs));

                    //TODO: How do we handle this case? Warning in log?
                    if (key != file.Hash)
                    {
                        //throw new Exception(string.Format("Failed to restore file: {0}", file.Path));
                        Console.WriteLine(string.Format("Failed to restore file: {0}", file.Path));
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

        private static void ScanForExistingSourceBlocks(LocalRestoredatabase database, byte[] blockbuffer, System.Security.Cryptography.HashAlgorithm hasher)
        {
            // Fill BLOCKS with data from known local source files
            using (var blockmarker = database.CreateBlockMarker())
            {
                foreach (var restorelist in database.GetFilesAndSourceBlocks())
                {
                    var targetpath = restorelist.TargetPath;
                    //TODO: Try/Catch
                    using (var file = System.IO.File.Open(targetpath, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                    using (var block = new Blockprocessor(file, blockbuffer))
                        foreach (var targetblock in restorelist.Blocks)
                        {
                            file.Position = targetblock.Offset;
                            foreach (var source in targetblock.Blocksources)
                            {
                                //TODO: Try/Catch
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
                        }
                }

                blockmarker.Commit();
            }
        }

        private static void PrepareBlockAndFileList(LocalRestoredatabase database, FhOptions options, string destination)
        {
            // Create a temporary table FILES by selecting the files from fileset that matches a specific operation id
            // Delete all entries from the temp table that are excluded by the filter(s)
            database.PrepareRestoreFilelist(options.RestoreTime, (options.FileToRestore ?? "").Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries), options.HasFilter ? options.Filter : null);

            // Find the largest common prefix
            string largest_prefix = database.GetLargestPrefix();

            // Set the target paths, special care with C:\ and /
            database.SetTargetPaths(largest_prefix, destination);

            // Create a temporary table BLOCKS that lists all blocks that needs to be recovered
            database.FindMissingBlocks();
        }

        private static void CreateDirectoryStructure(LocalRestoredatabase database)
        {
            foreach (var folder in database.GetTargetFolders())
            {
                if (!System.IO.Directory.Exists(folder))
                    System.IO.Directory.CreateDirectory(folder);

                ApplyMetadata(folder, database);
            }
        }

        private static void ScanForExistingTargetBlocks(LocalRestoredatabase database, byte[] blockbuffer, System.Security.Cryptography.HashAlgorithm hasher)
        {
            // Scan existing files for existing BLOCKS
            using (var blockmarker = database.CreateBlockMarker())
            {
                foreach (var restorelist in database.GetExistingFilesWithBlocks())
                {
                    var targetpath = restorelist.TargetPath;
                    if (System.IO.File.Exists(targetpath))
                    {
                        //TODO: Try/Catch
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
                }

                blockmarker.Commit();
            }
        }

        public void Dispose()
        {
        }
    }
}
