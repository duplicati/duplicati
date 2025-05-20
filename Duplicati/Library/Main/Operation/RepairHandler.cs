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
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Operation.Backup;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation
{
    internal class RepairHandler
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<RepairHandler>();
        private readonly Options m_options;
        private readonly RepairResults m_result;

        public RepairHandler(Options options, RepairResults result)
        {
            m_options = options;
            m_result = result;

            if (options.AllowPassphraseChange)
                throw new UserInformationException(Strings.Common.PassphraseChangeUnsupported, "PassphraseChangeUnsupported");
        }

        public async Task RunAsync(IBackendManager backendManager, IFilter filter)
        {
            if (!File.Exists(m_options.Dbpath))
            {
                await RunRepairLocalAsync(backendManager, filter).ConfigureAwait(false);
                RunRepairCommon();
                m_result.EndTime = DateTime.UtcNow;
                return;
            }

            long knownRemotes = -1;
            try
            {
                using (var db = new LocalRepairDatabase(m_options.Dbpath, m_options.SqlitePageCache))
                    knownRemotes = db.GetRemoteVolumes().Count();
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "FailedToReadLocalDatabase", ex, "Failed to read local db {0}, error: {1}", m_options.Dbpath, ex.Message);
            }

            if (knownRemotes <= 0)
            {
                if (m_options.Dryrun)
                {
                    Logging.Log.WriteDryrunMessage(LOGTAG, "PerformingDryrunRecreate", "Performing dryrun recreate");
                }
                else
                {
                    var baseName = Path.ChangeExtension(m_options.Dbpath, "backup");
                    var i = 0;
                    while (File.Exists(baseName) && i++ < 1000)
                        baseName = Path.ChangeExtension(m_options.Dbpath, "backup-" + i.ToString());

                    Logging.Log.WriteInformationMessage(LOGTAG, "RenamingDatabase", "Renaming existing db from {0} to {1}", m_options.Dbpath, baseName);
                    File.Move(m_options.Dbpath, baseName);
                }

                await RunRepairLocalAsync(backendManager, filter).ConfigureAwait(false);
                RunRepairCommon();
            }
            else
            {
                RunRepairCommon();
                await RunRepairBrokenFilesets(backendManager).ConfigureAwait(false);
                await RunRepairRemoteAsync(backendManager, m_result.TaskControl.ProgressToken).ConfigureAwait(false);
            }

            m_result.EndTime = DateTime.UtcNow;

        }

        public async Task RunRepairLocalAsync(IBackendManager backendManager, IFilter filter)
        {
            m_result.RecreateDatabaseResults = new RecreateDatabaseResults(m_result);
            using (new Logging.Timer(LOGTAG, "RecreateDbForRepair", "Recreate database for repair"))
            using (var f = m_options.Dryrun ? new TempFile() : null)
            {
                if (f != null && File.Exists(f))
                    File.Delete(f);

                var filelistfilter = RestoreHandler.FilterNumberedFilelist(m_options.Time, m_options.Version);

                await new RecreateDatabaseHandler(m_options, (RecreateDatabaseResults)m_result.RecreateDatabaseResults)
                    .RunAsync(m_options.Dryrun ? (string)f : m_options.Dbpath, backendManager, filter, filelistfilter, null)
                    .ConfigureAwait(false);
            }
        }

        private async Task RunRepairRemoteAsync(IBackendManager backendManager, CancellationToken cancellationToken)
        {
            if (!File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath), "RepairDatabaseFileDoesNotExist");

            m_result.OperationProgressUpdater.UpdateProgress(0);

            using (var db = new LocalRepairDatabase(m_options.Dbpath, m_options.SqlitePageCache))
            using (var rtr = new ReusableTransaction(db))
            {
                Utility.UpdateOptionsFromDb(db, m_options);
                Utility.VerifyOptionsAndUpdateDatabase(db, m_options);

                if (db.PartiallyRecreated)
                    throw new UserInformationException("The database was only partially recreated. This database may be incomplete and the repair process is not allowed to alter remote files as that could result in data loss.", "DatabaseIsPartiallyRecreated");

                if (db.RepairInProgress)
                    throw new UserInformationException("The database was attempted repaired, but the repair did not complete. This database may be incomplete and the repair process is not allowed to alter remote files as that could result in data loss.", "DatabaseIsInRepairState");

                // Ensure the database is consistent before we start fixing the remote
                db.VerifyConsistencyForRepair(m_options.Blocksize, m_options.BlockhashSize, true, rtr.Transaction);

                // If the last backup failed, guard the incomplete fileset, so we can create a synthetic filelist
                var lastTempFilelist = db.GetLastIncompleteFilesetVolume(rtr.Transaction);
                var tp = await FilelistProcessor.RemoteListAnalysis(backendManager, m_options, db, rtr.Transaction, m_result.BackendWriter, [lastTempFilelist.Name], null, FilelistProcessor.VerifyMode.VerifyAndCleanForced).ConfigureAwait(false);
                rtr.Commit("CommitRemoteListAnalysisTransaction");

                var buffer = new byte[m_options.Blocksize];
                var hashsize = m_options.BlockhashSize;

                var missingRemoteFilesets = db.MissingRemoteFilesets().ToList();
                var missingLocalFilesets = db.MissingLocalFilesets().ToList();
                var emptyIndexFiles = db.EmptyIndexFiles().ToList();

                var progress = 0;
                var targetProgess = tp.ExtraVolumes.Count() + tp.MissingVolumes.Count() + tp.VerificationRequiredVolumes.Count() + missingRemoteFilesets.Count + missingLocalFilesets.Count + emptyIndexFiles.Count;

                var mostRecentLocal = db.GetRemoteVolumes(rtr.Transaction)
                    .Where(x => x.Type == RemoteVolumeType.Files && x.State != RemoteVolumeState.Deleted)
                    .Select(x => VolumeBase.ParseFilename(x.Name).Time.ToLocalTime())
                    .Append(DateTime.MinValue).Max();

                var mostRecentRemote = tp.ParsedVolumes.Select(x => x.Time.ToLocalTime()).Append(DateTime.MinValue).Max();
                if (mostRecentLocal < DateTime.UnixEpoch)
                    throw new UserInformationException("The local database has no fileset times. Consider deleting the local database and run the repair operation again.", "LocalDatabaseHasNoFilesetTimes");
                if (mostRecentRemote > mostRecentLocal)
                {
                    if (m_options.RepairIgnoreOutdatedDatabase)
                        Logging.Log.WriteWarningMessage(LOGTAG, "RemoteFilesNewerThanLocalDatabase", null, "The remote files are newer ({0}) than the local database ({1}), this is likely because the database is outdated. Continuing as the options force ignoring this.", mostRecentRemote, mostRecentLocal);
                    else
                        throw new UserInformationException($"The remote files are newer ({mostRecentRemote}) than the local database ({mostRecentLocal}), this is likely because the database is outdated. Consider deleting the local database and run the repair operation again. If this is expected, set the option \"--repair-ignore-outdated-database\" ", "RemoteFilesNewerThanLocalDatabase");
                }

                if (m_options.Dryrun)
                {
                    if (!tp.ParsedVolumes.Any() && tp.OtherVolumes.Any())
                    {
                        if (tp.BackupPrefixes.Length == 1)
                            throw new UserInformationException(string.Format("Found no backup files with prefix {0}, but files with prefix {1}, did you forget to set the backup prefix?", m_options.Prefix, tp.BackupPrefixes[0]), "RemoteFolderEmptyWithPrefix");
                        else
                            throw new UserInformationException(string.Format("Found no backup files with prefix {0}, but files with prefixes {1}, did you forget to set the backup prefix?", m_options.Prefix, string.Join(", ", tp.BackupPrefixes)), "RemoteFolderEmptyWithPrefix");
                    }
                    else if (!tp.ParsedVolumes.Any() && tp.ExtraVolumes.Any())
                    {
                        throw new UserInformationException(string.Format("No files were missing, but {0} remote files were, found, did you mean to run recreate-database?", tp.ExtraVolumes.Count()), "NoRemoteFilesMissing");
                    }
                }

                if (tp.ExtraVolumes.Any() || tp.MissingVolumes.Any() || tp.VerificationRequiredVolumes.Any() || missingRemoteFilesets.Any() || missingLocalFilesets.Any() || emptyIndexFiles.Any())
                {
                    if (tp.VerificationRequiredVolumes.Any())
                    {
                        using (var testdb = new LocalTestDatabase(db))
                        {
                            foreach (var n in tp.VerificationRequiredVolumes)
                                try
                                {
                                    if (!await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                                    {
                                        await backendManager.WaitForEmptyAsync(testdb, rtr.Transaction, cancellationToken).ConfigureAwait(false);
                                        if (!m_options.Dryrun)
                                            rtr.Commit("CommitEarlyExit", false);
                                        return;
                                    }

                                    progress++;
                                    m_result.OperationProgressUpdater.UpdateProgress((float)progress / targetProgess);

                                    KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>> res;
                                    (var tf, var hash, var size) = await backendManager.GetWithInfoAsync(n.Name, n.Hash, n.Size, cancellationToken).ConfigureAwait(false);
                                    using (tf)
                                        res = TestHandler.TestVolumeInternals(testdb, rtr, n, tf, m_options, 1);

                                    if (res.Value.Any())
                                        throw new Exception(string.Format("Remote verification failure: {0}", res.Value.First()));

                                    if (!m_options.Dryrun)
                                    {
                                        Logging.Log.WriteInformationMessage(LOGTAG, "CapturedRemoteFileHash", "Sucessfully captured hash for {0}, updating database", n.Name);
                                        db.UpdateRemoteVolume(n.Name, RemoteVolumeState.Verified, size, hash);
                                    }

                                }
                                catch (Exception ex)
                                {
                                    Logging.Log.WriteErrorMessage(LOGTAG, "RemoteFileVerificationError", ex, "Failed to perform verification for file: {0}, please run verify; message: {1}", n.Name, ex.Message);
                                    if (ex.IsAbortException())
                                        throw;
                                }

                            rtr.Commit("CommitVerificationTransaction");
                        }
                    }

                    // TODO: It is actually possible to use the extra files if we parse them
                    foreach (var n in tp.ExtraVolumes)
                        try
                        {
                            if (!await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                            {
                                await backendManager.WaitForEmptyAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);
                                if (!m_options.Dryrun)
                                    rtr.Commit("CommitEarlyExit", false);
                                return;
                            }

                            progress++;
                            m_result.OperationProgressUpdater.UpdateProgress((float)progress / targetProgess);

                            // If this is a new index file, we can accept it if it matches our local data
                            // This makes it possible to augment the remote store with new index data
                            if (n.FileType == RemoteVolumeType.Index && m_options.IndexfilePolicy != Options.IndexFileStrategy.None)
                            {
                                try
                                {
                                    (var tf, var hash, var size) = await backendManager.GetWithInfoAsync(n.File.Name, null, n.File.Size, cancellationToken).ConfigureAwait(false);
                                    using (tf)
                                    using (var ifr = new IndexVolumeReader(n.CompressionModule, tf, m_options, m_options.BlockhashSize))
                                    {
                                        foreach (var rv in ifr.Volumes)
                                        {
                                            var entry = db.GetRemoteVolume(rv.Filename);
                                            if (entry.ID < 0)
                                                throw new Exception(string.Format("Unknown remote file {0} detected", rv.Filename));

                                            if (!new[] { RemoteVolumeState.Uploading, RemoteVolumeState.Uploaded, RemoteVolumeState.Verified }.Contains(entry.State))
                                                throw new Exception(string.Format("Volume {0} has local state {1}", rv.Filename, entry.State));

                                            if (entry.Hash != rv.Hash || entry.Size != rv.Length || !new[] { RemoteVolumeState.Uploading, RemoteVolumeState.Uploaded, RemoteVolumeState.Verified }.Contains(entry.State))
                                                throw new Exception(string.Format("Volume {0} hash/size mismatch ({1} - {2}) vs ({3} - {4})", rv.Filename, entry.Hash, entry.Size, rv.Hash, rv.Length));

                                            db.CheckAllBlocksAreInVolume(rv.Filename, rv.Blocks, rtr.Transaction);
                                        }

                                        var blocksize = m_options.Blocksize;
                                        foreach (var ixb in ifr.BlockLists)
                                            db.CheckBlocklistCorrect(ixb.Hash, ixb.Length, ixb.Blocklist, blocksize, hashsize, rtr.Transaction);

                                        // Register the new index file and link it to the block files
                                        var selfid = db.RegisterRemoteVolume(n.File.Name, RemoteVolumeType.Index, RemoteVolumeState.Uploading, size, new TimeSpan(0), rtr.Transaction);
                                        foreach (var rv in ifr.Volumes)
                                        {
                                            // Guard against unknown block files
                                            long id = db.GetRemoteVolumeID(rv.Filename, rtr.Transaction);
                                            if (id == -1)
                                                Logging.Log.WriteWarningMessage(LOGTAG, "UnknownBlockFile", null, "Index file {0} references unknown block file: {1}", n.File.Name, rv.Filename);
                                            else
                                                db.AddIndexBlockLink(selfid, id, rtr.Transaction);
                                        }
                                        if (!m_options.Dryrun)
                                            rtr.Commit("CommitIndexFileTransaction");
                                    }

                                    // All checks fine, we accept the new index file
                                    Logging.Log.WriteInformationMessage(LOGTAG, "AcceptNewIndexFile", "Accepting new index file {0}", n.File.Name);
                                    db.UpdateRemoteVolume(n.File.Name, RemoteVolumeState.Verified, size, hash, rtr.Transaction);
                                    continue;
                                }
                                catch (Exception rex)
                                {
                                    Logging.Log.WriteErrorMessage(LOGTAG, "FailedNewIndexFile", rex, "Failed to accept new index file: {0}, message: {1}", n.File.Name, rex.Message);
                                }
                            }

                            if (!m_options.Dryrun)
                            {
                                db.RegisterRemoteVolume(n.File.Name, n.FileType, n.File.Size, RemoteVolumeState.Deleting);
                                await backendManager.DeleteAsync(n.File.Name, n.File.Size, false, cancellationToken).ConfigureAwait(false);
                            }
                            else
                                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldDeleteFile", "would delete file {0}", n.File.Name);
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteErrorMessage(LOGTAG, "FailedExtraFileCleanup", ex, "Failed to perform cleanup for extra file: {0}, message: {1}", n.File.Name, ex.Message);
                            if (ex.IsAbortException())
                                throw;
                        }

                    var missingDblocks = tp.MissingVolumes.Where(x => x.Type == RemoteVolumeType.Blocks);
                    if (!m_options.RebuildMissingDblockFiles && missingDblocks.Count() > 0)
                        throw new UserInformationException($"The backup storage destination is missing data files. You can either enable `--rebuild-missing-dblock-files` or run the purge command to remove these files. The following files are missing: {string.Join(", ", missingDblocks.Select(x => x.Name))}", "MissingDblockFiles");

                    var anyDlistUploads = false;
                    foreach (var (filesetId, timestamp, isfull) in missingRemoteFilesets)
                    {
                        if (!await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                        {
                            await backendManager.WaitForEmptyAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);
                            if (!m_options.Dryrun)
                                rtr.Commit("CommitEarlyExit", false);
                            return;
                        }

                        progress++;
                        m_result.OperationProgressUpdater.UpdateProgress((float)progress / targetProgess);
                        var fileTime = FilesetVolumeWriter.ProbeUnusedFilenameName(db, rtr.Transaction, m_options, timestamp);

                        var fsw = new FilesetVolumeWriter(m_options, fileTime);
                        Logging.Log.WriteInformationMessage(LOGTAG, "ReuploadingFileset", "Re-uploading fileset {0} from {1} as remote volume registration is missing, new filename: {2}", filesetId, timestamp, fsw.RemoteFilename);

                        if (!string.IsNullOrEmpty(m_options.ControlFiles))
                            foreach (var p in m_options.ControlFiles.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                                fsw.AddControlFile(p, m_options.GetCompressionHintFromFilename(p));

                        fsw.CreateFilesetFile(isfull);
                        db.WriteFileset(fsw, filesetId, null);
                        fsw.Close();


                        fsw.VolumeID = db.RegisterRemoteVolume(fsw.RemoteFilename, RemoteVolumeType.Files, -1, RemoteVolumeState.Temporary);
                        db.LinkFilesetToVolume(filesetId, fsw.VolumeID, null);
                        if (m_options.Dryrun)
                        {
                            fsw.Dispose();
                            Logging.Log.WriteDryrunMessage(LOGTAG, "WouldReUploadFileset", "would re-upload fileset {0}", fsw.RemoteFilename);
                            continue;
                        }
                        await backendManager.PutAsync(fsw, null, null, false, null, cancellationToken).ConfigureAwait(false);
                    }

                    if (anyDlistUploads)
                    {
                        await backendManager.WaitForEmptyAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);
                        await backendManager.FlushPendingMessagesAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);
                        if (!m_options.Dryrun)
                            rtr.Commit("CommitFilesetTransaction");
                    }

                    foreach (var volumename in missingLocalFilesets)
                    {
                        var remoteVolume = db.GetRemoteVolume(volumename);
                        using (var tmpfile = await backendManager.GetAsync(remoteVolume.Name, remoteVolume.Hash, remoteVolume.Size, cancellationToken).ConfigureAwait(false))
                        {
                            var parsed = VolumeBase.ParseFilename(remoteVolume.Name);
                            using (var stream = new FileStream(tmpfile, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (var compressor = DynamicLoader.CompressionLoader.GetModule(parsed.CompressionModule, stream, ArchiveMode.Read, m_options.RawOptions))
                            using (var recreatedb = new LocalRecreateDatabase(db, m_options))
                            {
                                if (compressor == null)
                                    throw new UserInformationException(string.Format("Failed to load compression module: {0}", parsed.CompressionModule), "FailedToLoadCompressionModule");

                                var filesetid = db.CreateFileset(remoteVolume.ID, parsed.Time, rtr.Transaction);
                                RecreateDatabaseHandler.RecreateFilesetFromRemoteList(recreatedb, rtr.Transaction, compressor, filesetid, m_options, new FilterExpression());
                                if (!m_options.Dryrun)
                                    rtr.Commit("CommitRecreateFilesetTransaction");
                            }
                        }
                    }

                    if (!m_options.Dryrun && tp.MissingVolumes.Any())
                        db.TerminatedWithActiveUploads = true;

                    if (tp.MissingVolumes.Any(x => x.Type != RemoteVolumeType.Index && x.Type != RemoteVolumeType.Files && x.Type != RemoteVolumeType.Blocks))
                        throw new InvalidOperationException(string.Format("Unknown volume type {0} detected", tp.MissingVolumes.First(x => x.Type != RemoteVolumeType.Index && x.Type != RemoteVolumeType.Files && x.Type != RemoteVolumeType.Blocks).Type));

                    // Process each of the missing volumes in the order of blocks, files and index
                    // It is important that we process the blocks first, as the index files are derived from the blocks

                    Action incrementProgress = () =>
                    {
                        progress++;
                        m_result.OperationProgressUpdater.UpdateProgress((float)progress / targetProgess);
                    };

                    // Blocks are recreated with the entire list of missing files to handle cases where partial recreation is needed
                    await RunRepairDblocks(backendManager, db, rtr, missingDblocks, incrementProgress, cancellationToken).ConfigureAwait(false);

                    // Filesets are recreated one at a time
                    foreach (var n in tp.MissingVolumes.Where(x => x.Type == RemoteVolumeType.Files))
                    {
                        FilesetVolumeWriter newEntry = null;
                        incrementProgress();

                        try
                        {
                            var timestamp = VolumeBase.ParseFilename(n.Name).Time;
                            var fileTime = FilesetVolumeWriter.ProbeUnusedFilenameName(db, rtr.Transaction, m_options, timestamp);
                            var volumeWriter = newEntry = new FilesetVolumeWriter(m_options, fileTime);

                            await RunRepairDlist(backendManager, db, rtr, volumeWriter, n, fileTime, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            if (newEntry != null)
                                try { newEntry?.Dispose(); }
                                catch { }

                            Logging.Log.WriteErrorMessage(LOGTAG, "CleanupMissingFileError", ex, "Failed to perform cleanup for missing file: {0}, message: {1}", n.Name, ex.Message);

                            if (ex.IsAbortException())
                                throw;
                        }
                    }

                    // Index files are recreated one at a time, as they are derived from the blocks
                    foreach (var n in tp.MissingVolumes.Where(x => x.Type == RemoteVolumeType.Index))
                    {
                        IndexVolumeWriter newEntry = null;
                        incrementProgress();

                        try
                        {
                            // Check if the index file has already been deleted, beause the dblock was recreated
                            var currentState = db.GetRemoteVolume(n.Name, rtr.Transaction).State;
                            if (currentState == RemoteVolumeState.Deleted || currentState == RemoteVolumeState.Temporary || currentState == RemoteVolumeState.Deleting)
                                continue;

                            var w = newEntry = new IndexVolumeWriter(m_options);
                            await RunRepairDindex(backendManager, db, rtr, w, n, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            if (newEntry != null)
                                try { newEntry?.Dispose(); }
                                catch { }

                            Logging.Log.WriteErrorMessage(LOGTAG, "CleanupMissingFileError", ex, "Failed to perform cleanup for missing file: {0}, message: {1}", n.Name, ex.Message);

                            if (ex.IsAbortException())
                                throw;
                        }
                    }

                    foreach (var emptyIndexFile in emptyIndexFiles)
                    {
                        try
                        {
                            if (!await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                            {
                                await backendManager.WaitForEmptyAsync(db, null, cancellationToken).ConfigureAwait(false);
                                return;
                            }

                            progress++;
                            m_result.OperationProgressUpdater.UpdateProgress((float)progress / targetProgess);

                            if (m_options.Dryrun)
                                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldDeleteEmptyIndexFile", "would delete empty index file {0}", emptyIndexFile.Name);
                            else
                            {
                                if (emptyIndexFile.Size > 2048)
                                {
                                    Logging.Log.WriteWarningMessage(LOGTAG, "LargeEmptyIndexFile", null, "The empty index file {0} is larger than expected ({1} bytes), choosing not to delete it", emptyIndexFile.Name, emptyIndexFile.Size);
                                }
                                else
                                {
                                    Logging.Log.WriteInformationMessage(LOGTAG, "DeletingEmptyIndexFile", "Deleting empty index file {0}", emptyIndexFile.Name);
                                    await backendManager.DeleteAsync(emptyIndexFile.Name, emptyIndexFile.Size, false, cancellationToken).ConfigureAwait(false);
                                    await backendManager.FlushPendingMessagesAsync(db, null, cancellationToken).ConfigureAwait(false);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteErrorMessage(LOGTAG, "CleanupEmptyIndexFileError", ex, "Failed to perform cleanup for empty index file: {0}, message: {1}", emptyIndexFile.Name, ex.Message);

                            if (ex.IsAbortException())
                                throw;
                        }
                    }
                }
                else
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "DatabaseIsSynchronized", "Destination and database are synchronized, not making any changes");
                }

                m_result.OperationProgressUpdater.UpdateProgress(1);
                await backendManager.WaitForEmptyAsync(db, null, cancellationToken).ConfigureAwait(false);
                if (!m_options.Dryrun)
                {
                    db.TerminatedWithActiveUploads = false;
                    rtr.Commit("CommitRepairTransaction", false);
                }
            }
        }

        /// <summary>
        /// Repairs a single fileset by recreating it from the database content and uploading it
        /// </summary>
        /// <param name="backendManager">The backend manager to use for uploading</param>
        /// <param name="db">The database to use for the repair</param>
        /// <param name="rtr">The transaction to use for the repair</param>
        /// <param name="volumeWriter">The volume writer to use for the repair</param>
        /// <param name="originalVolume">The remote volume entry to repair</param>
        /// <param name="filesetTime">The time of the new fileset to create</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
        /// <returns>A task representing the asynchronous operation</returns>
        private async Task RunRepairDlist(IBackendManager backendManager, LocalRepairDatabase db, ReusableTransaction rtr, FilesetVolumeWriter volumeWriter, RemoteVolumeEntry originalVolume, DateTime filesetTime, CancellationToken cancellationToken)
        {
            volumeWriter.VolumeID = db.RegisterRemoteVolume(volumeWriter.RemoteFilename, RemoteVolumeType.Files, RemoteVolumeState.Temporary, -1, TimeSpan.Zero, rtr.Transaction);
            (var prevFilesetId, var _, var isPrevFull) = db.GetFilesetFromRemotename(originalVolume.Name, rtr.Transaction);

            if (!string.IsNullOrEmpty(m_options.ControlFiles))
                foreach (var p in m_options.ControlFiles.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                    volumeWriter.AddControlFile(p, m_options.GetCompressionHintFromFilename(p));

            volumeWriter.CreateFilesetFile(isPrevFull);
            var newFilesetID = db.CreateFileset(volumeWriter.VolumeID, filesetTime, rtr.Transaction);
            db.UpdateFullBackupStateInFileset(newFilesetID, isPrevFull);

            db.LinkFilesetToVolume(newFilesetID, volumeWriter.VolumeID, rtr.Transaction);
            db.MoveFilesFromFileset(newFilesetID, prevFilesetId, rtr.Transaction);

            db.WriteFileset(volumeWriter, newFilesetID, rtr.Transaction);
            volumeWriter.Close();

            db.UpdateRemoteVolume(volumeWriter.RemoteFilename, RemoteVolumeState.Uploading, -1, null, rtr.Transaction);
            db.UpdateRemoteVolume(originalVolume.Name, RemoteVolumeState.Deleted, originalVolume.Size, originalVolume.Hash, false, TimeSpan.FromHours(2), null, rtr.Transaction);
            await backendManager.FlushPendingMessagesAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);

            if (m_options.Dryrun)
                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldReUploadFileset", "would upload fileset {0}, with size {1}, previous size {2}", originalVolume.Name, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(volumeWriter.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(originalVolume.Size));
            else
            {
                rtr.Commit("CommitPriorToFilesetUpload");
                await backendManager.PutAsync(volumeWriter, null, null, false, null, cancellationToken).ConfigureAwait(false);
            }

            await backendManager.WaitForEmptyAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);
            if (!m_options.Dryrun)
                rtr.Commit("CommitFilesetTransaction");
        }

        /// <summary>
        /// Repairs a single index file by recreating it from the database content and uploading it
        /// </summary>
        /// <param name="backendManager">The backend manager to use for uploading</param>
        /// <param name="db">The database to use for the repair</param>
        /// <param name="rtr">The transaction to use for the repair</param>
        /// <param name="indexWriter">The volume writer to use for the repair</param>
        /// <param name="originalVolume">The remote volume entry to repair</param>
        /// <param name="filesetTime">The time of the new fileset to create</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
        /// <returns>A task representing the asynchronous operation</returns>

        private async Task RunRepairDindex(IBackendManager backendManager, LocalRepairDatabase db, ReusableTransaction rtr, IndexVolumeWriter indexWriter, RemoteVolumeEntry originalVolume, CancellationToken cancellationToken)
        {
            indexWriter.VolumeID = db.RegisterRemoteVolume(indexWriter.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Uploading, -1, TimeSpan.Zero, rtr.Transaction);

            var blockvolumeids = new List<long>();
            using var h = HashFactory.CreateHasher(m_options.BlockHashAlgorithm);
            foreach (var blockvolume in db.GetBlockVolumesFromIndexName(originalVolume.Name))
            {
                indexWriter.StartVolume(blockvolume.Name);
                var volumeid = db.GetRemoteVolumeID(blockvolume.Name, rtr.Transaction);

                foreach (var b in db.GetBlocks(volumeid))
                    indexWriter.AddBlock(b.Hash, b.Size);

                indexWriter.FinishVolume(blockvolume.Hash, blockvolume.Size);
                blockvolumeids.Add(volumeid);

                if (m_options.IndexfilePolicy == Options.IndexFileStrategy.Full)
                    foreach (var b in db.GetBlocklists(volumeid, m_options.Blocksize, m_options.BlockhashSize))
                    {
                        var bh = Convert.ToBase64String(h.ComputeHash(b.Buffer, 0, b.Size));
                        if (bh != b.Hash)
                            throw new Exception(string.Format("Internal consistency check failed, generated index block has wrong hash, {0} vs {1}", bh, b.Item1));

                        indexWriter.WriteBlocklist(b.Hash, b.Buffer, 0, b.Size);
                    }
            }

            foreach (var blockvolumeid in blockvolumeids)
                db.AddIndexBlockLink(indexWriter.VolumeID, blockvolumeid, rtr.Transaction);

            indexWriter.Close();

            if (m_options.Dryrun)
                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldReUploadIndexFile", "would re-upload index file {0}, with size {1}, previous size {2}", originalVolume.Name, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(indexWriter.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(originalVolume.Size));
            else
            {
                db.UpdateRemoteVolume(originalVolume.Name, RemoteVolumeState.Deleted, originalVolume.Size, originalVolume.Hash, false, TimeSpan.FromHours(2), null, rtr.Transaction);
                await backendManager.FlushPendingMessagesAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);
                await backendManager.PutAsync(indexWriter, null, null, false, null, cancellationToken).ConfigureAwait(false);
            }

            await backendManager.WaitForEmptyAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);
            if (!m_options.Dryrun)
                rtr.Commit("CommitRepairTransaction");
        }

        /// <summary>
        /// Class for holding the state of a dblock volume that is being constructed
        /// </summary>
        private sealed class InProgressDblockVolumes
        {
            /// <summary>
            /// Database to use for the operation
            /// </summary>
            private readonly LocalDatabase m_database;
            /// <summary>
            /// Transaction to use for the operation
            /// </summary>
            private readonly ReusableTransaction m_transaction;
            /// <summary>
            /// List of volumes to delete after the upload is complete
            /// </summary>
            private readonly List<RemoteVolumeEntry> m_toDelete = new List<RemoteVolumeEntry>();
            /// <summary>
            /// Options for the current operation
            /// </summary>
            private readonly Options m_options;
            /// <summary>
            /// Maximum size of the volume before it is considered full
            /// </summary>
            private readonly long m_maxVolumeSize;
            /// <summary>
            /// The writers that are completed and ready to be uploaded
            /// </summary>
            private readonly List<BlockVolumeWriter> m_completedWriters = new List<BlockVolumeWriter>();
            /// <summary>
            /// Writer for the current in-progress volume
            /// </summary>
            private BlockVolumeWriter m_activeWriter;
            /// <summary>
            /// Flag indicating if any data has been added to the volume
            /// </summary>
            private bool m_anyData = false;

            /// <summary>
            /// Constructor for the InProgressDblockVolume class
            /// </summary>
            /// <param name="options">The options for the current operation</param>
            /// <param name="db">The database to use for registering a volume in progress</param>
            /// <param name="transaction">The transaction to use for the operation</param>
            public InProgressDblockVolumes(Options options, LocalDatabase db, ReusableTransaction transaction)
            {
                m_database = db;
                m_transaction = transaction;
                m_options = options;
                m_maxVolumeSize = options.VolumeSize - m_options.Blocksize;
                m_activeWriter = new BlockVolumeWriter(options);
                m_activeWriter.VolumeID = m_database.RegisterRemoteVolume(m_activeWriter.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, -1, TimeSpan.Zero, m_transaction.Transaction);
            }

            /// <summary>
            /// Checks if any data has been added to the current volume
            /// </summary>
            public bool AnyData => m_anyData;
            /// <summary>
            /// Checks if the current volume is full
            /// </summary>
            private bool IsFull => m_activeWriter.Filesize > m_maxVolumeSize;

            /// <summary>
            /// Gets the volume ID of the current volume
            /// </summary>
            public long VolumeID => m_activeWriter.VolumeID;
            /// <summary>
            /// Gets the remote filename of the current volume
            /// </summary>
            public string RemoteFilename => m_activeWriter.RemoteFilename;
            /// <summary>
            /// Gets the list of volumes to delete after the upload is complete
            /// </summary>
            public List<RemoteVolumeEntry> VolumesToDelete => m_toDelete;
            /// <summary>
            /// Gets the list of completed writers
            /// </summary>
            public List<BlockVolumeWriter> CompletedWriters => m_completedWriters;

            /// <summary>
            /// Adds a block to the current volume, starting a new volume if the current one is full
            /// </summary>
            /// <param name="hash">The hash of the block</param>
            /// <param name="buffer">The buffer containing the block data</param>
            /// <param name="offset">The offset in the buffer where the block data starts</param>
            /// <param name="size">The size of the block data</param>
            /// <param name="hint">The compression hint for the block</param>
            public void AddBlock(string hash, byte[] buffer, int offset, int size, CompressionHint hint)
            {
                m_activeWriter.AddBlock(hash, buffer, offset, size, hint);
                m_anyData = true;
                if (IsFull)
                    StartNewVolume();
            }

            /// <summary>
            /// Starts a new volume for the current operation
            /// </summary>
            public void StartNewVolume()
            {
                if (!m_anyData)
                    return;

                m_completedWriters.Add(m_activeWriter);

                m_activeWriter = new BlockVolumeWriter(m_options);
                m_activeWriter.VolumeID = m_database.RegisterRemoteVolume(m_activeWriter.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, -1, TimeSpan.Zero, m_transaction.Transaction);
                m_anyData = false;
            }
        }

        /// <summary>
        /// Runs the repair process for missing dblock files
        /// </summary>
        /// <param name="backendManager">The backend manager to use for uploading</param>
        /// <param name="db">The database to use for the repair</param>
        /// <param name="rtr">The transaction to use for the repair</param>
        /// <param name="missingDblockFiles">The list of missing dblock files</param>
        /// <param name="incrementProgress"><The callback to increment the progress</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
        /// <returns>>A task representing the asynchronous operation</returns>
        private async Task RunRepairDblocks(IBackendManager backendManager, LocalRepairDatabase db, ReusableTransaction rtr, IEnumerable<RemoteVolumeEntry> missingDblockFiles, Action incrementProgress, CancellationToken cancellationToken)
        {
            var currentVolume = new InProgressDblockVolumes(m_options, db, rtr);

            foreach (var n in missingDblockFiles)
            {
                try
                {
                    if (!await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                    {
                        await backendManager.WaitForEmptyAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);
                        if (!m_options.Dryrun)
                            rtr.Commit("CommitEarlyExit", false);
                        return;
                    }

                    incrementProgress();

                    await RunRepairDblock(backendManager, db, rtr, n, currentVolume, cancellationToken).ConfigureAwait(false);
                    if (currentVolume.CompletedWriters.Count > 0)
                        await UploadCompletedBlockVolumes(backendManager, currentVolume, db, rtr, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "RepairDblockError", ex, "Failed to perform repair for dblock: {0}, message: {1}", n.Name, ex.Message);
                    if (ex.IsAbortException())
                        throw;
                }
            }

            // If we have any data in the volume, complete it so it will be uploaded
            if (currentVolume.AnyData)
                currentVolume.StartNewVolume();
            // Complete any pending uploads
            await UploadCompletedBlockVolumes(backendManager, currentVolume, db, rtr, cancellationToken).ConfigureAwait(false);

            // After reset, we need to remove the temporary volume from the database
            db.RemoveRemoteVolume(currentVolume.RemoteFilename, rtr.Transaction);
        }

        /// <summary>
        /// Uploads all completed block volumes to the backend and deletes the old files
        /// </summary>
        /// <param name="backendManager">The backend manager to use for uploading</param>
        /// <param name="activeVolume">The completed volume state to upload</param>
        /// <param name="db">The database to use for the upload</param>
        /// <param name="rtr">>The transaction to use for the upload</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
        /// <returns>>A task representing the asynchronous operation</returns>
        async Task UploadCompletedBlockVolumes(IBackendManager backendManager, InProgressDblockVolumes activeVolume, LocalRepairDatabase db, ReusableTransaction rtr, CancellationToken cancellationToken)
        {
            foreach (var completedVolume in activeVolume.CompletedWriters)
            {
                db.UpdateRemoteVolume(completedVolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, rtr.Transaction);

                // Create a new index file that points to the new volume
                IndexVolumeWriter newvolindex = null;
                Action indexVolumeFinished = null;
                if (m_options.IndexfilePolicy != Options.IndexFileStrategy.None)
                {
                    newvolindex = new IndexVolumeWriter(m_options);
                    newvolindex.VolumeID = db.RegisterRemoteVolume(newvolindex.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary, rtr.Transaction);
                    newvolindex.StartVolume(completedVolume.RemoteFilename);
                    foreach (var b in db.GetBlocks(completedVolume.VolumeID))
                        newvolindex.AddBlock(b.Hash, b.Size);

                    db.AddIndexBlockLink(newvolindex.VolumeID, completedVolume.VolumeID, rtr.Transaction);
                    if (m_options.IndexfilePolicy == Options.IndexFileStrategy.Full)
                        indexVolumeFinished = () =>
                        {
                            foreach (var blocklist in db.GetBlocklists(completedVolume.VolumeID, m_options.Blocksize, m_options.BlockhashSize))
                                newvolindex.WriteBlocklist(blocklist.Item1, blocklist.Item2, 0, blocklist.Item3);
                        };
                }

                // All information is in place, we can now upload the new volume
                await backendManager.FlushPendingMessagesAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);
                if (m_options.Dryrun)
                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldReplaceBlockFile", "would upload new block file {0}", completedVolume.RemoteFilename);
                else
                {
                    rtr.Commit("PostRepairPreUploadBlockVolume");
                    await backendManager.PutAsync(completedVolume, newvolindex, indexVolumeFinished, false, null, cancellationToken).ConfigureAwait(false);
                }
            }

            // Flush the database as we have new volumes uploaded
            await backendManager.WaitForEmptyAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);

            // Prepare for deleting the old stuff
            foreach (var vol in activeVolume.VolumesToDelete)
                db.UpdateRemoteVolume(vol.Name, RemoteVolumeState.Deleting, vol.Size, vol.Hash, false, TimeSpan.FromHours(2), null, rtr.Transaction);

            // Persist desired state prior to deleting the old files
            if (!m_options.Dryrun)
                rtr.Commit("PostRepairBlockVolume");

            // Delete the old files
            foreach (var vol in activeVolume.VolumesToDelete)
                if (m_options.Dryrun)
                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldDeleteIndexFile", "would delete index file {0}", vol.Name);
                else
                    await backendManager.DeleteAsync(vol.Name, vol.Size, false, cancellationToken).ConfigureAwait(false);

            // All done, the new dblocks are now in place
            await backendManager.WaitForEmptyAsync(db, null, cancellationToken).ConfigureAwait(false);
            if (!m_options.Dryrun)
                rtr.Commit("PostRepairBlockVolume");

            activeVolume.CompletedWriters.Clear();
            activeVolume.VolumesToDelete.Clear();
        }

        /// <summary>
        /// Repairs a single dblock file by recreating it from available data
        /// </summary>
        /// <param name="backendManager">The backend manager to use for uploading</param>
        /// <param name="db">The database to use for the repair</param>
        /// <param name="rtr">The transaction to use for the repair</param>
        /// <param name="originalVolume">The remote volume entry to recreate</param>
        /// <param name="pendingVolume">The in-progress dblock volume to use for the recreate</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
        /// <returns>>A task representing the asynchronous operation</returns>
        private async Task RunRepairDblock(IBackendManager backendManager, LocalRepairDatabase db, ReusableTransaction rtr, RemoteVolumeEntry originalVolume, InProgressDblockVolumes pendingVolume, CancellationToken cancellationToken)
        {
            // The dblock files are the most complex to recreate
            // as data can be either file contents, metadata or blocklist hashes
            // We attempt to recover all three source parts in the steps below
            using (var mbl = db.CreateBlockList(originalVolume.Name, rtr))
            {
                var originalMissingBlockCount = mbl.GetMissingBlockCount();
                var recoveredSourceBlocks = 0L;

                // First we grab all known blocks from local files
                string lastRestoredHash = null;
                long lastRestoredSize = -1;
                foreach (var block in mbl.GetSourceFilesWithBlocks(m_options.Blocksize))
                {
                    if (block.Hash == lastRestoredHash && block.Size == lastRestoredSize)
                        continue;

                    var size = (int)block.Size;

                    try
                    {
                        if (!File.Exists(block.File))
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "FileNotFound", null, "File not found: {0}", block.File);
                            continue;
                        }

                        using var f = File.OpenRead(block.File);
                        f.Position = block.Offset;

                        var buffer = new byte[m_options.Blocksize];
                        if (size != Library.Utility.Utility.ForceStreamRead(f, buffer, size))
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "FileLengthMismatch", null, "Block length mismatch on {0}, expected {1} but got {2}", block.File, size, f.Length);
                            continue;
                        }

                        using var blockhasher = HashFactory.CreateHasher(m_options.BlockHashAlgorithm);
                        var newhash = Convert.ToBase64String(blockhasher.ComputeHash(buffer, 0, size));
                        if (newhash != block.Hash)
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "FileHashMismatch", null, "Block hash mismatch on {0}, expected {1} but got {2}", block.File, block.Hash, newhash);
                            continue;
                        }

                        // Found it, no need to look again
                        lastRestoredHash = block.Hash;
                        lastRestoredSize = block.Size;
                        if (mbl.SetBlockRestored(block.Hash, block.Size, pendingVolume.VolumeID))
                        {
                            recoveredSourceBlocks++;
                            pendingVolume.AddBlock(block.Hash, buffer, 0, size, CompressionHint.Default);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteErrorMessage(LOGTAG, "FileAccessError", ex, "Failed to access file: {0}", block.File);
                    }
                }

                // Then grab all blocks with metadata
                foreach (var block in mbl.GetSourceItemsWithMetadataBlocks())
                {
                    if (block.Hash == lastRestoredHash && block.Size == lastRestoredSize)
                        continue;

                    var size = (int)block.Size;

                    try
                    {
                        var isFile = File.Exists(block.Path);
                        var isDir = Directory.Exists(block.Path);
                        if (!isFile && !isDir)
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "EntryNotFound", null, "Entry not found: {0}", block.Path);
                            continue;
                        }

                        using var snapshot = Snapshots.SnapshotUtility.CreateNoSnapshot([block.Path], true, true);
                        var entry = snapshot.GetFilesystemEntry(block.Path, isDir);
                        if (entry == null)
                        {
                            Logging.Log.WriteErrorMessage(LOGTAG, "FileAccessError", null, "Entry not found: {0}", block.Path);
                            continue;
                        }

                        var metadata = MetadataGenerator.GenerateMetadata(entry, entry.Attributes, m_options);
                        var metahash = Utility.WrapMetadata(metadata, m_options);
                        if (metahash.FileHash != block.Hash || metahash.Blob.Length != size)
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "MetadataHashMismatch", null, "Metadata block hash mismatch on {0}, expected {1} but got {2}", block.Path, block.Hash, metahash.FileHash);
                            continue;
                        }

                        // Found it, no need to look again
                        lastRestoredHash = block.Hash;
                        lastRestoredSize = block.Size;
                        if (mbl.SetBlockRestored(block.Hash, block.Size, pendingVolume.VolumeID))
                        {
                            recoveredSourceBlocks++;
                            pendingVolume.AddBlock(block.Hash, metahash.Blob, 0, metahash.Blob.Length, CompressionHint.Default);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteErrorMessage(LOGTAG, "FileAccessError", ex, "Failed to access file: {0}", block.Path);
                    }
                }

                // If we did not find any source blocks,
                // we only have the database contents and duplicated blocks to work with
                // so no point in creating a new dblock volume for storing this data
                if (recoveredSourceBlocks != 0)
                {
                    // Then restore any blocklists that are missing
                    using var blocklistHash = new MemoryStream();
                    LocalRepairDatabase.BlocklistHashesEntry lastBlocklist = null;
                    var blockhashsize = m_options.BlockhashSize;
                    var hashesPerBlock = m_options.Blocksize / m_options.BlockhashSize;

                    // Helper function to emit a blocklist hash
                    void EmitBlockListBlock()
                    {
                        blocklistHash.Position = 0;
                        using var blockhasher = HashFactory.CreateHasher(m_options.BlockHashAlgorithm);
                        var resulthash = Convert.ToBase64String(blockhasher.ComputeHash(blocklistHash));
                        var totalHashes = (lastBlocklist.BlocklistHashLength + m_options.Blocksize - 1) / m_options.Blocksize;
                        var hashesInLastBlock = totalHashes % hashesPerBlock;
                        var targetsize = lastBlocklist.Index >= totalHashes - 1
                            ? hashesInLastBlock * blockhashsize
                            : hashesPerBlock * blockhashsize;

                        if (resulthash == lastBlocklist.BlocklistHash && targetsize == blocklistHash.Length)
                        {
                            if (mbl.SetBlockRestored(resulthash, blocklistHash.Length, pendingVolume.VolumeID))
                                pendingVolume.AddBlock(resulthash, blocklistHash.ToArray(), 0, (int)blocklistHash.Length, CompressionHint.Default);
                        }
                        else
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "BlocklistHashMismatch", null, "Internal consistency issue: blocklist hash mismatch on {0} / {1}, expected {2} but got {3}", lastBlocklist.BlocklistHash, lastBlocklist.Index, lastBlocklist.Hash, resulthash);
                        }

                        blocklistHash.SetLength(0);
                    }

                    foreach (var blocklist in mbl.GetBlocklistHashes(hashesPerBlock))
                    {
                        lastBlocklist ??= blocklist;

                        if (lastBlocklist.BlocksetId != blocklist.BlocksetId || lastBlocklist.BlocklistHashIndex != blocklist.BlocklistHashIndex)
                            EmitBlockListBlock();

                        var data = Convert.FromBase64String(blocklist.Hash);
                        blocklistHash.Write(data, 0, data.Length);
                        lastBlocklist = blocklist;
                    }

                    // Handle any trailing blocklist hash
                    if (blocklistHash.Length > 0)
                        EmitBlockListBlock();

                    //Then we grab all remote volumes that have the missing blocks
                    await foreach (var (tmpfile, _, _, name) in backendManager.GetFilesOverlappedAsync(mbl.GetMissingBlockSources().ToList(), cancellationToken).ConfigureAwait(false))
                    {
                        try
                        {
                            var buffer = new byte[m_options.Blocksize];
                            using (tmpfile)
                            using (var f = new BlockVolumeReader(RestoreHandler.GetCompressionModule(name), tmpfile, m_options))
                                foreach (var b in f.Blocks)
                                    if (mbl.SetBlockRestored(b.Key, b.Value, pendingVolume.VolumeID))
                                        if (f.ReadBlock(b.Key, buffer) == b.Value)
                                            pendingVolume.AddBlock(b.Key, buffer, 0, (int)b.Value, CompressionHint.Default);
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteErrorMessage(LOGTAG, "RemoteFileAccessError", ex, "Failed to access remote file: {0}", name);
                        }
                    }
                }

                var missingBlocks = mbl.GetMissingBlockCount();
                var recoveredBlocks = originalMissingBlockCount - missingBlocks;
                if (recoveredBlocks == 0 || (m_options.DisablePartialDblockRecovery && missingBlocks > 0))
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "RepairMissingBlocks", "Repair cannot acquire {0} required blocks for volume {1}, which are required by the following filesets: ", missingBlocks, originalVolume.Name);
                    foreach (var f in mbl.GetFilesetsUsingMissingBlocks())
                        Logging.Log.WriteInformationMessage(LOGTAG, "AffectedFilesetName", f.Name);

                    var recoverymsg = string.Format("If you want to continue working with the database, you can use the \"{0}\" and \"{1}\" commands to purge the missing data from the database and the remote storage.", "list-broken-files", "purge-broken-files");
                    var logmsg = string.Format("Repair not possible, missing {0} blocks.\n" + recoverymsg, missingBlocks);

                    Logging.Log.WriteInformationMessage(LOGTAG, "RecoverySuggestion", null, logmsg);
                    throw new UserInformationException(logmsg, "RepairIsNotPossible");
                }
                else if (recoveredBlocks > 0)
                {
                    if (missingBlocks > 0)
                        Logging.Log.WriteWarningMessage(LOGTAG, "RepairMissingBlocks", null, "Repair acquired {0} blocks for volume {1}, but {2} blocks are still missing. If you want to continue working with the database, you can use the \"{3}\" and \"{4}\" commands to purge the missing data from the database and the remote storage.", recoveredBlocks, originalVolume.Name, missingBlocks, "list-broken-files", "purge-broken-files");

                    // If we cannot fully recover the blocks, we will not be able to delete the old files.
                    // They will instead be registered with duplicated blocks, so the purge-broken-files command
                    // can remove them cleanly later
                    if (missingBlocks == 0)
                    {
                        pendingVolume.VolumesToDelete.Add(originalVolume);
                        var oldIndexFiles = db.GetIndexFilesReferencingBlockFile(originalVolume.ID, rtr.Transaction).ToList();

                        // Find all index files that point to the old volume
                        foreach (var oldIndexFile in oldIndexFiles)
                        {
                            var oldVolume = db.GetRemoteVolume(oldIndexFile, rtr.Transaction);
                            if (oldVolume.State == RemoteVolumeState.Uploading || oldVolume.State == RemoteVolumeState.Uploaded || oldVolume.State == RemoteVolumeState.Verified)
                            {
                                var blockVolumesReferenced = db.GetBlockVolumesFromIndexName(oldIndexFile).ToList();
                                if (blockVolumesReferenced.Any(x => x.Name != originalVolume.Name))
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "IndexFileNotDeleted", null, "Index file {0} references multiple remote volumes, skipping", oldVolume.Name);
                                else
                                    pendingVolume.VolumesToDelete.Add(oldVolume);
                            }
                            else
                            {
                                Logging.Log.WriteVerboseMessage(LOGTAG, "IndexFileNotDeleted", null, "Index file {0} is not in a state to be deleted, skipping", oldVolume.Name);
                            }
                        }
                    }
                }
            }
        }

        public async Task RunRepairBrokenFilesets(IBackendManager backendManager)
        {
            if (!File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath), "DatabaseDoesNotExist");

            using (var db = new LocalRepairDatabase(m_options.Dbpath, m_options.SqlitePageCache))
            using (var tr = new ReusableTransaction(db))
            {
                var sets = db.GetFilesetsWithMissingFiles(null).ToList();
                if (sets.Count == 0)
                    return;

                Logging.Log.WriteInformationMessage(LOGTAG, "RepairingBrokenFilesets", "Repairing {0} broken filesets", sets.Count);
                var ix = 0;
                foreach (var entry in sets)
                {
                    ix++;
                    Logging.Log.WriteInformationMessage(LOGTAG, "RepairingBrokenFileset", "Repairing broken fileset {0} of {1}: {2}", ix, sets.Count, entry.Value);
                    var volume = db.GetRemoteVolumeFromFilesetID(entry.Key, tr.Transaction);
                    var parsed = VolumeBase.ParseFilename(volume.Name);
                    using var tmpfile = await backendManager.GetAsync(volume.Name, volume.Hash, volume.Size, CancellationToken.None).ConfigureAwait(false);
                    using var stream = new FileStream(tmpfile, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var compressor = DynamicLoader.CompressionLoader.GetModule(parsed.CompressionModule, stream, ArchiveMode.Read, m_options.RawOptions);
                    if (compressor == null)
                        throw new UserInformationException(string.Format("Failed to load compression module: {0}", parsed.CompressionModule), "FailedToLoadCompressionModule");

                    // Clear out the old fileset
                    db.DeleteFilesetEntries(entry.Key, tr.Transaction);
                    using (var rdb = new LocalRecreateDatabase(db, m_options))
                        RecreateDatabaseHandler.RecreateFilesetFromRemoteList(rdb, tr.Transaction, compressor, entry.Key, m_options, new FilterExpression());

                    tr.Commit("PostRepairFileset");
                }

            }
        }

        public void RunRepairCommon()
        {
            if (!File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath), "DatabaseDoesNotExist");

            m_result.OperationProgressUpdater.UpdateProgress(0);

            using (var db = new LocalRepairDatabase(m_options.Dbpath, m_options.SqlitePageCache))
            {
                Utility.UpdateOptionsFromDb(db, m_options);

                if (db.RepairInProgress || db.PartiallyRecreated)
                    Logging.Log.WriteWarningMessage(LOGTAG, "InProgressDatabase", null, "The database is marked as \"in-progress\" and may be incomplete.");

                db.FixDuplicateMetahash();
                db.FixDuplicateFileentries();
                db.FixDuplicateBlocklistHashes(m_options.Blocksize, m_options.BlockhashSize);
                db.FixMissingBlocklistHashes(m_options.BlockHashAlgorithm, m_options.Blocksize);
            }
        }
    }
}
