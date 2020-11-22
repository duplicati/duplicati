#region Disclaimer / License
// Copyright (C) 2019, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
//
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;

namespace Duplicati.Library.Main.Operation
{
    internal class RestoreHandler
    {    
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<RestoreHandler>();

        private readonly string m_backendurl;
        private readonly Options m_options;
        private byte[] m_blockbuffer;
        private readonly RestoreResults m_result;
        private static readonly string DIRSEP = Util.DirectorySeparatorString;

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
                throw new UserInformationException(string.Format("Unable to parse filename to valid entry: {0}", filename), "FailedToParseRemoteName");

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
            
            if (!m_options.NoLocalDb && SystemIO.IO_OS.FileExists(m_options.Dbpath))
            {
                using(var db = new LocalRestoreDatabase(m_options.Dbpath))
                {
                    db.SetResult(m_result);
                    DoRun(db, filter, m_result);
                    db.WriteResults();
                }
                    
                return;
            }
            
            
            Logging.Log.WriteInformationMessage(LOGTAG, "NoLocalDatabase", "No local database, building a temporary database");
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
                        // TODO: When UpdateMissingBlocksTable is implemented, the localpatcher
                        // (removed in revision 9ce1e807 ("Remove unused variables and fields") can be activated
                        // and this will reduce the need for multiple downloads of the same volume
                        // TODO: This will need some work to preserve the missing block list for use with --fh-dryrun
                        m_result.RecreateDatabaseResults = new RecreateDatabaseResults(m_result);
                        using(new Logging.Timer(LOGTAG, "RecreateTempDbForRestore", "Recreate temporary database for restore"))
                            new RecreateDatabaseHandler(m_backendurl, m_options, (RecreateDatabaseResults)m_result.RecreateDatabaseResults)
                                .DoRun(database, false, filter, filelistfilter, null);

                        if (!m_options.SkipMetadata)
                            ApplyStoredMetadata(m_options, metadatastorage);
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
            var blockhasher = fullblockverification ? Library.Utility.HashAlgorithmHelper.Create(options.BlockHashAlgorithm) : null;

            using (var blockmarker = database.CreateBlockMarker())
            using(var volumekeeper = database.GetMissingBlockData(blocks, options.Blocksize))
            {
                foreach(var restorelist in volumekeeper.FilesWithMissingBlocks)
                {
                    var targetpath = restorelist.Path;

                    if (options.Dryrun)
                    {
                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldPatchFile", "Would patch file with remote data: {0}", targetpath);
                    }
                    else
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "PatchingFile", "Patching file with remote data: {0}", targetpath);

                        try
                        {   
                            var folderpath = SystemIO.IO_OS.PathGetDirectoryName(targetpath);
                            if (!options.Dryrun && !SystemIO.IO_OS.DirectoryExists(folderpath))
                            {
                                Logging.Log.WriteWarningMessage(LOGTAG, "CreateMissingFolder", null, "Creating missing folder {0} for  file {1}", folderpath, targetpath);
                                SystemIO.IO_OS.DirectoryCreate(folderpath);
                            }
                            
                            // TODO: Much faster if we iterate the volume and checks what blocks are used,
                            // because the compressors usually like sequential reading
                            using(var file = SystemIO.IO_OS.FileOpenWrite(targetpath))
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
                                                Logging.Log.WriteWarningMessage(LOGTAG, "InvalidBlock", null, "Invalid block detected for {0}, expected hash: {1}, actual hash: {2}", targetpath, targetblock.Key, key);
                                        }

                                        if (valid)
                                        {
                                            file.Write(blockbuffer, 0, size);
                                            blockmarker.SetBlockRestored(restorelist.FileID, targetblock.Offset / blocksize, targetblock.Key, size, false);
                                        }
                                    } 
                                    else
                                    {
                                        Logging.Log.WriteWarningMessage(LOGTAG, "WrongBlockSize", null, "Block with hash {0} should have size {1} but has size {2}", targetblock.Key, targetblock.Size, size);
                                    }
                                }
                            
