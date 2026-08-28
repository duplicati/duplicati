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
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation
{
    internal class PurgeBrokenFilesHandler
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(PurgeBrokenFilesHandler));
        protected readonly Options m_options;
        protected readonly PurgeBrokenFilesResults m_result;

        public PurgeBrokenFilesHandler(Options options, PurgeBrokenFilesResults result)
        {
            m_options = options;
            m_result = result;
        }

        public async Task RunAsync(IBackendManager backendManager, Library.Utility.IFilter filter)
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath), "DatabaseDoesNotExist");

            if (filter != null && !filter.Empty)
                throw new UserInformationException("Filters are not supported for this operation", "FiltersNotAllowedOnPurgeBrokenFiles");

            await using var db = await LocalListBrokenFilesDatabase.CreateAsync(m_options.Dbpath, null, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
            if (await db.PartiallyRecreatedAsync(m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                throw new UserInformationException("The command does not work on partially recreated databases", "CannotPurgeOnPartialDatabase");

            await Utility.UpdateOptionsFromDbAsync(db, m_options, m_result.TaskControl.ProgressToken)
                .ConfigureAwait(false);
            await Utility.VerifyOptionsAndUpdateDatabaseAsync(db, m_options, m_result.TaskControl.ProgressToken)
                .ConfigureAwait(false);

            var (sets, missing) = await ListBrokenFilesHandler.GetBrokenFilesetsFromRemoteAsync(backendManager, m_result, db, m_options).ConfigureAwait(false);
            if (sets == null)
                return;

            if (sets.Length == 0)
            {
                if (missing == null)
                    Logging.Log.WriteInformationMessage(LOGTAG, "NoBrokenFilesets", "Found no broken filesets");
                else if (missing.Count == 0)
                    Logging.Log.WriteInformationMessage(LOGTAG, "NoBrokenFilesetsOrMissingFiles", "Found no broken filesets and no missing remote files");
                else
                    Logging.Log.WriteInformationMessage(LOGTAG, "NoBrokenSetsButMissingRemoteFiles", string.Format("Found no broken filesets, but {0} missing remote files. Purging from database.", missing.Count));
            }
            else
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "FoundBrokenFilesets", "Found {0} broken filesets with {1} affected files, purging files", sets.Length, sets.Sum(x => x.RemoveCount));

                var pgoffset = 0.0f;
                var pgspan = 0.95f / sets.Length;

                var filesets = await db
                    .FilesetTimesAsync(m_result.TaskControl.ProgressToken)
                    .ToListAsync(cancellationToken: m_result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);

                var replaceMetadata = !m_options.DisableReplaceMissingMetadata;
                var replacementMetadataBlocksetId = -1L;
                // The number of files per fileset that the replacement recovers, i.e. that were only broken
                // because their metadata could not be restored.
                var recoveredFileCounts = new Dictionary<long, long>();

                if (replaceMetadata)
                {
                    // Metadata rows are shared between filesets, but only the selected filesets get a new
                    // filelist. Replacing the shared rows while some filelist keeps referencing the old
                    // metadata means a later database recreate reintroduces the damage, so refuse to do it.
                    // Without a version filter every fileset with replaceable metadata is broken, and is
                    // therefore part of the selection, so this can only trigger for a scoped purge.
                    var outsideSelection = (await db.GetFilesetsWithReplaceableMetadataAsync(m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                        .Except(sets.Select(x => x.FilesetID))
                        .ToArray();

                    if (outsideSelection.Length > 0)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "ReplaceableMetadataOutsideSelection", null, "Not replacing metadata that cannot be restored, because {0} fileset(s) outside the selected versions reference the same metadata entries, and only the selected filelists are rewritten. The affected files are removed from the selected version(s) instead. Run {1} without {2} and {3} to recover them by replacing the metadata.", outsideSelection.Length, "purge-broken-files", "--version", "--time");
                        replaceMetadata = false;
                    }
                }

                var emptymetadata = Utility.WrapMetadata(new Dictionary<string, string>(), m_options);
                // The volumes that are missing cannot supply blocks, so a blockset that depends on them
                // is no more restorable than the metadata we are replacing.
                var unavailableVolumeIds = (missing ?? []).Select(x => x.ID).ToArray();

                if (replaceMetadata)
                {
                    // Only ask whether a replacement can be obtained here. Obtaining it may mean uploading a
                    // new block volume, and that must not happen until it is known that something needs
                    // replacing, nor during a dry-run.
                    if (!await CanProvideReplacementMetadataAsync(db, emptymetadata, unavailableVolumeIds).ConfigureAwait(false))
                        throw new UserInformationException($"Failed to locate an empty metadata blockset to replace missing metadata. Set the option --disable-replace-missing-metadata=true to ignore this and drop files with missing metadata.", "FailedToLocateEmptyMetadataBlockset");
                }

                // Project the per-fileset broken-file counts for the state after the replacement. Without
                // this, a fileset whose files are only metadata-broken counts as fully broken and gets
                // deleted, instead of being rewritten with the replacement metadata.
                var brokenCounts = sets.ToDictionary(x => x.FilesetID, x => x.RemoveCount);
                if (replaceMetadata)
                {
                    var projected = new Dictionary<long, long>();
                    await foreach (var p in db.GetBrokenFilesetsAsync(m_options.Time, m_options.Version, ignoreReplaceableMetadata: true, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                        projected[p.FilesetID] = p.RemoveFileCount;

                    foreach (var set in sets)
                    {
                        var remaining = projected.TryGetValue(set.FilesetID, out var r) ? r : 0;
                        recoveredFileCounts[set.FilesetID] = set.RemoveCount - remaining;
                        brokenCounts[set.FilesetID] = remaining;
                    }
                }

                var compare_list = sets.Select(async x => new
                {
                    FilesetID = x.FilesetID,
                    Timestamp = x.FilesetTime,
                    RemoveCount = brokenCounts[x.FilesetID],
                    RecoverCount = recoveredFileCounts.TryGetValue(x.FilesetID, out var recovered) ? recovered : 0,
                    Version = filesets.FindIndex(y => y.Key == x.FilesetID),
                    SetCount = await db
                        .GetFilesetFileCountAsync(x.FilesetID, m_result.TaskControl.ProgressToken)
                        .ConfigureAwait(false)
                })
                    .Select(x => x.Result)
                    .ToArray();

                var fully_emptied = compare_list.Where(x => x.RemoveCount == x.SetCount).ToArray();
                var to_purge = compare_list.Where(x => x.RemoveCount != x.SetCount).ToArray();

                // Only a fileset that gets a new filelist can carry the replacement to the destination, and a
                // fileset that is removed entirely does not need one. If none of the filesets being rewritten
                // recovers anything, there is nothing to replace, and nothing to store.
                var filesToRecover = to_purge.Sum(x => x.RecoverCount);
                if (filesToRecover <= 0)
                    replaceMetadata = false;
                else
                    Logging.Log.WriteWarningMessage(LOGTAG, "MetadataReplacedWithEmpty", null, "Recovering {0} file(s) by replacing metadata that cannot be restored. The affected files keep their contents, but lose their original permissions and timestamps. Use {1} to remove them instead.", filesToRecover, "--disable-replace-missing-metadata");

                if (fully_emptied.Length == await db.FilesetTimesAsync(m_result.TaskControl.ProgressToken).CountAsync(cancellationToken: m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                    throw new UserInformationException("All filesets are fully broken and needs to be removed. To avoid unexpected deletions, you must manually remove the remote files and delete the database.", "AllFilesetsBroken");

                if (!m_options.Dryrun)
                    await db.Transaction.CommitAsync(m_result.TaskControl.ProgressToken).ConfigureAwait(false);


                if (fully_emptied.Length != 0)
                {
                    if (fully_emptied.Length == 1)
                        Logging.Log.WriteInformationMessage(LOGTAG, "RemovingFilesets", "Removing entire fileset {1} as all {0} file(s) are broken", fully_emptied.First().Timestamp, fully_emptied.First().RemoveCount);
                    else
                        Logging.Log.WriteInformationMessage(LOGTAG, "RemovingFilesets", "Removing {0} filesets where all file(s) are broken: {1}", fully_emptied.Length, string.Join(", ", fully_emptied.Select(x => x.Timestamp.ToLocalTime().ToString())));

                    m_result.DeleteResults = new DeleteResults(m_result);
                    await using (var rmdb = await LocalDeleteDatabase.CreateAsync(db, null, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                    {
                        var opts = new Options(new Dictionary<string, string?>(m_options.RawOptions));
                        opts.RawOptions["version"] = string.Join(",", fully_emptied.Select(x => x.Version.ToString()));
                        opts.RawOptions.Remove("time");
                        opts.RawOptions["no-auto-compact"] = "true";

                        await new DeleteHandler(opts, (DeleteResults)m_result.DeleteResults)
                            .DoRunAsync(rmdb, true, false, backendManager).ConfigureAwait(false);

                        if (!m_options.Dryrun)
                            await rmdb.Transaction
                                .CommitAsync("CommitDelete", true, m_result.TaskControl.ProgressToken)
                                .ConfigureAwait(false);
                    }

                    pgoffset += (pgspan * fully_emptied.Length);
                    m_result.OperationProgressUpdater.UpdateProgress(pgoffset);
                }

                if (replaceMetadata)
                {
                    // Obtain the replacement only now: the delete above removes filesets, and everything that
                    // is no longer referenced with them, which would take a freshly created blockset with it.
                    replacementMetadataBlocksetId = await EnsureReplacementMetadataBlocksetAsync(backendManager, db, emptymetadata, unavailableVolumeIds)
                        .ConfigureAwait(false);

                    // The probe above said a replacement could be obtained, so this means an upload failed
                    // and there was nothing left to fall back to. Stop rather than fall back to removing the
                    // files, which would silently do something the projected counts no longer describe.
                    if (replacementMetadataBlocksetId < 0)
                        throw new UserInformationException($"Failed to obtain replacement metadata for the files that cannot be restored. Set the option --disable-replace-missing-metadata=true to remove those files instead.", "FailedToLocateEmptyMetadataBlockset");
                }

                if (to_purge.Length > 0)
                {
                    m_result.PurgeResults = new PurgeFilesResults(m_result);

                    foreach (var bs in to_purge)
                    {
                        Logging.Log.WriteInformationMessage(LOGTAG, "PurgingFiles", "Purging {0} file(s) from fileset {1}, recovering {2} file(s) with replacement metadata", bs.RemoveCount, bs.Timestamp.ToLocalTime(), bs.RecoverCount);
                        var opts = new Options(new Dictionary<string, string?>(m_options.RawOptions));

                        await using (var pgdb = await LocalPurgeDatabase.CreateAsync(db, null, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                        {
                            // Recompute the version number after we deleted the versions before
                            filesets = await pgdb
                                .FilesetTimesAsync(m_result.TaskControl.ProgressToken)
                                .ToListAsync(cancellationToken: m_result.TaskControl.ProgressToken)
                                .ConfigureAwait(false);
                            var thisversion = filesets.FindIndex(y => y.Key == bs.FilesetID);
                            if (thisversion < 0)
                                throw new Exception(string.Format("Failed to find match for {0} ({1}) in {2}", bs.FilesetID, bs.Timestamp.ToLocalTime(), string.Join(", ", filesets.Select(x => x.ToString()))));

                            opts.RawOptions["version"] = thisversion.ToString();
                            opts.RawOptions.Remove("time");
                            opts.RawOptions["no-auto-compact"] = "true";

                            await new PurgeFilesHandler(opts, (PurgeFilesResults)m_result.PurgeResults).RunAsync(backendManager, pgdb, pgoffset, pgspan, async (cmd, filesetid, tablename) =>
                            {
                                if (filesetid != bs.FilesetID)
                                    throw new Exception(string.Format("Unexpected filesetid: {0}, expected {1}", filesetid, bs.FilesetID));

                                // Update entries that would be removed because of missing metadata
                                var updatedEntries = 0;
                                if (replaceMetadata)
                                {
                                    await db.ReplaceMetadataAsync(filesetid, replacementMetadataBlocksetId, m_result.TaskControl.ProgressToken);

                                    // Report the projected recovery, not the number of rows the update
                                    // touched: metadata rows are shared between filesets, so the first
                                    // fileset repairs rows the later ones also use, and they would all
                                    // report zero. A non-zero count here is what makes PurgeFilesHandler
                                    // write a new filelist even when nothing is removed, which is what
                                    // persists the replacement at the destination.
                                    updatedEntries = (int)bs.RecoverCount;
                                }

                                await db.InsertBrokenFileIDsIntoTableAsync(filesetid, tablename, "FileID", ignoreReplaceableMetadata: replaceMetadata, m_result.TaskControl.ProgressToken);
                                return updatedEntries;
                            }).ConfigureAwait(false);
                        }

                        pgoffset += pgspan;
                        m_result.OperationProgressUpdater.UpdateProgress(pgoffset);
                    }
                }
            }

            m_result.OperationProgressUpdater.UpdateProgress(0.95f);

            if (!m_options.Dryrun && await db.RepairInProgressAsync(m_result.TaskControl.ProgressToken).ConfigureAwait(false))
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "ValidatingDatabase", "Database was previously marked as in-progress, checking if it is valid after purging files");
                await db
                    .VerifyConsistencyAsync(m_options.Blocksize, m_options.BlockhashSize, true, m_result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);
                Logging.Log.WriteInformationMessage(LOGTAG, "UpdatingDatabase", "Purge completed, and consistency checks completed, marking database as complete");
                await db.RepairInProgressAsync(m_result.TaskControl.ProgressToken, false).ConfigureAwait(false);
            }
            else
            {
                await db.Transaction.RollBackAsync(m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                await db
                    .VerifyConsistencyAsync(m_options.Blocksize, m_options.BlockhashSize, true, m_result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);
            }

            m_result.OperationProgressUpdater.UpdateProgress(1.0f);
        }

        /// <summary>
        /// Computes the block hash of the given data.
        /// </summary>
        /// <remarks>
        /// A blockset records the <em>file</em> hash of its contents in <c>Blockset.FullHash</c>, while the
        /// blocks it is made of are identified by their <em>block</em> hash. For a blob that fits in a single
        /// block these are two different hashes of the same bytes.
        /// </remarks>
        /// <param name="data">The data to hash.</param>
        /// <returns>The base64 encoded block hash.</returns>
        private string CalculateBlockHash(byte[] data)
        {
            using var blockhasher = Library.Utility.HashFactory.CreateHasher(m_options.BlockHashAlgorithm);
            if (blockhasher == null)
                throw new UserInformationException(Strings.Common.InvalidHashAlgorithm(m_options.BlockHashAlgorithm), "BlockHashAlgorithmNotSupported");

            return Convert.ToBase64String(blockhasher.ComputeHash(data));
        }

        /// <summary>
        /// Answers, without changing anything, whether replacement metadata can be obtained.
        /// </summary>
        /// <remarks>
        /// This must follow the same chain as <see cref="EnsureReplacementMetadataBlocksetAsync"/>, including
        /// the dry-run rule, or a dry-run projects a recovery that the real run would not perform, and the
        /// other way around.
        /// </remarks>
        /// <param name="db">The database to query.</param>
        /// <param name="emptymetadata">The canonical empty metadata blob.</param>
        /// <param name="unavailableVolumeIds">The ids of the volumes that cannot supply blocks.</param>
        /// <returns>A task that when awaited is <c>true</c> if a replacement can be obtained.</returns>
        private async Task<bool> CanProvideReplacementMetadataAsync(LocalListBrokenFilesDatabase db, IMetahash emptymetadata, long[] unavailableVolumeIds)
        {
            var token = m_result.TaskControl.ProgressToken;

            if (await db.FindExactMetadataBlocksetIdAsync(unavailableVolumeIds, emptymetadata.FileHash, emptymetadata.Blob.Length, token).ConfigureAwait(false) >= 0)
                return true;

            if (await db.FindAvailableBlockIdAsync(CalculateBlockHash(emptymetadata.Blob), emptymetadata.Blob.Length, unavailableVolumeIds, token).ConfigureAwait(false) >= 0)
                return true;

            // We can store it ourselves, unless it would not fit in a single block, or this is a dry-run
            if (!m_options.Dryrun && emptymetadata.Blob.Length <= m_options.Blocksize)
                return true;

            return await db.FindSmallestUsableMetadataBlocksetIdAsync(unavailableVolumeIds, token).ConfigureAwait(false) >= 0;
        }

        /// <summary>
        /// Returns the id of a blockset holding the canonical empty metadata blob, storing it at the
        /// destination if it is not there yet, and falling back to another file's metadata if it cannot be
        /// stored.
        /// </summary>
        /// <remarks>
        /// Storing the blob matters because the replacement is recorded in the filelists at the destination.
        /// If nothing at the destination backs the blockset the filelists point at, a database recreate
        /// reintroduces exactly the damage this is repairing.
        /// </remarks>
        /// <param name="backendManager">The backend manager to upload with.</param>
        /// <param name="db">The database to record the blockset in.</param>
        /// <param name="emptymetadata">The canonical empty metadata blob.</param>
        /// <param name="unavailableVolumeIds">The ids of the volumes that cannot supply blocks.</param>
        /// <returns>A task that when awaited contains the id of the blockset, or -1 if none could be obtained.</returns>
        private async Task<long> EnsureReplacementMetadataBlocksetAsync(IBackendManager backendManager, LocalListBrokenFilesDatabase db, IMetahash emptymetadata, long[] unavailableVolumeIds)
        {
            var token = m_result.TaskControl.ProgressToken;

            // 1. The blob is already stored, and a blockset already describes it
            var blocksetId = await db
                .FindExactMetadataBlocksetIdAsync(unavailableVolumeIds, emptymetadata.FileHash, emptymetadata.Blob.Length, token)
                .ConfigureAwait(false);
            if (blocksetId >= 0)
                return blocksetId;

            // 2. The block is stored, but no usable blockset points at it
            var blockHash = CalculateBlockHash(emptymetadata.Blob);
            var blockId = await db
                .FindAvailableBlockIdAsync(blockHash, emptymetadata.Blob.Length, unavailableVolumeIds, token)
                .ConfigureAwait(false);
            if (blockId >= 0)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "ReusingStoredReplacementMetadata", "The empty metadata block is already stored, registering it as replacement metadata");
                blocksetId = await db
                    .CreateMetadataBlocksetAsync(emptymetadata.FileHash, emptymetadata.Blob.Length, [blockId], token)
                    .ConfigureAwait(false);
                await db.GetOrCreateMetadatasetIdAsync(blocksetId, token).ConfigureAwait(false);
                if (!m_options.Dryrun)
                    await db.Transaction.CommitAsync("RegisteredReplacementMetadata", true, token).ConfigureAwait(false);

                return blocksetId;
            }

            // 3. Store it, so the replacement survives a database recreate
            if (emptymetadata.Blob.Length > m_options.Blocksize)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "ReplacementMetadataTooLarge", null, "The empty metadata entry is {0} bytes, which does not fit in a single block of {1} bytes, so it cannot be stored as replacement metadata.", emptymetadata.Blob.Length, m_options.Blocksize);
            }
            else if (m_options.Dryrun)
            {
                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldUploadReplacementMetadata", "Would upload a new block volume with the empty metadata entry to use as replacement metadata");
            }
            else
            {
                try
                {
                    return await UploadReplacementMetadataAsync(backendManager, db, emptymetadata, blockHash)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (ex.IsAbortOrCancelException())
                        throw;

                    Logging.Log.WriteWarningMessage(LOGTAG, "ReplacementMetadataUploadFailed", ex, "Failed to store the empty metadata entry: {0}", ex.Message);
                }
            }

            // 4. Fall back to another file's metadata, which is restorable but describes the wrong file
            blocksetId = await db
                .FindSmallestUsableMetadataBlocksetIdAsync(unavailableVolumeIds, token)
                .ConfigureAwait(false);

            if (blocksetId >= 0)
                Logging.Log.WriteInformationMessage(LOGTAG, "ReplacementMetadataIsNotEmpty", "The empty metadata entry is not stored in the backup, using the smallest available metadata as replacement. The affected files will lose their original permissions and timestamps.");

            return blocksetId;
        }

        /// <summary>
        /// Uploads a new block volume holding the empty metadata blob, and records the resulting blockset.
        /// </summary>
        /// <param name="backendManager">The backend manager to upload with.</param>
        /// <param name="db">The database to record the volume, block and blockset in.</param>
        /// <param name="emptymetadata">The canonical empty metadata blob.</param>
        /// <param name="blockHash">The block hash of the blob.</param>
        /// <returns>A task that when awaited contains the id of the blockset.</returns>
        private async Task<long> UploadReplacementMetadataAsync(IBackendManager backendManager, LocalListBrokenFilesDatabase db, IMetahash emptymetadata, string blockHash)
        {
            var token = m_result.TaskControl.ProgressToken;
            Volumes.BlockVolumeWriter? blockVolume = null;
            Volumes.IndexVolumeWriter? indexVolume = null;
            LocalListBrokenFilesDatabase.BlockRegistration? registration = null;

            // Record that an upload is in progress before anything is registered. If the process is killed
            // between the commit below and the end of the upload, the volume is left in Uploading state with
            // the block pointing at it, and this flag is what lets the next run clean that up: without it
            // the next operation verifies in strict mode and refuses to continue instead.
            await db.TerminatedWithActiveUploadsAsync(token, true).ConfigureAwait(false);

            try
            {
                blockVolume = new Volumes.BlockVolumeWriter(m_options);
                blockVolume.VolumeID = await db
                    .RegisterRemoteVolumeAsync(blockVolume.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, token)
                    .ConfigureAwait(false);

                // This has to be recorded before the upload starts, and is therefore visible before the
                // volume exists at the destination. It is undone below if the volume never arrives.
                registration = await db
                    .RegisterBlockAsync(blockHash, emptymetadata.Blob.Length, blockVolume.VolumeID, token)
                    .ConfigureAwait(false);

                await blockVolume
                    .AddBlockAsync(blockHash, emptymetadata.Blob, 0, emptymetadata.Blob.Length, CompressionHint.Compressible)
                    .ConfigureAwait(false);

                await db
                    .UpdateRemoteVolumeAsync(blockVolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, token)
                    .ConfigureAwait(false);

                if (m_options.IndexfilePolicy != Options.IndexFileStrategy.None)
                {
                    indexVolume = new Volumes.IndexVolumeWriter(m_options);
                    indexVolume.VolumeID = await db
                        .RegisterRemoteVolumeAsync(indexVolume.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary, token)
                        .ConfigureAwait(false);
                    indexVolume.StartVolume(blockVolume.RemoteFilename);
                    indexVolume.AddBlock(blockHash, emptymetadata.Blob.Length);
                    await db.AddIndexBlockLinkAsync(indexVolume.VolumeID, blockVolume.VolumeID, token)
                        .ConfigureAwait(false);

                    // No blocklists to write, even for IndexFileStrategy.Full: the blockset is a single
                    // block, so it has no blocklist hashes.
                }

                await backendManager.FlushPendingMessagesAsync(db, token).ConfigureAwait(false);
                await db.Transaction.CommitAsync("PreUploadReplacementMetadata", true, token).ConfigureAwait(false);

                // waitForComplete, so the volume is known to be at the destination before anything is pointed
                // at it, and so the temp files are no longer needed once this returns or throws
                await backendManager.PutAsync(blockVolume, indexVolume, null, true, null, token).ConfigureAwait(false);
                await backendManager.WaitForEmptyAsync(db, token).ConfigureAwait(false);

                var blocksetId = await db
                    .CreateMetadataBlocksetAsync(emptymetadata.FileHash, emptymetadata.Blob.Length, [registration.BlockId], token)
                    .ConfigureAwait(false);
                await db.GetOrCreateMetadatasetIdAsync(blocksetId, token).ConfigureAwait(false);
                await db.Transaction.CommitAsync("StoredReplacementMetadata", true, token).ConfigureAwait(false);

                // Nothing is in flight any more. The flag is deliberately left set when this throws, so the
                // next run cleans up whatever the failure left behind.
                await db.TerminatedWithActiveUploadsAsync(token, false).ConfigureAwait(false);

                Logging.Log.WriteInformationMessage(LOGTAG, "StoredReplacementMetadata", "Stored the empty metadata entry in {0} to use as replacement metadata", blockVolume.RemoteFilename);
                return blocksetId;
            }
            catch
            {
                if (registration != null)
                    await db.RollbackBlockRegistrationAsync(registration, token).ConfigureAwait(false);

                foreach (var name in new[] { blockVolume?.RemoteFilename, indexVolume?.RemoteFilename })
                    if (!string.IsNullOrWhiteSpace(name))
                        try
                        {
                            await db.RemoveRemoteVolumeAsync(name, token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "ReplacementMetadataCleanupFailed", ex, "Failed to remove the registration of {0}: {1}. If the file reached the destination, run repair to clean it up.", name, ex.Message);
                        }

                await db.Transaction.CommitAsync("RolledBackReplacementMetadata", true, token).ConfigureAwait(false);
                throw;
            }
            finally
            {
                // With waitForComplete the backend is done with the temp files by the time PutAsync returns
                // or throws, so disposing here neither leaks a temp file when an exception is raised before
                // ownership transfers, nor deletes a file the backend still needs.
                DisposeVolumeWriter(indexVolume);
                DisposeVolumeWriter(blockVolume);
            }
        }

        /// <summary>
        /// Disposes a volume writer without letting the disposal hide whatever went wrong before it.
        /// </summary>
        /// <param name="writer">The writer to dispose, if any.</param>
        private static void DisposeVolumeWriter(Volumes.VolumeWriterBase? writer)
        {
            if (writer == null)
                return;

            // An index volume refuses to be disposed while a volume is still being written, which is the
            // state it is left in when something fails between StartVolume and the upload
            if (writer is Volumes.IndexVolumeWriter indexVolume)
                try { indexVolume.FinishVolume(null, 0); }
                catch (Exception ex) { Logging.Log.WriteVerboseMessage(LOGTAG, "FinishVolumeFailed", ex, "Failed to finish {0}: {1}", writer.RemoteFilename, ex.Message); }

            try { writer.Dispose(); }
            catch (Exception ex) { Logging.Log.WriteVerboseMessage(LOGTAG, "DisposeVolumeFailed", ex, "Failed to dispose a volume writer: {0}", ex.Message); }
        }
    }
}
