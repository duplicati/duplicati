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

        /// <summary>
        /// Writes a value straight into the DBPath column, so a test can plant the shape an older
        /// version stored rather than the shape <see cref="Connection.UpdateBackupDBPath"/> writes.
        /// </summary>
        private void SetRawDbPath(string backupId, string rawValue)
        {
            _connection.ExecuteWithCommand(cmd =>
            {
                cmd.CommandText = @"UPDATE ""Backup"" SET ""DBPath"" = @path WHERE ""ID"" = @id";

                var path = cmd.CreateParameter();
                path.ParameterName = "@path";
                path.Value = rawValue;
                cmd.Parameters.Add(path);

                var id = cmd.CreateParameter();
                id.ParameterName = "@id";
                id.Value = long.Parse(backupId);
                cmd.Parameters.Add(id);

                cmd.ExecuteNonQuery();
            });
        }

        /// <summary>
        /// The data folder without its root, ending in a directory separator. A Linux install with
        /// no home folder resolved its data folder to the relative path "var/lib/Duplicati", and
        /// the database paths it stored were joined onto that, so they look like this.
        /// </summary>
        private string RootlessDataFolder()
        {
            var full = Library.Common.IO.Util.AppendDirSeparator(Path.GetFullPath(_tempDataFolder));
            return full.Substring((Path.GetPathRoot(full) ?? "").Length);
        }

        /// <summary>
        /// A path stored before the data folder was rooted names the file from the root, so joining
        /// it onto the data folder again doubles it (issue #7284).
        /// </summary>
        [Test]
        public void LegacyRootlessDbPath_GetBackup_ResolvesInsideDataFolder()
        {
            var backup = CreateTestBackup();
            _connection.AddOrUpdateBackupAndSchedule(backup, null);
            SetRawDbPath(backup.ID!, RootlessDataFolder() + "legacy.sqlite");

            var loadedBackup = _connection.GetBackup(backup.ID);
            Assert.IsNotNull(loadedBackup);
            Assert.AreEqual(Path.GetFullPath(Path.Combine(_tempDataFolder, "legacy.sqlite")), loadedBackup!.DBPath,
                "A path stored by an install whose data folder was itself relative was joined onto the data folder again");
        }

        /// <summary>
        /// The backup list reads the same column through its own query, so it has to agree.
        /// </summary>
        [Test]
        public void LegacyRootlessDbPath_BackupsProperty_ResolvesInsideDataFolder()
        {
            var backup = CreateTestBackup();
            _connection.AddOrUpdateBackupAndSchedule(backup, null);
            SetRawDbPath(backup.ID!, RootlessDataFolder() + "legacy.sqlite");

            var listed = _connection.Backups.Single();
            Assert.AreEqual(Path.GetFullPath(Path.Combine(_tempDataFolder, "legacy.sqlite")), listed.DBPath);
        }

        /// <summary>
        /// An ordinary relative path with a folder in it is still relative to the data folder, so
        /// the rule above must not swallow it.
        /// </summary>
        [Test]
        public void RelativeDbPath_WithSubfolder_ResolvesUnderDataFolder()
        {
            var backup = CreateTestBackup();
            _connection.AddOrUpdateBackupAndSchedule(backup, null);
            SetRawDbPath(backup.ID!, Path.Combine("sub", "guard.sqlite"));

            var loadedBackup = _connection.GetBackup(backup.ID);
            Assert.IsNotNull(loadedBackup);
            Assert.AreEqual(Path.GetFullPath(Path.Combine(_tempDataFolder, "sub", "guard.sqlite")), loadedBackup!.DBPath);
        }

        /// <summary>
        /// The match has to be on a whole directory component: a sibling folder whose name merely
        /// starts with the data folder's name is not the data folder.
        /// </summary>
        [Test]
        public void RelativeDbPath_SiblingOfDataFolderPrefix_IsNotTreatedAsLegacy()
        {
            var backup = CreateTestBackup();
            _connection.AddOrUpdateBackupAndSchedule(backup, null);

            var sibling = RootlessDataFolder().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "-sibling";
            SetRawDbPath(backup.ID!, Path.Combine(sibling, "guard.sqlite"));

            var loadedBackup = _connection.GetBackup(backup.ID);
            Assert.IsNotNull(loadedBackup);
            Assert.AreEqual(Path.GetFullPath(Path.Combine(_tempDataFolder, sibling, "guard.sqlite")), loadedBackup!.DBPath);
        }

        /// <summary>
        /// A path that is already absolute is never joined onto anything, including one that points
        /// inside the data folder.
        /// </summary>
        [Test]
        public void AbsoluteDbPathInsideDataFolder_IsReturnedUnchanged()
        {
            var backup = CreateTestBackup();
            _connection.AddOrUpdateBackupAndSchedule(backup, null);

            var absolute = Path.Combine(_tempDataFolder, "abs.sqlite");
            SetRawDbPath(backup.ID!, absolute);

            var loadedBackup = _connection.GetBackup(backup.ID);
            Assert.IsNotNull(loadedBackup);
            Assert.AreEqual(absolute, loadedBackup!.DBPath);
        }

        /// <summary>
        /// A data folder that is the root itself has no path to recognise, and an empty prefix would
        /// match every path.
        /// </summary>
        [Test]
        public void LegacyRule_DataFolderIsRoot_DoesNotRewrite()
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_tempDataFolder)) ?? "";
            Assert.IsNotEmpty(root);

            Assert.AreEqual(Path.GetFullPath(Path.Combine(root, "X.sqlite")),
                DataFolderManager.ResolveDataFolderRelativePath(root, "X.sqlite"));
        }

        /// <summary>
        /// The one path the rule above would misread if it were stored relative: a database that
        /// really does live in a folder inside the data folder named after the data folder itself.
        /// It has to survive a write and a read.
        /// </summary>
        [Test]
        public void UpdateBackupDbPath_MirroredNestedPath_RoundTrips()
        {
            var backup = CreateTestBackup();
            _connection.AddOrUpdateBackupAndSchedule(backup, null);

            var newPath = Path.Combine(_tempDataFolder, RootlessDataFolder(), "mirrored.sqlite");
            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            File.WriteAllText(newPath, "");

            _connection.UpdateBackupDBPath(backup, newPath);

            var loadedBackup = _connection.GetBackup(backup.ID);
            Assert.IsNotNull(loadedBackup);
            Assert.AreEqual(Path.GetFullPath(newPath), loadedBackup!.DBPath);
        }
    }
}
