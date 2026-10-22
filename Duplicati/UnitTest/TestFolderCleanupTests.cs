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
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The cleanup after each test removes the test folders recursively. On Windows, a
/// recursive delete that meets a junction below the folder tries to unmount it first, which
/// is refused; the junction is removed anyway, but the delete then fails, the test fails in
/// its teardown, and the rest of the folder is left behind.
/// </summary>
public class TestFolderCleanupTests : BasicSetupHelper
{
    /// <summary>A folder outside the test folders for the junction to point to</summary>
    private string JunctionTarget => this.DATAFOLDER.TrimEnd(Path.DirectorySeparatorChar) + "-junction-target";

    [Test]
    [Category("Utility")]
    public void TheCleanupRemovesAFolderHoldingAJunction()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("Junctions are a Windows thing");

        Directory.CreateDirectory(JunctionTarget);
        File.WriteAllText(Path.Combine(JunctionTarget, "keep.txt"), "keep");
        var junction = Path.Combine(this.DATAFOLDER, "deeper", "junction");
        Directory.CreateDirectory(Path.GetDirectoryName(junction)!);
        using (var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{JunctionTarget}\"") { UseShellExecute = false, RedirectStandardOutput = true })!)
            p.WaitForExit();
        Assert.That(new DirectoryInfo(junction).LinkTarget, Is.Not.Null, "the test could not make a junction");
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "file.txt"), "x");

        try
        {
            Assert.DoesNotThrow(() => BasicHelperTearDown(), "the cleanup should remove a folder that holds a junction");
            Assert.That(Directory.Exists(this.DATAFOLDER), Is.False, "the data folder should be gone");
            Assert.That(File.ReadAllText(Path.Combine(JunctionTarget, "keep.txt")), Is.EqualTo("keep"), "what the junction points to should be left alone");
        }
        finally
        {
            if (Directory.Exists(JunctionTarget))
                Directory.Delete(JunctionTarget, true);
        }
    }
}
