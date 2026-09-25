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
using System.Threading;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Utility;
using Duplicati.WebserverCore.Abstractions;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Program = Duplicati.Server.Program;
using ServerSettings = Duplicati.Server.Database.ServerSettings;
using Connection = Duplicati.Server.Database.Connection;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests that the "database appears to be encrypted, but no key was specified"
/// warning emitted by <see cref="Program.GetDatabaseConnection"/> is only shown
/// when the database actually contains encrypted fields.
/// Specifically, a clean install must never trigger this warning.
/// </summary>
[TestFixture]
public class EncryptedFieldsWarningTests
{
    private sealed class LogSink : ILogDestination
    {
        public List<LogEntry> Entries { get; } = [];

        public void WriteMessage(LogEntry entry)
            => Entries.Add(entry);
    }

    private sealed class TestApplicationSettings(string dataFolder) : IApplicationSettings
    {
        public Action? StartOrStopUsageReporter { get; set; }
        public string DataFolder => dataFolder;
        public string Origin { get; set; } = "UnitTest";
        public CancellationToken ApplicationExit => CancellationToken.None;
        public void SignalApplicationExit() { }
        public ISecretProvider? SecretProvider { get; set; }
        public bool SettingsEncryptionKeyProvidedExternally { get; set; }
    }

    private string dataFolder = string.Empty;

    [SetUp]
    public void Setup()
    {
        this.dataFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this.dataFolder);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(this.dataFolder, true);
        }
        catch
        {
            // If there's an exception, let the OS deal with cleaning up the temp folder.
        }
    }

    [Test]
    [Category("Startup")]
    public void CleanInstallWithoutKeyDoesNotWarnAboutEncryptedDatabase()
    {
        var logSink = new LogSink();
        var options = new Dictionary<string, string>
        {
            // Prevent interference from a default secret provider on the test machine
            { "disable-default-secret-provider", "true" }
        };

        using var isolatingScope = Log.StartIsolatingScope(true);
        using var log = Log.StartScope(logSink, LogMessageType.Warning);

        using (Program.GetDatabaseConnection(new TestApplicationSettings(this.dataFolder), options, true, false))
        {
        }

        var warningIds = logSink.Entries.Select(e => e.Id).ToList();

        // The clean install must not claim the database is encrypted
        Assert.That(warningIds, Does.Not.Contain("EncryptionKeyMissing"),
            "A clean install must not trigger the 'database appears to be encrypted' warning");

        // The database must not have the encrypted-fields flag set
        using (var con = Library.SQLiteHelper.SQLiteLoader.LoadConnection())
        {
            var databasePath = Path.Combine(this.dataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);
            Library.SQLiteHelper.SQLiteLoader.OpenDatabaseAsync(con, databasePath, null).Await();
            using var cmd = con.CreateCommand(@"SELECT ""Value"" FROM ""Option"" WHERE ""Name"" = @Name AND ""BackupID"" = @BackupId");
            var value = cmd
                .SetParameterValue("@Name", ServerSettings.CONST.ENCRYPTED_FIELDS)
                .SetParameterValue("@BackupId", Connection.SERVER_SETTINGS_ID)
                .ExecuteScalar()?.ToString();
            Assert.That(value, Is.Not.EqualTo("True"), "A clean install must not set the encrypted-fields flag");
        }
    }

    [Test]
    [Category("Startup")]
    public void CleanInstallWithKeyDoesNotWarnAboutEncryptedDatabase()
    {
        var logSink = new LogSink();
        var options = new Dictionary<string, string>
        {
            { "disable-default-secret-provider", "true" },
            { "settings-encryption-key", "a valid test encryption key" }
        };

        using var isolatingScope = Log.StartIsolatingScope(true);
        using var log = Log.StartScope(logSink, LogMessageType.Warning);

        using (Program.GetDatabaseConnection(new TestApplicationSettings(this.dataFolder), options, true, false))
        {
        }

        var warningIds = logSink.Entries.Select(e => e.Id).ToList();
        Assert.That(warningIds, Does.Not.Contain("EncryptionKeyMissing"),
            "A clean install with a key must not trigger the 'database appears to be encrypted' warning");
        Assert.That(warningIds, Does.Not.Contain("MissingEncryptionKey"),
            "A clean install with a key must not warn about a missing encryption key");
    }

    [Test]
    [Category("Startup")]
    public void EncryptedDatabaseWithoutKeyWarnsAboutEncryptedDatabase()
    {
        // Create a clean database first
        var setupOptions = new Dictionary<string, string>
        {
            { "disable-default-secret-provider", "true" }
        };
        using (Program.GetDatabaseConnection(new TestApplicationSettings(this.dataFolder), setupOptions, true, false))
        {
        }

        // Set the encrypted-fields flag directly in the database,
        // simulating an existing database previously opened with a key
        using (var con = Library.SQLiteHelper.SQLiteLoader.LoadConnection())
        {
            var databasePath = Path.Combine(this.dataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);
            Library.SQLiteHelper.SQLiteLoader.OpenDatabaseAsync(con, databasePath, null).Await();
            using var cmd = con.CreateCommand(@"INSERT INTO ""Option"" (""BackupID"", ""Filter"", ""Name"", ""Value"") VALUES (@BackupId, '', @Name, 'True')");
            cmd.SetParameterValue("@Name", ServerSettings.CONST.ENCRYPTED_FIELDS)
                .SetParameterValue("@BackupId", Connection.SERVER_SETTINGS_ID)
                .ExecuteNonQuery();
        }

        // Sanity check: the flag must now be stored in the database
        Assert.IsTrue(GetEncryptedFieldsFlag(), "Test setup failed to persist the encrypted-fields flag");

        // Open the same database again, but now without a key
        var logSink = new LogSink();
        var options = new Dictionary<string, string>
        {
            { "disable-default-secret-provider", "true" }
        };

        using var isolatingScope = Log.StartIsolatingScope(true);
        using var log = Log.StartScope(logSink, LogMessageType.Warning);

        using (Program.GetDatabaseConnection(new TestApplicationSettings(this.dataFolder), options, true, false))
        {
        }

        var warningIds = logSink.Entries.Select(e => e.Id).ToList();
        Assert.That(warningIds, Does.Contain("EncryptionKeyMissing"),
            "An encrypted database opened without a key must trigger the 'database appears to be encrypted' warning");
    }

    private bool GetEncryptedFieldsFlag()
    {
        using var con = Library.SQLiteHelper.SQLiteLoader.LoadConnection();
        var databasePath = Path.Combine(this.dataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);
        Library.SQLiteHelper.SQLiteLoader.OpenDatabaseAsync(con, databasePath, null).Await();
        using var cmd = con.CreateCommand(@"SELECT ""Value"" FROM ""Option"" WHERE ""Name"" = @Name AND ""BackupID"" = @BackupId");
        var value = cmd
            .SetParameterValue("@Name", ServerSettings.CONST.ENCRYPTED_FIELDS)
            .SetParameterValue("@BackupId", Connection.SERVER_SETTINGS_ID)
            .ExecuteScalar()?.ToString();
        return Library.Utility.Utility.ParseBool(value, false);
    }
}
