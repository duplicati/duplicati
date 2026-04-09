// Copyright (C) 2025, The Duplicati Team
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
using System.Threading.Tasks;
using Duplicati.CommandLine.DatabaseTool;
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
        public async Task TestLocalDbMethods()
        {
            using var dbfile = new TempFile();
            using var db = SQLiteLoader.LoadConnection(dbfile);
            using var cmd = db.CreateCommand();
            cmd.CommandText = LocalSchemaV12;
            cmd.ExecuteNonQuery();

            Assert.AreEqual(0, await Program.Main(["upgrade", dbfile, "--no-backups"]));
            Assert.AreEqual(0, await Program.Main(["downgrade", dbfile, "--server-version=6", "--local-version=12", "--no-backups"]));
            Assert.AreEqual(0, await Program.Main(["upgrade", dbfile, "--no-backups"]));

            Assert.AreEqual(0, await Program.Main(["list", dbfile]));
            Assert.AreEqual(0, await Program.Main(["list", dbfile, "RemoteVolume"]));
            Assert.AreEqual(0, await Program.Main(["list", dbfile, "RemoteVolume", "--output-json"]));
            Assert.AreEqual(0, await Program.Main(["execute", dbfile, "SELECT * FROM RemoteVolume", "--output-json"]));
        }

        [Test]
        [Category("DatabaseTool")]
        public async Task TestServerDbMethods()
        {
            using var dbfile = new TempFile();
            using var db = SQLiteLoader.LoadConnection(dbfile);
            using var cmd = db.CreateCommand();
            cmd.CommandText = ServerSchemaV6;
            cmd.ExecuteNonQuery();

            Assert.AreEqual(0, await Program.Main(["upgrade", dbfile, "--no-backups"]));
            Assert.AreEqual(0, await Program.Main(["downgrade", dbfile, "--server-version=6", "--local-version=12", "--no-backups"]));
            Assert.AreEqual(0, await Program.Main(["upgrade", dbfile, "--no-backups"]));

            Assert.AreEqual(0, await Program.Main(["list", dbfile]));
            Assert.AreEqual(0, await Program.Main(["list", dbfile, "Source"]));
            Assert.AreEqual(0, await Program.Main(["list", dbfile, "Source", "--output-json"]));
            Assert.AreEqual(0, await Program.Main(["execute", dbfile, "SELECT * FROM Source", "--output-json"]));
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

        [Test]
        [Category("DatabaseTool")]
        public async Task TestVerifyCommand()
        {
            using var tempFolder = new Library.Utility.TempFolder();
            string tempDir = tempFolder;

            var serverDb = Path.Combine(tempDir, "server.sqlite");
            var localDb1 = Path.Combine(tempDir, "local1.sqlite");
            var localDb2 = Path.Combine(tempDir, "local2.sqlite"); // Referenced but won't exist = Missing
            var orphanDb = Path.Combine(tempDir, "ABCDEFGHIJ.sqlite"); // Random name = Orphaned

            // Create server database
            using (var db = SQLiteLoader.LoadConnection(serverDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = ServerSchemaV6;
                cmd.ExecuteNonQuery();

                // Insert a backup referencing localDb1
                cmd.CommandText = $@"
                    INSERT INTO ""Backup"" (""Name"", ""Tags"", ""TargetURL"", ""DBPath"")
                    VALUES ('Test Backup', '', 'file:///test', '{localDb1.Replace("'", "''")}');
                ";
                cmd.ExecuteNonQuery();
            }

            // Create local database
            using (var db = SQLiteLoader.LoadConnection(localDb1))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = LocalSchemaV12;
                cmd.ExecuteNonQuery();
            }

            // Create orphan database (exists but not referenced)
            using (var db = SQLiteLoader.LoadConnection(orphanDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = LocalSchemaV12;
                cmd.ExecuteNonQuery();
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
                Assert.AreEqual(0, await Program.Main(["verify", "--datafolder", tempDir, "--output-json"]));
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
        [TestCase(true)]  // Dry run - no files should be deleted
        [TestCase(false)] // Force - only orphaned files should be deleted
        public async Task TestCleanupCommand(bool dryRun)
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
            using (var db = SQLiteLoader.LoadConnection(serverDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = ServerSchemaV6;
                cmd.ExecuteNonQuery();

                cmd.CommandText = $@"
                    INSERT INTO ""Backup"" (""Name"", ""Tags"", ""TargetURL"", ""DBPath"")
                    VALUES ('Test Backup', '', 'file:///test', '{foundDb.Replace("'", "''")}');
                ";
                cmd.ExecuteNonQuery();
            }

            // Create "Found" database (referenced in server DB and exists)
            using (var db = SQLiteLoader.LoadConnection(foundDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = LocalSchemaV12;
                cmd.ExecuteNonQuery();
            }

            // Create "Orphaned" database (exists but not referenced)
            using (var db = SQLiteLoader.LoadConnection(orphanDb))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = LocalSchemaV12;
                cmd.ExecuteNonQuery();
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

            Assert.AreEqual(0, await Program.Main(args));

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