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
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Server;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Services;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// A command sent from the web interface's command line page waits in the task queue.
/// When a backup runs first, the version numbers move by one, so a version number has
/// to select the version it named when the command was sent, not the one that has that
/// number when the command finally runs (#4159).
/// </summary>
[TestFixture]
public class CommandlineVersionPinningTests : BasicSetupHelper
{
    /// <summary>
    /// A queue that holds on to the tasks it is given, so the test decides when they run
    /// </summary>
    private sealed class HoldingQueueRunnerService : IQueueRunnerService
    {
        public readonly List<IQueuedTask> Tasks = new();

        public List<IQueuedTask> GetCurrentTasks() => Tasks.ToList();
        public bool GetIsActive() => true;
        public IQueuedTask? GetCurrentTask() => null;
        public CachedTaskResult? GetCachedTaskResults(long taskID) => null;
        public long AddTask(IQueuedTask task) => AddTask(task, false);
        public long AddTask(IQueuedTask task, bool skipQueue)
        {
            Tasks.Add(task);
            return Tasks.Count;
        }
        public void Terminate(bool wait) { }
        public void Resume() { }
        public void Pause() { }
        public IList<Tuple<long, string?>> GetQueueWithIds() => new List<Tuple<long, string?>>();
        public void CancelCurrentTaskLockWait(long taskID) { }
        public Task<IBasicResults?> RunImmediatelyAsync(IQueuedTask task) => throw new NotSupportedException();
    }

    /// <summary>
    /// Runs a task the queue was given, the way the queue runner does for a custom task
    /// </summary>
    private static void RunQueuedTask(IQueuedTask task)
    {
        var run = (Action<IMessageSink>)task.GetType().GetField("Run")!.GetValue(task)!;
        run(new MultiMessageSink());
    }

    private async Task BackupAsync()
    {
        // Fileset times have a resolution of one second, so every backup needs a second of its own
        if (File.Exists(DBFILE))
        {
            var newest = (await FilesetTimesAsync()).Select(x => x.ToUniversalTime()).Max();
            while (DateTime.UtcNow < newest.AddSeconds(2))
                await Task.Delay(100);
        }

        File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), Guid.NewGuid().ToString());
        using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
    }

    private async Task<DateTime[]> FilesetTimesAsync()
    {
        using var c = new Controller("file://" + TARGETFOLDER, TestOptions, null);
        return (await c.ListFilesetsAsync()).Filesets.OrderBy(x => x.Version).Select(x => x.Time).ToArray();
    }

    private string[] CommandLine(params string[] extra)
        => new[] { "delete", "file://" + TARGETFOLDER }
            .Concat(extra)
            .Concat(TestOptions.Select(x => $"--{x.Key}={x.Value}"))
            .Append("--disable-module=console-password-input")
            .ToArray();

    [Test]
    [Category("Commandline")]
    public async Task ADeleteQueuedBehindABackupDeletesTheVersionItNamedAsync()
    {
        for (var i = 0; i < 3; i++)
            await BackupAsync();

        var before = await FilesetTimesAsync();
        Assert.AreEqual(3, before.Length);
        var named = before[1];

        var queue = new HoldingQueueRunnerService();
        var service = new CommandlineRunService(queue, new LogWriteHandler());
        service.StartTask(CommandLine("--version=1"));
        Assert.AreEqual(1, queue.Tasks.Count, "the command should be waiting in the queue");

        // The scheduled backup gets there first
        await BackupAsync();

        RunQueuedTask(queue.Tasks[0]);

        var after = await FilesetTimesAsync();
        Assert.AreEqual(3, after.Length, "exactly one version should have been deleted");
        CollectionAssert.DoesNotContain(after, named, "the version that was number 1 when the command was sent should be the one deleted");
        CollectionAssert.IsSubsetOf(before.Where(x => x != named), after, "every other version that existed when the command was sent should be kept");
    }

    [Test]
    [Category("Commandline")]
    public async Task ARangeQueuedBehindABackupDeletesTheVersionsItNamedAsync()
    {
        for (var i = 0; i < 3; i++)
            await BackupAsync();

        var before = await FilesetTimesAsync();
        var named = before.Take(2).ToArray();

        var queue = new HoldingQueueRunnerService();
        new CommandlineRunService(queue, new LogWriteHandler()).StartTask(CommandLine("--version=0-1"));

        await BackupAsync();
        RunQueuedTask(queue.Tasks[0]);

        var after = await FilesetTimesAsync();
        Assert.AreEqual(2, after.Length, "exactly the two named versions should have been deleted");
        foreach (var time in named)
            CollectionAssert.DoesNotContain(after, time, "a version named by the range should be deleted");
        CollectionAssert.Contains(after, before[2], "the version the range did not name should be kept");
    }

    [Test]
    [Category("Commandline")]
    public async Task ACommandWhoseVersionIsGoneBeforeItRunsDoesNothingAsync()
    {
        for (var i = 0; i < 3; i++)
            await BackupAsync();

        var queue = new HoldingQueueRunnerService();
        new CommandlineRunService(queue, new LogWriteHandler()).StartTask(CommandLine("--version=1"));

        // Something else removes the named version before the command gets its turn
        using (var c = new Controller("file://" + TARGETFOLDER, TestOptions.Expand(new { version = 1 }), null))
            TestUtils.AssertResults(await c.DeleteAsync());
        var before = await FilesetTimesAsync();

        var ex = Assert.Throws<UserInformationException>(() => RunQueuedTask(queue.Tasks[0]));
        Assert.AreEqual("CommandlineVersionNoLongerExists", ex!.HelpID);

        CollectionAssert.AreEqual(before, await FilesetTimesAsync(), "the command should not have run on the versions that took the number instead");
    }

    [Test]
    [Category("Commandline")]
    public async Task ADeleteWithNothingInBetweenDeletesTheVersionItNamedAsync()
    {
        for (var i = 0; i < 3; i++)
            await BackupAsync();

        var before = await FilesetTimesAsync();

        var queue = new HoldingQueueRunnerService();
        new CommandlineRunService(queue, new LogWriteHandler()).StartTask(CommandLine("--version=1"));
        RunQueuedTask(queue.Tasks[0]);

        CollectionAssert.AreEqual(new[] { before[0], before[2] }, await FilesetTimesAsync());
    }
}
