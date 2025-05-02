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
using Duplicati.WebserverCore.Dto;

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
/// Interface for the task result cache service
/// </summary>
public interface ITaskCacheService
{
    /// <summary>
    /// Gets the cached task results for a given task ID
    /// </summary>
    /// <param name="taskID">The task ID</param>
    /// <returns>The cached task result</returns>
    CachedTaskResult? GetCachedTaskResults(long taskID);
    /// <summary>
    /// Adds a task result to the cache
    /// </summary>
    /// <param name="taskResult">The task result to add</param>
    void AddTaskResult(CachedTaskResult taskResult);
}