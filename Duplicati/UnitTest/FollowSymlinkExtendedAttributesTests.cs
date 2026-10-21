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
using System.Text;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Mono.Unix.Native;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// With --symlink-policy=follow a symbolic link is backed up as the file it points to. The
/// names of its extended attributes were listed with listxattr, which follows the link, but
/// the values were read with lgetxattr, which does not, so every attribute was dropped.
/// </summary>
public class FollowSymlinkExtendedAttributesTests : BasicSetupHelper
{
    /// <summary>The attribute put on the files. user.* needs no root</summary>
    private const string AttributeName = "user.duplicati-follow-xattr";

    /// <summary>The value of the attribute</summary>
    private static readonly byte[] AttributeValue = Encoding.UTF8.GetBytes("on-the-target");

    /// <summary>The folder outside the data folder that holds the file the link points to</summary>
    private string LinkTargetFolder => this.DATAFOLDER.TrimEnd(Path.DirectorySeparatorChar) + "-outside";

    /// <summary>Removes the folder outside the data folder, which the base class does not know about</summary>
    [TearDown]
    public void RemoveLinkTargetFolder()
    {
        if (Directory.Exists(LinkTargetFolder))
            Directory.Delete(LinkTargetFolder, true);
    }

    /// <summary>Reads an attribute of a file</summary>
    /// <returns>The value, or <c>null</c> if the file does not have the attribute.</returns>
    private static byte[]? GetAttribute(string path)
        => Syscall.getxattr(path, AttributeName, out var value) < 0 ? null : value;

    [Test]
    [Category("SymLink")]
    public async Task AnAttributeOfAFollowedLinkTargetIsRestored()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("The attribute is a Linux user.* attribute");

        var plain = Path.Combine(this.DATAFOLDER, "plain.txt");
        File.WriteAllText(plain, "plain");
        if (Syscall.setxattr(plain, AttributeName, AttributeValue) != 0)
            Assert.Ignore($"The file system does not take {AttributeName}: {Stdlib.GetLastError()}");

        Directory.CreateDirectory(LinkTargetFolder);
        var linkTarget = Path.Combine(LinkTargetFolder, "target.txt");
        File.WriteAllText(linkTarget, "target");
        Assert.That(Syscall.setxattr(linkTarget, AttributeName, AttributeValue), Is.EqualTo(0));
        File.CreateSymbolicLink(Path.Combine(this.DATAFOLDER, "link.txt"), linkTarget);

        var backupOptions = new Dictionary<string, string>(this.TestOptions) { ["symlink-policy"] = "follow" };
        using (var c = new Controller("file://" + this.TARGETFOLDER, backupOptions, null))
            TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));

        var restoreOptions = new Dictionary<string, string>(this.TestOptions) { ["restore-path"] = this.RESTOREFOLDER };
        using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            TestUtils.AssertResults(await c.RestoreAsync(null));

        var restoredLink = Path.Combine(this.RESTOREFOLDER, "link.txt");
        Assert.That(new FileInfo(restoredLink).LinkTarget, Is.Null, "the followed link should be restored as a file");
        Assert.That(GetAttribute(Path.Combine(this.RESTOREFOLDER, "plain.txt")), Is.EqualTo(AttributeValue), "the plain file should get its attribute back");
        Assert.That(GetAttribute(restoredLink), Is.EqualTo(AttributeValue), "the followed link should get the attribute of its target");
    }
}
