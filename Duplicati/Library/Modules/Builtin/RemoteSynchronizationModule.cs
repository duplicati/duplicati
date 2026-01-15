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
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;
using RemoteSynchronization;

#nullable enable

namespace Duplicati.Library.Modules.Builtin;

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
    /// Trigger based on a schedule.
    /// </summary>
    Scheduled,
    /// <summary>
    /// Trigger after a certain number of backups.
    /// </summary>
    Counting
}

/// <summary>
/// Configuration for a single remote synchronization destination.
/// </summary>
public record RemoteSyncDestinationConfig(
    RemoteSynchronization.Config Config,
    RemoteSyncTriggerMode Mode = RemoteSyncTriggerMode.Inline,
    TimeSpan? Schedule = null,
    int Count = 0
);

/// <summary>
/// Module for synchronizing backup data to remote destinations after a successful backup operation.
/// </summary>
public class RemoteSynchronizationModule : IGenericCallbackModule
{
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<RemoteSynchronizationModule>();

    private const string OPTION_JSON_CONFIG = "remote-sync-json-config";

    private string? m_dbpath;
    private List<RemoteSyncDestinationConfig> m_destinations = [];
    private bool m_enabled;
    private string? m_operationName;
    private string? m_source;
    private bool m_syncOnWarnings = true;

    // Default configuration for the remote synchronization runner
    private RemoteSynchronization.Config m_defaultRunnerConfig = new(
        Src: string.Empty,
        Dst: string.Empty,

        AutoCreateFolders: true,
        BackendRetries: 3,
        BackendRetryDelay: 1000,
        BackendRetryWithExponentialBackoff: true,
        Confirm: true,
        DryRun: false,
        DstOptions: [],
        Force: false,
        GlobalOptions: [],
        LogFile: string.Empty,
        LogLevel: "Information",
        ParseArgumentsOnly: false,
        Progress: false,
        Retention: false,
        Retry: 3,
        SrcOptions: [],
        VerifyContents: false,
        VerifyGetAfterPut: false
    );

    /// <summary>
    /// Gets the key identifier for this module.
    /// </summary>
    public string Key => "remotesync";
    /// <summary>
    /// Gets the display name for this module.
    /// </summary>
    public string DisplayName => Strings.RemoteSynchronization.DisplayName;
    /// <summary>
    /// Gets the description of this module.
    /// </summary>
    public string Description => Strings.RemoteSynchronization.Description;
    /// <summary>
    /// Gets whether this module should be loaded by default.
    /// </summary>
    public bool LoadAsDefault => true;

    /// <summary>
    /// Gets the list of supported command line arguments.
    /// </summary>
    public IList<ICommandLineArgument> SupportedCommands =>
    [
        new CommandLineArgument(OPTION_JSON_CONFIG, CommandLineArgument.ArgumentType.String, "JSON configuration for remote synchronization", "JSON string or file path containing remote synchronization configuration"),
    ];

