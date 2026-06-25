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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.RestAPI.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests the <see cref="OperationType"/> backup configuration field: that it is
/// persisted to and read back from the RestAPI database, that the default is
/// <see cref="OperationType.Backup"/>, and that a <see cref="OperationType.Sync"/>
/// backup is rejected by validation when encryption is enabled (a sync mirrors
/// files unencrypted, so a passphrase is meaningless and must not be set).
/// </summary>
[TestFixture]
[Category("OperationType")]
public class OperationTypeTests
{
    private string _tempDataFolder = null!;
    private string _databasePath = null!;
    private Connection _connection = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-op-type-test-{Guid.NewGuid()}");
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
    /// Builds a minimal backup with the given operation type and an optional
    /// passphrase setting, mirroring how the web UI constructs a backup.
    /// </summary>
    private static IBackup BuildBackup(OperationType operationType, bool includePassphrase)
    {
        var settings = new List<ISetting>();
        if (includePassphrase)
            settings.Add(new Setting { Name = "passphrase", Value = "secret", Filter = string.Empty });

        // For Backup, a passphrase is required by validation; for Sync, encryption
        // (and thus a passphrase) is rejected. Include a non-secret setting so the
        // round-trip has something to compare.
        settings.Add(new Setting { Name = "backup-name", Value = "nonsecret", Filter = string.Empty });

        return new Backup
        {
            Name = $"op-type-test-{operationType}",
            Description = "OperationType test",
            Tags = Array.Empty<string>(),
            TargetURL = "file:///mock_op_type_target",
            Sources = new[] { "Mock Source" },
            Settings = settings.ToArray(),
            Filters = Array.Empty<IFilter>(),
            Metadata = new Dictionary<string, string>(),
            OperationType = operationType,
        };
    }

    /// <summary>
    /// Returns the fully-loaded backup (with settings) for a saved name. The
    /// <see cref="Connection.Backups"/> listing returns partially-populated
    /// objects (no settings), so use <see cref="Connection.GetBackup(string)"/>
    /// which calls <c>LoadChildren</c> to load settings/filters/sources.
    /// </summary>
    private IBackup LoadFull(string name)
    {
        var listed = _connection.Backups.Single(b => b.Name == name);
        return _connection.GetBackup(listed.ID) ?? throw new Exception($"Backup '{name}' not found");
    }

    /// <summary>
    /// A Backup-typed backup should round-trip its OperationType through the database
    /// and keep its passphrase setting.
    /// </summary>
    [Test]
    public void BackupOperationType_RoundTripsAndKeepsPassphrase()
    {
        var backup = BuildBackup(OperationType.Backup, includePassphrase: true);
        _connection.AddOrUpdateBackupAndSchedule(backup, null);

        var loaded = LoadFull(backup.Name);
        Assert.AreEqual(OperationType.Backup, loaded.OperationType,
            "The default Backup operation type should round-trip through the database.");
        Assert.IsNotNull(loaded.Settings.SingleOrDefault(s => string.Equals(s.Name, "passphrase", StringComparison.OrdinalIgnoreCase)),
            "A Backup-typed backup should keep its passphrase setting.");
    }

    /// <summary>
    /// A Sync-typed backup with a passphrase (encryption enabled) must be rejected by
    /// <see cref="Connection.ValidateBackup"/>: sync mirrors files unencrypted, so a
    /// passphrase is meaningless and must not be set. The save path itself does not
    /// strip the passphrase - the web layer is expected to honor the validation
    /// result and refuse to persist such a configuration.
    /// </summary>
    [Test]
    public void SyncOperationType_WithPassphrase_IsRejectedByValidation()
    {
        var backup = BuildBackup(OperationType.Sync, includePassphrase: true);
        var error = _connection.ValidateBackup(backup, null);
        Assert.IsNotNull(error, "A Sync-typed backup with a passphrase should fail validation.");
        StringAssert.IsMatch("encryption", (error ?? string.Empty).ToLowerInvariant(),
            "The validation error should explain that encryption is not allowed for sync operations.");
    }

    /// <summary>
    /// A Sync-typed backup without a passphrase and with encryption disabled should
    /// validate and round-trip its OperationType through the database, preserving
    /// its non-passphrase settings.
    /// </summary>
    [Test]
    public void SyncOperationType_WithoutPassphrase_RoundTrips()
    {
        var backup = BuildBackup(OperationType.Sync, includePassphrase: false);
        // A Sync backup must disable encryption; without that it would be rejected.
        backup.Settings = (backup.Settings ?? Array.Empty<ISetting>())
            .Concat(new[] { new Setting { Name = "--no-encryption", Value = "true", Filter = string.Empty } })
            .ToArray();

        var error = _connection.ValidateBackup(backup, null);
        Assert.IsNull(error, $"A Sync-typed backup with encryption disabled should validate, but got: {error}");

        _connection.AddOrUpdateBackupAndSchedule(backup, null);

        var loaded = LoadFull(backup.Name);
        Assert.AreEqual(OperationType.Sync, loaded.OperationType,
            "The Sync operation type should round-trip through the database.");
        Assert.IsNotNull(loaded.Settings.SingleOrDefault(s => string.Equals(s.Name, "backup-name", StringComparison.OrdinalIgnoreCase)),
            "Non-passphrase settings should be preserved.");
    }

