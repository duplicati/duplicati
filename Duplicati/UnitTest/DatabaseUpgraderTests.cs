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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests that <see cref="DatabaseUpgrader.UpgradeDatabase"/> correctly loads
    /// the embedded <c>Schema.sql</c> on a fresh database and identifies/applies
    /// the staged numbered upgrade scripts to bring the database to the latest
    /// schema version. The schema is resolved via the LocalDatabase schema marker,
    /// which is the assembly carrying the <c>Database_schema</c> embedded resources.
    /// </summary>
    [TestFixture]
    [Category("Database")]
    public class DatabaseUpgraderTests
    {
        /// <summary>
        /// The schema version that <c>Schema.sql</c> inserts into the <c>Version</c>
        /// table on a fresh install. This corresponds to the value written by the
        /// last line of <c>Duplicati/Library/Main/Database/Local/Database schema/Schema.sql</c>
        /// and must be kept in sync if the schema is bumped.
        /// </summary>
        private const int EXPECTED_LATEST_SCHEMA_VERSION = 19;

        /// <summary>
        /// The most recent numbered upgrade script in the LocalDatabase schema
        /// resources, e.g. <c>19. Add metadata content column.sql</c>. This is the
        /// last upgrade the upgrader should be able to discover and apply.
        /// </summary>
        private const int EXPECTED_HIGHEST_UPGRADE_SCRIPT = 19;

        /// <summary>
        /// Calling <c>UpgradeDatabase</c> on a fresh (empty) database should load the
        /// latest schema directly and record the current schema version, so that no
        /// numbered upgrade scripts need to run.
        /// </summary>
        [Test]
        public async Task UpgradeDatabase_OnFreshDatabase_LoadsLatestSchemaAndVersionAsync()
        {
            using var dbfile = new TempFile();
            using var db = await SQLiteLoader.LoadConnectionAsync(dbfile);

            // Fresh database: no Version table exists yet, so this exercises the
            // dbversion == -1 path inside DatabaseUpgrader.
            DatabaseUpgrader.UpgradeDatabase(db, dbfile, typeof(DatabaseSchemaMarker));

            var version = await ReadVersionAsync(db);
            Assert.AreEqual(EXPECTED_LATEST_SCHEMA_VERSION, version,
                "Fresh install should be created at the latest schema version without running upgrades.");

            // The schema should be usable: a core table defined in Schema.sql must exist.
            Assert.IsTrue(await TableExistsAsync(db, "Version"),
                "The Version table should exist after upgrading.");
            Assert.IsTrue(await TableExistsAsync(db, "Configuration"),
                "The Configuration table should exist after upgrading.");
        }

        /// <summary>
        /// When a database reports an older schema version than the latest, the upgrader
        /// must discover the numbered upgrade scripts embedded in the schema assembly and
        /// apply the missing ones in order until the database reaches the latest version.
        ///
        /// This simulates a database left at version <c>EXPECTED_LATEST_SCHEMA_VERSION - 1</c>
        /// by loading the current schema, removing the artifact introduced by the final
        /// upgrade script (so that re-applying it is valid), and pinning the Version row
        /// one below the latest. The upgrader should then locate and run that last upgrade
        /// script and bump the recorded version to <c>EXPECTED_LATEST_SCHEMA_VERSION</c>.
        /// </summary>
        [Test]
        public async Task UpgradeDatabase_OnOldVersion_AppliesNumberedUpgradeScriptsAsync()
        {
            using var dbfile = new TempFile();

            using (var db = await SQLiteLoader.LoadConnectionAsync(dbfile))
            using (var cmd = db.CreateCommand())
            {
                // Load the current schema, then rewind it to look like the previous
                // version: drop the column the most recent upgrade script introduces.
                DatabaseUpgrader.UpgradeDatabase(db, dbfile, typeof(DatabaseSchemaMarker));

                // The final upgrade script ("19. Add metadata content column.sql") does:
                //   ALTER TABLE "Metadataset" ADD COLUMN "Content" TEXT NULL;
                // Remove that column to simulate the pre-upgrade (version 18) state so the
                // upgrader has an outstanding upgrade to apply. ALTER TABLE DROP COLUMN
                // requires SQLite >= 3.35.0, which the bundled Microsoft.Data.Sqlite
                // runtime ships (3.46.x). If older SQLite versions ever need supporting,
                // fall back to recreating the table without the column.
                cmd.CommandText = @"ALTER TABLE ""Metadataset"" DROP COLUMN ""Content""";
                await cmd.ExecuteNonQueryAsync();

                // Pin the recorded version one below the latest so the upgrader has work to do.
                cmd.CommandText = @"UPDATE ""Version"" SET ""Version"" = @ver";
                cmd.Parameters.AddWithValue("@ver", EXPECTED_LATEST_SCHEMA_VERSION - 1);
                await cmd.ExecuteNonQueryAsync();
                await db.CloseAsync();
            }

            using (var db = await SQLiteLoader.LoadConnectionAsync(dbfile))
            {
                // dbversion == (latest - 1) < versions.Count, so the final upgrade runs.
                DatabaseUpgrader.UpgradeDatabase(db, dbfile, typeof(DatabaseSchemaMarker));

                var version = await ReadVersionAsync(db);
                Assert.AreEqual(EXPECTED_LATEST_SCHEMA_VERSION, version,
                    "Upgrader should apply the outstanding upgrade script and reach the latest version.");

                // The column added by the final upgrade script must now exist again,
                // proving the upgrade script was actually executed and not just skipped.
                Assert.IsTrue(await ColumnExistsAsync(db, "Metadataset", "Content"),
                    "The Metadataset.Content column (added by the final upgrade script) should exist after the upgrade.");
            }
        }

        /// <summary>
        /// The backup taken before the numbered upgrade scripts run must carry an
        /// unambiguous timestamp. The format string used to specify <c>hh</c> (12-hour
        /// clock) without an AM/PM designator, so a backup taken at 13:30 was named as
        /// if it had been taken at 01:30 and collided with one taken in the morning,
        /// forcing the surrounding retry loop to shift the timestamp for no reason.
        ///
        /// Note that the 12/24-hour part of this test only discriminates when the local
        /// time is 13:00 or later; before noon the two formats agree. It never fails
        /// spuriously, and a month/minute mix-up is caught at any time of day.
        /// </summary>
        [Test]
        public async Task UpgradeDatabase_BackupFileName_UsesUnambiguousTimestampAsync()
        {
            using var dbfile = new TempFile();
            await RewindToPreviousVersionAsync(dbfile);

            var folder = Path.GetDirectoryName(Path.GetFullPath(dbfile))!;
            var name = Path.GetFileNameWithoutExtension(dbfile);

            var before = DateTime.Now;
            using (var db = await SQLiteLoader.LoadConnectionAsync(dbfile))
                DatabaseUpgrader.UpgradeDatabase(db, dbfile, typeof(DatabaseSchemaMarker));
            var after = DateTime.Now;

            // The backup is named "<prefix> <database name> <timestamp>.sqlite". Match on
            // the database name so the test does not depend on the prefix, which is a
            // localized string internal to Duplicati.Library.SQLiteHelper.
            var backups = Directory.GetFiles(folder, "* " + name + " *.sqlite");
            Assert.AreEqual(1, backups.Length, "The upgrade should have created exactly one backup file.");

            try
            {
                var stamp = Path.GetFileNameWithoutExtension(backups[0]).Split(' ').Last();
                Assert.IsTrue(
                    DateTime.TryParseExact(stamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed),
                    $"The backup timestamp \"{stamp}\" is not a valid yyyyMMddHHmmss value.");

                // The upgrader may add up to 15 seconds to dodge a name collision.
                Assert.IsTrue(parsed >= before.AddSeconds(-5) && parsed <= after.AddSeconds(20),
                    $"The backup timestamp \"{stamp}\" does not match the time the backup was taken ({before:yyyyMMddHHmmss}).");
            }
            finally
            {
                File.Delete(backups[0]);
            }
        }

        /// <summary>
        /// Brings the database to the latest schema and then rewinds it to the previous
        /// version, so that the upgrader has exactly one outstanding upgrade script to
        /// apply. This mirrors the setup used by
        /// <see cref="UpgradeDatabase_OnOldVersion_AppliesNumberedUpgradeScriptsAsync"/>.
        /// </summary>
        private static async Task RewindToPreviousVersionAsync(string dbfile)
        {
            using var db = await SQLiteLoader.LoadConnectionAsync(dbfile);
            using var cmd = db.CreateCommand();

            DatabaseUpgrader.UpgradeDatabase(db, dbfile, typeof(DatabaseSchemaMarker));

            // Drop the column the most recent upgrade script introduces, so that
            // re-applying that script is valid.
            cmd.CommandText = @"ALTER TABLE ""Metadataset"" DROP COLUMN ""Content""";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = @"UPDATE ""Version"" SET ""Version"" = @ver";
            cmd.Parameters.AddWithValue("@ver", EXPECTED_LATEST_SCHEMA_VERSION - 1);
            await cmd.ExecuteNonQueryAsync();
            await db.CloseAsync();
        }

        /// <summary>
        /// Running <c>UpgradeDatabase</c> against a database that is already at the
        /// latest version should be a no-op and leave the version unchanged.
        /// </summary>
        [Test]
        public async Task UpgradeDatabase_OnLatestVersion_IsNoOpAsync()
        {
            using var dbfile = new TempFile();
            using var db = await SQLiteLoader.LoadConnectionAsync(dbfile);

            // Bring the database up to the latest version first.
            DatabaseUpgrader.UpgradeDatabase(db, dbfile, typeof(DatabaseSchemaMarker));
            Assert.AreEqual(EXPECTED_LATEST_SCHEMA_VERSION, await ReadVersionAsync(db));

            // Calling again must not throw and must not change the recorded version.
            Assert.DoesNotThrow(() => DatabaseUpgrader.UpgradeDatabase(db, dbfile, typeof(DatabaseSchemaMarker)));
            Assert.AreEqual(EXPECTED_LATEST_SCHEMA_VERSION, await ReadVersionAsync(db),
                "Re-running the upgrader on a current database must not change the version.");
        }

        /// <summary>
        /// The number of numbered upgrade scripts discoverable from the schema
        /// assembly must match (and not exceed) the version recorded in
        /// <c>Schema.sql</c>. A mismatch would mean a new upgrade file was added
        /// without bumping the schema version line, or vice versa.
        /// </summary>
        [Test]
        public void UpgradeDatabase_UpgradeScriptsMatchSchemaVersion()
        {
            // The public UpgradeDatabase overload that takes a Type discovers the
            // embedded resources; we mirror its discovery logic here to count the
            // numbered upgrade scripts in the LocalDatabase schema assembly.
            var eltype = typeof(DatabaseSchemaMarker);
            var asm = eltype.Assembly;
            const string FOLDER_NAME = "Database_schema";
            const string SCHEMA_NAME = "Schema.sql";
            var prefix = (eltype.Namespace ?? "") + "." + FOLDER_NAME + ".";

            var highest = -1;
            foreach (string s in asm.GetManifestResourceNames())
            {
                if (!s.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                if (s.EndsWith(prefix + SCHEMA_NAME, StringComparison.Ordinal))
                    continue;

                // Resource names look like:
                //   Duplicati.Library.Main.Database.Local.Database_schema.1.Add_index.sql
                // Extract the leading numeric segment after the folder prefix.
                var start = prefix.Length;
                var dot = s.IndexOf(".", start, StringComparison.Ordinal);
                if (dot < 0)
                    continue;

                if (int.TryParse(s.Substring(start, dot - start), out var v) && v > highest)
                    highest = v;
            }

            Assert.AreEqual(EXPECTED_HIGHEST_UPGRADE_SCRIPT, highest,
                "The highest numbered upgrade script must match the version recorded in Schema.sql. " +
                "If you add a new upgrade script, also bump the version inserted at the end of Schema.sql.");
        }

        private static async Task<int> ReadVersionAsync(SqliteConnection db)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = @"SELECT max(""Version"") FROM ""Version""";
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        private static async Task<bool> TableExistsAsync(SqliteConnection db, string tableName)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = @"SELECT COUNT(*) FROM SQLITE_MASTER WHERE type = 'table' AND Name = @name";
            cmd.Parameters.AddWithValue("@name", tableName);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        private static async Task<bool> ColumnExistsAsync(SqliteConnection db, string tableName, string columnName)
        {
            // PRAGMA does not support parameter binding, so the table name is inlined.
            // It is validated against a strict identifier pattern to prevent injection.
            if (!System.Text.RegularExpressions.Regex.IsMatch(tableName, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                throw new ArgumentException("Invalid table name for PRAGMA query.", nameof(tableName));

            using var cmd = db.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info(\"{tableName}\")";
            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                if (string.Equals(rd.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
