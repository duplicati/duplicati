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
using Duplicati.Library.Logging;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Server.Database;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// An update check interval that cannot be parsed is stored without complaint and
    /// then silently replaced with a week, so the configured interval never takes
    /// effect and the user has no way of finding out why.
    /// </summary>
    [TestFixture]
    [Category("UpdateCheckIntervalSetting")]
    public class UpdateCheckIntervalSettingTests
    {
        private const string SettingName = "update-check-interval";
        private const string InvalidInterval = "1 Week";

        private string _tempDataFolder = null!;
        private string _databasePath = null!;

        [SetUp]
        public void SetUp()
        {
            _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-interval-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDataFolder);
            _databasePath = Path.Combine(_tempDataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDataFolder))
                    Directory.Delete(_tempDataFolder, true);
            }
            catch
            {
            }
        }

        private async Task<Connection> MakeConnectionAsync()
        {
            var dbConnection = await SQLiteLoader.LoadConnectionAsync(_databasePath);
            DatabaseUpgrader.UpgradeDatabase(dbConnection, _databasePath, typeof(Library.RestAPI.Database.DatabaseSchemaMarker));
            return new Connection(dbConnection, true, null, _tempDataFolder, () => { });
        }

        /// <summary>
        /// Stores a value the way a previous version would have, bypassing any validation.
        /// The settings are cached per connection, so this runs between connections.
        /// </summary>
        private async Task StoreRawSettingAsync(string value)
        {
            // Create the schema, and let the connection go again, as the settings are
            // cached per connection
            using (await MakeConnectionAsync()) { }

            await using var db = await SQLiteLoader.LoadConnectionAsync(_databasePath);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = @$"INSERT OR REPLACE INTO ""Option"" (""BackupID"", ""Filter"", ""Name"", ""Value"") VALUES (-2, '', '{SettingName}', '{value}')";
            Assert.AreEqual(1, await cmd.ExecuteNonQueryAsync(), "The setting should be stored");
        }

        [Test]
        public async Task UnusableStoredIntervalWarnsOnceAndFallsBack()
        {
            await StoreRawSettingAsync(InvalidInterval);

            using var connection = await MakeConnectionAsync();
            var lastCheck = connection.ApplicationSettings.LastUpdateCheck;

            var captured = new List<LogEntry>();
            DateTime first, second;
            using (Log.StartScope(e => captured.Add(e)))
            {
                first = connection.ApplicationSettings.NextUpdateCheck;
                second = connection.ApplicationSettings.NextUpdateCheck;
            }

            Assert.AreEqual(lastCheck.AddDays(7), first,
                "An unusable interval falls back to a week (pre-existing behavior)");
            Assert.AreEqual(first, second);

            var warnings = captured.Where(x => x.Level == LogMessageType.Warning && x.Id == "InvalidUpdateCheckInterval").ToList();
            Assert.AreEqual(1, warnings.Count,
                $"The fallback must be logged exactly once per value (got {warnings.Count}); the poll thread reads this on every pass");
            Assert.IsTrue(warnings[0].FormattedMessage.Contains(InvalidInterval),
                $"The warning should name the unusable value; got: {warnings[0].FormattedMessage}");
        }

        [Test]
        public async Task UnusableIntervalIsRejectedWhenStored()
        {
            using var connection = await MakeConnectionAsync();

            var ex = Assert.Throws<UserInformationException>(() =>
                connection.ApplicationSettings.UpdateSettings(new Dictionary<string, string?>
                {
                    [SettingName] = InvalidInterval
                }, false));

            Assert.AreEqual("InvalidUpdateCheckInterval", ex!.HelpID);
            Assert.IsTrue(ex.Message.Contains(InvalidInterval),
                $"The error should name the rejected value; got: {ex.Message}");
        }

        [Test]
        public async Task UsableIntervalIsAccepted()
        {
            using var connection = await MakeConnectionAsync();

            var captured = new List<LogEntry>();
            DateTime next;
            using (Log.StartScope(e => captured.Add(e)))
            {
                connection.ApplicationSettings.UpdateSettings(new Dictionary<string, string?>
                {
                    [SettingName] = "2W"
                }, false);

                next = connection.ApplicationSettings.NextUpdateCheck;
            }

            Assert.AreEqual(connection.ApplicationSettings.LastUpdateCheck.AddDays(14), next);
            Assert.IsFalse(captured.Any(x => x.Id == "InvalidUpdateCheckInterval"),
                "A usable interval should not be reported");
        }

        [Test]
        public async Task StoredUnusableIntervalDoesNotBlockOtherSettings()
        {
            // An installation that already holds an unusable value must still be able
            // to change unrelated settings
            await StoreRawSettingAsync(InvalidInterval);

            using var connection = await MakeConnectionAsync();

            Assert.DoesNotThrow(() =>
                connection.ApplicationSettings.UpdateSettings(new Dictionary<string, string?>
                {
                    ["some-unrelated-setting"] = "value",
                    [SettingName] = InvalidInterval
                }, false));
        }
    }
}
