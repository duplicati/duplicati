// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Runtime.Versioning;
using System.Threading;
using Duplicati.Proprietary.DiskImage.Disk;
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
