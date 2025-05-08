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

                var mostRecentLocal = db.FilesetTimes.Select(x => x.Value.ToLocalTime()).Append(DateTime.MinValue).Max();
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

                                            db.CheckAllBlocksAreInVolume(rv.Filename, rv.Blocks);
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

                    if (!m_options.RebuildMissingDblockFiles)
                    {
                        var missingDblocks = tp.MissingVolumes.Where(x => x.Type == RemoteVolumeType.Blocks).ToArray();
                        if (missingDblocks.Length > 0)
                            throw new UserInformationException($"The backup storage destination is missing data files. You can either enable `--rebuild-missing-dblock-files` or run the purge command to remove these files. The following files are missing: {string.Join(", ", missingDblocks.Select(x => x.Name))}", "MissingDblockFiles");
                    }

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

                    foreach (var n in tp.MissingVolumes.OrderBy(x => x.Type switch
                    {
                        // Make sure we process the content first, then the fileset
                        // process index last, as it may have been deleted during block processing
                        RemoteVolumeType.Blocks => 0,
                        RemoteVolumeType.Files => 1,
                        RemoteVolumeType.Index => 2,
                        _ => 3
                    }))
                    {
                        IDisposable newEntry = null;

                        try
                        {
                            if (!await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                            {
                                await backendManager.WaitForEmptyAsync(db, null, cancellationToken).ConfigureAwait(false);
                                return;
                            }

                            progress++;
                            m_result.OperationProgressUpdater.UpdateProgress((float)progress / targetProgess);

                            if (n.Type == RemoteVolumeType.Files)
                            {
                                var timestamp = VolumeBase.ParseFilename(n.Name).Time;
                                var fileTime = FilesetVolumeWriter.ProbeUnusedFilenameName(db, rtr.Transaction, m_options, timestamp);
                                var volumeWriter = new FilesetVolumeWriter(m_options, fileTime);

                                newEntry = volumeWriter;
                                await RunRepairDlist(backendManager, db, rtr, volumeWriter, n, fileTime, cancellationToken).ConfigureAwait(false);
                            }
                            else if (n.Type == RemoteVolumeType.Index)
                            {
                                var w = new IndexVolumeWriter(m_options);
                                newEntry = w;

                                // Check if the index file has already been deleted, beause the dblock was recreated
                                var currentState = db.GetRemoteVolume(n.Name, rtr.Transaction).State;
                                if (currentState == RemoteVolumeState.Deleted || currentState == RemoteVolumeState.Temporary || currentState == RemoteVolumeState.Deleting)
                                    continue;

                                await RunRepairDindex(backendManager, db, rtr, w, n, cancellationToken).ConfigureAwait(false);
                            }
                            else if (n.Type == RemoteVolumeType.Blocks)
                            {
                                var w = new BlockVolumeWriter(m_options);
                                newEntry = w;
                                await RunRepairDblock(backendManager, db, rtr, w, n, cancellationToken).ConfigureAwait(false);
                            }
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

        private async Task RunRepairDlist(IBackendManager backendManager, LocalRepairDatabase db, ReusableTransaction rtr, FilesetVolumeWriter volumeWriter, RemoteVolumeEntry n, DateTime filesetTime, CancellationToken cancellationToken)
        {
            volumeWriter.VolumeID = db.RegisterRemoteVolume(volumeWriter.RemoteFilename, RemoteVolumeType.Files, RemoteVolumeState.Temporary, -1, TimeSpan.Zero, rtr.Transaction);
            (var prevFilesetId, var _, var isPrevFull) = db.GetFilesetFromRemotename(n.Name, rtr.Transaction);

            if (!string.IsNullOrEmpty(m_options.ControlFiles))
                foreach (var p in m_options.ControlFiles.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                    volumeWriter.AddControlFile(p, m_options.GetCompressionHintFromFilename(p));

            volumeWriter.CreateFilesetFile(isPrevFull);
            var newFilesetID = db.CreateFileset(volumeWriter.VolumeID, filesetTime, rtr.Transaction);
            db.LinkFilesetToVolume(newFilesetID, volumeWriter.VolumeID, rtr.Transaction);
            db.MoveFilesFromFileset(newFilesetID, prevFilesetId, rtr.Transaction);

            db.WriteFileset(volumeWriter, newFilesetID, rtr.Transaction);
            volumeWriter.Close();

            if (m_options.Dryrun)
                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldReUploadFileset", "would re-upload fileset {0}, with size {1}, previous size {2}", n.Name, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(volumeWriter.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(n.Size));
            else
            {
                db.UpdateRemoteVolume(volumeWriter.RemoteFilename, RemoteVolumeState.Uploading, -1, null, rtr.Transaction);
                db.UpdateRemoteVolume(n.Name, RemoteVolumeState.Deleting, n.Size, n.Hash, false, TimeSpan.FromHours(2), null, rtr.Transaction);
                await backendManager.FlushPendingMessagesAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);
                if (!m_options.Dryrun)
                    rtr.Commit("CommitPriorToFilesetUpload");
                await backendManager.PutAsync(volumeWriter, null, null, false, null, cancellationToken).ConfigureAwait(false);
            }

            await backendManager.WaitForEmptyAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);
            if (!m_options.Dryrun)
                rtr.Commit("CommitFilesetTransaction");
        }

        private async Task RunRepairDindex(IBackendManager backendManager, LocalRepairDatabase db, ReusableTransaction rtr, IndexVolumeWriter w, RemoteVolumeEntry n, CancellationToken cancellationToken)
        {
            w.VolumeID = db.RegisterRemoteVolume(w.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Uploading, -1, TimeSpan.Zero, rtr.Transaction);

            var blockvolumeids = new List<long>();
            using var h = HashFactory.CreateHasher(m_options.BlockHashAlgorithm);
            foreach (var blockvolume in db.GetBlockVolumesFromIndexName(n.Name))
            {
                w.StartVolume(blockvolume.Name);
                var volumeid = db.GetRemoteVolumeID(blockvolume.Name, rtr.Transaction);

                foreach (var b in db.GetBlocks(volumeid))
                    w.AddBlock(b.Hash, b.Size);

                w.FinishVolume(blockvolume.Hash, blockvolume.Size);
                blockvolumeids.Add(volumeid);

                if (m_options.IndexfilePolicy == Options.IndexFileStrategy.Full)
                    foreach (var b in db.GetBlocklists(volumeid, m_options.Blocksize, m_options.BlockhashSize))
                    {
                        var bh = Convert.ToBase64String(h.ComputeHash(b.Item2, 0, b.Item3));
                        if (bh != b.Item1)
                            throw new Exception(string.Format("Internal consistency check failed, generated index block has wrong hash, {0} vs {1}", bh, b.Item1));

                        w.WriteBlocklist(b.Item1, b.Item2, 0, b.Item3);
                    }
            }

            foreach (var blockvolumeid in blockvolumeids)
                db.AddIndexBlockLink(w.VolumeID, blockvolumeid, rtr.Transaction);

            w.Close();

            if (m_options.Dryrun)
                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldReUploadIndexFile", "would re-upload index file {0}, with size {1}, previous size {2}", n.Name, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(w.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(n.Size));
            else
            {
                db.UpdateRemoteVolume(n.Name, RemoteVolumeState.Deleting, n.Size, n.Hash, false, TimeSpan.FromHours(2), null, rtr.Transaction);
                await backendManager.FlushPendingMessagesAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);
                await backendManager.PutAsync(w, null, null, false, null, cancellationToken).ConfigureAwait(false);
            }

            await backendManager.WaitForEmptyAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);
            if (!m_options.Dryrun)
                rtr.Commit("CommitRepairTransaction");
        }

        private async Task RunRepairDblock(IBackendManager backendManager, LocalRepairDatabase db, ReusableTransaction rtr, BlockVolumeWriter w, RemoteVolumeEntry n, CancellationToken cancellationToken)
        {
            // TODO: Keep an open volume and append to that until we reach the size threshold,
            // similar to how the creation works. For now we just attempt to recreate the original volume

            // The dblock files are the most complex to recreate
            // as data can be either file contents, metadata or blocklist hashes
            // We attempt to recover all three source parts in the steps below
            using (var mbl = db.CreateBlockList(n.Name, rtr))
            {
                var originalMissingBlockCount = mbl.GetMissingBlockCount();

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
                        if (File.Exists(block.File))
                        {
                            using (var f = File.OpenRead(block.File))
                            {
                                f.Position = block.Offset;

                                var buffer = new byte[m_options.Blocksize];
                                if (size == Library.Utility.Utility.ForceStreamRead(f, buffer, size))
                                {
                                    using (var blockhasher = HashFactory.CreateHasher(m_options.BlockHashAlgorithm))
                                    {
                                        var newhash = Convert.ToBase64String(blockhasher.ComputeHash(buffer, 0, size));
                                        if (newhash == block.Hash)
                                        {
                                            // Found it, no need to look again
                                            lastRestoredHash = block.Hash;
                                            lastRestoredSize = block.Size;
                                            if (mbl.SetBlockRestored(block.Hash, block.Size))
                                                w.AddBlock(block.Hash, buffer, 0, size, CompressionHint.Default);
                                        }
                                        else
                                        {
                                            Logging.Log.WriteVerboseMessage(LOGTAG, "FileHashMismatch", null, "Block hash mismatch on {0}, expected {1} but got {2}", block.File, block.Hash, newhash);
                                        }
                                    }
                                }
                                else
                                {
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "FileLengthMismatch", null, "Block length mismatch on {0}, expected {1} but got {2}", block.File, size, f.Length);
                                }
                            }
                        }
                        else
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "FileNotFound", null, "File not found: {0}", block.File);
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
                        if (isFile || isDir)
                        {
                            using var snapshot = Snapshots.SnapshotUtility.CreateNoSnapshot([block.Path], true, true);
                            var entry = snapshot.GetFilesystemEntry(block.Path, isDir);
                            if (entry == null)
                            {
                                Logging.Log.WriteErrorMessage(LOGTAG, "FileAccessError", null, "Entry not found: {0}", block.Path);
                                continue;
                            }

                            var metadata = MetadataGenerator.GenerateMetadata(entry, entry.Attributes, m_options);
                            var metahash = Utility.WrapMetadata(metadata, m_options);
                            if (metahash.FileHash == block.Hash && metahash.Blob.Length == size)
                            {
                                // Found it, no need to look again
                                lastRestoredHash = block.Hash;
                                lastRestoredSize = block.Size;
                                if (mbl.SetBlockRestored(block.Hash, block.Size))
                                    w.AddBlock(block.Hash, metahash.Blob, 0, metahash.Blob.Length, CompressionHint.Default);
                            }
                            else
                            {
                                Logging.Log.WriteVerboseMessage(LOGTAG, "MetadataHashMismatch", null, "Metadata block hash mismatch on {0}, expected {1} but got {2}", block.Path, block.Hash, metahash.FileHash);
                            }
                        }
                        else
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "EntryNotFound", null, "Entry not found: {0}", block.Path);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteErrorMessage(LOGTAG, "FileAccessError", ex, "Failed to access file: {0}", block.Path);
                    }
                }

                // Then restore any blocklists that are missing
                using var blocklistHash = new MemoryStream();
                LocalRepairDatabase.BlocklistHashesEntry lastBlocklist = null;
                var blockhashsize = m_options.BlockhashSize;
                var hashesPerBlock = m_options.Blocksize / m_options.BlockhashSize;

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
                        if (mbl.SetBlockRestored(resulthash, blocklistHash.Length))
                            w.AddBlock(resulthash, blocklistHash.ToArray(), 0, (int)blocklistHash.Length, CompressionHint.Default);
                    }
                    else
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "BlocklistHashMismatch", null, "Blocklist hash mismatch on {0} / {1}, expected {2} but got {3}", lastBlocklist.BlocklistHash, lastBlocklist.Index, lastBlocklist.Hash, resulthash);
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
                                if (mbl.SetBlockRestored(b.Key, b.Value))
                                    if (f.ReadBlock(b.Key, buffer) == b.Value)
                                        w.AddBlock(b.Key, buffer, 0, (int)b.Value, CompressionHint.Default);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteErrorMessage(LOGTAG, "RemoteFileAccessError", ex, "Failed to access remote file: {0}", name);
                    }
                }

                // If we managed to recover all blocks, NICE!
                var missingBlocks = mbl.GetMissingBlockCount();
                if (missingBlocks > 0)
                {
                    // TODO: If we managed to get ANY blocks recovered, we can register a new volume
                    // and move existing blocks to duplicate, so a later purge has less negative impact

                    Logging.Log.WriteInformationMessage(LOGTAG, "RepairMissingBlocks", "Repair cannot acquire {0} required blocks for volume {1}, which are required by the following filesets: ", missingBlocks, n.Name);
                    foreach (var f in mbl.GetFilesetsUsingMissingBlocks())
                        Logging.Log.WriteInformationMessage(LOGTAG, "AffectedFilesetName", f.Name);

                    var recoverymsg = string.Format("If you want to continue working with the database, you can use the \"{0}\" and \"{1}\" commands to purge the missing data from the database and the remote storage.", "list-broken-files", "purge-broken-files");
                    var logmsg = string.Format("Repair not possible, missing {0} blocks.\n" + recoverymsg, missingBlocks);

                    Logging.Log.WriteInformationMessage(LOGTAG, "RecoverySuggestion", null, logmsg);
                    if (!m_options.Dryrun)
                        throw new UserInformationException(logmsg, "RepairIsNotPossible");
                }
                else
                {
                    w.VolumeID = db.RegisterRemoteVolume(w.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Uploading, -1, TimeSpan.Zero, rtr.Transaction);

                    var fixedBlockCount = mbl.MoveBlocksToNewVolume(w.VolumeID, n.ID, rtr.Transaction);
                    if (fixedBlockCount != originalMissingBlockCount)
                        throw new UserInformationException(string.Format("Failed to move {0} blocks to new volume {1}, only moved {2}", originalMissingBlockCount, w.RemoteFilename, fixedBlockCount), "FailedToMoveBlocks");

                    // Create a new index file that points to the new volume
                    IndexVolumeWriter newvolindex = null;
                    Action indexVolumeFinished = null;
                    if (m_options.IndexfilePolicy != Options.IndexFileStrategy.None)
                    {
                        newvolindex = new IndexVolumeWriter(m_options);
                        newvolindex.VolumeID = db.RegisterRemoteVolume(newvolindex.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary, rtr.Transaction);
                        newvolindex.StartVolume(w.RemoteFilename);
                        foreach (var b in db.GetBlocks(w.VolumeID))
                            newvolindex.AddBlock(b.Hash, b.Size);

                        db.AddIndexBlockLink(newvolindex.VolumeID, w.VolumeID, rtr.Transaction);
                        if (m_options.IndexfilePolicy == Options.IndexFileStrategy.Full)
                            indexVolumeFinished = () =>
                            {
                                foreach (var blocklist in db.GetBlocklists(w.VolumeID, m_options.Blocksize, m_options.BlockhashSize))
                                    newvolindex.WriteBlocklist(blocklist.Item1, blocklist.Item2, 0, blocklist.Item3);
                            };
                    }

                    var oldIndexFiles = db.GetIndexFilesReferencingBlockFile(n.ID, rtr.Transaction).ToList();
                    var toDelete = new List<RemoteVolumeEntry>();
                    // Find all index files that point to the old volume
                    foreach (var oldIndexFile in oldIndexFiles)
                    {
                        var oldVolume = db.GetRemoteVolume(oldIndexFile, rtr.Transaction);
                        if (oldVolume.State == RemoteVolumeState.Uploading || oldVolume.State == RemoteVolumeState.Uploaded || oldVolume.State == RemoteVolumeState.Verified)
                        {
                            var blockVolumesReferenced = db.GetBlockVolumesFromIndexName(oldIndexFile).ToList();
                            if (blockVolumesReferenced.Any(x => x.Name != n.Name))
                                Logging.Log.WriteVerboseMessage(LOGTAG, "IndexFileNotDeleted", null, "Index file {0} references multiple remote volumes, skipping", oldVolume.Name);
                            else
                                toDelete.Add(oldVolume);
                        }
                        else
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "IndexFileNotDeleted", null, "Index file {0} is not in a state to be deleted, skipping", oldVolume.Name);
                        }
                    }

                    // All information is in place, we can now upload the new volume
                    await backendManager.FlushPendingMessagesAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);
                    if (m_options.Dryrun)
                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldReplaceBlockFile", "would replace block file {0} with {1}", n.Name, w.RemoteFilename);
                    else
                    {
                        rtr.Commit("PostRepairPreUploadBlockVolume");
                        await backendManager.PutAsync(w, newvolindex, indexVolumeFinished, false, null, cancellationToken).ConfigureAwait(false);
                    }

                    // Flush the database as we have a new volume uploaded
                    await backendManager.WaitForEmptyAsync(db, rtr.Transaction, cancellationToken).ConfigureAwait(false);

                    // Prepare for deleting the old stuff
                    db.UpdateRemoteVolume(n.Name, RemoteVolumeState.Deleting, n.Size, n.Hash, false, TimeSpan.FromHours(2), null, rtr.Transaction);
                    foreach (var vol in toDelete)
                        db.UpdateRemoteVolume(vol.Name, RemoteVolumeState.Deleting, vol.Size, vol.Hash, false, TimeSpan.FromHours(2), null, rtr.Transaction);

                    // Persist desired state prior to deleting the old files
                    if (!m_options.Dryrun)
                        rtr.Commit("PostRepairBlockVolume");

                    // Delete the old files
                    foreach (var vol in toDelete)
                        if (m_options.Dryrun)
                            Logging.Log.WriteDryrunMessage(LOGTAG, "WouldDeleteIndexFile", "would delete index file {0}", vol.Name);
                        else
                            await backendManager.DeleteAsync(vol.Name, vol.Size, false, cancellationToken).ConfigureAwait(false);
                }

                // All done, the new dblock is now in place
                await backendManager.WaitForEmptyAsync(db, null, cancellationToken).ConfigureAwait(false);
                if (!m_options.Dryrun)
                    rtr.Commit("PostRepairBlockVolume");
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
