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

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.SourceProvider;

/// <summary>
/// A source provider that wraps a backend
/// </summary>
/// <param name="backend">The backend to wrap</param>
public class BackendSourceProvider(IFolderEnabledBackend backend) : ISourceProvider, ISourceProviderModule
{
    /// <summary>
    /// The wrapped backend
    /// </summary>
    public IFolderEnabledBackend WrappedBackend => backend;

    /// <summary>
    /// The path key unique for the destination
    /// </summary>
    public string PathKey => backend.PathKey;

    /// <inheritdoc/>
    public string Key => backend.ProtocolKey;

    /// <inheritdoc/>
    public string DisplayName => backend.DisplayName;

    /// <inheritdoc/>
    public IList<ICommandLineArgument> SupportedCommands => backend.SupportedCommands;

    /// <inheritdoc/>
    public IAsyncEnumerable<ISourceFileEntry> Enumerate(CancellationToken cancellationToken)
        => new[] { new BackendSourceFileEntry(this, PathKey, true, true, new DateTime(0), new DateTime(0), 0) }.ToAsyncEnumerable();

    /// <inheritdoc/>
    public Task<ISourceFileEntry?> GetEntry(string path, bool isFolder, CancellationToken cancellationToken)
        => backend.GetEntryAsync(path, cancellationToken);

    /// <inheritdoc/>
    public void Dispose()
    {
        WrappedBackend.Dispose();
        GC.SuppressFinalize(this);
    }
}
