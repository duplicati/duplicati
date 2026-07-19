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
using Duplicati.Server.Database;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A malformed value in the server settings is ignored by design, but it must not be
    /// ignored <em>silently</em>: the user has to be able to find out from the log why the
    /// setting they typed had no effect.
    /// </summary>
    [TestFixture]
    [Category("LiveControls")]
    public class LiveControlsLoggingTests
    {
        private string _tempDataFolder = null!;
        private string _databasePath = null!;
        private Connection _connection = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-livecontrols-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDataFolder);

            _databasePath = Path.Combine(_tempDataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);

            var dbConnection = await SQLiteLoader.LoadConnectionAsync(_databasePath);
            DatabaseUpgrader.UpgradeDatabase(dbConnection, _databasePath, typeof(Library.RestAPI.Database.DatabaseSchemaMarker));

            _connection = new Connection(dbConnection, true, null, _tempDataFolder, () => { });
        }

        [TearDown]
        public void TearDown()
        {
            _connection?.Dispose();

            if (Directory.Exists(_tempDataFolder))
            {
                try
                {
                    Directory.Delete(_tempDataFolder, true);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// The startup delay is parsed while LiveControls is being constructed. An unparsable
        /// value leaves the delay at zero, which is the correct fallback, but the failure has
        /// to reach the log so the setting does not appear to be honoured when it is not.
        /// </summary>
        [Test]
        [Category("LiveControls")]
        public void MalformedStartupDelayIsReportedAsync()
        {
            const string badValue = "not-a-duration";
            _connection.ApplicationSettings.StartupDelayDuration = badValue;

            var captured = new List<LogEntry>();
            using (Log.StartScope(e => captured.Add(e)))
                _ = new Duplicati.Server.LiveControls(_connection);

            var entry = captured.FirstOrDefault(x => x.Id == "ParseStartupDelayError");
            Assert.IsNotNull(entry,
                "A startup delay that cannot be parsed must be reported in the log instead of being discarded silently.");
            Assert.AreEqual(LogMessageType.Warning, entry!.Level);
            Assert.IsTrue(entry.FormattedMessage.Contains(badValue),
                $"The log message should quote the offending value; got: {entry.FormattedMessage}");
            Assert.IsNotNull(entry.Exception,
                "The parse exception should be attached so the reason for the failure is visible.");
        }
    }
}
