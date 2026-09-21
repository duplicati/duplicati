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

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Services;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The task state the server reports through <c>GET /api/v1/task/{id}</c>. The queue runner
/// stamps <c>TaskFinished</c> before it clears the current task, so for a moment a task is
/// both current and finished; a client that stops waiting on <c>TaskFinished</c> - the web
/// UI does - must see the outcome then, not "Running".
/// </summary>
[TestFixture]
[Category("TaskQueue")]
public class TaskQueueServiceTests
{
    private sealed class MockQueueRunnerService : IQueueRunnerService
    {
        public IQueuedTask? CurrentTask { get; set; }
        public List<IQueuedTask> QueuedTasks { get; set; } = new();
        public Dictionary<long, CachedTaskResult> Results { get; } = new();

        public IQueuedTask? GetCurrentTask() => CurrentTask;
        public List<IQueuedTask> GetCurrentTasks() => QueuedTasks;
        public bool GetIsActive() => CurrentTask != null;
        public CachedTaskResult? GetCachedTaskResults(long taskID) => Results.TryGetValue(taskID, out var r) ? r : null;
        public long AddTask(IQueuedTask task) => 0;
        public long AddTask(IQueuedTask task, bool skipQueue) => 0;
        public void Terminate(bool wait) { }
        public void Resume() { }
        public void Pause() { }
        public IList<Tuple<long, string?>> GetQueueWithIds() => new List<Tuple<long, string?>>();
        public void CancelCurrentTaskLockWait(long taskID) { }
        public Task<IBasicResults?> RunImmediatelyAsync(IQueuedTask task) => Task.FromResult<IBasicResults?>(null);
    }

    private sealed class MockQueuedTask : IQueuedTask
    {
        public long TaskID { get; set; }
        public string? BackupID { get; set; }
        public DuplicatiOperation Operation { get; set; }
        public Func<Task>? OnStarting { get; set; }
        public Func<Exception?, Task>? OnFinished { get; set; }
        public DateTime? TaskStarted { get; set; }
        public DateTime? TaskFinished { get; set; }

        public Task UpdateThrottleSpeedsAsync(string? uploadSpeed, string? downloadSpeed) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task AbortAsync() => Task.CompletedTask;
        public Task PauseAsync(bool alsoTransfers) => Task.CompletedTask;
        public Task ResumeAsync() => Task.CompletedTask;
    }

    private static readonly DateTime Started = new(2026, 9, 13, 4, 14, 47, DateTimeKind.Utc);
    private static readonly DateTime Finished = Started.AddMilliseconds(342);

    /// <summary>
    /// The window the runner leaves open: the task is still the current one, its TaskFinished
    /// is set, and its result is already cached. The state must carry the outcome, the way
    /// GetTaskQueue reports the same task, so a client that stops at TaskFinished sees a
    /// failure and its message.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void GetTaskInfo_CurrentTaskThatHasFinished_ReportsTheCachedOutcome(bool failed)
    {
        var runner = new MockQueueRunnerService();
        var task = new MockQueuedTask { TaskID = 7, BackupID = "1", TaskStarted = Started, TaskFinished = Finished };
        runner.CurrentTask = task;
        runner.Results[7] = new CachedTaskResult(7, "1", Started, Finished, failed ? new InvalidOperationException("The passphrase is wrong") : null);

        var state = new TaskQueueService(runner).GetTaskInfo(7);

        Assert.That(state.Status, Is.EqualTo(failed ? "Failed" : "Completed"), "A finished task must not be reported as running.");
        Assert.That(state.TaskStarted, Is.EqualTo(Started));
        Assert.That(state.TaskFinished, Is.EqualTo(Finished));
        Assert.That(state.ErrorMessage, Is.EqualTo(failed ? "The passphrase is wrong" : null));
        Assert.That(state.Exception, failed ? Does.Contain("InvalidOperationException") : Is.Null);
    }

    [Test]
    public void GetTaskInfo_CurrentTaskStillRunning_ReportsRunning()
    {
        var runner = new MockQueueRunnerService();
        runner.CurrentTask = new MockQueuedTask { TaskID = 7, BackupID = "1", TaskStarted = Started };

        var state = new TaskQueueService(runner).GetTaskInfo(7);

        Assert.That(state.Status, Is.EqualTo("Running"));
        Assert.That(state.TaskStarted, Is.EqualTo(Started));
        Assert.That(state.TaskFinished, Is.Null);
        Assert.That(state.ErrorMessage, Is.Null);
    }

    [Test]
    public void GetTaskInfo_QueuedTask_ReportsWaiting()
    {
        var runner = new MockQueueRunnerService();
        runner.QueuedTasks.Add(new MockQueuedTask { TaskID = 7, BackupID = "1" });

        var state = new TaskQueueService(runner).GetTaskInfo(7);

        Assert.That(state.Status, Is.EqualTo("Waiting"));
        Assert.That(state.TaskStarted, Is.Null);
        Assert.That(state.TaskFinished, Is.Null);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void GetTaskInfo_FinishedTaskNoLongerCurrent_ReportsFromTheCache(bool failed)
    {
        var runner = new MockQueueRunnerService();
        runner.Results[7] = new CachedTaskResult(7, "1", Started, Finished, failed ? new InvalidOperationException("The passphrase is wrong") : null);

        var state = new TaskQueueService(runner).GetTaskInfo(7);

        Assert.That(state.Status, Is.EqualTo(failed ? "Failed" : "Completed"));
        Assert.That(state.TaskFinished, Is.EqualTo(Finished));
        Assert.That(state.ErrorMessage, Is.EqualTo(failed ? "The passphrase is wrong" : null));
    }
}
