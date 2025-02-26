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

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Utility;

/// <summary>
/// A stream that wraps a temporary file.
/// The temporary file is automatically deleted when the stream is disposed.
/// </summary>
public class TempFileStream : StreamUtil.WrappingAsyncStream
{
    /// <summary>
    /// The temporary file instance.
    /// </summary>
    private readonly TempFile tempFile;
    /// <summary>
    /// The stream for the temporary file.
    /// </summary>
    private readonly Stream stream;

    /// <summary>
    /// The path to the temporary file.
    /// </summary>
    public string Path => tempFile.Name;

    /// <summary>
    /// Creates a new TempFileStream instance.
    /// </summary>
    /// <param name="tempFile">The temporary file instance.</param>
    /// <param name="stream">The stream for the temporary file.</param>
    private TempFileStream(TempFile tempFile, Stream stream)
        : base(stream)
    {
        this.tempFile = tempFile;
        this.stream = stream;
    }

    /// <summary>
    /// Creates a new TempFileStream instance.
    /// </summary>
    /// <param name="tempFile">The temporary file instance.</param>
    /// <returns>The new TempFileStream instance.</returns>
    public static TempFileStream Create(TempFile tempFile)
    {
        return new TempFileStream(tempFile, File.Open(tempFile.Name, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
    }

    /// <summary>
    /// Creates a new TempFileStream instance.
    /// </summary>
    /// <returns>The new TempFileStream instance.</returns>
    public static TempFileStream Create()
    {
        var tempFile = new TempFile();
        return new TempFileStream(tempFile, File.Open(tempFile.Name, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
    }

    /// <inheritdoc/>
    protected override Task<int> ReadImplAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => stream.ReadAsync(buffer, offset, count, cancellationToken);

    /// <inheritdoc/>
    protected override Task WriteImplAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => stream.WriteAsync(buffer, offset, count, cancellationToken);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        stream.Dispose();
        tempFile.Dispose();
    }
}
