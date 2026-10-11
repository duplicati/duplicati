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
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Interface;
using Duplicati.Library.RestAPI;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Program = Duplicati.Server.Program;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests that the unacknowledged warning and error flags are only saved when they change
/// </summary>
[TestFixture]
public class UnackedFlagSettingTests
{
    private sealed class TestApplicationSettings(string dataFolder) : IApplicationSettings
    {
        public Action? StartOrStopUsageReporter { get; set; }
        public string DataFolder => dataFolder;
        public string Origin { get; set; } = "UnitTest";
        public CancellationToken ApplicationExit => CancellationToken.None;
        public void SignalApplicationExit() { }
        public ISecretProvider? SecretProvider { get; set; }
        public ISecretProvider? DefaultSecretProvider { get; set; }
        public bool SettingsEncryptionKeyProvidedExternally { get; set; }
    }

    /// <summary>
    /// Queue runner with nothing running, needed when the settings are saved
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

    private string dataFolder = string.Empty;
    private Connection connection = null!;
    private NotificationUpdateService notificationUpdateService = null!;
    private int serverSettingsUpdatedCount;
    private SqliteConnection observer = null!;

    [SetUp]
    public void Setup()
    {
        dataFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dataFolder);

        var options = new Dictionary<string, string>
        {
            // Prevent interference from a default secret provider on the test machine
            { "disable-default-secret-provider", "true" }
        };
        connection = Program.GetDatabaseConnection(new TestApplicationSettings(dataFolder), options, true, false);

        notificationUpdateService = new NotificationUpdateService();
        var eventPollNotify = new EventPollNotify();
        eventPollNotify.ServerSettingsUpdate += (_, _) => Interlocked.Increment(ref serverSettingsUpdatedCount);
        connection.SetServiceProvider(new ServiceCollection()
            .AddSingleton<INotificationUpdateService>(notificationUpdateService)
            .AddSingleton(eventPollNotify)
            .AddSingleton<IQueueRunnerService>(new IdleQueueRunnerService())
            .AddSingleton(new LiveControls(connection))
            .BuildServiceProvider());

        // A second connection sees every commit made by the server connection as a new data_version
        observer = SQLiteLoader.LoadConnection(Path.Combine(dataFolder, DataFolderManager.SERVER_DATABASE_FILENAME));
    }

    [TearDown]
    public void TearDown()
    {
        observer?.Dispose();
        connection?.Dispose();
        try
        {
            Directory.Delete(dataFolder, true);
        }
        catch
        {
        }
    }

    private long DataVersion()
    {
        using var cmd = observer.CreateCommand();
        cmd.CommandText = "PRAGMA data_version";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private (long DataVersion, long LastDataUpdateId, int ServerSettingsUpdated) Snapshot()
        => (DataVersion(), notificationUpdateService.LastDataUpdateId, serverSettingsUpdatedCount);

    private void RegisterNotification(NotificationType type)
        => connection.RegisterNotification(type, "title", "message", null, "1", "", null, null, null, (n, a) => n);

    [Test]
    [Category("Server")]
    public void SettingUnackedFlagsToTheSameValueDoesNotSaveOrSignal([Values] bool value)
    {
        connection.ApplicationSettings.UnackedWarning = value;
        connection.ApplicationSettings.UnackedError = value;
        var before = Snapshot();

        connection.ApplicationSettings.UnackedWarning = value;
        connection.ApplicationSettings.UnackedError = value;

        Assert.AreEqual(before, Snapshot());
    }

    [Test]
    [Category("Server")]
    public void ChangingUnackedFlagsSavesAndSignals()
    {
        connection.ApplicationSettings.UnackedWarning = false;
        connection.ApplicationSettings.UnackedError = false;

        foreach (var value in new[] { true, false })
        {
            var before = Snapshot();
            connection.ApplicationSettings.UnackedWarning = value;
            var afterWarning = Snapshot();
            connection.ApplicationSettings.UnackedError = value;
            var afterError = Snapshot();

            Assert.AreNotEqual(before.DataVersion, afterWarning.DataVersion);
            Assert.AreEqual(before.LastDataUpdateId + 1, afterWarning.LastDataUpdateId);
            Assert.AreEqual(before.ServerSettingsUpdated + 1, afterWarning.ServerSettingsUpdated);
            Assert.AreNotEqual(afterWarning.DataVersion, afterError.DataVersion);
            Assert.AreEqual(afterWarning.LastDataUpdateId + 1, afterError.LastDataUpdateId);
            Assert.AreEqual(afterWarning.ServerSettingsUpdated + 1, afterError.ServerSettingsUpdated);

            // The new values are stored in the database, not just in memory
            connection.ApplicationSettings.ReloadSettings();
            Assert.AreEqual(value, connection.ApplicationSettings.UnackedWarning);
            Assert.AreEqual(value, connection.ApplicationSettings.UnackedError);
        }
    }

    [Test]
    [Category("Server")]
    public void RegisteringAndDismissingNotificationsOnlySavesSettingsWhenAFlagChanges()
    {
        RegisterNotification(NotificationType.Warning);
        RegisterNotification(NotificationType.Error);
        var before = Snapshot();

        // Both flags are already set, and stay set while another notification of each type remains
        RegisterNotification(NotificationType.Warning);
        RegisterNotification(NotificationType.Error);
        foreach (var n in connection.GetNotifications().Skip(2))
            connection.DismissNotification(n.ID);

        var after = Snapshot();
        Assert.AreEqual(before.LastDataUpdateId, after.LastDataUpdateId);
        Assert.AreEqual(before.ServerSettingsUpdated, after.ServerSettingsUpdated);

        // Dismissing the last warning clears the flag, which is saved and signalled once
        connection.DismissNotification(connection.GetNotifications().Single(x => x.Type == NotificationType.Warning).ID);
        Assert.AreEqual(after.LastDataUpdateId + 1, notificationUpdateService.LastDataUpdateId);
        Assert.AreEqual(after.ServerSettingsUpdated + 1, serverSettingsUpdatedCount);
        connection.ApplicationSettings.ReloadSettings();
        Assert.IsFalse(connection.ApplicationSettings.UnackedWarning);
        Assert.IsTrue(connection.ApplicationSettings.UnackedError);
    }
}
