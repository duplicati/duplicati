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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Tests on Windows for resolving every link in a path, which is what a restore path is checked with
/// when the caller is limited to some folders.
/// The expected result of a test is where Windows itself puts a file that is written through the path,
/// so a test fails when the resolved path is not the one Windows follows, whatever the reason.
/// </summary>
[TestFixture]
[Category("IO")]
[Platform("Win")]
public class FinalPathWindowsTests
{
    /// <summary>
    /// The number of links that are followed in a path before it is refused
    /// </summary>
    private const int LinkLimit = 10;

    /// <summary>
    /// The folder the test works in
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
        // The folder of the tests is used and not the temporary folder, which can be given with short names
        // for its folders. Windows reports where a link leads with the long names, which are not the same text.
        _root = Path.Combine(AppContext.BaseDirectory, "final-path-tests", Guid.NewGuid().ToString("N"));
        _target = Path.Combine(_root, "target");
        _outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(_target);
        Directory.CreateDirectory(Path.Combine(_outside, "sub"));
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch (Exception ex)
        {
            TestContext.Out.WriteLine($"Failed to remove {_root}: {ex.Message}");
        }
    }

    /// <summary>
    /// Makes a junction with mklink, which needs no privilege
    /// </summary>
    /// <param name="path">Where to make it</param>
    /// <param name="target">What it points to</param>
    /// <returns><c>true</c> if the junction was made</returns>
    private static bool TryMakeJunction(string path, string target)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{path}\" \"{target}\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0 && new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    /// <summary>
    /// Makes a junction, and fails the test if it cannot be made
    /// </summary>
    /// <param name="path">Where to make it</param>
    /// <param name="target">What it points to</param>
    private static void MakeJunction(string path, string target)
        => Assert.That(TryMakeJunction(path, target), Is.True, $"The test could not make a junction from {path} to {target}");

    /// <summary>
    /// Makes a symbolic link, and ignores the test if the user is not allowed to make one
    /// </summary>
    /// <param name="path">Where to make it</param>
    /// <param name="target">The target, as it is written to the link</param>
    /// <param name="toFolder">True if the link leads to a folder</param>
    private static void MakeSymbolicLink(string path, string target, bool toFolder)
    {
        try
        {
            if (toFolder)
                Directory.CreateSymbolicLink(path, target);
            else
                File.CreateSymbolicLink(path, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be made here: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes a file through a path, and reports which of the possible places Windows put it in
    /// </summary>
    /// <param name="path">The path to write through</param>
    /// <param name="candidates">The places the file can end up in</param>
    /// <returns>The place the file is in</returns>
    private static string WriteAndFind(string path, params string[] candidates)
    {
        foreach (var candidate in candidates)
            Assert.That(File.Exists(candidate), Is.False, $"{candidate} is there before the test wrote it");

        var content = Guid.NewGuid().ToString("N");
        File.WriteAllText(path, content);

        var found = candidates.Where(x => File.Exists(x) && File.ReadAllText(x) == content).ToList();
        Assert.That(found, Has.Count.EqualTo(1), $"A file written to {path} was expected in one of: {string.Join(", ", candidates)}");
        return found[0];
    }

    /// <summary>
    /// Checks that a path is resolved to the place where Windows puts a file that is written through it
    /// </summary>
    /// <param name="path">The path of a file that does not exist</param>
    /// <param name="expected">Where the file is expected</param>
    private static void AssertResolvesTo(string path, string expected)
    {
        Assert.That(Util.ResolveAllLinks(path), Is.EqualTo(expected).IgnoreCase);

        Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
        Assert.That(WriteAndFind(path, expected), Is.EqualTo(expected), "Windows does not follow the path to where the test expects");
    }

    [Test]
    public void PathWithoutLinksIsKept()
    {
        var path = Path.Combine(_target, "missing", "folder", "file.txt");

        Assert.That(Util.ResolveAllLinks(path), Is.EqualTo(path));
    }

    [Test]
    public void JunctionAsLastExistingFolderIsResolved()
    {
        MakeJunction(Path.Combine(_target, "link"), _outside);

        AssertResolvesTo(Path.Combine(_target, "link", "new", "file.txt"), Path.Combine(_outside, "new", "file.txt"));
    }

    /// <summary>
    /// A folder that exists below a junction is not a link itself, so the junction is only found
    /// when every folder in the path is looked at
    /// </summary>
    [Test]
    public void JunctionAboveAnExistingFolderIsResolved()
    {
        MakeJunction(Path.Combine(_target, "link"), _outside);

        AssertResolvesTo(Path.Combine(_target, "link", "sub", "file.txt"), Path.Combine(_outside, "sub", "file.txt"));
    }

    [Test]
    public void JunctionThatStaysInsideTheTargetIsInside()
    {
        Directory.CreateDirectory(Path.Combine(_target, "real", "sub"));
        MakeJunction(Path.Combine(_target, "link"), Path.Combine(_target, "real"));

        AssertResolvesTo(Path.Combine(_target, "link", "sub", "file.txt"), Path.Combine(_target, "real", "sub", "file.txt"));
    }

    [Test]
    public void TrailingSeparatorDoesNotHideAJunction()
    {
        MakeJunction(Path.Combine(_target, "link"), _outside);
        var path = Path.Combine(_target, "link") + Path.DirectorySeparatorChar;

        Assert.That(Util.ResolveAllLinks(path), Is.EqualTo(_outside).IgnoreCase);
    }

    /// <summary>
    /// A junction whose target is gone is followed as far as its text says
    /// </summary>
    [Test]
    public void JunctionWithoutATargetIsFollowed()
    {
        var gone = Path.Combine(_outside, "gone");
        Directory.CreateDirectory(gone);
        MakeJunction(Path.Combine(_target, "dangling"), gone);
        Directory.Delete(gone);
        var path = Path.Combine(_target, "dangling", "file.txt");

        Assert.That(Util.ResolveAllLinks(path), Is.EqualTo(Path.Combine(gone, "file.txt")).IgnoreCase);
    }

    /// <summary>
    /// A junction can be made with the name of a volume in place of a drive letter, which is not a path
    /// </summary>
    [Test]
    public void JunctionToAVolumeNameIsResolved()
    {
        var driveRoot = Path.GetPathRoot(_outside)!;
        using var p = Process.Start(new ProcessStartInfo("mountvol.exe", $"{driveRoot} /L") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        var volume = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        if (p.ExitCode != 0 || !volume.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
            Assert.Ignore($"The name of the volume of {driveRoot} could not be read: {volume}");

        if (!TryMakeJunction(Path.Combine(_target, "link"), volume + _outside.Substring(driveRoot.Length)))
            Assert.Ignore("A junction to the name of a volume cannot be made here");

        AssertResolvesTo(Path.Combine(_target, "link", "sub", "file.txt"), Path.Combine(_outside, "sub", "file.txt"));
    }

    /// <summary>
    /// The check must end on junctions that point at each other. It runs on another thread,
    /// so the test fails and does not hang if following the junctions never ends.
    /// </summary>
    [Test]
    public void JunctionsThatPointAtEachOtherAreRefused()
    {
        var a = Path.Combine(_target, "a");
        var b = Path.Combine(_target, "b");
        Directory.CreateDirectory(b);
        MakeJunction(a, b);
        Directory.Delete(b);
        if (!TryMakeJunction(b, a))
            Assert.Ignore("A junction to a junction without a target cannot be made here");

        var check = Task.Run(() => Assert.That(() => Util.ResolveAllLinks(Path.Combine(a, "file.txt")), Throws.InstanceOf<IOException>()));

        Assert.That(check.Wait(TimeSpan.FromSeconds(30)), Is.True, "Following the junctions did not end");
    }

    /// <summary>
    /// A chain of junctions is followed up to a limit, and a longer chain is refused,
    /// also when Windows would follow it
    /// </summary>
    [Test]
    public void ChainOfJunctionsIsFollowedUpToTheLimit()
    {
        Directory.CreateDirectory(Path.Combine(_target, "real"));

        // Each junction leads to the one before it, and the first to a folder
        for (var i = 1; i <= LinkLimit + 1; i++)
            MakeJunction(Path.Combine(_target, $"link{i}"), Path.Combine(_target, i == 1 ? "real" : $"link{i - 1}"));

        AssertResolvesTo(Path.Combine(_target, $"link{LinkLimit}", "file.txt"), Path.Combine(_target, "real", "file.txt"));

        var overLimit = Path.Combine(_target, $"link{LinkLimit + 1}", "other.txt");
        Assert.That(() => Util.ResolveAllLinks(overLimit), Throws.InstanceOf<IOException>());
    }

    [Test]
    public void RelativeSymbolicLinkAboveAnExistingFolderIsResolved()
    {
        MakeSymbolicLink(Path.Combine(_target, "link"), Path.Combine("..", "outside"), true);

        AssertResolvesTo(Path.Combine(_target, "link", "sub", "file.txt"), Path.Combine(_outside, "sub", "file.txt"));
    }

    /// <summary>
    /// A link to a file that is not there is where Windows makes the file when the link is written to
    /// </summary>
    [Test]
    public void SymbolicLinkToAMissingFileIsResolved()
    {
        MakeSymbolicLink(Path.Combine(_target, "link.txt"), Path.Combine(_outside, "made.txt"), false);

        AssertResolvesTo(Path.Combine(_target, "link.txt"), Path.Combine(_outside, "made.txt"));
    }

    /// <summary>
    /// A target that starts with a separator and has no drive is relative to the root of the drive,
    /// and not to the folder the link is in
    /// </summary>
    [Test]
    public void SymbolicLinkRelativeToTheDriveRootIsResolved()
    {
        var driveRoot = Path.GetPathRoot(_outside)!;
        var rootRelative = Path.DirectorySeparatorChar + Path.Combine(_outside, "made.txt").Substring(driveRoot.Length);
        MakeSymbolicLink(Path.Combine(_target, "link.txt"), rootRelative, false);

        // The other place is where the file would be if the target was relative to the folder of the link
        var inLinkFolder = Path.GetFullPath(_target + rootRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(inLinkFolder)!);
        var landed = WriteAndFind(Path.Combine(_target, "link.txt"), Path.Combine(_outside, "made.txt"), inLinkFolder);
        File.Delete(landed);

        Assert.That(Util.ResolveAllLinks(Path.Combine(_target, "link.txt")), Is.EqualTo(landed).IgnoreCase);
    }

    /// <summary>
    /// A link to a missing file, whose target goes into a junction and up again. Where that leads depends on
    /// whether Windows goes to the parent of the folder the junction leads to, or removes the two segments as text.
    /// </summary>
    [Test]
    public void ParentSegmentAfterAJunctionInATargetIsResolvedAsWindowsDoes()
    {
        MakeJunction(Path.Combine(_target, "junction"), Path.Combine(_outside, "sub"));
        MakeSymbolicLink(Path.Combine(_target, "link.txt"), Path.Combine("junction", "..", "made.txt"), false);

        var landed = WriteAndFind(Path.Combine(_target, "link.txt"), Path.Combine(_target, "made.txt"), Path.Combine(_outside, "made.txt"));
        File.Delete(landed);

        Assert.That(Util.ResolveAllLinks(Path.Combine(_target, "link.txt")), Is.EqualTo(landed).IgnoreCase);
    }
}
