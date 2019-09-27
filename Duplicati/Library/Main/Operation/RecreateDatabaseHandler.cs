﻿using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;

namespace Duplicati.Library.Main.Operation
{
    internal class RecreateDatabaseHandler : IDisposable
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<RecreateDatabaseHandler>();

        private readonly string m_backendurl;
        private readonly Options m_options;
        private readonly RecreateDatabaseResults m_result;

        public delegate IEnumerable<KeyValuePair<long, IParsedVolume>> NumberedFilterFilelistDelegate(IEnumerable<IParsedVolume> filelist);
        public delegate void BlockVolumePostProcessor(string volumename,BlockVolumeReader reader);

        public RecreateDatabaseHandler(string backendurl, Options options, RecreateDatabaseResults result)
        {
            m_options = options;
            m_backendurl = backendurl;
            m_result = result;
        }

        /// <summary>
        /// Run the recreate procedure
        /// </summary>
        /// <param name="path">Path to the database that will be created</param>
        /// <param name="filelistfilter">A filter that can be used to disregard certain remote files, intended to be used to select a certain filelist</param>
        /// <param name="filter">Filters the files in a filelist to prevent downloading unwanted data</param>
        /// <param name="blockprocessor">A callback hook that can be used to work with downloaded block volumes, intended to be use to recover data blocks while processing blocklists</param>
        public void Run(string path, Library.Utility.IFilter filter = null, NumberedFilterFilelistDelegate filelistfilter = null, BlockVolumePostProcessor blockprocessor = null)
        {
            if (System.IO.File.Exists(path))
                throw new UserInformationException(string.Format("Cannot recreate database because file already exists: {0}", path), "RecreateTargetDatabaseExists");

            using(var db = new LocalDatabase(path, "Recreate", true))
            {
                m_result.SetDatabase(db);
                DoRun(db, false, filter, filelistfilter, blockprocessor);
                db.WriteResults();
            }
        }

        /// <summary>
        /// Updates a database with new path information from a remote fileset
        /// </summary>
        /// <param name="filelistfilter">A filter that can be used to disregard certain remote files, intended to be used to select a certain filelist</param>
        /// <param name="filter">Filters the files in a filelist to prevent downloading unwanted data</param>
        /// <param name="blockprocessor">A callback hook that can be used to work with downloaded block volumes, intended to be use to recover data blocks while processing blocklists</param>
        public void RunUpdate(Library.Utility.IFilter filter = null, NumberedFilterFilelistDelegate filelistfilter = null, BlockVolumePostProcessor blockprocessor = null)
        {
            if (!m_options.RepairOnlyPaths)
                throw new UserInformationException(string.Format("Can only update with paths, try setting {0}", "--repair-only-paths"), "RepairUpdateRequiresPathsOnly");

            using(var db = new LocalDatabase(m_options.Dbpath, "Recreate", true))
            {
                m_result.SetDatabase(db);

                if (db.FindMatchingFilesets(m_options.Time, m_options.Version).Any())
                    throw new UserInformationException("The version(s) being updated to, already exists", "UpdateVersionAlreadyExists");

                // Mark as incomplete
                db.PartiallyRecreated = true;

                Utility.UpdateOptionsFromDb(db, m_options, null);
                DoRun(db, true, filter, filelistfilter, blockprocessor);
                db.WriteResults();
            }
        }

