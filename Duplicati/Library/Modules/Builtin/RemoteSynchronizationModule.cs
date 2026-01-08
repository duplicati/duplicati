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

    private const string OPTION_BACKEND_DST = "remote-sync-dst";
    private const string OPTION_FORCE = "remote-sync-force";
    private const string OPTION_RETENTION = "remote-sync-retention";
    private const string OPTION_BACKEND_RETRIES = "remote-sync-backend-retries";
    private const string OPTION_RETRY = "remote-sync-retry";

    private IReadOnlyDictionary<string, string> m_options = new Dictionary<string, string>();
    private string m_source;
    private string m_destination;
    private string m_operationName;
    private bool m_enabled;

    public string Key => "remotesync";
    public string DisplayName => Strings.RemoteSynchronization.DisplayName;
    public string Description => Strings.RemoteSynchronization.Description;
    public bool LoadAsDefault => true;

    public IList<ICommandLineArgument> SupportedCommands =>
    [
        new CommandLineArgument(OPTION_BACKEND_DST, CommandLineArgument.ArgumentType.String, Strings.RemoteSynchronization.BackendDestinationShort, Strings.RemoteSynchronization.BackendDestinationLong),
        new CommandLineArgument(OPTION_FORCE, CommandLineArgument.ArgumentType.Boolean, Strings.RemoteSynchronization.ForceShort, Strings.RemoteSynchronization.ForceLong, "false"),
        new CommandLineArgument(OPTION_RETENTION, CommandLineArgument.ArgumentType.Boolean, Strings.RemoteSynchronization.RetentionShort, Strings.RemoteSynchronization.RetentionLong, "false"),
        new CommandLineArgument(OPTION_BACKEND_RETRIES, CommandLineArgument.ArgumentType.Integer, Strings.RemoteSynchronization.BackendRetriesShort, Strings.RemoteSynchronization.BackendRetriesLong, "3"),
        new CommandLineArgument(OPTION_RETRY, CommandLineArgument.ArgumentType.Integer, Strings.RemoteSynchronization.RetryShort, Strings.RemoteSynchronization.RetryLong, "3"),
    ];

    public void Configure(IDictionary<string, string> commandlineOptions)
    {
        m_options = commandlineOptions.AsReadOnly();
        commandlineOptions.TryGetValue(OPTION_BACKEND_DST, out m_destination);
        m_enabled = !string.IsNullOrWhiteSpace(m_destination);
    }

    public void OnStart(string operationname, ref string remoteurl, ref string[] localpath)
    {
        if (!m_enabled)
            return;

        m_operationName = operationname;

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

    private string[] AddOption(string optionKey, string toolOption, string[] defaultvalue)
    {
        if (!m_options.TryGetValue(optionKey, out var value))
            return defaultvalue;

        if (string.IsNullOrWhiteSpace(value))
            return defaultvalue;

        return [toolOption, value];
    }

}
