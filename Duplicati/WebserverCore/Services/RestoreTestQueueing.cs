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

using Duplicati.Library.Logging;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Queues restore tests for backups named by the id they are reported with, for the remote control
/// command. The console only knows the reported id, which the user can set freely, so the backups are
/// matched on the id they report rather than on the database id.
/// </summary>
public static class RestoreTestQueueing
{
    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType(typeof(RestoreTestQueueing));

    /// <summary>
    /// Queues a restore test for each backup reported with one of the ids, unless one is already queued or running for it
    /// </summary>
    /// <param name="connection">The database connection</param>
    /// <param name="queueRunnerService">The task queue</param>
    /// <param name="reportedBackupIds">The backup ids as they appear in the backup reports</param>
    /// <returns>The reported ids a restore test is queued or running for</returns>
    public static IReadOnlyList<string> QueueForReportedBackupIds(Connection connection, IQueueRunnerService queueRunnerService, IEnumerable<string> reportedBackupIds)
    {
        var requested = reportedBackupIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.Ordinal);
        if (requested.Count == 0)
            return [];

        // The running task has left the queue, so it is added to the queued ones
        var pending = queueRunnerService.GetCurrentTasks()
            .Append(queueRunnerService.GetCurrentTask())
            .OfType<IQueuedTask>()
            .Where(x => x.Operation == DuplicatiOperation.RestoreTest && x.BackupID != null)
            .Select(x => x.BackupID!)
            .ToHashSet();

        var queued = new HashSet<string>(StringComparer.Ordinal);
        foreach (var listed in connection.Backups.Where(x => !x.IsTemporary))
        {
            var backup = connection.GetBackup(listed.ID);
            if (backup == null)
                continue;

            var reportedId = Runner.GetReportedBackupId(connection, backup);
            if (!requested.Contains(reportedId))
                continue;

            if (!pending.Contains(backup.ID))
                queueRunnerService.AddTask(Runner.CreateTask(DuplicatiOperation.RestoreTest, backup));

            queued.Add(reportedId);
        }

        foreach (var unknown in requested.Except(queued))
            Log.WriteMessage(LogMessageType.Warning, LOGTAG, "RestoreTestBackupNotFound", "Ignoring restore test request for unknown backup: {0}", unknown);

        return queued.ToList();
    }
}
