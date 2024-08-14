namespace DeviceId;

/// <summary>
/// Represents a component that forms part of a device identifier.
/// </summary>
public interface IDeviceIdComponent
{
    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    string GetValue();
}
