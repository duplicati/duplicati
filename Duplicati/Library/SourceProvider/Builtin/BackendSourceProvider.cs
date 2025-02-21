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
        => new[] { new BackendSourceFileEntry(this, "", true, true, new DateTime(0), new DateTime(0), 0) }.ToAsyncEnumerable();

    /// <inheritdoc/>
    public Task<ISourceFileEntry?> GetEntry(string path, bool isFolder, CancellationToken cancellationToken)
        => backend.GetEntryAsync(path, cancellationToken);

    /// <inheritdoc/>
    public void Dispose()
    {
        WrappedBackend.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Helper function to use a List function that returns IFileEntry and convert it to ISourceFileEntry
    /// </summary>
    /// <param name="prefix">The prefix to hide from the caller</param>
    /// <param name="path">The logical path to list</param>
    /// <param name="listFunction">The function that returns the IFileEntry items</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The list of source file entries</returns>
    public async IAsyncEnumerable<ISourceFileEntry> ListFromFileEntryAsync(string prefix, string path, Func<string, CancellationToken, IAsyncEnumerable<IFileEntry>> listFunction, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(prefix) && !prefix.EndsWith("/", StringComparison.Ordinal))
            prefix += "/";

        // Logic here is that the prefix is invisible to the caller, so we add it to the prefix to filter the results
        // and remove it from the name to make the prefix invisible.
        // The input path is preserved so the output looks like a "full path" to the caller.

        var filterPath = prefix + path;
        if (filterPath != "" && !filterPath.EndsWith("/", StringComparison.Ordinal))
            filterPath += "/";

        await foreach (var f in listFunction(filterPath, cancellationToken).ConfigureAwait(false))
        {
            if (!f.Name.StartsWith(filterPath, StringComparison.Ordinal))
                continue;

            ((FileEntry)f).Name = f.Name.Substring(filterPath.Length);

            // Skip self and sub-folder items
            if (f.Name.Length == 0 || f.Name[0..^1].Contains("/"))
                continue;

            yield return BackendSourceFileEntry.FromFileEntry(this, path, f);
        }
    }
}
