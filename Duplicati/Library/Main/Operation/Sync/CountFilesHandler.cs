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
using Duplicati.Library.Snapshots.USN;

namespace Duplicati.Library.Main.Operation.Sync;

/// <summary>
/// Runs a parallel background enumeration of the source tree that counts the
/// files and accumulates their total size, feeding the running totals into
/// <see cref="SyncResults.OperationProgressUpdater"/> via
/// <see cref="IOperationProgressUpdater.UpdatefileCount"/>. This mirrors the
/// backup <c>CountFilesHandler</c>: the main sync loop (the per-folder work
/// queue) runs concurrently while this task walks the whole source tree once
/// to give the UI an early, monotonically-growing estimate of how many files
/// the run will touch. The main loop is the source of truth for what is
/// actually uploaded/deleted; this counter only drives progress reporting, so
/// it may be cancelled as soon as the main loop finishes without affecting
/// correctness.
/// </summary>
internal static class CountFilesHandler
{
    /// <summary>
    /// Counts the files in the source tree in the background, reporting the
    /// running count and size to <paramref name="results"/>. The task
    /// completes when the enumeration is exhausted or <paramref name="token"/>
    /// is cancelled. The caller is expected to run this concurrently with the
    /// main sync loop and to cancel <paramref name="token"/> once that loop
    /// is done so this task does not outlive the run.
    /// </summary>
    /// <param name="sources">The source provider tree to enumerate.</param>
    /// <param name="journalService">The optional USN journal service (may be null; sync does not use one today but the parameter is kept for parity with the backup handler).</param>
    /// <param name="results">The sync results whose progress updater receives the count.</param>
    /// <param name="fileAttributeFilter">The file-attribute filter applied during enumeration.</param>
    /// <param name="filter">The include/exclude filter applied during enumeration.</param>
    /// <param name="symlinkPolicy">The symlink policy applied during enumeration.</param>
    /// <param name="hardlinkPolicy">The hardlink policy applied during enumeration.</param>
    /// <param name="disableBackupExclusionXattr">Whether xattr-based backup exclusions are disabled.</param>
    /// <param name="excludeEmptyFolders">Whether empty folders are excluded from the count.</param>
    /// <param name="ignoreNames">The ignore-names list applied during enumeration.</param>
    /// <param name="blacklistPaths">The set of blacklisted paths excluded from the count.</param>
    /// <param name="token">A cancellation token that, when cancelled, aborts the enumeration.</param>
    public static async Task RunAsync(
        ISourceProvider sources,
        UsnJournalService? journalService,
        SyncResults results,
        FileAttributes fileAttributeFilter,
        Library.Utility.IFilter filter,
        Options.SymlinkStrategy symlinkPolicy,
        Options.HardlinkStrategy hardlinkPolicy,
        bool disableBackupExclusionXattr,
        bool excludeEmptyFolders,
        string[]? ignoreNames,
        HashSet<string> blacklistPaths,
        CancellationToken token)
    {
        var count = 0L;
        var size = 0L;

        try
        {
            await foreach (var entry in FileEnumerationProcess.EnumerateAsync(
                sources,
                journalService,
                fileAttributeFilter,
                filter,
                symlinkPolicy,
                hardlinkPolicy,
                disableBackupExclusionXattr,
                excludeEmptyFolders,
                ignoreNames,
                blacklistPaths,
                changedfilelist: null,
                token).ConfigureAwait(false))
            {
                if (token.IsCancellationRequested)
                    break;

                if (entry.IsFolder)
                    continue;

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

                results.OperationProgressUpdater.UpdatefileCount(count, size, false);
            }
        }
        finally
        {
            // Mark the count as final for this enumeration pass. If the
            // counter was cancelled before it finished (the main loop ended
            // first) the count is incomplete, but the main loop's own
            // per-file reporting takes over as the authoritative progress
            // source, so a partial count here is fine.
            results.OperationProgressUpdater.UpdatefileCount(count, size, true);
        }
    }
}
