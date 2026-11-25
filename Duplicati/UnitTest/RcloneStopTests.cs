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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Stopping an rclone call killed only the rclone process. A process it had started kept the
/// output pipes open, and the wait for the output to end then never returned, so the job could
/// not be stopped. The stand-in for rclone is a script that starts a long sleep as a child.
/// </summary>
public class RcloneStopTests : BasicSetupHelper
{
    /// <summary>
    /// Writes a script that ignores its arguments and runs a sleep of two minutes as a child process; the test gives up after one
    /// </summary>
    private string WriteStallingRclone()
    {
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(BASEFOLDER, "stalling-rclone.cmd");
            File.WriteAllText(script, "@echo off\r\nping -n 120 127.0.0.1 >nul\r\n");
            return script;
        }
        else
        {
            var script = Path.Combine(BASEFOLDER, "stalling-rclone.sh");
            // The trailing command keeps the shell from replacing itself with the sleep
            File.WriteAllText(script, "#!/bin/sh\nsleep 120\ntrue\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return script;
        }
    }

    [Test]
    [Category("Targeted")]
    public async Task StoppingACallWhoseRcloneStartedAChildEndsItAsync()
    {
        var backend = new Duplicati.Library.Backend.Rclone("rclone://remote/folder", new Dictionary<string, string?> { ["rclone-executable"] = WriteStallingRclone() });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var download = Task.Run(() => backend.GetAsync("file", Path.Combine(BASEFOLDER, "file"), cts.Token));
        var finished = await Task.WhenAny(download, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);

        Assert.That(finished, Is.SameAs(download), "The call was still running 60 seconds after it was stopped");
        Assert.CatchAsync<OperationCanceledException>(() => download);
    }
}
