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
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Operation.RemoteSynchronization;
using Duplicati.Library.SQLiteHelper;

#nullable enable

namespace Duplicati.Library.Main.Operation;

/// <summary>
/// Synchronizes the backup destination to further destinations after a successful backup.
/// The handler decides which destinations are due and records the ones that were
/// synchronized; the copying itself is done by <see cref="RemoteReplication"/>.
/// </summary>
internal class RemoteSynchronizationHandler : IDisposable
{
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<RemoteSynchronizationHandler>();

    private string? m_dbpath;
    private bool m_enabled = false;
    private List<RemoteSyncDestinationConfig> m_destinations = [];
    private string m_source;
    private bool m_syncOnWarnings = true;
    private BackupResults m_results;
    private readonly Options m_options;

    /// <summary>
    /// Configures the module with the provided command line options.
    /// </summary>
    /// <param name="backendUrl">The URL of the source backend.</param>
    /// <param name="options">The command line options dictionary.</param>
    /// <param name="results">The backup results object, used to determine whether to run the synchronization and to report results.</param>
    public RemoteSynchronizationHandler(string backendUrl, Options options, BackupResults results)
    {
        m_results = results;
        m_source = backendUrl;
        m_options = options;
        m_dbpath = options.Dbpath;

        // Without a valid configuration the module is disabled
        var config = RemoteSyncConfig.Parse(options, backendUrl, problemsAreErrors: true);
        if (config == null)
            return;

        m_syncOnWarnings = config.SyncOnWarnings;
        if (config.Destinations.Count == 0)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncJsonEmptyDestinations", null, "JSON configuration is missing entries in the 'destinations' array.");
            return;
        }

