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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Interface;
using Duplicati.Library.RestAPI.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Exceptions;
using Duplicati.WebserverCore.Services;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests the real <see cref="FolderStatusService"/> against a server database.
/// The backup listing does not carry the sources, so the service must load each
/// backup fully; these tests fail if it does not.
/// </summary>
[TestFixture]
[Category("FolderStatus")]
public class FolderStatusServiceDatabaseTests
{
    private string _tempDataFolder = null!;
    private Connection _connection = null!;
    private FolderStatusService _service = null!;

    /// <summary>
    /// Queue runner with nothing running
    /// </summary>
    private sealed class IdleQueueRunnerService : IQueueRunnerService
    {
        public IQueuedTask? GetCurrentTask() => null;
        public List<IQueuedTask> GetCurrentTasks() => new();
        public bool GetIsActive() => false;
        public CachedTaskResult? GetCachedTaskResults(long taskID) => null;
        public long AddTask(IQueuedTask task) => 0;
        public long AddTask(IQueuedTask task, bool skipQueue) => 0;
        public void Terminate(bool wait) { }
        public void Resume() { }
        public void Pause() { }
        public IList<Tuple<long, string?>> GetQueueWithIds() => new List<Tuple<long, string?>>();
        public void CancelCurrentTaskLockWait(long taskID) { }
        public Task<IBasicResults?> RunImmediatelyAsync(IQueuedTask task) => Task.FromResult<IBasicResults?>(null);
    }

    [SetUp]
    public async Task SetUpAsync()
    {
        _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-folder-status-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDataFolder);

        var databasePath = Path.Combine(_tempDataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);
        var dbConnection = await SQLiteLoader.LoadConnectionAsync(databasePath);
        DatabaseUpgrader.UpgradeDatabase(dbConnection, databasePath, typeof(DatabaseSchemaMarker));
        _connection = new Connection(dbConnection, true, null, _tempDataFolder, () => { });
        _connection.ApplicationSettings.EnableFolderStatusService = true;
        _service = new FolderStatusService(_connection, new IdleQueueRunnerService());
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

