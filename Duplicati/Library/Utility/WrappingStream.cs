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
/// Wraps a <see cref="Stream"/> and delegates all calls to it.
/// </summary>
public abstract class WrappingStream : Stream
{
    /// <summary>
    /// The stream being wrapped.
    /// </summary>
    public Stream BaseStream { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WrappingStream"/> class.
    /// </summary>
    /// <param name="stream">The stream to wrap.</param>
    protected WrappingStream(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
        BaseStream = stream;
    }

    /// <inheritdoc/>
    public override bool CanTimeout => BaseStream.CanTimeout;
    /// <inheritdoc/>
    public override bool CanRead => BaseStream.CanRead;
    /// <inheritdoc/>
    public override bool CanSeek => BaseStream.CanSeek;
    /// <inheritdoc/>
    public override bool CanWrite => BaseStream.CanWrite;
    /// <inheritdoc/>
    public override long Length => BaseStream.Length;
    /// <inheritdoc/>
    public override long Position
    {
        get => BaseStream.Position;
        set => BaseStream.Position = value;
    }

    /// <inheritdoc/>
    public override void Flush() => BaseStream.Flush();
    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => BaseStream.Seek(offset, origin);
    /// <inheritdoc/>
    public override void SetLength(long value) => BaseStream.SetLength(value);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            BaseStream.Dispose();
    }

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
        => BaseStream.DisposeAsync();

    /// <inheritdoc/>
    public override void Close()
        => BaseStream.Close();

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken)
        => BaseStream.FlushAsync(cancellationToken);
}
