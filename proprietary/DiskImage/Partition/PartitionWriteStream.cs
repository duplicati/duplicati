// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Disk;

namespace Duplicati.Proprietary.DiskImage.Partition;

/// <summary>
/// A stream that buffers writes to a partition and flushes to an <see cref="IRawDisk"/> on dispose.
/// Uses ArrayPool to avoid LOH allocations for large buffers.
/// </summary>
internal class PartitionWriteStream : Stream
{
    private readonly IRawDisk _disk;
    private readonly long _startOffset;
    private readonly long _maxSize;
    private readonly byte[] _buffer;
    private readonly bool _bufferRented;
    private long _position;
    private long _length;
    private bool _disposed = false;

    public PartitionWriteStream(IRawDisk disk, long startOffset, long maxSize)
    {
        _disk = disk;
        _startOffset = startOffset;
        _maxSize = maxSize;
        // Rent buffer from ArrayPool to avoid LOH allocations for large buffers
        _buffer = ArrayPool<byte>.Shared.Rent((int)maxSize);
        _bufferRented = true;
        _position = 0;
        _length = 0;
    }

    public override bool CanRead => false;
    public override bool CanSeek => true;
    public override bool CanWrite => true;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set
        {
            if (value < 0 || value > _maxSize)
                throw new System.ArgumentOutOfRangeException(nameof(value));
            _position = value;
        }
    }

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new System.NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin)
    {
        long newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new System.ArgumentOutOfRangeException(nameof(origin))
        };
        if (newPosition < 0 || newPosition > _maxSize)
            throw new IOException("Cannot seek beyond partition size.");
        _position = newPosition;
        return _position;
    }
    public override void SetLength(long value)
    {
        if (value > _maxSize)
            throw new IOException($"Cannot write beyond partition size of {_maxSize} bytes.");
        _length = value;
        if (_position > _length)
            _position = _length;
    }
    public override void Write(byte[] buffer, int offset, int count)
    {
        if (_position + count > _maxSize)
            throw new IOException($"Cannot write beyond partition size of {_maxSize} bytes.");
        System.Buffer.BlockCopy(buffer, offset, _buffer, (int)_position, count);
        _position += count;
        if (_position > _length)
            _length = _position;
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Write all buffered data to disk
                if (_length > 0)
                {
                    _disk.WriteBytesAsync(_startOffset, _buffer.AsMemory(0, (int)_length), CancellationToken.None).GetAwaiter().GetResult();
                }
                // Return the rented buffer to the pool
                if (_bufferRented)
                {
                    ArrayPool<byte>.Shared.Return(_buffer);
                }
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}
