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
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A directory junction was restored as a directory symbolic link (issue #3870). Both are
/// reparse points and both have a target, but they are different things: a junction needs
/// no privilege to make, and it is what Windows itself uses, such as "My Documents" in a
/// user profile.
/// </summary>
public class RestoreJunctionTests : BasicSetupHelper
{
    /// <summary>The reparse tag of a junction</summary>
    private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
    /// <summary>The reparse tag of a symbolic link</summary>
    private const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATA
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileW(string lpFileName, out WIN32_FIND_DATA lpFindFileData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindClose(IntPtr hFindFile);

    /// <summary>
    /// Reads the reparse tag of a path, which tells a junction from a symbolic link
    /// </summary>
    /// <param name="path">The path to read</param>
    /// <returns>The reparse tag, or 0 if the path is not a reparse point.</returns>
    private static uint ReparseTag(string path)
    {
        var handle = FindFirstFileW(path.TrimEnd(Path.DirectorySeparatorChar), out var data);
        if (handle == new IntPtr(-1))
            throw new IOException($"Cannot read {path}: {Marshal.GetLastWin32Error()}");
        FindClose(handle);
        return (data.dwFileAttributes & (uint)FileAttributes.ReparsePoint) != 0 ? data.dwReserved0 : 0;
    }

    /// <summary>The folder outside the data folder that the links point to</summary>
    private string LinkTarget => this.DATAFOLDER.TrimEnd(Path.DirectorySeparatorChar) + "-outside";

    /// <summary>The junction in the data folder</summary>
    private string Junction => Path.Combine(this.DATAFOLDER, "junction");

    /// <summary>The symbolic link to a folder in the data folder</summary>
    private string FolderLink => Path.Combine(this.DATAFOLDER, "link");

    [SetUp]
    public void SkipOutsideWindows()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("Junctions are a Windows thing");
    }

    [TearDown]
    public void RemoveLinks()
    {
        // The recursive delete of the test folders cannot remove a junction
        foreach (var path in new[] { Junction, FolderLink })
            if (new DirectoryInfo(path).LinkTarget != null)
            {
                Icacls($"\"{path}\" /L /remove:d *S-1-1-0");
                new DirectoryInfo(path).Delete();
            }
    }

    /// <summary>
    /// Runs icacls, which can work on a link itself rather than on what it points to
    /// </summary>
    /// <param name="arguments">The arguments</param>
    /// <returns>What it wrote.</returns>
    private static string Icacls(string arguments)
    {
        using var p = Process.Start(new ProcessStartInfo("icacls.exe", arguments) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    /// <summary>
    /// Makes a junction with mklink, which is what a user would do
    /// </summary>
    /// <param name="path">Where to make it</param>
    /// <param name="target">What it points to</param>
    private static void MakeJunction(string path, string target)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{path}\" \"{target}\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.WaitForExit();
        Assert.That(ReparseTag(path), Is.EqualTo(IO_REPARSE_TAG_MOUNT_POINT), "the test could not make a junction");
    }

    /// <summary>
    /// Makes a junction to a folder outside the data folder, and backs up the data folder,
    /// which stores the junction as a link
    /// </summary>
    private async Task MakeJunctionAndBackupAsync()
    {
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "kept.txt"), "kept");
        if (Directory.Exists(LinkTarget))
            Directory.Delete(LinkTarget, true);
        Directory.CreateDirectory(LinkTarget);
        File.WriteAllText(Path.Combine(LinkTarget, "target.txt"), "target");
        MakeJunction(Junction, LinkTarget);

        using var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null);
        TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));
    }

    /// <summary>
    /// Restores everything to the original location, and fails on any error or warning
    /// </summary>
    private async Task RestoreAsync()
    {
        var options = new Dictionary<string, string>(this.TestOptions) { ["overwrite"] = "true" };
        using var c = new Controller("file://" + this.TARGETFOLDER, options, null);
        TestUtils.AssertResults(await c.RestoreAsync(null));
    }

    /// <summary>
    /// The case from the issue: a junction that is gone is restored as a junction
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task AJunctionIsRestoredAsAJunction()
    {
        await MakeJunctionAndBackupAsync();
        new DirectoryInfo(Junction).Delete();

        await RestoreAsync();

        Assert.That(ReparseTag(Junction), Is.EqualTo(IO_REPARSE_TAG_MOUNT_POINT), $"expected a junction, got reparse tag 0x{ReparseTag(Junction):X8}");
        Assert.That(new DirectoryInfo(Junction).LinkTarget, Is.EqualTo(LinkTarget));
        Assert.That(File.ReadAllText(Path.Combine(Junction, "target.txt")), Is.EqualTo("target"), "the junction should lead to its target");
    }

    /// <summary>
    /// A symbolic link to the same place in place of the junction points where it should,
    /// but it is not the junction, so it is replaced
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task ASymbolicLinkInPlaceOfAJunctionIsReplacedByTheJunction()
    {
        await MakeJunctionAndBackupAsync();
        new DirectoryInfo(Junction).Delete();
        try
        {
            Directory.CreateSymbolicLink(Junction, LinkTarget);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be made here: {ex.Message}");
        }

        await RestoreAsync();

        Assert.That(ReparseTag(Junction), Is.EqualTo(IO_REPARSE_TAG_MOUNT_POINT), $"expected a junction, got reparse tag 0x{ReparseTag(Junction):X8}");
    }

    /// <summary>
    /// Green before and after: a symbolic link to a folder is still restored as a symbolic link
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task ASymbolicLinkToAFolderIsStillRestoredAsASymbolicLink()
    {
        Directory.CreateDirectory(LinkTarget);
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "kept.txt"), "kept");
        try
        {
            Directory.CreateSymbolicLink(FolderLink, LinkTarget);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be made here: {ex.Message}");
        }

        using (var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null))
            TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));
        new DirectoryInfo(FolderLink).Delete();

        await RestoreAsync();

        Assert.That(ReparseTag(FolderLink), Is.EqualTo(IO_REPARSE_TAG_SYMLINK), $"expected a symbolic link, got reparse tag 0x{ReparseTag(FolderLink):X8}");
    }

    /// <summary>
    /// The rest of the issue: the junctions in a user profile are hidden, and deny Everyone
    /// listing them. Like the attributes and permissions of any link, those come back with
    /// --restore-symlink-metadata and --restore-permissions, and they are applied to the
    /// junction itself, not to the folder it points to.
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task TheAttributesAndPermissionsOfAJunctionAreRestoredWhenAskedFor()
    {
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "kept.txt"), "kept");
        if (Directory.Exists(LinkTarget))
            Directory.Delete(LinkTarget, true);
        Directory.CreateDirectory(LinkTarget);
        MakeJunction(Junction, LinkTarget);
        new DirectoryInfo(Junction).Attributes |= FileAttributes.Hidden;
        Icacls($"\"{Junction}\" /L /deny *S-1-1-0:(RD)");
        Assert.That(Icacls($"\"{Junction}\" /L"), Does.Contain("(DENY)(RD)"), "the test could not deny listing the junction");

        using (var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null))
            TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));
        Icacls($"\"{Junction}\" /L /remove:d *S-1-1-0");
        new DirectoryInfo(Junction).Delete();

        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["overwrite"] = "true",
            ["restore-symlink-metadata"] = "true",
            ["restore-permissions"] = "true"
        };
        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.RestoreAsync(null));

        Assert.That(ReparseTag(Junction), Is.EqualTo(IO_REPARSE_TAG_MOUNT_POINT), $"expected a junction, got reparse tag 0x{ReparseTag(Junction):X8}");
        Assert.That(File.GetAttributes(Junction).HasFlag(FileAttributes.Hidden), Is.True, "the junction should be hidden again");
        Assert.That(Icacls($"\"{Junction}\" /L"), Does.Contain("(DENY)(RD)"), "the junction should deny listing it again");
        Assert.That(Icacls($"\"{LinkTarget}\""), Does.Not.Contain("(DENY)"), "the folder it points to should be left alone");
    }

    /// <summary>
    /// Green before and after: a junction that is still there, pointing where it should, is
    /// left as a junction
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task AJunctionThatIsStillThereStaysAJunction()
    {
        await MakeJunctionAndBackupAsync();

        await RestoreAsync();

        Assert.That(ReparseTag(Junction), Is.EqualTo(IO_REPARSE_TAG_MOUNT_POINT), $"expected a junction, got reparse tag 0x{ReparseTag(Junction):X8}");
    }

    /// <summary>
    /// A source that is itself a junction is followed, so its files are in the backup below
    /// it, and the junction is recorded as a link at the root. It is recorded as a junction
    /// too: a symbolic link to the same place in its place is replaced by the junction, after
    /// the files are restored through it.
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task ASourceThatIsAJunctionIsRestoredAsAJunction()
    {
        if (Directory.Exists(LinkTarget))
            Directory.Delete(LinkTarget, true);
        Directory.CreateDirectory(Path.Combine(LinkTarget, "sub"));
        File.WriteAllText(Path.Combine(LinkTarget, "a.txt"), "a");
        File.WriteAllText(Path.Combine(LinkTarget, "sub", "b.txt"), "b");
        MakeJunction(Junction, LinkTarget);

        using (var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null))
            TestUtils.AssertResults(await c.BackupAsync([Junction + Path.DirectorySeparatorChar]));

        new DirectoryInfo(Junction).Delete();
        try
        {
            Directory.CreateSymbolicLink(Junction, LinkTarget);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be made here: {ex.Message}");
        }
        File.WriteAllText(Path.Combine(LinkTarget, "a.txt"), "changed");

        await RestoreAsync();

        Assert.That(ReparseTag(Junction), Is.EqualTo(IO_REPARSE_TAG_MOUNT_POINT), $"expected a junction, got reparse tag 0x{ReparseTag(Junction):X8}");
        Assert.That(new DirectoryInfo(Junction).LinkTarget, Is.EqualTo(LinkTarget));
        Assert.That(File.ReadAllText(Path.Combine(Junction, "a.txt")), Is.EqualTo("a"), "the files should be restored through it");
        Assert.That(File.ReadAllText(Path.Combine(Junction, "sub", "b.txt")), Is.EqualTo("b"));
    }
}
