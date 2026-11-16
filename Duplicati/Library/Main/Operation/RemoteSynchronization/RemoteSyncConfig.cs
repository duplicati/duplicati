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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Operation.RemoteSynchronization;

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
/// <param name="Target">The destination and how to write to it</param>
/// <param name="Mode">When the destination is synchronized</param>
/// <param name="Interval">The interval for <see cref="RemoteSyncTriggerMode.Interval"/></param>
/// <param name="Count">The number of backups for <see cref="RemoteSyncTriggerMode.Counting"/></param>
/// <param name="SameAsSource">Set when the destination is the source itself, which is never synchronized</param>
public record RemoteSyncDestinationConfig(
    RemoteReplicationTarget Target,
    RemoteSyncTriggerMode Mode = RemoteSyncTriggerMode.Inline,
    TimeSpan? Interval = null,
    int? Count = null,
    bool SameAsSource = false
)
{
    /// <summary>
    /// The destination url with any credentials removed, safe to include in log messages.
    /// </summary>
    public string DisplayDestination => Target.DisplayDestination;

    /// <summary>
    /// Whether the destination can be synchronized: it has a url, and it is not the source.
    /// </summary>
    public bool IsUsable => !SameAsSource && !string.IsNullOrWhiteSpace(Target.Url);

    /// <summary>
    /// Describes the destination without its options, which carry passphrases and backend credentials.
    /// The generated record ToString would print every member, so it is replaced here to keep the
    /// record safe to pass into a log message.
    /// </summary>
    public override string ToString()
        => $"Dst: {DisplayDestination}, Mode: {Mode}, Interval: {Interval}, Count: {Count}";
}

/// <summary>
/// Raw configuration for a remote synchronization destination, as deserialized from JSON.
/// A value that is not given is <c>null</c>, so the option it maps to keeps the value of the job.
/// </summary>
public record RemoteSyncDestinationConfigRaw
{
    public string? Url { get; init; }
    public string? Mode { get; init; }
    public string? Interval { get; init; }
    public int? Count { get; init; }
    public string? SyncDatabasePath { get; init; }

    public bool DryRun { get; init; }
    public bool Force { get; init; }
    public bool? AutoCreateFolders { get; init; }
    public int? BackendRetries { get; init; }
    public int? BackendRetryDelay { get; init; }
    public bool? BackendRetryWithExponentialBackoff { get; init; }
    public List<string> DstOptions { get; init; } = [];
    public List<string> GlobalOptions { get; init; } = [];

    // Accepted for configurations written for earlier versions, and no longer used
    public bool Retention { get; init; }
    public bool VerifyContents { get; init; }
    public bool VerifyGetAfterPut { get; init; }
    public int? Retry { get; init; }
    public string? LogFile { get; init; }
    public string? LogLevel { get; init; }
    public bool? Progress { get; init; }
    public bool? ParseArgumentsOnly { get; init; }

    /// <summary>
    /// The keys that are not part of the configuration
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownKeys { get; init; }

    /// <summary>
    /// Describes the destination without the url credentials or the options, which carry passphrases
    /// and backend credentials. The generated record ToString would print every member.
    /// </summary>
    public override string ToString()
        => $"Url: {Duplicati.Library.Utility.Utility.GetUrlWithoutCredentials(Url ?? "")}, Mode: {Mode}, Interval: {Interval}, Count: {Count}";
}

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
/// The parsed <c>--remote-sync-json-config</c>, shared by the post-backup remote
/// synchronization and the sync operation.
/// </summary>
/// <param name="SyncOnWarnings">Whether the destinations are synchronized after a backup that reported warnings</param>
/// <param name="Destinations">The destinations, at the index they have in the configuration</param>
public sealed record RemoteSyncConfig(bool SyncOnWarnings, IReadOnlyList<RemoteSyncDestinationConfig> Destinations)
{
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<RemoteSyncConfig>();

