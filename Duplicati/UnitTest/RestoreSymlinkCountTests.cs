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
/// A restore of only a symbolic link makes the link, and the restore results should say so.
/// The legacy engine did not count the links it made, and warned that nothing was restored.
/// A link is counted as a file, and also in RestoredSymlinks, which neither engine counted.
/// </summary>
public class RestoreSymlinkCountTests : BasicSetupHelper
{
    /// <summary>The file the link points to</summary>
    private string TargetFile => Path.Combine(this.DATAFOLDER, "target.txt");

    /// <summary>The link to the file</summary>
    private string LinkPath => Path.Combine(this.DATAFOLDER, "link.txt");

    /// <summary>
    /// Makes a file and a link to it, and backs up both
    /// </summary>
    private async Task MakeLinkAndBackupAsync()
    {
        File.WriteAllText(TargetFile, "target");
        try
        {
            File.CreateSymbolicLink(LinkPath, TargetFile);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be made here: {ex.Message}");
        }

        using var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null);
        TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));
    }

    /// <summary>
    /// Restores only the link to its original location
    /// </summary>
    /// <param name="legacy">The value of --restore-legacy</param>
    /// <returns>The results of the restore.</returns>
    private Task<IRestoreResults> RestoreLinkAsync(bool legacy)
        => RestoreAsync(legacy, LinkPath);

    /// <summary>
    /// Restores the given paths to their original location
    /// </summary>
    /// <param name="legacy">The value of --restore-legacy</param>
    /// <param name="paths">The paths to restore</param>
    /// <returns>The results of the restore.</returns>
    private async Task<IRestoreResults> RestoreAsync(bool legacy, params string[] paths)
    {
        var options = new Dictionary<string, string>(this.TestOptions) { ["restore-legacy"] = legacy ? "true" : "false" };
        using var c = new Controller("file://" + this.TARGETFOLDER, options, null);
        var r = await c.RestoreAsync(paths);
        Assert.That(r.Errors, Is.Empty, "the restore should not fail");
        return r;
    }

    /// <summary>
    /// The link is missing, so the restore makes it and counts it as restored
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task ARestoredLinkIsCounted([Values] bool legacy)
    {
        await MakeLinkAndBackupAsync();
        File.Delete(LinkPath);

        var r = await RestoreLinkAsync(legacy);

        Assert.That(new FileInfo(LinkPath).LinkTarget, Is.EqualTo(TargetFile), "the link should have been restored");
        Assert.That(r.Warnings, Has.None.Contains("NoFilesOrFoldersRestored"), $"got: {string.Join(" | ", r.Warnings)}");
        Assert.That(r.RestoredFiles, Is.EqualTo(1), "the link should be counted as restored");
        Assert.That(r.RestoredSymlinks, Is.EqualTo(1), "the link should be counted as a restored link");
    }

    /// <summary>
    /// The file and the link are both missing, so the restore makes both, and only the link is a restored link
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task OnlyTheLinkIsCountedAsALink([Values] bool legacy)
    {
        await MakeLinkAndBackupAsync();
        File.Delete(LinkPath);
        File.Delete(TargetFile);

        var r = await RestoreAsync(legacy, LinkPath, TargetFile);

        Assert.That(File.ReadAllText(TargetFile), Is.EqualTo("target"), "the file should have been restored");
        Assert.That(new FileInfo(LinkPath).LinkTarget, Is.EqualTo(TargetFile), "the link should have been restored");
        Assert.That(r.RestoredFiles, Is.EqualTo(2), "the file and the link should be counted as restored");
        Assert.That(r.RestoredSymlinks, Is.EqualTo(1), "only the link should be counted as a restored link");
    }

    /// <summary>
    /// The link is already in place, so the restore leaves it and counts it as unmodified
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task ALinkInPlaceIsCountedAsUnmodified([Values] bool legacy)
    {
        await MakeLinkAndBackupAsync();

        var r = await RestoreLinkAsync(legacy);

        Assert.That(new FileInfo(LinkPath).LinkTarget, Is.EqualTo(TargetFile), "the link should be kept");
        Assert.That(r.Warnings, Has.None.Contains("NoFilesOrFoldersRestored"), $"got: {string.Join(" | ", r.Warnings)}");
        Assert.That(r.RestoredFiles, Is.EqualTo(0), "the link should not be counted as restored");
        Assert.That(r.UnmodifiedFiles, Is.EqualTo(1), "the link should be counted as unmodified");
        Assert.That(r.RestoredSymlinks, Is.EqualTo(0), "the link should not be counted as a restored link");
    }
}
