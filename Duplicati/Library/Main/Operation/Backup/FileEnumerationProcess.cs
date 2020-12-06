#region Disclaimer / License
// Copyright (C) 2019, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
//
#endregion
using System;
using CoCoL;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Snapshots;
using Duplicati.Library.Common.IO;

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

        public static Task Run(IEnumerable<string> sources, Snapshots.ISnapshotService snapshot, UsnJournalService journalService, FileAttributes fileAttributes, Duplicati.Library.Utility.IFilter sourcefilter, Duplicati.Library.Utility.IFilter emitfilter, Options.SymlinkStrategy symlinkPolicy, Options.HardlinkStrategy hardlinkPolicy, bool excludeemptyfolders, string[] ignorenames, string[] changedfilelist, ISet<string> forbiddenPaths, ITaskReader taskreader, CancellationToken token)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Output = Backup.Channels.SourcePaths.ForWrite
            },

            async self =>
            {
                if (!token.IsCancellationRequested)
                {
                    var hardlinkmap = new Dictionary<string, string>();
                    var mixinqueue = new Queue<string>();
                    Duplicati.Library.Utility.IFilter enumeratefilter = emitfilter;

                    bool includes;
                    bool excludes;
                    Library.Utility.FilterExpression.AnalyzeFilters(emitfilter, out includes, out excludes);
                    if (includes && !excludes)
                        enumeratefilter = Library.Utility.FilterExpression.Combine(emitfilter, new Duplicati.Library.Utility.FilterExpression("*" + System.IO.Path.DirectorySeparatorChar, true));

                    // Simplify checking for an empty list
                    if (ignorenames != null && ignorenames.Length == 0)
                        ignorenames = null;

                    // If we have a specific list, use that instead of enumerating the filesystem
                    IEnumerable<string> worklist;
                    if (changedfilelist != null && changedfilelist.Length > 0)
                    {
                        worklist = changedfilelist.Where(x =>
                        {
                            var fa = FileAttributes.Normal;
                            try
                            {
                                fa = snapshot.GetAttributes(x);
                            }
                            catch
                            {
                            }

                            if (token.IsCancellationRequested)
                            {
                                return false;
                            }

                            return AttributeFilter(x, fa, snapshot, sourcefilter, hardlinkPolicy, symlinkPolicy, hardlinkmap, fileAttributes, enumeratefilter, ignorenames, mixinqueue);
                        });
                    }
                    else
                    {
                        Library.Utility.Utility.EnumerationFilterDelegate attributeFilter = (root, path, attr) =>
                            AttributeFilter(path, attr, snapshot, sourcefilter, hardlinkPolicy, symlinkPolicy, hardlinkmap, fileAttributes, enumeratefilter, ignorenames, mixinqueue);

                        if (journalService != null)
                        {
                            // filter sources using USN journal, to obtain a sub-set of files / folders that may have been modified
                            sources = journalService.GetModifiedSources(attributeFilter);
                        }

                        worklist = snapshot.EnumerateFilesAndFolders(sources, attributeFilter, (rootpath, errorpath, ex) =>
                        {
                            Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "FileAccessError", ex, "Error reported while accessing file: {0}", errorpath);
                        });
                    }

                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    var source = ExpandWorkList(worklist, mixinqueue, emitfilter, enumeratefilter);
                    source = ExcludeForbiddenPaths(source, forbiddenPaths);
                    if (excludeemptyfolders)
                        source = ExcludeEmptyFolders(source);

                    // Process each path, and dequeue the mixins with symlinks as we go
                    foreach (var s in source)
                    {
                        if (token.IsCancellationRequested)
                        {
                            break;
                        }

                        if (!await taskreader.ProgressAsync)
                        {
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
            /// The path for the folder
            /// </summary>
            public string Path;
            /// <summary>
            /// A flag indicating if any items are found in this folder
            /// </summary>
            public bool AnyEntries;

        }

        /// <summary>
        /// Excludes empty folders.
        /// </summary>
        /// <returns>The list without empty folders.</returns>
        /// <param name="source">The list with potential empty folders.</param>
        private static IEnumerable<string> ExcludeEmptyFolders(IEnumerable<string> source)
        {
            var pathstack = new Stack<DirectoryStackEntry>();

            foreach (var s in source)
            {
                // Keep track of directories
                var isDirectory = s[s.Length - 1] == System.IO.Path.DirectorySeparatorChar;
                if (isDirectory)
                {
                    while (pathstack.Count > 0 && !s.StartsWith(pathstack.Peek().Path, Library.Utility.Utility.ClientFilenameStringComparison))
                    {
                        var e = pathstack.Pop();
                        if (e.AnyEntries || pathstack.Count == 0)
                        {
                            // Propagate the any-flag upwards
                            if (pathstack.Count > 0)
                                pathstack.Peek().AnyEntries = true;
                                
                            yield return e.Path;
                        }
                        else
                            Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingEmptyFolder", "Excluding empty folder {0}", e.Path);
                    }

                    if (pathstack.Count == 0 || s.StartsWith(pathstack.Peek().Path, Library.Utility.Utility.ClientFilenameStringComparison))
                    {
                        pathstack.Push(new DirectoryStackEntry() { Path = s });
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
                if (e.AnyEntries|| pathstack.Count == 0)
                {
                    // Propagate the any-flag upwards
                    if (pathstack.Count > 0)
                        pathstack.Peek().AnyEntries = true;

                    yield return e.Path;
                }
            }
        }

        /// <summary>
        /// Exclude forbidden paths.
        /// </summary>
        /// <param name="paths">The candidate collection of paths.</param>
        /// <param name="forbiddenPaths">The paths that should never be included.</param>
        /// <returns>The collection of paths with the forbidden paths removed.</returns>
        public static IEnumerable<string> ExcludeForbiddenPaths(IEnumerable<string> paths, ISet<string> forbiddenPaths)
        {
            foreach (string path in paths)
            {
                if (forbiddenPaths != null && forbiddenPaths.Contains(path))
                {
                    Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingForbiddenPath", "Excluding forbidden path: {0}", path);
                    continue;
                }

                yield return path;
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
        private static IEnumerable<string> ExpandWorkList(IEnumerable<string> worklist, Queue<string> mixinqueue, Library.Utility.IFilter emitfilter, Library.Utility.IFilter enumeratefilter)
        {
            // Process each path, and dequeue the mixins with symlinks as we go
            foreach (var s in worklist)
            {
                while (mixinqueue.Count > 0)
                    yield return mixinqueue.Dequeue();

                Library.Utility.IFilter m;
                if (emitfilter != enumeratefilter && !Library.Utility.FilterExpression.Matches(emitfilter, s, out m))
                    continue;

                yield return s;
            }

            // Trailing symlinks are caught here
            while (mixinqueue.Count > 0)
                yield return mixinqueue.Dequeue();
        }

        /// <summary>
        /// Plugin filter for enumerating a list of files.
        /// </summary>
        /// <returns>True if the path should be returned, false otherwise.</returns>
        /// <param name="path">The current path.</param>
        /// <param name="attributes">The file or folder attributes.</param>
        private static bool AttributeFilter(string path, FileAttributes attributes, Snapshots.ISnapshotService snapshot, Library.Utility.IFilter sourcefilter, Options.HardlinkStrategy hardlinkPolicy, Options.SymlinkStrategy symlinkPolicy, Dictionary<string, string> hardlinkmap, FileAttributes fileAttributes, Duplicati.Library.Utility.IFilter enumeratefilter, string[] ignorenames, Queue<string> mixinqueue)
        {
			// Step 1, exclude block devices
			try
            {
                if (snapshot.IsBlockDevice(path))
                {
                    Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingBlockDevice", "Excluding block device: {0}", path);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "PathProcessingError", ex, "Failed to process path: {0}", path);
                return false;
            }

            // Check if we explicitly include this entry
            Duplicati.Library.Utility.IFilter sourcematch;
            bool sourcematches;
            if (sourcefilter.Matches(path, out sourcematches, out sourcematch) && sourcematches)
            {
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "IncludingSourcePath", "Including source path: {0}", path);
                return true;
            }

            // If we have a hardlink strategy, obey it
            if (hardlinkPolicy != Options.HardlinkStrategy.All)
            {
                try
                {
                    var id = snapshot.HardlinkTargetID(path);
                    if (id != null)
                    {
                        if (hardlinkPolicy == Options.HardlinkStrategy.None)
                        {
                            Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingHardlinkByPolicy", "Excluding hardlink: {0} ({1})", path, id);
                            return false;
                        }
                        else if (hardlinkPolicy == Options.HardlinkStrategy.First)
                        {
                            string prevPath;
                            if (hardlinkmap.TryGetValue(id, out prevPath))
                            {
                                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingDuplicateHardlink", "Excluding hardlink ({1}) for: {0}, previous hardlink: {2}", path, id, prevPath);
                                return false;
                            }
                            else
                            {
                                hardlinkmap.Add(id, path);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "PathProcessingError", ex, "Failed to process path: {0}", path);
                    return false;
                }                    
            }

            if (ignorenames != null && (attributes & FileAttributes.Directory) != 0)
            {
                try
                {
                    foreach (var n in ignorenames)
                    {
                        var ignorepath = SystemIO.IO_OS.PathCombine(path, n);
                        if (snapshot.FileExists(ignorepath))
                        {
                            Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingPathDueToIgnoreFile", "Excluding path because ignore file was found: {0}", ignorepath);
                            return false;
                        }
                    }
                }
                catch(Exception ex)
                {
                    Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "PathProcessingError", ex, "Failed to process path: {0}", path);
                }
            }

            // If we exclude files based on attributes, filter that
            if ((fileAttributes & attributes) != 0)
            {
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingPathFromAttributes", "Excluding path due to attribute filter: {0}", path);
                return false;
            }

            // Then check if the filename is not explicitly excluded by a filter
            Library.Utility.IFilter match;
            var filtermatch = false;
            if (!Library.Utility.FilterExpression.Matches(enumeratefilter, path, out match))
            {
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludingPathFromFilter", "Excluding path due to filter: {0} => {1}", path, match == null ? "null" : match.ToString());
                return false;
            }
            else if (match != null)
            {
                filtermatch = true;
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "IncludingPathFromFilter", "Including path due to filter: {0} => {1}", path, match.ToString());
            }

            // If the file is a symlink, apply special handling
            var isSymlink = snapshot.IsSymlink(path, attributes);
            string symlinkTarget = null;
            if (isSymlink)
                try { symlinkTarget = snapshot.GetSymlinkTarget(path); }
                catch (Exception ex) { Logging.Log.WriteExplicitMessage(FILTER_LOGTAG, "SymlinkTargetReadError", ex, "Failed to read symlink target for path: {0}", path); }

            if (isSymlink)
            {
                if (!string.IsNullOrWhiteSpace(symlinkTarget))
                {
                    if (symlinkPolicy == Options.SymlinkStrategy.Ignore)
                    {
                        Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "ExcludeSymlink", "Excluding symlink: {0}", path);
                        return false;
                    }

                    if (symlinkPolicy == Options.SymlinkStrategy.Store)
                    {
                        Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "StoreSymlink", "Storing symlink: {0}", path);

                        // We return false because we do not want to recurse into the path,
                        // but we add the symlink to the mixin so we process the symlink itself
                        mixinqueue.Enqueue(path);
                        return false;
                    }
                }
                else
                {
                    Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "FollowingEmptySymlink", "Treating empty symlink as regular path {0}", path);
                }
            }

            if (!filtermatch)
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "IncludingPath", "Including path as no filters matched: {0}", path);

            // All the way through, yes!
            return true;
        }
    }
}

