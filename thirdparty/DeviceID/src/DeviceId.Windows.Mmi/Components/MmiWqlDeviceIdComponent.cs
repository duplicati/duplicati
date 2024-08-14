using System.Collections.Generic;
using Microsoft.Management.Infrastructure;

namespace DeviceId.Windows.Mmi.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that retrieves data from a WQL query
/// </summary>
public class MmiWqlDeviceIdComponent : IDeviceIdComponent
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
    /// Initializes a new instance of the <see cref="MmiWqlDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="className">The class name.</param>
    /// <param name="propertyName">The property name.</param>
    public MmiWqlDeviceIdComponent(string className, string propertyName)
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
            using var session = CimSession.Create(null);

            var instances = session.QueryInstances(@"root\cimv2", "WQL", $"SELECT {_propertyName} FROM {_className}");
            foreach (var instance in instances)
            {
                try
                {
                    if (instance.CimInstanceProperties[_propertyName].Value is string value)
                    {
                        values.Add(value);
                    }
                }
                finally
                {
                    instance.Dispose();
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
