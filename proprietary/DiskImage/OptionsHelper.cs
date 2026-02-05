using System.Collections.Generic;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.DiskImage;

internal static class OptionsHelper
{
    internal const string ModuleKey = "diskimage";
    internal const string DISK_DEVICE_OPTION = "diskimage-device";

    internal static IList<ICommandLineArgument> SupportedCommands =>
    [
        new CommandLineArgument(DISK_DEVICE_OPTION, CommandLineArgument.ArgumentType.String, Strings.DiskDeviceOptionShort, Strings.DiskDeviceOptionLong)
    ];
}
