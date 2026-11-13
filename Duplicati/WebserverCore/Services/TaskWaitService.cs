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
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Waits on tasks in the task queue by listening for the task completed event
/// </summary>
/// <param name="eventPollNotify">The source of the task completed event</param>
/// <param name="taskQueueService">The service reporting the state of a task</param>
/// <param name="progressStateProviderService">The source of the progress of the running task</param>
public class TaskWaitService(EventPollNotify eventPollNotify, ITaskQueueService taskQueueService, IProgressStateProviderService progressStateProviderService) : ITaskWaitService
{
    /// <summary>
    /// Checks if a task state is a final state
    /// </summary>
    /// <param name="state">The state to check</param>
    /// <returns>True if the task has finished</returns>
    public static bool IsFinished(GetTaskStateDto state)
        => state.Status is "Completed" or "Failed";

    /// <inheritdoc/>
    public async Task<GetTaskStateDto> WaitAsync(long taskId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnTaskCompleted(object? sender, long id)
        {
            if (id == taskId)
                completed.TrySetResult();
        }

        // Subscribe before reading the state, so a task that finishes between the two is not missed
        eventPollNotify.TaskCompleted += OnTaskCompleted;
        try
        {
            var state = taskQueueService.GetTaskInfo(taskId);
            if (IsFinished(state) || timeout <= TimeSpan.Zero)
                return state;

            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = Task.Delay(timeout, delayCts.Token);
            await Task.WhenAny(completed.Task, delay).ConfigureAwait(false);
            delayCts.Cancel();
            cancellationToken.ThrowIfCancellationRequested();

            return taskQueueService.GetTaskInfo(taskId);
        }
        finally
        {
            eventPollNotify.TaskCompleted -= OnTaskCompleted;
        }
    }

    /// <inheritdoc/>
    public IProgressEventData? GetProgress(long taskId)
    {
        var progress = progressStateProviderService.GenerateProgressState?.Invoke();
        return progress != null && progress.TaskID == taskId ? progress : null;
    }
}
