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

#nullable enable

using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Logging;

namespace Duplicati.Library.Utility;

/// <summary>
/// Reads the body of an HTTP response as a forward-only stream, straight from the
/// connection, so a body of any size is read with a fixed amount of memory and no
/// temporary file. When the connection fails, stalls, or ends before the body is
/// complete, the body is re-requested from the byte where reading stopped with a
/// <c>Range</c> header, and reading continues as if nothing happened. A body that
/// cannot be completed within the resume budget fails with an <see cref="IOException"/>,
/// never with a silent end of stream, so a caller that stores what it read never stores
/// a truncated copy.
/// </summary>
/// <remarks>
/// The stream does not know how to send a request: the caller supplies an
/// <see cref="OpenResponseDelegate"/> that sends a request for the body from a byte offset
/// and returns the response with its headers read. That keeps authentication, throttling
/// and retries of the request itself with the API client that owns them, while this stream
/// takes care of what happens after the headers: the long read of the body.
///
/// The length comes from the caller, when the metadata the body belongs to states it, or
/// from the <c>Content-Length</c> header of the first response. When neither states it,
/// <see cref="Length"/> throws <see cref="NotSupportedException"/>, and
/// <see cref="HasKnownLength"/> says so; an end of stream is then taken as the end of the
/// body, since nothing says otherwise.
/// </remarks>
public sealed class ResumableHttpReadStream : Stream
{
    /// <summary>
    /// Sends a request for the body from a byte offset and returns the response with its
    /// headers read, as with <see cref="HttpCompletionOption.ResponseHeadersRead"/>. For an
    /// offset of zero the request is a plain request for the body; for any other offset the
    /// request must carry <c>Range: bytes=&lt;offset&gt;-</c>, and the response is expected
    /// to be <c>206 Partial Content</c>. A response that is not successful is reported by
    /// throwing; a response that is returned is owned by the stream.
    /// </summary>
    /// <param name="offset">The byte offset to read the body from</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The response, with its headers read</returns>
    public delegate Task<HttpResponseMessage> OpenResponseDelegate(long offset, CancellationToken cancellationToken);

    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<ResumableHttpReadStream>();

    /// <summary>
    /// Thrown when the server answered a resume request, but not with the rest of the body:
    /// it ignored the range, or the body changed meanwhile. Asking again would get the same
    /// answer, so the download is given up on at once, unlike a connection failure
    /// </summary>
    private sealed class ResumeRefusedException(string message) : IOException(message);

    /// <summary>
    /// The number of times reading may be resumed after a failure before the body is
    /// given up on, when no other value is given
    /// </summary>
    public const int DefaultMaxResumeAttempts = 5;

    /// <summary>
    /// The pause before the first resume, when no other pause is given. Each following
    /// pause is twice the previous one, up to <see cref="DefaultMaxResumeDelay"/>.
    /// </summary>
    public static readonly TimeSpan DefaultInitialResumeDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The longest pause before a resume, when no other pause is given
    /// </summary>
    public static readonly TimeSpan DefaultMaxResumeDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Opens the response for a byte offset
    /// </summary>
    private readonly OpenResponseDelegate _open;
    /// <summary>
    /// The length the caller stated, or <c>null</c> if it stated none
    /// </summary>
    private readonly long? _statedLength;
    /// <summary>
    /// How long a single read may wait for bytes before the connection is taken as stalled
    /// </summary>
    private readonly TimeSpan _readTimeout;
    /// <summary>
    /// The number of times reading may be resumed after a failure
    /// </summary>
    private readonly int _maxResumeAttempts;
    /// <summary>
    /// Gives the pause before a resume attempt, numbered from one
    /// </summary>
    private readonly Func<int, TimeSpan> _resumeDelay;
    /// <summary>
    /// What is being read, for log and error messages
    /// </summary>
    private readonly string _description;

