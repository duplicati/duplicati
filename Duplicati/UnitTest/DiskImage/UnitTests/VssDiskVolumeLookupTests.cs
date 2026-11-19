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
using System.Runtime.Versioning;
using System.Threading;
using Duplicati.Library.SourceProvider.DiskImage.Disk;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

#nullable enable

/// <summary>
/// Looking up the volumes on a disk asks PowerShell for them. A disk with no
/// partitions is an ordinary thing to meet - a raw disk, or one waiting to be
/// partitioned - and it has to answer "no volumes" rather than failing the run.
/// </summary>
[TestFixture]
[Category("DiskImageUnit")]
public class VssDiskVolumeLookupTests
{
    /// <summary>
    /// A number no machine running these tests is expected to have a disk for,
    /// which asks PowerShell the same question a partitionless disk does
    /// </summary>
    private const string AbsentDisk = @"\\.\PhysicalDrive99";

    [Test]
    [SupportedOSPlatform("windows")]
    public void ADiskWithNoPartitionsGivesNoVolumes()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("The disk wrapper is Windows-only.");
            return;
        }

        // This throwing is the fault: PowerShell answers with exit code 1 and no
        // text when there is nothing to list, and that became an IOException
        // carrying an empty message
        Assert.DoesNotThrowAsync(async () =>
            await VssDiskWrapper.GetVolumesOnDiskAsync(AbsentDisk, CancellationToken.None));
    }

    [Test]
    public void ASingleDigitDriveIsReadAsItself()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("The disk wrapper is Windows-only.");
            return;
        }

        Assert.IsTrue(VssDiskWrapper.TryGetDiskNumber(@"\\.\PhysicalDrive0", out var number));
        Assert.AreEqual(0, number);
    }

    [Test]
    public void ATwoDigitDriveIsNotTruncated()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("The disk wrapper is Windows-only.");
            return;
        }

        // Reading only the last character turned disk 10 into disk 0, so the
        // volumes of an entirely different disk were reported
        Assert.IsTrue(VssDiskWrapper.TryGetDiskNumber(@"\\.\PhysicalDrive10", out var number));
        Assert.AreEqual(10, number);
    }

    [Test]
    public void ATrailingSeparatorIsIgnored()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("The disk wrapper is Windows-only.");
            return;
        }

        Assert.IsTrue(VssDiskWrapper.TryGetDiskNumber(@"\\.\PhysicalDrive7\", out var number));
        Assert.AreEqual(7, number);
    }

    [Test]
    public void APathWithoutANumberIsRejected()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("The disk wrapper is Windows-only.");
            return;
        }

        Assert.IsFalse(VssDiskWrapper.TryGetDiskNumber(@"\\.\PhysicalDrive", out _));
        Assert.IsFalse(VssDiskWrapper.TryGetDiskNumber(string.Empty, out _));
    }
}
