using System;
using DeviceId.Internal;

namespace DeviceId;

/// <summary>
/// Provides a fluent interface for adding Mac-specific components to a device identifier.
/// </summary>
public class MacDeviceIdBuilder
{
    /// <summary>
    /// The base device identifier builder.
    /// </summary>
    private readonly DeviceIdBuilder _baseBuilder;

    /// <summary>
    /// Initializes a new instance of the <see cref="MacDeviceIdBuilder"/> class.
    /// </summary>
    /// <param name="baseBuilder">The base device identifier builder.</param>
    public MacDeviceIdBuilder(DeviceIdBuilder baseBuilder)
    {
        _baseBuilder = baseBuilder ?? throw new ArgumentNullException(nameof(baseBuilder));
    }

    /// <summary>
    /// Adds a component to the device identifier.
    /// If a component with the specified name already exists, it will be replaced with this newly added component.
    /// </summary>
    /// <param name="name">The component name.</param>
    /// <param name="component">The component to add.</param>
    /// <returns>The builder instance.</returns>
    public MacDeviceIdBuilder AddComponent(string name, IDeviceIdComponent component)
    {
        if (OS.IsMacOS)
        {
            _baseBuilder.AddComponent(name, component);
        }

        return this;
    }
}
