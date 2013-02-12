using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Duplicati.Library.Main.ForestHash.Database;
using Duplicati.Library.Main.ForestHash.Volumes;

namespace Duplicati.Library.Main.ForestHash.Operation
{
    internal class BackupHandler : IDisposable
    {
        private readonly FhOptions m_options;
        private readonly BackupStatistics m_stat;
        private FhBackend m_backend;

        private readonly byte[] m_blockbuffer;
        private readonly byte[] m_blocklistbuffer;
        private readonly System.Security.Cryptography.HashAlgorithm m_blockhasher;
        private readonly System.Security.Cryptography.HashAlgorithm m_filehasher;

        private LocalBackupDatabase m_database;
        private BlockVolumeWriter m_blockvolume;
        private FilesetVolumeWriter m_filesetvolume;
        private ShadowVolumeWriter m_shadowvolume;

        private Snapshots.ISnapshotService m_snapshot;
        private long m_changedfiles;
        private long m_changedfolders;

        private string[] m_sources;

        public BackupHandler(string backendurl, FhOptions options, BackupStatistics stat, string[] sources)
        {
            m_options = options;
            m_stat = stat;
            m_database = new LocalBackupDatabase(m_options.Fhdbpath);
            m_backend = new FhBackend(backendurl, options, m_database);

            m_sources = sources;
            m_blockbuffer = new byte[m_options.Fhblocksize];
            m_blocklistbuffer = new byte[m_options.Fhblocksize];

            m_blockhasher = System.Security.Cryptography.SHA256.Create();
            m_filehasher = System.Security.Cryptography.SHA256.Create();

            if (!m_blockhasher.CanReuseTransform || !m_filehasher.CanReuseTransform)
                throw new Exception(Strings.Foresthash.InvalidCryptoSystem);
        }

        private static Snapshots.ISnapshotService GetSnapshot(string[] sourcefolders, Options options, CommunicationStatistics stat)
        {
            try
            {
                if (options.SnapShotStrategy != Options.OptimizationStrategy.Off)
                    return Duplicati.Library.Snapshots.SnapshotUtility.CreateSnapshot(sourcefolders, options.RawOptions);
            }
            catch (Exception ex)
            {
                if (options.SnapShotStrategy == Options.OptimizationStrategy.Required)
                    throw;
                else if (options.SnapShotStrategy == Options.OptimizationStrategy.On)
                {
                    stat.LogWarning(string.Format(Strings.RSyncDir.SnapshotFailedError, ex.ToString()), ex);
                }
            }

            return Utility.Utility.IsClientLinux ?
                (Library.Snapshots.ISnapshotService)new Duplicati.Library.Snapshots.NoSnapshotLinux(sourcefolders, options.RawOptions)
                    :
                (Library.Snapshots.ISnapshotService)new Duplicati.Library.Snapshots.NoSnapshotWindows(sourcefolders, options.RawOptions);
        }


