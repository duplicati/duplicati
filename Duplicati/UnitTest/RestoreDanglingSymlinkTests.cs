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
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A symbolic link to a file has no content of its own; it is made from its metadata. The
/// restore still opened whatever was at its path to compare it block by block, which reads
/// through a link that is there. When that link pointed nowhere, the open failed, the file
/// was reported as failing, the restore ended in errors, and the link was not restored.
/// </summary>
public class RestoreDanglingSymlinkTests : BasicSetupHelper
{
    /// <summary>The folder outside the data folder that the link points into</summary>
    private string LinkTarget => this.DATAFOLDER.TrimEnd(Path.DirectorySeparatorChar) + "-outside";

    /// <summary>The file the link points to</summary>
    private string TargetFile => Path.Combine(LinkTarget, "file.txt");

    /// <summary>The link to a file</summary>
    private string FileLink => Path.Combine(this.DATAFOLDER, "flink.txt");

    /// <summary>
    /// Makes a link to a file outside the data folder, and backs up the data folder, which
    /// stores the link as a link
    /// </summary>
    private async Task MakeLinkAndBackupAsync()
    {
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "kept.txt"), "kept");
        if (Directory.Exists(LinkTarget))
            Directory.Delete(LinkTarget, true);
        Directory.CreateDirectory(LinkTarget);
        File.WriteAllText(TargetFile, "target");

        try
        {
            File.CreateSymbolicLink(FileLink, TargetFile);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be made here: {ex.Message}");
        }

        using var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null);
        TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));
    }

    /// <summary>
    /// Restores everything to the original location, and fails on any error or warning
    /// </summary>
    /// <param name="overwrite">The value of --overwrite</param>
    /// <param name="legacy">The value of --restore-legacy</param>
    private async Task RestoreAsync(bool overwrite, bool legacy = false)
    {
        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["overwrite"] = overwrite ? "true" : "false",
            ["restore-legacy"] = legacy ? "true" : "false"
        };
        using var c = new Controller("file://" + this.TARGETFOLDER, options, null);
        TestUtils.AssertResults(await c.RestoreAsync(null));
    }

    /// <summary>
    /// The link is still there, but what it points to is gone, as it is for a link into a
    /// drive that is not mounted. There is nothing to restore but the link itself.
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task ALinkWhoseTargetIsGoneIsRestoredWithoutErrors([Values] bool overwrite)
    {
        await MakeLinkAndBackupAsync();
        File.Delete(TargetFile);

        await RestoreAsync(overwrite);

        Assert.That(new FileInfo(FileLink).LinkTarget, Is.EqualTo(TargetFile));
    }

    /// <summary>
    /// A link that points nowhere is in place of the link, and the link is put back
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task ADanglingLinkInPlaceOfALinkIsReplaced([Values] bool overwrite)
    {
        await MakeLinkAndBackupAsync();
        File.Delete(FileLink);
        File.CreateSymbolicLink(FileLink, Path.Combine(LinkTarget, "nothing-here.txt"));

        await RestoreAsync(overwrite);

        Assert.That(new FileInfo(FileLink).LinkTarget, Is.EqualTo(TargetFile), "the link should be put back");
    }

    /// <summary>
    /// Green before and after: a link to another file that exists is replaced by the link,
    /// and the file it pointed to is not touched
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task ALinkToAnotherFileIsReplacedWithoutTouchingThatFile([Values] bool overwrite)
    {
        await MakeLinkAndBackupAsync();
        var other = Path.Combine(LinkTarget, "other.txt");
        File.WriteAllText(other, "other content");
        File.Delete(FileLink);
        File.CreateSymbolicLink(FileLink, other);

        await RestoreAsync(overwrite);

        Assert.That(new FileInfo(FileLink).LinkTarget, Is.EqualTo(TargetFile), "the link should be put back");
        Assert.That(File.ReadAllText(other), Is.EqualTo("other content"), "the file the other link pointed to should be left alone");
    }

    /// <summary>
    /// The legacy restore, for comparison
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task ALinkWhoseTargetIsGoneIsRestoredWithoutErrorsByTheLegacyRestore()
    {
        await MakeLinkAndBackupAsync();
        File.Delete(TargetFile);

        await RestoreAsync(overwrite: true, legacy: true);

        Assert.That(new FileInfo(FileLink).LinkTarget, Is.EqualTo(TargetFile));
    }
}
