using System;
using System.Buffers;

namespace Duplicati.Proprietary.DiskImage.Disk;

/// <summary>
/// A disposable struct that rents a byte array from the shared ArrayPool and returns it when disposed.
/// This eliminates GC pressure from repeated large byte[] allocations, especially for buffers >= 85 KB
/// which would otherwise land on the Large Object Heap and cause Gen2 GC pauses.
/// </summary>
internal readonly struct PooledBuffer : IDisposable
{
    private readonly byte[] _array;

    /// <summary>
    /// Gets the actual length of data requested (may be less than the array length).
    /// </summary>
    public readonly int Length;

    /// <summary>
    /// Gets the underlying array. Note: the array may be larger than <see cref="Length"/>.
    /// Always use Length when accessing data, not _array.Length.
    /// </summary>
    public byte[] Array => _array;

    /// <summary>
    /// Gets a Memory<byte> view of the buffer sized to the requested length.
    /// </summary>
    public Memory<byte> Memory => _array.AsMemory(0, Length);

    /// <summary>
    /// Gets a Span<byte> view of the buffer sized to the requested length.
    /// </summary>
    public Span<byte> Span => _array.AsSpan(0, Length);

    /// <summary>
    /// Rents a buffer from the shared ArrayPool.
    /// </summary>
    /// <param name="size">The minimum size of the buffer needed.</param>
    public PooledBuffer(int size)
    {
        _array = ArrayPool<byte>.Shared.Rent(size);
        Length = size;
    }

    /// <summary>
    /// Returns the buffer to the shared ArrayPool.
    /// </summary>
    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_array);
    }
}
