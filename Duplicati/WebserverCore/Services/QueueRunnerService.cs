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

using Duplicati.Library.Interface;
using Duplicati.Library.RestAPI;
using Duplicati.Library.Utility;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Simple queue that will run the given task
/// </summary>
public class QueueRunnerService(
    Connection connection,
    EventPollNotify eventPollNotify,
    INotificationUpdateService notificationUpdateService,
    IProgressStateProviderService progressStateProviderService,
    ILogWriteHandler logWriteHandler) : IQueueRunnerService
{
    private readonly object _lock = new();
    /// <summary>
    /// A thread-safe dictionary to store cached task results.
    /// </summary>
    private readonly Dictionary<long, CachedTaskResult> _taskCache = new();

    /// <summary>
    /// The maximum number of completed task results to keep in memory
    /// </summary>
    private static readonly int MAX_TASK_RESULT_CACHE_SIZE = 100;

    private readonly List<IQueuedTask> _tasks = new();
    private (Task? Task, IQueuedTask? QueuedTask) _current;
    private bool _isPaused;
    private bool _isTerminated;

    public long AddTask(IQueuedTask task)
        => AddTask(task, false);

    public long AddTask(IQueuedTask task, bool skipQueue)
    {
        lock (_lock)
            if (skipQueue)
                _tasks.Insert(0, task);
            else
                _tasks.Add(task);

        eventPollNotify.SignalNewEvent();
        StartNextTask();
        return task.TaskID;
    }

    public bool GetIsActive()
        => _current.Task != null;

    public IQueuedTask? GetCurrentTask()
        => _current.QueuedTask;

    public List<IQueuedTask> GetCurrentTasks()
    {
        lock (_lock)
            return [.. _tasks];
    }

    public void Pause()
    {
        lock (_lock)
            _isPaused = true;
    }

    public void Resume()
    {
        lock (_lock)
            _isPaused = false;

        StartNextTask();
    }

    public void Terminate(bool wait)
    {
        _isTerminated = true;
        if (wait)
        {
            var task = _current.Task;
            if (task != null)
                task.Await();
        }
    }

    private void StartNextTask()
    {
        lock (_lock)
        {
            if (_isTerminated || _isPaused || (_current.Task != null && !_current.Task.IsCompleted))
                return;

            // Clean up completed tasks
            if (_current.Task != null && _current.Task.IsCompleted)
                _current = (null, null);

            if (_tasks.Count == 0)
                return;

            var nextTask = _tasks[0];
            _tasks.RemoveAt(0);
            _current = (Task.Run(() => RunTask(nextTask), CancellationToken.None), nextTask);
        }
    }

    private async Task RunTask(IQueuedTask task)
    {
        var completed = false;
        try
        {
            eventPollNotify.SignalNewEvent();
            task.TaskStarted = DateTime.UtcNow;
            if (task.OnStarting != null)
                await task.OnStarting().ConfigureAwait(false);

            Runner.Run(connection, eventPollNotify, notificationUpdateService, progressStateProviderService, task, true);

            // If the task is completed, don't call OnFinished again
            completed = true;
            AddTaskResult(new CachedTaskResult(task.TaskID, task.BackupID, task.TaskStarted, task.TaskFinished ?? DateTime.Now, null));
            if (task.OnFinished != null)
                await task.OnFinished(null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            connection.LogError(task.BackupID, "Error in worker", ex);
            if (!completed)
            {
                AddTaskResult(new CachedTaskResult(task.TaskID, task.BackupID, task.TaskStarted, task.TaskFinished ?? DateTime.Now, ex));
                if (task.OnFinished != null)
                    await task.OnFinished(ex).ConfigureAwait(false);
            }
        }
        finally
        {
            task.TaskFinished = DateTime.UtcNow;
            lock (_lock)
                _current = (null, null);
            eventPollNotify.SignalNewEvent();
            StartNextTask();
        }
    }

    public IList<Tuple<long, string?>> GetQueueWithIds()
    {
        return (from n in GetCurrentTasks()
                where n.BackupID != null
                select new Tuple<long, string?>(n.TaskID, n.BackupID)).ToList();
    }


    /// <inheritdoc/>
    public CachedTaskResult? GetCachedTaskResults(long taskID)
    {
        lock (_lock)
        {
            _taskCache.TryGetValue(taskID, out var result);
            return result;
        }
    }

    private void AddTaskResult(CachedTaskResult taskResult)
    {
        lock (_lock)
        {
            // If the task result is already in the cache, remove it
            if (_taskCache.TryGetValue(taskResult.TaskID, out var existingResult))
            {
                // If the stored task result has an exception, do not overwrite it
                if (existingResult.Exception != null)
                    return;
            }

            // Add/update the new task result in the cache
            _taskCache[taskResult.TaskID] = taskResult;

            // If the cache size exceeds the maximum, remove the oldest entry
            while (_taskCache.Count >= MAX_TASK_RESULT_CACHE_SIZE)
            {
                var oldestTaskID = _taskCache.Keys.Min();
                _taskCache.Remove(oldestTaskID);
            }
        }
    }

    /// <inheritdoc/>
    public IBasicResults? RunImmediately(IQueuedTask task)
    {
        return Runner.Run(connection, eventPollNotify, notificationUpdateService, progressStateProviderService, task, false);
    }
}