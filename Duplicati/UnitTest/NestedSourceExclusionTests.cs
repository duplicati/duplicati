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
    /// <param name="extraOptions">Options to set on top of the test options, if any</param>
    /// <returns>The file names, without their folders.</returns>
    private async Task<string[]> BackupAndListAsync(string[] sources, IFilter filter, Dictionary<string, string> extraOptions = null)
    {
        var options = new Dictionary<string, string>(this.TestOptions);
        if (extraOptions != null)
            foreach (var kv in extraOptions)
                options[kv.Key] = kv.Value;

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

    /// <summary>The option that excludes hidden files and folders</summary>
    private static Dictionary<string, string> ExcludeHidden
        => new() { ["exclude-files-attributes"] = "hidden" };

    /// <summary>
    /// Makes a folder below the data folder hidden. The name starts with a dot, which is
    /// what hidden means outside Windows, and on Windows the attribute is set as well.
    /// </summary>
    /// <param name="name">The name of the folder, which must start with a dot</param>
    private void HideFolder(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            var folder = new DirectoryInfo(Path.Combine(this.DATAFOLDER, name));
            folder.Attributes |= FileAttributes.Hidden;
        }
    }

    /// <summary>
    /// The walk also stops at a folder holding an ignore marker, which the filter knows
    /// nothing about. CACHEDIR.TAG is the default marker, so this needs no options at all.
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task ASourceInsideAFolderWithAnIgnoreMarkerIsStillBackedUp()
    {
        WriteFile("kept.txt");
        WriteFile("cache", "CACHEDIR.TAG");
        WriteFile("cache", "dropped.txt");
        WriteFile("cache", "wanted", "wanted.txt");

        var files = await BackupAndListAsync([this.DATAFOLDER, Folder("cache", "wanted")], null);

        Assert.That(files, Does.Contain("wanted.txt"), $"got: {string.Join(", ", files)}");
        Assert.That(files, Does.Contain("kept.txt"));
        Assert.That(files, Does.Not.Contain("dropped.txt"), "the marked folder itself should stay excluded");
    }

    /// <summary>
    /// The same with an exclude that has nothing to do with it, which takes the other
    /// branch: the nested source would be turned into an include filter, and that cannot
    /// bring it back either
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task ASourceInsideAFolderWithAnIgnoreMarkerIsStillBackedUpWithAnUnrelatedExclude()
    {
        WriteFile("kept.txt");
        WriteFile("other", "other.txt");
        WriteFile("cache", "CACHEDIR.TAG");
        WriteFile("cache", "dropped.txt");
        WriteFile("cache", "wanted", "wanted.txt");

        var files = await BackupAndListAsync(
            [this.DATAFOLDER, Folder("cache", "wanted")],
            new FilterExpression(Folder("other"), false));

        Assert.That(files, Does.Contain("wanted.txt"), $"got: {string.Join(", ", files)}");
        Assert.That(files, Does.Contain("kept.txt"));
        Assert.That(files, Does.Not.Contain("dropped.txt"));
        Assert.That(files, Does.Not.Contain("other.txt"));
    }

    /// <summary>
    /// The walk also stops at a folder whose attributes are excluded
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task ASourceInsideAFolderWithExcludedAttributesIsStillBackedUp()
    {
        WriteFile("kept.txt");
        WriteFile(".hidden", "dropped.txt");
        WriteFile(".hidden", "wanted", "wanted.txt");
        HideFolder(".hidden");

        var files = await BackupAndListAsync([this.DATAFOLDER, Folder(".hidden", "wanted")], null, ExcludeHidden);

        Assert.That(files, Does.Contain("wanted.txt"), $"got: {string.Join(", ", files)}");
        Assert.That(files, Does.Contain("kept.txt"));
        Assert.That(files, Does.Not.Contain("dropped.txt"), "the hidden folder itself should stay excluded");
    }

    /// <summary>
    /// Unlike an exclude filter, an excluded attribute on the nested source itself is not
    /// overruled by the include filter that replaces it, so the walk stops at the source
    /// itself. Named on its own it is a root, and a root is never excluded.
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task ANestedSourceWithExcludedAttributesIsStillBackedUp()
    {
        WriteFile("kept.txt");
        WriteFile(".hidden", "wanted.txt");
        HideFolder(".hidden");

        var files = await BackupAndListAsync([this.DATAFOLDER, Folder(".hidden")], null, ExcludeHidden);

        Assert.That(files, Does.Contain("wanted.txt"), $"got: {string.Join(", ", files)}");
        Assert.That(files, Does.Contain("kept.txt"));
    }

    /// <summary>
    /// Green before and after: a hidden folder that no source sits inside is still left out
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task AFolderWithExcludedAttributesAndNoSourceInsideStaysExcluded()
    {
        WriteFile("kept.txt");
        WriteFile(".hidden", "dropped.txt");
        HideFolder(".hidden");

        var files = await BackupAndListAsync([this.DATAFOLDER], null, ExcludeHidden);

        Assert.That(files, Does.Contain("kept.txt"));
        Assert.That(files, Does.Not.Contain("dropped.txt"));
    }

    /// <summary>
    /// Marks a folder below the data folder as excluded from backups with an extended
    /// attribute, which is only tried on Linux here
    /// </summary>
    /// <param name="name">The name of the folder</param>
    private void MarkFolderExcludedByXattr(string name)
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("The extended attribute is set with the Linux call");

        var path = Path.Combine(this.DATAFOLDER, name);
        if (Mono.Unix.Native.Syscall.setxattr(path, "user.duplicati.exclude", [1]) != 0)
            Assert.Ignore($"The file system does not take extended attributes: {Mono.Unix.Native.Stdlib.GetLastError()}");
    }

    /// <summary>
    /// The walk also stops at a folder that an extended attribute marks as excluded from
    /// backups, and it checks that after the filter, so the include filter cannot help
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task ASourceInsideAFolderExcludedByAnExtendedAttributeIsStillBackedUp()
    {
        WriteFile("kept.txt");
        WriteFile("x", "dropped.txt");
        WriteFile("x", "wanted", "wanted.txt");
        MarkFolderExcludedByXattr("x");

        var files = await BackupAndListAsync([this.DATAFOLDER, Folder("x", "wanted")], null);

        Assert.That(files, Does.Contain("wanted.txt"), $"got: {string.Join(", ", files)}");
        Assert.That(files, Does.Contain("kept.txt"));
        Assert.That(files, Does.Not.Contain("dropped.txt"), "the marked folder itself should stay excluded");
    }

    /// <summary>
    /// The same when the nested source itself is marked
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task ANestedSourceExcludedByAnExtendedAttributeIsStillBackedUp()
    {
        WriteFile("kept.txt");
        WriteFile("x", "wanted.txt");
        MarkFolderExcludedByXattr("x");

        var files = await BackupAndListAsync([this.DATAFOLDER, Folder("x")], null);

        Assert.That(files, Does.Contain("wanted.txt"), $"got: {string.Join(", ", files)}");
        Assert.That(files, Does.Contain("kept.txt"));
    }

    /// <summary>
    /// Green before and after: when the extended attribute is not honoured, the walk of the
    /// outer source reaches the nested one, so it is still taken out and nothing is walked twice
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task ANestedSourceIsStillCoveredWhenTheExtendedAttributeIsNotHonoured()
    {
        WriteFile("kept.txt");
        WriteFile("x", "other.txt");
        WriteFile("x", "wanted", "wanted.txt");
        MarkFolderExcludedByXattr("x");

        var files = await BackupAndListAsync(
            [this.DATAFOLDER, Folder("x", "wanted")], null,
            new() { ["disable-backup-exclusion-xattr"] = "true" });

        Assert.That(files, Does.Contain("wanted.txt"));
        Assert.That(files, Does.Contain("other.txt"));
        Assert.That(files, Does.Contain("kept.txt"));
    }

    /// <summary>The folder outside the data folder that the link points to</summary>
    private string LinkTarget => this.DATAFOLDER.TrimEnd(Path.DirectorySeparatorChar) + "-outside";

    /// <summary>
    /// Makes <c>link</c> in the data folder a symbolic link to a folder outside it, which
    /// holds <c>other.txt</c> and <c>wanted/wanted.txt</c>
    /// </summary>
    private void MakeLink()
    {
        if (Directory.Exists(LinkTarget))
            Directory.Delete(LinkTarget, true);
        Directory.CreateDirectory(Path.Combine(LinkTarget, "wanted"));
        File.WriteAllText(Path.Combine(LinkTarget, "other.txt"), Contents);
        File.WriteAllText(Path.Combine(LinkTarget, "wanted", "wanted.txt"), Contents);

        try
        {
            Directory.CreateSymbolicLink(Path.Combine(this.DATAFOLDER, "link"), LinkTarget);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be made here: {ex.Message}");
        }
    }

    /// <summary>
    /// A link the walk ignores is left out with everything behind it, and the include filter
    /// cannot help, so a source behind it has to be kept
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task ASourceBehindAnIgnoredSymlinkIsStillBackedUp()
    {
        WriteFile("kept.txt");
        MakeLink();

        var files = await BackupAndListAsync(
            [this.DATAFOLDER, Folder("link", "wanted")], null,
            new() { ["symlink-policy"] = "ignore" });

        Assert.That(files, Does.Contain("wanted.txt"), $"got: {string.Join(", ", files)}");
        Assert.That(files, Does.Contain("kept.txt"));
        Assert.That(files, Does.Not.Contain("other.txt"), "the ignored link itself should stay ignored");
    }

    /// <summary>
    /// The same when the nested source is the ignored link itself. Named on its own, a
    /// link is a root, and a root is followed.
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task AnIgnoredSymlinkNamedAsANestedSourceIsStillBackedUp()
    {
        WriteFile("kept.txt");
        MakeLink();

        var files = await BackupAndListAsync(
            [this.DATAFOLDER, Folder("link")], null,
            new() { ["symlink-policy"] = "ignore" });

        Assert.That(files, Does.Contain("wanted.txt"), $"got: {string.Join(", ", files)}");
        Assert.That(files, Does.Contain("other.txt"));
        Assert.That(files, Does.Contain("kept.txt"));
    }

    /// <summary>
    /// Runs a backup that is expected to warn, and reports the warnings and the names of
    /// the files it recorded
    /// </summary>
    /// <param name="sources">The sources to back up</param>
    /// <returns>The warnings, and the file names without their folders.</returns>
    private async Task<(string[] Warnings, string[] Files)> BackupWithWarningsAndListAsync(string[] sources)
    {
        var options = new Dictionary<string, string>(this.TestOptions);
        string[] warnings;

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var r = await c.BackupAsync(sources);
            Assert.That(r.Errors, Is.Empty, "the backup should not fail");
            warnings = r.Warnings.ToArray();
        }

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var r = await c.ListAsync("*");
            TestUtils.AssertResults(r);
            return (warnings, r.Files
                .Select(x => x.Path)
                .Where(x => !x.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal))
                .Select(Path.GetFileName)
                .ToArray()!);
        }
    }

    /// <summary>
    /// A link the walk stores as a link is not followed, so a source behind it is not
    /// reached. It cannot be kept either, as the backup would then hold the link and a
    /// folder below the same path, which a restore to the original location does not put
    /// back. So it is left out as before, but with a warning.
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task ASourceBehindAStoredSymlinkIsLeftOutWithAWarning()
    {
        WriteFile("kept.txt");
        MakeLink();

        var (warnings, files) = await BackupWithWarningsAndListAsync([this.DATAFOLDER, Folder("link", "wanted")]);

        Assert.That(warnings, Has.Some.Contains("NestedSourceBehindStoredSymlink"),
            $"got: {string.Join(" | ", warnings)}");
        Assert.That(files, Does.Contain("kept.txt"));
        Assert.That(files, Does.Not.Contain("wanted.txt"), "the link should still be stored as a link");
    }

    /// <summary>
    /// The same when the nested source is the stored link itself. Keeping it would record
    /// the link and the folder under the same path, which fails the backup.
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task AStoredSymlinkNamedAsANestedSourceIsLeftOutWithAWarning()
    {
        WriteFile("kept.txt");
        MakeLink();

        var (warnings, files) = await BackupWithWarningsAndListAsync([this.DATAFOLDER, Folder("link")]);

        Assert.That(warnings, Has.Some.Contains("NestedSourceBehindStoredSymlink"),
            $"got: {string.Join(" | ", warnings)}");
        Assert.That(files, Does.Contain("kept.txt"));
        Assert.That(files, Does.Not.Contain("wanted.txt"), "the link should still be stored as a link");
    }

    /// <summary>
    /// Green before and after: a link the walk follows reaches the nested source, which is
    /// still taken out, so nothing is walked twice and nothing is warned about
    /// </summary>
    [Test]
    [Category("Controller")]
    public async Task ASourceBehindAFollowedSymlinkIsStillCovered()
    {
        WriteFile("kept.txt");
        MakeLink();

        var files = await BackupAndListAsync(
            [this.DATAFOLDER, Folder("link", "wanted")], null,
            new() { ["symlink-policy"] = "follow" });

        Assert.That(files, Does.Contain("wanted.txt"));
        Assert.That(files, Does.Contain("other.txt"));
        Assert.That(files, Does.Contain("kept.txt"));
    }
}
