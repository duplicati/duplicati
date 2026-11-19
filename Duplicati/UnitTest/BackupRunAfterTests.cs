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
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests running a backup after another backup: that the reference is persisted and
/// validated, is removed with the backup it points to, is added and removed by the schema
/// upgrade and downgrade, and that the scheduler queues the right backups when a run finishes.
/// </summary>
[TestFixture]
[Category("BackupRunAfter")]
public class BackupRunAfterTests
{
    /// <summary>
    /// The server database version that introduced the run-after columns
    /// </summary>
    private const int RUN_AFTER_SCHEMA_VERSION = 14;

    private string _tempDataFolder = null!;
    private string _databasePath = null!;
    private Connection _connection = null!;

    /// <summary>
    /// Queue runner that records the tasks the scheduler adds, and reports them as waiting
    /// </summary>
    private sealed class RecordingQueueRunnerService : IQueueRunnerService
    {
        public List<IQueuedTask> Added { get; } = new();
        public bool ThrowOnAdd { get; set; }

        public IQueuedTask? GetCurrentTask() => null;
        public List<IQueuedTask> GetCurrentTasks() => [.. Added];
        public bool GetIsActive() => false;
        public CachedTaskResult? GetCachedTaskResults(long taskID) => null;
        public long AddTask(IQueuedTask task) => AddTask(task, false);
        public long AddTask(IQueuedTask task, bool skipQueue)
        {
            if (ThrowOnAdd)
                throw new InvalidOperationException("The queue is broken");
            Added.Add(task);
            return task.TaskID;
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
        _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-backup-run-after-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDataFolder);

        _databasePath = Path.Combine(_tempDataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);
        var dbConnection = await SQLiteLoader.LoadConnectionAsync(_databasePath);
        DatabaseUpgrader.UpgradeDatabase(dbConnection, _databasePath, typeof(DatabaseSchemaMarker));
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
    private static Backup BuildBackup(string name, string? runAfter, RunAfterCondition condition)
        => new Backup
        {
            Name = name,
            Description = "Run-after test",
            Tags = Array.Empty<string>(),
            TargetURL = "file:///mock_run_after_target",
            Sources = new[] { "Mock Source" },
            Settings = new ISetting[] { new Setting { Name = "passphrase", Value = "secret", Filter = string.Empty } },
            Filters = Array.Empty<IFilter>(),
            Metadata = new Dictionary<string, string>(),
            RunAfterBackupID = runAfter!,
            RunAfterCondition = condition,
        };

    private IBackup Save(string name, string? runAfter, RunAfterCondition condition)
    {
        var backup = BuildBackup(name, runAfter, condition);
        Assert.IsNull(_connection.ValidateBackup(backup, null), "The backup should pass validation.");
        _connection.AddOrUpdateBackupAndSchedule(backup, null);
        return backup;
    }

    private IBackup Save(string name)
        => Save(name, null, RunAfterCondition.Success);

    private IBackup Listed(string id) => _connection.Backups.Single(b => b.ID == id);
    private IBackup Loaded(string id) => _connection.GetBackup(id) ?? throw new Exception($"Backup {id} not found");

    /// <summary>
    /// Stores a changed reference on a saved backup, and returns the validation error, if any
    /// </summary>
    private string? TrySetRunAfter(IBackup backup, string? runAfter)
    {
        var update = (Backup)Loaded(backup.ID);
        update.RunAfterBackupID = runAfter!;
        var error = _connection.ValidateBackup(update, null);
        if (error == null)
            _connection.AddOrUpdateBackupAndSchedule(update, null);
        return error;
    }

    /// <summary>
    /// Builds the result of a backup run with the given outcome
    /// </summary>
    private static CachedTaskResult Finished(IBackup backup, ParsedResultType parsedResult, bool interrupted)
        => new CachedTaskResult(1, backup.ID, DateTime.UtcNow, DateTime.UtcNow, null, null,
            new TaskResultSummary(parsedResult, DateTime.UtcNow, DateTime.UtcNow, interrupted, 0, [], 0, [], null, null),
            DuplicatiOperation.BackupOrSync, false);

