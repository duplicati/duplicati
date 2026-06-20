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
using Duplicati.Library.SQLiteHelper;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests for relative DBPath storage and resolution in the server database.
    /// </summary>
    [TestFixture]
    [Category("RelativeDbPath")]
    public class RelativeDbPathTests
    {
        private string _tempDataFolder = null!;
        private string _databasePath = null!;
        private Connection _connection = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-relative-db-test-{Guid.NewGuid()}");
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
                    // Best effort cleanup
                }
            }
        }

        private static Backup CreateTestBackup()
        {
            return new Backup()
            {
                ID = null,
                Name = "Test Backup",
                Description = "Test Description",
                Tags = new string[] { "test" },
                TargetURL = "file:///test",
                Sources = new string[] { "/test" },
                Settings = new ISetting[0],
                Filters = new IFilter[0],
                Metadata = new Dictionary<string, string>()
            };
        }

        [Test]
        public void NewBackup_StoresRelativeDbPath()
        {
            var backup = CreateTestBackup();

            _connection.AddOrUpdateBackupAndSchedule(backup, null);

            Assert.IsNotNull(backup.DBPath);
            Assert.IsFalse(Path.IsPathRooted(backup.DBPath), "New backup DBPath should be stored as relative");
            Assert.IsTrue(backup.DBPath.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase));
        }

        [Test]
        public void LoadBackup_ResolvesRelativeDbPath()
        {
            var backup = CreateTestBackup();
            _connection.AddOrUpdateBackupAndSchedule(backup, null);

            var loadedBackup = _connection.GetBackup(backup.ID);
            Assert.IsNotNull(loadedBackup);
            Assert.IsTrue(Path.IsPathRooted(loadedBackup!.DBPath), "Loaded backup DBPath should be absolute");
            Assert.AreEqual(Path.GetFullPath(Path.Combine(_tempDataFolder, backup.DBPath)), loadedBackup.DBPath);
        }

        [Test]
        public void UpdateBackupDbPath_UnderDataFolder_StoresRelative()
        {
            var backup = CreateTestBackup();
            _connection.AddOrUpdateBackupAndSchedule(backup, null);

            var newPath = Path.Combine(_tempDataFolder, "moved-backup.sqlite");
            File.WriteAllText(newPath, "");

            _connection.UpdateBackupDBPath(backup, newPath);

            var loadedBackup = _connection.GetBackup(backup.ID);
            Assert.IsNotNull(loadedBackup);
            // The loaded DBPath should be resolved to absolute
            Assert.IsTrue(Path.IsPathRooted(loadedBackup!.DBPath));
            Assert.AreEqual(Path.GetFileName(newPath), Path.GetFileName(loadedBackup.DBPath));

            // Verify the raw stored value is relative by querying the database directly
            using (var db = SQLiteLoader.LoadConnection(_databasePath))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"SELECT ""DBPath"" FROM ""Backup"" WHERE ""ID"" = @id";
                var param = cmd.CreateParameter();
                param.ParameterName = "@id";
                param.Value = long.Parse(backup.ID);
                cmd.Parameters.Add(param);
                var rawDbPath = cmd.ExecuteScalar()?.ToString() ?? "";
                Assert.AreEqual("moved-backup.sqlite", rawDbPath);
            }
        }

        [Test]
        public void UpdateBackupDbPath_OutsideDataFolder_StoresAbsolute()
        {
            var backup = CreateTestBackup();
            _connection.AddOrUpdateBackupAndSchedule(backup, null);

            var outsideFolder = Path.Combine(Path.GetTempPath(), $"duplicati-outside-{Guid.NewGuid()}");
            Directory.CreateDirectory(outsideFolder);
            try
            {
                var newPath = Path.Combine(outsideFolder, "moved-backup.sqlite");
                File.WriteAllText(newPath, "");

                _connection.UpdateBackupDBPath(backup, newPath);

                var loadedBackup = _connection.GetBackup(backup.ID);
                Assert.IsNotNull(loadedBackup);
                Assert.AreEqual(newPath, loadedBackup!.DBPath);
            }
            finally
            {
                Directory.Delete(outsideFolder, true);
            }
        }

        [Test]
        public void BackupsProperty_ResolvesRelativeDbPaths()
        {
            var backup1 = CreateTestBackup();
            backup1.Name = "Backup 1";
            var backup2 = CreateTestBackup();
            backup2.Name = "Backup 2";

            _connection.AddOrUpdateBackupAndSchedule(backup1, null);
            _connection.AddOrUpdateBackupAndSchedule(backup2, null);

            var backups = _connection.Backups;
            Assert.AreEqual(2, backups.Length);

            foreach (var bk in backups)
            {
                Assert.IsTrue(Path.IsPathRooted(bk.DBPath), $"Backup '{bk.Name}' DBPath should be absolute");
            }
        }

        [Test]
        public void ExistingAbsoluteDbPath_StaysAbsolute()
        {
            var outsideFolder = Path.Combine(Path.GetTempPath(), $"duplicati-outside-{Guid.NewGuid()}");
            Directory.CreateDirectory(outsideFolder);
            try
            {
                var absolutePath = Path.Combine(outsideFolder, "external-backup.sqlite");
                File.WriteAllText(absolutePath, "");

                var backup = CreateTestBackup();
                backup.SetDBPath(absolutePath);
                _connection.AddOrUpdateBackupAndSchedule(backup, null);

                var loadedBackup = _connection.GetBackup(backup.ID);
                Assert.IsNotNull(loadedBackup);
                Assert.AreEqual(absolutePath, loadedBackup!.DBPath);
            }
            finally
            {
                Directory.Delete(outsideFolder, true);
            }
        }
    }
}
