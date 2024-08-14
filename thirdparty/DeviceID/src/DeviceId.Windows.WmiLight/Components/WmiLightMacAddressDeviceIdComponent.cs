using System.Collections.Generic;
using DeviceId.Components;
using DeviceId.Internal;
using WmiLight;

namespace DeviceId.Windows.WmiLight.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that uses the MAC Address of the PC.
/// This improves upon the basic <see cref="MacAddressDeviceIdComponent"/> by using WMI
/// to get better information from either MSFT_NetAdapter or Win32_NetworkAdapter.
/// </summary>
/// <param name="excludeWireless">A value determining whether wireless devices should be excluded.</param>
/// <param name="excludeNonPhysical">A value determining whether non-physical devices should be excluded.</param>
public class WmiLightMacAddressDeviceIdComponent(bool excludeWireless, bool excludeNonPhysical) : IDeviceIdComponent
{
    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    public string GetValue()
    {
        // First, try to get a value using MSFT_NetAdapter:
        try
        {
            return GetValueUsingMsftNetAdapter(excludeWireless, excludeNonPhysical);
        }
        catch { }

        // Next, try using Win32_NetworkAdapter:
        try
        {
            return GetValueUsingWin32NetworkAdapter(excludeWireless, excludeNonPhysical);
        }
        catch { }

        // Finally, try the fallback component:
        var fallback = new MacAddressDeviceIdComponent(excludeWireless);
        return fallback.GetValue();
    }

    /// <summary>
    /// Gets the component value using MSFT_NetAdapter.
    /// </summary>
    /// <param name="excludeWireless">A value determining whether wireless devices should be excluded.</param>
    /// <param name="excludeNonPhysical">A value determining whether non-physical devices should be excluded.</param>
    /// <returns>The component value.</returns>
    private static string GetValueUsingMsftNetAdapter(bool excludeWireless, bool excludeNonPhysical)
    {
        var values = new List<string>();

        values.Sort();

        using var wmiConnection = new WmiConnection(@"\\.\root\StandardCimv2");

        foreach (var wmiObject in wmiConnection.CreateQuery("SELECT * FROM MSFT_NetAdapter"))
        {
            try
            {
                // Skip non-physical adapters if instructed to do so.
                if (wmiObject["ConnectorPresent"] is bool isPhysical)
                {
                    if (excludeNonPhysical && !isPhysical)
                    {
                        continue;
                    }
                }

                // Skip wireless adapters if instructed to do so.
                if (wmiObject["NdisPhysicalMedium"] is uint ndisPhysicalMedium)
                {
                    if (excludeWireless && ndisPhysicalMedium == 9) // Native802_11
                    {
                        continue;
                    }
                }
                if (wmiObject["PermanentAddress"] is string permanentAddress)
                {
                    // Ensure the hardware addresses are formatted as MAC addresses if possible.
                    // This is a discrepancy between the MSFT_NetAdapter and Win32_NetworkAdapter interfaces.
                    values.Add(MacAddressFormatter.FormatMacAddress(permanentAddress));
                }
            }
            finally
            {
                wmiObject.Dispose();
            }
        }

        return values.Count > 0
            ? string.Join(",", values.ToArray())
            : null;
    }

    /// <summary>
    /// Gets the component value using Win32_NetworkAdapter.
    /// </summary>
    /// <param name="excludeWireless">A value determining whether wireless devices should be excluded.</param>
    /// <param name="excludeNonPhysical">A value determining whether non-physical devices should be excluded.</param>
    /// <returns>The component value.</returns>
    private static string GetValueUsingWin32NetworkAdapter(bool excludeWireless, bool excludeNonPhysical)
    {
        var values = new List<string>();

        using var wmiConnection = new WmiConnection();
        var wmiQuery = wmiConnection.CreateQuery("SELECT MACAddress, AdapterTypeID, PhysicalAdapter FROM Win32_NetworkAdapter");
        foreach (var managementObject in wmiQuery)
        {
            try
            {
                // Skip non-physical adapters if instructed to do so.
                if (managementObject["PhysicalAdapter"] is bool isPhysical)
                {
                    if (excludeNonPhysical && !isPhysical)
                    {
                        continue;
                    }
                }

                // Skip wireless adapters if instructed to do so.
                if (managementObject["AdapterTypeID"] is ushort adapterTypeId)
                {
                    if (excludeWireless && adapterTypeId == 9)
                    {
                        continue;
                    }
                }

                if (managementObject["MACAddress"] is string macAddress)
                {
                    if (!string.IsNullOrEmpty(macAddress))
                    {
                        values.Add(macAddress);
                    }
                }
            }
            finally
            {
                managementObject.Dispose();
            }
        }

        values.Sort();

        return values.Count > 0
            ? string.Join(",", values.ToArray())
            : null;
    }
}
