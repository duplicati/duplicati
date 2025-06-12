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

using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation
{
    internal class CompactHandler
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<CompactHandler>();
        protected readonly Options m_options;
        protected readonly CompactResults m_result;

        public CompactHandler(Options options, CompactResults result)
        {
            m_options = options;
            m_result = result;
        }

        public async Task RunAsync(IBackendManager backendManager)
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new Exception(string.Format("Database file does not exist: {0}", m_options.Dbpath));

            using (var db = await LocalDeleteDatabase.CreateAsync(m_options.Dbpath, "Compact", m_options.SqlitePageCache).ConfigureAwait(false))
            {
                await Utility.UpdateOptionsFromDb(db, m_options)
                    .ConfigureAwait(false);
                await Utility.VerifyOptionsAndUpdateDatabase(db, m_options)
                    .ConfigureAwait(false);

                var changed = await DoCompactAsync(db, false, backendManager).ConfigureAwait(false);

                if (changed && m_options.UploadVerificationFile)
                    await FilelistProcessor.UploadVerificationFile(backendManager, m_options, db);

                if (!m_options.Dryrun)
                {
                    await db.Transaction
                        .CommitAsync("CommitCompact")
                        .ConfigureAwait(false);

                    if (changed)
                    {
                        await db.WriteResults(m_result).ConfigureAwait(false);
                        if (m_options.AutoVacuum)
                        {
                            m_result.VacuumResults = new VacuumResults(m_result);
                            await new VacuumHandler(m_options, (VacuumResults)m_result.VacuumResults).RunAsync().ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        internal async Task<bool> DoCompactAsync(LocalDeleteDatabase db, bool hasVerifiedBackend, IBackendManager backendManager)
        {
            var report = await db
                .GetCompactReport(m_options.VolumeSize, m_options.Threshold, m_options.SmallFileSize, m_options.SmallFileMaxCount)
                .ConfigureAwait(false);

            report.ReportCompactData();

            if (report.ShouldReclaim || report.ShouldCompact)
            {
                // Workaround where we allow a running backendmanager to be used
                if (!hasVerifiedBackend)
                    await FilelistProcessor.VerifyRemoteList(backendManager, m_options, db, m_result.BackendWriter, true, FilelistProcessor.VerifyMode.VerifyStrict).ConfigureAwait(false);

                var newvol = new BlockVolumeWriter(m_options);
                newvol.VolumeID = await db
                    .RegisterRemoteVolume(newvol.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary)
                    .ConfigureAwait(false);

                IndexVolumeWriter newvolindex = null;
                if (m_options.IndexfilePolicy != Options.IndexFileStrategy.None)
                {
                    newvolindex = new IndexVolumeWriter(m_options);
                    newvolindex.VolumeID = await db
                        .RegisterRemoteVolume(newvolindex.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary)
                        .ConfigureAwait(false);

                    await db
                        .AddIndexBlockLink(newvolindex.VolumeID, newvol.VolumeID)
                        .ConfigureAwait(false);
                }

                var blocksInVolume = 0L;
                var buffer = new byte[m_options.Blocksize];
                var remoteList = await db
                    .GetRemoteVolumes()
                    .Where(n => n.State == RemoteVolumeState.Uploaded || n.State == RemoteVolumeState.Verified)
                    .ToArrayAsync()
                    .ConfigureAwait(false);

                //These are for bookkeeping
                var uploadedVolumes = new List<KeyValuePair<string, long>>();
                var deletedVolumes = new List<KeyValuePair<string, long>>();
                var downloadedVolumes = new List<KeyValuePair<string, long>>();

                //We start by deleting unused volumes to save space before uploading new stuff
                List<IRemoteVolume> fullyDeleteable = [];
                if (report.DeleteableVolumes.Any())
                {
                    var deleteableVolumesAsHashSet = new HashSet<string>(report.DeleteableVolumes);
                    fullyDeleteable =
                        remoteList
                            .Where(n => deleteableVolumesAsHashSet.Contains(n.Name))
                            .Cast<IRemoteVolume>()
                            .ToList();
                }
                await foreach (var d in DoDelete(db, backendManager, fullyDeleteable, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                    deletedVolumes.Add(d);

                // This list is used to pick up unused volumes,
                // so they can be deleted once the upload of the
                // required fragments is complete
                var deleteableVolumes = new List<IRemoteVolume>();

                if (report.ShouldCompact)
                {
                    // If we crash now, we may leave partial files
                    if (!m_options.Dryrun)
                        await db
                            .TerminatedWithActiveUploads(true)
                            .ConfigureAwait(false);

                    newvolindex?.StartVolume(newvol.RemoteFilename);
                    List<IRemoteVolume> volumesToDownload = [];
                    if (report.CompactableVolumes.Any())
                    {
                        var compactableVolumesAsHashSet = new HashSet<string>(report.CompactableVolumes);
                        volumesToDownload =
                            remoteList
                                .Where(n => compactableVolumesAsHashSet.Contains(n.Name))
                                .Cast<IRemoteVolume>()
                                .ToList();
                    }

                    using (var q = await db.CreateBlockQueryHelper().ConfigureAwait(false))
                    {
                        await foreach (var (tmpfile, hash, size, name) in backendManager.GetFilesOverlappedAsync(volumesToDownload, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                        {
                            using (tmpfile)
                            {
                                var entry = new RemoteVolume(name, hash, size);
                                if (!await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                                {
                                    await backendManager.WaitForEmptyAsync(db, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                                    return false;
                                }

                                downloadedVolumes.Add(new KeyValuePair<string, long>(entry.Name, entry.Size));
                                var volumeid = await db
                                    .GetRemoteVolumeID(entry.Name)
                                    .ConfigureAwait(false);

                                var inst = VolumeBase.ParseFilename(entry.Name);
                                using (var f = new BlockVolumeReader(inst.CompressionModule, tmpfile, m_options))
                                {
                                    foreach (var e in f.Blocks)
                                    {
                                        if (await q.UseBlock(e.Key, e.Value, volumeid).ConfigureAwait(false))
                                        {
                                            //TODO: How do we get the compression hint? Reverse query for filename in db?
                                            var s = f.ReadBlock(e.Key, buffer);
                                            if (s != e.Value)
                                                throw new Exception(string.Format("Size mismatch problem for block {0}, {1} vs {2}", e.Key, s, e.Value));

                                            await newvol
                                                .AddBlock(e.Key, buffer, 0, s, Interface.CompressionHint.Compressible)
                                                .ConfigureAwait(false);

                                            if (newvolindex != null)
                                                newvolindex.AddBlock(e.Key, e.Value);

                                            await db
                                                .RegisterDuplicatedBlock(e.Key, e.Value, newvol.VolumeID)
                                                .ConfigureAwait(false);

                                            blocksInVolume++;

                                            if (newvol.Filesize > (m_options.VolumeSize - m_options.Blocksize))
                                            {
                                                await FinishVolumeAndUpload(db, backendManager, newvol, newvolindex, uploadedVolumes)
                                                    .ConfigureAwait(false);

                                                newvol = new BlockVolumeWriter(m_options);
                                                newvol.VolumeID = await db
                                                    .RegisterRemoteVolume(newvol.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary)
                                                    .ConfigureAwait(false);

                                                if (m_options.IndexfilePolicy != Options.IndexFileStrategy.None)
                                                {
                                                    newvolindex = new IndexVolumeWriter(m_options);
                                                    newvolindex.VolumeID = await db
                                                        .RegisterRemoteVolume(newvolindex.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary)
                                                        .ConfigureAwait(false);

                                                    await db
                                                        .AddIndexBlockLink(newvolindex.VolumeID, newvol.VolumeID)
                                                        .ConfigureAwait(false);

                                                    newvolindex.StartVolume(newvol.RemoteFilename);
                                                }

                                                blocksInVolume = 0;

                                                // Wait for the backend to catch up
                                                await backendManager.WaitForEmptyAsync(db, m_result.TaskControl.ProgressToken).ConfigureAwait(false);

                                                // Commit as we have uploaded a volume
                                                if (!m_options.Dryrun)
                                                    await db.Transaction
                                                        .CommitAsync("CommitCompact")
                                                        .ConfigureAwait(false);

                                                if (deleteableVolumes.Any())
                                                {
                                                    // Preserve space by deleting the old volume
                                                    await foreach (var d in DoDelete(db, backendManager, deleteableVolumes, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                                                        deletedVolumes.Add(d);
                                                    deleteableVolumes.Clear();
                                                }
                                            }
                                        }
                                    }
                                }

                                deleteableVolumes.Add(entry);
                            }
                        }

                        if (blocksInVolume > 0)
                        {
                            await FinishVolumeAndUpload(db, backendManager, newvol, newvolindex, uploadedVolumes).ConfigureAwait(false);
                        }
                        else
                        {
                            await db
                                .RemoveRemoteVolume(newvol.RemoteFilename)
                                .ConfigureAwait(false);

                            if (newvolindex != null)
                            {
                                await db
                                    .RemoveRemoteVolume(newvolindex.RemoteFilename)
                                    .ConfigureAwait(false);

                                newvolindex.FinishVolume(null, 0);
                            }
                        }
                    }

                    // The remainder of the operation cannot leave partial files
                    if (!m_options.Dryrun)
                        await db
                            .TerminatedWithActiveUploads(false)
                            .ConfigureAwait(false);
                }
                else
                {
                    newvolindex?.Dispose();
                    newvol.Dispose();
                }

                await foreach (var d in DoDelete(db, backendManager, deleteableVolumes, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                    deletedVolumes.Add(d);

                var downloadSize = downloadedVolumes.Where(x => x.Value >= 0).Aggregate(0L, (a, x) => a + x.Value);
                var deletedSize = deletedVolumes.Where(x => x.Value >= 0).Aggregate(0L, (a, x) => a + x.Value);
                var uploadSize = uploadedVolumes.Where(x => x.Value >= 0).Aggregate(0L, (a, x) => a + x.Value);

                m_result.DeletedFileCount = deletedVolumes.Count;
                m_result.DownloadedFileCount = downloadedVolumes.Count;
                m_result.UploadedFileCount = uploadedVolumes.Count;
                m_result.DeletedFileSize = deletedSize;
                m_result.DownloadedFileSize = downloadSize;
                m_result.UploadedFileSize = uploadSize;
                m_result.Dryrun = m_options.Dryrun;

                if (m_result.Dryrun)
                {
                    if (downloadedVolumes.Count == 0)
                        Logging.Log.WriteDryrunMessage(LOGTAG, "CompactResults", "Would delete {0} files, which would reduce storage by {1}", m_result.DeletedFileCount, Library.Utility.Utility.FormatSizeString(m_result.DeletedFileSize));
                    else
                        Logging.Log.WriteDryrunMessage(LOGTAG, "CompactResults", "Would download {0} file(s) with a total size of {1}, delete {2} file(s) with a total size of {3}, and compact to {4} file(s) with a size of {5}, which would reduce storage by {6} file(s) and {7}",
                                                                m_result.DownloadedFileCount,
                                                                Library.Utility.Utility.FormatSizeString(m_result.DownloadedFileSize),
                                                                m_result.DeletedFileCount,
                                                                Library.Utility.Utility.FormatSizeString(m_result.DeletedFileSize), m_result.UploadedFileCount,
                                                                Library.Utility.Utility.FormatSizeString(m_result.UploadedFileSize),
                                                                m_result.DeletedFileCount - m_result.UploadedFileCount,
                                                                Library.Utility.Utility.FormatSizeString(m_result.DeletedFileSize - m_result.UploadedFileSize));
                }
                else
                {
                    if (m_result.DownloadedFileCount == 0)
                        Logging.Log.WriteInformationMessage(LOGTAG, "CompactResults", "Deleted {0} files, which reduced storage by {1}", m_result.DeletedFileCount, Library.Utility.Utility.FormatSizeString(m_result.DeletedFileSize));
                    else
                        Logging.Log.WriteInformationMessage(LOGTAG, "CompactResults", "Downloaded {0} file(s) with a total size of {1}, deleted {2} file(s) with a total size of {3}, and compacted to {4} file(s) with a size of {5}, which reduced storage by {6} file(s) and {7}",
                                                          m_result.DownloadedFileCount,
                                                          Library.Utility.Utility.FormatSizeString(downloadSize),
                                                          m_result.DeletedFileCount,
                                                          Library.Utility.Utility.FormatSizeString(m_result.DeletedFileSize),
                                                          m_result.UploadedFileCount,
                                                          Library.Utility.Utility.FormatSizeString(m_result.UploadedFileSize),
                                                          m_result.DeletedFileCount - m_result.UploadedFileCount,
                                                          Library.Utility.Utility.FormatSizeString(m_result.DeletedFileSize - m_result.UploadedFileSize));
                }

                await backendManager.WaitForEmptyAsync(db, m_result.TaskControl.ProgressToken).ConfigureAwait(false);

                m_result.EndTime = DateTime.UtcNow;
                return (m_result.DeletedFileCount + m_result.UploadedFileCount) > 0;
            }
            else
            {
                m_result.EndTime = DateTime.UtcNow;
                return false;
            }
        }

        private async IAsyncEnumerable<KeyValuePair<string, long>> DoDelete(LocalDeleteDatabase db, IBackendManager backend, IEnumerable<IRemoteVolume> deleteableVolumes, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Find volumes that can be deleted
            var remoteFilesToRemove = await db
                .ReOrderDeleteableVolumes(deleteableVolumes)
                .ToListAsync()
                .ConfigureAwait(false);

            // Make sure we do not re-assign blocks to any of the volumes we are about to delete
            var toRemoveVolumeIds = await db
                .GetRemoteVolumeIDs(remoteFilesToRemove.Select(x => x.Name))
                .Select(x => x.Value)
                .Distinct()
                .ToListAsync()
                .ConfigureAwait(false);

            // Mark all volumes and relevant index files as disposable
            foreach (var f in remoteFilesToRemove)
            {
                await db
                    .PrepareForDelete(f.Name, toRemoveVolumeIds)
                    .ConfigureAwait(false);

                await db
                    .UpdateRemoteVolume(f.Name, RemoteVolumeState.Deleting, f.Size, f.Hash)
                    .ConfigureAwait(false);
            }

            // Before we commit the current state, make sure the backend has caught up
            await backend.WaitForEmptyAsync(db, cancellationToken).ConfigureAwait(false);

            if (!m_options.Dryrun)
                await db.Transaction
                    .CommitAsync("CommitDelete")
                    .ConfigureAwait(false);

            await foreach (var d in PerformDelete(backend, remoteFilesToRemove, cancellationToken).ConfigureAwait(false))
                yield return d;
        }

        private async Task FinishVolumeAndUpload(LocalDeleteDatabase db, IBackendManager backendManager, BlockVolumeWriter newvol, IndexVolumeWriter newvolindex, List<KeyValuePair<string, long>> uploadedVolumes)
        {
            Action indexVolumeFinished = null;
            if (newvolindex != null && m_options.IndexfilePolicy == Options.IndexFileStrategy.Full)
                indexVolumeFinished = async () =>
                {
                    await foreach (var blocklist in db.GetBlocklists(newvol.VolumeID, m_options.Blocksize, m_options.BlockhashSize).ConfigureAwait(false))
                        newvolindex.WriteBlocklist(blocklist.Hash, blocklist.Buffer, 0, blocklist.Size);
                };

            uploadedVolumes.Add(new KeyValuePair<string, long>(newvol.RemoteFilename, newvol.Filesize));
            if (newvolindex != null)
                uploadedVolumes.Add(new KeyValuePair<string, long>(newvolindex.RemoteFilename, newvolindex.Filesize));

            // We can handle at most one in-flight upload at a time,
            // because the transaction is not thread-safe, and shared with the upload
            await backendManager.WaitForEmptyAsync(db, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
            await db
                .UpdateRemoteVolume(newvol.RemoteFilename, RemoteVolumeState.Uploading, -1, null)
                .ConfigureAwait(false);

            // TODO: The upload here does not flush the database messages,
            // and this can leave the database in a state where it does not know of the remote file
            // To fix it, we need thread-safe access to the database and transaction
            // Once fixed, we can perhaps let he backend manager simply call the database directly
            if (!m_options.Dryrun)
            {
                await db.Transaction
                    .CommitAsync("CommitUpload")
                    .ConfigureAwait(false);

                await backendManager.PutAsync(newvol, newvolindex, indexVolumeFinished, false, null, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
            }
            else
                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldUploadGeneratedBlockset", "Would upload generated blockset of size {0}", Library.Utility.Utility.FormatSizeString(newvol.Filesize));
        }

        private async IAsyncEnumerable<KeyValuePair<string, long>> PerformDelete(IBackendManager backendManager, IEnumerable<IRemoteVolume> list, [EnumeratorCancellation] CancellationToken cancelToken)
        {
            foreach (var f in list)
            {
                if (!m_options.Dryrun)
                    await backendManager.DeleteAsync(f.Name, f.Size, false, cancelToken).ConfigureAwait(false);
                else
                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldDeleteRemoteFile", "Would delete remote file: {0}, size: {1}", f.Name, Library.Utility.Utility.FormatSizeString(f.Size));

                yield return new KeyValuePair<string, long>(f.Name, f.Size);
            }
        }
    }
}
