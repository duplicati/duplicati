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
using Duplicati.CommandLine.DatabaseTool;
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
            Assert.AreEqual(0, await Program.MainAsync(["downgrade", dbfile, "--server-version=6", "--local-version=12", "--no-backups"]));
            Assert.AreEqual(0, await Program.MainAsync(["upgrade", dbfile, "--no-backups"]));

            Assert.AreEqual(0, await Program.MainAsync(["list", dbfile]));
            Assert.AreEqual(0, await Program.MainAsync(["list", dbfile, "RemoteVolume"]));
            Assert.AreEqual(0, await Program.MainAsync(["list", dbfile, "RemoteVolume", "--output-json"]));
            Assert.AreEqual(0, await Program.MainAsync(["execute", dbfile, "SELECT * FROM RemoteVolume", "--output-json"]));
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
            Assert.AreEqual(0, await Program.MainAsync(["downgrade", dbfile, "--server-version=6", "--local-version=12", "--no-backups"]));
            Assert.AreEqual(0, await Program.MainAsync(["upgrade", dbfile, "--no-backups"]));

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
            // Schema.sql and records the current sync schema version (0).
            using (var db = await SQLiteLoader.LoadConnectionAsync(dbfile))
            {
                DatabaseUpgrader.UpgradeDatabase(db, dbfile, typeof(DatabaseSchemaMarker));
            }

            // The sync schema is currently at version 0 and there are no numbered
            // upgrade scripts. The upgrade command falls back to target version 1
            // (syncVersions.Any() ? Max : 1) and, finding no script for version 1,
            // reports the missing script and returns success without modifying the DB.
            Assert.AreEqual(0, await Program.MainAsync(["upgrade", dbfile, "--no-backups"]));

            // No sync downgrade scripts ship today, so downgrade reports the missing
            // scripts and returns success without modifying the DB. The default
            // --sync-version is 1 and the DB is at 0, so there is nothing to downgrade
            // and the command is a no-op that still exits 0.
            Assert.AreEqual(0, await Program.MainAsync(["downgrade", dbfile, "--sync-version=1", "--no-backups"]));

            // Re-running upgrade should remain a no-op and still succeed.
            Assert.AreEqual(0, await Program.MainAsync(["upgrade", dbfile, "--no-backups"]));

            // The database must be untouched by the no-op upgrade/downgrade: the
            // recorded version is still 0 and the sync tables are still present.
            Assert.AreEqual(SYNC_SCHEMA_VERSION, await ReadSyncVersionAsync(dbfile));
            Assert.IsTrue(await SyncTableExistsAsync(dbfile, "RemoteInventory"));
            Assert.IsTrue(await SyncTableExistsAsync(dbfile, "PendingOperation"));
            Assert.IsTrue(await SyncTableExistsAsync(dbfile, "RemoteOperation"));

            // The list and execute commands should work against the sync database
            // just as they do for the server and local databases.
            Assert.AreEqual(0, await Program.MainAsync(["list", dbfile]));
            Assert.AreEqual(0, await Program.MainAsync(["list", dbfile, "RemoteInventory"]));
            Assert.AreEqual(0, await Program.MainAsync(["list", dbfile, "RemoteInventory", "--output-json"]));
            Assert.AreEqual(0, await Program.MainAsync(["execute", dbfile, "SELECT * FROM RemoteInventory", "--output-json"]));

            // The helper must classify a sync database as DatabaseType.Sync at the
            // recorded version, proving the tool will route a sync database to the
            // sync upgrade/downgrade scripts once any are added.
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
        /// and must be kept in sync if the sync schema is bumped. Today the sync schema
        /// ships at version 0 with no numbered upgrade scripts.
        /// </summary>
        private const int SYNC_SCHEMA_VERSION = 0;

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
            }
        }
    }
}