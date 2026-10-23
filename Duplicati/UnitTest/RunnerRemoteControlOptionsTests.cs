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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Interface;
using Duplicati.Library.RestAPI.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests that the options injected by the remote controller (the additional report url
/// and the restore test interval) are applied to the backup options by the runner.
/// </summary>
[TestFixture]
[Category("RemoteControl")]
public class RunnerRemoteControlOptionsTests
{
    private string _tempDataFolder = null!;
    private Connection _connection = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-runner-rc-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDataFolder);

        var databasePath = Path.Combine(_tempDataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);
        var dbConnection = await SQLiteLoader.LoadConnectionAsync(databasePath);
        DatabaseUpgrader.UpgradeDatabase(dbConnection, databasePath, typeof(DatabaseSchemaMarker));
        _connection = new Connection(dbConnection, true, null, _tempDataFolder, () => { });
    }

    [TearDown]
    public void TearDown()
    {
        _connection?.Dispose();
        try
        {
            if (Directory.Exists(_tempDataFolder))
                Directory.Delete(_tempDataFolder, true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private static IBackup MakeBackup(params (string Name, string Value)[] settings)
        => MakeBackup(0, settings);

    private static IBackup MakeBackup(long id, params (string Name, string Value)[] settings)
        => new Backup
        {
            ID = id.ToString(),
            Name = "test",
            TargetURL = "file://test",
            Sources = [],
            Settings = settings.Select(s => (ISetting)new Setting { Name = s.Name, Value = s.Value, Filter = string.Empty }).ToArray()
        };

    private Dictionary<string, string?> Apply(IBackup backup)
        => Runner.ApplyOptions(_connection, backup, Runner.GetCommonOptions(_connection), out _);

    private static HashSet<string> SplitList(string? value)
        => new((value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

    [Test]
    public void NoReportUrlLeavesReportOptionsAlone()
    {
        var options = Apply(MakeBackup());

        Assert.IsFalse(options.ContainsKey("send-http-json-urls"));
        Assert.IsFalse(options.ContainsKey("send-http-operations"));
        Assert.IsFalse(options.ContainsKey("perform-restore-test-after"));
    }

    [Test]
    public void ReportUrlWithoutAcceptedOperationsEnablesBackupReportsOnly()
    {
        // A console that predates the report operations setting only takes backup reports
        _connection.ApplicationSettings.AdditionalReportUrl = "https://console.example.com/report";

        var options = Apply(MakeBackup());

        Assert.AreEqual("https://console.example.com/report", options["send-http-json-urls"]);
        CollectionAssert.AreEquivalent(new[] { nameof(OperationMode.Backup) }, SplitList(options["send-http-operations"]));
    }

    [Test]
    public void ReportUrlEnablesTheOperationsTheConsoleAccepts()
    {
        _connection.ApplicationSettings.AdditionalReportUrl = "https://console.example.com/report";
        _connection.ApplicationSettings.RemoteControlReportOperations = "Backup, sync,RestoreTest,NotAnOperation";

        var operations = SplitList(Apply(MakeBackup())["send-http-operations"]);

        Assert.AreEqual(3, operations.Count, "Unknown names from the console are ignored");
        Assert.IsTrue(operations.Contains(nameof(OperationMode.Backup)));
        Assert.IsTrue(operations.Contains(nameof(OperationMode.Sync)));
        Assert.IsTrue(operations.Contains(nameof(OperationMode.RestoreTest)));
    }

    [Test]
    public void InjectedOperationsPassOptionValidation()
    {
        _connection.ApplicationSettings.AdditionalReportUrl = "https://console.example.com/report";
        _connection.ApplicationSettings.RemoteControlReportOperations = "Backup,Sync,RestoreTest";
        var value = Apply(MakeBackup())["send-http-operations"];

        var argument = new Duplicati.Library.Modules.Builtin.SendHttpMessage().SupportedCommands.First(x => x.Name == "send-http-operations");
        Assert.IsNull(Duplicati.Library.Utility.CommandLineArgumentValidator.ValidateOptionValue(argument, "send-http-operations", value));
    }

    [Test]
    public void ReportUrlMergesWithUserSuppliedOperationsAndUrls()
    {
        _connection.ApplicationSettings.AdditionalReportUrl = "https://console.example.com/report";
        _connection.ApplicationSettings.RemoteControlReportOperations = "Backup,Sync,RestoreTest";

        var options = Apply(MakeBackup(
            ("send-http-json-urls", "https://user.example.com/hook"),
            ("send-http-operations", "backup,Restore")));

        Assert.AreEqual("https://user.example.com/hook;https://console.example.com/report", options["send-http-json-urls"]);
        var operations = SplitList(options["send-http-operations"]);
        Assert.AreEqual(4, operations.Count, "Duplicate operation names should be merged case-insensitively");
        Assert.IsTrue(operations.Contains("Backup"));
        Assert.IsTrue(operations.Contains("Sync"));
        Assert.IsTrue(operations.Contains("Restore"));
        Assert.IsTrue(operations.Contains("RestoreTest"));
    }

    /// <summary>
    /// The restore test intervals as the console sends them: a JSON object of backup id to interval, with the
    /// default for every other backup under the empty id
    /// </summary>
    private static string Intervals(params (string BackupId, string Interval)[] intervals)
        => System.Text.Json.JsonSerializer.Serialize(intervals.ToDictionary(x => x.BackupId, x => x.Interval));

    [Test]
    public void RestoreTestIntervalIsAppliedWhenNotSetByUser()
    {
        _connection.ApplicationSettings.RemoteControlRestoreTestIntervals = Intervals((Runner.DefaultRestoreTestIntervalBackupId, "7D"));

        var options = Apply(MakeBackup());

        Assert.AreEqual("7D", options["perform-restore-test-after"]);
    }

    [Test]
    public void RestoreTestIntervalDoesNotOverrideUserValue()
    {
        _connection.ApplicationSettings.RemoteControlRestoreTestIntervals = Intervals((Runner.DefaultRestoreTestIntervalBackupId, "7D"), ("DB-1", "3D"));

        Assert.AreEqual("1D", Apply(MakeBackup(("perform-restore-test-after", "1D")))["perform-restore-test-after"]);
        Assert.AreEqual("0s", Apply(MakeBackup(("perform-restore-test-after", "0s")))["perform-restore-test-after"],
            "An explicit zero interval disables the restore test and must be kept");
        Assert.AreEqual("2D", Apply(MakeBackup(("--perform-restore-test-after", "2D")))["perform-restore-test-after"],
            "Override options must also be respected");
        Assert.AreEqual("5D", Apply(MakeBackup(1, ("perform-restore-test-after", "5D")))["perform-restore-test-after"],
            "The user's own value also wins over the interval the console sent for the backup");
    }

    [Test]
    public void EmptyOrZeroOrInvalidRestoreTestIntervalIsIgnored()
    {
        foreach (var interval in new[] { "", "   ", "0s", "0", "not-a-timespan" })
        {
            _connection.ApplicationSettings.RemoteControlRestoreTestIntervals = Intervals((Runner.DefaultRestoreTestIntervalBackupId, interval));
            var options = Apply(MakeBackup());
            Assert.IsFalse(options.ContainsKey("perform-restore-test-after"), $"Interval '{interval}' should not be applied");
        }
    }

    [Test]
    public void PerBackupRestoreTestIntervalTakesPrecedenceOverTheDefault()
    {
        _connection.ApplicationSettings.RemoteControlRestoreTestIntervals = Intervals(
            (Runner.DefaultRestoreTestIntervalBackupId, "7D"), ("DB-1", "1D"), ("my-own-id", "2D"), ("DB-3", "0s"));

        Assert.AreEqual("1D", Apply(MakeBackup(1))["perform-restore-test-after"], "A backup the console names gets its own interval");
        Assert.AreEqual("2D", Apply(MakeBackup(2, ("backup-id", "my-own-id")))["perform-restore-test-after"], "The backup is named by the id it reports");
        Assert.IsFalse(Apply(MakeBackup(3)).ContainsKey("perform-restore-test-after"), "A zero interval means no restore test for the backup");
        Assert.AreEqual("7D", Apply(MakeBackup(4))["perform-restore-test-after"], "A backup the console does not name gets the default");
    }

    [Test]
    public void WithoutADefaultOnlyTheNamedBackupsGetAnInterval()
    {
        _connection.ApplicationSettings.RemoteControlRestoreTestIntervals = Intervals(("DB-1", "1D"));

        Assert.AreEqual("1D", Apply(MakeBackup(1))["perform-restore-test-after"]);
        Assert.IsFalse(Apply(MakeBackup(2)).ContainsKey("perform-restore-test-after"), "No default means no restore test for the other backups");
    }

    [Test]
    public void ABackupWithAnEmptyReportedIdDoesNotTakeTheDefaultByName()
    {
        // An empty backup id falls back to the id calculated from the url, so it never names the default entry
        _connection.ApplicationSettings.RemoteControlRestoreTestIntervals = Intervals((Runner.DefaultRestoreTestIntervalBackupId, "7D"));

        Assert.AreEqual("7D", Apply(MakeBackup(1, ("backup-id", "")))["perform-restore-test-after"], "It gets the default as a backup the console does not name");
    }

    [Test]
    public void InvalidRestoreTestIntervalsAreIgnored()
    {
        foreach (var intervals in new[] { "not json", "[]", "null", "{\"\":7}" })
        {
            _connection.ApplicationSettings.RemoteControlRestoreTestIntervals = intervals;
            Assert.IsFalse(Apply(MakeBackup(1)).ContainsKey("perform-restore-test-after"), $"Intervals '{intervals}' should not be applied");
        }
    }
}
