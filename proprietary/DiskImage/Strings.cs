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

    public static string RestoreProviderDisplayName => LC.L("Disk Image Restore Provider");

    public static string RestoreProviderDescription => LC.L(
        "Restore disk images to physical disks, partitions, and filesystems.");

    public static string DiskRestoreSkipPartitionTableShort => LC.L("Skip partition table restore.");
    public static string DiskRestoreSkipPartitionTableLong => LC.L("If set, the partition table (MBR/GPT) will not be restored, only the data within partitions.");

    public static string DiskRestoreValidateSizeShort => LC.L("Validate target size.");
    public static string DiskRestoreValidateSizeLong => LC.L("If set, the target disk size will be validated against the source size before restoring.");

    public static string RestoreTargetNotFound => LC.L("Restore target device not found: {0}");
    public static string RestoreTargetTooSmall => LC.L("Restore target device is too small. Target: {0} bytes, Source: {1} bytes.");
    public static string RestoreOverwriteNotSet => LC.L("The overwrite option must be set to restore to a disk image target. Use --overwrite to enable.");
    public static string RestorePlatformNotSupported => LC.L("Disk image restore is only supported on Windows.");
    public static string RestoreDeviceNotWriteable => LC.L("Failed to open device for write access: {0}");
    public static string RestoreInvalidPath => LC.L("Invalid restore path: {0}");
}
