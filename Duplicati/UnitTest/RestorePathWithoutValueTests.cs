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
/// Written without the equals sign, <c>--restore-path D:\target</c> gives the option no value, and
/// the folder becomes one more path to restore. The restore then went to the original location
/// (issue #4818).
/// </summary>
public class RestorePathWithoutValueTests : BasicSetupHelper
{
    [Test]
    [Category("RestoreHandler")]
    public async Task ARestorePathWithoutAValueStopsTheRestore()
    {
        var file = Path.Combine(DATAFOLDER, "f.txt");
        File.WriteAllText(file, "original");

        using (var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(TestOptions), null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        // Changed after the backup, so a restore to the original location would put it back
        File.WriteAllText(file, "changed");

        var args = new[] { "restore", "file://" + TARGETFOLDER, file, "--restore-path", RESTOREFOLDER, "--overwrite=true" }
            .Concat(TestOptions.Select(x => $"--{x.Key}={x.Value}"))
            .ToArray();

        using var output = new StringWriter();
        using var errors = new StringWriter();
        var exitCode = Duplicati.CommandLine.Program.RunCommandLine(output, errors, _ => { }, args);

        Assert.That(exitCode, Is.Not.EqualTo(0), output.ToString() + errors.ToString());
        Assert.That(errors.ToString(), Does.Contain("restore-path"));
        Assert.That(File.ReadAllText(file), Is.EqualTo("changed"), "The file was restored to the original location");
        Assert.That(Directory.EnumerateFileSystemEntries(RESTOREFOLDER).Any(), Is.False);
    }

    [Test]
    [Category("RestoreHandler")]
    public async Task ARestorePathWithAValueStillRestoresThere()
    {
        var file = Path.Combine(DATAFOLDER, "f.txt");
        File.WriteAllText(file, "original");

        using (var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(TestOptions), null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var args = new[] { "restore", "file://" + TARGETFOLDER, file, "--restore-path=" + RESTOREFOLDER }
            .Concat(TestOptions.Select(x => $"--{x.Key}={x.Value}"))
            .ToArray();

        using var output = new StringWriter();
        using var errors = new StringWriter();
        var exitCode = Duplicati.CommandLine.Program.RunCommandLine(output, errors, _ => { }, args);

        Assert.That(exitCode, Is.EqualTo(0), output.ToString() + errors.ToString());
        Assert.That(File.ReadAllText(Path.Combine(RESTOREFOLDER, "f.txt")), Is.EqualTo("original"));
    }
}
