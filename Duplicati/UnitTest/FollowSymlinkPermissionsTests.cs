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
using Duplicati.Library.Main;
using Mono.Unix.Native;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// With --symlink-policy=follow a symbolic link is backed up as the file or folder it points
/// to, but its owner and permissions were read with lstat, from the link itself. It was then
/// restored with mode 0777 and the owner of the link.
/// </summary>
public class FollowSymlinkPermissionsTests : BasicSetupHelper
{
    /// <summary>The mode of the files, which is not the 0777 of a link</summary>
    private const FilePermissions Mode = FilePermissions.S_IRUSR | FilePermissions.S_IWUSR | FilePermissions.S_IRGRP;

    /// <summary>The mode of the folders, which is not the 0777 of a link</summary>
    private const FilePermissions FolderMode = Mode | FilePermissions.S_IXUSR | FilePermissions.S_IXGRP;

    /// <summary>The owner given to the files and folders when the test runs as root</summary>
    private const int OtherId = 1234;

    /// <summary>The folder outside the data folder that holds the file the link points to</summary>
    private string LinkTargetFolder => this.DATAFOLDER.TrimEnd(Path.DirectorySeparatorChar) + "-outside";

    /// <summary>Removes the folder outside the data folder, which the base class does not know about</summary>
    [TearDown]
    public void RemoveLinkTargetFolder()
    {
        if (Directory.Exists(LinkTargetFolder))
            Directory.Delete(LinkTargetFolder, true);
    }

    /// <summary>Reads the status of an entry itself, without following a link</summary>
    private static Stat GetStatus(string path)
    {
        Assert.That(Syscall.lstat(path, out var status), Is.EqualTo(0), $"lstat {path}");
        return status;
    }

    /// <summary>Creates a file with the test mode, and the other owner when running as root</summary>
    private static void CreateFile(string path, bool isRoot)
    {
        File.WriteAllText(path, Path.GetFileName(path));
        Assert.That(Syscall.chmod(path, Mode), Is.EqualTo(0));
        if (isRoot)
            Assert.That(Syscall.chown(path, OtherId, OtherId), Is.EqualTo(0));
    }

    [Test]
    [Category("SymLink")]
    public async Task TheOwnerAndPermissionsOfAFollowedLinkTargetAreRestored()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("The test checks Linux owners and permissions");

        // Only root can give a file another owner; the permissions are checked either way
        var isRoot = Syscall.geteuid() == 0;

        var plain = Path.Combine(this.DATAFOLDER, "plain.txt");
        CreateFile(plain, isRoot);
        Directory.CreateDirectory(LinkTargetFolder);
        var linkTarget = Path.Combine(LinkTargetFolder, "target.txt");
        CreateFile(linkTarget, isRoot);
        File.CreateSymbolicLink(Path.Combine(this.DATAFOLDER, "link.txt"), linkTarget);

        var backupOptions = new Dictionary<string, string>(this.TestOptions) { ["symlink-policy"] = "follow" };
        using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
            TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));

        var restoreOptions = new Dictionary<string, string>(this.TestOptions)
        {
            ["restore-path"] = this.RESTOREFOLDER,
            ["restore-permissions"] = "true"
        };
        using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            TestUtils.AssertResults(await c.RestoreAsync(null));

        foreach (var name in new[] { "plain.txt", "link.txt" })
        {
            var status = GetStatus(Path.Combine(this.RESTOREFOLDER, name));
            Assert.That(status.st_mode & FilePermissions.S_IFMT, Is.EqualTo(FilePermissions.S_IFREG), $"{name} should be restored as a file");
            Assert.That(status.st_mode & FilePermissions.ALLPERMS, Is.EqualTo(Mode), $"{name} should get the permissions of the file");
            if (isRoot)
                Assert.That(status.st_uid, Is.EqualTo((uint)OtherId), $"{name} should get the owner of the file");
        }
    }

    [Test]
    [Category("SymLink")]
    public async Task TheOwnerAndPermissionsOfAFollowedFolderLinkTargetAreRestored()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("The test checks Linux owners and permissions");

        // Only root can give a folder another owner; the permissions are checked either way
        var isRoot = Syscall.geteuid() == 0;

        var plain = Path.Combine(this.DATAFOLDER, "plain");
        var linkTarget = Path.Combine(LinkTargetFolder, "target");
        foreach (var folder in new[] { plain, linkTarget })
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "file.txt"), "file");
            Assert.That(Syscall.chmod(folder, FolderMode), Is.EqualTo(0));
            if (isRoot)
                Assert.That(Syscall.chown(folder, OtherId, OtherId), Is.EqualTo(0));
        }
        File.CreateSymbolicLink(Path.Combine(this.DATAFOLDER, "link"), linkTarget);

        var backupOptions = new Dictionary<string, string>(this.TestOptions) { ["symlink-policy"] = "follow" };
        using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
            TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));

        var restoreOptions = new Dictionary<string, string>(this.TestOptions)
        {
            ["restore-path"] = this.RESTOREFOLDER,
            ["restore-permissions"] = "true"
        };
        using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            TestUtils.AssertResults(await c.RestoreAsync(null));

        foreach (var name in new[] { "plain", "link" })
        {
            var status = GetStatus(Path.Combine(this.RESTOREFOLDER, name));
            Assert.That(status.st_mode & FilePermissions.S_IFMT, Is.EqualTo(FilePermissions.S_IFDIR), $"{name} should be restored as a folder");
            Assert.That(status.st_mode & FilePermissions.ALLPERMS, Is.EqualTo(FolderMode), $"{name} should get the permissions of the folder");
            if (isRoot)
                Assert.That(status.st_uid, Is.EqualTo((uint)OtherId), $"{name} should get the owner of the folder");
        }
    }
}
