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
/// Module for synchronizing backup data to a remote destination after a successful backup operation.
/// </summary>
public class RemoteSynchronizationModule : IGenericCallbackModule
{
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<RemoteSynchronizationModule>();

    private static readonly Regex ARGREGEX = new Regex(
        @"(?<arg>(?<=\s|^)(""(?<value>[^""\\]*(?:\\.[^""\\]*)*)""|'(?<value>[^'\\]*(?:\\.[^'\\]*)*)'|(?<value>[^\s]+))\s?)",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture
    );

    private const string OPTION_BACKEND_DST = "remote-sync-dst";
    private const string OPTION_FORCE = "remote-sync-force";
    private const string OPTION_RETENTION = "remote-sync-retention";
    private const string OPTION_BACKEND_RETRIES = "remote-sync-backend-retries";
    private const string OPTION_RETRY = "remote-sync-retry";
    private const string OPTION_MODE = "remote-sync-mode";
    private const string OPTION_SCHEDULE = "remote-sync-schedule";
    private const string OPTION_COUNT = "remote-sync-count";

    private IReadOnlyDictionary<string, string> m_options = new Dictionary<string, string>();
    private string m_source;
    private string m_destination;
    private string m_operationName;
    private bool m_enabled;
    private RemoteSyncTriggerMode m_mode = RemoteSyncTriggerMode.Inline;
    private TimeSpan m_schedule = TimeSpan.Zero;
    private int m_count = 0;

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
    ];

    /// <summary>
    /// Configures the module with the provided command line options.
    /// </summary>
    /// <param name="commandlineOptions">The command line options dictionary.</param>
    public void Configure(IDictionary<string, string> commandlineOptions)
    {
        m_options = commandlineOptions.AsReadOnly();
        commandlineOptions.TryGetValue(OPTION_BACKEND_DST, out m_destination);
        m_enabled = !string.IsNullOrWhiteSpace(m_destination);

        if (commandlineOptions.TryGetValue(OPTION_MODE, out var modeStr) && !string.IsNullOrWhiteSpace(modeStr))
        {
            if (Enum.TryParse<RemoteSyncTriggerMode>(modeStr, true, out var mode))
                m_mode = mode;
        }

        if (commandlineOptions.TryGetValue(OPTION_SCHEDULE, out var scheduleStr) && !string.IsNullOrWhiteSpace(scheduleStr))
        {
            if (TimeSpan.TryParse(scheduleStr, out var schedule))
                m_schedule = schedule;
        }

        if (commandlineOptions.TryGetValue(OPTION_COUNT, out var countStr) && int.TryParse(countStr, out var count))
            m_count = count;
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

        if (string.IsNullOrWhiteSpace(m_source) || string.IsNullOrWhiteSpace(m_destination))
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncMissingBackends", null, "Remote synchronization skipped because source or destination is missing.");
            return;
        }

        if (!ShouldTriggerSync())
        {
            Logging.Log.WriteInformationMessage(LOGTAG, "RemoteSyncSkipped", "Remote synchronization skipped due to trigger mode conditions not met.");
            return;
        }

        RecordSyncOperation();

        var args = BuildArguments();
        try
        {
            var exitCode = RemoteSynchronizationRunner.RunAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();
            if (exitCode != 0)
                Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncFailed", null, "Remote synchronization failed with exit code {0}.", exitCode);
        }
        catch (Exception ex)
        {
            Logging.Log.WriteErrorMessage(LOGTAG, "RemoteSyncFailed", ex, "Remote synchronization failed: {0}", ex.Message);
        }
    }

    /// <summary>
    /// Checks if remote synchronization should be triggered based on the configured mode.
    /// </summary>
    /// <returns>True if synchronization should be triggered.</returns>
    private bool ShouldTriggerSync()
    {
        if (!m_options.TryGetValue("dbpath", out var dbpath) || string.IsNullOrWhiteSpace(dbpath))
            return false;

        using var db = SQLiteLoader.LoadConnection(dbpath);
        using var cmd = db.CreateCommand();

        switch (m_mode)
        {
            case RemoteSyncTriggerMode.Inline:
                return true;
            case RemoteSyncTriggerMode.Scheduled:
                // Find last remote sync operation
                cmd.CommandText = "SELECT Timestamp FROM Operation WHERE Description = 'Remote Synchronization' ORDER BY Timestamp DESC LIMIT 1";
                var lastSync = cmd.ExecuteScalar();
                if (lastSync == null)
                    return true; // No previous sync, trigger
                var lastSyncTime = DateTime.FromFileTimeUtc((long)lastSync);
                var now = DateTime.UtcNow;
                return (now - lastSyncTime) >= m_schedule;
            case RemoteSyncTriggerMode.Counting:
                // Count backup operations since last sync
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM Operation
                    WHERE Description = 'Backup'
                    AND Timestamp > COALESCE(
                        (SELECT Timestamp FROM Operation WHERE Description = 'Remote Synchronization' ORDER BY Timestamp DESC LIMIT 1),
                        0
                    )";
                var count = (long)cmd.ExecuteScalar();
                return count >= m_count;
            default:
                return false;
        }
    }

    /// <summary>
    /// Records a remote synchronization operation in the database.
    /// </summary>
    private void RecordSyncOperation()
    {
        if (!m_options.TryGetValue("dbpath", out var dbpath) || string.IsNullOrWhiteSpace(dbpath))
            return;

        using var db = SQLiteLoader.LoadConnection(dbpath);
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO Operation (Description, Timestamp) VALUES ('Remote Synchronization', @timestamp)";
        cmd.Parameters.AddWithValue("@timestamp", DateTime.UtcNow.ToFileTimeUtc());
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Builds the arguments for the remote synchronization command.
    /// </summary>
    /// <returns>An array of command line arguments.</returns>
    private string[] BuildArguments()
    {
        string[] args = [
            m_source,
            m_destination,
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
