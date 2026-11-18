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
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests that the scheduler skips a missed scheduled run when the
/// skip-missed-scheduled-backups option is set, and still runs it otherwise
/// </summary>
[TestFixture]
[Category("Scheduler")]
public class SchedulerMissedRunTests
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
        _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-scheduler-missed-test-{Guid.NewGuid()}");
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
    /// Builds a minimal backup that passes validation
    /// </summary>
    private static Backup BuildBackup(string name, bool skipMissed)
    {
        var settings = new List<ISetting> { new Setting { Name = "passphrase", Value = "secret", Filter = string.Empty } };
        if (skipMissed)
            settings.Add(new Setting { Name = "--skip-missed-scheduled-backups", Value = "true", Filter = string.Empty });

        return new Backup
        {
            Name = name,
            Description = "Missed schedule test",
            Tags = Array.Empty<string>(),
            TargetURL = "file:///mock_missed_target",
            Sources = new[] { "Mock Source" },
            Settings = settings.ToArray(),
            Filters = Array.Empty<IFilter>(),
            Metadata = new Dictionary<string, string>(),
        };
    }

    /// <summary>
    /// Builds a daily schedule whose first run was due the given time ago
    /// </summary>
    private static Schedule BuildScheduleDueAgo(TimeSpan ago)
        => new Schedule
        {
            Tags = Array.Empty<string>(),
            Time = DateTime.UtcNow - ago,
            Repeat = "1D",
            LastRun = new DateTime(0, DateTimeKind.Utc),
            Rule = string.Empty,
        };

    private IBackup Save(Backup backup, ISchedule schedule)
    {
        _connection.AddOrUpdateBackupAndSchedule(backup, schedule);
        return backup;
    }

    /// <summary>
    /// Runs one scheduler pass and returns the IDs of the queued backups and the proposed schedule
    /// </summary>
    private (string?[] Queued, IList<Tuple<string, DateTime>> Proposed) RunSchedulerPass()
    {
        var queue = new RecordingQueueRunnerService();
        var scheduler = new Scheduler(_connection, queue);
        try
        {
            scheduler.Reschedule();
            Assert.IsTrue(queue.TaskAdded.Wait(TimeSpan.FromSeconds(30)),
                "The scheduler should queue at least one due backup.");
        }
        finally
        {
            // Terminating waits for the scheduler pass to finish, so every task it queues is recorded
            scheduler.Terminate(true);
        }

        return (queue.Added.Select(x => x.BackupID).ToArray(), scheduler.GetProposedSchedule());
    }

    [Test]
    public void MissedRun_IsSkippedOnlyWhenTheOptionIsSet()
    {
        var runsLate = Save(BuildBackup("missed-default", skipMissed: false), BuildScheduleDueAgo(TimeSpan.FromHours(2)));
        var skips = Save(BuildBackup("missed-skip", skipMissed: true), BuildScheduleDueAgo(TimeSpan.FromHours(2)));

        var (queued, proposed) = RunSchedulerPass();

        CollectionAssert.AreEqual(new[] { runsLate.ID }, queued,
            "Only the backup without skip-missed-scheduled-backups should run its missed slot.");

        var next = proposed.Single(x => x.Item1 == skips.ID).Item2;
        Assert.Greater(next, DateTime.UtcNow,
            "The skipped backup should move on to its next slot in the future.");
    }

    [Test]
    public void RunDueWithinTolerance_StillRunsWithTheOption()
    {
        var onTime = Save(BuildBackup("on-time-skip", skipMissed: true), BuildScheduleDueAgo(TimeSpan.FromMinutes(1)));

        var (queued, _) = RunSchedulerPass();

        CollectionAssert.AreEqual(new[] { onTime.ID }, queued,
            "A run that is only slightly late is not missed and should still be queued.");
    }
}
