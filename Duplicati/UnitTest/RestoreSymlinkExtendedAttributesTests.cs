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
/// The extended attributes of a symbolic link are read from the link itself, with
/// llistxattr and lgetxattr. With --restore-symlink-metadata they were written back with
/// setxattr, which follows the link, so they ended up on the file the link points to.
/// </summary>
public class RestoreSymlinkExtendedAttributesTests : BasicSetupHelper
{
    /// <summary>
    /// The attribute put on the link. Linux allows only trusted.* and security.* on a
    /// symbolic link, and trusted.* needs root
    /// </summary>
    private const string AttributeName = "trusted.duplicati-symlink-xattr";

    /// <summary>The value of the attribute</summary>
    private static readonly byte[] AttributeValue = Encoding.UTF8.GetBytes("on-the-link");

    /// <summary>The folder outside the data folder that holds the file the link points to</summary>
    private string LinkTargetFolder => this.DATAFOLDER.TrimEnd(Path.DirectorySeparatorChar) + "-outside";

    /// <summary>The file the link points to</summary>
    private string LinkTarget => Path.Combine(LinkTargetFolder, "target.txt");

    /// <summary>The link</summary>
    private string Link => Path.Combine(this.DATAFOLDER, "link.txt");

    /// <summary>Removes the folder outside the data folder, which the base class does not know about</summary>
    [TearDown]
    public void RemoveLinkTargetFolder()
    {
        if (Directory.Exists(LinkTargetFolder))
            Directory.Delete(LinkTargetFolder, true);
    }

    /// <summary>Reads an attribute of the entry itself, without following a link</summary>
    /// <returns>The value, or <c>null</c> if the entry does not have the attribute.</returns>
    private static byte[]? GetOwnAttribute(string path)
        => Syscall.lgetxattr(path, AttributeName, out var value) < 0 ? null : value;

    [Test]
    [Category("RestoreHandler")]
    public async Task AnAttributeOfALinkIsRestoredOnTheLinkNotItsTarget()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("The attribute is a Linux trusted.* attribute");
        if (Syscall.geteuid() != 0)
            Assert.Ignore("Only root can put a trusted.* attribute on a symbolic link");

        // A backup of nothing but a link warns that no files were processed
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "file.txt"), "file");
        Directory.CreateDirectory(LinkTargetFolder);
        File.WriteAllText(LinkTarget, "target");
        File.CreateSymbolicLink(Link, LinkTarget);
        if (Syscall.lsetxattr(Link, AttributeName, AttributeValue) != 0)
            Assert.Ignore($"The file system does not take {AttributeName} on a symbolic link: {Stdlib.GetLastError()}");

        using (var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null))
            TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));

        File.Delete(Link);
        Assert.That(File.Exists(LinkTarget), Is.True, "removing the link should keep its target");

        var options = new Dictionary<string, string>(this.TestOptions) { ["restore-symlink-metadata"] = "true" };
        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.RestoreAsync(null));

        Assert.That(new FileInfo(Link).LinkTarget, Is.EqualTo(LinkTarget), "the link should be restored");
        Assert.That(GetOwnAttribute(LinkTarget), Is.Null, "the attribute of the link should not be written to the file it points to");
        Assert.That(GetOwnAttribute(Link), Is.EqualTo(AttributeValue), "the link should get its attribute back");
    }
}
