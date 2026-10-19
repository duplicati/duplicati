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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Snapshots;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests for https://github.com/duplicati/duplicati/issues/2171.
/// A symlink made by WSL that Windows cannot express as its own symlink, such as one
/// with an absolute Linux path or a missing target, is stored as a reparse point with
/// the tag IO_REPARSE_TAG_LX_SYMLINK. The tests write that reparse point directly, as
/// WSL would, so they do not need WSL to be installed.
/// </summary>
[TestFixture]
public class Issue2171 : BasicSetupHelper
{
    private const uint IO_REPARSE_TAG_LX_SYMLINK = 0xA000001D;
    private const uint FSCTL_SET_REPARSE_POINT = 0x000900A4;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, byte[] lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    /// <summary>
    /// Makes a WSL symlink at <paramref name="path"/>: a reparse point with the LX symlink
    /// tag, holding a version of 2 and the UTF-8 target, the way WSL writes it.
    /// </summary>
    private static void CreateWslSymlink(string path, string target)
    {
        var targetBytes = Encoding.UTF8.GetBytes(target);
        var buffer = new byte[8 + 4 + targetBytes.Length];
        BitConverter.GetBytes(IO_REPARSE_TAG_LX_SYMLINK).CopyTo(buffer, 0);
        BitConverter.GetBytes((ushort)(4 + targetBytes.Length)).CopyTo(buffer, 4);
        BitConverter.GetBytes(2u).CopyTo(buffer, 8);
        targetBytes.CopyTo(buffer, 12);

        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        if (!DeviceIoControl(fs.SafeFileHandle, FSCTL_SET_REPARSE_POINT, buffer, (uint)buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>
    /// Makes the source folder with a regular file and two WSL symlinks, or ignores the
    /// test where WSL symlinks cannot be made.
    /// </summary>
    private void CreateSource()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("WSL symlinks only exist on Windows");

        File.WriteAllText(Path.Combine(DATAFOLDER, "real.txt"), "hello");
        try
        {
            CreateWslSymlink(Path.Combine(DATAFOLDER, "absolute"), "/usr/bin/python3");
            CreateWslSymlink(Path.Combine(DATAFOLDER, "dangling"), "nonexistent");
        }
        catch (Exception ex)
        {
            Assert.Ignore($"Could not make a WSL symlink: {ex.Message}");
        }
    }

    [Test]
    [Category("SymLink")]
    public void WslSymlinkIsReadAsSymlink()
    {
        CreateSource();

        var link = Path.Combine(DATAFOLDER, "absolute");
        Assert.That(systemIO.GetSymlinkTarget(link), Is.EqualTo("/usr/bin/python3"));
        Assert.That(systemIO.IsSymlink(link), Is.True);
        Assert.That(systemIO.IsSymlink(Path.Combine(DATAFOLDER, "real.txt")), Is.False);
    }

    [Test]
    [Category("SymLink")]
    [TestCase(Options.SymlinkStrategy.Ignore)]
    [TestCase(Options.SymlinkStrategy.Store)]
    public async Task BackupAppliesSymlinkPolicyToWslSymlinks(Options.SymlinkStrategy symlinkPolicy)
    {
        CreateSource();

        var backupOptions = new Dictionary<string, string>(TestOptions) { ["symlink-policy"] = symlinkPolicy.ToString() };
        using (var c = new Controller("file://" + TARGETFOLDER, backupOptions, null))
        {
            var backupResults = await c.BackupAsync([DATAFOLDER]);
            Assert.That(backupResults.Errors, Is.Empty);
            Assert.That(backupResults.Warnings, Is.Empty);
        }

        using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
        {
            var names = (await c.ListAsync("*")).Files.Select(f => Path.GetFileName(f.Path)).ToList();
            Assert.That(names, Does.Contain("real.txt"));
            if (symlinkPolicy == Options.SymlinkStrategy.Store)
                Assert.That(names, Is.SupersetOf(new[] { "absolute", "dangling" }));
            else
                Assert.That(names, Has.None.EqualTo("absolute").And.None.EqualTo("dangling"));
        }
    }

    [Test]
    [Category("SymLink")]
    public async Task StoredWslSymlinkIsRestoredWithItsTarget()
    {
        CreateSource();

        using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
        {
            var backupResults = await c.BackupAsync([DATAFOLDER]);
            Assert.That(backupResults.Warnings, Is.Empty);
        }

        var restoreOptions = new Dictionary<string, string>(TestOptions)
        {
            ["restore-path"] = RESTOREFOLDER,
            // The absolute Linux path is outside the restore target as Windows reads it
            ["allow-restore-outside-target-directory"] = "true"
        };
        using (var c = new Controller("file://" + TARGETFOLDER, restoreOptions, null))
        {
            var restoreResults = await c.RestoreAsync(null);
            Assert.That(restoreResults.Errors, Is.Empty);
            Assert.That(restoreResults.Warnings, Is.Empty);
        }

        Assert.That(systemIO.GetSymlinkTarget(Path.Combine(RESTOREFOLDER, "absolute")), Is.EqualTo("/usr/bin/python3"));
        Assert.That(systemIO.GetSymlinkTarget(Path.Combine(RESTOREFOLDER, "dangling")), Is.EqualTo("nonexistent"));
    }
}
