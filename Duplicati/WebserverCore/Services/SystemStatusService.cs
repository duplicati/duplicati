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
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Creates system status snapshots from the resource sampler, the task queue and the progress of the running task.
/// </summary>
public class SystemStatusService(
    ISystemResourceSampler systemResourceSampler,
    IProgressStateProviderService progressStateProviderService,
    IQueueRunnerService queueRunnerService,
    ITaskQueueService taskQueueService) : ISystemStatusService
{
    /// <inheritdoc />
    public TimeSpan Interval => Library.Main.ProgressReporting.Interval;

    /// <inheritdoc />
    public SystemStatusDto GetSystemStatus()
    {
        // The progress generator is not cleared when a task completes, so only report it for the running task
        var currentTask = queueRunnerService.GetCurrentTask();
        var progress = currentTask == null ? null : progressStateProviderService.GenerateProgressState?.Invoke();
        if (progress != null && progress.TaskID != currentTask?.TaskID)
            progress = null;

        return new SystemStatusDto(
            Timestamp: DateTime.UtcNow,
            IntervalMs: (int)Interval.TotalMilliseconds,
            Machine: systemResourceSampler.SampleMachine(),
            Processes: systemResourceSampler.SampleProcesses(),
            Queue: taskQueueService.GetTaskQueueDetails(),
            Progress: progress
        );
    }
}
