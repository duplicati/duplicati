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
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A symbolic link restored with --restore-symlink-metadata and --restore-permissions should get
/// the owner it had, and the file it points to should be left as it is (issue #3848).
/// </summary>
[TestFixture]
public class RestoreSymlinkMetadataTests : BasicSetupHelper
{
    [Test]
    [Category("SymLink")]
    [UnsupportedOSPlatform("windows")]
    public async Task RestoredSymlinkGetsItsOwnerAndLeavesItsTargetAloneAsync(
        [Values(true, false)] bool restoreSymlinkMetadata,
        [Values(true, false)] bool restorePermissions,
        [Values(true, false)] bool legacyRestore)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Assert.Ignore("The owner of a symbolic link is a POSIX concept");

        // As root, a file that belongs to one user and a link to it that belongs to another. The
        // ids are ones no account has, so that the stored owner names do not map them to anything
        // else. Without root only the permissions and the timestamp of the file can be checked.
        var isRoot = Mono.Unix.Native.Syscall.geteuid() == 0;
        long targetUid = isRoot ? 3848001 : Mono.Unix.Native.Syscall.geteuid();
        long targetGid = isRoot ? 3848002 : Mono.Unix.Native.Syscall.getegid();
        long linkUid = isRoot ? 3848003 : targetUid;
        long linkGid = isRoot ? 3848004 : targetGid;

        var target = Path.Combine(DATAFOLDER, "target.txt");
        File.WriteAllText(target, "target");
        var link = Path.Combine(DATAFOLDER, "link");
        File.CreateSymbolicLink(link, target);

        var targetTime = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        new Mono.Unix.UnixFileInfo(target).SetOwner(targetUid, targetGid);
        File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        File.SetLastWriteTimeUtc(target, targetTime);
        new Mono.Unix.UnixSymbolicLinkInfo(link).SetOwner(linkUid, linkGid);

        using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
        {
            var backupResults = await c.BackupAsync(new[] { DATAFOLDER });
            Assert.That(backupResults.Errors.Count(), Is.EqualTo(0));
            Assert.That(backupResults.Warnings.Count(), Is.EqualTo(0));
        }

        var restoreOptions = new Dictionary<string, string>(TestOptions)
        {
            ["restore-path"] = RESTOREFOLDER,
            // The link points at the file in the source folder, outside the restore target
            ["allow-restore-outside-target-directory"] = "true",
            ["restore-symlink-metadata"] = restoreSymlinkMetadata.ToString(),
            ["restore-permissions"] = restorePermissions.ToString(),
            ["restore-legacy"] = legacyRestore.ToString()
        };

        using (var c = new Controller("file://" + TARGETFOLDER, restoreOptions, null))
        {
            var restoreResults = await c.RestoreAsync(null);
            Assert.That(restoreResults.Errors.Count(), Is.EqualTo(0), string.Join("; ", restoreResults.Errors));
            Assert.That(restoreResults.Warnings.Count(), Is.EqualTo(0), string.Join("; ", restoreResults.Warnings));
        }

        var restoredLink = new Mono.Unix.UnixSymbolicLinkInfo(Path.Combine(RESTOREFOLDER, "link"));
        Assert.That(restoredLink.IsSymbolicLink, Is.True, "the restored path is not a symbolic link");
        Assert.That(restoredLink.ContentsPath, Is.EqualTo(target));

        // The file the restored link points to is not part of the restore
        var targetInfo = new Mono.Unix.UnixFileInfo(target);
        Assert.Multiple(() =>
        {
            Assert.That(targetInfo.OwnerUserId, Is.EqualTo(targetUid), "the owner of the link target changed");
            Assert.That(targetInfo.OwnerGroupId, Is.EqualTo(targetGid), "the group of the link target changed");
            Assert.That(File.GetUnixFileMode(target),
                Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead),
                "the permissions of the link target changed");
            Assert.That(File.GetLastWriteTimeUtc(target), Is.EqualTo(targetTime), "the timestamp of the link target changed");
        });

        if (restoreSymlinkMetadata && restorePermissions)
        {
            Assert.Multiple(() =>
            {
                Assert.That(restoredLink.OwnerUserId, Is.EqualTo(linkUid), "the restored link did not get its owner");
                Assert.That(restoredLink.OwnerGroupId, Is.EqualTo(linkGid), "the restored link did not get its group");
            });
        }
    }
}
