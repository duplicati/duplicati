using System;
using System.Collections.Generic;
using System.Linq;

namespace DeviceId.Formatters;

/// <summary>
/// An implementation of <see cref="IDeviceIdFormatter"/> that combines the components into a concatenated string.
/// </summary>
public class StringDeviceIdFormatter : IDeviceIdFormatter
{
    /// <summary>
    /// The <see cref="IDeviceIdComponentEncoder"/> instance to use to encode individual components.
    /// </summary>
    private readonly IDeviceIdComponentEncoder _encoder;

    /// <summary>
    /// The delimiter to use when concatenating the encoded component values.
    /// </summary>
    private readonly string _delimiter;

    /// <summary>
    /// Initializes a new instance of the <see cref="StringDeviceIdFormatter"/> class.
    /// </summary>
    /// <param name="encoder">The <see cref="IDeviceIdComponentEncoder"/> instance to use to encode individual components.</param>
    public StringDeviceIdFormatter(IDeviceIdComponentEncoder encoder)
        : this(encoder, ".") { }

    /// <summary>
    /// Initializes a new instance of the <see cref="StringDeviceIdFormatter"/> class.
    /// </summary>
    /// <param name="encoder">The <see cref="IDeviceIdComponentEncoder"/> instance to use to encode individual components.</param>
    /// <param name="delimiter">The delimiter to use when concatenating the encoded component values.</param>
    public StringDeviceIdFormatter(IDeviceIdComponentEncoder encoder, string delimiter)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _delimiter = delimiter;
    }

    /// <summary>
    /// Returns the device identifier string created by combining the specified components.
    /// </summary>
    /// <param name="components">A dictionary containing the components.</param>
    /// <returns>The device identifier string.</returns>
    public string GetDeviceId(IDictionary<string, IDeviceIdComponent> components)
    {
        if (components == null)
        {
            throw new ArgumentNullException(nameof(components));
        }

        return string.Join(_delimiter, components.OrderBy(x => x.Key).Select(x => _encoder.Encode(x.Value)).ToArray());
    }
}
