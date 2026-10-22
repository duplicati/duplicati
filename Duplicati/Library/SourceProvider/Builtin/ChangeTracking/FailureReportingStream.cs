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


namespace Duplicati.Library.SourceProvider.ChangeTracking;

/// <summary>
/// A read-only stream wrapper that reports the first exception thrown while reading, so
/// a download that fails after it was opened is noticed by the change tracking. The
/// backup reads the content long after the entry was listed, and a failed download drops
/// the item from the backup without the listing knowing.
/// </summary>
/// <param name="inner">The stream to read from</param>
/// <param name="onFailure">Called once, on the first read failure</param>
public sealed class FailureReportingStream(Stream inner, Action onFailure) : Stream
{
    /// <summary>
    /// Whether the failure was reported
    /// </summary>
    private bool _reported;

    /// <summary>
    /// Whether the inner stream was released, so the synchronous disposal that the base
    /// class runs after the asynchronous one does not release it again
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Reports a failure, unless it is a cancellation or was reported before
    /// </summary>
    /// <param name="ex">The exception</param>
    private void Report(Exception ex)
    {
        if (_reported || ex is OperationCanceledException)
            return;

        _reported = true;
        onFailure();
    }

    /// <inheritdoc />
    public override bool CanRead => inner.CanRead;
    /// <inheritdoc />
    public override bool CanSeek => inner.CanSeek;
    /// <inheritdoc />
    public override bool CanWrite => false;
    /// <inheritdoc />
    public override long Length => inner.Length;
    /// <inheritdoc />
    public override long Position { get => inner.Position; set => inner.Position = value; }

    /// <inheritdoc />
    public override void Flush() => inner.Flush();
    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        try
        {
            return inner.Read(buffer, offset, count);
        }
        catch (Exception ex)
        {
            Report(ex);
            throw;
        }
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        try
        {
            return inner.Read(buffer);
        }
        catch (Exception ex)
        {
            Report(ex);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        try
        {
            return await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Report(ex);
            throw;
        }
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            return await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Report(ex);
            throw;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            inner.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await inner.DisposeAsync().ConfigureAwait(false);
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
