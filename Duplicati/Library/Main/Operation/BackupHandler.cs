#region Disclaimer / License

// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 

#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Interface;
using Duplicati.Library.Snapshots;

namespace Duplicati.Library.Main.Operation
{
    internal class BackupHandler : IDisposable
    {    
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<BackupHandler>();

        private readonly Options m_options;
        private readonly string m_backendurl;

        private byte[] m_blockbuffer;
        private byte[] m_blocklistbuffer;
        private System.Security.Cryptography.HashAlgorithm m_blockhasher;
        private System.Security.Cryptography.HashAlgorithm m_filehasher;

        private LocalBackupDatabase m_database;
        private System.Data.IDbTransaction m_transaction;
        private BlockVolumeWriter m_blockvolume;
        private IndexVolumeWriter m_indexvolume;

        // ReSharper disable once InconsistentNaming
        private readonly IMetahash EMPTY_METADATA;
        
        private Library.Utility.IFilter m_filter;
        private Library.Utility.IFilter m_sourceFilter;

        private readonly BackupResults m_result;

        // To better record what happens with the backend, we flush the log messages regularly
        // We cannot flush immediately, because that would mess up the transaction, as the uploader
        // is on another thread
        // ReSharper disable once InconsistentNaming
        private readonly TimeSpan FLUSH_TIMESPAN = TimeSpan.FromSeconds(10);
        private DateTime m_backendLogFlushTimer;
        
        // Speed up things by caching these
        private readonly FileAttributes m_attributeFilter;
        private readonly Options.SymlinkStrategy m_symlinkPolicy;
        private int m_blocksize;
        private long m_maxmetadatasize;
        private long m_prevFilesetId;

        public BackupHandler(string backendurl, Options options, BackupResults results)
        {
            EMPTY_METADATA = Utility.WrapMetadata(new Dictionary<string, string>(), options);
            
            m_options = options;
            m_result = results;
            m_backendurl = backendurl;

            m_attributeFilter = m_options.FileAttributeFilter;
            m_symlinkPolicy = m_options.SymlinkPolicy;
                
            if (options.AllowPassphraseChange)
                throw new UserInformationException(Strings.Common.PassphraseChangeUnsupported, "PassphraseChangeUnsupported");
        }


        public static ISnapshotService GetSnapshot(IEnumerable<string> sources, Options options)
        {
            try
            {
                if (options.SnapShotStrategy != Options.OptimizationStrategy.Off)
                    return SnapshotUtility.CreateSnapshot(sources, options.RawOptions);
            }
            catch (Exception ex)
            {
                if (options.SnapShotStrategy == Options.OptimizationStrategy.Required)
                    throw;

                if (options.SnapShotStrategy == Options.OptimizationStrategy.On)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "SnapshotFailed", ex,
                        Strings.Common.SnapshotFailedError(ex.ToString()));
                }
            }

