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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Operation.RemoteSynchronization;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;

#nullable enable

namespace Duplicati.Library.Main.Operation;

/// <summary>
/// Trigger modes for remote synchronization.
/// </summary>
public enum RemoteSyncTriggerMode
{
    /// <summary>
    /// Trigger after every successful backup.
    /// </summary>
    Inline,
    /// <summary>
    /// Trigger based on a time interval.
    /// </summary>
    Interval,
    /// <summary>
    /// Trigger after a certain number of backups.
    /// </summary>
    Counting
}

/// <summary>
/// Configuration for a single remote synchronization destination.
/// </summary>
public record RemoteSyncDestinationConfig(
    RemoteSynchronizationConfig Config,
    RemoteSyncTriggerMode Mode = RemoteSyncTriggerMode.Inline,
    TimeSpan? Interval = null,
    int? Count = null
);

/// <summary>
/// Raw configuration for a remote synchronization destination, as deserialized from JSON.
/// </summary>
public record RemoteSyncDestinationConfigRaw(
    bool AutoCreateFolders = true,
    int BackendRetries = 3,
    int BackendRetryDelay = 1000,
    bool BackendRetryWithExponentialBackoff = true,
    bool DryRun = false,
    bool Force = false,
    string LogFile = "",
    string LogLevel = "",
    bool ParseArgumentsOnly = false,
    bool Progress = false,
    bool Retention = false,
    int Retry = 3,
    bool VerifyContents = false,
    bool VerifyGetAfterPut = false
)
{
    public string? Url { get; init; }

    public List<string> DstOptions { get; init; } = [];
    public List<string> GlobalOptions { get; init; } = [];

    public string? Mode { get; init; }
    public string? Interval { get; init; }
    public int? Count { get; init; }
};

/// <summary>
/// Top-level configuration for remote synchronization, containing the destinations array and global settings.
/// </summary>
public record TopLevelRemoteSyncConfig(
    bool SyncOnWarnings = true
)
{
    public required List<RemoteSyncDestinationConfigRaw> Destinations { get; init; }
}

