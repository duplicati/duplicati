using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;

namespace Duplicati.Library.Main.Operation
{
    internal class RecreateDatabaseHandler : IDisposable
    {
        private string m_backendurl;
        private Options m_options;
        private RecreateDatabaseResults m_result;

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
                throw new Exception(string.Format("Cannot recreate database because file already exists: {0}", path));

            using(var db = new LocalDatabase(path, "Recreate"))
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
                throw new Exception(string.Format("Can only update with paths, try setting {0}", "--repair-only-paths"));
            
            using(var db = new LocalDatabase(m_options.Dbpath, "Recreate"))
            {
                m_result.SetDatabase(db);

                if (db.FindMatchingFilesets(m_options.Time, m_options.Version).Any())
                    throw new Exception(string.Format("The version(s) being updated to, already exists"));

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
        /// <param name="filenamefilter">Filters the files in a filelist to prevent downloading unwanted data</param>
        /// <param name="blockprocessor">A callback hook that can be used to work with downloaded block volumes, intended to be use to recover data blocks while processing blocklists</param>
        internal void DoRun(LocalDatabase dbparent, bool updating, Library.Utility.IFilter filter = null, NumberedFilterFilelistDelegate filelistfilter = null, BlockVolumePostProcessor blockprocessor = null)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Recreate_Running);

