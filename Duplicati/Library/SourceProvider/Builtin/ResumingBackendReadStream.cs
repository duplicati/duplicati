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

using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;
using Duplicati.Library.Interface;

namespace Duplicati.Library.SourceProvider;

/// <summary>
/// A read-only stream over a file on a streaming backend.
/// The download starts on the first read. If it fails or ends before the reported size,
/// the download is started again and continues from the bytes the caller has already received,
/// so a connection the server closed while the caller was busy elsewhere does not fail the read.
/// A backend that implements <see cref="IResumeEnabledBackend"/> is asked for the remaining bytes
/// under the version token it handed out. Any other backend delivers the file from the start again,
/// and the bytes already handed out are read past and checked against a hash of what the caller received.
/// </summary>
internal sealed class ResumingBackendReadStream : Stream
{
    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<ResumingBackendReadStream>();

    /// <summary>
    /// The size of the buffer used to read past bytes already delivered
    /// </summary>
    private const int SKIP_BUFFER_SIZE = 64 * 1024;

    /// <summary>
    /// The longest time to wait for an abandoned download to notice it was cancelled
    /// </summary>
    private static readonly TimeSpan ABANDON_WAIT = TimeSpan.FromSeconds(10);

    /// <summary>
    /// One attempt at downloading the remote file
    /// </summary>
    /// <param name="stream">The stream delivering the remote data</param>
    /// <param name="transfer">The task running the backend download into the pipe behind <paramref name="stream"/>, or null if the backend handed out <paramref name="stream"/> itself</param>
    /// <param name="cancellation">The token source that stops <paramref name="transfer"/>, or null if there is none</param>
    private sealed class Download(Stream stream, Task? transfer, CancellationTokenSource? cancellation) : IAsyncDisposable
    {
        /// <summary>
        /// The stream delivering the remote data
        /// </summary>
        public Stream Stream => stream;

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            cancellation?.Cancel();
            stream.Dispose();
            if (transfer != null)
                await Task.WhenAny(transfer, Task.Delay(ABANDON_WAIT)).ConfigureAwait(false);
            cancellation?.Dispose();
        }
    }

    /// <summary>
    /// The backend the file is read from
    /// </summary>
    private readonly IStreamingBackend backend;

    /// <summary>
    /// The backend, if it can continue a download from an offset
    /// </summary>
    private readonly IResumeEnabledBackend? resumeEnabledBackend;

    /// <summary>
    /// The name of the file on the backend
    /// </summary>
    private readonly string remotename;

    /// <summary>
    /// The path of the file as shown to the user
    /// </summary>
    private readonly string displayPath;

    /// <summary>
    /// The size of the file as reported by the backend listing
    /// </summary>
    private readonly long length;

    /// <summary>
    /// The number of times the download may be started again
    /// </summary>
    private readonly int maxResumeAttempts;

    /// <summary>
    /// The time to wait before the download is started again
    /// </summary>
    private readonly TimeSpan resumeDelay;

    /// <summary>
    /// Cancellation for everything this stream started; cancelled when the stream is disposed
    /// </summary>
    private readonly CancellationTokenSource lifetime;

    /// <summary>
    /// A hash of the bytes handed to the caller, used to check that a restarted download delivers the same bytes.
    /// Null when the backend can resume from an offset, as the version token does that check.
    /// </summary>
    private readonly IncrementalHash? deliveredHash;

    /// <summary>
    /// The download currently delivering data, or null if none is running
    /// </summary>
    private Download? current;

    /// <summary>
    /// The version token of the remote file, once a resume-enabled backend has handed one out.
    /// Null until the first download is opened.
    /// </summary>
    private string? etag;

    /// <summary>
    /// The number of bytes handed to the caller
    /// </summary>
    private long position;

    /// <summary>
    /// The number of times the download has been started again
    /// </summary>
    private int resumeAttempts;

    /// <summary>
    /// True once the stream is disposed
    /// </summary>
    private bool disposed;

    /// <summary>
    /// Creates a new stream over a file on a streaming backend
    /// </summary>
    /// <param name="backend">The backend the file is read from</param>
    /// <param name="remotename">The name of the file on the backend</param>
    /// <param name="displayPath">The path of the file as shown to the user</param>
    /// <param name="length">The size of the file as reported by the backend listing</param>
    /// <param name="maxResumeAttempts">The number of times the download may be started again</param>
    /// <param name="resumeDelay">The time to wait before the download is started again</param>
    /// <param name="cancellationToken">A token that cancels the download</param>
    public ResumingBackendReadStream(IStreamingBackend backend, string remotename, string displayPath, long length, int maxResumeAttempts, TimeSpan resumeDelay, CancellationToken cancellationToken)
    {
        this.backend = backend;
        this.resumeEnabledBackend = backend as IResumeEnabledBackend;
        this.remotename = remotename;
        this.displayPath = displayPath;
        this.length = length;
        this.maxResumeAttempts = maxResumeAttempts;
        this.resumeDelay = resumeDelay;
        this.lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        this.deliveredHash = resumeEnabledBackend == null
            ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            : null;
    }

    /// <summary>
    /// The number of times the download has been started again
    /// </summary>
    public int ResumeCount => resumeAttempts;

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => length;

    /// <inheritdoc/>
    public override long Position
    {
        get => position;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Flush() { }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (buffer.IsEmpty)
            return 0;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        var token = linked.Token;

        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                current ??= await OpenAsync(token).ConfigureAwait(false);
                var read = await current.Stream.ReadAsync(buffer, token).ConfigureAwait(false);

                // Some backends do not report an error if the connection is lost during a download,
                // so an early end-of-stream is treated as a failed download instead of storing a truncated file
                if (read == 0 && position < length)
                    throw new IOException($"The stream for {displayPath} ended after {position} bytes, but the reported size is {length} bytes");

                deliveredHash?.AppendData(buffer.Span[..read]);
                position += read;
                return read;
            }
            catch (Exception ex) when (ex is not RemoteFileChangedException && !token.IsCancellationRequested)
            {
                await AbandonCurrentAsync().ConfigureAwait(false);
                if (resumeAttempts >= maxResumeAttempts)
                    throw;

                resumeAttempts++;
                Logging.Log.WriteRetryMessage(LOGTAG, "ResumingDownload", ex, "Download of {0} failed after {1} of {2} bytes, starting it again from that point (attempt {3} of {4})", displayPath, position, length, resumeAttempts, maxResumeAttempts);
                if (resumeDelay > TimeSpan.Zero)
                    await Task.Delay(resumeDelay, token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Starts a download that delivers the bytes from <see cref="position"/> onwards
    /// </summary>
    /// <param name="token">The cancellation token</param>
    /// <returns>The download</returns>
    private async Task<Download> OpenAsync(CancellationToken token)
    {
        if (resumeEnabledBackend != null)
        {
            var (stream, tag) = await resumeEnabledBackend.GetResumableStreamAsync(remotename, position, etag, token).ConfigureAwait(false);
            etag = tag;
            return new Download(stream, null, null);
        }

        var download = StartPipedDownload();
        try
        {
            await SkipDeliveredBytesAsync(download.Stream, token).ConfigureAwait(false);
            return download;
        }
        catch
        {
            await download.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Starts the backend download into a pipe and returns the reading end
    /// </summary>
    /// <returns>The download</returns>
    private Download StartPipedDownload()
    {
        var pipe = new Pipe();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = cancellation.Token;
        var transfer = Task.Run(async () =>
        {
            try
            {
                await backend.GetAsync(remotename, pipe.Writer.AsStream(), token).ConfigureAwait(false);
                await pipe.Writer.CompleteAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The reader sees the exception on its next read, and decides whether to start again
                await pipe.Writer.CompleteAsync(ex).ConfigureAwait(false);
            }
        }, token);

        return new Download(pipe.Reader.AsStream(), transfer, cancellation);
    }

    /// <summary>
    /// Reads past the bytes already handed to the caller, and checks that they are the same bytes
    /// </summary>
    /// <param name="stream">The stream delivering the file from the start</param>
    /// <param name="token">The cancellation token</param>
    private async Task SkipDeliveredBytesAsync(Stream stream, CancellationToken token)
    {
        if (position == 0)
            return;

        using var skippedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(SKIP_BUFFER_SIZE);
        try
        {
            long skipped = 0;
            while (skipped < position)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, position - skipped)), token).ConfigureAwait(false);
                if (read == 0)
                    throw new IOException($"The stream for {displayPath} ended after {skipped} bytes, but {position} bytes were already read from it");

                skippedHash.AppendData(buffer, 0, read);
                skipped += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (!skippedHash.GetCurrentHash().AsSpan().SequenceEqual(deliveredHash!.GetCurrentHash()))
            throw new RemoteFileChangedException($"The first {position} bytes of {displayPath} changed while the file was being read");
    }

    /// <summary>
    /// Stops and forgets the current download, if any
    /// </summary>
    private async Task AbandonCurrentAsync()
    {
        var download = current;
        current = null;
        if (download != null)
            await download.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            lifetime.Cancel();
            // The transfer task is not awaited here; it ends on its own once its token is cancelled
            current?.Stream.Dispose();
            current = null;
            deliveredHash?.Dispose();
            lifetime.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            lifetime.Cancel();
            await AbandonCurrentAsync().ConfigureAwait(false);
            deliveredHash?.Dispose();
            lifetime.Dispose();
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }
}
