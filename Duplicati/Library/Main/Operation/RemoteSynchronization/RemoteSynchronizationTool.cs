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
using System.CommandLine;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Localization.Short;
using Duplicati.Library.Logging;

namespace Duplicati.Library.Main.Operation.RemoteSynchronization;

#nullable enable

/// <summary>
/// The arguments of the remote synchronization tool.
/// </summary>
/// <param name="Src">The url of the destination to mirror</param>
/// <param name="Dst">The url of the destination to write to</param>
/// <param name="AutoCreateFolders">Whether a missing destination folder is created</param>
/// <param name="BackendRetries">The number of times a backend operation is retried</param>
/// <param name="BackendRetryDelay">The delay between retries, in milliseconds</param>
/// <param name="BackendRetryWithExponentialBackoff">Whether the delay doubles for each retry</param>
/// <param name="Confirm">Whether the run starts without asking</param>
/// <param name="DryRun">Whether nothing is written or deleted</param>
/// <param name="DstOptions">The options of the destination, as <c>key=value</c></param>
/// <param name="Force">Whether every volume is copied again without looking at the destination</param>
/// <param name="GlobalOptions">The options of both sides, as <c>key=value</c></param>
/// <param name="LogFile">The log file to write, or empty</param>
/// <param name="LogLevel">The log level</param>
/// <param name="ParseArgumentsOnly">Whether the tool stops after reading the arguments</param>
/// <param name="Progress">Whether progress is printed</param>
/// <param name="SrcOptions">The options of the source, as <c>key=value</c></param>
internal sealed record RemoteSynchronizationToolConfig(
    string Src,
    string Dst,
    string SecretProvider,
    string SecretProviderPattern,
    string SecretProviderCache,
    bool AutoCreateFolders = true,
    int BackendRetries = 3,
    int BackendRetryDelay = 1000,
    bool BackendRetryWithExponentialBackoff = true,
    bool Confirm = false,
    bool DryRun = false,
    List<string>? DstOptions = null,
    bool Force = false,
    List<string>? GlobalOptions = null,
    string LogFile = "",
    string LogLevel = "Information",
    bool ParseArgumentsOnly = false,
    bool Progress = false,
    List<string>? SrcOptions = null
)
{
    /// <summary>
    /// Describes the arguments without the url credentials, the options or the secret
    /// provider, which carry passphrases and credentials.
    /// </summary>
    public override string ToString()
        => $"Src: {Library.Utility.Utility.GetUrlWithoutCredentials(Src)}, Dst: {Library.Utility.Utility.GetUrlWithoutCredentials(Dst)}, AutoCreateFolders: {AutoCreateFolders}, BackendRetries: {BackendRetries}, BackendRetryDelay: {BackendRetryDelay}, BackendRetryWithExponentialBackoff: {BackendRetryWithExponentialBackoff}, Confirm: {Confirm}, DryRun: {DryRun}, Force: {Force}, LogFile: {LogFile}, LogLevel: {LogLevel}, ParseArgumentsOnly: {ParseArgumentsOnly}, Progress: {Progress}";
}

/// <summary>
/// The remote synchronization tool: a command line front-end that mirrors one
/// Duplicati destination to another with <see cref="RemoteReplication"/>.
/// </summary>
public static class RemoteSynchronizationTool
{
    /// <summary>
    /// The log tag for this tool.
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType(typeof(RemoteSynchronizationTool));

    /// <summary>
    /// The options of earlier versions that the tool no longer has
    /// </summary>
    private static readonly string[] REMOVED_OPTIONS = ["--retention", "--verify-contents", "--verify-get-after-put", "--retry"];