            return Library.Utility.Utility.IsClientLinux ? new NoSnapshotLinux() : (ISnapshotService) new NoSnapshotWindows();
        }

        private void CountFilesThread(object state)
        {
            // ReSharper disable once InconsistentNaming
            var COUNTER_LOGTAG = LOGTAG + ".Counter";
            var typedState = (Tuple<ISnapshotService, string[]>) state;
            var snapshot = typedState.Item1;
            var sources = typedState.Item2;
            var count = 0L;
            var size = 0L;
            var followSymlinks = m_options.SymlinkPolicy != Options.SymlinkStrategy.Follow;
            
            foreach(var path in new FilterHandler(snapshot, m_attributeFilter, m_sourceFilter, m_filter, m_symlinkPolicy, m_options.HardlinkPolicy).EnumerateFilesAndFolders(sources))
            {
                var fa = SafeGetFileAttributes(path, snapshot, COUNTER_LOGTAG);

                if (followSymlinks && snapshot.IsSymlink(path, fa))
                    continue;
                if (fa.HasFlag(FileAttributes.Directory))
                    continue;

                count++;
                
                try { size += snapshot.GetFileSize(path); }
                catch (Exception ex) { Logging.Log.WriteVerboseMessage(COUNTER_LOGTAG, "SizeReadFailed", "Failed to read length of file {0}: {1}", path, ex.Message); }
                
                m_result.OperationProgressUpdater.UpdatefileCount(count, size, false);                    
            }
            
            m_result.OperationProgressUpdater.UpdatefileCount(count, size, true);
            
        }

        private void UploadSyntheticFilelist(BackendManager backend) 
        {
            var incompleteFilesets = m_database.GetIncompleteFilesets(null).OrderBy(x => x.Value).ToList();

            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PreviousBackupFinalize);
            Logging.Log.WriteInformationMessage(LOGTAG, "PreviousBackupFilelistUpload", "Uploading filelist from previous interrupted backup");
            using(var trn = m_database.BeginTransaction())
            {
                var incompleteSet = incompleteFilesets.Last();
                var badIds = from n in incompleteFilesets select n.Key;

                var prevs = (from n in m_database.FilesetTimes 
                    where 
                    n.Key < incompleteSet.Key
                    &&
                    !badIds.Contains(n.Key)
                    orderby n.Key                                                
                    select n.Key).ToArray();

                var prevId = prevs.Length == 0 ? -1 : prevs.Last();

                FilesetVolumeWriter fsw = null;
                try
                {
                    var s = 1;
                    var fileTime = incompleteSet.Value + TimeSpan.FromSeconds(s);

                    // Probe for an unused filename
                    while (s < 60)
                    {
                        var id = m_database.GetRemoteVolumeID(VolumeBase.GenerateFilename(RemoteVolumeType.Files, m_options, null, fileTime));
                        if (id < 0)
                            break;

                        fileTime = incompleteSet.Value + TimeSpan.FromSeconds(++s);
                    }

                    fsw = new FilesetVolumeWriter(m_options, fileTime);
                    fsw.VolumeID = m_database.RegisterRemoteVolume(fsw.RemoteFilename, RemoteVolumeType.Files, RemoteVolumeState.Temporary, m_transaction);

                    if (!string.IsNullOrEmpty(m_options.ControlFiles))
                        foreach(var p in m_options.ControlFiles.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                            fsw.AddControlFile(p, m_options.GetCompressionHintFromFilename(p));

                    var newFilesetId = m_database.CreateFileset(fsw.VolumeID, fileTime, trn);
                    m_database.LinkFilesetToVolume(newFilesetId, fsw.VolumeID, trn);
                    m_database.AppendFilesFromPreviousSet(trn, null, newFilesetId, prevId, fileTime);

                    m_database.WriteFileset(fsw, trn, newFilesetId);

                    if (m_options.Dryrun)
                    {
                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldUploadFileset", "Would upload fileset: {0}, size: {1}", fsw.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(fsw.LocalFilename).Length));
                    }
                    else
                    {
                        m_database.UpdateRemoteVolume(fsw.RemoteFilename, RemoteVolumeState.Uploading, -1, null, trn);

                        using(new Logging.Timer(LOGTAG, "CommitUpdateFilelistVolume", "CommitUpdateFilelistVolume"))
                            trn.Commit();

                        backend.Put(fsw);
                        fsw = null;
                    }
                }
                finally
                {
                    if (fsw != null)
                    {
                        try { fsw.Dispose(); }
                        catch
                        {
                            // ignored
                        }
                    }
                }                          
            }
        }

        private void RecreateMissingIndexFiles(BackendManager backend)
        {
            if (m_options.IndexfilePolicy != Options.IndexFileStrategy.None)
            {
                var blockhasher = Library.Utility.HashAlgorithmHelper.Create(m_options.BlockHashAlgorithm);
                var hashsize = blockhasher.HashSize / 8;

                foreach (var blockfile in m_database.GetMissingIndexFiles())
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "RecreateMissingIndexFile", "Re-creating missing index file for {0}", blockfile);
                    var w = new IndexVolumeWriter(m_options);
                    w.VolumeID = m_database.RegisterRemoteVolume(w.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary, null);

                    var blockvolume = m_database.GetRemoteVolumeFromName(blockfile);
                    w.StartVolume(blockvolume.Name);
                    var volumeid = m_database.GetRemoteVolumeID(blockvolume.Name);

                    foreach (var b in m_database.GetBlocks(volumeid))
                        w.AddBlock(b.Hash, b.Size);

                    w.FinishVolume(blockvolume.Hash, blockvolume.Size);

                    if (m_options.IndexfilePolicy == Options.IndexFileStrategy.Full)
                        foreach (var b in m_database.GetBlocklists(volumeid, m_options.Blocksize, hashsize))
                            w.WriteBlocklist(b.Item1, b.Item2, 0, b.Item3);

                    w.Close();

                    m_database.AddIndexBlockLink(w.VolumeID, volumeid, null);

                    if (m_options.Dryrun)
                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldUpdateIndexFile", "Would upload new index file {0}, with size {1}, previous size {2}", w.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(w.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(w.Filesize));
                    else
                    {
                        m_database.UpdateRemoteVolume(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
                        backend.Put(w);
                    }
                }
            }
        }

        private void PreBackupVerify(BackendManager backend, string protectedfile)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PreBackupVerify);
            using(new Logging.Timer(LOGTAG, "PreBackupVerify", "PreBackupVerify"))
            {
                try
                {
                    if (m_options.NoBackendverification)
                    {
                        FilelistProcessor.VerifyLocalList(backend, m_options, m_database, m_result.BackendWriter);
                        UpdateStorageStatsFromDatabase();
                    }
                    else
                        FilelistProcessor.VerifyRemoteList(backend, m_options, m_database, m_result.BackendWriter, protectedfile);
                }
                catch (Exception ex)
                {
                    if (m_options.AutoCleanup)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "BackendVerifyFailedAttemptingCleanup", ex, "Backend verification failed, attempting automatic cleanup");
                        m_result.RepairResults = new RepairResults(m_result);
                        new RepairHandler(backend.BackendUrl, m_options, (RepairResults)m_result.RepairResults).Run();

                        Logging.Log.WriteInformationMessage(LOGTAG, "BackendCleanupFinished", "Backend cleanup finished, retrying verification");
                        FilelistProcessor.VerifyRemoteList(backend, m_options, m_database, m_result.BackendWriter);
                    }
                    else
                        throw;
                }
            }
        }

        /// <summary>
        /// Perform actual backup operation
        /// </summary>
        /// <param name="snapshot">Snapshot service to use</param>
        /// <param name="backend">Storage back-end</param>
        /// <param name="sources">Sources to back up</param>
        private void RunMainOperation(ISnapshotService snapshot, BackendManager backend, IEnumerable<string> sources)
        {
            var filterhandler = new FilterHandler(snapshot, m_attributeFilter, m_sourceFilter, m_filter, m_symlinkPolicy, m_options.HardlinkPolicy);

            using(new Logging.Timer(LOGTAG, "BackupMainOperation", "BackupMainOperation"))
            {
                if (m_options.ChangedFilelist != null && m_options.ChangedFilelist.Any())
                {
                    BackupUsingChangedFileList(snapshot, backend, m_options.ChangedFilelist, filterhandler);
                }
                else if (m_options.UsnStrategy != Options.OptimizationStrategy.Off)
                {
                    BackupUsingChangeJournal(snapshot, backend, sources, filterhandler);
                }
                else
                {
                    BackupUsingFullScan(snapshot, backend, sources, filterhandler);
                }

                m_result.OperationProgressUpdater.UpdatefileCount(m_result.ExaminedFiles, m_result.SizeOfExaminedFiles, true);
            }
        }

        /// <summary>
        /// Performs backup operation using supplied changed file list
        /// </summary>
        /// <param name="snapshot">Snapshot service to use</param>
        /// <param name="backend">Storage back-end</param>
        /// <param name="changedFileList">List of changed file / folders</param>
        /// <param name="filterhandler">Exclusion filter</param>
        private void BackupUsingChangedFileList(ISnapshotService snapshot, BackendManager backend, IReadOnlyCollection<string> changedFileList, FilterHandler filterhandler)
        {
            var existingSources = changedFileList.Where(snapshot.PathExists);

            Logging.Log.WriteVerboseMessage(LOGTAG, "UsingChangeList",
                "Processing supplied change list instead of enumerating filesystem");
            m_result.OperationProgressUpdater.UpdatefileCount(changedFileList.Count, 0, true);

            foreach (var p in filterhandler.Mixin(existingSources))
            {
                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "StopBackup", "Stopping backup operation on request");
                    break;
                }

                try
                {
                    HandleFilesystemEntry(snapshot, backend, p, snapshot.GetAttributes(p));
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "FailedToProcessEntry", ex,
                        "Failed to process element: {0}, message: {1}", p, ex.Message);
                }
            }

            m_database.AppendFilesFromPreviousSet(m_transaction, m_options.DeletedFilelist);
        }

        /// <summary>
        /// Performs backup operation by scanning supplied source list for changes
        /// </summary>
        /// <param name="snapshot">Snapshot service to use</param>
        /// <param name="backend">Storage back-end</param>
        /// <param name="sources">Sources to back up</param>
        /// <param name="filterhandler">Exclusion filter</param>
        private void BackupUsingFullScan(ISnapshotService snapshot, BackendManager backend, IEnumerable<string> sources,
            FilterHandler filterhandler)
        {
            // [DG] I believe that we need to sanitize the sources to ensure that they exist in the *snapshot*.
            // Controller.ExpandInputSources() only verifies if sources exist in the non-snapshot filesystem, 
            // which may or may not correspond to their state in the snapshot.
            var existingSources = sources.Where(snapshot.PathExists);

            foreach (var path in filterhandler.EnumerateFilesAndFolders(existingSources))
            {
                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "StopBackup",
                        "Stopping backup operation on request");
                    break;
                }

                var fa = SafeGetFileAttributes(path, snapshot, LOGTAG);
                HandleFilesystemEntry(snapshot, backend, path, fa);
            }
        }

        /// <summary>
        /// Performs backup operation using USN Journal
        /// </summary>
        /// <param name="snapshot">Snapshot service to use</param>
        /// <param name="backend">Storage back-end</param>
        /// <param name="sources">Sources to back up</param>
        /// <param name="filterhandler">Exclusion filter</param>
        private void BackupUsingChangeJournal(ISnapshotService snapshot, BackendManager backend, IEnumerable<string> sources, FilterHandler filterhandler)
        {
            try
            {
                var journalService = new UsnJournalService(UsnErrorHandler, () =>
                {
                    if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                        throw new OperationCanceledException();
                });

                // obtain journal data from previous fileset
                var journalData = m_database.GetChangeJournalData(m_prevFilesetId);

                // get filter identifying current source filter / sources configuration
                // ReSharper disable once PossibleMultipleEnumeration
                var configHash =  m_filter.GetFilterHash() + Library.Utility.MD5HashHelper.GetHashString(Library.Utility.MD5HashHelper.GetHash(sources));

                // filter sources using USN journal, to obtain a sub-set of files / folders that
                // may have been modified
                // ReSharper disable once PossibleMultipleEnumeration
                var modifiedSources = journalService.FilterSources(sources, configHash, journalData);

                // scan modified sources for modifications
                if (modifiedSources.Files.Any() || modifiedSources.Folders.Any())
                {
                    // the modified sources may contain non-existent files / folders, which need to 
                    // be removed before trying to enumerate them
                    var existingModifiedSources = modifiedSources.Files.Where(snapshot.FileExists)
                        .Concat(modifiedSources.Folders.Where(snapshot.FolderExists));
            
                    foreach (var path in filterhandler.EnumerateFilesAndFolders(existingModifiedSources))
                    {
                        if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                            throw new OperationCanceledException();

                        var fa = SafeGetFileAttributes(path, snapshot, LOGTAG);
                        HandleFilesystemEntry(snapshot, backend, path, fa);
                    }
                    
                    // append files from previous fileset, unless part of modifiedSources, which we've just scanned
                    //TODO: we also need to check the original <c>sources</c> and exclude if not contained in those!
                    m_database.AppendFilesFromPreviousSetWithPredicate(m_transaction, path =>
                    {
                        if (modifiedSources.Files.Contains(path))
                            return true; // do not append from previous set, already scanned

                        foreach (var folder in modifiedSources.Folders)
                        {
                            if (path.Equals(folder, Library.Utility.Utility.ClientFilenameStringComparision))
                                return true; // do not append from previous set, already scanned

                            if (Library.Utility.Utility.IsPathBelowFolder(path, folder))
                                return true; // do not append from previous set, already scanned
                        }

                        return false; // append from previous set
                    });
                }
                else
                {
                    // add full previous fileset, as nothing has changed
                    m_database.AppendFilesFromPreviousSet(m_transaction);
                }

                if (modifiedSources.JournalData.Any())
                {
                    // always record change journal data for current fileset (entry may be dropped later if nothing is uploaded)
                    m_database.CreateChangeJournalData(modifiedSources.JournalData, m_transaction);

                    // update the previous fileset's change journal entry to resume at this point in case nothing was backed up
                    m_database.UpdateChangeJournalData(modifiedSources.JournalData, m_prevFilesetId, m_transaction);
                }
            }
            catch (OperationCanceledException)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "StopBackup", "Stopping backup operation on request");
            }
        }

        /// <summary>
        /// Error callback handler
        /// </summary>
        /// <param name="volume">Volume name</param>
        /// <param name="path">Not used</param>
        /// <param name="ex">Exception message</param>
        private void UsnErrorHandler(string volume, string path, Exception ex)
        {
            if (m_options.UsnStrategy == Options.OptimizationStrategy.Auto)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "FailedToUseChangeJournal", "Failed to use change journal for volume \"{0}\": {1}", volume, ex.Message);
            }
            else if (m_options.UsnStrategy == Options.OptimizationStrategy.On)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "FailedToUseChangeJournal", ex, "Failed to use change journal for volume \"{0}\": {1}", volume, ex.Message);
            }
            else
                throw ex;
        }

        private long FinalizeRemoteVolumes(BackendManager backend)
        {
            var lastVolumeSize = -1L;
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Finalize);
            using(new Logging.Timer(LOGTAG, "FinalizeRemoteVolumes", "FinalizeRemoteVolumes"))
            {
                if (m_blockvolume != null && m_blockvolume.SourceSize > 0)
                {
                    lastVolumeSize = m_blockvolume.SourceSize;

                    if (m_options.Dryrun)
                    {
                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldUploadBlock", "Would upload block volume: {0}, size: {1}", m_blockvolume.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(m_blockvolume.LocalFilename).Length));
                        if (m_indexvolume != null)
                        {
                            m_blockvolume.Close();
                            UpdateIndexVolume();
                            m_indexvolume.FinishVolume(Library.Utility.Utility.CalculateHash(m_blockvolume.LocalFilename), new FileInfo(m_blockvolume.LocalFilename).Length);
                            Logging.Log.WriteDryrunMessage(LOGTAG, "WouldUploadIndex", "Would upload index volume: {0}, size: {1}", m_indexvolume.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(m_indexvolume.LocalFilename).Length));
                        }

                        m_blockvolume.Dispose();
                        m_blockvolume = null;
                        m_indexvolume?.Dispose();
                        m_indexvolume = null;
                    }
                    else
                    {
                        m_database.UpdateRemoteVolume(m_blockvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);
                        m_blockvolume.Close();
                        UpdateIndexVolume();

                        using(new Logging.Timer(LOGTAG, "CommitUpdateRemoteVolume", "CommitUpdateRemoteVolume"))
                            m_transaction.Commit();
                        m_transaction = m_database.BeginTransaction();

                        backend.Put(m_blockvolume, m_indexvolume);

                        using(new Logging.Timer(LOGTAG, "CommitUpdateRemoteVolume", "CommitUpdateRemoteVolume"))
                            m_transaction.Commit();
                        m_transaction = m_database.BeginTransaction();

                        m_blockvolume = null;
                        m_indexvolume = null;
                    }
                }
            }

            return lastVolumeSize;
        }

        private void UploadRealFileList(BackendManager backend, FilesetVolumeWriter filesetvolume)
        {
            var changeCount = 
                m_result.AddedFiles + m_result.ModifiedFiles + m_result.DeletedFiles +
                m_result.AddedFolders + m_result.ModifiedFolders + m_result.DeletedFolders +
                m_result.AddedSymlinks + m_result.ModifiedSymlinks + m_result.DeletedSymlinks;

            //Changes in the filelist triggers a filelist upload
            if (m_options.UploadUnchangedBackups || changeCount > 0)
            {
                using(new Logging.Timer(LOGTAG, "UploadNewFileset", "Uploading a new fileset"))
                {
                    if (!string.IsNullOrEmpty(m_options.ControlFiles))
                        foreach(var p in m_options.ControlFiles.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                            filesetvolume.AddControlFile(p, m_options.GetCompressionHintFromFilename(p));

                    m_database.WriteFileset(filesetvolume, m_transaction);
                    filesetvolume.Close();

                    if (m_options.Dryrun)
                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldUploadFileset", "Would upload fileset volume: {0}, size: {1}", filesetvolume.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(filesetvolume.LocalFilename).Length));
                    else
                    {
                        m_database.UpdateRemoteVolume(filesetvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);

                        using(new Logging.Timer(LOGTAG, "CommitUpdateRemoteVolume", "CommitUpdateRemoteVolume"))
                            m_transaction.Commit();
                        m_transaction = m_database.BeginTransaction();

                        backend.Put(filesetvolume);

                        using(new Logging.Timer(LOGTAG, "CommitUpdateRemoteVolume", "CommitUpdateRemoteVolume"))
                            m_transaction.Commit();
                        m_transaction = m_database.BeginTransaction();

                    }
                }
            }
            else
            {
                Logging.Log.WriteVerboseMessage(LOGTAG, "RemovingLeftoverTempFile", "removing temp files, as no data needs to be uploaded");
                m_database.RemoveRemoteVolume(filesetvolume.RemoteFilename, m_transaction);
            }
        }

        private void CompactIfRequired(BackendManager backend, long lastVolumeSize)
        {
            var currentIsSmall = lastVolumeSize != -1 && lastVolumeSize <= m_options.SmallFileSize;

            if (m_options.KeepTime.Ticks > 0 || m_options.KeepVersions != 0 || m_options.RetentionPolicy.Count > 0)
            {
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Delete);
                m_result.DeleteResults = new DeleteResults(m_result);
                using(var db = new LocalDeleteDatabase(m_database))
                    new DeleteHandler(backend.BackendUrl, m_options, (DeleteResults)m_result.DeleteResults).DoRun(db, ref m_transaction, true, currentIsSmall, backend);

            }
            else if (currentIsSmall && !m_options.NoAutoCompact)
            {
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Compact);
                m_result.CompactResults = new CompactResults(m_result);
                using(var db = new LocalDeleteDatabase(m_database))
                    new CompactHandler(backend.BackendUrl, m_options, (CompactResults)m_result.CompactResults).DoCompact(db, true, ref m_transaction, backend);
            }
        }

        private void PostBackupVerification()
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PostBackupVerify);
            using(var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, m_database))
            {
                using(new Logging.Timer(LOGTAG, "AfterBackupVerify", "AfterBackupVerify"))
                    FilelistProcessor.VerifyRemoteList(backend, m_options, m_database, m_result.BackendWriter);
                backend.WaitForComplete(m_database, null);
            }

            if (m_options.BackupTestSampleCount > 0 && m_database.GetRemoteVolumes().Any())
            {
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PostBackupTest);
                m_result.TestResults = new TestResults(m_result);

                using(var testdb = new LocalTestDatabase(m_database))
                using(var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, testdb))
                    new TestHandler(m_backendurl, m_options, (TestResults)m_result.TestResults)
                        .DoRun(m_options.BackupTestSampleCount, testdb, backend);
            }
        }

        /// <summary>
        /// Handler for computing backend statistics, without relying on a remote folder listing
        /// </summary>
        private void UpdateStorageStatsFromDatabase()
        {
            if (m_result.BackendWriter != null)
            {
                m_result.BackendWriter.KnownFileCount = m_database.GetRemoteVolumes().Count();
                m_result.BackendWriter.KnownFileSize = m_database.GetRemoteVolumes().Select(x => Math.Max(0, x.Size)).Sum();

                m_result.BackendWriter.UnknownFileCount = 0;
                m_result.BackendWriter.UnknownFileSize = 0;

                m_result.BackendWriter.BackupListCount = m_database.FilesetTimes.Count();
                m_result.BackendWriter.LastBackupDate = m_database.FilesetTimes.FirstOrDefault().Value.ToLocalTime();

                // TODO: If we have a BackendManager, we should query through that
                using (var backend = DynamicLoader.BackendLoader.GetBackend(m_backendurl, m_options.RawOptions))
                {
                    if (backend is IQuotaEnabledBackend enabledBackend)
                    {
                        var quota = enabledBackend.Quota;
                        if (quota != null)
                        {
                            m_result.BackendWriter.TotalQuotaSpace = quota.TotalQuotaSpace;
                            m_result.BackendWriter.FreeQuotaSpace = quota.FreeQuotaSpace;
                        }
                    }
                }
            }

            if (m_result.BackendWriter != null) 
                m_result.BackendWriter.AssignedQuotaSpace = m_options.QuotaSize;
        }

        public void Run(string[] sources, Library.Utility.IFilter filter)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Begin);                        
            
            using(m_database = new LocalBackupDatabase(m_options.Dbpath, m_options))
            {
                m_result.SetDatabase(m_database);
                m_result.Dryrun = m_options.Dryrun;

                Utility.UpdateOptionsFromDb(m_database, m_options);
                Utility.VerifyParameters(m_database, m_options);

                var probePath = m_database.GetFirstPath();
                if (probePath != null && Library.Utility.Utility.GuessDirSeparator(probePath) != Path.DirectorySeparatorChar.ToString())
                    throw new UserInformationException(
                        "The backup contains files that belong to another operating system. Proceeding with a backup would cause the database to contain paths from two different operation systems, which is not supported. To proceed without losing remote data, delete all filesets and make sure the --no-auto-compact option is set, then run the backup again to re-use the existing data on the remote store.", "CrossOsDatabaseReuseNotSupported");

                if (m_database.PartiallyRecreated)
                    throw new UserInformationException("The database was only partially recreated. This database may be incomplete and the repair process is not allowed to alter remote files as that could result in data loss.", "DatabaseIsPartiallyRecreated");
                
                if (m_database.RepairInProgress)
                    throw new UserInformationException("The database was attempted repaired, but the repair did not complete. This database may be incomplete and the backup process cannot continue. You may delete the local database and attempt to repair it again.", "DatabaseRepairInProgress");

                m_blocksize = m_options.Blocksize;
                m_maxmetadatasize = (m_blocksize / (long)m_options.BlockhashSize) * m_blocksize;

                m_blockbuffer = new byte[m_options.Blocksize * Math.Max(1, m_options.FileReadBufferSize / m_options.Blocksize)];
                m_blocklistbuffer = new byte[m_options.Blocksize];

                m_blockhasher = Library.Utility.HashAlgorithmHelper.Create(m_options.BlockHashAlgorithm);
                m_filehasher = Library.Utility.HashAlgorithmHelper.Create(m_options.FileHashAlgorithm);

                if (m_blockhasher == null)
                    throw new UserInformationException(Strings.Common.InvalidHashAlgorithm(m_options.BlockHashAlgorithm), "BlockHashAlgoithmNotSupported");
                if (m_filehasher == null)
                    throw new UserInformationException(Strings.Common.InvalidHashAlgorithm(m_options.FileHashAlgorithm), "FileHashAlgoithmNotSupported");

                if (!m_blockhasher.CanReuseTransform)
                    throw new UserInformationException(Strings.Common.InvalidCryptoSystem(m_options.BlockHashAlgorithm), "BlockHashAlgoithmNotSupported");
                if (!m_filehasher.CanReuseTransform)
                    throw new UserInformationException(Strings.Common.InvalidCryptoSystem(m_options.FileHashAlgorithm), "FileHashAlgoithmNotSupported");

                m_database.VerifyConsistency(null, m_options.Blocksize, m_options.BlockhashSize, false);
                // If there is no filter, we set an empty filter to simplify the code
                // If there is a filter, we make sure that the sources are included
                m_filter = filter ?? new Library.Utility.FilterExpression();
                m_sourceFilter = new Library.Utility.FilterExpression(sources);
                
                m_backendLogFlushTimer = DateTime.Now.Add(FLUSH_TIMESPAN);
                System.Threading.Thread parallelScanner = null;
    
                try
                {
                    using(var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, m_database))
                    using(var filesetvolume = new FilesetVolumeWriter(m_options, m_database.OperationTimestamp))
                    {
                        using(var snapshot = GetSnapshot(sources, m_options))
                        {
                            // Start parallel scan, or use the database
                            if (m_options.ChangedFilelist == null || m_options.ChangedFilelist.Length < 1)
                            {
                                if (m_options.DisableFileScanner)
                                {
                                    var d = m_database.GetLastBackupFileCountAndSize();
                                    m_result.OperationProgressUpdater.UpdatefileCount(d.Item1, d.Item2, true);
                                }
                                else
                                {
                                    parallelScanner = new System.Threading.Thread(CountFilesThread)
                                    {
                                        Name = "Read ahead file counter",
                                        IsBackground = true
                                    };
                                    parallelScanner.Start(Tuple.Create(snapshot, sources));
                                }
                            }

                            string lasttempfilelist = null;
                            long lasttempfileid = -1;
                            if (!m_options.DisableSyntheticFilelist)
                            {
                                var candidates = m_database.GetIncompleteFilesets(null).OrderBy(x => x.Value).ToArray();
                                if (candidates.Length > 0)
                                {
                                    lasttempfileid = candidates.Last().Key;
                                    lasttempfilelist = m_database.GetRemoteVolumeFromID(lasttempfileid).Name;
                                }
                            }

                            // Verify before uploading a synthetic list
                            PreBackupVerify(backend, lasttempfilelist);

                            // If we have an incomplete entry, upload it now
                            if (!m_options.DisableSyntheticFilelist && !string.IsNullOrWhiteSpace(lasttempfilelist) && lasttempfileid >= 0)
                            {
                                // Check that we still need to process this after the cleanup has performed its duties
                                var syntbase = m_database.GetRemoteVolumeFromID(lasttempfileid);
                                if (syntbase.Name != null && (syntbase.State == RemoteVolumeState.Uploading || syntbase.State == RemoteVolumeState.Temporary))
                                {
                                    UploadSyntheticFilelist(backend);

                                    // Remove the protected file
                                    if (syntbase.State == RemoteVolumeState.Uploading)
                                    {
                                        Logging.Log.WriteInformationMessage(LOGTAG, "RemoveIncompleteFilelist", "Removing incomplete remote file listed as {0}: {1}", syntbase.State, syntbase.Name);
                                        backend.Delete(syntbase.Name, syntbase.Size);
                                    }
                                    else if (syntbase.State == RemoteVolumeState.Temporary)
                                    {
                                        Logging.Log.WriteInformationMessage(LOGTAG, "RemoveTemporaryFile", "Removing file listed as {0}: {1}", syntbase.State, syntbase.Name);
                                        m_database.RemoveRemoteVolume(syntbase.Name);
                                    }
                                }
                                else if (syntbase.Name == null || syntbase.State != RemoteVolumeState.Uploaded)
                                    Logging.Log.WriteWarningMessage(LOGTAG, "MissingTemporaryFilelist", null, "Expected there to be a temporary fileset for synthetic filelist ({0}, {1}), but none was found?", lasttempfileid, lasttempfilelist);
                            }

                            var prevfileset = m_database.FilesetTimes.FirstOrDefault();
                            if (prevfileset.Value.ToUniversalTime() > m_database.OperationTimestamp.ToUniversalTime())
                                throw new UserInformationException(
                                    $"The previous backup has time {prevfileset.Value.ToLocalTime()}, but this backup has time {m_database.OperationTimestamp.ToLocalTime()}. Something is wrong with the clock.", "FilesetIntervalClockIssue");
                            
                            m_prevFilesetId = prevfileset.Value.Ticks == 0 ? -1 : prevfileset.Key;

                            // Rebuild any index files that are missing
                            RecreateMissingIndexFiles(backend);

                            m_database.BuildLookupTable(m_options);
                            m_transaction = m_database.BeginTransaction();

                            var repcnt = 0;
                            while(repcnt < 100 && m_database.GetRemoteVolumeID(filesetvolume.RemoteFilename) >= 0)
                                filesetvolume.ResetRemoteFilename(m_options, m_database.OperationTimestamp.AddSeconds(repcnt++));
                            
                            if (m_database.GetRemoteVolumeID(filesetvolume.RemoteFilename) >= 0)
                                throw new Exception("Unable to generate a unique fileset name");

                            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_ProcessingFiles);
                            var filesetvolumeid = m_database.RegisterRemoteVolume(filesetvolume.RemoteFilename, RemoteVolumeType.Files, RemoteVolumeState.Temporary, m_transaction);
                            m_database.CreateFileset(filesetvolumeid, VolumeBase.ParseFilename(filesetvolume.RemoteFilename).Time, m_transaction);
            
                            RunMainOperation(snapshot, backend, sources);

                            //If the scanner is still running for some reason, make sure we kill it now 
                            if (parallelScanner != null && parallelScanner.IsAlive)
                                parallelScanner.Abort();
                        }

                        var lastVolumeSize = FinalizeRemoteVolumes(backend);
                        
                        using(new Logging.Timer(LOGTAG, "UpdateChangeStatistics", "UpdateChangeStatistics"))
                            m_database.UpdateChangeStatistics(m_result);
                        using(new Logging.Timer(LOGTAG, "VerifyConsistency", "VerifyConsistency"))
                            m_database.VerifyConsistency(m_transaction, m_options.Blocksize, m_options.BlockhashSize, false);
    
                        UploadRealFileList(backend, filesetvolume);
                                        
                        m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_WaitForUpload);
                        using(new Logging.Timer(LOGTAG, "AsyncBackendWait", "Async backend wait"))
                            backend.WaitForEmpty(m_database, m_transaction);
                            
                        if (m_result.TaskControlRendevouz() != TaskControlState.Stop) 
                            CompactIfRequired(backend, lastVolumeSize);

                        using (new Logging.Timer(LOGTAG, "AsyncBackendWait", "Async backend wait"))
                            backend.WaitForComplete(m_database, m_transaction);

                        if (m_options.UploadVerificationFile)
                        {
                            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_VerificationUpload);
                            FilelistProcessor.UploadVerificationFile(backend.BackendUrl, m_options, m_result.BackendWriter, m_database, m_transaction);
                        }

                        if (m_options.Dryrun)
                        {
                            m_transaction.Rollback();
                            m_transaction = null;
                        }
                        else
                        {
                            using(new Logging.Timer(LOGTAG, "CommitFinalizingBackupe", "CommitFinalizingBackup"))
                                m_transaction.Commit();
                                
                            m_transaction = null;

                            if (m_result.TaskControlRendevouz() != TaskControlState.Stop)
                            {
                                if (m_options.NoBackendverification)
                                    UpdateStorageStatsFromDatabase();
                                else
                                    PostBackupVerification();
                            }
                        }
                        
                        m_database.WriteResults();                    
                        m_database.PurgeLogData(m_options.LogRetention);
                        if (m_options.AutoVacuum)
                        {
                            m_database.Vacuum();
                        }
                        m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Complete);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "FatalError", ex, "Fatal error");
                    throw;
                }
                finally
                {
                    if (parallelScanner != null && parallelScanner.IsAlive)
                    {
                        parallelScanner.Abort();
                        parallelScanner.Join(500);
                        if (parallelScanner.IsAlive)
                            Logging.Log.WriteWarningMessage(LOGTAG, "FileCounterTerminateFailed", null, "Failed to terminate filecounter thread");
                    }
                
                    if (m_transaction != null)
                        try { m_transaction.Rollback(); }
                        catch (Exception ex) { Logging.Log.WriteErrorMessage(LOGTAG, "RollbackError", ex, "Rollback error: {0}", ex.Message); }
                }
            }
        }

        private Dictionary<string, string> GenerateMetadata(ISnapshotService snapshot, string path, FileAttributes attributes)
        {
            // ReSharper disable once InconsistentNaming
            var METALOGTAG = LOGTAG + ".Metadata";
            try
            {
                Dictionary<string, string> metadata;

                if (m_options.StoreMetadata)
                {
                    metadata = snapshot.GetMetadata(path, attributes.HasFlag(FileAttributes.ReparsePoint), m_symlinkPolicy == Options.SymlinkStrategy.Follow) ??
                               new Dictionary<string, string>();

                    if (!metadata.ContainsKey("CoreAttributes"))
                        metadata["CoreAttributes"] = attributes.ToString();

                    if (!metadata.ContainsKey("CoreLastWritetime"))
                    {
                        try
                        {
                            metadata["CoreLastWritetime"] = snapshot.GetLastWriteTimeUtc(path).Ticks.ToString();
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(METALOGTAG, "TimestampReadFailed", ex, "Failed to read timestamp on \"{0}\"", path);
                        }
                    }

                    if (!metadata.ContainsKey("CoreCreatetime"))
                    {
                        try
                        {
                            metadata["CoreCreatetime"] = snapshot.GetCreationTimeUtc(path).Ticks.ToString();
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(METALOGTAG, "TimestampReadFailed", ex, "Failed to read timestamp on \"{0}\"", path);
                        }
                    }
                }
                else
                {
                    metadata = new Dictionary<string, string>();
                }

                return metadata;
            }
            catch(Exception ex)
            {
                Logging.Log.WriteWarningMessage(METALOGTAG, "MetadataProcessFailed", ex, "Failed to process metadata for \"{0}\", storing empty metadata", path);
                return new Dictionary<string, string>();
            }
        }
        
        /// <summary>
        /// Add <c>path</c> to backup if it has been modified, and isn't included by exclusion filters
        /// </summary>
        /// <param name="snapshot">Snapshot handler</param>
        /// <param name="backend">Backend</param>
        /// <param name="path">File / folder path to add (non-recursively!)</param>
        /// <param name="attributes">File attributes</param>
        private void HandleFilesystemEntry(ISnapshotService snapshot, BackendManager backend, string path, FileAttributes attributes)
        {
            // ReSharper disable once InconsistentNaming
            var FILELOGTAG = LOGTAG + ".FileEntry";

            // If we lost the connection, there is no point in keeping on processing
            if (backend.HasDied)
                throw backend.LastException;
            
            try
            {
                m_result.OperationProgressUpdater.StartFile(path, -1);            
                
                if (m_backendLogFlushTimer < DateTime.Now)
                {
                    m_backendLogFlushTimer = DateTime.Now.Add(FLUSH_TIMESPAN);
                    backend.FlushDbMessages(m_database, null);
                }

                var lastwrite = new DateTime(0, DateTimeKind.Utc);
                try 
                { 
                    lastwrite = snapshot.GetLastWriteTimeUtc(path); 
                }
                catch (Exception ex) 
                {
                    Logging.Log.WriteWarningMessage(FILELOGTAG, "TimestampReadFailed", ex, "Failed to read timestamp on \"{0}\"", path);
                }
                                            
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    // Not all reparse points are symlinks.
                    // For example, on Windows 10 Fall Creator's Update, the OneDrive folder (and all subfolders)
                    // are reparse points, which allows the folder to hook into the OneDrive service and download things on-demand.
                    // If we can't find a symlink target for the current path, we won't treat it as a symlink.
                    string symlinkTarget = snapshot.GetSymlinkTarget(path);
                    if (!string.IsNullOrWhiteSpace(symlinkTarget))
                    {
                        if (m_options.SymlinkPolicy == Options.SymlinkStrategy.Ignore)
                        {
                            Logging.Log.WriteVerboseMessage(FILELOGTAG, "IgnoreSymlink", "Ignoring symlink {0}", path);
                            return;
                        }

                        if (m_options.SymlinkPolicy == Options.SymlinkStrategy.Store)
                        {
                            Dictionary<string, string> metadata = GenerateMetadata(snapshot, path, attributes);

                            if (!metadata.ContainsKey("CoreSymlinkTarget"))
                            {
                                metadata["CoreSymlinkTarget"] = symlinkTarget;
                            }

                            var metahash = Utility.WrapMetadata(metadata, m_options);
                            AddSymlinkToOutput(backend, path, DateTime.UtcNow, metahash);

                            Logging.Log.WriteVerboseMessage(FILELOGTAG, "StoreSymlink", "Stored symlink {0}", path);
                            //Do not recurse symlinks
                            return;
                        }
                    }
                    else
                    {
                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "FollowingEmptySymlink", "Treating empty symlink as regular path {0}", path);
                    }
                }
    
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    var metahash = m_options.StoreMetadata ? Utility.WrapMetadata(GenerateMetadata(snapshot, path, attributes), m_options) : EMPTY_METADATA;
    
                    Logging.Log.WriteVerboseMessage(FILELOGTAG, "AddDirectory", "Adding directory {0}", path);
                    AddFolderToOutput(backend, path, lastwrite, metahash);
                    return;
                }
    
                m_result.OperationProgressUpdater.UpdatefilesProcessed(++m_result.ExaminedFiles, m_result.SizeOfExaminedFiles);
                
                bool changed = false;
                
                // Last scan time
                DateTime oldModified;
                long lastFileSize;
                string oldMetahash;
                long oldMetasize;
                long oldId;

                long filestatsize = -1;
                try { filestatsize = snapshot.GetFileSize(path); }
                catch (Exception ex) { Logging.Log.WriteVerboseMessage(FILELOGTAG, "SizeReadFailed", "Failed to read length of file {0}: {1}", path, ex.Message); }

                IMetahash metahashandsize = m_options.StoreMetadata ? Utility.WrapMetadata(GenerateMetadata(snapshot, path, attributes), m_options) : EMPTY_METADATA;

                if (m_options.CheckFiletimeOnly || m_options.DisableFiletimeCheck)
                {
                    lastFileSize = filestatsize;
                    oldMetahash = metahashandsize.FileHash;
                    oldMetasize = metahashandsize.Blob.Length;
                    oldId = m_database.GetFileLastModified(path, m_prevFilesetId, out oldModified);
                }
                else
                {
                    oldId = m_database.GetFileEntry(path, m_prevFilesetId, out oldModified, out lastFileSize, out oldMetahash, out oldMetasize);
                }

                var timestampChanged = lastwrite != oldModified || lastwrite.Ticks == 0 || oldModified.Ticks == 0;
                var filesizeChanged = filestatsize < 0 || lastFileSize < 0 || filestatsize != lastFileSize;
                var tooLargeFile = m_options.SkipFilesLargerThan != long.MaxValue && m_options.SkipFilesLargerThan != 0 && filestatsize >= 0 && filestatsize > m_options.SkipFilesLargerThan;
                var metadatachanged = !m_options.SkipMetadata && (metahashandsize.Blob.Length != oldMetasize || metahashandsize.FileHash != oldMetahash);

                if ((oldId < 0 || m_options.DisableFiletimeCheck || timestampChanged || filesizeChanged || metadatachanged) && !tooLargeFile)
                {
                    Logging.Log.WriteVerboseMessage(FILELOGTAG, "CheckFileForChanges", "Checking file for changes {0}, new: {1}, timestamp changed: {2}, size changed: {3}, metadatachanged: {4}, {5} vs {6}", path, oldId <= 0, timestampChanged, filesizeChanged, metadatachanged, lastwrite, oldModified);

                    m_result.OpenedFiles++;

                    var hint = m_options.GetCompressionHintFromFilename(path);
                    var oldHash = oldId < 0 ? null : m_database.GetFileHash(oldId);

                    using (var blocklisthashes = new Library.Utility.FileBackedStringList())
                    using (var hashcollector = new Library.Utility.FileBackedStringList())
                    {
                        long filesize;
                        using (var fs = snapshot.OpenRead(path))
                        {
                            try { m_result.OperationProgressUpdater.StartFile(path, fs.Length); }
                            catch (Exception ex) { Logging.Log.WriteWarningMessage(FILELOGTAG, "FileLengthFailure", ex, "Failed to read file length for file {0}", path); }
                            
                            if ((filesize = ProcessStream(fs, hint, backend, blocklisthashes, hashcollector, false)) < 0) return;
                        }

                        m_result.SizeOfOpenedFiles += filesize;

                        var filekey = Convert.ToBase64String(m_filehasher.Hash);
                        if (oldHash != filekey)
                        {
                            if (oldHash == null)
                                Logging.Log.WriteVerboseMessage(FILELOGTAG, "NewFile", "New file {0}", path);
                            else
                                Logging.Log.WriteVerboseMessage(FILELOGTAG, "ChangedFile", "File has changed {0}", path);
                            if (oldId < 0)
                            {
                                m_result.AddedFiles++;
                                m_result.SizeOfAddedFiles += filesize;
                                
                                if (m_options.Dryrun)
                                    Logging.Log.WriteVerboseMessage(FILELOGTAG, "WoudlAddNewFile", "Would add new file {0}, size {1}", path, Library.Utility.Utility.FormatSizeString(filesize));
                            }
                            else
                            {
                                m_result.ModifiedFiles++;
                                m_result.SizeOfModifiedFiles += filesize;
                                
                                if (m_options.Dryrun)
                                    Logging.Log.WriteVerboseMessage(FILELOGTAG, "WoudlAddChangedFile", "Would add changed file {0}, size {1}", path, Library.Utility.Utility.FormatSizeString(filesize));
                            }

                            AddFileToOutput(backend, path, filesize, lastwrite, metahashandsize, hashcollector, filekey, blocklisthashes);
                            changed = true;
                        }
                        else if (metadatachanged)
                        {
                            Logging.Log.WriteVerboseMessage(FILELOGTAG, "FileMetadataChanged", "File has only metadata changes {0}", path);
                            AddFileToOutput(backend, path, filesize, lastwrite, metahashandsize, hashcollector, filekey, blocklisthashes);
                            changed = true;
                        }
                        else
                        {
                            // When we write the file to output, update the last modified time
                            Logging.Log.WriteVerboseMessage(FILELOGTAG, "NoFileChanges", "File has not changed {0}", path);
                        }
                    }
                }
                else
                {
                    if (tooLargeFile)                
                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "ExcludeLargeFile", "Excluding file because the size {0} exceeds limit ({1}): {2}", Library.Utility.Utility.FormatSizeString(filestatsize), Library.Utility.Utility.FormatSizeString(m_options.SkipFilesLargerThan), path);
                    else
                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "SkipCheckNoTimestampChange", "Skipped checking file, because timestamp was not updated {0}", path);
                }

                // If the file was not previously found, we cannot add it
                // If the file was too large, we treat it as missing,
                // otherwise the backups appear to contain the file
                // but has an old version
                if (!changed && oldId >= 0 && !tooLargeFile)
                    AddUnmodifiedFile(oldId, lastwrite);

                m_result.SizeOfExaminedFiles += filestatsize;
                if (filestatsize != 0)
                    m_result.OperationProgressUpdater.UpdatefilesProcessed(m_result.ExaminedFiles, m_result.SizeOfExaminedFiles);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(FILELOGTAG, "PathProcessingFailed", ex, "Failed to process path: {0}", path);
                m_result.FilesWithError++;
            }
        }

        private long ProcessStream(Stream stream, CompressionHint hint, BackendManager backend,
            Library.Utility.FileBackedStringList blocklisthashes, Library.Utility.FileBackedStringList hashcollector,
            bool skipfilehash)
        {
            int blocklistoffset = 0;
            long filesize = 0;

            using (var fs = new Blockprocessor(stream, m_blockbuffer))
            {
                m_filehasher.Initialize();

                var offset = 0;
                var remaining = fs.Readblock();

                do
                {
                    var size = Math.Min(m_blocksize, remaining);

                    if (!skipfilehash)
                        m_filehasher.TransformBlock(m_blockbuffer, offset, size, m_blockbuffer, offset);

                    var blockkey = m_blockhasher.ComputeHash(m_blockbuffer, offset, size);
                    if (m_blocklistbuffer.Length - blocklistoffset < blockkey.Length)
                    {
                        var blkey = Convert.ToBase64String(m_blockhasher.ComputeHash(m_blocklistbuffer, 0,
                            blocklistoffset));
                        blocklisthashes.Add(blkey);
                        AddBlockToOutput(backend, blkey, m_blocklistbuffer, 0, blocklistoffset,
                            CompressionHint.Noncompressible, true);
                        blocklistoffset = 0;
                    }

                    Array.Copy(blockkey, 0, m_blocklistbuffer, blocklistoffset, blockkey.Length);
                    blocklistoffset += blockkey.Length;

                    var key = Convert.ToBase64String(blockkey);
                    AddBlockToOutput(backend, key, m_blockbuffer, offset, size, hint, false);
                    hashcollector.Add(key);
                    filesize += size;

                    if (!skipfilehash)
                    {
                        m_result.OperationProgressUpdater.UpdateFileProgress(filesize);
                        if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                            return -1;
                    }

                    remaining -= size;
                    offset += size;

                    if (remaining == 0)
                    {
                        offset = 0;
                        remaining = fs.Readblock();
                    }

                } while (remaining > 0);

                //If all fits in a single block, don't bother with blocklists
                if (hashcollector.Count > 1)
                {
                    var blkeyfinal =
                        Convert.ToBase64String(m_blockhasher.ComputeHash(m_blocklistbuffer, 0, blocklistoffset));
                    blocklisthashes.Add(blkeyfinal);
                    AddBlockToOutput(backend, blkeyfinal, m_blocklistbuffer, 0, blocklistoffset,
                        CompressionHint.Noncompressible, true);
                }
            }

            if (!skipfilehash)
                m_filehasher.TransformFinalBlock(m_blockbuffer, 0, 0);

            return filesize;

        }

        /// <summary>
        /// Adds the found file data to the output unless the block already exists
        /// </summary>
        /// <param name="backend"></param>
        /// <param name="key">The block hash</param>
        /// <param name="data">The data matching the hash</param>
        /// <param name="offset">The offset into the data</param>
        /// <param name="len">The size of the data</param>
        /// <param name="hint">Hint for compression module</param>
        /// <param name="isBlocklistData">Indicates if the block is list data</param>
        private void AddBlockToOutput(BackendManager backend, string key, byte[] data, int offset, int len,
            CompressionHint hint, bool isBlocklistData)
        {
            if (m_blockvolume == null)
            {
                m_blockvolume = new BlockVolumeWriter(m_options);
                m_blockvolume.VolumeID = m_database.RegisterRemoteVolume(m_blockvolume.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, m_transaction);

                if (m_options.IndexfilePolicy != Options.IndexFileStrategy.None)
                {
                    m_indexvolume = new IndexVolumeWriter(m_options);
                    m_indexvolume.VolumeID = m_database.RegisterRemoteVolume(m_indexvolume.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary, m_transaction);
                }
            }

            if (m_database.AddBlock(key, len, m_blockvolume.VolumeID, m_transaction))
            {
                m_blockvolume.AddBlock(key, data, offset, len, hint);
                
                //TODO: In theory a normal data block and blocklist block could be equal.
                // this would cause the index file to not contain all data,
                // if the data file is added before the blocklist data
                // ... highly theoretical ...
                if (m_options.IndexfilePolicy == Options.IndexFileStrategy.Full && isBlocklistData)
                    m_indexvolume.WriteBlocklist(key, data, offset, len);
                    
                if (m_blockvolume.Filesize > m_options.VolumeSize - m_options.Blocksize)
                {
                    if (m_options.Dryrun)
                    {
                        m_blockvolume.Close();
                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldUploadBlock", "Would upload block volume: {0}, size: {1}", m_blockvolume.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(m_blockvolume.LocalFilename).Length));
                        
                        if (m_indexvolume != null)
                        {
                            UpdateIndexVolume();
                            m_indexvolume.FinishVolume(Library.Utility.Utility.CalculateHash(m_blockvolume.LocalFilename), new FileInfo(m_blockvolume.LocalFilename).Length);
                            Logging.Log.WriteDryrunMessage(LOGTAG, "WouldUploadIndex", "Would upload index volume: {0}, size: {1}", m_indexvolume.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(m_indexvolume.LocalFilename).Length));
                            m_indexvolume.Dispose();
                            m_indexvolume = null;
                        }
                        
                        m_blockvolume.Dispose();
                        m_blockvolume = null;
                    }
                    else
                    {
                        //When uploading a new volume, we register the volumes and then flush the transaction
                        // this ensures that the local database and remote storage are as closely related as possible
                        m_database.UpdateRemoteVolume(m_blockvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);
                        m_blockvolume.Close();
                        UpdateIndexVolume();
                        
                        backend.FlushDbMessages(m_database, m_transaction);
                        m_backendLogFlushTimer = DateTime.Now.Add(FLUSH_TIMESPAN);

                        using(new Logging.Timer(LOGTAG, "CommitAddBlockToOutputFlush", "CommitAddBlockToOutputFlush"))
                            m_transaction.Commit();
                        m_transaction = m_database.BeginTransaction();

                        backend.Put(m_blockvolume, m_indexvolume);
                        m_blockvolume = null;
                        m_indexvolume = null;

                        using(new Logging.Timer(LOGTAG, "CommitAddBlockToOutputFlush", "CommitAddBlockToOutputFlush"))
                            m_transaction.Commit();
                        m_transaction = m_database.BeginTransaction();
                        
                    }
                }
            }
        }

        private void AddUnmodifiedFile(long oldId, DateTime lastModified)
        {
            m_database.AddUnmodifiedFile(oldId, lastModified, m_transaction);
        }

        private long AddMetadataToOutput(BackendManager backend, IMetahash meta)
        {
            long metadataid;

            if (meta.Blob.Length > m_maxmetadatasize)
            {
                //TODO: To fix this, the "WriteFileset" method in BackupHandler needs to
                // be updated such that it can select sets even when there are multiple
                // blocklist hashes for the metadata.
                // This could be done such that an extra query is made if the metadata
                // spans multiple blocklist hashes, as it is not expected to be common

                Logging.Log.WriteWarningMessage(LOGTAG, "TooLargeMetadata", null, "Metadata size is {0}, but the largest accepted size is {1}, recording empty metadata", meta.Blob.Length, m_maxmetadatasize);
                meta = EMPTY_METADATA;
            }

            using(var blocklisthashes = new Library.Utility.FileBackedStringList())
            using(var hashcollector = new Library.Utility.FileBackedStringList())
            {
                using(var ms = new MemoryStream(meta.Blob))
                    ProcessStream(ms, CompressionHint.Compressible, backend, blocklisthashes, hashcollector, true);

                m_database.AddMetadataset(meta.FileHash, meta.Blob.Length, m_blocksize, hashcollector, blocklisthashes, out metadataid, m_transaction);
            }  

            return metadataid;
        }

        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="backend"></param>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        /// <param name="metadata">A lookup table with various metadata values describing the file</param>
        private void AddFolderToOutput(BackendManager backend, string filename, DateTime lastModified, IMetahash metadata)
        {
            var metadataid = AddMetadataToOutput(backend, metadata);
            m_database.AddDirectoryEntry(filename, metadataid, lastModified, m_transaction);
        }

        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="backend">Storage backend manager</param>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        /// <param name="metadata">A lookup table with various metadata values describing the file</param>
        private void AddSymlinkToOutput(BackendManager backend, string filename, DateTime lastModified, IMetahash metadata)
        {
            var metadataid = AddMetadataToOutput(backend, metadata);
            m_database.AddSymlinkEntry(filename, metadataid, lastModified, m_transaction);
        }

        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="backend">Storage backend manager</param>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastmodified">The value of the lastModified timestamp</param>
        /// <param name="hashlist">The list of hashes that make up the file</param>
        /// <param name="size">The size of the file</param>
        /// <param name="metadata">A lookup table with various metadata values describing the file</param>
        /// <param name="filehash">File hash</param>
        /// <param name="blocklisthashes"></param>
        private void AddFileToOutput(BackendManager backend, string filename, long size, DateTime lastmodified, IMetahash metadata, IEnumerable<string> hashlist, string filehash, IEnumerable<string> blocklisthashes)
        {
            var metadataid = AddMetadataToOutput(backend, metadata);
            m_database.AddBlockset(filehash, size, m_blocksize, hashlist, blocklisthashes, out var blocksetid, m_transaction);
            m_database.AddFile(filename, lastmodified, blocksetid, metadataid, m_transaction);
        }


        private void UpdateIndexVolume()
        {
            if (m_indexvolume != null)
            {
                m_database.AddIndexBlockLink(m_indexvolume.VolumeID, m_blockvolume.VolumeID, m_transaction);
                m_indexvolume.StartVolume(m_blockvolume.RemoteFilename);
                
                foreach(var b in m_database.GetBlocks(m_blockvolume.VolumeID))
                    m_indexvolume.AddBlock(b.Hash, b.Size);

                m_database.UpdateRemoteVolume(m_indexvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);
            }
        }

        internal static FileAttributes SafeGetFileAttributes(string path, ISnapshotService snapshot, string tag)
        {
            var fa = FileAttributes.Normal;
            try
            {
                fa = snapshot.GetAttributes(path);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteVerboseMessage(tag, "FailedAttributeRead",
                    "Failed to read attributes from {0}: {1}", path, ex.Message);
            }

            return fa;
        }

        public void Dispose()
        {
            if (m_blockvolume != null)
            {
                try { m_blockvolume.Dispose(); }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "BlockDisposeFailed", ex, "Failed disposing block volume", ex); }
                finally { m_blockvolume = null; }
            }

            if (m_indexvolume != null)
            {
                try { m_indexvolume.Dispose(); }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "IndexDisposeFailed", ex, "Failed disposing index volume", ex); }
                finally { m_indexvolume = null; }
            }

            if (m_result.EndTime.Ticks == 0)
                m_result.EndTime = DateTime.UtcNow;
        }
    }

    internal sealed class FilterHandler
    {
        private static readonly string FILTER_LOGTAG = Logging.Log.LogTagFromType<FilterHandler>();
        private readonly ISnapshotService m_snapshot;
        private readonly FileAttributes m_attributeFilter;
        private readonly Library.Utility.IFilter m_enumeratefilter;
        private readonly Library.Utility.IFilter m_emitfilter;
        private readonly Options.SymlinkStrategy m_symlinkPolicy;
        private readonly Options.HardlinkStrategy m_hardlinkPolicy;
        private readonly Dictionary<string, string> m_hardlinkmap;
        private readonly Library.Utility.IFilter m_sourcefilter;
        private readonly Queue<string> m_mixinqueue;

        public FilterHandler(ISnapshotService snapshot, FileAttributes attributeFilter,
            Library.Utility.IFilter sourcefilter, Library.Utility.IFilter filter,
            Options.SymlinkStrategy symlinkPolicy, Options.HardlinkStrategy hardlinkPolicy)
        {
            m_snapshot = snapshot;
            m_attributeFilter = attributeFilter;
            m_sourcefilter = sourcefilter;
            m_emitfilter = filter;
            m_symlinkPolicy = symlinkPolicy;
            m_hardlinkPolicy = hardlinkPolicy;
            m_hardlinkmap = new Dictionary<string, string>();
            m_mixinqueue = new Queue<string>();

            Library.Utility.FilterExpression.AnalyzeFilters(filter, out var includes, out var excludes);
            if (includes && !excludes)
            {
                m_enumeratefilter = Library.Utility.FilterExpression.Combine(filter,
                    new Library.Utility.FilterExpression("*" + Path.DirectorySeparatorChar));
            }
            else
            {
                m_enumeratefilter = m_emitfilter;
            }
        }

        public void ReportError(string rootpath, string path, Exception ex)
        {
            Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "FileAccessError", ex, "Error reported while accessing file: {0}", path);
        }

        public bool AttributeFilter(string rootpath, string path, FileAttributes attributes)
        {
            try
            {
                if (m_snapshot.IsBlockDevice(path))
                {
                    Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingBlockDevice", "Excluding block device: {0}", path);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "PathProcessingError", ex, "Failed to process path: {0}", path);
                return false;
            }

            if (m_sourcefilter.Matches(path, out var sourcematches, out _) && sourcematches)
            {
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "IncludingSourcePath", "Including source path: {0}", path);

                return true;
            }
                
            if (m_hardlinkPolicy != Options.HardlinkStrategy.All)
            {
                try
                {
                    var id = m_snapshot.HardlinkTargetID(path);
                    if (id != null)
                    {
                        switch (m_hardlinkPolicy)
                        {
                            case Options.HardlinkStrategy.None:
                                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingHardlinkByPolicy", "Excluding hardlink: {0} ({1})", path, id);
                                return false;

                            case Options.HardlinkStrategy.First:
                                if (m_hardlinkmap.TryGetValue(id, out var prevPath))
                                {
                                    Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingDuplicateHardlink", "Excluding hardlink ({1}) for: {0}, previous hardlink: {2}", path, id, prevPath);
                                    return false;
                                }
                                else
                                {
                                    m_hardlinkmap.Add(id, path);
                                }

                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "PathProcessingError", ex, "Failed to process path: {0}", path);
                    return false;
                }                    
            }
            
            if ((m_attributeFilter & attributes) != 0)
            {
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingPathFromAttributes", "Excluding path due to attribute filter: {0}", path);
                return false;
            }

            var filtermatch = false;
            if (!Library.Utility.FilterExpression.Matches(m_enumeratefilter, path, out var match))
            {
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingPathFromFilter", "Excluding path due to filter: {0} => {1}", path, match == null ? "null" : match.ToString());
                return false;
            }

            if (match != null)
            {
                filtermatch = true;
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "IncludingPathFromFilter", "Including path due to filter: {0} => {1}", path, match.ToString());
            }

            var isSymlink = m_snapshot.IsSymlink(path, attributes);
            if (isSymlink && m_symlinkPolicy == Options.SymlinkStrategy.Ignore)
            {
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludeSymlink", "Excluding symlink: {0}", path);
                return false;
            }

            if (isSymlink && m_symlinkPolicy == Options.SymlinkStrategy.Store)
            {
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "StoreSymlink", "Storing symlink: {0}", path);

                m_mixinqueue.Enqueue(path);
                return false;
            }
                              
            if (!filtermatch)
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "IncludingPath", "Including path as no filters matched: {0}", path);

            return true;
        }

        /// <summary>
        /// Enumerate files and folders (recursively) in <c>sources</c>, according to active attribute and exclusion filters.
        /// </summary>
        /// <param name="sources">Sources to enumerate</param>
        /// <returns></returns>
        public IEnumerable<string> EnumerateFilesAndFolders(IEnumerable<string> sources)
        {
            foreach(var s in m_snapshot.EnumerateFilesAndFolders(sources, AttributeFilter, ReportError))
            {
                // emit any paths that were added by the enumeration itself (symlink targets, ...)
                while (m_mixinqueue.Any())
                    yield return m_mixinqueue.Dequeue();

                if (m_emitfilter != m_enumeratefilter && !Library.Utility.FilterExpression.Matches(m_emitfilter, s, out _))
                    continue;

                yield return s;
            }

            // emit any paths that were added by the enumeration itself (symlink targets, ...)
            while (m_mixinqueue.Any())
                yield return m_mixinqueue.Dequeue();
        }

        public IEnumerable<string> Mixin(IEnumerable<string> list)
        {
            foreach (var s in list.Where(x =>
            {
                var fa = BackupHandler.SafeGetFileAttributes(x, m_snapshot, FILTER_LOGTAG);
                return AttributeFilter(null, x, fa);
            }))
            {
                while (m_mixinqueue.Count > 0)
                    yield return m_mixinqueue.Dequeue();

                yield return s;
            }

            while (m_mixinqueue.Count > 0)
                yield return m_mixinqueue.Dequeue();
        }
    }
}
