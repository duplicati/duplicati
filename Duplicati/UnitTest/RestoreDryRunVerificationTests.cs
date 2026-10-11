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
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A dry run restore writes no files. The legacy restore still checked the files it would have
/// restored afterwards, so a dry run to an empty folder failed with an error for each file. It
/// also counts the restored files while it checks them, so a dry run, or a restore with
/// --skip-restore-verification, reported none.
/// </summary>
public class RestoreDryRunVerificationTests : BasicSetupHelper
{
    [Test]
    [Category("RestoreHandler")]
    [TestCase(true)]
    [TestCase(false)]
    public async Task DryRunToAnEmptyFolderHasNoErrors(bool legacy)
    {
        Directory.CreateDirectory(Path.Combine(this.DATAFOLDER, "sub"));
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "a.txt"), "data");
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "sub", "b.txt"), "more data");

        using (var c = new Controller("file://" + this.TARGETFOLDER, this.TestOptions, null))
        {
            var r = await c.BackupAsync([this.DATAFOLDER]);
            Assert.That(r.Errors, Is.Empty, "the backup should not fail");
        }

        Directory.CreateDirectory(this.RESTOREFOLDER);
        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["dry-run"] = "true",
            ["restore-legacy"] = legacy ? "true" : "false",
            ["restore-path"] = this.RESTOREFOLDER,
        };

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var r = await c.RestoreAsync(null);
            Assert.That(r.Errors, Is.Empty, "a dry run should not fail");
            Assert.That(r.RestoredFiles, Is.EqualTo(2), "a dry run should report the files it would restore");
            Assert.That(r.SizeOfRestoredFiles, Is.EqualTo("data".Length + "more data".Length), "a dry run should report the size it would restore");
        }

        Assert.That(Directory.GetFileSystemEntries(this.RESTOREFOLDER), Is.Empty, "a dry run should not write anything");
    }

    /// <summary>
    /// Without the check after the restore, the legacy restore counted no restored files
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    [TestCase(true)]
    [TestCase(false)]
    public async Task RestoreWithoutVerificationCountsTheFiles(bool legacy)
    {
        Directory.CreateDirectory(Path.Combine(this.DATAFOLDER, "sub"));
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "a.txt"), "data");
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "sub", "b.txt"), "more data");

        using (var c = new Controller("file://" + this.TARGETFOLDER, this.TestOptions, null))
        {
            var r = await c.BackupAsync([this.DATAFOLDER]);
            Assert.That(r.Errors, Is.Empty, "the backup should not fail");
        }

        Directory.CreateDirectory(this.RESTOREFOLDER);
        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["skip-restore-verification"] = "true",
            ["restore-legacy"] = legacy ? "true" : "false",
            ["restore-path"] = this.RESTOREFOLDER,
        };

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var r = await c.RestoreAsync(null);
            Assert.That(r.Errors, Is.Empty, "the restore should not fail");
            Assert.That(r.RestoredFiles, Is.EqualTo(2), "the restore should report the files it restored");
            Assert.That(r.SizeOfRestoredFiles, Is.EqualTo("data".Length + "more data".Length), "the restore should report the size it restored");
        }

        Assert.That(File.ReadAllText(Path.Combine(this.RESTOREFOLDER, "a.txt")), Is.EqualTo("data"));
        Assert.That(File.ReadAllText(Path.Combine(this.RESTOREFOLDER, "sub", "b.txt")), Is.EqualTo("more data"));
    }
}
