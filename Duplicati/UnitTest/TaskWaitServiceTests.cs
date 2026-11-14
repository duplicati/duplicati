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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Server;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Exceptions;
using Duplicati.WebserverCore.Services;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Waiting on a task by its completion event, which is what the MCP tool wait_for_task rests on
/// </summary>
[TestFixture]
[Category("Mcp")]
public class TaskWaitServiceTests
{
    /// <summary>
    /// The ID of the task the tests wait for
    /// </summary>
    private const long TaskId = 42;

    /// <summary>
    /// A wait that is expected to end long before it times out
    /// </summary>
    private static readonly TimeSpan LongTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The time a wait that should end early is given to do so
    /// </summary>
    private static readonly TimeSpan Prompt = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Reports the state a test has set for the task
    /// </summary>
    private sealed class FakeTaskQueueService : ITaskQueueService
    {
        /// <summary>
        /// The state of the task, or <c>null</c> if the task is not known
        /// </summary>
        public volatile string? Status = "Running";

        /// <summary>
        /// The number of times the state has been read
        /// </summary>
        public int Reads;

        public GetTaskStateDto GetTaskInfo(long taskid)
        {
            Interlocked.Increment(ref Reads);
            return new GetTaskStateDto(Status ?? throw new NotFoundException("No such task found"), taskid, null, null);
        }

        public IEnumerable<GetTaskStateDto> GetTaskQueue() => [];
        public IEnumerable<SystemStatusQueueItemDto> GetTaskQueueDetails() => [];
        public Task StopTaskAsync(long taskid) => Task.CompletedTask;
        public Task AbortTaskAsync(long taskid) => Task.CompletedTask;
    }

    /// <summary>
    /// Holds the source of the progress of the running task
    /// </summary>
    private sealed class FakeProgressStateProvider : IProgressStateProviderService
    {
        public Func<IProgressEventData>? GenerateProgressState { get; set; }
    }

    private EventPollNotify _events = null!;
    private FakeTaskQueueService _queue = null!;
    private FakeProgressStateProvider _progress = null!;
    private TaskWaitService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _events = new EventPollNotify();
        _queue = new FakeTaskQueueService();
        _progress = new FakeProgressStateProvider();
        _service = new TaskWaitService(_events, _queue, _progress);
    }

    /// <summary>
    /// Counts the handlers listening for completed tasks
    /// </summary>
    /// <returns>The number of handlers</returns>
    private int CountCompletionHandlers()
        => ((Delegate?)typeof(EventPollNotify)
            .GetField(nameof(EventPollNotify.TaskCompleted), BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_events))?.GetInvocationList().Length ?? 0;

    /// <summary>
    /// Waits until a wait has subscribed to the completion event and read the state
    /// </summary>
    /// <returns>An awaitable task</returns>
    private async Task UntilWaitingAsync()
    {
        using var giveUp = new CancellationTokenSource(Prompt);
        while (CountCompletionHandlers() == 0 || Volatile.Read(ref _queue.Reads) == 0)
            await Task.Delay(10, giveUp.Token).ConfigureAwait(false);
    }

    [TestCase("Completed")]
    [TestCase("Failed")]
    public async Task FinishedTaskIsReturnedWithoutWaiting_Async(string status)
    {
        _queue.Status = status;

        var state = await _service.WaitAsync(TaskId, LongTimeout, CancellationToken.None).WaitAsync(Prompt).ConfigureAwait(false);

        Assert.That(state.Status, Is.EqualTo(status));
        Assert.That(TaskWaitService.IsFinished(state), Is.True);
        Assert.That(CountCompletionHandlers(), Is.Zero);
    }

    [TestCase("Waiting")]
    [TestCase("Running")]
    public async Task NoTimeoutReturnsTheStateAsItIs_Async(string status)
    {
        _queue.Status = status;

        var state = await _service.WaitAsync(TaskId, TimeSpan.Zero, CancellationToken.None).WaitAsync(Prompt).ConfigureAwait(false);

        Assert.That(state.Status, Is.EqualTo(status));
        Assert.That(TaskWaitService.IsFinished(state), Is.False);
        Assert.That(CountCompletionHandlers(), Is.Zero);
    }

    [Test]
    public async Task CompletionOfTheTaskEndsTheWait_Async()
    {
        var wait = _service.WaitAsync(TaskId, LongTimeout, CancellationToken.None);
        await UntilWaitingAsync().ConfigureAwait(false);
        Assert.That(wait.IsCompleted, Is.False);

        // The runner caches the result before it signals, so the state read after the signal is the final one
        _queue.Status = "Completed";
        _events.SignalTaskCompleted(TaskId);

        var state = await wait.WaitAsync(Prompt).ConfigureAwait(false);
        Assert.That(state.Status, Is.EqualTo("Completed"));
        Assert.That(CountCompletionHandlers(), Is.Zero);
    }

    [Test]
    public async Task CompletionOfAnotherTaskDoesNotEndTheWait_Async()
    {
        var wait = _service.WaitAsync(TaskId, TimeSpan.FromSeconds(3), CancellationToken.None);
        await UntilWaitingAsync().ConfigureAwait(false);
        var readsBeforeSignal = Volatile.Read(ref _queue.Reads);

        _events.SignalTaskCompleted(TaskId + 1);
        await Task.Delay(100).ConfigureAwait(false);
        Assert.That(Volatile.Read(ref _queue.Reads), Is.EqualTo(readsBeforeSignal), "The state is read again when the wait ends, which it must not have done yet");

        var state = await wait.WaitAsync(Prompt).ConfigureAwait(false);
        Assert.That(state.Status, Is.EqualTo("Running"));
        Assert.That(CountCompletionHandlers(), Is.Zero);
    }

    [Test]
    public async Task TimeoutReturnsTheUnfinishedState_Async()
    {
        var state = await _service.WaitAsync(TaskId, TimeSpan.FromMilliseconds(50), CancellationToken.None).WaitAsync(Prompt).ConfigureAwait(false);

        Assert.That(state.Status, Is.EqualTo("Running"));
        Assert.That(Volatile.Read(ref _queue.Reads), Is.EqualTo(2), "The state is read before the wait and again after it");
        Assert.That(CountCompletionHandlers(), Is.Zero);
    }

    [Test]
    public async Task CancellationEndsTheWaitWithAnException_Async()
    {
        using var cts = new CancellationTokenSource();
        var wait = _service.WaitAsync(TaskId, LongTimeout, cts.Token);
        await UntilWaitingAsync().ConfigureAwait(false);

        cts.Cancel();

        Assert.That(async () => await wait.WaitAsync(Prompt).ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(CountCompletionHandlers(), Is.Zero);
    }

    [Test]
    public void UnknownTaskIsReportedAndLeavesNoHandler()
    {
        _queue.Status = null;

        Assert.That(async () => await _service.WaitAsync(TaskId, LongTimeout, CancellationToken.None).ConfigureAwait(false), Throws.InstanceOf<NotFoundException>());
        Assert.That(CountCompletionHandlers(), Is.Zero);
    }

    [Test]
    public void ProgressIsOnlyGivenForTheRunningTask()
    {
        Assert.That(_service.GetProgress(TaskId), Is.Null, "There is no progress while nothing runs");

        var progress = InterfaceStub.Create<IProgressEventData>(new() { [nameof(IProgressEventData.TaskID)] = TaskId });
        _progress.GenerateProgressState = () => progress;

        Assert.That(_service.GetProgress(TaskId), Is.SameAs(progress));
        Assert.That(_service.GetProgress(TaskId + 1), Is.Null, "The progress of the running task is not the progress of another task");
    }
}
