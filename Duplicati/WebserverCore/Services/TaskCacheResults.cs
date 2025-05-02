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
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Service for caching task results.
/// </summary>
public class TaskCacheService : ITaskCacheService
{
    /// <summary>
    /// A thread-safe dictionary to store cached task results.
    /// </summary>
    private readonly Dictionary<long, CachedTaskResult> _taskCache = new();

    /// <summary>
    /// The maximum number of completed task results to keep in memory
    /// </summary>
    private static readonly int MAX_TASK_RESULT_CACHE_SIZE = 100;

    /// <inheritdoc/>
    public CachedTaskResult? GetCachedTaskResults(long taskID)
    {
        lock (_taskCache)
        {
            _taskCache.TryGetValue(taskID, out var result);
            return result;
        }
    }

    /// <inheritdoc/>
    public void AddTaskResult(CachedTaskResult taskResult)
    {
        lock (_taskCache)
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

}
