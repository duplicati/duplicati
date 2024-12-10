// Copyright (C) 2024, The Duplicati Team
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
using System.Linq;

namespace Duplicati.Library.Main.Operation
{
    internal class CompactHandler
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<CompactHandler>();
        protected readonly string m_backendurl;
        protected readonly Options m_options;
        protected readonly CompactResults m_result;

        public CompactHandler(string backend, Options options, CompactResults result)
        {
            m_backendurl = backend;
            m_options = options;
            m_result = result;
        }

        public virtual void Run()
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new Exception(string.Format("Database file does not exist: {0}", m_options.Dbpath));

            using (var db = new LocalDeleteDatabase(m_options.Dbpath, "Compact"))
            {
                var tr = db.BeginTransaction();
                try
                {
                    m_result.SetDatabase(db);
                    Utility.UpdateOptionsFromDb(db, m_options);
                    Utility.VerifyOptionsAndUpdateDatabase(db, m_options);

                    var changed = DoCompact(db, false, ref tr, null);

                    if (changed && m_options.UploadVerificationFile)
                        FilelistProcessor.UploadVerificationFile(m_backendurl, m_options, m_result.BackendWriter, db, null);

                    if (!m_options.Dryrun)
                    {
                        using (new Logging.Timer(LOGTAG, "CommitCompact", "CommitCompact"))
                            tr.Commit();
                        if (changed)
                        {
                            db.WriteResults();
                            if (m_options.AutoVacuum)
                            {
                                m_result.VacuumResults = new VacuumResults(m_result);
                                new VacuumHandler(m_options, (VacuumResults)m_result.VacuumResults).Run();
                            }
                        }
                    }
                    else
                        tr.Rollback();

                    tr = null;
                }
                finally
                {
                    if (tr != null)
                        try { tr.Rollback(); }
                        catch { }
                }
            }
        }

        internal bool DoCompact(LocalDeleteDatabase db, bool hasVerifiedBackend, ref System.Data.IDbTransaction transaction, BackendManager sharedBackend)
        {
            var report = db.GetCompactReport(m_options.VolumeSize, m_options.Threshold, m_options.SmallFileSize, m_options.SmallFileMaxCount, transaction);
            report.ReportCompactData();

            if (report.ShouldReclaim || report.ShouldCompact)
            {
                // Workaround where we allow a running backendmanager to be used
                using (var bk = sharedBackend == null ? new BackendManager(m_backendurl, m_options, m_result.BackendWriter, db) : null)
                {
                    var backend = bk ?? sharedBackend;
                    if (!hasVerifiedBackend)
                    {
                        FilelistProcessor.VerifyRemoteList(backend, m_options, db, m_result.BackendWriter, true, transaction);
                    }

                    BlockVolumeWriter newvol = new BlockVolumeWriter(m_options);
                    newvol.VolumeID = db.RegisterRemoteVolume(newvol.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, transaction);

                    IndexVolumeWriter newvolindex = null;
                    if (m_options.IndexfilePolicy != Options.IndexFileStrategy.None)
                    {
                        newvolindex = new IndexVolumeWriter(m_options);
                        newvolindex.VolumeID = db.RegisterRemoteVolume(newvolindex.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary, transaction);
                        db.AddIndexBlockLink(newvolindex.VolumeID, newvol.VolumeID, transaction);
                    }

                    long blocksInVolume = 0;
                    byte[] buffer = new byte[m_options.Blocksize];
                    var remoteList = db.GetRemoteVolumes().Where(n => n.State == RemoteVolumeState.Uploaded || n.State == RemoteVolumeState.Verified).ToArray();

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
                    deletedVolumes.AddRange(DoDelete(db, backend, fullyDeleteable, ref transaction));

                    // This list is used to pick up unused volumes,
                    // so they can be deleted once the upload of the
                    // required fragments is complete
                    var deleteableVolumes = new List<IRemoteVolume>();

                    if (report.ShouldCompact)
                    {
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

                        using (var q = db.CreateBlockQueryHelper(transaction))
                        {
                            foreach (var entry in new AsyncDownloader(volumesToDownload, backend))
                            {
                                using (var tmpfile = entry.TempFile)
                                {
                                    if (!m_result.TaskControl.ProgressRendevouz().Await())
                                    {
                                        backend.WaitForComplete(db, transaction);
                                        return false;
                                    }

                                    downloadedVolumes.Add(new KeyValuePair<string, long>(entry.Name, entry.Size));
                                    var inst = VolumeBase.ParseFilename(entry.Name);
                                    using (var f = new BlockVolumeReader(inst.CompressionModule, tmpfile, m_options))
                                    {
                                        foreach (var e in f.Blocks)
                                        {
                                            if (q.UseBlock(e.Key, e.Value, transaction))
                                            {
                                                //TODO: How do we get the compression hint? Reverse query for filename in db?
                                                var s = f.ReadBlock(e.Key, buffer);
                                                if (s != e.Value)
                                                    throw new Exception(string.Format("Size mismatch problem for block {0}, {1} vs {2}", e.Key, s, e.Value));

                                                newvol.AddBlock(e.Key, buffer, 0, s, Duplicati.Library.Interface.CompressionHint.Compressible);
                                                if (newvolindex != null)
                                                    newvolindex.AddBlock(e.Key, e.Value);

                                                db.RegisterDuplicatedBlock(e.Key, e.Value, newvol.VolumeID, transaction);
                                                blocksInVolume++;

                                                if (newvol.Filesize > m_options.VolumeSize)
                                                {
                                                    FinishVolumeAndUpload(db, backend, newvol, newvolindex, uploadedVolumes);

                                                    newvol = new BlockVolumeWriter(m_options);
                                                    newvol.VolumeID = db.RegisterRemoteVolume(newvol.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, transaction);

                                                    if (m_options.IndexfilePolicy != Options.IndexFileStrategy.None)
                                                    {
                                                        newvolindex = new IndexVolumeWriter(m_options);
                                                        newvolindex.VolumeID = db.RegisterRemoteVolume(newvolindex.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary, transaction);
                                                        db.AddIndexBlockLink(newvolindex.VolumeID, newvol.VolumeID, transaction);
                                                        newvolindex.StartVolume(newvol.RemoteFilename);
                                                    }

                                                    blocksInVolume = 0;

                                                    // Wait for the backend to catch up
                                                    backend.WaitForEmpty(db, transaction);

                                                    // Commit as we have uploaded a volume
                                                    if (!m_options.Dryrun)
                                                    {
                                                        transaction.Commit();
                                                        transaction = db.BeginTransaction();
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
                                FinishVolumeAndUpload(db, backend, newvol, newvolindex, uploadedVolumes);
                            }
                            else
                            {
                                db.RemoveRemoteVolume(newvol.RemoteFilename, transaction);
                                if (newvolindex != null)
                                {
                                    db.RemoveRemoteVolume(newvolindex.RemoteFilename, transaction);
                                    newvolindex.FinishVolume(null, 0);
                                }
                            }
                        }
                    }
                    else
                    {
                        newvolindex?.Dispose();
                        newvol.Dispose();
                    }

                    deletedVolumes.AddRange(DoDelete(db, backend, deleteableVolumes, ref transaction));

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

                    backend.WaitForComplete(db, transaction);
                }

                m_result.EndTime = DateTime.UtcNow;
                return (m_result.DeletedFileCount + m_result.UploadedFileCount) > 0;
            }
            else
            {
                m_result.EndTime = DateTime.UtcNow;
                return false;
            }
        }

        private IEnumerable<KeyValuePair<string, long>> DoDelete(LocalDeleteDatabase db, BackendManager backend, IEnumerable<IRemoteVolume> deleteableVolumes, ref System.Data.IDbTransaction transaction)
        {
            // Find volumes that can be deleted
            var remoteFilesToRemove = db.ReOrderDeleteableVolumes(deleteableVolumes, transaction).ToList();

            // Make sure we do not re-assign blocks to any of the volumes we are about to delete
            var toRemoveVolumeIds = db.GetRemoteVolumeIDs(remoteFilesToRemove.Select(x => x.Name), transaction)
                .Select(x => x.Value)
                .Distinct()
                .ToList();

            // Mark all volumes and relevant index files as disposable
            foreach (var f in remoteFilesToRemove)
            {
                db.PrepareForDelete(f.Name, toRemoveVolumeIds, transaction);
                db.UpdateRemoteVolume(f.Name, RemoteVolumeState.Deleting, f.Size, f.Hash, transaction);
            }

            // Before we commit the current state, make sure the backend has caught up
            backend.WaitForEmpty(db, transaction);

            if (!m_options.Dryrun)
            {
                transaction.Commit();
                transaction = db.BeginTransaction();
            }

            return PerformDelete(backend, remoteFilesToRemove);
        }

        private void FinishVolumeAndUpload(LocalDeleteDatabase db, BackendManager backend, BlockVolumeWriter newvol, IndexVolumeWriter newvolindex, List<KeyValuePair<string, long>> uploadedVolumes)
        {
            Action indexVolumeFinished = () =>
            {
                if (newvolindex != null && m_options.IndexfilePolicy == Options.IndexFileStrategy.Full)
                {
                    foreach (var blocklist in db.GetBlocklists(newvol.VolumeID, m_options.Blocksize, m_options.BlockhashSize))
                    {
                        newvolindex.WriteBlocklist(blocklist.Item1, blocklist.Item2, 0, blocklist.Item3);
                    }
                }
            };

            uploadedVolumes.Add(new KeyValuePair<string, long>(newvol.RemoteFilename, newvol.Filesize));
            if (newvolindex != null)
                uploadedVolumes.Add(new KeyValuePair<string, long>(newvolindex.RemoteFilename, newvolindex.Filesize));
            if (!m_options.Dryrun)
                backend.Put(newvol, newvolindex, indexVolumeFinished);
            else
                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldUploadGeneratedBlockset", "Would upload generated blockset of size {0}", Library.Utility.Utility.FormatSizeString(newvol.Filesize));
        }

        private IEnumerable<KeyValuePair<string, long>> PerformDelete(BackendManager backend, IEnumerable<IRemoteVolume> list)
        {
            foreach (var f in list)
            {
                if (!m_options.Dryrun)
                    backend.Delete(f.Name, f.Size);
                else
                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldDeleteRemoteFile", "Would delete remote file: {0}, size: {1}", f.Name, Library.Utility.Utility.FormatSizeString(f.Size));

                yield return new KeyValuePair<string, long>(f.Name, f.Size);
            }
        }
    }
}
