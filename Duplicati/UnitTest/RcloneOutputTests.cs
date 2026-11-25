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
using Rclone = Duplicati.Library.Backend.Rclone;

namespace Duplicati.UnitTest;

/// <summary>
/// The output of rclone can still be unread when the process has ended. The backend stopped
/// reading at that point, so a listing was lost, and an upload whose error was lost passed as done.
/// The stand-in for rclone is a script that ends at once and leaves the output to a child.
/// </summary>
public class RcloneOutputTests : BasicSetupHelper
{
    /// <summary>
    /// Writes a script that ends at once, while a child it started writes a line about a second later
    /// </summary>
    /// <param name="line">The line to write</param>
    /// <param name="toError">True to write to the error stream, false for the output stream</param>
    private Rclone CreateBackend(string line, bool toError)
    {
        var redirect = toError ? " 1>&2" : "";
        var lineFile = Path.Combine(BASEFOLDER, "rclone-line.txt");
        File.WriteAllText(lineFile, line + Environment.NewLine);

        string script;
        if (OperatingSystem.IsWindows())
        {
            var child = Path.Combine(BASEFOLDER, "late-rclone-child.cmd");
            File.WriteAllText(child, $"@echo off\r\nping -n 2 127.0.0.1 >nul\r\ntype \"{lineFile}\"{redirect}\r\n");
            script = Path.Combine(BASEFOLDER, "late-rclone.cmd");
            File.WriteAllText(script, $"@echo off\r\nstart \"\" /b cmd /c call \"{child}\"\r\n");
        }
        else
        {
            script = Path.Combine(BASEFOLDER, "late-rclone.sh");
            File.WriteAllText(script, $"#!/bin/sh\n(sleep 1; cat '{lineFile}'{redirect}) &\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return new Rclone("rclone://remote/folder", new Dictionary<string, string?> { ["rclone-executable"] = script });
    }

    [Test]
    [Category("Targeted")]
    public async Task AListingThatArrivesAfterRcloneEndedIsReadAsync()
    {
        var backend = CreateBackend("""[{"Path":"a.zip","Name":"a.zip","Size":3,"ModTime":"2026-01-02T03:04:05Z","IsDir":false}]""", toError: false);

        var names = new List<string>();
        await foreach (var entry in backend.ListAsync(CancellationToken.None))
            names.Add(entry.Name);

        Assert.That(names, Is.EqualTo(new[] { "a.zip" }));
    }

    [Test]
    [Category("Targeted")]
    public void AnErrorThatArrivesAfterRcloneEndedFailsTheUpload()
    {
        var backend = CreateBackend("Failed to copyto: access denied", toError: true);
        var file = Path.Combine(BASEFOLDER, "upload.bin");
        File.WriteAllText(file, "data");

        var ex = Assert.CatchAsync(() => backend.PutAsync("a.zip", file, CancellationToken.None));
        Assert.That(ex?.Message, Does.Contain("access denied"));
    }
}
