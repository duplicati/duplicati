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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
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

        public void Run(IBackendManager backendManager, IFilter filter)
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
            {
                RunRepairLocal(backendManager, filter);
                RunRepairCommon();
                m_result.EndTime = DateTime.UtcNow;
                return;
            }

            long knownRemotes = -1;
            try
            {
                using (var db = new LocalRepairDatabase(m_options.Dbpath))
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
                    var baseName = System.IO.Path.ChangeExtension(m_options.Dbpath, "backup");
                    var i = 0;
                    while (System.IO.File.Exists(baseName) && i++ < 1000)
                        baseName = System.IO.Path.ChangeExtension(m_options.Dbpath, "backup-" + i.ToString());

                    Logging.Log.WriteInformationMessage(LOGTAG, "RenamingDatabase", "Renaming existing db from {0} to {1}", m_options.Dbpath, baseName);
                    System.IO.File.Move(m_options.Dbpath, baseName);
                }

                RunRepairLocal(backendManager, filter);
                RunRepairCommon();
            }
            else
            {
                RunRepairCommon();
                RunRepairRemote(backendManager, m_result.TaskControl.ProgressToken).Await();
            }

            m_result.EndTime = DateTime.UtcNow;

        }

        public void RunRepairLocal(IBackendManager backendManager, Library.Utility.IFilter filter)
        {
            m_result.RecreateDatabaseResults = new RecreateDatabaseResults(m_result);
            using (new Logging.Timer(LOGTAG, "RecreateDbForRepair", "Recreate database for repair"))
            using (var f = m_options.Dryrun ? new Library.Utility.TempFile() : null)
            {
                if (f != null && System.IO.File.Exists(f))
                    System.IO.File.Delete(f);

                var filelistfilter = RestoreHandler.FilterNumberedFilelist(m_options.Time, m_options.Version);

                new RecreateDatabaseHandler(m_options, (RecreateDatabaseResults)m_result.RecreateDatabaseResults)
                    .Run(m_options.Dryrun ? (string)f : m_options.Dbpath, backendManager, filter, filelistfilter, null);
            }
        }

        public async Task RunRepairRemote(IBackendManager backendManager, CancellationToken cancellationToken)
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath), "RepairDatabaseFileDoesNotExist");

            m_result.OperationProgressUpdater.UpdateProgress(0);

            using (var db = new LocalRepairDatabase(m_options.Dbpath))
            {
                m_result.SetDatabase(db);
                Utility.UpdateOptionsFromDb(db, m_options);
                Utility.VerifyOptionsAndUpdateDatabase(db, m_options);

                if (db.PartiallyRecreated)
                    throw new UserInformationException("The database was only partially recreated. This database may be incomplete and the repair process is not allowed to alter remote files as that could result in data loss.", "DatabaseIsPartiallyRecreated");

                if (db.RepairInProgress)
                    throw new UserInformationException("The database was attempted repaired, but the repair did not complete. This database may be incomplete and the repair process is not allowed to alter remote files as that could result in data loss.", "DatabaseIsInRepairState");

                var tp = await FilelistProcessor.RemoteListAnalysis(backendManager, m_options, db, m_result.BackendWriter, null).ConfigureAwait(false);
                var buffer = new byte[m_options.Blocksize];
                var hashsize = HashFactory.HashSizeBytes(m_options.BlockHashAlgorithm);

                var progress = 0;
                var targetProgess = tp.ExtraVolumes.Count() + tp.MissingVolumes.Count() + tp.VerificationRequiredVolumes.Count();

                var mostRecentLocal = db.FilesetTimes.Select(x => x.Value).Append(DateTime.MinValue).Max();
                var mostRecentRemote = tp.ParsedVolumes.Select(x => x.Time).Append(DateTime.MinValue).Max();
                if (mostRecentLocal < DateTime.UnixEpoch)
                    throw new UserInformationException("The local database has no fileset times. Consider deleting the local database and run the repair operation again.", "LocalDatabaseHasNoFilesetTimes");
                if (mostRecentRemote > mostRecentLocal)
                    throw new UserInformationException($"The remote files are newer ({mostRecentRemote}) than the local database ({mostRecentLocal}), this is likely because the database is outdated. Consider deleting the local database and run the repair operation again.", "RemoteFilesNewerThanLocalDatabase");

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

                if (tp.ExtraVolumes.Any() || tp.MissingVolumes.Any() || tp.VerificationRequiredVolumes.Any())
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
                                        await backendManager.WaitForEmptyAsync(testdb, null, cancellationToken).ConfigureAwait(false);
                                        return;
                                    }

                                    progress++;
                                    m_result.OperationProgressUpdater.UpdateProgress((float)progress / targetProgess);

                                    KeyValuePair<string, IEnumerable<KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>>> res;
                                    (var tf, var hash, var size) = await backendManager.GetWithInfoAsync(n.Name, n.Hash, n.Size, cancellationToken).ConfigureAwait(false);
                                    using (tf)
                                        res = TestHandler.TestVolumeInternals(testdb, n, tf, m_options, 1);

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
                                    if (ex is System.Threading.ThreadAbortException)
                                        throw;
                                }
                        }
                    }

                    // TODO: It is actually possible to use the extra files if we parse them
                    foreach (var n in tp.ExtraVolumes)
                        try
                        {
                            if (!await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                            {
                                await backendManager.WaitForEmptyAsync(db, null, cancellationToken).ConfigureAwait(false);
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
                                            db.CheckBlocklistCorrect(ixb.Hash, ixb.Length, ixb.Blocklist, blocksize, hashsize);

                                        // Register the new index file and link it to the block files
                                        using (var tr = db.BeginTransaction())
                                        {
                                            var selfid = db.RegisterRemoteVolume(n.File.Name, RemoteVolumeType.Index, RemoteVolumeState.Uploading, size, new TimeSpan(0), tr);
                                            foreach (var rv in ifr.Volumes)
                                            {
                                                // Guard against unknown block files
                                                long id = db.GetRemoteVolumeID(rv.Filename, tr);
                                                if (id == -1)
                                                    Logging.Log.WriteWarningMessage(LOGTAG, "UnknownBlockFile", null, "Index file {0} references unknown block file: {1}", n.File.Name, rv.Filename);
                                                else
                                                    db.AddIndexBlockLink(selfid, id, tr);
                                            }
                                            tr.Commit();
                                        }
                                    }

                                    // All checks fine, we accept the new index file
                                    Logging.Log.WriteInformationMessage(LOGTAG, "AcceptNewIndexFile", "Accepting new index file {0}", n.File.Name);
                                    db.UpdateRemoteVolume(n.File.Name, RemoteVolumeState.Verified, size, hash);
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
                            if (ex is System.Threading.ThreadAbortException)
                                throw;
                        }

                    if (!m_options.RebuildMissingDblockFiles)
                    {
                        var missingDblocks = tp.MissingVolumes.Where(x => x.Type == RemoteVolumeType.Blocks).ToArray();
                        if (missingDblocks.Length > 0)
                            throw new UserInformationException($"The backup storage destination is missing data files. You can either enable `--rebuild-missing-dblock-files` or run the purge command to remove these files. The following files are missing: {string.Join(", ", missingDblocks.Select(x => x.Name))}", "MissingDblockFiles");
                    }

                    foreach (var n in tp.MissingVolumes)
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
                                var filesetId = db.GetFilesetIdFromRemotename(n.Name);

                                // We cannot wrap the FilesetVolumeWriter in a using statement here because a reference to it is
                                // retained in newEntry.
                                FilesetVolumeWriter volumeWriter = new FilesetVolumeWriter(m_options, DateTime.UtcNow);
                                newEntry = volumeWriter;
                                volumeWriter.SetRemoteFilename(n.Name);

                                db.WriteFileset(volumeWriter, filesetId, null);
                                DateTime filesetTime = db.FilesetTimes.First(x => x.Key == filesetId).Value;
                                volumeWriter.CreateFilesetFile(db.IsFilesetFullBackup(filesetTime));

                                volumeWriter.Close();
                                if (m_options.Dryrun)
                                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldReUploadFileset", "would re-upload fileset {0}, with size {1}, previous size {2}", n.Name, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(volumeWriter.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(n.Size));
                                else
                                {
                                    db.UpdateRemoteVolume(volumeWriter.RemoteFilename, RemoteVolumeState.Uploading, -1, null, null);
                                    await backendManager.PutAsync(volumeWriter, null, null, false, cancellationToken).ConfigureAwait(false);
                                }
                            }
                            else if (n.Type == RemoteVolumeType.Index)
                            {
                                IndexVolumeWriter w = new IndexVolumeWriter(m_options);
                                newEntry = w;
                                w.SetRemoteFilename(n.Name);

                                using (var h = HashFactory.CreateHasher(m_options.BlockHashAlgorithm))
                                {

                                    foreach (var blockvolume in db.GetBlockVolumesFromIndexName(n.Name))
                                    {
                                        w.StartVolume(blockvolume.Name);
                                        var volumeid = db.GetRemoteVolumeID(blockvolume.Name);

                                        foreach (var b in db.GetBlocks(volumeid))
                                            w.AddBlock(b.Hash, b.Size);

                                        w.FinishVolume(blockvolume.Hash, blockvolume.Size);

                                        if (m_options.IndexfilePolicy == Options.IndexFileStrategy.Full)
                                            foreach (var b in db.GetBlocklists(volumeid, m_options.Blocksize, hashsize))
                                            {
                                                var bh = Convert.ToBase64String(h.ComputeHash(b.Item2, 0, b.Item3));
                                                if (bh != b.Item1)
                                                    throw new Exception(string.Format("Internal consistency check failed, generated index block has wrong hash, {0} vs {1}", bh, b.Item1));

                                                w.WriteBlocklist(b.Item1, b.Item2, 0, b.Item3);
                                            }
                                    }
                                }

                                w.Close();

                                if (m_options.Dryrun)
                                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldReUploadIndexFile", "would re-upload index file {0}, with size {1}, previous size {2}", n.Name, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(w.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(n.Size));
                                else
                                {
                                    db.UpdateRemoteVolume(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null, null);
                                    await backendManager.PutAsync(w, null, null, false, cancellationToken).ConfigureAwait(false);
                                }
                            }
                            else if (n.Type == RemoteVolumeType.Blocks)
                            {
                                BlockVolumeWriter w = new BlockVolumeWriter(m_options);
                                newEntry = w;
                                w.SetRemoteFilename(n.Name);

                                using (var mbl = db.CreateBlockList(n.Name))
                                {
                                    //First we grab all known blocks from local files
                                    foreach (var block in mbl.GetSourceFilesWithBlocks(m_options.Blocksize))
                                    {
                                        var hash = block.Hash;
                                        var size = (int)block.Size;

                                        foreach (var source in block.Sources)
                                        {
                                            var file = source.File;
                                            var offset = source.Offset;

                                            try
                                            {
                                                if (System.IO.File.Exists(file))
                                                    using (var f = System.IO.File.OpenRead(file))
                                                    {
                                                        f.Position = offset;
                                                        if (size == Library.Utility.Utility.ForceStreamRead(f, buffer, size))
                                                        {
                                                            using (var blockhasher = HashFactory.CreateHasher(m_options.BlockHashAlgorithm))
                                                            {
                                                                var newhash = Convert.ToBase64String(blockhasher.ComputeHash(buffer, 0, size));
                                                                if (newhash == hash)
                                                                {
                                                                    if (mbl.SetBlockRestored(hash, size))
                                                                        w.AddBlock(hash, buffer, 0, size, Duplicati.Library.Interface.CompressionHint.Default);
                                                                    break;
                                                                }
                                                            }
                                                        }
                                                    }
                                            }
                                            catch (Exception ex)
                                            {
                                                Logging.Log.WriteErrorMessage(LOGTAG, "FileAccessError", ex, "Failed to access file: {0}", file);
                                            }
                                        }
                                    }

                                    //Then we grab all remote volumes that have the missing blocks
                                    await foreach (var (tmpfile, _, _, name) in backendManager.GetFilesOverlappedAsync(mbl.GetMissingBlockSources().ToList(), cancellationToken).ConfigureAwait(false))
                                    {
                                        try
                                        {
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
                                    var missingBlocks = mbl.GetMissingBlocks().Count();
                                    if (missingBlocks > 0)
                                    {
                                        Logging.Log.WriteInformationMessage(LOGTAG, "RepairMissingBlocks", "Repair cannot acquire {0} required blocks for volume {1}, which are required by the following filesets: ", missingBlocks, n.Name);
                                        foreach (var f in mbl.GetFilesetsUsingMissingBlocks())
                                            Logging.Log.WriteInformationMessage(LOGTAG, "AffectedFilesetName", f.Name);

                                        var recoverymsg = string.Format("If you want to continue working with the database, you can use the \"{0}\" and \"{1}\" commands to purge the missing data from the database and the remote storage.", "list-broken-files", "purge-broken-files");

                                        if (!m_options.Dryrun)
                                        {
                                            Logging.Log.WriteInformationMessage(LOGTAG, "RecoverySuggestion", "This may be fixed by deleting the filesets and running repair again");

                                            throw new UserInformationException(string.Format("Repair not possible, missing {0} blocks.\n" + recoverymsg, missingBlocks), "RepairIsNotPossible");
                                        }
                                        else
                                        {
                                            Logging.Log.WriteInformationMessage(LOGTAG, "RecoverySuggestion", recoverymsg);
                                        }
                                    }
                                    else
                                    {
                                        if (m_options.Dryrun)
                                            Logging.Log.WriteDryrunMessage(LOGTAG, "WouldReUploadBlockFile", "would re-upload block file {0}, with size {1}, previous size {2}", n.Name, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(w.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(n.Size));
                                        else
                                        {
                                            db.UpdateRemoteVolume(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null, null);
                                            await backendManager.PutAsync(w, null, null, false, cancellationToken).ConfigureAwait(false);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            if (newEntry != null)
                                try { newEntry.Dispose(); }
                                catch { }
                                finally { newEntry = null; }

                            Logging.Log.WriteErrorMessage(LOGTAG, "CleanupMissingFileError", ex, "Failed to perform cleanup for missing file: {0}, message: {1}", n.Name, ex.Message);

                            if (ex is System.Threading.ThreadAbortException)
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
                db.WriteResults();
            }
        }

        public void RunRepairCommon()
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath), "DatabaseDoesNotExist");

            m_result.OperationProgressUpdater.UpdateProgress(0);

            using (var db = new LocalRepairDatabase(m_options.Dbpath))
            {
                db.SetResult(m_result);

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
