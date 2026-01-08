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
using System.Linq;
using System.Text.RegularExpressions;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using RemoteSynchronization;

namespace Duplicati.Library.Modules.Builtin;

public class RemoteSynchronizationModule : IGenericCallbackModule
{
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<RemoteSynchronizationModule>();

    private static readonly Regex ARGREGEX = new Regex(
        @"(?<arg>(?<=\s|^)(""(?<value>[^""\\]*(?:\\.[^""\\]*)*)""|'(?<value>[^'\\]*(?:\\.[^'\\]*)*)'|(?<value>[^\s]+))\s?)",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture
    );

    private const string OPTION_BACKEND_SRC = "remote-sync-src";
    private const string OPTION_BACKEND_DST = "remote-sync-dst";
    private const string OPTION_AUTO_CREATE_FOLDERS = "remote-sync-auto-create-folders";
    private const string OPTION_BACKEND_RETRIES = "remote-sync-backend-retries";
    private const string OPTION_BACKEND_RETRY_DELAY = "remote-sync-backend-retry-delay";
    private const string OPTION_BACKEND_RETRY_BACKOFF = "remote-sync-backend-retry-with-exponential-backoff";
    private const string OPTION_CONFIRM = "remote-sync-confirm";
    private const string OPTION_DRY_RUN = "remote-sync-dry-run";
    private const string OPTION_DST_OPTIONS = "remote-sync-dst-options";
    private const string OPTION_FORCE = "remote-sync-force";
    private const string OPTION_GLOBAL_OPTIONS = "remote-sync-global-options";
    private const string OPTION_LOG_FILE = "remote-sync-log-file";
    private const string OPTION_LOG_LEVEL = "remote-sync-log-level";
    private const string OPTION_PARSE_ARGUMENTS_ONLY = "remote-sync-parse-arguments-only";
    private const string OPTION_PROGRESS = "remote-sync-progress";
    private const string OPTION_RETENTION = "remote-sync-retention";
    private const string OPTION_RETRY = "remote-sync-retry";
    private const string OPTION_SRC_OPTIONS = "remote-sync-src-options";
    private const string OPTION_VERIFY_CONTENTS = "remote-sync-verify-contents";
    private const string OPTION_VERIFY_GET_AFTER_PUT = "remote-sync-verify-get-after-put";

    private IReadOnlyDictionary<string, string> m_options = new Dictionary<string, string>();
    private string m_source;
    private string m_destination;
    private string m_operationName;
    private string m_remoteUrl;
    private bool m_enabled;

    public string Key => "remotesync";
    public string DisplayName => Strings.RemoteSynchronization.DisplayName;
    public string Description => Strings.RemoteSynchronization.Description;
    public bool LoadAsDefault => true;

    public IList<ICommandLineArgument> SupportedCommands =>
    [
        new CommandLineArgument(OPTION_BACKEND_SRC, CommandLineArgument.ArgumentType.String, Strings.RemoteSynchronization.BackendSourceShort, Strings.RemoteSynchronization.BackendSourceLong),
        new CommandLineArgument(OPTION_BACKEND_DST, CommandLineArgument.ArgumentType.String, Strings.RemoteSynchronization.BackendDestinationShort, Strings.RemoteSynchronization.BackendDestinationLong),
        new CommandLineArgument(OPTION_AUTO_CREATE_FOLDERS, CommandLineArgument.ArgumentType.Boolean, Strings.RemoteSynchronization.AutoCreateFoldersShort, Strings.RemoteSynchronization.AutoCreateFoldersLong, "false"),
        new CommandLineArgument(OPTION_BACKEND_RETRIES, CommandLineArgument.ArgumentType.Integer, Strings.RemoteSynchronization.BackendRetriesShort, Strings.RemoteSynchronization.BackendRetriesLong, "3"),
        new CommandLineArgument(OPTION_BACKEND_RETRY_DELAY, CommandLineArgument.ArgumentType.Integer, Strings.RemoteSynchronization.BackendRetryDelayShort, Strings.RemoteSynchronization.BackendRetryDelayLong, "1000"),
        new CommandLineArgument(OPTION_BACKEND_RETRY_BACKOFF, CommandLineArgument.ArgumentType.Boolean, Strings.RemoteSynchronization.BackendRetryBackoffShort, Strings.RemoteSynchronization.BackendRetryBackoffLong, "false"),
        new CommandLineArgument(OPTION_CONFIRM, CommandLineArgument.ArgumentType.Boolean, Strings.RemoteSynchronization.ConfirmShort, Strings.RemoteSynchronization.ConfirmLong, "false"),
        new CommandLineArgument(OPTION_DRY_RUN, CommandLineArgument.ArgumentType.Boolean, Strings.RemoteSynchronization.DryRunShort, Strings.RemoteSynchronization.DryRunLong, "false"),
        new CommandLineArgument(OPTION_DST_OPTIONS, CommandLineArgument.ArgumentType.String, Strings.RemoteSynchronization.DestinationOptionsShort, Strings.RemoteSynchronization.DestinationOptionsLong),
        new CommandLineArgument(OPTION_FORCE, CommandLineArgument.ArgumentType.Boolean, Strings.RemoteSynchronization.ForceShort, Strings.RemoteSynchronization.ForceLong, "false"),
        new CommandLineArgument(OPTION_GLOBAL_OPTIONS, CommandLineArgument.ArgumentType.String, Strings.RemoteSynchronization.GlobalOptionsShort, Strings.RemoteSynchronization.GlobalOptionsLong),
        new CommandLineArgument(OPTION_LOG_FILE, CommandLineArgument.ArgumentType.Path, Strings.RemoteSynchronization.LogFileShort, Strings.RemoteSynchronization.LogFileLong, string.Empty),
        new CommandLineArgument(OPTION_LOG_LEVEL, CommandLineArgument.ArgumentType.String, Strings.RemoteSynchronization.LogLevelShort, Strings.RemoteSynchronization.LogLevelLong, "Information"),
        new CommandLineArgument(OPTION_PARSE_ARGUMENTS_ONLY, CommandLineArgument.ArgumentType.Boolean, Strings.RemoteSynchronization.ParseArgumentsOnlyShort, Strings.RemoteSynchronization.ParseArgumentsOnlyLong, "false"),
        new CommandLineArgument(OPTION_PROGRESS, CommandLineArgument.ArgumentType.Boolean, Strings.RemoteSynchronization.ProgressShort, Strings.RemoteSynchronization.ProgressLong, "false"),
        new CommandLineArgument(OPTION_RETENTION, CommandLineArgument.ArgumentType.Boolean, Strings.RemoteSynchronization.RetentionShort, Strings.RemoteSynchronization.RetentionLong, "false"),
        new CommandLineArgument(OPTION_RETRY, CommandLineArgument.ArgumentType.Integer, Strings.RemoteSynchronization.RetryShort, Strings.RemoteSynchronization.RetryLong, "3"),
        new CommandLineArgument(OPTION_SRC_OPTIONS, CommandLineArgument.ArgumentType.String, Strings.RemoteSynchronization.SourceOptionsShort, Strings.RemoteSynchronization.SourceOptionsLong),
        new CommandLineArgument(OPTION_VERIFY_CONTENTS, CommandLineArgument.ArgumentType.Boolean, Strings.RemoteSynchronization.VerifyContentsShort, Strings.RemoteSynchronization.VerifyContentsLong, "false"),
        new CommandLineArgument(OPTION_VERIFY_GET_AFTER_PUT, CommandLineArgument.ArgumentType.Boolean, Strings.RemoteSynchronization.VerifyGetAfterPutShort, Strings.RemoteSynchronization.VerifyGetAfterPutLong, "false"),
    ];

