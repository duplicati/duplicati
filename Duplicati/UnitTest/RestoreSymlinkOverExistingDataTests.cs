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
/// A symbolic link is restored by making the link, and whatever is in its place is removed
/// first. When the link was replaced by real data after the backup, a restore to the
/// original location removed that data - a folder with everything in it - with no warning,
/// even without --overwrite, and even when the folder held files that are in no backup.
/// </summary>
public class RestoreSymlinkOverExistingDataTests : BasicSetupHelper
{
    /// <summary>The folder outside the data folder that the links point to</summary>
    private string LinkTarget => this.DATAFOLDER.TrimEnd(Path.DirectorySeparatorChar) + "-outside";

    /// <summary>A second folder outside the data folder, for a link that was repointed</summary>
    private string OtherLinkTarget => this.DATAFOLDER.TrimEnd(Path.DirectorySeparatorChar) + "-outside2";

    /// <summary>Removes the folders outside the data folder, which the base class does not know about</summary>
    [TearDown]
    public void RemoveLinkTargets()
    {
        foreach (var folder in new[] { LinkTarget, OtherLinkTarget })
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
    }

    /// <summary>The link to a folder</summary>
    private string FolderLink => Path.Combine(this.DATAFOLDER, "link");

    /// <summary>The link to a file</summary>
    private string FileLink => Path.Combine(this.DATAFOLDER, "flink.txt");

