namespace DeviceId;

/// <summary>
/// Provides functionality to encode a <see cref="IDeviceIdComponent"/> as a string.
/// </summary>
public interface IDeviceIdComponentEncoder
{
    /// <summary>
    /// Encodes the specified <see cref="IDeviceIdComponent"/> as a string.
    /// </summary>
    /// <param name="component">The component to encode.</param>
    /// <returns>The component encoded as a string.</returns>
    string Encode(IDeviceIdComponent component);
}
