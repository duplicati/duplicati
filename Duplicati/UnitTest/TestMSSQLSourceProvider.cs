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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Duplicati.Library.Logging;
using Duplicati.Library.Snapshots;
using Duplicati.Library.Snapshots.Windows;
using Duplicati.Library.SourceProvider.Builtin.MSSQL;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class TestMSSQLSourceProvider
    {
        private const char DS = '\\'; // Directory separator

        /// <summary>
        /// Mock that returns a pre-populated set of databases without touching VSS.
        /// </summary>
        private sealed class MockMSSQLUtility : IMSSQLUtility
        {
            public Guid MSSQLWriterGuid { get; set; } = Guid.Parse("a65faa63-5ea8-4ebc-9dbd-a0c4db26912a");
            public bool IsMSSQLInstalled { get; set; } = true;
            public List<MSSQLDB> DBs { get; set; } = new();

            public void QueryDBsInfo(WindowsSnapshotProvider provider)
            {
                // No-op; DBs are injected directly by the test
            }
        }

        private sealed class LogSink : ILogDestination
        {
            public List<LogEntry> Entries { get; } = [];
            public void WriteMessage(LogEntry entry) => Entries.Add(entry);
        }

        private static MSSQLDB MakeDb(string server, string instance, string database, params string[] paths)
            => new MSSQLDB
            {
                Server = server,
                InstanceId = instance,
                Database = database,
                DataPaths = paths.ToList()
            };

        [SupportedOSPlatform("windows")]
        private static void RunPrepareOptions(Dictionary<string, string?> options, MockMSSQLUtility utility)
        {
            using var _ = Log.StartScope(new LogSink());
            new MSSQLSourceProvider().PrepareOptions(options, utility);
        }

        [SupportedOSPlatform("windows")]
        private static List<MSSQLDB> RunSelectDatabases(MockMSSQLUtility utility, params string[] sources)
        {
            using var _ = Log.StartScope(new LogSink());
            return MSSQLSourceProvider.SelectDatabases(utility, sources);
        }

        #region MatchesSource

        [Test]
        public void MatchesSource_recognizes_mssql_paths()
        {
            var provider = new MSSQLSourceProvider();

            Assert.Multiple(() =>
            {
                Assert.That(provider.MatchesSource("%MSSQL%"), Is.True);
                Assert.That(provider.MatchesSource($"%MSSQL%{DS}SERVER"), Is.True);
                Assert.That(provider.MatchesSource($"%MSSQL%{DS}SERVER{DS}INSTANCE"), Is.True);
                Assert.That(provider.MatchesSource($"%MSSQL%{DS}SERVER{DS}INSTANCE{DS}DATABASE"), Is.True);
                Assert.That(provider.MatchesSource(@$"C:{DS}data"), Is.False);
                Assert.That(provider.MatchesSource("%HYPERV%"), Is.False);
                Assert.That(provider.MatchesSource(""), Is.False);
            });
        }

        #endregion

        #region PrepareOptions

        [Test]
        public void PrepareOptions_does_nothing_when_not_installed()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var utility = new MockMSSQLUtility { IsMSSQLInstalled = false };

            RunPrepareOptions(options, utility);

            Assert.That(options, Is.Empty, "No options should be changed when MSSQL is not installed");
        }

        [Test]
        public void PrepareOptions_forces_snapshot_policy_required()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var utility = new MockMSSQLUtility();

            RunPrepareOptions(options, utility);

            Assert.That(options["snapshot-policy"], Is.EqualTo("required"));
        }

        [Test]
        public void PrepareOptions_removes_mssql_writer_from_vss_exclude_list()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            var mssqlGuid = Guid.Parse("a65faa63-5ea8-4ebc-9dbd-a0c4db26912a");
            var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["vss-exclude-writers"] = $"{mssqlGuid};bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                ["snapshot-policy"] = "required"
            };

            var utility = new MockMSSQLUtility { MSSQLWriterGuid = mssqlGuid };

            RunPrepareOptions(options, utility);

            Assert.Multiple(() =>
            {
                Assert.That(options["vss-exclude-writers"], Does.Not.Contain(mssqlGuid.ToString()),
                    "The MSSQL writer GUID must have been stripped");
                Assert.That(options["vss-exclude-writers"], Does.Contain("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    "Other writer GUIDs must be kept");
            });
        }

        [Test]
        public void PrepareOptions_switches_snapshot_provider_from_wmi_to_native()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["snapshot-policy"] = "required",
                ["snapshot-provider"] = "wmi"
            };

            var utility = new MockMSSQLUtility();

            RunPrepareOptions(options, utility);

            Assert.That(options["snapshot-provider"], Is.EqualTo("Native"),
                "The Wmi snapshot provider must be replaced with Native");
        }

        #endregion

        #region SelectDatabases

