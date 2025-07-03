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
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using Microsoft.Data.Sqlite;

namespace Duplicati.Library.Main.Operation
{
    internal class PurgeFilesHandler
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<PurgeFilesHandler>();
        protected readonly Options m_options;
        protected readonly PurgeFilesResults m_result;

        public PurgeFilesHandler(Options options, PurgeFilesResults result)
        {
            m_options = options;
            m_result = result;
        }

        public async Task RunAsync(IBackendManager backendManager, IFilter filter)
        {
            if (filter == null || filter.Empty)
                throw new UserInformationException("Cannot purge with an empty filter, as that would cause all files to be removed.\nTo remove an entire backup set, use the \"delete\" command.", "EmptyFilterPurgeNotAllowed");

            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath), "DatabaseDoesNotExist");

            await using var db = await Database.LocalPurgeDatabase.CreateAsync(m_options.Dbpath, null, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
            await DoRunAsync(backendManager, db, filter, null, 0, 1).ConfigureAwait(false);
            await db
                .VerifyConsistency(m_options.Blocksize, m_options.BlockhashSize, true, m_result.TaskControl.ProgressToken)
                .ConfigureAwait(false);
        }

        public Task RunAsync(IBackendManager backendManager, Database.LocalPurgeDatabase db, float pgoffset, float pgspan, Func<SqliteCommand, long, string, Task<int>> filtercommand)
            => DoRunAsync(backendManager, db, null, filtercommand, pgoffset, pgspan);

        private async Task DoRunAsync(IBackendManager backendManager, Database.LocalPurgeDatabase db, IFilter filter, Func<SqliteCommand, long, string, Task<int>> filtercommand, float pgoffset, float pgspan)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.PurgeFiles_Begin);
            Logging.Log.WriteInformationMessage(LOGTAG, "StartingPurge", "Starting purge operation");

            var doCompactStep = !m_options.NoAutoCompact && filtercommand == null;

            if (await db.PartiallyRecreated(m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                throw new UserInformationException("The purge command does not work on partially recreated databases", "PurgeNotAllowedOnPartialDatabase");

            if (await db.RepairInProgress(m_result.TaskControl.ProgressToken).ConfigureAwait(false) && filtercommand == null)
                throw new UserInformationException(string.Format("The purge command does not work on an incomplete database, try the {0} operation.", "purge-broken-files"), "PurgeNotAllowedOnIncompleteDatabase");

            var versions = await db
                .GetFilesetIDs(m_options.Time, m_options.Version, false, m_result.TaskControl.ProgressToken)
                .OrderByDescending(x => x)
                .ToArrayAsync(cancellationToken: m_result.TaskControl.ProgressToken)
                .ConfigureAwait(false);
            if (versions.Length <= 0)
                throw new UserInformationException("No filesets matched the supplied time or versions", "NoFilesetFoundForTimeOrVersion");

            var orphans = await db.CountOrphanFiles(m_result.TaskControl.ProgressToken).ConfigureAwait(false);
            if (orphans != 0)
                throw new UserInformationException(string.Format("Unable to start the purge process as there are {0} orphan file(s)", orphans), "CannotPurgeWithOrphans");

            await Utility.UpdateOptionsFromDb(db, m_options, m_result.TaskControl.ProgressToken)
                .ConfigureAwait(false);
            await Utility.VerifyOptionsAndUpdateDatabase(db, m_options, m_result.TaskControl.ProgressToken)
                .ConfigureAwait(false);

            if (filtercommand == null)
            {
                await db.VerifyConsistency(m_options.Blocksize, m_options.BlockhashSize, false, m_result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);

                if (m_options.NoBackendverification)
                    await FilelistProcessor.VerifyLocalList(backendManager, db, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                else
                    await FilelistProcessor.VerifyRemoteList(backendManager, m_options, db, m_result.BackendWriter, null, null, logErrors: true, verifyMode: FilelistProcessor.VerifyMode.VerifyStrict, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
            }

            var filesets = await db
                .FilesetTimes(m_result.TaskControl.ProgressToken)
                .OrderByDescending(x => x.Value)
                .ToArrayAsync(cancellationToken: m_result.TaskControl.ProgressToken)
                .ConfigureAwait(false);

            var versionprogress = ((doCompactStep ? 0.75f : 1.0f) / versions.Length) * pgspan;
            var currentprogress = pgoffset;
            var progress = 0;

            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.PurgeFiles_Process);
            m_result.OperationProgressUpdater.UpdateProgress(currentprogress);

            // If we crash now, it is possible that the remote storage contains partial files
            if (!m_options.Dryrun)
                await db.TerminatedWithActiveUploads(m_result.TaskControl.ProgressToken, true).ConfigureAwait(false);

            // Reverse makes sure we re-write the old versions first
            foreach (var versionid in versions.Reverse())
            {
                progress++;
                Logging.Log.WriteVerboseMessage(LOGTAG, "ProcessingFilelistVolumes", "Processing filelist volume {0} of {1}", progress, versions.Length);

                (var _, var tsOriginal, var ix) = filesets.Select((x, i) => (x.Key, x.Value, i)).FirstOrDefault(x => x.Key == versionid);
                if (ix < 0 || tsOriginal.Ticks == 0)
                    throw new InvalidProgramException(string.Format("Fileset was reported with id {0}, but could not be found?", versionid));

                var ts = await FilesetVolumeWriter.ProbeUnusedFilenameName(db, m_options, tsOriginal, m_result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);
                var prevfilename = await db.GetRemoteVolumeNameForFileset(filesets[ix].Key, m_result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);

                if (ix != 0 && filesets[ix - 1].Value <= ts)
                    throw new Exception(string.Format("Unable to create a new fileset for {0} because the resulting timestamp {1} is larger than the next timestamp {2}", prevfilename, ts, filesets[ix - 1].Value));

                await using (var tempset = await db.CreateTemporaryFileset(versionid, m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                {
                    if (filtercommand == null)
                        await tempset.ApplyFilter(filter, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                    else
                        await tempset.ApplyFilter(filtercommand, m_result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);

                    if (tempset.RemovedFileCount + tempset.UpdatedFileCount == 0)
                    {
                        Logging.Log.WriteInformationMessage(LOGTAG, "NotWritingNewFileset", "Not writing a new fileset for {0} as it was not changed", prevfilename);
                        currentprogress += versionprogress;
                        await db.Transaction
                            .RollBackAsync()
                            .ConfigureAwait(false);
                        continue;
                    }
                    else
                    {
                        using (var tf = new TempFile())
                        using (var vol = new FilesetVolumeWriter(m_options, ts))
                        {
                            var isOriginalFilesetFullBackup = await db
                                .IsFilesetFullBackup(tsOriginal, m_result.TaskControl.ProgressToken)
                                .ConfigureAwait(false);
                            var newids = await tempset
                                .ConvertToPermanentFileset(vol.RemoteFilename, ts, isOriginalFilesetFullBackup, m_result.TaskControl.ProgressToken)
                                .ConfigureAwait(false);
                            vol.VolumeID = newids.Item1;
                            vol.CreateFilesetFile(isOriginalFilesetFullBackup);

                            Logging.Log.WriteInformationMessage(LOGTAG, "ReplacingFileset", "Replacing fileset {0} with {1} which has with {2} fewer file(s) ({3} reduction)", prevfilename, vol.RemoteFilename, tempset.RemovedFileCount, Library.Utility.Utility.FormatSizeString(tempset.RemovedFileSize));

                            await db
                                .WriteFileset(vol, newids.Item2, m_result.TaskControl.ProgressToken)
                                .ConfigureAwait(false);

                            m_result.RemovedFileSize += tempset.RemovedFileSize;
                            m_result.RemovedFileCount += tempset.RemovedFileCount;
                            m_result.UpdatedFileCount += tempset.UpdatedFileCount;
                            m_result.RewrittenFileLists++;

                            currentprogress += (versionprogress / 2);
                            m_result.OperationProgressUpdater.UpdateProgress(currentprogress);

                            if (m_options.Dryrun || m_options.FullResult)
                            {
                                await foreach (var fe in tempset.ListAllDeletedFiles(m_result.TaskControl.ProgressToken).ConfigureAwait(false))
                                {
                                    var msg = string.Format("  Purging file {0} ({1})", fe.Key, Library.Utility.Utility.FormatSizeString(fe.Value));

                                    Logging.Log.WriteProfilingMessage(LOGTAG, "PurgeFile", msg);
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "PurgeFile", msg);

                                    if (m_options.Dryrun)
                                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldPurgeFile", msg);
                                }

                                if (m_options.Dryrun)
                                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldWriteRemoteFiles", "Would write files to remote storage");

                                Logging.Log.WriteVerboseMessage(LOGTAG, "WritingRemoteFiles", "Writing files to remote storage");
                            }

                            if (m_options.Dryrun)
                            {
                                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldUploadAndDelete", "Would upload file {0} ({1}) and delete file {2}, removing {3} files", vol.RemoteFilename, Library.Utility.Utility.FormatSizeString(vol.Filesize), prevfilename, tempset.RemovedFileCount);
                                await db.Transaction
                                    .RollBackAsync()
                                    .ConfigureAwait(false);
                            }
                            else
                            {
                                await db
                                    .UpdateRemoteVolume(vol.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_result.TaskControl.ProgressToken)
                                    .ConfigureAwait(false);
                                var lst = await db
                                    .DropFilesetsFromTable(new[] { tsOriginal }, m_result.TaskControl.ProgressToken)
                                    .ToArrayAsync(cancellationToken: m_result.TaskControl.ProgressToken)
                                    .ConfigureAwait(false);
                                foreach (var f in lst)
                                    await db
                                        .UpdateRemoteVolume(f.Key, RemoteVolumeState.Deleting, f.Value, null, m_result.TaskControl.ProgressToken)
                                        .ConfigureAwait(false);

                                await db.Transaction
                                    .CommitAsync(m_result.TaskControl.ProgressToken)
                                    .ConfigureAwait(false);

                                await backendManager.PutAsync(vol, null, null, true, async () =>
                                {
                                    await backendManager.FlushPendingMessagesAsync(db, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                                    // This commit mimics the pre-
                                    // Microsoft.Data.Sqlite backend way of not
                                    // passing down a transaction, but maybe is
                                    // not needed anymore.
                                    await db.Transaction
                                        .CommitAsync(m_result.TaskControl.ProgressToken)
                                        .ConfigureAwait(false);
                                }, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                                await backendManager.DeleteAsync(prevfilename, -1, true, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                                await backendManager.WaitForEmptyAsync(db, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                            }
                        }
                    }
                }

                currentprogress += (versionprogress / 2);
                m_result.OperationProgressUpdater.UpdateProgress(currentprogress);
            }

            if (!m_options.Dryrun)
                await db
                    .TerminatedWithActiveUploads(m_result.TaskControl.ProgressToken, false)
                    .ConfigureAwait(false);

            if (doCompactStep)
            {
                if (m_result.RewrittenFileLists == 0)
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "SkippingCompacting", "Skipping compacting as no new volumes were written");
                }
                else
                {
                    m_result.OperationProgressUpdater.UpdateProgress(pgoffset + (0.75f * pgspan));
                    m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.PurgeFiles_Compact);
                    m_result.CompactResults = new CompactResults(m_result);
                    await using var cdb = await Database.LocalDeleteDatabase.CreateAsync(db, null, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                    await new CompactHandler(m_options, (CompactResults)m_result.CompactResults)
                        .DoCompactAsync(cdb, true, backendManager)
                        .ConfigureAwait(false);

                    await cdb.Transaction.CommitAsync("PostCompact", true, m_result.TaskControl.ProgressToken);
                }

                m_result.OperationProgressUpdater.UpdateProgress(pgoffset + pgspan);
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.PurgeFiles_Complete);

                await backendManager.WaitForEmptyAsync(db, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
            }
        }
    }
}
