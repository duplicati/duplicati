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
using System.IO;
using Duplicati.Library.Common.IO;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Tests for resolving every link in a path, which is what a restore path is checked with
/// when the caller is limited to some folders
/// </summary>
[TestFixture]
[Category("IO")]
[Platform(Exclude = "Win", Reason = "Creating a symbolic link needs a privilege on Windows")]
public class FinalPathTests
{
    /// <summary>
    /// The folder the test works in, with its own links resolved
    /// </summary>
    private string _root = null!;

    /// <summary>
    /// The folder that stands in for a restore target
    /// </summary>
    private string _target = null!;

    /// <summary>
    /// A folder next to the target, which a restore to the target must not write to
    /// </summary>
    private string _outside = null!;

    [SetUp]
    public void SetUp()
    {
        // The temporary folder is itself reached through a link on some systems
        var root = Path.Combine(Path.GetTempPath(), $"final-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        _root = Util.ResolveAllLinks(root);
        _target = Path.Combine(_root, "target");
        _outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(_target);
        Directory.CreateDirectory(Path.Combine(_outside, "sub"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Test]
    public void PathWithoutLinksIsKept()
    {
        var path = Path.Combine(_target, "missing", "folder", "file.txt");

        Assert.That(Util.ResolveAllLinks(path), Is.EqualTo(path));
    }

    [Test]
    public void LinkAsLastExistingFolderIsResolved()
    {
        Directory.CreateSymbolicLink(Path.Combine(_target, "link"), _outside);
        var path = Path.Combine(_target, "link", "new", "file.txt");

        Assert.That(Util.ResolveAllLinks(path), Is.EqualTo(Path.Combine(_outside, "new", "file.txt")));
    }

    /// <summary>
    /// A folder that exists below a link is not a link itself, so the link is only found
    /// when every folder in the path is looked at
    /// </summary>
    [Test]
    public void LinkAboveAnExistingFolderIsResolved()
    {
        Directory.CreateSymbolicLink(Path.Combine(_target, "link"), Path.Combine("..", "outside"));

        foreach (var path in new[] { Path.Combine(_target, "link", "sub"), Path.Combine(_target, "link", "sub", "file.txt") })
        {
            Assert.That(Util.ResolveAllLinks(path), Is.EqualTo(Path.Combine(_outside, path.Substring(Path.Combine(_target, "link").Length + 1))));
        }
    }

    /// <summary>
    /// The operating system goes to the parent of what a link points to, which is not the folder the link is in
    /// </summary>
    [Test]
    public void ParentSegmentInALinkTargetFollowsTheLinkBeforeIt()
    {
        Directory.CreateSymbolicLink(Path.Combine(_target, "link"), Path.Combine(_outside, "sub"));
        Directory.CreateSymbolicLink(Path.Combine(_target, "parent-link"), Path.Combine("link", "..", "other"));
        var path = Path.Combine(_target, "parent-link", "file.txt");

        Assert.That(Util.ResolveAllLinks(path), Is.EqualTo(Path.Combine(_outside, "other", "file.txt")));
    }

    [Test]
    public void LinkThatStaysInsideTheTargetIsInside()
    {
        Directory.CreateDirectory(Path.Combine(_target, "real", "sub"));
        Directory.CreateSymbolicLink(Path.Combine(_target, "link"), "real");
        var path = Path.Combine(_target, "link", "sub", "file.txt");

        Assert.That(Util.ResolveAllLinks(path), Is.EqualTo(Path.Combine(_target, "real", "sub", "file.txt")));
    }

    /// <summary>
    /// The check a restore makes of each path it writes follows a link that the target itself is
    /// </summary>
    [Test]
    public void TargetThatIsALinkIsInsideItself()
    {
        Directory.CreateSymbolicLink(Path.Combine(_root, "target-link"), _target);

        Assert.That(Util.IsPathInsideTarget(Path.Combine(_target, "file.txt"), Path.Combine(_root, "target-link")), Is.True);
        Assert.That(Util.IsPathInsideTarget(Path.Combine(_root, "target-link", "file.txt"), _target), Is.True);
        Assert.That(Util.IsPathInsideTarget(Path.Combine(_outside, "file.txt"), Path.Combine(_root, "target-link")), Is.False);
    }

    [Test]
    public void LinkWithoutATargetIsFollowed()
    {
        Directory.CreateSymbolicLink(Path.Combine(_target, "dangling"), Path.Combine(_root, "not-there"));
        var path = Path.Combine(_target, "dangling", "file.txt");

        Assert.That(Util.ResolveAllLinks(path), Is.EqualTo(Path.Combine(_root, "not-there", "file.txt")));
    }

    [Test]
    public void LinksThatPointAtEachOtherAreRefused()
    {
        Directory.CreateSymbolicLink(Path.Combine(_target, "a"), "b");
        Directory.CreateSymbolicLink(Path.Combine(_target, "b"), "a");

        Assert.That(() => Util.ResolveAllLinks(Path.Combine(_target, "a", "file.txt")), Throws.InstanceOf<IOException>());
    }

    /// <summary>
    /// A chain of links is followed up to a limit, and a longer chain is refused,
    /// also when the operating system would follow it
    /// </summary>
    [Test]
    public void ChainOfLinksIsFollowedUpToTheLimit()
    {
        const int Limit = 10;
        Directory.CreateDirectory(Path.Combine(_target, "real"));

        // Each link leads to the one before it, and the first to a folder
        for (var i = 1; i <= Limit + 1; i++)
            Directory.CreateSymbolicLink(Path.Combine(_target, $"link{i}"), i == 1 ? "real" : $"link{i - 1}");

        var atLimit = Path.Combine(_target, $"link{Limit}", "file.txt");
        Assert.That(Util.ResolveAllLinks(atLimit), Is.EqualTo(Path.Combine(_target, "real", "file.txt")));

        var overLimit = Path.Combine(_target, $"link{Limit + 1}", "file.txt");
        Assert.That(() => Util.ResolveAllLinks(overLimit), Throws.InstanceOf<IOException>());
    }

    [Test]
    public void TrailingSeparatorDoesNotHideALink()
    {
        Directory.CreateSymbolicLink(Path.Combine(_target, "link"), _outside);
        var path = Path.Combine(_target, "link") + Path.DirectorySeparatorChar;

        Assert.That(Util.ResolveAllLinks(path), Is.EqualTo(_outside));
    }

    /// <summary>
    /// A link can be made unreadable on macOS, and is followed by the operating system all the same,
    /// so it must not be taken for a folder
    /// </summary>
    [Test]
    [Platform("MacOsX")]
    public void LinkThatCannotBeReadIsNotTakenForAFolder()
    {
        var link = Path.Combine(_target, "unreadable");
        Directory.CreateSymbolicLink(link, _outside);
        using (var chmod = System.Diagnostics.Process.Start("chmod", new[] { "-h", "000", link }))
        {
            chmod.WaitForExit();
            Assert.That(chmod.ExitCode, Is.EqualTo(0), "Failed to change the mode of the link");
        }

        // The administrator can read the link, and then it is followed to where it leads
        string? resolved = null;
        try
        {
            resolved = Util.ResolveAllLinks(Path.Combine(link, "file.txt"));
        }
        catch (IOException)
        {
        }

        Assert.That(resolved, Is.Null.Or.EqualTo(Path.Combine(_outside, "file.txt")));
    }
}
