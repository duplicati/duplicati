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
using Duplicati.Library.Interface;

namespace Duplicati.Library.SourceProvider;

/// <summary>
/// Helper class that wraps multiple source providers into a single source provider
/// </summary>
public class Combiner(IEnumerable<ISourceProvider> providers) : ISourceProvider
{
    /// <inheritdoc/>
    public string MountedPath => string.Empty;

    /// <summary>
    /// The providers to combine
    /// </summary>
    private readonly List<ISourceProvider> providers = providers.SelectMany(x => x is Combiner c ? c.providers.AsEnumerable() : [x]).ToList();

    /// <inheritdoc/>
    public Task Initialize(CancellationToken cancellationToken)
        => Task.WhenAll(providers.Select(x => x.Initialize(cancellationToken)));

    /// <inheritdoc />
    public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
        {
            await foreach (var entry in provider.Enumerate(cancellationToken).ConfigureAwait(false))
                yield return entry;
        }
    }

    /// <summary>
    /// Provides access to the combined providers
    /// </summary>
    public IEnumerable<ISourceProvider> Providers => providers;

    /// <summary>
    /// Gets a filesystem entry for a given path
    /// </summary>
    /// <param name="path">The path to get</param>
    /// <param name="isFolder">True if the path is a folder</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The filesystem entry</returns>
    public async Task<ISourceProviderEntry?> GetEntry(string path, bool isFolder, CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
            if (!string.IsNullOrWhiteSpace(MountedPath) && provider.MountedPath.StartsWith(path))
            {
                var subpath = provider.MountedPath.Substring(path.Length);
                return await provider.GetEntry(subpath, isFolder, cancellationToken).ConfigureAwait(false);
            }

        foreach (var provider in providers.Where(x => string.IsNullOrWhiteSpace(x.MountedPath)))
        {
            var res = await provider.GetEntry(path, isFolder, cancellationToken).ConfigureAwait(false);
            if (res != null)
                return res;
        }

        return null;
    }

    /// <summary>
    /// Creates a combined source provider from a list of providers
    /// </summary>
    /// <param name="providers">The providers to combine</param>
    /// <returns>The combined provider</returns>
    public static ISourceProvider Combine(IEnumerable<ISourceProvider> providers)
    {
        var providerTotal = providers.SelectMany(x => x is Combiner c ? c.providers.AsEnumerable() : [x]).ToList();
        return providerTotal.Count == 1 ? providerTotal[0] : new Combiner(providers);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var provider in providers)
            provider.Dispose();
    }
}
