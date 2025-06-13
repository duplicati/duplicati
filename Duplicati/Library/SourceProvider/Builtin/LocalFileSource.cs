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

using Duplicati.Library.Interface;
using Duplicati.Library.Snapshots;

namespace Duplicati.Library.SourceProvider;

/// <summary>
/// A source provider that wraps a file system through a snapshot service
/// </summary>
/// <param name="snapshotService">The snapshot service to use</param>
public class LocalFileSource(ISnapshotService snapshotService) : ISourceProvider
{
    /// <inheritdoc/>
    public string MountedPath => string.Empty;

    /// <inheritdoc/>
    public Task Initialize(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken)
        => snapshotService.EnumerateFilesystemEntries().ToAsyncEnumerable();

    /// <summary>
    /// The snapshot service being used
    /// </summary>
    public ISnapshotService SnapshotService => snapshotService;

    /// <inheritdoc/>
    public Task<ISourceProviderEntry?> GetEntry(string path, bool isFolder, CancellationToken cancellationToken)
        => Task.FromResult(snapshotService.GetFilesystemEntry(path, isFolder));

    /// <inheritdoc/>
    public void Dispose()
    {
        snapshotService.Dispose();
        GC.SuppressFinalize(this);
    }
}