    /// <summary>
    /// The descriptions of the tool and its arguments
    /// </summary>
    private static class Strings
    {
        public static string RootCommandDescription => LC.L(@"Remote Synchronization Tool

This tool makes one Duplicati destination a mirror of another. Only the backup
volumes are mirrored (the dblock, dindex and dlist files, and their parity
files); other files and all sub-folders are left alone on both sides. To mirror
the volumes of a single backup in a shared folder, give its prefix with
--global-options prefix=<prefix>.

A volume that is in the source but not in the destination is copied. A volume
that is in both is copied again if the sizes differ or the source volume is
newer. A volume that is in the destination but no longer in the source is
deleted.

With --global-options sync-keep-modification-time=true the copies get the
modification time of the source volumes, on destinations that can set the time.

If the force option is set, every volume is copied to the destination without
looking at what the destination holds, and nothing is deleted.

Unless the confirm option is set, the tool first works out what it would do,
shows it, and asks before it changes anything.

With --secret-provider the query parameters of the two backend strings, and the
values of the options, can name a secret instead of holding it, e.g.
auth-password=$name. The names are replaced with the values from the provider.
");
        public static string SourceBackendDescription => LC.L("The source backend string");
        public static string DestinationBackendDescription => LC.L("The destination backend string");
        public static string AutoCreateFoldersDescription => LC.L("Automatically create folders in the destination backend if they do not exist");
        public static string BackendRetriesDescription => LC.L("Number of times to retry a backend operation on errors");
        public static string BackendRetryDelayDescription => LC.L("Delay in milliseconds between backend retries");
        public static string BackendRetryWithExponentialBackoffDescription => LC.L("Use exponential backoff for backend retries, multiplying the delay by two for each failure.");
        public static string ConfirmDescription => LC.L("Automatically confirm the operation");
        public static string DryRunDescription => LC.L("Do not actually write or delete files. If not set here, the global options will be checked");
        public static string DstOptionsDescription => LC.L("Options for the destination backend. Each option is a key-value pair separated by an equals sign, e.g. --dst-options key1=value1 key2=value2 [default: empty]");
        public static string ForceDescription => LC.L("Copy every volume to the destination, without comparing with what the destination holds");
        public static string GlobalOptionsDescription => LC.L("Global options all backends. May be overridden by backend specific options (src-options, dst-options). Each option is a key-value pair separated by an equals sign, e.g. --global-options key1=value1 key2=value2 [default: empty]");
        public static string LogFileDescription => LC.L("The log file to write to [default: \"\"]");
        public static string LogLevelDescription => LC.L("The log level to use");
        public static string ParseArgumentsOnlyDescription => LC.L("Only parse the arguments and then exit");
        public static string ProgressDescription => LC.L("Print progress to STDOUT");
        public static string SecretProviderDescription => LC.L("The url of the secret provider to read secrets from, e.g. env:// or wincred://. A query parameter of a backend string, or the value of an option, that is the name of a secret is replaced with the secret [default: \"\"]");
        public static string SecretProviderPatternDescription => LC.L("The pattern that marks a value as the name of a secret, e.g. $ for $name or ${} for ${name} [default: $]");
        public static string SecretProviderCacheDescription => LC.L("How the secrets are cached for when the provider cannot be reached: None, InMemory or Persistent [default: None]");
        public static string SrcOptionsDescription => LC.L("Options for the source backend. Each option is a key-value pair separated by an equals sign, e.g. --src-options key1=value1 key2=value2 [default: empty]");
        public static string RemovedOptionError(string option) => LC.L("The option {0} is no longer supported.", option);
    }

