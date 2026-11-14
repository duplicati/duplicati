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
/// Leaving out the files smaller than a given size (#3614)
/// </summary>
public class BackupSkipSmallFilesTests : BasicSetupHelper
{
    private string EmptyFile => Path.Combine(DATAFOLDER, "empty.txt");
    private string SmallFile => Path.Combine(DATAFOLDER, "small.txt");
    private string ExactFile => Path.Combine(DATAFOLDER, "exact.txt");
    private string LargeFile => Path.Combine(DATAFOLDER, "large.txt");

    [SetUp]
    public void WriteFiles()
    {
        File.WriteAllBytes(EmptyFile, []);
        File.WriteAllBytes(SmallFile, new byte[1023]);
        File.WriteAllBytes(ExactFile, new byte[1024]);
        File.WriteAllBytes(LargeFile, new byte[4096]);
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
    public async Task WithoutTheOptionAllFilesAreBackedUp()
        => Assert.That(await BackUpAsync([]), Is.SupersetOf(new[] { EmptyFile, SmallFile, ExactFile, LargeFile }));

    [Test]
    [Category("Targeted")]
    public async Task FilesBelowTheSizeAreSkipped()
    {
        var paths = await BackUpAsync(new() { ["skip-files-smaller-than"] = "1kb" });

        Assert.That(paths, Is.SupersetOf(new[] { ExactFile, LargeFile }));
        Assert.That(paths, Has.No.Member(EmptyFile));
        Assert.That(paths, Has.No.Member(SmallFile));
    }

    /// <summary>
    /// A file that is skipped now is not carried over from an earlier version either
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task AFileSkippedLaterLeavesTheNewVersion()
    {
        Assert.That(await BackUpAsync([]), Has.Member(SmallFile));

        var paths = await BackUpAsync(new() { ["skip-files-smaller-than"] = "1kb" });

        Assert.That(paths, Has.Member(ExactFile));
        Assert.That(paths, Has.No.Member(SmallFile));
    }

    [Test]
    [Category("Targeted")]
    public async Task BothSizeLimitsCanBeCombined()
    {
        var paths = await BackUpAsync(new() { ["skip-files-smaller-than"] = "1kb", ["skip-files-larger-than"] = "2kb" });

        Assert.That(paths, Has.Member(ExactFile));
        Assert.That(paths, Has.No.Member(SmallFile));
        Assert.That(paths, Has.No.Member(LargeFile));
    }
}
