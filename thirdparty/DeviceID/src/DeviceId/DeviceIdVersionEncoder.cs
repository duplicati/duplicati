using System.Text.RegularExpressions;

namespace DeviceId;

/// <summary>
/// The default implementation of <see cref="IDeviceIdVersionEncoder"/>.
/// </summary>
public class DeviceIdVersionEncoder : IDeviceIdVersionEncoder
{
    /// <summary>
    /// Encodes a device identifier and a version number into a string.
    /// </summary>
    /// <param name="deviceId">The device identifier.</param>
    /// <param name="version">The version number.</param>
    /// <returns>An encoded string comprising a device identifier and a version number.</returns>
    public string Encode(string deviceId, int version)
    {
        return $"${version}${deviceId}";
    }

    /// <summary>
    /// Attempts to decode a string into a device identifier and a version number.
    /// </summary>
    /// <param name="value">The string to decode.</param>
    /// <param name="deviceId">If successful, the device identifier; otherwise, null.</param>
    /// <param name="version">If successful, the version number; otherwise, 0.</param>
    /// <returns>true if the string was decoded successfully; otherwise, false.</returns>
    public bool TryDecode(string value, out string deviceId, out int version)
    {
        var match = Regex.Match(value, "^\\$(.*?)\\$(.*?)$");
        if (match.Success)
        {
            if (int.TryParse(match.Groups[1].Value, out var parsedVersion))
            {
                version = parsedVersion;
                deviceId = match.Groups[2].Value;

                return true;
            }
        }

        version = default;
        deviceId = default;
        return false;
    }
}
