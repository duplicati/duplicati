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
using System.Text;
using System.IO;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Interface;
using System.Threading.Tasks;
using CoCoL;
using System.Threading;
using Duplicati.Library.Snapshots;
using Duplicati.Library.Utility;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Common;

namespace Duplicati.Library.Main.Operation
{
    /// <summary>
    /// The backup handler is the primary function,
    /// which performs a backup of the given sources
    /// to the chosen destination
    /// </summary>
    internal class BackupHandler : IDisposable
    {   
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<BackupHandler>();

        private readonly Options m_options;
        private readonly string m_backendurl;

        private LocalBackupDatabase m_database;
        private System.Data.IDbTransaction m_transaction;

        private Library.Utility.IFilter m_filter;
        private Library.Utility.IFilter m_sourceFilter;

        private readonly BackupResults m_result;

        public BackupHandler(string backendurl, Options options, BackupResults results)
        {
            m_options = options;
            m_result = results;
            m_backendurl = backendurl;
                            
            if (options.AllowPassphraseChange)
                throw new UserInformationException(Strings.Common.PassphraseChangeUnsupported, "PassphraseChangeUnsupported");
        }
        
        public static Snapshots.ISnapshotService GetSnapshot(string[] sources, Options options)
        {
            try
            {
                if (options.SnapShotStrategy != Options.OptimizationStrategy.Off)
                    return Duplicati.Library.Snapshots.SnapshotUtility.CreateSnapshot(sources, options.RawOptions);
            }
            catch (Exception ex)
            {
                if (options.SnapShotStrategy == Options.OptimizationStrategy.Required)
                    throw;
                else if (options.SnapShotStrategy == Options.OptimizationStrategy.On)
                    Logging.Log.WriteWarningMessage(LOGTAG, "SnapshotFailed", ex, Strings.Common.SnapshotFailedError(ex.ToString()));
            }

            return Platform.IsClientPosix ?
                (Library.Snapshots.ISnapshotService)new Duplicati.Library.Snapshots.NoSnapshotLinux()
                    :
                new Duplicati.Library.Snapshots.NoSnapshotWindows();
        }

