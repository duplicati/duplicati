using System.Collections.Generic;
using System.Management;
using DeviceId.Components;
using DeviceId.Internal;

namespace DeviceId.Windows.Wmi.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that uses the MAC Address of the PC.
/// This improves upon the basic <see cref="MacAddressDeviceIdComponent"/> by using WMI
/// to get better information from either MSFT_NetAdapter or Win32_NetworkAdapter.
/// </summary>
public class WmiMacAddressDeviceIdComponent : IDeviceIdComponent
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
    /// Initializes a new instance of the <see cref="WmiMacAddressDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="excludeWireless">A value determining whether wireless devices should be excluded.</param>
    /// <param name="excludeNonPhysical">A value determining whether non-physical devices should be excluded.</param>
    public WmiMacAddressDeviceIdComponent(bool excludeWireless, bool excludeNonPhysical)
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

        using var managementClass = new ManagementClass("root/StandardCimv2", "MSFT_NetAdapter", new ObjectGetOptions());

        foreach (var managementInstance in managementClass.GetInstances())
        {
            try
            {
                // Skip non-physical adapters if instructed to do so.
                if (managementInstance["ConnectorPresent"] is bool isPhysical)
                {
                    if (excludeNonPhysical && !isPhysical)
                    {
                        continue;
                    }
                }

                // Skip wireless adapters if instructed to do so.
                if (managementInstance["NdisPhysicalMedium"] is uint ndisPhysicalMedium)
                {
                    if (excludeWireless && ndisPhysicalMedium == 9) // Native802_11
                    {
                        continue;
                    }
                }

                if (managementInstance["PermanentAddress"] is string permanentAddress)
                {
                    // Ensure the hardware addresses are formatted as MAC addresses if possible.
                    // This is a discrepancy between the MSFT_NetAdapter and Win32_NetworkAdapter interfaces.
                    values.Add(MacAddressFormatter.FormatMacAddress(permanentAddress));
                }
            }
            finally
            {
                managementInstance.Dispose();
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

        using var managementObjectSearcher = new ManagementObjectSearcher("select MACAddress, AdapterTypeID, PhysicalAdapter from Win32_NetworkAdapter");
        using var managementObjectCollection = managementObjectSearcher.Get();
        foreach (var managementObject in managementObjectCollection)
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
