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
using System.Security.Principal;
using Duplicati.Library.Snapshots;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A Windows snapshot that fails after the snapshot set was started, such as one with a source
/// on a volume that cannot be snapshotted, left the set in progress. Every later snapshot then
/// failed with VSS_E_SNAPSHOT_SET_IN_PROGRESS (0x80042316) until the process ended, and for
/// a while after it (issues #7005 and #6866). VSS needs an elevated process.
/// </summary>
[NonParallelizable]
public class WindowsSnapshotFailureTests : BasicSetupHelper
{
    /// <summary>
    /// Takes a snapshot of the data folder and releases it
    /// </summary>
    /// <param name="sources">The sources to snapshot</param>
    /// <param name="provider">The snapshot provider to use</param>
    [SupportedOSPlatform("windows")]
    private static void Snapshot(string[] sources, string provider)
    {
        using var snapshot = new WindowsSnapshot(sources, new Dictionary<string, string> { ["snapshot-provider"] = provider }, false);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsElevated()
        => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    [TestCase("Native")]
    [TestCase("Vanara")]
    [SupportedOSPlatform("windows")]
    public void ASnapshotAfterAFailedSnapshotWorks(string provider)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("Snapshots with VSS are Windows only");
        if (!IsElevated())
            Assert.Ignore("VSS needs an elevated process");

        File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "data");
        var source = DATAFOLDER.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        try
        {
            Snapshot([source], provider);
        }
        catch (Exception ex)
        {
            Assert.Ignore($"A snapshot cannot be made here: {ex.Message}");
        }

        // A drive letter that is not in use, which VSS cannot snapshot. It is checked
        // after the snapshot set is started.
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var unused = Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => (char)c).Reverse().First(c => !used.Contains(c));
        Assert.That(() => Snapshot([source, $"{unused}:\\missing\\"], provider), Throws.Exception, "the snapshot with a missing drive should fail");

        Assert.That(() => Snapshot([source], provider), Throws.Nothing, "a snapshot after the failed one should work");
    }
}
