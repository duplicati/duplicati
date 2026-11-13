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

using Duplicati.Server.Serialization.Interface;

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// Service for waiting on tasks in the task queue without polling
/// </summary>
public interface ITaskWaitService
{
    /// <summary>
    /// Waits until a task has finished, or the timeout has passed
    /// </summary>
    /// <param name="taskId">The ID of the task to wait for</param>
    /// <param name="timeout">The longest time to wait</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The state of the task when the wait ended</returns>
    /// <exception cref="Exceptions.NotFoundException">The task is not known</exception>
    Task<Dto.GetTaskStateDto> WaitAsync(long taskId, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current progress of a task
    /// </summary>
    /// <param name="taskId">The ID of the task</param>
    /// <returns>The progress, or <c>null</c> if the task is not the one running</returns>
    IProgressEventData? GetProgress(long taskId);
}