        /// <summary>
        /// Create instance of USN journal service
        /// </summary>
        /// <param name="sources"></param>
        /// <param name="snapshot"></param>
        /// <param name="filter"></param>
        /// <param name="lastfilesetid"></param>
        /// <returns></returns>
        private UsnJournalService GetJournalService(IEnumerable<string> sources, ISnapshotService snapshot, IFilter filter, long lastfilesetid)
        {
            if (m_options.UsnStrategy == Options.OptimizationStrategy.Off) return null;
            var journalData = m_database.GetChangeJournalData(lastfilesetid);
            var service = new UsnJournalService(sources, snapshot, filter, journalData);

            foreach (var volumeData in service.VolumeDataList)
            {
                if (volumeData.IsFullScan)
                {
                    if (volumeData.Exception == null || volumeData.Exception is UsnJournalSoftFailureException)
                    {
                        // soft fail
                        Logging.Log.WriteInformationMessage(LOGTAG, "SkipUsnForVolume", $"Performing full scan for volume \"{volumeData.Volume}\"");
                    }
                    else
                    {
                        if (m_options.UsnStrategy == Options.OptimizationStrategy.Auto)
                        {
                            Logging.Log.WriteInformationMessage(LOGTAG, "FailedToUseChangeJournal",
                                "Failed to use change journal for volume \"{0}\": {1}", volumeData.Volume, volumeData.Exception.Message);
                        }
                        else if (m_options.UsnStrategy == Options.OptimizationStrategy.On)
                        {
                            Logging.Log.WriteErrorMessage(LOGTAG, "FailedToUseChangeJournal", volumeData.Exception,
                                "Failed to use change journal for volume \"{0}\": {1}", volumeData.Volume, volumeData.Exception.Message);
                        }
                        else
                            throw volumeData.Exception;
                    }
                }
            }

            return service;
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
                        FilelistProcessor.VerifyLocalList(backend, m_database);
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
        /// Performs the bulk of work by starting all relevant processes
        /// </summary>
        private static async Task RunMainOperation(IEnumerable<string> sources, Snapshots.ISnapshotService snapshot, UsnJournalService journalService, Backup.BackupDatabase database, Backup.BackupStatsCollector stats, Options options, IFilter sourcefilter, IFilter filter, BackupResults result, Common.ITaskReader taskreader, long lastfilesetid)
        {
            using(new Logging.Timer(LOGTAG, "BackupMainOperation", "BackupMainOperation"))
            {
                // Make sure the CompressionHints table is initialized, otherwise all workers will initialize it
                var unused = options.CompressionHints.Count;

                Task all;
                using(new ChannelScope())
                {
                    all = Task.WhenAll(
                        new [] 
                        {
                            Backup.DataBlockProcessor.Run(database, options, taskreader),
                            Backup.FileBlockProcessor.Run(snapshot, options, database, stats, taskreader),
                            Backup.StreamBlockSplitter.Run(options, database, taskreader),
                            Backup.FileEnumerationProcess.Run(sources, snapshot, journalService, options.FileAttributeFilter, sourcefilter, filter, options.SymlinkPolicy, options.HardlinkPolicy, options.ExcludeEmptyFolders, options.IgnoreFilenames, options.ChangedFilelist, taskreader),
                            Backup.FilePreFilterProcess.Run(snapshot, options, stats, database),
                            Backup.MetadataPreProcess.Run(snapshot, options, database, lastfilesetid),
                            Backup.SpillCollectorProcess.Run(options, database, taskreader),
                            Backup.ProgressHandler.Run(result)
                        }
                        // Spawn additional block hashers
                        .Union(
                            Enumerable.Range(0, options.ConcurrencyBlockHashers - 1).Select(x => Backup.StreamBlockSplitter.Run(options, database, taskreader))
                        )
                        // Spawn additional compressors
                        .Union(
                            Enumerable.Range(0, options.ConcurrencyCompressors - 1).Select(x => Backup.DataBlockProcessor.Run(database, options, taskreader))
                        )
                    );
                }

                await all.ConfigureAwait(false);

                if (options.ChangedFilelist != null && options.ChangedFilelist.Length >= 1)
                {
                    await database.AppendFilesFromPreviousSetAsync(options.DeletedFilelist);
                }
                else if (journalService != null)
                {
                    // append files from previous fileset, unless part of modifiedSources, which we've just scanned
                    await database.AppendFilesFromPreviousSetWithPredicateAsync((path, fileSize) =>
                    {
                        if (journalService.IsPathEnumerated(path))
                            return true;

                        if (fileSize >= 0)
                        {
                            stats.AddExaminedFile(fileSize);
                        }
                        return false;
                    });

                    // store journal data in database
                    var data = journalService.VolumeDataList.Where(p => p.JournalData != null).Select(p => p.JournalData).ToList();
                    if (data.Any())
                    {
                        // always record change journal data for current fileset (entry may be dropped later if nothing is uploaded)
                        await database.CreateChangeJournalDataAsync(data);

                        // update the previous fileset's change journal entry to resume at this point in case nothing was backed up
                        await database.UpdateChangeJournalDataAsync(data, lastfilesetid);
                    }
                }
                
                result.OperationProgressUpdater.UpdatefileCount(result.ExaminedFiles, result.SizeOfExaminedFiles, true);
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

            long remoteVolumeCount = m_database.GetRemoteVolumes().LongCount(x => x.State == RemoteVolumeState.Verified);
            long samplesToTest = Math.Max(m_options.BackupTestSampleCount, (long)Math.Round(remoteVolumeCount * (m_options.BackupTestPercentage / 100D), MidpointRounding.AwayFromZero));
            if (samplesToTest > 0 && remoteVolumeCount > 0)
            {
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PostBackupTest);
                m_result.TestResults = new TestResults(m_result);

                using(var testdb = new LocalTestDatabase(m_database))
                using(var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, testdb))
                    new TestHandler(m_backendurl, m_options, (TestResults)m_result.TestResults)
                        .DoRun(samplesToTest, testdb, backend);
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
                    if (backend is Library.Interface.IQuotaEnabledBackend)
                    {
                        Library.Interface.IQuotaInfo quota = ((Library.Interface.IQuotaEnabledBackend)backend).Quota;
                        if (quota != null)
                        {
                            m_result.BackendWriter.TotalQuotaSpace = quota.TotalQuotaSpace;
                            m_result.BackendWriter.FreeQuotaSpace = quota.FreeQuotaSpace;
                        }
                    }
                }
                
                m_result.BackendWriter.AssignedQuotaSpace = m_options.QuotaSize;
            }
        }

        public void Run(string[] sources, Library.Utility.IFilter filter)
        {
            RunAsync(sources, filter).WaitForTaskOrThrow();
        }

        private static Exception BuildException(Exception source, params Task[] tasks)
        {
            if (tasks == null || tasks.Length == 0)
                return source;

            var ex = new List<Exception>();
            ex.Add(source);

            foreach(var t in tasks)
                if (t != null)
                {
                    if (!t.IsCompleted && !t.IsFaulted && !t.IsCanceled)
                        t.Wait(500);

                    if (t.IsFaulted && t.Exception != null)
                        ex.Add(t.Exception);
                }

            if (ex.Count == 1)
                return ex.First();
            else
                return new AggregateException(ex.First().Message, ex);
        }

