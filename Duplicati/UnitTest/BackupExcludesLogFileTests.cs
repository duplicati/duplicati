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
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The log file that the backup writes to was backed up as well when it was inside the source,
/// although it changes while the backup runs (#1443)
/// </summary>
public class BackupExcludesLogFileTests : BasicSetupHelper
{
    private async Task<string[]> BackedUpPathsAsync(Dictionary<string, string> options)
    {
        using var c = new Controller("file://" + TARGETFOLDER, options, null);
        var list = await c.ListAsync([Path.Combine(DATAFOLDER, "*")], null);
        return list.Files.Select(x => x.Path).ToArray();
    }

    [Test]
    [Category("Targeted")]
    [TestCase(false)]
    [TestCase(true)]
    public async Task TheLogFileOfTheBackupIsNotBackedUp(bool relativePath)
    {
        File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "data");
        var logfile = Path.Combine(DATAFOLDER, "duplicati.log");

        var options = new Dictionary<string, string>(TestOptions)
        {
            ["log-file"] = relativePath ? Path.GetRelativePath(Environment.CurrentDirectory, logfile) : logfile
        };

        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        Assert.That(File.Exists(logfile), Is.True, "The log file was not written");
        var paths = await BackedUpPathsAsync(options);
        Assert.That(paths, Has.Member(Path.Combine(DATAFOLDER, "file.txt")));
        Assert.That(paths, Has.No.Member(logfile));
    }

    /// <summary>
    /// A file with the same name in another folder is still backed up
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task AnotherFileWithTheSameNameIsBackedUp()
    {
        var other = Path.Combine(DATAFOLDER, "sub", "duplicati.log");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllText(other, "not the log of this backup");

        var options = new Dictionary<string, string>(TestOptions)
        {
            ["log-file"] = Path.Combine(DATAFOLDER, "duplicati.log")
        };

        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        Assert.That(await BackedUpPathsAsync(options), Has.Member(other));
    }
}
