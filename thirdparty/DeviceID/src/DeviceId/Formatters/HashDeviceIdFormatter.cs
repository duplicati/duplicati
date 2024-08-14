using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DeviceId.Encoders;

namespace DeviceId.Formatters;

/// <summary>
/// An implementation of <see cref="IDeviceIdFormatter"/> that combines the components into a hash.
/// </summary>
public class HashDeviceIdFormatter : IDeviceIdFormatter
{
    /// <summary>
    /// The <see cref="IByteArrayHasher"/> to use to hash the device ID.
    /// </summary>
    private readonly IByteArrayHasher _byteArrayHasher;

    /// <summary>
    /// The <see cref="IByteArrayEncoder"/> to use to encode the resulting hash.
    /// </summary>
    private readonly IByteArrayEncoder _byteArrayEncoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="HashDeviceIdFormatter"/> class.
    /// </summary>
    /// <param name="byteArrayHasher">The <see cref="IByteArrayHasher"/> to use to hash the device ID.</param>
    /// <param name="byteArrayEncoder">The <see cref="IByteArrayEncoder"/> to use to encode the resulting hash.</param>
    public HashDeviceIdFormatter(IByteArrayHasher byteArrayHasher, IByteArrayEncoder byteArrayEncoder)
    {
        _byteArrayHasher = byteArrayHasher ?? throw new ArgumentNullException(nameof(byteArrayHasher));
        _byteArrayEncoder = byteArrayEncoder ?? throw new ArgumentNullException(nameof(byteArrayEncoder));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HashDeviceIdFormatter"/> class.
    /// </summary>
    /// <param name="hashAlgorithm">A function that returns the hash algorithm to use.</param>
    /// <param name="byteArrayEncoder">The <see cref="IByteArrayEncoder"/> to use to encode the resulting hash.</param>
    public HashDeviceIdFormatter(Func<HashAlgorithm> hashAlgorithm, IByteArrayEncoder byteArrayEncoder)
        : this(new ByteArrayHasher(hashAlgorithm ?? throw new ArgumentNullException(nameof(hashAlgorithm))), byteArrayEncoder) { }

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

        var value = string.Join(",", components.OrderBy(x => x.Key).Select(x => x.Value.GetValue()).ToArray());
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = _byteArrayHasher.Hash(bytes);
        return _byteArrayEncoder.Encode(hash);
    }
}
