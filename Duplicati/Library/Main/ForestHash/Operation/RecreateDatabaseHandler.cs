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
        private FhOptions m_options;
        private byte[] m_blockbuffer;
        private string m_destination;

        public delegate IEnumerable<IParsedVolume> FilterFilelistDelegate(IEnumerable<IParsedVolume> filelist);
        public delegate IEnumerable<IFileEntry> FilenameFilterDelegate(IEnumerable<IFileEntry> filenamelist);
        public delegate void BlockVolumePostProcessor(string volumename, BlockVolumeReader reader);

        public RecreateDatabaseHandler(string backendurl, FhOptions options, string destination)
        {
            m_options = options;
            m_backendurl = backendurl;

            m_destination = destination;
            m_blockbuffer = new byte[m_options.Fhblocksize];

        }

        public void Run(string path, FilterFilelistDelegate filelistfilter = null, FilenameFilterDelegate filenamefilter = null, BlockVolumePostProcessor blockprocessor = null)
        {
            var hashsize = System.Security.Cryptography.SHA256.Create().HashSize / 8;

            //We build a local database in steps.
            using (var restoredb = new LocalBlocklistUpdateDatabase(path, m_options.Fhblocksize))
            using (var backend = new FhBackend(m_backendurl, m_options, restoredb))
            {
                //First step is to examine the remote storage to see what
                // kind of data we can find
                var remotefiles =
                    (from n in
                            (from x in backend.List()
                            select VolumeBase.ParseFilename(x))
                        where
                            n != null
                                &&
                            n.EncryptionModule == (m_options.NoEncryption ? null : m_options.EncryptionModule)
                                &&
                            n.Prefix == m_options.BackupPrefix
                        select n).ToArray(); //ToArray() ensures that we do not remote-request it multiple times

                //Then we select the filelist we should work with,
                // and create the filelist table to fit
                IEnumerable<IParsedVolume> filelists =
                    from n in remotefiles
                    where n.FileType == RemoteVolumeType.Files
                    orderby n.Time descending
                    select n;

                if (filelistfilter != null)
                    filelists = filelistfilter(filelists);

                using (var backupdb = new LocalBackupDatabase(path))
                {
                    foreach (var fl in remotefiles)
                        backupdb.RegisterRemoteVolume(fl.File.Name, fl.FileType, RemoteVolumeState.Uploaded);

                    var shadowfiles =
                        from n in remotefiles
                        where n.FileType == RemoteVolumeType.Shadow
                        select new RemoteVolume(n.File) as IRemoteVolume;

                    //We grab all shadow files, and update the block table
                    foreach (var sf in new AsyncDownloader(shadowfiles.ToList(), backend))
                    {
                        if (sf.Key.Hash != null && sf.Key.Size > 0)
                            backupdb.UpdateRemoteVolume(sf.Key.Name, RemoteVolumeState.Verified, sf.Key.Size, sf.Key.Hash);
                        
                        using (var svr = new ShadowVolumeReader(RestoreHandler.GetCompressionModule(sf.Key.Name), sf.Value, m_options, hashsize))
                        {
                            //If there are blocklists in the shadow file, update the blocklists
                            using (var tr = restoredb.BeginTransaction())
                            {
                                foreach (var b in svr.BlockLists)
                                    restoredb.UpdateBlocklist(b.Hash, b.Blocklist, hashsize, tr);

                                tr.Commit();
                            }

                            foreach (var a in svr.Volumes)
                            {
                                //Add all block/volume mappings
                                foreach (var b in a.Blocks)
                                    backupdb.AddBlock(b.Key, b.Value, a.Filename);

                                backupdb.UpdateRemoteVolume(a.Filename, RemoteVolumeState.Verified, a.Length, a.Hash);
                            }
                        }
                    }

                    //We need this to prepare for the block-lists
                    var dummylist = new string[0];

                    // Default filter is no filter (pass-through)
                    if (filenamefilter == null)
                        filenamefilter = lst => lst;

                    //Now record all blocksets and files needed
                    foreach (var entry in filelists)
                    using (var filelist = backend.Get(entry.File.Name, entry.File.Size, null))
                    using (var filelistreader = new FilesetVolumeReader(RestoreHandler.GetCompressionModule(entry.File.Name), filelist, m_options))
                        foreach (var fe in filenamefilter(filelistreader.Files))
                        {
                            if (fe.Type == FilelistEntryType.Folder)
                            {
                                long metaid = -1;
                                if (fe.Metahash != null)
                                    backupdb.AddMetadataset(fe.Metahash, fe.Metasize, out metaid);
                                backupdb.AddDirectoryEntry(fe.Path, metaid, fe.Time);

                            }
                            else if (fe.Type == FilelistEntryType.File)
                            {
                                long metaid = -1;
                                long blocksetid;
                                if (fe.Metahash != null)
                                    backupdb.AddMetadataset(fe.Metahash, fe.Metasize, out metaid);

                                backupdb.AddBlockset(fe.Hash, fe.Size, m_options.Fhblocksize, dummylist, fe.BlocklistHashes, out blocksetid);
                                backupdb.AddFile(fe.Path, fe.Time, blocksetid, metaid);
                            }
                        }
                }

                //We now need some blocklists, so we start by grabbing a
                // volume with one of the blocklists
                //For each volume we then update the blocklist table
                // and then restore the blocks we know
                var blockfiles =
                    from n in remotefiles
                    where n.FileType == RemoteVolumeType.Blocks
                    select new RemoteVolume(n.File) as IRemoteVolume;

                restoredb.FindMissingBlocklistHashes();

                foreach (var sf in new AsyncDownloader(restoredb.GetMissingBlockListVolumes(), backend))
                    using (var rd = new BlockVolumeReader(RestoreHandler.GetCompressionModule(sf.Key.Name), sf.Value, m_options))
                    using (var tr = restoredb.BeginTransaction())
                    {
                        foreach (var blocklisthash in restoredb.GetBlockLists(sf.Key.Name))
                            restoredb.UpdateBlocklist(blocklisthash, rd.ReadBlocklist(blocklisthash, hashsize), hashsize, tr);

                        tr.Commit();

                        //At this point we can patch files with data from the block volume
                        if (blockprocessor != null)
                            blockprocessor(sf.Key.Name, rd);
                    }

                //All done, we must verify that we have all blocklist fully intact
                restoredb.VerifyDatabaseIntegrity();

            }
        }

        public void Dispose()
        {
        }
    }
}
