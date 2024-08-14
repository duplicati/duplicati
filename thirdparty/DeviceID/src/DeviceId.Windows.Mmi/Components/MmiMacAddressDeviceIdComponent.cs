using System.Collections.Generic;
using DeviceId.Components;
using DeviceId.Internal;
using Microsoft.Management.Infrastructure;

namespace DeviceId.Windows.Mmi.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that uses the MAC Address of the PC.
/// This improves upon the basic <see cref="MacAddressDeviceIdComponent"/> by using MMI
/// to get better information from either MSFT_NetAdapter or Win32_NetworkAdapter.
/// </summary>
public class MmiMacAddressDeviceIdComponent : IDeviceIdComponent
{
    /// <summary>
    /// A value determining whether wireless devices should be excluded.
    /// </summary>
    private readonly bool _excludeWireless;

    /// <summary>
    /// A value determining whether non-physical devices should be excluded.
    /// </summary>
    private readonly bool _excludeNonPhysical;

    /// <summary>
    /// Initializes a new instance of the <see cref="MmiMacAddressDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="excludeWireless">A value determining whether wireless devices should be excluded.</param>
    /// <param name="excludeNonPhysical">A value determining whether non-physical devices should be excluded.</param>
    public MmiMacAddressDeviceIdComponent(bool excludeWireless, bool excludeNonPhysical)
    {
        _excludeWireless = excludeWireless;
        _excludeNonPhysical = excludeNonPhysical;
    }

    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    public string GetValue()
    {
        // First, try to get a value using MSFT_NetAdapter:
        try
        {
            return GetValueUsingMsftNetAdapter(_excludeWireless, _excludeNonPhysical);
        }
        catch { }

        // Next, try using Win32_NetworkAdapter:
        try
        {
            return GetValueUsingWin32NetworkAdapter(_excludeWireless, _excludeNonPhysical);
        }
        catch { }

        // Finally, try the fallback component:
        var fallback = new MacAddressDeviceIdComponent(_excludeWireless);
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

        using var session = CimSession.Create(null);

        foreach (var instance in session.EnumerateInstances("root/StandardCimv2", "MSFT_NetAdapter"))
        {
            // Skip non-physical adapters if instructed to do so.
            if (instance.CimInstanceProperties["ConnectorPresent"].Value is bool connectorPresent)
            {
                if (excludeNonPhysical && !connectorPresent)
                {
                    continue;
                }
            }

            // Skip wireless adapters if instructed to do so.
            if (instance.CimInstanceProperties["NdisPhysicalMedium"].Value is uint ndisPhysicalMedium)
            {
                if (excludeWireless && ndisPhysicalMedium == 9) // Native802_11
                {
                    continue;
                }
            }

            if (instance.CimInstanceProperties["PermanentAddress"].Value is string permanentAddress)
            {
                if (!string.IsNullOrEmpty(permanentAddress))
                {
                    // Ensure the hardware addresses are formatted as MAC addresses if possible.
                    // This is a discrepancy between the MSFT_NetAdapter and Win32_NetworkAdapter interfaces.
                    values.Add(MacAddressFormatter.FormatMacAddress(permanentAddress));
                }
            }
        }

        values.Sort();

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

        using var session = CimSession.Create(null);
        foreach (var instance in session.QueryInstances(@"root\cimv2", "WQL", "select MACAddress, AdapterTypeID, PhysicalAdapter from Win32_NetworkAdapter"))
        {
            // Skip non-physical adapters if instructed to do so.
            if (instance.CimInstanceProperties["PhysicalAdapter"].Value is bool isPhysical)
            {
                if (excludeNonPhysical && !isPhysical)
                {
                    continue;
                }
            }

            // Skip wireless adapters if instructed to do so.
            if (instance.CimInstanceProperties["AdapterTypeID"].Value is ushort adapterTypeId)
            {
                if (excludeWireless && adapterTypeId == 9)
                {
                    continue;
                }
            }

            if (instance.CimInstanceProperties["MACAddress"].Value is string macAddress)
            {
                if (!string.IsNullOrEmpty(macAddress))
                {
                    values.Add(macAddress);
                }
            }
        }

        values.Sort();

        return values.Count > 0
            ? string.Join(",", values.ToArray())
            : null;
    }
}
