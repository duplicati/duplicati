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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Interface;
using Duplicati.Library.RestAPI.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Duplicati.WebserverCore.Services;
using Duplicati.Library.Main.SystemPower;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests shutting down or restarting the computer when the task queue has run empty:
/// that the action waits for the whole queue, runs once, can be cancelled, is refused
/// when nothing is queued, and that the right operating system command is built.
/// </summary>
[TestFixture]
[Category("QueueCompletionAction")]
public class QueueCompletionActionTests
{
    /// <summary>
    /// The settle delay used by the tests
    /// </summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// The time to wait for an action that is expected to be performed
    /// </summary>
    private static readonly TimeSpan PerformTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The time to wait before concluding that no action is performed
    /// </summary>
    private static readonly TimeSpan QuietTime = TimeSpan.FromMilliseconds(500);

    private string _tempDataFolder = null!;
    private Connection _connection = null!;
    private EventPollNotify _eventPollNotify = null!;
    private FakeQueueRunnerService _queue = null!;
    private RecordingPowerControl _power = null!;
    private RecordingScriptRunner _script = null!;
    private QueueCompletionActionService _service = null!;

    /// <summary>
    /// A task that only carries an ID
    /// </summary>
    private sealed class FakeQueuedTask : IQueuedTask
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

    /// <summary>
    /// Queue runner where the test decides what is running and what is waiting
    /// </summary>
    private sealed class FakeQueueRunnerService : IQueueRunnerService
    {
        private readonly object _lock = new();
        private IQueuedTask? _current;
        private readonly List<IQueuedTask> _waiting = new();

        public void Set(IQueuedTask? current, params IQueuedTask[] waiting)
        {
            lock (_lock)
            {
                _current = current;
                _waiting.Clear();
                _waiting.AddRange(waiting);
            }
        }

        public IQueuedTask? GetCurrentTask() { lock (_lock) return _current; }
        public List<IQueuedTask> GetCurrentTasks() { lock (_lock) return [.. _waiting]; }
        public bool GetIsActive() => GetCurrentTask() != null;
        public CachedTaskResult? GetCachedTaskResults(long taskID) => null;
        public long AddTask(IQueuedTask task) => AddTask(task, false);
        public long AddTask(IQueuedTask task, bool skipQueue) => task.TaskID;
        public void Terminate(bool wait) { }
        public void Resume() { }
        public void Pause() { }
        public IList<Tuple<long, string?>> GetQueueWithIds() => new List<Tuple<long, string?>>();
        public void CancelCurrentTaskLockWait(long taskID) { }
        public Task<IBasicResults?> RunImmediatelyAsync(IQueuedTask task) => Task.FromResult<IBasicResults?>(null);
    }

    /// <summary>
    /// Power control that records the requests instead of touching the computer
    /// </summary>
    private sealed class RecordingPowerControl : ISystemPowerControl
    {
        private readonly object _lock = new();
        private readonly List<QueueCompletionAction> _performed = new();
        private readonly SemaphoreSlim _signal = new(0);

        public Exception? Failure { get; set; }

        public HashSet<QueueCompletionAction> Supported { get; } = [QueueCompletionAction.Shutdown, QueueCompletionAction.Reboot];

        public Task<bool> CanPerformAsync(QueueCompletionAction action, CancellationToken cancellationToken)
            => Task.FromResult(Supported.Contains(action));

        public List<QueueCompletionAction> Performed { get { lock (_lock) return [.. _performed]; } }

        public Task PerformAsync(QueueCompletionAction action, CancellationToken cancellationToken)
        {
            lock (_lock)
                _performed.Add(action);
            _signal.Release();
            return Failure == null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public Task<bool> WaitForRequestAsync(TimeSpan timeout) => _signal.WaitAsync(timeout);
    }

    /// <summary>
    /// Script runner that records the runs instead of starting a script
    /// </summary>
    private sealed class RecordingScriptRunner : IPostQueueScriptRunner
    {
        private readonly SemaphoreSlim _signal = new(0);
        private int _runs;

        public bool Configured { get; set; }
        public Exception? Failure { get; set; }
        public int Runs => Volatile.Read(ref _runs);

        public bool CanRun() => Configured;

        public Task RunAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runs);
            _signal.Release();
            return Failure == null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public Task<bool> WaitForRunAsync(TimeSpan timeout) => _signal.WaitAsync(timeout);
    }

    [SetUp]
    public async Task SetUpAsync()
    {
        _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-queue-completion-action-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDataFolder);

        var databasePath = Path.Combine(_tempDataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);
        var dbConnection = await SQLiteLoader.LoadConnectionAsync(databasePath);
        DatabaseUpgrader.UpgradeDatabase(dbConnection, databasePath, typeof(DatabaseSchemaMarker));
        _connection = new Connection(dbConnection, true, null, _tempDataFolder, () => { });

