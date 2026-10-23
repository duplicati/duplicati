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
using System.Reflection;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Server;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A stop or abort that arrives after a task has been started, but before its operation is
/// ready to take it, was dropped: the operation then ran to the end although the user had
/// asked it to stop. On the first run after the server starts, this lasted for at least a
/// third of a second.
/// </summary>
[NonParallelizable]
public class EarlyStopRequestTests : BasicSetupHelper
{
    private void WriteSourceFiles()
    {
        var rng = new Random(42);
        var data = new byte[64 * 1024];
        for (var i = 0; i < 20; i++)
        {
            rng.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, $"file{i}"), data);
        }
    }

    [Test]
    [Category("Targeted")]
    public void AnAbortBeforeTheBackupStartsAbortsIt()
    {
        WriteSourceFiles();
        using var c = new Controller("file://" + TARGETFOLDER, TestOptions, null);

        c.AbortAsync().Wait();

        Assert.That(async () => await c.BackupAsync(new[] { DATAFOLDER }), Throws.InstanceOf<OperationCanceledException>(),
            "An abort asked for before the backup started should abort it");
    }

    [Test]
    [Category("Targeted")]
    public async Task AStopBeforeTheBackupStartsStopsIt()
    {
        WriteSourceFiles();
        using var c = new Controller("file://" + TARGETFOLDER, TestOptions, null);

        await c.StopAsync();
        var result = await c.BackupAsync(new[] { DATAFOLDER });

        Assert.That(result.Interrupted, Is.True, "A stop asked for before the backup started should stop it");
    }

    [Test]
    [Category("Targeted")]
    public async Task AnAbortBetweenTwoOperationsIsNotCarriedToTheNext()
    {
        WriteSourceFiles();
        using var c = new Controller("file://" + TARGETFOLDER, TestOptions, null);

        var first = await c.BackupAsync(new[] { DATAFOLDER });
        Assert.That(first.Interrupted, Is.False);

        await c.AbortAsync();
        File.WriteAllText(Path.Combine(DATAFOLDER, "later"), "later");
        var second = await c.BackupAsync(new[] { DATAFOLDER });

        Assert.That(second.Interrupted, Is.False, "An abort asked for while nothing was running should not stop the next operation");
    }

    /// <summary>
    /// Stands in for a controller, and records the stop and abort calls it gets
    /// </summary>
    public class RecordingController : DispatchProxy
    {
        public List<string> Calls { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);
            if (targetMethod.ReturnType == typeof(Task))
                return Task.CompletedTask;
            return targetMethod.ReturnType.IsValueType ? Activator.CreateInstance(targetMethod.ReturnType) : null;
        }

        public static (IController Controller, RecordingController Recorder) Create()
        {
            var proxy = Create<IController, RecordingController>();
            return (proxy, (RecordingController)(object)proxy);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    [Category("Targeted")]
    public async Task AStartedTaskPassesAnEarlyRequestToItsController(bool abort)
    {
        var task = Runner.CreateCustomTask(_ => { });
        task.TaskStarted = DateTime.UtcNow;

        if (abort)
            await task.AbortAsync();
        else
            await task.StopAsync();

        var (controller, recorder) = RecordingController.Create();
        task.SetController(controller);

        Assert.That(recorder.Calls, Does.Contain(abort ? nameof(IController.AbortAsync) : nameof(IController.StopAsync)),
            "The request made before the controller was there should reach it");
    }

    [Test]
    [Category("Targeted")]
    public async Task AQueuedTaskKeepsIgnoringARequest()
    {
        var task = Runner.CreateCustomTask(_ => { });

        await task.AbortAsync();

        var (controller, recorder) = RecordingController.Create();
        task.SetController(controller);

        Assert.That(recorder.Calls, Does.Not.Contain(nameof(IController.AbortAsync)),
            "A request for a task that has not started is left as it was");
    }
}
