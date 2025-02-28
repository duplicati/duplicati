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

namespace Duplicati.Library.SourceProvider;

/// <summary>
/// A source provider that wraps a backend
/// </summary>
/// <param name="backend">The backend to wrap</param>
/// <param name="mountedPath">The path to mount the backend</param>
public class BackendSourceProvider(IFolderEnabledBackend backend, string mountedPath) : ISourceProvider, ISourceProviderModule
{
    /// <summary>
    /// The wrapped backend
    /// </summary>
    public IFolderEnabledBackend WrappedBackend => backend;

    /// <summary>
    /// The path where the provider is logically mounted
    /// </summary>
    public string MountedPath => mountedPath;

    /// <inheritdoc/>
    public string Key => backend.ProtocolKey;

    /// <inheritdoc/>
    public string DisplayName => backend.DisplayName;

    /// <inheritdoc/>
    public IList<ICommandLineArgument> SupportedCommands => backend.SupportedCommands;

    /// <summary>
    /// The prepared root entry, if any
    /// </summary>
    private BackendSourceFileEntry? preparedRoot;

    /// <summary>
    /// Flags if the provider has been initialized
    /// </summary>
    private int isInitialized = 0;

    /// <summary>
    /// Creates a root entry
    /// </summary>
    /// <returns>The root entry</returns>
    private BackendSourceFileEntry CreateRoot()
        => new BackendSourceFileEntry(this, "", true, true, new DateTime(0), new DateTime(0), 0);

    /// <inheritdoc/>
    public async Task Initialize(CancellationToken cancellationToken)
    {
        // Only allow a single intiiialization call
        if (Interlocked.Exchange(ref isInitialized, 1) != 0)
            return;

        // Prepare the root entry
        var root = CreateRoot();
        await root.PrepareEnumerator(cancellationToken).ConfigureAwait(false);
        preparedRoot = root;
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken)
        => new[] { Interlocked.Exchange(ref preparedRoot, null) ?? CreateRoot() }.ToAsyncEnumerable();

    /// <inheritdoc/>
    public async Task<ISourceProviderEntry?> GetEntry(string path, bool isFolder, CancellationToken cancellationToken)
    {
        var entry = await backend.GetEntryAsync(BackendSourceFileEntry.NormalizePathTo(path, '/'), cancellationToken).ConfigureAwait(false);
        return entry == null ? null : BackendSourceFileEntry.FromFileEntry(this, path, entry);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        WrappedBackend.Dispose();
        GC.SuppressFinalize(this);
    }
}
