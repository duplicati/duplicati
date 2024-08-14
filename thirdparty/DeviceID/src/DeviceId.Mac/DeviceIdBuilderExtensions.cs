using System;
using DeviceId.Internal;

namespace DeviceId;

/// <summary>
/// Extension methods for <see cref="DeviceIdBuilder"/>.
/// </summary>
public static class DeviceIdBuilderExtensions
{
    /// <summary>
    /// Adds Mac-specific components to the device ID.
    /// </summary>
    /// <param name="builder">The device ID builder to add the components to.</param>
    /// <param name="macBuilderConfiguration">An action that adds the Mac-specific components.</param>
    /// <returns>The device ID builder.</returns>
    public static DeviceIdBuilder OnMac(this DeviceIdBuilder builder, Action<MacDeviceIdBuilder> macBuilderConfiguration)
    {
        if (OS.IsMacOS && macBuilderConfiguration is not null)
        {
            var macBuilder = new MacDeviceIdBuilder(builder);
            macBuilderConfiguration.Invoke(macBuilder);
        }

        return builder;
    }
}
