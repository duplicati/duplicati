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

using Duplicati.Library.Interface;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using ModelContextProtocol;

namespace Duplicati.WebserverCore.Mcp;

/// <summary>
/// Helpers shared by the MCP tools
/// </summary>
public static class McpHelpers
{
    /// <summary>
    /// The log tag for MCP tool calls
    /// </summary>
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType(typeof(McpHelpers));

    /// <summary>
    /// Runs a tool body, logging the call and turning the errors meant for the user into errors the agent can read.
    /// Other exceptions are left to the MCP server, which reports them without details.
    /// </summary>
    /// <typeparam name="T">The result type</typeparam>
    /// <param name="tool">The name of the tool</param>
    /// <param name="target">The backup or task the call is about, for the log</param>
    /// <param name="scope">What the caller is allowed to do</param>
    /// <param name="level">The permission level the tool needs</param>
    /// <param name="body">The tool body</param>
    /// <returns>The result of the body</returns>
    public static async Task<T> RunAsync<T>(string tool, string? target, McpScope scope, McpTokenLevel level, Func<Task<T>> body)
    {
        Library.Logging.Log.WriteInformationMessage(LOGTAG, "McpToolCall", "MCP tool {0} called for {1} by {2}", tool, target ?? "the server", scope.CallerName);
        try
        {
            scope.RequireLevel(level);
            return await body().ConfigureAwait(false);
        }
        catch (DatabaseLockedException)
        {
            throw new McpException("The backup is in use by another task. Use get_server_status to find the task, wait for it with wait_for_task, and try again.");
        }
        catch (UserInformationException ex)
        {
            throw new McpException(ex.Message);
        }
        catch (UserReportedHttpException ex)
        {
            throw new McpException(ex.Message);
        }
        catch (Exception ex) when (ex is not McpException and not OperationCanceledException)
        {
            // The agent gets no details of an unexpected error, so they are logged here
            Library.Logging.Log.WriteErrorMessage(LOGTAG, "McpToolFailed", ex, "MCP tool {0} failed", tool);
            throw;
        }
    }

    /// <summary>
    /// Finds a backup from its ID or its exact name
    /// </summary>
    /// <param name="connection">The server database connection</param>
    /// <param name="scope">What the caller is allowed to do; a backup outside it is reported as not existing</param>
    /// <param name="backup">The ID or name of the backup</param>
    /// <returns>The backup</returns>
    public static IBackup ResolveBackup(Connection connection, McpScope scope, string backup)
    {
        if (string.IsNullOrWhiteSpace(backup))
            throw new McpException("No backup given. Use list_backups to find the ID.");

        var byId = connection.GetBackup(backup);
        if (byId != null && scope.CanAccessBackup(byId.ID))
            return byId;

        // The list of backups holds no sources or settings, so the match is loaded by its ID
        var byName = connection.Backups.Where(x => scope.CanAccessBackup(x.ID) && string.Equals(x.Name, backup, StringComparison.OrdinalIgnoreCase)).ToList();
        return byName.Count switch
        {
            1 => connection.GetBackup(byName[0].ID) ?? throw new McpException($"The backup \"{backup}\" no longer exists."),
            0 => throw new McpException($"No backup has the ID or name \"{backup}\". Use list_backups to find the ID."),
            _ => throw new McpException($"More than one backup is named \"{backup}\". Use the ID from list_backups.")
        };
    }

    /// <summary>
    /// Parses a time given by the caller of a tool
    /// </summary>
    /// <param name="time">The time, as a point in time or as an interval that is added to the current time</param>
    /// <returns>The time in UTC</returns>
    /// <exception cref="McpException">The time is not understood</exception>
    public static DateTime ParseTime(string time)
    {
        try
        {
            return Library.Utility.Timeparser.ParseTimeInterval(time, DateTime.Now).ToUniversalTime();
        }
        catch (Exception)
        {
            throw new McpException($"The time \"{time}\" is not understood. Pass a time as list_versions gives it, such as 2026-01-31T12:00:00Z.");
        }
    }

    /// <summary>
    /// Formats a time for tool output
    /// </summary>
    /// <param name="time">The time to format</param>
    /// <returns>The time in UTC as an ISO 8601 string</returns>
    public static string FormatTime(DateTime time)
        => time.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a time that is stored with a backup, so it reads like the other times the tools return
    /// </summary>
    /// <param name="value">The stored time, if any</param>
    /// <returns>The time in UTC as an ISO 8601 string, the value as it is if it is not a stored time, or <c>null</c> if there is none</returns>
    public static string? FormatStoredTime(string? value)
        => string.IsNullOrWhiteSpace(value) ? null
            : Library.Utility.Utility.TryDeserializeDateTime(value, out var time) ? FormatTime(time)
            : value;

    /// <summary>
    /// Names an operation the way a finished task names it, where a backup is not called a backup or sync
    /// </summary>
    /// <param name="operation">The name of the operation of a task</param>
    /// <returns>The name the tools report</returns>
    public static string FormatOperation(string operation)
        => operation == nameof(Server.Serialization.DuplicatiOperation.BackupOrSync) ? "Backup" : operation;
}
