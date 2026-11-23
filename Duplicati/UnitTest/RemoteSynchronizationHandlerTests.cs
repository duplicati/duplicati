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
using StringAssert = NUnit.Framework.Legacy.StringAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;
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
            Assert.AreEqual($"file://{dest1.Replace("\\\\", "\\")}", destinations[0].Target.Url);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_DestinationToStringHidesCredentialsAndOptions()
        {
            const string password = "s3cret-destination-password";
            const string passphrase = "s3cret-backup-passphrase";
            var options = new Dictionary<string, string>
            {
                ["passphrase"] = passphrase,
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""ftp://user:{password}@example.com/backups?auth-password={password}"", ""dst-options"": [""auth-password={password}""]}}
                ]}}"
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            Assert.AreEqual(1, destinations.Count);

            // The raw config still carries everything it needs to run
            StringAssert.Contains(password, destinations[0].Target.Url);
            Assert.AreEqual(password, destinations[0].Target.Options["auth-password"]);

            // ...but nothing that is rendered into a log message may contain it
            foreach (var rendered in new[] { destinations[0].ToString(), destinations[0].Target.ToString(), destinations[0].DisplayDestination })
            {
                StringAssert.DoesNotContain(password, rendered);
                StringAssert.DoesNotContain(passphrase, rendered);
                StringAssert.DoesNotContain("auth-password", rendered);
            }
            Assert.AreEqual("ftp://example.com/backups", destinations[0].DisplayDestination);
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
            Assert.AreEqual($"file://{dest1.Replace("\\\\", "\\")}", destinations[0].Target.Url);
            Assert.AreEqual($"file://{dest2.Replace("\\\\", "\\")}", destinations[1].Target.Url);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_Modes()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""inline""}},
                    {{""url"": ""file://{dest2}"", ""mode"": ""interval"", ""interval"": ""1h""}}
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

            var remoteurl = $"file://{source}";
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

        /// <summary>
        /// Collects the log entries written while a handler is constructed, so a test can
        /// see the warning the configuration parsing writes.
        /// </summary>
        private sealed class LogSink : Duplicati.Library.Logging.ILogDestination
        {
            public List<Duplicati.Library.Logging.LogEntry> Entries { get; } = [];

            public void WriteMessage(Duplicati.Library.Logging.LogEntry entry)
            {
                Entries.Add(entry);
            }
        }

        /// <summary>
        /// An explicit mode without its parameter cannot be triggered a second time: the check
        /// compares against a null interval or count, which is always false. The parser has to
        /// say so and run the destination inline, the way it already does for an interval it
        /// cannot parse.
        /// </summary>
        [TestCase("interval", "RemoteSyncMissingInterval")]
        [TestCase("counting", "RemoteSyncMissingCount")]
        [Category("RemoteSync")]
        public void TestConfigure_ModeWithoutItsParameter_WarnsAndDefaultsToInline(string mode, string warningId)
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""{mode}""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var sink = new LogSink();
            RemoteSynchronizationHandler handler;
            using (Duplicati.Library.Logging.Log.StartScope(sink, Duplicati.Library.Logging.LogMessageType.Warning))
                handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            Assert.AreEqual(1, destinations.Count);
            Assert.AreEqual(RemoteSyncTriggerMode.Inline, destinations[0].Mode, $"A {mode} destination without its parameter should run inline.");
            Assert.AreEqual(1, sink.Entries.Count(x => x.Id == warningId), $"Expected one {warningId} warning, got: {string.Join(" | ", sink.Entries.Select(x => x.Id + ": " + x.FormattedMessage))}");
        }

        /// <summary>
        /// Without the fallback the destination syncs exactly once: the first check passes
        /// because nothing was recorded yet, and every later one fails against the null
        /// parameter. After one recorded sync the destination must still be triggered.
        /// </summary>
        [TestCase("interval")]
        [TestCase("counting")]
        [Category("RemoteSync")]
        public void TestShouldTriggerSync_ModeWithoutItsParameter_StillTriggersAfterAFirstSync(string mode)
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""{mode}""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            // Create Operation table as we're now calling outside of the Controller, and record one sync
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);
            handler.GetType().GetMethod("RecordSyncOperation", BINDING_FLAGS).Invoke(handler, [0]);

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)handler.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(handler, [0, destinations[0]]);

            Assert.IsTrue(shouldTrigger, $"A {mode} destination without its parameter was not triggered again after its first sync.");
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
        public async Task TestRunAsync_SuccessfulBackup_TriggersSyncAsync()
        {
            // Create some test files
            System.IO.File.WriteAllText(System.IO.Path.Combine(DATAFOLDER, "testfile.txt"), "test content");

            var options = TestOptions;
            options["remote-sync-json-config"] = $@"{{""destinations"": [{{""url"": ""file://{dest1}""}}]}}";
            options["dbpath"] = DBFILE;

            // Run actual backup
            using var console = new CommandLine.ConsoleOutput(Console.Out, options);
            using var controller = new Controller($"file://{TARGETFOLDER}", options, console);
            var result = await controller.BackupAsync([DATAFOLDER]);

            // Now check if sync was triggered by checking if Operation table
            // has sync entry
            using var db = await SQLiteLoader.LoadConnectionAsync(DBFILE);
            using var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" LIKE 'Rsync %'
            ";
            var count = (long)await cmd.ExecuteScalarAsync();

            Assert.AreEqual(1, count);
        }

        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_FailedBackup_SkipsSync_Async()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [{{""url"": ""file://{dest1}""}}]}}",
                ["dbpath"] = DBFILE
            };

            var remoteurl = $"file://{source}";
            var results = new BasicBackupResults(ParsedResultType.Error);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), results);

            // Create Operation table
            using var db = await SQLiteLoader.LoadConnectionAsync(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            await handler.RunAsync();

            // Check if sync was not recorded
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" = 'Rsync 0'
            ";
            var count = (long)await cmd.ExecuteScalarAsync();

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
        public async Task TestRunAsync_NonBackupOperation_SkipsSync_Async()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var remoteurl = $"file://{source}";
            var results = new BasicBackupResults(ParsedResultType.Success);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), results);

            await handler.RunAsync();

            // Check if sync was not recorded
            using var db = await SQLiteLoader.LoadConnectionAsync(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" = 'Rsync 0'
            ";
            var count = (long)await cmd.ExecuteScalarAsync();

            Assert.AreEqual(0, count);
        }

        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_FatalResult_SkipsSync_Async()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}}
                ]}}",
                ["dbpath"] = DBFILE
            };
            var remoteurl = $"file://{source}";
            var results = new BasicBackupResults(ParsedResultType.Fatal);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), results);

            await handler.RunAsync();

            // Check if sync was not recorded
            using var db = await SQLiteLoader.LoadConnectionAsync(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" = 'Rsync 0'
            ";
            var count = (long)await cmd.ExecuteScalarAsync();

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

        /// <summary>
        /// Without a local database there is no record of an earlier synchronization, as for the first one,
        /// so the modes that read the records synchronize instead of failing to open a database
        /// </summary>
        [Test]
        [Category("RemoteSync")]
        [TestCase(@"""mode"": ""interval"", ""interval"": ""1h""")]
        [TestCase(@"""mode"": ""counting"", ""count"": 2")]
        public void TestShouldTriggerSync_MissingDbPath_Triggers(string mode)
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", {mode}}}
                ]}}"
                // No dbpath
            };

            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), new BackupResults());

            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            var shouldTrigger = (bool)handler.GetType().GetMethod("ShouldTriggerSync", BINDING_FLAGS).Invoke(handler, [0, destinations[0]]);

            Assert.IsTrue(shouldTrigger);
        }

        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_DefaultMode()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}},
                    {{""url"": ""file://{dest2}"", ""mode"": ""interval"", ""interval"": ""1h""}}
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
        public async Task TestRunAsync_WithEmptyDestinations_SkipsSync_Async()
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

            var remoteurl = $"file://{source}/";
            var results = new BasicBackupResults(ParsedResultType.Success);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), results);

            // Create Operation table
            using var db = await SQLiteLoader.LoadConnectionAsync(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            // The source of a synchronization is a backend that exists: the inline path reads
            // the backup's own destination
            Directory.CreateDirectory(Path.Combine(TARGETFOLDER, "source"));

            await handler.RunAsync();

            // Check that two syncs were recorded (skipping the empty one)
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description""
                LIKE 'Rsync %'
            ";
            var count = (long)await cmd.ExecuteScalarAsync();

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
        public async Task TestRunAsync_WithMultipleDestinations_SelectiveSync_Async()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""mode"": ""inline""}},
                    {{""url"": ""file://{dest2}"", ""mode"": ""interval"", ""interval"": ""1h""}}
                ]}}",
                ["dbpath"] = DBFILE
            };
            var remoteurl = $"file://{source}";
            var results = new BasicBackupResults(ParsedResultType.Success);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), results);

            // Create Operation table and insert recent sync for dest2
            using var db = await SQLiteLoader.LoadConnectionAsync(DBFILE);
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
            await cmd.ExecuteNonQueryAsync();

            // The source of a synchronization is a backend that exists: the inline path reads
            // the backup's own destination
            Directory.CreateDirectory(Path.Combine(TARGETFOLDER, "source"));

            await handler.RunAsync();

            // Check that one additional sync was recorded (dest1 inline, dest2 interval not due)
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" LIKE 'Rsync %'
            ";
            var count = (long)await cmd.ExecuteScalarAsync();

            Assert.AreEqual(2, count); // Initial 1 + 1 new = 2
        }

        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_WithWarningResult_TriggersSync_Async()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var remoteurl = $"file://{source}";
            var result = new BasicBackupResults(ParsedResultType.Warning);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), result);

            // Create Operation table
            using var db = await SQLiteLoader.LoadConnectionAsync(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            // The source of a synchronization is a backend that exists: the inline path reads
            // the backup's own destination
            Directory.CreateDirectory(Path.Combine(TARGETFOLDER, "source"));

            await handler.RunAsync();

            // Check that sync was recorded
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" = 'Rsync 0'
            ";
            var count = (long)await cmd.ExecuteScalarAsync();

            Assert.AreEqual(1, count);
        }

        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_WithWarningResult_SkipsSync_WhenDisabled_Async()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""sync-on-warnings"": false, ""destinations"": [
                    {{""url"": ""file://{dest1}""}}
                ]}}",
                ["dbpath"] = DBFILE
            };
            var remoteurl = $"file://{source}";
            var result = new BasicBackupResults(ParsedResultType.Warning);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), result);

            // Create Operation table
            using var db = await SQLiteLoader.LoadConnectionAsync(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            await handler.RunAsync();

            // Check that sync was not recorded
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" = 'Rsync 0'
            ";
            var count = (long)await cmd.ExecuteScalarAsync();

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
            Assert.AreEqual($"  file://{dest1.Replace("\\\\", "\\")}  ", destinations[0].Target.Url);
            Assert.AreEqual($"  file://{dest2.Replace("\\\\", "\\")}  ", destinations[1].Target.Url);
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
            Assert.AreEqual($"file://{dest1.Replace("\\\\", "\\")}", destinations[0].Target.Url);
            Assert.AreEqual("", destinations[1].Target.Url);
            Assert.AreEqual($"file://{dest2.Replace("\\\\", "\\")}", destinations[2].Target.Url);
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
        public async Task TestRunAsync_WithNoDestinations_DoesNotActivate_Async()
        {
            var options = new Dictionary<string, string>
            {
                // No remote-sync-json-config
                ["dbpath"] = DBFILE
            };

            var remoteurl = $"file://{source}";
            var results = new BasicBackupResults(ParsedResultType.Success);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), results);

            var enabled = handler.GetType().GetField("m_enabled", BINDING_FLAGS).GetValue(handler);
            Assert.IsFalse((bool)enabled);

            // Create Operation table
            using var db = await SQLiteLoader.LoadConnectionAsync(DBFILE);
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
            var count = (long)await cmd.ExecuteScalarAsync();

            Assert.AreEqual(0, count);
        }


        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_ForNonBackupOperations_SkipsSync_Async()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = $@"{{""destinations"": [
                    {{""url"": ""file://{dest1}""}}
                ]}}",
                ["dbpath"] = DBFILE
            };

            var remoteurl = $"file://{source}";
            var results = new BasicBackupResults(ParsedResultType.Success);
            var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), results);

            await handler.RunAsync();

            // Check if sync was not recorded
            using var db = await SQLiteLoader.LoadConnectionAsync(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM ""Operation""
                WHERE ""Description"" = 'Rsync 0'
            ";
            var count = (long)await cmd.ExecuteScalarAsync();

            Assert.AreEqual(0, count);
        }

        [Test]
        [Category("RemoteSync")]
        public async Task TestMultipleBackups_WithCountingDestinationsAsync()
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
            async Task BackupAsync()
            {
                await Task.Delay(1000); // Ensure timestamp difference between backups
                using var console = new CommandLine.ConsoleOutput(Console.Out, options);
                using var controller = new Controller("file://" + TARGETFOLDER, options, console);
                var result = await controller.BackupAsync([DATAFOLDER]);
                Assert.AreEqual(ParsedResultType.Success, result.ParsedResult);
            }
            await BackupAsync();

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

                for (var i = 0; i < files1.Length; i++)
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
                var matches = true;
                for (var i = 1; i < paths.Length; i++)
                    matches &= ContainsSameFiles(paths[0], paths[i], shouldAssert);
                return matches;
            }

            DestinationsAreEqual(true, TARGETFOLDER, syncDest1, syncDest2, syncDest3);

            // Assert restore from each works
            async Task AssertRestoreWorksAsync(string[] Urls)
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
                    var result = await controller.RestoreAsync(["*"]);
                }

                DestinationsAreEqual(true, [.. restoreFolders.Select(f => System.IO.Path.Combine(f, "data"))]);

            }

            await AssertRestoreWorksAsync([TARGETFOLDER, syncDest1, syncDest2, syncDest3]);

            // Add another file and backup
            NewRandomFile(testDataFolder, "file7.txt");
            await BackupAsync();
            DestinationsAreEqual(true, TARGETFOLDER, syncDest1);
            Assert.IsFalse(DestinationsAreEqual(false, TARGETFOLDER, syncDest2)); // syncDest2 should be out of date
            Assert.IsFalse(DestinationsAreEqual(false, TARGETFOLDER, syncDest3)); // syncDest3 should be out of date
            await AssertRestoreWorksAsync([TARGETFOLDER, syncDest1]);

            // Add another file and backup
            NewRandomFile(sub1, "file8.txt");
            await BackupAsync();
            DestinationsAreEqual(true, TARGETFOLDER, syncDest1, syncDest2);
            Assert.IsFalse(DestinationsAreEqual(false, TARGETFOLDER, syncDest3)); // syncDest3 should be out of date
            await AssertRestoreWorksAsync([TARGETFOLDER, syncDest1, syncDest2]);

            // Add another file and backup
            NewRandomFile(sub2, "file9.txt");
            await BackupAsync();
            DestinationsAreEqual(true, TARGETFOLDER, syncDest1, syncDest3);
            Assert.IsFalse(DestinationsAreEqual(false, TARGETFOLDER, syncDest2)); // syncDest2 should be out of date
            await AssertRestoreWorksAsync([TARGETFOLDER, syncDest1, syncDest3]);
        }

        /// <summary>
        /// The handler records a sync as done when the runner returns 0, and an Interval or Counting
        /// destination is not tried again until its next trigger. A run in which every delete failed
        /// has not synchronized anything, so it must not be recorded as done.
        /// </summary>
        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_FailedDeletes_DoNotRecordASyncAsync()
        {
            // An empty source, so every destination file is due for a delete
            Directory.CreateDirectory(Path.Combine(TARGETFOLDER, "source"));
            await ToolTests.GenerateTestVolumesAsync(Path.Combine(TARGETFOLDER, "dest1"), 3, 1024);

            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            DeterministicErrorBackend.ErrorGenerator = (action, _) => action == DeterministicErrorBackend.BackendAction.DeleteBefore;
            try
            {
                var options = new Dictionary<string, string>
                {
                    ["remote-sync-json-config"] = @$"{{""destinations"": [
                        {{""url"": ""deterror://{dest1}"", ""backend-retries"": 1, ""backend-retry-delay"": 0}}
                    ]}}",
                    ["dbpath"] = DBFILE
                };

                var remoteurl = $"file://{source}";
                var result = new BasicBackupResults(ParsedResultType.Success);
                var handler = new RemoteSynchronizationHandler(remoteurl, new Options(options), result);

                using var db = await SQLiteLoader.LoadConnectionAsync(DBFILE);
                using var cmd = db.CreateCommand();
                EnsureOperationTableCreated(cmd);

                await handler.RunAsync();

                cmd.CommandText = @"
                    SELECT COUNT(*)
                    FROM ""Operation""
                    WHERE ""Description"" LIKE 'Rsync %'
                ";
                var count = (long)await cmd.ExecuteScalarAsync();

                Assert.AreEqual(0, count, "A sync that could not delete anything was recorded as done.");
                Assert.AreEqual(3, Directory.EnumerateFiles(Path.Combine(TARGETFOLDER, "dest1")).Count(), "A file went missing although its delete failed.");
            }
            finally
            {
                DeterministicErrorBackend.ErrorGenerator = null;
            }
        }

        /// <summary>
        /// A destination is written with the options of the backup, so throttling, retries and the
        /// like apply to it as well, but not with what only describes the backup itself: its
        /// database, its passphrase and the synchronization configuration. The options of the
        /// destination come last.
        /// </summary>
        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_DestinationInheritsBackupOptions()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""dst-options"": [""auth-password=other=password"", ""number-of-retries=7""], ""backend-retries"": 2, ""backend-retry-delay"": 1500, ""auto-create-folders"": false}}
                ]}}",
                ["auth-username"] = "someuser",
                ["auth-password"] = "some=password=with=equals",
                ["throttle-upload"] = "1mb",
                ["passphrase"] = "secret",
                ["encryption-module"] = "aes",
                ["dbpath"] = DBFILE
            };

            var parsed = new Options(options);
            var handler = new RemoteSynchronizationHandler($"file://{source}", parsed, new BackupResults());
            var destinations = handler.GetType().GetField("m_destinations", BINDING_FLAGS).GetValue(handler) as List<RemoteSyncDestinationConfig>;
            Assert.AreEqual(1, destinations.Count);

            var targetOptions = RemoteSyncConfig.BuildTargetOptions(parsed.RawOptions, destinations[0].Target);

            Assert.AreEqual("someuser", targetOptions["auth-username"], "An option of the backup was not inherited.");
            Assert.AreEqual("1mb", targetOptions["throttle-upload"], "An option of the backup was not inherited.");
            Assert.AreEqual("other=password", targetOptions["auth-password"], "The option of the destination did not win, or lost what follows its first equals sign.");
            Assert.AreEqual("7", targetOptions["number-of-retries"], "An option given by its real name must win over the legacy key.");
            Assert.AreEqual("2s", targetOptions["retry-delay"], "The retry delay in milliseconds was not rounded up to seconds.");
            Assert.AreEqual("true", targetOptions["disable-autocreate-folder"]);
            foreach (var key in new[] { "passphrase", "encryption-module", "dbpath", "remote-sync-json-config" })
                Assert.IsFalse(targetOptions.ContainsKey(key), $"The destination inherited --{key}.");
        }

        /// <summary>
        /// What the earlier synchronization did and this one does not is reported when it is asked
        /// for, and a key nobody knows is reported as well; neither stops the destination.
        /// </summary>
        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_RemovedAndUnknownKeysWarn()
        {
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""file://{dest1}"", ""retention"": true, ""verify-contents"": true, ""retry"": 3, ""no-such-key"": 1}}
                ]}}"
            };

            var log = new LogCollector();
            RemoteSyncConfig config;
            using (Library.Logging.Log.StartScope(log, Library.Logging.LogMessageType.Information))
                config = RemoteSyncConfig.Parse(new Options(options), $"file://{source}", problemsAreErrors: true);

            Assert.IsNotNull(config);
            Assert.AreEqual(1, config.Destinations.Count);
            Assert.IsTrue(config.Destinations[0].IsUsable);

            var unsupported = log.Entries.Single(x => x.Id == "RemoteSyncUnsupportedOption");
            Assert.AreEqual(Library.Logging.LogMessageType.Warning, unsupported.Level);
            StringAssert.Contains("retention", unsupported.FormattedMessage);
            StringAssert.Contains("verify-contents", unsupported.FormattedMessage);

            var unknown = log.Entries.Single(x => x.Id == "RemoteSyncUnknownOption");
            Assert.AreEqual(Library.Logging.LogMessageType.Warning, unknown.Level);
            StringAssert.Contains("no-such-key", unknown.FormattedMessage);

            Assert.AreEqual(Library.Logging.LogMessageType.Information, log.Entries.Single(x => x.Id == "RemoteSyncIgnoredOption").Level);
        }

        /// <summary>
        /// A destination that is the source itself is never written to, and keeps its place in
        /// the list so the other destinations keep their index.
        /// </summary>
        [Test]
        [Category("RemoteSync")]
        public void TestConfigure_DestinationEqualToSourceIsNotUsable()
        {
            var sourceUrl = $"file://{source.Replace("\\\\", "\\")}";
            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [
                    {{""url"": ""file://{source}""}},
                    {{""url"": ""file://{dest1}"", ""sync-database-path"": ""some.sqlite"", ""dry-run"": true, ""force"": true}}
                ]}}"
            };

            var config = RemoteSyncConfig.Parse(new Options(options), sourceUrl, problemsAreErrors: false);

            Assert.AreEqual(2, config.Destinations.Count);
            Assert.IsFalse(config.Destinations[0].IsUsable);
            Assert.IsTrue(config.Destinations[1].IsUsable);
            Assert.AreEqual("some.sqlite", config.Destinations[1].Target.SyncDatabasePath);
            Assert.IsTrue(config.Destinations[1].Target.DryRun);
            Assert.IsTrue(config.Destinations[1].Target.Force);
        }

        /// <summary>
        /// Backs up twice to a primary with a secondary destination, and returns the result of
        /// the second backup. The folders and the test data are prepared by the caller.
        /// </summary>
        private async Task<IBackupResults> BackupWithDestinationsAsync(string json, Action<Dictionary<string, string>> configure = null)
        {
            var options = TestOptions;
            options["remote-sync-json-config"] = json;
            options["dbpath"] = DBFILE;
            configure?.Invoke(options);

            using var controller = new Controller($"file://{TARGETFOLDER}", options, null);
            return await controller.BackupAsync([DATAFOLDER]);
        }

        private static string[] VolumeNames(string folder)
            => [.. Directory.EnumerateFiles(folder).Select(Path.GetFileName).Where(x => x.StartsWith("duplicati-", StringComparison.Ordinal)).Order()];

        /// <summary>
        /// The destination gets exactly the volumes of the backup: a stray file and a stray folder
        /// in the primary are not copied, and a stray file in the destination is not deleted. The
        /// per-destination result carries the counts, and the uploads of the synchronization are
        /// not counted as uploads of the backup.
        /// </summary>
        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_CopiesOnlyTheVolumesAndReportsPerDestinationAsync()
        {
            var syncDest = Path.Combine(BASEFOLDER, "volumes-dest");
            if (Directory.Exists(syncDest)) Directory.Delete(syncDest, true);
            Directory.CreateDirectory(syncDest);
            File.WriteAllText(Path.Combine(syncDest, "readme.txt"), "not a volume");
            // Another backup shares the folder of the destination
            File.WriteAllText(Path.Combine(syncDest, "other-b0123456789abcdef0123456789abcdef.dblock.zip"), "another backup");

            File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "content a");
            File.WriteAllText(Path.Combine(TARGETFOLDER, "stray.txt"), "not a volume");
            Directory.CreateDirectory(Path.Combine(TARGETFOLDER, "lost+found"));
            File.WriteAllText(Path.Combine(TARGETFOLDER, "lost+found", "duplicati-b0123456789abcdef0123456789abcdef.dblock.zip"), "in a folder");

            var json = System.Text.Json.JsonSerializer.Serialize(new { destinations = new[] { new { url = $"file://{syncDest}" } } });
            var result = await BackupWithDestinationsAsync(json);
            Assert.AreEqual(0, result.Errors.Count(), string.Join(Environment.NewLine, result.Errors));

            var primary = VolumeNames(TARGETFOLDER);
            Assert.IsNotEmpty(primary);
            CollectionAssert.AreEqual(primary, VolumeNames(syncDest));
            foreach (var name in primary)
                CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(TARGETFOLDER, name)), File.ReadAllBytes(Path.Combine(syncDest, name)), name);

            Assert.IsFalse(File.Exists(Path.Combine(syncDest, "stray.txt")), "A file that is not a volume was copied.");
            Assert.IsFalse(Directory.Exists(Path.Combine(syncDest, "lost+found")), "A folder of the primary was created on the destination.");
            Assert.IsTrue(File.Exists(Path.Combine(syncDest, "readme.txt")), "A file that is not a volume was deleted from the destination.");
            Assert.IsTrue(File.Exists(Path.Combine(syncDest, "other-b0123456789abcdef0123456789abcdef.dblock.zip")), "A volume of another backup was deleted from the destination.");

            var syncResults = result.RemoteSynchronizationResults;
            Assert.AreEqual(1, syncResults.Length);
            Assert.AreEqual(primary.Length, syncResults[0].CopiedFileCount);
            Assert.AreEqual(primary.Sum(x => new FileInfo(Path.Combine(TARGETFOLDER, x)).Length), syncResults[0].CopiedFileSize);
            Assert.AreEqual(0, syncResults[0].DeletedFileCount);
            Assert.AreEqual(primary.Length, ((BasicResults)syncResults[0]).BackendStatistics.FilesUploaded, "The destination did not count its own uploads.");
            Assert.AreEqual(primary.Length, result.BackendStatistics.FilesUploaded, "The backup counted the uploads of the synchronization as its own.");

            // A second backup adds volumes and replaces none; nothing is copied twice
            File.WriteAllText(Path.Combine(DATAFOLDER, "b.txt"), "content b");
            result = await BackupWithDestinationsAsync(json);
            Assert.AreEqual(0, result.Errors.Count(), string.Join(Environment.NewLine, result.Errors));
            var after = VolumeNames(TARGETFOLDER);
            CollectionAssert.AreEqual(after, VolumeNames(syncDest));
            Assert.AreEqual(after.Length - primary.Length, result.RemoteSynchronizationResults[0].CopiedFileCount);
        }

        /// <summary>
        /// A volume the primary no longer has is deleted from the destination, a volume that
        /// changed is copied again, and force copies everything.
        /// </summary>
        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_DeletesStaleAndRecopiesChangedVolumesAsync()
        {
            var syncDest = Path.Combine(BASEFOLDER, "stale-dest");
            if (Directory.Exists(syncDest)) Directory.Delete(syncDest, true);
            Directory.CreateDirectory(syncDest);

            File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "content a");
            var json = System.Text.Json.JsonSerializer.Serialize(new { destinations = new[] { new { url = $"file://{syncDest}" } } });
            var result = await BackupWithDestinationsAsync(json);
            Assert.AreEqual(0, result.Errors.Count(), string.Join(Environment.NewLine, result.Errors));

            var volumes = VolumeNames(syncDest);
            var stale = Path.Combine(syncDest, "duplicati-bffffffffffffffffffffffffffffffff.dblock.zip");
            File.WriteAllText(stale, "a volume compact removed from the primary");
            var changed = Path.Combine(syncDest, volumes[0]);
            File.WriteAllText(changed, "x");

            File.WriteAllText(Path.Combine(DATAFOLDER, "b.txt"), "content b");
            result = await BackupWithDestinationsAsync(json);
            Assert.AreEqual(0, result.Errors.Count(), string.Join(Environment.NewLine, result.Errors));

            Assert.IsFalse(File.Exists(stale), "A volume the primary does not have was left in the destination.");
            Assert.AreEqual(1, result.RemoteSynchronizationResults[0].DeletedFileCount);
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(TARGETFOLDER, volumes[0])), File.ReadAllBytes(changed), "A changed volume was not copied again.");
            CollectionAssert.AreEqual(VolumeNames(TARGETFOLDER), VolumeNames(syncDest));

            // Force copies every volume, whatever the destination holds
            var forced = System.Text.Json.JsonSerializer.Serialize(new { destinations = new[] { new { url = $"file://{syncDest}", force = true } } });
            File.WriteAllText(Path.Combine(DATAFOLDER, "c.txt"), "content c");
            result = await BackupWithDestinationsAsync(forced);
            Assert.AreEqual(0, result.Errors.Count(), string.Join(Environment.NewLine, result.Errors));
            Assert.AreEqual(VolumeNames(TARGETFOLDER).Length, result.RemoteSynchronizationResults[0].CopiedFileCount);
        }

        /// <summary>
        /// A dry-run destination is left untouched, and is still recorded as synchronized; a
        /// destination that fails records nothing, reports an error, and does not stop the other.
        /// </summary>
        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_DryRunRecordsAndFailedDestinationDoesNotAsync()
        {
            var dryDest = Path.Combine(BASEFOLDER, "dry-dest");
            var goodDest = Path.Combine(BASEFOLDER, "good-dest");
            var failDest = Path.Combine(BASEFOLDER, "fail-dest");
            foreach (var folder in new[] { dryDest, goodDest, failDest })
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "content a");

            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            // Only the failing destination is read through the error backend
            DeterministicErrorBackend.ErrorGenerator = (action, _) => action == DeterministicErrorBackend.BackendAction.PutBefore;
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    destinations = new object[]
                    {
                        new Dictionary<string, object> { ["url"] = $"file://{dryDest}", ["dry-run"] = true },
                        new Dictionary<string, object> { ["url"] = $"deterror://{failDest}", ["backend-retries"] = 0, ["backend-retry-delay"] = 0 },
                        new Dictionary<string, object> { ["url"] = $"file://{goodDest}" },
                    }
                });

                var result = await BackupWithDestinationsAsync(json);

                Assert.IsEmpty(VolumeNames(dryDest), "A dry run wrote to the destination.");
                Assert.IsEmpty(VolumeNames(failDest));
                CollectionAssert.AreEqual(VolumeNames(TARGETFOLDER), VolumeNames(goodDest), "The destination after the failing one was not synchronized.");
                Assert.IsTrue(result.Errors.Any(x => x.Contains("RemoteSyncFailed")), "The failed destination was not reported as an error.");

                using var db = await SQLiteLoader.LoadConnectionAsync(DBFILE);
                using var cmd = db.CreateCommand();
                cmd.CommandText = @"SELECT ""Description"" FROM ""Operation"" WHERE ""Description"" LIKE 'Rsync %' ORDER BY ""Description""";
                var rows = new List<string>();
                using (var rd = await cmd.ExecuteReaderAsync())
                    while (await rd.ReadAsync())
                        rows.Add(rd.GetString(0));

                CollectionAssert.AreEqual(new[] { "Rsync 0", "Rsync 2" }, rows);
                Assert.AreEqual(3, result.RemoteSynchronizationResults.Length);
            }
            finally
            {
                DeterministicErrorBackend.ErrorGenerator = null;
            }
        }

        /// <summary>
        /// A stop request ends the synchronization: nothing is recorded, so the destination is
        /// tried again after the next backup.
        /// </summary>
        [Test]
        [Category("RemoteSync")]
        public async Task TestRunAsync_StopEndsTheSynchronizationAsync()
        {
            Directory.CreateDirectory(Path.Combine(TARGETFOLDER, "source"));
            await ToolTests.GenerateTestVolumesAsync(Path.Combine(TARGETFOLDER, "source"), 3, 1024);

            var options = new Dictionary<string, string>
            {
                ["remote-sync-json-config"] = @$"{{""destinations"": [{{""url"": ""file://{dest1}""}}]}}",
                ["dbpath"] = DBFILE
            };

            var result = new BasicBackupResults(ParsedResultType.Success);
            var handler = new RemoteSynchronizationHandler($"file://{source}", new Options(options), result);

            using var db = await SQLiteLoader.LoadConnectionAsync(DBFILE);
            using var cmd = db.CreateCommand();
            EnsureOperationTableCreated(cmd);

            result.TaskControl.Stop();
            await handler.RunAsync();

            cmd.CommandText = @"SELECT COUNT(*) FROM ""Operation"" WHERE ""Description"" LIKE 'Rsync %'";
            Assert.AreEqual(0, (long)await cmd.ExecuteScalarAsync());
            Assert.IsFalse(Directory.Exists(Path.Combine(TARGETFOLDER, "dest1")) && Directory.EnumerateFiles(Path.Combine(TARGETFOLDER, "dest1")).Any(), "Volumes were copied after the stop request.");
        }

    }

    /// <summary>
    /// Collects the log messages written while it is the log scope
    /// </summary>
    internal sealed class LogCollector : Library.Logging.ILogDestination
    {
        public List<Library.Logging.LogEntry> Entries { get; } = [];

        public void WriteMessage(Library.Logging.LogEntry entry)
        {
            lock (Entries)
                Entries.Add(entry);
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
