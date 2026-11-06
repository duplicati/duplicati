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
/// Tests the per-backup <see cref="IBackup.Enabled"/> flag: that it is persisted,
/// defaults to enabled, survives export, is added and removed by the schema
/// upgrade and downgrade, and that the scheduler skips disabled backups.
/// </summary>
[TestFixture]
[Category("BackupEnabled")]
public class BackupEnabledTests
{
    /// <summary>
    /// The server database version that introduced the Enabled column
    /// </summary>
    private const int ENABLED_SCHEMA_VERSION = 13;

    private string _tempDataFolder = null!;
    private string _databasePath = null!;
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
        _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-backup-enabled-test-{Guid.NewGuid()}");
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
    private static Backup BuildBackup(string name, bool enabled = true)
        => new Backup
        {
            Name = name,
            Description = "Enabled flag test",
            Tags = Array.Empty<string>(),
            TargetURL = "file:///mock_enabled_target",
            Sources = new[] { "Mock Source" },
            Settings = new ISetting[] { new Setting { Name = "passphrase", Value = "secret", Filter = string.Empty } },
            Filters = Array.Empty<IFilter>(),
            Metadata = new Dictionary<string, string>(),
            Enabled = enabled,
        };

    /// <summary>
    /// Builds a daily schedule whose first run is in the past, so it is due immediately
    /// </summary>
    private static Schedule BuildDueSchedule()
        => new Schedule
        {
            Tags = Array.Empty<string>(),
            Time = DateTime.UtcNow.AddHours(-1),
            Repeat = "1D",
            LastRun = new DateTime(0, DateTimeKind.Utc),
            Rule = string.Empty,
        };

    private IBackup Save(Backup backup, ISchedule? schedule = null)
    {
        _connection.AddOrUpdateBackupAndSchedule(backup, schedule);
        return backup;
    }

    private IBackup Listed(string id) => _connection.Backups.Single(b => b.ID == id);
    private IBackup Loaded(string id) => _connection.GetBackup(id) ?? throw new Exception($"Backup {id} not found");

    [Test]
    public void NewBackup_IsEnabledByDefault()
    {
        var backup = Save(BuildBackup("default-enabled"));

        Assert.IsTrue(Listed(backup.ID).Enabled, "The backup list should report a new backup as enabled.");
        Assert.IsTrue(Loaded(backup.ID).Enabled, "GetBackup should report a new backup as enabled.");
    }

    [Test]
    public void DisabledBackup_RoundTripsThroughBothReadPaths()
    {
        var backup = Save(BuildBackup("saved-disabled", enabled: false));

        Assert.IsFalse(Listed(backup.ID).Enabled, "The backup list should report the stored disabled flag.");
        Assert.IsFalse(Loaded(backup.ID).Enabled, "GetBackup should report the stored disabled flag.");
    }

    [Test]
    public void UpdatingABackup_WritesTheEnabledFlag()
    {
        var backup = Save(BuildBackup("updated-flag"));

        var update = (Backup)Loaded(backup.ID);
        update.Enabled = false;
        _connection.AddOrUpdateBackupAndSchedule(update, null);

        Assert.IsFalse(Loaded(backup.ID).Enabled, "Saving an existing backup should persist a changed Enabled flag.");
    }

    [Test]
    public void SetBackupEnabled_TogglesOnlyTheFlag()
    {
        var backup = Save(BuildBackup("toggled", enabled: true));

        _connection.SetBackupEnabled(Loaded(backup.ID), false);
        var disabled = Loaded(backup.ID);
        Assert.IsFalse(disabled.Enabled, "SetBackupEnabled(false) should disable the backup.");
        Assert.AreEqual("toggled", disabled.Name, "Toggling should not change other fields.");
        Assert.AreEqual("secret", disabled.Settings.Single(x => x.Name == "passphrase").Value,
            "Toggling should not touch the backup settings.");
        CollectionAssert.AreEquivalent(new[] { backup.ID }, _connection.GetDisabledBackupIDs());

        _connection.SetBackupEnabled(Loaded(backup.ID), true);
        Assert.IsTrue(Loaded(backup.ID).Enabled, "SetBackupEnabled(true) should enable the backup again.");
        Assert.IsEmpty(_connection.GetDisabledBackupIDs());
    }

    [Test]
    public void SetBackupEnabled_RejectsTemporaryBackups()
    {
        var id = _connection.RegisterTemporaryBackup(BuildBackup("temporary"), null);
        var temporary = _connection.GetBackup(id) ?? throw new Exception("Temporary backup not found");

        Assert.Throws<InvalidOperationException>(() => _connection.SetBackupEnabled(temporary, false));
    }

    [Test]
    public void Export_KeepsTheDisabledFlag()
    {
        var backup = Save(BuildBackup("exported", enabled: false));

        var export = _connection.PrepareBackupForExport(Loaded(backup.ID));
        Assert.IsFalse(export.Backup.Enabled, "An exported disabled backup should stay disabled when imported.");
    }

    [Test]
    public async Task FreshSchema_HasEnabledColumnDefaultingToEnabledAsync()
    {
        await using var db = await SQLiteLoader.LoadConnectionAsync(_databasePath);
        Assert.AreEqual(ENABLED_SCHEMA_VERSION, await ReadVersionAsync(db),
            "A fresh server database should be created at the latest schema version.");
        Assert.IsTrue(await ColumnExistsAsync(db, "Backup", "Enabled"),
            "The Enabled column should exist on a fresh database.");

        // A row written without the column must be enabled through the column default
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText =
                @"INSERT INTO ""Backup"" (""Name"", ""Tags"", ""TargetURL"", ""DBPath"") " +
                @"VALUES ('raw-row', '', 'file:///raw', 'raw.sqlite')";
            await cmd.ExecuteNonQueryAsync();
        }

