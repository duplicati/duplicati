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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.CommandLine.DatabaseTool;
using Duplicati.CommandLine.DatabaseTool.Commands;
using Duplicati.Library.Main.Database.Sync;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class DatabaseToolTests
    {
        /// <summary>
        /// Reads the schema version recorded in the database
        /// </summary>
        /// <param name="dbfile">The database file</param>
        /// <returns>The schema version</returns>
        private static async Task<int> ReadVersionAsync(string dbfile)
        {
            using var db = await SQLiteLoader.LoadConnectionAsync(dbfile);
            using var cmd = db.CreateCommand();
            cmd.CommandText = @"SELECT MAX(""Version"") FROM ""Version""";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        /// <summary>
        /// Checks that the downgrade scripts reach the newest upgrade script without gaps.
        /// The oldest downgrade script marks the floor the tool can take a database back to,
        /// so the scripts below that floor are not required, but every version from the
        /// floor up to the newest upgrade script must have one.
        /// </summary>
        /// <param name="kind">The database kind, used in the assertion messages</param>
        /// <param name="upgrades">The upgrade script versions</param>
        /// <param name="downgrades">The downgrade script versions</param>
        /// <param name="folder">The folder the downgrade scripts live in</param>
        private static void AssertDowngradeScriptsCoverUpgrades(string kind, List<int> upgrades, List<int> downgrades, string folder)
        {
            if (upgrades.Count == 0)
            {
                Assert.IsEmpty(downgrades, $"There are no {kind} upgrade scripts, so no downgrade scripts are expected in {folder}");
                return;
            }

            Assert.IsNotEmpty(downgrades, $"No {kind} downgrade scripts were found in {folder}");

            var expected = Enumerable.Range(downgrades.Min(), upgrades.Max() - downgrades.Min() + 1).ToList();
            var missing = expected.Except(downgrades).ToList();
            Assert.IsEmpty(missing, $"Missing {kind} downgrade script(s) in {folder} for version(s): {string.Join(", ", missing)}. Every upgrade script needs a downgrade script of the same number.");
        }

        /// <summary>
        /// Every numbered upgrade script must have a downgrade script of the same number in
        /// the <c>Database schema/Downgrade</c> folder beside it, so a database can always be
        /// taken back to a prior version. The downgrade command treats a missing script as
        /// informational and exits with success, so nothing else catches a migration that was
        /// added without its downgrade.
        /// </summary>
        [Test]
        [Category("DatabaseTool")]
        public void EveryUpgradeScriptHasADowngradeScript()
        {
            foreach (var (kind, marker, folder) in SchemaMarkers)
            {
                var scripts = DatabaseUpgrader.GetSchemaScripts(marker);
                AssertDowngradeScriptsCoverUpgrades(kind,
                    scripts.Upgrades.Select(x => x.Version).Distinct().ToList(),
                    scripts.Downgrades.Select(x => x.Version).Distinct().ToList(),
                    folder);
            }
        }

        /// <summary>
        /// Downgrade scripts are stored in databases and run by older releases of the tool, so
        /// they must be plain SQL that the tool can run as is: no preparser blocks, and no
        /// changes to the <c>Version</c> or <c>SchemaScript</c> tables, which the tool updates itself.
        /// </summary>
        [Test]
        [Category("DatabaseTool")]
        public void DowngradeScriptsArePlainSql()
        {
            foreach (var (kind, marker, folder) in SchemaMarkers)
                foreach (var script in DatabaseUpgrader.GetSchemaScripts(marker).Downgrades)
                {
                    Assert.IsFalse(script.Content.Contains("{#if", StringComparison.OrdinalIgnoreCase),
                        $"The {kind} downgrade script {folder}/{script.Filename} uses a preparser block");
                    Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(script.Content, @"\b(SchemaScript|Version)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                        $"The {kind} downgrade script {folder}/{script.Filename} mentions the Version or SchemaScript table");
                }
        }

        /// <summary>
        /// The three database kinds, with the marker type for their embedded scripts
        /// </summary>
        private static readonly (string Kind, Type Marker, string Folder)[] SchemaMarkers =
        [
            ("local", typeof(Library.Main.Database.Local.DatabaseSchemaMarker), "Library/Main/Database/Local/Database schema/Downgrade"),
            ("server", typeof(Library.RestAPI.Database.DatabaseSchemaMarker), "Library/RestAPI/Database/Database schema/Downgrade"),
            ("sync", typeof(DatabaseSchemaMarker), "Library/Main/Database/Sync/Database schema/Downgrade"),
        ];

        [Test]
        [Category("DatabaseTool")]
        public async Task TestLocalDbMethodsAsync()
        {
            using var dbfile = new TempFile();
            using var db = await SQLiteLoader.LoadConnectionAsync(dbfile);
            using var cmd = db.CreateCommand();
            cmd.CommandText = LocalSchemaV12;
            await cmd.ExecuteNonQueryAsync();

            Assert.AreEqual(0, await Program.MainAsync(["upgrade", dbfile, "--no-backups"]));
            var latest = await ReadVersionAsync(dbfile);
            Assert.IsTrue(latest > 12, "The upgrade should move the database past version 12");

            // A missing downgrade script makes the tool report and exit with success without
            // touching the database, so the version reached is what proves the downgrade ran.
            Assert.AreEqual(0, await Program.MainAsync(["downgrade", dbfile, "--server-version=6", "--local-version=12", "--no-backups"]));
            Assert.AreEqual(12, await ReadVersionAsync(dbfile), "The downgrade should reach version 12; a missing downgrade script leaves the database untouched");
            Assert.AreEqual(0, await Program.MainAsync(["upgrade", dbfile, "--no-backups"]));
            Assert.AreEqual(latest, await ReadVersionAsync(dbfile), "The upgrade should reach the latest version again");

            Assert.AreEqual(0, await Program.MainAsync(["list", dbfile]));
            Assert.AreEqual(0, await Program.MainAsync(["list", dbfile, "RemoteVolume"]));
            Assert.AreEqual(0, await Program.MainAsync(["list", dbfile, "RemoteVolume", "--output-json"]));
            Assert.AreEqual(0, await Program.MainAsync(["execute", dbfile, "SELECT * FROM RemoteVolume", "--output-json"]));
        }

        /// <summary>
        /// The --allow-insecure-datafolder option is registered on the root command
        /// but must be accepted after a subcommand, like the global option it
        /// replaced during the System.CommandLine 2.0 migration.
        /// </summary>
        [Test]
        [Category("DatabaseTool")]
        public async Task GlobalOptionIsAcceptedAfterSubcommandAsync()
        {
            using var dbfile = new TempFile();

            var output = new StringWriter();
            var originalOut = Console.Out;
            int exitCode;
            try
            {
                Console.SetOut(output);
                // The database is empty, so "list" fails with a message about the
                // database not existing. A parse error would instead complain about
                // an unrecognized option.
                exitCode = await Program.MainAsync(["list", dbfile, "--allow-insecure-datafolder"]);
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            var consoleOutput = output.ToString();
            Assert.IsFalse(consoleOutput.Contains("Unrecognized"),
                $"The global option was not accepted after the subcommand: {consoleOutput}");
            Assert.AreEqual(0, exitCode, $"Unexpected exit code, output was: {consoleOutput}");
        }

        /// <summary>
        /// The backup taken before an upgrade must carry an unambiguous timestamp.
        /// The format string used to specify <c>hh</c> (12-hour clock) without an
        /// AM/PM designator, so a backup taken at 13:30 was named as if it had been
        /// taken at 01:30 and collided with one taken in the morning.
        ///
        /// Note that the 12/24-hour part of this test only discriminates when the
        /// local time is 13:00 or later; before noon the two formats agree. It never
        /// fails spuriously, and a month/minute mix-up is caught at any time of day.
        /// </summary>
        [Test]
        [Category("DatabaseTool")]
        public async Task UpgradeBackupFileNameUsesUnambiguousTimestampAsync()
        {
            using var dbfile = new TempFile();
            using (var db = await SQLiteLoader.LoadConnectionAsync(dbfile))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = LocalSchemaV12;
                await cmd.ExecuteNonQueryAsync();
            }

            var folder = Path.GetDirectoryName(Path.GetFullPath(dbfile))!;
            var prefix = Path.GetFileNameWithoutExtension(dbfile) + "-";

            // Deliberately omit --no-backups so that Helper.CreateFileBackup runs.
            var before = DateTime.Now;
            Assert.AreEqual(0, await Program.MainAsync(["upgrade", dbfile]));
            var after = DateTime.Now;

            var backups = Directory.GetFiles(folder, prefix + "*.bak");
            Assert.AreEqual(1, backups.Length, "The upgrade should have created exactly one backup file.");

            try
            {
                var stamp = Path.GetFileNameWithoutExtension(backups[0]).Substring(prefix.Length);
                Assert.IsTrue(
                    DateTime.TryParseExact(stamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed),
                    $"The backup timestamp \"{stamp}\" is not a valid yyyyMMddHHmmss value.");

                Assert.IsTrue(parsed >= before.AddSeconds(-5) && parsed <= after.AddSeconds(5),
                    $"The backup timestamp \"{stamp}\" does not match the time the backup was taken ({before:yyyyMMddHHmmss}).");
            }
            finally
            {
                File.Delete(backups[0]);
            }
        }

        [Test]
        [Category("DatabaseTool")]
        public async Task TestServerDbMethodsAsync()
        {
            using var dbfile = new TempFile();
            using var db = await SQLiteLoader.LoadConnectionAsync(dbfile);
            using var cmd = db.CreateCommand();
            cmd.CommandText = ServerSchemaV6;
            await cmd.ExecuteNonQueryAsync();

            Assert.AreEqual(0, await Program.MainAsync(["upgrade", dbfile, "--no-backups"]));
            var latest = await ReadVersionAsync(dbfile);
            Assert.IsTrue(latest > 6, "The upgrade should move the database past version 6");

            // See TestLocalDbMethodsAsync: only the version reached proves the downgrade ran.
            Assert.AreEqual(0, await Program.MainAsync(["downgrade", dbfile, "--server-version=6", "--local-version=12", "--no-backups"]));
            Assert.AreEqual(6, await ReadVersionAsync(dbfile), "The downgrade should reach version 6; a missing downgrade script leaves the database untouched");
            Assert.AreEqual(0, await Program.MainAsync(["upgrade", dbfile, "--no-backups"]));
            Assert.AreEqual(latest, await ReadVersionAsync(dbfile), "The upgrade should reach the latest version again");

            Assert.AreEqual(0, await Program.MainAsync(["list", dbfile]));
            Assert.AreEqual(0, await Program.MainAsync(["list", dbfile, "Source"]));
            Assert.AreEqual(0, await Program.MainAsync(["list", dbfile, "Source", "--output-json"]));
            Assert.AreEqual(0, await Program.MainAsync(["execute", dbfile, "SELECT * FROM Source", "--output-json"]));
        }

        /// <summary>
        /// Verifies the database tool commands against a sync database.
        ///
        /// The sync database schema currently ships at version 0 and ships no numbered
        /// upgrade scripts (there is no <c>1.*.sql</c> in the sync <c>Database_schema</c>
        /// folder) and no downgrade scripts (there is no <c>Scripts/Sync/</c> folder).
        /// Therefore <c>upgrade</c> and <c>downgrade</c> are effectively no-ops: they
        /// report the missing scripts and return successfully without modifying the
        /// database. These tests pin that behavior so that, when sync upgrade/downgrade
        /// scripts are eventually added, these tests will fail and force an update to
        /// actually exercise the new scripts rather than silently continuing to assert
        /// the no-op path.
        /// </summary>
        [Test]
        [Category("DatabaseTool")]
        public async Task TestSyncDbMethodsAsync()
        {
            using var dbfile = new TempFile();

            // Build the sync database the same way LocalSyncDatabase does: hand an
            // empty file to DatabaseUpgrader with the sync schema marker, which loads
            // Schema.sql and records the current sync schema version.
            using (var db = await SQLiteLoader.LoadConnectionAsync(dbfile))
            {
                DatabaseUpgrader.UpgradeDatabase(db, dbfile, typeof(DatabaseSchemaMarker));
            }

            Assert.AreEqual(SYNC_SCHEMA_VERSION, await ReadSyncVersionAsync(dbfile));
            Assert.IsTrue(await SyncTableExistsAsync(dbfile, "LogData"));

            // The database is already at the latest version, so upgrade is a no-op.
            Assert.AreEqual(0, await Program.MainAsync(["upgrade", dbfile, "--no-backups"]));
            Assert.AreEqual(SYNC_SCHEMA_VERSION, await ReadSyncVersionAsync(dbfile));

            // The default --sync-version is the newest version this release supports, so
            // downgrade without options leaves the database alone.
            Assert.AreEqual(0, await Program.MainAsync(["downgrade", dbfile, "--no-backups"]));
            Assert.AreEqual(SYNC_SCHEMA_VERSION, await ReadSyncVersionAsync(dbfile));

            // Version 0 is the version before the LogData table was added, so downgrade
            // removes the table and records version 0.
            Assert.AreEqual(0, await Program.MainAsync(["downgrade", dbfile, "--sync-version=0", "--no-backups"]));
            Assert.AreEqual(0, await ReadSyncVersionAsync(dbfile));
            Assert.IsFalse(await SyncTableExistsAsync(dbfile, "LogData"));

            // Upgrading again re-applies the upgrade script and restores the table.
            Assert.AreEqual(0, await Program.MainAsync(["upgrade", dbfile, "--no-backups"]));

            // The round trip must leave the database at the latest version with all
            // sync tables present.
            Assert.AreEqual(SYNC_SCHEMA_VERSION, await ReadSyncVersionAsync(dbfile));
            Assert.IsTrue(await SyncTableExistsAsync(dbfile, "RemoteInventory"));
            Assert.IsTrue(await SyncTableExistsAsync(dbfile, "PendingOperation"));
            Assert.IsTrue(await SyncTableExistsAsync(dbfile, "RemoteOperation"));
            Assert.IsTrue(await SyncTableExistsAsync(dbfile, "LogData"));

            // The list and execute commands should work against the sync database
            // just as they do for the server and local databases.
            Assert.AreEqual(0, await Program.MainAsync(["list", dbfile]));
            Assert.AreEqual(0, await Program.MainAsync(["list", dbfile, "RemoteInventory"]));
            Assert.AreEqual(0, await Program.MainAsync(["list", dbfile, "RemoteInventory", "--output-json"]));
            Assert.AreEqual(0, await Program.MainAsync(["execute", dbfile, "SELECT * FROM RemoteInventory", "--output-json"]));

            // The helper must classify a sync database as DatabaseType.Sync at the
            // recorded version, so the tool routes it to the sync upgrade/downgrade scripts.
            var (version, type) = await Helper.ExamineDatabaseAsync(dbfile);
            Assert.AreEqual(SYNC_SCHEMA_VERSION, version);
            Assert.AreEqual(DatabaseType.Sync, type);
        }

        /// <summary>
        /// The schema for the server database, captured in version 6.
        /// </summary>
        private const string ServerSchemaV6 = @"
CREATE TABLE ""Backup"" (
    ""ID"" INTEGER PRIMARY KEY AUTOINCREMENT,
    ""Name"" TEXT NOT NULL,
    ""Description"" TEXT NOT NULL DEFAULT '',
    ""Tags"" TEXT NOT NULL,
    ""TargetURL"" TEXT NOT NULL,
    ""DBPath"" TEXT NOT NULL
);

CREATE TABLE ""Schedule"" (
    ""ID"" INTEGER PRIMARY KEY,
    ""Tags"" TEXT NOT NULL,
    ""Time"" INTEGER NOT NULL,
    ""Repeat"" TEXT NOT NULL,
    ""LastRun"" INTEGER NOT NULL,
    ""Rule"" TEXT NOT NULL
);

CREATE TABLE ""Source"" (
    ""BackupID"" INTEGER NOT NULL,
    ""Path"" TEXT NOT NULL
);

CREATE TABLE ""Filter"" (
    ""BackupID"" INTEGER NOT NULL,
    ""Order"" INTEGER NOT NULL,
    ""Include"" INTEGER NOT NULL,
    ""Expression"" TEXT NOT NULL
);

CREATE TABLE ""Option"" (
    ""BackupID"" INTEGER NOT NULL,
    ""Filter"" TEXT NOT NULL,
    ""Name"" TEXT NOT NULL,
    ""Value"" TEXT NOT NULL
);

CREATE TABLE ""Metadata"" (
    ""BackupID"" INTEGER NOT NULL,
    ""Name"" TEXT NOT NULL,
    ""Value"" TEXT NOT NULL
);

CREATE TABLE ""Log"" (
    ""BackupID"" INTEGER NOT NULL,
    ""Description"" TEXT NOT NULL,
    ""Start"" INTEGER NOT NULL,
    ""Finish"" INTEGER NOT NULL,
    ""Result"" TEXT NOT NULL,
    ""SuggestedIcon"" TEXT NOT NULL
);

CREATE TABLE ""ErrorLog"" (
    ""BackupID"" INTEGER,
    ""Message"" TEXT NOT NULL,
    ""Exception"" TEXT,
    ""Timestamp"" INTEGER NOT NULL
);

CREATE TABLE ""Version"" (
    ""ID"" INTEGER PRIMARY KEY,
    ""Version"" INTEGER NOT NULL
);

CREATE TABLE ""Notification"" (
    ""ID"" INTEGER PRIMARY KEY,
    ""Type"" TEXT NOT NULL,
    ""Title"" TEXT NOT NULL,
    ""Message"" TEXT NOT NULL,
    ""Exception"" TEXT NOT NULL,
    ""BackupID"" TEXT NULL,
    ""Action"" TEXT NOT NULL,
    ""Timestamp"" INTEGER NOT NULL,
    ""LogEntryID"" TEXT NULL,
    ""MessageID"" TEXT NULL,
    ""MessageLogTag"" TEXT NULL
);

CREATE TABLE ""UIStorage"" (
    ""Scheme"" TEXT NOT NULL,
    ""Key"" TEXT NOT NULL,
    ""Value"" TEXT NOT NULL
);

CREATE TABLE ""TempFile"" (
    ""ID"" INTEGER PRIMARY KEY,
    ""Origin"" TEXT NOT NULL,
    ""Path"" TEXT NOT NULL,
    ""Timestamp"" INTEGER NOT NULL,
    ""Expires"" INTEGER NOT NULL
);

INSERT INTO ""Version"" (""Version"") VALUES (6);

";

        /// <summary>
        /// The schema for the local database, captured in version 12.
        /// </summary>
        private const string LocalSchemaV12 = @"
CREATE TABLE ""Operation"" (
	""ID"" INTEGER PRIMARY KEY,
	""Description"" TEXT NOT NULL,
	""Timestamp"" INTEGER NOT NULL
);

CREATE TABLE ""Remotevolume"" (
	""ID"" INTEGER PRIMARY KEY,
	""OperationID"" INTEGER NOT NULL,
	""Name"" TEXT NOT NULL,
	""Type"" TEXT NOT NULL,
	""Size"" INTEGER NULL,
	""Hash"" TEXT NULL,
	""State"" TEXT NOT NULL,
	""VerificationCount"" INTEGER NOT NULL,
	""DeleteGraceTime"" INTEGER NOT NULL
);

CREATE UNIQUE INDEX ""RemotevolumeName"" ON ""Remotevolume"" (""Name"", ""State"");

CREATE TABLE ""IndexBlockLink"" (
	""IndexVolumeID"" INTEGER NOT NULL,
	""BlockVolumeID"" INTEGER NOT NULL
);

CREATE TABLE ""Fileset"" (
	""ID"" INTEGER PRIMARY KEY,
	""OperationID"" INTEGER NOT NULL,
	""VolumeID"" INTEGER NOT NULL,
	""IsFullBackup"" INTEGER NOT NULL,
	""Timestamp"" INTEGER NOT NULL
);

CREATE TABLE ""FilesetEntry"" (
	""FilesetID"" INTEGER NOT NULL,
	""FileID"" INTEGER NOT NULL,
	""Lastmodified"" INTEGER NOT NULL,
	CONSTRAINT ""FilesetEntry_PK_FilesetIdFileId"" PRIMARY KEY (""FilesetID"", ""FileID"")
) WITHOUT ROWID;

CREATE INDEX ""FilesetentryFileIdIndex"" on ""FilesetEntry"" (""FileID"");
CREATE INDEX ""nn_FilesetentryFile"" on FilesetEntry (""FilesetID"",""FileID"");

CREATE TABLE ""PathPrefix"" (
    ""ID"" INTEGER PRIMARY KEY,
    ""Prefix"" TEXT NOT NULL
);
CREATE UNIQUE INDEX ""PathPrefixPrefix"" ON ""PathPrefix"" (""Prefix"");

CREATE TABLE ""FileLookup"" (
    ""ID"" INTEGER PRIMARY KEY,
    ""PrefixID"" INTEGER NOT NULL,
    ""Path"" TEXT NOT NULL,
    ""BlocksetID"" INTEGER NOT NULL,
    ""MetadataID"" INTEGER NOT NULL
);

CREATE UNIQUE INDEX ""FileLookupPath"" ON ""FileLookup"" (""PrefixID"", ""Path"", ""BlocksetID"", ""MetadataID"");
CREATE INDEX ""nn_FileLookup_BlockMeta"" ON FileLookup (""BlocksetID"", ""MetadataID"");

CREATE VIEW ""File"" AS SELECT ""A"".""ID"" AS ""ID"", ""B"".""Prefix"" || ""A"".""Path"" AS ""Path"", ""A"".""BlocksetID"" AS ""BlocksetID"", ""A"".""MetadataID"" AS ""MetadataID"" FROM ""FileLookup"" ""A"", ""PathPrefix"" ""B"" WHERE ""A"".""PrefixID"" = ""B"".""ID"";

CREATE TABLE ""BlocklistHash"" (
	""BlocksetID"" INTEGER NOT NULL,
	""Index"" INTEGER NOT NULL,
	""Hash"" TEXT NOT NULL
);

CREATE UNIQUE INDEX ""BlocklistHashBlocksetIDIndex"" ON ""BlocklistHash"" (""BlocksetID"", ""Index"");

CREATE TABLE ""Blockset"" (
	""ID"" INTEGER PRIMARY KEY,
	""Length"" INTEGER NOT NULL,
	""FullHash"" TEXT NOT NULL
);

CREATE UNIQUE INDEX ""BlocksetFullHash"" ON ""Blockset"" (""FullHash"", ""Length"");

CREATE TABLE ""BlocksetEntry"" (
	""BlocksetID"" INTEGER NOT NULL,
	""Index"" INTEGER NOT NULL,
	""BlockID"" INTEGER NOT NULL,
	CONSTRAINT ""BlocksetEntry_PK_IdIndex"" PRIMARY KEY (""BlocksetID"", ""Index"")
) WITHOUT ROWID;

CREATE INDEX ""BlocksetEntry_IndexIdsBackwards"" ON ""BlocksetEntry"" (""BlockID"");
CREATE INDEX ""nnc_BlocksetEntry"" ON ""BlocksetEntry"" (""Index"", ""BlocksetID"", ""BlockID"");

CREATE TABLE ""Block"" (
	""ID"" INTEGER PRIMARY KEY,
    ""Hash"" TEXT NOT NULL,
	""Size"" INTEGER NOT NULL,
	""VolumeID"" INTEGER NOT NULL
);

CREATE UNIQUE INDEX ""BlockHashSize"" ON ""Block"" (""Hash"", ""Size"");

CREATE INDEX ""Block_IndexByVolumeId"" ON ""Block"" (""VolumeID"");

CREATE INDEX ""BlockSize"" ON ""Block"" (""Size"");
CREATE INDEX ""BlockHashVolumeID"" ON ""Block"" (""Hash"", ""VolumeID"");

CREATE TABLE ""DeletedBlock"" (
	""ID"" INTEGER PRIMARY KEY,
    ""Hash"" TEXT NOT NULL,
	""Size"" INTEGER NOT NULL,
	""VolumeID"" INTEGER NOT NULL
);

CREATE TABLE ""DuplicateBlock"" (
    ""BlockID"" INTEGER NOT NULL,
    ""VolumeID"" INTEGER NOT NULL
);

CREATE TABLE ""Metadataset"" (
	""ID"" INTEGER PRIMARY KEY,
	""BlocksetID"" INTEGER NOT NULL
);

CREATE INDEX ""MetadatasetBlocksetID"" ON ""Metadataset"" (""BlocksetID"");
CREATE INDEX ""nnc_Metadataset"" ON Metadataset (""ID"",""BlocksetID"");

CREATE TABLE ""RemoteOperation"" (
	""ID"" INTEGER PRIMARY KEY,
	""OperationID"" INTEGER NOT NULL,
	""Timestamp"" INTEGER NOT NULL,
	""Operation"" TEXT NOT NULL,
	""Path"" TEXT NOT NULL,
	""Data"" BLOB NULL
);

CREATE TABLE ""LogData"" (
	""ID"" INTEGER PRIMARY KEY,
	""OperationID"" INTEGER NOT NULL,
	""Timestamp"" INTEGER NOT NULL,
	""Type"" TEXT NOT NULL,
	""Message"" TEXT NOT NULL,
	""Exception"" TEXT NULL
);

CREATE TABLE ""Version"" (
    ""ID"" INTEGER PRIMARY KEY,
    ""Version"" INTEGER NOT NULL
);

CREATE TABLE ""Configuration"" (
	""Key"" TEXT PRIMARY KEY NOT NULL,
	""Value"" TEXT NOT NULL
);

CREATE TABLE ""ChangeJournalData"" (
    ""ID"" INTEGER PRIMARY KEY,
    ""FilesetID"" INTEGER NOT NULL,
    ""VolumeName"" TEXT NOT NULL,
    ""JournalID"" INTEGER NOT NULL,
    ""NextUsn"" INTEGER NOT NULL,
    ""ConfigHash"" TEXT NOT NULL
);

INSERT INTO ""Version"" (""Version"") VALUES (12);

";

        /// <summary>
        /// The schema version that the sync <c>Schema.sql</c> records in the
        /// <c>Version</c> table on a fresh install. This mirrors the value written by
        /// the last line of <c>Duplicati/Library/Main/Database/Sync/Database schema/Schema.sql</c>
        /// and must be kept in sync if the sync schema is bumped.
        /// </summary>
        private const int SYNC_SCHEMA_VERSION = 1;

        /// <summary>
        /// Reads the recorded schema version from a sync database file.
        /// </summary>
        private static async Task<int> ReadSyncVersionAsync(string dbfile)
        {
            using var db = await SQLiteLoader.LoadConnectionAsync(dbfile);
            using var cmd = db.CreateCommand();
            cmd.CommandText = @"SELECT max(""Version"") FROM ""Version""";
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        /// <summary>
        /// Returns <c>true</c> if the given table exists in the sync database file.
        /// </summary>
        private static async Task<bool> SyncTableExistsAsync(string dbfile, string tableName)
        {
            using var db = await SQLiteLoader.LoadConnectionAsync(dbfile);
            using var cmd = db.CreateCommand();
            cmd.CommandText = @"SELECT COUNT(*) FROM SQLITE_MASTER WHERE type = 'table' AND Name = @name";
            cmd.Parameters.AddWithValue("@name", tableName);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        /// <summary>
        /// The marker type for the local database scripts
        /// </summary>
        private static readonly Type LocalMarker = typeof(Library.Main.Database.Local.DatabaseSchemaMarker);

        /// <summary>
        /// Creates a local database at the newest version, with its <c>SchemaScript</c> rows
        /// </summary>
        /// <param name="dbfile">The database file</param>
        private static async Task CreateCurrentLocalDatabaseAsync(string dbfile)
        {
            using var db = await SQLiteLoader.LoadConnectionAsync(dbfile);
            DatabaseUpgrader.UpgradeDatabase(db, dbfile, LocalMarker);
        }

        /// <summary>
        /// Gets the local scripts as an older release that only knew versions up to <paramref name="latest"/> would bundle them
        /// </summary>
        /// <param name="latest">The newest version the older release knew</param>
        /// <returns>The truncated scripts</returns>
        private static SchemaScriptSet LocalScriptsUpTo(int latest)
        {
            var scripts = DatabaseUpgrader.GetSchemaScripts(LocalMarker);
            return new SchemaScriptSet(
                scripts.Schema,
                scripts.Upgrades.Where(x => x.Version <= latest).ToList(),
                scripts.Downgrades.Where(x => x.Version <= latest).ToList());
        }

        /// <summary>
        /// Replaces the stored downgrade script for a version
        /// </summary>
        /// <param name="dbfile">The database file</param>
        /// <param name="version">The version of the row</param>
        /// <param name="script">The script, or <c>null</c> for none</param>
        /// <param name="writerVersion">The writer version to record</param>
        private static async Task SetStoredDowngradeScriptAsync(string dbfile, int version, string script, int writerVersion)
        {
            using var db = await SQLiteLoader.LoadConnectionAsync(dbfile);
            using var cmd = db.CreateCommand();
            cmd.CommandText = @"UPDATE ""SchemaScript"" SET ""DowngradeScript"" = @script, ""WriterVersion"" = @writer WHERE ""Version"" = @version";
            cmd.Parameters.AddWithValue("@script", (object)script ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@writer", writerVersion);
            cmd.Parameters.AddWithValue("@version", version);
            Assert.AreEqual(1, await cmd.ExecuteNonQueryAsync());
        }

        /// <summary>
        /// Reads the highest version in the <c>SchemaScript</c> table
        /// </summary>
        /// <param name="dbfile">The database file</param>
        /// <returns>The highest version</returns>
        private static async Task<int> ReadMaxSchemaScriptVersionAsync(string dbfile)
        {
            using var db = await SQLiteLoader.LoadConnectionAsync(dbfile);
            using var cmd = db.CreateCommand();
            cmd.CommandText = @"SELECT IFNULL(MAX(""Version""), 0) FROM ""SchemaScript""";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        /// <summary>
        /// A tool that only knows versions up to N - 2 must downgrade a database at N by
        /// running the scripts the newer release stored in it, and the result must be a
        /// database the current release can upgrade again.
        /// </summary>
        [Test]
        [Category("DatabaseTool")]
        public async Task DowngradeUsesStoredScriptsUnknownToTheToolAsync()
        {
            using var dbfile = new TempFile();
            await CreateCurrentLocalDatabaseAsync(dbfile);
            var latest = DatabaseUpgrader.GetSchemaScripts(LocalMarker).LatestVersion;

            await Downgrade.ApplyDowngradeAsync(dbfile, latest, latest - 3, LocalScriptsUpTo(latest - 2), true);

            Assert.AreEqual(latest - 3, await ReadVersionAsync(dbfile));
            Assert.AreEqual(latest - 3, await ReadMaxSchemaScriptVersionAsync(dbfile), "Rows above the new version must be removed");
            Assert.IsFalse(await SyncTableExistsAsync(dbfile, "SourceProviderState"));

            // The upgrader keeps a backup beside the database before it runs the scripts
            var backupPattern = "* " + Path.GetFileNameWithoutExtension(dbfile) + " *.sqlite";
            try
            {
                await CreateCurrentLocalDatabaseAsync(dbfile);
                Assert.AreEqual(latest, await ReadVersionAsync(dbfile));
                Assert.AreEqual(latest, await ReadMaxSchemaScriptVersionAsync(dbfile));
                Assert.IsTrue(await SyncTableExistsAsync(dbfile, "SourceProviderState"));
            }
            finally
            {
                foreach (var backup in Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(dbfile))!, backupPattern))
                    File.Delete(backup);
            }
        }

        /// <summary>
        /// A stored script written by a newer release wins over the bundled one; a stored
        /// script from this release or an older one does not.
        /// </summary>
        /// <param name="newerWriter">Whether the stored row was written by a newer release</param>
        [Test]
        [Category("DatabaseTool")]
        [TestCase(true)]
        [TestCase(false)]
        public async Task DowngradePrefersStoredScriptOnlyFromNewerReleaseAsync(bool newerWriter)
        {
            using var dbfile = new TempFile();
            await CreateCurrentLocalDatabaseAsync(dbfile);
            var scripts = DatabaseUpgrader.GetSchemaScripts(LocalMarker);
            var latest = scripts.LatestVersion;

            await SetStoredDowngradeScriptAsync(dbfile, latest,
                scripts.GetDowngradeScript(latest) + ";\nCREATE TABLE \"FromDatabase\" (\"X\" INTEGER);",
                newerWriter ? latest + 1 : latest);

            await Downgrade.ApplyDowngradeAsync(dbfile, latest, latest - 1, scripts, true);

            Assert.AreEqual(latest - 1, await ReadVersionAsync(dbfile));
            Assert.AreEqual(newerWriter, await SyncTableExistsAsync(dbfile, "FromDatabase"));
        }

        /// <summary>
        /// A newer release that stored no downgrade script for a version marks it as not
        /// downgradable, even when the tool bundles a script for it.
        /// </summary>
        [Test]
        [Category("DatabaseTool")]
        public async Task DowngradeIsBlockedByMissingScriptFromNewerReleaseAsync()
        {
            using var dbfile = new TempFile();
            await CreateCurrentLocalDatabaseAsync(dbfile);
            var scripts = DatabaseUpgrader.GetSchemaScripts(LocalMarker);
            var latest = scripts.LatestVersion;

            await SetStoredDowngradeScriptAsync(dbfile, latest, null, latest + 1);
            await Downgrade.ApplyDowngradeAsync(dbfile, latest, latest - 1, scripts, true);

            Assert.AreEqual(latest, await ReadVersionAsync(dbfile));
            Assert.IsTrue(await SyncTableExistsAsync(dbfile, "SourceProviderState"));
        }

        /// <summary>
        /// A stored script that reaches outside the database is refused, and nothing changes.
        /// </summary>
        [Test]
        [Category("DatabaseTool")]
        public async Task DowngradeRefusesStoredScriptWithAttachAsync()
        {
            using var dbfile = new TempFile();
            await CreateCurrentLocalDatabaseAsync(dbfile);
            var latest = DatabaseUpgrader.GetSchemaScripts(LocalMarker).LatestVersion;

            await SetStoredDowngradeScriptAsync(dbfile, latest, "ATTACH DATABASE ':memory:' AS \"Other\"", latest);
            await Downgrade.ApplyDowngradeAsync(dbfile, latest, latest - 1, LocalScriptsUpTo(latest - 1), true);

            Assert.AreEqual(latest, await ReadVersionAsync(dbfile));
            Assert.AreEqual(latest, await ReadMaxSchemaScriptVersionAsync(dbfile));
        }

        /// <summary>
        /// A version with neither a bundled nor a stored script is reported, and nothing changes.
        /// </summary>
        [Test]
        [Category("DatabaseTool")]
        public async Task DowngradeStopsWhenNoScriptIsFoundAsync()
        {
            using var dbfile = new TempFile();
            await CreateCurrentLocalDatabaseAsync(dbfile);
            var latest = DatabaseUpgrader.GetSchemaScripts(LocalMarker).LatestVersion;

            using (var db = await SQLiteLoader.LoadConnectionAsync(dbfile))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"DELETE FROM ""SchemaScript"" WHERE ""Version"" = @version";
                cmd.Parameters.AddWithValue("@version", latest);
                await cmd.ExecuteNonQueryAsync();
            }

            await Downgrade.ApplyDowngradeAsync(dbfile, latest, latest - 2, LocalScriptsUpTo(latest - 1), true);

            Assert.AreEqual(latest, await ReadVersionAsync(dbfile));
            Assert.IsTrue(await SyncTableExistsAsync(dbfile, "SourceProviderState"));
        }

        [Test]
        [Category("DatabaseTool")]
        public async Task TestVerifyCommandAsync()
        {
            using var tempFolder = new Library.Utility.TempFolder();
            string tempDir = tempFolder;

            var serverDb = Path.Combine(tempDir, "server.sqlite");
            var localDb1 = Path.Combine(tempDir, "local1.sqlite");
            var localDb2 = Path.Combine(tempDir, "local2.sqlite"); // Referenced but won't exist = Missing
            var orphanDb = Path.Combine(tempDir, "ABCDEFGHIJ.sqlite"); // Random name = Orphaned

            // Create server database
            using (var db = await SQLiteLoader.LoadConnectionAsync(serverDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = ServerSchemaV6;
                await cmd.ExecuteNonQueryAsync();

                // Insert a backup referencing localDb1
                cmd.CommandText = $@"
                    INSERT INTO ""Backup"" (""Name"", ""Tags"", ""TargetURL"", ""DBPath"")
                    VALUES ('Test Backup', '', 'file:///test', '{localDb1.Replace("'", "''")}');
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Create local database
            using (var db = await SQLiteLoader.LoadConnectionAsync(localDb1))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = LocalSchemaV12;
                await cmd.ExecuteNonQueryAsync();
            }

            // Create orphan database (exists but not referenced)
            using (var db = await SQLiteLoader.LoadConnectionAsync(orphanDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = LocalSchemaV12;
                await cmd.ExecuteNonQueryAsync();
            }

            // Copy server database to expected location
            var serverTargetPath = Path.Combine(tempDir, "Duplicati-server.sqlite");
            File.Copy(serverDb, serverTargetPath);

            // Create dbconfig.json with localDb2 (which won't exist)
            var dbConfigPath = Path.Combine(tempDir, "dbconfig.json");
            var dbConfig = new List<Duplicati.Library.Main.CLIDatabaseLocator.BackendEntry>
            {
                new()
                {
                    Type = "file",
                    Server = "localhost",
                    Path = "/backup1",
                    Prefix = "dup",
                    Username = "user",
                    Port = 0,
                    Databasepath = localDb2, // This file won't exist = Missing
                    ParameterFile = null
                },
                new()
                {
                    Type = "file",
                    Server = "localhost",
                    Path = "/backup2",
                    Prefix = "dup",
                    Username = "user",
                    Port = 0,
                    Databasepath = localDb1, // This file exists = Found
                    ParameterFile = null
                }
            };
            await File.WriteAllTextAsync(dbConfigPath, Newtonsoft.Json.JsonConvert.SerializeObject(dbConfig));

            // Test verify command with JSON output
            var output = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(output);

            try
            {
                Assert.AreEqual(0, await Program.MainAsync(["verify", "--datafolder", tempDir, "--output-json"]));
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            var result = output.ToString();

            // Should report localDb1 as Found (in both server and dbconfig)
            Assert.That(result, Does.Contain("\"Status\": \"Found\""));
            // Should report localDb2 as Missing (referenced in dbconfig but file doesn't exist)
            Assert.That(result, Does.Contain("\"Status\": \"Missing\""));
            // Should report orphanDb as Orphaned (exists but not referenced)
            Assert.That(result, Does.Contain("\"Status\": \"Orphaned\""));
        }

        [Test]
        [Category("DatabaseTool")]
        public async Task TestVerifyCommandWithRelativeDbPathAsync()
        {
            using var tempFolder = new Library.Utility.TempFolder();
            string tempDir = tempFolder;

            var serverDb = Path.Combine(tempDir, "server.sqlite");
            var localDb1 = Path.Combine(tempDir, "local1.sqlite");
            var relativeDbPath = "local1.sqlite";
            var orphanDb = Path.Combine(tempDir, "ABCDEFGHIJ.sqlite");

            // Create server database
            using (var db = await SQLiteLoader.LoadConnectionAsync(serverDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = ServerSchemaV6;
                await cmd.ExecuteNonQueryAsync();

                // Insert a backup with a relative DBPath
                cmd.CommandText = $@"
                    INSERT INTO ""Backup"" (""Name"", ""Tags"", ""TargetURL"", ""DBPath"")
                    VALUES ('Test Backup', '', 'file:///test', '{relativeDbPath.Replace("'", "''")}');
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Create local database
            using (var db = await SQLiteLoader.LoadConnectionAsync(localDb1))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = LocalSchemaV12;
                await cmd.ExecuteNonQueryAsync();
            }

            // Create orphan database
            using (var db = await SQLiteLoader.LoadConnectionAsync(orphanDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = LocalSchemaV12;
                await cmd.ExecuteNonQueryAsync();
            }

            // Copy server database to expected location
            var serverTargetPath = Path.Combine(tempDir, "Duplicati-server.sqlite");
            File.Copy(serverDb, serverTargetPath);

            // Test verify command with JSON output
            var output = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(output);

            try
            {
                Assert.AreEqual(0, await Program.MainAsync(["verify", "--datafolder", tempDir, "--output-json"]));
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            var result = output.ToString();

            // Should report localDb1 as Found (referenced with relative path but resolved)
            Assert.That(result, Does.Contain("\"Status\": \"Found\""));
            // Should report orphanDb as Orphaned (exists but not referenced)
            Assert.That(result, Does.Contain("\"Status\": \"Orphaned\""));
        }

        /// <summary>
        /// Verifies that <see cref="Verify.AnalyzeDatabasesAsync"/> takes sync databases
        /// into account: a sync database referenced in dbconfig.json with the
        /// <c>IsSyncDb</c> flag is classified as <see cref="DatabaseType.Sync"/> whether
        /// the file exists (Found) or is missing (Missing), and a sync database that exists
        /// on disk but is not referenced is classified as <see cref="DatabaseType.Sync"/>
        /// by examining its schema (Orphaned). Backup databases referenced in dbconfig.json
        /// without the flag remain <see cref="DatabaseType.Backup"/>.
        /// </summary>
        [Test]
        [Category("DatabaseTool")]
        public async Task TestVerifyCommandAccountsForSyncDatabasesAsync()
        {
            using var tempFolder = new Library.Utility.TempFolder();
            string tempDir = tempFolder;

            var serverTargetPath = Path.Combine(tempDir, "Duplicati-server.sqlite");
            var backupDb = Path.Combine(tempDir, "backup.sqlite");
            var syncDbFound = Path.Combine(tempDir, "syncfound.sqlite");
            var syncDbMissing = Path.Combine(tempDir, "syncmissing.sqlite"); // referenced but won't exist
            var syncDbOrphan = Path.Combine(tempDir, "syncorphan.sqlite"); // exists, not referenced

            // Create the server database (empty Backup table; sync DBs are not tracked here).
            using (var db = await SQLiteLoader.LoadConnectionAsync(serverTargetPath))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = ServerSchemaV6;
                await cmd.ExecuteNonQueryAsync();
            }

            // Create a backup database referenced by the server database.
            using (var db = await SQLiteLoader.LoadConnectionAsync(backupDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = LocalSchemaV12;
                await cmd.ExecuteNonQueryAsync();
            }
            using (var db = await SQLiteLoader.LoadConnectionAsync(serverTargetPath))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = $@"
                    INSERT INTO ""Backup"" (""Name"", ""Tags"", ""TargetURL"", ""DBPath"")
                    VALUES ('Backup', '', 'file:///backup', '{backupDb.Replace("'", "''")}');
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Create a real sync database (found + referenced) and an orphan sync database
            // (exists on disk, not referenced anywhere). Build them the same way
            // LocalSyncDatabase does so the schema classification matches DatabaseType.Sync.
            foreach (var syncPath in new[] { syncDbFound, syncDbOrphan })
            {
                using var db = await SQLiteLoader.LoadConnectionAsync(syncPath);
                DatabaseUpgrader.UpgradeDatabase(db, syncPath, typeof(DatabaseSchemaMarker));
            }

            // Create dbconfig.json: one backup entry, one sync entry (found), one sync
            // entry (missing). IsSyncDb distinguishes the sync entries from the backup.
            var dbConfigPath = Path.Combine(tempDir, "dbconfig.json");
            var dbConfig = new List<Duplicati.Library.Main.CLIDatabaseLocator.BackendEntry>
            {
                new()
                {
                    Type = "file", Server = "localhost", Path = "/backup",
                    Prefix = "dup", Username = "user", Port = 0,
                    Databasepath = backupDb, ParameterFile = null, IsSyncDb = false
                },
                new()
                {
                    Type = "file", Server = "localhost", Path = "/syncfound",
                    Prefix = "dup", Username = "user", Port = 0,
                    Databasepath = syncDbFound, ParameterFile = null, IsSyncDb = true
                },
                new()
                {
                    Type = "file", Server = "localhost", Path = "/syncmissing",
                    Prefix = "dup", Username = "user", Port = 0,
                    Databasepath = syncDbMissing, ParameterFile = null, IsSyncDb = true
                }
            };
            await File.WriteAllTextAsync(dbConfigPath, Newtonsoft.Json.JsonConvert.SerializeObject(dbConfig));

            // Run AnalyzeDatabasesAsync directly so the typed result is inspectable.
            var results = await Duplicati.CommandLine.DatabaseTool.Commands.Verify
                .AnalyzeDatabasesAsync(tempDir, includeServer: true);

            var byPath = results.ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);

            // The backup database is referenced by the server DB and dbconfig; classified Backup.
            Assert.IsTrue(byPath.ContainsKey(backupDb), "backup database should be reported");
            Assert.AreEqual(DatabaseType.Backup, byPath[backupDb].Type);
            Assert.AreEqual("Found", byPath[backupDb].Status);

            // The found sync database is referenced in dbconfig with IsSyncDb and exists;
            // it is classified Sync and reported as Found.
            Assert.IsTrue(byPath.ContainsKey(syncDbFound), "found sync database should be reported");
            Assert.AreEqual(DatabaseType.Sync, byPath[syncDbFound].Type);
            Assert.AreEqual("Found", byPath[syncDbFound].Status);
            Assert.IsTrue(byPath[syncDbFound].InDbConfigAsSync);

            // The missing sync database is referenced in dbconfig with IsSyncDb but does
            // not exist on disk; it is classified Sync (inferred from the reference) and
            // reported as Missing.
            Assert.IsTrue(byPath.ContainsKey(syncDbMissing), "missing sync database should be reported");
            Assert.AreEqual(DatabaseType.Sync, byPath[syncDbMissing].Type);
            Assert.AreEqual("Missing", byPath[syncDbMissing].Status);
            Assert.IsFalse(byPath[syncDbMissing].FileExists);
            Assert.IsTrue(byPath[syncDbMissing].InDbConfigAsSync);

            // The orphan sync database exists on disk but is not referenced; it is
            // classified Sync by examining the schema and reported as Orphaned.
            Assert.IsTrue(byPath.ContainsKey(syncDbOrphan), "orphan sync database should be reported");
            Assert.AreEqual(DatabaseType.Sync, byPath[syncDbOrphan].Type);
            Assert.AreEqual("Orphaned", byPath[syncDbOrphan].Status);
            Assert.IsFalse(byPath[syncDbOrphan].InDbConfigAsSync);

            // The server database is classified Server when included.
            Assert.IsTrue(byPath.ContainsKey(serverTargetPath), "server database should be reported");
            Assert.AreEqual(DatabaseType.Server, byPath[serverTargetPath].Type);
        }

        [Test]
        [Category("DatabaseTool")]
        [TestCase(true)]  // Dry run - no files should be deleted
        [TestCase(false)] // Force - only orphaned files should be deleted
        public async Task TestCleanupCommandAsync(bool dryRun)
        {
            using var tempFolder = new Library.Utility.TempFolder();
            var tempDir = (string)tempFolder;

            // Create three types of databases:
            // 1. FOUND: Referenced in server DB and exists
            var foundDb = Path.Combine(tempDir, "FOUNDDB.sqlite");
            // 2. MISSING: Referenced in dbconfig but doesn't exist
            var missingDb = Path.Combine(tempDir, "MISSINGDB.sqlite");
            // 3. ORPHANED: Exists but not referenced anywhere
            var orphanDb = Path.Combine(tempDir, "ORPHANXYZ.sqlite");

            var serverDb = Path.Combine(tempDir, "Duplicati-server.sqlite");

            // Create server database with a backup referencing foundDb
            using (var db = await SQLiteLoader.LoadConnectionAsync(serverDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = ServerSchemaV6;
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = $@"
                    INSERT INTO ""Backup"" (""Name"", ""Tags"", ""TargetURL"", ""DBPath"")
                    VALUES ('Test Backup', '', 'file:///test', '{foundDb.Replace("'", "''")}');
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Create "Found" database (referenced in server DB and exists)
            using (var db = await SQLiteLoader.LoadConnectionAsync(foundDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = LocalSchemaV12;
                await cmd.ExecuteNonQueryAsync();
            }

            // Create "Orphaned" database (exists but not referenced)
            using (var db = await SQLiteLoader.LoadConnectionAsync(orphanDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = LocalSchemaV12;
                await cmd.ExecuteNonQueryAsync();
            }

            // Create dbconfig.json with missingDb reference (file won't exist = Missing)
            var dbConfigPath = Path.Combine(tempDir, "dbconfig.json");
            var dbConfig = new List<Duplicati.Library.Main.CLIDatabaseLocator.BackendEntry>
            {
                new()
                {
                    Type = "file",
                    Server = "localhost",
                    Path = "/backup1",
                    Prefix = "dup",
                    Username = "user",
                    Port = 0,
                    Databasepath = missingDb, // This file won't exist = Missing
                    ParameterFile = null
                },
                new()
                {
                    Type = "file",
                    Server = "localhost",
                    Path = "/backup2",
                    Prefix = "dup",
                    Username = "user",
                    Port = 0,
                    Databasepath = foundDb, // This file exists = Found
                    ParameterFile = null
                }
            };
            await File.WriteAllTextAsync(dbConfigPath, Newtonsoft.Json.JsonConvert.SerializeObject(dbConfig));

            // Verify pre-cleanup state
            Assert.That(File.Exists(foundDb), Is.True, "Found DB should exist before cleanup");
            Assert.That(File.Exists(orphanDb), Is.True, "Orphan DB should exist before cleanup");
            Assert.That(File.Exists(missingDb), Is.False, "Missing DB should not exist before cleanup");

            // Run cleanup with appropriate flag
            string[] args = dryRun
                ? ["cleanup", "--datafolder", tempDir, "--dry-run"]
                : ["cleanup", "--datafolder", tempDir, "--force"];

            Assert.AreEqual(0, await Program.MainAsync(args));

            if (dryRun)
            {
                // Dry run: No files should be deleted
                Assert.That(File.Exists(foundDb), Is.True, "Found DB should still exist after dry-run");
                Assert.That(File.Exists(orphanDb), Is.True, "Orphan DB should still exist after dry-run");
                Assert.That(File.Exists(missingDb), Is.False, "Missing DB should still not exist after dry-run");
            }
            else
            {
                // Force cleanup: Only orphaned file should be deleted
                Assert.That(File.Exists(foundDb), Is.True, "Found DB should still exist after cleanup");
                Assert.That(File.Exists(orphanDb), Is.False, "Orphan DB should be deleted after cleanup");
                Assert.That(File.Exists(missingDb), Is.False, "Missing DB should still not exist after cleanup");
                Assert.That(File.Exists(serverDb), Is.True, "Server DB should still exist after cleanup");
            }
        }

        /// <summary>
        /// Verifies that cleanup never deletes a database with the server schema.
        /// The server database is not referenced by dbconfig.json or by its own
        /// Backup table, so every server-schema file (the active one and the copies
        /// the database upgrader leaves behind) is unreferenced by construction.
        /// Orphaned local databases are still deleted.
        /// </summary>
        [Test]
        [Category("DatabaseTool")]
        public async Task TestCleanupKeepsServerSchemaDatabasesAsync()
        {
            using var tempFolder = new Library.Utility.TempFolder();
            var tempDir = (string)tempFolder;

            var serverDb = Path.Combine(tempDir, "Duplicati-server.sqlite");
            var serverUpgradeCopy = Path.Combine(tempDir, "backup Duplicati-server 20261010120000.sqlite");
            var localUpgradeCopy = Path.Combine(tempDir, "backup ORPHANXYZ 20261010120000.sqlite");

            foreach (var path in new[] { serverDb, serverUpgradeCopy })
            {
                using var db = await SQLiteLoader.LoadConnectionAsync(path);
                using var cmd = db.CreateCommand();
                cmd.CommandText = ServerSchemaV6;
                await cmd.ExecuteNonQueryAsync();
            }

            using (var db = await SQLiteLoader.LoadConnectionAsync(localUpgradeCopy))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = LocalSchemaV12;
                await cmd.ExecuteNonQueryAsync();
            }

            Assert.AreEqual(0, await Program.MainAsync(["cleanup", "--datafolder", tempDir, "--force"]));

            Assert.That(File.Exists(serverDb), Is.True, "Active server DB should not be deleted");
            Assert.That(File.Exists(serverUpgradeCopy), Is.True, "Server upgrade copy should not be deleted");
            Assert.That(File.Exists(localUpgradeCopy), Is.False, "Orphaned local DB should be deleted");
        }

        /// <summary>
        /// Wipes encrypted fields from a server database and verifies that all
        /// <c>enc-v1:</c>-prefixed values are cleared, the <c>encrypted-fields</c>
        /// server setting is reset to <c>False</c>, and a backup file is produced.
        /// </summary>
        [Test]
        [Category("DatabaseTool")]
        public async Task TestWipeEncryptionServerDatabaseAsync()
        {
            using var tempFolder = new Library.Utility.TempFolder();
            var tempDir = (string)tempFolder;

            var serverDb = Path.Combine(tempDir, "Duplicati-server.sqlite");

            using (var db = await SQLiteLoader.LoadConnectionAsync(serverDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = ServerSchemaV6;
                await cmd.ExecuteNonQueryAsync();

                // Insert a backup with an encrypted TargetURL, a normal TargetURL,
                // an encrypted option (passphrase), a non-sensitive option, an
                // encrypted Source path, and the encrypted-fields server setting.
                var key = Duplicati.Library.Encryption.EncryptedFieldHelper.KeyInstance.CreateKey("a good and long password");
                var encryptedTargetUrl = Duplicati.Library.Encryption.EncryptedFieldHelper.Encrypt("s3://bucket/path?auth-password=secret", key);
                var encryptedSource = Duplicati.Library.Encryption.EncryptedFieldHelper.Encrypt("@/path|url://host/?auth-password=secret", key);
                var encryptedPassphrase = Duplicati.Library.Encryption.EncryptedFieldHelper.Encrypt("my-passphrase", key);

                cmd.CommandText = $@"
                    INSERT INTO ""Backup"" (""Name"", ""Tags"", ""TargetURL"", ""DBPath"")
                    VALUES ('EncryptedBackup', '', '{encryptedTargetUrl.Replace("'", "''")}', 'backup1.sqlite');
                    INSERT INTO ""Backup"" (""Name"", ""Tags"", ""TargetURL"", ""DBPath"")
                    VALUES ('PlainBackup', '', 'file:///plain', 'backup2.sqlite');
                    INSERT INTO ""Option"" (""BackupID"", ""Filter"", ""Name"", ""Value"")
                    VALUES (1, '', 'passphrase', '{encryptedPassphrase.Replace("'", "''")}');
                    INSERT INTO ""Option"" (""BackupID"", ""Filter"", ""Name"", ""Value"")
                    VALUES (1, '', 'dblock-size', '50MB');
                    INSERT INTO ""Source"" (""BackupID"", ""Path"")
                    VALUES (1, '{encryptedSource.Replace("'", "''")}');
                    INSERT INTO ""Option"" (""BackupID"", ""Filter"", ""Name"", ""Value"")
                    VALUES (-2, '', 'encrypted-fields', 'True');
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Run wipe-encryption. A backup should be created by default.
            Assert.AreEqual(0, await Program.MainAsync(["wipe-encryption", serverDb]));

            // Verify a backup file was created
            var backupFiles = Directory.GetFiles(tempDir, "Duplicati-server-*.bak");
            Assert.AreEqual(1, backupFiles.Length, "A backup file should have been created");

            // Verify the encrypted fields were cleared
            using (var db = await SQLiteLoader.LoadConnectionAsync(serverDb))
            using (var cmd = db.CreateCommand())
            {
                // The encrypted TargetURL should now be empty
                cmd.CommandText = @"SELECT ""TargetURL"" FROM ""Backup"" WHERE ""Name"" = 'EncryptedBackup'";
                Assert.AreEqual("", (string)cmd.ExecuteScalar());

                // The plain TargetURL should be untouched
                cmd.CommandText = @"SELECT ""TargetURL"" FROM ""Backup"" WHERE ""Name"" = 'PlainBackup'";
                Assert.AreEqual("file:///plain", (string)cmd.ExecuteScalar());

                // The encrypted passphrase option should be empty
                cmd.CommandText = @"SELECT ""Value"" FROM ""Option"" WHERE ""BackupID"" = 1 AND ""Name"" = 'passphrase'";
                Assert.AreEqual("", (string)cmd.ExecuteScalar());

                // The non-sensitive dblock-size option should be untouched
                cmd.CommandText = @"SELECT ""Value"" FROM ""Option"" WHERE ""BackupID"" = 1 AND ""Name"" = 'dblock-size'";
                Assert.AreEqual("50MB", (string)cmd.ExecuteScalar());

                // The encrypted source path should be empty
                cmd.CommandText = @"SELECT ""Path"" FROM ""Source"" WHERE ""BackupID"" = 1";
                Assert.AreEqual("", (string)cmd.ExecuteScalar());

                // The encrypted-fields server setting should be reset to False
                cmd.CommandText = @"SELECT ""Value"" FROM ""Option"" WHERE ""BackupID"" = -2 AND ""Name"" = 'encrypted-fields'";
                Assert.AreEqual("False", (string)cmd.ExecuteScalar());
            }

            // Clean up the backup file
            File.Delete(backupFiles[0]);
        }

        /// <summary>
        /// Verifies that <c>wipe-encryption</c> skips non-server databases (backup and
        /// sync databases) without modifying them, since the command only applies to the
        /// server database.
        /// </summary>
        [Test]
        [Category("DatabaseTool")]
        public async Task TestWipeEncryptionSkipsNonServerDatabasesAsync()
        {
            using var tempFolder = new Library.Utility.TempFolder();
            var tempDir = (string)tempFolder;

            var localDb = Path.Combine(tempDir, "local.sqlite");

            using (var db = await SQLiteLoader.LoadConnectionAsync(localDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = LocalSchemaV12;
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = @"
                    INSERT INTO ""Configuration"" (""Key"", ""Value"") VALUES ('passphrase', 'abc123hashed');
                    INSERT INTO ""Configuration"" (""Key"", ""Value"") VALUES ('passphrase-salt', 'v1:deadbeef');
                    INSERT INTO ""Configuration"" (""Key"", ""Value"") VALUES ('blocksize', '102400');
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Run wipe-encryption against the local (backup) database. It should be skipped.
            Assert.AreEqual(0, await Program.MainAsync(["wipe-encryption", localDb, "--no-backups"]));

            // No backup file should be created for a skipped database
            Assert.AreEqual(0, Directory.GetFiles(tempDir, "local-*.bak").Length, "No backup file should be created for a skipped database");

            // The database should be unchanged
            using (var db = await SQLiteLoader.LoadConnectionAsync(localDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"SELECT COUNT(*) FROM ""Configuration"" WHERE ""Key"" IN ('passphrase', 'passphrase-salt')";
                Assert.AreEqual(2, Convert.ToInt64(cmd.ExecuteScalar()));

                cmd.CommandText = @"SELECT ""Value"" FROM ""Configuration"" WHERE ""Key"" = 'passphrase'";
                Assert.AreEqual("abc123hashed", (string)cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// Verifies that <c>--dry-run</c> reports the encrypted fields that would be wiped
        /// from a server database without modifying it, and that no backup file is created.
        /// </summary>
        [Test]
        [Category("DatabaseTool")]
        public async Task TestWipeEncryptionDryRunAsync()
        {
            using var tempFolder = new Library.Utility.TempFolder();
            var tempDir = (string)tempFolder;

            var serverDb = Path.Combine(tempDir, "Duplicati-server.sqlite");

            using (var db = await SQLiteLoader.LoadConnectionAsync(serverDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = ServerSchemaV6;
                await cmd.ExecuteNonQueryAsync();

                var key = Duplicati.Library.Encryption.EncryptedFieldHelper.KeyInstance.CreateKey("a good and long password");
                var encryptedTargetUrl = Duplicati.Library.Encryption.EncryptedFieldHelper.Encrypt("s3://bucket/path?auth-password=secret", key);
                var encryptedPassphrase = Duplicati.Library.Encryption.EncryptedFieldHelper.Encrypt("my-passphrase", key);

                cmd.CommandText = $@"
                    INSERT INTO ""Backup"" (""Name"", ""Tags"", ""TargetURL"", ""DBPath"")
                    VALUES ('EncryptedBackup', '', '{encryptedTargetUrl.Replace("'", "''")}', 'backup1.sqlite');
                    INSERT INTO ""Option"" (""BackupID"", ""Filter"", ""Name"", ""Value"")
                    VALUES (1, '', 'passphrase', '{encryptedPassphrase.Replace("'", "''")}');
                    INSERT INTO ""Option"" (""BackupID"", ""Filter"", ""Name"", ""Value"")
                    VALUES (-2, '', 'encrypted-fields', 'True');
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Run with --dry-run
            Assert.AreEqual(0, await Program.MainAsync(["wipe-encryption", serverDb, "--dry-run"]));

            // No backup file should be created during a dry run
            Assert.AreEqual(0, Directory.GetFiles(tempDir, "Duplicati-server-*.bak").Length, "No backup file should be created during a dry run");

            // The database should be unchanged
            using (var db = await SQLiteLoader.LoadConnectionAsync(serverDb))
            using (var cmd = db.CreateCommand())
            {
                // The encrypted TargetURL should still be present
                cmd.CommandText = @"SELECT ""TargetURL"" FROM ""Backup"" WHERE ""Name"" = 'EncryptedBackup'";
                var targetUrl = (string)cmd.ExecuteScalar();
                Assert.IsTrue(targetUrl.StartsWith("enc-v1:"), "Encrypted TargetURL should be unchanged during a dry run");

                // The encrypted-fields server setting should still be True
                cmd.CommandText = @"SELECT ""Value"" FROM ""Option"" WHERE ""BackupID"" = -2 AND ""Name"" = 'encrypted-fields'";
                Assert.AreEqual("True", (string)cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// A Linux installation with no home folder used to resolve its data folder to the relative
        /// path "var/lib/Duplicati" and stored the database paths joined onto it, so the server
        /// database holds a path that names the file from the root without saying so. Resolving that
        /// against the data folder a second time points at a file that is not there, and the real
        /// database is then reported as orphaned - which is what "cleanup" offers to delete.
        /// </summary>
        [Test]
        [Category("DatabaseTool")]
        public async Task TestVerifyCommandWithLegacyRootlessDbPathAsync()
        {
            using var tempFolder = new Library.Utility.TempFolder();
            string tempDir = tempFolder;

            var serverTargetPath = Path.Combine(tempDir, "Duplicati-server.sqlite");
            var localDb = Path.Combine(tempDir, "local1.sqlite");

            // The data folder's own path without its root, which is the shape that was stored
            var fullDataFolder = Duplicati.Library.Common.IO.Util.AppendDirSeparator(Path.GetFullPath(tempDir));
            var legacyDbPath = fullDataFolder.Substring((Path.GetPathRoot(fullDataFolder) ?? "").Length) + "local1.sqlite";

            using (var db = await SQLiteLoader.LoadConnectionAsync(serverTargetPath))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = ServerSchemaV6;
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = $@"
                    INSERT INTO ""Backup"" (""Name"", ""Tags"", ""TargetURL"", ""DBPath"")
                    VALUES ('Test Backup', '', 'file:///test', '{legacyDbPath.Replace("'", "''")}');
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            using (var db = await SQLiteLoader.LoadConnectionAsync(localDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = LocalSchemaV12;
                await cmd.ExecuteNonQueryAsync();
            }

            var results = await Duplicati.CommandLine.DatabaseTool.Commands.Verify
                .AnalyzeDatabasesAsync(tempDir, includeServer: true);

            var byPath = results.ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);

            Assert.IsTrue(byPath.ContainsKey(localDb), "the database the backup uses should be reported");
            Assert.AreEqual("Found", byPath[localDb].Status,
                "the database the backup uses was reported as orphaned, which is what cleanup deletes");
        }
    }
}