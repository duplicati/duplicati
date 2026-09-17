// Copyright (C) 2026, The Duplicati Team
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Localization.Short;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation
{
    /// <summary>
    /// Proves that a backup is restorable without relying on anything local.
    /// A sample of files from the chosen version is restored into a scratch folder using
    /// only remote data, and every restored file is verified against the hash and size
    /// recorded in the backup. The local database can be rebuilt from the remote destination
    /// into a temporary file first, so nothing local is trusted.
    /// The candidate files and the sample are kept in scratch tables in the database, so the
    /// memory use does not grow with the number of files in the backup.
    /// </summary>
    internal class RestoreTestHandler
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<RestoreTestHandler>();

        /// <summary>
        /// The prefix of the scratch folder created below the temporary path
        /// </summary>
        private const string SCRATCH_FOLDER_PREFIX = "duplicati-restore-test-";
        /// <summary>
        /// The name of the temporary database inside the scratch folder
        /// </summary>
        private const string TEMP_DATABASE_NAME = "restore-test.sqlite";
        /// <summary>
        /// The name of the folder inside the scratch folder that receives the restored files
        /// </summary>
        private const string RESTORE_FOLDER_NAME = "files";
        /// <summary>
        /// The name of the file written to check that the scratch folder is writable
        /// </summary>
        private const string WRITE_PROBE_NAME = "write-probe.tmp";
        /// <summary>
        /// The tolerance applied when comparing file timestamps
        /// </summary>
        private static readonly TimeSpan TIMESTAMP_TOLERANCE = TimeSpan.FromSeconds(2);
        /// <summary>
        /// The verification outcome recorded in the history table for a verified file
        /// </summary>
        private const string HISTORY_RESULT_PASSED = "Passed";
        /// <summary>
        /// The verification outcome recorded in the history table for a failed file
        /// </summary>
        private const string HISTORY_RESULT_FAILED = "Failed";

        private readonly Options m_options;
        private readonly RestoreTestResults m_result;

        public RestoreTestHandler(Options options, RestoreTestResults result)
        {
            m_options = options;
            m_result = result;
        }

        /// <summary>
        /// Runs the restore test.
        /// </summary>
        /// <param name="backendManager">The backend manager used to access the remote destination.</param>
        /// <param name="filter">An optional filter limiting the files that can be selected.</param>
        public async Task RunAsync(IBackendManager backendManager, IFilter? filter)
        {
            var token = m_result.TaskControl.ProgressToken;
            var stopwatch = Stopwatch.StartNew();
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.RestoreTest_Begin);

            // Read and validate the options up front, so invalid values fail before any remote access
            var mode = m_options.RestoreTestMode;
            var seed = m_options.RestoreTestSeed ?? new Random().Next();
            var requestedVersion = ResolveRequestedVersion();
            var maxDownload = m_options.RestoreTestMaxDownloadSize;
            var maxRuntime = m_options.RestoreTestMaxRuntime;
            var sampleCount = m_options.RestoreTestSampleCount;
            var samplePercent = m_options.RestoreTestSamplePercent;
            var rollingWindow = m_options.RestoreTestRollingWindow;

            m_result.Mode = mode;
            m_result.Seed = seed;
            m_result.Version = requestedVersion;

            if ((mode == RestoreTestMode.RandomFiles || mode == RestoreTestMode.Rolling) && sampleCount <= 0)
                throw new UserInformationException(LC.L("The restore test sample count must be larger than zero, got: {0}", sampleCount), "RestoreTestInvalidSampleCount");

            Logging.Log.WriteInformationMessage(LOGTAG, "RestoreTestStarting", LC.L("Starting restore test of version {0} in {1} mode with seed {2}", requestedVersion, mode, seed));

            var scratchRoot = CreateScratchFolder();
            var candidateFilter = BuildCandidateFilter(filter);

            var realDbPath = m_options.Dbpath;
            var realDbAvailable = !m_options.NoLocalDb && !string.IsNullOrWhiteSpace(realDbPath) && File.Exists(realDbPath);
            var recreate = m_options.RestoreTestRecreateDatabase || !realDbAvailable;

            try
            {
                if (!m_options.RestoreTestRecreateDatabase && !realDbAvailable)
                    Logging.Log.WriteInformationMessage(LOGTAG, "NoUsableLocalDatabase", LC.L("No usable local database was found, recreating a temporary database from the remote destination"));

                string dbPath;
                long dbVersionIndex;
                if (recreate)
                {
                    m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.RestoreTest_RecreateDatabase);
                    dbPath = Path.Combine(scratchRoot, TEMP_DATABASE_NAME);
                    m_result.DatabaseRecreated = true;
                    var recreateResults = new RecreateDatabaseResults(m_result);
                    m_result.RecreateDatabaseResults = recreateResults;

                    // Only the requested version is rebuilt, which keeps the number of downloads down
                    var filelistFilter = RestoreHandler.GetNumberedFilelistFilterDelegate(new DateTime(0, DateTimeKind.Utc), [requestedVersion]);
                    Logging.Log.WriteInformationMessage(LOGTAG, "RecreatingDatabase", LC.L("Recreating a temporary database for version {0} from the remote destination", requestedVersion));
                    using (new Logging.Timer(LOGTAG, "RecreateTempDbForRestoreTest", "Recreate temporary database for restore test"))
                        await new RecreateDatabaseHandler(m_options, recreateResults)
                            .RunAsync(dbPath, backendManager, null, filelistFilter, null)
                            .ConfigureAwait(false);

                    // The temporary database contains only the requested version
                    dbVersionIndex = 0;
                }
                else
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "UsingLocalDatabase", LC.L("Using the existing local database for the restore test"));
                    dbPath = realDbPath!;
                    dbVersionIndex = requestedVersion;
                }

                if (CheckDownloadBudget(maxDownload, 0) || CheckRuntimeBudget(stopwatch, maxRuntime))
                    return;

                // The connection stays open for the rest of the run, as the sample lives in temporary
                // tables on it. Its transaction is committed before the restore engine opens the same
                // database file, so the idle connection holds no lock while the restore runs.
                await using var db = await LocalRestoreTestDatabase.CreateAsync(dbPath, createOperation: true, token).ConfigureAwait(false);

                // Select the sample
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.RestoreTest_SelectSample);
                var filesetId = await db.GetFilesetIdForVersionAsync(dbVersionIndex, token).ConfigureAwait(false);
                if (filesetId < 0)
                {
                    var count = await db.GetFilesetCountAsync(token).ConfigureAwait(false);
                    throw new UserInformationException(LC.L("The backup version {0} does not exist, the backup has {1} version(s)", requestedVersion, count), "RestoreTestVersionNotFound");
                }

                if (mode == RestoreTestMode.Rolling && realDbAvailable)
                    await db.UseHistoryAsync(recreate ? realDbPath : null, token).ConfigureAwait(false);

                var candidates = await db.CreateCandidateTableAsync(filesetId, candidateFilter, token).ConfigureAwait(false);
                Logging.Log.WriteVerboseMessage(LOGTAG, "CandidateFiles", LC.L("Found {0} candidate file(s) in version {1}", candidates, requestedVersion));

                await db.CreateSampleTableAsync(mode, sampleCount, samplePercent, seed, DateTime.UtcNow - rollingWindow, token).ConfigureAwait(false);

                var (sampleFiles, sampleBytes) = await db.GetSampleStatisticsAsync(token).ConfigureAwait(false);
                var estimatedDownload = sampleFiles == 0 ? 0 : await db.EstimateSampleDownloadAsync(token).ConfigureAwait(false);

                m_result.FilesTested = sampleFiles;
                if (sampleFiles == 0)
                {
                    await db.Transaction.CommitAsync("RestoreTestNoSample", token: token).ConfigureAwait(false);
                    Logging.Log.WriteWarningMessage(LOGTAG, "NoFilesToTest", null, LC.L("No files were selected for the restore test, the version may be empty or the filters exclude all files"));
                    FinishStatistics();
                    return;
                }

                Logging.Log.WriteInformationMessage(LOGTAG, "SampleSelected", LC.L("Selected {0} file(s) ({1}) for the restore test, requiring an estimated {2} of remote data", sampleFiles, Library.Utility.Utility.FormatSizeString(sampleBytes), Library.Utility.Utility.FormatSizeString(estimatedDownload)));

                if (CheckDownloadBudget(maxDownload, estimatedDownload) || CheckRuntimeBudget(stopwatch, maxRuntime))
                {
                    await db.Transaction.CommitAsync("RestoreTestBudgetExceeded", token: token).ConfigureAwait(false);
                    m_result.FilesSkipped = sampleFiles;
                    FinishStatistics();
                    return;
                }

                EnsureFreeSpace(scratchRoot, sampleBytes + m_options.VolumeSize);

                // A full test restores the version with the candidate filter, exactly as a normal restore
                // would, so the restore engine strips the same path prefix as it does for the candidates.
                // A sampled test restores just the sampled paths, which are bounded by the sample size.
                var restoreFolder = Path.Combine(scratchRoot, RESTORE_FOLDER_NAME);
                Directory.CreateDirectory(restoreFolder);
                IFilter? restoreFilter;
                string prefix;
                if (mode == RestoreTestMode.Full)
                {
                    restoreFilter = candidateFilter;
                    prefix = await db.GetLargestPrefixAsync(useCandidates: true, token).ConfigureAwait(false);
                }
                else
                {
                    prefix = await db.GetLargestPrefixAsync(useCandidates: false, token).ConfigureAwait(false);
                    var paths = new List<string>();
                    await foreach (var file in db.GetSampleFilesAsync(filesetId, token).ConfigureAwait(false))
                    {
                        paths.Add(ToLiteralFilter(file.Path));

                        // The sample holds only files, so the restore has no folder entries to create the
                        // target structure from. Creating the folders here keeps the restore from reporting
                        // every folder it has to create on its own.
                        var folder = Path.GetDirectoryName(MapTargetPath(restoreFolder, prefix, file.Path));
                        if (!string.IsNullOrEmpty(folder))
                            Directory.CreateDirectory(folder);
                    }
                    restoreFilter = new FilterExpression(paths, true);
                }

                // Release the lock before the restore engine opens the database
                await db.Transaction.CommitAsync("RestoreTestSampleSelected", token: token).ConfigureAwait(false);

                // Restore the sample into the scratch folder
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.RestoreTest_Restore);
                var restoreResults = new RestoreResults(m_result);
                m_result.RestoreResults = restoreResults;
                var runtimeExceeded = false;
                string? restoreError = null;

                using (var watchdog = new RuntimeWatchdog(stopwatch, maxRuntime, m_result.TaskControl))
                {
                    using var destination = new Library.SourceProvider.FileRestoreDestinationProvider(restoreFolder, false);
                    try
                    {
                        using (new Logging.Timer(LOGTAG, "RestoreSample", "Restore sample for restore test"))
                            await new RestoreHandler(CreateRestoreOptions(dbPath, dbVersionIndex, restoreFolder), restoreResults)
                                .RunAsync([], backendManager, restoreFilter, destination)
                                .ConfigureAwait(false);

                        await destination.Finalize(null, token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!ex.IsAbortOrCancelException() && !watchdog.Fired)
                    {
                        // A damaged or missing remote volume stops the restore with an exception.
                        // The files that were restored before that are still verified, and the
                        // rest are reported as failures, so the outcome is a structured result.
                        restoreError = ex.Message;
                        Logging.Log.WriteErrorMessage(LOGTAG, "RestoreFailed", ex, LC.L("The restore of the sample failed: {0}", ex.Message));
                    }

                    runtimeExceeded = watchdog.Fired;
                }

                restoreResults.EndTime = DateTime.UtcNow;

                if (runtimeExceeded)
                    MarkBudgetExceeded(RestoreTestBudgetReason.Runtime, LC.L("The restore test exceeded the runtime budget of {0} while restoring the sample", maxRuntime));
                else if (maxDownload > 0 && m_result.BackendStatistics.BytesDownloaded > maxDownload)
                    MarkBudgetExceeded(RestoreTestBudgetReason.DownloadSize, LC.L("The restore test downloaded {0}, which exceeds the download budget of {1}", Library.Utility.Utility.FormatSizeString(m_result.BackendStatistics.BytesDownloaded), Library.Utility.Utility.FormatSizeString(maxDownload)));

                // Verify the restored files, streaming the sample from the database
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.RestoreTest_Verify);
                var brokenVolumes = new HashSet<string>(restoreResults.BrokenRemoteFiles, StringComparer.Ordinal);
                // A damaged volume is recorded by the downloader when it notices the problem, but the
                // failure can also surface later, when the volume is decrypted or read. Those failures
                // are only visible as logged errors naming the volume, so the errors are scanned as well.
                var errorMessages = m_result.RootResults.Errors?.ToList() ?? new List<string>();
                var compareSource = m_options.RestoreTestCompareSource;
                var verifyMetadata = m_options.RestoreTestVerifyMetadata;

                // The history lives in the real database, even when the restore used the temporary one.
                // The run is always recorded as a restore test operation there, which is how a later
                // backup knows when the previous restore test ran.
                LocalRestoreTestDatabase? historyDb = null;
                if (realDbAvailable && !m_options.Dryrun)
                    historyDb = recreate
                        ? await LocalRestoreTestDatabase.CreateAsync(realDbPath!, createOperation: true, token).ConfigureAwait(false)
                        : db;

                try
                {
                    using var filehasher = HashFactory.CreateHasher(m_options.FileHashAlgorithm);
                    var progress = 0L;
                    await foreach (var file in db.GetSampleFilesAsync(filesetId, token).ConfigureAwait(false))
                    {
                        // Files that could not be restored within the budget are not verified
                        if (runtimeExceeded || !await m_result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
                        {
                            m_result.FilesSkipped++;
                            continue;
                        }

                        if (CheckRuntimeBudget(stopwatch, maxRuntime))
                        {
                            runtimeExceeded = true;
                            m_result.FilesSkipped++;
                            continue;
                        }

                        progress++;
                        m_result.OperationProgressUpdater.UpdateProgress((float)progress / sampleFiles);

                        var targetPath = MapTargetPath(restoreFolder, prefix, file.Path);
                        var passed = await VerifyRestoredFileAsync(db, file, targetPath, brokenVolumes, errorMessages, restoreError, filehasher, verifyMetadata, token).ConfigureAwait(false);
                        if (passed)
                        {
                            m_result.FilesPassed++;
                            m_result.BytesRestored += file.Size;
                            if (compareSource)
                                CompareWithSource(file, filehasher);
                        }
                        else
                        {
                            m_result.FilesFailed++;
                        }

                        if (historyDb != null)
                            await historyDb.RecordVerificationAsync(file.Path, DateTime.UtcNow, passed ? HISTORY_RESULT_PASSED : HISTORY_RESULT_FAILED, token).ConfigureAwait(false);
                    }

                    if (historyDb != null)
                        await historyDb.Transaction.CommitAsync("RestoreTestHistoryWrite", token: token).ConfigureAwait(false);
                }
                finally
                {
                    if (historyDb != null && !ReferenceEquals(historyDb, db))
                        await historyDb.DisposeAsync().ConfigureAwait(false);
                }

                await db.DropScratchTablesAsync(token).ConfigureAwait(false);
                await db.Transaction.CommitAsync("RestoreTestVerified", token: token).ConfigureAwait(false);

                if (!realDbAvailable)
                    Logging.Log.WriteVerboseMessage(LOGTAG, "HistoryNotRecorded", LC.L("The restore test history was not recorded because no local database is available"));

                FinishStatistics();

                if (m_result.FilesFailed > 0)
                    Logging.Log.WriteErrorMessage(LOGTAG, "RestoreTestFailed", null, LC.L("Restore test verified {0} file(s) with {1} failure(s)", m_result.FilesTested, m_result.FilesFailed));
                else if (m_result.FilesSkipped > 0)
                    Logging.Log.WriteWarningMessage(LOGTAG, "RestoreTestIncomplete", null, LC.L("Restore test verified {0} of {1} file(s), {2} file(s) were skipped", m_result.FilesPassed, m_result.FilesTested, m_result.FilesSkipped));
                else
                    Logging.Log.WriteInformationMessage(LOGTAG, "RestoreTestPassed", LC.L("Restore test verified {0} file(s) ({1}) successfully", m_result.FilesPassed, Library.Utility.Utility.FormatSizeString(m_result.BytesRestored)));
            }
            finally
            {
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.RestoreTest_Cleanup);
                try
                {
                    if (Directory.Exists(scratchRoot))
                        Directory.Delete(scratchRoot, true);
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "ScratchCleanupFailed", ex, LC.L("Failed to remove the restore test scratch folder {0}: {1}", scratchRoot, ex.Message));
                }

                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.RestoreTest_Complete);
                m_result.EndTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Creates the options used for the restore of the sample. The restore reads from the given
        /// database and version, writes into the scratch folder, and never reuses local data, so
        /// every block comes from the remote destination.
        /// </summary>
        /// <param name="dbPath">The database to restore from</param>
        /// <param name="version">The version index in that database</param>
        /// <param name="restoreFolder">The folder to restore into</param>
        /// <returns>The restore options</returns>
        private Options CreateRestoreOptions(string dbPath, long version, string restoreFolder)
        {
            var raw = new Dictionary<string, string?>(m_options.RawOptions)
            {
                ["dbpath"] = dbPath,
                ["no-local-db"] = "false",
                ["version"] = version.ToString(CultureInfo.InvariantCulture),
                ["restore-path"] = restoreFolder,
                ["restore-with-local-blocks"] = "false",
                ["no-local-blocks"] = "true",
                ["restore-all-files"] = nameof(RestoreAllFilesMode.False),
                ["overwrite"] = "true",
            };
            raw.Remove("time");

            return new Options(raw);
        }

        /// <summary>
        /// Resolves the version to test from the restore test version option, falling back to the regular version option
        /// </summary>
        /// <returns>The 0-based version index</returns>
        private long ResolveRequestedVersion()
        {
            var version = m_options.RestoreTestVersion;
            if (version.HasValue)
                return version.Value;

            var versions = m_options.Version;
            if (versions != null && versions.Length > 0)
            {
                if (versions.Length > 1)
                    Logging.Log.WriteWarningMessage(LOGTAG, "MultipleVersions", null, LC.L("The restore test can only test one version, using version {0}", versions[0]));
                return versions[0];
            }

            return 0;
        }

        /// <summary>
        /// Builds the filter limiting the candidate files from the include and exclude options and the caller-supplied filter
        /// </summary>
        /// <param name="filter">The caller-supplied filter</param>
        /// <returns>The combined filter, or null if there is no filter</returns>
        private IFilter? BuildCandidateFilter(IFilter? filter)
        {
            IFilter? result = null;
            var excludes = m_options.RestoreTestExclude;
            if (excludes.Length > 0)
                result = JoinedFilterExpression.Join(result, new FilterExpression(excludes, false));

            var includes = m_options.RestoreTestInclude;
            if (includes.Length > 0)
                result = JoinedFilterExpression.Join(result, new FilterExpression(includes, true));

            if (filter != null && !filter.Empty)
                result = JoinedFilterExpression.Join(result, filter);

            return result == null || result.Empty ? null : result;
        }

        /// <summary>
        /// Creates the scratch folder and checks that it is writable and has some free space
        /// </summary>
        /// <returns>The path to the scratch folder</returns>
        private string CreateScratchFolder()
        {
            var basePath = m_options.RestoreTestTempPath;
            if (string.IsNullOrWhiteSpace(basePath))
                basePath = TempFolder.SystemTempPath;

            basePath = Environment.ExpandEnvironmentVariables(basePath);
            if (!Directory.Exists(basePath))
                throw new UserInformationException(LC.L("The restore test temporary path does not exist: {0}", basePath), "RestoreTestTempPathMissing");

            var scratchRoot = Path.Combine(Path.GetFullPath(basePath), SCRATCH_FOLDER_PREFIX + Library.Utility.Utility.GetHexGuid());
            try
            {
                Directory.CreateDirectory(scratchRoot);
                var probe = Path.Combine(scratchRoot, WRITE_PROBE_NAME);
                File.WriteAllBytes(probe, new byte[] { 0 });
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                throw new UserInformationException(LC.L("The restore test temporary path is not writable: {0} ({1})", basePath, ex.Message), "RestoreTestTempPathNotWritable", ex);
            }

            // The temporary database and the restore cache need at least a volume of room before anything is known about the sample
            EnsureFreeSpace(scratchRoot, m_options.VolumeSize);
            Logging.Log.WriteVerboseMessage(LOGTAG, "ScratchFolder", LC.L("Using scratch folder {0} for the restore test", scratchRoot));
            return scratchRoot;
        }

        /// <summary>
        /// Throws if the free space at the path is known and below the required amount
        /// </summary>
        /// <param name="path">The path to check</param>
        /// <param name="required">The required number of free bytes</param>
        private static void EnsureFreeSpace(string path, long required)
        {
            var space = Library.Utility.Utility.GetFreeSpaceForPath(path);
            if (space == null)
            {
                Logging.Log.WriteVerboseMessage(LOGTAG, "FreeSpaceUnknown", LC.L("Could not determine the free space at {0}", path));
                return;
            }

            if (space.Value.FreeSpace < required)
                throw new UserInformationException(LC.L("The restore test temporary path {0} has {1} free, but at least {2} is required", path, Library.Utility.Utility.FormatSizeString(space.Value.FreeSpace), Library.Utility.Utility.FormatSizeString(required)), "RestoreTestTempPathInsufficientSpace");
        }

        /// <summary>
        /// Marks the download budget as exceeded if the downloaded and estimated bytes exceed the limit
        /// </summary>
        /// <param name="maxDownload">The download limit, or 0 for unlimited</param>
        /// <param name="estimated">The estimated number of bytes still to download</param>
        /// <returns><c>true</c> if the budget is exceeded</returns>
        private bool CheckDownloadBudget(long maxDownload, long estimated)
        {
            if (maxDownload <= 0)
                return false;

            var downloaded = m_result.BackendStatistics.BytesDownloaded;
            if (downloaded + estimated <= maxDownload)
                return false;

            MarkBudgetExceeded(RestoreTestBudgetReason.DownloadSize, LC.L("The restore test would download {0} ({1} already downloaded, {2} estimated), which exceeds the download budget of {3}", Library.Utility.Utility.FormatSizeString(downloaded + estimated), Library.Utility.Utility.FormatSizeString(downloaded), Library.Utility.Utility.FormatSizeString(estimated), Library.Utility.Utility.FormatSizeString(maxDownload)));
            return true;
        }

        /// <summary>
        /// Marks the runtime budget as exceeded if the elapsed time exceeds the limit
        /// </summary>
        /// <param name="stopwatch">The stopwatch measuring the run</param>
        /// <param name="maxRuntime">The runtime limit, or zero for unlimited</param>
        /// <returns><c>true</c> if the budget is exceeded</returns>
        private bool CheckRuntimeBudget(Stopwatch stopwatch, TimeSpan maxRuntime)
        {
            if (maxRuntime <= TimeSpan.Zero || stopwatch.Elapsed <= maxRuntime)
                return false;

            MarkBudgetExceeded(RestoreTestBudgetReason.Runtime, LC.L("The restore test has run for {0}, which exceeds the runtime budget of {1}", stopwatch.Elapsed, maxRuntime));
            return true;
        }

        /// <summary>
        /// Records that a budget was exceeded, logging the reason once
        /// </summary>
        /// <param name="reason">The budget that was exceeded</param>
        /// <param name="message">The message to log</param>
        private void MarkBudgetExceeded(RestoreTestBudgetReason reason, string message)
        {
            if (m_result.Budget.Exceeded)
                return;

            m_result.SetBudgetExceeded(reason);
            Logging.Log.WriteErrorMessage(LOGTAG, "BudgetExceeded", null, message);
        }

        /// <summary>
        /// Copies the backend statistics into the result
        /// </summary>
        private void FinishStatistics()
        {
            m_result.BytesDownloaded = m_result.BackendStatistics.BytesDownloaded;
            m_result.RemoteVolumesDownloaded = m_result.BackendStatistics.FilesDownloaded;
        }

        /// <summary>
        /// Converts a path to a filter entry that matches the path literally
        /// </summary>
        /// <param name="path">The path</param>
        /// <returns>The filter entry</returns>
        private static string ToLiteralFilter(string path)
        {
            // Paths that would be read as wildcard, regular expression or group filters are wrapped in an anchored regular expression
            if (path.IndexOfAny(['*', '?']) >= 0 || path.StartsWith("[", StringComparison.Ordinal) || path.StartsWith("{", StringComparison.Ordinal) || path.StartsWith("@", StringComparison.Ordinal))
                return "[^" + Regex.Escape(path) + "$]";
            return path;
        }

        /// <summary>
        /// Maps a backup path to the path the restore writes it to below the restore folder
        /// </summary>
        /// <param name="restoreFolder">The restore folder</param>
        /// <param name="prefix">The largest common prefix removed by the restore</param>
        /// <param name="path">The backup path</param>
        /// <returns>The restored path</returns>
        private static string MapTargetPath(string restoreFolder, string prefix, string path)
        {
            string target;
            if (string.IsNullOrEmpty(prefix))
            {
                // No shared root: drive letters lose the colon and UNC paths lose the leading separator
                target = path.Length > 1 && path[1] == ':' ? path[0] + path.Substring(2) : path;
                if (target.StartsWith(@"\\", StringComparison.Ordinal))
                    target = target.Substring(1);
            }
            else
            {
                target = path.Substring(prefix.Length);
            }

            var dirsep = Util.GuessDirSeparator(string.IsNullOrEmpty(prefix) ? path : prefix);
            if ((OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) && dirsep == "\\")
                target = target.Replace('\\', '/');
            else if (OperatingSystem.IsWindows() && dirsep == "/")
                target = target.Replace('\\', '_').Replace('/', '\\');

            return Util.AppendDirSeparator(restoreFolder) + target;
        }

        /// <summary>
        /// Verifies a restored file against the values recorded in the backup
        /// </summary>
        /// <param name="db">The database holding the sample, used to find the volumes of a failed file</param>
        /// <param name="file">The sampled file</param>
        /// <param name="targetPath">The path of the restored file</param>
        /// <param name="brokenVolumes">The remote volumes the restore reported as broken</param>
        /// <param name="errorMessages">The errors logged so far, scanned for volume names</param>
        /// <param name="restoreError">The message of the error that stopped the restore, if any</param>
        /// <param name="filehasher">The hasher for the file hash algorithm</param>
        /// <param name="verifyMetadata">True to also verify the modification time</param>
        /// <param name="token">The cancellation token</param>
        /// <returns><c>true</c> if the file passed verification</returns>
        private async Task<bool> VerifyRestoredFileAsync(LocalRestoreTestDatabase db, LocalRestoreTestDatabase.SampleFile file, string targetPath, HashSet<string> brokenVolumes, List<string> errorMessages, string? restoreError, System.Security.Cryptography.HashAlgorithm filehasher, bool verifyMetadata, CancellationToken token)
        {
            string? problem = null;
            RestoreTestFailureReason reason;
            string expected;
            string actual;

            try
            {
                if (!File.Exists(targetPath))
                {
                    reason = RestoreTestFailureReason.RestoreError;
                    expected = LC.L("File restored to {0}", targetPath);
                    actual = restoreError != null
                        ? LC.L("File was not restored because the restore stopped: {0}", restoreError)
                        : LC.L("File was not restored");
                    problem = LC.L("File was not restored");
                }
                else
                {
                    // A size or hash difference only points at damaged data when the restore ran to
                    // completion. When the restore stopped early, a partially written file is a
                    // consequence of the stop and is reported as a restore error instead.
                    var size = new FileInfo(targetPath).Length;
                    if (size != file.Size)
                    {
                        reason = restoreError != null ? RestoreTestFailureReason.RestoreError : RestoreTestFailureReason.SizeMismatch;
                        expected = file.Size.ToString(CultureInfo.InvariantCulture);
                        actual = restoreError != null
                            ? LC.L("Restored size {0} differs from recorded size {1} because the restore stopped: {2}", size, file.Size, restoreError)
                            : size.ToString(CultureInfo.InvariantCulture);
                        problem = LC.L("Restored size {0} differs from recorded size {1}", size, file.Size);
                    }
                    else
                    {
                        string hash;
                        using (var fs = File.OpenRead(targetPath))
                            hash = Convert.ToBase64String(filehasher.ComputeHash(fs));

                        if (!string.Equals(hash, file.Hash, StringComparison.Ordinal))
                        {
                            reason = restoreError != null ? RestoreTestFailureReason.RestoreError : RestoreTestFailureReason.HashMismatch;
                            expected = file.Hash;
                            actual = restoreError != null
                                ? LC.L("Restored hash {0} differs from recorded hash {1} because the restore stopped: {2}", hash, file.Hash, restoreError)
                                : hash;
                            problem = LC.L("Restored hash {0} differs from recorded hash {1}", hash, file.Hash);
                        }
                        else if (verifyMetadata && !m_options.SkipMetadata && file.LastModified.Ticks > 0 && (File.GetLastWriteTimeUtc(targetPath) - file.LastModified).Duration() > TIMESTAMP_TOLERANCE)
                        {
                            reason = RestoreTestFailureReason.MetadataMismatch;
                            expected = Library.Utility.Utility.SerializeDateTime(file.LastModified);
                            actual = Library.Utility.Utility.SerializeDateTime(File.GetLastWriteTimeUtc(targetPath));
                            AddFailure(file, reason, expected, actual);
                            return false;
                        }
                        else
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "FileVerified", LC.L("Verified restored file {0}", file.Path));
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.IsAbortOrCancelException())
                    throw;

                AddFailure(file, RestoreTestFailureReason.RestoreError, LC.L("Readable restored file"), ex.Message);
                return false;
            }

            // A file whose data lives in a volume that failed to download is reported against that volume
            var failedVolumes = (await db.GetFileVolumesAsync(file.FileId, token).ConfigureAwait(false))
                .Where(x => brokenVolumes.Contains(x) || errorMessages.Any(e => e.Contains(x, StringComparison.Ordinal)))
                .ToList();

            if (failedVolumes.Count > 0)
                AddFailure(file, RestoreTestFailureReason.MissingRemoteVolume, string.Join(", ", failedVolumes), problem);
            else
                AddFailure(file, reason, expected, actual);

            return false;
        }

        /// <summary>
        /// Records a verification failure in the result and the log
        /// </summary>
        /// <param name="file">The sampled file</param>
        /// <param name="reason">The failure reason</param>
        /// <param name="expected">The expected value</param>
        /// <param name="actual">The actual value</param>
        private void AddFailure(LocalRestoreTestDatabase.SampleFile file, RestoreTestFailureReason reason, string expected, string actual)
        {
            m_result.AddFailure(file.Path, reason, expected, actual);
            Logging.Log.WriteErrorMessage(LOGTAG, "FileVerificationFailed", null, LC.L("Restored file {0} failed verification ({1}), expected: {2}, actual: {3}", file.Path, reason, expected, actual));
        }

        /// <summary>
        /// Compares a verified file with the live source file, if the source still exists unchanged
        /// </summary>
        /// <param name="file">The sampled file</param>
        /// <param name="filehasher">The hasher for the file hash algorithm</param>
        private void CompareWithSource(LocalRestoreTestDatabase.SampleFile file, System.Security.Cryptography.HashAlgorithm filehasher)
        {
            try
            {
                if (!File.Exists(file.Path))
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "SourceMissing", LC.L("The source file {0} no longer exists, skipping the source comparison", file.Path));
                    return;
                }

                var info = new FileInfo(file.Path);
                if (info.Length != file.Size || (info.LastWriteTimeUtc - file.LastModified).Duration() > TIMESTAMP_TOLERANCE)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "SourceChanged", LC.L("The source file {0} has changed since the backup, skipping the source comparison", file.Path));
                    return;
                }

                string hash;
                using (var fs = File.OpenRead(file.Path))
                    hash = Convert.ToBase64String(filehasher.ComputeHash(fs));

                if (!string.Equals(hash, file.Hash, StringComparison.Ordinal))
                {
                    m_result.AddSourceDifference(file.Path, LC.L("The source file content differs from the backup although the size and modification time are unchanged"), file.Hash, hash);
                    Logging.Log.WriteWarningMessage(LOGTAG, "SourceDifference", null, LC.L("The source file {0} differs from the backup, expected hash: {1}, actual hash: {2}", file.Path, file.Hash, hash));
                }
            }
            catch (Exception ex)
            {
                if (ex.IsAbortOrCancelException())
                    throw;

                Logging.Log.WriteWarningMessage(LOGTAG, "SourceCompareFailed", ex, LC.L("Failed to compare the source file {0}: {1}", file.Path, ex.Message));
            }
        }

        /// <summary>
        /// Requests a controlled stop of the operation when the runtime budget runs out
        /// </summary>
        private sealed class RuntimeWatchdog : IDisposable
        {
            private readonly CancellationTokenSource m_cts = new CancellationTokenSource();
            private readonly Task? m_task;
            private int m_fired;

            /// <summary>
            /// True if the watchdog requested a stop
            /// </summary>
            public bool Fired => Volatile.Read(ref m_fired) != 0;

            public RuntimeWatchdog(Stopwatch stopwatch, TimeSpan maxRuntime, ITaskControl taskControl)
            {
                if (maxRuntime <= TimeSpan.Zero)
                    return;

                var remaining = maxRuntime - stopwatch.Elapsed;
                if (remaining < TimeSpan.Zero)
                    remaining = TimeSpan.Zero;

                m_task = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(remaining, m_cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    Volatile.Write(ref m_fired, 1);
                    Logging.Log.WriteWarningMessage(LOGTAG, "RuntimeBudgetStop", null, LC.L("The runtime budget of {0} was exceeded, stopping the restore", maxRuntime));
                    taskControl.Stop();
                });
            }

            public void Dispose()
            {
                m_cts.Cancel();
                m_cts.Dispose();
            }
        }
    }
}