        public void Run()
        {
            ForestHash.VerifyRemoteList(m_backend, m_options, m_database);

            m_blockvolume = new BlockVolumeWriter(m_options);
            m_filesetvolume = new FilesetVolumeWriter(m_options, m_database.OperationTimestamp);
            m_shadowvolume = new ShadowVolumeWriter(m_options);

            m_database.RegisterRemoteVolume(m_blockvolume.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary);
            m_database.RegisterRemoteVolume(m_filesetvolume.RemoteFilename, RemoteVolumeType.Files, RemoteVolumeState.Temporary);
            m_database.RegisterRemoteVolume(m_shadowvolume.RemoteFilename, RemoteVolumeType.Shadow, RemoteVolumeState.Temporary);
            m_shadowvolume.StartVolume(m_blockvolume.RemoteFilename);

            List<string> hashes = new List<string>(10000);
            using (m_snapshot = GetSnapshot(m_sources, m_options, m_stat))
                m_snapshot.EnumerateFilesAndFolders(this.HandleFilesystemEntry);

            if (m_changedfiles > 0 || m_changedfolders > 0)
            {
                if (m_blockvolume.SourceSize > 0)
                {
                    m_backend.Put(m_blockvolume, m_shadowvolume);
                }
                else
                {
                    m_database.RemoveRemoteVolume(m_blockvolume.RemoteFilename);
                    m_database.RemoveRemoteVolume(m_shadowvolume.RemoteFilename);
                    m_shadowvolume.FinishVolume(null, 0);
                }

                m_backend.Put(m_filesetvolume);
            }
            else
            {
                m_database.LogMessage("info", "removing temp files, as no data needs to be uploaded", null);
                m_database.RemoveRemoteVolume(m_blockvolume.RemoteFilename);
                m_database.RemoveRemoteVolume(m_filesetvolume.RemoteFilename);
                m_database.RemoveRemoteVolume(m_shadowvolume.RemoteFilename);
                m_shadowvolume.FinishVolume(null, 0);
            }

            m_backend.WaitForComplete();
        }

        private bool HandleFilesystemEntry(string rootpath, string path, System.IO.FileAttributes attributes)
        {
            if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                if (m_options.SymlinkPolicy == Options.SymlinkStrategy.Ignore)
                    return false;
                if (m_options.SymlinkPolicy == Options.SymlinkStrategy.Store)
                {
                    Dictionary<string, string> metadata = null; //snapshot.GetMetadata(path);
                    if (metadata == null)
                        metadata = new Dictionary<string, string>();

                    if (!metadata.ContainsKey("CoreAttributes"))
                        metadata["CoreAttributes"] = attributes.ToString();
                    if (!metadata.ContainsKey("CoreLastWritetime"))
                        metadata["CoreLastWritetime"] = Utility.Utility.SerializeDateTime(m_snapshot.GetLastWriteTime(path));
                    if (!metadata.ContainsKey("CoreSymlinkTarget"))
                        metadata["CoreSymlinkTarget"] = m_snapshot.GetSymlinkTarget(path);

                    var metahash = ForestHash.WrapMetadata(metadata);
                    m_filesetvolume.AddSymlink(path, metahash.Hash, metahash.Size);
                    if (AddSymlinkToOutput(path, DateTime.UtcNow, metahash))
                        m_changedfolders++;
                    
                    //Do not recurse symlinks
                    return false;
                }
            }

            if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
            {
                Dictionary<string, string> metadata = null; //snapshot.GetMetadata(path);
                if (metadata == null)
                    metadata = new Dictionary<string, string>();

                if (!metadata.ContainsKey("CoreAttributes"))
                    metadata["CoreAttributes"] = attributes.ToString();
                if (!metadata.ContainsKey("CoreLastWritetime"))
                    metadata["CoreLastWritetime"] = Utility.Utility.SerializeDateTime(m_snapshot.GetLastWriteTime(path));
                var metahash = ForestHash.WrapMetadata(metadata);

                m_filesetvolume.AddDirectory(path, metahash.Hash, metahash.Size);
                if (AddFolderToOutput(path, DateTime.UtcNow, metahash))
                    m_changedfolders++;
                return true;
            }

            string oldHash;
            string oldMetahash;
            DateTime oldScanned;
            long oldId;
            long oldSize;
            long oldMetasize;
            IList<string> oldBlocklistHashes;
            bool fileExists = m_database.GetFileEntry(path, out oldId, out oldSize, out oldScanned, out oldHash, out oldMetahash, out oldMetasize, out oldBlocklistHashes);
            bool changed = false;

            //Skip symlinks if required
            if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint && m_options.SymlinkPolicy == Options.SymlinkStrategy.Ignore)
                return false;

