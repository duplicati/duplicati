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
    /// Gets a boolean value from the dictionary for the specified key.
    /// </summary>
    /// <param name="dict">The dictionary to search.</param>
    /// <param name="key">The key to look for in the dictionary.</param>
    /// <param name="defaultValue">The default value to return if the key is not found or the value is not a boolean.</param>
    /// <returns>The boolean value associated with the key, or the default value if not found or invalid.</returns>
    private static bool GetBoolFromDictionary(Dictionary<string, object?> dict, string key, bool defaultValue = false)
    {
        if (dict.TryGetValue(key, out var val))
        {
            if (val is bool b)
                return b;
            else if (val is JsonElement elem && (elem.ValueKind == JsonValueKind.True || elem.ValueKind == JsonValueKind.False))
                return elem.GetBoolean();
        }
        return defaultValue;
    }

    /// <summary>
    /// Gets an integer value from the dictionary for the specified key.
    /// </summary>
    /// <param name="dict">The dictionary to search.</param>
    /// <param name="key">The key to look for in the dictionary.</param>
    /// <param name="defaultValue">The default value to return if the key is not found or the value is not an integer.</param>
    /// <returns>The integer value associated with the key, or the default value if not found or invalid.</returns>
    private static int GetIntFromDictionary(Dictionary<string, object?> dict, string key, int defaultValue = 0)
    {
        if (dict.TryGetValue(key, out var val))
        {
            if (val is int l)
                return l;
            else if (val is JsonElement elem && elem.ValueKind == JsonValueKind.Number && elem.TryGetInt32(out var parsedInt))
                return parsedInt;
        }
        return defaultValue;
    }

    /// <summary>
    /// Gets a list of strings from the dictionary for the specified key.
    /// </summary>
    /// <param name="dict">The dictionary to search.</param>
    /// <param name="key">The key to look for in the dictionary.</param>
    /// <param name="defaultValue">The default list to return if the key is not found or the value is not a string.</param>
    /// <returns>The list of strings associated with the key, or the default list if not found or invalid.</returns>
    private static List<string> GetOptionsFromDictionary(Dictionary<string, object?> dict, string key, List<string> defaultValue)
    {
        var options = GetStringFromDictionary(dict, key)
            .Split(' ')
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (options.Count != 0)
            return [.. options];

        return defaultValue;
    }

    /// <summary>
    /// Gets a string value from the dictionary for the specified key.
    /// </summary>
    /// <param name="dict">The dictionary to search.</param>
    /// <param name="key">The key to look for in the dictionary.</param>
    /// <param name="defaultValue">The default value to return if the key is not found or the value is not a string.</param>
    /// <returns>The string value associated with the key, or the default value if not found or invalid.</returns>
    private static string GetStringFromDictionary(Dictionary<string, object?> dict, string key, string defaultValue = "")
    {
        if (dict.TryGetValue(key, out var val))
        {
            if (val is string str)
                return str;
            else if (val is JsonElement elem && elem.ValueKind == JsonValueKind.String)
                return elem.GetString() ?? defaultValue;
        }
        return defaultValue;
    }

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

                if (toplevel == null)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncJsonParseError", null, "Failed to parse JSON configuration: top-level object is null.");
                    return;
                }
                m_syncOnWarnings = GetBoolFromDictionary(toplevel, "sync-on-warnings", m_syncOnWarnings);

                if (toplevel?.TryGetValue("destinations", out var destinationsObj) == true &&
                    destinationsObj is JsonElement destinationsElem &&
                    destinationsElem.ValueKind == JsonValueKind.Array)
                {
                    var destinations = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(destinationsElem.GetRawText(), deserializeOpts) ?? [];
                    foreach (var destination in destinations)
                    {
                        var loglevel = GetStringFromDictionary(destination, "log-level");
                        var mode = GetStringFromDictionary(destination, "mode");
                        var schedule = GetStringFromDictionary(destination, "schedule");

                        m_destinations.Add(new(
                            Config: m_defaultRunnerConfig with
                            {
                                Dst = GetStringFromDictionary(destination, "url", string.Empty),

                                AutoCreateFolders = GetBoolFromDictionary(destination, "auto-create-folders", m_defaultRunnerConfig.AutoCreateFolders),
                                BackendRetries = GetIntFromDictionary(destination, "backend-retries", m_defaultRunnerConfig.BackendRetries),
                                BackendRetryDelay = GetIntFromDictionary(destination, "backend-retry-delay", m_defaultRunnerConfig.BackendRetryDelay),
                                BackendRetryWithExponentialBackoff = GetBoolFromDictionary(destination, "backend-retry-with-exponential-backoff", m_defaultRunnerConfig.BackendRetryWithExponentialBackoff),
                                Confirm = GetBoolFromDictionary(destination, "confirm", m_defaultRunnerConfig.Confirm),
                                DryRun = GetBoolFromDictionary(destination, "dry-run", m_defaultRunnerConfig.DryRun),
                                DstOptions = GetOptionsFromDictionary(destination, "dst-options", m_defaultRunnerConfig.DstOptions),
                                Force = GetBoolFromDictionary(destination, "force", m_defaultRunnerConfig.Force),
                                GlobalOptions = GetOptionsFromDictionary(destination, "global-options", m_defaultRunnerConfig.GlobalOptions),
                                LogFile = GetStringFromDictionary(destination, "log-file"),
                                LogLevel = loglevel ?? (commandlineOptions.TryGetValue("log-file-log-level", out var logLevel) ? logLevel : m_defaultRunnerConfig.LogLevel),
                                ParseArgumentsOnly = GetBoolFromDictionary(destination, "parse-arguments-only", m_defaultRunnerConfig.ParseArgumentsOnly),
                                Progress = GetBoolFromDictionary(destination, "progress", m_defaultRunnerConfig.Progress),
                                Retention = GetBoolFromDictionary(destination, "retention", m_defaultRunnerConfig.Retention),
                                Retry = GetIntFromDictionary(destination, "retry", m_defaultRunnerConfig.Retry),
                                SrcOptions = GetOptionsFromDictionary(destination, "src-options", m_defaultRunnerConfig.SrcOptions),
                                VerifyContents = GetBoolFromDictionary(destination, "verify-contents", m_defaultRunnerConfig.VerifyContents),
                                VerifyGetAfterPut = GetBoolFromDictionary(destination, "verify-get-after-put", m_defaultRunnerConfig.VerifyGetAfterPut)
                            },
                            Mode: Enum.TryParse<RemoteSyncTriggerMode>(mode, true, out var parsedMode) ? parsedMode : RemoteSyncTriggerMode.Inline,
                            Schedule: string.IsNullOrWhiteSpace(schedule) ? (TimeSpan?)null : TimeSpan.TryParse(schedule, out var scheduleTimeSpan) ? scheduleTimeSpan : (TimeSpan?)null,
                            Count: GetIntFromDictionary(destination, "count", 0)
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