        m_destinations = [.. config.Destinations];
        m_enabled = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to dispose currently, but implement IDisposable in case we need it in the future
    }

    /// <summary>
    /// Runs the remote synchronization operations based on the configured destinations and the results of the backup operation.
    /// The synchronization will only be triggered for destinations whose conditions are met (e.g. interval has passed, backup count reached, etc.).
    /// </summary>
    public async Task RunAsync()
    {
        if (!m_enabled)
            return;

        if (m_results.ParsedResult == ParsedResultType.Error || m_results.ParsedResult == ParsedResultType.Fatal)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncSkipped", null, "Remote synchronization skipped because backup reported errors.");
            return;
        }

        if (m_results.ParsedResult == ParsedResultType.Warning && !m_syncOnWarnings)
        {
            Logging.Log.WriteInformationMessage(LOGTAG, "RemoteSyncSkipped", "Remote synchronization skipped because backup reported warnings and sync on warnings is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(m_source))
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncMissingBackends", null, "Remote synchronization skipped because source is missing.");
            return;
        }

        if (m_destinations.Count == 0)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncNoDestinations", null, "Remote synchronization skipped because no destinations are configured.");
            return;
        }

        var taskControl = m_results.TaskControl;
        if (taskControl.StopToken.IsCancellationRequested || taskControl.ProgressToken.IsCancellationRequested)
        {
            Logging.Log.WriteInformationMessage(LOGTAG, "RemoteSyncSkipped", "Remote synchronization skipped because the backup was stopped.");
            return;
        }

        // Pick the destinations whose trigger fires. Only the credential-free url is ever
        // written to the log; the target itself carries the raw url and its options.
        var triggered = new List<(int Index, RemoteSyncDestinationConfig Destination)>();
        for (int i = 0; i < m_destinations.Count; i++)
        {
            var dest = m_destinations[i];
            if (!dest.IsUsable)
                continue;

            try
            {
                if (ShouldTriggerSync(i, dest))
                    triggered.Add((i, dest));
                else
                    Logging.Log.WriteInformationMessage(LOGTAG, "RemoteSyncSkipped", "Remote synchronization to {0} skipped due to trigger mode conditions not met.", dest.DisplayDestination);
            }
            catch (Exception ex)
            {
                // Error is captured into the result's Errors collection via the active log scope
                Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncFailed", ex, "Remote synchronization to {0} failed: {1}", dest.DisplayDestination, ex.Message);
            }
        }

        if (triggered.Count == 0)
            return;

        // Update progress to show sync is in progress
        var progress = m_results.OperationProgressUpdater;
        progress.UpdatePhase(OperationPhase.Backup_RemoteSynchronization);
        progress.UpdateRemoteSyncDestination(triggered[0].Index + 1, m_destinations.Count);
        progress.UpdateProgress(0);

        // Each destination reports on its own, with its own backend statistics, so the
        // statistics of the backup do not count the uploads of the synchronization. The
        // transfer progress still goes to the backup, which is what the UI shows.
        var syncResults = triggered.Select(x => new RemoteSynchronizationResults { Destination = x.Destination.DisplayDestination }).ToList();
        var writers = syncResults.Select(x => (IBackendWriter)new ReplicationBackendWriter(x.BackendWriter, m_results.BackendProgressUpdater)).ToList();

        // The destinations are written one after the other, so a destination begins
        // when the work moves to it and ends when it moves on.
        var current = -1;
        void OnTargetStarted(int index, int count)
        {
            var now = DateTime.UtcNow;
            if (current >= 0)
                syncResults[current].EndTime = now;
            current = index;
            syncResults[index].BeginTime = now;
            progress.UpdateRemoteSyncDestination(triggered[index].Index + 1, m_destinations.Count);
            progress.UpdateProgress(0);
        }

        IReadOnlyList<RemoteReplicationSummary>? summaries = null;
        // Stop and abort from the task control both end the synchronization
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(taskControl.StopToken, taskControl.ProgressToken);
        try
        {
            summaries = await RemoteReplication.RunAsync(
                m_source,
                m_options.RawOptions,
                m_options.RawOptions,
                [.. triggered.Select(x => x.Destination.Target)],
                m_results.BackendWriter,
                writers,
                taskControl,
                progress,
                OnTargetStarted,
                anyPrefix: false,
                cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncStopped", null, "Remote synchronization was stopped before it completed.");
        }
        catch (Exception ex)
        {
            // Error is captured into the result's Errors collection via the active log scope
            foreach (var (_, dest) in triggered)
                Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncFailed", ex, "Remote synchronization to {0} failed: {1}", dest.DisplayDestination, ex.Message);
        }

        var end = DateTime.UtcNow;
        for (var i = 0; i < triggered.Count; i++)
        {
            var syncResult = syncResults[i];
            if (syncResult.BeginTime.Ticks == 0)
                syncResult.BeginTime = end;
            if (syncResult.EndTime.Ticks == 0)
                syncResult.EndTime = end;

            if (summaries == null)
                continue;

            var summary = summaries[i];
            syncResult.CopiedFileCount = summary.Copied;
            syncResult.CopiedFileSize = summary.CopiedSize;
            syncResult.DeletedFileCount = summary.Deleted;

            // A destination that is not a mirror of the source is not recorded, so an
            // interval or counting destination is tried again after the next backup.
            if (summary.Succeeded)
            {
                try
                {
                    RecordSyncOperation(triggered[i].Index);
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncFailed", ex, "Remote synchronization to {0} could not be recorded: {1}", summary.Destination, ex.Message);
                }
            }
            else if (summary.Failed)
                Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncFailed", null, "Remote synchronization to {0} failed.", summary.Destination);
            else
                Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncFailed", null, "Remote synchronization to {0} is incomplete: {1} files could not be copied and {2} files could not be deleted.", summary.Destination, summary.UploadFailures, summary.DeleteFailures);
        }

        // Add remote synchronization results to the backup results for reporting
        m_results.RemoteSynchronizationResults = [.. syncResults];
    }

    /// <summary>
    /// Sends the backend events of a destination to the statistics of that destination,
    /// and the transfer progress to the operation the synchronization is a step of.
    /// </summary>
    /// <param name="statistics">The writer that counts the events</param>
    /// <param name="progress">The progress updater of the outer operation</param>
    private sealed class ReplicationBackendWriter(IBackendWriter statistics, IBackendProgressUpdater progress) : IBackendWriter
    {
        public bool ReportedQuotaError { get => statistics.ReportedQuotaError; set => statistics.ReportedQuotaError = value; }
        public bool ReportedQuotaWarning { get => statistics.ReportedQuotaWarning; set => statistics.ReportedQuotaWarning = value; }
        public Action<long> UploadCompleted { get => statistics.UploadCompleted; set => statistics.UploadCompleted = value; }
        public OperationMode MainOperation => statistics.MainOperation;
        public IBackendProgressUpdater BackendProgressUpdater => progress;
        public void SendEvent(BackendActionType action, BackendEventType type, string path, long size, bool updateProgress = true)
            => statistics.SendEvent(action, type, path, size, updateProgress);

        public long UnknownFileSize { get => statistics.UnknownFileSize; set => statistics.UnknownFileSize = value; }
        public long UnknownFileCount { get => statistics.UnknownFileCount; set => statistics.UnknownFileCount = value; }
        public long KnownFileCount { get => statistics.KnownFileCount; set => statistics.KnownFileCount = value; }
        public long KnownFileSize { get => statistics.KnownFileSize; set => statistics.KnownFileSize = value; }
        public long KnownFilesets { get => statistics.KnownFilesets; set => statistics.KnownFilesets = value; }
        public DateTime LastBackupDate { get => statistics.LastBackupDate; set => statistics.LastBackupDate = value; }
        public long BackupListCount { get => statistics.BackupListCount; set => statistics.BackupListCount = value; }
        public long TotalQuotaSpace { get => statistics.TotalQuotaSpace; set => statistics.TotalQuotaSpace = value; }
        public long FreeQuotaSpace { get => statistics.FreeQuotaSpace; set => statistics.FreeQuotaSpace = value; }
        public long AssignedQuotaSpace { get => statistics.AssignedQuotaSpace; set => statistics.AssignedQuotaSpace = value; }

        public long RemoteCalls => statistics.RemoteCalls;
        public long BytesUploaded => statistics.BytesUploaded;
        public long BytesDownloaded => statistics.BytesDownloaded;
        public long FilesUploaded => statistics.FilesUploaded;
        public long FilesDownloaded => statistics.FilesDownloaded;
        public long FilesDeleted => statistics.FilesDeleted;
        public long FoldersCreated => statistics.FoldersCreated;
        public long RetryAttempts => statistics.RetryAttempts;
    }

    /// <summary>
    /// Checks if remote synchronization should be triggered for the specified destination based on the configured mode.
    /// </summary>
    /// <param name="index">The index of the destination in the list.</param>
    /// <returns>True if synchronization should be triggered.</returns>
    private bool ShouldTriggerSync(int index, RemoteSyncDestinationConfig dest)
    {
        if (index < 0 || index >= m_destinations.Count)
            return false;

        var description = $"Rsync {index}";

        switch (dest.Mode)
        {
            case RemoteSyncTriggerMode.Inline:
                return true;
            // Without a local database there is no record of an earlier synchronization, as for the first one
            case RemoteSyncTriggerMode.Interval or RemoteSyncTriggerMode.Counting when string.IsNullOrWhiteSpace(m_dbpath):
                return true;
            case RemoteSyncTriggerMode.Interval:
                {
                    using var db = SQLiteLoader.LoadConnection(m_dbpath!);
                    using var cmd = db.CreateCommand();

                    cmd.CommandText = @"
                        SELECT ""Timestamp""
                        FROM ""Operation""
                        WHERE ""Description"" = @description
                        ORDER BY ""Timestamp"" DESC
                        LIMIT 1
                    ";
                    cmd.AddNamedParameter("@description", description);

                    var lastSync = cmd.ExecuteScalar();
                    if (lastSync is null)
                        return true;
                    var lastSyncTime = Duplicati.Library.Utility.Utility.EPOCH.AddSeconds((long)lastSync);
                    var now = DateTime.UtcNow;
                    return (now - lastSyncTime) >= dest.Interval;
                }
            case RemoteSyncTriggerMode.Counting:
                {
                    using var db = SQLiteLoader.LoadConnection(m_dbpath!);
                    using var cmd = db.CreateCommand();
                    cmd.CommandText = @"
                        SELECT COUNT(*)
                        FROM ""Operation""
                        WHERE ""Description"" = @description
                    ";
                    cmd.AddNamedParameter("@description", description);
                    var syncCount = (long)(cmd.ExecuteScalar() ?? 0L);
                    if (syncCount == 0)
                        return true;
                    cmd.Parameters.Clear();

                    cmd.CommandText = @"
                        SELECT COUNT(*)
                        FROM ""Operation""
                        WHERE ""Description"" = 'Backup'
                        AND ""Timestamp"" > COALESCE(
                            (
                                SELECT ""Timestamp""
                                FROM ""Operation""
                                WHERE ""Description"" = @description
                                ORDER BY ""Timestamp"" DESC LIMIT 1
                            ),
                            0
                        )";
                    cmd.AddNamedParameter("@description", description);

                    var backupCount = (long)(cmd.ExecuteScalar() ?? 0L);
                    return backupCount >= dest.Count;
                }
            default:
                return false;
        }
    }

    /// <summary>
    /// Records a remote synchronization operation in the database.
    /// </summary>
    /// <param name="index">The index of the destination.</param>
    private void RecordSyncOperation(int index)
    {
        // Validate index
        if (index < 0 || index >= m_destinations.Count)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncRecordInvalidIndex", null, "Cannot record remote synchronization operation: invalid index {0}.", index);
            return;
        }

        if (string.IsNullOrWhiteSpace(m_dbpath))
            return;

        using var db = SQLiteLoader.LoadConnection(m_dbpath!);
        using var transaction = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ""Operation"" (
                ""Description"", ""Timestamp""
            )
            VALUES (
                @description,
                @timestamp
            )";
        cmd.SetTransaction(transaction);
        cmd.AddNamedParameter("@description", $"Rsync {index}");
        cmd.AddNamedParameter("@timestamp", Duplicati.Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow));
        cmd.ExecuteNonQuery();
        transaction.Commit();
    }

}
