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

#nullable enable

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Snapshots.MacOS;

[SupportedOSPlatform("macOS")]
internal sealed class MacOSPhotosNativeStream : Stream
{
    private readonly MacOSPhotosNative.SafeAssetHandle handle;
    private readonly long length;
    private long position;
    private bool disposed;

    public MacOSPhotosNativeStream(MacOSPhotosNative.SafeAssetHandle handle, long length)
    {
        this.handle = handle ?? throw new ArgumentNullException(nameof(handle));
        this.length = length;
        this.position = 0;
    }

    public override bool CanRead => !disposed;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => disposed ? throw new ObjectDisposedException(nameof(MacOSPhotosNativeStream)) : length;

    public override long Position
    {
        get => disposed ? throw new ObjectDisposedException(nameof(MacOSPhotosNativeStream)) : position;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateReadArguments(buffer, offset, count);
        if (disposed)
            throw new ObjectDisposedException(nameof(MacOSPhotosNativeStream));

        if (count == 0)
            return 0;

        var bytesRead = MacOSPhotosNative.ReadAsset(handle, new Span<byte>(buffer, offset, count));
        position += bytesRead;
        return bytesRead;
    }

    public override int Read(Span<byte> buffer)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(MacOSPhotosNativeStream));

        var bytesRead = MacOSPhotosNative.ReadAsset(handle, buffer);
        position += bytesRead;
        return bytesRead;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (disposed)
            throw new ObjectDisposedException(nameof(MacOSPhotosNativeStream));

        var bytesRead = MacOSPhotosNative.ReadAsset(handle, buffer.Span);
        position += bytesRead;
        return ValueTask.FromResult(bytesRead);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateReadArguments(buffer, offset, count);
        cancellationToken.ThrowIfCancellationRequested();

        if (disposed)
            throw new ObjectDisposedException(nameof(MacOSPhotosNativeStream));

        if (count == 0)
            return Task.FromResult(0);

        var bytesRead = MacOSPhotosNative.ReadAsset(handle, new Span<byte>(buffer, offset, count));
        position += bytesRead;
        return Task.FromResult(bytesRead);
    }

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    public override void Write(ReadOnlySpan<byte> buffer)
        => throw new NotSupportedException();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!disposed)
        {
            if (disposing)
                handle.Dispose();

            disposed = true;
        }

        base.Dispose(disposing);
    }

    private static void ValidateReadArguments(byte[] buffer, int offset, int count)
    {
        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));

        if ((uint)offset > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if ((uint)count > buffer.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(count));
    }
}
