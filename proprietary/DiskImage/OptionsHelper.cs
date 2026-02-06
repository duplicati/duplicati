using System.Collections.Generic;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.DiskImage;

internal static class OptionsHelper
{
    internal const string ModuleKey = "diskimage";
    internal const string DISK_RESTORE_SKIP_PARTITION_TABLE_OPTION = "diskimage-restore-skip-partition-table";
    internal const string DISK_RESTORE_VALIDATE_SIZE_OPTION = "diskimage-restore-validate-size";

    internal static IList<ICommandLineArgument> SupportedCommands =>
    [
        new CommandLineArgument(DISK_RESTORE_SKIP_PARTITION_TABLE_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.DiskRestoreSkipPartitionTableShort, Strings.DiskRestoreSkipPartitionTableLong, "false"),
        new CommandLineArgument(DISK_RESTORE_VALIDATE_SIZE_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.DiskRestoreValidateSizeShort, Strings.DiskRestoreValidateSizeLong, "true")
    ];
}
