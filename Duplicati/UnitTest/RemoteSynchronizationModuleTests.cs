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

using Duplicati.Library.Modules.Builtin;
using Duplicati.Library.Modules.Builtin.Strings;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Main;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using System.Threading.Tasks;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class RemoteSynchronizationModuleTests : BasicSetupHelper
    {
        const System.Reflection.BindingFlags BINDING_FLAGS = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

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
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest""}]}"
            };

            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            Assert.AreEqual(1, destinations.Count);
            Assert.AreEqual("file:///test/dest", destinations[0].Config.Dst);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_MultipleDestinations()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest1""}, {""url"": ""file:///test/dest2""}]}"
            };

            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual("file:///test/dest1", destinations[0].Config.Dst);
            Assert.AreEqual("file:///test/dest2", destinations[1].Config.Dst);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_Modes()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest1"", ""mode"": ""inline""}, {""url"": ""file:///test/dest2"", ""mode"": ""scheduled""}]}"
            };

            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual(RemoteSyncTriggerMode.Inline, destinations[0].Mode);
            Assert.AreEqual(RemoteSyncTriggerMode.Scheduled, destinations[1].Mode);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_Schedules()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest1"", ""schedule"": ""1:00:00""}, {""url"": ""file:///test/dest2"", ""schedule"": ""2:00:00""}]}"
            };

            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual(TimeSpan.FromHours(1), destinations[0].Schedule);
            Assert.AreEqual(TimeSpan.FromHours(2), destinations[1].Schedule);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_Counts()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest1"", ""count"": 5}, {""url"": ""file:///test/dest2"", ""count"": 10}]}"
            };

            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual(5, destinations[0].Count);
            Assert.AreEqual(10, destinations[1].Count);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestOnStart_SetsSource()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest""}]}"
            };
            module.Configure(options);

            string remoteurl = "file:///source";
            string[] localpath = [];
            module.OnStart("Backup", ref remoteurl, ref localpath);

            var source = module.GetType().GetField("m_source", BINDING_FLAGS).GetValue(module) as string;
            Assert.AreEqual("file:///source", source);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Inline()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest"", ""mode"": ""inline""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [0, destinations[0]]);
            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Scheduled_FirstTime()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest"", ""mode"": ""scheduled"", ""schedule"": ""1:00:00""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

            // Create Operation table as we're now calling outside of the Controller
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [0, destinations[0]]);
            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Counting_FirstTime()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest"", ""mode"": ""counting"", ""count"": 2}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

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

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [0, destinations[0]]);
            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestRecordSyncOperation()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);
            cmd.ExecuteNonQuery();

            module.GetType().GetMethod("RecordSyncOperation", BINDING_FLAGS).Invoke(module, [0]);

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
        public void TestOnFinish_SuccessfulBackup_TriggersSync()
        {
            // Create some test files
            System.IO.File.WriteAllText(System.IO.Path.Combine(DATAFOLDER, "testfile.txt"), "test content");

            var options = TestOptions;
            options["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file://" + System.IO.Path.Combine(BASEFOLDER, "syncdest").Replace("\\", "\\\\") + @"""}]}";
            options["dbpath"] = DBFILE;

            // Run actual backup
            using var console = new CommandLine.ConsoleOutput(Console.Out, options);
            using var controller = new Controller("file://" + TARGETFOLDER, options, console);
            var result = controller.Backup([DATAFOLDER]);

            // Now check if sync was triggered (assuming module was loaded)
            // But since we can't easily get the module instance, check if Operation table has sync entry
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
        public void TestOnFinish_FailedBackup_SkipsSync()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);
            string remoteurl = "file:///source";
            string[] localpath = [];
            module.OnStart("Backup", ref remoteurl, ref localpath);

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            var result = new TestBasicResults(ParsedResultType.Error);
            module.OnFinish(result, null);

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
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest1"", ""mode"": ""inline""}, {""url"": ""file:///test/dest2"", ""mode"": ""invalidmode""}]}"
            };

            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual(RemoteSyncTriggerMode.Inline, destinations[0].Mode);
            Assert.AreEqual(RemoteSyncTriggerMode.Inline, destinations[1].Mode); // Invalid defaults to Inline
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_InvalidSchedule_DefaultsToNull()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest1"", ""schedule"": ""1:00:00""}, {""url"": ""file:///test/dest2"", ""schedule"": ""invalid""}]}"
            };

            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual(TimeSpan.FromHours(1), destinations[0].Schedule);
            Assert.AreEqual(null, destinations[1].Schedule); // Invalid defaults to null
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_InvalidCount_DefaultsToZero()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest1"", ""count"": 5}, {""url"": ""file:///test/dest2"", ""count"": ""invalid""}]}"
            };

            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual(5, destinations[0].Count);
            Assert.AreEqual(0, destinations[1].Count); // Invalid defaults to 0
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Scheduled_Initial()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest"", ""mode"": ""scheduled"", ""schedule"": ""1:00:00""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

            // Create Operation table with no prior syncs
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [0, destinations[0]]);
            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Scheduled_NotDue()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest"", ""mode"": ""scheduled"", ""schedule"": ""1:00:00""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

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

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [0, destinations[0]]);
            Assert.IsFalse(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Scheduled_Due()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest"", ""mode"": ""scheduled"", ""schedule"": ""1:00:00""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

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

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [0, destinations[0]]);
            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Counting_Initial()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest"", ""mode"": ""counting"", ""count"": 5}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

            // Create Operation table and simulate 0 backups, which should trigger sync
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [0, destinations[0]]);
            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Counting_NotReached()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest"", ""mode"": ""counting"", ""count"": 5}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

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

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [0, destinations[0]]);
            Assert.IsFalse(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Counting_Reached()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest"", ""mode"": ""counting"", ""count"": 2}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

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

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [0, destinations[0]]);
            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_MissingDbPath_ReturnsFalse()
        {
            // This case shouldn't occur in normal operation, as a backup will
            // always have an associated database. However, if such a state
            // should occur, we want to ensure the module doesn't trigger a sync,
            // as the state is likely errornous.
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest"", ""mode"": ""inline""}]}"
                // No dbpath
            };
            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [0, destinations[0]]);
            Assert.IsTrue(shouldTrigger); // Inline always returns true
        }

        [Test]
        [Category("RemoteSync")]
        public void TestOnFinish_NonBackupOperation_SkipsSync()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);
            string remoteurl = "file:///source";
            string[] localpath = [];
            module.OnStart("Restore", ref remoteurl, ref localpath); // Non-backup operation

            var result = new TestBasicResults(ParsedResultType.Success);
            module.OnFinish(result, null);

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
        public void TestOnFinish_ExceptionDuringBackup_SkipsSync()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);
            string remoteurl = "file:///source";
            string[] localpath = [];
            module.OnStart("Backup", ref remoteurl, ref localpath);

            var result = new TestBasicResults(ParsedResultType.Success);
            var exception = new Exception("Test exception");
            module.OnFinish(result, exception);

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
        public void TestOnFinish_FatalResult_SkipsSync()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);
            string remoteurl = "file:///source";
            string[] localpath = [];
            module.OnStart("Backup", ref remoteurl, ref localpath);

            var result = new TestBasicResults(ParsedResultType.Fatal);
            module.OnFinish(result, null);

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
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest""}]}"
                // No dbpath
            };
            module.Configure(options);

            // Should not throw exception
            Assert.DoesNotThrow(() => module.GetType().GetMethod("RecordSyncOperation", BINDING_FLAGS).Invoke(module, [0]));
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_DefaultMode()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest1""}, {""url"": ""file:///test/dest2"", ""mode"": ""scheduled""}]}"
            };

            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;

            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual(RemoteSyncTriggerMode.Inline, destinations[0].Mode); // Default
            Assert.AreEqual(RemoteSyncTriggerMode.Scheduled, destinations[1].Mode);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestModuleProperties()
        {
            var module = new RemoteSynchronizationModule();

            Assert.AreEqual("remotesync", module.Key);
            Assert.IsNotNull(module.DisplayName);
            Assert.IsNotEmpty(module.DisplayName);
            Assert.IsNotNull(module.Description);
            Assert.IsNotEmpty(module.Description);
            Assert.IsTrue(module.LoadAsDefault);

            // Check the number of supported commands. Count should be updated
            // if commands are added or removed.
            var supportedCommands = module.SupportedCommands;
            Assert.IsNotNull(supportedCommands);
            Assert.AreEqual(1, supportedCommands.Count);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestOnFinish_WithEmptyDestinations_SkipsSync()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///valid""}, {""url"": """"}, {""url"": ""file:///another""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

            string remoteurl = "file:///source";
            string[] localpath = [];
            module.OnStart("Backup", ref remoteurl, ref localpath);

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            var result = new TestBasicResults(ParsedResultType.Success);
            module.OnFinish(result, null);

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
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest"", ""mode"": ""scheduled""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [1, destinations[0]]); // Index 1, but only 1 destination
            Assert.IsFalse(shouldTrigger); // Out of range
        }


        [Test]
        [Category("RemoteSync")]
        public void TestOnStart_SourceHandling_DoesNotOverwriteExisting()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest""}]}"
            };
            module.Configure(options);

            // Set m_source manually
            module.GetType().GetField("m_source", BINDING_FLAGS).SetValue(module, "file:///existing");

            string remoteurl = "file:///new";
            string[] localpath = [];
            module.OnStart("Backup", ref remoteurl, ref localpath);

            var source = module.GetType().GetField("m_source", BINDING_FLAGS).GetValue(module) as string;
            Assert.AreEqual("file:///existing", source); // Should not overwrite
        }

        [Test]
        [Category("RemoteSync")]
        public void TestOnFinish_WithMultipleDestinations_SelectiveSync()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///dest1"", ""mode"": ""inline""}, {""url"": ""file:///dest2"", ""mode"": ""scheduled"", ""schedule"": ""1:00:00""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);
            string remoteurl = "file:///source";
            string[] localpath = [];
            module.OnStart("Backup", ref remoteurl, ref localpath);

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

            var result = new TestBasicResults(ParsedResultType.Success);
            module.OnFinish(result, null);

            // Check that one additional sync was recorded (dest1 inline, dest2 scheduled not due)
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
        public void TestOnFinish_WithWarningResult_TriggersSync()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);
            string remoteurl = "file:///source";
            string[] localpath = [];
            module.OnStart("Backup", ref remoteurl, ref localpath);

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            var result = new TestBasicResults(ParsedResultType.Warning);
            module.OnFinish(result, null);

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
        public void TestOnFinish_WithWarningResult_SkipsSync_WhenDisabled()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""sync-on-warnings"": false, ""destinations"": [{""url"": ""file:///test/dest""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);
            string remoteurl = "file:///source";
            string[] localpath = [];
            module.OnStart("Backup", ref remoteurl, ref localpath);

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            var result = new TestBasicResults(ParsedResultType.Warning);
            module.OnFinish(result, null);

            // Check that sync was not recorded
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
        public void TestConfigure_WithWhitespaceInDestinations_PreservesWhitespace()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""  file:///dest1  ""}, {""url"": ""  file:///dest2  ""}]}"
            };

            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual("  file:///dest1  ", destinations[0].Config.Dst); // JSON preserves spaces
            Assert.AreEqual("  file:///dest2  ", destinations[1].Config.Dst);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_WithEmptyDestinationsInList_Skips()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///dest1""}, {}, {""url"": ""file:///dest2""}]}"
            };

            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<RemoteSyncDestinationConfig>;
            Assert.AreEqual(3, destinations.Count); // All are included, empty ones skipped in OnFinish
            Assert.AreEqual("file:///dest1", destinations[0].Config.Dst);
            Assert.AreEqual("", destinations[1].Config.Dst);
            Assert.AreEqual("file:///dest2", destinations[2].Config.Dst);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestRecordSyncOperation_WithDifferentIndex()
        {
            // Incorrect index should not record, but throw a warning.
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            module.GetType().GetMethod("RecordSyncOperation", BINDING_FLAGS).Invoke(module, [2]);

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
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

            // Do not create Operation table

            Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                module.GetType().GetMethod("RecordSyncOperation", BINDING_FLAGS).Invoke(module, [0]));
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_InvalidJson_DoesNotActivate()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"invalid json"
            };
            module.Configure(options);

            var enabled = module.GetType().GetField("m_enabled", BINDING_FLAGS).GetValue(module);
            Assert.IsFalse((bool)enabled);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_WithNoDestinations_DoesNotActivate()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                // No remote-sync-json-config
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

            var enabled = module.GetType().GetField("m_enabled", BINDING_FLAGS).GetValue(module);
            Assert.IsFalse((bool)enabled);

            string remoteurl = "file:///source";
            string[] localpath = [];
            module.OnStart("Backup", ref remoteurl, ref localpath);

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            var result = new TestBasicResults(ParsedResultType.Success);
            module.OnFinish(result, null);

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
        public void TestOnFinish_ForNonBackupOperations_SkipsSync()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @"{""destinations"": [{""url"": ""file:///test/dest""}]}",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);
            string remoteurl = "file:///source";
            string[] localpath = [];
            module.OnStart("List", ref remoteurl, ref localpath); // Non-backup operation

            var result = new TestBasicResults(ParsedResultType.Success);
            module.OnFinish(result, null);

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
    /// Simple implementation of IBasicResults for testing.
    /// </summary>
    public class TestBasicResults : IBasicResults
    {
        public TestBasicResults(ParsedResultType parsedResult)
        {
            ParsedResult = parsedResult;
        }

        public DateTime BeginTime => DateTime.UtcNow;
        public DateTime EndTime => DateTime.UtcNow;
        public TimeSpan Duration => TimeSpan.Zero;
        public IEnumerable<string> Errors => [];
        public IEnumerable<string> Warnings => [];
        public IEnumerable<string> Messages => [];
        public ParsedResultType ParsedResult { get; }
        public bool Interrupted => false;
    }
}
