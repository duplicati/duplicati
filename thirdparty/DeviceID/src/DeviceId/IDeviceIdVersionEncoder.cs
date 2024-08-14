namespace DeviceId;

/// <summary>
/// Provides functionality to encode/decode device identifiers and version numbers to/from strings.
/// </summary>
public interface IDeviceIdVersionEncoder
{
    /// <summary>
    /// Encodes a device identifier and a version number into a string.
    /// </summary>
    /// <param name="deviceId">The device identifier.</param>
    /// <param name="version">The version number.</param>
    /// <returns>An encoded string comprising a device identifier and a version number.</returns>
    string Encode(string deviceId, int version);

    /// <summary>
    /// Attempts to decode a string into a device identifier and a version number.
    /// </summary>
    /// <param name="value">The string to decode.</param>
    /// <param name="deviceId">If successful, the device identifier; otherwise, null.</param>
    /// <param name="version">If successful, the version number; otherwise, 0.</param>
    /// <returns>true if the string was decoded successfully; otherwise, false.</returns>
    bool TryDecode(string value, out string deviceId, out int version);
}
