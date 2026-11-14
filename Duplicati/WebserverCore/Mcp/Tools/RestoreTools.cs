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
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Duplicati.WebserverCore.Mcp.Tools;

/// <summary>
/// MCP tools for restoring from a backup
/// </summary>
/// <param name="connection">The server database connection</param>
/// <param name="backupTaskService">The service queuing restores</param>
/// <param name="mcpTaskService">The service describing tasks</param>
/// <param name="scope">What the caller is allowed to do</param>
/// <remarks>
/// The default values on the tool methods are what marks an argument as optional in the tool schema,
/// so an agent can leave it out. They are part of the MCP interface, not a convenience for callers in code.
/// </remarks>
[McpServerToolType]
public class RestoreTools(Connection connection, IBackupTaskService backupTaskService, McpTaskService mcpTaskService, McpScope scope)
{
    [McpServerTool(Name = "restore", Title = "Restore from a backup", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Queues a restore of files from a backup and returns at once with the task ID. Use wait_for_task to wait for it to finish. A restore writes the files of the chosen version back. It is not a rollback: files created after that version are left in place, and only what the backup's source paths cover can be restored. With overwrite false, an existing file with different content is kept and the restored copy gets a timestamp added to its name.")]
    public Task<McpTaskInfo> Restore(
        [Description("The ID of the backup from list_backups, or its exact name")] string backup,
        [Description("True to replace existing files with the version from the backup; false to keep them and write the restored copies next to them")] bool overwrite,
        [Description("The time of the version to restore, as given by the restore point of a finished backup or by list_versions; a time between two versions selects the older one. Leave out for the newest version.")] string? time = null,
        [Description("The files and folders to restore, as full paths; a folder path ends with the path separator. Wildcards * and ? are allowed. Leave out to restore everything.")] string[]? paths = null,
        [Description("The folder to restore into. Leave out to restore every file to the location it was backed up from.")] string? restorePath = null,
        [Description("True to also restore file permissions. Leave out to use the setting of the backup.")] bool? restorePermissions = null,
        CancellationToken cancellationToken = default)
        => McpHelpers.RunAsync("restore", backup, scope, McpTokenLevel.Restore, async () =>
        {
            var bk = McpHelpers.ResolveBackup(connection, scope, backup);
            if (!string.IsNullOrWhiteSpace(restorePath) && restorePath.TrimStart().StartsWith('@'))
                throw new McpException("Restoring to a remote destination is not available through this tool. Give a local folder, or leave restorePath out.");

            // A caller limited to some folders restores to the path that was checked, which has no links in it
            restorePath = scope.ResolveRestorePath(restorePath);

            // A restore that is given a time older than every version takes the newest version instead,
            // so the version is found here, and the restore is given the exact time of it
            var version = await mcpTaskService.ResolveVersionAsync(bk, time).ConfigureAwait(false);

            var started = await backupTaskService.StartRestoreAsync(
                bk,
                new RestoreInputDto(
                    paths: paths,
                    passphrase: null,
                    time: version == null ? string.Empty : McpHelpers.FormatTime(version.Value.Time),
                    restore_path: string.IsNullOrWhiteSpace(restorePath) ? null : restorePath,
                    overwrite: overwrite,
                    permissions: restorePermissions,
                    skip_metadata: null,
                    connection_string_id: null,
                    source_prefix: null,
                    options: null),
                cancellationToken).ConfigureAwait(false);

            return await mcpTaskService.DescribeAsync(started.TaskID, false).ConfigureAwait(false);
        });
}