    /// <summary>
    /// Configures the module with the provided command line options.
    /// </summary>
    /// <param name="commandlineOptions">The command line options dictionary.</param>
    public void Configure(IDictionary<string, string> commandlineOptions)
    {
        // Default is no valid JSON config provided, which disables the module
        m_enabled = false;
        m_destinations = [];

        if (commandlineOptions.TryGetValue("dbpath", out var dbpath))
            m_dbpath = dbpath;

        // Check if JSON config is provided
        if (commandlineOptions.TryGetValue(OPTION_JSON_CONFIG, out var jsonConfigStr) && !string.IsNullOrWhiteSpace(jsonConfigStr))
        {
            string jsonContent;
            if (jsonConfigStr.TrimStart().StartsWith("{"))
            {
                // It's a JSON string
                jsonContent = jsonConfigStr;
            }
            else
            {
                // It's a file path
                try
                {
                    jsonContent = File.ReadAllText(jsonConfigStr);
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncJsonFileReadError", ex, "Failed to read JSON configuration file '{0}': {1}", jsonConfigStr, ex.Message);
                    return;
                }
            }

            try
            {
                var deserializeOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var toplevel = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonContent, deserializeOpts);

                if (toplevel?.TryGetValue("sync-on-warnings", out var syncOnWarningsObj) == true)
                {
                    if (syncOnWarningsObj is JsonElement elem && elem.ValueKind == JsonValueKind.True)
                        m_syncOnWarnings = true;
                    else if (syncOnWarningsObj is JsonElement elem2 && elem2.ValueKind == JsonValueKind.False)
                        m_syncOnWarnings = false;
                    else if (syncOnWarningsObj is bool b)
                        m_syncOnWarnings = b;
                }

                if (toplevel?.TryGetValue("destinations", out var destinationsObj) == true &&
                    destinationsObj is JsonElement destinationsElem &&
                    destinationsElem.ValueKind == JsonValueKind.Array)
                {
                    var destinations = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(destinationsElem.GetRawText(), deserializeOpts) ?? [];
                    foreach (var destination in destinations)
                    {
                        m_destinations.Add(new(
                            Config: m_defaultRunnerConfig with
                            {
                                Dst = (destination.GetValueOrDefault("url") as string) ?? (destination.GetValueOrDefault("url") is JsonElement elem && elem.ValueKind == JsonValueKind.String ? elem.GetString() : null) ?? string.Empty,

                                AutoCreateFolders = destination.TryGetValue("auto-create-folders", out var autoCreateFoldersObj) && autoCreateFoldersObj is bool autoCreateFolders ? autoCreateFolders : m_defaultRunnerConfig.AutoCreateFolders,
                                BackendRetries = destination.TryGetValue("backend-retries", out var backendRetriesObj) && backendRetriesObj is long backendRetriesLong ? (int)backendRetriesLong : m_defaultRunnerConfig.BackendRetries,
                                BackendRetryDelay = destination.TryGetValue("backend-retry-delay", out var backendRetryDelayObj) && backendRetryDelayObj is long backendRetryDelayLong ? (int)backendRetryDelayLong : m_defaultRunnerConfig.BackendRetryDelay,
                                BackendRetryWithExponentialBackoff = destination.TryGetValue("backend-retry-with-exponential-backoff", out var backendRetryWithExponentialBackoffObj) && backendRetryWithExponentialBackoffObj is bool backendRetryWithExponentialBackoff ? backendRetryWithExponentialBackoff : m_defaultRunnerConfig.BackendRetryWithExponentialBackoff,
                                Confirm = destination.TryGetValue("confirm", out var confirmObj) && confirmObj is bool confirm ? confirm : m_defaultRunnerConfig.Confirm,
                                DryRun = destination.TryGetValue("dry-run", out var dryRunObj) && dryRunObj is bool dryRun ? dryRun : m_defaultRunnerConfig.DryRun,
                                DstOptions = destination.TryGetValue("dst-options", out var dstOptionsObj) && ((dstOptionsObj is string dstOptionsStr) || (dstOptionsObj is JsonElement elemDstOpt && elemDstOpt.ValueKind == JsonValueKind.String && (dstOptionsStr = elemDstOpt.GetString()) != null)) ? dstOptionsStr.Split(' ').ToList() : m_defaultRunnerConfig.DstOptions,
                                Force = destination.TryGetValue("force", out var forceObj) && forceObj is bool force ? force : m_defaultRunnerConfig.Force,
                                GlobalOptions = destination.TryGetValue("global-options", out var globalOptionsObj) && ((globalOptionsObj is string globalOptionsStr) || (globalOptionsObj is JsonElement elemGlob && elemGlob.ValueKind == JsonValueKind.String && (globalOptionsStr = elemGlob.GetString()) != null)) ? globalOptionsStr.Split(' ').ToList() : m_defaultRunnerConfig.GlobalOptions,
                                LogFile = (destination.GetValueOrDefault("log-file") as string) ?? (destination.GetValueOrDefault("log-file") is JsonElement elemLog && elemLog.ValueKind == JsonValueKind.String ? elemLog.GetString() : null) ?? string.Empty,
                                LogLevel = (destination.GetValueOrDefault("log-level") as string) ?? (destination.GetValueOrDefault("log-level") is JsonElement elemLvl && elemLvl.ValueKind == JsonValueKind.String ? elemLvl.GetString() : null) ?? (commandlineOptions.TryGetValue("log-file-log-level", out var logLevel) ? logLevel : m_defaultRunnerConfig.LogLevel),
                                ParseArgumentsOnly = destination.TryGetValue("parse-arguments-only", out var parseArgumentsOnlyObj) && parseArgumentsOnlyObj is bool parseArgumentsOnly ? parseArgumentsOnly : m_defaultRunnerConfig.ParseArgumentsOnly,
                                Progress = destination.TryGetValue("progress", out var progressObj) && progressObj is bool progress ? progress : m_defaultRunnerConfig.Progress,
                                Retention = destination.TryGetValue("retention", out var retentionObj) && retentionObj is bool retention ? retention : m_defaultRunnerConfig.Retention,
                                Retry = destination.TryGetValue("retry", out var retryObj) && retryObj is long retryLong ? (int)retryLong : m_defaultRunnerConfig.Retry,
                                SrcOptions = destination.TryGetValue("src-options", out var srcOptionsObj) && ((srcOptionsObj is string srcOptionsStr) || (srcOptionsObj is JsonElement elemSrc && elemSrc.ValueKind == JsonValueKind.String && (srcOptionsStr = elemSrc.GetString()) != null)) ? srcOptionsStr.Split(' ').ToList() : m_defaultRunnerConfig.SrcOptions,
                                VerifyContents = destination.TryGetValue("verify-contents", out var verifyContentsObj) && verifyContentsObj is bool verifyContents ? verifyContents : m_defaultRunnerConfig.VerifyContents,
                                VerifyGetAfterPut = destination.TryGetValue("verify-get-after-put", out var verifyGetAfterPutObj) && verifyGetAfterPutObj is bool verifyGetAfterPut ? verifyGetAfterPut : m_defaultRunnerConfig.VerifyGetAfterPut
                            },
                            Mode: Enum.TryParse<RemoteSyncTriggerMode>((destination.GetValueOrDefault("mode") as string) ?? (destination.GetValueOrDefault("mode") is JsonElement elemMode && elemMode.ValueKind == JsonValueKind.String ? elemMode.GetString() : null), true, out var mode) ? mode : RemoteSyncTriggerMode.Inline,
                            Schedule: destination.TryGetValue("schedule", out var scheduleObj) && ((scheduleObj is string scheduleStr) || (scheduleObj is JsonElement elemSch && elemSch.ValueKind == JsonValueKind.String && (scheduleStr = elemSch.GetString()) != null)) && TimeSpan.TryParse(scheduleStr, out var schedule) ? schedule : null,
                            Count: destination.TryGetValue("count", out var countObj) && ((countObj is long countLong) || (countObj is JsonElement elemCnt && elemCnt.ValueKind == JsonValueKind.Number && elemCnt.TryGetInt64(out countLong))) ? (int)countLong : 0
                        ));
                    }

                    m_enabled = true;
                }
                else
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncJsonMissingDestinations", null, "JSON configuration is missing 'destinations' array.");
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

    /// <summary>
    /// Called when an operation starts.
    /// </summary>
    /// <param name="operationname">The name of the operation.</param>
    /// <param name="remoteurl">The remote URL.</param>
    /// <param name="localpath">The local paths.</param>
    public void OnStart(string operationname, ref string remoteurl, ref string[] localpath)
    {
        if (!m_enabled)
            return;

        m_operationName = operationname;

        if (string.IsNullOrWhiteSpace(m_source))
            m_source = remoteurl;
    }

    /// <summary>
    /// Called when an operation finishes.
    /// </summary>
    /// <param name="result">The results of the operation.</param>
    /// <param name="exception">Any exception that occurred during the operation.</param>
    public void OnFinish(IBasicResults result, Exception exception)
    {
        if (!m_enabled)
            return;

        if (!string.Equals(m_operationName, "Backup", StringComparison.OrdinalIgnoreCase))
            return;

        if (exception != null)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncSkipped", exception, "Remote synchronization skipped due to operation failure.");
            return;
        }

        if (result != null && (result.ParsedResult == ParsedResultType.Error || result.ParsedResult == ParsedResultType.Fatal))
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncSkipped", null, "Remote synchronization skipped because backup reported errors.");
            return;
        }

        if (result != null && result.ParsedResult == ParsedResultType.Warning && !m_syncOnWarnings)
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

        for (int i = 0; i < m_destinations.Count; i++)
        {
            var dest = m_destinations[i];
            if (string.IsNullOrWhiteSpace(dest.Config.Dst))
                continue;

            if (!ShouldTriggerSync(i, dest))
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "RemoteSyncSkipped", "Remote synchronization to {0} skipped due to trigger mode conditions not met.", dest);
                continue;
            }

            RecordSyncOperation(i);

            try
            {
                var config = dest.Config with { Src = m_source! };
                var exitCode = RemoteSynchronizationRunner.Run(config, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
                if (exitCode != 0)
                    Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncFailed", null, "Remote synchronization to {0} failed with exit code {1}.", dest, exitCode);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncFailed", ex, "Remote synchronization to {0} failed: {1}", dest, ex.Message);
            }
        }
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
            case RemoteSyncTriggerMode.Scheduled:
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
                    var lastSyncTime = Utility.Utility.EPOCH.AddSeconds((long)lastSync);
                    var now = DateTime.UtcNow;
                    return (now - lastSyncTime) >= dest.Schedule;
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
        cmd.AddNamedParameter("@timestamp", Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow));
        cmd.ExecuteNonQuery();
        transaction.Commit();
    }

}
