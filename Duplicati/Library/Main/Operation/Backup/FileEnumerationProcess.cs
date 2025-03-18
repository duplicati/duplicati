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
using CoCoL;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Interface;
using System.Runtime.CompilerServices;
using Duplicati.Library.SourceProvider;
using Duplicati.Library.Snapshots.USN;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// The file enumeration process takes a list of source folders as input,
    /// applies all filters requested and emits the filtered set of filenames
    /// to its output channel
    /// </summary>
    internal static class FileEnumerationProcess
    {
        /// <summary>
        /// The log tag to use
        /// </summary>
        private static readonly string FILTER_LOGTAG = Logging.Log.LogTagFromType(typeof(FileEnumerationProcess));

        public static Task Run(
            Channels channels,
            ISourceProvider sourceProvider,
            UsnJournalService? journalService,
            FileAttributes fileAttributeFilter,
            Library.Utility.IFilter emitfilter,
            Options.SymlinkStrategy symlinkPolicy,
            Options.HardlinkStrategy hardlinkPolicy,
            bool excludeemptyfolders,
            string[]? ignorenames,
            HashSet<string> blacklistPaths,
            IEnumerable<string>? changedfilelist,
            ITaskReader taskreader,
            Action? onStopRequested,
            CancellationToken token)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Output = channels.SourcePaths.AsWrite()
            },

            async self =>
            {
                if (!token.IsCancellationRequested)
                {
                    // The hardlink map tracks the hardlink targets we have seen
                    // and avoid multiple processing of the same contents
                    var hardlinkmap = new Dictionary<string, string>();

                    // The mixin queue is used to store symlinks that should be processed
                    // The symlinks are emitted during the enumeration process when they are found
                    var mixinqueue = new Queue<ISourceProviderEntry>();

                    // The enumeration filter is used to determine what paths to
                    // recurse into. If the emit filter only has includes,
                    // the enumeration filter will also include all folders,
                    // as nothing will match otherwise
                    var enumeratefilter = emitfilter;

                    Library.Utility.FilterExpression.AnalyzeFilters(emitfilter, out var includes, out var excludes);
                    if (includes && !excludes)
                        enumeratefilter = Library.Utility.FilterExpression.Combine(emitfilter, new Duplicati.Library.Utility.FilterExpression("*" + System.IO.Path.DirectorySeparatorChar, true));

                    // Simplify checking for an empty list
                    if (ignorenames != null && ignorenames.Length == 0)
                        ignorenames = null;


                    // Shared filter function with bound variables
                    ValueTask<bool> FilterEntry(ISourceProviderEntry entry)
                        => SourceFileEntryFilter(entry, blacklistPaths, hardlinkPolicy, symlinkPolicy, hardlinkmap, fileAttributeFilter, enumeratefilter, ignorenames, mixinqueue, token);

                    // Prepare the work list
                    IAsyncEnumerable<ISourceProviderEntry> worklist;

                    // If we have a specific list, use that instead of enumerating the filesystem
                    if (changedfilelist != null && changedfilelist.Any())
                    {
                        async IAsyncEnumerable<ISourceProviderEntry> ExpandSources(IEnumerable<string> list)
                        {
                            foreach (var s in list)
                            {
                                var r = await sourceProvider.GetEntry(s, s.EndsWith(Path.DirectorySeparatorChar), token).ConfigureAwait(false);
                                if (r != null)
                                {
                                    //TODO: Set r.IsRoot = true for source elements
                                    yield return r;
                                }
                            }
                        }
                        worklist = ExpandSources(changedfilelist).WhereAwait(FilterEntry);
                    }
                    else if (journalService != null)
                    {
                        if (!OperatingSystem.IsWindows())
                            throw new NotSupportedException("USN is only supported on Windows");

                        var fileProviders = (sourceProvider is Combiner c ? c.Providers.AsEnumerable() : [sourceProvider])
                            .OfType<SourceProvider.LocalFileSource>()
                            .ToList();

                        if (fileProviders.Count <= 0)
                            throw new InvalidOperationException("No file providers found, but USN was enabled?");
                        if (fileProviders.Count > 1)
                            throw new InvalidOperationException("Multiple file providers found, but USN only supports one");

                        // TODO: This is not as effecient as possible.
                        // If the root folder is marked changed by USN, the expansion with RecurseEntries
                        // will cause a full regular scan. It should be possible to *only* process the
                        // changed elements as returned from the USN journal.
                        // It should be possible to remove RecurseEntries from the GetModifiedSources()
                        // enumeration result.
                        // Such a change requires significant testing as there are many pitfalls with USN.
                        worklist = RecurseEntries(journalService.GetModifiedSources(FilterEntry, token),
                            FilterEntry,
                            token
                        )
                        .Concat(
                            RecurseEntries(journalService.GetFullScanSources(token),
                            FilterEntry,
                            token)
                        );
                    }
                    else
                    {
                        worklist = RecurseEntries(sourceProvider.Enumerate(token),
                            FilterEntry,
                            token
                        );
                    }

                    if (token.IsCancellationRequested)
                        return;

                    var source = ExpandWorkList(worklist, mixinqueue, emitfilter, enumeratefilter, token);
                    // TODO: There was a call to DistinctBy here, but this would cause all paths to be stored in memory
                    //.DistinctBy(x => x.Path, Library.Utility.Utility.IsFSCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

                    if (excludeemptyfolders)
                        source = ExcludeEmptyFolders(source, token);

                    // Process each path, and dequeue the mixins with symlinks as we go
                    await foreach (var s in source.WithCancellation(token).ConfigureAwait(false))
                    {
#if DEBUG
                        // For testing purposes, we need exact control
                        // when requesting a process stop.
                        // The "onStopRequested" callback is used to detect
                        // if the process is the real file enumeration process
                        // because the counter processe does not have a callback
                        if (onStopRequested != null)
                            taskreader.TestMethodCallback?.Invoke(s.Path);
#endif
                        // Stop if requested
                        if (token.IsCancellationRequested || !await taskreader.ProgressRendevouz().ConfigureAwait(false))
                        {
                            onStopRequested?.Invoke();
                            return;
                        }

                        await self.Output.WriteAsync(s);
                    }
                }
            });
        }

        /// <summary>
        /// A helper class to assist in excluding empty folders
        /// </summary>
        private class DirectoryStackEntry
        {
            /// <summary>
            /// The item being tracked
            /// </summary>
            public required ISourceProviderEntry Item;
            /// <summary>
            /// A flag indicating if any items are found in this folder
            /// </summary>
            public required bool AnyEntries;

        }

        /// <summary>
        /// Excludes empty folders.
        /// </summary>
        /// <returns>The list without empty folders.</returns>
        /// <param name="source">The list with potential empty folders.</param>
        private static async IAsyncEnumerable<ISourceProviderEntry> ExcludeEmptyFolders(IAsyncEnumerable<ISourceProviderEntry> source, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var pathstack = new Stack<DirectoryStackEntry>();

            await foreach (var s in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                // Keep track of directories
                var isDirectory = s.Path[s.Path.Length - 1] == System.IO.Path.DirectorySeparatorChar;
                if (isDirectory)
                {
                    while (pathstack.Count > 0 && !s.Path.StartsWith(pathstack.Peek().Item.Path, Library.Utility.Utility.ClientFilenameStringComparison))
                    {
                        var e = pathstack.Pop();
                        if (e.AnyEntries || pathstack.Count == 0)
                        {
                            // Propagate the any-flag upwards
                            if (pathstack.Count > 0)
                                pathstack.Peek().AnyEntries = true;

                            yield return e.Item;
                        }
                        else
                            Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingEmptyFolder", "Excluding empty folder {0}", e.Item);
                    }

                    if (pathstack.Count == 0 || s.Path.StartsWith(pathstack.Peek().Item.Path, Library.Utility.Utility.ClientFilenameStringComparison))
                    {
                        pathstack.Push(new DirectoryStackEntry() { Item = s, AnyEntries = false });
                        continue;
                    }
                }
                // Just emit files
                else
                {
                    if (pathstack.Count != 0)
                        pathstack.Peek().AnyEntries = true;
                    yield return s;
                }
            }

            while (pathstack.Count > 0)
            {
                var e = pathstack.Pop();
                if (e.AnyEntries || pathstack.Count == 0)
                {
                    // Propagate the any-flag upwards
                    if (pathstack.Count > 0)
                        pathstack.Peek().AnyEntries = true;

                    yield return e.Item;
                }
            }
        }

        /// <summary>
        /// Performs recursive traversal of the sources
        /// </summary>
        /// <param name="entries">The entries to recurse</param>
        /// <param name="filter">The filter to apply</param>
        /// <returns></returns>
        private static async IAsyncEnumerable<ISourceProviderEntry> RecurseEntries(IAsyncEnumerable<ISourceProviderEntry> entries, Func<ISourceProviderEntry, ValueTask<bool>> filter, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var work = new Stack<ISourceProviderEntry>();

            await foreach (var e in entries.WithCancellation(cancellationToken).ConfigureAwait(false))
                if (await filter(e).ConfigureAwait(false))
                    work.Push(e);

            while (work.Count > 0)
            {
                var e = work.Pop();

                // Process meta entry contents, but don't emit them for processing
                if (!e.IsMetaEntry)
                    yield return e;

                if (e.IsFolder)
                {
                    try
                    {
                        // We only filter new items, as we assume the input is already filtered
                        await foreach (var r in e.Enumerate(cancellationToken).ConfigureAwait(false))
                            if (await filter(r).ConfigureAwait(false))
                                work.Push(r);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "PathProcessingErrorEnumerate", ex, "Failed to enumerate path: {0}", e.Path);
                    }
                }
            }
        }

        /// <summary>
        /// Re-integrates the mixin queue to form a strictly sequential list of results
        /// </summary>
        /// <returns>The expanded list.</returns>
        /// <param name="worklist">The basic enumerable.</param>
        /// <param name="mixinqueue">The mix in queue.</param>
        /// <param name="emitfilter">The emitfilter.</param>
        /// <param name="enumeratefilter">The enumeratefilter.</param>
        private static async IAsyncEnumerable<ISourceProviderEntry> ExpandWorkList(IAsyncEnumerable<ISourceProviderEntry> worklist, Queue<ISourceProviderEntry> mixinqueue, Library.Utility.IFilter emitfilter, Library.Utility.IFilter enumeratefilter, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Process each path, and dequeue the mixins with symlinks as we go
            await foreach (var s in worklist.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                while (mixinqueue.Count > 0)
                    yield return mixinqueue.Dequeue();

                // If there are only includes in the filter, check if the item is in the original filter
                // Since the enumerate filter also includes all folders, we need to ensure we do not emit
                // any entries that are filtered explicitly by the user
                if (emitfilter != enumeratefilter && !Library.Utility.FilterExpression.Matches(emitfilter, s.Path, out var _))
                    continue;

                yield return s;
            }

            // Trailing symlinks are caught here
            while (mixinqueue.Count > 0)
                yield return mixinqueue.Dequeue();
        }

        /// <summary>
        /// Performs a pre-filter on the source entry to see if it should be included in the backup
        /// </summary>
        /// <param name="entry">The entry to evaluate.</param>
        /// <param name="blacklistPaths">The blacklist paths.</param>
        /// <returns>True if the path should be returned, false otherwise.</returns>
        private static bool PreFilterSourceEntry(ISourceProviderEntry entry, HashSet<string> blacklistPaths)
        {
            // Don't filter meta stuff
            if (entry.IsMetaEntry)
                return true;

            // Exclude any blacklisted paths
            if (blacklistPaths.Contains(entry.Path))
            {
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingBlacklistedPath", "Excluding blacklisted path: {0}", entry.Path);
                return false;
            }

            // Exclude block devices
            try
            {
                if (entry.IsBlockDevice)
                {
                    Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingBlockDevice", "Excluding block device: {0}", entry.Path);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "PathProcessingErrorBlockDevice", ex, "Failed to process path: {0}", entry.Path);
                return false;
            }

            // Exclude character devices
            try
            {
                if (entry.IsCharacterDevice)
                {
                    Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingCharacterDevice", "Excluding character device: {0}", entry.Path);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "PathProcessingErrorCharacterDevice", ex, "Failed to process path: {0}", entry.Path);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Evaluates a single entry for inclusion in the backup
        /// </summary>
        /// <param name="entry">The current entry.</param>
        /// <param name="snapshot">The snapshot service.</param>
        /// <param name="blacklistPaths">The blacklist paths.</param>
        /// <param name="hardlinkPolicy">The hardlink policy.</param>
        /// <param name="symlinkPolicy">The symlink policy.</param>
        /// <param name="hardlinkmap">The hardlink map.</param>
        /// <param name="fileAttributeFilter">The file attributes to exclude.</param>
        /// <param name="enumeratefilter">The enumerate filter.</param>
        /// <param name="ignorenames">The ignore names.</param>
        /// <param name="mixinqueue">The mixin queue.</param>
        /// <returns>True if the path should be returned, false otherwise.</returns>
        private static async ValueTask<bool> SourceFileEntryFilter(ISourceProviderEntry entry, HashSet<string> blacklistPaths, Options.HardlinkStrategy hardlinkPolicy, Options.SymlinkStrategy symlinkPolicy, Dictionary<string, string> hardlinkmap, FileAttributes fileAttributeFilter, Duplicati.Library.Utility.IFilter enumeratefilter, string[]? ignorenames, Queue<ISourceProviderEntry> mixinqueue, CancellationToken cancellationToken)
        {
            // Do the course pre-filtering first
            if (!PreFilterSourceEntry(entry, blacklistPaths))
                return false;

            // Never exclude the root entries
            if (entry.IsRootEntry)
                return true;

            // If we have a hardlink strategy, obey it
            if (hardlinkPolicy != Options.HardlinkStrategy.All)
            {
                try
                {
                    var id = entry.HardlinkTargetId;
                    if (id != null)
                    {
                        if (hardlinkPolicy == Options.HardlinkStrategy.None)
                        {
                            Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingHardlinkByPolicy", "Excluding hardlink: {0} ({1})", entry.Path, id);
                            return false;
                        }
                        else if (hardlinkPolicy == Options.HardlinkStrategy.First)
                        {
                            if (hardlinkmap.TryGetValue(id, out var prevPath))
                            {
                                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingDuplicateHardlink", "Excluding hardlink ({1}) for: {0}, previous hardlink: {2}", entry.Path, id, prevPath);
                                return false;
                            }
                            else
                            {
                                hardlinkmap.Add(id, entry.Path);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "PathProcessingErrorHardLink", ex, "Failed to process path: {0}", entry.Path);
                    return false;
                }
            }

            // Check if there is an ignore marker file
            if (ignorenames != null && entry.IsFolder)
            {
                try
                {
                    foreach (var n in ignorenames)
                    {
                        if (await entry.FileExists(n, cancellationToken).ConfigureAwait(false))
                        {
                            Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingPathDueToIgnoreFile", "Excluding path because ignore file {0} was found in: {1}", n, entry.Path);
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "PathProcessingErrorIgnoreFile", ex, "Failed to process path: {0}", entry.Path);
                }
            }

            // Setup some basic processing attributes
            var attributes = entry.IsFolder
                ? FileAttributes.Directory
                : FileAttributes.Normal;

            try
            {
                attributes = entry.Attributes;
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "PathProcessingErrorAttributes", ex, "Failed to process path, using default attributes: {0}", entry.Path);
            }

            // If we exclude files based on attributes, filter that
            if ((fileAttributeFilter & attributes) != 0)
            {
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingPathFromAttributes", "Excluding path due to attribute filter: {0}", entry.Path);
                return false;
            }

            // Then check if the filename is not explicitly excluded by a filter
            var filtermatch = false;
            if (!Library.Utility.FilterExpression.Matches(enumeratefilter, entry.Path, out var match))
            {
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingPathFromFilter", "Excluding path due to filter: {0} => {1}", entry.Path, match == null ? "null" : match.ToString());
                return false;
            }
            else if (match != null)
            {
                filtermatch = true;
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "IncludingPathFromFilter", "Including path due to filter: {0} => {1}", entry.Path, match.ToString());
            }

            // If the file is a symlink, apply special handling
            string? symlinkTarget = null;
            try
            {
                symlinkTarget = entry.SymlinkTarget;
            }
            catch (Exception ex)
            {
                Logging.Log.WriteExplicitMessage(FILTER_LOGTAG, "SymlinkTargetReadError", ex, "Failed to read symlink target for path: {0}", entry.Path);
            }

            if (symlinkTarget != null)
            {
                if (!string.IsNullOrWhiteSpace(symlinkTarget))
                {
                    if (symlinkPolicy == Options.SymlinkStrategy.Ignore)
                    {
                        Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludeSymlink", "Excluding symlink: {0}", entry.Path);
                        return false;
                    }

                    if (symlinkPolicy == Options.SymlinkStrategy.Store)
                    {
                        Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "StoreSymlink", "Storing symlink: {0}", entry.Path);

                        // We return false because we do not want to recurse into the path,
                        // but we add the symlink to the mixin so we process the symlink itself
                        mixinqueue.Enqueue(entry);
                        return false;
                    }
                }
                else
                {
                    Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "FollowingEmptySymlink", "Treating empty symlink as regular path {0}", entry.Path);
                }
            }

            if (!filtermatch)
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "IncludingPath", "Including path as no filters matched: {0}", entry.Path);

            // All the way through, yes!
            return true;
        }
    }
}