        Assert.IsTrue(_connection.Backups.Single(b => b.Name == "raw-row").Enabled,
            "A row inserted without the Enabled column should default to enabled.");
    }

    [Test]
    public async Task UpgradeFromPreviousVersion_AddsEnabledColumnAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"duplicati-backup-enabled-upgrade-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "upgrade.sqlite");
        try
        {
            // Create the current schema, then rewind it to the previous version
            await using (var db = await SQLiteLoader.LoadConnectionAsync(dbPath))
            await using (var cmd = db.CreateCommand())
            {
                DatabaseUpgrader.UpgradeDatabase(db, dbPath, typeof(DatabaseSchemaMarker));

                cmd.CommandText = @"ALTER TABLE ""Backup"" DROP COLUMN ""Enabled""";
                await cmd.ExecuteNonQueryAsync();
                cmd.CommandText = @"INSERT INTO ""Backup"" (""Name"", ""Description"", ""Tags"", ""TargetURL"", ""DBPath"") VALUES ('legacy', '', 'tag', 'file:///legacy', 'legacy.sqlite')";
                await cmd.ExecuteNonQueryAsync();
                cmd.CommandText = $@"UPDATE ""Version"" SET ""Version"" = {ENABLED_SCHEMA_VERSION - 1}";
                await cmd.ExecuteNonQueryAsync();
                await db.CloseAsync();
            }

            await using (var db = await SQLiteLoader.LoadConnectionAsync(dbPath))
            {
                DatabaseUpgrader.UpgradeDatabase(db, dbPath, typeof(DatabaseSchemaMarker));

                Assert.AreEqual(ENABLED_SCHEMA_VERSION, await ReadVersionAsync(db),
                    "The upgrader should apply the Enabled column script.");
                Assert.IsTrue(await ColumnExistsAsync(db, "Backup", "Enabled"),
                    "The upgrade script should add the Enabled column.");

                using var conn = new Connection(db, true, null, tempDir, () => { });
                Assert.IsTrue(conn.Backups.Single(b => b.Name == "legacy").Enabled,
                    "Backups that existed before the upgrade should be enabled.");
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Test]
    public async Task DatabaseToolDowngrade_RemovesEnabledColumnAndKeepsForeignKeysAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"duplicati-backup-enabled-downgrade-{Guid.NewGuid()}");
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
                conn.AddOrUpdateBackupAndSchedule(BuildBackup("downgraded", enabled: false), null);

            Assert.AreEqual(0, await Duplicati.CommandLine.DatabaseTool.Program.MainAsync(
                ["downgrade", dbPath, $"--server-version={ENABLED_SCHEMA_VERSION - 1}", "--no-backups"]),
                "The DatabaseTool downgrade should succeed.");

            await using (var db = await SQLiteLoader.LoadConnectionAsync(dbPath))
            {
                Assert.AreEqual(ENABLED_SCHEMA_VERSION - 1, await ReadVersionAsync(db));
                Assert.IsFalse(await ColumnExistsAsync(db, "Backup", "Enabled"),
                    "The downgrade script should remove the Enabled column.");

                using var cmd = db.CreateCommand();
                cmd.CommandText = @"SELECT COUNT(*) FROM ""Backup""";
                Assert.AreEqual(1, Convert.ToInt64(await cmd.ExecuteScalarAsync()),
                    "The backup row should survive the downgrade.");

                cmd.CommandText = @"SELECT ""table"" FROM pragma_foreign_key_list('BackupTargetUrl')";
                Assert.AreEqual("Backup", Convert.ToString(await cmd.ExecuteScalarAsync()),
                    "The BackupTargetUrl foreign key should still reference the Backup table.");
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Test]
    public void Scheduler_SkipsDisabledBackupsAndHidesThemFromTheProposedSchedule()
    {
        var enabled = Save(BuildBackup("scheduled-enabled", enabled: true), BuildDueSchedule());
        var disabled = Save(BuildBackup("scheduled-disabled", enabled: false), BuildDueSchedule());

        var queue = new RecordingQueueRunnerService();
        var scheduler = new Scheduler(_connection, queue);
        try
        {
            scheduler.Reschedule();
            Assert.IsTrue(queue.TaskAdded.Wait(TimeSpan.FromSeconds(30)),
                "The scheduler should queue the enabled backup whose schedule is due.");
        }
        finally
        {
            // Terminating waits for the scheduler pass to finish, so every task it queues is recorded
            scheduler.Terminate(true);
        }

        CollectionAssert.AreEqual(new[] { enabled.ID }, queue.Added.Select(x => x.BackupID).ToArray(),
            "Only the enabled backup should be queued.");

        var proposed = scheduler.GetProposedSchedule().Select(x => x.Item1).ToArray();
        CollectionAssert.AreEqual(new[] { enabled.ID }, proposed,
            "The proposed schedule should not list a disabled backup.");

        // Enabling the backup again brings it back into the proposed schedule
        _connection.SetBackupEnabled(Loaded(disabled.ID), true);
        CollectionAssert.AreEquivalent(new[] { enabled.ID, disabled.ID },
            scheduler.GetProposedSchedule().Select(x => x.Item1).ToArray());
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