    /// <summary>
    /// The response the body is being read from, or <c>null</c> when no response is open
    /// </summary>
    private HttpResponseMessage? _response;
    /// <summary>
    /// The body of <see cref="_response"/>, or <c>null</c> when no response is open
    /// </summary>
    private Stream? _body;
    /// <summary>
    /// The number of bytes handed out so far, which is where a resume starts from
    /// </summary>
    private long _position;
    /// <summary>
    /// The length of the body, once known
    /// </summary>
    private long? _length;
    /// <summary>
    /// The number of resumes since the last read that returned bytes
    /// </summary>
    private int _resumeAttempts;
    /// <summary>
    /// Whether the body was read to its end
    /// </summary>
    private bool _completed;
    /// <summary>
    /// Whether the stream was disposed
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Creates a stream over an HTTP body. Nothing is sent until the stream is read or
    /// <see cref="EnsureOpenedAsync"/> is called.
    /// </summary>
    /// <param name="open">Sends a request for the body from a byte offset</param>
    /// <param name="expectedLength">The length of the body as the caller knows it, or <c>null</c> to take it from the response</param>
    /// <param name="readTimeout">How long a single read may wait for bytes before the connection is taken as stalled, or <see cref="Timeout.InfiniteTimeSpan"/> to wait forever</param>
    /// <param name="description">What is being read, for log and error messages</param>
    /// <param name="maxResumeAttempts">The number of times reading may be resumed after a failure before the body is given up on</param>
    /// <param name="resumeDelay">Gives the pause before a resume attempt, numbered from one, or <c>null</c> for an exponential pause</param>
    public ResumableHttpReadStream(OpenResponseDelegate open, long? expectedLength, TimeSpan readTimeout, string description, int maxResumeAttempts = DefaultMaxResumeAttempts, Func<int, TimeSpan>? resumeDelay = null)
    {
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _statedLength = expectedLength is >= 0 ? expectedLength : null;
        _length = _statedLength;
        _readTimeout = readTimeout;
        _maxResumeAttempts = Math.Max(0, maxResumeAttempts);
        _resumeDelay = resumeDelay ?? DefaultResumeDelay;
        _description = description ?? "the response";
    }

    /// <summary>
    /// The exponential pause used when no other pause is given
    /// </summary>
    /// <param name="attempt">The resume attempt, numbered from one</param>
    /// <returns>The pause</returns>
    private static TimeSpan DefaultResumeDelay(int attempt)
    {
        var seconds = DefaultInitialResumeDelay.TotalSeconds * Math.Pow(2, Math.Max(0, attempt - 1));
        return TimeSpan.FromSeconds(Math.Min(seconds, DefaultMaxResumeDelay.TotalSeconds));
    }

    /// <summary>
    /// Whether the length of the body is known, from the caller or from the first response
    /// </summary>
    public bool HasKnownLength => _length.HasValue;

