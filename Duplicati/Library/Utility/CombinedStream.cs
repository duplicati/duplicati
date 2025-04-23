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
using System.Collections.Generic;
using System.IO;

namespace Duplicati.Library.Utility;

/// <summary>
/// A stream that combines multiple streams into one.
/// </summary>
public class CombinedStream : Stream
{
    /// <summary>
    /// The streams to combine
    /// </summary>
    private readonly Queue<Stream> _streams;
    /// <summary>
    /// Flag keeping track of whether to leave the streams open when the combined stream is closed
    /// </summary>
    private readonly bool _leaveOpen;

    /// <summary>
    /// Creates a new combined stream
    /// </summary>
    /// <param name="streams">The streams to combine</param>
    /// <param name="leaveOpen">True to leave the streams open when the combined stream is closed</param>
    public CombinedStream(IEnumerable<Stream> streams, bool leaveOpen = false)
    {
        _streams = new Queue<Stream>(streams);
        _leaveOpen = leaveOpen;
    }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_streams.Count == 0)
            return 0;

        var bytesRead = _streams.Peek().Read(buffer, offset, count);

        if (bytesRead == 0)
        {
            var prev = _streams.Dequeue();
            if (!_leaveOpen)
                prev.Dispose();

            return Read(buffer, offset, count);
        }

        return bytesRead;
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        if (!_leaveOpen)
            foreach (var stream in _streams)
                stream.Dispose();

        _streams.Clear();
    }
}
