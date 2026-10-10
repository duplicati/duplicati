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

using Duplicati.Library.Interface;

namespace Duplicati.Library.SourceProvider;

/// <summary>
/// A source provider that wraps a backend
/// </summary>
/// <param name="backend">The backend to wrap</param>
/// <param name="mountedPath">The path to mount the backend</param>
/// <param name="url">The url the backend was created from</param>
public class BackendSourceProvider(IFolderEnabledBackend backend, string mountedPath, string url) : ISourceProvider, ISourceProviderModule
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
    public string Description => backend.Description;

    /// <inheritdoc/>
    public IList<ICommandLineArgument> SupportedCommands => backend.SupportedCommands;

    /// <inheritdoc />
    public bool NeedsStoredMetadata => true;

    /// <summary>
    /// The metadata key prefix used by this provider
    /// </summary>
    public const string METADATA_PREFIX = "backend:";

    /// <summary>
    /// The version of the metadata written to the entries
    /// </summary>
    public const string METADATA_VERSION = "1";

    /// <summary>
    /// The type written to the metadata of the root entry
    /// </summary>
    public const string METADATA_ROOT_TYPE = "BackendSourceProvider";

    /// <summary>
    /// The host suffix of the backend, if it is a known public server.
    /// Private hostnames are not kept, so they do not end up in the metadata.
    /// </summary>
    private readonly string? hostSuffix = Utility.Utility.GuessHostSuffixSafe(url);

    /// <summary>
    /// Gets the minor metadata for the root entry, describing the wrapped backend
    /// </summary>
    /// <returns>The root metadata</returns>
    internal Dictionary<string, string?> GetRootMetadata()
        // The caller adds to the result, so it must be a new instance on each call
        => new Dictionary<string, string?>()
            {
                { METADATA_PREFIX + "v", METADATA_VERSION },
                { METADATA_PREFIX + "Type", METADATA_ROOT_TYPE },
                { METADATA_PREFIX + "Name", backend.DisplayName },
                { METADATA_PREFIX + "Protocol", backend.ProtocolKey },
                { METADATA_PREFIX + "Host", hostSuffix },
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

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
    public async Task InitializeAsync(CancellationToken cancellationToken)
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
    public Task TestAsync(CancellationToken cancellationToken)
        => backend.TestAsync(false, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync(CancellationToken cancellationToken)
        => new[] { Interlocked.Exchange(ref preparedRoot, null) ?? CreateRoot() }.ToAsyncEnumerable();

    /// <inheritdoc/>
    /// <remarks>
    /// The path may be given as reported by <see cref="BackendSourceFileEntry.Path"/>,
    /// that is, rooted or prefixed with the mount point. The backend is always
    /// given the path relative to the mount point.
    /// </remarks>
    public async Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
    {
        var relativePath = path;
        if (!string.IsNullOrEmpty(mountedPath) && relativePath.StartsWith(mountedPath, StringComparison.Ordinal))
            relativePath = relativePath.Substring(mountedPath.Length);
        relativePath = relativePath.TrimStart('/', '\\');

        var entry = await backend.GetEntryAsync(BackendSourceFileEntry.NormalizePathTo(relativePath, '/'), cancellationToken).ConfigureAwait(false);
        return entry == null
            ? null
            : new BackendSourceFileEntry(this, relativePath, entry.IsFolder, false, entry.Created, entry.LastModification, entry.Size);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        WrappedBackend.Dispose();
        GC.SuppressFinalize(this);
    }
}
