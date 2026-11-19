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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Interface;
using Duplicati.Library.RestAPI.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility.Power;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests that the scheduler runs a backup skipped on battery power once the
/// system is on AC power, when the run-skipped-on-ac-power option is set
/// </summary>
[TestFixture]
[Category("Scheduler")]
public class SchedulerBatteryTests
{
    private string _tempDataFolder = null!;
    private Connection _connection = null!;

    /// <summary>
    /// Queue runner that records the tasks the scheduler adds
    /// </summary>
    private sealed class RecordingQueueRunnerService : IQueueRunnerService
    {
        public ConcurrentQueue<IQueuedTask> Added { get; } = new();
        public ManualResetEventSlim TaskAdded { get; } = new(false);
        public ConcurrentQueue<IQueuedTask> Pending { get; } = new();

        public IQueuedTask? GetCurrentTask() => null;
        public List<IQueuedTask> GetCurrentTasks() => Pending.ToList();
        public bool GetIsActive() => false;
        public CachedTaskResult? GetCachedTaskResults(long taskID) => null;
        public long AddTask(IQueuedTask task) => AddTask(task, false);
        public long AddTask(IQueuedTask task, bool skipQueue)
        {
            Added.Enqueue(task);
            TaskAdded.Set();
            return Added.Count;
        }
        public void Terminate(bool wait) { }
        public void Resume() { }
        public void Pause() { }
        public IList<Tuple<long, string?>> GetQueueWithIds() => new List<Tuple<long, string?>>();
        public void CancelCurrentTaskLockWait(long taskID) { }
        public Task<IBasicResults?> RunImmediatelyAsync(IQueuedTask task) => Task.FromResult<IBasicResults?>(null);
    }

    /// <summary>
    /// A switchable power source that counts how often it is read
    /// </summary>
    private sealed class FakePowerSource
    {
        private int _reads;
        public volatile bool OnBattery = true;
        public int Reads => Volatile.Read(ref _reads);

        public PowerSupply.Source Get()
        {
            var source = OnBattery ? PowerSupply.Source.Battery : PowerSupply.Source.AC;
            Interlocked.Increment(ref _reads);
            return source;
        }
    }

    /// <summary>
    /// A queued task reported by the fake queue runner
    /// </summary>
    private sealed class QueuedBackup(string backupId) : IQueuedTask
    {
        public long TaskID => 1;
        public string? BackupID => backupId;
        public Duplicati.Server.Serialization.DuplicatiOperation Operation => Duplicati.Server.Serialization.DuplicatiOperation.BackupOrSync;
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

    [SetUp]
    public async Task SetUpAsync()
    {
        _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-scheduler-battery-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDataFolder);

        var databasePath = Path.Combine(_tempDataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);
        var dbConnection = await SQLiteLoader.LoadConnectionAsync(databasePath);
        DatabaseUpgrader.UpgradeDatabase(dbConnection, databasePath, typeof(DatabaseSchemaMarker));
        _connection = new Connection(dbConnection, true, null, _tempDataFolder, () => { });
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
    /// Saves a backup that is disabled on battery power, with a daily schedule that is due
    /// </summary>
    private (Backup Backup, Schedule Schedule) SaveBatteryBackup(string name, bool runSkippedOnAcPower)
    {
        var settings = new List<ISetting>
        {
            new Setting { Name = "passphrase", Value = "secret", Filter = string.Empty },
            new Setting { Name = "--disable-on-battery", Value = "true", Filter = string.Empty },
        };
        if (runSkippedOnAcPower)
            settings.Add(new Setting { Name = "--run-skipped-on-ac-power", Value = "true", Filter = string.Empty });

        var backup = new Backup
        {
            Name = name,
            Description = "Battery test",
            Tags = Array.Empty<string>(),
            TargetURL = "file:///mock_battery_target",
            Sources = new[] { "Mock Source" },
            Settings = settings.ToArray(),
            Filters = Array.Empty<IFilter>(),
            Metadata = new Dictionary<string, string>(),
        };
        var schedule = new Schedule
        {
            Tags = Array.Empty<string>(),
            Time = DateTime.UtcNow.AddHours(-1),
            Repeat = "1D",
            LastRun = new DateTime(0, DateTimeKind.Utc),
            Rule = string.Empty,
        };
        _connection.AddOrUpdateBackupAndSchedule(backup, schedule);
        return (backup, schedule);
    }

