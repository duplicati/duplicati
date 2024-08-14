using System;
using System.Collections.Generic;

namespace DeviceId;

/// <summary>
/// Provides a fluent interface for constructing unique device identifiers.
/// </summary>
public class DeviceIdBuilder
{
    /// <summary>
    /// Gets or sets the formatter to use.
    /// </summary>
    public IDeviceIdFormatter Formatter { get; set; }

    /// <summary>
    /// A dictionary containing the components that will make up the device identifier.
    /// </summary>
    public IDictionary<string, IDeviceIdComponent> Components { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceIdBuilder"/> class.
    /// </summary>
    public DeviceIdBuilder()
    {
        Formatter = DeviceIdFormatters.DefaultV6;
        Components = new Dictionary<string, IDeviceIdComponent>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds a component to the device identifier.
    /// If a component with the specified name already exists, it will be replaced with this newly added component.
    /// </summary>
    /// <param name="name">The component name.</param>
    /// <param name="component">The component to add.</param>
    /// <returns>The builder instance.</returns>
    public DeviceIdBuilder AddComponent(string name, IDeviceIdComponent component)
    {
        Components[name] = component;
        return this;
    }

    /// <summary>
    /// Returns a string representation of the device identifier.
    /// </summary>
    /// <returns>A string representation of the device identifier.</returns>
    public override string ToString()
    {
        if (Formatter == null)
        {
            throw new InvalidOperationException($"The {nameof(Formatter)} property must not be null in order for {nameof(ToString)} to be called.");
        }

        return Formatter.GetDeviceId(Components);
    }
}