    /// <summary>
    /// Converting an existing Backup-typed backup to a Sync operation type while a
    /// passphrase is still set must be rejected by validation, so an in-place
    /// conversion to Sync cannot leave a persisted passphrase behind. The caller is
    /// expected to drop the passphrase (and disable encryption) before the save.
    /// </summary>
    [Test]
    public void UpdatingBackupToSync_WithExistingPassphrase_IsRejectedByValidation()
    {
        // Start as a Backup with a passphrase.
        var backup = BuildBackup(OperationType.Backup, includePassphrase: true);
        _connection.AddOrUpdateBackupAndSchedule(backup, null);
        var stored = LoadFull(backup.Name);
        Assert.IsNotNull(stored.Settings.SingleOrDefault(s => string.Equals(s.Name, "passphrase", StringComparison.OrdinalIgnoreCase)));

        // Convert to Sync while the passphrase is still present; validation must reject.
        stored.OperationType = OperationType.Sync;
        stored.Settings = stored.Settings
            .Concat(new[] { new Setting { Name = "passphrase", Value = "still-here", Filter = string.Empty } })
            .ToArray();
        var error = _connection.ValidateBackup(stored, null);
        Assert.IsNotNull(error,
            "Converting a backup to Sync while a passphrase is set should fail validation.");
    }

    /// <summary>
    /// A backup loaded via <see cref="Connection.GetBackup(string)"/> should carry the
    /// persisted OperationType (this exercises the per-id SELECT path, not just the
    /// list path).
    /// </summary>
    [Test]
    public void GetBackupById_ReturnsPersistedOperationType()
    {
        var backup = BuildBackup(OperationType.Sync, includePassphrase: false);
        _connection.AddOrUpdateBackupAndSchedule(backup, null);

        var byId = LoadFull(backup.Name);
        Assert.AreEqual(OperationType.Sync, byId.OperationType,
            "GetBackup(id) should return the persisted OperationType.");
    }

    /// <summary>
    /// A fresh database (created from the embedded <c>Schema.sql</c>) must record the
    /// latest schema version and have the <c>OperationType</c> column on the Backup
    /// table. The column's <c>DEFAULT 'Backup'</c> must take effect for a row inserted
    /// without specifying <c>OperationType</c> (i.e. the default is enforced by the
    /// database, not just by the C# layer which always writes an explicit value).
    /// </summary>
    [Test]
    public async Task FreshSchema_HasOperationTypeColumnAndLatestVersionAsync()
    {
        // The fresh database created in SetUp is at the latest version (12).
        const int EXPECTED_LATEST = 12;
        await using var freshConn = await SQLiteLoader.LoadConnectionAsync(_databasePath);
        Assert.AreEqual(EXPECTED_LATEST, await ReadVersionAsync(freshConn),
            "A fresh RestAPI database should be created at the latest schema version (12).");

        Assert.IsTrue(await ColumnExistsAsync(freshConn, "Backup", "OperationType"),
            "The OperationType column should exist on a fresh database.");

        // Insert a backup row via raw SQL, deliberately omitting OperationType so the
        // column's DEFAULT 'Backup' is the only thing that can populate it. The Backup
        // table requires Name, Tags, TargetURL and DBPath (NOT NULL, no default). The
        // values are test constants, so inline literals are safe and match the style of
        // the other raw-SQL assertions in this fixture.
        const string backupName = "default-op-type";
        await using (var insertConn = await SQLiteLoader.LoadConnectionAsync(_databasePath))
        using (var cmd = insertConn.CreateCommand())
        {
            cmd.CommandText =
                @"INSERT INTO ""Backup"" (""Name"", ""Tags"", ""TargetURL"", ""DBPath"") " +
                @"VALUES ('" + backupName + "', '', 'file:///mock_default_target', 'default-op-type.sqlite')";
            await cmd.ExecuteNonQueryAsync();
        }

        // Reading through Connection.Backups parses the stored value with
        // OperationType.Backup as the fallback; the column default should make this
        // read back as Backup without the fallback ever being needed.
        var loaded = _connection.Backups.Single(b => b.Name == backupName);
        Assert.AreEqual(OperationType.Backup, loaded.OperationType,
            "A row inserted without specifying OperationType should default to Backup via the column DEFAULT.");
    }