    /// <summary>
    /// Entry point for the tool.
    /// </summary>
    /// <param name="args">The commandline arguments</param>
    /// <returns>0 on success, -1 on abort, and the number of errors encountered otherwise.</returns>
    public static async Task<int> RunAsync(string[] args)
    {
        // An option that is gone fails loudly instead of being accepted and doing nothing
        foreach (var arg in args)
        {
            var name = arg.Split('=', 2)[0];
            if (REMOVED_OPTIONS.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                await Console.Error.WriteLineAsync(Strings.RemovedOptionError(name)).ConfigureAwait(false);
                return 1;
            }
        }

        var arg_src = new Argument<string>("backend_src") { Description = Strings.SourceBackendDescription };
        var arg_dst = new Argument<string>("backend_dst") { Description = Strings.DestinationBackendDescription };

        var opt_auto_create_folders = new Option<bool>("--auto-create-folders") { Description = Strings.AutoCreateFoldersDescription, DefaultValueFactory = _ => true };
        var opt_backend_retries = new Option<int>("--backend-retries") { Description = Strings.BackendRetriesDescription, DefaultValueFactory = _ => 3, Arity = ArgumentArity.ExactlyOne };
        var opt_backend_retry_delay = new Option<int>("--backend-retry-delay") { Description = Strings.BackendRetryDelayDescription, DefaultValueFactory = _ => 1000, Arity = ArgumentArity.ExactlyOne };
        var opt_backend_retry_with_exponential_backoff = new Option<bool>("--backend-retry-with-exponential-backoff") { Description = Strings.BackendRetryWithExponentialBackoffDescription, DefaultValueFactory = _ => true };
        var opt_confirm = new Option<bool>("--confirm", "--yes", "-y") { Description = Strings.ConfirmDescription, DefaultValueFactory = _ => false };
        var opt_dry_run = new Option<bool>("--dry-run", "-d") { Description = Strings.DryRunDescription, DefaultValueFactory = _ => false };
        var opt_dst_options = OptionWithMultipleTokens("--dst-options", Strings.DstOptionsDescription);
        var opt_force = new Option<bool>("--force", "-f") { Description = Strings.ForceDescription, DefaultValueFactory = _ => false };
        var opt_global_options = OptionWithMultipleTokens("--global-options", Strings.GlobalOptionsDescription);
        var opt_log_file = new Option<string>("--log-file") { Description = Strings.LogFileDescription, DefaultValueFactory = _ => "", Arity = ArgumentArity.ExactlyOne };
        var opt_log_level = new Option<string>("--log-level") { Description = Strings.LogLevelDescription, DefaultValueFactory = _ => "Information", Arity = ArgumentArity.ExactlyOne };
        var opt_parse_arguments_only = new Option<bool>("--parse-arguments-only") { Description = Strings.ParseArgumentsOnlyDescription, DefaultValueFactory = _ => false };
        var opt_progress = new Option<bool>("--progress") { Description = Strings.ProgressDescription, DefaultValueFactory = _ => false };
        var opt_secret_provider = new Option<string>("--secret-provider") { Description = Strings.SecretProviderDescription, DefaultValueFactory = _ => "", Arity = ArgumentArity.ExactlyOne };
        var opt_secret_provider_cache = new Option<string>("--secret-provider-cache") { Description = Strings.SecretProviderCacheDescription, DefaultValueFactory = _ => "", Arity = ArgumentArity.ExactlyOne };
        var opt_secret_provider_pattern = new Option<string>("--secret-provider-pattern") { Description = Strings.SecretProviderPatternDescription, DefaultValueFactory = _ => "", Arity = ArgumentArity.ExactlyOne };
        var opt_src_options = OptionWithMultipleTokens("--src-options", Strings.SrcOptionsDescription);

        var root_cmd = new RootCommand(Strings.RootCommandDescription)
            {
                arg_src,
                arg_dst,
                opt_auto_create_folders,
                opt_backend_retries,
                opt_backend_retry_delay,
                opt_backend_retry_with_exponential_backoff,
                opt_confirm,
                opt_dry_run,
                opt_dst_options,
                opt_force,
                opt_global_options,
                opt_log_file,
                opt_log_level,
                opt_parse_arguments_only,
                opt_progress,
                opt_secret_provider,
                opt_secret_provider_cache,
                opt_secret_provider_pattern,
                opt_src_options,
            };

        root_cmd.SetAction((parseResult, cancellationToken) =>
        {
            var config = new RemoteSynchronizationToolConfig(
                Src: parseResult.GetValue(arg_src)!,
                Dst: parseResult.GetValue(arg_dst)!,
                SecretProvider: parseResult.GetValue(opt_secret_provider) ?? "",
                SecretProviderPattern: parseResult.GetValue(opt_secret_provider_pattern) ?? "",
                SecretProviderCache: parseResult.GetValue(opt_secret_provider_cache) ?? "",
                AutoCreateFolders: parseResult.GetValue(opt_auto_create_folders),
                BackendRetries: parseResult.GetValue(opt_backend_retries),
                BackendRetryDelay: parseResult.GetValue(opt_backend_retry_delay),
                BackendRetryWithExponentialBackoff: parseResult.GetValue(opt_backend_retry_with_exponential_backoff),
                Confirm: parseResult.GetValue(opt_confirm),
                DryRun: parseResult.GetValue(opt_dry_run),
                DstOptions: parseResult.GetValue(opt_dst_options) ?? [],
                Force: parseResult.GetValue(opt_force),
                GlobalOptions: parseResult.GetValue(opt_global_options) ?? [],
                LogFile: parseResult.GetValue(opt_log_file) ?? "",
                LogLevel: parseResult.GetValue(opt_log_level) ?? "Information",
                ParseArgumentsOnly: parseResult.GetValue(opt_parse_arguments_only),
                Progress: parseResult.GetValue(opt_progress),
                SrcOptions: parseResult.GetValue(opt_src_options) ?? []);

            return RunAsync(config, cancellationToken);
        });

        return await root_cmd.Parse(args).InvokeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the tool with parsed arguments: sets up the logging and mirrors the source.
    /// </summary>
    /// <param name="config">The arguments of the tool</param>
    /// <param name="token">The cancellation token</param>
    /// <returns>0 on success, -1 on abort, and the number of errors encountered otherwise.</returns>
    internal static async Task<int> RunAsync(RemoteSynchronizationToolConfig config, CancellationToken token)
    {
        var log_level = Enum.TryParse<LogMessageType>(config.LogLevel, true, out var parsed_level) ? parsed_level : LogMessageType.Information;

        using var console_sink = new StreamLogDestination(Console.OpenStandardOutput());
        StreamLogDestination? log_file_sink = null;
        if (!string.IsNullOrEmpty(config.LogFile))
        {
            var log_file_dir = SystemIO.IO_OS.PathGetDirectoryName(config.LogFile);
            if (!string.IsNullOrEmpty(log_file_dir) && !SystemIO.IO_OS.DirectoryExists(log_file_dir))
                SystemIO.IO_OS.DirectoryCreate(log_file_dir);
            log_file_sink = new StreamLogDestination(config.LogFile);
        }

        try
        {
            using var sink = new MultiLogDestination(log_file_sink == null ? [console_sink] : [console_sink, log_file_sink]);
            using var _ = Log.StartScope(sink, log_level);
            return await RunCoreAsync(config, token).ConfigureAwait(false);
        }
        finally
        {
            log_file_sink?.Dispose();
        }
    }

    /// <summary>
    /// Mirrors the source to the destination, asking first unless told not to.
    /// </summary>
    /// <param name="config">The arguments of the tool</param>
    /// <param name="token">The cancellation token</param>
    /// <returns>0 on success, -1 on abort, and the number of errors encountered otherwise.</returns>
    private static async Task<int> RunCoreAsync(RemoteSynchronizationToolConfig config, CancellationToken token)
    {
        var global_options = RemoteSyncConfig.ParseOptionList(config.GlobalOptions);
        var src_options = RemoteSyncConfig.ParseOptionList(config.SrcOptions);
        var dst_options = RemoteSyncConfig.ParseOptionList(config.DstOptions);

        // The retry arguments of the tool apply to both sides, unless a side has the
        // option under its real name
        var tool_options = new Dictionary<string, string?>
        {
            ["number-of-retries"] = Math.Max(0, config.BackendRetries).ToString(),
            ["retry-delay"] = RemoteSyncConfig.MillisecondsToTimespanOption(config.BackendRetryDelay),
            ["retry-with-exponential-backoff"] = config.BackendRetryWithExponentialBackoff ? "true" : "false",
        };

        // The global options are overridden by the options of each side
        var source_options = Merge(tool_options, global_options, src_options);
        var target_base_options = Merge(tool_options, global_options);
        if (!dst_options.ContainsKey("disable-autocreate-folder") && !global_options.ContainsKey("disable-autocreate-folder"))
            dst_options["disable-autocreate-folder"] = config.AutoCreateFolders ? "false" : "true";

        if (config.ParseArgumentsOnly)
        {
            Log.WriteInformationMessage(LOGTAG, "ArgumentsParsed", "Arguments parsed successfully; {0}; exiting.", config);
            return 0;
        }

        // The urls with the secrets in place are for the backends only; what is shown
        // is made from the urls as they were given
        var (src_url, dst_url) = await ApplySecretProviderAsync(config, source_options, target_base_options, dst_options, token).ConfigureAwait(false);

        // The tool mirrors the volumes of every backup in the source, unless it is told which one
        var any_prefix = !source_options.ContainsKey("prefix");
        var display_src = Library.Utility.Utility.GetUrlWithoutCredentials(config.Src);
        var display_dst = Library.Utility.Utility.GetUrlWithoutCredentials(config.Dst);

        // The tool keeps no state between runs: the journal of a run is of no use to
        // the next one, which lists both sides again
        using var sync_database_folder = new Library.Utility.TempFolder();
        var sync_database = System.IO.Path.Combine(sync_database_folder, "sync.sqlite");

        async Task<RemoteReplicationSummary> RunOnceAsync(bool dryRun)
        {
            var results = new RemoteSynchronizationResults { Destination = display_dst };
            IOperationProgressUpdater progress = config.Progress && !dryRun
                ? new ConsoleProgress(results.OperationProgressUpdater)
                : results.OperationProgressUpdater;

            var summaries = await RemoteReplication.RunAsync(
                src_url,
                source_options,
                target_base_options,
                [new RemoteReplicationTarget(dst_url, dst_options, sync_database, dryRun, config.Force)],
                results.BackendWriter,
                null,
                results.TaskControl,
                progress,
                null,
                any_prefix,
                token).ConfigureAwait(false);

            if (config.Progress && !dryRun)
                Console.WriteLine();
            return summaries[0];
        }

        static int ExitCode(RemoteReplicationSummary summary)
        {
            if (summary.Succeeded)
                return 0;
            var failures = summary.UploadFailures + summary.DeleteFailures;
            return (int)Math.Clamp(failures, 1, int.MaxValue);
        }

        // As this is a potentially destructive operation, ask for confirmation. What
        // would be done is only known after both sides are listed, so the question is
        // preceded by a run that changes nothing.
        if (!config.Confirm && !config.DryRun)
        {
            var plan = await RunOnceAsync(dryRun: true).ConfigureAwait(false);
            if (!plan.Succeeded)
                return ExitCode(plan);

            Console.WriteLine("This will perform the following actions:");
            Console.WriteLine($"    - Copy {plan.Copied} files from {display_src} to {display_dst}");
            Console.WriteLine($"    - Delete {plan.Deleted} files from {display_dst}");
            Console.WriteLine();
            Console.WriteLine("Do you want to continue? [y/N]");

            var response = Console.ReadLine();
            if (!response?.Trim().Equals("y", StringComparison.CurrentCultureIgnoreCase) ?? true)
            {
                Log.WriteInformationMessage(LOGTAG, "AbortedByUser", "Aborted");
                return -1;
            }
        }

        var summary = await RunOnceAsync(config.DryRun).ConfigureAwait(false);
        if (summary.Succeeded)
            Log.WriteInformationMessage(LOGTAG, "SynchronizationComplete", "Remote synchronization completed successfully: {0} files copied, {1} files deleted.", summary.Copied, summary.Deleted);
        else
            Log.WriteErrorMessage(LOGTAG, "SynchronizationFailed", null, "Remote synchronization to {0} did not complete: {1} files could not be copied and {2} files could not be deleted.", display_dst, summary.UploadFailures, summary.DeleteFailures);

        return ExitCode(summary);
    }

    /// <summary>
    /// Replaces the names of secrets in the urls and the options with their values,
    /// if the tool was given a secret provider. The options are changed in place.
    /// </summary>
    /// <param name="config">The arguments of the tool</param>
    /// <param name="sourceOptions">The options of the source</param>
    /// <param name="targetBaseOptions">The options the destination starts from</param>
    /// <param name="dstOptions">The options of the destination</param>
    /// <param name="token">The cancellation token</param>
    /// <returns>The urls of the source and the destination, with the secrets in place</returns>
    private static async Task<(string Src, string Dst)> ApplySecretProviderAsync(RemoteSynchronizationToolConfig config, Dictionary<string, string?> sourceOptions, Dictionary<string, string?> targetBaseOptions, Dictionary<string, string?> dstOptions, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(config.SecretProvider))
            return (config.Src, config.Dst);

        // The three sets are resolved in one go, so the provider is asked once. The
        // prefix keeps the sets apart, and keeps an option of a backend from being
        // read as a setting of the secret provider.
        var sets = new[] { ("src:", sourceOptions), ("base:", targetBaseOptions), ("dst:", dstOptions) };
        var combined = new Dictionary<string, string?> { ["secret-provider"] = config.SecretProvider };
        if (!string.IsNullOrWhiteSpace(config.SecretProviderPattern))
            combined["secret-provider-pattern"] = config.SecretProviderPattern;
        if (!string.IsNullOrWhiteSpace(config.SecretProviderCache))
            combined["secret-provider-cache"] = config.SecretProviderCache;

        foreach (var (prefix, set) in sets)
            foreach (var (key, value) in set)
                combined[prefix + key] = value;

        var urls = new[] { new Library.Utility.RelaxedUri(config.Src), new Library.Utility.RelaxedUri(config.Dst) };
        var unresolved = urls.Select(x => x.ToString()).ToArray();
        await SecretProviderHelper.ApplySecretProviderAsync([], urls, combined, Library.Utility.TempFolder.SystemTempPath, null, token).ConfigureAwait(false);

        foreach (var (prefix, set) in sets)
            foreach (var key in set.Keys.ToList())
                set[key] = combined[prefix + key];

        // A url without a secret is passed on as it was given
        return (
            urls[0].ToString() == unresolved[0] ? config.Src : urls[0].ToString(),
            urls[1].ToString() == unresolved[1] ? config.Dst : urls[1].ToString());
    }

