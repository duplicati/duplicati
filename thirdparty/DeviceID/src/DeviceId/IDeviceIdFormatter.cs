using System.Collections.Generic;

namespace DeviceId;

/// <summary>
/// Provides a method to combine a number of component values into a single device identifier string.
/// </summary>
public interface IDeviceIdFormatter
{
    /// <summary>
    /// Returns the device identifier string created by combining the specified components.
    /// </summary>
    /// <param name="components">A dictionary containing the components.</param>
    /// <returns>The device identifier string.</returns>
    string GetDeviceId(IDictionary<string, IDeviceIdComponent> components);
}
