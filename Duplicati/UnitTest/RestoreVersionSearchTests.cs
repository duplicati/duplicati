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
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A restore that is not given a version looks for the newest version with something to restore.
/// Only files with contents were seen as something to restore, so a restore of only links or only
/// folders went through all versions and ended with the oldest one.
/// </summary>
public class RestoreVersionSearchTests : BasicSetupHelper
{
    private string LinkPath => Path.Combine(this.DATAFOLDER, "link.txt");
    private string FolderPath => Path.Combine(this.DATAFOLDER, "folder");

    /// <summary>
    /// Makes a symbolic link, or ignores the test if that is not possible
    /// </summary>
    /// <param name="target">The name of a file next to the link. A link to outside the restore folder is not restored.</param>
    private void MakeLink(string target)
    {
        try
        {
            File.CreateSymbolicLink(LinkPath, target);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be made here: {ex.Message}");
        }
    }

    /// <summary>
    /// Makes a version for each of the changes
    /// </summary>
    private async Task BackupEachAsync(params Action[] changes)
    {
        foreach (var name in new[] { "target.txt", "other.txt" })
            File.WriteAllText(Path.Combine(this.DATAFOLDER, name), name);

        var first = true;
        foreach (var change in changes)
        {
            // The time of a version is in whole seconds
            if (!first)
                await Task.Delay(TimeSpan.FromSeconds(2));
            first = false;

            change();
            using var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null);
            TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));
        }
    }

    /// <summary>
    /// Restores the given paths to the restore folder, without giving a version
    /// </summary>
    private async Task RestoreAsync(bool legacy, params string[] paths)
    {
        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["restore-legacy"] = legacy.ToString(),
            ["restore-path"] = this.RESTOREFOLDER
        };
        using var c = new Controller("file://" + this.TARGETFOLDER, options, null);
        // Restoring only folders warns that no files were restored
        TestUtils.AssertResults(await c.RestoreAsync(paths), "NoFilesRestored");
    }

    /// <summary>
    /// The restored entries, with the target of the links
    /// </summary>
    private string[] Restored
        => new DirectoryInfo(this.RESTOREFOLDER)
            .EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
            .Select(x => x.LinkTarget == null ? x.Name : $"{x.Name} -> {x.LinkTarget}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    [Test]
    [Category("RestoreHandler")]
    public async Task ALinkThatIsOnlyInTheNewestVersionIsRestored([Values] bool legacy)
    {
        await BackupEachAsync(() => { }, () => MakeLink("target.txt"));
        await RestoreAsync(legacy, LinkPath);

        Assert.That(Restored, Is.EqualTo(new[] { "link.txt -> target.txt" }));
    }

    [Test]
    [Category("RestoreHandler")]
    public async Task AChangedLinkIsRestoredFromTheNewestVersion([Values] bool legacy)
    {
        await BackupEachAsync(() => MakeLink("target.txt"), () =>
        {
            File.Delete(LinkPath);
            MakeLink("other.txt");
        });
        await RestoreAsync(legacy, LinkPath);

        Assert.That(Restored, Is.EqualTo(new[] { "link.txt -> other.txt" }));
    }

    [Test]
    [Category("RestoreHandler")]
    public async Task ALinkThatReplacedAFileIsRestored([Values] bool legacy)
    {
        await BackupEachAsync(() => File.WriteAllText(LinkPath, "a file"), () =>
        {
            File.Delete(LinkPath);
            MakeLink("target.txt");
        });
        await RestoreAsync(legacy, LinkPath);

        Assert.That(Restored, Is.EqualTo(new[] { "link.txt -> target.txt" }));
    }

    /// <summary>
    /// An older version is still searched when the newest one does not have the link
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task ARemovedLinkIsRestoredFromTheVersionThatHasIt([Values] bool legacy)
    {
        await BackupEachAsync(() => { }, () => MakeLink("target.txt"), () => File.Delete(LinkPath));
        await RestoreAsync(legacy, LinkPath);

        Assert.That(Restored, Is.EqualTo(new[] { "link.txt -> target.txt" }));
    }

    [Test]
    [Category("RestoreHandler")]
    public async Task AFolderThatIsOnlyInTheNewestVersionIsRestored([Values] bool legacy)
    {
        await BackupEachAsync(() => { }, () => { }, () => Directory.CreateDirectory(FolderPath));
        await RestoreAsync(legacy, FolderPath + Path.DirectorySeparatorChar);

        Assert.That(Restored, Is.EqualTo(new[] { "folder" }));
    }

    /// <summary>
    /// An empty folder is not a reason to leave out the files an older version has in it
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task TheFilesRemovedFromAFolderAreRestoredFromAnOlderVersion([Values] bool legacy)
    {
        var file = Path.Combine(FolderPath, "file.txt");
        await BackupEachAsync(() =>
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(file, "contents");
        }, () => File.Delete(file));
        await RestoreAsync(legacy, FolderPath + Path.DirectorySeparatorChar + "*");

        Assert.That(Restored, Does.Contain("file.txt"));
    }
}
