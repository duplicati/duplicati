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

namespace Duplicati.Library.Main.Operation.Backup
{
    public struct LogMessage
    {
        public Logging.LogMessageType Level;
        public string Message;
        public Exception Exception;
        public bool IsVerbose;

        public static LogMessage Warning(string message, Exception ex)
        {
            return new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Warning,
                Message = message,
                Exception = ex
            };
        }

        public static LogMessage Error(string message, Exception ex)
        {
            return new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Error,
                Message = message,
                Exception = ex
            };
        }

        public static LogMessage Profiling(string message, Exception ex)
        {
            return new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Profiling,
                Message = message,
                Exception = ex
            };
        }

        public static LogMessage Information(string message, Exception ex = null)
        {
            return new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Information,
                Message = message,
                Exception = ex
            };
        }

        public static LogMessage Verbose(string message, params object[] args)
        {
            return new LogMessage() { 
                Level = Duplicati.Library.Logging.LogMessageType.Information,
                Message = string.Format(message, args),
                IsVerbose = true
            };
        }

    }


    /// <summary>
    /// The file enumeration process takes a list of source folders as input,
    /// applies all filters requested and emits the filtered set of filenames
    /// to its output channel
    /// </summary>
    public class FileEnumerationProcess : ProcessHelper
    {
        private Snapshots.ISnapshotService m_snapshot;
        private FileAttributes m_attributeFilter;
        private Duplicati.Library.Utility.IFilter m_enumeratefilter;
        private Duplicati.Library.Utility.IFilter m_emitfilter;
        private Options.SymlinkStrategy m_symlinkPolicy;
        private Options.HardlinkStrategy m_hardlinkPolicy;
        private Dictionary<string, string> m_hardlinkmap;
        private Duplicati.Library.Utility.IFilter m_sourcefilter;
        private Queue<string> m_mixinqueue;
        private string[] m_changedfilelist;

        [ChannelName("LogChannel")]
        private IWriteChannel<LogMessage> m_logchannel;

        [ChannelName("SourcePaths")]
        private IWriteChannel<string> m_output;


        public FileEnumerationProcess(Snapshots.ISnapshotService snapshot, FileAttributes attributeFilter, Duplicati.Library.Utility.IFilter sourcefilter, Duplicati.Library.Utility.IFilter filter, Options.SymlinkStrategy symlinkPolicy, Options.HardlinkStrategy hardlinkPolicy, string[] changedfilelist)
            : base()
        {
            m_snapshot = snapshot;
            m_attributeFilter = attributeFilter;
            m_sourcefilter = sourcefilter;
            m_emitfilter = filter;
            m_symlinkPolicy = symlinkPolicy;
            m_hardlinkPolicy = hardlinkPolicy;
            m_hardlinkmap = new Dictionary<string, string>();
            m_mixinqueue = new Queue<string>();
            m_changedfilelist = changedfilelist;

            bool includes;
            bool excludes;
            Library.Utility.FilterExpression.AnalyzeFilters(filter, out includes, out excludes);
            if (includes && !excludes)
            {
                m_enumeratefilter = Library.Utility.FilterExpression.Combine(filter, new Duplicati.Library.Utility.FilterExpression("*" + System.IO.Path.DirectorySeparatorChar, true));
            }
            else
                m_enumeratefilter = m_emitfilter;
        }

        // Non-async wrapper
        private bool AttributeFilter(string rootpath, string path, FileAttributes attributes)
        {
            return AttributeFilterAsync(rootpath, path, attributes).WaitForTask().Result;
        }

        /// <summary>
        /// Plugin filter for enumerating a list of files.
        /// </summary>
        /// <returns>True if the path should be returned, false otherwise.</returns>
        /// <param name="rootpath">The root path that initiated this enumeration.</param>
        /// <param name="path">The current path.</param>
        /// <param name="attributes">The file or folder attributes.</param>
        private async Task<bool> AttributeFilterAsync(string rootpath, string path, FileAttributes attributes)
        {
            // Step 1, exclude block devices
            try
            {
                if (m_snapshot.IsBlockDevice(path))
                {
                    await m_logchannel.WriteAsync(LogMessage.Verbose("Excluding block device: {0}", path));
                    return false;
                }
            }
            catch (Exception ex)
            {
                await m_logchannel.WriteAsync(LogMessage.Warning(string.Format("Failed to process path: {0}", path), ex));
                return false;
            }

            // Check if we explicitly include this entry
            Duplicati.Library.Utility.IFilter sourcematch;
            bool sourcematches;
            if (m_sourcefilter.Matches(path, out sourcematches, out sourcematch) && sourcematches)
            {
                await m_logchannel.WriteAsync(LogMessage.Verbose("Including source path: {0}", path));
                return true;
            }

            // If we have a hardlink strategy, obey it
            if (m_hardlinkPolicy != Options.HardlinkStrategy.All)
            {
                try
                {
                    var id = m_snapshot.HardlinkTargetID(path);
                    if (id != null)
                    {
                        if (m_hardlinkPolicy == Options.HardlinkStrategy.None)
                        {
                            await m_logchannel.WriteAsync(LogMessage.Verbose("Excluding hardlink: {0} ({1})", path, id));
                            return false;
                        }
                        else if (m_hardlinkPolicy == Options.HardlinkStrategy.First)
                        {
                            string prevPath;
                            if (m_hardlinkmap.TryGetValue(id, out prevPath))
                            {
                                await m_logchannel.WriteAsync(LogMessage.Verbose("Excluding hardlink ({1}) for: {0}, previous hardlink: {2}", path, id, prevPath));
                                return false;
                            }
                            else
                            {
                                m_hardlinkmap.Add(id, path);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    await m_logchannel.WriteAsync(LogMessage.Warning(string.Format("Failed to process path: {0}", path), ex));
                    return false;
                }                    
            }

            // If we exclude files based on attributes, filter that
            if ((m_attributeFilter & attributes) != 0)
            {
                await m_logchannel.WriteAsync(LogMessage.Verbose("Excluding path due to attribute filter: {0}", path));
                return false;
            }

            // Then check if the filename is not explicitly excluded by a filter
            Library.Utility.IFilter match;
            if (!Library.Utility.FilterExpression.Matches(m_enumeratefilter, path, out match))
            {
                await m_logchannel.WriteAsync(LogMessage.Verbose("Excluding path due to filter: {0} => {1}", path, match == null ? "null" : match.ToString()));
                return false;
            }
            else if (match != null)
            {
                await m_logchannel.WriteAsync(LogMessage.Verbose("Including path due to filter: {0} => {1}", path, match.ToString()));
            }

            // If the file is a symlink, apply special handling
            var isSymlink = (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            if (isSymlink && m_symlinkPolicy == Options.SymlinkStrategy.Ignore)
            {
                await m_logchannel.WriteAsync(LogMessage.Verbose("Excluding symlink: {0}", path));
                return false;
            }

            if (isSymlink && m_symlinkPolicy == Options.SymlinkStrategy.Store)
            {
                await m_logchannel.WriteAsync(LogMessage.Verbose("Storing symlink: {0}", path));

                // We return false because we do not want to recurse into the path,
                // but we add the symlink to the mixin so we process the symlink itself
                m_mixinqueue.Enqueue(path);
                return false;
            }

            // All the way through, yes!
            return true;
        }

        protected override async Task Start()
        {
            using(var chan = ((IChannel<string>)FilenameOutput).AsWriteOnly())
            {

                // If we have a specific list specified, use that instead of enumerating the filesystem
                IEnumerable<string> worklist;
                if (m_changedfilelist != null && m_changedfilelist.Length > 0)
                {
                    worklist = m_changedfilelist.Where(x =>
                    {
                        var fa = FileAttributes.Normal;
                        try
                        {
                            fa = m_snapshot.GetAttributes(x);
                        }
                        catch
                        {
                        }

                        return AttributeFilter(null, x, fa);
                    });
                }
                else
                {
                    worklist = m_snapshot.EnumerateFilesAndFolders(this.AttributeFilter);
                }


                // Process each path, and dequeue the mixins with symlinks as we go
                foreach(var s in worklist)
                {
                    while (m_mixinqueue.Count > 0)
                        await chan.WriteAsync(m_mixinqueue.Dequeue());

                    Library.Utility.IFilter m;
                    if (m_emitfilter != m_enumeratefilter && !Library.Utility.FilterExpression.Matches(m_emitfilter, s, out m))
                        continue;

                    await chan.WriteAsync(s);
                }

                // Trailing symlinks are caught here
                while (m_mixinqueue.Count > 0)
                    await chan.WriteAsync(m_mixinqueue.Dequeue());
            }
        }
    }
}