            try
            {
                DateTime lastModified = m_snapshot.GetLastWriteTime(path);
                if (!fileExists || m_options.DisableFiletimeCheck || lastModified > oldScanned)
                {
                    long filesize = 0;
                    DateTime scantime = DateTime.UtcNow;

                    Dictionary<string, string> metadata = null; //snapshot.GetMetadata(file);
                    if (metadata == null)
                        metadata = new Dictionary<string, string>();

                    if (!metadata.ContainsKey("CoreAttributes"))
                        metadata["CoreAttributes"] = attributes.ToString();
                    if (!metadata.ContainsKey("CoreLastWritetime"))
                        metadata["CoreLastWritetime"] = Utility.Utility.SerializeDateTime(lastModified);

                    var metahashandsize = ForestHash.WrapMetadata(metadata);
                    var blocklisthashes = new List<string>();
                    using (var hashcollector = new HashlistCollector())
                    {
                        using (var fs = new Blockprocessor(m_snapshot.OpenRead(path), m_blockbuffer))
                        {
                            int size;
                            int blocklistoffset = 0;

                            m_filehasher.Initialize();

                            do
                            {
                                size = fs.Readblock();

                                m_filehasher.TransformBlock(m_blockbuffer, 0, size, m_blockbuffer, 0);
                                var blockkey = m_blockhasher.ComputeHash(m_blockbuffer, 0, size);
                                if (m_blocklistbuffer.Length - blocklistoffset < blockkey.Length)
                                {
                                    var blkey = Convert.ToBase64String(m_blockhasher.ComputeHash(m_blocklistbuffer, 0, blocklistoffset));
                                    blocklisthashes.Add(blkey);
                                    AddBlockToOutput(blkey, m_blocklistbuffer, blocklistoffset);
                                    blocklistoffset = 0;
                                }

                                Array.Copy(blockkey, 0, m_blocklistbuffer, blocklistoffset, blockkey.Length);
                                blocklistoffset += blockkey.Length;

                                var key = Convert.ToBase64String(blockkey);
                                AddBlockToOutput(key, m_blockbuffer, size);
                                hashcollector.Add(key);
                                filesize += size;


                            } while (size == m_blockbuffer.Length);

                            //If all fits in a single block, don't bother with blocklists
                            if (hashcollector.Count > 1)
                            {
                                var blkeyfinal = Convert.ToBase64String(m_blockhasher.ComputeHash(m_blocklistbuffer, 0, blocklistoffset));
                                blocklisthashes.Add(blkeyfinal);
                                AddBlockToOutput(blkeyfinal, m_blocklistbuffer, blocklistoffset);
                            }
                        }


                        m_filehasher.TransformFinalBlock(m_blockbuffer, 0, 0);

                        var filekey = Convert.ToBase64String(m_filehasher.Hash);
                        if (oldHash != filekey)
                        {
                            m_changedfiles++;
                            AddFileToOutput(path, filesize, scantime, metahashandsize, hashcollector, filekey, blocklisthashes);
                            changed = true;
                        }
                    }
                }

                if (!changed)
                    AddUnmodifiedFile(path, oldId, oldSize, oldScanned, oldHash, oldMetahash, oldMetasize, oldBlocklistHashes);

            }
            catch (Exception ex)
            {
                m_stat.LogWarning(string.Format("Failed to process path: {0}", path), ex);
            }

            if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                if (m_options.SymlinkPolicy == Options.SymlinkStrategy.Follow)
                    return true;
                else
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Adds the found file data to the output unless the block already exists
        /// </summary>
        /// <param name="key">The block hash</param>
        /// <param name="data">The data matching the hash</param>
        private bool AddBlockToOutput(string key, byte[] data, int len)
        {
            if (m_database.AddBlock(key, len, m_blockvolume.RemoteFilename))
            {
                m_blockvolume.AddBlock(key, data, len);
                m_shadowvolume.AddBlock(key, len);
                if (m_blockvolume.Filesize > m_options.VolumeSize - m_options.Fhblocksize)
                {
                    m_backend.Put(m_blockvolume, m_shadowvolume);

                    m_blockvolume = new BlockVolumeWriter(m_options);
                    m_shadowvolume = new ShadowVolumeWriter(m_options);
                    m_shadowvolume.StartVolume(m_blockvolume.RemoteFilename);
                }

                return true;
            }

            return false;
        }

