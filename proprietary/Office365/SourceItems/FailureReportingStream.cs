// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.Office365.SourceItems;

/// <summary>
/// A read-only stream wrapper that reports the first exception thrown while reading, so
/// a download that fails after it was opened is noticed by the change tracking. The
/// backup reads the content long after the entry was listed, and a failed download drops
/// the item from the backup without the listing knowing.
/// </summary>
/// <param name="inner">The stream to read from</param>
/// <param name="onFailure">Called once, on the first read failure</param>
internal sealed class FailureReportingStream(Stream inner, Action onFailure) : Stream
{
    /// <summary>
    /// Whether the failure was reported
    /// </summary>
    private bool _reported;

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
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
