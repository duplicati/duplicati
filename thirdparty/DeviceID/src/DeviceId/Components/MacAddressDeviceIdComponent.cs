using System.Linq;
using System.Net.NetworkInformation;
using DeviceId.Internal;

namespace DeviceId.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that uses the MAC Address of the PC.
/// </summary>
public class MacAddressDeviceIdComponent : IDeviceIdComponent
{
    private const string _dockerBridgeInterfaceName = "docker0";

    /// <summary>
    /// A value determining whether wireless devices should be excluded.
    /// </summary>
    private readonly bool _excludeWireless;

    /// <summary>
    /// A value determining whether docker bridge should be excluded.
    /// </summary>
    private readonly bool _excludeDockerBridge;

    /// <summary>
    /// Initializes a new instance of the <see cref="MacAddressDeviceIdComponent"/> class.
    /// </summary>
    public MacAddressDeviceIdComponent()
        : this(false, false) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="MacAddressDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="excludeWireless">A value determining whether wireless devices should be excluded.</param>
    public MacAddressDeviceIdComponent(bool excludeWireless)
        : this(excludeWireless, false) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="MacAddressDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="excludeWireless">A value determining whether wireless devices should be excluded.</param>
    /// <param name="excludeDockerBridge">A value determining whether docker bridge should be excluded.</param>
    public MacAddressDeviceIdComponent(bool excludeWireless, bool excludeDockerBridge)
    {
        _excludeWireless = excludeWireless;
        _excludeDockerBridge = excludeDockerBridge;
    }

    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    public string GetValue()
    {
        var values = NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => (!_excludeWireless || x.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) && (!_excludeDockerBridge || x.Name != _dockerBridgeInterfaceName))
            .Select(x => x.GetPhysicalAddress().ToString())
            .Where(x => x != "000000000000")
            .Select(x => MacAddressFormatter.FormatMacAddress(x))
            .ToList();

        values.Sort();

        return values.Count > 0
            ? string.Join(",", values.ToArray())
            : null;
    }
}
