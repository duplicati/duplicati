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

using System.Collections.Concurrent;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto.V2;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V2;

/// <summary>
/// Endpoint for sending a test email with the email report settings
/// </summary>
public class TestEmail : IEndpointV2
{
    /// <summary>
    /// The API extension that signals the endpoint is available
    /// </summary>
    public const string ApiExtension = "v2:report:test-email";

    /// <summary>
    /// The key of the module that sends emails
    /// </summary>
    private const string SendMailModuleKey = "sendmail";

    /// <summary>
    /// The prefix of the options used by the send mail module
    /// </summary>
    private const string SendMailOptionPrefix = "send-mail-";

    /// <summary>
    /// Maps the endpoint routes
    /// </summary>
    /// <param name="group">The route group builder</param>
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/report/test-email", ([FromServices] Connection connection, [FromServices] IApplicationSettings applicationSettings, [FromBody] TestEmailRequestDto input, CancellationToken cancelToken)
            => ExecuteAsync(connection, applicationSettings, input, cancelToken))
            .RequireAuthorization();
    }

    /// <summary>
    /// Sends a test email
    /// </summary>
    /// <param name="connection">The database connection</param>
    /// <param name="applicationSettings">The application settings</param>
    /// <param name="input">The request</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>The response DTO</returns>
    private static async Task<TestEmailResponseDto> ExecuteAsync(Connection connection, IApplicationSettings applicationSettings, TestEmailRequestDto input, CancellationToken cancelToken)
    {
        if (Library.DynamicLoader.GenericLoader.GetModule(SendMailModuleKey) is not ITestMessageModule module)
            return TestEmailResponseDto.CreateFailure("The email module is not available", "module-not-found");

        Dictionary<string, string?> options;
        try
        {
            options = BuildOptions(connection, input);
        }
        catch (UserInformationException ex)
        {
            return TestEmailResponseDto.CreateFailure(ex.Message, ex.HelpID ?? "invalid-options");
        }

        if (string.IsNullOrWhiteSpace(options.GetValueOrDefault("send-mail-to")))
            return TestEmailResponseDto.CreateFailure("No recipient is set", "missing-recipient");

        // Only messages from the module, and not the protocol log, which is logged as
        // profiling and has the login details
        var log = new ConcurrentQueue<string>();
        var moduleTag = Log.LogTagFromType(module.GetType());
        using (Log.StartScope(
            entry => log.Enqueue(entry.Exception == null
                ? entry.FormattedMessage
                : $"{entry.FormattedMessage} => {entry.Exception.Message}"),
            entry => entry.Level >= LogMessageType.Information && entry.FilterTag.Contains(moduleTag, StringComparison.Ordinal)))
        {
            try
            {
                await SecretProviderHelper.ApplySecretProviderAsync([], [], options, Library.Utility.TempFolder.SystemTempPath, applicationSettings.SecretProvider, cancelToken).ConfigureAwait(false);
                await module.SendTestMessageAsync(options, cancelToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
            {
                // The client went away, so there is no one to answer
                throw;
            }
            catch (Exception ex)
            {
                return TestEmailResponseDto.CreateFailure(GetMessage(ex), "send-failed", log);
            }
        }

        return TestEmailResponseDto.Create(log);
    }

    /// <summary>
    /// Builds the options for the module from the request and the stored settings
    /// </summary>
    /// <param name="connection">The database connection</param>
    /// <param name="input">The request</param>
    /// <returns>The options, with names without leading dashes</returns>
    internal static Dictionary<string, string?> BuildOptions(Connection connection, TestEmailRequestDto input)
    {
        var globalOptions = new Dictionary<string, string?>(Runner.GetCommonOptions(connection), StringComparer.OrdinalIgnoreCase);

        // The global options are kept for settings such as the secret provider,
        // but the send-mail options only if the caller asks for them
        var options = new Dictionary<string, string?>(globalOptions, StringComparer.OrdinalIgnoreCase);
        if (input.IncludeGlobalOptions != true)
            foreach (var key in options.Keys.Where(IsSendMailOption).ToList())
                options.Remove(key);

        var backupOptions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(input.BackupId))
        {
            var backup = connection.GetBackup(input.BackupId)
                ?? throw new UserInformationException("The backup was not found", "backup-not-found");
            foreach (var setting in backup.Settings ?? [])
                backupOptions[TrimDashes(setting.Name)] = setting.Value;
        }

        foreach (var (rawKey, value) in input.Options ?? [])
        {
            var key = TrimDashes(rawKey);
            if (!IsSendMailOption(key))
                continue;

            if (Connection.IsPasswordPlaceholder(value))
            {
                // The client only has the masked value, so the stored one is used
                var stored = !string.IsNullOrWhiteSpace(input.BackupId)
                    ? backupOptions.GetValueOrDefault(key)
                    : globalOptions.GetValueOrDefault(key);
                if (string.IsNullOrEmpty(stored) || Connection.IsPasswordPlaceholder(stored))
                    throw new UserInformationException($"The stored value for --{key} was not found, please enter it again", "password-not-found");
                options[key] = stored;
            }
            else
            {
                options[key] = value;
            }
        }

        return options;
    }

    private static bool IsSendMailOption(string key)
        => key.StartsWith(SendMailOptionPrefix, StringComparison.OrdinalIgnoreCase);

    private static string TrimDashes(string key)
        => key.StartsWith("--", StringComparison.Ordinal) ? key.Substring(2) : key;

    private static string GetMessage(Exception ex)
    {
        if (ex is AggregateException { InnerExceptions.Count: 1 } aggregate)
            ex = aggregate.InnerExceptions[0];

        var messages = new List<string>();
        for (var e = ex; e != null; e = e.InnerException)
            if (!messages.Contains(e.Message))
                messages.Add(e.Message);

        return string.Join(" => ", messages);
    }
}
