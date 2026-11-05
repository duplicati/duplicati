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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A restore of many small files must not keep something alive for every block it requests, or its
/// memory grows with the number of files (#7538):
/// <list type="bullet">
/// <item>Each block request is watched by a delay that warns when the block takes too long. When the
/// block arrives first, the delay must be cancelled, or its timer stays alive for the whole timeout,
/// minutes long.</item>
/// <item>The volume manager reads each request from a channel with the token of the restore. The
/// channel registers a callback on that token and does not remove it when the read completes.</item>
/// </list>
/// </summary>
public class RestoreBlockRequestTimerTests : BasicSetupHelper
{
    private const int FileCount = 2000;

    /// <summary>
    /// Counts the callbacks registered on a token, by walking the registrations of its source
    /// </summary>
    private static int CountRegistrations(CancellationToken token)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var source = typeof(CancellationToken).GetField("_source", flags)!.GetValue(token);
        var registrations = source?.GetType().GetField("_registrations", flags)!.GetValue(source);
        if (registrations == null)
            return 0;

        var node = registrations.GetType().GetField("Callbacks", flags)!.GetValue(registrations);
        var count = 0;
        while (node != null)
        {
            count++;
            node = node.GetType().GetField("Next", flags)!.GetValue(node);
        }
        return count;
    }

    [Test]
    [Category("Targeted")]
    public async Task ARestoreDoesNotKeepSomethingForEveryBlockAsync()
    {
        var options = TestOptions.Expand(new { no_encryption = true });
        var rnd = new Random(1);
        for (var i = 0; i < FileCount; i++)
        {
            var data = new byte[100 + rnd.Next(400)];
            rnd.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, $"f{i}.bin"), data);
        }

        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var before = Timer.ActiveCount;
        Library.Main.RestoreResults? results = null;
        int registrations;
        using (var c = new Controller("file://" + TARGETFOLDER, options.Expand(new { restore_path = RESTOREFOLDER, restore_legacy = false }), null))
        {
            c.OnOperationStarted += r => results = (Library.Main.RestoreResults)r;
            TestUtils.AssertResults(await c.RestoreAsync(["*"]));
            registrations = CountRegistrations(results!.TaskControl.ProgressToken);
        }
        var after = Timer.ActiveCount;

        Assert.That(Directory.GetFiles(RESTOREFOLDER), Has.Length.EqualTo(FileCount), "The files were not restored");
        // Every file has a data block and a metadata block; a few timers of the runtime and the
        // restore itself may still be running
        Assert.That(after - before, Is.LessThan(FileCount / 10), $"{after - before} timers were left running after restoring {FileCount} files");
        Assert.That(registrations, Is.LessThan(FileCount / 10), $"{registrations} callbacks were left registered on the token of the restore after restoring {FileCount} files");
    }
}
