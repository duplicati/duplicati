namespace DeviceId.Encoders;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponentEncoder"/> that encodes components as plain text.
/// </summary>
public class PlainTextDeviceIdComponentEncoder : IDeviceIdComponentEncoder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlainTextDeviceIdComponentEncoder"/> class.
    /// </summary>
    public PlainTextDeviceIdComponentEncoder() { }

    /// <summary>
    /// Encodes the specified <see cref="IDeviceIdComponent"/> as a string.
    /// </summary>
    /// <param name="component">The component to encode.</param>
    /// <returns>The component encoded as a string.</returns>
    public string Encode(IDeviceIdComponent component)
    {
        return component.GetValue() ?? string.Empty;
    }
}
