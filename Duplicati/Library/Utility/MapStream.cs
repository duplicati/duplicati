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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Duplicati.Library.Utility;

/// <summary>
/// A stream that maps data with an internal buffer.
/// This allows a reader and a writer to share a stream without blocking.
/// </summary>
public class MapStream : Stream
{
    /// <summary>
    /// The channel used to communicate between the reader and writer.
    /// </summary>
    private readonly Channel<byte[]> channel = Channel.CreateBounded<byte[]>(0);

    /// <summary>
    /// The buffer used to store data that has been written but not consumed by the reader.
    /// </summary>
    private Memory<byte> readBuffer = Array.Empty<byte>();

    /// <summary>
    /// The task that is using the map stream.
    /// </summary>
    public Task? CopyTask { get; set; }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => true;

    /// <inheritdoc/>
    public override long Length => throw new NotImplementedException();

    /// <inheritdoc/>
    public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    /// <inheritdoc/>
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int read = 0;
        // If we have left-over data from the previous read, return it
        if (readBuffer.Length > 0)
        {
            // Return as much as possible
            var requested = Math.Min(readBuffer.Length, count);
            readBuffer.Span.Slice(0, requested).CopyTo(buffer.AsSpan(offset, count));
            // Adjust the remaining buffer
            if (readBuffer.Length > requested)
                readBuffer = readBuffer.Slice(requested);
            else
                readBuffer = Array.Empty<byte>();

            // Adjust the counters
            offset += requested;
            count -= requested;
            read += requested;

            // Return if we have read all that was requested
            if (count == 0)
                return read;
        }

        // Handle external task
        var ct = CopyTask;

        // If the task is completed, clear it
        if (ct != null && ct.IsCompletedSuccessfully)
            CopyTask = ct = null;
        // If the task failed, stop reading
        else if (ct != null && (ct.IsCanceled || ct.IsFaulted))
        {
            // If we have any data in the buffer, return it
            if (read > 0)
                return read;

            // Otherwise, throw the exception
            await ct;
        }

        if (!await channel.Reader.WaitToReadAsync(cancellationToken))
        {
            // If we have any data in the buffer, return it
            if (read > 0)
                return read;

            // If the copy crashed, throw the exception
            ct = CopyTask;
            if (ct != null && (ct.IsCanceled || ct.IsFaulted))
                await ct;

            // Otherwise, return 0
            return read;
        }

        // Read the data from the channel
        var res = await channel.Reader.ReadAsync(cancellationToken);

        // Consume as much as was requested
        var consumed = Math.Min(res.Length, count);

        // Keep the rest in the buffer
        if (res.Length > count)
            readBuffer = res.AsMemory(count);

        res.AsSpan(0, consumed).CopyTo(buffer.AsSpan(offset, count));
        read += consumed;
        return read;
    }

    /// <inheritdoc/>
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var ct = CopyTask;
        // Throw if the task failed
        if (ct != null && (ct.IsCanceled || ct.IsFaulted))
            await ct;
        await channel.Writer.WriteAsync(buffer.AsSpan(offset, count).ToArray(), cancellationToken);
    }

    public override void Close()
    {
        base.Close();
        channel.Writer.Complete();
    }

    protected override void Dispose(bool disposing)
    {
        channel.Writer.Complete();
        base.Dispose(disposing);
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer, offset, count, CancellationToken.None).Await();

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotImplementedException();

    public override void SetLength(long value)
        => throw new NotImplementedException();

    public override void Write(byte[] buffer, int offset, int count)
        => WriteAsync(buffer, offset, count, CancellationToken.None).Await();
}
