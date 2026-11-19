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
using Duplicati.Library.RestAPI;
using Duplicati.Library.RestAPI.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests <see cref="Scheduler.SkipNextRun"/>: the skipped run is not queued,
/// and the following slot is stored with the schedule so it survives a restart.
/// </summary>
[TestFixture]
[Category("Scheduler")]
public class SchedulerSkipNextRunTests
{
    private string _tempDataFolder = null!;
    private Connection _connection = null!;
    private EventPollNotify _eventPollNotify = null!;

    /// <summary>
    /// Queue runner that records the tasks the scheduler adds
    /// </summary>
    private sealed class RecordingQueueRunnerService : IQueueRunnerService
    {
        public ConcurrentQueue<IQueuedTask> Added { get; } = new();
        public ManualResetEventSlim TaskAdded { get; } = new(false);

        public IQueuedTask? GetCurrentTask() => null;
        public List<IQueuedTask> GetCurrentTasks() => new();
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

    [SetUp]
    public async Task SetUpAsync()
    {
        _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-skip-next-run-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDataFolder);

        var databasePath = Path.Combine(_tempDataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);
        var dbConnection = await SQLiteLoader.LoadConnectionAsync(databasePath);
        DatabaseUpgrader.UpgradeDatabase(dbConnection, databasePath, typeof(DatabaseSchemaMarker));
        _connection = new Connection(dbConnection, true, null, _tempDataFolder, () => { });

        // Saving a schedule signals the clients through this, as in the server
        _eventPollNotify = new EventPollNotify();
        _connection.SetServiceProvider(new ServiceCollection()
            .AddSingleton<INotificationUpdateService>(new NotificationUpdateService())
            .AddSingleton(_eventPollNotify)
            .BuildServiceProvider());
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

    private IBackup Save(string name, ISchedule? schedule)
    {
        var backup = new Backup
        {
            Name = name,
            Description = "Skip next run test",
            Tags = Array.Empty<string>(),
            TargetURL = "file:///mock_skip_target",
            Sources = new[] { "Mock Source" },
            Settings = new ISetting[] { new Setting { Name = "passphrase", Value = "secret", Filter = string.Empty } },
            Filters = Array.Empty<IFilter>(),
            Metadata = new Dictionary<string, string>(),
        };
        _connection.AddOrUpdateBackupAndSchedule(backup, schedule);
        return backup;
    }

    /// <summary>
    /// Builds a daily schedule starting at the given time, in whole seconds as the database stores it
    /// </summary>
    private static Schedule BuildDailySchedule(DateTime firstRun)
        => new Schedule
        {
            Tags = Array.Empty<string>(),
            Time = new DateTime(firstRun.Ticks - firstRun.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc),
            Repeat = "1D",
            LastRun = new DateTime(0, DateTimeKind.Utc),
            Rule = string.Empty,
        };

    private ISchedule StoredSchedule(string backupId)
        => _connection.GetSchedule(_connection.GetScheduleIDsFromTags(["ID=" + backupId]).Single())
           ?? throw new Exception($"Schedule for backup {backupId} not found");

    [Test]
    public void SkipNextRun_StoresTheFollowingSlot()
    {
        var schedule = BuildDailySchedule(DateTime.UtcNow.AddHours(1));
        var firstRun = schedule.Time;
        var backup = Save("skip-future", schedule);

        var scheduler = new Scheduler(_connection, new RecordingQueueRunnerService());
        DateTime? next;
        try
        {
            next = scheduler.SkipNextRun(backup.ID);
        }
        finally
        {
            scheduler.Terminate(true);
        }

        Assert.AreEqual(firstRun.AddDays(1), next, "Skipping should move the next run to the following day.");
        Assert.AreEqual(firstRun.AddDays(1), StoredSchedule(backup.ID).Time.ToUniversalTime(),
            "The skipped slot should be stored, so it survives a restart.");
    }