    /// <summary>
    /// Merges option sets; a later set overrides an earlier one
    /// </summary>
    /// <param name="sets">The option sets</param>
    /// <returns>The merged options</returns>
    private static Dictionary<string, string?> Merge(params Dictionary<string, string?>[] sets)
    {
        var result = new Dictionary<string, string?>();
        foreach (var set in sets)
            foreach (var (key, value) in set)
                result[key] = value;
        return result;
    }

    /// <summary>
    /// Creates an option that can take multiple tokens.
    /// </summary>
    /// <param name="name">The name of the option</param>
    /// <param name="description">The description of the option</param>
    /// <returns>The option</returns>
    private static Option<List<string>> OptionWithMultipleTokens(string name, string description)
        => new Option<List<string>>(name)
        {
            Description = description,
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.OneOrMore,
            DefaultValueFactory = _ => []
        };

    /// <summary>
    /// Writes each log message to several destinations
    /// </summary>
    /// <param name="destinations">The destinations; owned by the caller</param>
    private sealed class MultiLogDestination(IReadOnlyList<ILogDestination> destinations) : ILogDestination, IDisposable
    {
        public void WriteMessage(LogEntry entry)
        {
            foreach (var destination in destinations)
                destination.WriteMessage(entry);
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Prints the number of files handled to the console, and passes everything on
    /// </summary>
    /// <param name="inner">The progress updater to pass the calls on to</param>
    private sealed class ConsoleProgress(IOperationProgressUpdater inner) : IOperationProgressUpdater
    {
        private long m_total;

        public void UpdatePhase(OperationPhase phase) => inner.UpdatePhase(phase);
        public void UpdateProgress(float progress) => inner.UpdateProgress(progress);
        public void StartFile(string filename, long size) => inner.StartFile(filename, size);
        public void UpdateFileProgress(long offset) => inner.UpdateFileProgress(offset);
        public void UpdatefileCount(long filecount, long filesize, bool done)
        {
            m_total = filecount;
            inner.UpdatefileCount(filecount, filesize, done);
        }
        public void UpdatefilesProcessed(long count, long size)
        {
            Console.Write($"\rProcessed: {count}/{m_total}");
            inner.UpdatefilesProcessed(count, size);
        }
        public void SetActiveFilesProvider(Func<ActiveFileProgress[]> provider) => inner.SetActiveFilesProvider(provider);
        public void UpdateSourceBytesRead(long bytesRead) => inner.UpdateSourceBytesRead(bytesRead);
        public void UpdateRemoteSyncDestination(int destinationIndex, int destinationCount) => inner.UpdateRemoteSyncDestination(destinationIndex, destinationCount);
    }
}
