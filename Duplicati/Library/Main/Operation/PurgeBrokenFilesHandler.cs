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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.Main.Volumes;
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

                var compare_list = sets.Select(async x => new
                {
                    FilesetID = x.FilesetID,
                    Timestamp = x.FilesetTime,
                    RemoveCount = x.RemoveCount,
                    Version = filesets.FindIndex(y => y.Key == x.FilesetID),
                    SetCount = await db
                        .GetFilesetFileCountAsync(x.FilesetID, m_result.TaskControl.ProgressToken)
                        .ConfigureAwait(false)
                })
                    .Select(x => x.Result)
                    .ToArray();

                var replacementMetadataBlocksetId = -1L;
                if (!m_options.DisableReplaceMissingMetadata)
                {
                    var emptymetadata = Utility.WrapMetadata(new Dictionary<string, string>(), m_options);
                    replacementMetadataBlocksetId = await db
                        .FindEmptyMetadataBlocksetIdAsync(
                            (missing ?? []).Select(x => x.ID),
                            emptymetadata.FileHash,
                            emptymetadata.Blob.Length,
                            m_result.TaskControl.ProgressToken
                        )
                        .ConfigureAwait(false);

                    // An ordinary backup holds no empty metadata, so there is usually nothing to
                    // find. Rather than dressing the files in whatever other metadata happens to be
                    // smallest, the empty metadata is written now: it is a constant, so it can
                    // always be produced.
                    if (replacementMetadataBlocksetId < 0)
                        replacementMetadataBlocksetId = await StoreEmptyMetadataAsync(backendManager, db, emptymetadata, m_result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);
                }

                var fully_emptied = compare_list.Where(x => x.RemoveCount == x.SetCount).ToArray();
                var to_purge = compare_list.Where(x => x.RemoveCount != x.SetCount).ToArray();

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

                if (to_purge.Length > 0)
                {
                    m_result.PurgeResults = new PurgeFilesResults(m_result);

                    foreach (var bs in to_purge)
                    {
                        Logging.Log.WriteInformationMessage(LOGTAG, "PurgingFiles", "Purging {0} file(s) from fileset {1}", bs.RemoveCount, bs.Timestamp.ToLocalTime());
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
                                if (!m_options.DisableReplaceMissingMetadata)
                                    updatedEntries = await db.ReplaceMetadataAsync(filesetid, replacementMetadataBlocksetId, m_result.TaskControl.ProgressToken);

                                await db.InsertBrokenFileIDsIntoTableAsync(filesetid, tablename, "FileID", m_result.TaskControl.ProgressToken);
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
        /// Writes the empty metadata to the destination as a volume of its own and records it, so
        /// a file that lost its metadata can be given metadata that says nothing rather than
        /// metadata that belongs to something else.
        /// </summary>
        /// <remarks>
        /// This deliberately does not commit or wait for the backend. It runs inside the
        /// transaction the purge is already holding, and the rows it writes are committed with the
        /// rest of the purge. Committing here instead leaves the later ReplaceMetadata matching
        /// nothing, which is how the substitution went unnoticed while it was being replaced.
        /// </remarks>
        /// <param name="backendManager">The backend manager to upload with.</param>
        /// <param name="db">The database to record the new blockset in.</param>
        /// <param name="emptymetadata">The empty metadata to store.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>A task that when awaited contains the ID of the new blockset.</returns>
        private async Task<long> StoreEmptyMetadataAsync(IBackendManager backendManager, LocalListBrokenFilesDatabase db, IMetahash emptymetadata, CancellationToken cancellationToken)
        {
            string blockHash;
            using (var blockhasher = HashFactory.CreateHasher(m_options.BlockHashAlgorithm))
                blockHash = Convert.ToBase64String(blockhasher.ComputeHash(emptymetadata.Blob));

            var blockVolume = new BlockVolumeWriter(m_options);
            blockVolume.VolumeID = await db
                .RegisterRemoteVolumeAsync(blockVolume.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, cancellationToken)
                .ConfigureAwait(false);
            await blockVolume
                .AddBlockAsync(blockHash, emptymetadata.Blob, 0, emptymetadata.Blob.Length, CompressionHint.Default)
                .ConfigureAwait(false);
            blockVolume.Close();

            var blocksetId = await db
                .AddEmptyMetadataBlocksetAsync(blockVolume.VolumeID, blockHash, emptymetadata.FileHash, emptymetadata.Blob.Length, cancellationToken)
                .ConfigureAwait(false);

            IndexVolumeWriter? indexVolume = null;
            if (m_options.IndexfilePolicy != Options.IndexFileStrategy.None)
            {
                indexVolume = new IndexVolumeWriter(m_options);
                indexVolume.VolumeID = await db
                    .RegisterRemoteVolumeAsync(indexVolume.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary, cancellationToken)
                    .ConfigureAwait(false);
                indexVolume.StartVolume(blockVolume.RemoteFilename);
                indexVolume.AddBlock(blockHash, emptymetadata.Blob.Length);
                await db
                    .AddIndexBlockLinkAsync(indexVolume.VolumeID, blockVolume.VolumeID, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (m_options.Dryrun)
            {
                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldStoreEmptyMetadata", "would upload {0}, holding the empty metadata that replaces what is missing", blockVolume.RemoteFilename);
                return blocksetId;
            }

            await db
                .UpdateRemoteVolumeAsync(blockVolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, cancellationToken)
                .ConfigureAwait(false);
            await backendManager.PutAsync(blockVolume, indexVolume, null, false, null, cancellationToken)
                .ConfigureAwait(false);

            Logging.Log.WriteInformationMessage(LOGTAG, "StoredEmptyMetadata", "Stored the empty metadata in {0}, to replace metadata that is missing", blockVolume.RemoteFilename);
            return blocksetId;
        }
    }
}
