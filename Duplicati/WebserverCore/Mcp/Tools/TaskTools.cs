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

using System.ComponentModel;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Duplicati.WebserverCore.Mcp.Tools;

/// <summary>
/// MCP tools for following and stopping tasks
/// </summary>
/// <param name="taskQueueService">The task queue</param>
/// <param name="taskWaitService">The service waiting on tasks</param>
/// <param name="mcpTaskService">The service describing tasks</param>
/// <param name="scope">What the caller is allowed to do</param>
/// <param name="applicationLifetime">The source of the signal that the server is stopping</param>
/// <remarks>
/// The default values on the tool methods are what marks an argument as optional in the tool schema,
/// so an agent can leave it out. They are part of the MCP interface, not a convenience for callers in code.
/// </remarks>
[McpServerToolType]
public class TaskTools(ITaskQueueService taskQueueService, ITaskWaitService taskWaitService, McpTaskService mcpTaskService, McpScope scope, IHostApplicationLifetime applicationLifetime)
{
    /// <summary>
    /// The wait used when the caller does not give one, in seconds.
    /// It is below the one minute after which some clients give up on a tool call.
    /// </summary>
    public const int DefaultWaitSeconds = 45;

    /// <summary>
    /// The longest wait a single call can ask for, in seconds
    /// </summary>
    public const int MaxWaitSeconds = 300;

    /// <summary>
    /// The interval between progress notifications during a wait
    /// </summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(3);

    [McpServerTool(Name = "get_task", Title = "Get task state", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Reports the state of a task without waiting: Waiting, Running, Completed or Failed, with progress while it runs and the result when it has finished. Task IDs are forgotten when the server restarts.")]
    public Task<McpTaskInfo> GetTask(
        [Description("The task ID returned by run_backup or restore")] long taskId)
        => McpHelpers.RunAsync("get_task", $"task {taskId}", scope, McpTokenLevel.Read, () =>
        {
            mcpTaskService.RequireAccess(taskId, scope);
            return mcpTaskService.DescribeAsync(taskId, false);
        });

    [McpServerTool(Name = "wait_for_task", Title = "Wait for a task", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Waits until a task has finished or the timeout has passed, whichever is first, and reports its state. If finished is false the task is still waiting or running: call wait_for_task again. A backup or restore can take from seconds to hours. When finished, check status and result.parsedResult: Success and Warning mean the data is safe, Error and Fatal mean it is not. If serverPaused is true a waiting task will not start until someone resumes the server.")]
    public Task<McpTaskInfo> WaitForTask(
        [Description("The task ID returned by run_backup or restore")] long taskId,
        [Description("The longest time to wait in seconds, at most 300. Leave out for 45 seconds.")] int? timeoutSeconds = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
        => McpHelpers.RunAsync("wait_for_task", $"task {taskId}", scope, McpTokenLevel.Read, async () =>
        {
            mcpTaskService.RequireAccess(taskId, scope);
            var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(timeoutSeconds ?? DefaultWaitSeconds, 0, MaxWaitSeconds));

            // A wait that goes on while the server stops would hold up the shutdown until the wait times out
            using var stopWaiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, applicationLifetime.ApplicationStopping);
            try
            {
                while (true)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    var state = await taskWaitService.WaitAsync(taskId, remaining < ProgressInterval ? remaining : ProgressInterval, stopWaiting.Token).ConfigureAwait(false);
                    if (TaskWaitService.IsFinished(state) || DateTime.UtcNow >= deadline)
                        break;

                    var current = taskWaitService.GetProgress(taskId);
                    if (current != null)
                        progress?.Report(new ProgressNotificationValue { Progress = current.OverallProgress, Total = 1, Message = current.Phase });
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The server is stopping: answer with the state as it is, as when the wait times out
            }

            return await mcpTaskService.DescribeAsync(taskId, false).ConfigureAwait(false);
        });

    [McpServerTool(Name = "stop_task", Title = "Stop a task", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Stops a running task. A stopped backup keeps what it has uploaded as a partial version; a stopped restore leaves the files it has not reached untouched. Returns the state of the task, which may still be Running while it winds down: use wait_for_task. A task that is still Waiting cannot be stopped: the call fails and changes nothing, and can be repeated once the task is Running.")]
    public Task<McpTaskInfo> StopTask(
        [Description("The task ID to stop")] long taskId,
        [Description("False to let the task finish the file it is working on and end cleanly; true to abort it as fast as possible. Leave out for false.")] bool abort = false)
        => McpHelpers.RunAsync("stop_task", $"task {taskId}", scope, McpTokenLevel.Backup, async () =>
        {
            mcpTaskService.RequireAccess(taskId, scope);

            // A request to stop a task that has not started is dropped by the task, so it is refused
            // here; answering with the state of the task would read as if the task had been stopped.
            // A task is reported as running a moment before it has started, which the start time shows.
            var state = taskQueueService.GetTaskInfo(taskId);
            if (!TaskWaitService.IsFinished(state) && state.TaskStarted == null)
                throw new McpException("The task has not started, and a waiting task cannot be stopped. It starts when the tasks ahead of it have finished and the server is not paused. Call stop_task again when get_task reports it as Running.");

            if (!TaskWaitService.IsFinished(state))
            {
                try
                {
                    if (abort)
                        await taskQueueService.AbortTaskAsync(taskId).ConfigureAwait(false);
                    else
                        await taskQueueService.StopTaskAsync(taskId).ConfigureAwait(false);
                }
                catch (Exceptions.NotFoundException)
                {
                    // The task finished in between, which the state returned below says
                }
            }

            return await mcpTaskService.DescribeAsync(taskId, false).ConfigureAwait(false);
        });
}
