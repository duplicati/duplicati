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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A restore that could not restore some files carries on with the others and reports the failed
/// files as errors. The command line must then exit with the code for errors, as a backup does, so
/// a script can tell the restore was not complete.
/// </summary>
public class RestoreExitCodeTests : BasicSetupHelper
{
    private async Task BackupTwoFilesAsync()
    {
        File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "a");
        File.WriteAllText(Path.Combine(DATAFOLDER, "b.txt"), "b");
        using var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(TestOptions), null);
        TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
    }

    private int RunRestore(out string output)
    {
        var args = new[] { "restore", "file://" + TARGETFOLDER, "*", "--restore-path=" + RESTOREFOLDER }
            .Concat(TestOptions.Select(x => $"--{x.Key}={x.Value}"))
            .ToArray();

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exitCode = Duplicati.CommandLine.Program.RunCommandLine(stdout, stderr, _ => { }, args);
        output = stdout.ToString() + stderr.ToString();
        return exitCode;
    }

    [Test]
    [Category("RestoreHandler")]
    public async Task ARestoreThatCouldNotRestoreAFileExitsWithTheErrorCodeAsync()
    {
        await BackupTwoFilesAsync();

        // A folder where the file should go, so the file cannot be created
        Directory.CreateDirectory(Path.Combine(RESTOREFOLDER, "a.txt"));

        var exitCode = RunRestore(out var output);

        Assert.That(exitCode, Is.EqualTo(3), output);
        Assert.That(File.ReadAllText(Path.Combine(RESTOREFOLDER, "b.txt")), Is.EqualTo("b"), "The other file was not restored");
    }

    [Test]
    [Category("RestoreHandler")]
    public async Task ARestoreOfEveryFileExitsWithSuccessAsync()
    {
        await BackupTwoFilesAsync();

        var exitCode = RunRestore(out var output);

        Assert.That(exitCode, Is.EqualTo(0), output);
        Assert.That(File.ReadAllText(Path.Combine(RESTOREFOLDER, "a.txt")), Is.EqualTo("a"));
    }
}