    /// <summary>
    /// An existing database left at schema version 11 (before the OperationType
    /// column was added) must be upgraded to version 12 by the DatabaseUpgrader, and
    /// the OperationType column must then be present and default to 'Backup' for the
    /// rows that pre-date the upgrade.
    /// </summary>
    [Test]
    public async Task UpgradeFromV11_AddsOperationTypeColumnAndBumpsVersionAsync()
    {
        // Use a separate database so this test does not rely on the SetUp fresh DB.
        var tempDir = Path.Combine(Path.GetTempPath(), $"duplicati-op-type-upgrade-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "upgrade.sqlite");
        try
        {
            // Start from the full current schema (v12), then rewind it to look like the
            // previous version: drop the column the v12 upgrade script introduces and
            // pin the Version row to 11. This mirrors the DatabaseUpgraderTests approach
            // and ensures the full schema (Option table, etc.) is present so a Connection
            // can be opened after the upgrade. A legacy backup row is inserted before the
            // upgrade to verify pre-existing rows default to Backup.
            await using (var db = await SQLiteLoader.LoadConnectionAsync(dbPath))
            await using (var cmd = db.CreateCommand())
            {
                DatabaseUpgrader.UpgradeDatabase(db, dbPath, typeof(DatabaseSchemaMarker));
                Assert.AreEqual(12, await ReadVersionAsync(db));

                // The v12 upgrade script does: ALTER TABLE "Backup" ADD COLUMN "OperationType" ...
                // Drop it to simulate the pre-upgrade (v11) state.
                cmd.CommandText = @"ALTER TABLE ""Backup"" DROP COLUMN ""OperationType""";
                await cmd.ExecuteNonQueryAsync();

                // Insert a legacy backup row that predates the column.
                cmd.CommandText = @"INSERT INTO ""Backup"" (""Name"", ""Description"", ""Tags"", ""TargetURL"", ""DBPath"") VALUES ('legacy', '', 'tag', 'file:///legacy', 'legacy.sqlite')";
                await cmd.ExecuteNonQueryAsync();

                // Pin the recorded version to 11 so the upgrader has an outstanding upgrade.
                cmd.CommandText = @"UPDATE ""Version"" SET ""Version"" = 11";
                await cmd.ExecuteNonQueryAsync();
                await db.CloseAsync();
            }

            // Reopen and run the upgrader; it should discover and apply script 12.
            await using (var db = await SQLiteLoader.LoadConnectionAsync(dbPath))
            {
                DatabaseUpgrader.UpgradeDatabase(db, dbPath, typeof(DatabaseSchemaMarker));

                Assert.AreEqual(12, await ReadVersionAsync(db),
                    "The upgrader should bump the RestAPI database from 11 to 12.");
                Assert.IsTrue(await ColumnExistsAsync(db, "Backup", "OperationType"),
                    "The v12 upgrade script should add the OperationType column.");

                // The legacy row must read back with the default Backup operation type.
                using var conn = new Connection(db, true, null, tempDir, () => { });
                var loaded = conn.Backups.Single(b => b.Name == "legacy");
                Assert.AreEqual(OperationType.Backup, loaded.OperationType,
                    "Rows that pre-date the OperationType column should default to Backup after the upgrade.");
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// The DatabaseTool <c>downgrade</c> command must be able to downgrade a server
    /// database from version 12 back to 11 by removing the OperationType column. This
    /// verifies the downgrade script that pairs with the v12 upgrade: after the
    /// downgrade the OperationType column is gone, the version is 11, and a backup
    /// row that was present is preserved.
    /// </summary>
    [Test]
    public async Task DatabaseToolDowngradeFromV12_RemovesOperationTypeColumnAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"duplicati-op-type-downgrade-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "server.sqlite");
        try
        {
            // Start from a fresh v12 server database with a saved backup row.
            await using (var db = await SQLiteLoader.LoadConnectionAsync(dbPath))
            {
                DatabaseUpgrader.UpgradeDatabase(db, dbPath, typeof(DatabaseSchemaMarker));
                Assert.AreEqual(12, await ReadVersionAsync(db));
                Assert.IsTrue(await ColumnExistsAsync(db, "Backup", "OperationType"));
                await db.CloseAsync();
            }

            using (var conn = new Connection(
                await SQLiteLoader.LoadConnectionAsync(dbPath), true, null, tempDir, () => { }))
            {
                conn.AddOrUpdateBackupAndSchedule(BuildBackup(OperationType.Sync, includePassphrase: false), null);
            }

            // Downgrade the server database from 12 to 11 via the DatabaseTool.
            Assert.AreEqual(0, await Duplicati.CommandLine.DatabaseTool.Program.MainAsync(["downgrade", dbPath, "--server-version=11", "--no-backups"]),
                "The DatabaseTool downgrade from v12 to v11 should succeed.");

            await using (var db = await SQLiteLoader.LoadConnectionAsync(dbPath))
            {
                Assert.AreEqual(11, await ReadVersionAsync(db),
                    "After the downgrade the server database version should be 11.");
                Assert.IsFalse(await ColumnExistsAsync(db, "Backup", "OperationType"),
                    "The OperationType column should be removed by the v12 downgrade script.");

                // The backup row (and the rest of the schema) must survive the table recreation.
                using var cmd = db.CreateCommand();
                cmd.CommandText = @"SELECT COUNT(*) FROM ""Backup""";
                Assert.AreEqual(1, Convert.ToInt64(await cmd.ExecuteScalarAsync()),
                    "The Backup row should be preserved across the downgrade table recreation.");
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
