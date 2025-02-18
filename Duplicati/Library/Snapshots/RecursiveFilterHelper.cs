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
using System.Linq;
using System.Threading;
using Duplicati.Library.Interface;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Duplicati.Library.Snapshots;

public static class RecursiveFilterHelper
{
    /// <summary>
    /// The log tag to use
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(RecursiveFilterHelper));

    /// <summary>
    /// Filters sources, returning sub-set having been modified since last
    /// change, as specified by <c>journalData</c>.
    /// </summary>
    /// <param name="entries">List of sources</param>
    /// <param name="snapshot">Snapshot service</param>
    /// <param name="filter">Filter callback to exclude filtered items</param>
    /// <param name="token">Cancellation token</param>
    /// <returns>Filtered sources</returns>
    public static async IAsyncEnumerable<ISourceFileEntry> GetModifiedSources(IAsyncEnumerable<ISourceFileEntry> entries, ISnapshotService snapshot, Func<ISourceFileEntry, ValueTask<bool>> filter, [EnumeratorCancellation] CancellationToken token)
    {
        // prepare cache for includes (value = true) and excludes (value = false, will be populated
        // on-demand)
        var cache = new Dictionary<string, bool>();
        await foreach (var source in entries)
        {
            if (token.IsCancellationRequested)
                break;

            cache[source.Path] = true;
        }

        // Check the simplified folders, and their parent folders  against the exclusion filter.
        // This is needed because the filter may exclude "C:\A\", but this won't match the more
        // specific "C:\A\B\" in our list, even though it's meant to be excluded.
        // The reason why the filter doesn't exclude it is because during a regular (non-USN) full scan, 
        // FilterHandler.EnumerateFilesAndFolders() works top-down, and won't even enumerate child
        // folders. 
        // The sources are needed to stop evaluating parent folders above the specified source folders
        await foreach (var folder in FilterExcludedFolders(entries.Where(x => x.IsFolder), snapshot, filter, cache, token))
        {
            if (token.IsCancellationRequested)
                break;

            if (!snapshot.DirectoryExists(folder.Path))
                continue;

            yield return folder;
        }

        // The simplified file list also needs to be checked against the exclusion filter, as it 
        // may contain entries excluded due to attributes, but also because they are below excluded
        // folders, which themselves aren't in the folder list from step 1.
        // Note that the simplified file list may contain entries that have been deleted! They need to 
        // be kept in the list (unless excluded by the filter) in order for the backup handler to record their 
        // deletion.
        await foreach (var files in FilterExcludedFiles(entries.Where(x => !x.IsFolder), snapshot, filter, cache, token))
        {
            if (token.IsCancellationRequested)
                break;

            if (!snapshot.FileExists(files.Path))
                continue;

            yield return files;
        }
    }

    /// <summary>
    /// Filter supplied <c>files</c>, removing any files which itself, or one
    /// of its parent folders, is excluded by the <c>filter</c>.
    /// </summary>
    /// <param name="files">Files to filter</param>
    /// <param name="snapshot">Snapshot service</param>
    /// <param name="filter">Exclusion filter</param>
    /// <param name="cache">Cache of included and excluded files / folders</param>
    /// <param name="token">Cancellation token</param>
    /// <returns>Filtered files</returns>
    private static async IAsyncEnumerable<ISourceFileEntry> FilterExcludedFiles(
        IAsyncEnumerable<ISourceFileEntry> files,
        ISnapshotService snapshot,
        Func<ISourceFileEntry, ValueTask<bool>> filter,
        IDictionary<string, bool> cache,
        [EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var file in files.WithCancellation(token).ConfigureAwait(false))
        {
            try
            {
                if (!await filter(file))
                    continue;

                var parentPath = Utility.Utility.GetParent(file.Path, true);
                if (!string.IsNullOrWhiteSpace(parentPath))
                {
                    var parent = snapshot.GetFilesystemEntry(parentPath, true);
                    if (await IsFolderOrAncestorsExcluded(parent, snapshot, filter, cache, token))
                        continue;
                }
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "FilterExcludedFilesError", ex, "Error while filtering file: {0}", ex.Message);
                continue;
            }

            yield return file;
        }
    }

    /// <summary>
    /// Filter supplied <c>folders</c>, removing any folder which itself, or one
    /// of its ancestors, is excluded by the <c>filter</c>.
    /// </summary>
    /// <param name="folders">Folder to filter</param>
    /// <param name="snapshot">Snapshot service</param>
    /// <param name="filter">Exclusion filter</param>
    /// <param name="cache">Cache of excluded folders</param>
    /// <param name="token">Cancellation token</param>
    /// <returns>Filtered folders</returns>
    private static async IAsyncEnumerable<ISourceFileEntry> FilterExcludedFolders(
        IAsyncEnumerable<ISourceFileEntry> folders,
        ISnapshotService snapshot,
        Func<ISourceFileEntry, ValueTask<bool>> filter,
        IDictionary<string, bool> cache,
        [EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var folder in folders.WithCancellation(token).ConfigureAwait(false))
        {
            try
            {
                if (await IsFolderOrAncestorsExcluded(folder, snapshot, filter, cache, token))
                    continue;
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "FilterExcludedFoldersError", ex, "Error while filtering folder: {0}", ex.Message);
                continue;
            }

            yield return folder;
        }
    }

    /// <summary>
    /// Tests if specified folder, or any of its ancestors, is excluded by the filter
    /// </summary>
    /// <param name="folder">Folder to test</param>
    /// <param name="filter">Filter</param>
    /// <param name="cache">Cache of excluded folders (optional)</param>
    /// <returns>True if excluded, false otherwise</returns>
    private static async ValueTask<bool> IsFolderOrAncestorsExcluded(
        ISourceFileEntry folder,
        ISnapshotService snapshot,
        Func<ISourceFileEntry, ValueTask<bool>> filter,
        IDictionary<string, bool> cache,
        CancellationToken token)
    {
        List<string>? parents = null;
        while (folder != null)
        {
            if (token.IsCancellationRequested)
                break;

            // first check cache
            if (cache.TryGetValue(folder.Path, out var include))
            {
                if (include)
                    return false;

                break; // hit!
            }

            parents ??= []; // create on-demand

            // remember folder for cache
            parents.Add(folder.Path);


            if (!await filter(folder))
                break; // excluded

            var parentPath = Utility.Utility.GetParent(folder.Path, true);
            if (string.IsNullOrWhiteSpace(parentPath))
                break;

            folder = snapshot.GetFilesystemEntry(parentPath, true);
        }

        if (folder != null)
        {
            // update cache
            parents?.ForEach(p => cache[p] = false);
        }

        return folder != null;
    }
}