            //We build a local database in steps.
            using(var restoredb = new LocalRecreateDatabase(dbparent, m_options))
            using(var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, restoredb))
            {
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
                        throw new Exception("No files were found at the remote location, perhaps the target url is incorrect?");
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
                            throw new Exception(string.Format("Found {0} files at the remote storage, but none that could be parsed", rawlist.Count));
                        else if (types.Length == 1)
                            throw new Exception(string.Format("Found {0} parse-able files with the prefix {1}, did you forget to set the backup-prefix?", tmp.Length, types[0]));
                        else
                            throw new Exception(string.Format("Found {0} parse-able files (of {1} files) with different prefixes: {2}, did you forget to set the backup-prefix?", tmp.Length, rawlist.Count, string.Join(", ", types)));
                    }
                }

                //Then we select the filelist we should work with,
                // and create the filelist table to fit
                IEnumerable<IParsedVolume> filelists =
                    from n in remotefiles
                    where n.FileType == RemoteVolumeType.Files
                    orderby n.Time descending
                    select n;

                if (filelists.Count() <= 0)
                    throw new Exception(string.Format("No filelists found on the remote destination"));
                
                if (filelistfilter != null)
                    filelists = filelistfilter(filelists).Select(x => x.Value).ToArray();

                if (filelists.Count() <= 0)
                    throw new Exception(string.Format("No filelists"));

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
                    var progress = 0;

                    // Register the files we are working with, if not already updated
                    if (updating)
                    {
                        foreach(var n in filelists)
                            if (volumeIds[n.File.Name] == -1)
                                volumeIds[n.File.Name] = restoredb.RegisterRemoteVolume(n.File.Name, n.FileType, RemoteVolumeState.Uploaded, n.File.Size, new TimeSpan(0), tr);
                    }
                                
                    var isFirstFilelist = true;

                    foreach(var entry in new AsyncDownloader(filelistWork, backend))
                        try
                        {
                            if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                            {
                                backend.WaitForComplete(restoredb, null);
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

                                if (!hasUpdatedOptions && !updating) 
                                {
                                    VolumeReaderBase.UpdateOptionsFromManifest(parsed.CompressionModule, tmpfile, m_options);
                                    hasUpdatedOptions = true;
                                }

                                // Create timestamped operations based on the file timestamp
                                var filesetid = restoredb.CreateFileset(volumeIds[entry.Name], parsed.Time, tr);
                                using(var filelistreader = new FilesetVolumeReader(parsed.CompressionModule, tmpfile, m_options))
                                    foreach(var fe in filelistreader.Files.Where(x => Library.Utility.FilterExpression.Matches(filter, x.Path)))
                                    {
                                        try
                                        {
                                            if (fe.Type == FilelistEntryType.Folder)
                                            {
                                                restoredb.AddDirectoryEntry(filesetid, fe.Path, fe.Time, fe.Metahash, fe.Metahash == null ? -1 : fe.Metasize, tr);
                                            }
                                            else if (fe.Type == FilelistEntryType.File)
                                            {
                                                var blocksetid = restoredb.AddBlockset(fe.Hash, fe.Size, fe.BlocklistHashes, tr);
                                                restoredb.AddFileEntry(filesetid, fe.Path, fe.Time, blocksetid, fe.Metahash, fe.Metahash == null ? -1 : fe.Metasize, tr);
                                            }
                                            else if (fe.Type == FilelistEntryType.Symlink)
                                            {
                                                restoredb.AddSymlinkEntry(filesetid, fe.Path, fe.Time, fe.Metahash, fe.Metahash == null ? -1 : fe.Metasize, tr);
                                            }
                                            else
                                            {
                                                m_result.AddWarning(string.Format("Skipping file-entry with unknown type {0}: {1} ", fe.Type, fe.Path), null);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            m_result.AddWarning(string.Format("Failed to process file-entry: {0}", fe.Path), ex);
                                        }
                                    }
                            }
                        }
                        catch (Exception ex)
                        {
                            m_result.AddWarning(string.Format("Failed to process file: {0}", entry.Name), ex);
                            if (ex is System.Threading.ThreadAbortException)
                                throw;

                            if (isFirstFilelist && ex is System.Security.Cryptography.CryptographicException)
                                throw;
                        }

                    //Make sure we write the config
                    if (!updating)
                        Utility.VerifyParameters(restoredb, m_options, tr);

                    using(new Logging.Timer("CommitUpdateFilesetFromRemote"))
                        tr.Commit();
                }
            
                if (!m_options.RepairOnlyPaths)
                {
                    var hashalg = System.Security.Cryptography.HashAlgorithm.Create(m_options.BlockHashAlgorithm);
                    if (hashalg == null)
                        throw new Exception(Strings.Common.InvalidHashAlgorithm(m_options.BlockHashAlgorithm));
                    var hashsize = hashalg.HashSize / 8;

                    //Grab all index files, and update the block table
                    using(var tr = restoredb.BeginTransaction())
                    {
                        var indexfiles = (
                                         from n in remotefiles
                                          where n.FileType == RemoteVolumeType.Index
                                          select new RemoteVolume(n.File) as IRemoteVolume).ToList();

                        var progress = 0;
                                    
                        foreach(var sf in new AsyncDownloader(indexfiles, backend))
                            try
                            {
                                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                                {
                                    backend.WaitForComplete(restoredb, null);
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

                                            // Still broken, register a missing item
                                            if (volumeID < 0)
                                            {
                                                var p = VolumeBase.ParseFilename(filename);
                                                if (p == null)
                                                    throw new Exception(string.Format("Unable to parse filename: {0}", filename));
                                                m_result.AddError(string.Format("Remote file referenced as {0}, but not found in list, registering a missing remote file", filename), null);
                                                volumeID = restoredb.RegisterRemoteVolume(filename, p.FileType, RemoteVolumeState.Verified, tr);
                                            }
                                            
                                            //Add all block/volume mappings
                                            foreach(var b in a.Blocks)
                                                restoredb.UpdateBlock(b.Key, b.Value, volumeID, tr);

                                            restoredb.UpdateRemoteVolume(filename, RemoteVolumeState.Verified, a.Length, a.Hash, tr);
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
                                m_result.AddWarning(string.Format("Failed to process index file: {0}", sf.Name), ex);
                                if (ex is System.Threading.ThreadAbortException)
                                    throw;
                            }

                        using(new Logging.Timer("CommitRecreatedDb"))
                            tr.Commit();
                    
                        // TODO: In some cases, we can avoid downloading all index files, 
                        // if we are lucky and pick the right ones
                    }

                    // We have now grabbed as much information as possible,
                    // if we are still missing data, we must now fetch block files
                    restoredb.FindMissingBlocklistHashes(hashsize, m_options.Blocksize, null);
                
                    //We do this in three passes
                    for(var i = 0; i < 3; i++)
                    {
                        // Grab the list matching the pass type
                        var lst = restoredb.GetMissingBlockListVolumes(i).ToList();
                        if (lst.Count > 0)
                        {
                            switch (i)
                            {
                                case 0:
                                    if (m_options.Verbose)
                                        m_result.AddVerboseMessage("Processing required {0} blocklist volumes: {1}", lst.Count, string.Join(", ", lst.Select(x => x.Name)));
                                    else
                                        m_result.AddMessage(string.Format("Processing required {0} blocklist volumes", lst.Count));
                                    break;
                                case 1:
                                    if (m_options.Verbose)
                                        m_result.AddVerboseMessage("Probing {0} candidate blocklist volumes: {1}", lst.Count, string.Join(", ", lst.Select(x => x.Name)));
                                    else
                                        m_result.AddMessage(string.Format("Probing {0} candidate blocklist volumes", lst.Count));
                                    break;
                                default:
                                    if (m_options.Verbose)
                                        m_result.AddVerboseMessage("Processing all of the {0} volumes for blocklists: {1}", lst.Count, string.Join(", ", lst.Select(x => x.Name)));
                                    else
                                        m_result.AddMessage(string.Format("Processing all of the {0} volumes for blocklists", lst.Count));
                                    break;
                            }
                        }

                        var progress = 0;
                        foreach(var sf in new AsyncDownloader(lst, backend))
                            using(var tmpfile = sf.TempFile)
                            using(var rd = new BlockVolumeReader(RestoreHandler.GetCompressionModule(sf.Name), tmpfile, m_options))
                            using(var tr = restoredb.BeginTransaction())
                            {
                                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                                {
                                    backend.WaitForComplete(restoredb, null);
                                    return;
                                }    
                            
                                progress++;
                                m_result.OperationProgressUpdater.UpdateProgress((((float)progress / lst.Count) * 0.1f) + 0.7f + (i * 0.1f));

                                var volumeid = restoredb.GetRemoteVolumeID(sf.Name);

                                restoredb.UpdateRemoteVolume(sf.Name, RemoteVolumeState.Uploaded, sf.Size, sf.Hash, tr);
                            
                                // Update the block table so we know about the block/volume map
                                foreach(var h in rd.Blocks)
                                    restoredb.UpdateBlock(h.Key, h.Value, volumeid, tr);
                            
                                // Grab all known blocklists from the volume
                                foreach(var blocklisthash in restoredb.GetBlockLists(volumeid))
                                    restoredb.UpdateBlockset(blocklisthash, rd.ReadBlocklist(blocklisthash, hashsize), tr);
    
                                // Update tables so we know if we are done
                                restoredb.FindMissingBlocklistHashes(hashsize, m_options.Blocksize, tr);
                        
                                using(new Logging.Timer("CommitRestoredBlocklist"))
                                    tr.Commit();
    
                                //At this point we can patch files with data from the block volume
                                if (blockprocessor != null)
                                    blockprocessor(sf.Name, rd);
                            }
                    }
                }
                
				backend.WaitForComplete(restoredb, null);

                //All done, we must verify that we have all blocklist fully intact
                // if this fails, the db will not be deleted, so it can be used,
                // except to continue a backup
                restoredb.VerifyConsistency(null, m_options.Blocksize, m_options.BlockhashSize);
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
                            m_result.AddWarning(string.Format("Unable to find volume {0}, but mapping to matching file {1}", filename, testfilename), null);
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
