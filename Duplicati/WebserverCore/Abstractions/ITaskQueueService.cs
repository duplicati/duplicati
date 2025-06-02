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

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// Interface for a service that provides access to the task queue.
/// </summary>
public interface ITaskQueueService
{
    /// <summary>
    /// Gets the task queue.
    /// </summary>
    /// <returns>A collection of task state DTOs representing the current task queue.</returns>
    IEnumerable<Dto.GetTaskStateDto> GetTaskQueue();

    /// <summary>
    /// Gets the task information for a specific task ID.
    /// </summary>
    /// <param name="taskid">The ID of the task to retrieve information for.</param>
    /// <returns>A task state DTO containing the information for the specified task.</returns>
    Dto.GetTaskStateDto GetTaskInfo(long taskid);

    /// <summary>
    /// Stops a task with the specified task ID. This will stop the task if it is currently running.
    /// </summary>
    /// <param name="taskid">The ID of the task to stop.</param>
    void StopTask(long taskid);

    /// <summary>
    /// Aborts a task with the specified task ID. This will immediately terminate the task with minimal waiting for it to finish.
    /// </summary>
    /// <param name="taskid">The ID of the task to abort.</param>
    void AbortTask(long taskid);
}