        /// <summary>
        /// Run the recreate procedure
        /// </summary>
        /// <param name="dbparent">The database to restore into</param>
        /// <param name="updating">True if this is an update call, false otherwise</param>
        /// <param name="filter">A filter that can be used to disregard certain remote files, intended to be used to select a certain filelist</param>
        /// <param name="filelistfilter">Filters the files in a filelist to prevent downloading unwanted data</param>
        /// <param name="blockprocessor">A callback hook that can be used to work with downloaded block volumes, intended to be use to recover data blocks while processing blocklists</param>
        internal void DoRun(LocalDatabase dbparent, bool updating, Library.Utility.IFilter filter = null, NumberedFilterFilelistDelegate filelistfilter = null, BlockVolumePostProcessor blockprocessor = null)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Recreate_Running);

            //We build a local database in steps.
            using(var restoredb = new LocalRecreateDatabase(dbparent, m_options))
            using(var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, restoredb))
            {
                restoredb.RepairInProgress = true;
                var autoDetectBlockSize = !(m_options.HasBlocksize && restoredb.GetDbOptions().ContainsKey("blocksize"));                    
                var volumeIds = new Dictionary<string, long>();

                var rawlist = backend.List();
        
                //First step is to examine the remote storage to see what
                // kind of data we can find
                var remotefiles =
                (from x in rawlist
                let n = VolumeBase.ParseFilename(x)
                where
                    n != null
                        &&
                    n.Prefix == m_options.Prefix
                select n).ToArray(); //ToArray() ensures that we do not remote-request it multiple times

                if (remotefiles.Length == 0)
                {
                    if (rawlist.Count == 0)
                        throw new UserInformationException("No files were found at the remote location, perhaps the target url is incorrect?", "EmptyRemoteLocation");
                    else
                    {
                        var tmp = 
                    (from x in rawlist
                        let n = VolumeBase.ParseFilename(x)
                    where
                        n != null
                    select n.Prefix).ToArray();
                
                        var types = tmp.Distinct().ToArray();
                        if (tmp.Length == 0)
                            throw new UserInformationException(string.Format("Found {0} files at the remote storage, but none that could be parsed", rawlist.Count), "EmptyRemoteLocation");
                        else if (types.Length == 1)
                            throw new UserInformationException(string.Format("Found {0} parse-able files with the prefix {1}, did you forget to set the backup prefix?", tmp.Length, types[0]), "EmptyRemoteLocationWithPrefix");
                        else
                            throw new UserInformationException(string.Format("Found {0} parse-able files (of {1} files) with different prefixes: {2}, did you forget to set the backup prefix?", tmp.Length, rawlist.Count, string.Join(", ", types)), "EmptyRemoteLocationWithPrefix");
                    }
                }

                //Then we select the filelist we should work with,
                // and create the filelist table to fit
                IEnumerable<IParsedVolume> filelists =
                    from n in remotefiles
                    where n.FileType == RemoteVolumeType.Files
                    orderby n.Time descending
                    select n;

                if (!filelists.Any())
                    throw new UserInformationException("No filelists found on the remote destination", "EmptyRemoteLocation");
                
                if (filelistfilter != null)
                    filelists = filelistfilter(filelists).Select(x => x.Value).ToArray();

                if (!filelists.Any())
                    throw new UserInformationException("No filelists", "NoMatchingRemoteFilelists");

                // If we are updating, all files should be accounted for
                foreach(var fl in remotefiles)
                    volumeIds[fl.File.Name] = updating ? restoredb.GetRemoteVolumeID(fl.File.Name) : restoredb.RegisterRemoteVolume(fl.File.Name, fl.FileType, fl.File.Size, RemoteVolumeState.Uploaded);

                var hasUpdatedOptions = false;

                if (updating)
                {
                    Utility.UpdateOptionsFromDb(restoredb, m_options);
                    Utility.VerifyParameters(restoredb, m_options);
                }

                //Record all blocksets and files needed
                using(var tr = restoredb.BeginTransaction())
                {
                    var filelistWork = (from n in filelists orderby n.Time select new RemoteVolume(n.File) as IRemoteVolume).ToList();
                    Logging.Log.WriteInformationMessage(LOGTAG, "RebuildStarted", "Rebuild database started, downloading {0} filelists", filelistWork.Count);

                    var progress = 0;

                    // Register the files we are working with, if not already updated
                    if (updating)
                    {
                        foreach(var n in filelists)
                            if (volumeIds[n.File.Name] == -1)
                                volumeIds[n.File.Name] = restoredb.RegisterRemoteVolume(n.File.Name, n.FileType, RemoteVolumeState.Uploaded, n.File.Size, new TimeSpan(0), tr);
                    }
                                
                    var isFirstFilelist = true;
                    var blocksize = m_options.Blocksize;
                    var hashes_pr_block = blocksize / m_options.BlockhashSize;

                    foreach(var entry in new AsyncDownloader(filelistWork, backend))
                        try
                        {
                            if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                            {
                                backend.WaitForComplete(restoredb, null);
                                m_result.EndTime = DateTime.UtcNow;
                                return;
                            }    
                        
                            progress++;
                            if (filelistWork.Count == 1 && m_options.RepairOnlyPaths)
                                m_result.OperationProgressUpdater.UpdateProgress(0.5f);
                            else
                                m_result.OperationProgressUpdater.UpdateProgress(((float)progress / filelistWork.Count()) * (m_options.RepairOnlyPaths ? 1f : 0.2f));

                            using(var tmpfile = entry.TempFile)
                            {
                                isFirstFilelist = false;

                                if (entry.Hash != null && entry.Size > 0)
                                    restoredb.UpdateRemoteVolume(entry.Name, RemoteVolumeState.Verified, entry.Size, entry.Hash, tr);

                                var parsed = VolumeBase.ParseFilename(entry.Name);

                                if (!hasUpdatedOptions && (!updating || autoDetectBlockSize)) 
                                {
                                    VolumeReaderBase.UpdateOptionsFromManifest(parsed.CompressionModule, tmpfile, m_options);
                                    hasUpdatedOptions = true;
                                    // Recompute the cached sizes
                                    blocksize = m_options.Blocksize;
                                    hashes_pr_block = blocksize / m_options.BlockhashSize;
                                }


                                // Create timestamped operations based on the file timestamp
                                var filesetid = restoredb.CreateFileset(volumeIds[entry.Name], parsed.Time, tr);
                                using(var filelistreader = new FilesetVolumeReader(parsed.CompressionModule, tmpfile, m_options))
                                    foreach(var fe in filelistreader.Files.Where(x => Library.Utility.FilterExpression.Matches(filter, x.Path)))
                                    {
                                        try
                                        {
                                            var expectedmetablocks = (fe.Metasize + blocksize - 1)  / blocksize;
                                            var expectedmetablocklisthashes = (expectedmetablocks + hashes_pr_block - 1) / hashes_pr_block;
                                            if (expectedmetablocks <= 1) expectedmetablocklisthashes = 0;

                                            var metadataid = long.MinValue;
                                            switch (fe.Type)
                                            {
                                                case FilelistEntryType.Folder:
                                                    metadataid = restoredb.AddMetadataset(fe.Metahash, fe.Metasize, fe.MetaBlocklistHashes, expectedmetablocklisthashes, tr);
                                                    restoredb.AddDirectoryEntry(filesetid, fe.Path, fe.Time, metadataid, tr);
                                                    break;
                                                case FilelistEntryType.File:
                                                    var expectedblocks = (fe.Size + blocksize - 1) / blocksize;
                                                    var expectedblocklisthashes = (expectedblocks + hashes_pr_block - 1) / hashes_pr_block;
                                                    if (expectedblocks <= 1) expectedblocklisthashes = 0;

                                                    var blocksetid = restoredb.AddBlockset(fe.Hash, fe.Size, fe.BlocklistHashes, expectedblocklisthashes, tr);
                                                    metadataid = restoredb.AddMetadataset(fe.Metahash, fe.Metasize, fe.MetaBlocklistHashes, expectedmetablocklisthashes, tr);
                                                    restoredb.AddFileEntry(filesetid, fe.Path, fe.Time, blocksetid, metadataid, tr);

                                                    if (fe.Size <= blocksize)
                                                    {
                                                        if (!string.IsNullOrWhiteSpace(fe.Blockhash))
                                                            restoredb.AddSmallBlocksetLink(fe.Hash, fe.Blockhash, fe.Blocksize, tr);
                                                        else if (m_options.BlockHashAlgorithm == m_options.FileHashAlgorithm)
                                                            restoredb.AddSmallBlocksetLink(fe.Hash, fe.Hash, fe.Size, tr);
                                                        else
                                                            Logging.Log.WriteWarningMessage(LOGTAG, "MissingBlockHash", null, "No block hash found for file: {0}", fe.Path);
                                                    }

                                                    break;
                                                case FilelistEntryType.Symlink:
                                                    metadataid = restoredb.AddMetadataset(fe.Metahash, fe.Metasize, fe.MetaBlocklistHashes, expectedmetablocklisthashes, tr);
                                                    restoredb.AddSymlinkEntry(filesetid, fe.Path, fe.Time, metadataid, tr);
                                                    break;
                                                default:
                                                        Logging.Log.WriteWarningMessage(LOGTAG, "SkippingUnknownFileEntry", null, "Skipping file-entry with unknown type {0}: {1} ", fe.Type, fe.Path);
                                                    break;
                                            }

                                            if (fe.Metasize <= blocksize && (fe.Type == FilelistEntryType.Folder || fe.Type == FilelistEntryType.File || fe.Type == FilelistEntryType.Symlink))
                                            {
                                                if (!string.IsNullOrWhiteSpace(fe.Metablockhash))
                                                    restoredb.AddSmallBlocksetLink(fe.Metahash, fe.Metablockhash, fe.Metasize, tr);
                                                else if (m_options.BlockHashAlgorithm == m_options.FileHashAlgorithm)
                                                    restoredb.AddSmallBlocksetLink(fe.Metahash, fe.Metahash, fe.Metasize, tr);
                                                else
                                                    Logging.Log.WriteWarningMessage(LOGTAG, "MissingMetadataBlockHash", null, "No block hash found for file metadata: {0}", fe.Path);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Logging.Log.WriteWarningMessage(LOGTAG, "FileEntryProcessingFailed", ex, "Failed to process file-entry: {0}", fe.Path);
                                        }
                                    }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "FileProcessingFailed", ex, "Failed to process file: {0}", entry.Name);
                            if (ex is System.Threading.ThreadAbortException)
                            {
                                m_result.EndTime = DateTime.UtcNow;
                                throw;
                            }

                            if (isFirstFilelist && ex is System.Security.Cryptography.CryptographicException)
                            {
                                m_result.EndTime = DateTime.UtcNow;
                                throw;
                            }

                            if (m_options.UnittestMode)
                                throw;

                        }

                    //Make sure we write the config
                    if (!updating)
                        Utility.VerifyParameters(restoredb, m_options, tr);

                    using(new Logging.Timer(LOGTAG, "CommitUpdateFilesetFromRemote", "CommitUpdateFilesetFromRemote"))
                        tr.Commit();
                }
            
                if (!m_options.RepairOnlyPaths)
                {
                    var hashalg = Library.Utility.HashAlgorithmHelper.Create(m_options.BlockHashAlgorithm);
                    if (hashalg == null)
                        throw new UserInformationException(Strings.Common.InvalidHashAlgorithm(m_options.BlockHashAlgorithm), "BlockHashAlgorithmNotSupported");
                    var hashsize = hashalg.HashSize / 8;

                    //Grab all index files, and update the block table
                    using(var tr = restoredb.BeginTransaction())
                    {
                        var indexfiles = (
                                         from n in remotefiles
                                          where n.FileType == RemoteVolumeType.Index
                                          select new RemoteVolume(n.File) as IRemoteVolume).ToList();

                        Logging.Log.WriteInformationMessage(LOGTAG, "FilelistsRestored", "Filelists restored, downloading {0} index files", indexfiles.Count);

                        var progress = 0;
                                    
                        foreach(var sf in new AsyncDownloader(indexfiles, backend))
                            try
                            {
                                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                                {
                                    backend.WaitForComplete(restoredb, null);
                                    m_result.EndTime = DateTime.UtcNow;
                                    return;
                                }

                                progress++;
                                m_result.OperationProgressUpdater.UpdateProgress((((float)progress / indexfiles.Count) * 0.5f) + 0.2f);

                                using(var tmpfile = sf.TempFile)
                                {
                                    if (sf.Hash != null && sf.Size > 0)
                                        restoredb.UpdateRemoteVolume(sf.Name, RemoteVolumeState.Verified, sf.Size, sf.Hash, tr);
                
                                    using(var svr = new IndexVolumeReader(RestoreHandler.GetCompressionModule(sf.Name), tmpfile, m_options, hashsize))
                                    {
                                        foreach(var a in svr.Volumes)
                                        {
                                            var filename = a.Filename;
                                            var volumeID = restoredb.GetRemoteVolumeID(filename);

                                            // No such file
                                            if (volumeID < 0)
                                                volumeID = ProbeForMatchingFilename(ref filename, restoredb);

											var missing = false;                                            
                                            // Still broken, register a missing item
                                            if (volumeID < 0)
                                            {
                                                var p = VolumeBase.ParseFilename(filename);
                                                if (p == null)
                                                    throw new Exception(string.Format("Unable to parse filename: {0}", filename));
    											Logging.Log.WriteErrorMessage(LOGTAG, "MissingFileDetected", null, "Remote file referenced as {0} by {1}, but not found in list, registering a missing remote file", filename, sf.Name);
												missing = true;
											    volumeID = restoredb.RegisterRemoteVolume(filename, p.FileType, RemoteVolumeState.Temporary, tr);
                                            }
                                            
                                            //Add all block/volume mappings
                                            foreach(var b in a.Blocks)
                                                restoredb.UpdateBlock(b.Key, b.Value, volumeID, tr);

										    restoredb.UpdateRemoteVolume(filename, missing ? RemoteVolumeState.Temporary : RemoteVolumeState.Verified, a.Length, a.Hash, tr);
                                            restoredb.AddIndexBlockLink(restoredb.GetRemoteVolumeID(sf.Name), volumeID, tr);
                                        }
                                
                                        //If there are blocklists in the index file, update the blocklists
                                        foreach(var b in svr.BlockLists)
                                            restoredb.UpdateBlockset(b.Hash, b.Blocklist, tr);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                //Not fatal
                                Logging.Log.WriteErrorMessage(LOGTAG, "IndexFileProcessingFailed", ex, "Failed to process index file: {0}", sf.Name);
                                if (ex is System.Threading.ThreadAbortException)
                                {
                                    m_result.EndTime = DateTime.UtcNow;
                                    throw;
                                }

                                if (m_options.UnittestMode)
                                    throw;
                            }

                        using(new Logging.Timer(LOGTAG, "CommitRecreateDb", "CommitRecreatedDb"))
                            tr.Commit();
                    
                        // TODO: In some cases, we can avoid downloading all index files, 
                        // if we are lucky and pick the right ones
                    }

                    // In some cases we have a stale reference from an index file to a deleted block file
                    if (!m_options.UnittestMode)
                        restoredb.CleanupMissingVolumes();

                    // We have now grabbed as much information as possible,
                    // if we are still missing data, we must now fetch block files
                    restoredb.FindMissingBlocklistHashes(hashsize, m_options.Blocksize, null);
                
                    //We do this in three passes
                    for(var i = 0; i < 3; i++)
                    {
                        // Grab the list matching the pass type
                        var lst = restoredb.GetMissingBlockListVolumes(i, m_options.Blocksize, hashsize).ToList();
                        if (lst.Count > 0)
                        {
                            var fullist = ": " + string.Join(", ", lst.Select(x => x.Name));
                            switch (i)
                            {                                
                                case 0:
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "ProcessingRequiredBlocklistVolumes", "Processing required {0} blocklist volumes{1}", lst.Count, fullist);
                                    Logging.Log.WriteInformationMessage(LOGTAG, "ProcessingRequiredBlocklistVolumes", "Processing required {0} blocklist volumes{1}", lst.Count, m_options.FullResult ? fullist : string.Empty);
                                    break;
                                case 1:
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "ProbingCandicateBlocklistVolumes", "Probing {0} candidate blocklist volumes{1}", lst.Count, fullist);
                                    Logging.Log.WriteInformationMessage(LOGTAG, "ProbingCandicateBlocklistVolumes", "Probing {0} candidate blocklist volumes{1}", lst.Count, m_options.FullResult ? fullist : string.Empty);
                                    break;
                                default:
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "ProcessingAllBlocklistVolumes", "Processing all of the {0} volumes for blocklists{1}", lst.Count, fullist);
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "ProcessingAllBlocklistVolumes", "Processing all of the {0} volumes for blocklists{1}", lst.Count, m_options.FullResult ? fullist : string.Empty);
                                    break;
                            }
                        }

                        var progress = 0;
                        foreach (var sf in new AsyncDownloader(lst, backend))
                        {
                            try
                            {
                                using (var tmpfile = sf.TempFile)
                                using (var rd = new BlockVolumeReader(RestoreHandler.GetCompressionModule(sf.Name), tmpfile, m_options))
                                using (var tr = restoredb.BeginTransaction())
                                {
                                    if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                                    {
                                        backend.WaitForComplete(restoredb, null);
                                        m_result.EndTime = DateTime.UtcNow;
                                        return;
                                    }

                                    progress++;
                                    m_result.OperationProgressUpdater.UpdateProgress((((float)progress / lst.Count) * 0.1f) + 0.7f + (i * 0.1f));

                                    var volumeid = restoredb.GetRemoteVolumeID(sf.Name);

                                    restoredb.UpdateRemoteVolume(sf.Name, RemoteVolumeState.Uploaded, sf.Size, sf.Hash, tr);

                                    // Update the block table so we know about the block/volume map
                                    foreach (var h in rd.Blocks)
                                        restoredb.UpdateBlock(h.Key, h.Value, volumeid, tr);

                                    // Grab all known blocklists from the volume
                                    foreach (var blocklisthash in restoredb.GetBlockLists(volumeid))
                                        restoredb.UpdateBlockset(blocklisthash, rd.ReadBlocklist(blocklisthash, hashsize), tr);

                                    // Update tables so we know if we are done
                                    restoredb.FindMissingBlocklistHashes(hashsize, m_options.Blocksize, tr);

                                    using (new Logging.Timer(LOGTAG, "CommitRestoredBlocklist", "CommitRestoredBlocklist"))
                                        tr.Commit();

                                    //At this point we can patch files with data from the block volume
                                    if (blockprocessor != null)
                                        blockprocessor(sf.Name, rd);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logging.Log.WriteWarningMessage(LOGTAG, "FailedRebuildingWithFile", ex, "Failed to use information from {0} to rebuild database: {1}", sf.Name, ex.Message);
                                if (m_options.UnittestMode)
                                    throw;
                            }
                        }
                    }
                }

                backend.WaitForComplete(restoredb, null);

				// In some cases we have a stale reference from an index file to a deleted block file
                if (!m_options.UnittestMode)
    				restoredb.CleanupMissingVolumes();

                if (m_options.RepairOnlyPaths)
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "RecreateOrUpdateOnly", "Recreate/path-update completed, not running consistency checks");
                }
                else
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "RecreateCompletedCheckingDatabase", "Recreate completed, verifying the database consistency");

                    //All done, we must verify that we have all blocklist fully intact
                    // if this fails, the db will not be deleted, so it can be used,
                    // except to continue a backup
                    m_result.EndTime = DateTime.UtcNow;

                    using (var lbfdb = new LocalListBrokenFilesDatabase(restoredb))
                    {
                        var broken = lbfdb.GetBrokenFilesets(new DateTime(0), null, null).Count();
                        if (broken != 0)
                            throw new UserInformationException(string.Format("Recreated database has missing blocks and {0} broken filelists. Consider using \"{1}\" and \"{2}\" to purge broken data from the remote store and the database.", broken, "list-broken-files", "purge-broken-files"), "DatabaseIsBrokenConsiderPurge");
                    }

                    restoredb.VerifyConsistency(m_options.Blocksize, m_options.BlockhashSize, true, null);

                    Logging.Log.WriteInformationMessage(LOGTAG, "RecreateCompleted", "Recreate completed, and consistency checks completed, marking database as complete");

                    restoredb.RepairInProgress = false;
                }

                m_result.EndTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Look in the database for filenames similar to the current filename, but with a different compression and encryption module
        /// </summary>
        /// <returns>The volume id of the item</returns>
        /// <param name="filename">The filename read and written</param>
        /// <param name="restoredb">The database to query</param>
        public long ProbeForMatchingFilename(ref string filename, LocalRestoreDatabase restoredb)
        {
            var p = VolumeBase.ParseFilename(filename);
            if (p != null)
            {
                foreach(var compmodule in Library.DynamicLoader.CompressionLoader.Keys)
                    foreach(var encmodule in Library.DynamicLoader.EncryptionLoader.Keys.Union(new string[] { "" }))
                    {
                        var testfilename = VolumeBase.GenerateFilename(p.FileType, p.Prefix, p.Guid, p.Time, compmodule, encmodule);
                        var tvid = restoredb.GetRemoteVolumeID(testfilename);
                        if (tvid >= 0)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "RewritingFilenameMapping", null, "Unable to find volume {0}, but mapping to matching file {1}", filename, testfilename);
                            filename = testfilename;
                            return tvid;
                        }
                    }
            }

            return -1;
        }

        public void Dispose()
        {
        }
    }
}
