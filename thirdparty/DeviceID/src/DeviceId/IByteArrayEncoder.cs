namespace DeviceId;

/// <summary>
/// Provides functionality to encode a byte array as a string.
/// </summary>
public interface IByteArrayEncoder
{
    /// <summary>
    /// Encodes the specified byte array as a string.
    /// </summary>
    /// <param name="bytes">The byte array to encode.</param>
    /// <returns>The byte array encoded as a string.</returns>
    string Encode(byte[] bytes);
}
