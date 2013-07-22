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
        /// <param name="filenamefilter">Filters the files in a filelist to prevent downloading unwanted data</param>
        /// <param name="blockprocessor">A callback hook that can be used to work with downloaded block volumes, intended to be use to recover data blocks while processing blocklists</param>
        public void Run(string path, Library.Utility.IFilter filter = null, NumberedFilterFilelistDelegate filelistfilter = null, BlockVolumePostProcessor blockprocessor = null)
        {
            if (System.IO.File.Exists(path))
                throw new Exception(string.Format("Cannot recreate database because file already exists: {0}", path));

            using(var db = new LocalDatabase(path, "Recreate"))
            {
                m_result.SetDatabase(db);
                DoRun(db, filter, filelistfilter, blockprocessor);
            }
        }

        /// <summary>
        /// Run the recreate procedure
        /// </summary>
        /// <param name="path">Path to the database that will be created</param>
        /// <param name="filelistfilter">A filter that can be used to disregard certain remote files, intended to be used to select a certain filelist</param>
        /// <param name="filenamefilter">Filters the files in a filelist to prevent downloading unwanted data</param>
        /// <param name="blockprocessor">A callback hook that can be used to work with downloaded block volumes, intended to be use to recover data blocks while processing blocklists</param>
        internal void DoRun(LocalDatabase dbparent, Library.Utility.IFilter filter = null, NumberedFilterFilelistDelegate filelistfilter = null, BlockVolumePostProcessor blockprocessor = null)
        {
            var hashalg = System.Security.Cryptography.HashAlgorithm.Create(m_options.BlockHashAlgorithm);
            if (hashalg == null)
                throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, m_options.BlockHashAlgorithm));
            var hashsize = hashalg.HashSize / 8;

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

                if (filelistfilter != null)
                    filelists = filelistfilter(filelists).Select(x => x.Value).ToArray();

                foreach(var fl in remotefiles)
                    volumeIds[fl.File.Name] = restoredb.RegisterRemoteVolume(fl.File.Name, fl.FileType, RemoteVolumeState.Uploaded);

                //Record all blocksets and files needed
                using(var tr = restoredb.BeginTransaction())
                {
                    var filelistWork = (from n in filelists orderby n.Time select new RemoteVolume(n.File) as IRemoteVolume).ToList();
                    foreach(var entry in new AsyncDownloader(filelistWork, backend))
                        try
                        {
                            using(var tmpfile = entry.TempFile)
                            {
                                if (entry.Hash != null && entry.Size > 0)
                                    restoredb.UpdateRemoteVolume(entry.Name, RemoteVolumeState.Verified, entry.Size, entry.Hash, tr);

                                var parsed = VolumeBase.ParseFilename(entry.Name);
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
                        }
                
                    using(new Logging.Timer("CommitUpdateFilesetFromRemote"))
                        tr.Commit();
                }
            
                //Grab all index files, and update the block table
                using(var tr = restoredb.BeginTransaction())
                {
                    var indexfiles = 
                        from n in remotefiles
                        where n.FileType == RemoteVolumeType.Index
                        select new RemoteVolume(n.File) as IRemoteVolume;
                                    
                    foreach(var sf in new AsyncDownloader(indexfiles.ToList(), backend))
                        try
                        {
                            using(var tmpfile = sf.TempFile)
                            {
                                if (sf.Hash != null && sf.Size > 0)
                                    restoredb.UpdateRemoteVolume(sf.Name, RemoteVolumeState.Verified, sf.Size, sf.Hash, tr);
                
                                using(var svr = new IndexVolumeReader(RestoreHandler.GetCompressionModule(sf.Name), tmpfile, m_options, hashsize))
                                {
                                    Utility.VerifyParameters(restoredb, m_options);

                                    foreach(var a in svr.Volumes)
                                    {
                                        var volumeID = restoredb.GetRemoteVolumeID(a.Filename);
                                        //Add all block/volume mappings
                                        foreach(var b in a.Blocks)
                                            restoredb.UpdateBlock(b.Key, b.Value, volumeID, tr);

                                        restoredb.UpdateRemoteVolume(a.Filename, RemoteVolumeState.Verified, a.Length, a.Hash, tr);
                                        restoredb.AddIndexBlockLink(restoredb.GetRemoteVolumeID(sf.Name), volumeID, tr);
                                    }
                                
                                    //If there are blocklists in the index file, update the blocklists
                                    foreach(var b in svr.BlockLists)
                                        restoredb.UpdateBlockset(b.Hash, b.Blocklist, hashsize, tr);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            //Not fatal
                            m_result.AddWarning(string.Format("Failed to process index file: {0}", sf.Name), ex);
                        }

                    using(new Logging.Timer("CommitRecreatedDb"))
                        tr.Commit();
                    
                    // TODO: In some cases, we can avoid downloading all index files, 
                    // if we are lucky and pick the right ones
                }

                // We have now grabbed as much information as possible,
                // if we are still missing data, we must now fetch block files
                restoredb.FindMissingBlocklistHashes(hashsize, null);
                
                //We do this in three passes
                for(var i = 0; i < 3; i++)
                {
                    // Grab the list matching the pass type
                    var lst = restoredb.GetMissingBlockListVolumes(i).ToList();
                    foreach (var sf in new AsyncDownloader(lst, backend))
                    	using (var tmpfile = sf.TempFile)
                        using (var rd = new BlockVolumeReader(RestoreHandler.GetCompressionModule(sf.Name), tmpfile, m_options))
                        using (var tr = restoredb.BeginTransaction())
                        {
                        	var volumeid = restoredb.GetRemoteVolumeID(sf.Name);
                            
                            // Update the block table so we know about the block/volume map
                            foreach(var h in rd.Blocks)
                                restoredb.UpdateBlock(h.Key, h.Value, volumeid, tr);
                            
                            // Grab all known blocklists from the volume
                            foreach (var blocklisthash in restoredb.GetBlockLists(volumeid))
                                restoredb.UpdateBlockset(blocklisthash, rd.ReadBlocklist(blocklisthash, hashsize), hashsize, tr);
    
                            // Update tables so we know if we are done
                            restoredb.FindMissingBlocklistHashes(hashsize, tr);
                        
                            using(new Logging.Timer("CommitRestoredBlocklist"))
                                tr.Commit();
    
                            //At this point we can patch files with data from the block volume
                            if (blockprocessor != null)
                                blockprocessor(sf.Name, rd);
                        }
                }
                
				backend.WaitForComplete(restoredb, null);

                //All done, we must verify that we have all blocklist fully intact
                // if this fails, the db will not be deleted, so it can be used,
                // except to continue a backup
                restoredb.VerifyConsistency(null);
            }
        }

        public void Dispose()
        {
        }
    }
}
