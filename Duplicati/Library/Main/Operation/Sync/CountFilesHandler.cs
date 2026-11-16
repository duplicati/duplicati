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

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Operation.Backup;

namespace Duplicati.Library.Main.Operation.Sync;

/// <summary>
/// Runs a parallel background enumeration of the source tree that counts the
/// files and accumulates their total size, feeding the running totals into
/// <see cref="IOperationProgressUpdater.UpdatefileCount"/>. The main sync loop
/// (the per-folder work queue) runs concurrently while this task walks the
/// source tree once, folder by folder as the main loop does and down to the same
/// depth, to give the UI an early, monotonically-growing estimate of how many
/// files the run will touch. The main loop is the source of truth for what is
/// actually uploaded/deleted; this counter only drives progress reporting, so
/// it may be cancelled as soon as the main loop finishes without affecting
/// correctness.
/// </summary>
internal static class CountFilesHandler
{
    /// <summary>
    /// Counts the files in the source tree in the background, reporting the
    /// running count and size to <paramref name="progress"/>. The task
    /// completes when the enumeration is exhausted or <paramref name="token"/>
    /// is cancelled. The caller is expected to run this concurrently with the
    /// main sync loop and to cancel <paramref name="token"/> once that loop
    /// is done so this task does not outlive the run.
    /// </summary>
    /// <param name="sources">The source provider to enumerate.</param>
    /// <param name="sourcePaths">The source root paths.</param>
    /// <param name="progress">The progress updater that receives the count.</param>
    /// <param name="fileAttributeFilter">The file-attribute filter applied during enumeration.</param>
    /// <param name="filter">The include/exclude filter applied during enumeration.</param>
    /// <param name="symlinkPolicy">The symlink policy applied during enumeration.</param>
    /// <param name="hardlinkPolicy">The hardlink policy applied during enumeration.</param>
    /// <param name="disableBackupExclusionXattr">Whether xattr-based backup exclusions are disabled.</param>
    /// <param name="ignoreNames">The ignore-names list applied during enumeration.</param>
    /// <param name="blacklistPaths">The set of blacklisted paths excluded from the count.</param>
    /// <param name="maxDepth">The number of folder levels below a source root to descend, or <c>null</c> for no limit.</param>
    /// <param name="token">A cancellation token that, when cancelled, aborts the enumeration.</param>
    public static async Task RunAsync(
        ISourceProvider sources,
        string[] sourcePaths,
        IOperationProgressUpdater progress,
        FileAttributes fileAttributeFilter,
        Library.Utility.IFilter filter,
        Options.SymlinkStrategy symlinkPolicy,
        Options.HardlinkStrategy hardlinkPolicy,
        bool disableBackupExclusionXattr,
        string[]? ignoreNames,
        HashSet<string> blacklistPaths,
        int? maxDepth,
        CancellationToken token)
    {
        var count = 0L;
        var size = 0L;

        try
        {
            var queue = new Queue<(ISourceProviderEntry Entry, int Depth)>();
            foreach (var path in sourcePaths)
            {
                var root = await SyncHandler.GetSourceRootAsync(sources, path, token).ConfigureAwait(false);
                if (root != null && root.IsFolder)
                    queue.Enqueue((root, 0));
            }

            while (queue.Count > 0 && !token.IsCancellationRequested)
            {
                var (folder, depth) = queue.Dequeue();
                await foreach (var entry in FileEnumerationProcess.EnumerateFolderAsync(
                    folder,
                    fileAttributeFilter,
                    filter,
                    symlinkPolicy,
                    hardlinkPolicy,
                    disableBackupExclusionXattr,
                    ignoreNames,
                    blacklistPaths,
                    token).ConfigureAwait(false))
                {
                    if (token.IsCancellationRequested)
                        break;

                    if (entry.IsFolder)
                    {
                        if (!maxDepth.HasValue || depth < maxDepth.Value)
                            queue.Enqueue((entry, depth + 1));
                        continue;
                    }

                    count++;

                    try
                    {
                        var entrySize = entry.Size;
                        if (entrySize >= 0)
                            size += entrySize;
                    }
                    catch
                    {
                        // Some source providers throw on size access for exotic
                        // entries; skip the size contribution but keep counting.
                    }

                    progress.UpdatefileCount(count, size, false);
                }
            }
        }
        finally
        {
            // Mark the count as final for this enumeration pass. If the
            // counter was cancelled before it finished (the main loop ended
            // first) the count is incomplete, but the main loop's own
            // per-file reporting takes over as the authoritative progress
            // source, so a partial count here is fine.
            progress.UpdatefileCount(count, size, true);
        }
    }
}
