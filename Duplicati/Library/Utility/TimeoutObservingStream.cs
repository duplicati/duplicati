// Copyright (C) 2024, The Duplicati Team
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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Utility;

/// <summary>
/// A stream that can observe timeouts for read and write operations.
/// </summary>
public sealed class TimeoutObservingStream : WrappingAsyncStream
{
    /// <summary>
    /// The read timeout.
    /// </summary>
    private int _readTimeout = Timeout.Infinite;
    /// <summary>
    /// The write timeout.
    /// </summary>
    private int _writeTimeout = Timeout.Infinite;

    /// <summary>
    /// The cancellation token source for the timeout.
    /// </summary>
    private readonly CancellationTokenSource _timeoutCts = new();

    /// <summary>
    /// The timer for the read timeout.
    /// </summary>
    private readonly Timer _readTimer;
    /// <summary>
    /// The timer for the write timeout.
    /// </summary>
    private readonly Timer _writeTimer;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeoutObservingStream"/> class.
    /// </summary>
    /// <param name="stream">The stream to wrap.</param>
    public TimeoutObservingStream(Stream stream)
        : base(stream)
    {
        _readTimer = new(_ => _timeoutCts.Cancel());
        _writeTimer = new(_ => _timeoutCts.Cancel());
    }

    /// <summary>
    /// The cancellation token for the timeout.
    /// </summary>
    public CancellationToken TimeoutToken => _timeoutCts.Token;

    /// <inheritdoc/>
    override public bool CanTimeout => true;

    /// <inheritdoc/>
    public override int ReadTimeout
    {
        get => _readTimeout;
        set
        {
            if (value <= 0 && value != Timeout.Infinite)
                throw new ArgumentOutOfRangeException(nameof(value));
            _readTimeout = value;
            _readTimer.Change(value, Timeout.Infinite);
        }
    }

    /// <inheritdoc/>
    public override int WriteTimeout
    {
        get => _writeTimeout;
        set
        {
            if (value <= 0 && value != Timeout.Infinite)
                throw new ArgumentOutOfRangeException(nameof(value));
            _writeTimeout = value;
            _writeTimer.Change(value, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Sets the timeout for both read and write operations to infinite.
    /// </summary>
    public void CancelTimeout()
        => WriteTimeout = ReadTimeout = Timeout.Infinite;

    /// <inheritdoc/>
    override protected async Task<int> ReadImplAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        // If the timer is disable, it is already stopped, otherwise restart it
        if (_readTimeout != Timeout.Infinite)
            _readTimer.Change(_readTimeout, Timeout.Infinite);

        // If there is no timeout and no cancellation token, we can just call the base stream
        if (_readTimeout == Timeout.Infinite && !cancellationToken.CanBeCanceled)
            return await BaseStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);

        // We need a cts here to handle cancellation when not handled by the callee, 
        // but in case the callee *does* observe it, we also link it to the timeout cts
        using var cts = cancellationToken.CanBeCanceled && cancellationToken != TimeoutToken
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _timeoutCts.Token)
            : null;

        // Get the token to use
        var tk = cts == null ? _timeoutCts.Token : cts.Token;

        var task = BaseStream.ReadAsync(buffer, offset, count, tk);

        // If the task is already completed, we can await it without a timeout
        if (task.IsCompleted)
            return await task.ConfigureAwait(false);

        // Run the task and observe the cancellation token
        var res = await Task.WhenAny(Task.Run(() => task, tk)).ConfigureAwait(false);

        // Check if we should throw a timeout exception
        // In case there is a race here, we prefer timeout over any other error for performance reasons
        if (!cancellationToken.IsCancellationRequested && _timeoutCts.IsCancellationRequested)
            throw new TimeoutException();

        // Any exceptions from the task are rethrown here
        return await res.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    override protected async Task WriteImplAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        // If the timer is disable, it is already stopped, otherwise restart it
        if (_writeTimeout != Timeout.Infinite)
            _writeTimer.Change(_writeTimeout, Timeout.Infinite);

        // If there is no timeout and no cancellation token, we can just call the base stream
        if (_writeTimeout == Timeout.Infinite && !cancellationToken.CanBeCanceled)
        {
            await BaseStream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            return;
        }

        // We need a cts here to handle cancellation when not handled by the callee, 
        // but in case the callee *does* observe it, we also link it to the timeout cts
        using var cts = cancellationToken.CanBeCanceled && cancellationToken != TimeoutToken
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _timeoutCts.Token)
            : null;

        // Get the token to use
        var tk = cts == null ? _timeoutCts.Token : cts.Token;

        var task = BaseStream.WriteAsync(buffer, offset, count, tk);

        // If the task is already completed, we can await it without a timeout
        if (task.IsCompleted)
        {
            await task.ConfigureAwait(false);
            return;
        }

        // Run the task and observe the cancellation token
        var res = await Task.WhenAny(Task.Run(() => task, tk)).ConfigureAwait(false);

        // Check if we should throw a timeout exception
        // In case there is a race here, we prefer timeout over any other error for performance reasons
        if (!cancellationToken.IsCancellationRequested && _timeoutCts.IsCancellationRequested)
            throw new TimeoutException();

        // Any exceptions from the task are rethrown here
        await task.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _readTimer.Dispose();
            _writeTimer.Dispose();
            _timeoutCts.Dispose();
        }

        base.Dispose(disposing);
    }
}
