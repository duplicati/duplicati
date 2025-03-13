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
using Duplicati.Library.Main.Database;
using Duplicati.Library.Utility;
using static Duplicati.Library.Main.Database.DatabaseConnectionManager;

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

        public async Task RunAsync(DatabaseConnectionManager dbManager, IBackendManager backendManager, IFilter filter)
        {
            if (filter == null || filter.Empty)
                throw new UserInformationException("Cannot purge with an empty filter, as that would cause all files to be removed.\nTo remove an entire backup set, use the \"delete\" command.", "EmptyFilterPurgeNotAllowed");

            if (!dbManager.Exists)
                throw new UserInformationException(string.Format("Database file does not exist: {0}", dbManager.Path), "DatabaseDoesNotExist");

            using (var tr = dbManager.BeginRootTransaction())
            using (var db = new LocalPurgeDatabase(dbManager))
            {
                await DoRun(backendManager, db, filter, null, 0, 1).ConfigureAwait(false);
                tr.Commit();
            }
        }

        public async Task RunAsync(IBackendManager backendManager, LocalPurgeDatabase db, float pgoffset, float pgspan, Action<DatabaseCommand, long, string> filtercommand)
        {
            using (var tr = db.BeginTransaction())
            {
                await DoRun(backendManager, db, null, filtercommand, pgoffset, pgspan).ConfigureAwait(false);
                tr.Commit();
            }
        }

        private async Task DoRun(IBackendManager backendManager, LocalPurgeDatabase db, IFilter filter, Action<DatabaseCommand, long, string> filtercommand, float pgoffset, float pgspan)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.PurgeFiles_Begin);
            Logging.Log.WriteInformationMessage(LOGTAG, "StartingPurge", "Starting purge operation");

            var doCompactStep = !m_options.NoAutoCompact && filtercommand == null;

            if (db.PartiallyRecreated)
                throw new UserInformationException("The purge command does not work on partially recreated databases", "PurgeNotAllowedOnPartialDatabase");

            if (db.RepairInProgress && filtercommand == null)
                throw new UserInformationException(string.Format("The purge command does not work on an incomplete database, try the {0} operation.", "purge-broken-files"), "PurgeNotAllowedOnIncompleteDatabase");

            var versions = db.GetFilesetIDs(m_options.Time, m_options.Version).OrderByDescending(x => x).ToArray();
            if (versions.Length <= 0)
                throw new UserInformationException("No filesets matched the supplied time or versions", "NoFilesetFoundForTimeOrVersion");

            var orphans = db.CountOrphanFiles();
            if (orphans != 0)
                throw new UserInformationException(string.Format("Unable to start the purge process as there are {0} orphan file(s)", orphans), "CannotPurgeWithOrphans");

            Utility.UpdateOptionsFromDb(db, m_options);
            Utility.VerifyOptionsAndUpdateDatabase(db, m_options);

            if (filtercommand == null)
            {
                db.VerifyConsistency(m_options.Blocksize, m_options.BlockhashSize, false);

                if (m_options.NoBackendverification)
                    await FilelistProcessor.VerifyLocalList(backendManager, db, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                else
                    await FilelistProcessor.VerifyRemoteList(backendManager, m_options, db, m_result.BackendWriter, null, null, logErrors: true, verifyMode: FilelistProcessor.VerifyMode.VerifyStrict).ConfigureAwait(false);
            }

            var filesets = db.FilesetTimes.OrderByDescending(x => x.Value).ToArray();

            var versionprogress = ((doCompactStep ? 0.75f : 1.0f) / versions.Length) * pgspan;
            var currentprogress = pgoffset;
            var progress = 0;

            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.PurgeFiles_Process);
            m_result.OperationProgressUpdater.UpdateProgress(currentprogress);

            // Reverse makes sure we re-write the old versions first
            foreach (var versionid in versions.Reverse())
            {
                progress++;
                Logging.Log.WriteVerboseMessage(LOGTAG, "ProcessingFilelistVolumes", "Processing filelist volume {0} of {1}", progress, versions.Length);

                using (var tr = db.BeginTransaction())
                {
                    var ix = -1;
                    for (var i = 0; i < filesets.Length; i++)
                        if (filesets[i].Key == versionid)
                        {
                            ix = i;
                            break;
                        }

                    if (ix < 0)
                        throw new InvalidProgramException(string.Format("Fileset was reported with id {0}, but could not be found?", versionid));

                    var secs = 0;
                    while (secs < 60)
                    {
                        secs++;
                        var tfn = Volumes.VolumeBase.GenerateFilename(RemoteVolumeType.Files, m_options, null, filesets[ix].Value.AddSeconds(secs));
                        if (db.GetRemoteVolumeID(tfn) < 0)
                            break;
                    }

                    var tsOriginal = filesets[ix].Value;
                    var ts = tsOriginal.AddSeconds(secs);

                    var prevfilename = db.GetRemoteVolumeNameForFileset(filesets[ix].Key);

                    if (secs >= 60)
                        throw new Exception(string.Format("Unable to create a new fileset for {0} because the resulting timestamp {1} is more than 60 seconds away", prevfilename, ts));

                    if (ix != 0 && filesets[ix - 1].Value <= ts)
                        throw new Exception(string.Format("Unable to create a new fileset for {0} because the resulting timestamp {1} is larger than the next timestamp {2}", prevfilename, ts, filesets[ix - 1].Value));

                    using (var tempset = db.CreateTemporaryFileset(versionid))
                    {
                        if (filtercommand == null)
                            tempset.ApplyFilter(filter);
                        else
                            tempset.ApplyFilter(filtercommand);

                        if (tempset.RemovedFileCount == 0)
                        {
                            Logging.Log.WriteInformationMessage(LOGTAG, "NotWritingNewFileset", "Not writing a new fileset for {0} as it was not changed", prevfilename);
                            currentprogress += versionprogress;
                            tr.Rollback();
                            continue;
                        }
                        else
                        {
                            using (var tf = new Library.Utility.TempFile())
                            using (var vol = new Volumes.FilesetVolumeWriter(m_options, ts))
                            {
                                var isOriginalFilesetFullBackup = db.IsFilesetFullBackup(tsOriginal);
                                var newids = tempset.ConvertToPermanentFileset(vol.RemoteFilename, ts, isOriginalFilesetFullBackup);
                                vol.VolumeID = newids.Item1;
                                vol.CreateFilesetFile(isOriginalFilesetFullBackup);

                                Logging.Log.WriteInformationMessage(LOGTAG, "ReplacingFileset", "Replacing fileset {0} with {1} which has with {2} fewer file(s) ({3} reduction)", prevfilename, vol.RemoteFilename, tempset.RemovedFileCount, Library.Utility.Utility.FormatSizeString(tempset.RemovedFileSize));

                                db.WriteFileset(vol, newids.Item2);

                                m_result.RemovedFileSize += tempset.RemovedFileSize;
                                m_result.RemovedFileCount += tempset.RemovedFileCount;
                                m_result.RewrittenFileLists++;

                                currentprogress += (versionprogress / 2);
                                m_result.OperationProgressUpdater.UpdateProgress(currentprogress);

                                if (m_options.Dryrun || m_options.FullResult)
                                {
                                    foreach (var fe in tempset.ListAllDeletedFiles())
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
                                    tr.Rollback();
                                }
                                else
                                {
                                    var lst = db.DropFilesetsFromTable(new[] { filesets[ix].Value }).ToArray();
                                    foreach (var f in lst)
                                        db.UpdateRemoteVolume(f.Key, RemoteVolumeState.Deleting, f.Value, null);

                                    tr.Commit();
                                    await backendManager.PutAsync(vol, null, null, true, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                                    await backendManager.DeleteAsync(prevfilename, -1, true, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                                    await backendManager.WaitForEmptyAsync(db, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                }

                currentprogress += (versionprogress / 2);
                m_result.OperationProgressUpdater.UpdateProgress(currentprogress);
            }

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
                    using (var cdb = new LocalDeleteDatabase(db))
                    {
                        var tr = cdb.BeginTransaction();
                        try
                        {
                            await new CompactHandler(m_options, (CompactResults)m_result.CompactResults)
                                .DoCompactAsync(cdb, true, tr, backendManager)
                                .ConfigureAwait(false);
                            tr.Commit();
                        }
                        catch
                        {
                            try { tr.Rollback(); }
                            catch { }
                        }
                    }
                }

                m_result.OperationProgressUpdater.UpdateProgress(pgoffset + pgspan);
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.PurgeFiles_Complete);

                await backendManager.WaitForEmptyAsync(db, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
            }
        }
    }
}