    /// <summary>
    /// Builds the result of a backup run that threw
    /// </summary>
    private static CachedTaskResult Threw(IBackup backup)
        => new CachedTaskResult(1, backup.ID, DateTime.UtcNow, DateTime.UtcNow, new Exception("The destination is gone"), null, null,
            DuplicatiOperation.BackupOrSync, false);

    /// <summary>
    /// Reports a finished task to a scheduler, and returns the IDs of the backups it queued
    /// </summary>
    private string?[] QueuedAfter(CachedTaskResult result)
    {
        var queue = new RecordingQueueRunnerService();
        new Scheduler(_connection, queue).OnTaskCompleted(result);
        return queue.Added.Select(x => x.BackupID).ToArray();
    }

    [Test]
    public void NewBackup_RunsAfterNothingByDefault()
    {
        var backup = Save("default");

        Assert.IsNull(Listed(backup.ID).RunAfterBackupID);
        Assert.IsNull(Loaded(backup.ID).RunAfterBackupID);
        Assert.AreEqual(RunAfterCondition.Success, Loaded(backup.ID).RunAfterCondition);
        CollectionAssert.IsEmpty(_connection.GetBackupIDsRunningAfter(backup.ID));
    }

    [Test]
    public void RunAfter_RoundTripsThroughBothReadPaths()
    {
        var first = Save("first");
        var second = Save("second", first.ID, RunAfterCondition.Always);

        Assert.AreEqual(first.ID, Listed(second.ID).RunAfterBackupID, "The backup list should report the stored reference.");
        Assert.AreEqual(RunAfterCondition.Always, Listed(second.ID).RunAfterCondition);
        Assert.AreEqual(first.ID, Loaded(second.ID).RunAfterBackupID, "GetBackup should report the stored reference.");
        Assert.AreEqual(RunAfterCondition.Always, Loaded(second.ID).RunAfterCondition);
        CollectionAssert.AreEqual(new[] { second.ID }, _connection.GetBackupIDsRunningAfter(first.ID));
    }

    [Test]
    public void RunAfter_CanBeChangedAndRemoved()
    {
        var first = Save("first");
        var other = Save("other");
        var second = Save("second", first.ID, RunAfterCondition.Success);

        Assert.IsNull(TrySetRunAfter(second, other.ID));
        Assert.AreEqual(other.ID, Loaded(second.ID).RunAfterBackupID);

        Assert.IsNull(TrySetRunAfter(second, null));
        Assert.IsNull(Loaded(second.ID).RunAfterBackupID);
        CollectionAssert.IsEmpty(_connection.GetBackupIDsRunningAfter(other.ID));
    }

    [Test]
    public void Validation_RejectsAMissingBackup()
    {
        Assert.IsNotNull(_connection.ValidateBackup(BuildBackup("orphan", "999999", RunAfterCondition.Success), null));
        Assert.IsNotNull(_connection.ValidateBackup(BuildBackup("not-a-number", "abc", RunAfterCondition.Success), null));
    }

    [Test]
    public void Validation_RejectsABackupThatRunsAfterItself()
    {
        var backup = Save("self");
        Assert.IsNotNull(TrySetRunAfter(backup, backup.ID));
        Assert.IsNull(Loaded(backup.ID).RunAfterBackupID);
    }

    [Test]
    public void Validation_RejectsLoops()
    {
        var a = Save("a");
        var b = Save("b", a.ID, RunAfterCondition.Success);
        var c = Save("c", b.ID, RunAfterCondition.Success);

        Assert.IsNotNull(TrySetRunAfter(a, b.ID), "A loop of two backups should be rejected.");
        Assert.IsNotNull(TrySetRunAfter(a, c.ID), "A loop of three backups should be rejected.");
        Assert.IsNull(Loaded(a.ID).RunAfterBackupID);

        // Two backups that run after the same backup are not a loop
        var d = Save("d", a.ID, RunAfterCondition.Success);
        CollectionAssert.AreEqual(new[] { b.ID, d.ID }, _connection.GetBackupIDsRunningAfter(a.ID));
    }

