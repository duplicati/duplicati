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
using Duplicati.Library.Interface;
using Duplicati.Library.RestAPI;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Duplicati.WebserverCore.Services;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Program = Duplicati.Server.Program;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests how the server database registers, reads and dismisses notifications
/// </summary>
[TestFixture]
public class NotificationQueryTests
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
    private EventPollNotify eventPollNotify = null!;
    private int notificationsUpdatedCount;

    [SetUp]
    public void Setup()
    {
        dataFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        // Not created here: the server creates the folder and locks it down,
        // whereas a folder that already exists is rejected for its inherited permissions

        var options = new Dictionary<string, string>
        {
            // Prevent interference from a default secret provider on the test machine
            { "disable-default-secret-provider", "true" }
        };
        connection = Program.GetDatabaseConnection(new TestApplicationSettings(dataFolder), options, true, false);

        notificationUpdateService = new NotificationUpdateService();
        eventPollNotify = new EventPollNotify();
        eventPollNotify.NotificationsUpdated += (_, _) => Interlocked.Increment(ref notificationsUpdatedCount);
        connection.SetServiceProvider(new ServiceCollection()
            .AddSingleton<INotificationUpdateService>(notificationUpdateService)
            .AddSingleton(eventPollNotify)
            .AddSingleton<IQueueRunnerService>(new IdleQueueRunnerService())
            .AddSingleton(new LiveControls(connection))
            .BuildServiceProvider());
    }

    [TearDown]
    public void TearDown()
    {
        connection?.Dispose();
        try
        {
            Directory.Delete(dataFolder, true);
        }
        catch
        {
            // If there's an exception, let the OS deal with cleaning up the temp folder.
        }
    }

    private void Register(NotificationType type, string? backupId, string action = "backup:show-log")
        => connection.RegisterNotification(type, $"{type} for {backupId}", "message", null, backupId, action, null, null, null, (n, a) => n);

    [Test]
    [Category("Notifications")]
    public void DismissingTheNotificationsOfABackupSignalsOneUpdate()
    {
        const string backupId = "job-1";
        const string otherBackupId = "job-2";
        for (var i = 0; i < 20; i++)
            Register(i % 2 == 0 ? NotificationType.Warning : NotificationType.Error, backupId);
        Register(NotificationType.Warning, otherBackupId);
        Register(NotificationType.Information, null, "update:new");

        var updateIdBefore = notificationUpdateService.LastNotificationUpdateId;
        var signalsBefore = notificationsUpdatedCount;

        Assert.IsTrue(connection.DismissNotificationsForBackup(backupId));

        var remaining = connection.GetNotifications();
        Assert.That(remaining.Select(x => x.BackupID), Is.EquivalentTo(new string?[] { otherBackupId, null }));
        Assert.AreEqual(1, notificationUpdateService.LastNotificationUpdateId - updateIdBefore, "One notification update for all dismissed notifications");
        Assert.AreEqual(1, notificationsUpdatedCount - signalsBefore, "One notification signal for all dismissed notifications");
        Assert.IsTrue(connection.ApplicationSettings.UnackedWarning, "A warning from another backup remains");
        Assert.IsFalse(connection.ApplicationSettings.UnackedError, "No error remains");
    }

    [Test]
    [Category("Notifications")]
    public void DismissingABackupWithoutNotificationsSignalsNothing()
    {
        Register(NotificationType.Error, "job-2");

        var updateIdBefore = notificationUpdateService.LastNotificationUpdateId;
        var signalsBefore = notificationsUpdatedCount;

        Assert.IsFalse(connection.DismissNotificationsForBackup("job-1"));

        Assert.AreEqual(1, connection.GetNotifications().Length);
        Assert.AreEqual(0, notificationUpdateService.LastNotificationUpdateId - updateIdBefore);
        Assert.AreEqual(0, notificationsUpdatedCount - signalsBefore);
        Assert.IsTrue(connection.ApplicationSettings.UnackedError);
    }

    [Test]
    [Category("Notifications")]
    public void DismissingOneNotificationRecomputesTheUnackedFlags()
    {
        Register(NotificationType.Warning, "job-1");
        Register(NotificationType.Error, "job-1");
        var warning = connection.GetNotifications().Single(x => x.Type == NotificationType.Warning);
        var error = connection.GetNotifications().Single(x => x.Type == NotificationType.Error);

        Assert.IsFalse(connection.DismissNotification(-1), "An unknown ID is not dismissed");

        Assert.IsTrue(connection.DismissNotification(error.ID));
        Assert.IsTrue(connection.ApplicationSettings.UnackedWarning);
        Assert.IsFalse(connection.ApplicationSettings.UnackedError);

        Assert.IsTrue(connection.DismissNotification(warning.ID));
        Assert.IsFalse(connection.ApplicationSettings.UnackedWarning);
        Assert.IsFalse(connection.DismissNotification(warning.ID), "A dismissed notification is gone");
    }

    [Test]
    [Category("Notifications")]
    public void NotificationServiceReadsAndDeletesOneNotificationById()
    {
        Register(NotificationType.Warning, "job-1");
        Register(NotificationType.Error, "job-2");
        var target = connection.GetNotifications().Single(x => x.BackupID == "job-2");
        var service = new NotificationService(connection);

        var dto = service.GetNotification(target.ID);
        Assert.AreEqual(target.ID, dto.ID);
        Assert.AreEqual("job-2", dto.BackupID);
        Assert.AreEqual(target.Title, dto.Title);
        Assert.AreEqual(target.Type, dto.Type);

        service.DeleteNotification(target.ID);
        Assert.That(connection.GetNotifications().Select(x => x.BackupID), Is.EqualTo(new[] { "job-1" }));
        Assert.Throws<NotFoundException>(() => service.GetNotification(target.ID));
        Assert.Throws<NotFoundException>(() => service.DeleteNotification(target.ID));
    }

    [Test]
    [Category("Notifications")]
    public void ConflictHandlerStillSeesTheExistingNotifications()
    {
        INotification Replace(INotification self, IEnumerable<INotification> all)
            => all.FirstOrDefault(x => x.Action == "update:new") ?? self;

        connection.RegisterNotification(NotificationType.Information, "Found update", "1", null, null, "update:new", null, null, null, Replace);
        connection.RegisterNotification(NotificationType.Information, "Found update", "2", null, null, "update:new", null, null, null, Replace);

        var remaining = connection.GetNotifications();
        Assert.AreEqual(1, remaining.Length);
        Assert.AreEqual("2", remaining[0].Message);
    }
}
