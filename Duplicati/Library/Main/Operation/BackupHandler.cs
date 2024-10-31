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

using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Interface;
using System.Threading.Tasks;
using CoCoL;
using System.Threading;
using Duplicati.Library.Snapshots;
using Duplicati.Library.Utility;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using System.IO;

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

        public readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

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
                    throw new UserInformationException(Strings.Common.SnapshotFailedError(ex.Message), "SnapshotFailed", ex);
                else if (options.SnapShotStrategy == Options.OptimizationStrategy.On)
                    Logging.Log.WriteWarningMessage(LOGTAG, "SnapshotFailed", ex, Strings.Common.SnapshotFailedError(ex.Message));
                else if (options.SnapShotStrategy == Options.OptimizationStrategy.Auto)
                    Logging.Log.WriteInformationMessage(LOGTAG, "SnapshotFailed", Strings.Common.SnapshotFailedError(ex.Message));
            }

            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                return new NoSnapshotLinux(options.IgnoreAdvisoryLocking);
            }
            else if (OperatingSystem.IsWindows())
            {
                return new NoSnapshotWindows();
            }
            else
            {
                throw new NotSupportedException("Unsupported Operating System");
            }
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
            var service = new UsnJournalService(sources, snapshot, filter, m_options.FileAttributeFilter, m_options.SkipFilesLargerThan,
                journalData, cancellationTokenSource.Token);

            foreach (var volumeData in service.VolumeDataList)
            {
                if (volumeData.IsFullScan)
                {
                    if (volumeData.Exception == null || volumeData.Exception is UsnJournalSoftFailureException)
                    {
                        // soft fail
                        Logging.Log.WriteInformationMessage(LOGTAG, "SkipUsnForVolume",
                            "Performing full scan for volume \"{0}\": {1}", volumeData.Volume, volumeData.Exception?.Message);
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
                            Logging.Log.WriteWarningMessage(LOGTAG, "FailedToUseChangeJournal", volumeData.Exception,
                                "Failed to use change journal for volume \"{0}\": {1}", volumeData.Volume, volumeData.Exception.Message);
                        }
                        else
                            throw volumeData.Exception;
                    }
                }
            }

            return service;
        }

        /// <summary>
        /// Returns a list of paths that should be blacklisted
        /// </summary>
        /// <returns>The list of paths</returns>
        public static HashSet<string> GetBlacklistedPaths(Options options)
            => new HashSet<string>(Library.Utility.Utility.ClientFilenameStringComparer)
            {
                //m_options.Dbpath,
                options.Dbpath + "-journal",
            };

        /// <summary>
        /// Returns a list of paths that should be blacklisted
        /// </summary>
        /// <returns>The list of paths</returns>
        public HashSet<string> GetBlacklistedPaths()
            => GetBlacklistedPaths(m_options);

        private sealed record PreBackupVerifyResult(
            LocalBackupDatabase Database,
            BackendManager BackendManager,
            string LastTempFilelist,
            long LastTempFilesetId
        );

        /// <summary>
        /// Verifies the database and backend before starting the backup.
        /// The logic here is needed to check that the database is in a state
        /// where it can be used for the backup, and that the backend is also
        /// in the same state as the database.
        /// 
        /// If the auto-repair option is enabled, this method will attempt to
        /// call the repair method, which requires that the database is closed
        /// and re-opened.
        /// 
        /// For efficiency, the database is only closed if the repair is needed,
        /// and returned to the caller in an open state in either case.
        /// </summary>
        /// <returns>Results from the pre-backup verification</returns>
        private static async Task<PreBackupVerifyResult> PreBackupVerify(string backendurl, Options options, BackupResults result)
        {
            result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PreBackupVerify);

            // Setup variables
            LocalBackupDatabase database = null;
            BackendManager backendManager = null;

            // If we have an interrupted backup, grab the fileset
            string lastTempFilelist = null;
            long lastTempFilesetId = -1;

            using (new Logging.Timer(LOGTAG, "PreBackupVerify", "PreBackupVerify"))
            {
                try
                {
                    database = new LocalBackupDatabase(options.Dbpath, options);
                    backendManager = new BackendManager(backendurl, options, result.BackendWriter, database);

                    result.SetDatabase(database);
                    result.Dryrun = options.Dryrun;

                    // Check the database integrity
                    Utility.UpdateOptionsFromDb(database, options);
                    Utility.VerifyOptionsAndUpdateDatabase(database, options);

                    var probe_path = database.GetFirstPath();
                    if (probe_path != null && Util.GuessDirSeparator(probe_path) != Util.DirectorySeparatorString)
                        throw new UserInformationException(string.Format("The backup contains files that belong to another operating system. Proceeding with a backup would cause the database to contain paths from two different operation systems, which is not supported. To proceed without losing remote data, delete all filesets and make sure the --{0} option is set, then run the backup again to re-use the existing data on the remote store.", "no-auto-compact"), "CrossOsDatabaseReuseNotSupported");

                    if (database.PartiallyRecreated)
                        throw new UserInformationException("The database was only partially recreated. This database may be incomplete and the repair process is not allowed to alter remote files as that could result in data loss.", "DatabaseIsPartiallyRecreated");

                    if (database.RepairInProgress)
                        throw new UserInformationException("The database was attempted repaired, but the repair did not complete. This database may be incomplete and the backup process cannot continue. You may delete the local database and attempt to repair it again.", "DatabaseRepairInProgress");

                    using (var db = new Backup.BackupDatabase(database, options))
                    {
                        // Make sure the database is sane
                        await db.VerifyConsistencyAsync(options.Blocksize, options.BlockhashSize, !options.DisableFilelistConsistencyChecks);

                        if (!options.DisableSyntheticFilelist)
                        {
                            var candidates = (await db.GetIncompleteFilesetsAsync()).OrderBy(x => x.Value).ToArray();
                            if (candidates.Any())
                            {
                                lastTempFilesetId = candidates.Last().Key;
                                lastTempFilelist = database.GetRemoteVolumeFromFilesetID(lastTempFilesetId).Name;
                            }
                        }
                    }

                    try
                    {
                        if (options.NoBackendverification)
                        {
                            FilelistProcessor.VerifyLocalList(backendManager, database);
                            UpdateStorageStatsFromDatabase(result, database, options, backendManager);
                        }
                        else
                            FilelistProcessor.VerifyRemoteList(backendManager, options, database, result.BackendWriter, new string[] { lastTempFilelist }, logErrors: false);
                    }
                    catch (RemoteListVerificationException ex)
                    {
                        if (options.AutoCleanup)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "BackendVerifyFailedAttemptingCleanup", ex, "Backend verification failed, attempting automatic cleanup");
                            result.RepairResults = new RepairResults(result);

                            // Close the database to allow the repair to run, it may create a new database
                            backendManager.Dispose();
                            database.Dispose();

                            database = null;
                            backendManager = null;
                            result.SetDatabase(null);
                            new RepairHandler(backendurl, options, (RepairResults)result.RepairResults).Run();

                            // Re-open the database and backend manager
                            database = new LocalBackupDatabase(options.Dbpath, options);
                            backendManager = new BackendManager(backendurl, options, result.BackendWriter, database);
                            result.SetDatabase(database);

                            Logging.Log.WriteInformationMessage(LOGTAG, "BackendCleanupFinished", "Backend cleanup finished, retrying verification");
                            FilelistProcessor.VerifyRemoteList(backendManager, options, database, result.BackendWriter, new string[] { lastTempFilelist });
                        }
                        else
                            throw;
                    }

                    return new PreBackupVerifyResult(database, backendManager, lastTempFilelist, lastTempFilesetId);
                }
                catch
                {
                    backendManager?.Dispose();
                    database?.Dispose();
                    throw;
                }
            }
        }

        /// <summary>
        /// Performs the bulk of work by starting all relevant processes
        /// </summary>
        private static async Task RunMainOperation(IEnumerable<string> sources, Snapshots.ISnapshotService snapshot, UsnJournalService journalService, Backup.BackupDatabase database, Backup.BackupStatsCollector stats, Options options, IFilter sourcefilter, IFilter filter, BackupResults result, Common.ITaskReader taskreader, long filesetid, long lastfilesetid, CancellationToken token)
        {
            using (new Logging.Timer(LOGTAG, "BackupMainOperation", "BackupMainOperation"))
            {
                // Make sure the CompressionHints table is initialized, otherwise all workers will initialize it
                var unused = options.CompressionHints.Count;

                Task all;
                using (new ChannelScope())
                {
                    all = Task.WhenAll(
                        new[]
                            {
                                    Backup.DataBlockProcessor.Run(database, options, taskreader),
                                    Backup.FileBlockProcessor.Run(snapshot, options, database, stats, taskreader, token),
                                    Backup.StreamBlockSplitter.Run(options, database, taskreader),
                                    Backup.FileEnumerationProcess.Run(sources, snapshot, journalService,
                                        options.FileAttributeFilter, sourcefilter, filter, options.SymlinkPolicy,
                                        options.HardlinkPolicy, options.ExcludeEmptyFolders, options.IgnoreFilenames,
                                        GetBlacklistedPaths(options), options.ChangedFilelist, taskreader, token),
                                    Backup.FilePreFilterProcess.Run(snapshot, options, stats, database),
                                    Backup.MetadataPreProcess.Run(snapshot, options, database, lastfilesetid, token),
                                    Backup.SpillCollectorProcess.Run(options, database, taskreader),
                                    Backup.ProgressHandler.Run(result)
                            }
                            // Spawn additional block hashers
                            .Union(
                                Enumerable.Range(0, options.ConcurrencyBlockHashers - 1).Select(x =>
                                    Backup.StreamBlockSplitter.Run(options, database, taskreader))
                            )
                            // Spawn additional compressors
                            .Union(
                                Enumerable.Range(0, options.ConcurrencyCompressors - 1).Select(x =>
                                    Backup.DataBlockProcessor.Run(database, options, taskreader))
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
                        // TODO: This is technically unsupported, but the method itself works cross-platform
                        if (journalService.IsPathEnumerated(path))
                            return true;

                        if (fileSize >= 0)
                        {
                            stats.AddExaminedFile(fileSize);
                        }
                        return false;
                    });

                    // store journal data in database, unless job is being canceled
                    if (!token.IsCancellationRequested)
                    {
                        var data = journalService.VolumeDataList.Where(p => p.JournalData != null).Select(p => p.JournalData).ToList();
                        if (data.Any())
                        {
                            // always record change journal data for current fileset (entry may be dropped later if nothing is uploaded)
                            await database.CreateChangeJournalDataAsync(data);

                            // update the previous fileset's change journal entry to resume at this point in case nothing was backed up
                            await database.UpdateChangeJournalDataAsync(data, lastfilesetid);
                        }
                    }
                }

                if (token.IsCancellationRequested)
                {
                    result.PartialBackup = true;
                    Log.WriteWarningMessage(LOGTAG, "CancellationRequested", null, "Cancellation was requested by user.");
                }
                else
                {
                    result.PartialBackup = false;
                    await database.UpdateFilesetAndMarkAsFullBackupAsync(filesetid);
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
                using (var db = new LocalDeleteDatabase(m_database))
                    new DeleteHandler(backend.BackendUrl, m_options, (DeleteResults)m_result.DeleteResults).DoRun(db, ref m_transaction, true, currentIsSmall, backend);

            }
            else if (currentIsSmall && !m_options.NoAutoCompact)
            {
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Compact);
                m_result.CompactResults = new CompactResults(m_result);
                using (var db = new LocalDeleteDatabase(m_database))
                    new CompactHandler(backend.BackendUrl, m_options, (CompactResults)m_result.CompactResults).DoCompact(db, true, ref m_transaction, backend);
            }
        }

        private void PostBackupVerification(string currentFilelistVolume)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PostBackupVerify);
            using (var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, m_database))
            {
                using (new Logging.Timer(LOGTAG, "AfterBackupVerify", "AfterBackupVerify"))
                    FilelistProcessor.VerifyRemoteList(backend, m_options, m_database, m_result.BackendWriter, new string[] { currentFilelistVolume });
                backend.WaitForComplete(m_database, null);
            }

            long remoteVolumeCount = m_database.GetRemoteVolumes().LongCount(x => x.State == RemoteVolumeState.Verified);
            long samplesToTest = Math.Max(m_options.BackupTestSampleCount, (long)Math.Round(remoteVolumeCount * (m_options.BackupTestPercentage / 100m), MidpointRounding.AwayFromZero));
            if (samplesToTest > 0 && remoteVolumeCount > 0)
            {
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PostBackupTest);
                m_result.TestResults = new TestResults(m_result);

                using (var testdb = new LocalTestDatabase(m_database))
                using (var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, testdb))
                    new TestHandler(m_backendurl, m_options, (TestResults)m_result.TestResults)
                        .DoRun(samplesToTest, testdb, backend);
            }
        }

        /// <summary>
        /// Handler for computing backend statistics, without relying on a remote folder listing
        /// </summary>
        private static void UpdateStorageStatsFromDatabase(BackupResults result, LocalBackupDatabase database, Options options, BackendManager backendManager)
        {
            if (result.BackendWriter != null)
            {
                result.BackendWriter.KnownFileCount = database.GetRemoteVolumes().Count();
                result.BackendWriter.KnownFileSize = database.GetRemoteVolumes().Select(x => Math.Max(0, x.Size)).Sum();

                result.BackendWriter.UnknownFileCount = 0;
                result.BackendWriter.UnknownFileSize = 0;

                result.BackendWriter.BackupListCount = database.FilesetTimes.Count();
                result.BackendWriter.LastBackupDate = database.FilesetTimes.FirstOrDefault().Value.ToLocalTime();

                if (!options.QuotaDisable)
                {
                    var quota = backendManager.GetQuotaInfoAsync(CancellationToken.None).Await();
                    if (quota != null)
                    {
                        result.BackendWriter.TotalQuotaSpace = quota.TotalQuotaSpace;
                        result.BackendWriter.FreeQuotaSpace = quota.FreeQuotaSpace;
                    }
                }

                result.BackendWriter.AssignedQuotaSpace = options.QuotaSize;
            }
        }

        public void Run(string[] sources, Library.Utility.IFilter filter, CancellationToken token)
        {
            RunAsync(sources, filter, token).WaitForTaskOrThrow();
        }

        private static Exception BuildException(Exception source, params Task[] tasks)
        {
            if (tasks == null || tasks.Length == 0)
                return source;

            var ex = new List<Exception>();
            ex.Add(source);

            foreach (var t in tasks)
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
            // Wait for upload completion
            result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_WaitForUpload);

            if (!await uploadtarget.IsRetiredAsync)
            {
                try
                {
                    var flushReq = new Backup.FlushRequest();
                    await uploadtarget.WriteAsync(flushReq).ConfigureAwait(false);
                    await uploader.ConfigureAwait(false);
                    // Grab the size of the last uploaded volume
                    return await flushReq.LastWriteSizeAsync;
                }
                catch (RetiredException)
                {
                    // Retired check is not atomic, so this exception can still happen
                }
            }
            await uploader.ConfigureAwait(false);
            return -1;
        }

        private async Task RunAsync(string[] sources, Library.Utility.IFilter filter, CancellationToken token)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Begin);

            // Do a remote verification, unless disabled
            var (database, backendManager, lastTempFilelist, lastTempFilesetId) = await PreBackupVerify(m_backendurl, m_options, m_result);

            // New isolated scope for each operation
            using (new IsolatedChannelScope())
            {
                // If there is no filter, we set an empty filter to simplify the code
                // If there is a filter, we make sure that the sources are included
                m_filter = filter ?? new Library.Utility.FilterExpression();
                m_sourceFilter = new Library.Utility.FilterExpression(sources, true);

                Task parallelScanner = null;
                Task uploaderTask = null;
                try
                {
                    using (m_database = database)
                    using (backendManager)
                    using (var db = new Backup.BackupDatabase(m_database, m_options))
                    // Setup runners and instances here
                    using (var filesetvolume = new FilesetVolumeWriter(m_options, m_database.OperationTimestamp))
                    using (var stats = new Backup.BackupStatsCollector(m_result))
                    // Keep a reference to these channels to avoid shutdown
                    using (var uploadtarget = ChannelManager.GetChannel(Backup.Channels.BackendRequest.ForWrite))
                    {
                        long filesetid;
                        var counterToken = new CancellationTokenSource();
                        var uploader = new Backup.BackendUploader(() => DynamicLoader.BackendLoader.GetBackend(m_backendurl, m_options.RawOptions), m_options, db, m_result.TaskReader, stats);
                        using (var snapshot = GetSnapshot(sources, m_options))
                        {
                            try
                            {
                                // Start the uploader process
                                uploaderTask = uploader.Run();

                                // If the previous backup was interrupted, send a synthetic list
                                await Backup.UploadSyntheticFilelist.Run(db, m_options, m_result, m_result.TaskReader, lastTempFilelist, lastTempFilesetId);

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
                                while (repcnt < 100 && await db.GetRemoteVolumeIDAsync(filesetvolume.RemoteFilename) >= 0)
                                    filesetvolume.ResetRemoteFilename(m_options, m_database.OperationTimestamp.AddSeconds(repcnt++));

                                if (await db.GetRemoteVolumeIDAsync(filesetvolume.RemoteFilename) >= 0)
                                    throw new Exception("Unable to generate a unique fileset name");

                                var filesetvolumeid = await db.RegisterRemoteVolumeAsync(filesetvolume.RemoteFilename, RemoteVolumeType.Files, RemoteVolumeState.Temporary);
                                filesetid = await db.CreateFilesetAsync(filesetvolumeid, VolumeBase.ParseFilename(filesetvolume.RemoteFilename).Time);

                                // create USN-based scanner if enabled
                                var journalService = GetJournalService(sources, snapshot, filter, lastfilesetid);

                                // Start parallel scan, or use the database
                                if (m_options.DisableFileScanner)
                                {
                                    var d = m_database.GetLastBackupFileCountAndSize();
                                    m_result.OperationProgressUpdater.UpdatefileCount(d.Item1, d.Item2, true);
                                }
                                else
                                {
                                    parallelScanner = Backup.CountFilesHandler.Run(sources, snapshot, journalService, m_result, m_options, m_sourceFilter, m_filter, GetBlacklistedPaths(), m_result.TaskReader, counterToken.Token);
                                }

                                // Run the backup operation
                                if (await m_result.TaskReader.ProgressAsync)
                                {
                                    await RunMainOperation(sources, snapshot, journalService, db, stats, m_options, m_sourceFilter, m_filter, m_result, m_result.TaskReader, filesetid, lastfilesetid, token).ConfigureAwait(false);
                                }
                            }
                            finally
                            {
                                //If the scanner is still running for some reason, make sure we kill it now 
                                counterToken.Cancel();
                            }
                        }

                        // Add the fileset file to the dlist file
                        filesetvolume.CreateFilesetFile(!token.IsCancellationRequested);

                        // Ensure the database is in a sane state after adding data
                        using (new Logging.Timer(LOGTAG, "VerifyConsistency", "VerifyConsistency"))
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

                        try
                        {
                            // If this throws, we should roll back the transaction
                            if (await m_result.TaskReader.ProgressAsync)
                                CompactIfRequired(backendManager, lastVolumeSize);

                            if (m_options.UploadVerificationFile && await m_result.TaskReader.ProgressAsync)
                            {
                                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_VerificationUpload);
                                FilelistProcessor.UploadVerificationFile(backendManager.BackendUrl, m_options, m_result.BackendWriter, m_database, m_transaction);
                            }
                        }
                        catch
                        {
                            try
                            {
                                m_transaction.Rollback();
                                m_transaction.Dispose();
                                m_transaction = null;
                            }
                            catch (Exception ex)
                            {
                                Logging.Log.WriteErrorMessage(LOGTAG, "RollbackError", ex, "Rollback error: {0}", ex.Message);
                            }

                            // Re-throw the original exception
                            throw;
                        }

                        if (m_options.Dryrun)
                        {
                            m_transaction.Rollback();
                            m_transaction = null;
                        }
                        else
                        {
                            using (new Logging.Timer(LOGTAG, "CommitFinalizingBackup", "CommitFinalizingBackup"))
                                m_transaction.Commit();

                            m_transaction = null;

                            if (m_result.TaskControlRendevouz() != TaskControlState.Abort)
                            {
                                if (m_options.NoBackendverification)
                                    UpdateStorageStatsFromDatabase(m_result, m_database, m_options, backendManager);
                                else
                                    PostBackupVerification(filesetvolume.RemoteFilename);
                            }
                        }

                        m_database.WriteResults();
                        m_database.PurgeLogData(m_options.LogRetention);
                        m_database.PurgeDeletedVolumes(DateTime.UtcNow);

                        if (m_options.AutoVacuum)
                        {
                            m_result.VacuumResults = new VacuumResults(m_result);
                            new VacuumHandler(m_options, (VacuumResults)m_result.VacuumResults).Run();
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