        _eventPollNotify = new EventPollNotify();
        _queue = new FakeQueueRunnerService();
        _power = new RecordingPowerControl();
        _script = new RecordingScriptRunner();
        _service = new QueueCompletionActionService(_connection, _eventPollNotify, _queue, _power, _script, new QueueCompletionSettings(SettleDelay, TimeSpan.FromSeconds(30)));
    }

    [TearDown]
    public void TearDown()
    {
        _connection?.Dispose();
        try
        {
            if (Directory.Exists(_tempDataFolder))
                Directory.Delete(_tempDataFolder, true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    /// <summary>
    /// Reports a task as finished, the way the queue runner does it
    /// </summary>
    private void CompleteTask(long taskId)
    {
        _eventPollNotify.SignalTaskQueueUpdate();
        _eventPollNotify.SignalTaskCompleted(taskId);
    }

    [Test]
    public void ActionIsRefusedWhenNothingIsQueued()
    {
        Assert.That(async () => await _service.SetPendingActionAsync(QueueCompletionAction.Shutdown, CancellationToken.None), Throws.InstanceOf<ConflictException>());
        Assert.That(_service.PendingAction, Is.EqualTo(QueueCompletionAction.None));

        // Cancelling is always possible
        Assert.That(async () => await _service.SetPendingActionAsync(QueueCompletionAction.None, CancellationToken.None), Throws.Nothing);
    }

    [Test]
    public async Task ActionTheComputerDoesNotAllowIsRefused()
    {
        _power.Supported.Remove(QueueCompletionAction.Shutdown);
        _queue.Set(new FakeQueuedTask { TaskID = 1 });

        Assert.That(await _service.GetSupportedActionsAsync(CancellationToken.None), Is.EqualTo(new[] { QueueCompletionAction.Reboot }));
        Assert.That(async () => await _service.SetPendingActionAsync(QueueCompletionAction.Shutdown, CancellationToken.None), Throws.InstanceOf<BadRequestException>());
        Assert.That(_service.PendingAction, Is.EqualTo(QueueCompletionAction.None));

        await _service.SetPendingActionAsync(QueueCompletionAction.Reboot, CancellationToken.None);
        Assert.That(_service.PendingAction, Is.EqualTo(QueueCompletionAction.Reboot));
    }

    [Test]
    public async Task ScriptIsOnlyOfferedWhenItIsSet()
    {
        _queue.Set(new FakeQueuedTask { TaskID = 1 });

        Assert.That(await _service.GetSupportedActionsAsync(CancellationToken.None), Does.Not.Contain(QueueCompletionAction.RunScript));
        Assert.That(async () => await _service.SetPendingActionAsync(QueueCompletionAction.RunScript, CancellationToken.None), Throws.InstanceOf<BadRequestException>());

        _script.Configured = true;
        Assert.That(await _service.GetSupportedActionsAsync(CancellationToken.None),
            Is.EqualTo(new[] { QueueCompletionAction.Shutdown, QueueCompletionAction.Reboot, QueueCompletionAction.RunScript }));

        // The script does not depend on the computer allowing a shutdown
        _power.Supported.Clear();
        Assert.That(await _service.GetSupportedActionsAsync(CancellationToken.None), Is.EqualTo(new[] { QueueCompletionAction.RunScript }));
    }

    [Test]
    public async Task ScriptIsRunOnceWhenTheQueueRunsEmpty()
    {
        _script.Configured = true;
        _queue.Set(new FakeQueuedTask { TaskID = 1 });
        await _service.SetPendingActionAsync(QueueCompletionAction.RunScript, CancellationToken.None);

        _queue.Set(null);
        CompleteTask(1);

        Assert.That(await _script.WaitForRunAsync(PerformTimeout), Is.True, "The script should run when the queue is empty");
        Assert.That(_service.PendingAction, Is.EqualTo(QueueCompletionAction.None));

        CompleteTask(2);
        Assert.That(await _script.WaitForRunAsync(QuietTime), Is.False, "The script should only run once");
        Assert.That(_script.Runs, Is.EqualTo(1));
        Assert.That(_power.Performed, Is.Empty, "The computer should not be shut down by the server when the script is chosen");
    }

    [Test]
    public async Task FailedScriptIsReported()
    {
        _script.Configured = true;
        _script.Failure = new InvalidOperationException("The script failed with exit code 3");
        _queue.Set(new FakeQueuedTask { TaskID = 1 });
        await _service.SetPendingActionAsync(QueueCompletionAction.RunScript, CancellationToken.None);

        _queue.Set(null);
        CompleteTask(1);
        Assert.That(await _script.WaitForRunAsync(PerformTimeout), Is.True);

        var deadline = DateTime.UtcNow + PerformTimeout;
        while (_connection.GetNotifications().Length == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        var notification = _connection.GetNotifications().Single();
        Assert.That(notification.Type, Is.EqualTo(NotificationType.Error));
        Assert.That(notification.Title, Does.Contain("script"));
        Assert.That(notification.Message, Does.Contain("exit code 3"));
    }

    [Test]
    public async Task ScriptRunnerFollowsTheServerSetting()
    {
        var runner = new PostQueueScriptRunner(_connection, new QueueCompletionSettings(SettleDelay, TimeSpan.FromSeconds(30)));
        Assert.That(runner.CanRun(), Is.False, "No script should be set on a new server");
        Assert.That(async () => await runner.RunAsync(CancellationToken.None), Throws.InstanceOf<InvalidOperationException>());

        _connection.ApplicationSettings.PostQueueProcessingScript = Path.Combine(_tempDataFolder, "missing-script");
        Assert.That(runner.CanRun(), Is.False, "A script that does not exist should not be offered");

        // The script records the event name it is given, and the second one reports a failure
        var output = Path.Combine(_tempDataFolder, "script-output.txt");
        var script = Path.Combine(_tempDataFolder, OperatingSystem.IsWindows() ? "post-queue.cmd" : "post-queue.sh");
        string Content(int exitCode) => OperatingSystem.IsWindows()
            ? $"@echo off\r\necho %{PostQueueScriptRunner.EventNameVariable}%> \"{output}\"\r\nexit /b {exitCode}\r\n"
            : $"#!/bin/sh\necho \"${PostQueueScriptRunner.EventNameVariable}\" > \"{output}\"\nexit {exitCode}\n";

        File.WriteAllText(script, Content(0));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        _connection.ApplicationSettings.PostQueueProcessingScript = script;
        Assert.That(runner.CanRun(), Is.True);

        await runner.RunAsync(CancellationToken.None);
        Assert.That(File.ReadAllText(output).Trim(), Is.EqualTo(PostQueueScriptRunner.EventName));

        File.WriteAllText(script, Content(3));
        Assert.That(async () => await runner.RunAsync(CancellationToken.None), Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("exit code 3"));

        _connection.ApplicationSettings.PostQueueProcessingScript = "";
        Assert.That(runner.CanRun(), Is.False, "Clearing the setting should remove the choice");
    }

    [Test]
    public async Task NoActionIsSupportedWhenTheComputerAllowsNone()
    {
        _power.Supported.Clear();
        Assert.That(await _service.GetSupportedActionsAsync(CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task ActionIsPerformedOnceWhenTheQueueRunsEmpty()
    {
        _queue.Set(new FakeQueuedTask { TaskID = 1 });
        await _service.SetPendingActionAsync(QueueCompletionAction.Shutdown, CancellationToken.None);
        Assert.That(_service.PendingAction, Is.EqualTo(QueueCompletionAction.Shutdown));

        _queue.Set(null);
        CompleteTask(1);

        Assert.That(await _power.WaitForRequestAsync(PerformTimeout), Is.True, "The computer should be shut down when the queue is empty");
        Assert.That(_service.PendingAction, Is.EqualTo(QueueCompletionAction.None), "A performed action should not stay pending");

        // A task that finishes later must not shut the computer down again
        CompleteTask(2);
        Assert.That(await _power.WaitForRequestAsync(QuietTime), Is.False, "The action should only be performed once");
        Assert.That(_power.Performed, Is.EqualTo(new[] { QueueCompletionAction.Shutdown }));
    }

    [Test]
    public async Task ActionWaitsForTheWaitingTasks()
    {
        var second = new FakeQueuedTask { TaskID = 2 };
        _queue.Set(new FakeQueuedTask { TaskID = 1 }, second);
        await _service.SetPendingActionAsync(QueueCompletionAction.Reboot, CancellationToken.None);

        // The first task finishes, and the second one starts
        _queue.Set(second);
        CompleteTask(1);
        Assert.That(await _power.WaitForRequestAsync(QuietTime), Is.False, "The action should wait while a task is running");
        Assert.That(_service.PendingAction, Is.EqualTo(QueueCompletionAction.Reboot));

        _queue.Set(null);
        CompleteTask(2);
        Assert.That(await _power.WaitForRequestAsync(PerformTimeout), Is.True, "The computer should restart when the last task is done");
        Assert.That(_power.Performed, Is.EqualTo(new[] { QueueCompletionAction.Reboot }));
    }

    [Test]
    public async Task ActionWaitsForTasksQueuedWhenATaskFinishes()
    {
        _queue.Set(new FakeQueuedTask { TaskID = 1 });
        await _service.SetPendingActionAsync(QueueCompletionAction.Shutdown, CancellationToken.None);

        // The queue is empty for a moment, before the backup that runs after the first one is queued
        _queue.Set(null);
        CompleteTask(1);
        _queue.Set(new FakeQueuedTask { TaskID = 2 });

        Assert.That(await _power.WaitForRequestAsync(QuietTime), Is.False, "A task queued by the finished task should be waited for");
        Assert.That(_service.PendingAction, Is.EqualTo(QueueCompletionAction.Shutdown));
    }

    [Test]
    public async Task CancelledActionIsNotPerformed()
    {
        _queue.Set(new FakeQueuedTask { TaskID = 1 });
        await _service.SetPendingActionAsync(QueueCompletionAction.Shutdown, CancellationToken.None);
        await _service.SetPendingActionAsync(QueueCompletionAction.None, CancellationToken.None);

        _queue.Set(null);
        CompleteTask(1);

        Assert.That(await _power.WaitForRequestAsync(QuietTime), Is.False, "A cancelled action should not be performed");
        Assert.That(_power.Performed, Is.Empty);
    }

    [Test]
    public async Task ActionIsPerformedWhenAWaitingTaskLeavesTheQueue()
    {
        _queue.Set(null, new FakeQueuedTask { TaskID = 1 });
        await _service.SetPendingActionAsync(QueueCompletionAction.Shutdown, CancellationToken.None);

        // The waiting task is removed without running, so no task completes
        _queue.Set(null);
        _eventPollNotify.SignalTaskQueueUpdate();

        Assert.That(await _power.WaitForRequestAsync(PerformTimeout), Is.True, "An emptied queue should count as completed");
    }

    [Test]
    public async Task FailedActionIsReportedAndCleared()
    {
        _power.Failure = new InvalidOperationException("Not allowed to shut down");
        _queue.Set(new FakeQueuedTask { TaskID = 1 });
        await _service.SetPendingActionAsync(QueueCompletionAction.Shutdown, CancellationToken.None);

        _queue.Set(null);
        CompleteTask(1);
        Assert.That(await _power.WaitForRequestAsync(PerformTimeout), Is.True);

        // The notification is registered just after the failed request
        var deadline = DateTime.UtcNow + PerformTimeout;
        while (_connection.GetNotifications().Length == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        var notification = _connection.GetNotifications().Single();
        Assert.That(notification.Type, Is.EqualTo(NotificationType.Error));
        Assert.That(notification.Message, Does.Contain("Not allowed to shut down"));
        Assert.That(_service.PendingAction, Is.EqualTo(QueueCompletionAction.None), "A failed action should not be retried");
    }

    [Test]
    public async Task SettingTheActionSignalsAStatusChange()
    {
        _queue.Set(new FakeQueuedTask { TaskID = 1 });
        var before = _eventPollNotify.EventNo;
        await _service.SetPendingActionAsync(QueueCompletionAction.Shutdown, CancellationToken.None);
        Assert.That(_eventPollNotify.EventNo, Is.GreaterThan(before), "The clients should be told that the status changed");
    }

    [Test]
    public async Task TheOperatingSystemAnswersTheCapabilityCheck()
    {
        // The answer depends on the computer and the account, so only the check itself is tested
        var control = new SystemPowerControl();
        Assert.That(await control.CanPerformAsync(QueueCompletionAction.None, CancellationToken.None), Is.False);
        Assert.That(async () => await control.CanPerformAsync(QueueCompletionAction.Shutdown, CancellationToken.None), Throws.Nothing);
        Assert.That(async () => await control.CanPerformAsync(QueueCompletionAction.Reboot, CancellationToken.None), Throws.Nothing);
    }

    [TestCase(QueueCompletionAction.Shutdown)]
    [TestCase(QueueCompletionAction.Reboot)]
    [Platform(Include = "MacOsX")]
    public void MacOSLoginWindowRequestCanBeBuilt(QueueCompletionAction action)
    {
        if (!OperatingSystem.IsMacOS())
            return;

        // The request is built and released, but never sent. A failure to build it throws.
        MacOSSystemPower.VerifyLoginWindowEvent(action == QueueCompletionAction.Reboot);
    }

    [TestCase(QueueCompletionAction.Shutdown, "-h")]
    [TestCase(QueueCompletionAction.Reboot, "-r")]
    public void MacOSRootUsesTheShutdownProgramWithADelay(QueueCompletionAction action, string flag)
    {
        if (!OperatingSystem.IsMacOS())
            return;

        Assert.That(MacOSSystemPower.ShutdownProgram, Is.EqualTo("/sbin/shutdown"));
        Assert.That(MacOSSystemPower.GetShutdownProgramArguments(action == QueueCompletionAction.Reboot, 1, "Done").Take(2), Is.EqualTo(new[] { flag, "+1" }));
    }
}
