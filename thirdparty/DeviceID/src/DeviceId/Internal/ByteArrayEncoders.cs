using DeviceId.Encoders;

namespace DeviceId.Internal;

/// <summary>
/// Static instances of the various byte array encoders.
/// </summary>
internal static class ByteArrayEncoders
{
    /// <summary>
    /// Gets a base-32 byte array encoder that uses the Crockkford alphabet.
    /// </summary>
    public static Base32ByteArrayEncoder Base32Crockford { get; } = new Base32ByteArrayEncoder(Base32ByteArrayEncoder.CrockfordAlphabet);
}
