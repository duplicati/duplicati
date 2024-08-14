namespace DeviceId;

/// <summary>
/// Provides functionality to hash a byte array.
/// </summary>
public interface IByteArrayHasher
{
    /// <summary>
    /// Returns a hash of the specified byte array.
    /// </summary>
    /// <param name="bytes">The byte array to hash.</param>
    /// <returns>A hash of the specified byte array.</returns>
    byte[] Hash(byte[] bytes);
}
