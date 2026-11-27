// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation.Restore
{
    /// <summary>
    /// Builds the temporary database for a restore without a local database, with only the
    /// information the restore reads: one fileset, the selected files, their blocksets, and the
    /// blocks and volumes those blocksets use.
    ///
    /// A recreate records every block of the backup and works out the blocksets with queries
    /// over all of them. This follows the recovery tool instead: the blocks that are needed are
    /// kept in memory, the index files are read sequentially, and each entry is a single lookup.
    /// </summary>
    internal class DirectRestoreDatabaseBuilder
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<DirectRestoreDatabaseBuilder>();

        /// <summary>
        /// The outcome of building the database.
        /// </summary>
        public enum Outcome
        {
            /// <summary>
            /// The database can be used for the restore
            /// </summary>
            Completed,
            /// <summary>
            /// The database must be discarded and rebuilt with a recreate
            /// </summary>
            UseRecreate,
            /// <summary>
            /// A stop was requested, and the database is not complete
            /// </summary>
            Stopped
        }

        /// <summary>
        /// A block that is needed for the restore.
        /// </summary>
        /// <param name="ID">The ID the block gets in the database.</param>
        /// <param name="Size">The size of the block.</param>
        /// <param name="VolumeID">The ID of the volume that holds the block, or -1 if it is not yet known.</param>
        private readonly record struct NeededBlock(long ID, long Size, long VolumeID);

        /// <summary>
        /// A blockset that a blocklist belongs to.
        /// </summary>
        /// <param name="BlocksetID">The ID of the blockset.</param>
        /// <param name="Index">The index of the blocklist in the blockset.</param>
        /// <param name="Length">The length of the blockset in bytes.</param>
        private readonly record struct BlocklistOwner(long BlocksetID, long Index, long Length);

        /// <summary>
        /// The options for the restore
        /// </summary>
        private readonly Options m_options;
        /// <summary>
        /// The results of the restore
        /// </summary>
        private readonly RestoreResults m_result;

        /// <summary>
        /// The blocks needed for the restore, by hash. The hashes are kept as bytes, as the table has every block the restore needs
        /// </summary>
        private readonly BlockHashTable<NeededBlock> m_blocks = new();
        /// <summary>
        /// The blocklists that are not yet expanded into blockset entries, by hash
        /// </summary>
        private readonly Dictionary<string, List<BlocklistOwner>> m_blocklists = new(StringComparer.Ordinal);
        /// <summary>
        /// The IDs of the block volumes registered in the database, by name
        /// </summary>
        private readonly Dictionary<string, long> m_volumeIds = new(StringComparer.Ordinal);
        /// <summary>
        /// The block volume that holds each blocklist that is not yet expanded, for those an index file has listed
        /// </summary>
        private readonly Dictionary<string, string> m_blocklistVolumes = new(StringComparer.Ordinal);
        /// <summary>
        /// The names of the block volumes that an index file lists the blocks of
        /// </summary>
        private readonly HashSet<string> m_describedVolumes = new(StringComparer.Ordinal);
        /// <summary>
        /// The number of needed blocks without a volume
        /// </summary>
        private long m_unresolvedBlocks;
        /// <summary>
        /// Set when a stop was requested while building
        /// </summary>
        private bool m_stopped;

        /// <summary>
        /// True while there is a block without a volume, or a blocklist that is neither expanded nor located
        /// </summary>
        private bool Seeking => m_unresolvedBlocks > 0 || m_blocklists.Count > m_blocklistVolumes.Count;
        /// <summary>
        /// Set when the remote data contradicts itself, so the result cannot be trusted
        /// </summary>
        private bool m_inconsistent;

        /// <summary>
        /// Creates a new builder.
        /// </summary>
        /// <param name="options">The options for the restore.</param>
        /// <param name="result">The results of the restore.</param>
        public DirectRestoreDatabaseBuilder(Options options, RestoreResults result)
        {
            m_options = options;
            m_result = result;
        }

        /// <summary>
        /// Fills the database with what is needed to restore the files matching the filter from a single version.
        /// </summary>
        /// <param name="backendManager">The backend manager to download the remote files with.</param>
        /// <param name="dbparent">The empty database to fill.</param>
        /// <param name="filter">The filter selecting the files to restore.</param>
        /// <param name="time">The time to restore from, with zero ticks for no time.</param>
        /// <param name="versions">The versions to restore from, or null or empty for no version.</param>
        /// <returns>Whether the database can be used for the restore.</returns>
        public async Task<Outcome> RunAsync(IBackendManager backendManager, LocalDatabase dbparent, IFilter? filter, DateTime time, long[]? versions)
        {
            var token = m_result.TaskControl.ProgressToken;
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_RecreateDatabase);

            await using var db = await LocalDirectRestoreDatabase.CreateAsync(dbparent, m_options, token).ConfigureAwait(false);

            // The database has one fileset and only some of the blocks, so it is of no use to a backup
            await db.PartiallyRecreatedAsync(token, true).ConfigureAwait(false);

            var remotefiles = await RecreateDatabaseHandler.ListRemoteVolumesAsync(backendManager, m_options, token).ConfigureAwait(false);
            var filelist = SelectFilelist(remotefiles, time, versions);
            Logging.Log.WriteInformationMessage(LOGTAG, "DirectRestoreStarted", "Building the restore database from filelist {0}", filelist.File.Name);

            using (new Logging.Timer(LOGTAG, "DirectRestoreFilelist", "Read filelist"))
                await LoadFilelistAsync(backendManager, db, filelist, filter, token).ConfigureAwait(false);

            if (!await m_result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
                return Outcome.Stopped;

            var blocksize = (long)m_options.Blocksize;
            var hashsize = (long)m_options.BlockhashSize;
            m_result.OperationProgressUpdater.UpdateProgress(0.1f);

            using (new Logging.Timer(LOGTAG, "DirectRestoreCollect", "Collect needed blocklists and blocks"))
            {
                await foreach (var (blocksetId, index, hash, length) in db.GetBlocklistHashesWithLengthAsync(token).ConfigureAwait(false))
                {
                    if (!m_blocklists.TryGetValue(hash, out var owners))
                        m_blocklists[hash] = owners = new List<BlocklistOwner>(1);
                    owners.Add(new BlocklistOwner(blocksetId, index, length));
                }

                // Read in full first, as the entries are written to a table the query does not read from,
                // but on the same connection
                var small = await db.GetSmallBlocksetLinksAsync(blocksize, token).ToListAsync(token).ConfigureAwait(false);
                foreach (var (blocksetId, blockHash, blockSize) in small)
                    await db.InsertBlocksetEntryAsync(blocksetId, 0, GetOrAddBlock(blockHash, blockSize), token).ConfigureAwait(false);
            }

            var blocklistCount = m_blocklists.Count;
            var dblocks = remotefiles
                .Where(x => x.FileType == RemoteVolumeType.Blocks)
                .GroupBy(x => x.File.Name, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            var indexfiles = remotefiles
                .Where(x => x.FileType == RemoteVolumeType.Index)
                .Select(x => (IRemoteVolume)new RemoteVolume(x.File, null))
                .ToList();

            // A block is mapped to its volume by the index file of that volume, but what makes the
            // block needed is a blocklist that can be in any index file. The index files are
            // therefore read twice: first for the blocklists, then for the volumes. They are kept
            // on disk in between, up to the size the volume cache may use.
            var retainLimit = m_options.RestoreVolumeCacheHint;
            if (retainLimit < 0)
                retainLimit = long.MaxValue;
            var retainedSize = 0L;
            var retained = new List<(TempFile File, string Name)>();
            var notRetained = new List<IRemoteVolume>();
            var indexFilesRead = 0L;
            var volumesScanned = 0L;

            // Reads the index files again, for the volumes of the blocks and blocklists that are still looked for
            async Task MapFromIndexFilesAsync()
            {
                foreach (var (tmpfile, name) in retained)
                {
                    if (!Seeking)
                        return;

                    await MapBlocksToVolumesAsync(db, tmpfile, name, dblocks, hashsize, token).ConfigureAwait(false);
                }

                if (Seeking && notRetained.Count > 0)
                    await foreach (var (tmpfile, _, _, name) in DownloadAsync(backendManager, notRetained, token).ConfigureAwait(false))
                    {
                        if (m_stopped || !await m_result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
                        {
                            m_stopped = true;
                            break;
                        }

                        // The remaining downloads are already started, so they are drained
                        if (Seeking)
                            await MapBlocksToVolumesAsync(db, tmpfile, name, dblocks, hashsize, token).ConfigureAwait(false);
                    }
            }

            try
            {
                using (new Logging.Timer(LOGTAG, "DirectRestoreBlocklists", "Read blocklists from index files"))
                    await foreach (var (tmpfile, _, size, name) in DownloadAsync(backendManager, indexfiles, token).ConfigureAwait(false))
                    {
                        if (!await m_result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
                        {
                            m_stopped = true;
                            break;
                        }

                        indexFilesRead++;
                        m_result.OperationProgressUpdater.UpdateProgress(0.1f + (((float)indexFilesRead / indexfiles.Count) * 0.5f));
                        Logging.Log.WriteVerboseMessage(LOGTAG, "ProcessingIndexlistVolumes", "Processing indexlist volume {0} of {1}", indexFilesRead, indexfiles.Count);

                        try
                        {
                            if (m_blocklists.Count > 0)
                                using (var reader = new IndexVolumeReader(RestoreHandler.GetCompressionModule(name), tmpfile, m_options, hashsize))
                                    await ExpandBlocklistsAsync(db, reader, name, blocksize, hashsize, token).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (!ex.IsAbortOrCancelException())
                        {
                            // The block volume it describes is then read as one without an index file, if it is needed
                            Logging.Log.WriteWarningMessage(LOGTAG, "DirectRestoreIndexFileFailed", ex, "Failed to process index file {0}, it is not used: {1}", name, ex.Message);
                            continue;
                        }

                        if (retainedSize + size <= retainLimit)
                        {
                            // The download deletes its file when the next one is asked for,
                            // so the content is moved to a file that is kept
                            var kept = new TempFile();
                            retained.Add((kept, name));
                            File.Move(tmpfile, kept, true);
                            retainedSize += size;
                        }
                        else
                            notRetained.Add(indexfiles.First(x => x.Name == name));
                    }

                if (!m_stopped)
                    using (new Logging.Timer(LOGTAG, "DirectRestoreVolumes", "Map blocks to volumes from index files"))
                    {
                        m_result.OperationProgressUpdater.UpdateProgress(0.6f);
                        await MapFromIndexFilesAsync().ConfigureAwait(false);
                    }

                // With --index-file-policy=Lookup the index files list the blocks of each volume, but
                // do not have a copy of the blocklists. The blocklists are then read from their volumes,
                // and the blocks they add are looked up in the index files
                if (!m_stopped && m_blocklistVolumes.Count > 0)
                    using (new Logging.Timer(LOGTAG, "DirectRestoreBlocklistVolumes", "Read blocklists from block volumes"))
                    {
                        var volumes = m_blocklistVolumes.Values.Distinct(StringComparer.Ordinal).Select(x => (IRemoteVolume)new RemoteVolume(dblocks[x].File, null)).ToList();
                        Logging.Log.WriteInformationMessage(LOGTAG, "DirectRestoreBlocklistVolumes", "The index files have no copy of {0} blocklists, reading them from {1} block volumes", m_blocklistVolumes.Count, volumes.Count);

                        volumesScanned += await ReadVolumesAsync(backendManager, db, volumes, false, blocksize, hashsize, 0.65f, 0.75f, token).ConfigureAwait(false);
                        if (!m_stopped)
                            await MapFromIndexFilesAsync().ConfigureAwait(false);
                    }

                // A block volume without an index file can hold anything, so those are listed until
                // everything is found. Without any index files, that is each of the block volumes
                if (!m_stopped && Seeking)
                {
                    var volumes = dblocks.Values.Where(x => !m_describedVolumes.Contains(x.File.Name)).Select(x => (IRemoteVolume)new RemoteVolume(x.File, null)).ToList();
                    if (volumes.Count > 0)
                        using (new Logging.Timer(LOGTAG, "DirectRestoreUnindexedVolumes", "Read block volumes without an index file"))
                        {
                            Logging.Log.WriteInformationMessage(LOGTAG, "DirectRestoreUnindexedVolumes", "The index files did not describe {0} blocklists and {1} blocks, reading up to {2} block volumes that no index file describes", m_blocklists.Count, m_unresolvedBlocks, volumes.Count);

                            var blocklistsBefore = m_blocklists.Count;
                            volumesScanned += await ReadVolumesAsync(backendManager, db, volumes, true, blocksize, hashsize, 0.75f, 0.85f, token).ConfigureAwait(false);

                            // A blocklist found in one of these volumes adds blocks that can be anywhere,
                            // including in a volume that was read before the blocklist was
                            if (!m_stopped && m_blocklists.Count != blocklistsBefore && Seeking)
                            {
                                await MapFromIndexFilesAsync().ConfigureAwait(false);
                                if (!m_stopped && Seeking)
                                    volumesScanned += await ReadVolumesAsync(backendManager, db, volumes, true, blocksize, hashsize, 0.85f, 0.9f, token).ConfigureAwait(false);
                            }
                        }
                }
            }
            catch (Exception ex) when (!ex.IsAbortOrCancelException())
            {
                // Not expected, and the recreate does not depend on what went wrong here
                Logging.Log.WriteWarningMessage(LOGTAG, "DirectRestoreIndexFailed", ex, "Failed to read the remote files, using a regular database rebuild: {0}", ex.Message);
                await db.Transaction.RollBackAsync(token).ConfigureAwait(false);
                return Outcome.UseRecreate;
            }
            finally
            {
                foreach (var (tmpfile, _) in retained)
                    tmpfile.Dispose();
            }

            await backendManager.WaitForEmptyAsync(db, token).ConfigureAwait(false);

            if (m_stopped)
            {
                // The blocks are not written, so the database is of no use to the restore
                await db.Transaction.RollBackAsync(token).ConfigureAwait(false);
                return Outcome.Stopped;
            }

            if (m_inconsistent)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "DirectRestoreInconsistent", "The remote files do not agree on the blocks, using a regular database rebuild");
                await db.Transaction.RollBackAsync(token).ConfigureAwait(false);
                return Outcome.UseRecreate;
            }

            m_result.OperationProgressUpdater.UpdateProgress(0.9f);
            using (new Logging.Timer(LOGTAG, "DirectRestoreWriteBlocks", "Write blocks"))
                foreach (var (hash, block) in m_blocks.Entries())
                    if (block.VolumeID >= 0)
                        await db.InsertBlockAsync(block.ID, hash, block.Size, block.VolumeID, token).ConfigureAwait(false);

            // What the destination does not have cannot be restored. The entries that need it are
            // taken out and reported, and the rest is restored. An entry that only lacks its
            // metadata is restored without it
            var (unrestorable, withoutMetadata) = await db.RemoveUnrestorableEntriesAsync(blocksize, token).ConfigureAwait(false);
            if (unrestorable.Count > 0)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "DirectRestoreMissingData", null, "The destination does not have {0} of {1} blocklists and {2} of {3} blocks that are needed, so {4} entries cannot be restored", m_blocklists.Count, blocklistCount, m_unresolvedBlocks, m_blocks.Count, unrestorable.Count);
                foreach (var path in unrestorable)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "DirectRestoreMissingDataForPath", null, "Cannot restore {0}, as not all of its data is at the destination", path);
                    m_result.BrokenLocalFiles.Add(path);
                }
            }

            if (withoutMetadata.Count > 0)
            {
                const int maxListed = 10;
                var listed = string.Join(Environment.NewLine, withoutMetadata.Take(maxListed));
                var first = withoutMetadata.Count > maxListed ? $"first {maxListed} " : string.Empty;
                Logging.Log.WriteWarningMessage(LOGTAG, "DirectRestoreMissingMetadata", null, "The destination does not have all of the metadata for {0} entries, such as timestamps and permissions, so they are restored without it. The {1}entries are:{2}{3}", withoutMetadata.Count, first, Environment.NewLine, listed);
                foreach (var path in withoutMetadata)
                    Logging.Log.WriteVerboseMessage(LOGTAG, "DirectRestoreMissingMetadataForPath", null, "Restoring {0} without metadata, as not all of it is at the destination", path);
            }

            await db.Transaction.CommitAsync(token).ConfigureAwait(false);
            m_result.OperationProgressUpdater.UpdateProgress(1f);

            Logging.Log.WriteInformationMessage(LOGTAG, "DirectRestoreCompleted", "Built the restore database from {0} index files and {1} block volumes: {2} blocks in {3} of {4} block volumes are needed", indexFilesRead, volumesScanned, m_blocks.Count - m_unresolvedBlocks, m_volumeIds.Count, dblocks.Count);
            return Outcome.Completed;
        }

        /// <summary>
        /// Downloads remote volumes one after another, starting on the next one while the current one
        /// is processed. A volume that cannot be downloaded is reported and left out, where the
        /// downloads of the backend manager end at the first failure.
        /// </summary>
        /// <param name="backendManager">The backend manager to download the volumes with.</param>
        /// <param name="volumes">The volumes to download.</param>
        /// <param name="token">The cancellation token to monitor for cancellation requests.</param>
        /// <returns>The downloaded files, which are deleted when the next one is asked for, with their hash, size and name.</returns>
        private async IAsyncEnumerable<(TempFile File, string Hash, long Size, string Name)> DownloadAsync(IBackendManager backendManager, List<IRemoteVolume> volumes, [EnumeratorCancellation] CancellationToken token)
        {
            Task<(TempFile File, string Hash, long Size)>? next = null;
            try
            {
                if (volumes.Count > 0)
                    next = backendManager.GetWithInfoAsync(volumes[0].Name, volumes[0].Hash, volumes[0].Size, true, token);

                for (var i = 0; i < volumes.Count; i++)
                {
                    var current = next!;
                    next = i + 1 < volumes.Count
                        ? backendManager.GetWithInfoAsync(volumes[i + 1].Name, volumes[i + 1].Hash, volumes[i + 1].Size, true, token)
                        : null;

                    (TempFile File, string Hash, long Size) result;
                    try
                    {
                        result = await current.ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!ex.IsAbortOrCancelException())
                    {
                        // What the volume holds is then looked for elsewhere, and reported if it is not found
                        Logging.Log.WriteWarningMessage(LOGTAG, "DirectRestoreDownloadFailed", ex, "Failed to download {0}, it is not used: {1}", volumes[i].Name, ex.Message);
                        continue;
                    }

                    using (result.File)
                        yield return (result.File, result.Hash, result.Size, volumes[i].Name);
                }
            }
            finally
            {
                // Left behind when the caller stops early
                if (next != null)
                    try { (await next.ConfigureAwait(false)).File.Dispose(); }
                    catch { }
            }
        }

        /// <summary>
        /// Selects the single filelist to restore from: the newest one matching the time and versions.
        /// </summary>
        /// <param name="remotefiles">The remote volumes of the backup.</param>
        /// <param name="time">The time to restore from, with zero ticks for no time.</param>
        /// <param name="versions">The versions to restore from, or null or empty for no version.</param>
        /// <returns>The filelist volume to restore from.</returns>
        private static IParsedVolume SelectFilelist(IParsedVolume[] remotefiles, DateTime time, long[]? versions)
        {
            IEnumerable<IParsedVolume> filelists =
                from n in remotefiles
                where n.FileType == RemoteVolumeType.Files
                orderby n.Time descending
                select n;

            if (!filelists.Any())
                throw new UserInformationException("No filelists found on the remote destination", "EmptyRemoteLocation");

            var filelistfilter = RestoreHandler.GetNumberedFilelistFilterDelegate(time, versions, false);
            if (filelistfilter != null)
                filelists = filelistfilter(filelists).Select(x => x.Value).OrderByDescending(x => x.Time).ToArray();

            return filelists.FirstOrDefault()
                ?? throw new UserInformationException("No filelists", "NoMatchingRemoteFilelists");
        }

        /// <summary>
        /// Downloads the filelist and records the fileset, the files matching the filter, and their blocksets.
        /// </summary>
        /// <param name="backendManager">The backend manager to download the filelist with.</param>
        /// <param name="db">The database to fill.</param>
        /// <param name="filelist">The filelist volume.</param>
        /// <param name="filter">The filter selecting the files to restore.</param>
        /// <param name="token">The cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the filelist is recorded.</returns>
        private async Task LoadFilelistAsync(IBackendManager backendManager, LocalDirectRestoreDatabase db, IParsedVolume filelist, IFilter? filter, CancellationToken token)
        {
            var volumeId = await db
                .RegisterRemoteVolumeAsync(filelist.File.Name, filelist.FileType, filelist.File.Size, RemoteVolumeState.Uploaded, token)
                .ConfigureAwait(false);

            // The hash algorithm is not known before the manifest is read, so the file is downloaded
            // without verification and decrypted here, as the recreate does
            var work = new List<IRemoteVolume> { new RemoteVolume(filelist.File, null) };
            await foreach (var (tmpencfile, name) in backendManager.GetFilesOverlappedDirectAsync(work, allowParityRepair: true, token).ConfigureAwait(false))
                using (tmpencfile)
                using (var tmpfile = backendManager.DecryptFile(tmpencfile, name, m_options, dispose: false))
                {
                    using var stream = new FileStream(tmpfile, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var compressor = DynamicLoader.CompressionLoader.GetModule(filelist.CompressionModule, stream, ArchiveMode.Read, m_options.RawOptions);
                    if (compressor == null)
                        throw new UserInformationException(Strings.Common.CompressionModuleNotLoaded(filelist.CompressionModule, name), "FailedToLoadCompressionModule");

                    VolumeReaderBase.UpdateOptionsFromManifest(compressor, m_options);

                    var size = new FileInfo(tmpencfile).Length;
                    string hash;
                    using (var fs = File.OpenRead(tmpencfile))
                    using (var hasher = HashFactory.CreateHasher(m_options.FileHashAlgorithm))
                        hash = Convert.ToBase64String(hasher.ComputeHash(fs));

                    if (size > 0)
                        await db.UpdateRemoteVolumeAsync(name, RemoteVolumeState.Verified, size, hash, token).ConfigureAwait(false);

                    var filesetId = await db.CreateFilesetAsync(volumeId, filelist.Time, token).ConfigureAwait(false);
                    await RecreateDatabaseHandler.RecreateFilesetFromRemoteListAsync(db, compressor, filesetId, m_options, filter, token).ConfigureAwait(false);
                }

            await Utility.VerifyOptionsAndUpdateDatabaseAsync(db, m_options, token).ConfigureAwait(false);
            await db.Transaction.CommitAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the ID of a needed block, registering the block if it is not yet known.
        /// </summary>
        /// <param name="hash">The hash of the block.</param>
        /// <param name="size">The size of the block.</param>
        /// <returns>The ID the block gets in the database.</returns>
        private long GetOrAddBlock(string hash, long size)
        {
            if (m_blocks.TryGetValue(hash, out var block))
            {
                if (block.Size != size && !m_inconsistent)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "DirectRestoreBlockSizeMismatch", null, "The block {0} is used with size {1} and with size {2}", hash, block.Size, size);
                    m_inconsistent = true;
                }

                return block.ID;
            }

            // The Block table is empty, so the IDs can be handed out here
            block = new NeededBlock(m_blocks.Count + 1, size, -1);
            m_blocks.Add(hash, block);
            m_unresolvedBlocks++;
            return block.ID;
        }

        /// <summary>
        /// Writes the blockset entries for the needed blocklists found in an index file.
        /// </summary>
        /// <param name="db">The database to fill.</param>
        /// <param name="reader">The index file.</param>
        /// <param name="name">The name of the index file.</param>
        /// <param name="blocksize">The size of a full block.</param>
        /// <param name="hashsize">The size of a block hash in bytes.</param>
        /// <param name="token">The cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the entries are written.</returns>
        private async Task ExpandBlocklistsAsync(LocalDirectRestoreDatabase db, IndexVolumeReader reader, string name, long blocksize, long hashsize, CancellationToken token)
        {
            var hashesPerBlocklist = blocksize / hashsize;

            foreach (var blocklist in reader.BlockLists)
            {
                var blocklistHash = blocklist.Hash;
                if (!m_blocklists.TryGetValue(blocklistHash, out var owners))
                    continue;

                List<string> hashes;
                try
                {
                    // Reading it in full verifies the content against the hash
                    hashes = blocklist.Blocklist.ToList();
                }
                catch (InvalidDataException ex)
                {
                    // Another index file can have a valid copy
                    Logging.Log.WriteVerboseMessage(LOGTAG, "InvalidDataBlocklist", ex, "Exception while processing blocklists in {0}", name);
                    continue;
                }

                await WriteBlocklistEntriesAsync(db, blocklistHash, owners, hashes, name, blocksize, hashesPerBlocklist, token).ConfigureAwait(false);
                if (m_blocklists.Count == 0)
                    break;
            }
        }

        /// <summary>
        /// Records the volume for each needed block that an index file lists.
        /// </summary>
        /// <param name="db">The database to fill.</param>
        /// <param name="indexfile">The decrypted index file.</param>
        /// <param name="name">The name of the index file.</param>
        /// <param name="dblocks">The block volumes found at the destination, by name.</param>
        /// <param name="hashsize">The size of a block hash in bytes.</param>
        /// <param name="token">The cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the index file is processed.</returns>
        private async Task MapBlocksToVolumesAsync(LocalDirectRestoreDatabase db, TempFile indexfile, string name, Dictionary<string, IParsedVolume> dblocks, long hashsize, CancellationToken token)
        {
            try
            {
                using var reader = new IndexVolumeReader(RestoreHandler.GetCompressionModule(name), indexfile, m_options, hashsize);
                await MapBlocksToVolumesAsync(db, reader, name, dblocks, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ex.IsAbortOrCancelException())
            {
                // The block volumes it describes are then read as ones without an index file, if they are needed
                Logging.Log.WriteWarningMessage(LOGTAG, "DirectRestoreIndexFileFailed", ex, "Failed to process index file {0}, it is not used: {1}", name, ex.Message);
            }
        }

        /// <summary>
        /// Records the volume for each needed block that an index file lists.
        /// </summary>
        /// <param name="db">The database to fill.</param>
        /// <param name="reader">The index file.</param>
        /// <param name="name">The name of the index file.</param>
        /// <param name="dblocks">The block volumes found at the destination, by name.</param>
        /// <param name="token">The cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the index file is processed.</returns>
        private async Task MapBlocksToVolumesAsync(LocalDirectRestoreDatabase db, IndexVolumeReader reader, string name, Dictionary<string, IParsedVolume> dblocks, CancellationToken token)
        {
            var found = new List<string>();
            foreach (var volume in reader.Volumes)
            {
                var filename = volume.Filename;
                if (!dblocks.TryGetValue(filename, out var remote))
                {
                    // The blocks can be listed for another volume as well, so this is only a problem if they stay unresolved
                    Logging.Log.WriteVerboseMessage(LOGTAG, "DirectRestoreVolumeNotFound", null, "Remote file referenced as {0} by {1}, but not found in list", filename, name);
                    continue;
                }

                // The size and hash of the volume follow the blocks in the index file, and reading
                // them moves the block reader to the end, so the blocks are read in full first
                found.Clear();
                foreach (var (hash, size) in volume.Blocks)
                {
                    if (m_blocks.TryGetValue(hash, out var block) && block.VolumeID < 0 && block.Size == size)
                        found.Add(hash);
                    else if (m_blocklists.ContainsKey(hash))
                        m_blocklistVolumes.TryAdd(hash, filename);
                }

                m_describedVolumes.Add(filename);
                if (found.Count > 0)
                    await AssignVolumeAsync(db, found, filename, remote.File.Size, volume.Length, volume.Hash, token).ConfigureAwait(false);

                if (!Seeking)
                    return;
            }
        }

        /// <summary>
        /// Records that a block volume holds the given blocks, registering the volume if needed.
        /// </summary>
        /// <param name="db">The database to fill.</param>
        /// <param name="hashes">The hashes of the needed blocks found in the volume.</param>
        /// <param name="filename">The name of the block volume.</param>
        /// <param name="listedSize">The size of the volume in the remote listing.</param>
        /// <param name="size">The size of the volume, as recorded or measured.</param>
        /// <param name="hash">The hash of the volume, as recorded or measured.</param>
        /// <param name="token">The cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the blocks are assigned.</returns>
        private async Task AssignVolumeAsync(LocalDirectRestoreDatabase db, List<string> hashes, string filename, long listedSize, long size, string? hash, CancellationToken token)
        {
            if (!m_volumeIds.TryGetValue(filename, out var volumeId))
            {
                volumeId = await db
                    .RegisterRemoteVolumeAsync(filename, RemoteVolumeType.Blocks, listedSize, RemoteVolumeState.Verified, token)
                    .ConfigureAwait(false);
                // The size and hash let the download be verified
                if (!string.IsNullOrWhiteSpace(hash) && size > 0)
                    await db
                        .UpdateRemoteVolumeAsync(filename, RemoteVolumeState.Verified, size, hash, token)
                        .ConfigureAwait(false);
                m_volumeIds.Add(filename, volumeId);
            }

            foreach (var blockHash in hashes)
            {
                var block = m_blocks[blockHash];
                // A volume can list the same block twice
                if (block.VolumeID >= 0)
                    continue;

                m_blocks[blockHash] = block with { VolumeID = volumeId };
                m_unresolvedBlocks--;
            }
        }

        /// <summary>
        /// Writes the blockset entries for a blocklist, and marks the blocklist as expanded.
        /// </summary>
        /// <param name="db">The database to fill.</param>
        /// <param name="blocklistHash">The hash of the blocklist.</param>
        /// <param name="owners">The blocksets the blocklist belongs to.</param>
        /// <param name="hashes">The block hashes in the blocklist.</param>
        /// <param name="name">The name of the remote file the blocklist was read from.</param>
        /// <param name="blocksize">The size of a full block.</param>
        /// <param name="hashesPerBlocklist">The number of hashes in a full blocklist.</param>
        /// <param name="token">The cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the entries are written.</returns>
        private async Task WriteBlocklistEntriesAsync(LocalDirectRestoreDatabase db, string blocklistHash, List<BlocklistOwner> owners, List<string> hashes, string name, long blocksize, long hashesPerBlocklist, CancellationToken token)
        {
            foreach (var owner in owners)
            {
                var firstIndex = owner.Index * hashesPerBlocklist;
                for (var i = 0; i < hashes.Count; i++)
                {
                    var index = firstIndex + i;
                    var size = Math.Min(blocksize, owner.Length - (index * blocksize));
                    if (size <= 0)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "DirectRestoreBlocklistTooLong", null, "The blocklist {0} in {1} has more hashes than the blockset {2} of {3} bytes can use", blocklistHash, name, owner.BlocksetID, owner.Length);
                        m_inconsistent = true;
                        break;
                    }

                    await db.InsertBlocksetEntryAsync(owner.BlocksetID, index, GetOrAddBlock(hashes[i], size), token).ConfigureAwait(false);
                }
            }

            m_blocklists.Remove(blocklistHash);
            m_blocklistVolumes.Remove(blocklistHash);
        }

        /// <summary>
        /// Downloads block volumes and reads the needed blocklists from them.
        /// </summary>
        /// <param name="backendManager">The backend manager to download the volumes with.</param>
        /// <param name="db">The database to fill.</param>
        /// <param name="volumes">The block volumes to read.</param>
        /// <param name="listBlocks">True to also record the volume for the needed blocks it holds, for a volume no index file describes.</param>
        /// <param name="blocksize">The size of a full block.</param>
        /// <param name="hashsize">The size of a block hash in bytes.</param>
        /// <param name="progressStart">The progress to report before the first volume.</param>
        /// <param name="progressEnd">The progress to report after the last volume.</param>
        /// <param name="token">The cancellation token to monitor for cancellation requests.</param>
        /// <returns>The number of volumes that were read.</returns>
        private async Task<long> ReadVolumesAsync(IBackendManager backendManager, LocalDirectRestoreDatabase db, List<IRemoteVolume> volumes, bool listBlocks, long blocksize, long hashsize, float progressStart, float progressEnd, CancellationToken token)
        {
            var hashesPerBlocklist = blocksize / hashsize;
            var listedSizes = volumes.ToDictionary(x => x.Name, x => x.Size, StringComparer.Ordinal);
            var found = new List<string>();
            var blocklists = new List<string>();
            var buffer = new byte[blocksize];
            var count = 0L;
            var downloaded = 0L;

            using var hasher = HashFactory.CreateHasher(m_options.BlockHashAlgorithm);
            await foreach (var (tmpfile, volumeHash, volumeSize, name) in DownloadAsync(backendManager, volumes, token).ConfigureAwait(false))
            {
                if (m_stopped || !await m_result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
                {
                    m_stopped = true;
                    break;
                }

                downloaded++;
                m_result.OperationProgressUpdater.UpdateProgress(progressStart + (((float)downloaded / volumes.Count) * (progressEnd - progressStart)));

                // The remaining downloads are already started, so they are drained
                if (listBlocks ? !Seeking : m_blocklistVolumes.Count == 0)
                    continue;

                count++;
                Logging.Log.WriteVerboseMessage(LOGTAG, "ProcessingBlocklistVolumes", "Processing block volume {0} of {1}", count, volumes.Count);
                using var reader = new BlockVolumeReader(RestoreHandler.GetCompressionModule(name), tmpfile, m_options);

                found.Clear();
                blocklists.Clear();
                if (listBlocks)
                {
                    foreach (var (hash, size) in reader.Blocks)
                    {
                        if (m_blocks.TryGetValue(hash, out var block) && block.VolumeID < 0 && block.Size == size)
                            found.Add(hash);
                        else if (m_blocklists.ContainsKey(hash))
                            blocklists.Add(hash);
                    }

                    if (found.Count > 0)
                        await AssignVolumeAsync(db, found, name, listedSizes[name], volumeSize, volumeHash, token).ConfigureAwait(false);
                }
                else
                    blocklists.AddRange(m_blocklistVolumes.Where(x => x.Value == name).Select(x => x.Key));

                foreach (var blocklistHash in blocklists)
                {
                    if (!m_blocklists.TryGetValue(blocklistHash, out var owners))
                        continue;

                    int read;
                    using (var stream = reader.ReadBlocklistRaw(blocklistHash))
                        read = stream == null ? -1 : Library.Utility.Utility.ForceStreamRead(stream, buffer, buffer.Length);

                    // The content is checked against the hash, as it decides which blocks a file is made of
                    if (read <= 0 || read % hashsize != 0 || Convert.ToBase64String(hasher.ComputeHash(buffer, 0, read)) != blocklistHash)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "DirectRestoreInvalidBlocklist", null, "The blocklist {0} in {1} does not match its hash and is not used", blocklistHash, name);
                        // It is not looked for in this volume again
                        m_blocklistVolumes.Remove(blocklistHash);
                        continue;
                    }

                    var hashes = new List<string>((int)(read / hashsize));
                    for (var i = 0; i < read; i += (int)hashsize)
                        hashes.Add(Convert.ToBase64String(buffer, i, (int)hashsize));

                    await WriteBlocklistEntriesAsync(db, blocklistHash, owners, hashes, name, blocksize, hashesPerBlocklist, token).ConfigureAwait(false);
                }
            }

            return count;
        }
    }
}
