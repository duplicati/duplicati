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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.StreamUtil;

#nullable enable

namespace Duplicati.Library.Utility;

/// <summary>
/// A stream that wraps another stream and allows for completion events to be handled.
/// </summary>
public class CompletionEventStream(Stream innerStream) : WrappingAsyncStream(innerStream)
{
    /// <summary>
    /// An action to be invoked when the stream completes reading.
    /// </summary>
    public Action? OnCompletion { get; set; }

    /// <inheritdoc/>
    protected override async Task<int> ReadImplAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var r = await BaseStream.ReadAsync(buffer, offset, count, cancellationToken);
        if (r == 0)
            OnCompletion?.Invoke();
        return r;
    }

    /// <inheritdoc/>
    protected override Task WriteImplAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => BaseStream.WriteAsync(buffer, offset, count, cancellationToken);
}