    /// <summary>
    /// Lets the scheduler skip every due backup on battery power, runs <paramref name="whileOnBattery"/>,
    /// then switches to AC power and returns the IDs of the backups the scheduler queued
    /// </summary>
    private string?[] RunOnBatteryThenOnAc(RecordingQueueRunnerService queue, int skippedBackups, Action? whileOnBattery = null)
    {
        var power = new FakePowerSource();
        var scheduler = new Scheduler(_connection, queue, power.Get);
        try
        {
            scheduler.Reschedule();

            // Each due backup reads the power source once before it is skipped
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (power.Reads < skippedBackups && DateTime.UtcNow < deadline)
                Thread.Sleep(10);
            Assert.GreaterOrEqual(power.Reads, skippedBackups, "The scheduler should check the power source of every due backup.");
            CollectionAssert.IsEmpty(queue.Added, "No backup should be queued on battery power.");

            whileOnBattery?.Invoke();

            power.OnBattery = false;
            scheduler.Reschedule();
            Assert.IsTrue(queue.TaskAdded.Wait(TimeSpan.FromSeconds(30)),
                "The scheduler should queue a skipped backup once on AC power.");
        }
        finally
        {
            // Terminating waits for the scheduler pass to finish, so every task it queues is recorded
            scheduler.Terminate(true);
        }

        return queue.Added.Select(x => x.BackupID).ToArray();
    }

    [Test]
    public void SkippedBackup_RunsOnAcPower_OnlyWhenTheOptionIsSet()
    {
        var (optedIn, _) = SaveBatteryBackup("opted-in", runSkippedOnAcPower: true);
        SaveBatteryBackup("not-opted-in", runSkippedOnAcPower: false);

        var queued = RunOnBatteryThenOnAc(new RecordingQueueRunnerService(), 2);

        CollectionAssert.AreEqual(new[] { optedIn.ID }, queued,
            "Only the backup with run-skipped-on-ac-power should run once, after switching to AC power.");
    }

    [Test]
    public void SkippedBackup_RunsWithTheSettingsAtTheTimeOfTheRun()
    {
        var (backup, schedule) = SaveBatteryBackup("before-edit", runSkippedOnAcPower: true);
        var queue = new RecordingQueueRunnerService();

        RunOnBatteryThenOnAc(queue, 1, () =>
        {
            var edited = (Backup)(_connection.GetBackup(backup.ID) ?? throw new Exception("Backup not found"));
            edited.Name = "after-edit";
            // Passing the schedule keeps it, as saving a backup without one removes its schedule
            _connection.AddOrUpdateBackupAndSchedule(edited, schedule);
        });

        var task = queue.Added.Single() as Duplicati.Server.Runner.IRunnerData;
        Assert.AreEqual("after-edit", task?.Backup?.Name,
            "A backup edited while waiting for AC power should run with its current settings.");
    }

    [Test]
    public void SkippedBackup_IsNotQueuedTwice()
    {
        var (alreadyQueued, _) = SaveBatteryBackup("already-queued", runSkippedOnAcPower: true);
        var (sentinel, _) = SaveBatteryBackup("sentinel", runSkippedOnAcPower: true);
        var queue = new RecordingQueueRunnerService();

        var queued = RunOnBatteryThenOnAc(queue, 2, () => queue.Pending.Enqueue(new QueuedBackup(alreadyQueued.ID)));

        CollectionAssert.AreEqual(new[] { sentinel.ID }, queued,
            "A skipped backup that is already in the queue should not be queued again.");
    }
}
