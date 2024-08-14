using System;
using System.Security.Cryptography;

namespace DeviceId.Encoders;

/// <summary>
/// An implementation of <see cref="IByteArrayHasher"/> that uses an arbitrary <see cref="HashAlgorithm"/>.
/// </summary>
public class ByteArrayHasher : IByteArrayHasher
{
    /// <summary>
    /// A function that returns a new <see cref="HashAlgorithm"/> instance.
    /// </summary>
    private readonly Func<HashAlgorithm> _hashAlgorithmFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ByteArrayHasher"/> class.
    /// </summary>
    /// <param name="hashAlgorithmFactory">A function that returns a new <see cref="HashAlgorithm"/> instance.</param>
    public ByteArrayHasher(Func<HashAlgorithm> hashAlgorithmFactory)
    {
        _hashAlgorithmFactory = hashAlgorithmFactory;
    }

    /// <summary>
    /// Returns a hash of the specified byte array.
    /// </summary>
    /// <param name="bytes">The byte array to hash.</param>
    /// <returns>A hash of the specified byte array.</returns>
    public byte[] Hash(byte[] bytes)
    {
        using var hashAlgorithm = _hashAlgorithmFactory.Invoke();
        return hashAlgorithm.ComputeHash(bytes);
    }
}
