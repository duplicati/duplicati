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
using Duplicati.Library.Utility;
using Duplicati.Library.Common.IO;
using System.Collections.ObjectModel;

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

        /// <summary>
        /// Enumerates the source entries with the same filtering and recursion logic used by the backup process.
        /// This method is shared between backup and sync to ensure consistent file enumeration behavior.
        /// </summary>
        internal static async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync(
            ISourceProvider sourceProvider,
            UsnJournalService? journalService,
            FileAttributes fileAttributeFilter,
            Library.Utility.IFilter emitfilter,
            Options.SymlinkStrategy symlinkPolicy,
            Options.HardlinkStrategy hardlinkPolicy,
            bool disableBackupExclusionXattr,
            bool excludeemptyfolders,
            Func<string, bool>? keepEmptyFolder,
            string[]? ignorenames,
            HashSet<string> blacklistPaths,
            IEnumerable<string>? changedfilelist,
            ChangeList? changeList,
            [EnumeratorCancellation] CancellationToken token)
        {
            if (token.IsCancellationRequested)
                yield break;

            // A folder examined from a change list without recursion lists no children,
            // so it cannot be told apart from an empty one and must not be dropped as empty
            HashSet<string>? examinedFolders = null;
            if (changeList != null)
            {
                examinedFolders = new HashSet<string>(Library.Utility.Utility.ClientFilenameStringComparer);
                var keepEmptyFolderInner = keepEmptyFolder;
                keepEmptyFolder = path => examinedFolders.Contains(path) || keepEmptyFolderInner?.Invoke(path) == true;
            }

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
                enumeratefilter = Library.Utility.FilterExpression.Combine(emitfilter, new Duplicati.Library.Utility.FilterExpression("*" + System.IO.Path.DirectorySeparatorChar, true))
                    ?? new Duplicati.Library.Utility.FilterExpression();

            // Simplify checking for an empty list
            if (ignorenames != null && ignorenames.Length == 0)
                ignorenames = null;

            // Shared filter function with bound variables
            ValueTask<bool> FilterEntry(ISourceProviderEntry entry)
                => SourceFileEntryFilterAsync(entry, blacklistPaths, hardlinkPolicy, symlinkPolicy, hardlinkmap, fileAttributeFilter, enumeratefilter, ignorenames, mixinqueue, disableBackupExclusionXattr, token);

            // Prepare the work list
            IAsyncEnumerable<ISourceProviderEntry> worklist;

            // If we have a change list, resolve its entries instead of enumerating the filesystem
            if (changeList != null)
            {
                // Ancestor checks must not mark hardlinks as seen or queue symlinks for the enumeration
                ValueTask<bool> CheckEntry(ISourceProviderEntry entry)
                    => SourceFileEntryFilterAsync(entry, blacklistPaths, hardlinkPolicy, symlinkPolicy, new Dictionary<string, string>(), fileAttributeFilter, enumeratefilter, ignorenames, new Queue<ISourceProviderEntry>(), disableBackupExclusionXattr, token);

                worklist = ExpandChangeListAsync(changeList, sourceProvider, FilterEntry, CheckEntry, examinedFolders!, token);

                // The change list only covers the file source, so the other providers
                // (e.g. remote sources, Hyper-V or MSSQL) are enumerated in full
                foreach (var provider in GetNonFileProviders(sourceProvider))
                    worklist = worklist.Concat(RecurseEntriesAsync(provider.EnumerateAsync(token), FilterEntry, token));
            }
            // If we have a specific list, use that instead of enumerating the filesystem
            else if (changedfilelist != null && changedfilelist.Any())
            {
                async IAsyncEnumerable<ISourceProviderEntry> ExpandSources(IEnumerable<string> list)
                {
                    foreach (var s in list)
                    {
                        var r = await sourceProvider.GetEntryAsync(s, s.EndsWith(Path.DirectorySeparatorChar), token).ConfigureAwait(false);
                        if (r != null)
                        {
                            //TODO: Set r.IsRoot = true for source elements
                            yield return r;
                        }
                    }
                }

                async IAsyncEnumerable<ISourceProviderEntry> FilterExpandedSources(IAsyncEnumerable<ISourceProviderEntry> source, [EnumeratorCancellation] CancellationToken token)
                {
                    await foreach (var entry in source.WithCancellation(token).ConfigureAwait(false))
                    {
                        if (await FilterEntry(entry).ConfigureAwait(false))
                            yield return entry;
                    }
                }

                worklist = FilterExpandedSources(ExpandSources(changedfilelist), token);
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
                worklist = RecurseEntriesAsync(journalService.GetModifiedSources(FilterEntry, token),
                    FilterEntry,
                    token
                )
                .Concat(
                    RecurseEntriesAsync(journalService.GetFullScanSources(token),
                    FilterEntry,
                    token)
                );

                // The change journal only covers the file source, so the other providers
                // (e.g. remote sources, Hyper-V or MSSQL) are enumerated in full
                foreach (var provider in GetNonFileProviders(sourceProvider))
                    worklist = worklist.Concat(RecurseEntriesAsync(provider.EnumerateAsync(token), FilterEntry, token));
            }
            else
            {
                worklist = RecurseEntriesAsync(sourceProvider.EnumerateAsync(token),
                    FilterEntry,
                    token
                );
            }

            if (token.IsCancellationRequested)
                yield break;

            var source = ExpandWorkListAsync(worklist, mixinqueue, emitfilter, enumeratefilter, token);
            // TODO: There was a call to DistinctBy here, but this would cause all paths to be stored in memory
            //.DistinctBy(x => x.Path, Library.Utility.Utility.IsFSCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

            if (excludeemptyfolders)
                source = ExcludeEmptyFoldersAsync(source, keepEmptyFolder, token);

            await foreach (var s in source.WithCancellation(token).ConfigureAwait(false))
                yield return s;
        }

        /// <summary>
        /// Enumerates the direct children of a single folder entry, applying the same
        /// per-entry filtering (attribute, hardlink, symlink, blacklist, ignore-names,
        /// xattr backup-exclusion) used by <see cref="EnumerateAsync"/>, but WITHOUT
        /// recursing into sub-folders. Sub-folder entries that pass the filter are
        /// yielded (so the caller can enqueue them) alongside file entries.
        /// </summary>
        /// <remarks>
        /// This is the per-folder building block used by the folder-by-folder sync
        /// handler: it lets the handler walk the local source tree one folder at a
        /// time, processing each folder's files before moving on, instead of
        /// materializing the whole tree into memory or a temp table. The filtering
        /// logic is shared with <see cref="EnumerateAsync"/> via
        /// <see cref="SourceFileEntryFilterAsync"/> so behavior stays consistent
        /// between backup and sync. Symlinks with a "store" policy are NOT enqueued
        /// into a mixin queue here (there is no recursion to defer them past); they
        /// are simply yielded to the caller, which treats them as regular files for
        /// sync purposes. The hardlink map is created fresh per call, so hardlink
        /// de-duplication is scoped to a single folder; cross-folder hardlinks are
        /// not de-duplicated, matching the previous behavior of the in-memory diff
        /// which never de-duplicated hardlinks at all.
        /// </remarks>
        /// <param name="folder">The folder entry to enumerate. Must be a folder.</param>
        /// <param name="fileAttributeFilter">The file attributes to exclude.</param>
        /// <param name="emitfilter">The emit filter (include/exclude expressions).</param>
        /// <param name="symlinkPolicy">The symlink policy.</param>
        /// <param name="hardlinkPolicy">The hardlink policy.</param>
        /// <param name="disableBackupExclusionXattr">Whether to disable xattr-based backup exclusion.</param>
        /// <param name="ignorenames">The ignore-marker filenames, if any.</param>
        /// <param name="blacklistPaths">The blacklisted paths.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An async stream of the filtered direct children of <paramref name="folder"/>.</returns>
        internal static async IAsyncEnumerable<ISourceProviderEntry> EnumerateFolderAsync(
            ISourceProviderEntry folder,
            FileAttributes fileAttributeFilter,
            Library.Utility.IFilter emitfilter,
            Options.SymlinkStrategy symlinkPolicy,
            Options.HardlinkStrategy hardlinkPolicy,
            bool disableBackupExclusionXattr,
            string[]? ignorenames,
            HashSet<string> blacklistPaths,
            [EnumeratorCancellation] CancellationToken token)
        {
            if (token.IsCancellationRequested)
                yield break;

            if (!folder.IsFolder)
                throw new ArgumentException("The entry is not a folder.", nameof(folder));

            // The hardlink map is scoped to this folder call. The mixin queue is not
            // used here (no recursion to defer symlinks past); a "store" symlink is
            // yielded directly to the caller.
            var hardlinkmap = new Dictionary<string, string>();
            var mixinqueue = new Queue<ISourceProviderEntry>();

            // The enumerate filter mirrors EnumerateAsync: if the emit filter only has
            // includes, folders are always accepted for enumeration so includes can
            // match nested files.
            var enumeratefilter = emitfilter;
            Library.Utility.FilterExpression.AnalyzeFilters(emitfilter, out var includes, out var excludes);
            if (includes && !excludes)
                enumeratefilter = Library.Utility.FilterExpression.Combine(emitfilter, new Duplicati.Library.Utility.FilterExpression("*" + System.IO.Path.DirectorySeparatorChar, true))
                    ?? new Duplicati.Library.Utility.FilterExpression();

            if (ignorenames != null && ignorenames.Length == 0)
                ignorenames = null;

            await foreach (var entry in folder.Enumerate(token).WithCancellation(token).ConfigureAwait(false))
            {
                if (await SourceFileEntryFilterAsync(entry, blacklistPaths, hardlinkPolicy, symlinkPolicy, hardlinkmap, fileAttributeFilter, enumeratefilter, ignorenames, mixinqueue, disableBackupExclusionXattr, token).ConfigureAwait(false))
                    yield return entry;

                // "Store" symlinks were enqueued by the filter instead of being yielded.
                // For sync there is no recursion to defer them past, so drain them out
                // now and yield them to the caller as regular entries.
                while (mixinqueue.Count > 0)
                    yield return mixinqueue.Dequeue();
            }
        }

        public static Task RunAsync(
            Channels channels,
            ISourceProvider sourceProvider,
            UsnJournalService? journalService,
            FileAttributes fileAttributeFilter,
            Library.Utility.IFilter emitfilter,
            Options.SymlinkStrategy symlinkPolicy,
            Options.HardlinkStrategy hardlinkPolicy,
            bool disableBackupExclusionXattr,
            bool excludeemptyfolders,
            Func<string, bool>? keepEmptyFolder,
            string[]? ignorenames,
            HashSet<string> blacklistPaths,
            IEnumerable<string>? changedfilelist,
            ChangeList? changeList,
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
                if (token.IsCancellationRequested)
                    return;

                // Listing a folder and reading the attributes of an entry are synchronous calls that
                // do not look at a cancellation token. One that is stuck, as on a network share that
                // stopped answering, would hold up the backup, so the listing runs on its own and is
                // no longer waited for once the operation is aborted. It only writes to the output
                // channel, which is retired by then, so a listing that ends later stops there.
                // A listing that is only no longer wanted, as the file counter's is when the backup
                // is done, is waited for: it stops at the next entry, and the source it reads is
                // disposed once the backup returns.
                await Task.Run(async () =>
                {
                    var source = EnumerateAsync(sourceProvider, journalService, fileAttributeFilter, emitfilter, symlinkPolicy, hardlinkPolicy, disableBackupExclusionXattr, excludeemptyfolders, keepEmptyFolder, ignorenames, blacklistPaths, changedfilelist, changeList, token);

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
                        if (token.IsCancellationRequested || !await taskreader.ProgressRendevouzAsync().ConfigureAwait(false))
                        {
                            onStopRequested?.Invoke();
                            return;
                        }

                        await self.Output.WriteAsync(s);
                    }
                }).UntilCancelledAsync(taskreader.ProgressToken).ConfigureAwait(false);
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
        /// Checks if a folder is below another folder. The paths are compared with the casing
        /// rules of the client filesystem, but a path that is not longer than the parent is never
        /// below it: two folders whose paths differ only by casing are siblings, which a remote
        /// source can list as it names items by case-sensitive ids.
        /// </summary>
        /// <param name="path">The folder path to check</param>
        /// <param name="parent">The path of the potential parent folder</param>
        /// <returns><c>true</c> if the folder is below the parent, <c>false</c> otherwise</returns>
        private static bool IsFolderBelow(string path, string parent)
            => path.Length > parent.Length && path.StartsWith(parent, Library.Utility.Utility.ClientFilenameStringComparison);

        /// <summary>
        /// Excludes empty folders.
        /// </summary>
        /// <returns>The list without empty folders.</returns>
        /// <param name="source">The list with potential empty folders.</param>
        internal static async IAsyncEnumerable<ISourceProviderEntry> ExcludeEmptyFoldersAsync(IAsyncEnumerable<ISourceProviderEntry> source, Func<string, bool>? keepEmptyFolder, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var pathstack = new Stack<DirectoryStackEntry>();

            await foreach (var s in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                // Keep track of directories
                if (s.IsFolder)
                {
                    while (pathstack.Count > 0 && !IsFolderBelow(s.Path, pathstack.Peek().Item.Path))
                    {
                        var e = pathstack.Pop();
                        // A folder enumerated incrementally lists only what changed, so it
                        // cannot be told apart from an empty one and is kept
                        if (e.AnyEntries || pathstack.Count == 0 || keepEmptyFolder?.Invoke(e.Item.Path) == true)
                        {
                            // Propagate the any-flag upwards
                            if (pathstack.Count > 0)
                                pathstack.Peek().AnyEntries = true;

                            yield return e.Item;
                        }
                        else
                            Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingEmptyFolder", "Excluding empty folder {0}", e.Item);
                    }

                    if (pathstack.Count == 0 || IsFolderBelow(s.Path, pathstack.Peek().Item.Path))
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
                if (e.AnyEntries || pathstack.Count == 0 || keepEmptyFolder?.Invoke(e.Item.Path) == true)
                {
                    // Propagate the any-flag upwards
                    if (pathstack.Count > 0)
                        pathstack.Peek().AnyEntries = true;

                    yield return e.Item;
                }
            }
        }

        /// <summary>
        /// Gets the providers that are not the local file source, which the change journal does not cover
        /// </summary>
        /// <param name="sourceProvider">The source provider, possibly a combination of providers</param>
        /// <returns>The providers other than the local file source</returns>
        internal static IEnumerable<ISourceProvider> GetNonFileProviders(ISourceProvider sourceProvider)
            => GetProviders(sourceProvider).Where(x => x is not LocalFileSource);

        /// <summary>
        /// Gets the individual providers a source provider consists of: the providers
        /// of a combiner, or the provider itself
        /// </summary>
        /// <param name="sourceProvider">The source provider, possibly a combination of providers</param>
        /// <returns>The individual providers</returns>
        internal static IEnumerable<ISourceProvider> GetProviders(ISourceProvider sourceProvider)
            => sourceProvider is Combiner c ? c.Providers : [sourceProvider];

        /// <summary>
        /// Performs recursive traversal of the sources
        /// </summary>
        /// <param name="entries">The entries to recurse</param>
        /// <param name="filter">The filter to apply</param>
        /// <returns></returns>
        private static async IAsyncEnumerable<ISourceProviderEntry> RecurseEntriesAsync(IAsyncEnumerable<ISourceProviderEntry> entries, Func<ISourceProviderEntry, ValueTask<bool>> filter, [EnumeratorCancellation] CancellationToken cancellationToken)
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
#if DEBUG
                    if (!e.Path.EndsWith(Path.DirectorySeparatorChar))
                        throw new Exception($"Expected folder entry to end with directory separator char: {e.Path}");
#endif
                    try
                    {
                        // Guard against duplicate paths from the source provider.
                        // The comparison is exact, as the database tells paths apart by their exact
                        // casing, and remote sources name items by case-sensitive ids: comparing
                        // without case on Windows drops every item whose id differs from another
                        // only by casing
                        var known = new HashSet<string>(StringComparer.Ordinal);

                        // We only filter new items, as we assume the input is already filtered
                        await foreach (var r in e.Enumerate(cancellationToken).ConfigureAwait(false))
                            if (await filter(r).ConfigureAwait(false))
                            {
                                if (known.Add(r.Path))
                                    work.Push(r);
                                else
                                    Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "DuplicatePath", null, "Duplicate path found: {0}", r.Path);
                            }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogExceptionHelper.LogCommonWarning(ex, FILTER_LOGTAG, "PathProcessingErrorEnumerate", e.Path, "Failed to enumerate path: {0}");
                    }
                }
            }
        }

        /// <summary>
        /// Builds the enumeration filter for an emit filter. If the emit filter only has
        /// includes, folders are always accepted for enumeration so includes can match nested files.
        /// </summary>
        /// <param name="emitfilter">The emit filter</param>
        /// <returns>The enumeration filter</returns>
        private static Library.Utility.IFilter GetEnumerateFilter(Library.Utility.IFilter emitfilter)
        {
            Library.Utility.FilterExpression.AnalyzeFilters(emitfilter, out var includes, out var excludes);
            if (includes && !excludes)
                return Library.Utility.FilterExpression.Combine(emitfilter, new Duplicati.Library.Utility.FilterExpression("*" + System.IO.Path.DirectorySeparatorChar, true))
                    ?? new Duplicati.Library.Utility.FilterExpression();
            return emitfilter;
        }

        /// <summary>
        /// The local file source roots of a change list, in the shapes needed to match paths against them
        /// </summary>
        private sealed class ChangeListRoots
        {
            private readonly List<string> _folderRoots;
            private readonly List<string> _fileRoots;

            public ChangeListRoots(IEnumerable<string> roots)
            {
                // A source inside another source is walked as a root of its own, so the
                // innermost root a path is below is the one that applies
                _folderRoots = roots.Where(x => x.EndsWith(Path.DirectorySeparatorChar)).OrderByDescending(x => x.Length).ToList();
                _fileRoots = roots.Where(x => !x.EndsWith(Path.DirectorySeparatorChar)).ToList();
            }

            /// <summary>
            /// Finds the source root a normalized path belongs to
            /// </summary>
            /// <param name="path">The normalized path</param>
            /// <returns>The root, folders ending with a separator, or null if the path is outside every source</returns>
            public string? Find(string path)
            {
                var comparison = Library.Utility.Utility.ClientFilenameStringComparison;
                foreach (var root in _folderRoots)
                    if (path.StartsWith(root, comparison) || path.Equals(ChangeList.NormalizePath(root), comparison))
                        return root;
                foreach (var root in _fileRoots)
                    if (path.Equals(root, comparison))
                        return root;
                return null;
            }

            /// <summary>
            /// The folder roots, innermost first
            /// </summary>
            public IReadOnlyList<string> FolderRoots => _folderRoots;

            /// <summary>
            /// Whether a normalized path is the root itself
            /// </summary>
            public static bool IsRoot(string path, string root)
                => path.Equals(ChangeList.NormalizePath(root), Library.Utility.Utility.ClientFilenameStringComparison);
        }

        /// <summary>
        /// Gets the parent folder of a path, with a trailing separator, or null for a root
        /// </summary>
        /// <param name="path">The path</param>
        /// <returns>The parent folder</returns>
        private static string? ParentFolder(string path)
        {
            var parent = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrEmpty(parent) ? null : Util.AppendDirSeparator(parent);
        }

        /// <summary>
        /// Resolves a normalized path to a file or folder entry
        /// </summary>
        private static async Task<ISourceProviderEntry?> ResolveAsync(ISourceProvider sourceProvider, string path, CancellationToken token)
            => await sourceProvider.GetEntryAsync(path, false, token).ConfigureAwait(false)
                ?? await sourceProvider.GetEntryAsync(Util.AppendDirSeparator(path), true, token).ConfigureAwait(false);

        /// <summary>
        /// Finds the spelling a path has on disk, by listing its parent folder
        /// </summary>
        /// <param name="sourceProvider">The source provider</param>
        /// <param name="path">The normalized path</param>
        /// <param name="token">The cancellation token</param>
        /// <returns>The normalized path as spelled on disk, or null if it is not found</returns>
        private static async Task<string?> ResolveSpellingAsync(ISourceProvider sourceProvider, string path, CancellationToken token)
        {
            var parentPath = ParentFolder(path);
            if (parentPath == null)
                return null;

            var parent = await sourceProvider.GetEntryAsync(parentPath, true, token).ConfigureAwait(false);
            if (parent == null)
                return null;

            await foreach (var child in parent.Enumerate(token).WithCancellation(token).ConfigureAwait(false))
            {
                var childPath = ChangeList.NormalizePath(child.Path);
                if (childPath.Equals(path, Library.Utility.Utility.ClientFilenameStringComparison))
                    return childPath;
            }

            return null;
        }

        /// <summary>
        /// Turns a raw change list into a normalized one, written to a new file:
        /// <list type="bullet">
        /// <item>each path appears once, and the folder holding a listed path is listed too, as a change
        /// below a folder changes the folder's own timestamps, and an ignore-marker file decides whether
        /// its folder is backed up;</item>
        /// <item>paths that differ only in case on a case-insensitive file system are written once, as spelled on disk;</item>
        /// <item>a path marked for recursion stays marked, even when it is now a file, as it may have been a folder;</item>
        /// <item>a path that no longer exists is rescanned with its subtree, as it may have been a folder;</item>
        /// <item>a folder examined by itself is rescanned with its subtree when it is excluded now, or neither it
        /// nor anything below it was in the previous fileset, as its children were then decided by something
        /// the list does not report.</item>
        /// </list>
        /// A list that cannot be trusted is refused, and the caller scans everything: a path outside
        /// the sources, when <paramref name="rejectOutsidePaths"/> is set, shows that the list was
        /// recorded under another spelling of the sources, and a listed file with other hard links
        /// may have changed those links, which the list does not name.
        /// The decisions use a filter of their own, so they do not touch the state of the enumeration.
        /// </summary>
        /// <param name="rawListPath">The raw change list</param>
        /// <param name="extraPaths">Further paths to rescan with their subtrees, such as paths a previous run failed on</param>
        /// <param name="fileSourceRoots">The local file source roots; folders end with a directory separator</param>
        /// <param name="normalizedListPath">The file to write the normalized list to</param>
        /// <param name="rejectOutsidePaths">Whether a listed path outside the sources makes the list unusable</param>
        /// <param name="photosLibraryIsVirtual">Whether a Photos library is backed up as an export, so a change inside it makes the list unusable</param>
        /// <param name="sourceProvider">The source provider</param>
        /// <param name="existedBefore">Whether a folder, or anything below it, was part of the previous fileset</param>
        /// <param name="folderExistedBefore">Whether a folder itself was part of the previous fileset</param>
        /// <param name="fileAttributeFilter">The file attributes to exclude</param>
        /// <param name="emitfilter">The emit filter</param>
        /// <param name="symlinkPolicy">The symlink policy</param>
        /// <param name="hardlinkPolicy">The hardlink policy</param>
        /// <param name="disableBackupExclusionXattr">Whether to disable xattr-based backup exclusion</param>
        /// <param name="ignorenames">The ignore-marker filenames, if any</param>
        /// <param name="blacklistPaths">The blacklisted paths</param>
        /// <param name="token">The cancellation token</param>
        /// <returns>The normalized change list, or null if the list cannot be trusted</returns>
        internal static async Task<ChangeList?> NormalizeChangeListAsync(
            string rawListPath,
            IEnumerable<string> extraPaths,
            IReadOnlyList<string> fileSourceRoots,
            string normalizedListPath,
            bool rejectOutsidePaths,
            bool photosLibraryIsVirtual,
            ISourceProvider sourceProvider,
            Func<string, Task<bool>> existedBefore,
            Func<string, Task<bool>> folderExistedBefore,
            FileAttributes fileAttributeFilter,
            Library.Utility.IFilter emitfilter,
            Options.SymlinkStrategy symlinkPolicy,
            Options.HardlinkStrategy hardlinkPolicy,
            bool disableBackupExclusionXattr,
            string[]? ignorenames,
            HashSet<string> blacklistPaths,
            CancellationToken token)
        {
            var comparer = Library.Utility.Utility.ClientFilenameStringComparer;
            var roots = new ChangeListRoots(fileSourceRoots);

            // A source inside another one is only kept when the outer walk cannot reach it, and a
            // rescan of a folder that holds it, or that folder's history, would then mix the two
            var nested = roots.FolderRoots.FirstOrDefault(inner => roots.FolderRoots.Any(outer => inner.Length > outer.Length && inner.StartsWith(outer, Library.Utility.Utility.ClientFilenameStringComparison)));
            if (nested != null)
            {
                Logging.Log.WriteInformationMessage(FILTER_LOGTAG, "ChangeListNestedSources", "The source \"{0}\" is inside another source, which a change list cannot describe, so all files are scanned", nested);
                return null;
            }

            var enumeratefilter = GetEnumerateFilter(emitfilter);
            if (ignorenames != null && ignorenames.Length == 0)
                ignorenames = null;

            // A filter with throwaway state, so a decision here does not mark a hardlink as
            // seen or queue a symlink for the enumeration
            ValueTask<bool> Check(ISourceProviderEntry entry)
                => SourceFileEntryFilterAsync(entry, blacklistPaths, hardlinkPolicy, symlinkPolicy, new Dictionary<string, string>(), fileAttributeFilter, enumeratefilter, ignorenames, new Queue<ISourceProviderEntry>(), disableBackupExclusionXattr, token);

            // Path (as the file system compares names) -> spelling, whether the spelling is
            // in doubt, and whether the subtree must be rescanned
            var entries = new Dictionary<string, (string Spelling, bool Conflict, bool Recurse)>(comparer);
            long outside = 0;
            string? firstOutside = null;
            string? inPhotosLibrary = null;

            void Add(string path, bool recurse)
            {
                if (entries.TryGetValue(path, out var existing))
                    entries[path] = (existing.Spelling, existing.Conflict || !string.Equals(existing.Spelling, path, StringComparison.Ordinal), existing.Recurse || recurse);
                else
                    entries[path] = (path, false, recurse);
            }

            void AddChange(string path, bool recurse, bool fromList)
            {
                var root = roots.Find(path);
                if (root == null)
                {
                    if (fromList)
                    {
                        outside++;
                        firstOutside ??= path;
                    }
                    Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ChangeListPathOutsideSources", "Ignoring listed path outside the sources: {0}", path);
                    return;
                }

                // The library's backup is generated from its database, not from the changed files
                if (photosLibraryIsVirtual && inPhotosLibrary == null && path.Split(Path.DirectorySeparatorChar).Any(x => x.EndsWith(".photoslibrary", StringComparison.OrdinalIgnoreCase)))
                    inPhotosLibrary = path;

                Add(path, recurse);

                // A change below a folder changes the folder's timestamps, and an ignore marker
                // decides whether the folder is backed up at all, so the folder is examined too
                if (!ChangeListRoots.IsRoot(path, root))
                {
                    var parent = ParentFolder(path);
                    if (parent != null)
                        Add(ChangeList.NormalizePath(parent), false);
                }
            }

            await foreach (var change in ChangeList.ReadFileAsync(rawListPath, token).ConfigureAwait(false))
                AddChange(change.Path, change.Recurse, true);
            foreach (var path in extraPaths)
                AddChange(ChangeList.NormalizePath(path), true, false);

            // A watcher on a folder does not report changes to the folder itself, so every root
            // is examined. A root that exists now but had nothing in the previous fileset was
            // missing then, and the watcher may not have seen it come back, so it is walked in full
            foreach (var root in roots.FolderRoots)
            {
                var exists = await sourceProvider.GetEntryAsync(root, true, token).ConfigureAwait(false) != null;
                AddChange(ChangeList.NormalizePath(root), exists && !await existedBefore(root).ConfigureAwait(false), false);
            }

            if (inPhotosLibrary != null)
            {
                Logging.Log.WriteInformationMessage(FILTER_LOGTAG, "ChangeListPhotosLibrary", "The change list names \"{0}\" inside a Photos library, which is backed up as an export the list cannot describe, so all files are scanned", inPhotosLibrary);
                return null;
            }

            if (rejectOutsidePaths && outside > 0)
            {
                Logging.Log.WriteInformationMessage(FILTER_LOGTAG, "ChangeListOutsideSources", "The change list names {0} paths outside the sources, such as \"{1}\", so it was recorded under another spelling of the sources and all files are scanned", outside, firstOutside!);
                return null;
            }

            // A path that cannot be resolved is only gone if its folder can be listed without it;
            // a folder that cannot be listed hides its content, as a full scan would also find
            var listings = new Dictionary<string, HashSet<string>?>(comparer);
            var goneMarker = new HashSet<string>();

            async Task<HashSet<string>?> ListFolderAsync(string folder)
            {
                if (listings.TryGetValue(folder, out var names))
                    return names;

                var entry = await sourceProvider.GetEntryAsync(Util.AppendDirSeparator(folder), true, token).ConfigureAwait(false);
                if (entry == null)
                {
                    // A folder that is a file now holds nothing; otherwise it is either gone, or
                    // hidden by a folder above it that cannot be listed
                    if (await sourceProvider.GetEntryAsync(folder, false, token).ConfigureAwait(false) != null)
                        names = goneMarker;
                    else
                        names = await IsGoneAsync(folder).ConfigureAwait(false) ? goneMarker : null;
                }
                else
                {
                    try
                    {
                        names = new HashSet<string>(comparer);
                        await foreach (var child in entry.Enumerate(token).WithCancellation(token).ConfigureAwait(false))
                            names.Add(Path.GetFileName(ChangeList.NormalizePath(child.Path)));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ChangeListFolderNotListed", ex, "Could not list {0}", folder);
                        names = null;
                    }
                }

                listings[folder] = names;
                return names;
            }

            async Task<bool> IsGoneAsync(string path)
            {
                var root = roots.Find(path);
                var parent = ParentFolder(path);
                // A missing source root is reported by the expansion; above it there is nothing to ask
                if (root == null || ChangeListRoots.IsRoot(path, root) || parent == null)
                    return true;

                var names = await ListFolderAsync(ChangeList.NormalizePath(parent)).ConfigureAwait(false);
                if (names == null)
                    return false;
                return ReferenceEquals(names, goneMarker) || !names.Contains(Path.GetFileName(path));
            }

            var recursed = new HashSet<string>(comparer);
            var gonePaths = new HashSet<string>(comparer);
            long written = 0, gone = 0, upgraded = 0;
            string? hardlinked = null;

            await using (var stream = new FileStream(normalizedListPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)) { NewLine = "\n" })
            {
                foreach (var (key, value) in entries)
                {
                    token.ThrowIfCancellationRequested();
                    var path = value.Spelling;
                    var recurse = value.Recurse;

                    // On a case-insensitive file system a case-only rename lists both spellings;
                    // the entry is stored as spelled on disk
                    if (value.Conflict)
                        path = await ResolveSpellingAsync(sourceProvider, path, token).ConfigureAwait(false) ?? path;

                    var root = roots.Find(path)!;
                    var entry = await ResolveAsync(sourceProvider, path, token).ConfigureAwait(false);

                    // A file system mounted case-insensitively on a case-sensitive system still
                    // resolves a name that was renamed away by a change of case; the name is only
                    // kept if the folder holds it exactly as spelled
                    if (entry != null && recurse && Library.Utility.Utility.IsFSCaseSensitive && !ChangeListRoots.IsRoot(path, root))
                    {
                        var parent = ParentFolder(path);
                        var names = parent == null ? null : await ListFolderAsync(ChangeList.NormalizePath(parent)).ConfigureAwait(false);
                        if (names != null && !ReferenceEquals(names, goneMarker) && !names.Contains(Path.GetFileName(path)))
                            entry = null;
                    }

                    if (entry == null)
                    {
                        // It may have been a folder, so its subtree goes with it
                        gone++;
                        recurse = true;
                        gonePaths.Add(path);

                        // It may also be hidden by a folder that cannot be listed; then it is
                        // looked at again by the next run, as a full scan would
                        if (!await IsGoneAsync(path).ConfigureAwait(false))
                        {
                            FailedPathRecorder.Record(path);
                            Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "ChangeListPathNotAccessible", null, "Excluding path because its folder cannot be listed: {0}", path);
                        }
                    }
                    else if (!entry.IsFolder)
                    {
                        // A write through one hard link changes the others, which the list does not name
                        if (IsHardlinked(entry))
                        {
                            hardlinked = path;
                            break;
                        }
                        // A path marked for recursion stays marked: it may have been a folder until
                        // just now, and what was below it must leave the fileset. A file that was a
                        // folder in the previous fileset is marked too, whatever the watcher reported
                        if (!recurse && await folderExistedBefore(Util.AppendDirSeparator(path)).ConfigureAwait(false))
                        {
                            upgraded++;
                            recurse = true;
                        }
                    }
                    else if (!recurse && !ChangeListRoots.IsRoot(path, root))
                    {
                        // A folder whose exclusion changed decides about children the list does not name,
                        // and one that cannot be listed hides them, as a full scan would also find
                        if (!await Check(entry).ConfigureAwait(false) || !await existedBefore(entry.Path).ConfigureAwait(false) || !await CanListAsync(entry).ConfigureAwait(false))
                        {
                            upgraded++;
                            recurse = true;
                        }
                    }
                    else if (!recurse && !await CanListAsync(entry).ConfigureAwait(false))
                    {
                        // A root that cannot be listed is walked, which reports it as a full scan does
                        upgraded++;
                        recurse = true;
                    }

                    if (recurse)
                        recursed.Add(path);

                    await writer.WriteAsync(recurse ? ChangeList.RECURSE_MARKER : ChangeList.EXAMINE_MARKER).ConfigureAwait(false);
                    await writer.WriteLineAsync(path).ConfigureAwait(false);
                    written++;
                }

                await writer.FlushAsync(token).ConfigureAwait(false);
            }

            if (hardlinked != null)
            {
                Logging.Log.WriteInformationMessage(FILTER_LOGTAG, "ChangeListHardlink", "The changed file \"{0}\" has other hard links, which the change list does not name, so all files are scanned", hardlinked);
                return null;
            }

            Logging.Log.WriteInformationMessage(FILTER_LOGTAG, "ChangeListNormalized", "The change list has {0} entries: {1} no longer exist, {2} folders are rescanned in full because their exclusion changed, {3} paths were outside the sources", written, gone, upgraded, outside);
            return new ChangeList(normalizedListPath, fileSourceRoots, recursed, gonePaths);
        }

        /// <summary>
        /// Checks whether a folder can be listed
        /// </summary>
        /// <param name="folder">The folder entry</param>
        /// <returns>True if the folder can be listed</returns>
        private static async Task<bool> CanListAsync(ISourceProviderEntry folder)
        {
            try
            {
                await foreach (var _ in folder.Enumerate(CancellationToken.None).ConfigureAwait(false))
                    break;
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ChangeListFolderNotListed", ex, "Could not list {0}", folder.Path);
                return false;
            }
        }

        /// <summary>
        /// Checks whether a file has other hard links
        /// </summary>
        /// <param name="entry">The file entry</param>
        /// <returns>True if the file has other hard links</returns>
        private static bool IsHardlinked(ISourceProviderEntry entry)
        {
            try
            {
                if (!string.IsNullOrEmpty(entry.HardlinkTargetId))
                    return true;

                // Not every snapshot service reports hard links, so the link count is read directly
                if (OperatingSystem.IsWindows())
                    return WindowsLinkCount.Get(entry.Path) > 1;
                if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                    return PosixFile.GetHardlinkCount(entry.Path) > 1;

                throw new InvalidOperationException("OS is not supported?");
            }
            catch (Exception ex)
            {
                // Not knowing is treated as linked, which makes the run scan everything
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "HardlinkCheckFailed", ex, "Could not check {0} for hard links", entry.Path);
                return true;
            }
        }

        /// <summary>
        /// Resolves the entries of a normalized change list against the source provider. A listed
        /// path that no longer exists produces nothing, so the carry-forward step drops it. A folder
        /// marked for recursion is walked like a source root; any other entry is examined by itself.
        /// An entry below a folder marked for recursion is left to that folder, so nothing is
        /// produced twice. The filter is applied to every ancestor folder below the source root,
        /// as a full scan never descends into an excluded folder; the source root itself is never
        /// filtered, as in a full scan.
        /// </summary>
        /// <param name="changeList">The normalized change list</param>
        /// <param name="sourceProvider">The source provider to resolve against</param>
        /// <param name="filter">The entry filter of the enumeration</param>
        /// <param name="check">A filter with throwaway state, for ancestor checks</param>
        /// <param name="examinedFolders">Receives the folders that were examined without recursion</param>
        /// <param name="token">The cancellation token</param>
        /// <returns>The resolved entries</returns>
        private static async IAsyncEnumerable<ISourceProviderEntry> ExpandChangeListAsync(ChangeList changeList, ISourceProvider sourceProvider, Func<ISourceProviderEntry, ValueTask<bool>> filter, Func<ISourceProviderEntry, ValueTask<bool>> check, HashSet<string> examinedFolders, [EnumeratorCancellation] CancellationToken token)
        {
            var comparison = Library.Utility.Utility.ClientFilenameStringComparison;
            var roots = new ChangeListRoots(changeList.FileSourceRoots);
            var ancestorCache = new Dictionary<string, bool>(Library.Utility.Utility.ClientFilenameStringComparer);

            long read = 0, examined = 0, missing = 0, filtered = 0, covered = 0;

            bool HasRecursedAncestor(string path, string root)
            {
                var rootPath = ChangeList.NormalizePath(root);
                var current = ParentFolder(path);
                while (current != null)
                {
                    var normalized = ChangeList.NormalizePath(current);
                    if (changeList.RecursedPaths.Contains(normalized))
                        return true;
                    if (normalized.Length <= rootPath.Length)
                        return false;
                    current = ParentFolder(current);
                }

                return false;
            }

            async ValueTask<bool> AncestorsIncludedAsync(string path, string root)
            {
                var current = ParentFolder(path);
                // The root is not filtered, so the walk stops below it
                while (current != null && current.Length > root.Length && current.StartsWith(root, comparison))
                {
                    if (!ancestorCache.TryGetValue(current, out var included))
                    {
                        var folder = await sourceProvider.GetEntryAsync(current, true, token).ConfigureAwait(false);
                        // A folder that cannot be resolved does not exclude what is below it
                        included = folder == null || await check(folder).ConfigureAwait(false);
                        ancestorCache[current] = included;
                    }

                    if (!included)
                        return false;

                    current = ParentFolder(current);
                }

                return true;
            }

            static async IAsyncEnumerable<ISourceProviderEntry> SingleAsync(ISourceProviderEntry entry)
            {
                await Task.CompletedTask;
                yield return entry;
            }

            await foreach (var change in changeList.ReadAsync(token).ConfigureAwait(false))
            {
                read++;
                var root = roots.Find(change.Path);
                if (root == null)
                    continue;

                if (HasRecursedAncestor(change.Path, root))
                {
                    covered++;
                    continue;
                }

                var isRoot = ChangeListRoots.IsRoot(change.Path, root);
                var entry = changeList.GonePaths.Contains(change.Path)
                    ? null
                    : await ResolveAsync(sourceProvider, change.Path, token).ConfigureAwait(false);

                if (entry == null)
                {
                    missing++;
                    if (isRoot)
                        Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "SourceMissing", null, "The source path does not exist: {0}", change.Path);
                    else
                        Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ChangeListPathMissing", "Listed path no longer exists: {0}", change.Path);
                    continue;
                }

                if (!isRoot && root.EndsWith(Path.DirectorySeparatorChar) && !await AncestorsIncludedAsync(entry.Path, root).ConfigureAwait(false))
                {
                    filtered++;
                    Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ChangeListPathUnderExcludedFolder", "Listed path is below an excluded folder: {0}", change.Path);
                    continue;
                }

                if (entry.IsFolder && change.Recurse)
                {
                    examined++;
                    // A source root is never filtered, as in a full scan
                    var rootEntry = isRoot ? entry : null;
                    await foreach (var e in RecurseEntriesAsync(SingleAsync(entry), e => ReferenceEquals(e, rootEntry) ? ValueTask.FromResult(true) : filter(e), token).ConfigureAwait(false))
                        yield return e;
                }
                else if (isRoot || await filter(entry).ConfigureAwait(false))
                {
                    examined++;
                    if (entry.IsFolder)
                        examinedFolders.Add(entry.Path);
                    yield return entry;
                }
                else
                {
                    filtered++;
                }
            }

            Logging.Log.WriteInformationMessage(FILTER_LOGTAG, "ChangeListProcessed", "Processed {0} change list entries: {1} examined, {2} covered by a rescanned folder, {3} no longer exist, {4} excluded by filters", read, examined, covered, missing, filtered);
        }

        /// <summary>
        /// Re-integrates the mixin queue to form a strictly sequential list of results
        /// </summary>
        /// <returns>The expanded list.</returns>
        /// <param name="worklist">The basic enumerable.</param>
        /// <param name="mixinqueue">The mix in queue.</param>
        /// <param name="emitfilter">The emitfilter.</param>
        /// <param name="enumeratefilter">The enumeratefilter.</param>
        private static async IAsyncEnumerable<ISourceProviderEntry> ExpandWorkListAsync(IAsyncEnumerable<ISourceProviderEntry> worklist, Queue<ISourceProviderEntry> mixinqueue, Library.Utility.IFilter emitfilter, Library.Utility.IFilter? enumeratefilter, [EnumeratorCancellation] CancellationToken cancellationToken)
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

            // Exclude the "System Volume Information" folder,
            // which contains system internal data that should not be backed up,
            // even if the process holds the backup privilege that allows reading it
            if (OperatingSystem.IsWindows() && entry.IsFolder && IsSystemVolumeInformationFolder(entry))
            {
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingSystemVolumeInformationFolder", "Excluding System Volume Information folder: {0}", entry.Path);
                return false;
            }

            // Exclude special files. On Linux and macOS, IsBlockDevice is true for every entry
            // that is not a regular file, a folder or a symlink: block and character devices,
            // FIFOs and sockets.
            try
            {
                if (entry.IsBlockDevice)
                {
                    Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingSpecialFile", "Excluding special file (device, FIFO or socket): {0}", entry.Path);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogExceptionHelper.LogCommonWarning(ex, FILTER_LOGTAG, "PathProcessingErrorSpecialFile", entry.Path);
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
                LogExceptionHelper.LogCommonWarning(ex, FILTER_LOGTAG, "PathProcessingErrorCharacterDevice", entry.Path);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if the given entry is a "System Volume Information" folder,
        /// based on the name, location, and attributes of the folder.
        /// This method is not inlined to prevent loading the snapshot types on non-Windows platforms.
        /// </summary>
        /// <param name="entry">The entry to evaluate.</param>
        /// <returns>True if the entry is a "System Volume Information" folder, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static bool IsSystemVolumeInformationFolder(ISourceProviderEntry entry)
        {
            // If we cannot read the attributes, we cannot verify the folder
            FileAttributes attributes;
            try
            {
                attributes = entry.Attributes;
            }
            catch (Exception ex)
            {
                LogExceptionHelper.LogCommonWarning(ex, FILTER_LOGTAG, "PathProcessingErrorAttributes", entry.Path);
                return false;
            }

            return Duplicati.Library.Snapshots.WindowsSnapshot.IsSystemVolumeInformationFolder(entry.Path, attributes);
        }

        /// <summary>
        /// Evaluates a single entry for inclusion in the backup
        /// </summary>
        /// <param name="entry">The current entry.</param>
        /// <param name="blacklistPaths">The blacklist paths.</param>
        /// <param name="hardlinkPolicy">The hardlink policy.</param>
        /// <param name="symlinkPolicy">The symlink policy.</param>
        /// <param name="hardlinkmap">The hardlink map.</param>
        /// <param name="fileAttributeFilter">The file attributes to exclude.</param>
        /// <param name="enumeratefilter">The enumerate filter.</param>
        /// <param name="ignorenames">The ignore names.</param>
        /// <param name="mixinqueue">The mixin queue.</param>
        /// <returns>True if the path should be returned, false otherwise.</returns>
        private static async ValueTask<bool> SourceFileEntryFilterAsync(ISourceProviderEntry entry, HashSet<string> blacklistPaths, Options.HardlinkStrategy hardlinkPolicy, Options.SymlinkStrategy symlinkPolicy, Dictionary<string, string> hardlinkmap, FileAttributes fileAttributeFilter, Duplicati.Library.Utility.IFilter enumeratefilter, string[]? ignorenames, Queue<ISourceProviderEntry> mixinqueue, bool disableBackupExclusionXattr, CancellationToken cancellationToken)
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
                    LogExceptionHelper.LogCommonWarning(ex, FILTER_LOGTAG, "PathProcessingErrorHardLink", entry.Path);
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
                    LogExceptionHelper.LogCommonWarning(ex, FILTER_LOGTAG, "PathProcessingErrorIgnoreFile", entry.Path);
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
                LogExceptionHelper.LogCommonWarning(ex, FILTER_LOGTAG, "PathProcessingErrorAttributes", entry.Path, "Failed to process path, using default attributes: {0}");
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

            // Filter entries that are marked as excluded from backups via filesystem extended attributes
            if (!disableBackupExclusionXattr && await HasBackupExclusionAttributeAsync(entry, cancellationToken).ConfigureAwait(false))
            {
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingPathFromBackupAttribute", "Excluding path marked as excluded from backups via filesystem attribute: {0}", entry.Path);
                return false;
            }

            // If the file is a symlink, apply special handling
            string? symlinkTarget = null;
            try
            {
                symlinkTarget = entry.SymlinkTarget;
            }
            catch (Exception ex)
            {
                LogExceptionHelper.LogCommonWarning(ex, FILTER_LOGTAG, "SymlinkTargetReadError", entry.Path, "Failed to read symlink target for path: {0}");
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

        /// <summary>
        /// Extended attribute prefix for files that should be excluded from backup.
        /// The "unix-ext:" prefix is added when reading the xattrs via the ISourceProviderEntry interface.
        /// </summary>
        internal static readonly ReadOnlySet<string> ExcludedBackupAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            // macOS common backup exclusion attributes
            "unix-ext:com.apple.metadata:com_apple_backup_excludeItem",
            // Linux custom attributes
            "unix-ext:duplicati.exclude",
            "unix-ext:user.duplicati.exclude"
        }.AsReadOnly();

        /// <summary>
        /// Checks if the entry has an attribute marking it as excluded from backups
        /// </summary>
        /// <param name="entry">The entry to check.</param>
        /// <returns>True if the entry has the attribute, false otherwise.</returns>
        private static async Task<bool> HasBackupExclusionAttributeAsync(ISourceProviderEntry entry, CancellationToken token)
        {
            try
            {
                var metadata = await entry.GetMinorMetadata(token).ConfigureAwait(false);
                if (metadata == null || metadata.Count == 0)
                    return false;

                if (metadata.Keys.Any(k => ExcludedBackupAttributes.Contains(k)))
                    return true;
            }
            catch (Exception ex)
            {
                LogExceptionHelper.LogCommonWarning(ex, FILTER_LOGTAG, "PathProcessingErrorMetadata", entry.Path, "Failed to process extended attributes for path: {0}");
            }

            return false;
        }
    }
}