    [Test]
    public void DeletingABackup_KeepsTheBackupsThatRanAfterIt()
    {
        var first = Save("first");
        var second = Save("second", first.ID, RunAfterCondition.Always);

        _connection.DeleteBackup(Loaded(first.ID));

        var kept = Loaded(second.ID);
        Assert.IsNull(kept.RunAfterBackupID, "The reference to the deleted backup should be removed.");
        Assert.AreEqual("second", kept.Name);
    }

    [Test]
    public void Export_LeavesOutTheReference()
    {
        var first = Save("first");
        var second = Save("second", first.ID, RunAfterCondition.Success);

        Assert.IsNull(_connection.PrepareBackupForExport(Loaded(second.ID)).Backup.RunAfterBackupID,
            "A backup ID has no meaning on another server, so it should not be exported.");
        Assert.AreEqual(first.ID, Loaded(second.ID).RunAfterBackupID, "Exporting should not change the stored backup.");
    }

    [Test]
    public void Scheduler_QueuesTheNextBackupAfterASuccessfulRun()
    {
        var first = Save("first");
        var second = Save("second", first.ID, RunAfterCondition.Success);
        Save("unrelated");

        CollectionAssert.AreEqual(new[] { second.ID }, QueuedAfter(Finished(first, ParsedResultType.Success, false)));
        CollectionAssert.AreEqual(new[] { second.ID }, QueuedAfter(Finished(first, ParsedResultType.Warning, false)),
            "A run with warnings completed, so the next backup should run.");
    }

    [Test]
    public void Scheduler_DoesNotQueueTheNextBackupAfterAFailedRun()
    {
        var first = Save("first");
        Save("second", first.ID, RunAfterCondition.Success);

        CollectionAssert.IsEmpty(QueuedAfter(Finished(first, ParsedResultType.Error, false)));
        CollectionAssert.IsEmpty(QueuedAfter(Finished(first, ParsedResultType.Fatal, false)));
        CollectionAssert.IsEmpty(QueuedAfter(Finished(first, ParsedResultType.Success, true)), "A stopped run did not complete.");
        CollectionAssert.IsEmpty(QueuedAfter(Threw(first)));
    }

    [Test]
    public void Scheduler_QueuesTheNextBackupAfterAnyRunWhenSetToAlways()
    {
        var first = Save("first");
        var second = Save("second", first.ID, RunAfterCondition.Always);

        CollectionAssert.AreEqual(new[] { second.ID }, QueuedAfter(Finished(first, ParsedResultType.Success, false)));
        CollectionAssert.AreEqual(new[] { second.ID }, QueuedAfter(Finished(first, ParsedResultType.Error, false)));
        CollectionAssert.AreEqual(new[] { second.ID }, QueuedAfter(Finished(first, ParsedResultType.Success, true)));
        CollectionAssert.AreEqual(new[] { second.ID }, QueuedAfter(Threw(first)));
    }

    [Test]
    public void Scheduler_IgnoresRunsThatWereSkippedAndOtherOperations()
    {
        var first = Save("first");
        Save("second", first.ID, RunAfterCondition.Always);

        CollectionAssert.IsEmpty(QueuedAfter(new CachedTaskResult(1, first.ID, DateTime.UtcNow, DateTime.UtcNow, null, null, null, DuplicatiOperation.BackupOrSync, true)),
            "A run that was skipped before it started should not start other backups.");
        CollectionAssert.IsEmpty(QueuedAfter(Finished(first, ParsedResultType.Success, false) with { Operation = DuplicatiOperation.Compact }),
            "Only a backup run should start other backups.");
        CollectionAssert.IsEmpty(QueuedAfter(Finished(first, ParsedResultType.Success, false) with { BackupId = null }));
    }

