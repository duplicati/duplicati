using System.Collections.Generic;
using WmiLight;

namespace DeviceId.Windows.WmiLight.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that retrieves data from a WMI class.
/// </summary>
/// <param name="className">The class name.</param>
/// <param name="propertyName">The property name.</param>
public class WmiLightDeviceIdComponent(string className, string propertyName) : IDeviceIdComponent
{
    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    public string GetValue()
    {
        var values = new List<string>();

        try
        {
            using var wmiConnection = new WmiConnection();
            foreach (var wmiObject in wmiConnection.CreateQuery($"SELECT * FROM {className}"))
            {
                try
                {
                    if (wmiObject[propertyName] is string value)
                    {
                        values.Add(value);
                    }
                }
                finally
                {
                    wmiObject.Dispose();
                }
            }
        }
        catch
        {
            // Ignore exceptions
        }

        values.Sort();

        return values.Count > 0
            ? string.Join(",", values.ToArray())
            : null;
    }
}
