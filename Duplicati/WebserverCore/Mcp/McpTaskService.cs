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
using Duplicati.Library.IO;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Services;
using ModelContextProtocol;

namespace Duplicati.WebserverCore.Mcp;

/// <summary>
/// Builds the description of a task that the MCP tools return
/// </summary>
/// <param name="connection">The server database connection</param>
/// <param name="taskQueueService">The service reporting the state of a task</param>
/// <param name="queueRunnerService">The task queue</param>
/// <param name="taskWaitService">The source of task progress</param>
/// <param name="liveControls">The pause state</param>
public class McpTaskService(Connection connection, ITaskQueueService taskQueueService, IQueueRunnerService queueRunnerService, ITaskWaitService taskWaitService, ILiveControls liveControls)
{
    /// <summary>
    /// Describes a task
    /// </summary>
    /// <param name="taskId">The ID of the task</param>
    /// <param name="alreadyQueued">True if the task was found queued by the request that returns this description</param>
    /// <returns>The description</returns>
    /// <exception cref="Exceptions.NotFoundException">The task is not known</exception>
    public async Task<McpTaskInfo> DescribeAsync(long taskId, bool alreadyQueued)
    {
        // Find the task before reading the state, so a task that finishes in between is found in the result cache
        var task = FindQueuedTask(taskId);

        var state = taskQueueService.GetTaskInfo(taskId);
        var finished = TaskWaitService.IsFinished(state);
        var cached = finished ? queueRunnerService.GetCachedTaskResults(taskId) : null;
        var summary = cached?.Summary;
        var backupId = task?.BackupID ?? cached?.BackupId;

        // A task that ran to the end can still have failed, which it reports in its results
        var status = state.Status == "Completed" && state.ErrorMessage != null ? "Failed" : state.Status;

        var progress = state.Status == "Running" ? taskWaitService.GetProgress(taskId) : null;

        return new McpTaskInfo(
            taskId,
            status,
            finished,
            backupId,
            task != null ? McpHelpers.FormatOperation(task.Operation.ToString()) : summary?.Backup != null ? "Backup" : summary?.Restore != null ? "Restore" : null,
            alreadyQueued,
            liveControls.IsPaused,
            state.ErrorMessage,
            progress == null
                ? null
                : new McpProgress(progress.Phase, progress.OverallProgress, progress.ProcessedFileCount, progress.TotalFileCount, progress.ProcessedFileSize, progress.TotalFileSize, progress.StillCounting),
            summary,
            // A backup that ended in an error has no version that is known to hold what it captured
            finished && backupId != null && summary is { Backup.VersionTime: not null, ParsedResult: ParsedResultType.Success or ParsedResultType.Warning }
                ? await GetRestorePointAsync(backupId, summary.Backup.VersionTime.Value).ConfigureAwait(false)
                : null);
    }

    /// <summary>
    /// Requires that a task belongs to a backup the caller can use.
    /// A task of another backup is reported as not existing.
    /// </summary>
    /// <param name="taskId">The ID of the task</param>
    /// <param name="scope">What the caller is allowed to do</param>
    /// <exception cref="Exceptions.NotFoundException">The task is outside the scope of the caller</exception>
    public void RequireAccess(long taskId, McpScope scope)
    {
        var task = FindQueuedTask(taskId);
        var cached = queueRunnerService.GetCachedTaskResults(taskId);

        // A task that is not known has no backup, so a caller limited to some backups is refused here.
        // Leaving it to the state lookup that follows would let through a task that is queued in between.
        if (!scope.CanAccessBackup(task?.BackupID ?? cached?.BackupId))
            throw new Exceptions.NotFoundException("No such task found");
    }

    /// <summary>
    /// Finds a task that is waiting or running
    /// </summary>
    /// <param name="taskId">The ID of the task</param>
    /// <returns>The task, or <c>null</c> if it is not in the queue and not running</returns>
    private IQueuedTask? FindQueuedTask(long taskId)
    {
        // A task only moves forward: from the queue, to running, to the result cache.
        // Reading in that order finds a task that moves on between the reads.
        var queued = queueRunnerService.GetCurrentTasks();
        var current = queueRunnerService.GetCurrentTask();

        return current != null && current.TaskID == taskId
            ? current
            : queued.FirstOrDefault(x => x.TaskID == taskId);
    }

    /// <summary>
    /// Gets the version of a backup that holds what a finished backup task captured
    /// </summary>
    /// <param name="backupId">The ID of the backup</param>
    /// <param name="versionTime">The time of the version, as the task recorded it when it finished, in UTC</param>
    /// <returns>The version, or <c>null</c> if it no longer exists</returns>
    private async Task<McpRestorePoint?> GetRestorePointAsync(string backupId, DateTime versionTime)
    {
        var backup = connection.GetBackup(backupId);
        if (backup == null)
            return null;

        var time = McpHelpers.FormatTime(versionTime);
        try
        {
            // The version is gone if a later run has deleted it, as the retention settings of a backup can ask for
            var version = (await ListFilesetsAsync(backup).ConfigureAwait(false)).FirstOrDefault(x => McpHelpers.FormatTime(x.Time) == time);
            return version == null ? null : new McpRestorePoint(time, version.Version);
        }
        catch (DatabaseLockedException)
        {
            // The versions cannot be listed while another task uses the backup, so the number of the version is left out
            return new McpRestorePoint(time, null);
        }
        catch (UserInformationException)
        {
            // The local database of the backup is gone
            return null;
        }
    }

