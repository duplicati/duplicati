using System;
using DeviceId.Internal;

namespace DeviceId;

/// <summary>
/// Extension methods for <see cref="DeviceIdBuilder"/>.
/// </summary>
public static class DeviceIdBuilderExtensions
{
    /// <summary>
    /// Adds Windows-specific components to the device ID.
    /// </summary>
    /// <param name="builder">The device ID builder to add the components to.</param>
    /// <param name="windowsBuilderConfiguration">An action that adds the Windows-specific components.</param>
    /// <returns>The device ID builder.</returns>
    public static DeviceIdBuilder OnWindows(this DeviceIdBuilder builder, Action<WindowsDeviceIdBuilder> windowsBuilderConfiguration)
    {
        if (OS.IsWindows && windowsBuilderConfiguration is not null)
        {
            var windowsBuilder = new WindowsDeviceIdBuilder(builder);
            windowsBuilderConfiguration.Invoke(windowsBuilder);
        }

        return builder;
    }
}