        private static async Task<long> FlushBackend(BackupResults result, IWriteChannel<Backup.IUploadRequest> uploadtarget, Task uploader)
        {
            var flushReq = new Backup.FlushRequest();

            // Wait for upload completion
            result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_WaitForUpload);
            await uploadtarget.WriteAsync(flushReq).ConfigureAwait(false);
            await uploader.ConfigureAwait(false);

            // Grab the size of the last uploaded volume
            return await flushReq.LastWriteSizeAsync;
        }

        private async Task RunAsync(string[] sources, Library.Utility.IFilter filter)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Begin);                        
            
            // New isolated scope for each operation
            using(new IsolatedChannelScope())
            using(m_database = new LocalBackupDatabase(m_options.Dbpath, m_options))
            {
                m_result.SetDatabase(m_database);
                m_result.Dryrun = m_options.Dryrun;

                // Check the database integrity
                Utility.UpdateOptionsFromDb(m_database, m_options);
                Utility.VerifyParameters(m_database, m_options);

                var probe_path = m_database.GetFirstPath();
                if (probe_path != null && Util.GuessDirSeparator(probe_path) != Util.DirectorySeparatorString)
                    throw new UserInformationException(string.Format("The backup contains files that belong to another operating system. Proceeding with a backup would cause the database to contain paths from two different operation systems, which is not supported. To proceed without losing remote data, delete all filesets and make sure the --{0} option is set, then run the backup again to re-use the existing data on the remote store.", "no-auto-compact"), "CrossOsDatabaseReuseNotSupported");

                if (m_database.PartiallyRecreated)
                    throw new UserInformationException("The database was only partially recreated. This database may be incomplete and the repair process is not allowed to alter remote files as that could result in data loss.", "DatabaseIsPartiallyRecreated");
                
                if (m_database.RepairInProgress)
                    throw new UserInformationException("The database was attempted repaired, but the repair did not complete. This database may be incomplete and the backup process cannot continue. You may delete the local database and attempt to repair it again.", "DatabaseRepairInProgress");

                // If there is no filter, we set an empty filter to simplify the code
                // If there is a filter, we make sure that the sources are included
                m_filter = filter ?? new Library.Utility.FilterExpression();
                m_sourceFilter = new Library.Utility.FilterExpression(sources, true);

                Task parallelScanner = null;
                Task uploaderTask = null;
                try
                {
                    // Setup runners and instances here
                    using(var db = new Backup.BackupDatabase(m_database, m_options))
                    using(var backendManager = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, m_database))
                    using(var filesetvolume = new FilesetVolumeWriter(m_options, m_database.OperationTimestamp))
                    using(var stats = new Backup.BackupStatsCollector(m_result))
                    using(var bk = new Common.BackendHandler(m_options, m_backendurl, db, stats, m_result.TaskReader))
                    // Keep a reference to these channels to avoid shutdown
                    using(var uploadtarget = ChannelManager.GetChannel(Backup.Channels.BackendRequest.ForWrite))
                    {
                        long filesetid;
                        var counterToken = new CancellationTokenSource();
                        var uploader = new Backup.BackendUploader(() => DynamicLoader.BackendLoader.GetBackend(m_backendurl, m_options.RawOptions), m_options, db, m_result.TaskReader, stats);
                        using (var snapshot = GetSnapshot(sources, m_options))
                        {
                            try
                            {
                                // Start parallel scan, or use the database
                                if (m_options.DisableFileScanner)
                                {
                                    var d = m_database.GetLastBackupFileCountAndSize();
                                    m_result.OperationProgressUpdater.UpdatefileCount(d.Item1, d.Item2, true);
                                }
                                else
                                {
                                    parallelScanner = Backup.CountFilesHandler.Run(sources, snapshot, m_result, m_options, m_sourceFilter, m_filter, m_result.TaskReader, counterToken.Token);
                                }

                                // Make sure the database is sane
                                await db.VerifyConsistencyAsync(m_options.Blocksize, m_options.BlockhashSize, !m_options.DisableFilelistConsistencyChecks);

                                // Start the uploader process
                                uploaderTask = uploader.Run();

                                // If we have an interrupted backup, grab the 
                                string lasttempfilelist = null;
                                long lasttempfileid = -1;
                                if (!m_options.DisableSyntheticFilelist)
                                {
                                    var candidates = (await db.GetIncompleteFilesetsAsync()).OrderBy(x => x.Value).ToArray();
                                    if (candidates.Length > 0)
                                    {
                                        lasttempfileid = candidates.Last().Key;
                                        lasttempfilelist = m_database.GetRemoteVolumeFromID(lasttempfileid).Name;
                                    }
                                }

                                // TODO: Rewrite to using the uploader process, or the BackendHandler interface
                                // Do a remote verification, unless disabled
                                PreBackupVerify(backendManager, lasttempfilelist);

                                // If the previous backup was interrupted, send a synthetic list
                                await Backup.UploadSyntheticFilelist.Run(db, m_options, m_result, m_result.TaskReader, lasttempfilelist, lasttempfileid);

                                // Grab the previous backup ID, if any
                                var prevfileset = m_database.FilesetTimes.FirstOrDefault();
                                if (prevfileset.Value.ToUniversalTime() > m_database.OperationTimestamp.ToUniversalTime())
                                    throw new Exception(string.Format("The previous backup has time {0}, but this backup has time {1}. Something is wrong with the clock.", prevfileset.Value.ToLocalTime(), m_database.OperationTimestamp.ToLocalTime()));
                                
                                var lastfilesetid = prevfileset.Value.Ticks == 0 ? -1 : prevfileset.Key;

                                // Rebuild any index files that are missing
                                await Backup.RecreateMissingIndexFiles.Run(db, m_options, m_result.TaskReader);

                                // Prepare the operation by registering the filelist
                                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_ProcessingFiles);

                                var repcnt = 0;
                                while(repcnt < 100 && await db.GetRemoteVolumeIDAsync(filesetvolume.RemoteFilename) >= 0)
                                    filesetvolume.ResetRemoteFilename(m_options, m_database.OperationTimestamp.AddSeconds(repcnt++));

                                if (await db.GetRemoteVolumeIDAsync(filesetvolume.RemoteFilename) >= 0)
                                    throw new Exception("Unable to generate a unique fileset name");

                                var filesetvolumeid = await db.RegisterRemoteVolumeAsync(filesetvolume.RemoteFilename, RemoteVolumeType.Files, RemoteVolumeState.Temporary);
                                filesetid = await db.CreateFilesetAsync(filesetvolumeid, VolumeBase.ParseFilename(filesetvolume.RemoteFilename).Time);

                                // create USN-based scanner if enabled
                                var journalService = GetJournalService(sources, snapshot, filter, lastfilesetid);

                                // Run the backup operation
                                if (await m_result.TaskReader.ProgressAsync)
                                    await RunMainOperation(sources, snapshot, journalService, db, stats, m_options, m_sourceFilter, m_filter, m_result, m_result.TaskReader, lastfilesetid).ConfigureAwait(false);
                            }
                            finally
                            {
                                //If the scanner is still running for some reason, make sure we kill it now 
                                counterToken.Cancel();
                            }
                        }

                        // Ensure the database is in a sane state after adding data
                        using(new Logging.Timer(LOGTAG, "VerifyConsistency", "VerifyConsistency"))
                            await db.VerifyConsistencyAsync(m_options.Blocksize, m_options.BlockhashSize, false);

                        // Send the actual filelist
                        if (await m_result.TaskReader.ProgressAsync)
                            await Backup.UploadRealFilelist.Run(m_result, db, m_options, filesetvolume, filesetid, m_result.TaskReader);

                        // Wait for upload completion
                        m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_WaitForUpload);
                        var lastVolumeSize = await FlushBackend(m_result, uploadtarget, uploaderTask).ConfigureAwait(false);

                        // Make sure we have the database up-to-date
                        await db.CommitTransactionAsync("CommitAfterUpload", false);

                        // TODO: Remove this later
                        m_transaction = m_database.BeginTransaction();
    		                                        
                        if (await m_result.TaskReader.ProgressAsync)
                            CompactIfRequired(backendManager, lastVolumeSize);

                        if (m_options.UploadVerificationFile && await m_result.TaskReader.ProgressAsync)
                        {
                            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_VerificationUpload);
                            FilelistProcessor.UploadVerificationFile(backendManager.BackendUrl, m_options, m_result.BackendWriter, m_database, m_transaction);
                        }

                        if (m_options.Dryrun)
                        {
                            m_transaction.Rollback();
                            m_transaction = null;
                        }
                        else
                        {
                            using(new Logging.Timer(LOGTAG, "CommitFinalizingBackup", "CommitFinalizingBackup"))
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
                        return;
                    }
                }
                catch (Exception ex)
                {
                    var aex = BuildException(ex, uploaderTask, parallelScanner);
                    Logging.Log.WriteErrorMessage(LOGTAG, "FatalError", ex, "Fatal error");
                    if (aex == ex)
                        throw;
                    
                    throw aex;
                }
                finally
                {
                    if (parallelScanner != null && !parallelScanner.IsCompleted)
                        parallelScanner.Wait(500);

                    // TODO: We want to commit? always?
                    if (m_transaction != null)
                        try { m_transaction.Rollback(); }
                        catch (Exception ex) { Logging.Log.WriteErrorMessage(LOGTAG, "RollbackError", ex, "Rollback error: {0}", ex.Message); }                
                }
            }
        }

        public void Dispose()
        {
            if (m_result.EndTime.Ticks == 0)
                m_result.EndTime = DateTime.UtcNow;
        }
    }
}