    /// <summary>
    /// Creates a source folder under the temp folder and returns its full path
    /// </summary>
    private string MakeSourceFolder(string name)
    {
        var path = Path.Combine(_tempDataFolder, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Saves a backup with the given sources, mirroring how the web UI constructs one
    /// </summary>
    private void SaveBackup(string name, params string[] sources)
        => SaveBackup(name, new Dictionary<string, string>(), sources);

    /// <summary>
    /// Saves a backup with the given metadata and sources
    /// </summary>
    private void SaveBackup(string name, Dictionary<string, string> metadata, params string[] sources)
    {
        var backup = new Backup
        {
            Name = name,
            Description = string.Empty,
            Tags = Array.Empty<string>(),
            TargetURL = "file:///mock_folder_status_target",
            Sources = sources,
            Settings = new ISetting[] { new Setting { Name = "passphrase", Value = "secret", Filter = string.Empty } },
            Filters = Array.Empty<IFilter>(),
            Metadata = metadata,
            OperationType = OperationType.Backup,
        };

        _connection.AddOrUpdateBackupAndSchedule(backup, null);
    }

    [Test]
    public void ListsTheSourcesOfEverySavedBackup()
    {
        var first = MakeSourceFolder("first");
        var second = MakeSourceFolder("second");
        var third = MakeSourceFolder("third");
        SaveBackup("Backup A", first, second);
        SaveBackup("Backup B", third);

        var statuses = _service.GetAllFolderStatuses().ToList();

        Assert.AreEqual(3, statuses.Count, "one entry per source is expected");
        Assert.AreEqual("Backup A", statuses.Single(x => x.Path == first).BackupName);
        Assert.AreEqual("Backup A", statuses.Single(x => x.Path == second).BackupName);
        Assert.AreEqual("Backup B", statuses.Single(x => x.Path == third).BackupName);
        Assert.IsTrue(statuses.All(x => x.Status == FolderBackupStatusValues.Never), "a backup that never ran reports 'never'");
    }

    [Test]
    public void SkipsSpecialSources()
    {
        var folder = MakeSourceFolder("plain");
        SaveBackup("Mixed", folder, "@sourceprovider|file:///not-a-folder");

        var statuses = _service.GetAllFolderStatuses().ToList();

        Assert.AreEqual(1, statuses.Count);
        Assert.AreEqual(folder, statuses[0].Path);
    }

    [Test]
    public void SubfolderInheritsTheStatusOfItsSource()
    {
        var folder = MakeSourceFolder("parent");
        SaveBackup("Parent backup", folder);

        var status = _service.GetFolderStatus(Path.Combine(folder, "child", "grandchild"));

        Assert.AreEqual("Parent backup", status.BackupName);
        Assert.AreEqual(FolderBackupStatusValues.Never, status.Status);
    }

    [Test]
    public void FolderOutsideAnyBackupIsNotInBackup()
    {
        SaveBackup("Some backup", MakeSourceFolder("inside"));

        var status = _service.GetFolderStatus(MakeSourceFolder("outside"));

        Assert.AreEqual(FolderBackupStatusValues.NotInBackup, status.Status);
        Assert.IsNull(status.BackupName);
    }

    [Test]
    public void ReadsTheDatesTheRunnerWrites()
    {
        var success = new DateTime(2026, 9, 1, 10, 30, 0, DateTimeKind.Utc);
        var backedUp = MakeSourceFolder("backedup");
        var failed = MakeSourceFolder("failed");
        var neverSucceeded = MakeSourceFolder("neversucceeded");

        SaveBackup("Backed up", new Dictionary<string, string>
        {
            { "LastBackupDate", Duplicati.Library.Utility.Utility.SerializeDateTime(success) },
            { "LastErrorDate", Duplicati.Library.Utility.Utility.SerializeDateTime(success.AddDays(-1)) },
        }, backedUp);
        SaveBackup("Failed", new Dictionary<string, string>
        {
            { "LastBackupDate", Duplicati.Library.Utility.Utility.SerializeDateTime(success) },
            { "LastErrorDate", Duplicati.Library.Utility.Utility.SerializeDateTime(success.AddDays(1)) },
        }, failed);
        SaveBackup("Never succeeded", new Dictionary<string, string>
        {
            { "LastErrorDate", Duplicati.Library.Utility.Utility.SerializeDateTime(success) },
        }, neverSucceeded);

        // An unchanged run completes without uploading a new version, so the
        // version time stays old while the run time moves past a later error
        var unchanged = MakeSourceFolder("unchanged");
        SaveBackup("Unchanged run", new Dictionary<string, string>
        {
            { "LastBackupDate", Duplicati.Library.Utility.Utility.SerializeDateTime(success) },
            { "LastErrorDate", Duplicati.Library.Utility.Utility.SerializeDateTime(success.AddDays(1)) },
            { "LastBackupFinished", Duplicati.Library.Utility.Utility.SerializeDateTime(success.AddDays(2)) },
        }, unchanged);

        var statuses = _service.GetAllFolderStatuses().ToDictionary(x => x.Path);

        Assert.AreEqual(FolderBackupStatusValues.BackedUp, statuses[unchanged].Status);
        Assert.AreEqual(success.AddDays(2), statuses[unchanged].LastBackupTime);
        Assert.AreEqual(FolderBackupStatusValues.BackedUp, statuses[backedUp].Status);
        Assert.AreEqual(success, statuses[backedUp].LastBackupTime);
        Assert.AreEqual(FolderBackupStatusValues.Failed, statuses[failed].Status);
        Assert.AreEqual(FolderBackupStatusValues.Failed, statuses[neverSucceeded].Status);
        Assert.IsNull(statuses[neverSucceeded].LastBackupTime);
    }

    [Test]
    public void DisabledServiceIsUnavailable()
    {
        SaveBackup("Some backup", MakeSourceFolder("inside"));
        _connection.ApplicationSettings.EnableFolderStatusService = false;

        Assert.Throws<ServiceUnavailableException>(() => _service.GetAllFolderStatuses());
        Assert.Throws<ServiceUnavailableException>(() => _service.GetFolderStatus(_tempDataFolder));
    }
}
