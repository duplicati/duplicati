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
using System.Linq;
using Duplicati.Library.Logging;
using Duplicati.Library.Snapshots;
using Duplicati.Library.Snapshots.Windows;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class TestMSSQLParser
    {
        private const char PS = ';'; // Path separator between filters/sources
        private const char DS = '\\'; // Directory separator
        private const string MSSQL = "%MSSQL%";

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

        private static Dictionary<string, string> RequiredPolicyOptions()
            => new(StringComparer.OrdinalIgnoreCase) { ["snapshot-policy"] = "required" };

        private static Dictionary<string, string> Run(MockMSSQLUtility util, ref string[] paths, ref string filter, Dictionary<string, string> options = null)
        {
            options ??= RequiredPolicyOptions();
            using var _ = Log.StartScope(new LogSink());
            return new Duplicati.Library.Modules.Builtin.MSSQLOptions()
                .RealParseSourcePaths(ref paths, ref filter, options, util);
        }

        [Test]
        public void Returns_empty_change_set_when_MSSQL_not_installed()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            var util = new MockMSSQLUtility { IsMSSQLInstalled = false };
            var paths = new[] { MSSQL };
            var filter = string.Empty;

            var result = Run(util, ref paths, ref filter);

            Assert.That(result, Is.Empty);
            Assert.That(paths, Is.EqualTo(new[] { MSSQL }), "Paths must stay untouched when MSSQL is not installed.");
        }

        [Test]
        public void All_marker_includes_every_database()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            var dbDefault = MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf");
            var dbInstance = MakeDb("SRV1", "INST", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var util = new MockMSSQLUtility { DBs = [dbDefault, dbInstance] };

            var paths = new[] { MSSQL };
            var filter = string.Empty;

            Run(util, ref paths, ref filter);

            Assert.That(paths, Does.Contain(dbDefault.DataPaths[0]));
            Assert.That(paths, Does.Contain(dbInstance.DataPaths[0]));
            Assert.That(paths, Does.Not.Contain(MSSQL), "Virtual marker must be replaced by real paths.");
        }

        [Test]
        public void Server_path_includes_all_databases_on_server()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            var dbDefault = MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf");
            var dbInstance = MakeDb("SRV1", "INST", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var dbOther = MakeDb("SRV2", "", "Other", @$"C:{DS}Data{DS}Other.mdf");
            var util = new MockMSSQLUtility { DBs = [dbDefault, dbInstance, dbOther] };

            var paths = new[] { $"{MSSQL}{DS}SRV1" };
            var filter = string.Empty;

            Run(util, ref paths, ref filter);

            Assert.That(paths, Does.Contain(dbDefault.DataPaths[0]), "default-instance db on server missing");
            Assert.That(paths, Does.Contain(dbInstance.DataPaths[0]), "named-instance db on server missing");
            Assert.That(paths, Does.Not.Contain(dbOther.DataPaths[0]), "db from another server leaked in");
        }

        [Test]
        public void Default_instance_database_path_resolves_single_database()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            // %MSSQL%\SRV1\Sales : 3 segments, default instance => Sales database
            var dbSales = MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf");
            var dbHr = MakeDb("SRV1", "", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var util = new MockMSSQLUtility { DBs = [dbSales, dbHr] };

            var paths = new[] { $"{MSSQL}{DS}SRV1{DS}Sales" };
            var filter = string.Empty;

            Run(util, ref paths, ref filter);

            Assert.That(paths, Does.Contain(dbSales.DataPaths[0]), "selected default-instance db missing");
            Assert.That(paths, Does.Not.Contain(dbHr.DataPaths[0]), "non-selected db leaked in");
        }

        [Test]
        public void Server_instance_path_includes_all_databases_in_instance()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            // %MSSQL%\SRV1\INST : 3 segments, matches a named instance => all its databases
            var dbInst1 = MakeDb("SRV1", "INST", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var dbInst2 = MakeDb("SRV1", "INST", "Payroll", @$"C:{DS}Data{DS}Payroll.mdf");
            var dbDefault = MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf");
            var util = new MockMSSQLUtility { DBs = [dbInst1, dbInst2, dbDefault] };

            var paths = new[] { $"{MSSQL}{DS}SRV1{DS}INST" };
            var filter = string.Empty;

            Run(util, ref paths, ref filter);

            Assert.That(paths, Does.Contain(dbInst1.DataPaths[0]));
            Assert.That(paths, Does.Contain(dbInst2.DataPaths[0]));
            Assert.That(paths, Does.Not.Contain(dbDefault.DataPaths[0]), "default-instance db leaked in");
        }

        [Test]
        public void Fully_qualified_path_resolves_single_database_on_named_instance()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            // %MSSQL%\SRV1\INST\HR : 4 segments
            var dbHr = MakeDb("SRV1", "INST", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var dbPayroll = MakeDb("SRV1", "INST", "Payroll", @$"C:{DS}Data{DS}Payroll.mdf");
            var util = new MockMSSQLUtility { DBs = [dbHr, dbPayroll] };

            var paths = new[] { $"{MSSQL}{DS}SRV1{DS}INST{DS}HR" };
            var filter = string.Empty;

            Run(util, ref paths, ref filter);

            Assert.That(paths, Does.Contain(dbHr.DataPaths[0]));
            Assert.That(paths, Does.Not.Contain(dbPayroll.DataPaths[0]));
        }

        [Test]
        public void Ambiguous_instance_and_default_database_name_throws()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            // A named instance "INST" exists, plus a default-instance database also called "INST".
            // %MSSQL%\SRV1\INST is ambiguous (could mean the instance or the default-instance db),
            // so the parser must reject it rather than silently guessing.
            var dbInst = MakeDb("SRV1", "INST", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var dbDefaultNamedInst = MakeDb("SRV1", "", "INST", @$"C:{DS}Data{DS}DefaultInst.mdf");
            var util = new MockMSSQLUtility { DBs = [dbInst, dbDefaultNamedInst] };

            var paths = new[] { $"{MSSQL}{DS}SRV1{DS}INST" };
            var filter = string.Empty;

            var ex = Assert.Throws<Duplicati.Library.Interface.UserInformationException>(() =>
            {
                var p = paths; var f = filter;
                Run(util, ref p, ref f);
            });
            Assert.That(ex.HelpID, Is.EqualTo("MsSqlServerInstanceAmbiguous"));
        }

        [Test]
        public void Server_instance_match_wins_when_no_competing_default_database()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            // Only the named instance "INST" matches %MSSQL%\SRV1\INST; there is no
            // default-instance database called "INST", so the instance is selected unambiguously.
            var dbInst = MakeDb("SRV1", "INST", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var dbDefault = MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf");
            var util = new MockMSSQLUtility { DBs = [dbInst, dbDefault] };

            var paths = new[] { $"{MSSQL}{DS}SRV1{DS}INST" };
            var filter = string.Empty;

            Run(util, ref paths, ref filter);

            Assert.That(paths, Does.Contain(dbInst.DataPaths[0]), "instance match missing");
            Assert.That(paths, Does.Not.Contain(dbDefault.DataPaths[0]), "unrelated default-instance db leaked in");
        }

        [Test]
        public void Unknown_server_throws()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            var util = new MockMSSQLUtility { DBs = [MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf")] };
            var paths = new[] { $"{MSSQL}{DS}NOPE" };
            var filter = string.Empty;

            var ex = Assert.Throws<Duplicati.Library.Interface.UserInformationException>(() =>
            {
                var p = paths; var f = filter;
                Run(util, ref p, ref f);
            });
            Assert.That(ex.HelpID, Is.EqualTo("MsSqlServerNotFound"));
        }

        [Test]
        public void Unknown_instance_or_database_throws()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            // %MSSQL%\SRV1\NOPE matches neither a named instance nor a default-instance database
            var util = new MockMSSQLUtility { DBs = [MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf")] };
            var paths = new[] { $"{MSSQL}{DS}SRV1{DS}NOPE" };
            var filter = string.Empty;

            var ex = Assert.Throws<Duplicati.Library.Interface.UserInformationException>(() =>
            {
                var p = paths; var f = filter;
                Run(util, ref p, ref f);
            });
            Assert.That(ex.HelpID, Is.EqualTo("MsSqlServerInstanceNotFound"));
        }

        [Test]
        public void Unknown_database_on_named_instance_throws()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            var util = new MockMSSQLUtility { DBs = [MakeDb("SRV1", "INST", "HR", @$"C:{DS}Data{DS}HR.mdf")] };
            var paths = new[] { $"{MSSQL}{DS}SRV1{DS}INST{DS}NOPE" };
            var filter = string.Empty;

            var ex = Assert.Throws<Duplicati.Library.Interface.UserInformationException>(() =>
            {
                var p = paths; var f = filter;
                Run(util, ref p, ref f);
            });
            Assert.That(ex.HelpID, Is.EqualTo("MsSqlDatabaseNotFound"));
        }

        [Test]
        public void Exclude_filter_removes_matching_database()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            var dbSales = MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf");
            var dbHr = MakeDb("SRV1", "", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var util = new MockMSSQLUtility { DBs = [dbSales, dbHr] };

            var paths = new[] { MSSQL };
            // Exclude HR on the default instance (no instance segment in the virtual path)
            var filter = $"-{MSSQL}{DS}SRV1{DS}HR";

            Run(util, ref paths, ref filter);

            Assert.That(paths, Does.Contain(dbSales.DataPaths[0]), "unaffected db missing");
            Assert.That(paths, Does.Not.Contain(dbHr.DataPaths[0]), "excluded db leaked in");
        }

        [Test]
        public void Include_filter_keeps_only_matching_database()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            var dbSales = MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf");
            var dbHr = MakeDb("SRV1", "", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var util = new MockMSSQLUtility { DBs = [dbSales, dbHr] };

            var paths = new[] { MSSQL };
            // Include-only filter: anything not matched is excluded
            var filter = $"+{MSSQL}{DS}SRV1{DS}Sales";

            Run(util, ref paths, ref filter);

            Assert.That(paths, Does.Contain(dbSales.DataPaths[0]), "included db missing");
            Assert.That(paths, Does.Not.Contain(dbHr.DataPaths[0]), "non-included db leaked in");
        }

        [Test]
        public void Exclude_filter_on_named_instance_database()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            var dbHr = MakeDb("SRV1", "INST", "HR", @$"C:{DS}Data{DS}HR.mdf");
            var dbPayroll = MakeDb("SRV1", "INST", "Payroll", @$"C:{DS}Data{DS}Payroll.mdf");
            var util = new MockMSSQLUtility { DBs = [dbHr, dbPayroll] };

            var paths = new[] { $"{MSSQL}{DS}SRV1{DS}INST" };
            var filter = $"-{MSSQL}{DS}SRV1{DS}INST{DS}Payroll";

            Run(util, ref paths, ref filter);

            Assert.That(paths, Does.Contain(dbHr.DataPaths[0]), "unaffected named-instance db missing");
            Assert.That(paths, Does.Not.Contain(dbPayroll.DataPaths[0]), "excluded named-instance db leaked in");
        }

        [Test]
        public void Non_MSSQL_paths_and_filters_pass_through_untouched()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            var dbSales = MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf");
            var util = new MockMSSQLUtility { DBs = [dbSales] };

            var ordinaryPath = @$"C:{DS}Users{DS}data";
            var ordinaryFilter = $"-C:{DS}Users{DS}data{DS}temp";
            var paths = new[] { MSSQL, ordinaryPath };
            var filter = ordinaryFilter;

            Run(util, ref paths, ref filter);

            Assert.That(paths, Does.Contain(ordinaryPath), "ordinary source path must survive");
            Assert.That(paths, Does.Contain(dbSales.DataPaths[0]), "db path must be added");
            Assert.That(filter, Is.EqualTo(ordinaryFilter), "non-MSSQL filter must be left in the filter string");
        }

        [Test]
        public void Handles_null_filter_gracefully()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            var dbSales = MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf");
            var util = new MockMSSQLUtility { DBs = [dbSales] };

            var paths = new[] { MSSQL };
            string filter = null;

            Run(util, ref paths, ref filter);

            Assert.That(paths, Does.Contain(dbSales.DataPaths[0]));
        }

        [Test]
        public void Enforces_required_snapshot_policy()
        {
            if (!OperatingSystem.IsWindows())
                return; // MSSQL is only available on Windows

            var dbSales = MakeDb("SRV1", "", "Sales", @$"C:{DS}Data{DS}Sales.mdf");
            var util = new MockMSSQLUtility { DBs = [dbSales] };

            var paths = new[] { MSSQL };
            var filter = string.Empty;
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var result = Run(util, ref paths, ref filter, options);

            Assert.That(result.ContainsKey("snapshot-policy"), Is.True);
            Assert.That(result["snapshot-policy"], Is.EqualTo("required"));
        }
    }
}