    /// <summary>
    /// Finds the version that a time given by a caller selects, which is the newest version
    /// that is not after the time, as it is for a restore
    /// </summary>
    /// <param name="backup">The backup</param>
    /// <param name="time">The time given by the caller, or nothing for the newest version</param>
    /// <returns>The time and number of the version, or <c>null</c> if no time was given</returns>
    /// <exception cref="McpException">The time is not understood, or the backup has no version that old</exception>
    public async Task<(DateTime Time, long Version)?> ResolveVersionAsync(Server.Serialization.Interface.IBackup backup, string? time)
    {
        if (string.IsNullOrWhiteSpace(time))
            return null;

        var limit = McpHelpers.ParseTime(time);
        return FindVersion(await ListFilesetsAsync(backup).ConfigureAwait(false), limit, time);
    }

    /// <summary>
    /// Finds the version to look in for a caller that lists or searches the contents of a backup,
    /// which is the newest version when no time is given
    /// </summary>
    /// <param name="backup">The backup</param>
    /// <param name="time">The time given by the caller, or nothing for the newest version</param>
    /// <returns>The time and number of the version</returns>
    /// <exception cref="McpException">The backup has no versions, the time is not understood, or the backup has no version that old</exception>
    public async Task<(DateTime Time, long Version)> ResolveExistingVersionAsync(Server.Serialization.Interface.IBackup backup, string? time)
    {
        DateTime? limit = string.IsNullOrWhiteSpace(time) ? null : McpHelpers.ParseTime(time);

        // Looking into a backup without versions fails in the engine, which the server
        // then records as an error on the backup and shows to the user as a notification
        var versions = await ListFilesetsAsync(backup).ConfigureAwait(false);
        if (versions.Count == 0)
            throw new McpException($"The backup \"{backup.Name}\" has no versions yet. Run it with run_backup first.");

        return limit == null
            ? (versions[0].Time, versions[0].Version)
            : FindVersion(versions, limit.Value, time!);
    }

    /// <summary>
    /// Finds the newest version that is not after a time
    /// </summary>
    /// <param name="versions">The versions, newest first</param>
    /// <param name="limit">The time, in UTC</param>
    /// <param name="time">The time as the caller gave it, for the error message</param>
    /// <returns>The time and number of the version</returns>
    /// <exception cref="McpException">There is no version that old</exception>
    private static (DateTime Time, long Version) FindVersion(List<IListFilesetResultFileset> versions, DateTime limit, string time)
    {
        var version = versions.FirstOrDefault(x => x.Time.ToUniversalTime() <= limit)
            ?? throw new McpException($"The backup has no version at or before {time}. Use list_versions to find the times of the versions.");

        return (version.Time, version.Version);
    }

    /// <summary>
    /// Lists the versions of a backup from its local database
    /// </summary>
    /// <param name="backup">The backup</param>
    /// <returns>The versions, newest first</returns>
    public async Task<List<McpVersion>> ListVersionsAsync(Server.Serialization.Interface.IBackup backup)
        => (await ListFilesetsAsync(backup).ConfigureAwait(false))
            .Select(x => new McpVersion(x.Version, McpHelpers.FormatTime(x.Time), x.IsFullBackup ?? true, x.FileCount, x.FileSizes))
            .ToList();

    /// <summary>
    /// Reads the versions of a backup from its local database
    /// </summary>
    /// <param name="backup">The backup</param>
    /// <returns>The versions, newest first; none if the backup has no local database</returns>
    private async Task<List<IListFilesetResultFileset>> ListFilesetsAsync(Server.Serialization.Interface.IBackup backup)
    {
        // Without the local database the engine lists the remote storage instead. For a backup that has
        // not run yet that fails, and the server records the failure as an error on the backup and
        // shows it to the user as a notification, all for a question that has the answer "none".
        if (string.IsNullOrWhiteSpace(backup.DBPath) || !File.Exists(backup.DBPath))
            return [];

        // Retries would hold up the request
        var extra = new Dictionary<string, string?> { ["number-of-retries"] = "0" };
        var r = await queueRunnerService.RunImmediatelyAsync(Runner.CreateListFilesetsTask(backup, extra)).ConfigureAwait(false) as IListFilesetResults;

        return (r?.Filesets ?? [])
            .OrderByDescending(x => x.Time)
            .ToList();
    }
}
