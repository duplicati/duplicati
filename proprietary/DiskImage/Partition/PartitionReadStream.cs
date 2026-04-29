// Copyright (C) 2026, The Duplicati Team
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
/// A stream that buffers reads from a partition.
/// Uses ArrayPool to avoid LOH allocations for large buffers.
/// </summary>
internal class PartitionReadStream : Stream
{
    private readonly IRawDisk _disk;
    /// <summary>
    /// Starting offset of the partition on the disk.
    /// </summary>
    private readonly long _startOffset;
    private readonly long _length;
    private long _position;
    private bool _disposed = false;

    public PartitionReadStream(IRawDisk disk, long startOffset, long length)
    {
        _disk = disk;
        _startOffset = startOffset;
        _length = length;
        _position = 0;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set
        {
            if (value < 0 || value > _length)
                throw new System.ArgumentOutOfRangeException(nameof(value));
            _position = value;
        }
    }

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    }
    public async override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PartitionReadStream));
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || offset > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0 || offset + count > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (_position >= _length)
            return 0; // EOF

        long bytesToRead = Math.Min(count, _length - _position);
        if (bytesToRead > 0)
        {
            return await _disk.ReadBytesAsync(_startOffset + _position, buffer.AsMemory(offset, (int)bytesToRead), token).ConfigureAwait(false);
        }
        return 0;
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        long newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new System.ArgumentOutOfRangeException(nameof(origin))
        };
        if (newPosition < 0 || newPosition > _length)
            throw new IOException("Cannot seek beyond partition size.");
        _position = newPosition;
        return _position;
    }
    public override void SetLength(long value)
    {
        throw new NotSupportedException("SetLength is not supported on PartitionReadStream.");
    }
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("Write is not supported on PartitionReadStream.");
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
            _disposed = true;

        base.Dispose(disposing);
    }
}
