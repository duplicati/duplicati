using System;
using System.Buffers;
using System.IO;

namespace Duplicati.Proprietary.DiskImage.Disk;

/// <summary>
/// A read-only stream that wraps a pooled byte array and returns it to the pool when disposed.
/// </summary>
internal sealed class PooledMemoryStream : Stream
{
    private byte[]? _buffer;
    private readonly int _length;
    private int _position;

    public PooledMemoryStream(byte[] buffer, int length)
    {
        _buffer = buffer;
        _length = length;
        _position = 0;
    }

    public override bool CanRead => _buffer != null;

    public override bool CanSeek => _buffer != null;

    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set
        {
            if (value < 0 || value > _length)
                throw new ArgumentOutOfRangeException(nameof(value));
            _position = (int)value;
        }
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_buffer == null)
            throw new ObjectDisposedException(nameof(PooledMemoryStream));

        int remaining = _length - _position;
        int toRead = Math.Min(count, remaining);

        if (toRead <= 0)
            return 0;

        Buffer.BlockCopy(_buffer, _position, buffer, offset, toRead);
        _position += toRead;
        return toRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (newPosition < 0 || newPosition > _length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        _position = (int)newPosition;
        return _position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && _buffer != null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }
        base.Dispose(disposing);
    }
}
