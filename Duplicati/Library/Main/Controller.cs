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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Duplicati.Library.Utility;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using System.Threading.Tasks;
using Duplicati.Library.Main.Operation.Common;
using System.IO;
using Duplicati.Library.Backends;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.Main.Database.Sync;

namespace Duplicati.Library.Main
{
    public class Controller : IDisposable, IController
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<Controller>();
        /// <summary>
        /// The backend url
        /// </summary>
        private string m_backendUrl;
        /// <summary>
        /// The parsed type-safe version of the commandline options
        /// </summary>
        private readonly Options m_options;
        /// <summary>
        /// The optional secret provider, if none provided in options
        /// </summary>
        public ISecretProvider SecretProvider { get; private set; }
        /// <summary>
        /// The destination for all output messages during execution
        /// </summary>
        private IMessageSink m_messageSink;

        // TODO: The m_current* approach here prevents us from having multiple concurrent operations

        /// <summary>
        /// The current executing task
        /// </summary>
        private ITaskControl m_currentTaskControl = null;

        /// <summary>
        /// Guards the current task control together with the requests kept for it
        /// </summary>
        private readonly object m_taskControlLock = new object();

        /// <summary>
        /// Set once an operation has started on this controller
        /// </summary>
        private bool m_operationStarted = false;

        /// <summary>
        /// A stop asked for before the first operation started, applied when it starts
        /// </summary>
        private bool m_stopBeforeStart = false;

        /// <summary>
        /// An abort asked for before the first operation started, applied when it starts
        /// </summary>
        private bool m_abortBeforeStart = false;

        /// <summary>
        /// The current backend manager
        /// </summary>
        private IBackendManager m_currentBackendManager = null;

        /// <summary>
        /// If not null, active locale change that needs to be reset
        /// </summary>
        private LocaleChange m_localeChange = null;

        /// <summary>
        /// The handle for the report modules loaded for the current operation. Set in
        /// <see cref="ReportModuleController.SetupAsync"/> and disposed in
        /// <see cref="OperationCompleteAsync"/> so the completion callbacks fire before the
        /// module instances themselves are disposed.
        /// </summary>
        private ReportModuleController.ReportModulesHandle ReportModulesHandler;

        /// <summary>
        /// The ID of the job log entry holding the results of the current operation, or -1 if none was written.
        /// The reports are sent after the results are written, so a report that fails is added to the entry afterwards.
        /// </summary>
        private long m_resultLogId = -1;

        /// <summary>
        /// The number of warnings and errors the results held when they were written to the job log.
        /// </summary>
        private int m_resultLogIssueCount;

        /// <summary>
        /// Callback method invoked when an operation is started
        /// </summary>
        public Action<IBasicResults> OnOperationStarted { get; set; }

        /// <summary>
        /// Callback method invoked when an operation is completed
        /// </summary>
        public Action<IBasicResults, Exception> OnOperationCompleted { get; set; }

        /// <summary>
        /// Constructs a new interface for performing backup and restore operations
        /// </summary>
        /// <param name="backendUrl">The url for the backend to use</param>
        /// <param name="options">All required options</param>
        public Controller(string backendUrl, Dictionary<string, string> options, IMessageSink messageSink)
        {
            m_backendUrl = backendUrl;
            m_options = new Options(options);
            m_messageSink = messageSink;
        }

