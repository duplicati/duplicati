using System;
using DeviceId.Components;
using DeviceId.Internal;

namespace DeviceId;

/// <summary>
/// Extension methods for <see cref="DeviceIdBuilder"/>.
/// </summary>
public static class DeviceIdBuilderExtensions
{
    /// <summary>
    /// Use the specified formatter.
    /// </summary>
    /// <param name="builder">The <see cref="DeviceIdBuilder"/> to use the formatter.</param>
    /// <param name="formatter">The <see cref="IDeviceIdFormatter"/> to use.</param>
    /// <returns>The <see cref="DeviceIdBuilder"/> instance.</returns>
    public static DeviceIdBuilder UseFormatter(this DeviceIdBuilder builder, IDeviceIdFormatter formatter)
    {
        builder.Formatter = formatter;
        return builder;
    }

    /// <summary>
    /// Adds the current user name to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="DeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="DeviceIdBuilder"/> instance.</returns>
    public static DeviceIdBuilder AddUserName(this DeviceIdBuilder builder)
    {
        // Default to false for backwards compatibility. May consider changing this to true in the next major version.

        return AddUserName(builder, false);
    }

    /// <summary>
    /// Adds the current user name to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="DeviceIdBuilder"/> to add the component to.</param>
    /// <param name="normalize">A value determining whether the user name should be normalized or not.</param>
    /// <returns>The <see cref="DeviceIdBuilder"/> instance.</returns>
    public static DeviceIdBuilder AddUserName(this DeviceIdBuilder builder, bool normalize)
    {
        var userName = normalize
            ? Environment.UserName?.ToLowerInvariant()
            : Environment.UserName;

        return builder.AddComponent("UserName", new DeviceIdComponent(userName));
    }

    /// <summary>
    /// Adds the machine name to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="DeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="DeviceIdBuilder"/> instance.</returns>
    public static DeviceIdBuilder AddMachineName(this DeviceIdBuilder builder)
    {
        return builder.AddComponent("MachineName", new DeviceIdComponent(Environment.MachineName));
    }

    /// <summary>
    /// Adds the operating system version to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="DeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="DeviceIdBuilder"/> instance.</returns>
    public static DeviceIdBuilder AddOsVersion(this DeviceIdBuilder builder)
    {
        return builder.AddComponent("OSVersion", new DeviceIdComponent(OS.Version));
    }

    /// <summary>
    /// Adds the MAC address to the device identifier, optionally excluding wireless adapters.
    /// </summary>
    /// <param name="builder">The <see cref="DeviceIdBuilder"/> to add the component to.</param>
    /// <param name="excludeWireless">A value indicating whether wireless adapters should be excluded.</param>
    /// <param name="excludeDockerBridge">A value determining whether docker bridge should be excluded.</param>
    /// <returns>The <see cref="DeviceIdBuilder"/> instance.</returns>
    public static DeviceIdBuilder AddMacAddress(this DeviceIdBuilder builder, bool excludeWireless = false, bool excludeDockerBridge = false)
    {
        return builder.AddComponent("MACAddress", new MacAddressDeviceIdComponent(excludeWireless, excludeDockerBridge));
    }

    /// <summary>
    /// Adds a file-based token to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="DeviceIdBuilder"/> to add the component to.</param>
    /// <param name="path">The path of the token.</param>
    /// <returns>The <see cref="DeviceIdBuilder"/> instance.</returns>
    public static DeviceIdBuilder AddFileToken(this DeviceIdBuilder builder, string path)
    {
        var name = string.Concat("FileToken", path.GetHashCode());
        return builder.AddComponent(name, new FileTokenDeviceIdComponent(path));
    }
}
