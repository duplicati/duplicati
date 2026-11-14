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
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using NUnit.Framework;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests that the scheduler skips a due run when the last completed run of the
/// backup finished less than --min-time-between-scheduled-runs ago
/// </summary>
[TestFixture]
[Category("Scheduler")]
public class SchedulerMinTimeBetweenRunsTests
{
    private const string OPTION = "--min-time-between-scheduled-runs";

    private string _tempDataFolder = null!;
    private Connection _connection = null!;

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
        _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-scheduler-min-time-test-{Guid.NewGuid()}");
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
    /// Saves a backup whose daily schedule is due, and whose last run finished the given time ago
    /// </summary>
    private IBackup SaveDueBackup(string name, TimeSpan finishedAgo, string? minTime)
    {
        var settings = new List<ISetting> { new Setting { Name = "passphrase", Value = "secret", Filter = string.Empty } };
        if (minTime != null)
            settings.Add(new Setting { Name = OPTION, Value = minTime, Filter = string.Empty });

        var backup = new Backup
        {
            Name = name,
            Description = "Minimum time between scheduled runs test",
            Tags = Array.Empty<string>(),
            TargetURL = "file:///mock_min_time_target",
            Sources = new[] { "Mock Source" },
            Settings = settings.ToArray(),
            Filters = Array.Empty<IFilter>(),
            Metadata = new Dictionary<string, string>
            {
                { "LastBackupFinished", Library.Utility.Utility.SerializeDateTime(DateTime.UtcNow - finishedAgo) },
            },
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
        return backup;
    }

    [Test]
    public void Scheduler_SkipsDueRunsThatComeTooSoonAfterTheLastRun()
    {
        var tooSoon = SaveDueBackup("too-soon", TimeSpan.FromMinutes(10), "1h");
        var longEnough = SaveDueBackup("long-enough", TimeSpan.FromMinutes(10), "5m");
        var noOption = SaveDueBackup("no-option", TimeSpan.FromMinutes(10), null);

        Assert.AreEqual("1h", _connection.GetBackup(tooSoon.ID)!.Settings.Single(x => x.Name == OPTION).Value);
        Assert.IsTrue(_connection.GetBackup(tooSoon.ID)!.Metadata.ContainsKey("LastBackupFinished"),
            "The test setup should store the last run time.");

        var queue = new RecordingQueueRunnerService();
        var scheduler = new Scheduler(_connection, queue);
        try
        {
            scheduler.Reschedule();
            Assert.IsTrue(queue.TaskAdded.Wait(TimeSpan.FromSeconds(30)),
                "The scheduler should queue the backups whose schedule is due.");
        }
        finally
        {
            // Terminating waits for the scheduler pass to finish, so every task it queues is recorded
            scheduler.Terminate(true);
        }

        CollectionAssert.AreEquivalent(new[] { longEnough.ID, noOption.ID }, queue.Added.Select(x => x.BackupID).ToArray(),
            "Only the backup whose last run finished less than the minimum time ago should be skipped.");
    }

    [Test]
    public void Scheduler_QueuesTheBackupWhenTheMinimumTimeIsInvalid()
    {
        var invalid = SaveDueBackup("invalid", TimeSpan.FromMinutes(10), "2 hours");
        var valid = SaveDueBackup("valid", TimeSpan.FromMinutes(10), "5m");

        var queue = new RecordingQueueRunnerService();
        var scheduler = new Scheduler(_connection, queue);
        try
        {
            scheduler.Reschedule();
            Assert.IsTrue(queue.TaskAdded.Wait(TimeSpan.FromSeconds(30)),
                "The scheduler should queue the backups whose schedule is due.");
        }
        finally
        {
            // Terminating waits for the scheduler pass to finish, so every task it queues is recorded
            scheduler.Terminate(true);
        }

        CollectionAssert.AreEquivalent(new[] { invalid.ID, valid.ID }, queue.Added.Select(x => x.BackupID).ToArray(),
            "An invalid minimum time should be ignored, not stop the scheduler.");
    }
}