    [Test]
    public void SkipNextRun_SkipsAnOverdueRunSoItIsNotQueued()
    {
        var skipped = Save("skip-overdue", BuildDailySchedule(DateTime.UtcNow.AddHours(-1)));
        // A second due backup tells the test when the scheduler has made its pass
        var sentinel = Save("sentinel-overdue", BuildDailySchedule(DateTime.UtcNow.AddHours(-1)));

        // A fresh scheduler reads the schedules from the database, as after a restart
        var queue = new RecordingQueueRunnerService();
        var scheduler = new Scheduler(_connection, queue);
        DateTime? next;
        try
        {
            next = scheduler.SkipNextRun(skipped.ID);
            Assert.IsTrue(queue.TaskAdded.Wait(TimeSpan.FromSeconds(30)),
                "The scheduler should queue the backup that was not skipped.");
        }
        finally
        {
            // Terminating waits for the scheduler pass to finish, so every task it queues is recorded
            scheduler.Terminate(true);
        }

        CollectionAssert.AreEqual(new[] { sentinel.ID }, queue.Added.Select(x => x.BackupID).ToArray(),
            "The skipped backup should not be queued.");
        Assert.That(next, Is.GreaterThan(DateTime.UtcNow), "The next run of a skipped overdue backup should be in the future.");
        var proposed = scheduler.GetProposedSchedule().Single(x => x.Item1 == skipped.ID);
        Assert.AreEqual(next, proposed.Item2, "The proposed schedule should show the slot after the skipped one.");
    }

    /// <summary>
    /// Waits until the condition holds, as the scheduler makes its pass on its own thread
    /// </summary>
    private static void WaitFor(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail(message);
            Thread.Sleep(50);
        }
    }

    private static DateTime? Proposed(Scheduler scheduler, string backupId)
        => scheduler.GetProposedSchedule().Where(x => x.Item1 == backupId).Select(x => (DateTime?)x.Item2).FirstOrDefault();

    [Test]
    public void SkipNextRun_StatusSignalledOnSaveShowsTheNewTime()
    {
        var schedule = BuildDailySchedule(DateTime.UtcNow.AddHours(1));
        var firstRun = schedule.Time;
        var backup = Save("skip-signal", schedule);

        var scheduler = new Scheduler(_connection, new RecordingQueueRunnerService());
        var signalled = new ConcurrentQueue<DateTime?>();
        DateTime? next;
        try
        {
            scheduler.Reschedule();
            WaitFor(() => Proposed(scheduler, backup.ID) == firstRun, "The scheduler should propose the first run.");

            // The websocket builds the status it pushes from the proposed schedule when the event is signalled
            _eventPollNotify.NewEvent += (_, _) => signalled.Enqueue(Proposed(scheduler, backup.ID));
            next = scheduler.SkipNextRun(backup.ID);
        }
        finally
        {
            scheduler.Terminate(true);
        }

        Assert.AreEqual(firstRun.AddDays(1), next, "Skipping should move the next run to the following day.");
        Assert.That(signalled, Is.Not.Empty, "Saving the skipped schedule should signal the clients.");
        Assert.AreEqual(next, signalled.First(), "The status signalled on save should already show the new next run.");
    }

    [Test]
    public void SkipNextRun_QueuedRunDoesNotUndoTheSkipWhenItFinishes()
    {
        var schedule = BuildDailySchedule(DateTime.UtcNow.AddHours(-1));
        var firstRun = schedule.Time;
        var backup = Save("skip-queued", schedule);

        var queue = new RecordingQueueRunnerService();
        var scheduler = new Scheduler(_connection, queue);
        DateTime? next;
        try
        {
            scheduler.Reschedule();
            Assert.IsTrue(queue.TaskAdded.Wait(TimeSpan.FromSeconds(30)), "The scheduler should queue the overdue run.");
            // Once the overdue run is queued, the scheduler holds the following slot, which the database does not have yet
            WaitFor(() => Proposed(scheduler, backup.ID) == firstRun.AddDays(1),
                "The scheduler should move on to the slot after the queued run.");

            next = scheduler.SkipNextRun(backup.ID);

            // The queued run starts and finishes after the skip
            var job = (Runner.IRunnerData)queue.Added.Single();
            job.OnStarting!().GetAwaiter().GetResult();
            job.OnFinished!(null).GetAwaiter().GetResult();
        }
        finally
        {
            scheduler.Terminate(true);
        }

        Assert.AreEqual(firstRun.AddDays(2), next, "The skip should move past the slot the scheduler already holds.");
        Assert.AreEqual(firstRun.AddDays(2), StoredSchedule(backup.ID).Time.ToUniversalTime(),
            "The queued run should not write the skipped slot back when it finishes.");
    }

    [Test]
    public void SkipNextRun_ReturnsNullWithoutASchedule()
    {
        var backup = Save("unscheduled", null);

        var scheduler = new Scheduler(_connection, new RecordingQueueRunnerService());
        try
        {
            Assert.IsNull(scheduler.SkipNextRun(backup.ID), "A backup without a schedule has no run to skip.");
        }
        finally
        {
            scheduler.Terminate(true);
        }
    }
}