        /// <inheritdoc />
        public Task AppendSinkAsync(IMessageSink sink)
        {
            if (this.m_messageSink is MultiMessageSink messageSink)
                messageSink.Append(sink);
            else
                m_messageSink = new MultiMessageSink(m_messageSink, sink);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SetSecretProviderAsync(ISecretProvider secretProvider)
        {
            SecretProvider = secretProvider;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task<IBackupResults> BackupAsync(string[] inputsources, IFilter inputFilter = null)
        {
            UsageReporter.Reporter.Report("USE_BACKEND", new Library.Utility.RelaxedUri(m_backendUrl).Scheme);
            UsageReporter.Reporter.Report("USE_COMPRESSION", m_options.CompressionModule);
            UsageReporter.Reporter.Report("USE_ENCRYPTION", m_options.EncryptionModule);

            // Warn about experimental tar-based compression modules
            if (m_options.CompressionModule.Equals("tzstd", StringComparison.OrdinalIgnoreCase) ||
                m_options.CompressionModule.Equals("tgz", StringComparison.OrdinalIgnoreCase))
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "ExperimentalCompressionModule", null, $"The compression module '{m_options.CompressionModule}' is experimental and for testing only.");
            }

            CheckAutoCompactInterval();
            CheckAutoVacuumInterval();
            SourceProviderFactory.EnableMetadataStorageIfRequiredBySources(inputsources, m_options.RawOptions);

            return await RunActionAsync(new BackupResults(), inputsources, inputFilter, new { lastRestoreTest = m_lastRestoreTest }, static async config =>
            {
                var (expandedSources, filter) = ExpandInputSources(config.Paths, config.Filter, config.Options);
                using (var h = new Operation.BackupHandler(config.Options, config.Result))
                    await h.RunAsync(expandedSources, config.BackendManager, filter)
                        .ConfigureAwait(false);

                UsageReporter.Reporter.Report("BACKUP_FILECOUNT", config.Result.ExaminedFiles);
                UsageReporter.Reporter.Report("BACKUP_FILESIZE", config.Result.SizeOfExaminedFiles);
                UsageReporter.Reporter.Report("BACKUP_DURATION", (long)config.Result.Duration.TotalSeconds);

                // The backup handler has released the database at this point, so the restore test can open it
                await RunPostBackupRestoreTestAsync(config.Options, config.Result, config.BackendManager, config.LogTarget, config.Context.lastRestoreTest).ConfigureAwait(false);

                using (var h = new Operation.RemoteSynchronizationHandler(config.BackendUrl, config.Options, config.Result))
                    await h.RunAsync()
                        .ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs a restore test as the last step of a backup, when the interval given by
        /// <c>--perform-restore-test-after</c> has passed since the previous restore test.
        /// The time of the previous restore test is supplied by the server when available,
        /// and is otherwise read from the operation log in the local database.
        /// </summary>
        /// <param name="options">The options</param>
        /// <param name="result">The backup results to attach the restore test results to</param>
        /// <param name="backendManager">The backend manager</param>
        /// <param name="logTarget">The log target of the backup, which collects the restore test messages in the restore test results</param>
        /// <param name="lastRestoreTest">The time of the previous restore test as supplied by the caller, or <see cref="DateTime.MinValue"/> if unknown</param>
        private static async Task RunPostBackupRestoreTestAsync(Options options, BackupResults result, IBackendManager backendManager, ControllerMultiLogTarget logTarget, DateTime lastRestoreTest)
        {
            var interval = options.PerformRestoreTestAfter;
            if (interval <= TimeSpan.Zero || options.Dryrun)
                return;

            var token = result.TaskControl.ProgressToken;
            if (result.TaskControl.StopToken.IsCancellationRequested || token.IsCancellationRequested)
                return;

            if (lastRestoreTest <= DateTime.MinValue && !options.NoLocalDb && !string.IsNullOrWhiteSpace(options.Dbpath) && File.Exists(options.Dbpath))
            {
                try
                {
                    await using var db = await LocalDatabase.CreateLocalDatabaseAsync(options.Dbpath, null, true, null, token).ConfigureAwait(false);
                    lastRestoreTest = await db.GetLastOperationTimeAsync(OperationMode.RestoreTest.ToString(), token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "LastRestoreTestLookupFailed", ex, "Failed to look up the time of the last restore test: {0}", ex.Message);
                }
            }

            if (lastRestoreTest > DateTime.MinValue && lastRestoreTest.ToUniversalTime().Add(interval) > DateTime.UtcNow)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "RestoreTestSkipped", "Skipping restore test until {0}", lastRestoreTest.ToLocalTime().Add(interval));
                return;
            }

            result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PostBackupRestoreTest);
            var restoreTestResults = new RestoreTestResults(result);
            result.RestoreTestResults = restoreTestResults;

            // The restore test is reported on its own: its messages still reach the log files, sinks and the
            // outer log scopes, but are collected in its own result and never in the backup's, so a failed test
            // does not fail the backup
            using (logTarget.CollectInNestedResult(restoreTestResults))
            {
                try
                {
                    await new Operation.RestoreTestHandler(options, restoreTestResults)
                        .RunAsync(backendManager, null)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (!ex.IsAbortOrCancelException())
                {
                    // The backup itself completed, so a restore test that cannot run is an error on the restore test, not on the backup
                    Logging.Log.WriteErrorMessage(LOGTAG, "RestoreTestFailed", ex, "The restore test after the backup failed: {0}", ex.Message);
                }
                finally
                {
                    if (restoreTestResults.EndTime.Ticks == 0)
                        restoreTestResults.EndTime = DateTime.UtcNow;
                }
            }
        }

        /// <inheritdoc />
        public async Task<IRestoreResults> RestoreAsync(string[] paths, IFilter inputFilter = null)
        {
            return await RunActionAsync(new RestoreResults(), null, inputFilter, new { paths }, static async config =>
            {
                // Written as "--restore-path <folder>", the option has no value and the folder is taken as a path to restore
                if (config.Options.RawOptions.ContainsKey("restore-path") && string.IsNullOrWhiteSpace(config.Options.Restorepath))
                    throw new UserInformationException("The option --restore-path has no value. Write it as --restore-path=<folder>, or leave it out to restore to the original location.", "RestorePathHasNoValue");

                using var restoreDestination =
                    (config.Options.Restorepath ?? "").StartsWith("@")

                    // Remote destination
                    ? await DynamicLoader.RestoreDestinationProviderLoader.GetRestoreDestinationProvider(
                        config.Options.Restorepath.Substring(1),
                        config.Options.RawOptions,
                        config.Result.TaskControl.ProgressToken)
                        .ConfigureAwait(false)

                    // Local destination
                    : new SourceProvider.FileRestoreDestinationProvider(config.Options.Restorepath ?? "", config.Options.AllowRestoreOutsideTargetDirectory, config.Options.Overwrite);

                if (restoreDestination == null)
                    throw new UserInformationException($"Could not find restore destination for path: {config.Options.Restorepath}", "InvalidRestoreDestination");

                await new Operation.RestoreHandler(config.Options, config.Result)
                    .RunAsync(config.Context.paths, config.BackendManager, config.Filter, restoreDestination, config.BackendUrl)
                    .ConfigureAwait(false);

                await restoreDestination.Finalize((pg) =>
                {
                    config.Result.OperationProgressUpdater.UpdateProgress((float)pg);
                }, config.Result.TaskControl.ProgressToken).ConfigureAwait(false);
                config.Result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_Complete);
                config.Result.EndTime = DateTime.UtcNow;

                UsageReporter.Reporter.Report("RESTORE_FILECOUNT", config.Result.RestoredFiles);
                UsageReporter.Reporter.Report("RESTORE_FILESIZE", config.Result.SizeOfRestoredFiles);
                UsageReporter.Reporter.Report("RESTORE_DURATION", (long)config.Result.Duration.TotalSeconds);
            }).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IRestoreControlFilesResults> RestoreControlFilesAsync(IEnumerable<string> files = null, IFilter inputFilter = null)
        {
            return await RunActionAsync(new RestoreControlFilesResults(), null, inputFilter, new { files }, static config =>
                new Operation.RestoreControlFilesHandler(config.Options, config.Result)
                    .RunAsync(config.Context.files?.ToArray(), config.BackendManager, config.Filter)
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IDeleteResults> DeleteAsync()
        {
            return await RunActionAsync(new DeleteResults(), null, null, false, static config =>
                new Operation.DeleteHandler(config.Options, config.Result)
                    .RunAsync(config.BackendManager)
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IRepairResults> RepairAsync(IFilter inputFilter = null)
        {
            return await RunActionAsync(new RepairResults(), null, inputFilter, false, static config =>
                new Operation.RepairHandler(config.Options, config.Result)
                    .RunAsync(config.BackendManager, config.Filter)
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IListFilesetResults> ListFilesetsAsync()
            => await RunActionAsync(new ListFilesetResults(), null, null, false, static config =>
                Operation.ListFilesetsHandler.RunAsync(config.Options, config.Result, config.BackendManager)
            ).ConfigureAwait(false);

        /// <inheritdoc />
        public async Task<ISetVersionLabelResults> SetVersionLabelAsync()
            => await RunActionAsync(new SetVersionLabelResults(), null, null, false, static config =>
                Operation.SetVersionLabelHandler.RunAsync(config.Options, config.Result)
            ).ConfigureAwait(false);

        /// <inheritdoc />
        public async Task<IListFolderResults> ListFolderAsync(string[] folders, long offset, long limit, bool extendedData)
            => await RunActionAsync(new ListFolderResults(), null, null, new { folders, offset, limit, extendedData }, static config =>
                Operation.ListFolderHandler.RunAsync(config.Options, config.Result, config.Context.folders, config.Context.offset, config.Context.limit, config.Context.extendedData)
            ).ConfigureAwait(false);

        /// <inheritdoc />
        public async Task<IListFileVersionsResults> ListFileVersionsAsync(string[] files, long offset, long limit)
            => await RunActionAsync(new ListFileVersionsResults(), null, null, new { files, offset, limit }, static config =>
                Operation.ListFileVersionsHandler.RunAsync(config.Options, config.Result, config.Context.files, config.Context.offset, config.Context.limit)
            ).ConfigureAwait(false);

        /// <inheritdoc />
        public async Task<ISearchFilesResults> SearchEntriesAsync(string[] pathprefixes, IFilter inputFilter, bool caseSensitive, long offset, long limit, bool returnExtendedData, bool searchMetadata)
            => await RunActionAsync(new SearchFilesResults(), null, inputFilter, new { pathprefixes, caseSensitive, offset, limit, returnExtendedData, searchMetadata }, static config =>
                Operation.SearchEntriesHandler.RunAsync(config.Options, config.Result, config.Context.pathprefixes, config.Filter, config.Context.caseSensitive, config.Context.offset, config.Context.limit, config.Context.returnExtendedData, config.Context.searchMetadata)
            ).ConfigureAwait(false);

        /// <inheritdoc />
        public Task<IListResults> ListAsync()
            => ListAsync(null, null);

        /// <inheritdoc />
        public Task<IListResults> ListAsync(string filterstring)
            => ListAsync(filterstring == null ? null : new string[] { filterstring }, null);

        /// <inheritdoc />
        public async Task<IListResults> ListAsync(IEnumerable<string> filterstrings, IFilter inputFilter)
        {
            return await RunActionAsync(new ListResults(), null, inputFilter, new { filterstrings }, static config =>
                new Operation.ListFilesHandler(config.Options, config.Result)
                    .RunAsync(config.BackendManager, config.Context.filterstrings?.ToArray(), config.Filter)
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IListResults> ListControlFilesAsync(IEnumerable<string> filterstrings, IFilter inputFilter)
        {
            return await RunActionAsync(new ListResults(), null, inputFilter, new { filterstrings }, static config =>
                new Operation.ListControlFilesHandler(config.Options, config.Result)
                    .RunAsync(config.BackendManager, config.Context.filterstrings?.ToArray(), config.Filter)
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IListRemoteResults> ListRemoteAsync()
        {
            return await RunActionAsync(new ListRemoteResults(), null, null, false, static async config =>
            {
                using var tf = File.Exists(config.Options.Dbpath) ? null : new TempFile();
                await using var db = await LocalDatabase.CreateLocalDatabaseAsync(((string)tf) ?? config.Options.Dbpath, "list-remote", true, null, config.Result.TaskControl.ProgressToken).ConfigureAwait(false);
                config.Result.SetResult(await config.BackendManager.ListAsync(null, CancellationToken.None).ConfigureAwait(false));
            }).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IListRemoteResults> DeleteAllRemoteFilesAsync()
        {
            return await RunActionAsync(new ListRemoteResults(), null, null, false, static async config =>
            {
                config.Result.OperationProgressUpdater.UpdatePhase(OperationPhase.Delete_Listing);
                {
                    // Only delete files that match the expected pattern and prefix
                    var list = (await config.BackendManager.ListAsync(null, config.Result.TaskControl.ProgressToken).ConfigureAwait(false))
                        .Select(x => Volumes.VolumeBase.ParseFilename(x))
                        .Where(x => x != null)
                        .Where(x => x.Prefix == config.Options.Prefix)
                        .ToList();

                    // If the local database is available, we will use it to avoid deleting unrelated files
                    // from the backend. Otherwise, we may accidentally delete non-Duplicati files, or
                    // files from a different Duplicati configuration that points to the same backend location
                    // and uses the same prefix (see issues #2678, #3845, and #4244).
                    if (File.Exists(config.Options.Dbpath))
                    {
                        await using LocalDatabase db = await LocalDatabase.CreateLocalDatabaseAsync(config.Options.Dbpath, "list-remote", true, null, config.Result.TaskControl.ProgressToken).ConfigureAwait(false);
                        var dbRemoteVolumes = db.GetRemoteVolumesAsync(config.Result.TaskControl.ProgressToken);
                        var dbRemoteFiles = await dbRemoteVolumes
                            .Select(x => x.Name)
                            .ToHashSetAsync(cancellationToken: config.Result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);
                        list = list.Where(x =>
                            dbRemoteFiles.Contains(x.File.Name)
                        ).ToList();
                    }

                    config.Result.OperationProgressUpdater.UpdatePhase(OperationPhase.Delete_Deleting);
                    config.Result.OperationProgressUpdater.UpdateProgress(0);
                    for (var i = 0; i < list.Count; i++)
                    {
                        try
                        {
                            if (config.Options.Dryrun)
                            {
                                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldDeleteFile", "Would delete file: {0}", list[i].File.Name);
                            }
                            else
                            {
                                await config.BackendManager.DeleteAsync(list[i].File.Name, list[i].File.Size, true, config.Result.TaskControl.ProgressToken).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "DeleteFilesetError", ex, "Failed to delete remote file: {0}", list[i].File.Name);
                        }
                        config.Result.OperationProgressUpdater.UpdateProgress((float)i / list.Count);
                    }
                    config.Result.OperationProgressUpdater.UpdateProgress(1);
                }
            }).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ICompactResults> CompactAsync()
        {
            CheckAutoVacuumInterval();

            return await RunActionAsync(new CompactResults(), null, null, false, static config =>
                new Operation.CompactHandler(config.Options, config.Result)
                    .RunAsync(config.BackendManager)
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ISetLockResults> SetLocksAsync()
        {
            return await RunActionAsync(new SetLockResults(), null, null, false, static config =>
                new Operation.SetLocksHandler(config.Options, config.Result)
                    .RunAsync(config.BackendManager)
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IReadLockInfoResults> ReadLockInfoAsync()
        {
            return await RunActionAsync(new ReadLockInfoResults(), null, null, false, static config =>
                new Operation.ReadLockInfoHandler(config.Options, config.Result)
                    .RunAsync(config.BackendManager)
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IRecreateDatabaseResults> UpdateDatabaseWithVersionsAsync(IFilter inputFilter = null)
        {
            return await RunActionAsync(new RecreateDatabaseResults(), null, inputFilter, false, static async config =>
            {
                var filelistfilter = Operation.RestoreHandler.GetNumberedFilelistFilterDelegate(config.Options.Time, config.Options.Version, singleTimeMatch: true);
                using (var h = new Operation.RecreateDatabaseHandler(config.Options, config.Result))
                    await h.RunUpdateAsync(config.BackendManager, config.Filter, filelistfilter, null)
                        .ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ICreateLogDatabaseResults> CreateLogDatabaseAsync(string targetpath)
        {
            return await RunActionAsync(new CreateLogDatabaseResults(), null, null, new { targetpath }, static async config =>
                await new Operation.CreateBugReportHandler(config.Context.targetpath, config.Options, config.Result).RunAsync()
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IListChangesResults> ListChangesAsync(string baseVersion, string targetVersion, IEnumerable<string> filterstrings = null, IFilter inputFilter = null, Action<IListChangesResults, IEnumerable<Tuple<ListChangesChangeType, ListChangesElementType, string>>> callback = null)
        {

            return await RunActionAsync(new ListChangesResults(), null, inputFilter, new { baseVersion, targetVersion, filterstrings, callback }, async static config =>
                await new Operation.ListChangesHandler(config.Options, config.Result)
                    .RunAsync(config.Context.baseVersion, config.Context.targetVersion, config.BackendManager, config.Context.filterstrings, config.Filter, config.Context.callback)
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IListAffectedResults> ListAffectedAsync(List<string> args, Action<IListAffectedResults> callback = null)
        {
            return await RunActionAsync(new ListAffectedResults(), null, null, new { args, callback }, static config =>
                new Operation.ListAffected(config.Options, config.Result)
                    .RunAsync(config.Context.args?.ToArray(), config.Context.callback)
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ITestResults> TestAsync(long samples = 1)
        {
            if (!m_options.RawOptions.ContainsKey("full-remote-verification"))
                m_options.RawOptions["full-remote-verification"] = "true";

            return await RunActionAsync(new TestResults(), null, null, new { samples }, static config =>
                new Operation.TestHandler(config.Options, config.Result)
                    .RunAsync(config.Context.samples, config.BackendManager)
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IRestoreTestResults> RestoreTestAsync(IFilter inputFilter = null)
        {
            return await RunActionAsync(new RestoreTestResults(), null, inputFilter, false, static config =>
                new Operation.RestoreTestHandler(config.Options, config.Result)
                    .RunAsync(config.BackendManager, config.Filter)
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ITestFilterResults> TestFilterAsync(string[] paths, IFilter inputFilter = null)
        {
            m_options.RawOptions["dry-run"] = "true";
            m_options.RawOptions["dbpath"] = "INVALID!";

            // Redirect all messages from the filter to the message sink
            var filtertag = Logging.Log.LogTagFromType(typeof(Operation.Backup.FileEnumerationProcess));
            using (Logging.Log.StartScope(m_messageSink.WriteMessage, x => x.FilterTag.Contains(filtertag)))
            {
                return await RunActionAsync(new TestFilterResults(), paths, inputFilter, false, static config =>
                {
                    var (expandedSources, filter) = ExpandInputSources(config.Paths, config.Filter, config.Options);
                    return new Operation.TestFilterHandler(config.Options, config.Result)
                        .RunAsync(expandedSources, filter);
                }
                ).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task<ISystemInfoResults> SystemInfoAsync()
        {
            return await RunActionAsync(new SystemInfoResults(), null, null, false, static config =>
                Operation.SystemInfoHandler.RunAsync(config.Result)
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IPurgeFilesResults> PurgeFilesAsync(IFilter inputFilter)
        {
            return await RunActionAsync(new PurgeFilesResults(), null, inputFilter, false, static config =>
                new Operation.PurgeFilesHandler(config.Options, config.Result)
                    .RunAsync(config.BackendManager, config.Filter)
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IListBrokenFilesResults> ListBrokenFilesAsync(IFilter inputFilter, Func<long, DateTime, long, string, long, bool> callbackhandler = null)
        {
            return await RunActionAsync(new ListBrokenFilesResults(), null, inputFilter, new { callbackhandler }, static config =>
                new Operation.ListBrokenFilesHandler(config.Options, config.Result)
                    .RunAsync(config.BackendManager, config.Filter, config.Context.callbackhandler)
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IPurgeBrokenFilesResults> PurgeBrokenFilesAsync(IFilter inputFilter)
        {
            return await RunActionAsync(new PurgeBrokenFilesResults(), null, inputFilter, false, static config =>
                new Operation.PurgeBrokenFilesHandler(config.Options, config.Result)
                    .RunAsync(config.BackendManager, config.Filter)
            ).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ISendMailResults> SendMailAsync()
        {
            m_options.RawOptions["send-mail-level"] = "all";
            m_options.RawOptions["send-mail-any-operation"] = "true";
            const string sendMailModuleKey = "sendmail";
            m_options.RawOptions.TryGetValue("send-mail-to", out string targetmail);
            if (string.IsNullOrWhiteSpace(targetmail))
                throw new Exception(string.Format("No email specified, please use --{0}", "send-mail-to"));

            m_options.RawOptions["disable-module"] = string.Join(
                ",",
                DynamicLoader.GenericLoader.Modules
                         .Where(m =>
                                !string.Equals(m.Key, sendMailModuleKey, StringComparison.OrdinalIgnoreCase)
                         )
                .Select(x => x.Key)
            );

            /// Forward all messages from the email module to the message sink
            var filtertag = Logging.Log.LogTagFromType(DynamicLoader.GenericLoader.GetModule(sendMailModuleKey).GetType());
            using (Logging.Log.StartScope(m_messageSink.WriteMessage, x => x.FilterTag.Contains(filtertag)))
            {
                return await RunActionAsync(new SendMailResults(), null, null, false, static config =>
                    Task.Run(() =>
                    {
                        config.Result.Lines = [];
                        Thread.Sleep(5);
                    })
                ).ConfigureAwait(false);
            }
        }

        public async Task<ISyncResults> SyncAsync(string[] sourcePaths, IFilter filter)
        {
            SourceProviderFactory.EnableMetadataStorageIfRequiredBySources(sourcePaths, m_options.RawOptions);

            return await RunActionAsync(new SyncResults(), sourcePaths, filter, false, config =>
                new Operation.Sync.SyncHandler(config.Paths, config.Options, config.Result, config.BackendUrl)
                    .RunAsync(config.BackendManager, config.Filter)
            );
        }

        public async Task<IVacuumResults> VacuumAsync()
        {
            return await RunActionAsync(new VacuumResults(), null, null, false, static config =>
                new Operation.VacuumHandler(config.Options, config.Result)
                    .RunAsync()
            ).ConfigureAwait(false);
        }

        private sealed record RunActionData<TRes, TCon>(
            TRes Result,
            TCon Context,
            string BackendUrl,
            string[] Paths,
            Options Options,
            IFilter Filter,
            IBackendManager BackendManager,
            ControllerMultiLogTarget LogTarget);

        private async Task<TRes> RunActionAsync<TRes, TCon>(TRes result, string[] paths, IFilter filter, TCon context, Func<RunActionData<TRes, TCon>, Task> method)
            where TRes : ISetCommonOptions, ITaskControlProvider, Logging.ILogDestination, IBasicResults, IBackendWriterProvider
        {
            OnOperationStarted?.Invoke(result);
            var resultSetter = result as ISetCommonOptions;
            using (var logTarget = new ControllerMultiLogTarget(result, Logging.LogMessageType.Information, null, m_options.SuppressWarningsFilter))
            using (Logging.Log.StartScope(logTarget, null))
            {
                using var httpListener = m_options.LogHttpRequests || m_options.LogSocketData >= 0
                    ? new NetworkTrafficLogger(m_options.LogHttpRequests, m_options.LogSocketData)
                    : null;

                try
                {
                    bool stopBeforeStart, abortBeforeStart;
                    lock (m_taskControlLock)
                    {
                        m_currentTaskControl = result.TaskControl;
                        m_resultLogId = -1;
                        m_operationStarted = true;
                        stopBeforeStart = m_stopBeforeStart;
                        abortBeforeStart = m_abortBeforeStart;
                        m_stopBeforeStart = m_abortBeforeStart = false;
                    }

                    // A request that came while the operation was being set up
                    if (abortBeforeStart)
                        result.TaskControl.Terminate();
                    else if (stopBeforeStart)
                        result.TaskControl.Stop();

                    m_options.MainAction = result.MainOperation;
                    await ApplySecretProviderAsync(CancellationToken.None).ConfigureAwait(false);
                    try
                    {
                        (paths, filter) = SetupCommonOptions(result, paths, filter, logTarget);
                    }
                    catch
                    {
                        // The report modules that were loaded before the failure must still
                        // see the operation, or the failure is never reported to them
                        ReportModulesHandler = await ReportModuleController.SetupAsync(this, result, m_options, m_currentTaskControl.StopToken);
                        throw;
                    }

                    ReportModulesHandler = await ReportModuleController.SetupAsync(this, result, m_options, m_currentTaskControl.StopToken);

                    logTarget.AddTarget(m_messageSink, m_options.ConsoleLoglevel, m_options.ConsoleLogFilter);
                    result.MessageSink = m_messageSink;
                    Logging.Log.WriteInformationMessage(LOGTAG, "StartingOperation", Strings.Controller.StartingOperationMessage(m_options.MainAction));

                    using (SlowQueryMonitor.StartMonitoring(m_options.SlowQueryThreshold))
                    using (new ProcessController(m_options))
                    using (new Logging.Timer(LOGTAG, string.Format("Run{0}", result.MainOperation), string.Format("Running {0}", result.MainOperation)))
                    using (new CoCoL.IsolatedChannelScope())
                    using (m_options.ConcurrencyMaxThreads <= 0 ? null : new CoCoL.CappedThreadedThreadPool(m_options.ConcurrencyMaxThreads))
                    // Sync keeps its own intent journal and does not record a remote operation log,
                    // so the messages are never flushed and must not be collected in memory
                    using (var backend = new Backend.BackendManager(m_backendUrl, m_options, result.BackendWriter, result.TaskControl, collectDatabaseMessages: m_options.MainAction != OperationMode.Sync))
                    {
                        try
                        {
                            backend.UpdateThrottleValues(m_options.MaxUploadPrSecond, m_options.MaxDownloadPrSecond);
                            m_currentBackendManager = backend;
                            var config = new RunActionData<TRes, TCon>(result, context, m_backendUrl, paths, m_options, filter, backend, logTarget);
                            await method(config).ConfigureAwait(false);
                        }
                        finally
                        {
                            m_currentBackendManager = null;

                            // TODO: Should also have a single shared database connection for all operations
                            // The transactions should be managed inside the connection, and not passed around

                            // This would allow us to pass the database instance to the backend manager
                            // And safeguard against remote operations not being logged in the database

                            // This would also allow us to control the unclean shutdown flag,
                            // by toggling this on start and completion of transfers in the manager,
                            // instead of relying on the operations to correctly toggle the flag
                            //
                            // The sync operation owns its own database and records its own audit log.
                            if (File.Exists(m_options.Dbpath) && !m_options.NoLocalDb && m_options.MainAction != OperationMode.Sync)
                            {
                                try
                                {
                                    await using var db = await LocalDatabase.CreateLocalDatabaseAsync(m_options.Dbpath, result.MainOperation.ToString(), true, null, CancellationToken.None).ConfigureAwait(false);
                                    await backend.StopRunnerAndFlushMessagesAsync(db).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    Logging.Log.WriteWarningMessage(LOGTAG, "FailedFlushBackendMessages", ex, "Failed to flush backend messages to database: {0}", ex.Message);
                                }
                            }
                            else
                            {
                                backend.StopRunnerAndDiscardMessages();
                            }
                        }
                    }

                    if (resultSetter.EndTime.Ticks == 0)
                        resultSetter.EndTime = DateTime.UtcNow;

                    // The operation returned, but if a stop was requested it returned early, so
                    // the run did not cover everything it was asked to. Its numbers must not be
                    // recorded as the latest state of the backup, which is what this flag decides
                    // (see Runner.UpdateMetadata).
                    resultSetter.Interrupted = m_currentTaskControl?.StopToken.IsCancellationRequested == true;

                    // The post-operation result/log writes target the backup LocalDatabase schema
                    // (Operation, LogData, DeletedVolume tables). The sync database has its own
                    // schema, so sync operations write their results through the sync database
                    // instead of running incompatible SQL against it.
                    if (File.Exists(m_options.Dbpath) && !m_options.Dryrun && m_options.MainAction != OperationMode.Sync)
                    {
                        try
                        {
                            await using var db = await LocalDatabase.CreateLocalDatabaseAsync(m_options.Dbpath, null, true, null, CancellationToken.None).ConfigureAwait(false);
                            await WriteResultsAsync(db, result).ConfigureAwait(false);
                            await db.PurgeLogDataAsync(m_options.LogRetention, CancellationToken.None).ConfigureAwait(false);
                            await db.PurgeDeletedVolumesAsync(DateTime.UtcNow, CancellationToken.None).ConfigureAwait(false);

                            // Vacuum is done AFTER the results are written to the database
                            // This means that the information about the vacuum is not stored in the database,
                            // but will be reported in the log output and messages sent with any of the reporting modules
                            if (m_options.AutoVacuum && result is IResultsWithVacuum vacuumResults && result is BasicResults basicResults)
                            {
                                try
                                {
                                    vacuumResults.VacuumResults = new VacuumResults(basicResults);
                                    await new Operation.VacuumHandler(m_options, (VacuumResults)vacuumResults.VacuumResults)
                                        .RunAsync().ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "FailedToVacuum", ex, "Failed to vacuum database");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "FailedWriteOperation", ex, "Failed to write operation results to database: {0}", ex.Message);
                        }
                    }
                    else if (m_options.MainAction == OperationMode.Sync)
                    {
                        try
                        {
                            await WriteSyncResultsAsync(result, true).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "FailedWriteOperation", ex, "Failed to write operation results to database: {0}", ex.Message);
                        }
                    }

                    await OperationCompleteAsync(result, null, filter).ConfigureAwait(false);

                    Logging.Log.WriteInformationMessage(LOGTAG, "CompletedOperation", Strings.Controller.CompletedOperationMessage(m_options.MainAction));

                    return result;
                }
                // Handle custom abort exception from run-script
                catch (OperationAbortException oae)
                {
                    ReportModulesHandler?.OperationException = null; // Requested aborts are not failures.
                    resultSetter.EndTime = DateTime.UtcNow;
                    // Log this as a normal operation, as the script raising the exception,
                    // has already populated either warning or log messages as required
                    Logging.Log.WriteInformationMessage(LOGTAG, "AbortOperation", "Aborting operation by request, requested result: {0}", oae.AbortReason);

                    resultSetter.Interrupted = true;
                    try
                    {
                        // No operation was started in database, so write logs to new operation.
                        // Sync uses its own database schema
                        if (m_options.MainAction == OperationMode.Sync)
                            await WriteSyncResultsAsync(result, false).ConfigureAwait(false);
                        else if (File.Exists(m_options.Dbpath) && !m_options.Dryrun)
                            await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(m_options.Dbpath, result.MainOperation.ToString(), true, null, CancellationToken.None).ConfigureAwait(false))
                                await WriteResultsAsync(db, result).ConfigureAwait(false);
                    }
                    catch (Exception we)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "FailedWriteOperation", we, we.Message);
                    }

                    // Do not propagate the cancel exception
                    await OperationCompleteAsync(result, null, filter).ConfigureAwait(false);

                    return result;
                }
                // Handle an abort requested through AbortAsync, which reaches this point as a
                // cancellation. Reported the way the run-script abort above is: the user asked
                // for it, so it is not a failure of the backup.
                //
                // The guard consults the token rather than the exception type. The progress token
                // is only ever cancelled by TaskControl.Terminate, so a cancellation from anything
                // else - an HttpClient request timeout, which surfaces as a TaskCanceledException,
                // or the caller's own token - still falls through to the failure path below.
                catch (OperationCanceledException) when (m_currentTaskControl?.ProgressToken.IsCancellationRequested == true)
                {
                    ReportModulesHandler?.OperationException = null; // Requested aborts are not failures.
                    resultSetter.EndTime = DateTime.UtcNow;
                    resultSetter.Interrupted = true;

                    // Information rather than an error: the operation did what it was told to do.
                    // Fatal is deliberately not set, and the phase is left alone, so the operation
                    // is not presented as having errored.
                    Logging.Log.WriteInformationMessage(LOGTAG, "TerminatedOperation", "Terminating operation {0} by request", m_options.MainAction);

                    try
                    {
                        // Write logs to previous operation if database exists.
                        // Sync uses its own database schema
                        if (m_options.MainAction == OperationMode.Sync)
                            await WriteSyncResultsAsync(result, false).ConfigureAwait(false);
                        else if (File.Exists(m_options.Dbpath) && !m_options.Dryrun)
                            await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(m_options.Dbpath, null, true, null, CancellationToken.None).ConfigureAwait(false))
                                await WriteResultsAsync(db, result).ConfigureAwait(false);
                    }
                    catch (Exception we)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "FailedWriteOperation", we, we.Message);
                    }

                    // Reported without the exception: ReportHelper forces the Fatal level whenever
                    // it is given one, regardless of the result's own classification.
                    await OperationCompleteAsync(result, null, filter).ConfigureAwait(false);

                    // Still propagated. Callers rely on an abort surfacing as an exception rather
                    // than as a returned result.
                    throw;
                }
                catch (Exception ex)
                {
                    ReportModulesHandler?.OperationException = ex;
                    Logging.Log.WriteErrorMessage(LOGTAG, "FailedOperation", ex, Strings.Controller.FailedOperationMessage(m_options.MainAction));

                    try
                    {
                        if (result is BasicResults basicResults)
                            basicResults.OperationProgressUpdater.UpdatePhase(OperationPhase.Error);

                        resultSetter.Fatal = true;
                        // Write logs to previous operation if database exists.
                        // Sync uses its own database schema
                        if (m_options.MainAction == OperationMode.Sync)
                            await WriteSyncResultsAsync(result, false).ConfigureAwait(false);
                        else if (File.Exists(m_options.Dbpath) && !m_options.Dryrun)
                            await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(m_options.Dbpath, null, true, null, CancellationToken.None).ConfigureAwait(false))
                                await WriteResultsAsync(db, result).ConfigureAwait(false);
                    }
                    catch (Exception we)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "FailedWriteOperation", we, we.Message);
                    }

                    // Report the result, and the failure
                    await OperationCompleteAsync(result, ex, filter).ConfigureAwait(false);

                    throw;
                }
                finally
                {
                    lock (m_taskControlLock)
                        m_currentTaskControl = null;
                }
            }
        }

        /// <summary>
        /// Writes the results of a sync operation to the sync database, so they can be shown in the job log.
        /// The sync database has its own schema, so the results cannot be written with <see cref="LocalDatabase"/>.
        /// As for a backup, nothing is written for a dry run, or if the database was never created.
        /// </summary>
        /// <param name="result">The results to write</param>
        /// <param name="purgeOldResults">Whether to purge results older than the log retention period</param>
        /// <returns>A task that completes when the results have been written</returns>
        private async Task WriteSyncResultsAsync(IBasicResults result, bool purgeOldResults)
        {
            if (!File.Exists(m_options.Dbpath) || m_options.Dryrun)
                return;

            using var db = new LocalSyncDatabase(m_options.Dbpath);
            await db.WriteResultsAsync(result, CancellationToken.None).ConfigureAwait(false);
            if (purgeOldResults)
                await db.PurgeResultsAsync(m_options.LogRetention, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes the results of an operation to the job log, and remembers the entry so
        /// <see cref="UpdateResultLogAsync"/> can add what is logged after it was written.
        /// </summary>
        /// <param name="db">The database to write the results to</param>
        /// <param name="result">The results to write</param>
        /// <returns>A task that completes when the results have been written</returns>
        private async Task WriteResultsAsync(LocalDatabase db, IBasicResults result)
        {
            m_resultLogId = await db.WriteResultsAndCommitAsync(result, CancellationToken.None).ConfigureAwait(false);
            m_resultLogIssueCount = CountIssues(result);
        }

        /// <summary>
        /// Rewrites the results in the job log if warnings or errors were logged after they were
        /// written, such as a report that could not be sent. The operation is already done, so a
        /// failure here leaves the job log as it was and does not change the outcome.
        /// </summary>
        /// <param name="result">The results of the operation</param>
        /// <returns>A task that completes when the job log has been updated</returns>
        private async Task UpdateResultLogAsync(IBasicResults result)
        {
            var logId = m_resultLogId;
            m_resultLogId = -1;
            if (logId < 0 || result == null || CountIssues(result) == m_resultLogIssueCount || !File.Exists(m_options.Dbpath))
                return;

            try
            {
                await using var db = await LocalDatabase.CreateLocalDatabaseAsync(m_options.Dbpath, null, true, null, CancellationToken.None).ConfigureAwait(false);
                await db.UpdateResultsAndCommitAsync(logId, result, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Verbose, as a warning would itself change the outcome of the operation
                Logging.Log.WriteVerboseMessage(LOGTAG, "FailedUpdateOperation", ex, "Failed to update operation results in database: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Counts the warnings and errors in the results.
        /// </summary>
        /// <param name="result">The results to count in</param>
        /// <returns>The number of warnings and errors</returns>
        private static int CountIssues(IBasicResults result)
            => (result.Warnings?.Count() ?? 0) + (result.Errors?.Count() ?? 0);

        /// <summary>
        /// Temporarily pushes the filter into the raw options so modules can read it.
        /// Returns a disposable that removes the key when disposed.
        /// </summary>
        /// <param name="options">The options to push the filter into</param>
        /// <param name="filter">The filter to push</param>
        /// <returns>A disposable that removes the key when disposed</returns>
        internal static IDisposable PushFilterToOptions(Options options, IFilter filter)
            => PushFilterToOptions(options, filter, out var _);
        /// <summary>
        /// Temporarily pushes the filter into the raw options so modules can read it.
        /// Returns a disposable that removes the key when disposed.
        /// </summary>
        /// <param name="options">The options to push the filter into</param>
        /// <param name="filter">The filter to push</param>
        /// <returns>A disposable that removes the key when disposed</returns>
        internal static IDisposable PushFilterToOptions(Options options, IFilter filter, out string filterString)
        {
            filterString = filter != null && !filter.Empty
                ? string.Join(Path.PathSeparator.ToString(), FilterExpression.Serialize(filter))
                : null;

            options.RawOptions["filter"] = filterString;
            return new FilterOptionScope(options);
        }

        /// <summary>
        /// Helper to remove the filter option on dispose
        /// </summary>
        private sealed class FilterOptionScope : IDisposable
        {
            private readonly Options m_opts;
            public FilterOptionScope(Options opts) => m_opts = opts;
            public void Dispose() => m_opts?.RawOptions.Remove("filter");
        }

        private async Task OperationCompleteAsync(IBasicResults result, Exception exception, IFilter filter)
        {
            using var _ = PushFilterToOptions(m_options, filter);

            // Fire the report modules' completion callbacks and stop their progress ticker
            // before the module instances themselves are disposed below. The handle reads
            // the exception captured in the RunActionAsync catch blocks via the field.
            try
            {
                if (this.ReportModulesHandler != null)
                {
                    // Wait at most 30s for the report modules to stop
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await this.ReportModulesHandler.StopAsync(cts.Token);
                }
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "ReportModuleHandleDisposeError", ex, "Report module handle dispose failed: {0}", ex.Message);
            }

            this.ReportModulesHandler = default;

            if (m_options?.LoadedModules != null)
            {
                foreach (var mx in m_options.LoadedModules)
                    if (mx is IGenericCallbackModule module)
                        try { RunScriptCallbackWithStatus(result, mx, () => module.OnFinish(result, exception)); }
                        catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, $"OnFinishError{mx.Key}", ex, "OnFinish callback {0} failed: {1}", mx.Key, ex.Message); }

                foreach (var mx in m_options.LoadedModules)
                    if (mx is IDisposable disposable)
                        try { disposable.Dispose(); }
                        catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, $"DisposeError{mx.Key}", ex, "Dispose for {0} failed: {1}", mx.Key, ex.Message); }

                m_options.ClearLoadedModules();
            }

            if (m_localeChange != null)
            {
                m_localeChange.Dispose();
                m_localeChange = null;
            }

            await UpdateResultLogAsync(result).ConfigureAwait(false);

            OnOperationCompleted?.Invoke(result, exception);
        }

        private (string[] paths, IFilter filter) SetupCommonOptions(ISetCommonOptions result, string[] paths, IFilter filter, ControllerMultiLogTarget logTarget)
        {
            m_options.MainAction = result.MainOperation;

            switch (m_options.MainAction)
            {
                case OperationMode.Backup:
                    break;

                default:
                    //It only makes sense to enable auto-creation if we are writing files.
                    if (!m_options.RawOptions.ContainsKey("disable-autocreate-folder"))
                        m_options.RawOptions["disable-autocreate-folder"] = "true";
                    break;
            }

            if (string.IsNullOrEmpty(m_options.Dbpath))
                m_options.Dbpath = CLIDatabaseLocator.GetDatabasePathForCLI(m_backendUrl, m_options, true, false, m_options.MainAction == OperationMode.Sync);

            RestoreOptionsFromExistingDatabase(result.MainOperation);

            //Load generic modules
            m_options.ClearLoadedModules();

            foreach (var m in DynamicLoader.GenericLoader.Modules)
            {
                var active = !m_options.DisableModules.Contains(m.Key, StringComparer.OrdinalIgnoreCase) && (m.LoadAsDefault || m_options.EnableModules.Contains(m.Key, StringComparer.OrdinalIgnoreCase));
                if (!active)
                    continue;

                m_options.AddLoadedModule(DynamicLoader.GenericLoader.GetModule(m.Key));
            }

            // Store the URL connection options separately, as these should only be visible to modules implementing IConnectionModule
            var conopts = new Dictionary<string, string>(m_options.RawOptions);
            var qp = new Library.Utility.RelaxedUri(m_backendUrl).QueryParameters;
            foreach (var k in qp.Keys)
                conopts[(string)k] = qp[(string)k];

            // Make the filter read-n-write able in the generic modules
            using (PushFilterToOptions(m_options, filter, out var pristinefilter))
            {
                // A module that fails the operation in OnStart (a run-script-before script) is
                // started before the report modules, so the remaining modules are still configured
                // and started. Otherwise their OnFinish has nothing to report the failure with.
                // A script asking to skip the run without a warning or error is not reported.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo startFailure = null;
                foreach (var mx in m_options.LoadedModules)
                {
                    if (mx is IConnectionModule)
                        mx.Configure(conopts);
                    else
                        mx.Configure(m_options.RawOptions);

                    if (mx is IGenericCallbackModule module)
                    {
                        try
                        {
                            RunScriptCallbackWithStatus(result, mx, () => module.OnStart(result.MainOperation.ToString(), ref m_backendUrl, ref paths));
                        }
                        catch (OperationAbortException oae) when (startFailure == null && oae.AbortReason == OperationAbortReason.Normal)
                        {
                            throw;
                        }
                        catch (Exception ex) when (startFailure == null)
                        {
                            startFailure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, $"OnStartError{mx.Key}", ex, "OnStart callback {0} failed: {1}", mx.Key, ex.Message);
                        }
                    }
                }

                startFailure?.Throw();

                // If the filters were changed by a module, read them back in
                if (pristinefilter != m_options.RawOptions["filter"])
                    filter = FilterExpression.Deserialize(m_options.RawOptions["filter"].Split([Path.PathSeparator.ToString()], StringSplitOptions.RemoveEmptyEntries));
            }

            if (!string.IsNullOrEmpty(m_options.Logfile))
            {
                var path = Path.GetDirectoryName(Path.GetFullPath(m_options.Logfile));
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                logTarget.AddTarget(
                    new Library.Logging.StreamLogDestination(m_options.Logfile),
                    m_options.LogFileLoglevel,
                    m_options.LogFileLogFilter
                );
            }

            if (m_options.HasTempDir)
            {
                Library.Utility.TempFolder.SystemTempPath = m_options.TempDir;
            }

            if (m_options.HasForcedLocale)
            {
                try
                {
                    m_localeChange = new LocaleChange(m_options.ForcedLocale);
                }
                catch (Exception ex)
                {
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "LocaleChangeError", ex, Strings.Controller.FailedForceLocaleError(ex.Message));
                }
            }

            ValidateOptions(paths);

            return (paths, filter);
        }

        internal static void RunScriptCallbackWithStatus(object result, IGenericModule module, Action callback)
        {
            if (!string.Equals(module?.Key, "runscript", StringComparison.OrdinalIgnoreCase) || result is not BasicResults basicResults)
            {
                callback();
                return;
            }

            var progress = basicResults.OperationProgressUpdater;
            progress.UpdateOverall(out var previousPhase, out _, out _, out _, out _, out _, out _);

            progress.UpdatePhase(OperationPhase.RunScript_Running);
            try
            {
                callback();
            }
            finally
            {
                progress.UpdatePhase(previousPhase);
            }
        }


        private void RestoreOptionsFromExistingDatabase(OperationMode operation)
        {
            // The sync database (LocalSyncDatabase) has its own schema, distinct from the
            // backup LocalDatabase, and does not store options to restore. Opening it as a
            // backup database would run the backup schema upgrade against an incompatible
            // schema and throw. Sync reads its options directly from the command-line dict.
            if (operation == OperationMode.Sync)
                return;

            if (m_options.NoLocalDb || string.IsNullOrWhiteSpace(m_options.Dbpath) || !File.Exists(m_options.Dbpath))
                return;

            using var db = LocalDatabase.CreateLocalDatabaseAsync(m_options.Dbpath, operation.ToString(), true, null, CancellationToken.None).Await();
            Utility.UpdateOptionsFromDbAsync(db, m_options, CancellationToken.None).Await();
        }

        private async Task ApplySecretProviderAsync(CancellationToken cancellationToken)
        {
            var args = new[] { new Library.Utility.RelaxedUri(m_backendUrl) };
            await SecretProviderHelper.ApplySecretProviderAsync([], args, m_options.RawOptions, TempFolder.SystemTempPath, SecretProvider, cancellationToken).ConfigureAwait(false);
            // Write back the backend argument, if it was modified by the secret provider
            m_backendUrl = args[0].ToString();
        }

        /// <summary>
        /// Warns if an identifier option holds characters that are known to cause trouble. The value is
        /// kept as given, as it may already be in use elsewhere; the user decides whether to change it.
        /// </summary>
        /// <param name="option">The name of the identifier option</param>
        /// <param name="messageId">The id of the warning</param>
        private void WarnOnProblematicIdentifier(string option, string messageId)
        {
            var problematic = Options.GetProblematicIdentifierCharacters(m_options.RawOptions.GetValueOrDefault(option));
            if (problematic.Count > 0)
                Logging.Log.WriteWarningMessage(LOGTAG, messageId, null, "The option --{0} contains the character(s) {1}, which may cause problems when the id is used in reports and by remote control. Consider using only letters, digits, dashes and dots, or add {2} to --suppress-warnings to keep the id.", option, string.Join(" ", problematic.Select(Options.DescribeIdentifierCharacter)), messageId);
        }

        /// <summary>
        /// This function will examine all options passed on the commandline, and test for unsupported or deprecated values.
        /// Any errors will be logged into the statistics module.
        /// </summary>
        /// <param name="paths">The source paths, used to determine the options supported by source providers; null for operations without sources.</param>
        private void ValidateOptions(string[] paths)
        {
            // The ids are passed around by the console and in reports, where some characters break the handling
            WarnOnProblematicIdentifier("backup-id", "BackupIdSpecialCharacters");
            WarnOnProblematicIdentifier("machine-id", "MachineIdSpecialCharacters");

            // Check if only one of the retention options is set
            var selectedRetentionOptions = new List<String>();

            if (m_options.KeepTime.Ticks > 0)
            {
                selectedRetentionOptions.Add("keep-time");
            }

            if (m_options.KeepVersions > 0)
            {
                selectedRetentionOptions.Add("keep-versions");
            }

            //Check validity of retention-policy option value
            List<Options.RetentionPolicyValue> retentionPolicy;
            try
            {
                retentionPolicy = m_options.RetentionPolicy;
                Options.ValidateRetentionPolicy(retentionPolicy);
            }
            catch (Exception e) // simply reading the option value might also result in an exception due to incorrect formatting
            {
                throw new Interface.UserInformationException(string.Format("An error occoured while processing the value of --{0}: {1}", "retention-policy", e.Message), "RetentionPolicyParseError", e);
            }

            if (retentionPolicy.Any())
            {
                selectedRetentionOptions.Add("retention-policy");
            }

            if (selectedRetentionOptions.Count() > 1)
            {
                throw new UserInformationException(string.Format("Setting multiple retention options ({0}) is not permitted",
                    string.Join(", ", selectedRetentionOptions.Select(x => "--" + x))), "MultipleRetentionOptionsNotSupported");
            }

            // Check Prefix
            if (!string.IsNullOrWhiteSpace(m_options.Prefix) && m_options.Prefix.Contains("-"))
                throw new UserInformationException("The prefix cannot contain hyphens (-)", "PrefixCannotContainHyphens");

            if (m_options.VolumeSize < m_options.Blocksize * 2)
                throw new UserInformationException("The volume size must be at least twice the block size", "VolumeSizeTooSmall");

            //Keep a list of all supplied options
            var ropts = new Dictionary<string, string>(m_options.RawOptions);

            //Keep a list of all supported options
            var supportedOptions = new Dictionary<string, ICommandLineArgument>();

            //There are a few internal options that are not accessible from outside, and thus not listed
            foreach (var s in Options.InternalOptions)
                supportedOptions[s] = null;

            //Figure out what module options are supported in the current setup
            var moduleOptions = new List<ICommandLineArgument>();
            var disabledModuleOptions = new Dictionary<string, string>();
            var loadedModuleKeys = m_options.LoadedModules.Select(k => k.Key).ToHashSet();

            foreach (var m in DynamicLoader.GenericLoader.Modules)
            {
                if (m.SupportedCommands == null)
                    continue;

                if (loadedModuleKeys.Contains(m.Key))
                    moduleOptions.AddRange(m.SupportedCommands);
                else
                    foreach (var c in m.SupportedCommands)
                    {
                        disabledModuleOptions[c.Name] = m.DisplayName + " (" + m.Key + ")";

                        if (c.Aliases != null)
                            foreach (string s in c.Aliases)
                                disabledModuleOptions[s] = disabledModuleOptions[c.Name];
                    }
            }

            //Figure out what options are supported by the source providers of the current sources
            var sourceProviderOptions = new List<ICommandLineArgument>();
            var sourceProviderSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths ?? [])
            {
                var remoteSource = SourceProviderFactory.ParseRemoteSource(path);
                if (remoteSource == null)
                    continue;

                try
                {
                    var sourceUri = new Library.Utility.RelaxedUri(remoteSource.Url);

                    // Throw url-encoded options into the mix
                    //TODO: This can hide values if both commandline and url-parameters supply the same key
                    foreach (var k in sourceUri.QueryParameters.AllKeys)
                        ropts[k] = sourceUri.QueryParameters[k];

                    // Each source provider scheme contributes its options once,
                    // even if multiple sources use the same scheme
                    if (!sourceProviderSchemes.Add(sourceUri.Scheme))
                        continue;

                    var sourcecommands = DynamicLoader.SourceProviderLoader.GetSupportedCommands(remoteSource.Url);
                    if (sourcecommands != null)
                        sourceProviderOptions.AddRange(sourcecommands);
                }
                catch
                {
                    // Invalid source urls are reported when the source provider is loaded
                    continue;
                }
            }

            // The prefix-based source providers (e.g. %HYPERV%) contribute their options
            // when one of the sources is theirs
            foreach (var module in Library.SourceProviders.SourceProviderModules.BuiltInPrefixSourceProviderModules)
                if (module.SupportedCommands != null && (paths ?? []).Any(module.MatchesSource))
                    sourceProviderOptions.AddRange(module.SupportedCommands);

            //Figure out what options are supported by the restore destination provider, if the restore target is remote
            var restoreDestinationOptions = new List<ICommandLineArgument>();
            var restorepath = m_options.Restorepath;
            if (!string.IsNullOrEmpty(restorepath) && restorepath.StartsWith("@"))
            {
                try
                {
                    var restoreUri = new Library.Utility.RelaxedUri(restorepath.Substring(1));

                    // Throw url-encoded options into the mix
                    foreach (var k in restoreUri.QueryParameters.AllKeys)
                        ropts[k] = restoreUri.QueryParameters[k];

                    var restorecommands = DynamicLoader.RestoreDestinationProviderLoader.GetSupportedCommands(restorepath.Substring(1));
                    if (restorecommands != null)
                        restoreDestinationOptions.AddRange(restorecommands);
                }
                catch
                {
                    // Invalid restore destination urls are reported when the restore destination provider is loaded
                }
            }

            // Throw url-encoded options into the mix
            //TODO: This can hide values if both commandline and url-parameters supply the same key
            var ext = new Library.Utility.RelaxedUri(m_backendUrl).QueryParameters;
            foreach (var k in ext.AllKeys)
                ropts[k] = ext[k];

            var backendCommands = DynamicLoader.BackendLoader.GetSupportedCommands(m_backendUrl);
            var encryptionCommands = m_options.NoEncryption ? [] : DynamicLoader.EncryptionLoader.GetSupportedCommands(m_options.EncryptionModule);
            var compressionCommands = DynamicLoader.CompressionLoader.GetSupportedCommands(m_options.CompressionModule);
            var parityCommands = string.IsNullOrEmpty(m_options.ParityModule) ? [] : DynamicLoader.ParityLoader.GetSupportedCommands(m_options.ParityModule);

            // The same module can legitimately contribute its options more than once, e.g. when
            // the target backend is also used as a source, or a provider is used as both the
            // source and the restore destination. Silently drop such identical re-declarations,
            // while keeping re-declarations with a differing definition, so the duplicate check
            // below still reports those.
            var declaredOptions = new Dictionary<string, ICommandLineArgument>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in new IEnumerable<ICommandLineArgument>[] { m_options.SupportedCommands, backendCommands, encryptionCommands, moduleOptions, compressionCommands, parityCommands })
                if (l != null)
                    foreach (var a in l)
                    {
                        declaredOptions.TryAdd(a.Name, a);
                        if (a.Aliases != null)
                            foreach (var s in a.Aliases)
                                declaredOptions.TryAdd(s, a);
                    }

            sourceProviderOptions = FilterIdenticalRedeclarations(sourceProviderOptions, declaredOptions);
            restoreDestinationOptions = FilterIdenticalRedeclarations(restoreDestinationOptions, declaredOptions);

            //Now run through all supported options, and look for deprecated options
            foreach (var l in new IEnumerable<ICommandLineArgument>[] {
                m_options.SupportedCommands,
                backendCommands,
                encryptionCommands,
                moduleOptions,
                sourceProviderOptions,
                restoreDestinationOptions,
                compressionCommands,
                parityCommands })
            {
                if (l != null)
                    foreach (ICommandLineArgument a in l)
                    {
                        if (supportedOptions.ContainsKey(a.Name) && !Options.KnownDuplicates.Contains(a.Name))
                            Logging.Log.WriteWarningMessage(LOGTAG, "DuplicateOption", null, Strings.Controller.DuplicateOptionNameWarning(a.Name));

                        supportedOptions[a.Name] = a;

                        if (a.Aliases != null)
                            foreach (string s in a.Aliases)
                            {
                                if (supportedOptions.ContainsKey(s) && !Options.KnownDuplicates.Contains(s))
                                    Logging.Log.WriteWarningMessage(LOGTAG, "DuplicateOption", null, Strings.Controller.DuplicateOptionNameWarning(s));

                                supportedOptions[s] = a;
                            }

                        if (a.Deprecated)
                        {
                            List<string> aliases = new List<string>();
                            aliases.Add(a.Name);
                            if (a.Aliases != null)
                                aliases.AddRange(a.Aliases);

                            foreach (string s in aliases)
                                if (ropts.ContainsKey(s))
                                {
                                    string optname = a.Name;
                                    if (a.Name != s)
                                        optname += " (" + s + ")";

                                    Logging.Log.WriteWarningMessage(LOGTAG, "DeprecatedOption", null, Strings.Controller.DeprecatedOptionUsedWarning(optname, a.DeprecationMessage), null);
                                }

                        }

                        if (a.Type == CommandLineArgument.ArgumentType.Boolean && a.DefaultValue == "true" && !a.Deprecated)
                            Logging.Log.WriteWarningMessage(LOGTAG, "DefaultBooleanOption", null, Strings.Controller.WrongDefaultBooleanOptionWarning(a.Name), null);
                    }
            }

            //Now look for options that were supplied but not supported
            foreach (var s in ropts.Keys)
                if (!supportedOptions.ContainsKey(s))
                    if (disabledModuleOptions.ContainsKey(s))
                        Logging.Log.WriteWarningMessage(LOGTAG, "UnsupportedDisabledModule", null, Strings.Controller.UnsupportedOptionDisabledModuleWarning(s, disabledModuleOptions[s]), null);
                    else
                        Logging.Log.WriteWarningMessage(LOGTAG, "UnsupportedOption", null, Strings.Controller.UnsupportedOptionWarning(s), null);

            //Look at the value supplied for each argument and see if is valid according to its type
            foreach (var s in ropts.Keys)
            {
                if (supportedOptions.TryGetValue(s, out var arg) && arg != null)
                {
                    var validationMessage = CommandLineArgumentValidator.ValidateOptionValue(arg, s, ropts[s]);
                    if (!string.IsNullOrWhiteSpace(validationMessage))
                        Logging.Log.WriteWarningMessage(LOGTAG, "OptionValidationError", null, validationMessage);
                }
            }

            var scheme = Library.Utility.Utility.GuessScheme(m_backendUrl);
            if (backendCommands == null && !string.Equals(m_backendUrl, "dummy://", StringComparison.OrdinalIgnoreCase))
            {
                // If the backend is HTTP or HTTPS, give a custom error message
                if (string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) || string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase))
                    throw new UserInformationException(Strings.Controller.BackendNotSupportedHttpError, "BackendNotSupportedHttp");

                throw new UserInformationException(Strings.Controller.BackendNotSupportedError(scheme, string.Join(", ", DynamicLoader.BackendLoader.Keys)), "BackendNotSupported");
            }

            //Inform the user about unmaintained backends
            if (BackendModules.DeprecatedBackendModules.Contains(scheme))
                Logging.Log.WriteWarningMessage(LOGTAG, "BackendDeprecated", null, $"The backend {scheme} is deprecated and should be migrated away from.");

            //Warn about experimental diskimage filesystem parsing
            if (m_options.RawOptions.ContainsKey("diskimage-filesystem-parsed"))
                Logging.Log.WriteWarningMessage(LOGTAG, "ExperimentalDiskImageFilesystemParsed", null, "The option 'diskimage-filesystem-parsed' is experimental and should be used with caution.");

            //TODO: Based on the action, see if all options are relevant
        }

        /// <summary>
        /// Removes commands that are identical re-declarations of an already declared option,
        /// and registers the remaining commands as declared, so that later lists are filtered
        /// against them as well.
        /// </summary>
        /// <param name="commands">The commands to filter</param>
        /// <param name="declaredOptions">The options declared so far, keyed by name and alias</param>
        /// <returns>The commands that are not identical re-declarations</returns>
        private static List<ICommandLineArgument> FilterIdenticalRedeclarations(List<ICommandLineArgument> commands, Dictionary<string, ICommandLineArgument> declaredOptions)
        {
            var result = new List<ICommandLineArgument>(commands.Count);
            foreach (var c in commands)
            {
                if (declaredOptions.TryGetValue(c.Name, out var existing)
                    && existing.Type == c.Type
                    && existing.DefaultValue == c.DefaultValue)
                    continue;

                result.Add(c);
                declaredOptions.TryAdd(c.Name, c);
                if (c.Aliases != null)
                    foreach (var s in c.Aliases)
                        declaredOptions.TryAdd(s, c);
            }

            return result;
        }

        /// <summary>
        /// Helper method that expands the users chosen source input paths,
        /// and removes duplicate paths
        /// </summary>
        /// <param name="inputsources">The input sources.</param>
        /// <param name="filter">The filter.</param>
        /// <param name="options">The options.</param>
        /// <returns>The expanded and filtered sources.</returns>
        internal static (string[] Sources, IFilter Filter) ExpandInputSources(string[] inputsources, IFilter filter, Options options)
        {
            if (inputsources == null || inputsources.Length == 0)
                throw new UserInformationException(Strings.Controller.NoSourceFoldersError, "NoSourceFolders");

            var sources = new List<string>(inputsources.Length);

            // Filters may name the content of a prefix-based source provider the same way as the sources do
            filter = SourceProviderFactory.TranslatePrefixedFilters(filter);

            DriveInfo[] drives = null;

            //Make sure they all have the same format and exist
            foreach (var inputsource in inputsources)
            {
                List<string> expandedSources = new List<string>();

                if (OperatingSystem.IsWindows() && (inputsource.StartsWith("*:", StringComparison.Ordinal) || inputsource.StartsWith("?:", StringComparison.Ordinal)))
                {
                    // *: drive paths are only supported on Windows clients
                    // Lazily load the drive info
                    drives = drives ?? DriveInfo.GetDrives();

                    // Replace the drive letter with each available drive
                    string sourcePath = inputsource.Substring(1);
                    foreach (DriveInfo drive in drives)
                    {
                        string expandedSource = drive.Name[0] + sourcePath;
                        Logging.Log.WriteVerboseMessage(LOGTAG, "AddingSourcePathFromWildcard", @"Adding source path ""{0}"" due to wildcard source path ""{1}""", expandedSource, inputsource);
                        expandedSources.Add(expandedSource);
                    }
                }
                else if (OperatingSystem.IsWindows() && inputsource.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
                {
                    // In order to specify a drive by it's volume name, adopt the volume guid path syntax:
                    //   \\?\Volume{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}
                    // The volume guid can be found using the 'mountvol' commandline tool.
                    // However, instead of using this path with Windows APIs directory, it is adapted here to a standard path.
                    Guid volumeGuid;
                    if (Guid.TryParse(inputsource.Substring(@"\\?\Volume{".Length, @"XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX".Length), out volumeGuid))
                    {
                        string driveLetter = Library.Utility.Utility.GetDriveLetterFromVolumeGuid(volumeGuid);
                        if (!string.IsNullOrEmpty(driveLetter))
                        {
                            string expandedSource = driveLetter + inputsource.Substring(@"\\?\Volume{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}".Length);
                            Logging.Log.WriteVerboseMessage(LOGTAG, "AddingSourceFromGuid", @"Adding source path ""{0}"" in place of volume guid source path ""{1}""", expandedSource, inputsource);
                            expandedSources.Add(expandedSource);
                        }
                        else
                        {
                            // Couldn't find a drive where this volume is mounted: abort if requested,
                            // otherwise warn (unless missing sources are silently allowed)
                            if (options.AbortIfSourceMissing)
                                throw new UserInformationException(Strings.Controller.SourceVolumeNameNotFoundError(inputsource, volumeGuid), "MissingSourceFolder");
                            else if (!options.AllowMissingSource)
                                Logging.Log.WriteWarningMessage(LOGTAG, "SourceVolumeNotFound", null, Strings.Controller.SourceVolumeNameNotFoundError(inputsource, volumeGuid));
                        }
                    }
                    else
                    {
                        // Couldn't parse/find this volume: abort if requested, otherwise warn
                        // (unless missing sources are silently allowed)
                        if (options.AbortIfSourceMissing)
                            throw new UserInformationException(Strings.Controller.SourceVolumeNameInvalidError(inputsource), "SourceVolumeNameInvalid");
                        else if (!options.AllowMissingSource)
                            Logging.Log.WriteWarningMessage(LOGTAG, "SourceVolumeInvalid", null, Strings.Controller.SourceVolumeNameInvalidError(inputsource));
                    }
                }
                else
                {
                    expandedSources.Add(inputsource);
                }

                var foundAnyPaths = false;
                var unauthorized = false;
                foreach (var expandedSource in expandedSources)
                {
                    string source;
                    // Check if this is a mounted path, avoid logging the source if something fails here
                    if (expandedSource.StartsWith("@"))
                    {
                        // NOTE: If the remote source fails to load,
                        // this will be an enumeration warning, but will result
                        // in the backup being recorded without files from the source
                        // Eventually, this could lead to retention deletion,
                        // causing the last backup with the data from the source to be deleted

                        // TODO: We do not honor the PreventEmptySource option here,
                        // as we do not know if the source is empty or not
                        foundAnyPaths = true;
                        sources.Add(expandedSource);
                        continue;
                    }

                    // Prefix-based source providers (e.g. %HYPERV%, %MSSQL%) are virtual paths
                    // handled by a source provider, so they should not be checked against the filesystem
                    if (Library.SourceProviders.SourceProviderModules.BuiltInPrefixSourceProviderModules.Any(m => m.MatchesSource(expandedSource)))
                    {
                        foundAnyPaths = true;
                        sources.Add(expandedSource);
                        continue;
                    }

                    try
                    {
                        // A bare drive is drive-relative, so GetFullPath would answer with
                        // the current directory on that drive instead of the root of it
                        source = Path.GetFullPath(Util.ExpandBareDriveRoot(expandedSource));
                    }
                    catch (Exception ex)
                    {
                        // Note that we use the original source (with the *) in the error
                        throw new UserInformationException(Strings.Controller.InvalidPathError(expandedSource, ex.Message), "InputSourceInvalid", ex);
                    }

                    var fi = new FileInfo(source);
                    var di = new DirectoryInfo(source);
                    if (fi.Exists || di.Exists)
                    {
                        foundAnyPaths = true;

                        if (!fi.Exists)
                        {
                            // Listing the folder also shows that it can be read. One that cannot, as on a
                            // failed drive, would be backed up without any of its files, and that version
                            // would push out the previous ones. A missing source is only skipped when allowed,
                            // and an unreadable one is treated the same way.
                            try
                            {
                                if (di.Exists && (options.PreventEmptySource || !options.AllowMissingSource))
                                {
                                    // If the directory exists, but is empty, and we are not allowed to have empty sources, throw an error
                                    var isEmpty = !di.EnumerateFileSystemInfos().Any();
                                    if (options.PreventEmptySource && isEmpty)
                                        throw new UserInformationException(Strings.Controller.SourceFolderEmptyError(inputsource), "SourceFolderEmpty");
                                }
                            }
                            catch (UnauthorizedAccessException ex)
                            {
                                throw new UserInformationException(Strings.Controller.SourceUnauthorizedError(inputsource), "SourceUnauthorized", ex);
                            }
                            catch (IOException ex)
                            {
                                throw new UserInformationException(Strings.Controller.SourceUnreadableError(inputsource, ex.Message), "SourceUnreadable", ex);
                            }

                            source = Util.AppendDirSeparator(source);
                        }

                        sources.Add(source);
                    }
                    else
                    {
                        try
                        {
                            // Try to get attributes. Returns -1 if source doesn't exist, otherwise throws an exception.
                            // In this case, it is irrelevant to use fileinfo or directoryinfo to retrieve attributes.
                            var unused = fi.Attributes;
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "AddingSourceFolder",
                                                            ex, @"Insufficient permissions to read ""{0}"", skipping", expandedSource);
                            unauthorized = true;
                        }
                    }
                }

                // If no paths were found, handle according to options
                if (!foundAnyPaths)
                {
                    if (options.AbortIfSourceMissing)
                    {
                        if (unauthorized)
                            throw new UserInformationException(Strings.Controller.SourceUnauthorizedError(inputsource), "SourceUnauthorized");

                        throw new UserInformationException(Strings.Controller.SourceIsMissingError(inputsource), "SourceIsMissing");
                    }
                    else if (!options.AllowMissingSource)
                    {
                        if (unauthorized)
                            throw new UserInformationException(Strings.Controller.SourceUnauthorizedError(inputsource), "SourceUnauthorized");

                        Logging.Log.WriteWarningMessage(LOGTAG, "SourceIsMissing", null, Strings.Controller.SourceIsMissingWarning(inputsource));
                    }
                }
            }

            //Sanity check for duplicate files/folders
            ISet<string> pathDuplicates;
            sources = Library.Utility.Utility.GetUniqueItems(sources, Library.Utility.Utility.ClientFilenameStringComparer, out pathDuplicates).ToList();

            foreach (var pathDuplicate in pathDuplicates)
                Logging.Log.WriteVerboseMessage(LOGTAG, "RemoveDuplicateSource", "Removing duplicate source: {0}", pathDuplicate);

            //Sanity check for multiple inclusions of the same folder
            for (int i = 0; i < sources.Count; i++)
            {
                // A link on the way that the walk stores as a link, if one was met
                string storedLink = null;

                for (int j = 0; j < sources.Count; j++)
                    if (i != j
                        && sources[i].StartsWith(sources[j], Library.Utility.Utility.ClientFilenameStringComparison)
                        && sources[j].EndsWith(Util.DirectorySeparatorString, Library.Utility.Utility.ClientFilenameStringComparison))
                    {
                        var reach = WalkTowards(sources[i], sources[j], filter, options, out var link);

                        // An include filter cannot bring it back when the walk of the
                        // containing source stops on the way to it, because the filter is
                        // never reached. Keeping it as a source is what works, because a
                        // source is walked from its own root, and the containing source still
                        // stops where it is told to. Carry on looking: another source may
                        // still be able to reach it, and keeping one that is reachable would
                        // walk the same tree twice.
                        if (reach == NestedSourceReach.CutOff)
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "KeepingSubfolderSource", "Keeping source \"{0}\" although it is inside \"{1}\", because the walk of the latter does not reach it", sources[i], sources[j]);
                            continue;
                        }

                        // A link that is stored as a link cannot be kept as a source as well:
                        // the backup would then hold the link and the folder behind it under
                        // the same path. Carry on looking, as another source may reach it.
                        if (reach == NestedSourceReach.StoredAsLink)
                        {
                            storedLink ??= link;
                            continue;
                        }

                        if (filter != null)
                        {
                            bool excludes;

                            FilterExpression.AnalyzeFilters(filter, out _, out excludes);

                            // If there are no excludes, there is no need to keep the folder as a filter
                            if (excludes)
                            {
                                Logging.Log.WriteVerboseMessage(LOGTAG, "RemovingSubfolderSource", "Removing source \"{0}\" because it is a folder or file inside \"{1}\", and using it as an include filter", sources[i], sources[j]);
                                filter = JoinedFilterExpression.Join(new FilterExpression(sources[i]), filter);
                            }
                            else
                                Logging.Log.WriteVerboseMessage(LOGTAG, "RemovingSubfolderSource", "Removing source \"{0}\" because it is a folder or file inside \"{1}\"", sources[i], sources[j]);
                        }
                        else
                            Logging.Log.WriteVerboseMessage(LOGTAG, "RemovingSubfolderSource", "Removing source \"{0}\" because it is a folder or file inside \"{1}\"", sources[i], sources[j]);

                        sources.RemoveAt(i);
                        i--;
                        storedLink = null;
                        break;
                    }

                // No source reaches it, and one of them stores a link on the way as a link.
                // Keeping it would put the link and the folder behind it under the same path,
                // so it is left out, but not quietly.
                if (storedLink != null)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "NestedSourceBehindStoredSymlink", null, "The source \"{0}\" is not backed up: it is reached through the symbolic link \"{1}\", which is inside another source and is stored as a link. Set --symlink-policy=follow to back up what the link points to.", sources[i], storedLink);
                    sources.RemoveAt(i);
                    i--;
                }
            }

            // If there is nothing to do, don't run the backup
            if (sources.Count == 0)
                throw new UserInformationException(Strings.Controller.NoSourcesError, "NoSources");

            return (sources.ToArray(), filter);
        }

        /// <summary>
        /// How the walk of a source treats another source inside it
        /// </summary>
        internal enum NestedSourceReach
        {
            /// <summary>The walk reaches the inner source</summary>
            Reached,
            /// <summary>The walk stops before it reaches the inner source, or leaves the inner source out</summary>
            CutOff,
            /// <summary>The walk stores a link on the way, or the inner source itself, as a link and does not follow it</summary>
            StoredAsLink
        }

        /// <summary>
        /// Follows the walk of <paramref name="container"/> towards <paramref name="source"/>,
        /// and reports whether it gets there. The walk does not descend into a folder that the
        /// filter excludes, that holds an ignore marker, whose attributes are excluded, that is
        /// marked as excluded from backups by an extended attribute, or that is a symbolic link
        /// it does not follow. Nothing below such a folder is reached, no matter what the filter
        /// says about the things below it.
        /// </summary>
        /// <returns>How the walk treats the source.</returns>
        /// <param name="source">The source that sits inside the other one.</param>
        /// <param name="container">The source that contains it, ending with a separator.</param>
        /// <param name="filter">The filter to ask.</param>
        /// <param name="options">The options that decide what the walk leaves out.</param>
        /// <param name="link">The link that is stored as a link, if that is where the walk stops.</param>
        internal static NestedSourceReach WalkTowards(string source, string container, IFilter filter, Options options, out string link)
        {
            link = null;

            // A mounted source is not a path on this machine, so there are no folders
            // between the two to ask about
            if (source.StartsWith("@", StringComparison.Ordinal) || container.StartsWith("@", StringComparison.Ordinal))
                return NestedSourceReach.Reached;

            // Every folder between the two, asked for in the form the enumeration uses,
            // which is with a trailing separator. The container is not one of them: it is
            // a root, and the walk never leaves out a root.
            var relative = source.Substring(container.Length);
            for (var at = relative.IndexOf(Util.DirectorySeparatorString, StringComparison.Ordinal);
                 at >= 0;
                 at = relative.IndexOf(Util.DirectorySeparatorString, at + 1, StringComparison.Ordinal))
            {
                var folder = container + relative.Substring(0, at + 1);
                if (folder.Length >= source.Length)
                    break;

                var reach = WalkPast(folder, filter, options);
                if (reach != NestedSourceReach.Reached)
                {
                    if (reach == NestedSourceReach.StoredAsLink)
                        link = folder;
                    return reach;
                }
            }

            // The source itself as well. The filter is not asked about it, because the include
            // filter that replaces it overrules an exclude, but everything else is, as the
            // enumeration either checks it before the filter, or checks it even when the filter
            // includes the path, so the include filter never gets a say.
            var own = WalkPast(source, null, options);
            if (own == NestedSourceReach.StoredAsLink)
                link = source;
            return own;
        }

        /// <summary>
        /// Reports how the walk treats a path that is not a root. These are the checks the
        /// enumeration makes, in the order it makes them, and like it, a path that cannot be
        /// read is not left out.
        /// </summary>
        /// <returns>How the walk treats the path.</returns>
        /// <param name="path">The path to check, ending with a separator if it is a folder.</param>
        /// <param name="filter">The filter to ask, or <c>null</c> to not ask it.</param>
        /// <param name="options">The options that decide what the walk leaves out.</param>
        private static NestedSourceReach WalkPast(string path, IFilter filter, Options options)
        {
            var isFolder = path.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal);

            var ignorenames = options.IgnoreFilenames;
            if (ignorenames != null && isFolder)
            {
                try
                {
                    if (ignorenames.Any(n => SystemIO.IO_OS.FileExists(Path.Combine(path, n))))
                        return NestedSourceReach.CutOff;
                }
                catch
                {
                }
            }

            FileAttributes? attributes = null;
            try
            {
                attributes = SystemIO.IO_OS.GetFileAttributes(path);
            }
            catch
            {
            }

            var attributeFilter = options.FileAttributeFilter;
            if ((attributeFilter & (attributes ?? (isFolder ? FileAttributes.Directory : FileAttributes.Normal))) != 0)
                return NestedSourceReach.CutOff;

            // A path the filter does not mention is not excluded, it only falls to the
            // default, so what matters is whether an entry matched and said to exclude it
            if (filter != null && !filter.Empty && filter.Matches(path, out var include, out _) && !include)
                return NestedSourceReach.CutOff;

            var isLink = false;
            try
            {
                isLink = attributes != null && Snapshots.SnapshotUtility.IsSymlink(SystemIO.IO_OS, path, attributes.Value);
            }
            catch
            {
            }

            if (!options.DisableBackupExclusionXattr)
            {
                try
                {
                    var metadata = SystemIO.IO_OS.GetMetadata(path, isLink, options.SymlinkPolicy == Options.SymlinkStrategy.Follow);
                    if (metadata != null && metadata.Keys.Any(Operation.Backup.FileEnumerationProcess.ExcludedBackupAttributes.Contains))
                        return NestedSourceReach.CutOff;
                }
                catch
                {
                }
            }

            if (isLink)
            {
                if (options.SymlinkPolicy == Options.SymlinkStrategy.Ignore)
                    return NestedSourceReach.CutOff;
                if (options.SymlinkPolicy == Options.SymlinkStrategy.Store)
                    return NestedSourceReach.StoredAsLink;
            }

            return NestedSourceReach.Reached;
        }

        /// <inheritdoc />
        public Task PauseAsync(bool alsoTransfers)
        {
            var ct = m_currentTaskControl;
            if (ct != null)
                ct.Pause(alsoTransfers);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task ResumeAsync()
        {
            var ct = m_currentTaskControl;
            if (ct != null)
                ct.Resume();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync()
        {
            ITaskControl ct;
            lock (m_taskControlLock)
            {
                ct = m_currentTaskControl;
                // Before the first operation has started, keep the request for it
                if (ct == null && !m_operationStarted)
                    m_stopBeforeStart = true;
            }
            if (ct == null)
                return Task.CompletedTask;

            Logging.Log.WriteVerboseMessage(LOGTAG, "CancellationRequested", "Cancellation Requested");
            ct.Stop();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task AbortAsync()
        {
            ITaskControl ct;
            lock (m_taskControlLock)
            {
                ct = m_currentTaskControl;
                // Before the first operation has started, keep the request for it
                if (ct == null && !m_operationStarted)
                    m_abortBeforeStart = true;
            }
            ct?.Terminate();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SetThrottleSpeedsAsync(long maxUploadPrSecond, long maxDownloadPrSecond)
        {
            // Set these values in the options, in case the backend is not up yet
            m_options.MaxUploadPrSecond = maxUploadPrSecond;
            m_options.MaxDownloadPrSecond = maxDownloadPrSecond;
            m_currentBackendManager?.UpdateThrottleValues(maxUploadPrSecond, maxDownloadPrSecond);
            return Task.CompletedTask;
        }

        /// <summary>
        /// The time of the last compact operation
        /// </summary>
        private DateTime m_lastCompact;

        /// <summary>
        /// The time of the last vacuum operation
        /// </summary>
        private DateTime m_lastVacuum;

        /// <summary>
        /// The time of the last restore test
        /// </summary>
        private DateTime m_lastRestoreTest;

        /// <inheritdoc />
        public Task SetLastCompactAsync(DateTime lastCompact)
        {
            m_lastCompact = lastCompact;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SetLastVacuumAsync(DateTime lastVacuum)
        {
            m_lastVacuum = lastVacuum;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SetLastRestoreTestAsync(DateTime lastRestoreTest)
        {
            m_lastRestoreTest = lastRestoreTest;
            return Task.CompletedTask;
        }

        private void CheckAutoCompactInterval()
        {
            if (!m_options.NoAutoCompact && (m_lastCompact > DateTime.MinValue) && (m_lastCompact.Add(m_options.AutoCompactInterval) > DateTime.Now))
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "CompactResults", "Skipping auto compaction until {0}", m_lastCompact.Add(m_options.AutoCompactInterval));
                m_options.RawOptions["no-auto-compact"] = "true";
            }
        }

        private void CheckAutoVacuumInterval()
        {
            if (m_options.AutoVacuum && (m_lastVacuum > DateTime.MinValue) && (m_lastVacuum.Add(m_options.AutoVacuumInterval) > DateTime.Now))
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "VacuumResults", "Skipping auto vacuum until {0}", m_lastVacuum.Add(m_options.AutoVacuumInterval));
                m_options.RawOptions["auto-vacuum"] = "false";
            }
        }

        #region IDisposable Members

        /// <inheritdoc />
        public void Dispose()
        {
        }

        #endregion
    }
}