    [Test]
    public void Scheduler_SkipsADisabledBackup()
    {
        var first = Save("first");
        var second = Save("second", first.ID, RunAfterCondition.Success);
        _connection.SetBackupEnabled(Loaded(second.ID), false);

        CollectionAssert.IsEmpty(QueuedAfter(Finished(first, ParsedResultType.Success, false)));
    }

    [Test]
    public void Scheduler_QueuesAllBackupsThatRunAfterTheSameBackupInIdOrder()
    {
        var first = Save("first");
        var second = Save("second", first.ID, RunAfterCondition.Success);
        var third = Save("third", first.ID, RunAfterCondition.Success);

        CollectionAssert.AreEqual(new[] { second.ID, third.ID }, QueuedAfter(Finished(first, ParsedResultType.Success, false)));
    }

    [Test]
    public void Scheduler_RunsAChainOneStepAtATime()
    {
        var first = Save("first");
        var second = Save("second", first.ID, RunAfterCondition.Success);
        var third = Save("third", second.ID, RunAfterCondition.Success);

        CollectionAssert.AreEqual(new[] { second.ID }, QueuedAfter(Finished(first, ParsedResultType.Success, false)),
            "Only the next backup in the chain should be queued.");
        CollectionAssert.AreEqual(new[] { third.ID }, QueuedAfter(Finished(second, ParsedResultType.Success, false)));
        CollectionAssert.IsEmpty(QueuedAfter(Finished(third, ParsedResultType.Success, false)));
    }

    [Test]
    public void Scheduler_DoesNotQueueABackupThatIsAlreadyWaiting()
    {
        var first = Save("first");
        var second = Save("second", first.ID, RunAfterCondition.Success);

        var queue = new RecordingQueueRunnerService();
        var scheduler = new Scheduler(_connection, queue);
        scheduler.OnTaskCompleted(Finished(first, ParsedResultType.Success, false));
        scheduler.OnTaskCompleted(Finished(first, ParsedResultType.Success, false));

        CollectionAssert.AreEqual(new[] { second.ID }, queue.Added.Select(x => x.BackupID).ToArray());
        Assert.AreEqual(DuplicatiOperation.BackupOrSync, queue.Added.Single().Operation);
    }

    [Test]
    public void Scheduler_DoesNotThrowWhenQueueingFails()
    {
        var first = Save("first");
        Save("second", first.ID, RunAfterCondition.Success);

        // The call comes from the task queue between two tasks, where an exception would stop the queue
        var scheduler = new Scheduler(_connection, new RecordingQueueRunnerService { ThrowOnAdd = true });
        Assert.DoesNotThrow(() => scheduler.OnTaskCompleted(Finished(first, ParsedResultType.Success, false)));
    }

    [Test]
    public async Task FreshSchema_HasRunAfterColumnsAsync()
    {
        await using var db = await SQLiteLoader.LoadConnectionAsync(_databasePath);
        Assert.GreaterOrEqual(await ReadVersionAsync(db), RUN_AFTER_SCHEMA_VERSION);
        Assert.IsTrue(await ColumnExistsAsync(db, "Backup", "RunAfterBackupID"));
        Assert.IsTrue(await ColumnExistsAsync(db, "Backup", "RunAfterCondition"));
    }

