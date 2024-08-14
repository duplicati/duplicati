using System;
using System.Collections.Generic;
using System.Linq;

namespace DeviceId;

/// <summary>
/// Provides functionality to manage the generation and validation of multiple device identifier formats.
/// </summary>
public class DeviceIdManager
{
    /// <summary>
    /// A dictionary mapping the version numbers to the device ID builders.
    /// </summary>
    private readonly Dictionary<int, DeviceIdBuilder> _builders;

    /// <summary>
    /// The version encoder.
    /// </summary>
    private IDeviceIdVersionEncoder _versionEncoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceIdManager"/> class.
    /// </summary>
    public DeviceIdManager()
    {
        _builders = new Dictionary<int, DeviceIdBuilder>();
        _versionEncoder = new DeviceIdVersionEncoder();
    }

    /// <summary>
    /// Sets the version encoder to use.
    /// </summary>
    /// <param name="encoder">The version encoder.</param>
    /// <returns>This <see cref="DeviceIdManager"/> instance.</returns>
    public DeviceIdManager WithVersionEncoder(IDeviceIdVersionEncoder encoder)
    {
        _versionEncoder = encoder;

        return this;
    }

    /// <summary>
    /// Adds a device identifier builder with the specified version number.
    /// </summary>
    /// <param name="version">The version number.</param>
    /// <param name="builder">The device identifier builder.</param>
    /// <returns>This <see cref="DeviceIdManager"/> instance.</returns>
    public DeviceIdManager AddBuilder(int version, DeviceIdBuilder builder)
    {
        _builders[version] = builder;

        return this;
    }

    /// <summary>
    /// Adds a device identifier builder with the specified version number.
    /// </summary>
    /// <param name="version">The version number.</param>
    /// <param name="builderConfiguration">The device identifier builder configuration.</param>
    /// <returns>This <see cref="DeviceIdManager"/> instance.</returns>
    public DeviceIdManager AddBuilder(int version, Action<DeviceIdBuilder> builderConfiguration)
    {
        var builder = new DeviceIdBuilder();
        builderConfiguration?.Invoke(builder);

        return AddBuilder(version, builder);
    }

    /// <summary>
    /// Gets the device identifier from the builder with the highest version number.
    /// </summary>
    /// <returns>A device identifier.</returns>
    public string GetDeviceId()
    {
        if (_builders.Count > 0)
        {
            var version = _builders.Keys.Max(); // Always use the latest version.
            return GetDeviceId(version);
        }

        return null;
    }

    /// <summary>
    /// Gets the device identifier from the builder with the specified version number.
    /// </summary>
    /// <param name="version">The version number.</param>
    /// <returns>A device identifier.</returns>
    public string GetDeviceId(int version)
    {
        if (_builders.TryGetValue(version, out var builder))
        {
            var deviceId = builder.ToString();
            return _versionEncoder.Encode(deviceId, version);
        }

        return null;
    }

    /// <summary>
    /// Returns a value indicating whether the specified value is a valid identifier for this device.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <returns>true if the value is a valid identifier for this device; otherwise, false.</returns>
    public bool Validate(string value)
    {
        // If no builders, nothing is valid.
        if (_builders.Count == 0)
        {
            return false;
        }

        // If we can decode the version/deviceId, then we just need to generate the current device identifier
        // with the specified builder, and test against that.
        if (_versionEncoder.TryDecode(value, out var versionedDeviceId, out var version))
        {
            if (_builders.TryGetValue(version, out var builder))
            {
                return string.Equals(builder.ToString(), versionedDeviceId);
            }
        }

        // If we couldn't decode the version/deviceId, there is still a chance!
        // We can treat the entire input as a raw unversioned device ID (perhaps generated before the
        // dev decided to use this class). We then assume that the FIRST builder known to us is the
        // same builder that generated the aforementioned raw unversioned device ID. This gives us a
        // bit of backwards compatibility.

        var firstVersion = _builders.Keys.Min();
        var firstBuilder = _builders[firstVersion];
        return string.Equals(firstBuilder.ToString(), value);
    }

    /// <summary>
    /// Returns a string representation of the device identifier generated from the builder with the highest version number.
    /// </summary>
    /// <returns>A device identifier.</returns>
    public override string ToString()
    {
        return GetDeviceId();
    }
}
