// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Localization.Short;

namespace Duplicati.Proprietary.DiskImage;

internal static class Strings
{
    public static string ProviderDisplayName => LC.L("Disk Image Provider");

    public static string ProviderDescription => LC.L(
        "Expose disks, partitions, and filesystems as a virtual folder structure.");

    public static string DiskDeviceOptionShort => LC.L("Disk device path.");
    public static string DiskDeviceOptionLong => LC.L("The device path of the disk to back up (e.g., \\\\.\\PhysicalDrive0).");
}
