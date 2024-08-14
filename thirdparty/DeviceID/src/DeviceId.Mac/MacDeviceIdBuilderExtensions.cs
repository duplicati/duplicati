using DeviceId.Components;
using DeviceId.Internal.CommandExecutors;

namespace DeviceId;

/// <summary>
/// Extension methods for <see cref="MacDeviceIdBuilder"/>.
/// </summary>
public static class MacDeviceIdBuilderExtensions
{
    /// <summary>
    /// Adds the system drive serial number to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="MacDeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="MacDeviceIdBuilder"/> instance.</returns>
    public static MacDeviceIdBuilder AddSystemDriveSerialNumber(this MacDeviceIdBuilder builder)
    {
        return builder.AddComponent("SystemDriveSerialNumber", new CommandComponent("system_profiler SPSerialATADataType | sed -En 's/.*Serial Number: ([\\d\\w]*)//p'", CommandExecutor.Bash));
    }

    /// <summary>
    /// Adds the platform serial number to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="MacDeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="MacDeviceIdBuilder"/> instance.</returns>
    public static MacDeviceIdBuilder AddPlatformSerialNumber(this MacDeviceIdBuilder builder)
    {
        return builder.AddComponent("IOPlatformSerialNumber", new CommandComponent("ioreg -l | grep IOPlatformSerialNumber | sed 's/.*= //' | sed 's/\"//g'", CommandExecutor.Bash));
    }
}
