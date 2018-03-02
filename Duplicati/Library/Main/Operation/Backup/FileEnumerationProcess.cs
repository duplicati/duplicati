//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using CoCoL;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Snapshots;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// The file enumeration process takes a list of source folders as input,
    /// applies all filters requested and emits the filtered set of filenames
    /// to its output channel
    /// </summary>
    internal static class FileEnumerationProcess 
    {
        public static Task Run(Snapshots.ISnapshotService snapshot, FileAttributes attributeFilter, Duplicati.Library.Utility.IFilter sourcefilter, Duplicati.Library.Utility.IFilter emitfilter, Options.SymlinkStrategy symlinkPolicy, Options.HardlinkStrategy hardlinkPolicy, string[] changedfilelist, Common.ITaskReader taskreader)
        {
            return AutomationExtensions.RunTask(
            new {
                LogChannel = Common.Channels.LogChannel.ForWrite,
                Output = Backup.Channels.SourcePaths.ForWrite
            },

            async self =>
            {
                var log = new LogWrapper(self.LogChannel);
                var hardlinkmap = new Dictionary<string, string>();
                var mixinqueue = new Queue<string>();
                Duplicati.Library.Utility.IFilter enumeratefilter = emitfilter;

                bool includes;
                bool excludes;
                Library.Utility.FilterExpression.AnalyzeFilters(emitfilter, out includes, out excludes);
                if (includes && !excludes)
                    enumeratefilter = Library.Utility.FilterExpression.Combine(emitfilter, new Duplicati.Library.Utility.FilterExpression("*" + System.IO.Path.DirectorySeparatorChar, true));

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

                        return AttributeFilterAsync(null, x, fa, snapshot, log, sourcefilter, hardlinkPolicy, symlinkPolicy, hardlinkmap, attributeFilter, enumeratefilter, mixinqueue).WaitForTask().Result;
                    });
                }
                else
                {
                    worklist = snapshot.EnumerateFilesAndFolders((root, path, attr) => {
                        return AttributeFilterAsync(root, path, attr, snapshot, log, sourcefilter, hardlinkPolicy, symlinkPolicy, hardlinkmap, attributeFilter, enumeratefilter, mixinqueue).WaitForTask().Result;                        
                    }, (rootpath, path, ex) => {
                        log.WriteWarningAsync(string.Format("Error reported while accessing path {0}", path), ex).WaitForTaskOrThrow();
                    });
                }


                // Process each path, and dequeue the mixins with symlinks as we go
                foreach(var s in worklist)
                {
                    if (!await taskreader.ProgressAsync)
                        return;
                    
                    while (mixinqueue.Count > 0)
                        await self.Output.WriteAsync(mixinqueue.Dequeue());

                    Library.Utility.IFilter m;
                    if (emitfilter != enumeratefilter && !Library.Utility.FilterExpression.Matches(emitfilter, s, out m))
                        continue;

                    await self.Output.WriteAsync(s);
                }

                // Trailing symlinks are caught here
                while (mixinqueue.Count > 0)
                    await self.Output.WriteAsync(mixinqueue.Dequeue());
            });
        }

  
        /// <summary>
        /// Plugin filter for enumerating a list of files.
        /// </summary>
        /// <returns>True if the path should be returned, false otherwise.</returns>
        /// <param name="rootpath">The root path that initiated this enumeration.</param>
        /// <param name="path">The current path.</param>
        /// <param name="attributes">The file or folder attributes.</param>
        private static async Task<bool> AttributeFilterAsync(string rootpath, string path, FileAttributes attributes, Snapshots.ISnapshotService snapshot, LogWrapper log, Library.Utility.IFilter sourcefilter, Options.HardlinkStrategy hardlinkPolicy, Options.SymlinkStrategy symlinkPolicy, Dictionary<string, string> hardlinkmap, FileAttributes attributeFilter, Duplicati.Library.Utility.IFilter enumeratefilter, Queue<string> mixinqueue)
        {
			// Step 1, exclude block devices
			try
            {
                if (snapshot.IsBlockDevice(path))
                {
                    await log.WriteVerboseAsync("Excluding block device: {0}", path);
                    return false;
                }
            }
            catch (Exception ex)
            {
                await log.WriteWarningAsync(string.Format("Failed to process path: {0}", path), ex);
                return false;
            }

            // Check if we explicitly include this entry
            Duplicati.Library.Utility.IFilter sourcematch;
            bool sourcematches;
            if (sourcefilter.Matches(path, out sourcematches, out sourcematch) && sourcematches)
            {
                await log.WriteVerboseAsync("Including source path: {0}", path);
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
                            await log.WriteVerboseAsync("Excluding hardlink: {0} ({1})", path, id);
                            return false;
                        }
                        else if (hardlinkPolicy == Options.HardlinkStrategy.First)
                        {
                            string prevPath;
                            if (hardlinkmap.TryGetValue(id, out prevPath))
                            {
                                await log.WriteVerboseAsync("Excluding hardlink ({1}) for: {0}, previous hardlink: {2}", path, id, prevPath);
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
                    await log.WriteWarningAsync(string.Format("Failed to process path: {0}", path), ex);
                    return false;
                }                    
            }

            // If we exclude files based on attributes, filter that
            if ((attributeFilter & attributes) != 0)
            {
                await log.WriteVerboseAsync("Excluding path due to attribute filter: {0}", path);
                return false;
            }

            // Then check if the filename is not explicitly excluded by a filter
            Library.Utility.IFilter match;
            if (!Library.Utility.FilterExpression.Matches(enumeratefilter, path, out match))
            {
                await log.WriteVerboseAsync("Excluding path due to filter: {0} => {1}", path, match == null ? "null" : match.ToString());
                return false;
            }
            else if (match != null)
            {
                await log.WriteVerboseAsync("Including path due to filter: {0} => {1}", path, match.ToString());
            }

            // If the file is a symlink, apply special handling
            var isSymlink = snapshot.IsSymlink(path, attributes);
            if (isSymlink && symlinkPolicy == Options.SymlinkStrategy.Ignore)
            {
                await log.WriteVerboseAsync("Excluding symlink: {0}", path);
                return false;
            }

            if (isSymlink && symlinkPolicy == Options.SymlinkStrategy.Store)
            {
                await log.WriteVerboseAsync("Storing symlink: {0}", path);

                // We return false because we do not want to recurse into the path,
                // but we add the symlink to the mixin so we process the symlink itself
                mixinqueue.Enqueue(path);
                return false;
            }

            // All the way through, yes!
            return true;
        }
    }
}

