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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface;

/// <summary>
/// Implements a data source that can be backed up
/// </summary>
public interface ISourceProvider : IDisposable
{
    /// <summary>
    /// Returns the path where this provider is logically mounted
    /// </summary>
    string MountedPath { get; }

    /// <summary>
    /// Initializes the provider, if needed
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    Task Initialize(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the root entries
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>An enumerable of file entries</returns>
    IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a specific entry
    /// </summary>
    /// <param name="path">The path to use</param>
    /// <param name="isFolder">True if the path is a folder</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The file entry</returns>
    Task<ISourceProviderEntry?> GetEntry(string path, bool isFolder, CancellationToken cancellationToken);
}
