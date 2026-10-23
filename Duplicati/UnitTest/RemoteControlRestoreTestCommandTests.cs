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
using Duplicati.Library.RemoteControl;
using Duplicati.Library.RestAPI.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Services;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests that the console can queue restore tests with the restore test control command,
/// and that the client advertises the capability when it connects.
/// </summary>
[TestFixture]
[Category("RemoteControl")]
public class RemoteControlRestoreTestCommandTests
{
    private string _tempDataFolder = null!;
    private Connection _connection = null!;
    private FakeQueueRunnerService _queue = null!;
    private RemoteControllerHandler _handler = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-rc-restoretest-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDataFolder);

        var databasePath = Path.Combine(_tempDataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);
        var dbConnection = await SQLiteLoader.LoadConnectionAsync(databasePath);
        DatabaseUpgrader.UpgradeDatabase(dbConnection, databasePath, typeof(DatabaseSchemaMarker));
        _connection = new Connection(dbConnection, true, null, _tempDataFolder, () => { });
        _queue = new FakeQueueRunnerService();

        // The http client, token provider and application settings are only used for forwarded commands
        _handler = new RemoteControllerHandler(_connection, null!, null!, null!, _queue);
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

    private string AddBackup(string name, params (string Name, string Value)[] settings)
    {
        var backup = new Backup
        {
            Name = name,
            TargetURL = "file://test",
            Sources = [],
            Settings = settings.Select(s => (ISetting)new Setting { Name = s.Name, Value = s.Value, Filter = string.Empty }).ToArray(),
            Filters = [],
            Tags = []
        };
        _connection.AddOrUpdateBackupAndSchedule(backup, null);
        return backup.ID;
    }

    private async Task<ControlResponseMessage> SendRestoreTestAsync(string? backupIds)
    {
        ControlResponseMessage? response = null;
        var parameters = new Dictionary<string, string?>();
        if (backupIds != null)
            parameters[ControlRequestMessage.BackupIdsKey] = backupIds;

        await _handler.OnControlAsync(new KeepRemoteConnection.ControlMessage(
            new ControlRequestMessage(ControlRequestMessage.RestoreTestCommand, parameters),
            r => { response = r; return true; },
            null));

        Assert.IsNotNull(response, "The control message should be answered");
        return response!;
    }

    private static void AssertSucceeded(ControlResponseMessage response)
        => Assert.IsTrue(response.Success, response.ErrorMessage ?? "The control message should succeed");

    private static string[] QueuedIds(ControlResponseMessage response)
        => (response.Output?.GetValueOrDefault(ControlResponseMessage.QueuedBackupIdsKey) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries);

    [Test]
    public async Task ConnectAdvertisesRestoreTestSupport()
    {
        var metadata = await _handler.OnConnectAsync(new Dictionary<string, string?>());
        Assert.AreEqual("true", metadata["feature:restore-test"]);
    }

    [Test]
    public async Task QueuesRestoreTestForReportedBackupIds()
    {
        var first = AddBackup("first");
        var second = AddBackup("second");

        var response = await SendRestoreTestAsync($"DB-{first}, DB-{second}");

        AssertSucceeded(response);
        CollectionAssert.AreEquivalent(new[] { $"DB-{first}", $"DB-{second}" }, QueuedIds(response));
        CollectionAssert.AreEquivalent(new[] { first, second }, _queue.Tasks.Select(x => x.BackupID));
        Assert.IsTrue(_queue.Tasks.All(x => x.Operation == DuplicatiOperation.RestoreTest));
    }

    [Test]
    public async Task IgnoresUnknownAndForeignBackupIds()
    {
        var existing = AddBackup("existing");

        var response = await SendRestoreTestAsync($"DB-{existing},DB-9999,{existing},cli-calculated-id");

        AssertSucceeded(response);
        CollectionAssert.AreEqual(new[] { $"DB-{existing}" }, QueuedIds(response));
        Assert.AreEqual(1, _queue.Tasks.Count);
    }

    [Test]
    public async Task MatchesABackupByTheIdTheUserConfigured()
    {
        var custom = AddBackup("custom", ("backup-id", "my-own-id"));
        var overridden = AddBackup("overridden", ("--backup-id", "forced-id"));
        AddBackup("default");

        var response = await SendRestoreTestAsync($"my-own-id,forced-id,DB-{custom},DB-{overridden}");

        AssertSucceeded(response);
        CollectionAssert.AreEquivalent(new[] { "my-own-id", "forced-id" }, QueuedIds(response), "A backup is known by the id it reports, not by its database id");
        CollectionAssert.AreEquivalent(new[] { custom, overridden }, _queue.Tasks.Select(x => x.BackupID));
    }

    [Test]
    public async Task DoesNotQueueASecondRestoreTestForTheSameBackup()
    {
        var id = AddBackup("backup");

        await SendRestoreTestAsync($"DB-{id}");
        var response = await SendRestoreTestAsync($"DB-{id}");

        AssertSucceeded(response);
        CollectionAssert.AreEqual(new[] { $"DB-{id}" }, QueuedIds(response), "A pending restore test still counts as queued");
        Assert.AreEqual(1, _queue.Tasks.Count);
    }

    [Test]
    public async Task DoesNotQueueARestoreTestWhileOneIsRunningForTheBackup()
    {
        var id = AddBackup("backup");
        _queue.Running = Runner.CreateTask(DuplicatiOperation.RestoreTest, _connection.GetBackup(id)!);

        var response = await SendRestoreTestAsync($"DB-{id}");

        AssertSucceeded(response);
        CollectionAssert.AreEqual(new[] { $"DB-{id}" }, QueuedIds(response), "A running restore test still counts as queued");
        Assert.IsEmpty(_queue.Tasks);
    }

    [Test]
    public async Task MissingBackupIdsQueueNothing()
    {
        var response = await SendRestoreTestAsync(null);

        AssertSucceeded(response);
        Assert.IsEmpty(QueuedIds(response));
        Assert.IsEmpty(_queue.Tasks);
    }

    [Test]
    public async Task RefusesWhenConsoleControlIsDisabled()
    {
        var id = AddBackup("backup");
        _connection.ApplicationSettings.DisableConsoleControl = true;

        var response = await SendRestoreTestAsync($"DB-{id}");

        Assert.IsFalse(response.Success);
        Assert.IsEmpty(_queue.Tasks);
    }

    /// <summary>
    /// A queue that records the tasks it is given without running them
    /// </summary>
    private sealed class FakeQueueRunnerService : IQueueRunnerService
    {
        public List<IQueuedTask> Tasks { get; } = [];
        /// <summary>
        /// The task that is running, which is no longer among the queued <see cref="Tasks"/>
        /// </summary>
        public IQueuedTask? Running { get; set; }

        public List<IQueuedTask> GetCurrentTasks() => Tasks.ToList();
        public bool GetIsActive() => Running != null;
        public IQueuedTask? GetCurrentTask() => Running;
        public CachedTaskResult? GetCachedTaskResults(long taskID) => null;
        public long AddTask(IQueuedTask task) => AddTask(task, false);
        public long AddTask(IQueuedTask task, bool skipQueue)
        {
            Tasks.Add(task);
            return task.TaskID;
        }
        public void Terminate(bool wait) { }
        public void Resume() { }
        public void Pause() { }
        public IList<Tuple<long, string?>> GetQueueWithIds() => Tasks.Select(x => new Tuple<long, string?>(x.TaskID, x.BackupID)).ToList();
        public void CancelCurrentTaskLockWait(long taskID) { }
        public Task<IBasicResults?> RunImmediatelyAsync(IQueuedTask task) => throw new NotSupportedException();
    }
}
