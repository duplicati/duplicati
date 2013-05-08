using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Main.ForestHash.Database;
using Duplicati.Library.Main.ForestHash.Volumes;

namespace Duplicati.Library.Main.ForestHash.Operation
{
    public class RecreateDatabaseHandler : IDisposable
    {
        private string m_backendurl;
        private Options m_options;
        private CommunicationStatistics m_stat;

        public delegate IEnumerable<IParsedVolume> FilterFilelistDelegate(IEnumerable<IParsedVolume> filelist);
        public delegate IEnumerable<IFileEntry> FilenameFilterDelegate(IEnumerable<IFileEntry> filenamelist);
        public delegate void BlockVolumePostProcessor(string volumename, BlockVolumeReader reader);

        public RecreateDatabaseHandler(string backendurl, Options options, CommunicationStatistics stat)
        {
            m_options = options;
            m_backendurl = backendurl;
            m_stat = stat;
        }

		/// <summary>
		/// Run the recreate procedure
		/// </summary>
		/// <param name="path">Path to the database that will be created</param>
		/// <param name="filelistfilter">A filter that can be used to disregard certain remote files, intended to be used to select a certain filelist</param>
		/// <param name="filenamefilter">Filters the files in a filelist to prevent downloading unwanted data</param>
		/// <param name="blockprocessor">A callback hook that can be used to work with downloaded block volumes, intended to be use to recover data blocks while processing blocklists</param>
        public void Run(string path, FilterFilelistDelegate filelistfilter = null, FilenameFilterDelegate filenamefilter = null, BlockVolumePostProcessor blockprocessor = null)
        {
        	if (System.IO.File.Exists(path))
        		throw new Exception(string.Format("Cannot recreate database because file already exists: {0}", path));

			using(var db = new LocalBlocklistUpdateDatabase(path, m_options.Blocksize))
        		DoRun(db, filelistfilter, filenamefilter, blockprocessor);
		}
		
		/// <summary>
		/// Run the recreate procedure
		/// </summary>
		/// <param name="path">Path to the database that will be created</param>
		/// <param name="filelistfilter">A filter that can be used to disregard certain remote files, intended to be used to select a certain filelist</param>
		/// <param name="filenamefilter">Filters the files in a filelist to prevent downloading unwanted data</param>
		/// <param name="blockprocessor">A callback hook that can be used to work with downloaded block volumes, intended to be use to recover data blocks while processing blocklists</param>
        internal void DoRun(LocalDatabase dbparent, FilterFilelistDelegate filelistfilter = null, FilenameFilterDelegate filenamefilter = null, BlockVolumePostProcessor blockprocessor = null)
        {
        	var hashalg = System.Security.Cryptography.HashAlgorithm.Create(m_options.BlockHashAlgorithm);
			if (hashalg == null)
				throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, m_options.BlockHashAlgorithm));
            var hashsize = hashalg.HashSize / 8;

            //We build a local database in steps.
            using (var restoredb = new LocalBlocklistUpdateDatabase(dbparent, m_options.Blocksize))
            using (var backend = new FhBackend(m_backendurl, m_options, m_stat, restoredb))
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
	                        n.Prefix == m_options.BackupPrefix
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
                    filelists = filelistfilter(filelists).ToArray();