    /// <summary>
    /// The options that describe the job the configuration belongs to, and that a
    /// destination must not inherit: its database, its encryption and the
    /// configuration itself.
    /// </summary>
    private static readonly string[] JOB_ONLY_OPTIONS = ["dbpath", "passphrase", "encryption-module", "remote-sync-json-config"];

    /// <summary>
    /// Parses the remote synchronization configuration of the options.
    /// </summary>
    /// <param name="options">The options that carry the configuration</param>
    /// <param name="sourceUrl">The url the destinations are written from; a destination equal to it is marked as not usable</param>
    /// <param name="problemsAreErrors">Whether a configuration that cannot be read is logged as an error, or as a warning</param>
    /// <returns>The configuration, or <c>null</c> if there is none or it could not be read</returns>
    public static RemoteSyncConfig? Parse(Options options, string? sourceUrl, bool problemsAreErrors)
    {
        var config = options.RemoteSyncJsonConfig;
        if (string.IsNullOrWhiteSpace(config))
            return null;

        void LogProblem(string id, Exception? ex, string message, params object?[] args)
        {
            if (problemsAreErrors)
                Logging.Log.WriteErrorMessage(LOGTAG, id, ex, message, args);
            else
                Logging.Log.WriteWarningMessage(LOGTAG, id, ex, message, args);
        }

        string jsonContent;
        if (config.TrimStart().StartsWith('{'))
        {
            // It's a JSON string
            jsonContent = config;
        }
        else
        {
            // It's a file path
            try
            {
                jsonContent = File.ReadAllText(config);
            }
            catch (Exception ex)
            {
                LogProblem("RemoteSyncJsonFileReadError", ex, "Failed to read JSON configuration file '{0}': {1}", config, ex.Message);
                return null;
            }
        }

        TopLevelRemoteSyncConfig? toplevel;
        try
        {
            toplevel = JsonSerializer.Deserialize<TopLevelRemoteSyncConfig>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower
            });
        }
        catch (Exception ex)
        {
            LogProblem("RemoteSyncJsonParseError", ex, "Failed to parse JSON configuration: {0}", ex.Message);
            return null;
        }

        if (toplevel is null)
        {
            LogProblem("RemoteSyncJsonParseError", null, "Failed to parse JSON configuration: top-level object is null.");
            return null;
        }

        var destinations = new List<RemoteSyncDestinationConfig>();
        foreach (var destination in toplevel.Destinations ?? [])
        {
            // A destination that cannot be read still takes its place in the list: the
            // index is what the trigger state of the other destinations is stored under.
            try
            {
                destinations.Add(ParseDestination(destination, sourceUrl));
            }
            catch (Exception ex)
            {
                LogProblem("RemoteSyncInvalidDestination", ex, "Remote synchronization destination {0} is not valid and is skipped: {1}", Library.Utility.Utility.GetUrlWithoutCredentials(destination.Url ?? ""), ex.Message);
                destinations.Add(new RemoteSyncDestinationConfig(new RemoteReplicationTarget("", [], null, false, false)));
            }
        }

        return new RemoteSyncConfig(toplevel.SyncOnWarnings, destinations);
    }

    /// <summary>
    /// Resolves the trigger and the options of a single destination
    /// </summary>
    /// <param name="destination">The destination as it was read</param>
    /// <param name="sourceUrl">The url the destinations are written from</param>
    /// <returns>The destination</returns>
    private static RemoteSyncDestinationConfig ParseDestination(RemoteSyncDestinationConfigRaw destination, string? sourceUrl)
    {
        var displayUrl = Library.Utility.Utility.GetUrlWithoutCredentials(destination.Url ?? "");
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

        // An explicit mode needs its parameter, or the destination syncs once and is
        // then never triggered again: the trigger check compares against a null
        // interval or count, which is always false. Treat it the way an interval
        // that cannot be parsed is treated: say so and run the destination inline.
        if (mode == RemoteSyncTriggerMode.Interval && !interval_parsed.HasValue)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncMissingInterval", null, "Remote synchronization destination uses interval mode but has no interval; defaulting to inline mode");
            mode = RemoteSyncTriggerMode.Inline;
        }
        else if (mode == RemoteSyncTriggerMode.Counting && !destination.Count.HasValue)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncMissingCount", null, "Remote synchronization destination uses counting mode but has no count; defaulting to inline mode");
            mode = RemoteSyncTriggerMode.Inline;
        }

        // The keys the earlier remote synchronization had its own names for map onto
        // the options of the backend manager. The option lists come last, so an option
        // given by its real name wins.
        var targetOptions = new Dictionary<string, string?>();
        if (destination.AutoCreateFolders.HasValue)
            targetOptions["disable-autocreate-folder"] = (!destination.AutoCreateFolders.Value).ToString().ToLowerInvariant();
        if (destination.BackendRetries.HasValue)
            targetOptions["number-of-retries"] = Math.Max(0, destination.BackendRetries.Value).ToString();
        if (destination.BackendRetryDelay.HasValue)
            targetOptions["retry-delay"] = MillisecondsToTimespanOption(destination.BackendRetryDelay.Value);
        if (destination.BackendRetryWithExponentialBackoff.HasValue)
            targetOptions["retry-with-exponential-backoff"] = destination.BackendRetryWithExponentialBackoff.Value.ToString().ToLowerInvariant();
        foreach (var (key, value) in ParseOptionList(destination.GlobalOptions))
            targetOptions[key] = value;
        foreach (var (key, value) in ParseOptionList(destination.DstOptions))
            targetOptions[key] = value;

        // What the earlier remote synchronization did and this one does not: say so
        // when it was asked for, so nobody relies on a rename or a verification that
        // no longer happens.
        var removed = new List<string>();
        if (destination.Retention) removed.Add("retention");
        if (destination.VerifyContents) removed.Add("verify-contents");
        if (destination.VerifyGetAfterPut) removed.Add("verify-get-after-put");
        if (removed.Count > 0)
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncUnsupportedOption", null, "Remote synchronization destination {0} uses options that are no longer supported and are ignored: {1}", displayUrl, string.Join(", ", removed));

        var ignored = new List<string>();
        if (destination.Retry.HasValue) ignored.Add("retry");
        if (!string.IsNullOrEmpty(destination.LogFile)) ignored.Add("log-file");
        if (!string.IsNullOrEmpty(destination.LogLevel)) ignored.Add("log-level");
        if (destination.Progress.HasValue) ignored.Add("progress");
        if (destination.ParseArgumentsOnly.HasValue) ignored.Add("parse-arguments-only");
        if (ignored.Count > 0)
            Logging.Log.WriteInformationMessage(LOGTAG, "RemoteSyncIgnoredOption", "Remote synchronization destination {0} uses options that no longer have an effect: {1}", displayUrl, string.Join(", ", ignored));

        if (destination.UnknownKeys is { Count: > 0 })
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncUnknownOption", null, "Remote synchronization destination {0} has unknown keys that are ignored: {1}", displayUrl, string.Join(", ", destination.UnknownKeys.Keys.Order()));

        var sameAsSource = !string.IsNullOrWhiteSpace(destination.Url) && string.Equals(destination.Url, sourceUrl, StringComparison.OrdinalIgnoreCase);
        if (sameAsSource)
            Logging.Log.WriteWarningMessage(LOGTAG, "RemoteSyncDestinationIsSource", null, "Remote synchronization destination {0} is the same as the source and is skipped.", displayUrl);

        return new RemoteSyncDestinationConfig(
            new RemoteReplicationTarget(
                destination.Url ?? "",
                targetOptions,
                string.IsNullOrWhiteSpace(destination.SyncDatabasePath) ? null : destination.SyncDatabasePath,
                destination.DryRun,
                destination.Force),
            mode,
            interval_parsed,
            destination.Count,
            sameAsSource);
    }

    /// <summary>
    /// Converts a delay in milliseconds to the value of a timespan option, which
    /// counts in whole seconds
    /// </summary>
    /// <param name="milliseconds">The delay in milliseconds</param>
    /// <returns>The option value</returns>
    internal static string MillisecondsToTimespanOption(int milliseconds)
        => $"{(Math.Max(0, milliseconds) + 999) / 1000}s";

    /// <summary>
    /// Parses a list of <c>key=value</c> options. The value is everything after the
    /// first equals sign, so it may contain equals signs itself.
    /// </summary>
    /// <param name="options">The options to parse</param>
    /// <returns>The options by name</returns>
    /// <exception cref="UserInformationException">If an entry has no name</exception>
    public static Dictionary<string, string?> ParseOptionList(IEnumerable<string>? options)
    {
        var result = new Dictionary<string, string?>();
        foreach (var option in options ?? [])
        {
            if (string.IsNullOrWhiteSpace(option))
                continue;

            var ix = option.IndexOf('=');
            var key = (ix < 0 ? option : option[..ix]).Trim();
            if (key.StartsWith("--", StringComparison.Ordinal))
                key = key[2..];
            if (key.Length == 0)
                throw new UserInformationException("An option without a name was given; options are written as key=value.", "InvalidOption");

            result[key] = ix < 0 ? null : option[(ix + 1)..];
        }

        return result;
    }

    /// <summary>
    /// Builds the options a destination is written with: the options of the job,
    /// without the ones that only describe the job itself, and then the options of
    /// the destination.
    /// </summary>
    /// <param name="jobOptions">The options of the job</param>
    /// <param name="target">The destination</param>
    /// <param name="alsoRemove">Further options of the job the destination does not inherit</param>
    /// <returns>The options of the destination</returns>
    internal static Dictionary<string, string?> BuildTargetOptions(IReadOnlyDictionary<string, string?> jobOptions, RemoteReplicationTarget target, Func<string, bool>? alsoRemove = null)
    {
        var result = new Dictionary<string, string?>();
        foreach (var (key, value) in jobOptions)
            if (!JOB_ONLY_OPTIONS.Contains(key, StringComparer.OrdinalIgnoreCase) && alsoRemove?.Invoke(key) != true)
                result[key] = value;

        foreach (var (key, value) in target.Options)
            result[key] = value;

        return result;
    }

    /// <summary>
    /// Finds the sync database of a destination: the path the destination names;
    /// otherwise a file next to the database of the job, named after the job
    /// database and the destination; and for a job without a database, the one
    /// registered for the url of the destination.
    /// </summary>
    /// <param name="target">The destination</param>
    /// <param name="targetOptions">The options of the destination</param>
    /// <param name="jobDatabasePath">The path of the database of the job the destination belongs to, if it has one</param>
    /// <returns>The path of the sync database</returns>
    /// <exception cref="UserInformationException">If no database could be located</exception>
    internal static string LocateSyncDatabase(RemoteReplicationTarget target, Options targetOptions, string? jobDatabasePath)
    {
        var path = target.SyncDatabasePath;
        if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(jobDatabasePath))
        {
            // The name follows from the job and the destination, so every run finds the
            // same file, and the file stays with the database of the job
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(target.DisplayDestination)), 0, 8).ToLowerInvariant();
            var full = Path.GetFullPath(jobDatabasePath);
            path = Path.Combine(Path.GetDirectoryName(full) ?? "", $"{Path.GetFileNameWithoutExtension(full)}-sync-{hash}.sqlite");
        }
        if (string.IsNullOrWhiteSpace(path))
            path = CLIDatabaseLocator.GetDatabasePathForCLI(target.Url, targetOptions, true, false, true);
        if (string.IsNullOrWhiteSpace(path))
            throw new UserInformationException($"Failed to locate database for {target.DisplayDestination}", "DatabaseLocateFailed");
        return path;
    }
}