/// <summary>
/// Module for synchronizing backup data to remote destinations after a successful backup operation.
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

        // Default is no valid JSON config provided, which disables the module
        m_enabled = false;
        m_destinations = [];
        m_dbpath = options.Dbpath;

        // Check if JSON config is provided
        if (!string.IsNullOrWhiteSpace(options.RemoteSyncJsonConfig))
        {
            string jsonContent;
            if (options.RemoteSyncJsonConfig.TrimStart().StartsWith("{"))
            {
                // It's a JSON string
                jsonContent = options.RemoteSyncJsonConfig;
            }
            else
            {
                // It's a file path
                try
                {
                    jsonContent = File.ReadAllText(options.RemoteSyncJsonConfig);
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncJsonFileReadError", ex, "Failed to read JSON configuration file '{0}': {1}", options.RemoteSyncJsonConfig, ex.Message);
                    return;
                }
            }

            try
            {
                var deserializeOpts = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower
                };
                var toplevel = JsonSerializer.Deserialize<TopLevelRemoteSyncConfig>(jsonContent, deserializeOpts);

                if (toplevel is null)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncJsonParseError", null, "Failed to parse JSON configuration: top-level object is null.");
                    return;
                }

                m_syncOnWarnings = toplevel.SyncOnWarnings;

                if (toplevel.Destinations.Count > 0)
                {
                    foreach (var destination in toplevel.Destinations)
                    {
                        var loglevel = options.LogFileLoglevel;
                        var mode = !string.IsNullOrWhiteSpace(destination.Mode) && Enum.TryParse<RemoteSyncTriggerMode>(destination.Mode, true, out var parsedMode) ? parsedMode : RemoteSyncTriggerMode.Inline;

                        TimeSpan? interval_parsed;
                        try
                        {
                            interval_parsed = string.IsNullOrWhiteSpace(destination.Interval) ? null : Duplicati.Library.Utility.Timeparser.ParseTimeSpan(destination.Interval);
                        }
                        catch (Exception ex)
                        {
                            var defaulting_string = string.Empty;
                            if (mode == RemoteSyncTriggerMode.Interval)
                            {
                                mode = RemoteSyncTriggerMode.Inline;
                                defaulting_string = "; defaulting to inline mode";
                            }
                            interval_parsed = null;
                            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncInvalidInterval", ex, "Invalid interval format '{0}' for remote synchronization destination{1}", destination.Interval, defaulting_string);
                        }

                        if (string.IsNullOrWhiteSpace(destination.Mode))
                        {
                            if (interval_parsed.HasValue && destination.Count.HasValue)
                            {
                                Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncBothIntervalAndCount", null, "Both interval and count specified for remote synchronization destination without explicit mode; defaulting to interval mode.");
                                mode = RemoteSyncTriggerMode.Interval;
                            }
                            else if (interval_parsed.HasValue)
                            {
                                mode = RemoteSyncTriggerMode.Interval;
                            }
                            else if (destination.Count.HasValue)
                            {
                                mode = RemoteSyncTriggerMode.Counting;
                            }
                        }

                        m_destinations.Add(new(
                            Config: new(
                                Src: "",
                                Dst: destination.Url ?? "",

                                AutoCreateFolders: destination.AutoCreateFolders,
                                BackendRetries: destination.BackendRetries,
                                BackendRetryDelay: destination.BackendRetryDelay,
                                BackendRetryWithExponentialBackoff: destination.BackendRetryWithExponentialBackoff,
                                Confirm: true,
                                DryRun: destination.DryRun,
                                DstOptions: destination.DstOptions,
                                Force: destination.Force,
                                GlobalOptions: destination.GlobalOptions,
                                LogFile: destination.LogFile,
                                LogLevel: loglevel.ToString(),
                                ParseArgumentsOnly: destination.ParseArgumentsOnly,
                                Progress: destination.Progress,
                                Retention: destination.Retention,
                                Retry: destination.Retry,
                                SrcOptions: [.. options.RawOptions.Select((k, v) => $"{k}={v}")],
                                VerifyContents: destination.VerifyContents,
                                VerifyGetAfterPut: destination.VerifyGetAfterPut
                            ),
                            Mode: mode,
                            Interval: interval_parsed,
                            Count: destination.Count
                        ));
                    }

                    m_enabled = true;
                }
                else
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncJsonEmptyDestinations", null, "JSON configuration is missing entries in the 'destinations' array.");
                    m_enabled = false;
                    return;
                }
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncJsonParseError", ex, "Failed to parse JSON configuration: {0}", ex.Message);
            }
        }

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

        // Store remote sync results to be added to backup results
        var syncResultsList = new List<IRemoteSynchronizationResults>();

        for (int i = 0; i < m_destinations.Count; i++)
        {
            var dest = m_destinations[i];
            if (string.IsNullOrWhiteSpace(dest.Config.Dst))
                continue;

            try
            {
                if (!ShouldTriggerSync(i, dest))
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "RemoteSyncSkipped", "Remote synchronization to {0} skipped due to trigger mode conditions not met.", dest);
                    continue;
                }

                // Update progress to show sync is in progress
                m_results.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_RemoteSynchronization);
                m_results.OperationProgressUpdater.UpdateRemoteSyncDestination(i + 1, m_destinations.Count);
                m_results.OperationProgressUpdater.UpdateProgress(0);

                var config = dest.Config with { Src = m_source! };
                var date_start = DateTime.UtcNow;
                var syncResult = new RemoteSynchronizationResults
                {
                    Destination = Duplicati.Library.Utility.Utility.GetUrlWithoutCredentials(config.Dst),
                };
                var exitCode = RemoteSynchronizationRunner.Run(config, CancellationToken.None, m_results.OperationProgressUpdater, m_results.BackendProgressUpdater, syncResult).ConfigureAwait(false).GetAwaiter().GetResult();
                var date_end = DateTime.UtcNow;
                syncResult.BeginTime = date_start;
                syncResult.EndTime = date_end;
                syncResultsList.Add(syncResult);

                if (exitCode == 0)
                    RecordSyncOperation(i);
                else
                {
                    // Error is captured into the result's Errors collection via the active log scope
                    Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncFailed", null, "Remote synchronization to {0} failed with exit code {1}.", dest, exitCode);
                }
            }
            catch (Exception ex)
            {
                // Error is captured into the result's Errors collection via the active log scope
                Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncFailed", ex, "Remote synchronization to {0} failed: {1}", dest, ex.Message);
            }
        }

        // Add remote synchronization results to the backup results for reporting
        if (syncResultsList.Count > 0)
            m_results.RemoteSynchronizationResults = [.. syncResultsList];
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
