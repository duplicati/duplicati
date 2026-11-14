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
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Files such as encrypted containers change without a new timestamp or size, and are only
/// picked up when the timestamp check is disabled for them (#2332)
/// </summary>
public class DisableFiletimeCheckForTests : BasicSetupHelper
{
    private string Container => Path.Combine(DATAFOLDER, "volume.vc");
    private string Document => Path.Combine(DATAFOLDER, "document.txt");

    private static readonly DateTime FixedTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Writes new content of the same length, and puts the old timestamp back, as VeraCrypt does
    /// </summary>
    private static void WriteKeepingTimestamp(string path, string content)
    {
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, FixedTime);
    }

    private async Task<IBackupResults> BackUpAsync(Dictionary<string, string> extraOptions)
    {
        var options = new Dictionary<string, string>(TestOptions);
        foreach (var option in extraOptions)
            options[option.Key] = option.Value;

        using var c = new Controller("file://" + TARGETFOLDER, options, null);
        var result = await c.BackupAsync([DATAFOLDER]);
        TestUtils.AssertResults(result);
        return result;
    }

    private async Task<string> RestoreAsync(string path)
    {
        var options = new Dictionary<string, string>(TestOptions)
        {
            ["restore-path"] = RESTOREFOLDER,
            ["overwrite"] = "true"
        };
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.RestoreAsync([path]));

        return File.ReadAllText(Path.Combine(RESTOREFOLDER, Path.GetFileName(path)));
    }

    private async Task BackUpChangedFilesAsync(Dictionary<string, string> extraOptions)
    {
        WriteKeepingTimestamp(Container, "first");
        WriteKeepingTimestamp(Document, "first");
        await BackUpAsync([]);

        WriteKeepingTimestamp(Container, "after");
        WriteKeepingTimestamp(Document, "after");
        await BackUpAsync(extraOptions);
    }

    [Test]
    [Category("Targeted")]
    public async Task WithoutTheOptionAnUnchangedTimestampHidesTheChange()
    {
        await BackUpChangedFilesAsync([]);

        Assert.That(await RestoreAsync(Container), Is.EqualTo("first"));
    }

    [Test]
    [Category("Targeted")]
    public async Task MatchingFilesAreScannedForChanges()
    {
        await BackUpChangedFilesAsync(new() { ["disable-filetime-check-for"] = "*.vc" });

        Assert.That(await RestoreAsync(Container), Is.EqualTo("after"));
        Assert.That(await RestoreAsync(Document), Is.EqualTo("first"));
    }

    [Test]
    [Category("Targeted")]
    public async Task SeveralExpressionsCanBeGiven()
    {
        await BackUpChangedFilesAsync(new() { ["disable-filetime-check-for"] = $"*.hc{Path.PathSeparator}*.vc" });

        Assert.That(await RestoreAsync(Container), Is.EqualTo("after"));
    }
}
