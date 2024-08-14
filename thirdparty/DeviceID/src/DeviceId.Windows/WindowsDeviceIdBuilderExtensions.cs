#if NET5_0_OR_GREATER && WINDOWS10_0_17763_0_OR_GREATER
using DeviceId.Internal;
#endif
using DeviceId.Windows.Components;
#if !NET35
using Microsoft.Win32;
#endif

namespace DeviceId;

/// <summary>
/// Extension methods for <see cref="WindowsDeviceIdBuilder"/>.
/// </summary>
public static class WindowsDeviceIdBuilderExtensions
{
#if !NET35
    private static RegistryView[] Both32BitAnd64BitRegistryViews { get; } = new[] { RegistryView.Registry32, RegistryView.Registry64 };
#endif

#if NET35
    /// <summary>
    /// Adds a registry value to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="WindowsDeviceIdBuilder"/> to add the component to.</param>
    /// <param name="componentName">The name of the component.</param>
    /// <param name="registryKeyName">The full path of the registry key.</param>
    /// <param name="registryValueName">The name of the registry value.</param>
    /// <returns>The <see cref="WindowsDeviceIdBuilder"/> instance.</returns>
    public static WindowsDeviceIdBuilder AddRegistryValue(this WindowsDeviceIdBuilder builder, string componentName, string registryKeyName, string registryValueName)
    {
        return builder.AddComponent(componentName, new RegistryValueDeviceIdComponent(registryKeyName, registryValueName));
    }
#else
    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryValueDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="builder">The <see cref="WindowsDeviceIdBuilder"/> to add the component to.</param>
    /// <param name="componentName">The name of the component.</param>
    /// <param name="registryView">The registry view.</param>
    /// <param name="registryHive">The registry hive.</param>
    /// <param name="registryKeyName">The name of the registry key.</param>
    /// <param name="registryValueName">The name of the registry value.</param>
    public static WindowsDeviceIdBuilder AddRegistryValue(this WindowsDeviceIdBuilder builder, string componentName, RegistryView registryView, RegistryHive registryHive, string registryKeyName, string registryValueName)
    {
        return builder.AddComponent(componentName, new RegistryValueDeviceIdComponent(registryView, registryHive, registryKeyName, registryValueName));
    }
#endif

    /// <summary>
    /// Adds the Windows Device ID (also known as Machine ID or Advertising ID) to the device identifier.
    /// This value is the one displayed as "Device ID" in the Windows Device Specifications UI.
    /// </summary>
    /// <param name="builder">The <see cref="WindowsDeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="WindowsDeviceIdBuilder"/> instance.</returns>
    public static WindowsDeviceIdBuilder AddWindowsDeviceId(this WindowsDeviceIdBuilder builder)
    {
#if NET35
        var component = new RegistryValueDeviceIdComponent(
           keyName: @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\SQMClient",
           valueName: "MachineId",
           formatter: value => value.TrimStart('{').TrimEnd('}'));
#else

        var component = new RegistryValueDeviceIdComponent(
            registryViews: Both32BitAnd64BitRegistryViews,
            registryHive: RegistryHive.LocalMachine,
            keyName: @"SOFTWARE\Microsoft\SQMClient",
            valueName: "MachineId",
            formatter: value => value.TrimStart('{').TrimEnd('}'));
#endif

        return builder.AddComponent("WindowsDeviceId", component);
    }

    /// <summary>
    /// Adds the Windows Product ID to the device identifier.
    /// This value is the one displayed as "Product ID" in the Windows Device Specifications UI.
    /// </summary>
    /// <param name="builder">The <see cref="WindowsDeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="WindowsDeviceIdBuilder"/> instance.</returns>
    public static WindowsDeviceIdBuilder AddWindowsProductId(this WindowsDeviceIdBuilder builder)
    {
#if NET35
        var component = new RegistryValueDeviceIdComponent(
            keyName: @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            valueName: "ProductId");
#else
        var component = new RegistryValueDeviceIdComponent(
            registryViews: Both32BitAnd64BitRegistryViews,
            registryHive: RegistryHive.LocalMachine,
            keyName: @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            valueName: "ProductId",
            formatter: null);
#endif

        return builder.AddComponent("WindowsProductId", component);
    }

    /// <summary>
    /// Adds the Machine GUID (from HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography\MachineGuid) to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="WindowsDeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="WindowsDeviceIdBuilder"/> instance.</returns>
    public static WindowsDeviceIdBuilder AddMachineGuid(this WindowsDeviceIdBuilder builder)
    {
#if NET35
        var component = new RegistryValueDeviceIdComponent(
            keyName: @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
            valueName: "MachineGuid");
#else
        var component = new RegistryValueDeviceIdComponent(
            registryViews: Both32BitAnd64BitRegistryViews,
            registryHive: RegistryHive.LocalMachine,
            keyName: @"SOFTWARE\Microsoft\Cryptography",
            valueName: "MachineGuid",
            formatter: null);
#endif

        return builder.AddComponent("MachineGuid", component);
    }

#if NET5_0_OR_GREATER && WINDOWS10_0_17763_0_OR_GREATER
    /// <summary>
    /// Adds the System ID to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="WindowsDeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="WindowsDeviceIdBuilder"/> instance.</returns>
    public static WindowsDeviceIdBuilder AddSystemId(this WindowsDeviceIdBuilder builder)
    {
        return builder.AddComponent("SystemId", new SystemIdDeviceIdComponent(ByteArrayEncoders.Base32Crockford));
    }
#endif
}
