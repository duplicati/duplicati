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

using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Endpoints.Shared;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Queues the backup and restore tasks requested through the API
/// </summary>
/// <param name="connection">The server database connection</param>
/// <param name="applicationSettings">The application settings</param>
/// <param name="queueRunnerService">The queue the tasks are added to</param>
public class BackupTaskService(Connection connection, IApplicationSettings applicationSettings, IQueueRunnerService queueRunnerService) : IBackupTaskService
{
    /// <inheritdoc/>
    public StartedTask StartBackup(IBackup backup, bool skipQueue)
    {
        // Already running
        var t = queueRunnerService.GetCurrentTask();
        if (t?.BackupID != null && backup.ID == t.BackupID)
            return new StartedTask(t.TaskID, true, t.Operation);

        t = queueRunnerService.GetCurrentTasks().FirstOrDefault(x => x.BackupID == backup.ID);
        if (t != null)
            return new StartedTask(t.TaskID, true, t.Operation);

        return new StartedTask(
            queueRunnerService.AddTask(Runner.CreateTask(DuplicatiOperation.BackupOrSync, backup), skipQueue),
            false,
            DuplicatiOperation.BackupOrSync);
    }

    /// <inheritdoc/>
    public async Task<StartedTask> StartRestoreAsync(IBackup backup, RestoreInputDto input, CancellationToken cancellationToken)
    {
        var restorepath = input.restore_path;
        if (restorepath != null && restorepath.StartsWith("@"))
        {
            var res = await SharedRemoteOperation.ExpandUrlAsync(connection, applicationSettings, restorepath.Substring(1), backup.ID, input.connection_string_id ?? -1, input.source_prefix, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(res.Url))
                restorepath = "@" + res.Url;
        }

        return new StartedTask(
            queueRunnerService.AddTask(Runner.CreateRestoreTask(
                backup,
                input.paths ?? [],
                Library.Utility.Timeparser.ParseTimeInterval(input.time, DateTime.Now),
                restorepath,
                input.overwrite,
                input.permissions,
                input.skip_metadata,
                string.IsNullOrWhiteSpace(input.passphrase) ? null : input.passphrase,
                input.options)),
            false,
            DuplicatiOperation.Restore);
    }
}
