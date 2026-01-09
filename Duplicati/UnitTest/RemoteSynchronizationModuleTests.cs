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
                ["remote-sync-dst"] = "file:///test/dest"
            };

            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<string>;
            Assert.AreEqual(1, destinations.Count);
            Assert.AreEqual("file:///test/dest", destinations[0]);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_MultipleDestinations()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest1,file:///test/dest2"
            };

            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<string>;
            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual("file:///test/dest1", destinations[0]);
            Assert.AreEqual("file:///test/dest2", destinations[1]);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_Modes()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest1,file:///test/dest2",
                ["remote-sync-mode"] = "inline,scheduled"
            };

            module.Configure(options);

            var modes = module.GetType().GetField("m_modes", BINDING_FLAGS).GetValue(module) as List<RemoteSyncTriggerMode>;
            Assert.AreEqual(2, modes.Count);
            Assert.AreEqual(RemoteSyncTriggerMode.Inline, modes[0]);
            Assert.AreEqual(RemoteSyncTriggerMode.Scheduled, modes[1]);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_Schedules()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest1,file:///test/dest2",
                ["remote-sync-schedule"] = "1:00:00,2:00:00"
            };

            module.Configure(options);

            var schedules = module.GetType().GetField("m_schedules", BINDING_FLAGS).GetValue(module) as List<TimeSpan>;
            Assert.AreEqual(2, schedules.Count);
            Assert.AreEqual(TimeSpan.FromHours(1), schedules[0]);
            Assert.AreEqual(TimeSpan.FromHours(2), schedules[1]);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_Counts()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest1,file:///test/dest2",
                ["remote-sync-count"] = "5,10"
            };

            module.Configure(options);

            var counts = module.GetType().GetField("m_counts", BINDING_FLAGS).GetValue(module) as List<int>;
            Assert.AreEqual(2, counts.Count);
            Assert.AreEqual(5, counts[0]);
            Assert.AreEqual(10, counts[1]);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestOnStart_SetsSource()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest"
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
                ["remote-sync-dst"] = "file:///test/dest",
                ["remote-sync-mode"] = "inline",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [0]);
            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Scheduled_FirstTime()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest",
                ["remote-sync-mode"] = "scheduled",
                ["remote-sync-schedule"] = "1:00:00",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

            // Create Operation table as we're now calling outside of the Controller
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [0]);
            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Counting_FirstTime()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest",
                ["remote-sync-mode"] = "counting",
                ["remote-sync-count"] = "2",
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

            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [0]);
            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestRecordSyncOperation()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest",
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
        public void TestBuildArguments()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest",
                ["remote-sync-force"] = "true",
                ["remote-sync-retention"] = "true"
            };
            module.Configure(options);

            // Set m_source
            module.GetType().GetField("m_source", BINDING_FLAGS).SetValue(module, "file:///source");

            var args = module.GetType().GetMethod("BuildArguments", BINDING_FLAGS).Invoke(module, ["file:///dest"]) as string[];

            Assert.IsTrue(args.Contains("file:///source"));
            Assert.IsTrue(args.Contains("file:///dest"));
            Assert.IsTrue(args.Contains("--force"));
            Assert.IsTrue(args.Contains("--retention"));
            Assert.IsTrue(args.Contains("--auto-create-folders"));
        }

        [Test]
        [Category("RemoteSync")]
        public void TestOnFinish_SuccessfulBackup_TriggersSync()
        {
            // Create some test files
            System.IO.File.WriteAllText(System.IO.Path.Combine(DATAFOLDER, "testfile.txt"), "test content");

            var options = TestOptions;
            options["remote-sync-dst"] = "file://" + System.IO.Path.Combine(BASEFOLDER, "syncdest");
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
                ["remote-sync-dst"] = "file:///test/dest",
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
                ["remote-sync-dst"] = "file:///test/dest1,file:///test/dest2",
                ["remote-sync-mode"] = "inline,invalidmode"
            };

            module.Configure(options);

            var modes = module.GetType().GetField("m_modes", BINDING_FLAGS).GetValue(module) as List<RemoteSyncTriggerMode>;
            Assert.AreEqual(2, modes.Count);
            Assert.AreEqual(RemoteSyncTriggerMode.Inline, modes[0]);
            Assert.AreEqual(RemoteSyncTriggerMode.Inline, modes[1]); // Invalid defaults to Inline
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_InvalidSchedule_DefaultsToZero()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest1,file:///test/dest2",
                ["remote-sync-schedule"] = "1:00:00,invalid"
            };

            module.Configure(options);

            var schedules = module.GetType().GetField("m_schedules", BINDING_FLAGS).GetValue(module) as List<TimeSpan>;
            Assert.AreEqual(2, schedules.Count);
            Assert.AreEqual(TimeSpan.FromHours(1), schedules[0]);
            Assert.AreEqual(TimeSpan.Zero, schedules[1]); // Invalid defaults to Zero
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_InvalidCount_DefaultsToZero()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest1,file:///test/dest2",
                ["remote-sync-count"] = "5,invalid"
            };

            module.Configure(options);

            var counts = module.GetType().GetField("m_counts", BINDING_FLAGS).GetValue(module) as List<int>;
            Assert.AreEqual(2, counts.Count);
            Assert.AreEqual(5, counts[0]);
            Assert.AreEqual(0, counts[1]); // Invalid defaults to 0
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Scheduled_NotDue()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest",
                ["remote-sync-mode"] = "scheduled",
                ["remote-sync-schedule"] = "1:00:00",
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

            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [0]);
            Assert.IsFalse(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_Counting_NotReached()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest",
                ["remote-sync-mode"] = "counting",
                ["remote-sync-count"] = "5",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

            // Create Operation table and simulate 2 backups (less than 5)
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

            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [0]);
            Assert.IsFalse(shouldTrigger);
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
                ["remote-sync-dst"] = "file:///test/dest",
                ["remote-sync-mode"] = "inline"
                // No dbpath
            };
            module.Configure(options);

            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [0]);
            Assert.IsFalse(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestOnFinish_NonBackupOperation_SkipsSync()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest",
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
                ["remote-sync-dst"] = "file:///test/dest",
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
                ["remote-sync-dst"] = "file:///test/dest",
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
        public void TestBuildArguments_AllOptions()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest",
                ["remote-sync-force"] = "true",
                ["remote-sync-retention"] = "true",
                ["remote-sync-retry"] = "5",
                ["remote-sync-backend-retries"] = "10"
            };
            module.Configure(options);

            // Set m_source
            module.GetType().GetField("m_source", BINDING_FLAGS).SetValue(module, "file:///source");

            var args = module.GetType().GetMethod("BuildArguments", BINDING_FLAGS).Invoke(module, ["file:///dest"]) as string[];

            Assert.IsTrue(args.Contains("file:///source"));
            Assert.IsTrue(args.Contains("file:///dest"));
            Assert.IsTrue(args.Contains("--force"));
            Assert.IsTrue(args.Contains("--retention"));
            Assert.IsTrue(args.Contains("--retry"));
            Assert.IsTrue(args.Contains("5"));
            Assert.IsTrue(args.Contains("--backend-retries"));
            Assert.IsTrue(args.Contains("10"));
            Assert.IsTrue(args.Contains("--auto-create-folders"));
            Assert.IsTrue(args.Contains("--backend-retry-delay"));
            Assert.IsTrue(args.Contains("1000"));
        }

        [Test]
        [Category("RemoteSync")]
        public void TestRecordSyncOperation_MissingDbPath_NoCrash()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest"
                // No dbpath
            };
            module.Configure(options);

            // Should not throw exception
            Assert.DoesNotThrow(() => module.GetType().GetMethod("RecordSyncOperation", BINDING_FLAGS).Invoke(module, [0]));
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_MismatchedParameterCounts_UsesDefaults()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest1,file:///test/dest2,file:///test/dest3",
                ["remote-sync-mode"] = "inline,scheduled" // Only 2 modes for 3 destinations
            };

            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<string>;
            var modes = module.GetType().GetField("m_modes", BINDING_FLAGS).GetValue(module) as List<RemoteSyncTriggerMode>;

            Assert.AreEqual(3, destinations.Count);
            Assert.AreEqual(3, modes.Count); // Modes list has the provided values, defaults used for missing indices
            Assert.AreEqual(RemoteSyncTriggerMode.Inline, modes[0]);
            Assert.AreEqual(RemoteSyncTriggerMode.Scheduled, modes[1]);
            Assert.AreEqual(RemoteSyncTriggerMode.Inline, modes[2]);
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
            Assert.AreEqual(8, supportedCommands.Count);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestOnFinish_WithEmptyDestinations_SkipsSync()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

            // Manually set destinations including empty and enable
            var destinationsField = module.GetType().GetField("m_destinations", BINDING_FLAGS);
            destinationsField.SetValue(module, new List<string> { "file:///valid", "", "file:///another" });
            var enabledField = module.GetType().GetField("m_enabled", BINDING_FLAGS);
            enabledField.SetValue(module, true);

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
        public void TestShouldTriggerSync_OutOfRangeIndex_UsesInline()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest",
                ["remote-sync-mode"] = "scheduled", // Only one mode
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(module, [1]); // Index 1, but only 1 destination
            Assert.IsTrue(shouldTrigger); // Uses default Inline, which is true
        }

        [Test]
        [Category("RemoteSync")]
        public void TestAddOption_WithWhitespace_ReturnsDefault()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-retry"] = "   " // Whitespace value
            };
            module.Configure(options);

            var addOptionMethod = module.GetType().GetMethod("AddOption", BINDING_FLAGS);
            var result = (string[])addOptionMethod.Invoke(module, ["remote-sync-retry", "--retry", Array.Empty<string>()]);

            Assert.AreEqual(0, result.Length); // Should return default empty array
        }

        [Test]
        [Category("RemoteSync")]
        public void TestOnStart_SourceHandling_DoesNotOverwriteExisting()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest"
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
                ["remote-sync-dst"] = "file:///dest1,file:///dest2",
                ["remote-sync-mode"] = "inline,scheduled",
                ["remote-sync-schedule"] = "1:00:00,2:00:00",
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
                ["remote-sync-dst"] = "file:///test/dest",
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
        public void TestConfigure_WithWhitespaceInDestinations_Trims()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "  file:///dest1  ,  file:///dest2  "
            };

            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<string>;
            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual("file:///dest1", destinations[0]);
            Assert.AreEqual("file:///dest2", destinations[1]);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_WithEmptyDestinationsInList_Skips()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///dest1,,file:///dest2"
            };

            module.Configure(options);

            var destinations = module.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(module) as List<string>;
            Assert.AreEqual(2, destinations.Count);
            Assert.AreEqual("file:///dest1", destinations[0]);
            Assert.AreEqual("file:///dest2", destinations[1]);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestRecordSyncOperation_WithDifferentIndex()
        {
            // Incorrect index should not record, but throw a warning.
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest",
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
                ["remote-sync-dst"] = "file:///test/dest",
                ["dbpath"] = DBFILE
            };
            module.Configure(options);

            // Do not create Operation table

            Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                module.GetType().GetMethod("RecordSyncOperation", BINDING_FLAGS).Invoke(module, [0]));
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_WithNoDestinations_DoesNotActivate()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                // No remote-sync-dst
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
        public void TestBuildArguments_WithNoOptions()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest"
                // No additional options
            };
            module.Configure(options);

            // Set m_source
            module.GetType().GetField("m_source", BINDING_FLAGS).SetValue(module, "file:///source");

            var args = module.GetType().GetMethod("BuildArguments", BINDING_FLAGS).Invoke(module, ["file:///dest"]) as string[];

            Assert.IsTrue(args.Contains("file:///source"));
            Assert.IsTrue(args.Contains("file:///dest"));
            Assert.IsTrue(args.Contains("--auto-create-folders"));
            Assert.IsTrue(args.Contains("--backend-retry-delay"));
            Assert.IsTrue(args.Contains("1000"));
            Assert.IsTrue(args.Contains("--backend-retry-with-exponential-backoff"));
            Assert.IsTrue(args.Contains("--confirm"));
            // No other options
        }

        [Test]
        [Category("RemoteSync")]
        public void TestOnFinish_ForNonBackupOperations_SkipsSync()
        {
            var module = new RemoteSynchronizationModule();
            var options = new Dictionary<string, string>
            {
                ["remote-sync-dst"] = "file:///test/dest",
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
