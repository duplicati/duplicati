
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
#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// A cached task result
/// </summary>
/// <param name="TaskID">The task ID</param>
/// <param name="BackupId">The backup ID</param>
/// <param name="TaskStarted">The time the task started</param>
/// <param name="TaskFinished">The time the task finished</param>
/// <param name="Exception">The exception that was thrown</param>
public sealed record CachedTaskResult(long TaskID, string? BackupId, DateTime? TaskStarted, DateTime? TaskFinished, Exception? Exception);

/// <summary>
/// Class to encapsulate a thread that runs a list of queued operations
/// </summary>
/// <typeparam name="Tx">The type to operate on</typeparam>
public interface IQueueRunnerService
{
    /// <summary>
    /// Returns a copy of the current tasks in the queue
    /// </summary>
    /// <returns>A list of queued tasks</returns>
    List<IQueuedTask> GetCurrentTasks();
    /// <summary>
    /// Gets a flag indicating if the queue is currently executing a task
    /// </summary>
    /// <returns>True if the queue is executing a task, false otherwise</returns>
    bool GetIsActive();
    /// <summary>
    /// Returns the currently executing task in the queue
    /// </summary>
    /// <returns>The currently executing task, or null if no task is executing</returns>
    IQueuedTask? GetCurrentTask();

    /// <summary>
    /// Gets the cached task results for a given task ID
    /// </summary>
    /// <param name="taskID">The task ID</param>
    /// <returns>The cached task result</returns>
    CachedTaskResult? GetCachedTaskResults(long taskID);

    /// <summary>
    /// Adds a task to the queue
    /// </summary>
    /// <param name="task">The task to add</param>
    long AddTask(IQueuedTask task);
    /// <summary>
    /// Adds a task to the queue, optionally skipping the queue
    /// </summary>
    /// <param name="task">The task to add</param>
    /// <param name="skipQueue">Whether to skip the queue</param>
    long AddTask(IQueuedTask task, bool skipQueue);
    /// <summary>
    /// Removes a task from the queue
    /// </summary>
    /// <param name="wait">Whether to wait for the task to finish</param>
    void Terminate(bool wait);
    /// <summary>
    /// Resumes processing items in the queue
    /// </summary>
    void Resume();
    /// <summary>
    /// Pauses processing items in the queue
    /// </summary>
    void Pause();

    /// <summary>
    /// Gets the IDs of the tasks in the worker queue
    /// </summary>
    /// <returns>A list of tuples containing the task ID and backup ID</returns>
    IList<Tuple<long, string?>> GetQueueWithIds();

    /// <summary>
    /// Cancels the database lock wait for the currently running task, if its ID matches
    /// <paramref name="taskID"/> and it is still waiting to acquire the database lock.
    /// If the task has already acquired the lock and is executing, this has no effect —
    /// call <see cref="IQueuedTask.AbortAsync"/> or <see cref="IQueuedTask.StopAsync"/>
    /// to interrupt a running task. This method is intended to be called *before*
    /// <see cref="IQueuedTask.AbortAsync"/>/<see cref="IQueuedTask.StopAsync"/> so that
    /// a task blocked on lock acquisition is unblocked rather than left stuck.
    /// </summary>
    /// <param name="taskID">The ID of the currently running task whose lock wait should be cancelled.</param>
    void CancelCurrentTaskLockWait(long taskID);

    /// <summary>
    /// Runs a task immediately, bypassing the queue.
    /// If the task's backup database is already in use by a queued task (e.g. a running backup),
    /// this method throws <see cref="Duplicati.Library.Interface.DatabaseLockedException"/> immediately
    /// rather than waiting or causing a concurrent "database is locked" error.
    /// <para>
    /// Only tasks that expose a database path via <see cref="Duplicati.Library.RestAPI.Runner.GetEffectiveDBPath"/>
    /// (i.e. <see cref="Duplicati.Library.RestAPI.IRunnerData"/>-backed tasks) are protected by the
    /// database lock. In production all tasks are <see cref="Duplicati.Library.RestAPI.IRunnerData"/>
    /// instances; non-runner <see cref="IQueuedTask"/> implementations (test mocks) return a
    /// <c>null</c> path and bypass locking.
    /// </para>
    /// </summary>
    /// <param name="task">The task to run</param>
    /// <exception cref="Duplicati.Library.Interface.DatabaseLockedException">Thrown when the backup database is already in use.</exception>
    Task<IBasicResults?> RunImmediatelyAsync(IQueuedTask task);
}
