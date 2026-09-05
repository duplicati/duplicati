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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Main;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A source that sits inside another source is taken out of the source list and turned
/// into an include filter. When a folder on the way to it is excluded, the walk of the
/// outer source stops at that folder, so the include filter is never reached and the
/// folder the user asked for is quietly absent from the backup. Reported as issue #3220.
/// </summary>
/// <remarks>
/// Every backup here goes through <see cref="TestUtils.AssertResults" />, which fails on
/// any error or warning. That is deliberate: keeping a source that another source can
/// still reach would walk the same tree twice, and the second pass reports a duplicate -
/// as a warning for a file, and as an error for a folder.
/// </remarks>
public class NestedSourceExclusionTests : BasicSetupHelper
{
    /// <summary>Written into every file, so none of them is empty</summary>
    private const string Contents = "some data";

    /// <summary>
    /// Creates a file and the folders above it, and returns the path.
    /// </summary>
    /// <param name="parts">The path of the file, relative to the data folder</param>
    /// <returns>The full path of the file.</returns>
    private string WriteFile(params string[] parts)
    {
        var path = Path.Combine([this.DATAFOLDER, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Contents);
        return path;
    }

    /// <summary>The given folder below the data folder, with a trailing separator</summary>
    /// <param name="parts">The path of the folder, relative to the data folder</param>
    /// <returns>The full path of the folder.</returns>
    private string Folder(params string[] parts)
        => Util.AppendDirSeparator(Path.Combine([this.DATAFOLDER, .. parts]));

    /// <summary>
    /// Runs a backup and reports the names of the files it recorded.
    /// </summary>
    /// <param name="sources">The sources to back up</param>
    /// <param name="filter">The filter to apply</param>
    /// <returns>The file names, without their folders.</returns>
    private async Task<string[]> BackupAndListAsync(string[] sources, IFilter filter)
    {
        var options = new Dictionary<string, string>(this.TestOptions);

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync(sources, filter));

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var r = await c.ListAsync("*");
            TestUtils.AssertResults(r);
            return r.Files
                .Select(x => x.Path)
                .Where(x => !x.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal))
                .Select(Path.GetFileName)
                .ToArray()!;
        }
    }

    /// <summary>
    /// The point of the issue: naming a folder as a source has to keep it in the backup
    /// even when a folder above it is excluded.
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task ASourceInsideAnExcludedFolderIsStillBackedUp()
    {
        WriteFile("kept.txt");
        WriteFile("excluded", "dropped.txt");
        WriteFile("excluded", "wanted", "wanted.txt");

        var files = await BackupAndListAsync(
            [this.DATAFOLDER, Folder("excluded", "wanted")],
            new FilterExpression(Folder("excluded"), false));

        Assert.That(files, Does.Contain("wanted.txt"),
            $"the source inside the excluded folder was dropped; got: {string.Join(", ", files)}");
        Assert.That(files, Does.Contain("kept.txt"), "the rest of the backup should be unaffected");
        Assert.That(files, Does.Not.Contain("dropped.txt"), "the excluded folder itself should stay excluded");
    }

    /// <summary>
    /// The same thing two levels down, so that more than one folder has to be looked at
    /// on the way
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task ASourceSeveralLevelsInsideAnExcludedFolderIsStillBackedUp()
    {
        WriteFile("kept.txt");
        WriteFile("a", "a.txt");
        WriteFile("a", "b", "b.txt");
        WriteFile("a", "b", "c", "c.txt");

        var files = await BackupAndListAsync(
            [this.DATAFOLDER, Folder("a", "b", "c")],
            new FilterExpression(Folder("a", "b"), false));

        Assert.That(files, Does.Contain("c.txt"), $"got: {string.Join(", ", files)}");
        Assert.That(files, Does.Contain("a.txt"), "only the excluded folder should be gone");
        Assert.That(files, Does.Contain("kept.txt"));
        Assert.That(files, Does.Not.Contain("b.txt"), "the excluded folder itself should stay excluded");
    }

    /// <summary>
    /// Two sources under the same excluded folder. Neither contains the other, so each is
    /// judged on its own, and neither may be walked twice.
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task TwoSourcesUnderTheSameExcludedFolderAreBothBackedUp()
    {
        WriteFile("kept.txt");
        WriteFile("excluded", "dropped.txt");
        WriteFile("excluded", "one", "one.txt");
        WriteFile("excluded", "two", "two.txt");

        var files = await BackupAndListAsync(
            [this.DATAFOLDER, Folder("excluded", "one"), Folder("excluded", "two")],
            new FilterExpression(Folder("excluded"), false));

        Assert.That(files, Does.Contain("one.txt"), $"got: {string.Join(", ", files)}");
        Assert.That(files, Does.Contain("two.txt"), $"got: {string.Join(", ", files)}");
        Assert.That(files, Does.Contain("kept.txt"));
        Assert.That(files, Does.Not.Contain("dropped.txt"));
    }

    /// <summary>
    /// Green before and after, and the reason the rule is as narrow as it is: an exclude
    /// that names the nested source itself is overruled by the include filter that
    /// replaces it, so that source is still taken out of the list.
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task AnExcludeOnTheSourceItselfIsStillOverruled()
    {
        WriteFile("kept.txt");
        WriteFile("sub", "sub.txt");

        var files = await BackupAndListAsync(
            [this.DATAFOLDER, Folder("sub")],
            new FilterExpression(Folder("sub"), false));

        Assert.That(files, Does.Contain("sub.txt"), $"got: {string.Join(", ", files)}");
        Assert.That(files, Does.Contain("kept.txt"));
    }

    /// <summary>
    /// Green before and after: with nothing excluded, the nested source adds nothing and
    /// is still taken out, so the tree is not walked twice
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task ANestedSourceWithNoExcludesIsStillCovered()
    {
        WriteFile("kept.txt");
        WriteFile("excluded", "dropped.txt");
        WriteFile("excluded", "wanted", "wanted.txt");

        var files = await BackupAndListAsync([this.DATAFOLDER, Folder("excluded", "wanted")], null);

        Assert.That(files, Does.Contain("wanted.txt"));
        Assert.That(files, Does.Contain("dropped.txt"));
        Assert.That(files, Does.Contain("kept.txt"));
    }

    /// <summary>
    /// Green before and after: an exclude that no source sits inside still excludes
    /// everything below it
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task AnExcludedFolderWithNoSourceInsideStaysExcluded()
    {
        WriteFile("kept.txt");
        WriteFile("excluded", "dropped.txt");
        WriteFile("excluded", "wanted", "wanted.txt");

        var files = await BackupAndListAsync(
            [this.DATAFOLDER],
            new FilterExpression(Folder("excluded"), false));

        Assert.That(files, Does.Contain("kept.txt"));
        Assert.That(files, Does.Not.Contain("dropped.txt"));
        Assert.That(files, Does.Not.Contain("wanted.txt"), "everything below the excluded folder should be gone");
    }
}
