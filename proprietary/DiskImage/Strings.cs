// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Localization.Short;

namespace Duplicati.Proprietary.DiskImage;

/// <summary>
/// Localization strings for the DiskImage module.
/// </summary>
internal static class Strings
{
    /// <summary>
    /// Display name for the source provider.
    /// </summary>
    public static string ProviderDisplayName => LC.L("Disk Image Provider");

    /// <summary>
    /// Description for the source provider.
    /// </summary>
    public static string ProviderDescription => LC.L(
        "Expose disks, partitions, and filesystems as a virtual folder structure.");

    /// <summary>
    /// Display name for the restore provider.
    /// </summary>
    public static string RestoreProviderDisplayName => LC.L("Disk Image Restore Provider");

    /// <summary>
    /// Description for the restore provider.
    /// </summary>
    public static string RestoreProviderDescription => LC.L(
        "Restore disk images to physical disks, partitions, and filesystems.");

    /// <summary>
    /// Short description for the auto unmount option.
    /// </summary>
    public static string DiskRestoreAutoUnmountShort => LC.L("Auto unmount target disk.");

    /// <summary>
    /// Long description for the auto unmount option.
    /// </summary>
    public static string DiskRestoreAutoUnmountLong => LC.L("If set, the target disk will automatically unmount all volumes, pull the disk offline, and clear the readonly attributes before restore operations. This is a requirement for restore on Windows.");

    /// <summary>
    /// Short description for the skip partition table option.
    /// </summary>
    public static string DiskRestoreSkipPartitionTableShort => LC.L("Skip partition table restore.");

    /// <summary>
    /// Long description for the skip partition table option.
    /// </summary>
    public static string DiskRestoreSkipPartitionTableLong => LC.L("If set, the partition table (MBR/GPT) will not be restored, only the data within partitions.");

    /// <summary>
    /// Short description for the validate size option.
    /// </summary>
    public static string DiskRestoreValidateSizeShort => LC.L("Validate target size.");

    /// <summary>
    /// Long description for the validate size option.
    /// </summary>
    public static string DiskRestoreValidateSizeLong => LC.L("If set, the target disk size will be validated against the source size before restoring.");

    /// <summary>
    /// Error message for when restore target device is not found.
    /// </summary>
    public static string RestoreTargetNotFound => LC.L("Restore target device not found: {0}");

    /// <summary>
    /// Error message for when restore target is too small.
    /// </summary>
    public static string RestoreTargetTooSmall => LC.L("Restore target device is too small. Target: {0} bytes, Source: {1} bytes.");

    /// <summary>
    /// Error message for when overwrite option is not set.
    /// </summary>
    public static string RestoreOverwriteNotSet => LC.L("The overwrite option must be set to restore to a disk image target. Use --overwrite to enable.");

    /// <summary>
    /// Error message for when platform is not supported.
    /// </summary>
    public static string RestorePlatformNotSupported => LC.L("Disk image restore is only supported on Windows.");

    /// <summary>
    /// Error message for when device cannot be opened for writing.
    /// </summary>
    public static string RestoreDeviceNotWriteable => LC.L("Failed to open device for write access: {0}");

    /// <summary>
    /// Error message for when restore path is invalid.
    /// </summary>
    public static string RestoreInvalidPath => LC.L("Invalid restore path: {0}");
}