                using (var backupdb = new LocalBackupDatabase(restoredb, m_options))
                {
                    foreach (var fl in remotefiles)
                        volumeIds[fl.File.Name] = backupdb.RegisterRemoteVolume(fl.File.Name, fl.FileType, RemoteVolumeState.Uploaded);
                                       
                    //We grab all index files, and update the block table
                    using (var tr = restoredb.BeginTransaction())
                    {
						var indexfiles = 
							from n in remotefiles
							where n.FileType == RemoteVolumeType.Index
							select new RemoteVolume(n.File) as IRemoteVolume;
												
                        foreach (var sf in new AsyncDownloader(indexfiles.ToList(), backend))
                        using (var tmpfile = sf.Value)
                        {
                            if (sf.Key.Hash != null && sf.Key.Size > 0)
                                backupdb.UpdateRemoteVolume(sf.Key.Name, RemoteVolumeState.Verified, sf.Key.Size, sf.Key.Hash, tr);
							
                            using (var svr = new IndexVolumeReader(RestoreHandler.GetCompressionModule(sf.Key.Name), tmpfile, m_options, hashsize))
                            {
					        	ForestHash.VerifyParameters(restoredb, m_options);

                                //If there are blocklists in the index file, update the blocklists
                                foreach (var b in svr.BlockLists)
                                    restoredb.UpdateBlocklist(b.Hash, b.Blocklist, hashsize, tr);

                                foreach (var a in svr.Volumes)
                                {
									var volumeID = restoredb.GetRemoteVolumeID(a.Filename);
                                    //Add all block/volume mappings
                                    foreach (var b in a.Blocks)
                                        backupdb.AddBlock(b.Key, b.Value, volumeID, tr);

                                    backupdb.UpdateRemoteVolume(a.Filename, RemoteVolumeState.Verified, a.Length, a.Hash, tr);
                                    backupdb.AddIndexBlockLink(restoredb.GetRemoteVolumeID(sf.Key.Name), volumeID, tr);
                                }
                            }
                        }

                        tr.Commit();
                    }

                    //We need this to prepare for the block-lists
                    var dummylist = new string[0];

                    // Default filter is no filter (pass-through)
                    if (filenamefilter == null)
                        filenamefilter = lst => lst;

                    //Now record all blocksets and files needed
                    using (var tr = backupdb.BeginTransaction())
                    {
                    	var filelistWork = (from n in filelists select new RemoteVolume(n.File) as IRemoteVolume).ToList();
                    	foreach (var entry in new AsyncDownloader(filelistWork, backend))
                    	using (var tmpfile = entry.Value)
                        {
                            if (entry.Key.Hash != null && entry.Key.Size > 0)
                                backupdb.UpdateRemoteVolume(entry.Key.Name, RemoteVolumeState.Verified, entry.Key.Size, entry.Key.Hash, tr);

                        	var parsed = VolumeBase.ParseFilename(entry.Key.Name);
		                    // Create timestamped operations based on the file timestamp
                        	backupdb.CreateFileset(volumeIds[entry.Key.Name], parsed.Time, tr);
                            using (var filelistreader = new FilesetVolumeReader(parsed.CompressionModule, tmpfile, m_options))
                                foreach (var fe in filenamefilter(filelistreader.Files))
                                {
                                    if (fe.Type == FilelistEntryType.Folder)
                                    {
                                        long metaid = -1;
                                        if (fe.Metahash != null)
                                            backupdb.AddMetadataset(fe.Metahash, fe.Metasize, out metaid, tr);
                                        backupdb.AddDirectoryEntry(fe.Path, metaid, fe.Time, tr);

                                    }
                                    else if (fe.Type == FilelistEntryType.File)
                                    {
                                        long metaid = -1;
                                        long blocksetid;
                                        if (fe.Metahash != null)
                                            backupdb.AddMetadataset(fe.Metahash, fe.Metasize, out metaid, tr);

                                        backupdb.AddBlockset(fe.Hash, fe.Size, m_options.Blocksize, dummylist, fe.BlocklistHashes, out blocksetid, tr);
                                        backupdb.AddFile(fe.Path, fe.Time, blocksetid, metaid, tr);
                                    }
                                }
                        }
                        
                        tr.Commit();
                    }
                }

                //We now need some blocklists, so we start by grabbing a
                // volume with one of the blocklists
                //For each volume we then update the blocklist table
                // and then restore the blocks we know
                restoredb.FindMissingBlocklistHashes();

                foreach (var sf in new AsyncDownloader(restoredb.GetMissingBlockListVolumes(), backend))
                	using (var tmpfile = sf.Value)
                    using (var rd = new BlockVolumeReader(RestoreHandler.GetCompressionModule(sf.Key.Name), tmpfile, m_options))
                    using (var tr = restoredb.BeginTransaction())
                    {
                    	var volumeid = restoredb.GetRemoteVolumeID(sf.Key.Name);
                        foreach (var blocklisthash in restoredb.GetBlockLists(volumeid))
                            restoredb.UpdateBlocklist(blocklisthash, rd.ReadBlocklist(blocklisthash, hashsize), hashsize, tr);

                        tr.Commit();

                        //At this point we can patch files with data from the block volume
                        if (blockprocessor != null)
                            blockprocessor(sf.Key.Name, rd);
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
            m_stat.EndTime = DateTime.Now;
        }
    }
}
