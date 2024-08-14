using System;

namespace DeviceId.Encoders;

/// <summary>
/// An implementation of <see cref="IByteArrayEncoder"/> that encodes byte arrays as Base64Url strings.
/// </summary>
public class Base64UrlByteArrayEncoder : IByteArrayEncoder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Base64UrlByteArrayEncoder"/> class.
    /// </summary>
    public Base64UrlByteArrayEncoder() { }

    /// <summary>
    /// Encodes the specified byte array as a string.
    /// </summary>
    /// <param name="bytes">The byte array to encode.</param>
    /// <returns>The byte array encoded as a string.</returns>
    public string Encode(byte[] bytes)
    {
        if (bytes == null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