        private void AddUnmodifiedFile(string path, long oldId, long size, DateTime scantime, string oldHash, string oldMetahash, long oldMetasize, IList<string> blocklisthashes)
        {
            m_database.AddUnmodifiedFile(oldId, scantime);
            m_filesetvolume.AddFile(path, oldHash, size, scantime, oldMetahash, oldMetasize, blocklisthashes);
        }


        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        /// <param name="hashlist">The list of hashes that make up the file</param>
        /// <param name="size">The size of the file</param>
        /// <param name="fragmentoffset">The offset into a fragment block where the last few bytes are stored</param>
        /// <param name="metadata">A lookup table with various metadata values describing the file</param>
        private bool AddFolderToOutput(string filename, DateTime scantime, ForestHash.IMetahash meta)
        {
            long metadataid;
            bool r = false;

            //TODO: If meta.Size > blocksize...
            r |= AddBlockToOutput(meta.Hash, meta.Blob, (int)meta.Size);
            r |= m_database.AddMetadataset(meta.Hash, meta.Size, out metadataid);

            m_database.AddDirectoryEntry(filename, metadataid, scantime);
            return r;
        }

        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        /// <param name="hashlist">The list of hashes that make up the file</param>
        /// <param name="size">The size of the file</param>
        /// <param name="fragmentoffset">The offset into a fragment block where the last few bytes are stored</param>
        /// <param name="metadata">A lookup table with various metadata values describing the file</param>
        private bool AddSymlinkToOutput(string filename, DateTime scantime, ForestHash.IMetahash meta)
        {
            long metadataid;
            bool r = false;

            //TODO: If meta.Size > blocksize...
            r |= AddBlockToOutput(meta.Hash, meta.Blob, (int)meta.Size);
            r |= m_database.AddMetadataset(meta.Hash, meta.Size, out metadataid);

            m_database.AddSymlinkEntry(filename, metadataid, scantime);
            return r;
        }

        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        /// <param name="hashlist">The list of hashes that make up the file</param>
        /// <param name="size">The size of the file</param>
        /// <param name="fragmentoffset">The offset into a fragment block where the last few bytes are stored</param>
        /// <param name="metadata">A lookup table with various metadata values describing the file</param>
        private void AddFileToOutput(string filename, long size, DateTime scantime, ForestHash.IMetahash metadata, HashlistCollector hashlist, string filehash, IList<string> blocklisthashes)
        {
            long metadataid;
            long blocksetid;

            //TODO: If metadata.Size > blocksize...
            AddBlockToOutput(metadata.Hash, metadata.Blob, (int)metadata.Size);
            m_database.AddMetadataset(metadata.Hash, metadata.Size, out metadataid);

            m_database.AddBlockset(filehash, size, m_blockbuffer.Length, hashlist.Hashes, blocklisthashes, out blocksetid);

            m_filesetvolume.AddFile(filename, filehash, size, scantime, metadata.Hash, metadata.Size, blocklisthashes);
            m_database.AddFile(filename, scantime, blocksetid, metadataid);
        }

        public void Dispose()
        {
            if (m_backend != null)
            {
                try { m_backend.Dispose(); }
                finally { m_backend = null; }
            }

            if (m_blockvolume != null)
            {
                try { m_blockvolume.Dispose(); }
                finally { m_blockvolume = null; }
            }

            if (m_filesetvolume != null)
            {
                try { m_filesetvolume.Dispose(); }
                finally { m_filesetvolume = null; }
            }

            if (m_shadowvolume != null)
            {
                try { m_shadowvolume.Dispose(); }
                finally { m_shadowvolume = null; }
            }

        }
    }
}
