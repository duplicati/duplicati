using System.Collections.Generic;
using System.Management;

namespace DeviceId.Windows.Wmi.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that retrieves data from a WMI class.
/// </summary>
public class WmiDeviceIdComponent : IDeviceIdComponent
{
    /// <summary>
    /// The class name.
    /// </summary>
    private readonly string _className;

    /// <summary>
    /// The property name.
    /// </summary>
    private readonly string _propertyName;

    /// <summary>
    /// Initializes a new instance of the <see cref="WmiDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="className">The class name.</param>
    /// <param name="propertyName">The property name.</param>
    public WmiDeviceIdComponent(string className, string propertyName)
    {
        _className = className;
        _propertyName = propertyName;
    }

    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    public string GetValue()
    {
        var values = new List<string>();

        try
        {
            using var managementObjectSearcher = new ManagementObjectSearcher($"SELECT {_propertyName} FROM {_className}");
            using var managementObjectCollection = managementObjectSearcher.Get();
            foreach (var managementObject in managementObjectCollection)
            {
                try
                {
                    if (managementObject[_propertyName] is string value)
                    {
                        values.Add(value);
                    }
                }
                finally
                {
                    managementObject.Dispose();
                }
            }
        }
        catch
        {

        }

        values.Sort();

        return values.Count > 0
            ? string.Join(",", values.ToArray())
            : null;
    }
}