    [Test]
    public async Task UpgradeFromPreviousVersion_AddsRunAfterColumnsAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"duplicati-backup-run-after-upgrade-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "upgrade.sqlite");
        try
        {
            // Create the current schema, then rewind it to the previous version
            await using (var db = await SQLiteLoader.LoadConnectionAsync(dbPath))
            await using (var cmd = db.CreateCommand())
            {
                DatabaseUpgrader.UpgradeDatabase(db, dbPath, typeof(DatabaseSchemaMarker));

                cmd.CommandText = @"ALTER TABLE ""Backup"" DROP COLUMN ""RunAfterBackupID""";
                await cmd.ExecuteNonQueryAsync();
                cmd.CommandText = @"ALTER TABLE ""Backup"" DROP COLUMN ""RunAfterCondition""";
                await cmd.ExecuteNonQueryAsync();
                cmd.CommandText = @"INSERT INTO ""Backup"" (""Name"", ""Description"", ""Tags"", ""TargetURL"", ""DBPath"") VALUES ('legacy', '', 'tag', 'file:///legacy', 'legacy.sqlite')";
                await cmd.ExecuteNonQueryAsync();
                cmd.CommandText = $@"UPDATE ""Version"" SET ""Version"" = {RUN_AFTER_SCHEMA_VERSION - 1}";
                await cmd.ExecuteNonQueryAsync();
                await db.CloseAsync();
            }

            await using (var db = await SQLiteLoader.LoadConnectionAsync(dbPath))
            {
                DatabaseUpgrader.UpgradeDatabase(db, dbPath, typeof(DatabaseSchemaMarker));

                Assert.GreaterOrEqual(await ReadVersionAsync(db), RUN_AFTER_SCHEMA_VERSION,
                    "The upgrader should apply the run-after script.");
                Assert.IsTrue(await ColumnExistsAsync(db, "Backup", "RunAfterBackupID"));
                Assert.IsTrue(await ColumnExistsAsync(db, "Backup", "RunAfterCondition"));

                using var conn = new Connection(db, true, null, tempDir, () => { });
                var legacy = conn.Backups.Single(b => b.Name == "legacy");
                Assert.IsNull(legacy.RunAfterBackupID, "Backups that existed before the upgrade should run after nothing.");
                Assert.AreEqual(RunAfterCondition.Success, legacy.RunAfterCondition);
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Test]
    public async Task DatabaseToolDowngrade_RemovesRunAfterColumnsAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"duplicati-backup-run-after-downgrade-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "server.sqlite");
        try
        {
            await using (var db = await SQLiteLoader.LoadConnectionAsync(dbPath))
            {
                DatabaseUpgrader.UpgradeDatabase(db, dbPath, typeof(DatabaseSchemaMarker));
                await db.CloseAsync();
            }

            using (var conn = new Connection(await SQLiteLoader.LoadConnectionAsync(dbPath), true, null, tempDir, () => { }))
            {
                var first = BuildBackup("first", null, RunAfterCondition.Success);
                conn.AddOrUpdateBackupAndSchedule(first, null);
                conn.AddOrUpdateBackupAndSchedule(BuildBackup("second", first.ID, RunAfterCondition.Always), null);
            }

            Assert.AreEqual(0, await Duplicati.CommandLine.DatabaseTool.Program.MainAsync(
                ["downgrade", dbPath, $"--server-version={RUN_AFTER_SCHEMA_VERSION - 1}", "--no-backups"]),
                "The DatabaseTool downgrade should succeed.");

            await using (var db = await SQLiteLoader.LoadConnectionAsync(dbPath))
            {
                Assert.AreEqual(RUN_AFTER_SCHEMA_VERSION - 1, await ReadVersionAsync(db));
                Assert.IsFalse(await ColumnExistsAsync(db, "Backup", "RunAfterBackupID"));
                Assert.IsFalse(await ColumnExistsAsync(db, "Backup", "RunAfterCondition"));

                using var cmd = db.CreateCommand();
                cmd.CommandText = @"SELECT COUNT(*) FROM ""Backup""";
                Assert.AreEqual(2, Convert.ToInt64(await cmd.ExecuteScalarAsync()),
                    "The backup rows should survive the downgrade.");
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static async Task<int> ReadVersionAsync(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT max(""Version"") FROM ""Version""";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task<bool> ColumnExistsAsync(Microsoft.Data.Sqlite.SqliteConnection db, string table, string column)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            if (string.Equals(rd.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
