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

            var destinations = module.GetType().GetField("m_destinations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(module) as List<string>;
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

            var destinations = module.GetType().GetField("m_destinations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(module) as List<string>;
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

            var modes = module.GetType().GetField("m_modes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(module) as List<RemoteSyncTriggerMode>;
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

            var schedules = module.GetType().GetField("m_schedules", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(module) as List<TimeSpan>;
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

            var counts = module.GetType().GetField("m_counts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(module) as List<int>;
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
            string[] localpath = new string[0];
            module.OnStart("Backup", ref remoteurl, ref localpath);

            var source = module.GetType().GetField("m_source", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(module) as string;
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

            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(module, new object[] { 0 });
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

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS \"Operation\" (\"Description\" TEXT, \"Timestamp\" INTEGER)";
            cmd.ExecuteNonQuery();

            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(module, new object[] { 0 });
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
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS \"Operation\" (\"Description\" TEXT, \"Timestamp\" INTEGER)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO \"Operation\" (\"Description\", \"Timestamp\") VALUES ('Backup', @ts)";
            cmd.AddNamedParameter("@ts", Duplicati.Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow));
            cmd.ExecuteNonQuery();
            cmd.ExecuteNonQuery();

            var shouldTrigger = (bool)module.GetType().GetMethod("ShouldTriggerSync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(module, new object[] { 0 });
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
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS \"Operation\" (\"Description\" TEXT, \"Timestamp\" INTEGER)";
            cmd.ExecuteNonQuery();

            module.GetType().GetMethod("RecordSyncOperation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(module, new object[] { 0 });

            cmd.CommandText = "SELECT COUNT(*) FROM \"Operation\" WHERE \"Description\" = 'Rsync 0'";
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
            module.GetType().GetField("m_source", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(module, "file:///source");

            var args = module.GetType().GetMethod("BuildArguments", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(module, new object[] { "file:///dest" }) as string[];

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
            using (var console = new CommandLine.ConsoleOutput(Console.Out, options))
            using (var controller = new Duplicati.Library.Main.Controller("file://" + TARGETFOLDER, options, console))
            {
                var result = controller.Backup(new string[] { DATAFOLDER });

                // Now check if sync was triggered (assuming module was loaded)
                // But since we can't easily get the module instance, check if Operation table has sync entry
                using var db = SQLiteLoader.LoadConnection(DBFILE);
                using var cmd = db.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM \"Operation\" WHERE \"Description\" LIKE 'Rsync %'";
                var count = (long)cmd.ExecuteScalar();
                Assert.AreEqual(1, count);
            }
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
            string[] localpath = new string[0];
            module.OnStart("Backup", ref remoteurl, ref localpath);

            // Create Operation table
            using var db = SQLiteLoader.LoadConnection(DBFILE);
            using var cmd = db.CreateCommand();
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS \"Operation\" (\"Description\" TEXT, \"Timestamp\" INTEGER)";
            cmd.ExecuteNonQuery();

            var result = new TestBasicResults(ParsedResultType.Error);
            module.OnFinish(result, null);

            // Check if sync was not recorded
            cmd.CommandText = "SELECT COUNT(*) FROM \"Operation\" WHERE \"Description\" = 'Rsync 0'";
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
        public IEnumerable<string> Errors => new string[0];
        public IEnumerable<string> Warnings => new string[0];
        public IEnumerable<string> Messages => new string[0];
        public ParsedResultType ParsedResult { get; }
        public bool Interrupted => false;
    }
}