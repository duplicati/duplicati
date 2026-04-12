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

using Duplicati.Library.Main.Operation;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using System.Threading.Tasks;
using System.IO;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class RemoteSynchronizationHandlerTests : BasicSetupHelper
    {
        const System.Reflection.BindingFlags BINDING_FLAGS = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        string source = "";
        string dest1 = "";
        string dest2 = "";
        string dest3 = "";

        [SetUp]
        public void Setup()
        {
            // On Windows, we have to escape backslashes in paths for JSON strings
            source = Path.Combine(TARGETFOLDER, "source").Replace("\\", "\\\\");
            dest1 = Path.Combine(TARGETFOLDER, "dest1").Replace("\\", "\\\\");
            dest2 = Path.Combine(TARGETFOLDER, "dest2").Replace("\\", "\\\\");
            dest3 = Path.Combine(TARGETFOLDER, "dest3").Replace("\\", "\\\\");
        }

        private static void EnsureOperationTableCreated(Microsoft.Data.Sqlite.SqliteCommand cmd)
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""Operation"" (
                    ""Description"" TEXT,
                    ""Timestamp"" INTEGER
                )";
            cmd.ExecuteNonQuery();
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_SingleDestination()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}}
                ]}}"
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            Assert.AreEqual(1, destinations.Count);
            // Convert the escaped backslashes to single backslashes for comparison on Windows
            Assert.AreEqual($"file://{dest1.Replace("\\\\", "\\")}", destinations[0].Config.Dst);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_MultipleDestinations()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}},
                    {{""url"": ""file://{dest2}""}}
                ]}}"
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;

            Assert.AreEqual(2, destinations.Count);
            // Convert the escaped backslashes to single backslashes for comparison on Windows
            Assert.AreEqual($"file://{dest1.Replace("\\\\", "\\")}", destinations[0].Config.Dst);
            Assert.AreEqual($"file://{dest2.Replace("\\\\", "\\")}", destinations[1].Config.Dst);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_Modes()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""inline""}},
                    {{""url"": ""file://{dest2}"", ""mode"": ""interval""}}
                ]}}"
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;

            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual(RemoteSyncTriggerMode.Inline, destinations[0].Mode);
            Assert.AreEqual(RemoteSyncTriggerMode.Interval, destinations[1].Mode);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_Schedules()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""interval"": ""1h""}},
                    {{""url"": ""file://{dest2}"", ""interval"": ""2h""}}
                ]}}"
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;

            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual(TimeSpan.FromHours(1), destinations[0].Interval);
            Assert.AreEqual(TimeSpan.FromHours(2), destinations[1].Interval);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_Counts()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""count"": 5}},
                    {{""url"": ""file://{dest2}"", ""count"": 10}}
                ]}}"
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;

            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual(5, destinations[0].Count);
            Assert.AreEqual(10, destinations[1].Count);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_SetsSource()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}}
                ]}}"
            };

            string remoteurl = $"file://{source}";
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), new BackupResults());

            var stored_source = handler.GetType().GetField("m_source", BINDING_FLAGS).GetValue(handler) as string;

            Assert.AreEqual(remoteurl, stored_source);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Inline()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""inline""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)handler.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(handler, [0, destinations[0]]);

            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Scheduled_FirstTime()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""interval"", ""interval"": ""1h""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            // Create Operation table as we're now calling outside of the Controller
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)handler.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(handler, [0, destinations[0]]);

            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Counting_FirstTime()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""counting"", ""count"": 2}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            // Create Operation table and simulate 2 backups
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);
            cmd.CommandText = @"
                INSERT INTO ""Operation"" (
                    ""Description"",
                    ""Timestamp""
                ) VALUES (
                    'Backup',
                    @ts
                )";
            cmd.AddNamedParameter("@ts", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow));
            cmd.ExecuteNonQuery();
            cmd.ExecuteNonQuery();

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)handler.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(handler, [0, destinations[0]]);

            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestRecordSyncOperation()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);
            cmd.ExecuteNonQuery();

            handler.GetType().GetMethod("RecordSyncOperation", BINDING_FLAGS).Invoke(handler, [0]);

            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" = 'Rsync 0'
            ";
            var count = (long)cmd.ExecuteScalar();
            Assert.AreEqual(1, count);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestRunAsync_SuccessfulBackup_TriggersSync()
        {
            // Create some test files
            System.IO.File.WriteAllText(System.IO.Path.Combine(DATAFOLDER, "testfile.txt"), "test content");

            var options = TestOptions;
            options["remote-sync-json-config"] = $@"{{""destinations"": [{{""url"": ""file://{dest1}""}}]}}";
            options["dbpath"] = DBFILE;

            // Run actual backup
            using var console = new CommandLine.ConsoleOutput(Console.Out, options);
            using var controller = new Controller($"file://{TARGETFOLDER}", options, console);
            var result = controller.Backup([DATAFOLDER]);

            // Now check if sync was triggered by checking if Operation table
            // has sync entry
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" LIKE 'Rsync %'
            ";
            var count = (long)cmd.ExecuteScalar();

            Assert.AreEqual(1, count);
        }

        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_FailedBackup_SkipsSync()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [{{""url"": ""file://{dest1}""}}]}}",
                ["dbpath"] = DBFILE
            };

            string remoteurl = $"file://{source}";
            var results = new BasicBackupResults(ParsedResultType.Error);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), results);

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            await handler.RunAsync();

            // Check if sync was not recorded
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" = 'Rsync 0'
            ";
            var count = (long)cmd.ExecuteScalar();

            Assert.AreEqual(0, count);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_InvalidMode_DefaultsToInline()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""inline""}},
                    {{""url"": ""file://{dest2}"", ""mode"": ""invalidmode""}}
                ]}}"
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;

            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual(RemoteSyncTriggerMode.Inline, destinations[0].Mode);
            Assert.AreEqual(RemoteSyncTriggerMode.Inline, destinations[1].Mode); // Invalid defaults to Inline
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_InvalidSchedule_DefaultsToNull()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""interval"": ""1h""}},
                    {{""url"": ""file://{dest2}"", ""interval"": ""invalid""}}
                ]}}"
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;

            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual(TimeSpan.FromHours(1), destinations[0].Interval);
            Assert.AreEqual(null, destinations[1].Interval); // Invalid defaults to null
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_InvalidCount_Fails()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""count"": 5}},
                    {{""url"": ""file://{dest2}"", ""count"": ""invalid""}}
                ]}}"
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;

            // Deserializing the json should fail for the invalid count, resulting in no destinations being configured
            Assert.AreEqual(0, destinations.Count);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_SyncOnWarnings()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""sync-on-warnings"": false, ""destinations"": [
                    {{""url"": ""file://{dest1}""}}
                ]}}"
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var syncOnWarnings = handler.GetType().GetField("m_syncOnWarnings", BINDING_FLAGS).GetValue(handler);
            Assert.IsFalse((bool)syncOnWarnings);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Scheduled_Initial()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""interval"", ""interval"": ""1h""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            // Create Operation table with no prior syncs
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)handler.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(handler, [0, destinations[0]]);

            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Scheduled_NotDue()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""interval"", ""interval"": ""1h""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            // Create Operation table and insert recent sync
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);
            cmd.CommandText = @"
                INSERT INTO ""Operation"" (
                    ""Description"",
                    ""Timestamp""
                ) VALUES (
                    'Rsync 0',
                    @ts
                )";
            cmd.AddNamedParameter("@ts", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow));
            cmd.ExecuteNonQuery();

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)handler.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(handler, [0, destinations[0]]);

            Assert.IsFalse(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Scheduled_Due()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""interval"", ""interval"": ""1h""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            // Create Operation table and insert recent sync
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);
            cmd.CommandText = @"
                INSERT INTO ""Operation"" (
                    ""Description"",
                    ""Timestamp""
                ) VALUES (
                    'Rsync 0',
                    @ts
                )";
            cmd.AddNamedParameter("@ts", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow - TimeSpan.FromHours(2)));
            cmd.ExecuteNonQuery();

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)handler.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(handler, [0, destinations[0]]);

            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Counting_Initial()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""counting"", ""count"": 5}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            // Create Operation table and simulate 0 backups, which should trigger sync
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)handler.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(handler, [0, destinations[0]]);

            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Counting_NotReached()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""counting"", ""count"": 5}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            // Create Operation table and simulate 2 backups (less than 5)
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd_backup = db.CreateCommand();
            using var cmd_rsync = db.CreateCommand();
            EnsureOperationTableCreated(cmd_backup);

            cmd_rsync.CommandText = @"
                INSERT INTO ""Operation"" (
                    ""Description"",
                    ""Timestamp""
                ) VALUES (
                    'Rsync 0',
                    @ts
                )";
            cmd_rsync.AddNamedParameter("@ts");

            cmd_backup.CommandText = @"
                INSERT INTO ""Operation"" (
                    ""Description"",
                    ""Timestamp""
                ) VALUES (
                    'Backup',
                    @ts
                )";
            cmd_backup.AddNamedParameter("@ts");

            var curtime = Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow);
            cmd_backup.SetParameterValue("@ts", curtime - 5);
            cmd_backup.ExecuteNonQuery();
            cmd_rsync.SetParameterValue("@ts", curtime - 4);
            cmd_rsync.ExecuteNonQuery();
            cmd_backup.SetParameterValue("@ts", curtime - 3);
            cmd_backup.ExecuteNonQuery();
            cmd_backup.SetParameterValue("@ts", curtime - 2);
            cmd_backup.ExecuteNonQuery();

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)handler.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(handler, [0, destinations[0]]);

            Assert.IsFalse(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Counting_Reached()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""counting"", ""count"": 2}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            // Create Operation table and simulate 2 backups (less than 5)
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd_backup = db.CreateCommand();
            using var cmd_rsync = db.CreateCommand();
            EnsureOperationTableCreated(cmd_backup);

            cmd_rsync.CommandText = @"
                INSERT INTO ""Operation"" (
                    ""Description"",
                    ""Timestamp""
                ) VALUES (
                    'Rsync 0',
                    @ts
                )";
            cmd_rsync.AddNamedParameter("@ts");

            cmd_backup.CommandText = @"
                INSERT INTO ""Operation"" (
                    ""Description"",
                    ""Timestamp""
                ) VALUES (
                    'Backup',
                    @ts
                )";
            cmd_backup.AddNamedParameter("@ts");

            var curtime = Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow);
            cmd_backup.SetParameterValue("@ts", curtime - 5);
            cmd_backup.ExecuteNonQuery();
            cmd_rsync.SetParameterValue("@ts", curtime - 4);
            cmd_rsync.ExecuteNonQuery();
            cmd_backup.SetParameterValue("@ts", curtime - 3);
            cmd_backup.ExecuteNonQuery();
            cmd_backup.SetParameterValue("@ts", curtime - 2);
            cmd_backup.ExecuteNonQuery();

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)handler.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(handler, [0, destinations[0]]);

            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_MissingDbPath_ReturnsFalse()
        {
            // This case shouldn't occur in normal operation, as a backup will
            // always have an associated database. However, if such a state
            // should occur, we want to ensure the handler doesn't trigger a
            // sync, as the state is likely errornous.
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""inline""}}
                ]}}"
                // No dbpath
            };
            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)handler.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(handler, [0, destinations[0]]);

            Assert.IsTrue(shouldTrigger); // Inline always returns true
        }

        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_NonBackupOperation_SkipsSync()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            string remoteurl = $"file://{source}";
            var results = new BasicBackupResults(ParsedResultType.Success);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), results);

            await handler.RunAsync();

            // Check if sync was not recorded
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" = 'Rsync 0'
            ";
            var count = (long)cmd.ExecuteScalar();

            Assert.AreEqual(0, count);
        }

        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_FatalResult_SkipsSync()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}}
                ]}}",
                ["dbpath"] = DBFILE
            };
            string remoteurl = $"file://{source}";
            var results = new BasicBackupResults(ParsedResultType.Fatal);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), results);

            await handler.RunAsync();

            // Check if sync was not recorded
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" = 'Rsync 0'
            ";
            var count = (long)cmd.ExecuteScalar();

            Assert.AreEqual(0, count);
        }


        [Test]
        [Category("RemoteSync")]
        public void TestRecordSyncOperation_MissingDbPath_NoCrash()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}}
                ]}}"
                // No dbpath
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            // Should not throw exception
            Assert.DoesNotThrow(() => handler.GetType().GetMethod("RecordSyncOperation", BINDING_FLAGS).Invoke(handler, [0]));
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_DefaultMode()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}},
                    {{""url"": ""file://{dest2}"", ""mode"": ""interval""}}
                ]}}"
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;

            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual(RemoteSyncTriggerMode.Inline, destinations[0].Mode); // Default
            Assert.AreEqual(RemoteSyncTriggerMode.Interval, destinations[1].Mode);
        }

        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_WithEmptyDestinations_SkipsSync()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}},
                    {{""url"": """"}},
                    {{""url"": ""file://{dest2}""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            string remoteurl = $"file://{source}/";
            var results = new BasicBackupResults(ParsedResultType.Success);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), results);

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            await handler.RunAsync();

            // Check that two syncs were recorded (skipping the empty one)
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description""
                LIKE 'Rsync %'
            ";
            var count = (long)cmd.ExecuteScalar();

            Assert.AreEqual(2, count); // Two valid destinations, empty skipped
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_OutOfRangeIndex_ReturnsFalse()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""interval""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)handler.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(handler, [1, destinations[0]]); // Index 1, but only 1 destination

            Assert.IsFalse(shouldTrigger); // Out of range
        }

        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_WithMultipleDestinations_SelectiveSync()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""inline""}},
                    {{""url"": ""file://{dest2}"", ""mode"": ""interval"", ""interval"": ""1h""}}
                ]}}",
                ["dbpath"] = DBFILE
            };
            string remoteurl = $"file://{source}";
            var results = new BasicBackupResults(ParsedResultType.Success);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), results);

            // Create Operation table and insert recent sync for dest2
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);
            cmd.CommandText = @"
                INSERT INTO ""Operation"" (
                    ""Description"",
                    ""Timestamp""
                ) VALUES (
                    'Rsync 1',
                    @ts
                )";
            cmd.AddNamedParameter("@ts", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow));
            cmd.ExecuteNonQuery();

            await handler.RunAsync();

            // Check that one additional sync was recorded (dest1 inline, dest2 interval not due)
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" LIKE 'Rsync %'
            ";
            var count = (long)cmd.ExecuteScalar();

            Assert.AreEqual(2, count); // Initial 1 + 1 new = 2
        }

        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_WithWarningResult_TriggersSync()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            string remoteurl = $"file://{source}";
            var result = new BasicBackupResults(ParsedResultType.Warning);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), result);

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            await handler.RunAsync();

            // Check that sync was recorded
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" = 'Rsync 0'
            ";
            var count = (long)cmd.ExecuteScalar();

            Assert.AreEqual(1, count);
        }

        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_WithWarningResult_SkipsSync_WhenDisabled()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""sync-on-warnings"": false, ""destinations"": [
                    {{""url"": ""file://{dest1}""}}
                ]}}",
                ["dbpath"] = DBFILE
            };
            string remoteurl = $"file://{source}";
            var result = new BasicBackupResults(ParsedResultType.Warning);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), result);

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            await handler.RunAsync();

            // Check that sync was not recorded
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" = 'Rsync 0'
            ";
            var count = (long)cmd.ExecuteScalar();

            Assert.AreEqual(0, count);
        }

        //TODO format destinations

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_WithWhitespaceInDestinations_PreservesWhitespace()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""  file://{dest1}  ""}},
                    {{""url"": ""  file://{dest2}  ""}}
                ]}}"
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;

            Assert.AreEqual(2, destinations.Count);
            // Convert the escaped backslashes to single backslashes for comparison on Windows
            Assert.AreEqual($"  file://{dest1.Replace("\\\\", "\\")}  ", destinations[0].Config.Dst);
            Assert.AreEqual($"  file://{dest2.Replace("\\\\", "\\")}  ", destinations[1].Config.Dst);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_WithEmptyDestinationsInList_Skips()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}},
                    {{}},
                    {{""url"": ""file://{dest2}""}}
                ]}}"
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;

            Assert.AreEqual(3, destinations.Count); // All are included, empty ones skipped in RunAsync
            // Convert the escaped backslashes to single backslashes for comparison on Windows
            Assert.AreEqual($"file://{dest1.Replace("\\\\", "\\")}", destinations[0].Config.Dst);
            Assert.AreEqual("", destinations[1].Config.Dst);
            Assert.AreEqual($"file://{dest2.Replace("\\\\", "\\")}", destinations[2].Config.Dst);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestRecordSyncOperation_WithDifferentIndex()
        {
            // Incorrect index should not record, but throw a warning.
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            handler.GetType().GetMethod("RecordSyncOperation", BINDING_FLAGS).Invoke(handler, [2]);

            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" = 'Rsync 2'
            ";
            var count = (long)cmd.ExecuteScalar();
            Assert.AreEqual(0, count);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestRecordSyncOperation_WithMissingTable_ThrowsException()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [{{""url"": ""file://{dest1}""}}]}}",
                ["dbpath"] = DBFILE
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            // Do not create Operation table

            Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                handler.GetType().GetMethod("RecordSyncOperation", BINDING_FLAGS).Invoke(handler, [0]));
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_InvalidJson_DoesNotActivate()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"invalid json"
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var enabled = handler.GetType().GetField("m_enabled", BINDING_FLAGS).GetValue(handler);
            Assert.IsFalse((bool)enabled);
        }

        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_WithNoDestinations_DoesNotActivate()
        {
            var options = new Dictionary<string, string>
            {
                // No remote-sync-json-config
                ["dbpath"] = DBFILE
            };

            string remoteurl = $"file://{source}";
            var results = new BasicBackupResults(ParsedResultType.Success);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), results);

            var enabled = handler.GetType().GetField("m_enabled", BINDING_FLAGS).GetValue(handler);
            Assert.IsFalse((bool)enabled);

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            await handler.RunAsync();

            // Check that no sync was recorded
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description""
                LIKE 'Rsync %'
            ";
            var count = (long)cmd.ExecuteScalar();

            Assert.AreEqual(0, count);
        }


        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_ForNonBackupOperations_SkipsSync()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            string remoteurl = $"file://{source}";
            var results = new BasicBackupResults(ParsedResultType.Success);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), results);

            await handler.RunAsync();

            // Check if sync was not recorded
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" = 'Rsync 0'
            ";
            var count = (long)cmd.ExecuteScalar();

            Assert.AreEqual(0, count);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestMultipleBackups_WithCountingDestinations()
        {
            // Create test files: one folder with 2 files, 2 subfolders each with 2 files, all ~1kb
            var testDataFolder = System.IO.Path.Combine(DATAFOLDER, "testdata");
            System.IO.Directory.CreateDirectory(testDataFolder);
            var sub1 = System.IO.Path.Combine(testDataFolder, "sub1");
            var sub2 = System.IO.Path.Combine(testDataFolder, "sub2");
            System.IO.Directory.CreateDirectory(sub1);
            System.IO.Directory.CreateDirectory(sub2);

            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var rng = new Random();
            var buf = new byte[1024];
            void NewRandomFile(string folder, string filename)
            {
                rng.NextBytes(buf);
                var content = new string([.. buf.Select(c => chars[c % chars.Length])]);
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder, filename), content);
            }
            NewRandomFile(testDataFolder, "file1.txt");
            NewRandomFile(testDataFolder, "file2.txt");
            NewRandomFile(sub1, "file3.txt");
            NewRandomFile(sub1, "file4.txt");
            NewRandomFile(sub2, "file5.txt");
            NewRandomFile(sub2, "file6.txt");

            // Sync destinations
            var syncDest1 = System.IO.Path.Combine(BASEFOLDER, "syncdest1");
            var syncDest2 = System.IO.Path.Combine(BASEFOLDER, "syncdest2");
            var syncDest3 = System.IO.Path.Combine(BASEFOLDER, "syncdest3");
            // Create the folders so that the destination exists.
            if (!System.IO.Directory.Exists(syncDest1))
                System.IO.Directory.CreateDirectory(syncDest1);
            if (!System.IO.Directory.Exists(syncDest2))
                System.IO.Directory.CreateDirectory(syncDest2);
            if (!System.IO.Directory.Exists(syncDest3))
                System.IO.Directory.CreateDirectory(syncDest3);

            var options = TestOptions;
            options["remote-sync-json-config"] = $@"
            {{
                ""destinations"": [
                    {{
                        ""url"": ""file://{syncDest1.Replace("\\", "\\\\")}""
                    }},
                    {{
                        ""url"": ""file://{syncDest2.Replace("\\", "\\\\")}"",
                        ""mode"": ""counting"",
                        ""count"": 2
                    }},
                    {{
                        ""url"": ""file://{syncDest3.Replace("\\", "\\\\")}"",
                        ""mode"": ""counting"",
                        ""count"": 3
                    }}
                ]
            }}";
            options["dbpath"] = DBFILE;

            // First backup
            void Backup()
            {
                Task.Delay(1000).Wait(); // Ensure timestamp difference between backups
                using var console = new CommandLine.ConsoleOutput(Console.Out, options);
                using var controller = new Controller("file://" + TARGETFOLDER, options, console);
                var result = controller.Backup([DATAFOLDER]);
                Assert.AreEqual(ParsedResultType.Success, result.ParsedResult);
            }
            Backup();

            // Assert all destinations have same files
            bool ContainsSameFiles(string path1, string path2, bool shouldAssert = true)
            {
                bool matches = true;
                var files1 = System.IO.Directory.GetFiles(path1, "*", System.IO.SearchOption.TopDirectoryOnly)
                    .Select(f => f.Substring(path1.Length).TrimStart(System.IO.Path.DirectorySeparatorChar))
                    .OrderBy(f => f)
                    .ToArray();
                var files2 = System.IO.Directory.GetFiles(path2, "*", System.IO.SearchOption.TopDirectoryOnly)
                    .Select(f => f.Substring(path2.Length).TrimStart(System.IO.Path.DirectorySeparatorChar))
                    .OrderBy(f => f)
                    .ToArray();

                if (shouldAssert)
                    Assert.AreEqual(files1.Length, files2.Length, $"File count mismatch between {path1} and {path2}");
                matches &= files1.Length == files2.Length;
                if (!matches)
                    return false;

                for (int i = 0; i < files1.Length; i++)
                {
                    if (shouldAssert)
                        Assert.AreEqual(files1[i], files2[i], $"Filename mismatch: {files1[i]} vs {files2[i]}");
                    matches &= files1[i] == files2[i];

                    var content1 = System.IO.File.ReadAllText(System.IO.Path.Combine(path1, files1[i]));
                    var content2 = System.IO.File.ReadAllText(System.IO.Path.Combine(path2, files2[i]));
                    if (shouldAssert)
                        Assert.AreEqual(content1, content2, $"File content mismatch for {files1[i]}");
                    matches &= content1 == content2;
                }

                var folders1 = System.IO.Directory.GetDirectories(path1, "*", System.IO.SearchOption.TopDirectoryOnly)
                    .Select(f => f.Substring(path1.Length).TrimStart(System.IO.Path.DirectorySeparatorChar))
                    .OrderBy(f => f)
                    .ToArray();
                var folders2 = System.IO.Directory.GetDirectories(path2, "*", System.IO.SearchOption.TopDirectoryOnly)
                    .Select(f => f.Substring(path2.Length).TrimStart(System.IO.Path.DirectorySeparatorChar))
                    .OrderBy(f => f)
                    .ToArray();
                if (shouldAssert)
                    Assert.AreEqual(folders1.Length, folders2.Length, $"Folder count mismatch between {path1} and {path2}");
                matches &= folders1.Length == folders2.Length;
                if (!matches)
                    return false;

                for (int i = 0; i < folders1.Length; i++)
                {
                    if (shouldAssert)
                        Assert.AreEqual(folders1[i], folders2[i], $"Folder name mismatch: {folders1[i]} vs {folders2[i]}");
                    matches &= folders1[i] == folders2[i];

                    // Recursively check subfolders
                    matches &= ContainsSameFiles(System.IO.Path.Combine(path1, folders1[i]), System.IO.Path.Combine(path2, folders2[i]), shouldAssert);
                }

                return matches;
            }

            bool DestinationsAreEqual(bool shouldAssert = true, params string[] paths)
            {
                bool matches = true;
                for (int i = 1; i < paths.Length; i++)
                    matches &= ContainsSameFiles(paths[0], paths[i], shouldAssert);
                return matches;
            }

            DestinationsAreEqual(true, TARGETFOLDER, syncDest1, syncDest2, syncDest3);

            // Assert restore from each works
            void AssertRestoreWorks(string[] Urls)
            {
                var restoreFolders = Urls.Select(url => System.IO.Path.Combine(BASEFOLDER, "restore_" + System.IO.Path.GetRandomFileName())).ToArray();
                foreach (var (url, restoreFolder) in Urls.Zip(restoreFolders))
                {
                    System.IO.Directory.CreateDirectory(restoreFolder);
                    var dataFolder = System.IO.Path.Combine(restoreFolder, "data");
                    System.IO.Directory.CreateDirectory(dataFolder);

                    var restoreOptions = new Dictionary<string, string>(TestOptions)
                    {
                        ["dbpath"] = System.IO.Path.Combine(restoreFolder, "db.sqlite"),
                        ["restore-path"] = dataFolder
                    };

                    using var console = new CommandLine.ConsoleOutput(Console.Out, restoreOptions);
                    using var controller = new Controller(url, restoreOptions, console);
                    var result = controller.Restore(["*"]);
                }

                DestinationsAreEqual(true, [.. restoreFolders.Select(f => System.IO.Path.Combine(f, "data"))]);

            }

            AssertRestoreWorks([TARGETFOLDER, syncDest1, syncDest2, syncDest3]);

            // Add another file and backup
            NewRandomFile(testDataFolder, "file7.txt");
            Backup();
            DestinationsAreEqual(true, TARGETFOLDER, syncDest1);
            Assert.IsFalse(DestinationsAreEqual(false, TARGETFOLDER, syncDest2)); // syncDest2 should be out of date
            Assert.IsFalse(DestinationsAreEqual(false, TARGETFOLDER, syncDest3)); // syncDest3 should be out of date
            AssertRestoreWorks([TARGETFOLDER, syncDest1]);

            // Add another file and backup
            NewRandomFile(sub1, "file8.txt");
            Backup();
            DestinationsAreEqual(true, TARGETFOLDER, syncDest1, syncDest2);
            Assert.IsFalse(DestinationsAreEqual(false, TARGETFOLDER, syncDest3)); // syncDest3 should be out of date
            AssertRestoreWorks([TARGETFOLDER, syncDest1, syncDest2]);

            // Add another file and backup
            NewRandomFile(sub2, "file9.txt");
            Backup();
            DestinationsAreEqual(true, TARGETFOLDER, syncDest1, syncDest3);
            Assert.IsFalse(DestinationsAreEqual(false, TARGETFOLDER, syncDest2)); // syncDest2 should be out of date
            AssertRestoreWorks([TARGETFOLDER, syncDest1, syncDest3]);
        }

    }

    /// <summary>
    /// Simple implementation of BackupResults for testing.
    /// </summary>
    internal class BasicBackupResults : BackupResults
    {
        public BasicBackupResults(ParsedResultType parsedResult)
        {
            _parsedResult = parsedResult;
        }

        private ParsedResultType _parsedResult;

        public override ParsedResultType ParsedResult => _parsedResult;
    }
}
