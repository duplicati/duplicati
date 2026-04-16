using System.Collections.Generic;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.DiskImage.General;

/// <summary>
/// Helper class containing constants and command-line argument definitions for the DiskImage module.
/// </summary>
internal static class OptionsHelper
{
    /// <summary>
    /// The module key identifier for the DiskImage module.
    /// </summary>
    internal const string ModuleKey = "diskimage";

    /// <summary>
    /// Option to automatically unmount the target disk before restore operations.
    /// </summary>
    internal const string DISK_RESTORE_AUTO_UNMOUNT_OPTION = "diskimage-restore-auto-unmount";

    /// <summary>
    /// Option to skip partition table restoration during restore operations.
    /// </summary>
    internal const string DISK_RESTORE_SKIP_PARTITION_TABLE_OPTION = "diskimage-restore-skip-partition-table";

    /// <summary>
    /// Option to validate target disk size before restoring.
    /// </summary>
    internal const string DISK_RESTORE_VALIDATE_SIZE_OPTION = "diskimage-restore-validate-size";

    /// <summary>
    /// Option to treat filesystem as unknown (force raw block-based backup).
    /// </summary>
    internal const string DISK_IMAGE_FILESYSTEM_UNKNOWN_OPTION = "diskimage-filesystem-unknown";

    /// <summary>
    /// Gets the list of supported command-line arguments for the DiskImage module.
    /// </summary>
    internal static IList<ICommandLineArgument> SupportedCommands =>
    [
        new CommandLineArgument(DISK_RESTORE_AUTO_UNMOUNT_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.DiskRestoreAutoUnmountShort, Strings.DiskRestoreAutoUnmountLong, "false"),
        new CommandLineArgument(DISK_RESTORE_SKIP_PARTITION_TABLE_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.DiskRestoreSkipPartitionTableShort, Strings.DiskRestoreSkipPartitionTableLong, "false"),
        new CommandLineArgument(DISK_RESTORE_VALIDATE_SIZE_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.DiskRestoreValidateSizeShort, Strings.DiskRestoreValidateSizeLong, "true"),
        new CommandLineArgument(DISK_IMAGE_FILESYSTEM_UNKNOWN_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.DiskImageFilesystemUnknownShort, Strings.DiskImageFilesystemUnknownLong, "false")
    ];
}
