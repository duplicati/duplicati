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
/// Backing up only the files changed after a point in time, or only the files older than a given
/// age (#1925)
/// </summary>
public class BackupModifiedTimeFilterTests : BasicSetupHelper
{
    private string OldFile => Path.Combine(DATAFOLDER, "old.txt");
    private string NewFile => Path.Combine(DATAFOLDER, "new.txt");

    [SetUp]
    public void WriteFiles()
    {
        File.WriteAllText(OldFile, "old");
        File.SetLastWriteTimeUtc(OldFile, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.WriteAllText(NewFile, "new");
        File.SetLastWriteTimeUtc(NewFile, DateTime.UtcNow.AddHours(-1));
    }

    private async Task<string[]> BackUpAsync(Dictionary<string, string> extraOptions)
    {
        var options = new Dictionary<string, string>(TestOptions);
        foreach (var option in extraOptions)
            options[option.Key] = option.Value;

        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            return (await c.ListAsync([Path.Combine(DATAFOLDER, "*")], null)).Files.Select(x => x.Path).ToArray();
    }

    [Test]
    [Category("Targeted")]
    public async Task WithoutTheOptionsAllFilesAreBackedUp()
        => Assert.That(await BackUpAsync([]), Is.SupersetOf(new[] { OldFile, NewFile }));

    [Test]
    [Category("Targeted")]
    public async Task FilesModifiedBeforeADateAreSkipped()
    {
        var paths = await BackUpAsync(new() { ["skip-files-modified-before"] = "2025-01-01" });

        Assert.That(paths, Has.Member(NewFile));
        Assert.That(paths, Has.No.Member(OldFile));
    }

    [Test]
    [Category("Targeted")]
    public async Task FilesModifiedWithinAnIntervalAreSkipped()
    {
        var paths = await BackUpAsync(new() { ["skip-files-modified-after"] = "1W" });

        Assert.That(paths, Has.Member(OldFile));
        Assert.That(paths, Has.No.Member(NewFile));
    }

    /// <summary>
    /// With --changed-files, the files not listed are copied from the previous version. A limit that
    /// moves with every run would keep the files it now skips, and miss the ones it now lets in
    /// </summary>
    [Test]
    [Category("Targeted")]
    [TestCase("1W")]
    [TestCase("now")]
    [TestCase("3600")]
    public async Task ALimitThatMovesIsRefusedWithChangedFiles(string limit)
    {
        Assert.That(await BackUpAsync([]), Has.Member(OldFile));

        var options = new Dictionary<string, string>(TestOptions)
        {
            ["skip-files-modified-after"] = limit,
            ["changed-files"] = NewFile
        };
        using var c = new Controller("file://" + TARGETFOLDER, options, null);
        var ex = Assert.ThrowsAsync<Library.Interface.UserInformationException>(() => c.BackupAsync([DATAFOLDER]));
        Assert.That(ex!.HelpID, Is.EqualTo("MovingTimeLimitWithChangedFiles"));
    }

    [Test]
    [Category("Targeted")]
    public async Task AFixedLimitWorksWithChangedFiles()
    {
        Assert.That(await BackUpAsync([]), Has.Member(OldFile));

        var paths = await BackUpAsync(new() { ["skip-files-modified-before"] = "2025-01-01", ["changed-files"] = NewFile });

        Assert.That(paths, Has.Member(NewFile));
    }

    /// <summary>
    /// A file that is skipped now is not carried over from an earlier version either
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task AFileSkippedLaterLeavesTheNewVersion()
    {
        Assert.That(await BackUpAsync([]), Has.Member(OldFile));

        var paths = await BackUpAsync(new() { ["skip-files-modified-before"] = "1Y" });

        Assert.That(paths, Has.Member(NewFile));
        Assert.That(paths, Has.No.Member(OldFile));
    }
}
