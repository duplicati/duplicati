// Copyright (C) 2025, The Duplicati Team
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation
{
    internal class RecreateDatabaseHandler : IDisposable
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<RecreateDatabaseHandler>();

        private readonly Options m_options;
        private readonly RecreateDatabaseResults m_result;

        public delegate IEnumerable<KeyValuePair<long, IParsedVolume>> NumberedFilterFilelistDelegate(IEnumerable<IParsedVolume> filelist);
        public delegate void BlockVolumePostProcessor(string volumename, BlockVolumeReader reader);

        public RecreateDatabaseHandler(Options options, RecreateDatabaseResults result)
        {
            m_options = options;
            m_result = result;
        }

        /// <summary>
        /// Run the recreate procedure.
        /// </summary>
        /// <param name="path">Path to the database that will be created.</param>
        /// <param name="backendManager">The backend manager to use for downloading files.</param>
        /// <param name="filter">Filters the files in a filelist to prevent downloading unwanted data.</param>
        /// <param name="filelistfilter">A filter that can be used to disregard certain remote files, intended to be used to select a certain filelist.</param>
        /// <param name="blockprocessor">A callback hook that can be used to work with downloaded block volumes, intended to be use to recover data blocks while processing blocklists.</param>
        public async Task RunAsync(string path, IBackendManager backendManager, IFilter filter, NumberedFilterFilelistDelegate filelistfilter, BlockVolumePostProcessor blockprocessor)
        {
            if (File.Exists(path))
                throw new UserInformationException(string.Format("Cannot recreate database because file already exists: {0}", path), "RecreateTargetDatabaseExists");

            await using var db =
                await LocalDatabase.CreateLocalDatabaseAsync(path, "Recreate", true, null, m_result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);

            await DoRunAsync(backendManager, db, false, filter, filelistfilter, blockprocessor).ConfigureAwait(false);

            // Ensure database is consistent after the recreate
            await db
                .VerifyConsistency(m_options.Blocksize, m_options.BlockhashSize, true, m_result.TaskControl.ProgressToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Updates a database with new path information from a remote fileset.
        /// </summary>
        /// <param name="backendManager">The backend manager to use for downloading files.</param>
        /// <param name="filter">Filters the files in a filelist to prevent downloading unwanted data.</param>
        /// <param name="filelistfilter">A filter that can be used to disregard certain remote files, intended to be used to select a certain filelist.</param>
        /// <param name="blockprocessor">A callback hook that can be used to work with downloaded block volumes, intended to be use to recover data blocks while processing blocklists.</param>
        public async Task RunUpdateAsync(IBackendManager backendManager, Library.Utility.IFilter filter, NumberedFilterFilelistDelegate filelistfilter, BlockVolumePostProcessor blockprocessor)
        {
            if (!m_options.RepairOnlyPaths)
                throw new UserInformationException(string.Format("Can only update with paths, try setting --{0}", "repair-only-paths"), "RepairUpdateRequiresPathsOnly");

            await using var db =
                await LocalDatabase.CreateLocalDatabaseAsync(m_options.Dbpath, "Recreate", true, null, m_result.TaskControl.ProgressToken)
                .ConfigureAwait(false);
            if ((await db.FindMatchingFilesets(m_options.Time, m_options.Version, m_result.TaskControl.ProgressToken).ConfigureAwait(false)).Any())
            {
                if (m_options.IgnoreUpdateIfVersionExists)
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "UpdateVersionAlreadyExists", "The version(s) being updated to already exists, ignoring update request");
                    return;
                }

                throw new UserInformationException("The version(s) being updated to, already exists", "UpdateVersionAlreadyExists");
            }

            // Mark as incomplete
            await db.PartiallyRecreated(m_result.TaskControl.ProgressToken, true).ConfigureAwait(false);

            var preexistingOptionsInDatabase =
                await Utility.ContainsOptionsForVerification(db, m_result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);
            await Utility.UpdateOptionsFromDb(db, m_options, m_result.TaskControl.ProgressToken)
                .ConfigureAwait(false);

            // Make sure the options have not changed between calls, unless there are no previous options
            if (preexistingOptionsInDatabase)
                await Utility.VerifyOptionsAndUpdateDatabase(db, m_options, m_result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);

            await DoRunAsync(backendManager, db, true, filter, filelistfilter, blockprocessor).ConfigureAwait(false);
        }

        /// <summary>
        /// Run the recreate procedure.
        /// </summary>
        /// <param name="backendManager">The backend manager to use for downloading files.</param>
        /// <param name="dbparent">The database to restore into.</param>
        /// <param name="updating">True if this is an update call, false otherwise.</param>
        /// <param name="filter">A filter that can be used to disregard certain remote files, intended to be used to select a certain filelist.</param>
        /// <param name="filelistfilter">Filters the files in a filelist to prevent downloading unwanted data.</param>
        /// <param name="blockprocessor">A callback hook that can be used to work with downloaded block volumes, intended to be use to recover data blocks while processing blocklists.</param>
        /// <returns>A task that completes when the operation is done.</returns>
        internal async Task DoRunAsync(IBackendManager backendManager, LocalDatabase dbparent, bool updating, IFilter filter = null, NumberedFilterFilelistDelegate filelistfilter = null, BlockVolumePostProcessor blockprocessor = null)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Recreate_Running);

            //We build a local database in steps.
            await using var restoredb =
                await LocalRecreateDatabase.CreateAsync(dbparent, m_options, null, m_result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);
            await restoredb.RepairInProgress(m_result.TaskControl.ProgressToken, true).ConfigureAwait(false);
            var expRecreateDb = false; // experimental recreate db code flag
            var volumeIds = new Dictionary<string, long>();

            if (string.Equals(Environment.GetEnvironmentVariable("EXPERIMENTAL_RECREATEDB_DUPLICATI") ?? string.Empty, "1"))
            {
                expRecreateDb = true;
            }

            var rawlist = await backendManager.ListAsync(m_result.TaskControl.ProgressToken).ConfigureAwait(false);

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
                if (!rawlist.Any())
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
                        throw new UserInformationException(string.Format("Found {0} files at the remote storage, but none that could be parsed", rawlist.Count()), "EmptyRemoteLocation");
                    else if (types.Length == 1)
                        throw new UserInformationException(string.Format("Found {0} parse-able files with the prefix {1}, did you forget to set the backup prefix?", tmp.Length, types[0]), "EmptyRemoteLocationWithPrefix");
                    else
                        throw new UserInformationException(string.Format("Found {0} parse-able files (of {1} files) with different prefixes: {2}, did you forget to set the backup prefix?", tmp.Length, rawlist.Count(), string.Join(", ", types)), "EmptyRemoteLocationWithPrefix");
                }
            }

            if (string.IsNullOrWhiteSpace(m_options.Passphrase) && remotefiles.Any(x => !string.IsNullOrWhiteSpace(x.EncryptionModule)))
                throw new UserInformationException("The remote files are encrypted, but no passphrase was provided", "MissingPassphrase");

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
            foreach (var fl in remotefiles)
                volumeIds[fl.File.Name] = updating
                    ? await restoredb.GetRemoteVolumeID(fl.File.Name, m_result.TaskControl.ProgressToken)
                        .ConfigureAwait(false)
                    : await restoredb.RegisterRemoteVolume(fl.File.Name, fl.FileType, fl.File.Size, RemoteVolumeState.Uploaded, m_result.TaskControl.ProgressToken)
                        .ConfigureAwait(false);

            var hasUpdatedOptions = false;

            // Record all blocksets and files needed
            var filelistWork = (from n in filelists orderby n.Time select new RemoteVolume(n.File) as IRemoteVolume).ToList();
            Logging.Log.WriteInformationMessage(LOGTAG, "RebuildStarted", "Rebuild database started, downloading {0} filelists", filelistWork.Count);

            var progress = 0;

            // Register the files we are working with, if not already updated
            if (updating)
            {
                foreach (var n in filelists)
                    if (volumeIds[n.File.Name] == -1)
                        volumeIds[n.File.Name] = await restoredb
                            .RegisterRemoteVolume(n.File.Name, n.FileType, RemoteVolumeState.Uploaded, n.File.Size, new TimeSpan(0), m_result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);
            }

            var isFirstFilelist = true;
            await foreach (var (tmpfile, hash, size, name) in backendManager.GetFilesOverlappedAsync(filelistWork, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
            {
                var entry = new RemoteVolume(name, hash, size);
                try
                {
                    if (!await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                    {
                        await backendManager.WaitForEmptyAsync(restoredb, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                        m_result.EndTime = DateTime.UtcNow;
                        // Implicit rollback
                        await restoredb.Transaction
                            .RollBackAsync(m_result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);
                        return;
                    }

                    progress++;
                    if (filelistWork.Count == 1 && m_options.RepairOnlyPaths)
                    {
                        m_result.OperationProgressUpdater.UpdateProgress(0.5f);
                    }
                    else
                    {
                        m_result.OperationProgressUpdater.UpdateProgress(((float)progress / filelistWork.Count()) * (m_options.RepairOnlyPaths ? 1f : 0.2f));
                        Logging.Log.WriteVerboseMessage(LOGTAG, "ProcessingFilelistVolumes", "Processing filelist volume {0} of {1}", progress, filelistWork.Count);
                    }

                    using (tmpfile)
                    {
                        isFirstFilelist = false;

                        if (!string.IsNullOrWhiteSpace(hash) && size > 0)
                            await restoredb
                                .UpdateRemoteVolume(entry.Name, RemoteVolumeState.Verified, size, hash, m_result.TaskControl.ProgressToken)
                                .ConfigureAwait(false);

                        var parsed = VolumeBase.ParseFilename(entry.Name);

                        using var stream = new FileStream(tmpfile, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var compressor = DynamicLoader.CompressionLoader.GetModule(parsed.CompressionModule, stream, ArchiveMode.Read, m_options.RawOptions);
                        if (compressor == null)
                            throw new UserInformationException(string.Format("Failed to load compression module: {0}", parsed.CompressionModule), "FailedToLoadCompressionModule");

                        if (!hasUpdatedOptions)
                        {
                            VolumeReaderBase.UpdateOptionsFromManifest(compressor, m_options);
                            hasUpdatedOptions = true;
                        }

                        // Create timestamped operations based on the file timestamp
                        var filesetid = await restoredb
                            .CreateFileset(volumeIds[entry.Name], parsed.Time, m_result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);

                        await RecreateFilesetFromRemoteList(restoredb, compressor, filesetid, m_options, filter, m_result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "FileProcessingFailed", ex, "Failed to process file: {0}", entry.Name);
                    if (ex.IsAbortException())
                    {
                        m_result.EndTime = DateTime.UtcNow;
                        // Implicit rollback
                        await restoredb.Transaction
                            .RollBackAsync(m_result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);
                        throw;
                    }

                    if (isFirstFilelist && ex is System.Security.Cryptography.CryptographicException)
                    {
                        m_result.EndTime = DateTime.UtcNow;
                        // Implicit rollback
                        await restoredb.Transaction
                            .RollBackAsync(m_result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);
                        throw;
                    }

                    if (m_options.UnittestMode)
                    {
                        // Implicit rollback
                        await restoredb.Transaction
                            .RollBackAsync(m_result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);
                        throw;
                    }
                }
            }

            //Make sure we write the config if it has been read from a manifest
            if (hasUpdatedOptions)
                await Utility.VerifyOptionsAndUpdateDatabase(restoredb, m_options, m_result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);

            using (new Logging.Timer(LOGTAG, "CommitUpdateFilesetFromRemote", "CommitUpdateFilesetFromRemote"))
                await restoredb.Transaction
                    .CommitAsync(m_result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);

            // do we stop after just handling the dlist files ?
            // (if yes, we never will be able to do a backup !)
            if (!m_options.RepairOnlyPaths)
            {
                var hashsize = 0;
                //Grab all index files, and update the block table

                using (var hashalg = HashFactory.CreateHasher(m_options.BlockHashAlgorithm))
                {
                    hashsize = hashalg.HashSize / 8;

                    var indexfiles = (
                                     from n in remotefiles
                                     where n.FileType == RemoteVolumeType.Index
                                     select new RemoteVolume(n.File) as IRemoteVolume).ToList();

                    Logging.Log.WriteInformationMessage(LOGTAG, "FilelistsRestored", "Filelists restored, downloading {0} index files", indexfiles.Count);

                    progress = 0;

                    await foreach (var (tmpfile, hash, size, name) in backendManager.GetFilesOverlappedAsync(indexfiles, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                    {
                        try
                        {
                            if (!await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                            {
                                await backendManager.WaitForEmptyAsync(restoredb, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                                m_result.EndTime = DateTime.UtcNow;
                                // Implicit rollback
                                await restoredb.Transaction
                                    .RollBackAsync(m_result.TaskControl.ProgressToken)
                                    .ConfigureAwait(false);
                                return;
                            }

                            progress++;
                            m_result.OperationProgressUpdater.UpdateProgress((((float)progress / indexfiles.Count) * 0.5f) + 0.2f);
                            Logging.Log.WriteVerboseMessage(LOGTAG, "ProcessingIndexlistVolumes", "Processing indexlist volume {0} of {1}", progress, indexfiles.Count);

                            using (tmpfile)
                            {
                                if (!string.IsNullOrWhiteSpace(hash) && size > 0)
                                    await restoredb
                                        .UpdateRemoteVolume(name, RemoteVolumeState.Verified, size, hash, m_result.TaskControl.ProgressToken)
                                        .ConfigureAwait(false);

                                using (var svr = new IndexVolumeReader(RestoreHandler.GetCompressionModule(name), tmpfile, m_options, hashsize))
                                {
                                    foreach (var a in svr.Volumes)
                                    {
                                        var filename = a.Filename;
                                        var volumeID = await restoredb
                                            .GetRemoteVolumeID(filename, m_result.TaskControl.ProgressToken)
                                            .ConfigureAwait(false);

                                        // No such file
                                        if (volumeID < 0)
                                            (volumeID, filename) =
                                                await ProbeForMatchingFilename(filename, restoredb, m_result.TaskControl.ProgressToken)
                                                    .ConfigureAwait(false);

                                        var missing = false;
                                        // Still broken, register a missing item
                                        if (volumeID < 0)
                                        {
                                            var p = VolumeBase.ParseFilename(filename);
                                            if (p == null)
                                                throw new Exception(string.Format("Unable to parse filename: {0}", filename));
                                            Logging.Log.WriteWarningMessage(LOGTAG, "MissingFileDetected", null, "Remote file referenced as {0} by {1}, but not found in list, registering a missing remote file", filename, name);
                                            missing = true;
                                            volumeID = await restoredb
                                                .RegisterRemoteVolume(filename, p.FileType, RemoteVolumeState.Temporary, m_result.TaskControl.ProgressToken)
                                                .ConfigureAwait(false);
                                        }

                                        bool anyChange = false;
                                        //Add all block/volume mappings
                                        foreach (var b in a.Blocks)
                                        {
                                            anyChange |= (await restoredb.UpdateBlock(b.Key, b.Value, volumeID, m_result.TaskControl.ProgressToken).ConfigureAwait(false)).Item1;
                                        }

                                        await restoredb
                                            .UpdateRemoteVolume(filename, missing ? RemoteVolumeState.Temporary : RemoteVolumeState.Verified, a.Length, a.Hash, m_result.TaskControl.ProgressToken)
                                            .ConfigureAwait(false);
                                        await restoredb
                                            .AddIndexBlockLink(
                                                await restoredb.GetRemoteVolumeID(name, m_result.TaskControl.ProgressToken).ConfigureAwait(false),
                                                volumeID,
                                                m_result.TaskControl.ProgressToken
                                            )
                                            .ConfigureAwait(false);
                                    }

                                    //If there are blocklists in the index file, add them to the temp blocklist hashes table
                                    int wrongHashes = 0;
                                    foreach (var b in svr.BlockLists)
                                    {
                                        // Compact might have created undetected invalid blocklist entries in index files due to broken LocalDatabase.GetBlocklists
                                        // If the hash is wrong, recreate will download the dblock volume with the correct file
                                        try
                                        {
                                            // We need to instantiate the list to ensure the verification is
                                            // done before we add it to the database, since we do not have nested transactions
                                            var list = b.Blocklist.ToList();
                                            await restoredb
                                                .AddTempBlockListHash(b.Hash, list, m_result.TaskControl.ProgressToken)
                                                .ConfigureAwait(false);
                                        }
                                        catch (System.IO.InvalidDataException e)
                                        {
                                            Logging.Log.WriteVerboseMessage(LOGTAG, "InvalidDataBlocklist", e, "Exception while processing blocklists in {0}", name);
                                            ++wrongHashes;
                                        }
                                    }
                                    if (wrongHashes != 0)
                                    {
                                        Logging.Log.WriteWarningMessage(LOGTAG, "WrongBlocklistHashes", null, "{0} had invalid blocklists which could not be used. Consider deleting this index file and run repair to recreate it.", name);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            //Not fatal
                            Logging.Log.WriteErrorMessage(LOGTAG, "IndexFileProcessingFailed", ex, "Failed to process index file: {0}", name);
                            if (ex.IsAbortException())
                            {
                                m_result.EndTime = DateTime.UtcNow;
                                // Implicit rollback
                                await restoredb.Transaction
                                    .RollBackAsync(m_result.TaskControl.ProgressToken)
                                    .ConfigureAwait(false);
                                throw;
                            }

                            if (m_options.UnittestMode)
                            {
                                // Implicit rollback
                                await restoredb.Transaction
                                    .RollBackAsync(m_result.TaskControl.ProgressToken)
                                    .ConfigureAwait(false);
                                throw;
                            }
                        }
                    }

                    using (new Logging.Timer(LOGTAG, "CommitRecreateDb", "CommitRecreatedDb"))
                        await restoredb.Transaction
                            .CommitAsync(m_result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);

                    // TODO: In some cases, we can avoid downloading all index files,
                    // if we are lucky and pick the right ones
                }

                await restoredb.CleanupMissingVolumes(m_result.TaskControl.ProgressToken).ConfigureAwait(false);

                // Update the real tables from the temp tables
                if (expRecreateDb)
                    // add missing blocks and blocksetentry data (at this point
                    // we have not yet anything in the blocksetentry table)
                    await restoredb
                        .AddBlockAndBlockSetEntryFromTemp(hashsize, m_options.Blocksize, false, m_result.TaskControl.ProgressToken)
                        .ConfigureAwait(false);
                else
                    await restoredb
                        .FindMissingBlocklistHashes(hashsize, m_options.Blocksize, m_result.TaskControl.ProgressToken)
                        .ConfigureAwait(false);

                // We have now grabbed as much information as possible,
                // if we are still missing data, we must now fetch block files
                //We do this in three passes
                for (var i = 0; i < 3; i++)
                {
                    // Grab the list matching the pass type
                    var lst = await restoredb
                        .GetMissingBlockListVolumes(i, m_options.Blocksize, hashsize, m_options.RepairForceBlockUse, m_result.TaskControl.ProgressToken)
                        .ToListAsync()
                        .ConfigureAwait(false);

                    if (lst.Count == 0)
                        continue;

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
                            Logging.Log.WriteInformationMessage(LOGTAG, "ProcessingAllBlocklistVolumes", "Processing all of the {0} volumes for blocklists{1}", lst.Count, m_options.FullResult ? fullist : string.Empty);
                            break;
                    }

                    progress = 0;
                    await foreach (var (tmpfile, hash, size, name) in backendManager.GetFilesOverlappedAsync(lst, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                    {
                        try
                        {
                            using (tmpfile)
                            using (var rd = new BlockVolumeReader(RestoreHandler.GetCompressionModule(name), tmpfile, m_options))
                            {
                                if (!await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                                {
                                    await backendManager.WaitForEmptyAsync(restoredb, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                                    m_result.EndTime = DateTime.UtcNow;
                                    // Implicit rollback
                                    await restoredb.Transaction
                                        .RollBackAsync(m_result.TaskControl.ProgressToken)
                                        .ConfigureAwait(false);
                                    return;
                                }

                                progress++;
                                m_result.OperationProgressUpdater.UpdateProgress((((float)progress / lst.Count) * 0.1f) + 0.7f + (i * 0.1f));
                                Logging.Log.WriteVerboseMessage(LOGTAG, "ProcessingBlocklistVolumes", "Pass {0} of 3, processing blocklist volume {1} of {2}", (i + 1), progress, lst.Count);

                                var volumeid = await restoredb
                                    .GetRemoteVolumeID(name, m_result.TaskControl.ProgressToken)
                                    .ConfigureAwait(false);

                                await restoredb
                                    .UpdateRemoteVolume(name, RemoteVolumeState.Uploaded, size, hash, m_result.TaskControl.ProgressToken)
                                    .ConfigureAwait(false);

                                bool anyChange = false;
                                // Update the block table so we know about the block/volume map
                                foreach (var h in rd.Blocks)
                                {
                                    anyChange |= (await restoredb.UpdateBlock(h.Key, h.Value, volumeid, m_result.TaskControl.ProgressToken).ConfigureAwait(false)).Item1;
                                }

                                // now that we have the blocks/volume relationships, we can go from the (already known from dlist step) blocklisthashes
                                // to the needed list blocks in the volume, so grab them from the database
                                // read the blocks list hashes from the volume data (the handled file) and insert them into the temp blocklisthash table
                                await foreach (var blocklisthash in restoredb.GetBlockLists(volumeid, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                                {
                                    if (await restoredb.AddTempBlockListHash(blocklisthash, rd.ReadBlocklist(blocklisthash, hashsize), m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                                    {
                                        anyChange = true;
                                    }
                                }

                                // Update tables if necessary (if no block or hash have been changed by a data volume
                                // there is no need to run expensive queries - most data volumes have been
                                // managed successfully by correct index volumes), so we know if we are done
                                // if any change, add to the block and blocksetentry tables the references found in
                                // the block lists of the volume saved in the temp blocklisthash table by AddTempBLockListHash
                                if (anyChange)
                                {
                                    if (i == 2)
                                    {
                                        Logging.Log.WriteWarningMessage(LOGTAG, "UpdatingTables", null, "Unexpected changes caused by block {0}", name);
                                    }
                                    if (expRecreateDb)
                                        await restoredb
                                            .AddBlockAndBlockSetEntryFromTemp(hashsize, m_options.Blocksize, false, m_result.TaskControl.ProgressToken)
                                            .ConfigureAwait(false);
                                    else
                                        await restoredb
                                            .FindMissingBlocklistHashes(hashsize, m_options.Blocksize, m_result.TaskControl.ProgressToken)
                                            .ConfigureAwait(false);
                                }

                                using (new Logging.Timer(LOGTAG, "CommitRestoredBlocklist", "CommitRestoredBlocklist"))
                                    await restoredb.Transaction
                                        .CommitAsync(m_result.TaskControl.ProgressToken)
                                        .ConfigureAwait(false);

                                //At this point we can patch files with data from the block volume
                                if (blockprocessor != null)
                                    blockprocessor(name, rd);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "FailedRebuildingWithFile", ex, "Failed to use information from {0} to rebuild database: {1}", name, ex.Message);
                            if (m_options.UnittestMode)
                            {
                                // Implicit rollback
                                await restoredb.Transaction
                                    .RollBackAsync(m_result.TaskControl.ProgressToken)
                                    .ConfigureAwait(false);
                                throw;
                            }
                        }
                    }
                }
            }

            await backendManager.WaitForEmptyAsync(restoredb, m_result.TaskControl.ProgressToken).ConfigureAwait(false);

            if (!m_options.RepairOnlyPaths)
            {
                // All blocks are collected and added into the Block table
                // Find out which blocks are deleted and move them into DeletedBlock,
                // so that compact can calculate the unused space
                await restoredb.CleanupDeletedBlocks(m_result.TaskControl.ProgressToken).ConfigureAwait(false);
            }

            await restoredb.CleanupMissingVolumes(m_result.TaskControl.ProgressToken).ConfigureAwait(false);

            if (!m_options.RepairOnlyPaths && m_options.StoreMetadataContentInDatabase)
            {
                await RestoreMetadataContentAsync(backendManager, restoredb, m_result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);

                // The metadata content restore is executed after the main recreate commits.
                // Ensure the restored Metadataset.Content values are persisted.
                await restoredb.Transaction
                    .CommitAsync(m_result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);
            }

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

                await using (var lbfdb = await LocalListBrokenFilesDatabase.CreateAsync(restoredb, null, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                {
                    var broken = await lbfdb
                        .GetBrokenFilesets(new DateTime(0), null, m_result.TaskControl.ProgressToken)
                        .CountAsync(cancellationToken: m_result.TaskControl.ProgressToken)
                        .ConfigureAwait(false);

                    if (broken != 0)
                        throw new UserInformationException(string.Format("Recreated database has missing blocks and {0} broken filelists. Consider using \"{1}\" and \"{2}\" to purge broken data from the remote store and the database.", broken, "list-broken-files", "purge-broken-files"), "DatabaseIsBrokenConsiderPurge");
                }

                await restoredb
                    .VerifyConsistency(m_options.Blocksize, m_options.BlockhashSize, true, m_result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);

                Logging.Log.WriteInformationMessage(LOGTAG, "RecreateCompleted", "Recreate completed, and consistency checks completed, marking database as complete");

                await restoredb.RepairInProgress(m_result.TaskControl.ProgressToken, false).ConfigureAwait(false);
            }

            m_result.EndTime = DateTime.UtcNow;
        }

        private sealed record MetadataBlockRef(long MetadataId, long BlockIndex, string BlockHash, long BlockSize);

        private sealed class MetadataRecreateState
        {
            public long MetadataId { get; }
            public long MetaLength { get; }
            public int ExpectedBlockCount { get; set; }

            // Index -> block bytes
            public SortedDictionary<long, byte[]> Blocks { get; } = new();

            public MetadataRecreateState(long metadataId, long metaLength)
            {
                MetadataId = metadataId;
                MetaLength = metaLength;
            }
        }

        private async Task RestoreMetadataContentAsync(IBackendManager backendManager, LocalRecreateDatabase restoredb, CancellationToken cancellationToken)
        {
            // Metadata content restoration requires the block structure to be present.
            if (m_options.RepairOnlyPaths)
                return;

            // Gather block references for each metadataset that is missing content.
            var volumeBlocks = new Dictionary<string, List<MetadataBlockRef>>(StringComparer.Ordinal);
            var volumeInfo = new Dictionary<string, RemoteVolume>(StringComparer.Ordinal);
            var metaStates = new Dictionary<long, MetadataRecreateState>();

            await foreach (var info in restoredb.GetMissingMetadataBlocks(cancellationToken).ConfigureAwait(false))
            {
                var metadataId = info.MetadataId;
                var metaLength = info.MetaLength;
                var blockIndex = info.BlockIndex;
                var blockHash = info.BlockHash;
                var blockSize = info.BlockSize;
                var volumeName = info.VolumeName;
                var volumeHash = info.VolumeHash;
                var volumeSize = info.VolumeSize;

                if (string.IsNullOrEmpty(volumeName) || string.IsNullOrEmpty(blockHash) || blockSize <= 0)
                    continue;

                if (!metaStates.TryGetValue(metadataId, out var state))
                    metaStates[metadataId] = state = new MetadataRecreateState(metadataId, metaLength);
                state.ExpectedBlockCount++;

                if (!volumeBlocks.TryGetValue(volumeName, out var list))
                    volumeBlocks[volumeName] = list = new List<MetadataBlockRef>();
                list.Add(new MetadataBlockRef(metadataId, blockIndex, blockHash, blockSize));

                if (!volumeInfo.ContainsKey(volumeName))
                    volumeInfo[volumeName] = new RemoteVolume(volumeName, volumeHash, volumeSize);
            }

            if (volumeInfo.Count == 0)
                return;

            Logging.Log.WriteInformationMessage(LOGTAG, "RestoringMetadataContent", "Restoring metadata content into the database by downloading {0} block volume(s)", volumeInfo.Count);

            // Download all required block volumes and extract metadata blocks.
            var volumesToDownload = volumeInfo.Values.Cast<IRemoteVolume>().ToList();
            await foreach (var (tmpfile, hash, size, name) in backendManager.GetFilesOverlappedAsync(volumesToDownload, cancellationToken).ConfigureAwait(false))
            {
                using (tmpfile)
                {
                    if (!await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                        return;

                    if (!volumeBlocks.TryGetValue(name, out var blocksForVolume) || blocksForVolume.Count == 0)
                        continue;

                    try
                    {
                        using var reader = new BlockVolumeReader(RestoreHandler.GetCompressionModule(name), tmpfile, m_options);

                        foreach (var bref in blocksForVolume)
                        {
                            if (!metaStates.TryGetValue(bref.MetadataId, out var state))
                                continue;

                            // Already captured
                            if (state.Blocks.ContainsKey(bref.BlockIndex))
                                continue;

                            if (bref.BlockSize > int.MaxValue)
                            {
                                Logging.Log.WriteWarningMessage(LOGTAG, "MetadataBlockTooLarge", null, "Skipping metadata block {0} for metadataset {1} because block size {2} exceeds supported range", bref.BlockHash, bref.MetadataId, bref.BlockSize);
                                continue;
                            }

                            var buf = new byte[(int)bref.BlockSize];
                            var read = reader.ReadBlock(bref.BlockHash, buf);
                            if (read != buf.Length)
                            {
                                // Trim buffer to actual read length.
                                Array.Resize(ref buf, read);
                            }

                            state.Blocks[bref.BlockIndex] = buf;

                            // If we have all blocks for this metadataset, assemble and store content now.
                            if (state.Blocks.Count == state.ExpectedBlockCount)
                            {
                                try
                                {
                                    var ms = new MemoryStream(checked((int)Math.Min(state.MetaLength, int.MaxValue)));
                                    foreach (var kvp in state.Blocks.OrderBy(x => x.Key))
                                    {
                                        if (ms.Length >= state.MetaLength)
                                            break;

                                        var remaining = state.MetaLength - ms.Length;
                                        var take = (int)Math.Min(remaining, kvp.Value.LongLength);
                                        ms.Write(kvp.Value, 0, take);
                                    }

                                    if (ms.Length != state.MetaLength)
                                    {
                                        Logging.Log.WriteWarningMessage(LOGTAG, "MetadataContentIncomplete", null, "Failed to restore metadataset content for {0}: expected {1} bytes, got {2} bytes", state.MetadataId, state.MetaLength, ms.Length);
                                    }
                                    else
                                    {
                                        var content = Library.Utility.Utility.GetStringWithoutBOM(ms, false);
                                        await restoredb.SetMetadataContent(state.MetadataId, content, cancellationToken).ConfigureAwait(false);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logging.Log.WriteWarningMessage(LOGTAG, "MetadataContentRestoreFailed", ex, "Failed to restore metadataset content for {0}", state.MetadataId);
                                }
                                finally
                                {
                                    metaStates.Remove(state.MetadataId);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "MetadataVolumeProcessingFailed", ex, "Failed to process block volume {0} for metadata content restore", name);
                        if (ex.IsAbortException())
                            throw;
                    }
                }
            }
        }

        /// <summary>
        /// Recreate a fileset from the remote filelist.
        /// </summary>
        /// <param name="restoredb">The database to restore to.</param>
        /// <param name="compressor">The compression module to use.</param>
        /// <param name="filesetid">The ID of the fileset to recreate.</param>
        /// <param name="options">The options to use for the operation.</param>
        /// <param name="filter">The filter to apply to the files.</param>
        /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
        /// <returns>A task that completes when the fileset has been recreated.</returns>
        public static async Task RecreateFilesetFromRemoteList(LocalRecreateDatabase restoredb, ICompression compressor, long filesetid, Options options, IFilter filter, CancellationToken cancellationToken)
        {
            var blocksize = options.Blocksize;
            var hashes_pr_block = blocksize / options.BlockhashSize;

            // retrieve fileset data from dlist
            var filesetData = VolumeReaderBase.GetFilesetData(compressor, options);

            // update fileset using filesetData
            await restoredb
                .UpdateFullBackupStateInFileset(filesetid, filesetData.IsFullBackup, cancellationToken)
                .ConfigureAwait(false);

            // clear any existing fileset entries
            await restoredb.ClearFilesetEntries(filesetid, cancellationToken).ConfigureAwait(false);

            using (var filelistreader = new FilesetVolumeReader(compressor, options))
                foreach (var fe in filelistreader.Files.Where(x => Library.Utility.FilterExpression.Matches(filter, x.Path)))
                {
                    try
                    {
                        var expectedmetablocks = (fe.Metasize + blocksize - 1) / blocksize;
                        var expectedmetablocklisthashes = (expectedmetablocks + hashes_pr_block - 1) / hashes_pr_block;
                        if (expectedmetablocks <= 1) expectedmetablocklisthashes = 0;

                        var metadataid = long.MinValue;
                        var split = LocalDatabase.SplitIntoPrefixAndName(fe.Path);
                        var prefixid = await restoredb
                            .GetOrCreatePathPrefix(split.Key, cancellationToken)
                            .ConfigureAwait(false);

                        switch (fe.Type)
                        {
                            case FilelistEntryType.Folder:
                                metadataid = await restoredb
                                    .AddMetadataset(fe.Metahash, fe.Metasize, fe.MetaBlocklistHashes, expectedmetablocklisthashes, cancellationToken)
                                    .ConfigureAwait(false);
                                await restoredb
                                    .AddDirectoryEntry(filesetid, prefixid, split.Value, fe.Time, metadataid, cancellationToken)
                                    .ConfigureAwait(false);

                                break;
                            case FilelistEntryType.File:
                                var expectedblocks = (fe.Size + blocksize - 1) / blocksize;
                                var expectedblocklisthashes = (expectedblocks + hashes_pr_block - 1) / hashes_pr_block;
                                if (expectedblocks <= 1) expectedblocklisthashes = 0;

                                var blocksetid = await restoredb
                                    .AddBlockset(fe.Hash, fe.Size, fe.BlocklistHashes, expectedblocklisthashes, cancellationToken)
                                    .ConfigureAwait(false);
                                metadataid = await restoredb
                                    .AddMetadataset(fe.Metahash, fe.Metasize, fe.MetaBlocklistHashes, expectedmetablocklisthashes, cancellationToken)
                                    .ConfigureAwait(false);
                                await restoredb
                                    .AddFileEntry(filesetid, prefixid, split.Value, fe.Time, blocksetid, metadataid, cancellationToken)
                                    .ConfigureAwait(false);

                                if (fe.Size <= blocksize)
                                {
                                    if (!string.IsNullOrWhiteSpace(fe.Blockhash))
                                        await restoredb
                                            .AddSmallBlocksetLink(fe.Hash, fe.Blockhash, fe.Blocksize, cancellationToken)
                                            .ConfigureAwait(false);
                                    else if (options.BlockHashAlgorithm == options.FileHashAlgorithm)
                                        await restoredb
                                            .AddSmallBlocksetLink(fe.Hash, fe.Hash, fe.Size, cancellationToken)
                                            .ConfigureAwait(false);
                                    else if (fe.Size > 0)
                                        Logging.Log.WriteWarningMessage(LOGTAG, "MissingBlockHash", null, "No block hash found for file: {0}", fe.Path);
                                }

                                break;
                            case FilelistEntryType.Symlink:
                                metadataid = await restoredb
                                    .AddMetadataset(fe.Metahash, fe.Metasize, fe.MetaBlocklistHashes, expectedmetablocklisthashes, cancellationToken)
                                    .ConfigureAwait(false);
                                await restoredb
                                    .AddSymlinkEntry(filesetid, prefixid, split.Value, fe.Time, metadataid, cancellationToken)
                                    .ConfigureAwait(false);
                                break;
                            default:
                                Logging.Log.WriteWarningMessage(LOGTAG, "SkippingUnknownFileEntry", null, "Skipping file-entry with unknown type {0}: {1} ", fe.Type, fe.Path);
                                break;
                        }

                        if (fe.Metasize <= blocksize && (fe.Type == FilelistEntryType.Folder || fe.Type == FilelistEntryType.File || fe.Type == FilelistEntryType.Symlink))
                        {
                            if (!string.IsNullOrWhiteSpace(fe.Metablockhash))
                                await restoredb
                                    .AddSmallBlocksetLink(fe.Metahash, fe.Metablockhash, fe.Metasize, cancellationToken)
                                    .ConfigureAwait(false);
                            else if (options.BlockHashAlgorithm == options.FileHashAlgorithm)
                                await restoredb
                                    .AddSmallBlocksetLink(fe.Metahash, fe.Metahash, fe.Metasize, cancellationToken)
                                    .ConfigureAwait(false);
                            else
                                Logging.Log.WriteWarningMessage(LOGTAG, "MissingMetadataBlockHash", null, "No block hash found for file metadata: {0}", fe.Path);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "FileEntryProcessingFailed", ex, "Failed to process file-entry: {0}", fe.Path);
                    }
                }

            await restoredb.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Look in the database for filenames similar to the current filename, but with a different compression and encryption module.
        /// </summary>
        /// <param name="filename">The filename read and written.</param>
        /// <param name="restoredb">The database to query.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>The volume id of the item.</returns>
        public static async Task<(long, string)> ProbeForMatchingFilename(string filename, LocalRecreateDatabase restoredb, CancellationToken cancellationToken)
        {
            var p = VolumeBase.ParseFilename(filename);
            if (p != null)
            {
                foreach (var compmodule in Library.DynamicLoader.CompressionLoader.Keys)
                    foreach (var encmodule in Library.DynamicLoader.EncryptionLoader.Keys.Union([""]))
                    {
                        var testfilename = VolumeBase.GenerateFilename(p.FileType, p.Prefix, p.Guid, p.Time, compmodule, encmodule);
                        var tvid = await restoredb
                            .GetRemoteVolumeID(testfilename, cancellationToken)
                            .ConfigureAwait(false);

                        if (tvid >= 0)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "RewritingFilenameMapping", null, "Unable to find volume {0}, but mapping to matching file {1}", filename, testfilename);
                            filename = testfilename;
                            return (tvid, filename);
                        }
                    }
            }

            return (-1, filename);
        }

        public void Dispose()
        {
        }
    }
}
