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

using System.ComponentModel;
using Duplicati.Library.Utility;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Duplicati.WebserverCore.Mcp.Tools;

/// <summary>
/// MCP tools for finding and running backups
/// </summary>
/// <param name="connection">The server database connection</param>
/// <param name="backupTaskService">The service queuing backups</param>
/// <param name="queueRunnerService">The task queue</param>
/// <param name="mcpTaskService">The service describing tasks</param>
/// <param name="scope">What the caller is allowed to do</param>
[McpServerToolType]
public class BackupTools(Connection connection, IBackupTaskService backupTaskService, IQueueRunnerService queueRunnerService, McpTaskService mcpTaskService, McpScope scope)
{
    [McpServerTool(Name = "list_backups", Title = "List backups", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Lists the configured backups with their ID, name, source paths and last run. Use the ID with the other tools. A backup only contains what is below its source paths.")]
    public Task<McpBackupList> ListBackups()
        => McpHelpers.RunAsync("list_backups", null, scope, McpTokenLevel.Read, () =>
        {
            var current = queueRunnerService.GetCurrentTask();
            var tasks = queueRunnerService.GetCurrentTasks();
            if (current != null)
                tasks.Insert(0, current);

            // The list of backups holds no sources, so each backup is loaded by its ID
            return Task.FromResult(new McpBackupList(connection.Backups
                .Where(x => scope.CanAccessBackup(x.ID))
                .Select(x => connection.GetBackup(x.ID))
                .WhereNotNull()
                .Where(x => !x.IsTemporary)
                .Select(x =>
                {
                    x.RemoveSensitiveInformation();
                    return x;
                })
                .Select(x => new McpBackupInfo(
                    x.ID,
                    x.Name,
                    string.IsNullOrWhiteSpace(x.Description) ? null : x.Description,
                    x.OperationType.ToString(),
                    (x.Sources ?? []).Select(DescribeSource).ToList(),
                    GetScheme(x.TargetURL),
                    McpHelpers.FormatStoredTime(GetMetadata(x, "LastBackupStarted")),
                    McpHelpers.FormatStoredTime(GetMetadata(x, "LastBackupFinished")),
                    McpHelpers.FormatStoredTime(GetMetadata(x, "LastErrorDate")),
                    GetMetadata(x, "LastErrorMessage"),
                    long.TryParse(GetMetadata(x, "BackupListCount"), out var versionCount) ? versionCount : null,
                    tasks.FirstOrDefault(t => t.BackupID == x.ID)?.TaskID))
                .ToList()));
        });

    [McpServerTool(Name = "run_backup", Title = "Run a backup", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Queues a run of a backup and returns at once with the task ID. Use wait_for_task to wait for it to finish; the finished task reports the restore point to pass to restore. If alreadyQueued is true, a run of this backup was already queued or running and may have read files before your latest changes: wait for it, and call run_backup again if that matters.")]
    public Task<McpTaskInfo> RunBackup(
        [Description("The ID of the backup from list_backups, or its exact name")] string backup)
        => McpHelpers.RunAsync("run_backup", backup, scope, McpTokenLevel.Backup, async () =>
        {
            var bk = McpHelpers.ResolveBackup(connection, scope, backup);
            if (bk.IsTemporary || bk.OperationType != OperationType.Backup)
                throw new McpException($"\"{bk.Name}\" is not a backup job and cannot be run with this tool.");

            var started = backupTaskService.StartBackup(bk, false);
            if (started.AlreadyQueued && started.Operation != DuplicatiOperation.BackupOrSync)
                throw new McpException($"Another operation ({started.Operation}, task {started.TaskID}) is queued or running for this backup. Wait for it with wait_for_task and call run_backup again.");

            return await mcpTaskService.DescribeAsync(started.TaskID, started.AlreadyQueued).ConfigureAwait(false);
        });

    /// <summary>
    /// Gets a metadata value of a backup
    /// </summary>
    /// <param name="backup">The backup</param>
    /// <param name="key">The metadata key</param>
    /// <returns>The value, or <c>null</c> if it is not set</returns>
    private static string? GetMetadata(IBackup backup, string key)
        => backup.Metadata != null && backup.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>
    /// Gets the scheme of a URL, which names the storage type without revealing the address or credentials
    /// </summary>
    /// <param name="url">The URL</param>
    /// <returns>The scheme, or <c>null</c> if the value has none</returns>
    private static string? GetScheme(string? url)
    {
        var ix = url?.IndexOf("://", StringComparison.Ordinal) ?? -1;
        return ix > 0 ? url!.Substring(0, ix) : null;
    }

    /// <summary>
    /// Describes a source so that a remote source, which is a URL that can hold credentials, is reduced to its type
    /// </summary>
    /// <param name="source">The source path</param>
    /// <returns>The path, or the type of the remote source</returns>
    private static string DescribeSource(string source)
        => source.StartsWith("@") || source.Contains("://", StringComparison.Ordinal) ? "(remote source)" : source;
}
