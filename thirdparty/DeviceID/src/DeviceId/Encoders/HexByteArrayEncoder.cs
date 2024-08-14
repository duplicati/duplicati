using System;
using System.Text;

namespace DeviceId.Encoders;

/// <summary>
/// An implementation of <see cref="IByteArrayEncoder"/> that encodes byte arrays as hex strings.
/// </summary>
public class HexByteArrayEncoder : IByteArrayEncoder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HexByteArrayEncoder"/> class.
    /// </summary>
    public HexByteArrayEncoder() { }

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

        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.AppendFormat("{0:x2}", b);
        }

        return sb.ToString();
    }
}
