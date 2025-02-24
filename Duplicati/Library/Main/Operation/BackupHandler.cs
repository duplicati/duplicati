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
using Duplicati.Library.Main.Operation.Common;
using System.Data;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.SourceProvider;
using Duplicati.Library.Snapshots.USN;
using System.Text.RegularExpressions;
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
        private readonly ITaskReader m_taskReader;

        public readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        public BackupHandler(string backendurl, Options options, BackupResults results)
        {
            m_options = options;
            m_result = results;
            m_backendurl = backendurl;
            m_taskReader = results.TaskControl;

            if (options.AllowPassphraseChange)
                throw new UserInformationException(Strings.Common.PassphraseChangeUnsupported, "PassphraseChangeUnsupported");
        }

        /// <summary>
        /// Gets a single source provider for the given sources
        /// </summary>
        /// <param name="sources">The sources to get providers for</param>
        /// <param name="options">The options to use</param>
        /// <returns>The source providers</returns>
        public static async Task<ISourceProvider> GetSourceProvider(IEnumerable<string> sources, Options options, CancellationToken cancellationToken)
            => Combiner.Combine(await GetSourceProviders(sources, options, cancellationToken));

        /// <summary>
        /// Gets a snapshot service for the given sources
        /// </summary>
        /// <param name="sources">The sources to get the snapshot for</param>
        /// <param name="options">The options to use</param>
        /// <returns>The source provider</returns>
        private static ISnapshotService GetFileSnapshotService(IEnumerable<string> sources, Options options)
        {
            try
            {
                if (options.SnapShotStrategy != Options.OptimizationStrategy.Off)
                    return SnapshotUtility.CreateSnapshot(sources, options.RawOptions, options.SymlinkPolicy == Options.SymlinkStrategy.Follow);
            }
            catch (Exception ex)
            {
                if (options.SnapShotStrategy == Options.OptimizationStrategy.Required)
                    throw new UserInformationException(Strings.Common.SnapshotFailedError(ex.Message), "SnapshotFailed", ex);
                else if (options.SnapShotStrategy == Options.OptimizationStrategy.On)
                    Log.WriteWarningMessage(LOGTAG, "SnapshotFailed", ex, Strings.Common.SnapshotFailedError(ex.Message));
                else if (options.SnapShotStrategy == Options.OptimizationStrategy.Auto)
                    Log.WriteInformationMessage(LOGTAG, "SnapshotFailed", Strings.Common.SnapshotFailedError(ex.Message));
            }

            return SnapshotUtility.CreateNoSnapshot(sources, options.IgnoreAdvisoryLocking, options.SymlinkPolicy == Options.SymlinkStrategy.Follow);
        }

        /// <summary>
        /// Gets all source providers for the given sources
        /// </summary>
        /// <param name="sources">The sources to get providers for</param>
        /// <param name="options">The options to use</param>
        /// <param name="filter">The filter to use</param>
        /// <param name="lastfilesetid">The last fileset id</param>
        /// <returns>The source providers</returns>
        private static async Task<List<ISourceProvider>> GetSourceProviders(IEnumerable<string> sources, Options options, CancellationToken cancellationToken)
        {
            // Group the sources by their type, so we can combine all snapshot paths into a single snapshot
            var sourceTypes = sources.GroupBy(x => x.StartsWith("@") ? "@" : Library.Utility.Utility.GuessScheme(x) ?? "file", StringComparer.OrdinalIgnoreCase);

            // To avoid leaking snapshot instances, we create all instances first and then dispose them if an exception occurs
            // The number of instances is expected to be low, so the memory overhead is acceptable
            var results = new List<ISourceProvider>();
            try
            {
                foreach (var entry in sourceTypes)
                {
                    if ("file".Equals(entry.Key, StringComparison.OrdinalIgnoreCase))
                        results.Add(new LocalFileSource(GetFileSnapshotService(entry, options)));
                    else if ("vss".Equals(entry.Key, StringComparison.OrdinalIgnoreCase) || "lvm".Equals(entry.Key, StringComparison.OrdinalIgnoreCase))
                        results.Add(new LocalFileSource(SnapshotUtility.CreateSnapshot(entry, options.RawOptions, options.SymlinkPolicy == Options.SymlinkStrategy.Follow)));
                    else if ("@".Equals(entry.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var url in entry)
                        {
                            var sanitizedUrl = Library.Utility.Utility.GetUrlWithoutCredentials(url);
                            var m = Regex.Match(url, @"^@(?<mountpoint>[^|]+)\|(?<url>.+)$", RegexOptions.IgnoreCase);
                            if (m.Success)
                            {
                                var mountpoint = m.Groups["mountpoint"].Value;

                                if (mountpoint.Any(x => Path.GetInvalidPathChars().Contains(x)))
                                    throw new UserInformationException(string.Format("The mountpoint \"{0}\" contains invalid characters", mountpoint), "InvalidMountpoint");
                                if (!Path.IsPathRooted(mountpoint))
                                    throw new UserInformationException(string.Format("The mountpoint \"{0}\" is not a valid rooted mountpoint", mountpoint), "InvalidMountpoint");

                                var backendurl = m.Groups["url"].Value;

                                ISourceProvider provider;
                                try
                                {
                                    provider = await SourceProviderLoader.GetSourceProvider(backendurl, Path.GetFullPath(mountpoint), options.RawOptions, cancellationToken).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    if (options.AllowMissingSource)
                                    {
                                        Log.WriteWarningMessage(LOGTAG, "SourceProviderFailed", ex, "Failed to load source provider for \"{0}\"", sanitizedUrl);
                                        continue;
                                    }

                                    throw new UserInformationException($"Failed to load source provider for \"{sanitizedUrl}\": {ex.Message}", "SourceProviderFailed", ex);
                                }

                                // Don't accept missing providers
                                results.Add(provider ?? throw new UserInformationException($"The source \"{sanitizedUrl}\" is not supported", "SourceNotSupported"));
                            }
                            else
                                throw new UserInformationException($"The source \"{sanitizedUrl}\" is not a supported format", "SourceFormatNotSupported");
                        }
                    }
                    else
                        throw new UserInformationException($"The source type \"{entry.Key}\" is not supported", "SourceTypeNotSupported");
                }
            }
            catch
            {
                foreach (var provider in results)
                    (provider as IDisposable)?.Dispose();

                throw;
            }

            if (results.Count == 0)
                throw new UserInformationException("No sources were available for the backup", "NoSourcesAvailable");

            return results;
        }

        public UsnJournalService GetJournalService(ISourceProvider provider, IFilter filter, long lastfilesetid)
        {
            if (m_options.UsnStrategy == Options.OptimizationStrategy.Off) return null;
            if (!OperatingSystem.IsWindows())
                throw new UserInformationException("USN journal is only supported on Windows", "UsnJournalNotSupported");

            var providers = (provider is Combiner c ? c.Providers : [provider])
                .Select((x, i) => new { Provider = x, Index = i })
                .Where(x => x.Provider is LocalFileSource)
                .ToList();

            if (providers.Count == 0)
                return null;
            if (providers.Count > 1)
                throw new UserInformationException("Multiple USN journal services are not supported", "MultipleUSNJournalServices");
            var fileProvider = providers.First().Provider as LocalFileSource;

            var journalData = m_database.GetChangeJournalData(lastfilesetid);
            var service = new UsnJournalService(fileProvider.SnapshotService, filter, m_options.FileAttributeFilter, m_options.SkipFilesLargerThan,
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
        private static async Task<PreBackupVerifyResult> PreBackupVerify(Options options, BackupResults result, IBackendManager backendManager)
        {
            result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PreBackupVerify);

            // Setup variables
            LocalBackupDatabase database = null;

            // If we have an interrupted backup, grab the fileset
            string lastTempFilelist = null;
            long lastTempFilesetId = -1;

            using (new Logging.Timer(LOGTAG, "PreBackupVerify", "PreBackupVerify"))
            {
                try
                {
                    database = new LocalBackupDatabase(options.Dbpath, options);
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
                            await FilelistProcessor.VerifyLocalList(backendManager, database, result.TaskControl.ProgressToken).ConfigureAwait(false);
                            await UpdateStorageStatsFromDatabase(result, database, options, backendManager, result.TaskControl.ProgressToken).ConfigureAwait(false);
                        }
                        else
                            await FilelistProcessor.VerifyRemoteList(backendManager, options, database, result.BackendWriter, new string[] { lastTempFilelist }, logErrors: false).ConfigureAwait(false);
                    }
                    catch (RemoteListVerificationException ex)
                    {
                        if (options.AutoCleanup)
                        {
                            Log.WriteWarningMessage(LOGTAG, "BackendVerifyFailedAttemptingCleanup", ex, "Backend verification failed, attempting automatic cleanup");
                            result.RepairResults = new RepairResults(result);
                            database.Dispose();

                            database = null;
                            result.SetDatabase(null);
                            new RepairHandler(options, (RepairResults)result.RepairResults).Run(backendManager, null);

                            // Re-open the database and backend manager
                            database = new LocalBackupDatabase(options.Dbpath, options);
                            result.SetDatabase(database);

                            Log.WriteInformationMessage(LOGTAG, "BackendCleanupFinished", "Backend cleanup finished, retrying verification");
                            await FilelistProcessor.VerifyRemoteList(backendManager, options, database, result.BackendWriter, new string[] { lastTempFilelist }).ConfigureAwait(false);
                        }
                        else
                            throw;
                    }

                    return new PreBackupVerifyResult(database, lastTempFilelist, lastTempFilesetId);
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
        private static async Task RunMainOperation(Backup.Channels channels, ISourceProvider source, UsnJournalService journalService, Backup.BackupDatabase database, IBackendManager backendManager, Backup.BackupStatsCollector stats, Options options, IFilter sourcefilter, IFilter filter, BackupResults result, ITaskReader taskreader, long filesetid, long lastfilesetid)
        {
            using (new Logging.Timer(LOGTAG, "BackupMainOperation", "BackupMainOperation"))
            {
                // Make sure the CompressionHints table is initialized, otherwise all workers will initialize it
                var unused = options.CompressionHints.Count;

                Task all = Task.WhenAll(
                    new[]
                        {
                                Backup.DataBlockProcessor.Run(channels, database, backendManager, options, taskreader),
                                Backup.FileBlockProcessor.Run(channels, options, database, stats, taskreader),
                                Backup.StreamBlockSplitter.Run(channels, options, database, taskreader),
                                Backup.FileEnumerationProcess.Run(channels, source, journalService,
                                    options.FileAttributeFilter, filter, options.SymlinkPolicy,
                                    options.HardlinkPolicy, options.ExcludeEmptyFolders, options.IgnoreFilenames,
                                    GetBlacklistedPaths(options), options.ChangedFilelist, taskreader,
                                    () => result.PartialBackup = true, CancellationToken.None),
                                Backup.FilePreFilterProcess.Run(channels, options, stats, database),
                                Backup.MetadataPreProcess.Run(channels, options, database, lastfilesetid, taskreader),
                                Backup.SpillCollectorProcess.Run(channels, options, database, backendManager, taskreader),
                                Backup.ProgressHandler.Run(channels, result)
                        }
                        // Spawn additional block hashers
                        .Concat(
                            Enumerable.Range(0, options.ConcurrencyBlockHashers - 1).Select(x =>
                                Backup.StreamBlockSplitter.Run(channels, options, database, taskreader))
                        )
                        // Spawn additional compressors
                        .Concat(
                            Enumerable.Range(0, options.ConcurrencyCompressors - 1).Select(x =>
                                Backup.DataBlockProcessor.Run(channels, database, backendManager, options, taskreader))
                        )
                        // Spawn additional file processors
                        .Concat(
                            Enumerable.Range(0, options.ConcurrencyFileprocessors - 1).Select(x =>
                                Backup.FileBlockProcessor.Run(channels, options, database, stats, taskreader)
                        )
                    )
                );

                await all.ConfigureAwait(false);

                if (options.ChangedFilelist != null && options.ChangedFilelist.Length >= 1)
                {
                    await database.AppendFilesFromPreviousSetAsync(options.DeletedFilelist);
                }
                else if (journalService != null)
                {
                    if (!OperatingSystem.IsWindows())
                        throw new UserInformationException("USN journal is only supported on Windows", "USNJournalNotSupported");

                    // append files from previous fileset, unless part of modifiedSources, which we've just scanned
                    await database.AppendFilesFromPreviousSetWithPredicateAsync((path, fileSize) =>
                    {
                        if (!OperatingSystem.IsWindows())
                            throw new UserInformationException("USN journal is only supported on Windows", "USNJournalNotSupported");

                        // TODO: This is technically unsupported, but the method itself works cross-platform
                        if (journalService.IsPathEnumerated(path))
                            return true;

                        if (fileSize >= 0)
                            stats.AddExaminedFile(fileSize);

                        return false;
                    });

                    // store journal data in database, unless job is being canceled
                    if (!result.PartialBackup)
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

                if (result.PartialBackup)
                {
                    Log.WriteWarningMessage(LOGTAG, "CancellationRequested", null, "Cancellation was requested by user.");
                }
                else
                {
                    await database.UpdateFilesetAndMarkAsFullBackupAsync(filesetid);
                }

                result.OperationProgressUpdater.UpdatefileCount(result.ExaminedFiles, result.SizeOfExaminedFiles, true);
            }
        }

        private async Task CompactIfRequired(IBackendManager backendManager, long lastVolumeSize)
        {
            var currentIsSmall = lastVolumeSize != -1 && lastVolumeSize <= m_options.SmallFileSize;

            if (m_options.KeepTime.Ticks > 0 || m_options.KeepVersions != 0 || m_options.RetentionPolicy.Count > 0)
            {
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Delete);
                m_result.DeleteResults = new DeleteResults(m_result);
                using (var db = new LocalDeleteDatabase(m_database))
                    new DeleteHandler(m_options, (DeleteResults)m_result.DeleteResults).DoRun(db, ref m_transaction, true, currentIsSmall, backendManager);

            }
            else if (currentIsSmall && !m_options.NoAutoCompact)
            {
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Compact);
                m_result.CompactResults = new CompactResults(m_result);
                using (var db = new LocalDeleteDatabase(m_database))
                {
                    (var _, var tr) = await new CompactHandler(m_options, (CompactResults)m_result.CompactResults).DoCompact(db, true, m_transaction, backendManager);
                    m_transaction = tr;
                }
            }
        }

        private async Task PostBackupVerification(string currentFilelistVolume, IBackendManager backendManager)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PostBackupVerify);
            using (new Logging.Timer(LOGTAG, "AfterBackupVerify", "AfterBackupVerify"))
                await FilelistProcessor.VerifyRemoteList(backendManager, m_options, m_database, m_result.BackendWriter, new string[] { currentFilelistVolume }).ConfigureAwait(false);
            await backendManager.WaitForEmptyAsync(m_database, m_transaction, m_taskReader.ProgressToken);

            long remoteVolumeCount = m_database.GetRemoteVolumes().LongCount(x => x.State == RemoteVolumeState.Verified);
            long samplesToTest = Math.Max(m_options.BackupTestSampleCount, (long)Math.Round(remoteVolumeCount * (m_options.BackupTestPercentage / 100m), MidpointRounding.AwayFromZero));
            if (samplesToTest > 0 && remoteVolumeCount > 0)
            {
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PostBackupTest);
                m_result.TestResults = new TestResults(m_result);

                using (var testdb = new LocalTestDatabase(m_database))
                    await new TestHandler(m_options, (TestResults)m_result.TestResults)
                        .DoRun(samplesToTest, testdb, backendManager).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Handler for computing backend statistics, without relying on a remote folder listing
        /// </summary>
        private static async Task UpdateStorageStatsFromDatabase(BackupResults result, LocalBackupDatabase database, Options options, IBackendManager backendManager, CancellationToken cancelToken)
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
                    var quota = await backendManager.GetQuotaInfoAsync(cancelToken).ConfigureAwait(false);
                    if (quota != null)
                    {
                        result.BackendWriter.TotalQuotaSpace = quota.TotalQuotaSpace;
                        result.BackendWriter.FreeQuotaSpace = quota.FreeQuotaSpace;
                    }
                }

                result.BackendWriter.AssignedQuotaSpace = options.QuotaSize;
            }
        }

        private static Exception BuildException(Exception source, params Task[] tasks)
        {
            if (tasks == null || tasks.Length == 0)
                return source;

            var ex = new List<Exception>
            {
                source
            };

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

        private static async Task<long> FlushBackend(LocalDatabase database, IDbTransaction transaction, BackupResults result, IBackendManager backendManager)
        {
            // Wait for upload completion
            result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_WaitForUpload);

            try
            {
                await backendManager.WaitForEmptyAsync(database, transaction, result.TaskControl.ProgressToken).ConfigureAwait(false);
                // Grab the size of the last uploaded volume
                return backendManager.LastWriteSize;
            }
            catch (RetiredException)
            {
            }

            return -1;
        }

        public async Task RunAsync(string[] sources, IBackendManager backendManager, Library.Utility.IFilter filter)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Begin);

            // Do a remote verification, unless disabled
            var (database, lastTempFilelist, lastTempFilesetId) = await PreBackupVerify(m_options, m_result, backendManager);

            Backup.Channels channels = new();

            // If there is no filter, we set an empty filter to simplify the code
            // If there is a filter, we make sure that the sources are included
            m_filter = filter ?? new Library.Utility.FilterExpression();
            m_sourceFilter = new Library.Utility.FilterExpression(sources, true);

            Task parallelScanner = null;
            try
            {
                using (m_database = database)
                using (var db = new Backup.BackupDatabase(m_database, m_options))
                // Setup runners and instances here
                using (var filesetvolume = new FilesetVolumeWriter(m_options, m_database.OperationTimestamp))
                using (var stats = new Backup.BackupStatsCollector(m_result))
                {
                    long filesetid;
                    using var counterToken = CancellationTokenSource.CreateLinkedTokenSource(m_taskReader.ProgressToken);
                    using (var source = await GetSourceProvider(sources, m_options, m_taskReader.ProgressToken).ConfigureAwait(false))
                    {
                        try
                        {
                            // If the previous backup was interrupted, send a synthetic list
                            await Backup.UploadSyntheticFilelist.Run(db, m_options, m_result, m_result.TaskControl, backendManager, lastTempFilelist, lastTempFilesetId);

                            // Grab the previous backup ID, if any
                            var prevfileset = m_database.FilesetTimes.FirstOrDefault();
                            if (prevfileset.Value.ToUniversalTime() > m_database.OperationTimestamp.ToUniversalTime())
                                throw new Exception(string.Format("The previous backup has time {0}, but this backup has time {1}. Something is wrong with the clock.", prevfileset.Value.ToLocalTime(), m_database.OperationTimestamp.ToLocalTime()));

                            var lastfilesetid = prevfileset.Value.Ticks == 0 ? -1 : prevfileset.Key;

                            // Rebuild any index files that are missing
                            await Backup.RecreateMissingIndexFiles.Run(db, backendManager, m_options, m_result.TaskControl);

                            // Prepare the operation by registering the filelist
                            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_ProcessingFiles);

                            var repcnt = 0;
                            while (repcnt < 100 && await db.GetRemoteVolumeIDAsync(filesetvolume.RemoteFilename) >= 0)
                                filesetvolume.ResetRemoteFilename(m_options, m_database.OperationTimestamp.AddSeconds(repcnt++));

                            if (await db.GetRemoteVolumeIDAsync(filesetvolume.RemoteFilename) >= 0)
                                throw new Exception("Unable to generate a unique fileset name");

                            var filesetvolumeid = await db.RegisterRemoteVolumeAsync(filesetvolume.RemoteFilename, RemoteVolumeType.Files, RemoteVolumeState.Temporary);
                            filesetid = await db.CreateFilesetAsync(filesetvolumeid, VolumeBase.ParseFilename(filesetvolume.RemoteFilename).Time);

                            var journalService = GetJournalService(source, filter, lastfilesetid);

                            // Start parallel scan, or use the database
                            if (m_options.DisableFileScanner)
                            {
                                var d = m_database.GetLastBackupFileCountAndSize();
                                m_result.OperationProgressUpdater.UpdatefileCount(d.Item1, d.Item2, true);
                            }
                            else
                            {
                                parallelScanner = Backup.CountFilesHandler.Run(source, journalService, m_result, m_options, m_filter, GetBlacklistedPaths(), m_result.TaskControl, counterToken.Token);
                            }

                            // Run the backup operation
                            if (await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                            {
                                await RunMainOperation(channels, source, journalService, db, backendManager, stats, m_options, m_sourceFilter, m_filter, m_result, m_result.TaskControl, filesetid, lastfilesetid).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            //If the scanner is still running for some reason, make sure we kill it now
                            counterToken.Cancel();
                        }
                    }

                    // Add the fileset file to the dlist file
                    filesetvolume.CreateFilesetFile(!m_result.PartialBackup);

                    // Ensure the database is in a sane state after adding data
                    using (new Logging.Timer(LOGTAG, "VerifyConsistency", "VerifyConsistency"))
                        await db.VerifyConsistencyAsync(m_options.Blocksize, m_options.BlockhashSize, false);

                    // Send the actual filelist
                    await Backup.UploadRealFilelist.Run(m_result, db, backendManager, m_options, filesetvolume, filesetid, m_result.TaskControl);

                    // Wait for upload completion
                    m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_WaitForUpload);
                    var lastVolumeSize = await FlushBackend(m_database, m_transaction, m_result, backendManager).ConfigureAwait(false);

                    // Make sure we have the database up-to-date
                    await db.CommitTransactionAsync("CommitAfterUpload", false);

                    // TODO: Remove this later
                    m_transaction = m_database.BeginTransaction();

                    try
                    {
                        // If this throws, we should roll back the transaction
                        if (await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                            await CompactIfRequired(backendManager, lastVolumeSize);

                        if (m_options.UploadVerificationFile && await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                        {
                            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_VerificationUpload);
                            await FilelistProcessor.UploadVerificationFile(backendManager, m_options, m_database, m_transaction);
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

                        if (await m_result.TaskControl.ProgressRendevouz().ConfigureAwait(false))
                        {
                            if (m_options.NoBackendverification)
                                await UpdateStorageStatsFromDatabase(m_result, m_database, m_options, backendManager, m_taskReader.ProgressToken).ConfigureAwait(false);
                            else
                                await PostBackupVerification(filesetvolume.RemoteFilename, backendManager).ConfigureAwait(false);
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
                var aex = BuildException(ex, parallelScanner);
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

        public void Dispose()
        {
            if (m_result.EndTime.Ticks == 0)
                m_result.EndTime = DateTime.UtcNow;
        }
    }
}