    /// <summary>
    /// Makes a link to a folder and a link to a file, both outside the data folder, and
    /// backs up the data folder, which stores the links as links
    /// </summary>
    private async Task MakeLinksAndBackupAsync()
    {
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "kept.txt"), "kept");
        if (Directory.Exists(LinkTarget))
            Directory.Delete(LinkTarget, true);
        Directory.CreateDirectory(LinkTarget);
        File.WriteAllText(Path.Combine(LinkTarget, "target.txt"), "target");

        try
        {
            Directory.CreateSymbolicLink(FolderLink, LinkTarget);
            File.CreateSymbolicLink(FileLink, Path.Combine(LinkTarget, "target.txt"));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be made here: {ex.Message}");
        }

        using var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null);
        TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));
    }

    /// <summary>
    /// Restores everything to the original location
    /// </summary>
    /// <param name="overwrite">The value of --overwrite</param>
    /// <returns>The warnings from the restore.</returns>
    private async Task<string[]> RestoreAsync(bool overwrite)
        => (await RestoreWithResultsAsync(overwrite, null)).Warnings.ToArray();

    /// <summary>
    /// Restores everything to the original location, or to the given folder
    /// </summary>
    /// <param name="overwrite">The value of --overwrite</param>
    /// <param name="restorePath">The folder to restore into, or <c>null</c> for the original location</param>
    /// <returns>The results of the restore.</returns>
    private async Task<Library.Interface.IRestoreResults> RestoreWithResultsAsync(bool overwrite, string restorePath)
    {
        var options = new Dictionary<string, string>(this.TestOptions) { ["overwrite"] = overwrite ? "true" : "false" };
        if (restorePath != null)
            options["restore-path"] = restorePath;
        using var c = new Controller("file://" + this.TARGETFOLDER, options, null);
        var r = await c.RestoreAsync(null);
        Assert.That(r.Errors, Is.Empty, "the restore should not fail");
        return r;
    }

    /// <summary>Replaces the link to a folder by a folder with data in it</summary>
    private void ReplaceFolderLinkByData()
    {
        new DirectoryInfo(FolderLink).Delete();
        Directory.CreateDirectory(Path.Combine(FolderLink, "sub"));
        File.WriteAllText(Path.Combine(FolderLink, "mine.txt"), "mine");
        File.WriteAllText(Path.Combine(FolderLink, "sub", "mine2.txt"), "mine2");
    }

    /// <summary>
    /// The case from the report. A folder with data in it is never removed to make a link:
    /// it holds files that are not in the backup, and a restore does not remove those
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task AFolderInPlaceOfALinkIsNotRemovedWithoutOverwrite()
    {
        await MakeLinksAndBackupAsync();
        ReplaceFolderLinkByData();

        var warnings = await RestoreAsync(overwrite: false);

        Assert.That(File.ReadAllText(Path.Combine(FolderLink, "mine.txt")), Is.EqualTo("mine"), "the data in the folder should be kept");
        Assert.That(File.ReadAllText(Path.Combine(FolderLink, "sub", "mine2.txt")), Is.EqualTo("mine2"));
        Assert.That(new DirectoryInfo(FolderLink).LinkTarget, Is.Null, "the folder should not have been replaced by the link");
        Assert.That(warnings, Has.Some.Contains("SymlinkPlaceTakenByFolder"), $"got: {string.Join(" | ", warnings)}");
    }

    /// <summary>
    /// The same with --overwrite, which overwrites files that are in the backup, but does
    /// not remove files that are in no backup
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task AFolderInPlaceOfALinkIsNotRemovedWithOverwrite()
    {
        await MakeLinksAndBackupAsync();
        ReplaceFolderLinkByData();

        var warnings = await RestoreAsync(overwrite: true);

        Assert.That(File.ReadAllText(Path.Combine(FolderLink, "mine.txt")), Is.EqualTo("mine"), "the data in the folder should be kept");
        Assert.That(File.ReadAllText(Path.Combine(FolderLink, "sub", "mine2.txt")), Is.EqualTo("mine2"));
        Assert.That(warnings, Has.Some.Contains("SymlinkPlaceTakenByFolder"), $"got: {string.Join(" | ", warnings)}");
    }

    /// <summary>
    /// A file in place of a link is kept without --overwrite, with a warning. A regular file
    /// that differs would be restored under a new name; a link has nothing to restore beside it
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task AFileInPlaceOfALinkIsKeptWithoutOverwrite()
    {
        await MakeLinksAndBackupAsync();
        File.Delete(FileLink);
        File.WriteAllText(FileLink, "mine");

        var warnings = await RestoreAsync(overwrite: false);

        Assert.That(new FileInfo(FileLink).LinkTarget, Is.Null, "the file should not have been replaced by the link");
        Assert.That(File.ReadAllText(FileLink), Is.EqualTo("mine"));
        Assert.That(warnings, Has.Some.Contains("SymlinkPlaceTakenByFile"), $"got: {string.Join(" | ", warnings)}");
    }

    /// <summary>
    /// Green before and after: with --overwrite, a file in place of a link is overwritten
    /// by the link, as any other file would be by the backed-up one
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task AFileInPlaceOfALinkIsReplacedWithOverwrite()
    {
        await MakeLinksAndBackupAsync();
        File.Delete(FileLink);
        File.WriteAllText(FileLink, "mine");

        await RestoreAsync(overwrite: true);

        Assert.That(new FileInfo(FileLink).LinkTarget, Is.Not.Null, "the link should be restored");
    }

    /// <summary>
    /// Green before and after: an empty folder loses nothing, so the link takes its place
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task AnEmptyFolderInPlaceOfALinkIsReplaced()
    {
        await MakeLinksAndBackupAsync();
        new DirectoryInfo(FolderLink).Delete();
        Directory.CreateDirectory(FolderLink);

        var warnings = await RestoreAsync(overwrite: false);

        Assert.That(new DirectoryInfo(FolderLink).LinkTarget, Is.Not.Null, "the link should be restored");
        Assert.That(warnings, Has.None.Contains("SymlinkPlaceTaken"), $"got: {string.Join(" | ", warnings)}");
    }

    /// <summary>
    /// Green before and after: a link that is still there is made again, and what it points
    /// to is not touched
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task ALinkThatIsStillThereIsRestoredWithoutTouchingItsTarget()
    {
        await MakeLinksAndBackupAsync();

        var warnings = await RestoreAsync(overwrite: true);

        Assert.That(new DirectoryInfo(FolderLink).LinkTarget, Is.Not.Null);
        Assert.That(new FileInfo(FileLink).LinkTarget, Is.Not.Null);
        Assert.That(File.ReadAllText(Path.Combine(LinkTarget, "target.txt")), Is.EqualTo("target"), "what the links point to should be left alone");
        Assert.That(warnings, Has.None.Contains("SymlinkPlaceTaken"), $"got: {string.Join(" | ", warnings)}");
    }

    /// <summary>
    /// Makes a link to a folder outside the data folder, holding <c>a.txt</c> and
    /// <c>sub/b.txt</c>, and backs up the link itself as the source. A source is followed,
    /// so its files are in the backup, below the link.
    /// </summary>
    private async Task MakeLinkSourceAndBackupAsync()
    {
        if (Directory.Exists(LinkTarget))
            Directory.Delete(LinkTarget, true);
        Directory.CreateDirectory(Path.Combine(LinkTarget, "sub"));
        File.WriteAllText(Path.Combine(LinkTarget, "a.txt"), "a");
        File.WriteAllText(Path.Combine(LinkTarget, "sub", "b.txt"), "b");

        try
        {
            Directory.CreateSymbolicLink(FolderLink, LinkTarget);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be made here: {ex.Message}");
        }

        using var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null);
        TestUtils.AssertResults(await c.BackupAsync([FolderLink + Path.DirectorySeparatorChar]));
    }

    /// <summary>
    /// Restoring to a new machine: neither the link nor what it pointed to is there. The
    /// files were restored into a folder at the path of the link, and making the link then
    /// removed that folder with them, leaving a link to nothing. Now the folder is kept, and
    /// as it holds what this restore put there, that is reported as information, not a warning.
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task TheFilesOfASourceThatIsALinkAreKeptWhenRestoredToANewMachine([Values] bool overwrite)
    {
        await MakeLinkSourceAndBackupAsync();
        new DirectoryInfo(FolderLink).Delete();
        Directory.Delete(LinkTarget, true);

        var r = await RestoreWithResultsAsync(overwrite, null);

        Assert.That(File.ReadAllText(Path.Combine(FolderLink, "a.txt")), Is.EqualTo("a"), "the restored files should be kept");
        Assert.That(File.ReadAllText(Path.Combine(FolderLink, "sub", "b.txt")), Is.EqualTo("b"));
        Assert.That(r.Warnings, Is.Empty, $"got: {string.Join(" | ", r.Warnings)}");
        Assert.That(r.Messages, Has.Some.Contains("SymlinkRestoredAsFolder"), $"got: {string.Join(" | ", r.Messages)}");
    }

    /// <summary>
    /// The link was repointed to another folder after the backup, and below it a folder is
    /// restored. The link is replaced last, so the folder's metadata is written while the
    /// link is still there, and the link then points where the backup says.
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task AStaleLinkWithAFolderRestoredBelowItIsReplacedWithoutWarnings()
    {
        await MakeLinkSourceAndBackupAsync();
        Directory.CreateDirectory(Path.Combine(OtherLinkTarget, "sub"));
        File.WriteAllText(Path.Combine(OtherLinkTarget, "a.txt"), "other");
        File.WriteAllText(Path.Combine(OtherLinkTarget, "sub", "b.txt"), "other");
        new DirectoryInfo(FolderLink).Delete();
        Directory.CreateSymbolicLink(FolderLink, OtherLinkTarget);

        var warnings = await RestoreAsync(overwrite: true);

        Assert.That(warnings, Is.Empty, $"got: {string.Join(" | ", warnings)}");
        Assert.That(new DirectoryInfo(FolderLink).LinkTarget, Is.EqualTo(LinkTarget), "the link should point where the backup says");
    }

    /// <summary>
    /// A link with nothing restored below it is made with the files, before the metadata of
    /// its folder is restored. Made after it, the link would set the folder's modification
    /// time to the time of the restore.
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task ALinkDoesNotChangeTheRestoredTimestampOfItsFolder()
    {
        var folder = Path.Combine(this.DATAFOLDER, "dir");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "file.txt"), "file");
        Directory.CreateDirectory(Path.Combine(this.DATAFOLDER, "other"));
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(folder, "link"), Path.Combine("..", "other"));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be made here: {ex.Message}");
        }
        var timestamp = new DateTime(2010, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        Directory.SetLastWriteTimeUtc(folder, timestamp);

        using (var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null))
            TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));

        var r = await RestoreWithResultsAsync(overwrite: false, restorePath: this.RESTOREFOLDER);

        var restoredFolder = Path.Combine(this.RESTOREFOLDER, "dir");
        Assert.That(r.Warnings, Is.Empty, $"got: {string.Join(" | ", r.Warnings)}");
        Assert.That(new DirectoryInfo(Path.Combine(restoredFolder, "link")).LinkTarget, Is.Not.Null, "the link should be restored");
        Assert.That(Directory.GetLastWriteTimeUtc(restoredFolder), Is.EqualTo(timestamp), "the folder should keep the backed-up modification time");
    }

    /// <summary>
    /// Restoring in place with everything still there: the files are restored through the
    /// link. The link was made again at the same time, and the files restored below it
    /// while it was gone failed to get their metadata, with a warning for each.
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task ASourceThatIsALinkIsRestoredInPlaceWithoutWarnings()
    {
        await MakeLinkSourceAndBackupAsync();
        File.WriteAllText(Path.Combine(LinkTarget, "a.txt"), "changed");

        var warnings = await RestoreAsync(overwrite: true);

        Assert.That(warnings, Is.Empty, $"got: {string.Join(" | ", warnings)}");
        Assert.That(new DirectoryInfo(FolderLink).LinkTarget, Is.Not.Null, "the link should still be a link");
        Assert.That(File.ReadAllText(Path.Combine(LinkTarget, "a.txt")), Is.EqualTo("a"), "the file should be restored through the link");
    }
}
