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
using System.Linq;
using System.Text.RegularExpressions;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;
using RemoteSynchronization;

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
/// Module for synchronizing backup data to remote destinations after a successful backup operation.
/// </summary>
public class RemoteSynchronizationModule : IGenericCallbackModule
{
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<RemoteSynchronizationModule>();

    private const string OPTION_BACKEND_DST = "remote-sync-dst";
    private const string OPTION_FORCE = "remote-sync-force";
    private const string OPTION_RETENTION = "remote-sync-retention";
    private const string OPTION_BACKEND_RETRIES = "remote-sync-backend-retries";
    private const string OPTION_RETRY = "remote-sync-retry";
    private const string OPTION_MODE = "remote-sync-mode";
    private const string OPTION_SCHEDULE = "remote-sync-schedule";
    private const string OPTION_COUNT = "remote-sync-count";
    private const string OPTION_SYNC_ON_WARNINGS = "remote-sync-on-warnings";

    private IReadOnlyDictionary<string, string> m_options = new Dictionary<string, string>();
    private string m_source;
    private List<string> m_destinations = [];
    private string m_operationName;
    private bool m_enabled;
    private bool m_syncOnWarnings = true;
    private List<RemoteSyncTriggerMode> m_modes = [];
    private List<TimeSpan> m_schedules = [];
    private List<int> m_counts = [];

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
        new CommandLineArgument(OPTION_BACKEND_DST, CommandLineArgument.ArgumentType.String, Strings.RemoteSynchronization.BackendDestinationShort, Strings.RemoteSynchronization.BackendDestinationLong),
        new CommandLineArgument(OPTION_FORCE, CommandLineArgument.ArgumentType.Boolean, Strings.RemoteSynchronization.ForceShort, Strings.RemoteSynchronization.ForceLong, "false"),
        new CommandLineArgument(OPTION_RETENTION, CommandLineArgument.ArgumentType.Boolean, Strings.RemoteSynchronization.RetentionShort, Strings.RemoteSynchronization.RetentionLong, "false"),
        new CommandLineArgument(OPTION_BACKEND_RETRIES, CommandLineArgument.ArgumentType.Integer, Strings.RemoteSynchronization.BackendRetriesShort, Strings.RemoteSynchronization.BackendRetriesLong, "3"),
        new CommandLineArgument(OPTION_RETRY, CommandLineArgument.ArgumentType.Integer, Strings.RemoteSynchronization.RetryShort, Strings.RemoteSynchronization.RetryLong, "3"),
        new CommandLineArgument(OPTION_MODE, CommandLineArgument.ArgumentType.Enumeration, Strings.RemoteSynchronization.ModeShort, Strings.RemoteSynchronization.ModeLong, "inline", null, ["inline", "scheduled", "counting"]),
        new CommandLineArgument(OPTION_SCHEDULE, CommandLineArgument.ArgumentType.Timespan, Strings.RemoteSynchronization.ScheduleShort, Strings.RemoteSynchronization.ScheduleLong),
        new CommandLineArgument(OPTION_COUNT, CommandLineArgument.ArgumentType.Integer, Strings.RemoteSynchronization.CountShort, Strings.RemoteSynchronization.CountLong),
        new CommandLineArgument(OPTION_SYNC_ON_WARNINGS, CommandLineArgument.ArgumentType.Boolean, Strings.RemoteSynchronization.SyncOnWarningsShort, Strings.RemoteSynchronization.SyncOnWarningsLong, "true"),
    ];

    /// <summary>
    /// Configures the module with the provided command line options.
    /// </summary>
    /// <param name="commandlineOptions">The command line options dictionary.</param>
    public void Configure(IDictionary<string, string> commandlineOptions)
    {
        m_options = commandlineOptions.AsReadOnly();
        m_destinations =
            commandlineOptions.TryGetValue(OPTION_BACKEND_DST, out var dstStr)
            && !string.IsNullOrWhiteSpace(dstStr)
            ? [.. dstStr
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
            ]
            : [];
        m_enabled = m_destinations.Count != 0;

        if (commandlineOptions.TryGetValue(OPTION_MODE, out var modeStr) && !string.IsNullOrWhiteSpace(modeStr))
        {
            m_modes = [.. modeStr
                .Split(',')
                .Select(s => s.Trim())
                .Select(s => Enum.TryParse<RemoteSyncTriggerMode>(s, true, out var mode)
                ? mode : RemoteSyncTriggerMode.Inline)
            ];
        }
        else
        {
            m_modes = [];
        }
        // Fill rest of the modes with default values, if the list is shorter than destinations
        m_modes.AddRange(Enumerable.Repeat(RemoteSyncTriggerMode.Inline, m_destinations.Count - m_modes.Count));

        if (commandlineOptions.TryGetValue(OPTION_SCHEDULE, out var scheduleStr) && !string.IsNullOrWhiteSpace(scheduleStr))
        {
            m_schedules = [.. scheduleStr
                .Split(',')
                .Select(s => s.Trim())
                .Select(s => TimeSpan.TryParse(s, out var ts)
                ? ts : TimeSpan.Zero)
            ];
        }
        else
        {
            m_schedules = [];
        }
        // Fill rest of the schedules with default values, if the list is shorter than destinations
        m_schedules.AddRange(Enumerable.Repeat(TimeSpan.Zero, m_destinations.Count - m_schedules.Count));

        if (commandlineOptions.TryGetValue(OPTION_COUNT, out var countStr) && !string.IsNullOrWhiteSpace(countStr))
        {
            m_counts = [.. countStr
                .Split(',')
                .Select(s => s.Trim())
                .Select(s => int.TryParse(s, out var c) ? c : 0)
            ];
        }
        else
        {
            m_counts = [];
        }
        // Fill rest of the counts with default values, if the list is shorter than destinations
        m_counts.AddRange(Enumerable.Repeat(0, m_destinations.Count - m_counts.Count));

        m_syncOnWarnings = commandlineOptions.TryGetValue(OPTION_SYNC_ON_WARNINGS, out var syncOnWarningsStr)
            ? bool.TryParse(syncOnWarningsStr, out var syncOnWarnings) ? syncOnWarnings : true
            : true;

        // Validate parameter lengths
        var destCount = m_destinations.Count;
        if (m_modes.Count != 0 && m_modes.Count != destCount)
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncConfigMismatch", null, "Number of modes ({0}) does not match number of destinations ({1}). Using defaults for missing values.", m_modes.Count, destCount);
        if (m_schedules.Count != 0 && m_schedules.Count != destCount)
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncConfigMismatch", null, "Number of schedules ({0}) does not match number of destinations ({1}). Using defaults for missing values.", m_schedules.Count, destCount);
        if (m_counts.Count != 0 && m_counts.Count != destCount)
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncConfigMismatch", null, "Number of counts ({0}) does not match number of destinations ({1}). Using defaults for missing values.", m_counts.Count, destCount);
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

        if (string.IsNullOrWhiteSpace(m_source) || m_destinations.Count == 0)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncMissingBackends", null, "Remote synchronization skipped because source or destinations are missing.");
            return;
        }

        for (int i = 0; i < m_destinations.Count; i++)
        {
            var dest = m_destinations[i];
            if (string.IsNullOrWhiteSpace(dest))
                continue;

            if (!ShouldTriggerSync(i))
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "RemoteSyncSkipped", "Remote synchronization to {0} skipped due to trigger mode conditions not met.", dest);
                continue;
            }

            RecordSyncOperation(i);

            var args = BuildArguments(dest);

            try
            {
                var exitCode = RemoteSynchronizationRunner.RunAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();
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
    private bool ShouldTriggerSync(int index)
    {
        if (!m_options.TryGetValue("dbpath", out var dbpath) || string.IsNullOrWhiteSpace(dbpath))
            return false;

        var mode = index < m_modes.Count ? m_modes[index] : RemoteSyncTriggerMode.Inline;
        var schedule = index < m_schedules.Count ? m_schedules[index] : TimeSpan.Zero;
        var count = index < m_counts.Count ? m_counts[index] : 0;
        var description = $"Rsync {index}";

        switch (mode)
        {
            case RemoteSyncTriggerMode.Inline:
                return true;
            case RemoteSyncTriggerMode.Scheduled:
                {
                    using var db = SQLiteLoader.LoadConnection(dbpath);
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
                    return (now - lastSyncTime) >= schedule;
                }
            case RemoteSyncTriggerMode.Counting:
                {
                    using var db = SQLiteLoader.LoadConnection(dbpath);
                    using var cmd = db.CreateCommand();
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
                    return backupCount >= count;
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

        if (!m_options.TryGetValue("dbpath", out var dbpath) || string.IsNullOrWhiteSpace(dbpath))
            return;

        using var db = SQLiteLoader.LoadConnection(dbpath);
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

    /// <summary>
    /// Builds the arguments for the remote synchronization command.
    /// </summary>
    /// <param name="dest">The destination backend string.</param>
    /// <returns>An array of command line arguments.</returns>
    private string[] BuildArguments(string dest)
    {
        string[] args = [
            m_source,
            dest,
            .. AddOption(OPTION_BACKEND_RETRIES, "--backend-retries", []),
            .. AddOption(OPTION_FORCE, "--force", []),
            .. AddOption(OPTION_RETENTION, "--retention", []),
            .. AddOption(OPTION_RETRY, "--retry", []),
            // Hardcoded defaults for automatic operation
            "--auto-create-folders",
            "--backend-retry-delay", "1000",
            "--backend-retry-with-exponential-backoff",
            "--confirm", // Automatic, no prompt
        ];

        return args;
    }

    /// <summary>
    /// Adds an option to the command line arguments if it is specified.
    /// </summary>
    /// <param name="optionKey">The key of the option in the options dictionary.</param>
    /// <param name="toolOption">The command line flag for the option.</param>
    /// <param name="defaultvalue">The default value if the option is not specified.</param>
    /// <returns>An array of strings representing the option and its value, or the default value.</returns>
    private string[] AddOption(string optionKey, string toolOption, string[] defaultvalue)
    {
        if (!m_options.TryGetValue(optionKey, out var value))
            return defaultvalue;

        if (string.IsNullOrWhiteSpace(value))
            return defaultvalue;

        return [toolOption, value];
    }

}
