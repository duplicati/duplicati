using System;
using DeviceId.Internal;

namespace DeviceId;

/// <summary>
/// Extension methods for <see cref="DeviceIdBuilder"/>.
/// </summary>
public static class DeviceIdBuilderExtensions
{
    /// <summary>
    /// Adds Linux-specific components to the device ID.
    /// </summary>
    /// <param name="builder">The device ID builder to add the components to.</param>
    /// <param name="linuxBuilderConfiguration">An action that adds the Linux-specific components.</param>
    /// <returns>The device ID builder.</returns>
    public static DeviceIdBuilder OnLinux(this DeviceIdBuilder builder, Action<LinuxDeviceIdBuilder> linuxBuilderConfiguration)
    {
        if (OS.IsLinux && linuxBuilderConfiguration is not null)
        {
            var linuxBuilder = new LinuxDeviceIdBuilder(builder);
            linuxBuilderConfiguration.Invoke(linuxBuilder);
        }

        return builder;
    }
}
