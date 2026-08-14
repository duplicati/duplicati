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
using Duplicati.Library.Logging;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;
using Duplicati.Server.Database;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A stored timezone id that no longer resolves (e.g. after moving the server
    /// database between Windows and Linux) makes the scheduler silently compute all
    /// next-run times in the server's local timezone. The fallback must be visible
    /// in the log, so the user can find out why their schedule shifted.
    /// </summary>
    [TestFixture]
    [Category("TimeZoneSetting")]
    public class TimeZoneSettingTests
    {
        private string _tempDataFolder = null!;
        private string _databasePath = null!;

        [SetUp]
        public void SetUp()
        {
            _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-tz-test-{Guid.NewGuid()}");
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

        [Test]
        public async Task UnresolvableStoredTimezoneWarnsOnceAndFallsBack()
        {
            // Store a valid timezone through the normal path, so the settings row exists
            using (var setup = await MakeConnectionAsync())
                setup.ApplicationSettings.Timezone = TimeZoneInfo.Utc;

            // Corrupt the stored id the way a cross-platform database move would:
            // write an id this system cannot resolve (settings are cached per
            // connection, so this happens between connections)
            await using (var db = await SQLiteLoader.LoadConnectionAsync(_databasePath))
            await using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"UPDATE ""Option"" SET ""Value"" = 'No/Such_Zone' WHERE ""Name"" = 'server-timezone' AND ""BackupID"" = -2";
                Assert.AreEqual(1, await cmd.ExecuteNonQueryAsync(), "The timezone settings row should exist");
            }

            using var connection = await MakeConnectionAsync();
            var captured = new List<LogEntry>();
            TimeZoneInfo first, second;
            using (Log.StartScope(e => captured.Add(e)))
            {
                first = connection.ApplicationSettings.Timezone;
                second = connection.ApplicationSettings.Timezone;
            }

            Assert.AreEqual(TimeZoneInfo.Local.Id, first.Id,
                "An unresolvable stored timezone falls back to the local timezone (pre-existing behavior)");
            Assert.AreEqual(TimeZoneInfo.Local.Id, second.Id);

            var warnings = captured.Where(x => x.Level == LogMessageType.Warning && x.Id == "UnresolvableTimezone").ToList();
            Assert.AreEqual(1, warnings.Count,
                $"The fallback must be logged exactly once per id (got {warnings.Count}); the scheduler reads this property on every pass");
            Assert.IsTrue(warnings[0].FormattedMessage.Contains("No/Such_Zone"),
                $"The warning should name the unresolvable id; got: {warnings[0].FormattedMessage}");
        }

        [Test]
        public void FindTimeZoneReturnsNullForUnknownId()
        {
            Assert.IsNull(TimeZoneHelper.FindTimeZone("No/Such_Zone"),
                "An unknown timezone must yield null so callers can report it");
            Assert.IsNotNull(TimeZoneHelper.FindTimeZone(TimeZoneInfo.Local.Id));
        }
    }
}