    public void Configure(IDictionary<string, string> commandlineOptions)
    {
        m_options = commandlineOptions.AsReadOnly();
        commandlineOptions.TryGetValue(OPTION_BACKEND_SRC, out m_source);
        commandlineOptions.TryGetValue(OPTION_BACKEND_DST, out m_destination);
        m_enabled = !string.IsNullOrWhiteSpace(m_destination);
    }

    public void OnStart(string operationname, ref string remoteurl, ref string[] localpath)
    {
        if (!m_enabled)
            return;

        m_operationName = operationname;
        m_remoteUrl = remoteurl;

        if (string.IsNullOrWhiteSpace(m_source))
            m_source = remoteurl;
    }

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

    private string[] BuildArguments()
    {
        var args = new List<string>
        {
            m_source,
            m_destination
        };

        AddBoolOptionWithDefaultTrue(OPTION_AUTO_CREATE_FOLDERS, "--auto-create-folders", args);
        AddOption(OPTION_BACKEND_RETRIES, "--backend-retries", args);
        AddOption(OPTION_BACKEND_RETRY_DELAY, "--backend-retry-delay", args);
        AddBoolOptionWithDefaultTrue(OPTION_BACKEND_RETRY_BACKOFF, "--backend-retry-with-exponential-backoff", args);
        AddBoolOptionWithDefaultTrue(OPTION_CONFIRM, "--confirm", args);
        AddBoolOption(OPTION_DRY_RUN, "--dry-run", args);
        AddMultiValueOption(OPTION_DST_OPTIONS, "--dst-options", args);
        AddBoolOption(OPTION_FORCE, "--force", args);
        AddMultiValueOption(OPTION_GLOBAL_OPTIONS, "--global-options", args);
        AddOption(OPTION_LOG_FILE, "--log-file", args);
        AddOption(OPTION_LOG_LEVEL, "--log-level", args);
        AddBoolOption(OPTION_PARSE_ARGUMENTS_ONLY, "--parse-arguments-only", args);
        AddBoolOption(OPTION_PROGRESS, "--progress", args);
        AddBoolOption(OPTION_RETENTION, "--retention", args);
        AddOption(OPTION_RETRY, "--retry", args);
        AddMultiValueOption(OPTION_SRC_OPTIONS, "--src-options", args);
        AddBoolOption(OPTION_VERIFY_CONTENTS, "--verify-contents", args);
        AddBoolOption(OPTION_VERIFY_GET_AFTER_PUT, "--verify-get-after-put", args);

        return args.ToArray();
    }

    private void AddBoolOptionWithDefaultTrue(string optionKey, string toolOption, List<string> args)
    {
        if (m_options.ContainsKey(optionKey))
            AddBoolOption(optionKey, toolOption, args);
        else
            args.Add(toolOption);
    }

    private void AddBoolOption(string optionKey, string toolOption, List<string> args)
    {
        if (!m_options.TryGetValue(optionKey, out var value))
            return;

        args.Add(toolOption);
        if (!string.IsNullOrWhiteSpace(value))
            args.Add(value);
    }

    private void AddOption(string optionKey, string toolOption, List<string> args)
    {
        if (!m_options.TryGetValue(optionKey, out var value))
            return;

        if (string.IsNullOrWhiteSpace(value))
            return;

        args.Add(toolOption);
        args.Add(value);
    }

    private void AddMultiValueOption(string optionKey, string toolOption, List<string> args)
    {
        if (!m_options.TryGetValue(optionKey, out var value))
            return;

        var tokens = SplitArguments(value);
        if (tokens.Count == 0)
            return;

        args.Add(toolOption);
        args.AddRange(tokens);
    }

    private static List<string> SplitArguments(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();

        return ARGREGEX.Matches(value)
            .Select(match => match.Groups["value"].Value)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToList();
    }
}
