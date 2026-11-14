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

using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// The task that runs, or will run, a requested operation
/// </summary>
/// <param name="TaskID">The ID of the task</param>
/// <param name="AlreadyQueued">True if a task for the backup was already queued or running, so no new task was added</param>
/// <param name="Operation">The operation the task performs</param>
public sealed record StartedTask(long TaskID, bool AlreadyQueued, DuplicatiOperation Operation);

/// <summary>
/// Service that queues the backup and restore tasks requested through the API
/// </summary>
public interface IBackupTaskService
{
    /// <summary>
    /// Queues a run of a backup. If a task for the backup is already queued or running,
    /// that task is returned and no new task is added.
    /// </summary>
    /// <param name="backup">The backup to run</param>
    /// <param name="skipQueue">True to put the task at the front of the queue</param>
    /// <returns>The task that runs the backup</returns>
    StartedTask StartBackup(IBackup backup, bool skipQueue);

    /// <summary>
    /// Queues a restore from a backup
    /// </summary>
    /// <param name="backup">The backup to restore from</param>
    /// <param name="input">What to restore, and where to</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The task that runs the restore</returns>
    Task<StartedTask> StartRestoreAsync(IBackup backup, Dto.RestoreInputDto input, CancellationToken cancellationToken);
}