                            if ((++updateCounter) % 20 == 0)
                                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "PatchFailed", ex, "Failed to patch file: \"{0}\", message: {1}, message: {1}", targetpath, ex.Message);
                            if (options.UnittestMode)
                                throw;
                        }
                    }
                }

                if (!options.SkipMetadata)
                {
                    foreach(var restoremetadata in volumekeeper.MetadataWithMissingBlocks)
                    {
                        var targetpath = restoremetadata.Path;
                        Logging.Log.WriteVerboseMessage(LOGTAG, "RecordingMetadata", "Recording metadata from remote data: {0}", targetpath);
                    
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
                            Logging.Log.WriteWarningMessage(LOGTAG, "MetatdataRecordFailed", ex, "Failed to record metadata for file: \"{0}\", message: {1}", targetpath, ex.Message);
                            if (options.UnittestMode)
                                throw;
                        }
                    }
                }
                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
                blockmarker.Commit();
            }
        }

        private static void ApplyStoredMetadata(Options options, RestoreHandlerMetadataStorage metadatastorage)
        {
            foreach(var metainfo in metadatastorage.Records)
            {
                var targetpath = metainfo.Key;

                if (options.Dryrun)
                {
                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldPatchMetadata", "Would patch metadata with remote data: {0}", targetpath);
                }
                else
                {               
                    Logging.Log.WriteVerboseMessage(LOGTAG, "PatchingMetadata", "Patching metadata with remote data: {0}", targetpath);
                    try
                    {
                        var folderpath = Duplicati.Library.Utility.Utility.GetParent(targetpath, false);
                        if (!options.Dryrun && !SystemIO.IO_OS.DirectoryExists(folderpath))
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "CreateMissingFolder", null, "Creating missing folder {0} for target {1}", folderpath, targetpath);
                            SystemIO.IO_OS.DirectoryCreate(folderpath);
                        }

                        ApplyMetadata(targetpath, metainfo.Value, options.RestorePermissions, options.RestoreSymlinkMetadata, options.Dryrun);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "MetadataWriteFailed", ex, "Failed to apply metadata to file: \"{0}\", message: {1}", targetpath, ex.Message);
                        if (options.UnittestMode)
                            throw;
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
                
                var blockhasher = Library.Utility.HashAlgorithmHelper.Create(m_options.BlockHashAlgorithm);
                var filehasher = Library.Utility.HashAlgorithmHelper.Create(m_options.FileHashAlgorithm);
                if (blockhasher == null)
                    throw new UserInformationException(Strings.Common.InvalidHashAlgorithm(m_options.BlockHashAlgorithm), "BlockHashAlgorithmNotSupported");
                if (!blockhasher.CanReuseTransform)
                    throw new UserInformationException(Strings.Common.InvalidCryptoSystem(m_options.BlockHashAlgorithm), "BlockHashAlgorithmNotSupported");

                if (filehasher == null)
                    throw new UserInformationException(Strings.Common.InvalidHashAlgorithm(m_options.FileHashAlgorithm), "FileHashAlgorithmNotSupported");
                if (!filehasher.CanReuseTransform)
                    throw new UserInformationException(Strings.Common.InvalidCryptoSystem(m_options.FileHashAlgorithm), "FileHashAlgorithmNotSupported");

                if (!m_options.NoBackendverification)
                {
                    m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_PreRestoreVerify);
                    FilelistProcessor.VerifyRemoteList(backend, m_options, database, result.BackendWriter, false, null);
                }

                //Figure out what files are to be patched, and what blocks are needed
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_CreateFileList);
                using(new Logging.Timer(LOGTAG, "PrepareBlockList", "PrepareBlockList"))
                    PrepareBlockAndFileList(database, m_options, filter, result);

                //Make the entire output setup
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_CreateTargetFolders);
                using(new Logging.Timer(LOGTAG, "CreateDirectory", "CreateDirectory"))
                    CreateDirectoryStructure(database, m_options, result);
                
                //If we are patching an existing target folder, do not touch stuff that is already updated
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_ScanForExistingFiles);
                using(new Logging.Timer(LOGTAG, "ScanForExistingTargetBlocks", "ScanForExistingTargetBlocks"))
                    ScanForExistingTargetBlocks(database, m_blockbuffer, blockhasher, filehasher, m_options, result);

                //Look for existing blocks in the original source files only
                using(new Logging.Timer(LOGTAG, "ScanForExistingSourceBlocksFast", "ScanForExistingSourceBlocksFast"))
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
                    using(new Logging.Timer(LOGTAG, "PatchWithLocalBlocks", "PatchWithLocalBlocks"))
                        ScanForExistingSourceBlocks(database, m_options, m_blockbuffer, blockhasher, result, metadatastorage);
                }

                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                {
                    backend.WaitForComplete(database, null);
                    return;
                }
                
                // Fill BLOCKS with remote sources
                List<IRemoteVolume> volumes;
                using (new Logging.Timer(LOGTAG, "GetMissingVolumes", "GetMissingVolumes"))
                    volumes = database.GetMissingVolumes().ToList();

                if (volumes.Count > 0)
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "RemoteFileCount", "{0} remote files are required to restore", volumes.Count);
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
                        Logging.Log.WriteErrorMessage(LOGTAG, "PatchingFailed", ex, "Failed to patch with remote file: \"{0}\", message: {1}", blockvolume.Name, ex.Message);
                        if (ex is System.Threading.ThreadAbortException)
                            throw;
                    }

                var fileErrors = 0L;

                // Restore empty files. They might not have any blocks so don't appear in any volume.
                foreach (var file in database.GetFilesToRestore(true).Where(item => item.Length == 0))
                {
                    Logging.Log.WriteProfilingMessage(LOGTAG, "RestoreFile", "Restoring empty file \"{0}\"", file.Path);

                    try
                    {
                        SystemIO.IO_OS.DirectoryCreate(SystemIO.IO_OS.PathGetDirectoryName(file.Path));
                        // Just create the file and close it right away, empty statement is intentional.
                        using (SystemIO.IO_OS.FileCreate(file.Path))
                        {
                        }
                    }
                    catch (Exception ex)
                    {
                        fileErrors++;
                        Logging.Log.WriteErrorMessage(LOGTAG, "RestoreFileFailed", ex, "Failed to restore empty file: \"{0}\". Error message was: {1}", file.Path, ex.Message);
                        if (ex is System.Threading.ThreadAbortException)
                            throw;
                    }
                }

                // Enforcing the length of files is now already done during ScanForExistingTargetBlocks
                // and thus not necessary anymore.

                // Apply metadata
                if (!m_options.SkipMetadata)
                    ApplyStoredMetadata(m_options, metadatastorage);
                
                // Reset the filehasher if it was used to verify existing files
                filehasher.Initialize();
                    
                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                    return;
                
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_PostRestoreVerify);

                if (m_options.PerformRestoredFileVerification)
                {
                    // After all blocks in the files are restored, verify the file hash
                    using(new Logging.Timer(LOGTAG, "RestoreVerification", "RestoreVerification"))
                        foreach(var file in database.GetFilesToRestore(true))
                        {
                            try
                            {
                                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                                {
                                    backend.WaitForComplete(database, null);
                                    return;
                                }
                            
                                Logging.Log.WriteVerboseMessage(LOGTAG, "TestFileIntegrity", "Testing restored file integrity: {0}", file.Path);
                            
                                string key;
                                long size;
                                using(var fs = SystemIO.IO_OS.FileOpenRead(file.Path))
                                {
                                    size = fs.Length;
                                    key = Convert.ToBase64String(filehasher.ComputeHash(fs));
                                }
    
                                if (key != file.Hash)
                                    throw new Exception(string.Format("Failed to restore file: \"{0}\". File hash is {1}, expected hash is {2}", file.Path, key, file.Hash));
                                result.RestoredFiles++;
                                result.SizeOfRestoredFiles += size;
                            }
                            catch (Exception ex)
                            {
                                fileErrors++;
                                Logging.Log.WriteErrorMessage(LOGTAG, "RestoreFileFailed", ex, "Failed to restore file: \"{0}\". Error message was: {1}", file.Path, ex.Message);
                                if (ex is System.Threading.ThreadAbortException)
                                    throw;
                            }
                        }
                }
                    
                if (fileErrors > 0 && brokenFiles.Count > 0)
                    Logging.Log.WriteInformationMessage(LOGTAG, "RestoreFailures", "Failed to restore {0} files, additionally the following files failed to download, which may be the cause:{1}{2}", fileErrors, Environment.NewLine, string.Join(Environment.NewLine, brokenFiles));
                else if (fileErrors > 0)
                    Logging.Log.WriteInformationMessage(LOGTAG, "RestoreFailures", "Failed to restore {0} files", fileErrors);
                else if (result.RestoredFiles == 0)
                    Logging.Log.WriteWarningMessage(LOGTAG, "NoFilesRestored", null, "Restore completed without errors but no files were restored");

                // Drop the temp tables
                database.DropRestoreTable();
                backend.WaitForComplete(database, null);
            }
            
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_Complete);
            result.EndTime = DateTime.UtcNow;
        }

        private static void ApplyMetadata(string path, System.IO.Stream stream, bool restorePermissions, bool restoreSymlinkMetadata, bool dryrun)
        {
            using(var tr = new System.IO.StreamReader(stream))
            using(var jr = new Newtonsoft.Json.JsonTextReader(tr))
            {
                var metadata = new Newtonsoft.Json.JsonSerializer().Deserialize<Dictionary<string, string>>(jr);
                string k;
                long t;
                System.IO.FileAttributes fa;

                // If this is dry-run, we stop after having deserialized the metadata
                if (dryrun)
                    return;

                var isDirTarget = path.EndsWith(DIRSEP, StringComparison.Ordinal);
                var targetpath = isDirTarget ? path.Substring(0, path.Length - 1) : path;

                // Make the symlink first, otherwise we cannot apply metadata to it
                if (metadata.TryGetValue("CoreSymlinkTarget", out k))
                    SystemIO.IO_OS.CreateSymlink(targetpath, k, isDirTarget);
                // If the target is a folder, make sure we create it first
                else if (isDirTarget && !SystemIO.IO_OS.DirectoryExists(targetpath))
                    SystemIO.IO_OS.DirectoryCreate(targetpath);

                // Avoid setting restoring symlink metadata, as that writes the symlink target, not the symlink itself
                if (!restoreSymlinkMetadata && Snapshots.SnapshotUtility.IsSymlink(SystemIO.IO_OS, targetpath))
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "no-symlink-metadata-restored", "Not applying metadata to symlink: {0}", targetpath);
                    return;
                }

                if (metadata.TryGetValue("CoreLastWritetime", out k) && long.TryParse(k, out t))
                {
                    if (isDirTarget)
                        SystemIO.IO_OS.DirectorySetLastWriteTimeUtc(targetpath, new DateTime(t, DateTimeKind.Utc));
                    else
                        SystemIO.IO_OS.FileSetLastWriteTimeUtc(targetpath, new DateTime(t, DateTimeKind.Utc));
                }

                if (metadata.TryGetValue("CoreCreatetime", out k) && long.TryParse(k, out t))
                {
                    if (isDirTarget)
                        SystemIO.IO_OS.DirectorySetCreationTimeUtc(targetpath, new DateTime(t, DateTimeKind.Utc));
                    else
                        SystemIO.IO_OS.FileSetCreationTimeUtc(targetpath, new DateTime(t, DateTimeKind.Utc));
                }

                if (metadata.TryGetValue("CoreAttributes", out k) && Enum.TryParse(k, true, out fa))
                    SystemIO.IO_OS.SetFileAttributes(targetpath, fa);

                SystemIO.IO_OS.SetMetadata(path, metadata, restorePermissions);
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
                        if (SystemIO.IO_OS.FileExists(sourcepath))
                        {
                            var folderpath = SystemIO.IO_OS.PathGetDirectoryName(targetpath);
                            if (!options.Dryrun && !SystemIO.IO_OS.DirectoryExists(folderpath))
                            {
                                Logging.Log.WriteWarningMessage(LOGTAG, "CreateMissingFolder", null, "Creating missing folder {0} for  file {1}", folderpath, targetpath);
                                SystemIO.IO_OS.DirectoryCreate(folderpath);
                            }
                        
                            using(var targetstream = options.Dryrun ? null : SystemIO.IO_OS.FileOpenWrite(targetpath))
                            {
                                try
                                {
                                    using(var sourcestream = SystemIO.IO_OS.FileOpenRead(sourcepath))
                                    {
                                        foreach(var block in entry.Blocks)
                                        {
                                            if (result.TaskControlRendevouz() == TaskControlState.Stop)
                                                return;

                                            //TODO: Handle metadata

                                            if (sourcestream.Length > block.Offset)
                                            {
                                                sourcestream.Position = block.Offset;

                                                int size = Library.Utility.Utility.ForceStreamRead(sourcestream, blockbuffer, blockbuffer.Length);
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
                                    Logging.Log.WriteWarningMessage(LOGTAG, "PatchingFileLocalFailed", ex, "Failed to patch file: \"{0}\" with data from local file \"{1}\", message: {2}", targetpath, sourcepath, ex.Message);
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
                            Logging.Log.WriteVerboseMessage(LOGTAG, "LocalSourceMissing", "Local source file not found: {0}", sourcepath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "PatchingFileLocalFailed", ex, "Failed to patch file: \"{0}\" with local data, message: {1}", targetpath, ex.Message);
                        if (ex is System.Threading.ThreadAbortException)
                            throw;
                        if (options.UnittestMode)
                            throw;
                    }
                    
                    if (patched)
                        Logging.Log.WriteVerboseMessage(LOGTAG, "FilePatchedWithLocal", "Target file is patched with some local data: {0}", targetpath);
                    else
                        Logging.Log.WriteVerboseMessage(LOGTAG, "FilePatchedWithLocal", "Target file is not patched any local data: {0}", targetpath);
                    
                    if (patched && options.Dryrun)
                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldPatchWithLocal", "Would patch file with local data: {0}", targetpath);
                }
                
                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
                blockmarker.Commit();
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
                        
                        var folderpath = SystemIO.IO_OS.PathGetDirectoryName(targetpath);
                        if (!options.Dryrun && !SystemIO.IO_OS.DirectoryExists(folderpath))
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "CreateMissingFolder", null, "Creating missing folder {0} for  file {1}", folderpath, targetpath);
                            SystemIO.IO_OS.DirectoryCreate(folderpath);
                        }
                    
                        using (var file = options.Dryrun ? null : SystemIO.IO_OS.FileOpenWrite(targetpath))
                            foreach (var targetblock in restorelist.Blocks)
                            {
                                foreach (var source in targetblock.Blocksources)
                                {
                                    try
                                    {
                                        if (result.TaskControlRendevouz() == TaskControlState.Stop)
                                            return;

                                        if (SystemIO.IO_OS.FileExists(source.Path))
                                        {
                                            if (source.IsMetadata)
                                            {
                                                // TODO: Handle this by reconstructing 
                                                // metadata from file and checking the hash

                                                continue;
                                            }
                                            else
                                            {
                                                using (var sourcefile = SystemIO.IO_OS.FileOpenRead(source.Path))
                                                {
                                                    sourcefile.Position = source.Offset;
                                                    int size = Library.Utility.Utility.ForceStreamRead(sourcefile, blockbuffer, blockbuffer.Length);
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
                                                                {
                                                                    file.Position = targetblock.Offset;
                                                                    file.Write(blockbuffer, 0, size);
                                                                }
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
                                        Logging.Log.WriteWarningMessage(LOGTAG, "PatchingFileLocalFailed", ex, "Failed to patch file: \"{0}\" with data from local file \"{1}\", message: {2}", targetpath, source.Path, ex.Message);
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
                        Logging.Log.WriteWarningMessage(LOGTAG, "PatchingFileLocalFailed", ex, "Failed to patch file: \"{0}\" with local data, message: {1}", targetpath, ex.Message);
                        if (options.UnittestMode)
                            throw;
                    }
                    
                    if (patched)
                        Logging.Log.WriteVerboseMessage(LOGTAG, "FilePatchedWithLocal", "Target file is patched with some local data: {0}", targetpath);
                    else
                        Logging.Log.WriteVerboseMessage(LOGTAG, "FilePatchedWithLocal", "Target file is not patched any local data: {0}", targetpath);
                        
                    if (patched && options.Dryrun)
                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldPatchWithLocal", string.Format("Would patch file with local data: {0}", targetpath));
                }

                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
                blockmarker.Commit();
            }
        }

        private static void PrepareBlockAndFileList(LocalRestoreDatabase database, Options options, Library.Utility.IFilter filter, RestoreResults result)
        {
            // Create a temporary table FILES by selecting the files from fileset that matches a specific operation id
            // Delete all entries from the temp table that are excluded by the filter(s)
            using(new Logging.Timer(LOGTAG, "PrepareRestoreFileList", "PrepareRestoreFileList"))
            {
                var c = database.PrepareRestoreFilelist(options.Time, options.Version, filter);
                result.OperationProgressUpdater.UpdatefileCount(c.Item1, c.Item2, true);
            }

            using(new Logging.Timer(LOGTAG, "SetTargetPaths", "SetTargetPaths"))
                if (!string.IsNullOrEmpty(options.Restorepath))
                {
                    // Find the largest common prefix
                    var largest_prefix = options.DontCompressRestorePaths ? "" : database.GetLargestPrefix();

                    Logging.Log.WriteVerboseMessage(LOGTAG, "MappingRestorePath", "Mapping restore path prefix to \"{0}\" to \"{1}\"", largest_prefix, Util.AppendDirSeparator(options.Restorepath));
    
                    // Set the target paths, special care with C:\ and /
                    database.SetTargetPaths(largest_prefix, Util.AppendDirSeparator(options.Restorepath));
                }
                else
                {
                    database.SetTargetPaths("", "");
                }

            // Create a temporary table BLOCKS that lists all blocks that needs to be recovered
            using(new Logging.Timer(LOGTAG, "FindMissingBlocks", "FindMissingBlocks"))
                database.FindMissingBlocks(options.SkipMetadata);

            // Create temporary tables and triggers that automatically track progress
            using (new Logging.Timer(LOGTAG, "CreateProgressTracker", "CreateProgressTracker"))
                database.CreateProgressTracker(false);

        }

        private static void CreateDirectoryStructure(LocalRestoreDatabase database, Options options, RestoreResults result)
        {
            // This part is not protected by try/catch as we need the target folder to exist
            if (!string.IsNullOrEmpty(options.Restorepath))
                if (!SystemIO.IO_OS.DirectoryExists(options.Restorepath))
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "CreateFolder", "Creating folder: {0}", options.Restorepath);
                        
                    if (options.Dryrun)
                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldCreateFolder", "Would create folder: {0}", options.Restorepath);
                    else
                        SystemIO.IO_OS.DirectoryCreate(options.Restorepath);
                }
        
            foreach (var folder in database.GetTargetFolders())
            {
                try
                {
                    if (result.TaskControlRendevouz() == TaskControlState.Stop)
                        return;
                    
                    if (!SystemIO.IO_OS.DirectoryExists(folder))
                    {
                        result.RestoredFolders++;
                        
                        Logging.Log.WriteVerboseMessage(LOGTAG, "CreateFolder", "Creating folder: {0}", folder);
                            
                        if (options.Dryrun)
                            Logging.Log.WriteDryrunMessage(LOGTAG, "WouldCreateFolder", "Would create folder: {0}", folder);
                        else
                            SystemIO.IO_OS.DirectoryCreate(folder);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "FolderCreateFailed", ex, "Failed to create folder: \"{0}\", message: {1}", folder, ex.Message);
                    if (options.UnittestMode)
                        throw;
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
                    var targetfilelength = restorelist.Length;
                    if (SystemIO.IO_OS.FileExists(targetpath))
                    {
                        try
                        {
                            if (result.TaskControlRendevouz() == TaskControlState.Stop)
                                return;

                            var currentfilelength = SystemIO.IO_OS.FileLength(targetpath);
                            var wasTruncated = false;

                            // Adjust file length in overwrite mode if necessary (smaller is ok, will be extended during restore)
                            // We do it before scanning for blocks. This allows full verification on files that only needs to 
                            // be truncated (i.e. forthwritten log files).
                            if (!rename && currentfilelength > targetfilelength)
                            {
                                var currentAttr = SystemIO.IO_OS.GetFileAttributes(targetpath);
                                if ((currentAttr & System.IO.FileAttributes.ReadOnly) != 0) // clear readonly attribute
                                {
                                    if (options.Dryrun) 
                                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldResetReadOnlyAttribute", "Would reset read-only attribute on file: {0}", targetpath);
                                    else SystemIO.IO_OS.SetFileAttributes(targetpath, currentAttr & ~System.IO.FileAttributes.ReadOnly);
                                }
                                if (options.Dryrun)
                                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldTruncateFile", "Would truncate file '{0}' to length of {1:N0} bytes", targetpath, targetfilelength);
                                else
                                {
                                    using (var file = SystemIO.IO_OS.FileOpenWrite(targetpath))
                                        file.SetLength(targetfilelength);
                                    currentfilelength = targetfilelength;
                                }
                                wasTruncated = true;
                            }

                            // If file size does not match and we have to rename on conflict, 
                            // the whole scan can be skipped here because all blocks have to be restored anyway.
                            // For the other cases, we will check block and and file hashes and look for blocks
                            // to be restored and files that can already be verified.
                            if (!rename || currentfilelength == targetfilelength)
                            {
                                // a file hash for verification will only be necessary if the file has exactly
                                // the wanted size so we have a chance to already mark the file as data-verified.
                                bool calcFileHash = (currentfilelength == targetfilelength);
                                if (calcFileHash) filehasher.Initialize();

                                using (var file = SystemIO.IO_OS.FileOpenRead(targetpath))
                                using (var block = new Blockprocessor(file, blockbuffer))
                                    foreach (var targetblock in restorelist.Blocks)
                                    {
                                        var size = block.Readblock();
                                        if (size <= 0)
                                            break;

                                        //TODO: Handle Metadata

                                        bool blockhashmatch = false;
                                        if (size == targetblock.Size)
                                        {
                                            // Parallelize file hash calculation on rename. Running read-only on same array should not cause conflicts or races.
                                            // Actually, in future always calculate the file hash and mark the file data as already verified.

                                            System.Threading.Tasks.Task calcFileHashTask = null;
                                            if (calcFileHash)
                                                calcFileHashTask = System.Threading.Tasks.Task.Run(
                                                    () => filehasher.TransformBlock(blockbuffer, 0, size, blockbuffer, 0));

                                            var key = Convert.ToBase64String(blockhasher.ComputeHash(blockbuffer, 0, size));

                                            if (calcFileHashTask != null) calcFileHashTask.Wait(); // wait because blockbuffer will be overwritten.

                                            if (key == targetblock.Hash)
                                            {
                                                blockmarker.SetBlockRestored(targetfileid, targetblock.Index, key, size, false);
                                                blockhashmatch = true;
                                            }
                                        }
                                        if (calcFileHash && !blockhashmatch) // will not be necessary anymore
                                        {
                                            filehasher.TransformFinalBlock(blockbuffer, 0, 0); // So a new initialize will not throw
                                            calcFileHash = false;
                                            if (rename) // file does not match. So break.
                                                break;
                                        }
                                    }

                                bool fullfilehashmatch = false;
                                if (calcFileHash) // now check if files are identical
                                {
                                    filehasher.TransformFinalBlock(blockbuffer, 0, 0);
                                    var filekey = Convert.ToBase64String(filehasher.Hash);
                                    fullfilehashmatch = (filekey == targetfilehash);
                                }

                                if (!rename && !fullfilehashmatch && !wasTruncated) // Reset read-only attribute (if set) to overwrite
                                {
                                    var currentAttr = SystemIO.IO_OS.GetFileAttributes(targetpath);
                                    if ((currentAttr & System.IO.FileAttributes.ReadOnly) != 0)
                                    {
                                        if (options.Dryrun) 
                                            Logging.Log.WriteDryrunMessage(LOGTAG, "WouldResetReadOnlyAttribyte", "Would reset read-only attribute on file: {0}", targetpath);
                                        else SystemIO.IO_OS.SetFileAttributes(targetpath, currentAttr & ~System.IO.FileAttributes.ReadOnly);
                                    }
                                }

                                if (fullfilehashmatch)
                                {
                                    //TODO: Check metadata to trigger rename? If metadata changed, it will still be restored for the file in-place.
                                    blockmarker.SetFileDataVerified(targetfileid);
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "TargetExistsInCorrectVersion", "Target file exists{1} and is correct version: {0}", targetpath, wasTruncated ? " (but was truncated)" : "");
                                    rename = false;
                                }
                                else if (rename)
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
                            Logging.Log.WriteWarningMessage(LOGTAG, "TargetFileReadError", ex, "Failed to read target file: \"{0}\", message: {1}", targetpath, ex.Message);
                            if (ex is System.Threading.ThreadAbortException)
                                throw;
                            if (options.UnittestMode)
                                throw;
                        }                        
                    }
                    else
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "MissingTargetFile", "Target file does not exist: {0}", targetpath);
                        rename = false;
                    }
                    
                    if (rename)
                    {
                        //Select a new filename
                        var ext = SystemIO.IO_OS.PathGetExtension(targetpath) ?? "";
                        if (!string.IsNullOrEmpty(ext) && !ext.StartsWith(".", StringComparison.Ordinal))
                            ext = "." + ext;
                        
                        // First we try with a simple date append, assuming that there are not many conflicts there
                        var newname = SystemIO.IO_OS.PathChangeExtension(targetpath, null) + "." + database.RestoreTime.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                        var tr = newname + ext;
                        var c = 0;
                        while (SystemIO.IO_OS.FileExists(tr) && c < 1000)
                        {
                            try
                            {
                                // If we have a file with the correct name, 
                                // it is most likely the file we want
                                filehasher.Initialize();
                                
                                string key;
                                using(var file = SystemIO.IO_OS.FileOpenRead(tr))
                                    key = Convert.ToBase64String(filehasher.ComputeHash(file));
                                    
                                if (key == targetfilehash)
                                {
                                    //TODO: Also needs metadata check to make correct decision.
                                    //      We stick to the policy to restore metadata in place, if data ok. So, metadata block may be restored.
                                    blockmarker.SetAllBlocksRestored(targetfileid, false);
                                    blockmarker.SetFileDataVerified(targetfileid);
                                    break;
                                }
                            }
                            catch(Exception ex)
                            {
                                Logging.Log.WriteWarningMessage(LOGTAG, "FailedToReadRestoreTarget", ex, "Failed to read candidate restore target {0}", tr);
                                if (options.UnittestMode)
                                    throw;
                            }
                            tr = newname + " (" + (c++).ToString() + ")" + ext;
                        }
                        
                        newname = tr;
                        
                        Logging.Log.WriteVerboseMessage(LOGTAG, "TargetFileRetargeted", "Target file exists and will be restored to: {0}", newname);
                        database.UpdateTargetPath(targetfileid, newname); 
                    }                        
                    
                }

                blockmarker.UpdateProcessed(result.OperationProgressUpdater);
                blockmarker.Commit();
            }
        }
    }
}