#pragma warning disable CA1416 // Windows-only API calls are guarded by OperatingSystem.IsWindows() checks

        [Test]
        public void SelectDatabases_all_marker_includes_every_database()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var dbDefault = MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf");
            var dbInstance = MakeDb("SRV1", "INST", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var util = new MockMSSQLUtility { DBs = [dbDefault, dbInstance] };

            var result = RunSelectDatabases(util, "%MSSQL%");

            Assert.Multiple(() =>
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(result.Select(x => x.Database), Does.Contain("Sales"));
                Assert.That(result.Select(x => x.Database), Does.Contain("HR"));
            });
        }

        [Test]
        public void SelectDatabases_server_path_includes_all_databases_on_server()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var dbDefault = MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf");
            var dbInstance = MakeDb("SRV1", "INST", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var dbOther = MakeDb("SRV2", "", "Other", @$"C:{DS}Data{DS}Other.mdf");
            var util = new MockMSSQLUtility { DBs = [dbDefault, dbInstance, dbOther] };

            var result = RunSelectDatabases(util, $"%MSSQL%{DS}SRV1");

            Assert.Multiple(() =>
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(result.Select(x => x.Database), Does.Contain("Sales"), "default-instance db on server missing");
                Assert.That(result.Select(x => x.Database), Does.Contain("HR"), "named-instance db on server missing");
            });
        }

        [Test]
        public void SelectDatabases_default_instance_database_resolves_single_database()
        {
            if (!OperatingSystem.IsWindows())
                return;

            // %MSSQL%\SRV1\Sales : 3 segments, default instance => Sales database
            var dbSales = MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf");
            var dbHr = MakeDb("SRV1", "", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var util = new MockMSSQLUtility { DBs = [dbSales, dbHr] };

            var result = RunSelectDatabases(util, $"%MSSQL%{DS}SRV1{DS}Sales");

            Assert.Multiple(() =>
            {
                Assert.That(result, Has.Count.EqualTo(1));
                Assert.That(result[0].Database, Is.EqualTo("Sales"));
            });
        }

        [Test]
        public void SelectDatabases_instance_path_includes_all_databases_in_instance()
        {
            if (!OperatingSystem.IsWindows())
                return;

            // %MSSQL%\SRV1\INST : 3 segments, matches a named instance => all its databases
            var dbInst1 = MakeDb("SRV1", "INST", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var dbInst2 = MakeDb("SRV1", "INST", "Payroll", @$"C:{DS}Data{DS}Payroll.mdf");
            var dbDefault = MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf");
            var util = new MockMSSQLUtility { DBs = [dbInst1, dbInst2, dbDefault] };

            var result = RunSelectDatabases(util, $"%MSSQL%{DS}SRV1{DS}INST");

            Assert.Multiple(() =>
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(result.Select(x => x.Database), Does.Contain("HR"));
                Assert.That(result.Select(x => x.Database), Does.Contain("Payroll"));
            });
        }

        [Test]
        public void SelectDatabases_fully_qualified_path_resolves_single_database()
        {
            if (!OperatingSystem.IsWindows())
                return;

            // %MSSQL%\SRV1\INST\HR : 4 segments
            var dbHr = MakeDb("SRV1", "INST", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var dbPayroll = MakeDb("SRV1", "INST", "Payroll", @$"C:{DS}Data{DS}Payroll.mdf");
            var util = new MockMSSQLUtility { DBs = [dbHr, dbPayroll] };

            var result = RunSelectDatabases(util, $"%MSSQL%{DS}SRV1{DS}INST{DS}HR");

            Assert.Multiple(() =>
            {
                Assert.That(result, Has.Count.EqualTo(1));
                Assert.That(result[0].Database, Is.EqualTo("HR"));
            });
        }

        [Test]
        public void SelectDatabases_ambiguous_instance_and_default_database_throws()
        {
            if (!OperatingSystem.IsWindows())
                return;

            // A named instance "INST" exists, plus a default-instance database also called "INST".
            // %MSSQL%\SRV1\INST is ambiguous and must be rejected.
            var dbInst = MakeDb("SRV1", "INST", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var dbDefaultNamedInst = MakeDb("SRV1", "", "INST", @$"C:{DS}Data{DS}DefaultInst.mdf");
            var util = new MockMSSQLUtility { DBs = [dbInst, dbDefaultNamedInst] };

            var ex = Assert.Throws<Library.Interface.UserInformationException>(
                () => RunSelectDatabases(util, $"%MSSQL%{DS}SRV1{DS}INST"));
            Assert.That(ex!.HelpID, Is.EqualTo("MsSqlServerInstanceAmbiguous"));
        }

        [Test]
        public void SelectDatabases_instance_match_wins_when_no_competing_default_database()
        {
            if (!OperatingSystem.IsWindows())
                return;

            // Only the named instance "INST" matches %MSSQL%\SRV1\INST
            var dbInst = MakeDb("SRV1", "INST", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var dbDefault = MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf");
            var util = new MockMSSQLUtility { DBs = [dbInst, dbDefault] };

            var result = RunSelectDatabases(util, $"%MSSQL%{DS}SRV1{DS}INST");

            Assert.Multiple(() =>
            {
                Assert.That(result, Has.Count.EqualTo(1));
                Assert.That(result[0].Database, Is.EqualTo("HR"), "instance match missing");
            });
        }

        [Test]
        public void SelectDatabases_unknown_server_throws()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var util = new MockMSSQLUtility { DBs = [MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf")] };

            var ex = Assert.Throws<Library.Interface.UserInformationException>(
                () => RunSelectDatabases(util, $"%MSSQL%{DS}NOPE"));
            Assert.That(ex!.HelpID, Is.EqualTo("MsSqlServerNotFound"));
        }

        [Test]
        public void SelectDatabases_unknown_instance_or_database_throws()
        {
            if (!OperatingSystem.IsWindows())
                return;

            // %MSSQL%\SRV1\NOPE matches neither a named instance nor a default-instance database
            var util = new MockMSSQLUtility { DBs = [MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf")] };

            var ex = Assert.Throws<Library.Interface.UserInformationException>(
                () => RunSelectDatabases(util, $"%MSSQL%{DS}SRV1{DS}NOPE"));
            Assert.That(ex!.HelpID, Is.EqualTo("MsSqlServerInstanceNotFound"));
        }

        [Test]
        public void SelectDatabases_unknown_database_on_named_instance_throws()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var util = new MockMSSQLUtility { DBs = [MakeDb("SRV1", "INST", "HR", @$"C:{DS}Data{DS}HR.mdf")] };

            var ex = Assert.Throws<Library.Interface.UserInformationException>(
                () => RunSelectDatabases(util, $"%MSSQL%{DS}SRV1{DS}INST{DS}NOPE"));
            Assert.That(ex!.HelpID, Is.EqualTo("MsSqlDatabaseNotFound"));
        }

        [Test]
        public void SelectDatabases_multiple_sources_merge_without_duplicates()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var dbSales = MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf");
            var dbHr = MakeDb("SRV1", "", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var util = new MockMSSQLUtility { DBs = [dbSales, dbHr] };

            // Both a server-level and a database-level source that overlap
            var result = RunSelectDatabases(util, $"%MSSQL%{DS}SRV1", $"%MSSQL%{DS}SRV1{DS}Sales");

            Assert.Multiple(() =>
            {
                Assert.That(result, Has.Count.EqualTo(2), "Should not have duplicates");
                Assert.That(result.Select(x => x.Database), Does.Contain("Sales"));
                Assert.That(result.Select(x => x.Database), Does.Contain("HR"));
            });
        }

#pragma warning restore CA1416

        #endregion
    }
}
