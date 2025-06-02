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
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Exceptions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Provides functionality to manage and retrieve information about tasks in the task queue.
/// </summary>
/// <param name="queueRunnerService">The service responsible for running and managing tasks in the queue.</param>
public class TaskQueueService(IQueueRunnerService queueRunnerService) : ITaskQueueService
{

    /// <inheritdoc/>
    public GetTaskStateDto GetTaskInfo(long taskid)
    {
        var task = queueRunnerService.GetCurrentTask();
        var tasks = queueRunnerService.GetCurrentTasks();

        if (task != null && task.TaskID == taskid)
            return new Dto.GetTaskStateDto("Running", taskid, task.TaskStarted, task.TaskFinished);

        if (tasks.FirstOrDefault(x => x.TaskID == taskid) == null)
        {
            var res = queueRunnerService.GetCachedTaskResults(taskid);
            if (res == null)
                throw new NotFoundException("No such task found");

            return new GetTaskStateDto(
                Status: res.Exception == null ? "Completed" : "Failed",
                ID: taskid,
                TaskStarted: res?.TaskStarted,
                TaskFinished: res?.TaskFinished,
                ErrorMessage: res?.Exception?.Message,
                Exception: res?.Exception?.ToString()
            );
        }

        return new GetTaskStateDto("Waiting", taskid, null, null);
    }

    /// <inheritdoc/>
    public IEnumerable<GetTaskStateDto> GetTaskQueue()
    {
        var cur = queueRunnerService.GetCurrentTask();
        var n = queueRunnerService.GetCurrentTasks();

        if (cur != null)
            n.Insert(0, cur);

        return n.Select(x =>
        {
            var res = queueRunnerService.GetCachedTaskResults(x.TaskID);

            return new GetTaskStateDto(
                Status: x.TaskFinished == null ? "Running" : (res?.Exception == null ? "Completed" : "Failed"),
                ID: x.TaskID,
                TaskStarted: x.TaskStarted,
                TaskFinished: x.TaskFinished,
                ErrorMessage: res?.Exception?.Message,
                Exception: res?.Exception?.ToString()
            );
        });
    }

    /// <inheritdoc/>
    public void StopTask(long taskid)
        => StopOrAbortTask(taskid, false);

    /// <inheritdoc/>
    public void AbortTask(long taskid)
        => StopOrAbortTask(taskid, true);

    /// <summary>
    /// Stops or aborts a task based on the provided task ID.
    /// </summary>
    /// <param name="taskid">The ID of the task to stop or abort.</param>
    /// <param name="abort">If true, the task will be aborted; otherwise, it will be stopped gracefully.</param>
    private void StopOrAbortTask(long taskid, bool abort)
    {
        var task = queueRunnerService.GetCurrentTask();
        var tasks = queueRunnerService.GetCurrentTasks();

        if (task != null)
            tasks.Insert(0, task);

        task = tasks.FirstOrDefault(x => x.TaskID == taskid);
        if (task == null)
            throw new NotFoundException("No such task found");

        if (abort)
            task.Abort();
        else
            task.Stop();
    }
}