    /// <summary>
    /// Sends the first request, so a failure to get the body at all is reported here,
    /// where the caller can act on it, instead of on the first read. After this,
    /// <see cref="HasKnownLength"/> reflects the response headers.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    public async Task EnsureOpenedAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_body == null && !_completed)
            await OpenAtAsync(_position, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens the response at an offset, checking that the server answered the request that
    /// was made: a full body for offset zero, the rest of the body for any other offset
    /// </summary>
    /// <param name="offset">The byte offset to read from</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    private async Task OpenAtAsync(long offset, CancellationToken cancellationToken)
    {
        CloseResponse();

        var response = await _open(offset, cancellationToken).ConfigureAwait(false)
            ?? throw new IOException($"No response was returned for {_description}");

        try
        {
            if (offset == 0)
            {
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"The request for {_description} failed with status {(int)response.StatusCode} {response.StatusCode}", null, response.StatusCode);

                // The server's word on the length wins over the caller's: it is the length of
                // what is actually sent, where the caller's is what the listing said at the time
                var contentLength = response.Content?.Headers.ContentLength;
                if (contentLength is >= 0)
                    _length = contentLength;
            }
            else
            {
                if (response.StatusCode != HttpStatusCode.PartialContent)
                    throw new ResumeRefusedException($"Resuming {_description} at offset {offset} was not honored: the server answered {(int)response.StatusCode} {response.StatusCode} instead of 206 Partial Content");

                var range = response.Content?.Headers.ContentRange;
                if (range != null)
                {
                    if (range.HasRange && range.From != offset)
                        throw new ResumeRefusedException($"Resuming {_description} at offset {offset} was not honored: the server answered with bytes from offset {range.From}");
                    if (range.HasLength && _length.HasValue && range.Length != _length.Value)
                        throw new ResumeRefusedException($"The length of {_description} changed from {_length.Value} to {range.Length} while it was being read");
                    if (range.HasLength && !_length.HasValue)
                        _length = range.Length;
                }
            }

            _body = response.Content == null
                ? Stream.Null
                : await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            _response = response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Releases the open response, if any
    /// </summary>
    private void CloseResponse()
    {
        var body = _body;
        var response = _response;
        _body = null;
        _response = null;

        try { body?.Dispose(); }
        catch { }
        try { response?.Dispose(); }
        catch { }
    }

    /// <summary>
    /// Whether an exception is one that resuming can get past: the connection failed or
    /// stalled, as opposed to the caller cancelling or the server refusing the request
    /// </summary>
    /// <param name="ex">The exception</param>
    /// <param name="cancellationToken">The caller's cancellation token</param>
    /// <returns><c>true</c> if reading should be resumed</returns>
    private static bool IsTransient(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        return ex switch
        {
            // The server answered the resume with something else; asking again gets the same answer
            ResumeRefusedException => false,
            // A cancellation that is not the caller's is the read timeout, or the client's own timeout
            OperationCanceledException => true,
            TimeoutException => true,
            // A status code means the server answered the request; that is for the caller's client to handle
            HttpRequestException hre => hre.StatusCode == null,
            IOException => true,
            SocketException => true,
            _ => false,
        };
    }

    /// <summary>
    /// Records a failure, gives up if the resume budget is spent, and otherwise pauses
    /// before the caller re-opens the response
    /// </summary>
    /// <param name="ex">The failure</param>
    /// <param name="what">What failed, for the messages</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    private async Task PrepareResumeAsync(Exception ex, string what, CancellationToken cancellationToken)
    {
        CloseResponse();

        _resumeAttempts++;
        if (_resumeAttempts > _maxResumeAttempts)
            throw new IOException($"{what} {_description} at offset {_position} failed, and it was given up on after {_maxResumeAttempts} resume attempt(s)", ex);

        Log.WriteVerboseMessage(LOGTAG, "ResumingHttpRead", ex, "{0} {1} at offset {2} failed, resuming in attempt {3} of {4}", what, _description, _position, _resumeAttempts, _maxResumeAttempts);

        var delay = _resumeDelay(_resumeAttempts);
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (buffer.Length == 0)
            return 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_completed)
                return 0;

            if (_body == null)
            {
                try
                {
                    await OpenAtAsync(_position, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsTransient(ex, cancellationToken))
                {
                    await PrepareResumeAsync(ex, "Opening", cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            int read;
            try
            {
                read = await ReadWithTimeoutAsync(_body!, buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken))
            {
                await PrepareResumeAsync(ex, "Reading", cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (read > 0)
            {
                _position += read;
                _resumeAttempts = 0;

                if (_length.HasValue && _position > _length.Value)
                    throw new IOException($"The server sent more than the {_length.Value} bytes it stated for {_description}");

                return read;
            }

            // The body ended. With a known length, an early end is a failure of the
            // connection, which resuming gets past, and never the end of the body
            if (_length.HasValue && _position < _length.Value)
            {
                await PrepareResumeAsync(new IOException($"The body of {_description} ended after {_position} of {_length.Value} bytes"), "Reading", cancellationToken).ConfigureAwait(false);
                continue;
            }

            _completed = true;
            CloseResponse();
            return 0;
        }
    }

    /// <summary>
    /// Reads from the body, taking a read that waits longer than the read timeout as a
    /// stalled connection
    /// </summary>
    /// <param name="body">The body to read from</param>
    /// <param name="buffer">The buffer to read into</param>
    /// <param name="cancellationToken">The caller's cancellation token</param>
    /// <returns>The number of bytes read</returns>
    private async ValueTask<int> ReadWithTimeoutAsync(Stream body, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (_readTimeout <= TimeSpan.Zero || _readTimeout == Timeout.InfiniteTimeSpan)
            return await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_readTimeout);
        try
        {
            return await body.ReadAsync(buffer, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No bytes were received for {_description} within {_readTimeout}");
        }
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(new Memory<byte>(buffer, offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var read = Read(rented, 0, buffer.Length);
            new ReadOnlySpan<byte>(rented, 0, read).CopyTo(buffer);
            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <summary>
    /// The length of the body, when the caller or the first response stated it
    /// </summary>
    /// <exception cref="NotSupportedException">The length is not known</exception>
    public override long Length
        => _length ?? throw new NotSupportedException($"The length of {_description} is not known");

    /// <inheritdoc />
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush() { }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>
    /// Throws if the stream was disposed
    /// </summary>
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            CloseResponse();
        }

        base.Dispose(disposing);
    }
}
